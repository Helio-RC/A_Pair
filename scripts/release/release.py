#!/usr/bin/env python3
"""
release — SeatFlow 发布编排脚本。

将构建 (dotnet publish)、打包 (zip/tar.gz)、SHA256 校验、
阿里云 OSS 上传、GitHub Release 创建串成一条自动化流水线。

用法:
  python3 scripts/release/release.py [--dry-run] [--skip-build] [--root DIR] [--config FILE]

依赖:
  pip install oss2 requests
"""

import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Optional

try:
    import requests
except ImportError:
    requests = None  # type: ignore
try:
    import oss2
except ImportError:
    oss2 = None  # type: ignore


# ──────────────────────────────────────────────
# 常量
# ──────────────────────────────────────────────

def _should_distribute(file_name: str) -> bool:
    """过滤不应分发的文件（vpk 内部元数据）。"""
    lower = file_name.lower()
    # assets.*.json — vpk 内部构建元数据
    if lower.startswith("assets.") and lower.endswith(".json"):
        return False
    return True


def _versioned_name(filename: str, version: str) -> str:
    """确保安装程序文件名包含版本号。

    SeatFlow Setup.exe          → SeatFlow-1.4.2-Setup.exe
    SeatFlow-1.4.2-osx-x64.pkg  → (不变，已有版本号)
    """
    if "." not in filename:
        return f"{filename}-{version}"
    stem, ext = filename.rsplit(".", 1)
    if f"-{version}" in stem or f"_{version}" in stem:
        return filename
    return f"{stem}-{version}.{ext}"


APP_NAME = "SeatFlow"
PROJECT = "SeatFlow.Presentation.Avalonia"
CONFIGURATION = "Release"

# RID → 平台标识 + 打包格式
RIDS: list[dict] = [
    {"rid": "win-x64",      "platform": "windows",     "ext": ".zip"},
    {"rid": "linux-x64",    "platform": "linux",       "ext": ".tar.gz"},
    {"rid": "osx-x64",      "platform": "macos-x64",   "ext": ".tar.gz"},
    {"rid": "osx-arm64",    "platform": "macos-arm64", "ext": ".tar.gz"},
]



# ──────────────────────────────────────────────
# 工具函数
# ──────────────────────────────────────────────


def resolve_root(root_arg: Optional[str] = None) -> Path:
    """解析项目根目录。"""
    if root_arg:
        return Path(root_arg)
    return Path(__file__).resolve().parent.parent.parent


def read_json(path: Path) -> dict:
    """读取 JSON 文件。"""
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def write_json(path: Path, data: dict) -> None:
    """写入 JSON 文件。"""
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.write("\n")


def sha256_file(path: Path) -> str:
    """计算文件的 SHA256 哈希值。"""
    h = hashlib.sha256()
    with open(path, "rb") as f:
        while chunk := f.read(8192):
            h.update(chunk)
    return h.hexdigest()


def format_size(bytes_count: int) -> str:
    """人类可读的文件大小。"""
    for unit in ("B", "KiB", "MiB", "GiB"):
        if bytes_count < 1024:
            return f"{bytes_count:.1f} {unit}"
        bytes_count /= 1024
    return f"{bytes_count:.1f} TiB"


# ──────────────────────────────────────────────
# ReleaseManager
# ──────────────────────────────────────────────


