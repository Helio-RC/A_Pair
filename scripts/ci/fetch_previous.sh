#!/usr/bin/env bash
# 拉取上一版本发布产物（供 vpk pack 生成 delta 增量包）。
# 首次发布 / feed 无历史 / 配置缺失时容错退出（仅生成 full 包）。
#
# 环境变量:
#   VPK_CHANNEL     必需 — 渠道名（如 win-x64 / linux-x64，与 matrix.rid 一致）
#   UPDATE_FEED_URL 必需 — 更新源 base URL（如 https://download.seatflow.work/updates/）
#   OUTPUT_DIR      可选 — 输出目录，默认 publish/out（与 vpk pack 的 --outputDir 一致）
set -u

CHANNEL="${VPK_CHANNEL:-}"
FEED_URL="${UPDATE_FEED_URL:-}"
OUT="${OUTPUT_DIR:-publish/out}"

if [ -z "$CHANNEL" ]; then
  echo "[warn] VPK_CHANNEL 未设置，跳过历史拉取（仅生成 full 包）"
  exit 0
fi
if [ -z "$FEED_URL" ]; then
  echo "[warn] UPDATE_FEED_URL 未设置，跳过历史拉取（仅生成 full 包）"
  exit 0
fi

mkdir -p "$OUT"

if vpk download http -o "$OUT" -c "$CHANNEL" --url "$FEED_URL"; then
  echo "✓ 已拉取历史产物到 ${OUT}（delta 基础就绪）"
else
  echo "[warn] 拉取历史产物失败（首次发布或无历史），本次仅生成 full 包"
fi

exit 0
