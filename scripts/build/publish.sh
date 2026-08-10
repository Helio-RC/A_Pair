#!/usr/bin/env bash
# ============================================================
# SeatFlow 多平台发布 — TUI 交互 / CLI 兼容
# 用法: ./publish.sh                                  # TUI
#        ./publish.sh both Release opt "" "1.1.0" clean aot  # CLI
#        ./publish.sh hash                              # 仅哈希
# ============================================================
set -euo pipefail
OLDPWD="$PWD"; trap 'cd "$OLDPWD"' EXIT
cd ../..

APP_NAME="SeatFlow"; PROJECT="src/SeatFlow.Presentation.Avalonia"; CONFIG="Release"
RIDS=("win-x64" "linux-x64" "osx-x64" "osx-arm64")
SUFFIXES=(".exe" "" "" ""); SEL=(0 0 0 0)
TYPE_IDX=2; TRIM_SEL=0; AOT=0; CLEAN=0; CURSOR=0; SUFFIX=""; VERSION=""; ITEMS=16

step(){ echo -e "  [$(date +%H:%M:%S)] \e[${2:-37}m$1\e[0m"; }

sha_table(){
    local files=() f h n
    while IFS= read -r -d '' f; do files+=("$f"); done < <(find publish -type f -name "$APP_NAME-*" -print0 2>/dev/null | sort -z)
    if [ ${#files[@]} -eq 0 ]; then echo -e "\e[33m没有找到已发布文件\e[0m"; return; fi
    echo ""; echo "| 文件 | SHA256 |"; echo "|------|--------|"
    for f in "${files[@]}"; do h=$(sha256sum "$f"|awk '{print tolower($1)}'); n=$(basename "$f"); echo "| \`$n\` | $h |"; done
}

publish_one(){
    local sc="$1" label="$2"; shift 2; local rids=("$@") rid sf tmp fn exe fb s
    local sc_flag="false"; [ "$sc" = "true" ] && sc_flag="true"
    local base="publish/$label"; mkdir -p "$base"
    for rid in "${rids[@]}"; do
        sf=""; [ "${rid:0:3}" = "win" ] && sf=".exe"
        tmp="$base/.tmp_$rid"; fn="$APP_NAME${VERSION:+-$VERSION}-$label-$rid${SUFFIX:+-$SUFFIX}$sf"
        echo -ne "\033]0;SeatFlow: $label / $rid\007"
        echo ""; echo -e "\e[36m══════════════════════════════════════════\e[0m"
        echo -e "\e[36m  $label / $rid\e[0m"
        echo -e "\e[36m══════════════════════════════════════════\e[0m"
        step "开始编译..." 33
        local ta=()
        [ "$TRIM_SEL" = "1" ] && [ "$sc" = "true" ] && ta=(-p:PublishTrimmed=true -p:TrimMode=partial -p:SuppressTrimAnalysisWarnings=true)
        [ "$AOT" = "1" ] && [ "$sc" = "true" ] && ta+=(-p:PublishAot=true)
        dotnet publish "$PROJECT" -c "$CONFIG" -r "$rid" --self-contained "$sc_flag" "${ta[@]}" -o "$tmp"
        exe="$APP_NAME$sf"
        if [ -f "$tmp/$exe" ]; then
            # 标准发布：打包整个输出目录
            fn="${fn%.exe}"  # 移除 .exe 后缀（Windows）
            local ext=".tar.gz"
            [ "${rid:0:3}" = "win" ] && ext=".zip"
            fn="$fn$ext"
            if [ "$ext" = ".zip" ]; then
                (cd "$tmp" && zip -qr "$OLDPWD/$base/$fn" .) && rm -rf "$tmp"
            else
                tar -czf "$base/$fn" -C "$tmp" . && rm -rf "$tmp"
            fi
            s=$(du -h "$base/$fn"|cut -f1); step "完成 → $label/$fn ($s)" 32
        else
            step "未找到可执行文件: $tmp/$exe" 31; fi
    done
}

# CLI 模式
if [ $# -gt 0 ]; then
    if [ "${1:-}" = "hash" ]; then sha_table; exit 0; fi
    MODE="${1:-both}"; CONFIG="${2:-Release}"; [ "${3:-}" = "opt" ] && TRIM_SEL=1; SUFFIX="${4:-}"; VERSION="${5:-}"; [ "${6:-}" = "clean" ] && CLEAN=1; [ "${7:-}" = "aot" ] && AOT=1
    if [ "$CLEAN" = "1" ] && [ -d publish ]; then find publish -type f | while read -r f; do echo -e "\e[90m  $f\e[0m"; done; read -r -p "确认删除以上文件? (y/N) " c; [ "$c" = "y" ] && { rm -rf publish; echo -e "\e[33m已清空\e[0m"; } || echo -e "\e[90m已取消\e[0m"; fi
    S=$(date +%s)
    if [ "$MODE" != "slim" ]; then echo -e "\n\e[35m--- 自包含 ---\e[0m"; publish_one "true" "full" "${RIDS[@]}"; fi
    if [ "$MODE" != "full" ]; then echo -e "\n\e[35m--- 依赖运行时 ---\e[0m"; publish_one "false" "slim" "${RIDS[@]}"; fi
    E=$(date +%s); echo -e "\n\e[36m完成 $((E-S))s\e[0m"; sha_table; exit 0
fi

# TUI 模式
OLD=$(stty -g 2>/dev/null||echo sane)
on_exit(){ stty "$OLD" 2>/dev/null||stty sane; printf "\e[?25h"; }
trap on_exit EXIT INT TERM HUP
printf "\e[2J\e[H"
echo "  SeatFlow 发布"; echo ""
stty -echo -icanon min 1 time 1 2>/dev/null||true; printf "\e[?25l"

read_key(){
    key=""
    while [ -z "$key" ]; do
        IFS= read -rsN1 b || continue
        case "$b" in
            $'\e')
                IFS= read -rsN1 -t 0.1 b2 || { key="ESC"; return; }
                case "$b2" in
                    '[') IFS= read -rsN1 b3; case "$b3" in A) key="UP";; B) key="DN";; esac ;;
                esac ;;
            ' ') key="SPC";;
            a|A) key="A_KEY";;
            n|N) key="N_KEY";;
            $'\n'|$'\r') key="ENT";;
        esac
    done
}

