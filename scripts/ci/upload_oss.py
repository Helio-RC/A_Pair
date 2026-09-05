#!/usr/bin/env python3
"""CI OSS 上传脚本 — 独立于 release.py，供 release.yml 调用。

职责:
  1. 安装包 → releases/{version}/
  2. 更新包（nupkg / RELEASES-* / releases.{channel}.json）→ updates/
  3. RELEASE.md → releases/{version}/RELEASE.md
  4. 更新 releases/releases.json 索引（含 SHA256）

所有凭据通过环境变量注入（GitHub Actions secrets/vars），
配置不写入本脚本或工作流文件。

用法:
  OSS_KEY_ID=... OSS_KEY_SECRET=... OSS_ENDPOINT=... OSS_BUCKET=... \
  python3 scripts/ci/upload_oss.py \
    --version 2.0.0 --artifacts-dir all-artifacts/ --release-md RELEASE.md --commit-id abc1234
"""

import argparse
import hashlib
import json
import os
import sys
from datetime import datetime, timezone, timedelta
from pathlib import Path

try:
    import oss2
    import oss2.exceptions  # noqa: F401
except ImportError:
    print("! 缺少 oss2 依赖，请先 pip install oss2")

def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        while chunk := f.read(8192):
            h.update(chunk)
    return h.hexdigest()


def is_installer(file_name: str) -> bool:
    n = file_name.lower()
    return n.endswith((".exe", ".appimage", ".pkg", ".dmg"))


def guess_platform(file_name: str) -> str:
    n = file_name.lower()
    if "win" in n or n.endswith(".exe"):
        return "windows"
    if "linux" in n or n.endswith(".appimage"):
        return "linux"
    if "osx-arm64" in n or "macos-arm64" in n:
        return "macos-arm64"
    if "osx" in n or "macos" in n or n.endswith((".pkg", ".dmg")):
        return "macos-x64"
    return "unknown"


def main() -> int:
    parser = argparse.ArgumentParser(description="CI OSS 上传")
    parser.add_argument("--version", required=True, help="语义版本（如 2.0.0）")
    parser.add_argument("--artifacts-dir", required=True, help="产物目录")
    parser.add_argument("--release-md", required=True, help="RELEASE.md 路径")
    parser.add_argument("--commit-id", default="unknown", help="Git commit sha")
    args = parser.parse_args()

    vars_need = ["OSS_KEY_ID", "OSS_KEY_SECRET", "OSS_ENDPOINT", "OSS_BUCKET"]
    missing = [k for k in vars_need if not os.environ.get(k)]
    if missing:
        print(f"✗ OSS 凭证缺失：{', '.join(missing)}（检查 Actions secrets/vars 配置）")
        return 1

    artifacts = Path(args.artifacts_dir)
    if not artifacts.is_dir():
        print(f"✗ 产物目录不存在: {artifacts}")
        return 1

    auth = oss2.Auth(os.environ["OSS_KEY_ID"], os.environ["OSS_KEY_SECRET"])
    bucket = oss2.Bucket(auth, os.environ["OSS_ENDPOINT"], os.environ["OSS_BUCKET"])

    files = sorted([f for f in artifacts.iterdir() if f.is_file()], key=lambda f: f.name)
    if not files:
        print("✗ 无产物文件")
        return 1

    installers = [f for f in files if is_installer(f.name)]
    updates = [f for f in files if not is_installer(f.name)]
    print(f"产物: {len(installers)} 安装包 + {len(updates)} 更新文件")

    # ── 0. 预检 releases.json 索引（重复版本 → 中止） ──
    index_key = "releases/releases.json"
    existing = None
    try:
        r = bucket.get_object(index_key)
        existing = json.loads(r.read())
    except oss2.exceptions.NoSuchKey:
        print("  releases.json 不存在，将创建。")
    except Exception:
        print("✗ 读取 releases.json 失败")
        return 1

    if existing is None:
        existing = {"latest": "", "versions": []}
    for v in existing.get("versions", []):
        if v["version"] == args.version:
            print(f"✗ 版本 {args.version} 已存在于 releases.json，中止。")
            return 1

    prefix = f"releases/{args.version}"

    # ── 1. RELEASE.md ──
    release_md = Path(args.release_md)
    release_notes = ""
    if release_md.exists():
        release_notes = release_md.read_text(encoding="utf-8")
        bucket.put_object(f"{prefix}/RELEASE.md", release_notes.encode("utf-8"),
                          headers={"Content-Type": "text/markdown"})
        print(f"  ✓ {prefix}/RELEASE.md")

    # ── 2. 安装包 → releases/{version}/（已存在则跳过） ──
    for f in installers:
        key = f"{prefix}/{f.name}"
        if bucket.object_exists(key):
            print(f"    - {key} (已存在，跳过)")
            continue
        bucket.put_object_from_file(key, str(f))
        print(f"    ✓ {key} ({f.stat().st_size / 1048576:.1f} MiB)")

    # ── 3. 更新包 → updates/（nupkg 幂等，索引类每次覆盖） ──
    for f in updates:
        key = f"updates/{f.name}"
        if f.name.lower().endswith(".nupkg") and bucket.object_exists(key):
            print(f"    - {f.name} (已存在，跳过)")
            continue
        bucket.put_object_from_file(key, str(f))
        print(f"    ✓ updates/{f.name}")

    # ── 4. 更新索引 ──
    release_date = datetime.now(timezone(timedelta(hours=8))).isoformat()
    entry = {
        "version": args.version,
        "commitId": args.commit_id,
        "releaseDate": release_date,
        "notes": release_notes.strip()[:2000],
        "files": [
            {
                "platform": guess_platform(f.name),
                "fileName": f.name,
                "size": f.stat().st_size,
                "sha256": sha256_file(f),
            }
            for f in installers
        ],
    }
    existing["versions"].insert(0, entry)
    existing["latest"] = args.version
    bucket.put_object(index_key,
                      json.dumps(existing, ensure_ascii=False, indent=2).encode("utf-8"),
                      headers={"Content-Type": "application/json"})
    print(f"  ✓ {index_key} (latest: {args.version})")
    print("OSS 上传完成。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
