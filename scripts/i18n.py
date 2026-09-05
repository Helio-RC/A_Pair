#!/usr/bin/env python3
"""
i18n — SeatFlow 本地化资源管理器。

管理 Resources.resx (zh-CN)、Resources.en-US.resx 和 Resources.Designer.cs
三文件的同步增删改查。

用法:
  python3 i18n.py list [--category CAT] [--missing-en] [--format-strings] [--pattern RE]
  python3 i18n.py check [--fix]
  python3 i18n.py add KEY --zh VAL --en VAL [--comment TEXT] [--dry-run] [--force]
  python3 i18n.py modify KEY [--zh VAL] [--en VAL] [--comment TEXT] [--clear-comment] [--dry-run] [--force]
  python3 i18n.py rename OLD_KEY NEW_KEY [--dry-run] [--force]
  python3 i18n.py delete KEY [--dry-run] [--force]
  python3 i18n.py sync [--dry-run] [--force]
  python3 i18n.py export [--format csv|json] [--output FILE]
  python3 i18n.py import FILE [--format csv|json] [--dry-run] [--force]
"""

import argparse
import csv
import io
import json
import os
import re
import shutil
import sys
import xml.etree.ElementTree as ET
from datetime import datetime
from pathlib import Path
from typing import Optional

# ──────────────────────────────────────────────
# 常量
# ──────────────────────────────────────────────

KNOWN_CATEGORIES = [
    "About", "App", "Common", "ConfigBlock", "Data",
    "Freeform", "Gender", "Guide", "Home", "Lang",
    "Member", "Nav", "Seating", "Settings",
    "Snapshot", "Startup", "Strategy", "Theme", "Venue",
    "Watchdog", "Zoom",
]

KEY_NAME_RE = re.compile(r"^[A-Z][a-zA-Z0-9]*(_[a-zA-Z0-9]+)+$")
FORMAT_ARG_RE = re.compile(r"\{(\d+)(?::[^}]*)?\}")

DESIGNER_PROPERTY_RE = re.compile(
    r'^\s*public static string (\w+) => ResourceManager\.GetString\("(\w+)"'
)

# ──────────────────────────────────────────────
# 工具函数
# ──────────────────────────────────────────────

def xml_escape(text: str) -> str:
    """转义 XML 特殊字符。"""
    text = text.replace("&", "&amp;")
    text = text.replace("<", "&lt;")
    text = text.replace(">", "&gt;")
    text = text.replace('"', "&quot;")
    text = text.replace("'", "&apos;")
    return text


def extract_format_args(value: str) -> set[int]:
    """提取格式字符串中的参数索引集合。"""
    return {int(m.group(1)) for m in FORMAT_ARG_RE.finditer(value)}


def validate_key_name(key: str) -> list[str]:
    """校验 key 命名规范，返回问题列表（空列表 = 通过）。"""
    issues = []
    if not KEY_NAME_RE.match(key):
        issues.append(f"Key '{key}' 不符合命名规范 (Category_MeaningfulName)")
    if "__" in key:
        issues.append(f"Key '{key}' 包含连续下划线")
    if len(key) > 120:
        issues.append(f"Key '{key}' 超过 120 字符限制")
    if "_" in key:
        prefix = key.split("_")[0]
        if prefix not in KNOWN_CATEGORIES:
            issues.append(
                f"Key '{key}' 的前缀 '{prefix}' 不在已知分类中: {', '.join(KNOWN_CATEGORIES)}"
            )
    return issues


def resolve_lang_dir(lang_dir: Optional[str] = None) -> Path:
    """解析 Lang 目录路径。"""
    if lang_dir:
        p = Path(lang_dir)
    else:
        p = Path(__file__).resolve().parent.parent / "src" / "SeatFlow.Presentation.Avalonia" / "Lang"
    return p


def confirm(msg: str, force: bool = False) -> bool:
    """请求用户确认。force=True 时跳过。"""
    if force:
        return True
    try:
        resp = input(f"{msg} (y/N): ").strip().lower()
        return resp in ("y", "yes")
    except (EOFError, KeyboardInterrupt):
        print("\n已取消。")
        return False


# ──────────────────────────────────────────────
# ResxFile — .resx 文件读写
# ──────────────────────────────────────────────