draw(){
    local mk hi nm
    printf "\e[3;1H"
    echo "  平台（空格切换）："
    for i in 0 1 2 3; do
        mk="[ ]"; [ "${SEL[$i]}" = "1" ] && mk="[*]"
        hi=" "; [ "$CURSOR" = "$i" ] && hi=">"
        echo "     $hi$mk ${RIDS[$i]}"
    done
    local sa=" "; [ "$CURSOR" = "4" ] && sa=">"
    echo "     $sa[A] 全选"
    local sn=" "; [ "$CURSOR" = "5" ] && sn=">"
    echo "     $sn[N] 全不选"
    echo ""
    echo "  发布类型（Enter 选择）："
    types=("自包含" "依赖运行时" "两者")
    for i in 0 1 2; do mk=" "; [ "$i" = "$TYPE_IDX" ] && mk="*"; hi=" "; [ "$CURSOR" = "$((6+i))" ] && hi=">"; echo "     $hi$mk ${types[$i]}"; done
    echo ""
    echo "  优化选项："
    mk="[ ]"; [ "$TRIM_SEL" = "1" ] && mk="[*]"; tc=" "; [ "$CURSOR" = "9" ] && tc=">"
    echo "     $tc$mk 裁剪 (TrimMode=partial)"
    mk="[ ]"; [ "$AOT" = "1" ] && mk="[*]"; taot=" "; [ "$CURSOR" = "10" ] && taot=">"
    echo "     $taot$mk AOT 编译 (需自包含)"
    local sx=" "; [ "$CURSOR" = "11" ] && sx=">"
    local sd="[未设置]"; [ -n "$SUFFIX" ] && sd="[$SUFFIX]"
    echo "     $sx$sd 文件名后缀（Enter 设置）"
    local vx=" "; [ "$CURSOR" = "12" ] && vx=">"
    local vd="[未设置]"; [ -n "$VERSION" ] && vd="[$VERSION]"
    echo "     $vx$vd 版本号（Enter 设置）"
    echo ""
    echo "  操作："
    mk="[ ]"; [ "$CLEAN" = "1" ] && mk="[*]"; ci=" "; [ "$CURSOR" = "13" ] && ci=">"
    echo "     $ci$mk 编译前清空 publish/ 目录"
    local b1=" "; [ "$CURSOR" = "14" ] && b1=">"
    echo "     $b1[ 开始编译 ]"
    local b2=" "; [ "$CURSOR" = "15" ] && b2=">"
    echo "     $b2[ 仅计算哈希 ]"
    echo ""
    echo "  ↑↓移动  Space切换  Enter确认  Esc退出"
}

