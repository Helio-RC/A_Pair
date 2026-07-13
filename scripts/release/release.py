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
import tarfile
import zipfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Optional


# ──────────────────────────────────────────────
# 常量
# ──────────────────────────────────────────────

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

EXE_SUFFIX = ".exe"  # Windows RID 的可执行文件后缀


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
                 skip_branch_check: bool = False):
        self.root = root
        self.dry_run = dry_run
        self.skip_build = skip_build
        self.skip_velopack = skip_velopack
        self.skip_branch_check = skip_branch_check

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
        self.version_dist_dir = self.dist_dir / self.version

    # ── 配置 ──────────────────────────────────

    def _load_config(self, config_path: Path) -> dict:
        """读取并验证 release 配置文件。"""
        if not config_path.exists():
            raise ValueError(
                f"配置文件不存在: {config_path}\n"
                f"请参考 {config_path.parent / 'config.json.example'} 创建。"
            )

        cfg = read_json(config_path)

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
        """并行构建所有平台的单文件可执行文件。返回 {rid: exe_path}。"""
        if self.skip_build:
            print("[1/5] 跳过构建 (--skip-build)")
            return {}

        print("[1/5] 构建所有平台...")

        self.version_dist_dir.mkdir(parents=True, exist_ok=True)

        results: dict[str, Path] = {}

        # 串行构建避免 obj/ 目录冲突
        for r in RIDS:
            rid = r["rid"]
            try:
                exe_path = self._build_one(r)
                results[rid] = exe_path
                print(f"  ✓ {rid}")
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
            "-p:PublishSingleFile=true",
            "-p:IncludeNativeLibrariesForSelfExtract=true",
            "-p:IncludeAllContentForSelfExtract=true",
            "-o", str(tmp_dir),
        ]

        result = subprocess.run(cmd, cwd=str(self.root),
                                capture_output=True, text=True)
        if result.returncode != 0:
            print(f"    stderr: {result.stderr[-500:]}", file=sys.stderr)
            result.check_returncode()

        # 查找输出可执行文件
        exe_name = "SeatFlow.exe" if rid.startswith("win") else "SeatFlow"
        exe_path = tmp_dir / exe_name
        if not exe_path.exists():
            # fallback: 搜索目录
            candidates = list(tmp_dir.glob("SeatFlow*"))
            if candidates:
                exe_path = candidates[0]
            else:
                raise FileNotFoundError(f"构建产物未找到: {tmp_dir}")

        return exe_path

    # ── 步骤 1.5: vpk pack ────────────────────

    # RID → vpk 指令映射（vpk 用括号语法 [win]/[linux]/[osx] 支持跨平台打包）
    _VPK_DIRECTIVE = {
        "win-x64": "[win]",
        "linux-x64": "[linux]",
        "osx-x64": "[osx]",
        "osx-arm64": "[osx]",
    }

    def vpk_pack_all(self, build_outputs: dict) -> list[dict]:
        """对各 RID 运行 vpk [os] pack，生成 .nupkg + 安装程序 + releases.{channel}.json。

        vpk 使用括号指令语法（如 vpk [win] pack）支持跨平台打包：
        - Windows 包可在任意 OS 上构建
        - Linux 包需主机安装 squashfs-tools（AppImage 依赖）
        - macOS 包仅限 macOS 主机（依赖 codesign/xcrun）
        """
        if self.skip_velopack or self.dry_run:
            label = "跳过 (--skip-velopack)" if self.skip_velopack else "[dry-run] 跳过"
            print(f"[vpk] {label} Velopack 打包")
            return []

        print("[vpk] 打包 Velopack 更新包...")

        vpk_artifacts: list[dict] = []

        for r in RIDS:
            rid = r["rid"]
            exe_path = build_outputs.get(rid)
            if exe_path is None:
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

            pack_dir = exe_path.parent
            output_dir = self.version_dist_dir / f"velopack_{rid}"
            output_dir.mkdir(parents=True, exist_ok=True)

            exe_name = "SeatFlow.exe" if rid.startswith("win") else "SeatFlow"
            cmd = [
                "vpk", directive, "pack",
                "--packId", "SeatFlow",
                "--packVersion", self.version,
                "--packDir", str(pack_dir),
                "--mainExe", exe_name,
                "--channel", rid,
                "--outputDir", str(output_dir),
            ]

            result = subprocess.run(cmd, cwd=str(self.root),
                                    capture_output=True, text=True)
            if result.returncode != 0:
                print(f"  ✗ {rid}: vpk pack 失败")
                if result.stderr:
                    lines = result.stderr.strip().split("\n")
                    for line in lines[-8:]:
                        print(f"    {line}")
                continue

            print(f"  ✓ {rid}")

            for f in sorted(output_dir.glob("*")):
                if f.is_file():
                    file_size = f.stat().st_size
                    file_hash = sha256_file(f)
                    vpk_artifacts.append({
                        "rid": rid,
                        "fileName": f.name,
                        "localPath": str(f),
                        "size": file_size,
                        "sha256": file_hash,
                    })
                    print(f"    {f.name} ({format_size(file_size)})")

        return vpk_artifacts

    # ── 步骤 2: 打包 ──────────────────────────

    def package_all(self, build_outputs: dict) -> list[dict]:
        """打包各平台产物，计算 SHA256。返回文件信息列表。"""
        print("[2/5] 打包平台产物...")

        files: list[dict] = []

        for r in RIDS:
            rid = r["rid"]
            platform = r["platform"]
            ext = r["ext"]

            exe_path = build_outputs.get(rid)
            if exe_path is None:
                print(f"  ! {rid}: 无构建产物，跳过")
                continue

            file_name = f"{APP_NAME}-{self.version}-{platform}{ext}"
            archive_path = self.version_dist_dir / file_name

            if ext == ".zip":
                self._create_zip(exe_path, archive_path, rid)
            else:
                self._create_tar_gz(exe_path, archive_path, rid)

            file_size = archive_path.stat().st_size
            file_hash = sha256_file(archive_path)

            files.append({
                "platform": platform,
                "fileName": file_name,
                "size": file_size,
                "sha256": file_hash,
                "localPath": str(archive_path),
            })

            print(f"  {file_name} ({format_size(file_size)}, sha256: {file_hash[:12]}...)")

        # 清理临时构建目录
        for r in RIDS:
            tmp_dir = self.version_dist_dir / f".tmp_{r['rid']}"
            if tmp_dir.exists():
                shutil.rmtree(tmp_dir)

        return files

    def _create_zip(self, exe_path: Path, archive_path: Path, rid: str) -> None:
        """创建 .zip 包。"""
        exe_name = f"{APP_NAME}.exe" if rid.startswith("win") else APP_NAME
        with zipfile.ZipFile(archive_path, "w", zipfile.ZIP_DEFLATED) as zf:
            zf.write(exe_path, exe_name)

    def _create_tar_gz(self, exe_path: Path, archive_path: Path, rid: str) -> None:
        """创建 .tar.gz 包。"""
        with tarfile.open(archive_path, "w:gz") as tf:
            tf.add(exe_path, APP_NAME)

    # ── 步骤 3: 发布说明 ───────────────────────

    def build_release_notes(self) -> str:
        """读取 RELEASE.md，附加 SHA256 表格。"""
        print("[3/5] 读取 RELEASE.md...")

        if not self.release_md_path.exists():
            raise FileNotFoundError(
                f"RELEASE.md 不存在: {self.release_md_path}\n"
                f"请在项目根目录创建 RELEASE.md 发布说明文件。"
            )

        content = self.release_md_path.read_text(encoding="utf-8")

        print(f"  RELEASE.md 已读取 ({len(content)} 字符)")
        return content

    # ── 步骤 5: OSS 上传 ───────────────────────

    def upload_to_oss(self, files: list[dict], vpk_artifacts: list[dict], release_notes: str) -> None:
        """上传 zip/tar.gz + Velopack 产物到阿里云 OSS（含 releases.json 索引更新）。"""
        print("[5/5] 上传到阿里云 OSS...")

        if self.dry_run:
            print("  [dry-run] 跳过 OSS 上传。")
            return

        try:
            import oss2
        except ImportError:
            print("  ! oss2 库未安装。跳过 OSS 上传。")
            print("    安装: pip install oss2")
            return

        oss_cfg = self.config["oss"]
        auth = oss2.Auth(oss_cfg["accessKeyId"], oss_cfg["accessKeySecret"])
        bucket = oss2.Bucket(auth, oss_cfg["endpoint"], oss_cfg["bucket"])

        prefix = f"releases/{self.version}"

        # 上传 RELEASE.md
        self._oss_put(bucket, f"{prefix}/RELEASE.md", release_notes.encode("utf-8"),
                      "text/markdown")
        print(f"  ✓ {prefix}/RELEASE.md")

        # 上传各平台包
        for f in files:
            local_path = Path(f["localPath"])
            oss_key = f"{prefix}/{f['fileName']}"
            self._oss_put_file(bucket, oss_key, str(local_path))
            print(f"  ✓ {oss_key} ({format_size(f['size'])})")

        # 上传 Velopack 产物到 updates/ 目录
        if vpk_artifacts:
            print("  [Velopack 产物]")
            for a in vpk_artifacts:
                oss_key = f"updates/{a['fileName']}"
                self._oss_put_file(bucket, oss_key, a["localPath"])
                print(f"    ✓ {oss_key} ({format_size(a['size'])})")

        # 更新 releases.json 索引
        self._update_releases_index(bucket, files, release_notes)

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

    def _update_releases_index(self, bucket, files: list[dict],
                               release_notes: str) -> None:
        """更新 releases.json 版本索引（ETag 乐观锁防并发覆盖）。"""
        index_key = "releases/releases.json"
        existing = None
        etag = None

        try:
            result = bucket.get_object(index_key)
            existing = json.loads(result.read())
            etag = result.headers.get("ETag")  # 保存 ETag 用于条件写入
            # oss2 返回的 ETag 可能包含或不包含引号；If-Match 要求带引号
            if etag and not etag.startswith('"'):
                etag = f'"{etag}"'
        except Exception:
            # releases.json 不存在 → 首次发布
            print("  releases.json 不存在，将创建新的版本索引。")

        if existing is None:
            existing = {"latest": self.version, "versions": []}
        else:
            # 检查版本是否已存在
            for v in existing.get("versions", []):
                if v["version"] == self.version:
                    raise ValueError(
                        f"版本 {self.version} 已存在于 releases.json 中。"
                        f"禁止覆盖已发布版本。"
                    )

        # 构建新条目（插入到头部）
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
                    "platform": f["platform"],
                    "fileName": f["fileName"],
                    "size": f["size"],
                    "sha256": f["sha256"],
                }
                for f in files
            ],
        }

        existing["versions"].insert(0, entry)
        existing["latest"] = self.version

        # 上传（使用 ETag 条件写入，防止并发覆盖）
        data = json.dumps(existing, ensure_ascii=False, indent=2).encode("utf-8")
        headers = {"Content-Type": "application/json"}
        if etag:
            headers["If-Match"] = etag

        try:
            bucket.put_object(index_key, data, headers=headers)
            print(f"  ✓ {index_key} (latest: {self.version})")
        except Exception as e:
            # oss2 412 PreconditionFailed → ETag 不匹配（并发写入冲突）
            if etag and getattr(e, "status", None) == 412:
                raise RuntimeError(
                    f"并发冲突: releases.json 已被其他进程修改。"
                    f"请重新运行发布脚本。"
                ) from e
            raise

    # ── 步骤 4: GitHub Release ────────────────

    def create_github_release(self, files: list[dict], vpk_artifacts: list[dict], release_notes: str) -> None:
        """通过 GitHub REST API 创建 Release，上传 zip/tar.gz + Velopack 产物。"""
        print("[4/5] 创建 GitHub Release...")

        if self.dry_run:
            print("  [dry-run] 跳过 GitHub Release 创建。")
            return

        try:
            import requests
        except ImportError:
            print("  ! requests 库未安装。跳过 GitHub Release。")
            print("    安装: pip install requests")
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
        all_artifacts = files + vpk_artifacts
        body = self._build_release_body(release_notes, all_artifacts)

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
        for f in all_artifacts:
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

    def rotate_worker_secrets(self) -> bool:
        """将 Worker 专用 OSS 只读凭证通过 Cloudflare API 下发到 Worker Secret。

        Worker 凭证与上传用的 oss.accessKeyId/Secret 是不同的子账号密钥：
        Worker 仅需 OSS 只读权限（回源下载），上传主密钥拥有写权限，不能混用。
        """
        if not self._has_cloudflare_config():
            print("[6/6] Worker Secret 轮换  → 跳过 (cloudflare 配置不完整)")
            return False

        cf = self.config["cloudflare"]
        account_id = cf["accountId"]
        script = cf["workerScript"]
        api_token = cf["apiToken"]

        worker_key_id = cf.get("workerOssKeyId")
        worker_key_secret = cf.get("workerOssKeySecret")
        if not worker_key_id or not worker_key_secret:
            print("[6/6] Worker Secret 轮换  → 跳过 (cloudflare.workerOssKeyId/Secret 未配置)")
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

        print(f"[6/6] Worker Secret 轮换: {script}...")

        if self.dry_run:
            print(f"  [dry-run] 将 PATCH {api_url}")
            print(f"  [dry-run] payload: {json.dumps(payload, ensure_ascii=False, indent=2)}")
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

    def _build_release_body(self, release_notes: str, files: list[dict]) -> str:
        """构建 Release body：RELEASE.md + SHA256 表格。"""
        lines = [release_notes.strip(), "", "### SHA256 Checksums", ""]
        lines.append("| File | SHA256 |")
        lines.append("|------|--------|")

        for f in files:
            lines.append(f"| {f['fileName']} | {f['sha256']} |")

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
            print("[0/5] 分支检查  → 跳过 (--skip-branch-check)")
            return True

        # CI 环境变量检测
        if any(os.environ.get(v) for v in ("CI", "GITHUB_ACTIONS", "GITLAB_CI")):
            print("[0/5] 分支检查  → 跳过 (CI 环境)")
            return True

        result = subprocess.run(
            ["git", "branch", "--show-current"],
            cwd=str(self.root),
            capture_output=True,
            text=True,
        )
        current = result.stdout.strip()

        if current == "main":
            print("[0/5] 分支检查  ✓ 已在 main 分支")
            return True

        # 检查是否有未提交的改动
        status = subprocess.run(
            ["git", "status", "--porcelain"],
            cwd=str(self.root),
            capture_output=True,
            text=True,
        )
        if status.stdout.strip():
            print(f"[0/5] 分支检查  ✗ 当前在 '{current}' 分支，且有未提交的改动。")
            print("    请先 commit 或 stash 改动后再切换。")
            return False

        # 干净，切换到 main
        print(f"[0/5] 分支检查  → 从 '{current}' 切换到 main...")
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
            print("[0/5] 版本号一致性检查  ✗ 失败")
            print(result.stdout)
            if result.stderr:
                print(result.stderr)
            return False
        print("[0/5] 版本号一致性检查  ✓ 通过")
        return True

    # ── 编排 ───────────────────────────────────

    def run(self) -> int:
        """执行全量发布流程。"""
        print(f"=== SeatFlow Release v{self.version} ===\n")

        try:
            # 步骤 0: 确保在 main 分支
            if not self._check_main_branch():
                return 1

            # 步骤 0: 版本号一致性检查
            if not self._check_versions():
                print("\n请先运行 python3 scripts/version.py check 查看详情，"
                      "再用 python3 scripts/version.py sync --force 修复同步问题。")
                return 1

            # 步骤 1: 构建
            build_outputs = self.build_all()

            if self.skip_build:
                # 从已有 dist 目录恢复
                build_outputs = self._find_existing_builds()

            if not build_outputs:
                print("错误: 无可用构建产物。")
                return 1

            # 步骤 1.5: vpk pack（Velopack 更新包）
            vpk_artifacts = self.vpk_pack_all(build_outputs)

            # 步骤 2: 打包 + SHA256
            files = self.package_all(build_outputs)
            if not files:
                print("错误: 无可用包文件。")
                return 1

            # 步骤 3: 发布说明
            release_notes = self.build_release_notes()

            # 步骤 4: GitHub Release（先创建，失败则 OSS 保持干净）
            self.create_github_release(files, vpk_artifacts, release_notes)

            # 步骤 5: OSS 上传（含 releases.json 索引更新）
            self.upload_to_oss(files, vpk_artifacts, release_notes)

            # 步骤 6: Cloudflare Worker Secret 轮换（可选，需配置 cloudflare 节）
            self.rotate_worker_secrets()

            print(f"\n=== Release v{self.version} 完成 ===")

            # 本地输出 SHA256 表格（zip/tar.gz + Velopack 产物）
            all_artifacts = files + vpk_artifacts
            print("\nSHA256 Checksums:")
            print(self._build_sha256_table(all_artifacts))

            return 0

        except (ValueError, FileNotFoundError) as e:
            print(f"\n错误: {e}")
            return 1
        except SystemExit as e:
            return e.code if isinstance(e.code, int) else 1

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

    def _build_sha256_table(self, files: list[dict]) -> str:
        """构建 SHA256 表格字符串。"""
        lines = ["| File | SHA256 |", "|------|--------|"]
        for f in files:
            lines.append(f"| {f['fileName']} | {f['sha256']} |")
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
    )

    return mgr.run()


if __name__ == "__main__":
    sys.exit(main())