class ResxFile:
    """表示一个 .resx 资源文件。"""

    def __init__(self, path: Path):
        self.path = path
        self.preamble = ""          # 第一个 <data> 之前的所有内容
        self.entries: list[dict] = []  # {name, value, comment}
        self.name_index: dict[str, int] = {}  # name -> entries index

    def load(self):
        """从文件加载。"""
        with open(self.path, "r", encoding="utf-8") as f:
            content = f.read()

        # 去除 XML 注释 (<!-- ... -->)，避免注释中的示例 <data> 被误匹配
        content_no_comments = re.sub(r"<!--.*?-->", "", content, flags=re.DOTALL)

        # 分离 preamble（第一个真实 <data 之前的部分）和 data 元素
        data_match = re.search(r"(\s*)<data\s", content_no_comments)
        if data_match:
            data_start = data_match.start()
            self.preamble = content_no_comments[:data_start]
            # 提取 body: 从第一个 <data 到 </root>
            body = content_no_comments[data_start:]
            # 去掉 </root> 闭合标签，只保留 data 元素
            body = re.sub(r"\s*</root>\s*$", "", body)
        else:
            self.preamble = content_no_comments
            body = ""

        # 用 ElementTree 解析 data 元素
        # 包装在合成根元素中
        if body.strip():
            wrapped = f"<root>{body}</root>"
            try:
                root = ET.fromstring(wrapped)
            except ET.ParseError as e:
                raise ValueError(f"XML 解析失败 {self.path}: {e}")

            for elem in root.findall("data"):
                name = elem.get("name", "")
                value_elem = elem.find("value")
                comment_elem = elem.find("comment")
                value = value_elem.text if value_elem is not None and value_elem.text else ""
                comment = comment_elem.text if comment_elem is not None and comment_elem.text else None
                entry = {"name": name, "value": value, "comment": comment}
                self.entries.append(entry)
                if name in self.name_index:
                    raise ValueError(f"{self.path}: 重复的 key '{name}'")
                self.name_index[name] = len(self.entries) - 1
        else:
            self.entries = []
            self.name_index = {}

    def save(self, path: Optional[Path] = None):
        """写入文件。"""
        target = path or self.path
        lines = [self.preamble.rstrip("\n")]
        for entry in self.entries:
            lines.append(self._format_entry(entry))
        lines.append("</root>\n")
        with open(target, "w", encoding="utf-8", newline="") as f:
            f.write("\n".join(lines))

    def _format_entry(self, entry: dict) -> str:
        """将一条 entry 格式化为 XML 字符串。"""
        name = entry["name"]
        value = xml_escape(entry["value"])
        if entry.get("comment"):
            comment = xml_escape(entry["comment"])
            return (
                f'  <data name="{name}" xml:space="preserve">\n'
                f"    <value>{value}</value>\n"
                f"    <comment>{comment}</comment>\n"
                f"  </data>"
            )
        else:
            return (
                f'  <data name="{name}" xml:space="preserve">\n'
                f"    <value>{value}</value>\n"
                f"  </data>"
            )

    def get(self, name: str) -> Optional[dict]:
        """获取指定 key 的 entry。"""
        idx = self.name_index.get(name)
        return self.entries[idx] if idx is not None else None

    def add(self, name: str, value: str, comment: Optional[str] = None):
        """按字母序插入新 entry。"""
        entry = {"name": name, "value": value, "comment": comment}
        # 找到插入位置
        insert_at = 0
        for i, e in enumerate(self.entries):
            if e["name"] > name:
                insert_at = i
                break
        else:
            insert_at = len(self.entries)
        self.entries.insert(insert_at, entry)
        # 重建索引
        self._rebuild_index()

    def modify(self, name: str, value: Optional[str] = None,
               comment: Optional[str] = None, clear_comment: bool = False):
        """修改已有 entry。"""
        idx = self.name_index[name]
        if value is not None:
            self.entries[idx]["value"] = value
        if clear_comment:
            self.entries[idx]["comment"] = None
        elif comment is not None:
            self.entries[idx]["comment"] = comment

    def rename(self, old_name: str, new_name: str):
        """重命名 entry。"""
        idx = self.name_index.pop(old_name)
        self.entries[idx]["name"] = new_name
        # 按字母序重排
        entry = self.entries.pop(idx)
        self.add(entry["name"], entry["value"], entry["comment"])

    def delete(self, name: str):
        """删除 entry。"""
        idx = self.name_index.pop(name)
        self.entries.pop(idx)
        self._rebuild_index()

    def _rebuild_index(self):
        """重建 name -> index 映射。"""
        self.name_index = {e["name"]: i for i, e in enumerate(self.entries)}

    @property
    def keys(self) -> list[str]:
        return [e["name"] for e in self.entries]


