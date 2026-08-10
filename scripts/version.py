#!/usr/bin/env python3
"""
version — SeatFlow 版本号统一管理。

管理项目中的 4 类版本号：
  - App 版本（version.json）
  - 文件格式版本（file_versions.json + Model 类默认值）
  - 策略清单版本（Manifests/*.json）
  - 引导配置版本（onboarding_config.json）

用法:
  python3 version.py show
  python3 version.py check
  python3 version.py bump-app [major|minor|patch|--set X.Y.Z] [--dry-run] [--force]
  python3 version.py bump-file TYPE --set X.Y [--dry-run] [--force]
  python3 version.py bump-strategy ID --set X.Y.Z [--dry-run] [--force]
  python3 version.py bump-onboarding --set X.Y [--dry-run] [--force]
  python3 version.py sync [--dry-run] [--force]
"""

import argparse
import json
import os
import re
import shutil
import sys
from datetime import datetime
from pathlib import Path
from typing import Optional


# ──────────────────────────────────────────────
# 常量
# ──────────────────────────────────────────────

APP_VERSION_RE = re.compile(r"^\d+\.\d+\.\d+$")
FILE_VERSION_RE = re.compile(r"^\d+\.\d+$")
STRATEGY_VERSION_RE = re.compile(r"^\d+\.\d+\.\d+$")
MANIFEST_VERSION_RE = re.compile(r"^\d+\.\d+$")
ONBOARDING_VERSION_RE = re.compile(r"^\d+\.\d+$")

# Model 类中 Version 默认值的匹配模式
MODEL_VERSION_RE = re.compile(
    r'(public\s+(string|required\s+string)\s+Version\s*\{\s*get;\s*(set|init);\s*\}\s*=\s*)"([^"]+)"'
)

# file_versions.json 类型 -> Model 文件映射
FILE_TYPE_TO_MODEL: dict[str, str] = {
    "venue": "VenueFile.cs",
    "roster": "RosterFile.cs",
    "snapshot": "SeatingSnapshot.cs",
    "venueInfo": "VenueSnapshotInfo.cs",
    "appSettings": "AppSettings.cs",
    "strategyConfig": "StrategyConfig.cs",
    "strategyDatasetConfig": "StrategyDatasetConfig.cs",
    "seatsets": "SeatSets/SeatSetsArchive.cs",
}


# ──────────────────────────────────────────────
# 工具函数
# ──────────────────────────────────────────────

def bump_version(version: str, level: str) -> str:
    """递增语义化版本号。"""
    parts = [int(x) for x in version.split(".")]
    if level == "major":
        return f"{parts[0] + 1}.0.0"
    elif level == "minor":
        return f"{parts[0]}.{parts[1] + 1}.0"
    elif level == "patch":
        return f"{parts[0]}.{parts[1]}.{parts[2] + 1}"
    raise ValueError(f"未知的递增级别: {level}")


def resolve_root(root: Optional[str] = None) -> Path:
    """解析项目根目录。"""
    if root:
        return Path(root)
    return Path(__file__).resolve().parent.parent


def confirm(msg: str, force: bool = False) -> bool:
    """请求用户确认。"""
    if force:
        return True
    try:
        resp = input(f"{msg} (y/N): ").strip().lower()
        return resp in ("y", "yes")
    except (EOFError, KeyboardInterrupt):
        print("\n已取消。")
        return False


def read_json(path: Path) -> dict:
    """读取 JSON 文件。"""
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def write_json(path: Path, data: dict):
    """写入 JSON 文件（保持缩进和格式）。"""
    with open(path, "w", encoding="utf-8", newline="") as f:
        json.dump(data, f, ensure_ascii=False, indent=4)
        f.write("\n")


def read_text(path: Path) -> str:
    """读取文本文件。"""
    with open(path, "r", encoding="utf-8") as f:
        return f.read()


def write_text(path: Path, content: str):
    """写入文本文件。"""
    with open(path, "w", encoding="utf-8", newline="") as f:
        f.write(content)


# ──────────────────────────────────────────────
# VersionManager — 核心管理器
# ──────────────────────────────────────────────

