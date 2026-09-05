# 工具集 (Tools Collection)

SeatFlow 项目的 Python 脚本工具集。

## 前置条件

- Python 3.10+
- （可选）.NET 10 SDK — 用于 `dotnet build` 编译验证

---

# i18n — 本地化资源管理

管理三文件同步本地化资源：

- `src/SeatFlow.Presentation.Avalonia/Lang/Resources.resx` — zh-CN（中性语言）
- `src/SeatFlow.Presentation.Avalonia/Lang/Resources.en-US.resx` — English
- `src/SeatFlow.Presentation.Avalonia/Lang/Resources.Designer.cs` — 强类型访问器

## 快速开始

```bash
cd scripts

# 查看帮助
python3 i18n.py --help

# 查看所有子命令
python3 i18n.py list --help
```

## 子命令

### `list` — 列出资源 key

```bash
# 列出所有 key
python3 i18n.py list

# 按分类过滤
python3 i18n.py list --category Settings

# 显示可能未翻译的项（zh-CN == en-US）
python3 i18n.py list --missing-en

# 显示含格式占位符的 key
python3 i18n.py list --format-strings

# 正则搜索
python3 i18n.py list --pattern "Export"

# 导出为 JSON
python3 i18n.py list --output json

# 导出为 CSV
python3 i18n.py list --output csv > translations.csv
```

### `check` — 一致性校验

```bash
# 校验三文件一致性
python3 i18n.py check

# 自动修复排序问题
python3 i18n.py check --fix
```

校验项：
- 三文件 key 集合一致性
- key 命名规范（`Category_MeaningfulName` PascalCase）
- zh-CN 和 en-US 格式字符串参数数量匹配
- 空值检测
- XML 重复 key 检测

### `add` — 添加新 key

```bash
# 基本用法
python3 i18n.py add Settings_NewOption --zh "新选项" --en "New Option"

# 带注释
python3 i18n.py add Common_NewAction --zh "执行操作" --en "Execute" --comment "工具栏按钮"

# 预览变更
python3 i18n.py add Member_NewField --zh "新字段" --en "New Field" --dry-run

# 跳过确认
python3 i18n.py add Data_NewFormat --zh "新格式" --en "New Format" --force
```

### `modify` — 修改已有 key

```bash
# 仅修改中文
python3 i18n.py modify Settings_Title --zh "系统设置"

# 仅修改英文
python3 i18n.py modify Settings_Title --en "System Settings"

# 添加注释
python3 i18n.py modify Seating_Export --comment "导出按钮提示"

# 移除注释
python3 i18n.py modify Seating_Export --clear-comment

# 同时修改中英文和注释
python3 i18n.py modify Common_OK --zh "确定" --en "OK" --comment "确认按钮"
```

### `rename` — 重命名 key

```bash
python3 i18n.py rename Seating_Title Seating_WindowTitle
```

三文件同步重命名。

### `delete` — 删除 key

```bash
python3 i18n.py delete Old_ObsoleteKey
python3 i18n.py delete Temp_DebugKey --force   # 跳过确认
```

### `sync` — 重新生成 Designer.cs

```bash
# 预览
python3 i18n.py sync --dry-run

# 执行
python3 i18n.py sync
```

从 `Resources.resx` 的 key 列表重新生成 `Resources.Designer.cs` 中的所有属性。保留原有注释分隔符和代码结构。

### `export` — 导出为 CSV/JSON

```bash
# 导出为 CSV（供 Excel/Google Sheets 审核）
python3 i18n.py export -o translations.csv

# 导出为 JSON
python3 i18n.py export --format json -o translations.json
```

### `import` — 从 CSV/JSON 导入

```bash
# 从 CSV 导入（根据扩展名自动检测格式）
python3 i18n.py import translations.csv --dry-run
python3 i18n.py import translations.csv --force

# 从 JSON 导入
python3 i18n.py import translations.json --format json
```

导入时仅更新已存在的 key，不存在的 key 会警告并跳过。

CSV 格式要求：列名 `Key, zh-CN, en-US, Comment`。

JSON 格式要求：
```json
{
  "Common_OK": { "zh-CN": "确定", "en-US": "OK" },
  "Settings_Title": { "zh-CN": "设置", "en-US": "Settings", "comment": "页面标题" }
}
```

## 安全机制

### 自动备份

所有写入操作（`add`/`modify`/`rename`/`delete`/`sync`/`import`）在执行前自动备份三文件到 `Lang/.backup/` 目录，文件名格式为 `{原文件名}.{YYYYMMDD_HHMMSS}`。

备份目录已加入 `.gitignore`，不会被提交到版本控制。

### Dry-run 模式

所有写入命令支持 `--dry-run` 参数，展示变更摘要但不实际写入文件。

### 确认提示

写入操作默认要求用户输入 `y` 确认。使用 `--force` / `-f` 跳过确认。

### 原子性

先完成全部校验，再一次性写入所有文件。校验失败不会修改任何文件。