# ──────────────────────────────────────────────
# DesignerFile — Resources.Designer.cs 读写
# ──────────────────────────────────────────────

DESIGNER_HEADER = """#nullable enable
// <auto-generated />
namespace SeatFlow.Presentation.Avalonia.Lang;

using System;

[global::System.CodeDom.Compiler.GeneratedCode("System.Resources.Tools.StronglyTypedResourceBuilder", "17.0.0.0")]
[global::System.Diagnostics.DebuggerNonUserCode]
[global::System.Runtime.CompilerServices.CompilerGenerated]
public class Resources
{
    private static global::System.Resources.ResourceManager? _resourceMan;

    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Advanced)]
    public static global::System.Resources.ResourceManager ResourceManager
    {
        get
        {
            if (ReferenceEquals(_resourceMan, null))
                _resourceMan = new global::System.Resources.ResourceManager(
                    "SeatFlow.Presentation.Avalonia.Lang.Resources", typeof(Resources).Assembly);
            return _resourceMan;
        }
    }

    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Advanced)]
    public static global::System.Globalization.CultureInfo Culture { get; set; } = global::System.Globalization.CultureInfo.CurrentUICulture;

"""


class DesignerFile:
    """表示 Resources.Designer.cs 文件。"""

    def __init__(self, path: Path):
        self.path = path
        self.properties: dict[str, str] = {}  # key -> getString key (usually same)
        self.interstitial: dict[str, list[str]] = {}  # key -> lines preceding it
        self.header_lines: list[str] = []
        self.trailing_lines: list[str] = []

    def load(self):
        """解析现有 Designer.cs。"""
        if not self.path.exists():
            return

        with open(self.path, "r", encoding="utf-8") as f:
            lines = f.readlines()

        state = "header"  # header | properties | trailing
        pending: list[str] = []

        for line in lines:
            m = DESIGNER_PROPERTY_RE.match(line)
            if state == "header":
                if m:
                    state = "properties"
                    key = m.group(1)
                    self.properties[key] = m.group(2)
                    if pending:
                        self.interstitial[key] = pending
                        pending = []
                else:
                    self.header_lines.append(line)
            elif state == "properties":
                if m:
                    key = m.group(1)
                    self.properties[key] = m.group(2)
                    if pending:
                        self.interstitial[key] = pending
                        pending = []
                elif line.rstrip() == "}":
                    state = "trailing"
                    self.trailing_lines.append(line)
                else:
                    pending.append(line)
            elif state == "trailing":
                self.trailing_lines.append(line)

    def generate(self, keys_in_order: list[str]) -> str:
        """根据 key 列表生成 Designer.cs 内容。"""
        result = io.StringIO()

        if self.header_lines:
            result.write("".join(self.header_lines))
        else:
            result.write(DESIGNER_HEADER)

        for key in keys_in_order:
            # 输出该 key 之前的注释/空行
            if key in self.interstitial:
                for il in self.interstitial[key]:
                    result.write(il)
            result.write(
                f'    public static string {key} => ResourceManager.GetString("{key}", Culture)!;\n'
            )

        # 剩余未关联的 interstitial（尾部的注释/空行，如果存在）
        trailing_inter = [k for k in self.interstitial if k not in keys_in_order]
        # 这些是仅存在于旧文件但不在新 key 列表中的 key 的前导行 — 跳过它们
        # 但如果 key 列表为空（不应该），保留空白 interstitial

        if self.trailing_lines:
            result.write("".join(self.trailing_lines))
        else:
            result.write("}\n")

        return result.getvalue()

    def save(self, content: str, path: Optional[Path] = None):
        """写入生成的内容。"""
        target = path or self.path
        with open(target, "w", encoding="utf-8", newline="") as f:
            f.write(content)


# ──────────────────────────────────────────────
# I18nManager — 核心管理器
# ──────────────────────────────────────────────

