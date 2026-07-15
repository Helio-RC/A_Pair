#!/usr/bin/env python3
"""
CI 专用 OSS 上传脚本 — 从 GitHub Actions workflow 调用。

与 release.py 分离是因为 CI 环境不需要完整的 ReleaseManager，
只需要：
  1. 上传安装包 → releases/{version}/
  2. 上传更新包 → updates/
  3. 上传 RELEASE.md → releases/{version}/RELEASE.md
  4. 更新 releases/releases.json 索引

凭证通过环境变量注入（GitHub Secrets），不依赖 config.json。

用法:
  python3 scripts/release/ci_oss_upload.py \
    --version 1.4.0 \
    --artifacts-dir all-artifacts/ \
    --release-md RELEASE.md \
    --commit-id abc1234
"""

import argparse
import hashlib
import json
import os
import sys
from datetime import datetime, timezone
from pathlib import Path

try:
    import oss2
except ImportError:
    print("! oss2 未安装，跳过 OSS 上传。")
    sys.exit(0)


def sha256_file(path: Path) -> str:
    """计算文件的 SHA256。"""
    h = hashlib.sha256()
    with open(path, "rb") as f:
        while chunk := f.read(8192):
            h.update(chunk)
    return h.hexdigest()


def is_installer(file_name: str) -> bool:
    """判断是否为用户可下载的安装程序文件。"""
    lower = file_name.lower()
    return lower.endswith('.exe') or lower.endswith('.appimage') \
        or lower.endswith('.pkg') or lower.endswith('.dmg')


def main() -> int:
    parser = argparse.ArgumentParser(description="CI OSS 上传")
    parser.add_argument("--version", required=True, help="Semantic version (e.g. 1.4.0)")
    parser.add_argument("--artifacts-dir", required=True, help="Directory containing all artifacts")
    parser.add_argument("--release-md", required=True, help="Path to RELEASE.md")
    parser.add_argument("--commit-id", default="unknown", help="Git commit short hash")
    args = parser.parse_args()

    # 从环境变量读取 OSS 凭证
    key_id = os.environ.get("OSS_KEY_ID")
    key_secret = os.environ.get("OSS_KEY_SECRET")
    endpoint = os.environ.get("OSS_ENDPOINT")
    bucket_name = os.environ.get("OSS_BUCKET")

    if not all([key_id, key_secret, endpoint, bucket_name]):
        print("! OSS 凭证不完整，跳过上传。")
        print("  需要: OSS_KEY_ID, OSS_KEY_SECRET, OSS_ENDPOINT, OSS_BUCKET")
        return 0

    artifacts_dir = Path(args.artifacts_dir)
    release_md_path = Path(args.release_md)

    # 收集产物
    all_files = sorted(
        [f for f in artifacts_dir.iterdir() if f.is_file()],
        key=lambda f: f.name,
    )
    if not all_files:
        print("! 无产物文件，跳过上传。")
        return 0

    installers = [f for f in all_files if is_installer(f.name)]
    updates = [f for f in all_files if not is_installer(f.name)]

    print(f"产物: {len(installers)} 安装包 + {len(updates)} 更新包")

    # 连接 OSS
    auth = oss2.Auth(key_id, key_secret)
    bucket = oss2.Bucket(auth, endpoint, bucket_name)
    prefix = f"releases/{args.version}"

    # 1. 上传 RELEASE.md
    if release_md_path.exists():
        release_notes = release_md_path.read_text(encoding="utf-8")
        bucket.put_object(
            f"{prefix}/RELEASE.md",
            release_notes.encode("utf-8"),
            headers={"Content-Type": "text/markdown"},
        )
        print(f"  ✓ {prefix}/RELEASE.md")

    # 2. 上传安装包 → releases/{version}/
    if installers:
        print(f"  [安装包 → {prefix}/]")
        for f in installers:
            key = f"{prefix}/{f.name}"
            bucket.put_object_from_file(key, str(f))
            size_mb = f.stat().st_size / (1024 * 1024)
            print(f"    ✓ {key} ({size_mb:.1f} MiB)")

    # 3. 上传更新包 → updates/
    if updates:
        print("  [更新包 → updates/]")
        for f in updates:
            key = f"updates/{f.name}"
            bucket.put_object_from_file(key, str(f))
            size_mb = f.stat().st_size / (1024 * 1024)
            print(f"    ✓ {key} ({size_mb:.1f} MiB)")

    # 4. 更新 releases.json 索引
    _update_releases_index(bucket, args.version, args.commit_id,
                           installers, release_md_path)

    print("OSS 上传完成。")
    return 0


def _update_releases_index(bucket, version: str, commit_id: str,
                           installers: list[Path], release_md_path: Path) -> None:
    """更新 releases/releases.json 版本索引（ETag 乐观锁）。"""
    index_key = "releases/releases.json"
    existing = None
    etag = None

    try:
        result = bucket.get_object(index_key)
        existing = json.loads(result.read())
        etag = result.headers.get("ETag")
        if etag and not etag.startswith('"'):
            etag = f'"{etag}'
    except Exception:
        print("  releases.json 不存在，将创建新的版本索引。")

    if existing is None:
        existing = {"latest": version, "versions": []}
    else:
        for v in existing.get("versions", []):
            if v["version"] == version:
                print(f"  ! 版本 {version} 已存在于 releases.json，跳过索引更新。")
                return

    # 读取 RELEASE.md 作为 release notes
    release_notes = ""
    if release_md_path.exists():
        release_notes = release_md_path.read_text(encoding="utf-8").strip()

    # 构建新条目（插入到头部）
    release_date = datetime.now(timezone.utc).astimezone().isoformat()

    files_meta = []
    for f in installers:
        files_meta.append({
            "platform": _guess_platform(f.name),
            "fileName": f.name,
            "size": f.stat().st_size,
            "sha256": sha256_file(f),
        })

    entry = {
        "version": version,
        "commitId": commit_id,
        "releaseDate": release_date,
        "notes": release_notes,
        "files": files_meta,
    }

    existing["versions"].insert(0, entry)
    existing["latest"] = version

    # 条件写入（防止并发覆盖）
    data = json.dumps(existing, ensure_ascii=False, indent=2).encode("utf-8")
    headers = {"Content-Type": "application/json"}
    if etag:
        headers["If-Match"] = etag

    try:
        bucket.put_object(index_key, data, headers=headers)
        print(f"  ✓ {index_key} (latest: {version})")
    except Exception as e:
        if etag and getattr(e, "status", None) == 412:
            print(f"  ✗ 并发冲突: releases.json 已被其他进程修改。请重试。")
        raise


def _guess_platform(file_name: str) -> str:
    """从文件名推断平台标识。"""
    lower = file_name.lower()
    if "win" in lower or lower.endswith(".exe"):
        return "windows"
    if "linux" in lower or lower.endswith(".appimage"):
        return "linux"
    if "osx-arm64" in lower or "macos-arm64" in lower:
        return "macos-arm64"
    if "osx" in lower or "macos" in lower or lower.endswith((".pkg", ".dmg")):
        return "macos-x64"
    return "unknown"


if __name__ == "__main__":
    sys.exit(main())