class VersionManager:
    """管理项目所有版本号。"""

    def __init__(self, root: Path):
        self.root = root
        self.backup_dir = root / ".version-backups"

        # 路径定义
        self.version_json_path = root / "version.json"
        self.about_path = root / "src/SeatFlow.Presentation.Avalonia/Data/about.json"
        self.file_versions_path = root / "src/SeatFlow.Infrastructure/Migration/file_versions.json"
        self.onboarding_path = root / "src/SeatFlow.Presentation.Avalonia/Data/onboarding_config.json"
        self.manifests_dir = root / "src/SeatFlow.Core/Strategies/Manifests"
        self.models_dir = root / "src/SeatFlow.Core/Models"
        self.json_student_writer_path = root / "src/SeatFlow.Infrastructure/Providers/JsonStudentWriter.cs"
        self.manifest_provider_path = root / "src/SeatFlow.Core/Services/StrategyManifestProvider.cs"

    # ── 备份 ──────────────────────────────────

    def backup(self):
        """备份将被修改的所有文件。"""
        self.backup_dir.mkdir(parents=True, exist_ok=True)
        ts = datetime.now().strftime("%Y%m%d_%H%M%S")
        files_to_backup = set()
        files_to_backup.add(self.version_json_path)
        files_to_backup.add(self.about_path)
        files_to_backup.add(self.file_versions_path)
        files_to_backup.add(self.onboarding_path)
        for mf in self.manifests_dir.glob("*.json"):
            files_to_backup.add(mf)
        for model_file in FILE_TYPE_TO_MODEL.values():
            files_to_backup.add(self.models_dir / model_file)
        files_to_backup.add(self.json_student_writer_path)

        for src in files_to_backup:
            if src.exists():
                dst = self.backup_dir / f"{src.name}.{ts}"
                shutil.copy2(src, dst)
        print(f"已备份到 {self.backup_dir}/ (时间戳: {ts})")

    # ── 读取 ──────────────────────────────────

    def get_app_version(self) -> str:
        """读取 App 版本。"""
        data = read_json(self.version_json_path)
        return data.get("version", "0.0.0")

    def get_file_versions(self) -> dict[str, str]:
        """读取文件格式版本。"""
        return read_json(self.file_versions_path)

    def get_strategy_versions(self) -> dict[str, dict[str, str]]:
        """读取所有策略清单版本。"""
        result = {}
        for mf in sorted(self.manifests_dir.glob("*.json")):
            data = read_json(mf)
            result[mf.stem] = {
                "version": data.get("version", ""),
                "manifestVersion": data.get("manifestVersion", ""),
            }
        return result

    def get_onboarding_version(self) -> str:
        """读取引导配置版本。"""
        data = read_json(self.onboarding_path)
        return data.get("version", "")

    def get_model_versions(self) -> dict[str, str]:
        """从 Model C# 文件中提取 Version 默认值。"""
        result = {}
        for ftype, fname in FILE_TYPE_TO_MODEL.items():
            path = self.models_dir / fname
            if path.exists():
                content = read_text(path)
                m = MODEL_VERSION_RE.search(content)
                if m:
                    result[ftype] = m.group(4)
        return result

    # ── Show ──────────────────────────────────

    def show(self):
        """显示全部版本号概览。"""
        app = self.get_app_version()
        file_vers = self.get_file_versions()
        model_vers = self.get_model_versions()
        strategy_vers = self.get_strategy_versions()
        onboarding = self.get_onboarding_version()

        # App
        print("═══════════════════════════════════════")
        print("  App 版本")
        print("═══════════════════════════════════════")
        print(f"  version.json                 {app}")

        # File format
        print()
        print("═══════════════════════════════════════")
        print("  文件格式版本")
        print("═══════════════════════════════════════")
        print(f"  {'类型':<25} {'JSON':<8} {'Model 类':<8} {'状态'}")
        print(f"  {'─' * 25} {'─' * 8} {'─' * 8} {'─' * 10}")
        for ftype in sorted(file_vers.keys()):
            jv = file_vers[ftype]
            mv = model_vers.get(ftype, "?")
            status = "✓" if jv == mv else "✗ 不一致"
            print(f"  {ftype:<25} {jv:<8} {mv:<8} {status}")

        # Strategy
        print()
        print("═══════════════════════════════════════")
        print("  策略清单版本")
        print("═══════════════════════════════════════")
        print(f"  {'策略':<25} {'version':<10} {'manifestVersion':<15}")
        print(f"  {'─' * 25} {'─' * 10} {'─' * 15}")
        for sid, sv in strategy_vers.items():
            print(f"  {sid:<25} {sv['version']:<10} {sv['manifestVersion']:<15}")

        # Onboarding
        print()
        print("═══════════════════════════════════════")
        print("  引导配置版本")
        print("═══════════════════════════════════════")
        print(f"  onboarding_config.json     {onboarding}")

        print()

    # ── Check ─────────────────────────────────

    def check(self) -> list[dict]:
        """校验所有版本定义的一致性。"""
        issues = []

        # App 版本
        if self.version_json_path.exists():
            data = read_json(self.version_json_path)
            ver = data.get("version", "")
            if ver and not APP_VERSION_RE.match(ver):
                issues.append({"level": "ERROR", "msg": f"App 版本 '{ver}' 格式不正确 (应为 X.Y.Z)"})

        # 文件格式版本
        file_vers = self.get_file_versions()
        model_vers = self.get_model_versions()

        for ftype, jv in file_vers.items():
            if not FILE_VERSION_RE.match(jv):
                issues.append({"level": "ERROR", "msg": f"file_versions.json: '{ftype}' 版本 '{jv}' 格式不正确 (应为 X.Y)"})

            mv = model_vers.get(ftype)
            if mv is None:
                issues.append({"level": "ERROR", "msg": f"Model 类中未找到 '{ftype}' 的 Version 定义"})
            elif jv != mv:
                issues.append({"level": "ERROR", "msg": f"'{ftype}': file_versions.json({jv}) ≠ Model 类默认值({mv})"})

        # JsonStudentWriter 硬编码版本
        if self.json_student_writer_path.exists():
            writer_content = read_text(self.json_student_writer_path)
            writer_match = re.search(r'Version\s*=\s*"([^"]+)"', writer_content)
            if writer_match:
                writer_ver = writer_match.group(1)
                roster_ver = file_vers.get("roster", "")
                if writer_ver != roster_ver:
                    issues.append({
                        "level": "WARNING",
                        "msg": f"JsonStudentWriter.cs 硬编码版本 '{writer_ver}' 与 roster 版本 '{roster_ver}' 不一致",
                    })

        # 策略清单版本
        for mf in sorted(self.manifests_dir.glob("*.json")):
            data = read_json(mf)
            sv = data.get("version", "")
            mv = data.get("manifestVersion", "")
            if sv and not STRATEGY_VERSION_RE.match(sv):
                issues.append({"level": "ERROR", "msg": f"{mf.name}: version '{sv}' 格式不正确 (应为 X.Y.Z)"})
            if mv and not MANIFEST_VERSION_RE.match(mv):
                issues.append({"level": "ERROR", "msg": f"{mf.name}: manifestVersion '{mv}' 格式不正确 (应为 X.Y)"})

        # MaxManifestVersion
        if self.manifest_provider_path.exists():
            provider_content = read_text(self.manifest_provider_path)
            max_match = re.search(r'MaxManifestVersion\s*=\s*"([^"]+)"', provider_content)
            if max_match:
                max_ver = max_match.group(1)
                for mf in sorted(self.manifests_dir.glob("*.json")):
                    data = read_json(mf)
                    mv = data.get("manifestVersion", "")
                    if mv and _compare_versions(mv, max_ver) > 0:
                        issues.append({
                            "level": "WARNING",
                            "msg": f"{mf.name}: manifestVersion({mv}) > MaxManifestVersion({max_ver})",
                        })

        # 引导配置版本
        if self.onboarding_path.exists():
            ov = self.get_onboarding_version()
            if ov and not ONBOARDING_VERSION_RE.match(ov):
                issues.append({"level": "ERROR", "msg": f"onboarding_config.json: version '{ov}' 格式不正确 (应为 X.Y)"})

        return issues

    # ── Bump App ──────────────────────────────

    def bump_app(self, level: Optional[str] = None, set_to: Optional[str] = None):
        """调整 App 版本（更新 version.json）。"""
        data = read_json(self.version_json_path)
        current = data.get("version", "0.0.0")

        if set_to:
            new_ver = set_to
            if not APP_VERSION_RE.match(new_ver):
                raise ValueError(f"App 版本格式错误: {new_ver} (应为 X.Y.Z)")
        elif level:
            new_ver = bump_version(current, level)
        else:
            raise ValueError("必须指定 --set 或 bump 级别")

        data["version"] = new_ver
        return {
            "version.json": {"from": current, "to": new_ver},
            "_version_data": data,
        }

    # ── Bump File ─────────────────────────────

    def bump_file(self, ftype: str, set_to: str):
        """调整文件格式版本并同步 Model 类。"""
        if not FILE_VERSION_RE.match(set_to):
            raise ValueError(f"文件格式版本格式错误: {set_to} (应为 X.Y)")

        if ftype not in FILE_TYPE_TO_MODEL:
            valid = ", ".join(sorted(FILE_TYPE_TO_MODEL.keys()))
            raise ValueError(f"未知文件类型: {ftype}。有效值: {valid}")

        changes = {}

        # 1. 更新 file_versions.json
        file_vers = self.get_file_versions()
        current = file_vers.get(ftype, "?")
        file_vers[ftype] = set_to
        changes[str(self.file_versions_path.relative_to(self.root))] = {
            "from": current, "to": set_to,
        }
        changes["_file_versions_data"] = file_vers

        # 2. 更新 Model 类
        model_file = self.models_dir / FILE_TYPE_TO_MODEL[ftype]
        if model_file.exists():
            content = read_text(model_file)
            new_content = MODEL_VERSION_RE.sub(
                rf'\g<1>"{set_to}"', content
            )
            if new_content != content:
                rel = str(model_file.relative_to(self.root))
                changes[rel] = {"from": current, "to": set_to}
                changes["_model_content"] = {str(model_file): new_content}

        # 3. 更新 JsonStudentWriter（如果是 roster）
        if ftype == "roster" and self.json_student_writer_path.exists():
            writer_content = read_text(self.json_student_writer_path)
            new_writer = re.sub(
                r'(Version\s*=\s*)"([^"]+)"',
                rf'\g<1>"{set_to}"',
                writer_content,
            )
            if new_writer != writer_content:
                rel = str(self.json_student_writer_path.relative_to(self.root))
                writer_current = re.search(r'Version\s*=\s*"([^"]+)"', writer_content)
                changes[rel] = {
                    "from": writer_current.group(1) if writer_current else "?",
                    "to": set_to,
                }
                if "_model_content" not in changes:
                    changes["_model_content"] = {}
                changes["_model_content"][str(self.json_student_writer_path)] = new_writer

        return changes

    # ── Bump Strategy ─────────────────────────

    def bump_strategy(self, strategy_id: str, set_to: str):
        """调整策略版本。"""
        if not STRATEGY_VERSION_RE.match(set_to):
            raise ValueError(f"策略版本格式错误: {set_to} (应为 X.Y.Z)")

        changes = {}

        if strategy_id.upper() == "ALL":
            manifests = sorted(self.manifests_dir.glob("*.json"))
        else:
            mf = self.manifests_dir / f"{strategy_id}.json"
            if not mf.exists():
                raise ValueError(f"策略清单不存在: {mf.name}")
            manifests = [mf]

        for mf in manifests:
            data = read_json(mf)
            current = data.get("version", "?")
            data["version"] = set_to
            changes[str(mf.relative_to(self.root))] = {"from": current, "to": set_to}
            if "_manifest_data" not in changes:
                changes["_manifest_data"] = {}
            changes["_manifest_data"][str(mf)] = data

        return changes

    # ── Bump Onboarding ───────────────────────

    def bump_onboarding(self, set_to: str):
        """调整引导配置版本。"""
        if not ONBOARDING_VERSION_RE.match(set_to):
            raise ValueError(f"引导配置版本格式错误: {set_to} (应为 X.Y)")

        data = read_json(self.onboarding_path)
        current = data.get("version", "?")
        data["version"] = set_to
        return {
            str(self.onboarding_path.relative_to(self.root)): {"from": current, "to": set_to},
            "_onboarding_data": data,
        }

    # ── Sync ──────────────────────────────────

    def sync_models(self) -> dict[str, dict[str, str]]:
        """从 file_versions.json 同步所有 Model 类默认值。"""
        file_vers = self.get_file_versions()
        model_vers = self.get_model_versions()
        changes = {}

        for ftype, fname in FILE_TYPE_TO_MODEL.items():
            target_ver = file_vers.get(ftype)
            if not target_ver:
                continue
            current_ver = model_vers.get(ftype, "?")
            if target_ver == current_ver:
                continue

            path = self.models_dir / fname
            if not path.exists():
                continue

            content = read_text(path)
            new_content = MODEL_VERSION_RE.sub(
                rf'\g<1>"{target_ver}"', content
            )
            if new_content != content:
                rel = str(path.relative_to(self.root))
                changes[rel] = {"from": current_ver, "to": target_ver}
                if "_model_content" not in changes:
                    changes["_model_content"] = {}
                changes["_model_content"][str(path)] = new_content

        # 同时检查 JsonStudentWriter
        if self.json_student_writer_path.exists():
            roster_ver = file_vers.get("roster", "")
            writer_content = read_text(self.json_student_writer_path)
            writer_current = re.search(r'Version\s*=\s*"([^"]+)"', writer_content)
            writer_ver = writer_current.group(1) if writer_current else "?"
            if writer_ver != roster_ver:
                new_writer = re.sub(
                    r'(Version\s*=\s*)"([^"]+)"',
                    rf'\g<1>"{roster_ver}"',
                    writer_content,
                )
                rel = str(self.json_student_writer_path.relative_to(self.root))
                changes[rel] = {"from": writer_ver, "to": roster_ver}
                if "_model_content" not in changes:
                    changes["_model_content"] = {}
                changes["_model_content"][str(self.json_student_writer_path)] = new_writer

        return changes

    # ── 写入变更 ──────────────────────────────

    def apply_changes(self, changes: dict):
        """将变更字典写入磁盘。"""
        # 写入 version.json
        if "_version_data" in changes:
            write_json(self.version_json_path, changes.pop("_version_data"))

        # 写入 onboarding_config.json
        if "_onboarding_data" in changes:
            write_json(self.onboarding_path, changes.pop("_onboarding_data"))

        # 写入 file_versions.json
        if "_file_versions_data" in changes:
            write_json(self.file_versions_path, changes.pop("_file_versions_data"))

        # 写入 manifest 数据
        if "_manifest_data" in changes:
            manifest_data = changes.pop("_manifest_data")
            for path_str, data in manifest_data.items():
                write_json(Path(path_str), data)

        # 写入 Model 类
        if "_model_content" in changes:
            model_content = changes.pop("_model_content")
            for path_str, content in model_content.items():
                write_text(Path(path_str), content)

        # 其余变更仅为展示用
        for rel, info in changes.items():
            if isinstance(info, dict) and "from" in info and "to" in info:
                print(f"  {rel}: {info['from']} → {info['to']}")