class I18nManager:
    """管理三文件同步的操作集合。"""

    def __init__(self, lang_dir: Path):
        self.lang_dir = lang_dir
        self.resx_zh_path = lang_dir / "Resources.resx"
        self.resx_en_path = lang_dir / "Resources.en-US.resx"
        self.designer_path = lang_dir / "Resources.Designer.cs"
        self.backup_dir = lang_dir / ".backup"

        self.resx_zh: Optional[ResxFile] = None
        self.resx_en: Optional[ResxFile] = None
        self.designer: Optional[DesignerFile] = None

    def load_all(self):
        """加载全部三个文件。"""
        self.resx_zh = ResxFile(self.resx_zh_path)
        self.resx_en = ResxFile(self.resx_en_path)
        self.designer = DesignerFile(self.designer_path)
        self.resx_zh.load()
        self.resx_en.load()
        self.designer.load()

    def backup(self):
        """备份全部三个文件到 .backup/ 目录。"""
        self.backup_dir.mkdir(parents=True, exist_ok=True)
        ts = datetime.now().strftime("%Y%m%d_%H%M%S")
        for src in [self.resx_zh_path, self.resx_en_path, self.designer_path]:
            if src.exists():
                dst = self.backup_dir / f"{src.name}.{ts}"
                shutil.copy2(src, dst)
        print(f"已备份到 {self.backup_dir}/ (时间戳: {ts})")

    def save_all(self):
        """保存全部三个文件。"""
        self.resx_zh.save()
        self.resx_en.save()
        # 生成并保存 Designer.cs
        keys_in_order = self.resx_zh.keys
        content = self.designer.generate(keys_in_order)
        self.designer.save(content)

    def check(self) -> list[dict]:
        """运行一致性校验，返回问题列表。"""
        issues = []

        zh_keys = set(self.resx_zh.keys)
        en_keys = set(self.resx_en.keys)
        designer_keys = set(self.designer.properties.keys())

        # Key 集一致性
        only_zh = zh_keys - en_keys
        for k in sorted(only_zh):
            issues.append({"level": "ERROR", "msg": f"Key '{k}' 存在于 zh-CN 但不在 en-US"})

        only_en = en_keys - zh_keys
        for k in sorted(only_en):
            issues.append({"level": "ERROR", "msg": f"Key '{k}' 存在于 en-US 但不在 zh-CN"})

        in_resx_not_designer = zh_keys - designer_keys
        for k in sorted(in_resx_not_designer):
            issues.append({"level": "ERROR", "msg": f"Key '{k}' 存在于 .resx 但不在 Designer.cs — 运行 'sync' 修复"})

        in_designer_not_resx = designer_keys - zh_keys
        for k in sorted(in_designer_not_resx):
            issues.append({"level": "WARNING", "msg": f"Key '{k}' 存在于 Designer.cs 但不在 .resx"})

        # 命名规范
        for k in sorted(zh_keys):
            for issue in validate_key_name(k):
                issues.append({"level": "WARNING", "msg": issue})

        # 格式字符串参数数量匹配
        for k in sorted(zh_keys & en_keys):
            zh_val = self.resx_zh.get(k)["value"]
            en_val = self.resx_en.get(k)["value"]
            zh_args = extract_format_args(zh_val)
            en_args = extract_format_args(en_val)
            if len(zh_args) != len(en_args):
                issues.append({
                    "level": "ERROR",
                    "msg": (
                        f"Key '{k}' 格式字符串参数数量不匹配: "
                        f"zh-CN 有 {len(zh_args)} 个, en-US 有 {len(en_args)} 个"
                    ),
                })

        # 空值
        for k in sorted(zh_keys):
            zh_val = self.resx_zh.get(k)["value"]
            if not zh_val.strip():
                issues.append({"level": "WARNING", "msg": f"Key '{k}' zh-CN 值为空"})

        for k in sorted(en_keys):
            en_val = self.resx_en.get(k)["value"]
            if not en_val.strip():
                issues.append({"level": "WARNING", "msg": f"Key '{k}' en-US 值为空"})

        return issues

    def fix_sort_order(self):
        """修复 .resx 文件中的排序（已在 add 时保证，此处做全量重排）。"""
        self.resx_zh.entries.sort(key=lambda e: e["name"])
        self.resx_zh._rebuild_index()
        self.resx_en.entries.sort(key=lambda e: e["name"])
        self.resx_en._rebuild_index()
        print("已按字母序重排 .resx 条目。")


# ──────────────────────────────────────────────
# 子命令实现
# ──────────────────────────────────────────────

