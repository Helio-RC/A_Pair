#!/usr/bin/env bash
# SeatFlow 示例插件构建脚本：编译程序集插件并打包所有 .ap-plugin 到 dist/
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SDK_PROJ="$ROOT/../SeatFlow.Plugins.Sdk/SeatFlow.Plugins.Sdk.csproj"
DIST="$ROOT/dist"
CONFIG="${CONFIG:-Debug}"

rm -rf "$DIST"
mkdir -p "$DIST"

log() { echo "[build] $*"; }

# ── 程序集插件：编译并把输出复制到插件策略目录 ──
build_assembly_plugin() {
  local src_dir="$1"   # 插件 csproj 目录
  local name="$2"      # 输出 DLL 名
  local out_dir="$3"   # 组装目录

  log "编译 $name ..."
  dotnet build "$src_dir/$name.csproj" -c "$CONFIG" --nologo -v:q -p:OutDir="$out_dir/bin/"
  # 组装目录只保留插件自身 DLL（依赖 Sdk/Contracts 由宿主提供或随包）
  cp "$out_dir/bin/$name.dll" "$out_dir/"
  rm -rf "$out_dir/bin"
}

# ── 组装脚本插件目录（复制内容，避免目标已存在时产生嵌套目录） ──
assemble_dir() {
  mkdir -p "$2"
  cp -r "$1/." "$2/"
}

# ── 打包为 .ap-plugin（ZIP） ──
package() {
  local pkg_dir="$1"   # 组装目录（含 plugins-manifest.json）
  local pkg_name="$2"
  ( cd "$pkg_dir" && zip -qr "$DIST/$pkg_name.ap-plugin" . )
  log "已打包 dist/$pkg_name.ap-plugin"
}

# ═══ 1. HeightSortPlugin（程序集独立策略） ═══
HS="$ROOT/_build/height-sort"
mkdir -p "$HS/strategy"
build_assembly_plugin "$ROOT/src/HeightSortPlugin" "HeightSortPlugin" "$HS/strategy"
cp "$ROOT/src/HeightSortPlugin/plugins-manifest.json" "$HS/"
cp "$ROOT/src/HeightSortPlugin/strategy/manifest.json" "$HS/strategy/"
package "$HS" "height-sort"

# ═══ 2. DeskPairPlugin（程序集依赖策略） ═══
DP="$ROOT/_build/desk-pair"
mkdir -p "$DP/strategy"
build_assembly_plugin "$ROOT/src/DeskPairPlugin" "DeskPairPlugin" "$DP/strategy"
cp "$ROOT/src/DeskPairPlugin/plugins-manifest.json" "$DP/"
cp "$ROOT/src/DeskPairPlugin/strategy/manifest.json" "$DP/strategy/"
package "$DP" "desk-pair"

# ═══ 3. ScriptPlugins（Lua + C# 脚本策略） ═══
SP="$ROOT/_build/script-plugins"
mkdir -p "$SP/lua" "$SP/csharp"
assemble_dir "$ROOT/src/ScriptPlugins/lua" "$SP/lua"
assemble_dir "$ROOT/src/ScriptPlugins/csharp" "$SP/csharp"
cp "$ROOT/src/ScriptPlugins/plugins-manifest.json" "$SP/"
package "$SP" "script-plugins"

# ═══ 4. MultiStrategyPackage（多策略包） ═══
MS="$ROOT/_build/multi-strategy"
mkdir -p "$MS/strat-a" "$MS/strat-b"
cp "$ROOT/src/MultiStrategyPackage/plugins-manifest.json" "$MS/"
cp "$ROOT/src/MultiStrategyPackage/strat-a/manifest.json" "$MS/strat-a/"
cp "$ROOT/src/MultiStrategyPackage/strat-b/manifest.json" "$MS/strat-b/"
cp "$ROOT/src/MultiStrategyPackage/strat-a/script.lua" "$MS/strat-a/"
cp "$ROOT/src/MultiStrategyPackage/strat-b/script.lua" "$MS/strat-b/"
package "$MS" "multi-strategy"

rm -rf "$ROOT/_build"
log "完成："
ls -la "$DIST"