class ReleaseManager:
    """发布编排器。"""

    def __init__(self, root: Path, config_path: Path, dry_run: bool = False,
                 skip_build: bool = False, skip_velopack: bool = False,
                 skip_branch_check: bool = False,
                 clean: bool = True,
                 skip_oss: bool = False, skip_github: bool = False,
                 retransmit: bool = False, rewrite_metadata: bool = False,
                 rotate_worker_only: bool = False):
        self.root = root
        self.dry_run = dry_run
        self.skip_build = skip_build
        self.skip_velopack = skip_velopack
        self.skip_branch_check = skip_branch_check
        self.clean = clean
        self.skip_oss = skip_oss
        self.skip_github = skip_github
        self.retransmit = retransmit
        self.rewrite_metadata = rewrite_metadata
        self.rotate_worker_only = rotate_worker_only

        # 路径
        self.version_json_path = root / "version.json"
        self.release_md_path = root / "RELEASE.md"
        self.project_path = root / PROJECT
        self.dist_dir = root / "publish" / "release"

        # 加载配置
        self.config = self._load_config(config_path)

        # 加载版本信息
        self.version_info = self._load_version_info()
        self.version = self.version_info["version"]

        # dist 子目录
        self.version_dist_dir = self.dist_dir

    # ── 配置 ──────────────────────────────────

    def _load_config(self, config_path: Path) -> dict:
        """读取并验证 release 配置文件。"""
        if not config_path.exists():
            raise ValueError(
                f"配置文件不存在: {config_path}\n"
                f"请参考 {config_path.parent / 'config.json.example'} 创建。"
            )

        cfg = read_json(config_path)

        # enabled 为 false 时跳过所有需要密钥的远程操作
        if cfg.get("enabled") is False:
            print("[!] config.json: enabled 为 false，跳过所有远程发布操作。")
            self.skip_oss = True
            self.skip_github = True

        # 验证 OSS 配置
        oss = cfg.get("oss", {})
        required_oss = ["accessKeyId", "accessKeySecret", "endpoint", "bucket"]
        for key in required_oss:
            if not oss.get(key):
                raise ValueError(f"config.json: oss.{key} 未设置")

        # 验证 GitHub 配置
        gh = cfg.get("github", {})
        if not gh.get("repo"):
            raise ValueError("config.json: github.repo 未设置")
        if not gh.get("token"):
            raise ValueError("config.json: github.token 未设置")

        return cfg

    def _has_cloudflare_config(self) -> bool:
        """检查 Cloudflare Worker 配置是否完整。"""
        cf = self.config.get("cloudflare", {})
        return all(cf.get(k) for k in ("accountId", "workerScript", "apiToken"))

    def _load_version_info(self) -> dict:
        """读取 version.json。"""
        if not self.version_json_path.exists():
            raise FileNotFoundError(f"version.json 不存在: {self.version_json_path}")

        data = read_json(self.version_json_path)
        for key in ("version", "releaseTag"):
            if not data.get(key):
                raise ValueError(f"version.json: '{key}' 字段缺失")
        return data

    # ── 步骤 1: 构建 ──────────────────────────

    def build_all(self) -> dict:
        """构建所有平台的标准发布。返回 {rid: publish_dir}。"""
        if self.skip_build:
            print("[2] 跳过构建 (--skip-build)")
            return {}

        print("[2] 构建所有平台...")

        self.version_dist_dir.mkdir(parents=True, exist_ok=True)

        results: dict[str, Path] = {}

        # 串行构建避免 obj/ 目录冲突
        for r in RIDS:
            rid = r["rid"]
            # macOS 包仅限 macOS 主机构建
            if rid.startswith("osx") and sys.platform != "darwin":
                print(f"    ! {rid}: macOS 包仅限 macOS 主机构建，跳过")
                continue
            try:
                publish_dir = self._build_one(r)
                results[rid] = publish_dir
                print(f"    ✓ {rid}")
            except subprocess.CalledProcessError as e:
                print(f"  ✗ {rid}: 构建失败")
                raise SystemExit(1) from e

        return results

    def _build_one(self, r: dict) -> Path:
        """构建单个 RID。返回可执行文件路径。"""
        rid = r["rid"]
        tmp_dir = self.version_dist_dir / f".tmp_{rid}"
        tmp_dir.mkdir(parents=True, exist_ok=True)

        cmd = [
            "dotnet", "publish", str(self.project_path),
            "-c", CONFIGURATION,
            "-r", rid,
            "--self-contained", "true",
            "-o", str(tmp_dir),
        ]

        result = subprocess.run(cmd, cwd=str(self.root),
                                capture_output=True, text=True)
        if result.returncode != 0:
            print(f"    stderr: {result.stderr[-500:]}", file=sys.stderr)
            result.check_returncode()


        # 返回发布目录
        return tmp_dir

    # ── 步骤 1.5: vpk pack ────────────────────

    # RID → vpk 指令映射（vpk 用括号语法 [win]/[linux]/[osx] 支持跨平台打包）
    _VPK_DIRECTIVE = {
        "win-x64": "[win]",
        "linux-x64": "[linux]",
        "osx-x64": "[osx]",
        "osx-arm64": "[osx]",
    }

    @staticmethod
    def _check_vpk_deps() -> bool:
        """检查 vpk 所需的系统依赖是否安装。"""
        import platform
        system = platform.system()
        missing = []

        if system == "Linux":
            import shutil as _shutil
            if _shutil.which("mksquashfs") is None:
                missing.append("mksquashfs (sudo apt install squashfs-tools)")

        if missing:
            print("[3] vpk 系统依赖缺失:")
            for m in missing:
                print(f"    ! {m}")
            return False
        return True

    def vpk_pack_all(self, build_outputs: dict) -> list[dict]:
        """对各 RID 运行 vpk [os] pack，生成 .nupkg + 安装程序 + releases.{channel}.json。

        vpk 使用括号指令语法（如 vpk [win] pack）支持跨平台打包：
        - Windows 包可在任意 OS 上构建
        - Linux 包需主机安装 squashfs-tools（AppImage 依赖）
        - macOS 包仅限 macOS 主机（依赖 codesign/xcrun）
        """
        if self.skip_velopack or self.dry_run:
            label = "跳过 (--skip-velopack)" if self.skip_velopack else "[dry-run] 跳过"
            print(f"[3] {label} Velopack 打包")
            return []

        print("[3] 打包 Velopack 更新包...")

        vpk_artifacts: list[dict] = []

        for r in RIDS:
            rid = r["rid"]
            publish_dir = build_outputs.get(rid)
            if publish_dir is None:
                print(f"  ! {rid}: 无构建产物，跳过")
                continue

            directive = self._VPK_DIRECTIVE.get(rid)
            if directive is None:
                print(f"  ! {rid}: 未知 RID，跳过")
                continue

            # macOS 包仅限 macOS 主机
            if directive == "[osx]" and sys.platform != "darwin":
                print(f"  ! {rid}: macOS 包仅限 macOS 主机构建，跳过")
                continue

            pack_dir = publish_dir
            output_dir = self.version_dist_dir / f"velopack_{rid}"
            output_dir.mkdir(parents=True, exist_ok=True)

            exe_name = "SeatFlow.exe" if rid.startswith("win") else "SeatFlow"
            cmd = [
                "dotnet", "vpk", directive, "pack",
                "--packId", "SeatFlow",
                "--packVersion", self.version,
                "--packDir", str(pack_dir),
                "--mainExe", exe_name,
                "--channel", rid,
                "--outputDir", str(output_dir),
                "--packTitle", "SeatFlow",
                "--packAuthors", "Helio-RC",
            ]

            # 如果 RELEASE.md 存在，附加 release notes
            if self.release_md_path.exists():
                cmd.extend(["--releaseNotes", str(self.release_md_path)])

            # Windows: 不生成 portable zip
            if directive == "[win]":
                cmd.append("--noPortable")

            result = subprocess.run(cmd, cwd=str(self.root),
                                    capture_output=True, text=True)
            if result.returncode != 0:
                output = (result.stderr or result.stdout)
                if output and "equal or greater" in output:
                    print(f"  ! {rid}: 版本 {self.version} 已存在，跳过（保留增量更新链）")
                else:
                    print(f"  ✗ {rid}: vpk pack 失败")
                    if output:
                        for line in output.strip().split("\n")[-6:]:
                            if line.strip():
                                print(f"    {line}")
                continue

            print(f"  ✓ {rid}")

            # 清理临时发布目录（vpk pack 完成后不再需要）
            shutil.rmtree(pack_dir, ignore_errors=True)

            for f in sorted(output_dir.glob("*")):
                if f.is_file() and _should_distribute(f.name):
                    file_size = f.stat().st_size
                    file_hash = sha256_file(f)
                    vpk_artifacts.append({
                        "rid": rid,
                        "platform": r["platform"],
                        "fileName": f.name,
                        "localPath": str(f),
                        "size": file_size,
                        "sha256": file_hash,
                    })
                    print(f"    {f.name} ({format_size(file_size)})")

        return vpk_artifacts
    # ── 步骤 3: 发布说明 ───────────────────────

    def build_release_notes(self) -> str:
        """读取 RELEASE.md，附加 SHA256 表格。"""
        print("[4] 读取 RELEASE.md...")

        if not self.release_md_path.exists():
            raise FileNotFoundError(
                f"RELEASE.md 不存在: {self.release_md_path}\n"
                f"请在项目根目录创建 RELEASE.md 发布说明文件。"
            )

        content = self.release_md_path.read_text(encoding="utf-8")

        print(f"  RELEASE.md 已读取 ({len(content)} 字符)")
        return content

    # ── 步骤 5: OSS 上传 ───────────────────────

    def upload_to_oss(self, vpk_artifacts: list[dict], release_notes: str) -> None:
        """上传 zip/tar.gz + Velopack 产物到阿里云 OSS（含 releases.json 索引更新）。"""
        print("[6] 上传到阿里云 OSS...")

        if self.dry_run:
            print("  [dry-run] 跳过 OSS 上传。")
            return

        if oss2 is None:
            print("  ! oss2 库未安装。跳过 OSS 上传。")
            return
        oss_cfg = self.config["oss"]
        auth = oss2.Auth(oss_cfg["accessKeyId"], oss_cfg["accessKeySecret"])
        bucket = oss2.Bucket(auth, oss_cfg["endpoint"], oss_cfg["bucket"])

        prefix = f"releases/{self.version}"

        # 上传 RELEASE.md
        self._oss_put(bucket, f"{prefix}/RELEASE.md", release_notes.encode("utf-8"),
                      "text/markdown")
        print(f"  ✓ {prefix}/RELEASE.md")

        # 上传产物：安装包 → releases/{version}/，更新包 → updates/
        if vpk_artifacts:
            installers = [a for a in vpk_artifacts if self._is_installer(a["fileName"])]
            updates = [a for a in vpk_artifacts if not self._is_installer(a["fileName"])]

            if installers:
                print(f"  [安装包 → {prefix}/]")
                for a in installers:
                    oss_key = f"{prefix}/{a['fileName']}"
                    if self._oss_put_file_if_new(bucket, oss_key, a["localPath"]):
                        print(f"    ✓ {oss_key} ({format_size(a['size'])})")

            if updates:
                print("  [更新包 → updates/]")
                for a in updates:
                    oss_key = f"updates/{a['fileName']}"
                    name = a["fileName"].lower()
                    # nupkg 包内容不变，可跳过；通道元数据每次覆盖
                    if name.endswith(".nupkg"):
                        if self._oss_put_file_if_new(bucket, oss_key, a["localPath"]):
                            print(f"    ✓ {oss_key} ({format_size(a['size'])})")
                    else:
                        self._oss_put_file(bucket, oss_key, a["localPath"])
                        print(f"    ✓ {oss_key} ({format_size(a['size'])})")

        elif self.rewrite_metadata:
            # 无 vpk 产物但 --rewrite-metadata：重传通道元数据 + 重建 files 列表
            print("  [--rewrite-metadata] 重传通道元数据...")
            rebuilt: list[dict] = []
            for r in RIDS:
                rid = r["rid"]
                vdir = self.version_dist_dir / f"velopack_{rid}"
                # 通道元数据
                for fname in (f"releases.{rid}.json", f"RELEASES-{rid}"):
                    fpath = vdir / fname
                    if fpath.exists():
                        self._oss_put_file(bucket, f"updates/{fname}", str(fpath))
                        print(f"    ✓ updates/{fname}")
                # 扫描安装程序（installer/ 子目录或根目录），仅匹配当前版本
                for pattern in ("installer/*", "*"):
                    for fpath in vdir.glob(pattern):
                        if not fpath.is_file():
                            continue
                        name = fpath.name.lower()
                        if not name.endswith((".exe", ".appimage", ".pkg", ".dmg")):
                            continue
                        if f"-{self.version}" not in name:
                            continue
                        rebuilt.append({
                            "rid": rid,
                            "platform": r["platform"],
                            "fileName": fpath.name,
                            "localPath": str(fpath),
                            "size": fpath.stat().st_size,
                            "sha256": sha256_file(fpath),
                        })
            vpk_artifacts = rebuilt
            if rebuilt:
                print(f"  ✓ 从本地重建 {len(rebuilt)} 个安装程序条目")

        # 更新 releases.json 索引
        self._update_releases_index(bucket, vpk_artifacts, release_notes)

    def _oss_put(self, bucket, key: str, data: bytes, content_type: str) -> None:
        """上传数据到 OSS。"""
        try:
            bucket.put_object(key, data, headers={"Content-Type": content_type})
        except Exception as e:
            print(f"  ✗ OSS 上传失败: {key} — {e}")
            raise

    def _oss_put_file(self, bucket, key: str, local_path: str) -> None:
        """上传文件到 OSS。"""
        try:
            bucket.put_object_from_file(key, local_path)
        except Exception as e:
            print(f"  ✗ OSS 上传失败: {key} — {e}")
            raise

    def _oss_put_file_if_new(self, bucket, key: str, local_path: str) -> bool:
        """上传文件到 OSS，若已存在则跳过。返回 True 表示已上传。"""
        if bucket.object_exists(key):
            print(f"    - {key} (已存在，跳过)")
            return False
        self._oss_put_file(bucket, key, local_path)
        return True

    def _update_releases_index(self, bucket, vpk_artifacts: list[dict],
                               release_notes: str) -> None:
        """更新 releases.json 版本索引。"""
        index_key = "releases/releases.json"
        existing = None

        try:
            result = bucket.get_object(index_key)
            existing = json.loads(result.read())
        except Exception:
            # releases.json 不存在 → 首次发布
            print("  releases.json 不存在，将创建新的版本索引。")

        if existing is None:
            existing = {"latest": self.version, "versions": []}

        # 检查版本是否已存在
        existing_entry = None
        for v in existing.get("versions", []):
            if v["version"] == self.version:
                existing_entry = v
                break
        if existing_entry is not None:
                if self.rewrite_metadata:
                    print(f"  ! 版本 {self.version} 已存在，--rewrite-metadata 将覆盖其元数据。")
                else:
                    raise ValueError(
                        f"版本 {self.version} 已存在于 releases.json 中。"
                        f"禁止覆盖已发布版本。如需覆盖，请使用 --rewrite-metadata。"
                    )

        # 构建新条目（如已存在则替换，否则插入到头部）
        release_date = datetime.now(timezone.utc).astimezone().isoformat()

        # 从 git 获取真实 commit ID，而非 version.json 中的静态值
        commit_id = self._get_git_commit_id()

        entry = {
            "version": self.version,
            "commitId": commit_id,
            "releaseDate": release_date,
            "notes": release_notes.strip(),
            "files": [
                {
                    "platform": a["platform"],
                    "fileName": a["fileName"],
                    "size": a["size"],
                    "sha256": a["sha256"],
                }
                for a in vpk_artifacts
                if self._is_installer(a["fileName"])  # 仅安装包
            ],
        }

        if existing_entry is not None and self.rewrite_metadata:
            # 原地替换已有条目
            idx = existing["versions"].index(existing_entry)
            existing["versions"][idx] = entry
        else:
            existing["versions"].insert(0, entry)
        existing["latest"] = self.version

        # 上传
        data = json.dumps(existing, ensure_ascii=False, indent=2).encode("utf-8")
        headers = {"Content-Type": "application/json"}

        bucket.put_object(index_key, data, headers=headers)
        print(f"  ✓ {index_key} (latest: {self.version})")

    def _save_oss_manifest(self, vpk_artifacts: list[dict],
                           release_notes: str) -> None:
        """保存本次上传的 OSS 文件清单（供 --retransmit 使用）。"""
        prefix = f"releases/{self.version}"
        manifest: dict[str, object] = {
            "version": self.version,
            "releaseNotes": release_notes.strip(),
            "files": [],
        }
        files: list[dict] = manifest["files"]  # type: ignore[assignment]

        # RELEASE.md
        files.append({
            "key": f"{prefix}/RELEASE.md",
            "type": "text",
        })

        # 安装包 + 更新包
        for a in vpk_artifacts:
            if self._is_installer(a["fileName"]):
                key = f"{prefix}/{a['fileName']}"
                ftype = "binary"
            else:
                key = f"updates/{a['fileName']}"
                name = a["fileName"].lower()
                # 通道元数据标记为 text，retransmit 时比对 SHA
                ftype = "text" if (
                    name.startswith("releases.") or name.startswith("releases-")
                ) else "binary"
            files.append({
                "key": key,
                "type": ftype,
                "localPath": a["localPath"],
            })

        # releases.json 索引
        files.append({
            "key": "releases/releases.json",
            "type": "text",
        })

        manifest_path = self.version_dist_dir / ".oss_manifest.json"
        write_json(manifest_path, manifest)
        print(f"  ✓ OSS manifest → {manifest_path.relative_to(self.root)}")

    def retransmit_to_oss(self) -> None:
        """检查 OSS 上各文件状态，自动补传缺失或内容错误者。

        优先读取 .oss_manifest.json；若不存在则扫描本地产物目录重建文件清单。

        校验策略：
        - .exe / .nupkg → HEAD 检查是否存在；本地缺失则跳过
        - .json / .md   → GET 下载后与本地内容比对
        """
        manifest_path = self.version_dist_dir / ".oss_manifest.json"
        if manifest_path.exists():
            manifest = read_json(manifest_path)
            print(f"[retransmit] 从 manifest 加载: {manifest_path.relative_to(self.root)}")
        else:
            print("[retransmit] manifest 不存在，从本地产物目录扫描...")
            manifest = self._scan_local_manifest()

        oss_cfg = self.config["oss"]
        auth = oss2.Auth(oss_cfg["accessKeyId"], oss_cfg["accessKeySecret"])
        bucket = oss2.Bucket(auth, oss_cfg["endpoint"], oss_cfg["bucket"])

        # 预加载本地参照物
        local_release_md = self.release_md_path.read_text(encoding="utf-8").strip()
        local_release_sha = hashlib.sha256(local_release_md.encode("utf-8")).hexdigest()

        # 构建预期的 releases.json（从 manifest/files 重建 vpk 信息）
        vpk_artifacts = self._rebuild_vpk_from_manifest(manifest)

        expected_index = self._build_releases_index(bucket, vpk_artifacts,
                                                     manifest.get("releaseNotes", ""))
        expected_index_str = json.dumps(expected_index, ensure_ascii=False, indent=2)

        files: list[dict] = manifest.get("files", [])

        # --rewrite-metadata: 确保通道元数据文件在清单中
        if self.rewrite_metadata:
            known_keys = {f["key"] for f in files}
            for r in RIDS:
                rid = r["rid"]
                for tmpl in (f"updates/releases.{rid}.json", f"updates/RELEASES-{rid}"):
                    if tmpl not in known_keys:
                        # 尝试找本地文件
                        local_file = self.version_dist_dir / f"velopack_{rid}" / tmpl.split("/")[-1]
                        if local_file.exists():
                            files.append({
                                "key": tmpl,
                                "type": "text",
                                "localPath": str(local_file),
                            })
                            print(f"  + 补充清单  text   | {tmpl}")

        total = len(files)
        missing: list[dict] = []
        mismatch: list[dict] = []
        ok_count = 0
        skipped_count = 0

        print(f"\n[OSS 校验] 共 {total} 个文件，正在逐项检查...\n")

        for f in files:
            key: str = f["key"]
            ftype: str = f["type"]
            label = f"{ftype:6s} | {key}"

            if ftype == "binary":
                local_path = f.get("localPath", "")
                if not local_path or not Path(local_path).exists():
                    print(f"  - 本地缺失   {label}")
                    skipped_count += 1
                    continue

                if bucket.object_exists(key):
                    print(f"  ✓           {label}")
                    ok_count += 1
                else:
                    print(f"  ✗ 远端缺失  {label}")
                    missing.append(f)

            elif ftype == "text":
                is_channel = key.startswith("updates/") and (
                    key.endswith(".json") or key.startswith("updates/RELEASES-")
                )
                # --rewrite-metadata: 通道元数据强制重传
                if is_channel and self.rewrite_metadata:
                    print(f"  ⚠ 强制重传   {label}")
                    mismatch.append(f)
                    continue

                try:
                    result = bucket.get_object(key)
                    remote_data = result.read()
                    remote_sha = hashlib.sha256(remote_data).hexdigest()

                    if key.endswith("RELEASE.md"):
                        local_sha = local_release_sha
                    elif key.endswith("releases.json") and not key.startswith("updates/"):
                        local_sha = hashlib.sha256(
                            expected_index_str.encode("utf-8")).hexdigest()
                    elif is_channel:
                        # Velopack 频道文件：本地存在则重算 SHA
                        local_path = f.get("localPath", "")
                        if local_path and Path(local_path).exists():
                            local_sha = sha256_file(Path(local_path))
                        else:
                            local_sha = None
                    else:
                        local_sha = None

                    if local_sha and remote_sha == local_sha:
                        print(f"  ✓           {label}")
                        ok_count += 1
                    else:
                        print(f"  ⚠ 内容差异  {label}")
                        mismatch.append(f)

                except oss2.exceptions.NoSuchKey:
                    print(f"  ✗ 远端缺失  {label}")
                    missing.append(f)
                except Exception as e:
                    print(f"  ✗ 检查失败  {label}  — {e}")

        # ── 汇总 ──
        print(f"\n{'─' * 50}")
        parts = [f"✓ {ok_count}"]
        if missing:
            parts.append(f"✗ 缺失 {len(missing)}")
        if mismatch:
            parts.append(f"⚠ 差异 {len(mismatch)}")
        if skipped_count:
            parts.append(f"- 跳过 {skipped_count}")
        print(f"  校验完成: {'  '.join(parts)}")

        if not missing and not mismatch:
            print("  所有文件已就绪，无需重传。")
            return

        # ── 补传缺失文件 ──
        if missing:
            print(f"\n[补传] 缺失文件 ({len(missing)} 个)...")
            for f in missing:
                key = f["key"]
                ftype = f["type"]
                try:
                    if ftype == "binary":
                        local_path = f.get("localPath", "")
                        if not local_path:
                            continue
                        bucket.put_object_from_file(key, local_path)
                        print(f"  ✓ {key}  (from {Path(local_path).name})")
                    elif ftype == "text":
                        if key.endswith("RELEASE.md"):
                            data = local_release_md.encode("utf-8")
                            ct = "text/markdown"
                        elif key.endswith("releases.json"):
                            data = expected_index_str.encode("utf-8")
                            ct = "application/json"
                        else:
                            continue
                        bucket.put_object(key, data, headers={"Content-Type": ct})
                        print(f"  ✓ {key}")
                except Exception as e:
                    print(f"  ✗ 补传失败: {key} — {e}")

        # ── 修复内容差异 ──
        if mismatch:
            print(f"\n[修复] 内容差异 ({len(mismatch)} 个)...")
            for f in mismatch:
                key = f["key"]
                try:
                    if key.endswith("RELEASE.md"):
                        data = local_release_md.encode("utf-8")
                        ct = "text/markdown"
                    elif key.endswith("releases.json"):
                        data = expected_index_str.encode("utf-8")
                        ct = "application/json"
                    else:
                        continue
                    bucket.put_object(key, data, headers={"Content-Type": ct})
                    print(f"  ✓ {key}")
                except Exception as e:
                    print(f"  ✗ 修复失败: {key} — {e}")

        # ── 如果 releases.json 需要重建 ──
        if any(f["key"] == "releases/releases.json" for f in missing + mismatch):
            print("\n[修复] 重建 releases.json 索引...")
            self._update_releases_index(bucket, vpk_artifacts,
                                        manifest.get("releaseNotes", ""))

        print("\n  OSS 重传完成。")

    def _scan_local_manifest(self) -> dict:
        """扫描本地产物目录，重建 OSS 文件清单（无需事先 manifest）。"""
        files: list[dict] = []
        prefix = f"releases/{self.version}"
        ver_dir = self.version_dist_dir

        if not ver_dir.exists():
            raise FileNotFoundError(
                f"产物目录不存在: {ver_dir}\n"
                f"请先运行构建流程 (dotnet publish + vpk pack)。"
            )

        # 扫描所有产物文件（递归，跳过 .tmp_* 临时目录）
        for path in ver_dir.rglob("*"):
            if not path.is_file() or ".tmp_" in str(path):
                continue
            name = path.name.lower()
            rel = path.relative_to(ver_dir)

            # Velopack 频道元数据
            if (name.startswith("releases.") and name.endswith(".json")) \
               or name.startswith("releases-"):
                files.append({
                    "key": f"updates/{path.name}",
                    "type": "text",
                    "localPath": str(path),
                })
            # 安装程序
            elif name.endswith((".exe", ".appimage", ".pkg", ".dmg")):
                files.append({
                    "key": f"{prefix}/{path.name}",
                    "type": "binary",
                    "localPath": str(path),
                })
            # 更新包
            elif name.endswith(".nupkg"):
                files.append({
                    "key": f"updates/{path.name}",
                    "type": "binary",
                    "localPath": str(path),
                })

        # 文本类文件
        files.append({"key": f"{prefix}/RELEASE.md", "type": "text"})
        files.append({"key": "releases/releases.json", "type": "text"})

        # 读入 RELEASE.md 作为 releaseNotes
        release_notes = ""
        if self.release_md_path.exists():
            release_notes = self.release_md_path.read_text(encoding="utf-8").strip()

        return {"version": self.version, "releaseNotes": release_notes, "files": files}

    @staticmethod
    def _rebuild_vpk_from_manifest(manifest: dict) -> list[dict]:
        """从 manifest 重建 vpk_artifacts（用于 releases.json 条目构建）。"""
        files: list[dict] = manifest.get("files", [])
        vpk: list[dict] = []
        for f in files:
            if f["type"] != "binary":
                continue
            fname = f["key"].rsplit("/", 1)[-1]
            local_path = f.get("localPath", "")
            sha = ""
            size = 0
            if local_path:
                lp = Path(local_path)
                if lp.exists():
                    sha = sha256_file(lp)
                    size = lp.stat().st_size
            # 推断平台
            platform = ""
            for rid_info in RIDS:
                if rid_info["rid"].replace("-", "") in fname.lower().replace("-", ""):
                    platform = rid_info["platform"]
                    break
            vpk.append({
                "fileName": fname,
                "localPath": local_path,
                "platform": platform,
                "size": size,
                "sha256": sha,
            })
        return vpk

    def _build_releases_index(self, bucket, vpk_artifacts: list[dict],
                              release_notes: str) -> dict:
        """仅构建 releases.json 内容（不上传），供重传校验使用。"""
        index_key = "releases/releases.json"
        existing: dict = {"latest": self.version, "versions": []}

        try:
            result = bucket.get_object(index_key)
            existing = json.loads(result.read())
        except Exception:
            pass

        # 检查版本是否已存在
        existing_entry = None
        for v in existing.get("versions", []):
            if v["version"] == self.version:
                existing_entry = v
                break

        # 非 rewrite 模式下，版本已存在则直接返回（幂等）
        if existing_entry is not None and not self.rewrite_metadata:
            return existing

        # 构建新条目（与 _update_releases_index 一致的逻辑）
        release_date = datetime.now(timezone.utc).astimezone().isoformat()
        commit_id = self._get_git_commit_id()

        entry = {
            "version": self.version,
            "commitId": commit_id,
            "releaseDate": release_date,
            "notes": release_notes.strip(),
            "files": [],
        }

        # 将 vpk_artifacts 转回文件列表
        for f in vpk_artifacts:
            entry["files"].append({
                "platform": f.get("platform", ""),
                "fileName": f["fileName"],
                "size": f.get("size", 0),
                "sha256": f.get("sha256", ""),
            })

        if existing_entry is not None and self.rewrite_metadata:
            idx = existing["versions"].index(existing_entry)
            existing["versions"][idx] = entry
        else:
            existing["versions"].insert(0, entry)
        existing["latest"] = self.version

        # 重新计算 size/sha256 — 从远端取真实值（如果存在）
        for ef in entry["files"]:
            fname = ef["fileName"]
            if "/updates/" not in str(fname):
                oss_key = f"releases/{self.version}/{fname}"
            else:
                oss_key = f"updates/{fname}"

            # 找到对应的 vpk_artifact 获取 sha256
            matched = [a for a in vpk_artifacts if a["fileName"] == fname]
            if matched:
                ef["sha256"] = matched[0].get("sha256", "")
                ef["size"] = matched[0].get("size", 0)

        return existing

    # ── 步骤 4: GitHub Release ────────────────
        """通过 GitHub REST API 创建 Release，上传 zip/tar.gz + Velopack 产物。"""
        print("[5] 创建 GitHub Release...")

        if self.dry_run:
            print("  [dry-run] 跳过 GitHub Release 创建。")
            return

        if requests is None:
            print("  ! requests 库未安装。跳过 GitHub Release。")
            return
        gh_cfg = self.config["github"]
        repo = gh_cfg["repo"]
        token = gh_cfg["token"]
        tag = self.version_info["releaseTag"]

        api_base = f"https://api.github.com/repos/{repo}"
        headers = {
            "Authorization": f"Bearer {token}",
            "Accept": "application/vnd.github+json",
        }

        # 1. 构建 body（RELEASE.md + SHA256 表格，含 Velopack 产物）
        body = self._build_release_body(release_notes, vpk_artifacts)

        # 2. 创建 Release
        release_url = f"{api_base}/releases"
        payload = {
            "tag_name": tag,
            "target_commitish": "main",
            "name": f"SeatFlow {self.version}",
            "body": body,
            "prerelease": False,
        }

        resp = requests.post(release_url, json=payload, headers=headers)
        if resp.status_code == 422 and "already_exists" in resp.text:
            print(f"  ! Release tag '{tag}' 已存在，跳过。")
            return
        if resp.status_code >= 400:
            print(f"  ✗ GitHub API 错误: {resp.status_code}")
            print(f"    {resp.text}")
            raise SystemExit(1)

        release_data = resp.json()
        release_id = release_data["id"]
        print(f"  ✓ Release 已创建: {release_data['html_url']}")

        # 3. 上传所有资产文件（zip/tar.gz + Velopack 产物）
        installers = [a for a in vpk_artifacts if self._is_installer(a["fileName"])]
        for f in installers:
            local_path = Path(f["localPath"])
            asset_url = f"{api_base}/releases/{release_id}/assets"
            params = {"name": f["fileName"]}
            asset_headers = {
                **headers,
                "Content-Type": "application/octet-stream",
            }

            with open(local_path, "rb") as fh:
                asset_resp = requests.post(
                    asset_url, params=params, headers=asset_headers, data=fh
                )

            if asset_resp.status_code == 201:
                print(f"  ✓ 已上传: {f['fileName']}")
            else:
                print(f"  ✗ 上传失败: {f['fileName']} — {asset_resp.status_code}")

    # ── 步骤 6: Cloudflare Worker Secret 轮换 ──

    def rotate_worker_secrets(self, force: bool = False) -> bool:
        """将 Worker 专用 OSS 只读凭证通过 Cloudflare API 下发到 Worker Secret。

        Worker 凭证与上传用的 oss.accessKeyId/Secret 是不同的子账号密钥：
        Worker 仅需 OSS 只读权限（回源下载），上传主密钥拥有写权限，不能混用。

        当 force=True 时跳过所有前置条件检查（供 --rotate-worker-secrets 专用模式使用）。
        """
        if not force:
            if self.skip_oss and self.skip_github:
                print("[7] Worker Secret 轮换  → 跳过 (远程已全部禁用)")
                return False
        if not self._has_cloudflare_config():
            print("[7] Worker Secret 轮换  → 跳过 (cloudflare 配置不完整)")
            return False

        if requests is None:
            print("[7] Worker Secret 轮换  → 跳过 (requests 未安装)")
            return False
        cf = self.config["cloudflare"]
        account_id = cf["accountId"]
        script = cf["workerScript"]
        api_token = cf["apiToken"]

        worker_key_id = cf.get("workerOssKeyId")
        worker_key_secret = cf.get("workerOssKeySecret")
        if not worker_key_id or not worker_key_secret:
            print("[7] Worker Secret 轮换  → 跳过 (cloudflare.workerOssKeyId/Secret 未配置)")
            return False

        api_url = (
            f"https://api.cloudflare.com/client/v4/accounts/{account_id}"
            f"/workers/scripts/{script}/secrets-bulk"
        )

        payload = {
            "OSS_ACCESS_KEY_ID": {
                "name": "OSS_ACCESS_KEY_ID",
                "text": worker_key_id,
                "type": "secret_text",
            },
            "OSS_ACCESS_KEY_SECRET": {
                "name": "OSS_ACCESS_KEY_SECRET",
                "text": worker_key_secret,
                "type": "secret_text",
            },
        }

        print(f"[7] Worker Secret 轮换: {script}...")

        if self.dry_run:
            # 脱敏显示
            masked = dict(payload)
            for k in masked:
                masked[k] = {**masked[k], "text": masked[k]["text"][:4] + "***"}
            print(f"  [dry-run] 将 PATCH {api_url}")
            print(f"  [dry-run] payload: {json.dumps(masked, ensure_ascii=False, indent=2)}")
            return False

        resp = requests.patch(
            api_url,
            json=payload,
            headers={
                "Authorization": f"Bearer {api_token}",
                "Content-Type": "application/merge-patch+json",
            },
        )

        if resp.status_code == 200:
            result = resp.json()
            if result.get("success"):
                print(f"  ✓ Secret 已更新（Worker 自动重新部署）")
                return True
            else:
                errors = result.get("errors", [])
                for e in errors:
                    print(f"  ✗ CF API 错误: {e.get('message', e)}")
                return False
        else:
            print(f"  ✗ CF API HTTP {resp.status_code}: {resp.text}")
            return False

    @staticmethod
    def _is_installer(file_name: str) -> bool:
        """判断是否为用户可下载的安装程序文件。"""
        lower = file_name.lower()
        return lower.endswith('.exe') or lower.endswith('.appimage') \
            or lower.endswith('.pkg') or lower.endswith('.dmg')

    def _build_release_body(self, release_notes: str, artifacts: list[dict]) -> str:
        """构建 Release body：RELEASE.md + 安装程序 SHA256 表格。"""
        installers = [a for a in artifacts if self._is_installer(a["fileName"])]
        if not installers:
            return release_notes.strip()

        lines = [release_notes.strip(), "", "### SHA256 Checksums", ""]
        lines.append("| File | SHA256 |")
        lines.append("|------|--------|")

        for a in installers:
            lines.append(f"| {a['fileName']} | {a['sha256']} |")

        return "\n".join(lines) + "\n"

    # ── 前置检查 ───────────────────────────────

    def _get_git_commit_id(self) -> str:
        """从 git 获取当前 HEAD 的短 commit ID。"""
        result = subprocess.run(
            ["git", "rev-parse", "--short", "HEAD"],
            cwd=str(self.root),
            capture_output=True,
            text=True,
        )
        return result.stdout.strip() if result.returncode == 0 else "unknown"

    def _check_main_branch(self) -> bool:
        """确保当前在 main 分支。不在则自动切换。"""
        # CI 环境（分离 HEAD）跳过分支检查
        if self.skip_branch_check:
            print("[0] 分支检查  → 跳过 (--skip-branch-check)")
            return True

        # CI 环境变量检测
        if any(os.environ.get(v) for v in ("CI", "GITHUB_ACTIONS", "GITLAB_CI")):
            print("[0] 分支检查  → 跳过 (CI 环境)")
            return True

        result = subprocess.run(
            ["git", "branch", "--show-current"],
            cwd=str(self.root),
            capture_output=True,
            text=True,
        )
        current = result.stdout.strip()

        if current == "main":
            print("[0] 分支检查  ✓ 已在 main 分支")
            return True

        # 检查是否有未提交的改动
        status = subprocess.run(
            ["git", "status", "--porcelain"],
            cwd=str(self.root),
            capture_output=True,
            text=True,
        )
        if status.stdout.strip():
            print(f"[0] 分支检查  ✗ 当前在 '{current}' 分支，且有未提交的改动。")
            print("    请先 commit 或 stash 改动后再切换。")
            return False

        # 干净，切换到 main
        print(f"[0] 分支检查  → 从 '{current}' 切换到 main...")
        result = subprocess.run(
            ["git", "checkout", "main"],
            cwd=str(self.root),
            capture_output=True,
            text=True,
        )
        if result.returncode != 0:
            print(f"[0/5] 分支检查  ✗ 切换失败: {result.stderr.strip()}")
            return False
        print("[0/5] 分支检查  ✓ 已切换到 main")
        return True

    def _check_versions(self) -> bool:
        """运行 version.py check，确保版本号一致性。"""
        version_py = self.root / "scripts" / "version.py"
        result = subprocess.run(
            ["python3", str(version_py), "check"],
            cwd=str(self.root),
            capture_output=True,
            text=True,
        )
        if result.returncode != 0:
            print("[0] 版本号一致性检查  ✗ 失败")
            print(result.stdout)
            if result.stderr:
                print(result.stderr)
            return False
        print("[0] 版本号一致性检查  ✓ 通过")
        return True

    # ── 编排 ───────────────────────────────────

    def run(self) -> int:
        """执行全量发布流程。"""
        print(f"=== SeatFlow Release v{self.version} ===\n")

        try:
            # Worker Secret 轮换模式 — 仅推送密钥到 Cloudflare Worker
            if self.rotate_worker_only:
                if not self._has_cloudflare_config():
                    print("错误: cloudflare 配置不完整（需 accountId/workerScript/apiToken）")
                    return 1
                ok = self.rotate_worker_secrets(force=True)
                print(f"\n=== Worker Secret 轮换 {'完成' if ok else '失败'} ===")
                return 0 if ok else 1

            # Retransmit 模式 — 仅校验 OSS 文件完整性并补传
            if self.retransmit:
                if self.dry_run:
                    print("[retransmit] dry-run 模式暂不支持，请直接运行。")
                    return 0
                self.retransmit_to_oss()
                print(f"\n=== OSS 重传 v{self.version} 完成 ===")
                return 0

            # [0] 确保在 main 分支
            if not self._check_main_branch():
                return 1

            # [0] 版本号一致性检查
            if not self._check_versions():
                print("\n请先运行 python3 scripts/version.py check 查看详情，"
                      "再用 python3 scripts/version.py sync --force 修复同步问题。")
                return 1

            # [1] 清理 bin/obj
            if self.clean:
                self._clean_bin_obj()

            # [2] 构建
            build_outputs = self.build_all()

            if not build_outputs:
                print("[!] 错误: 无可用构建产物。")
                return 1

            # [3] vpk pack
            if not self._check_vpk_deps():
                print("[3] vpk pack  → 跳过 (系统依赖缺失)")
                vpk_artifacts = []
            else:
                vpk_artifacts = self.vpk_pack_all(build_outputs)

            # [4] 发布说明
            release_notes = self.build_release_notes()

            if not vpk_artifacts and not self.skip_velopack:
                if self.rewrite_metadata:
                    print("[!] vpk 未产生产物，但 --rewrite-metadata 将继续上传通道元数据。")
                else:
                    print("[!] vpk 未产生任何产物，跳过远程发布。")
                    self.skip_oss = True
                    self.skip_github = True

            # [5] GitHub Release
            if self.skip_github:
                print("[5] GitHub Release  → 跳过 (--skip-github)")
            else:
                self.create_github_release(vpk_artifacts, release_notes)

            # [6] OSS 上传
            if self.skip_oss:
                print("[6] OSS 上传       → 跳过 (--skip-oss)")
            else:
                self.upload_to_oss(vpk_artifacts, release_notes)

            # [7] Cloudflare Worker Secret
            self.rotate_worker_secrets()

            # [8] 重组产物目录（上传完成后）
            if vpk_artifacts:
                self._organize_artifacts(vpk_artifacts)
                # 重组后保存 manifest（路径已更新）
                self._save_oss_manifest(vpk_artifacts, release_notes)

            print(f"\n=== Release v{self.version} 完成 ===")

            # 本地输出 SHA256 表格（zip/tar.gz + Velopack 产物）
            installers = [a for a in vpk_artifacts if self._is_installer(a["fileName"])]
            if installers:
                print("\nSHA256 Checksums:")
                print(self._build_sha256_table(installers))

            return 0

        except (ValueError, FileNotFoundError) as e:
            print(f"\n错误: {e}")
            return 1
        except SystemExit as e:
            return e.code if isinstance(e.code, int) else 1

    def _clean_bin_obj(self) -> None:
        """递归清理所有 bin/ 和 obj/ 目录，确保干净编译。"""
        dirs = sorted(
            [d for d in self.root.rglob("bin") if d.is_dir()] +
            [d for d in self.root.rglob("obj") if d.is_dir()]
        )
        if not dirs:
            print("[1] 没有可清理的 bin/obj 目录")
            return
        print(f"[1] 清理 {len(dirs)} 个 bin/obj 目录...")
        if self.dry_run:
            for d in dirs:
                print(f"  [dry-run] 将删除: {d.relative_to(self.root)}")
            print("[1] [dry-run] 跳过实际删除")
            return
        for d in dirs:
            shutil.rmtree(d, ignore_errors=True)
        print("[1] ✓ 已清理")

    def _find_existing_builds(self) -> dict:
        """从 dist 目录查找已有构建产物。"""
        results: dict[str, Path] = {}
        for r in RIDS:
            rid = r["rid"]
            exe_name = "SeatFlow.exe" if rid.startswith("win") else "SeatFlow"
            # 可能在 .tmp_{rid} 中
            tmp_dir = self.version_dist_dir / f".tmp_{rid}"
            exe_path = tmp_dir / exe_name
            if exe_path.exists():
                results[rid] = exe_path
        return results

    # ── 步骤 8: 重组产物目录 ─────────────────

    def _organize_artifacts(self, vpk_artifacts: list[dict]) -> None:
        """上传完成后，将安装程序归档到 {rid}/installer/，文件名补齐版本号。"""
        print("[8] 归档安装程序...")

        if self.dry_run:
            print("  [dry-run] 跳过归档。")
            return

        moved = 0
        for r in RIDS:
            rid = r["rid"]

            rid_installers = [
                a for a in vpk_artifacts
                if a["rid"] == rid
                and self._is_installer(a["fileName"])
            ]
            if not rid_installers:
                continue

            inst_dir = self.version_dist_dir / f"velopack_{rid}" / "installer"
            inst_dir.mkdir(parents=True, exist_ok=True)

            for a in rid_installers:
                src = Path(a["localPath"])
                if not src.exists():
                    continue

                dst_name = _versioned_name(src.name, self.version)
                dst = inst_dir / dst_name
                shutil.move(str(src), str(dst))
                a["fileName"] = dst_name   # OSS / releases.json / manifest 全部用版本化名称
                a["localPath"] = str(dst)
                moved += 1
                print(f"    {rid}/installer/{dst_name}")

        print(f"  ✓ 已归档 {moved} 个安装程序")

    def _build_sha256_table(self, artifacts: list[dict]) -> str:
        """构建 SHA256 表格字符串。"""
        lines = ["| File | SHA256 |", "|------|--------|"]
        for a in artifacts:
            lines.append(f"| {a['fileName']} | {a['sha256']} |")
        return "\n".join(lines)