def cmd_list(mgr: I18nManager, args):
    """列出资源 key。"""
    mgr.load_all()

    # 构建过滤条件
    keys = mgr.resx_zh.keys

    if args.category:
        prefix = f"{args.category}_"
        keys = [k for k in keys if k.startswith(prefix)]

    if args.pattern:
        pat = re.compile(args.pattern)
        keys = [k for k in keys if pat.search(k)]

    if args.missing_en:
        keys = [
            k for k in keys
            if mgr.resx_zh.get(k)["value"] == mgr.resx_en.get(k)["value"]
        ]

    if args.format_strings:
        keys = [
            k for k in keys
            if FORMAT_ARG_RE.search(mgr.resx_zh.get(k)["value"])
        ]

    if not keys:
        print("(无匹配结果)")
        return

    # 输出
    if args.output == "json":
        result = {}
        for k in keys:
            result[k] = {
                "zh-CN": mgr.resx_zh.get(k)["value"],
                "en-US": mgr.resx_en.get(k)["value"],
            }
            if mgr.resx_zh.get(k)["comment"]:
                result[k]["comment"] = mgr.resx_zh.get(k)["comment"]
        print(json.dumps(result, ensure_ascii=False, indent=2))
    elif args.output == "csv":
        writer = csv.writer(sys.stdout)
        writer.writerow(["Key", "zh-CN", "en-US", "Comment"])
        for k in keys:
            writer.writerow([
                k,
                mgr.resx_zh.get(k)["value"],
                mgr.resx_en.get(k)["value"],
                mgr.resx_zh.get(k)["comment"] or "",
            ])
    else:
        # 表格输出
        max_key_len = max(len(k) for k in keys) if keys else 10
        fmt = f"{{:<{max_key_len + 2}}} | {{}}"
        print(f"{'Key'.ljust(max_key_len + 2)} | zh-CN → en-US")
        print("-" * (max_key_len + 2) + "-+-" + "-" * 60)
        for k in keys:
            zh_val = mgr.resx_zh.get(k)["value"]
            en_val = mgr.resx_en.get(k)["value"]
            has_fmt = " [fmt]" if FORMAT_ARG_RE.search(zh_val) else ""
            has_comment = " [注释]" if mgr.resx_zh.get(k)["comment"] else ""
            print(f"{k:<{max_key_len + 2}} | {zh_val}{has_fmt}{has_comment}")
            if zh_val != en_val:
                print(f"{'':<{max_key_len + 2}} | → {en_val}")


def cmd_check(mgr: I18nManager, args):
    """运行一致性校验。"""
    mgr.load_all()
    issues = mgr.check()

    if args.fix:
        mgr.fix_sort_order()
        mgr.save_all()
        print("已修复排序问题。")

    if not issues:
        print("✓ 全部校验通过。")
        return 0

    errors = [i for i in issues if i["level"] == "ERROR"]
    warnings = [i for i in issues if i["level"] == "WARNING"]

    for w in warnings:
        print(f"⚠ {w['msg']}")
    for e in errors:
        print(f"✗ {e['msg']}")

    print(f"\n{len(errors)} 个错误, {len(warnings)} 个警告。")
    return 1 if errors else 0


def cmd_add(mgr: I18nManager, args):
    """添加新 key。"""
    mgr.load_all()

    key = args.key

    # 校验
    if key in mgr.resx_zh.name_index:
        print(f"错误: Key '{key}' 已存在。")
        return 1

    name_issues = validate_key_name(key)
    if name_issues:
        for issue in name_issues:
            print(f"警告: {issue}")

    zh_val = args.zh
    en_val = args.en

    if not zh_val or not en_val:
        print("错误: --zh 和 --en 都必须提供。")
        return 1

    # 格式字符串检查
    zh_args = extract_format_args(zh_val)
    en_args = extract_format_args(en_val)
    if len(zh_args) != len(en_args):
        print(f"警告: 格式字符串参数数量不匹配 (zh-CN: {len(zh_args)}, en-US: {len(en_args)})")

    # 预览
    print(f"\n将添加 Key: {key}")
    print(f"  zh-CN: {zh_val}")
    print(f"  en-US: {en_val}")
    if args.comment:
        print(f"  注释: {args.comment}")

    if args.dry_run:
        print("\n[dry-run] 未实际写入。")
        return 0

    if not confirm("\n确认添加?", args.force):
        return 0

    mgr.backup()
    mgr.resx_zh.add(key, zh_val, args.comment)
    mgr.resx_en.add(key, en_val, args.comment)
    mgr.save_all()
    print(f"✓ 已添加 Key '{key}'。")
    return 0