### XML 合法性

写入后立即重新解析验证 XML 格式正确。

## Key 命名规范

格式：`Category_MeaningfulName`（PascalCase，下划线分隔）

已知分类：

| 前缀 | 用途 |
|------|------|
| `About_` | 关于对话框 |
| `App_` | 应用级消息 |
| `Common_` | 共享 UI 标签 |
| `ConfigBlock_` | 配置块 UI |
| `Data_` | 数据加载/保存 |
| `Freeform_` | 自由布局管理 |
| `Gender_` | 性别标签 |
| `Guide_` | 引导系统 |
| `Home_` | 首页 |
| `Lang_` | 语言名称 |
| `Member_` | 成员管理 |
| `Nav_` | 导航栏 |
| `Seating_` | 座位安排 |
| `Settings_` | 设置页 |
| `Snapshot_` | 快照历史 |
| `Startup_` | 启动守卫 |
| `Strategy_` | 策略配置 |
| `Theme_` | 主题名称 |
| `Venue_` | 会场配置 |
| `Watchdog_` | 看门狗服务 |
| `Zoom_` | 缩放级别 |

格式字符串使用 `{0}`、`{1}` 占位符。建议以 `Fmt` 结尾命名。

## 常见工作流

### 添加新的 UI 文案

```bash
# 1. 添加 key，同步三文件
python3 i18n.py add Settings_AutoSave --zh "自动保存" --en "Auto Save"

# 2. 在 C# 代码中使用
# StatusMessage = Resources.Settings_AutoSave;

# 3. 或在 AXAML 中使用
# <TextBlock Text="{x:Static lang:Resources.Settings_AutoSave}" />

# 4. 验证
python3 i18n.py check
dotnet build
```

### 批量修改翻译

```bash
# 1. 导出为 CSV
python3 i18n.py export -o translations.csv

# 2. 在 Excel 中编辑 translations.csv

# 3. 预览导入
python3 i18n.py import translations.csv --dry-run

# 4. 执行导入
python3 i18n.py import translations.csv

# 5. 验证
python3 i18n.py check
dotnet build
```

### 重构清理

```bash
# 查找可能未翻译的项
python3 i18n.py list --missing-en

# 查找所有格式字符串（确认参数数量正确）
python3 i18n.py list --format-strings

# 全面检查
python3 i18n.py check
```

## 文件结构

```
scripts/
├── i18n.py          # 本地化资源管理
├── version.py       # 版本号管理
├── ToolsCollection.md  # 本文档
└── tests/
    ├── test_i18n.py     # i18n 单元测试
    └── test_version.py  # 版本管理单元测试

src/SeatFlow.Presentation.Avalonia/Lang/
├── Resources.resx          # zh-CN 资源文件
├── Resources.en-US.resx    # en-US 资源文件
├── Resources.Designer.cs   # 强类型访问器（由 sync 命令生成）
└── .backup/                # 自动备份目录（已 gitignore）
```

---

# version — 版本号统一管理

管理项目中的 4 类版本号：App 版本、文件格式版本、策略清单版本、引导配置版本。

## 快速开始

```bash
cd scripts

# 查看版本概览
python3 version.py show

# 校验一致性
python3 version.py check

# 调整 App 版本
python3 version.py bump-app patch --dry-run
python3 version.py bump-app minor --force

# 调整文件格式版本（自动同步 Model 类）
python3 version.py bump-file roster --set 1.2 --dry-run
python3 version.py bump-file roster --set 1.2 --force

# 调整策略清单版本
python3 version.py bump-strategy FixedSeat --set 1.1.0 --force

# 同步 Model 类默认值
python3 version.py sync --dry-run
python3 version.py sync --force
```

## 子命令

| 命令 | 功能 |
|------|------|
| `show` | 显示全部版本号概览（App / 文件格式 / 策略 / 引导配置） |
| `check` | 校验 15+ 处版本定义的一致性 |
| `bump-app [major\|minor\|patch\|--set X.Y.Z]` | 调整 App 版本 |
| `bump-file TYPE --set X.Y` | 调整文件格式版本（自动同步 Model 类 + JsonStudentWriter） |
| `bump-strategy ID --set X.Y.Z` | 调整策略清单版本（ID 或 ALL） |
| `bump-onboarding --set X.Y` | 调整引导配置版本 |
| `sync` | 从 file_versions.json 同步所有 Model 类默认值 |

## 受管文件

| 体系 | 文件 |
|------|------|
| App 版本 | `src/SeatFlow.Presentation.Avalonia/Data/about.json` (zh-CN + en-US) |
| 文件格式版本 | `file_versions.json` + 7 个 Model C# 类 + `JsonStudentWriter.cs` |
| 策略清单版本 | 7 个 `Manifests/*.json` |
| 引导配置版本 | `onboarding_config.json` |

## 安全机制

与 i18n 脚本一致：自动备份到 `.version-backups/`、`--dry-run` 预览、`--force` 跳过确认。
```