draw
while true; do
    read_key
    case "$key" in
        UP) CURSOR=$(((CURSOR-1+ITEMS)%ITEMS)); draw ;;
        DN) CURSOR=$(((CURSOR+1)%ITEMS)); draw ;;
        SPC) if [ "$CURSOR" -lt 4 ]; then [ "${SEL[$CURSOR]}" = "1" ] && SEL[$CURSOR]=0 || SEL[$CURSOR]=1
            elif [ "$CURSOR" = "4" ]; then for i in 0 1 2 3; do SEL[$i]=1; done
            elif [ "$CURSOR" = "5" ]; then for i in 0 1 2 3; do SEL[$i]=0; done
            elif [ "$CURSOR" -ge 6 ] && [ "$CURSOR" -le 8 ]; then TYPE_IDX=$((CURSOR-6))
            elif [ "$CURSOR" = "9" ]; then [ "$TRIM_SEL" = "1" ] && TRIM_SEL=0 || TRIM_SEL=1
            elif [ "$CURSOR" = "10" ]; then [ "$AOT" = "1" ] && AOT=0 || AOT=1
            elif [ "$CURSOR" = "13" ]; then [ "$CLEAN" = "1" ] && CLEAN=0 || CLEAN=1; fi; draw ;;
        A_KEY) [ "$CURSOR" = "4" ] && { for i in 0 1 2 3; do SEL[$i]=1; done; draw; } ;;
        N_KEY) [ "$CURSOR" = "5" ] && { for i in 0 1 2 3; do SEL[$i]=0; done; draw; } ;;
        ESC) stty "$OLD" 2>/dev/null||stty sane; printf "\e[?25h\e[2J"; exit 0 ;;
        ENT) if [ "$CURSOR" = "11" ]; then printf "\e[?25h"; stty "$OLD" 2>/dev/null||stty sane; read -r -p "文件名后缀: " SUFFIX; stty -echo -icanon min 1 time 1 2>/dev/null; printf "\e[?25l"; draw
            elif [ "$CURSOR" = "12" ]; then printf "\e[?25h"; stty "$OLD" 2>/dev/null||stty sane; read -r -p "版本号 (如 1.1.0): " VERSION; stty -echo -icanon min 1 time 1 2>/dev/null; printf "\e[?25l"; draw
            elif [ "$CURSOR" = "14" ]; then break
            elif [ "$CURSOR" = "15" ]; then stty "$OLD" 2>/dev/null||stty sane; printf "\e[?25h\n"; sha_table; exit 0
            elif [ "$CURSOR" -ge 6 ] && [ "$CURSOR" -le 8 ]; then TYPE_IDX=$((CURSOR-6)); draw; fi ;;
    esac
done
printf "\e[?25h\n"
stty "$OLD" 2>/dev/null||stty sane

SP=(); for i in 0 1 2 3; do [ "${SEL[$i]}" = "1" ] && SP+=("${RIDS[$i]}"); done
if [ "$CLEAN" = "1" ] && [ -d publish ]; then find publish -type f | while read -r f; do echo -e "\e[90m  $f\e[0m"; done; read -r -p "确认删除以上文件? (y/N) " c; [ "$c" = "y" ] && { rm -rf publish; echo -e "\e[33m已清空\e[0m"; } || echo -e "\e[90m已取消\e[0m"; fi
if [ ${#SP[@]} -eq 0 ]; then echo -e "\e[31m未选择任何平台\e[0m"; exit 1; fi

doSc=false; doFd=false
[ "$TYPE_IDX" = "0" ] && doSc=true; [ "$TYPE_IDX" = "1" ] && doFd=true; [ "$TYPE_IDX" = "2" ] && { doSc=true; doFd=true; }
S=$(date +%s)
if $doSc; then echo -e "\n\e[35m--- 自包含 (Self-Contained) ---\e[0m"; publish_one "true" "full" "${SP[@]}"; fi
if $doFd; then echo -e "\n\e[35m--- 依赖运行时 (Framework-Dependent) ---\e[0m"; publish_one "false" "slim" "${SP[@]}"; fi
E=$(date +%s); echo -e "\n\e[36m完成 $((E-S))s\e[0m"; sha_table