def cmd_modify(mgr: I18nManager, args):
    """修改已有 key。"""
    mgr.load_all()

    key = args.key

    if key not in mgr.resx_zh.name_index:
        print(f"错误: Key '{key}' 不存在。")
        return 1

    zh_entry = mgr.resx_zh.get(key)
    en_entry = mgr.resx_en.get(key)

    new_zh = args.zh if args.zh is not None else zh_entry["value"]
    new_en = args.en if args.en is not None else en_entry["value"]

    # 格式字符串检查
    zh_args = extract_format_args(new_zh)
    en_args = extract_format_args(new_en)
    if len(zh_args) != len(en_args):
        print(f"警告: 格式字符串参数数量不匹配 (zh-CN: {len(zh_args)}, en-US: {len(en_args)})")

    # 预览变更
    changes = []
    if args.zh is not None and args.zh != zh_entry["value"]:
        changes.append(f"  zh-CN: \"{zh_entry['value']}\" → \"{args.zh}\"")
    if args.en is not None and args.en != en_entry["value"]:
        changes.append(f"  en-US: \"{en_entry['value']}\" → \"{args.en}\"")
    if args.clear_comment and zh_entry.get("comment"):
        changes.append(f"  注释: \"{zh_entry['comment']}\" → (已移除)")
    elif args.comment is not None and args.comment != zh_entry.get("comment"):
        changes.append(f"  注释: \"{zh_entry.get('comment', '')}\" → \"{args.comment}\"")

    if not changes:
        print("无变更。")
        return 0

    print(f"\nKey: {key}")
    for c in changes:
        print(c)

    if args.dry_run:
        print("\n[dry-run] 未实际写入。")
        return 0

    if not confirm("\n确认修改?", args.force):
        return 0

    mgr.backup()
    mgr.resx_zh.modify(
        key,
        value=args.zh,
        comment=args.comment,
        clear_comment=args.clear_comment,
    )
    mgr.resx_en.modify(
        key,
        value=args.en,
        comment=args.comment,
        clear_comment=args.clear_comment,
    )
    mgr.save_all()
    print(f"✓ 已修改 Key '{key}'。")
    return 0


def cmd_rename(mgr: I18nManager, args):
    """重命名 key。"""
    mgr.load_all()

    old_key = args.old_key
    new_key = args.new_key

    if old_key not in mgr.resx_zh.name_index:
        print(f"错误: Key '{old_key}' 不存在。")
        return 1

    if new_key in mgr.resx_zh.name_index:
        print(f"错误: 目标 Key '{new_key}' 已存在。")
        return 1

    name_issues = validate_key_name(new_key)
    if name_issues:
        for issue in name_issues:
            print(f"警告: {issue}")

    zh_val = mgr.resx_zh.get(old_key)["value"]
    en_val = mgr.resx_en.get(old_key)["value"]

    print(f"\n将重命名: {old_key} → {new_key}")
    print(f"  zh-CN: {zh_val}")
    print(f"  en-US: {en_val}")

    if args.dry_run:
        print("\n[dry-run] 未实际写入。")
        return 0

    if not confirm("\n确认重命名?", args.force):
        return 0

    mgr.backup()
    mgr.resx_zh.rename(old_key, new_key)
    mgr.resx_en.rename(old_key, new_key)
    mgr.save_all()
    print(f"✓ 已重命名 '{old_key}' → '{new_key}'。")
    return 0


def cmd_delete(mgr: I18nManager, args):
    """删除 key。"""
    mgr.load_all()

    key = args.key

    if key not in mgr.resx_zh.name_index:
        print(f"错误: Key '{key}' 不存在。")
        return 1

    zh_val = mgr.resx_zh.get(key)["value"]
    en_val = mgr.resx_en.get(key)["value"]

    print(f"\n将删除 Key: {key}")
    print(f"  zh-CN: {zh_val}")
    print(f"  en-US: {en_val}")

    if args.dry_run:
        print("\n[dry-run] 未实际写入。")
        return 0

    if not confirm("\n确认删除?", args.force):
        return 0

    mgr.backup()
    mgr.resx_zh.delete(key)
    mgr.resx_en.delete(key)
    mgr.save_all()
    print(f"✓ 已删除 Key '{key}'。")
    return 0


def cmd_sync(mgr: I18nManager, args):
    """从 .resx 重新生成 Designer.cs。"""
    mgr.load_all()

    zh_keys = mgr.resx_zh.keys

    print(f"将从 .resx 生成 Designer.cs ({len(zh_keys)} 个属性)。")

    if args.dry_run:
        new_content = mgr.designer.generate(zh_keys)
        with open(mgr.designer_path, "r", encoding="utf-8") as f:
            old_content = f.read()
        if new_content == old_content:
            print("[dry-run] Designer.cs 已是最新，无需更新。")
        else:
            # 简单 diff 统计
            old_lines = set(old_content.splitlines())
            new_lines = set(new_content.splitlines())
            added = new_lines - old_lines
            removed = old_lines - new_lines
            print(f"[dry-run] 将添加 {len(added)} 行, 删除 {len(removed)} 行。")
        return 0

    if not confirm(f"\n确认重新生成 Designer.cs?", args.force):
        return 0

    mgr.backup()
    new_content = mgr.designer.generate(zh_keys)
    mgr.designer.save(new_content)
    print(f"✓ 已重新生成 Designer.cs ({len(zh_keys)} 个属性)。")
    return 0


