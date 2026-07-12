#!/usr/bin/env bash
# ============================================================
# 清理 bin/ & obj/ — 请在 scripts/build/ 目录下执行
# 用法: ./clean.sh       # 确认后删除
#        ./clean.sh -f    # 直接删除
#        ./clean.sh -n    # 仅预览
# ============================================================
set -euo pipefail
cd ../..

FORCE=false; DRY=false
while getopts "fn" opt; do case "$opt" in f) FORCE=true ;; n) DRY=true ;; *) echo "用法: $0 [-f] [-n]" && exit 1 ;; esac; done

echo -e "\e[36m=== SeatFlow 清理 bin/ & obj/ ===\e[0m"

mapfile -t dirs < <(find . -type d \( -name bin -o -name obj \) | sort)

if [ ${#dirs[@]} -eq 0 ]; then echo -e "\e[32m没有可清理的目录\e[0m"; exit 0; fi

total=0
for d in "${dirs[@]}"; do
    size=$(du -sk "$d" 2>/dev/null | cut -f1 || echo 0)
    total=$((total + size))
    echo -e "\e[90m  $d  ($((size/1024))M)\e[0m"
done

total_mb=$((total/1024))
echo -e "\n\e[33m共 ${#dirs[@]} 个目录，${total_mb}M\e[0m"

if $DRY; then echo -e "\e[36mDryRun — 未删除\e[0m"; exit 0; fi
if ! $FORCE; then read -r -p "确认删除? (y/N) " c; [ "$c" != "y" ] && [ "$c" != "Y" ] && { echo -e "\e[90m已取消\e[0m"; exit 0; }; fi

for d in "${dirs[@]}"; do rm -rf "$d"; done
echo -e "\e[32m已清理 ${total_mb}M\e[0m"
