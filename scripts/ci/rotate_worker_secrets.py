#!/usr/bin/env python3
"""Worker OSS 密钥同步脚本 — 将 OSS AccessKey 推送到 Cloudflare Worker。

供 worker-secret-sync.yml 定时调用；凭据从环境变量注入（GitHub secrets）。

用法:
  CF_ACCOUNT_ID=... CF_API_TOKEN=... CF_WORKER_SCRIPT=... \
  OSS_KEY_ID=... OSS_KEY_SECRET=... \
  python3 scripts/ci/rotate_worker_secrets.py
"""

import json
import os
import sys
import urllib.error
import urllib.request

CF_API_BASE = os.environ.get("CF_API_BASE", "https://api.cloudflare.com")

REQUIRED = [
    "CF_ACCOUNT_ID",
    "CF_API_TOKEN",
    "CF_WORKER_SCRIPT",
    "OSS_KEY_ID",
    "OSS_KEY_SECRET",
]


def main() -> int:
    missing = [k for k in REQUIRED if not os.environ.get(k)]
    if missing:
        print(f"✗ 缺少必需环境变量: {', '.join(missing)}")
        return 1

    account_id = os.environ["CF_ACCOUNT_ID"]
    script = os.environ["CF_WORKER_SCRIPT"]
    payload = {
        "OSS_KEY_ID": os.environ["OSS_KEY_ID"],
        "OSS_KEY_SECRET": os.environ["OSS_KEY_SECRET"],
    }

    url = f"{CF_API_BASE}/accounts/{account_id}/workers/scripts/{script}/secrets-bulk"
    req = urllib.request.Request(
        url,
        data=json.dumps(payload).encode("utf-8"),
        method="PUT",
        headers={
            "Authorization": f"Bearer {os.environ['CF_API_TOKEN']}",
            "Content-Type": "application/json",
        },
    )

    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            body = resp.read().decode("utf-8")
            print(f"✓ Worker secrets 更新成功（HTTP {resp.status}）")
            print("  已同步: OSS_KEY_ID, OSS_KEY_SECRET")
            if body:
                print(f"  响应: {body[:200]}")
            return 0
    except urllib.error.HTTPError as e:
        print(f"✗ Cloudflare API 返回 HTTP {e.code}")
        print(e.read().decode("utf-8")[:500])
        return 1
    except urllib.error.URLError as e:
        print(f"✗ 网络错误: {e.reason}")
        return 1


if __name__ == "__main__":
    sys.exit(main())