def cmd_export(mgr: I18nManager, args):
    """导出为 CSV 或 JSON。"""
    mgr.load_all()

    keys = mgr.resx_zh.keys
    fmt = args.format or "csv"
    output = args.output

    if fmt == "json":
        result = {}
        for k in keys:
            result[k] = {
                "zh-CN": mgr.resx_zh.get(k)["value"],
                "en-US": mgr.resx_en.get(k)["value"],
            }
            if mgr.resx_zh.get(k)["comment"]:
                result[k]["comment"] = mgr.resx_zh.get(k)["comment"]
        content = json.dumps(result, ensure_ascii=False, indent=2)
    else:
        buf = io.StringIO()
        writer = csv.writer(buf)
        writer.writerow(["Key", "zh-CN", "en-US", "Comment"])
        for k in keys:
            writer.writerow([
                k,
                mgr.resx_zh.get(k)["value"],
                mgr.resx_en.get(k)["value"],
                mgr.resx_zh.get(k)["comment"] or "",
            ])
        content = buf.getvalue()

    if output:
        with open(output, "w", encoding="utf-8", newline="") as f:
            f.write(content)
        print(f"✓ 已导出到 {output} ({len(keys)} 条)")
    else:
        print(content)


def cmd_import(mgr: I18nManager, args):
    """从 CSV 或 JSON 导入翻译。"""
    mgr.load_all()

    filepath = args.file
    fmt = args.format

    # 自动检测格式
    if fmt is None:
        if filepath.endswith(".json"):
            fmt = "json"
        else:
            fmt = "csv"

    # 解析导入文件
    if fmt == "json":
        with open(filepath, "r", encoding="utf-8") as f:
            data = json.load(f)
        updates = []
        for key, vals in data.items():
            updates.append({
                "key": key,
                "zh": vals.get("zh-CN", vals.get("zh", "")),
                "en": vals.get("en-US", vals.get("en", "")),
                "comment": vals.get("comment"),
            })
    else:
        with open(filepath, "r", encoding="utf-8", newline="") as f:
            reader = csv.DictReader(f)
            updates = [
                {
                    "key": row["Key"],
                    "zh": row.get("zh-CN", row.get("zh", "")),
                    "en": row.get("en-US", row.get("en", "")),
                    "comment": row.get("Comment", row.get("comment", "")).strip() or None,
                }
                for row in reader
            ]

    # 验证
    errors = []
    for u in updates:
        if u["key"] not in mgr.resx_zh.name_index:
            errors.append(f"Key '{u['key']}' 不存在，将被跳过")

    if errors:
        for e in errors:
            print(f"警告: {e}")

    # 仅处理存在的 key
    valid_updates = [u for u in updates if u["key"] in mgr.resx_zh.name_index]

    if not valid_updates:
        print("没有有效的更新。")
        return 1

    print(f"\n将更新 {len(valid_updates)} 个 Key:")
    for u in valid_updates[:5]:
        print(f"  {u['key']}: zh-CN=\"{u['zh'][:40]}...\"" if len(u['zh']) > 40
              else f"  {u['key']}: zh-CN=\"{u['zh']}\"")
    if len(valid_updates) > 5:
        print(f"  ... 及其他 {len(valid_updates) - 5} 个")

    if args.dry_run:
        print("\n[dry-run] 未实际写入。")
        return 0

    if not confirm(f"\n确认导入?", args.force):
        return 0

    mgr.backup()
    for u in valid_updates:
        mgr.resx_zh.modify(u["key"], value=u["zh"], comment=u["comment"])
        mgr.resx_en.modify(u["key"], value=u["en"], comment=u["comment"])
    mgr.save_all()
    print(f"✓ 已导入 {len(valid_updates)} 条。")
    return 0