# ──────────────────────────────────────────────
# CLI 入口
# ──────────────────────────────────────────────


def main() -> int:
    parser = argparse.ArgumentParser(
        description="SeatFlow 发布编排脚本 — 构建、打包、上传 OSS、创建 GitHub Release",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
示例:
  python3 scripts/release/release.py --dry-run        # 预览全流程（不实际上传）
  python3 scripts/release/release.py                  # 完整发布
  python3 scripts/release/release.py --skip-build     # 跳过构建，仅打包和发布
        """,
    )
    parser.add_argument("--root", default=None, help="项目根目录 (默认: 自动检测)")
    parser.add_argument("--config", default=None, help="配置文件路径 (默认: scripts/release/config.json)")
    parser.add_argument("--dry-run", action="store_true", help="预览模式：构建和打包，但不上传")
    parser.add_argument("--skip-build", action="store_true", help="跳过 dotnet publish，使用已有构建产物")
    parser.add_argument("--skip-velopack", action="store_true", help="跳过 Velopack vpk pack 步骤")
    parser.add_argument("--skip-branch-check", action="store_true", help="跳过 main 分支检查（CI 环境自动跳过）")
    parser.add_argument("--no-clean", action="store_true", help="禁用编译前 bin/obj 清理")
    parser.add_argument("--skip-oss", action="store_true", help="跳过阿里云 OSS 上传")
    parser.add_argument("--skip-github", action="store_true", help="跳过 GitHub Release 创建")
    parser.add_argument("--retransmit", action="store_true",
                        help="OSS 重传模式：检查各文件完整性，自动补传缺失或内容错误的文件")
    parser.add_argument("--rewrite-metadata", action="store_true",
                        help="允许覆盖 releases.json 中已存在的版本条目（默认禁止）")
    parser.add_argument("--rotate-worker-secrets", action="store_true",
                        help="仅推送 OSS 密钥到 Cloudflare Worker（跳过构建/打包/上传/发布）")

    args = parser.parse_args()

    root = resolve_root(args.root)
    config_path = Path(args.config) if args.config else root / "scripts" / "release" / "config.json"

    mgr = ReleaseManager(
        root=root,
        config_path=config_path,
        dry_run=args.dry_run,
        skip_build=args.skip_build,
        skip_velopack=args.skip_velopack,
        skip_branch_check=args.skip_branch_check,
        clean=not args.no_clean,
        skip_oss=args.skip_oss,
        skip_github=args.skip_github,
        retransmit=args.retransmit,
        rewrite_metadata=args.rewrite_metadata,
        rotate_worker_only=args.rotate_worker_secrets,
    )

    return mgr.run()


if __name__ == "__main__":
    sys.exit(main())