# ──────────────────────────────────────────────
# 辅助函数
# ──────────────────────────────────────────────

def _compare_versions(a: str, b: str) -> int:
    """比较两个版本字符串。返回 -1/0/1。"""
    parts_a = [int(x) for x in a.split(".")]
    parts_b = [int(x) for x in b.split(".")]
    for va, vb in zip(parts_a, parts_b):
        if va < vb:
            return -1
        if va > vb:
            return 1
    return 0


# ──────────────────────────────────────────────
# CLI 入口
# ──────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(
        description="SeatFlow 版本号统一管理",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument("--root", default=None, help="项目根目录 (默认: 自动检测)")

    sub = parser.add_subparsers(dest="command", help="子命令")

    # show
    sub.add_parser("show", help="显示全部版本号概览")

    # check
    sub.add_parser("check", help="校验版本一致性")

    # bump-app
    p_app = sub.add_parser("bump-app", help="调整 App 版本")
    p_app.add_argument("level", nargs="?", choices=["major", "minor", "patch"],
                       help="递增级别")
    p_app.add_argument("--set", dest="set_to", help="直接设置版本 (X.Y.Z)")
    p_app.add_argument("--dry-run", action="store_true", help="预览不写入")
    p_app.add_argument("--force", "-f", action="store_true", help="跳过确认")

    # bump-file
    p_file = sub.add_parser("bump-file", help="调整文件格式版本（自动同步 Model 类）")
    p_file.add_argument("type", help="文件类型 (venue/roster/snapshot/...)")
    p_file.add_argument("--set", dest="set_to", required=True, help="目标版本 (X.Y)")
    p_file.add_argument("--dry-run", action="store_true", help="预览不写入")
    p_file.add_argument("--force", "-f", action="store_true", help="跳过确认")

    # bump-strategy
    p_strat = sub.add_parser("bump-strategy", help="调整策略清单版本")
    p_strat.add_argument("id", help="策略 ID (FixedSeat/DeskMate/... 或 ALL)")
    p_strat.add_argument("--set", dest="set_to", required=True, help="目标版本 (X.Y.Z)")
    p_strat.add_argument("--dry-run", action="store_true", help="预览不写入")
    p_strat.add_argument("--force", "-f", action="store_true", help="跳过确认")

    # bump-onboarding
    p_onb = sub.add_parser("bump-onboarding", help="调整引导配置版本")
    p_onb.add_argument("--set", dest="set_to", required=True, help="目标版本 (X.Y)")
    p_onb.add_argument("--dry-run", action="store_true", help="预览不写入")
    p_onb.add_argument("--force", "-f", action="store_true", help="跳过确认")

    # sync
    p_sync = sub.add_parser("sync", help="从 file_versions.json 同步所有 Model 类默认值")
    p_sync.add_argument("--dry-run", action="store_true", help="预览不写入")
    p_sync.add_argument("--force", "-f", action="store_true", help="跳过确认")

    args = parser.parse_args()

    if not args.command:
        parser.print_help()
        return 1

    root = resolve_root(args.root)
    mgr = VersionManager(root)

    try:
        if args.command == "show":
            mgr.show()
            return 0

        elif args.command == "check":
            issues = mgr.check()
            if not issues:
                print("✓ 全部版本号校验通过。")
                return 0

            errors = [i for i in issues if i["level"] == "ERROR"]
            warnings = [i for i in issues if i["level"] == "WARNING"]

            for w in warnings:
                print(f"⚠ {w['msg']}")
            for e in errors:
                print(f"✗ {e['msg']}")

            print(f"\n{len(errors)} 个错误, {len(warnings)} 个警告。")
            return 1 if errors else 0

        elif args.command == "bump-app":
            if not args.set_to and not args.level:
                print("错误: 必须指定 bump 级别 (major/minor/patch) 或 --set X.Y.Z")
                return 1
            changes = mgr.bump_app(level=args.level, set_to=args.set_to)
        elif args.command == "bump-file":
            changes = mgr.bump_file(args.type, set_to=args.set_to)
        elif args.command == "bump-strategy":
            changes = mgr.bump_strategy(args.id, set_to=args.set_to)
        elif args.command == "bump-onboarding":
            changes = mgr.bump_onboarding(set_to=args.set_to)
        elif args.command == "sync":
            changes = mgr.sync_models()
        else:
            return 1

        # 展示变更
        if not changes:
            print("无变更。")
            return 0

        print("\n将进行以下变更:")
        for rel, info in changes.items():
            if rel.startswith("_"):
                continue
            if isinstance(info, dict) and "from" in info and "to" in info:
                print(f"  {rel}: {info['from']} → {info['to']}")

        if getattr(args, "dry_run", False):
            print("\n[dry-run] 未实际写入。")
            return 0

        if not confirm("\n确认执行?", getattr(args, "force", False)):
            return 0

        mgr.backup()
        mgr.apply_changes(changes)
        print("✓ 已完成。")
        return 0

    except ValueError as e:
        print(f"错误: {e}")
        return 1
    except FileNotFoundError as e:
        print(f"错误: 文件未找到: {e}")
        return 1
    except json.JSONDecodeError as e:
        print(f"错误: JSON 解析失败: {e}")
        return 1


if __name__ == "__main__":
    sys.exit(main())