# ──────────────────────────────────────────────
# CLI 入口
# ──────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(
        description="SeatFlow i18n 资源管理器 — 管理 Resources.resx / Resources.en-US.resx / Resources.Designer.cs 三文件同步",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument(
        "--lang-dir",
        default=None,
        help="Lang 目录路径 (默认: src/SeatFlow.Presentation.Avalonia/Lang/)",
    )

    sub = parser.add_subparsers(dest="command", help="子命令")

    # list
    p_list = sub.add_parser("list", help="列出资源 key")
    p_list.add_argument("--category", choices=KNOWN_CATEGORIES, help="按分类过滤")
    p_list.add_argument("--missing-en", action="store_true", help="显示 zh-CN 和 en-US 值相同的 key（可能未翻译）")
    p_list.add_argument("--format-strings", action="store_true", help="仅显示含格式占位符的 key")
    p_list.add_argument("--pattern", help="正则过滤 key 名称")
    p_list.add_argument("--output", choices=["table", "csv", "json"], default="table", help="输出格式")

    # check
    p_check = sub.add_parser("check", help="校验三文件一致性")
    p_check.add_argument("--fix", action="store_true", help="自动修复排序")

    # add
    p_add = sub.add_parser("add", help="添加新资源 key")
    p_add.add_argument("key", help="Key 名称 (Category_MeaningfulName)")
    p_add.add_argument("--zh", required=True, help="zh-CN 值")
    p_add.add_argument("--en", required=True, help="en-US 值")
    p_add.add_argument("--comment", help="可选的注释")
    p_add.add_argument("--dry-run", action="store_true", help="预览变更不写入")
    p_add.add_argument("--force", "-f", action="store_true", help="跳过确认提示")

    # modify
    p_mod = sub.add_parser("modify", help="修改已有资源 key")
    p_mod.add_argument("key", help="Key 名称")
    p_mod.add_argument("--zh", help="新的 zh-CN 值")
    p_mod.add_argument("--en", help="新的 en-US 值")
    p_mod.add_argument("--comment", help="新的注释")
    p_mod.add_argument("--clear-comment", action="store_true", help="移除注释")
    p_mod.add_argument("--dry-run", action="store_true", help="预览变更不写入")
    p_mod.add_argument("--force", "-f", action="store_true", help="跳过确认提示")

    # rename
    p_rename = sub.add_parser("rename", help="重命名 key")
    p_rename.add_argument("old_key", help="旧 Key 名称")
    p_rename.add_argument("new_key", help="新 Key 名称")
    p_rename.add_argument("--dry-run", action="store_true", help="预览变更不写入")
    p_rename.add_argument("--force", "-f", action="store_true", help="跳过确认提示")

    # delete
    p_delete = sub.add_parser("delete", help="删除 key")
    p_delete.add_argument("key", help="Key 名称")
    p_delete.add_argument("--dry-run", action="store_true", help="预览变更不写入")
    p_delete.add_argument("--force", "-f", action="store_true", help="跳过确认提示")

    # sync
    p_sync = sub.add_parser("sync", help="从 .resx 重新生成 Designer.cs")
    p_sync.add_argument("--dry-run", action="store_true", help="预览变更不写入")
    p_sync.add_argument("--force", "-f", action="store_true", help="跳过确认提示")

    # export
    p_export = sub.add_parser("export", help="导出为 CSV/JSON")
    p_export.add_argument("--format", choices=["csv", "json"], default="csv", help="输出格式")
    p_export.add_argument("--output", "-o", help="输出文件路径 (默认: stdout)")

    # import
    p_import = sub.add_parser("import", help="从 CSV/JSON 导入翻译")
    p_import.add_argument("file", help="导入文件路径")
    p_import.add_argument("--format", choices=["csv", "json"], help="文件格式 (默认: 根据扩展名自动检测)")
    p_import.add_argument("--dry-run", action="store_true", help="预览变更不写入")
    p_import.add_argument("--force", "-f", action="store_true", help="跳过确认提示")

    args = parser.parse_args()

    if not args.command:
        parser.print_help()
        return 1

    lang_dir = resolve_lang_dir(args.lang_dir)
    if not lang_dir.exists():
        print(f"错误: Lang 目录不存在: {lang_dir}")
        print("请使用 --lang-dir 指定正确的路径。")
        return 1

    mgr = I18nManager(lang_dir)

    commands = {
        "list": cmd_list,
        "check": cmd_check,
        "add": cmd_add,
        "modify": cmd_modify,
        "rename": cmd_rename,
        "delete": cmd_delete,
        "sync": cmd_sync,
        "export": cmd_export,
        "import": cmd_import,
    }

    handler = commands.get(args.command)
    if handler:
        try:
            return handler(mgr, args) or 0
        except ValueError as e:
            print(f"错误: {e}")
            return 1
        except FileNotFoundError as e:
            print(f"错误: 文件未找到: {e}")
            return 1

    return 1


if __name__ == "__main__":
    sys.exit(main())
