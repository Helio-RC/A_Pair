# Fluent UI Icons Reference

This file records all Fluent UI System Icons used in SeatFlow, sourced from the `FluentIcons.Avalonia` NuGet package.

NuGet: `FluentIcons.Avalonia` v2.1.325  
XAML namespace: `xmlns:fic="using:FluentIcons.Avalonia"`  
Enum namespace: `xmlns:ficEnum="clr-namespace:FluentIcons.Common;assembly=FluentIcons.Common"`  

Usage: `<fic:FluentIcon Icon="{x:Static ficEnum:Icon.{Name}}" FontSize="18"/>`

---

## Navigation Icons (Sidebar)

| Icon Name | Fluent UI Name | Usage |
|-----------|---------------|-------|
| `DataUsage` | 📊 Data Usage | 人员管理 / Data Management |
| `Building` | 🏗️ Building | 会场配置 / Venue Configuration |
| `Options` | ⚙️ Options | 策略配置 / Strategy Configuration |
| `Grid` | 📐 Grid | 座位安排 / Seating Arrangement |
| `History` | 🕐 History | 历史快照 / Snapshot History |
| `PuzzlePiece` | 🧩 Puzzle Piece | 插件管理 / Plugin Management |
| `Settings` | ⚙️ Settings | 设置 / Settings |
| `Info` | ℹ️ Info | 关于 / About |

## Action Icons (Sidebar Toggle)

| Icon Name | Fluent UI Name | Usage |
|-----------|---------------|-------|
| `PanelLeftContract` | ◀ Panel Left Contract | 折叠侧边栏 / Collapse sidebar |
| `PanelLeftExpand` | ▶ Panel Left Expand | 展开侧边栏 / Expand sidebar |

## Action Icons (General Operations)

| Icon Name | Fluent UI Name | Usage |
|-----------|---------------|-------|
| `ArrowDownload` | ⬇ Arrow Download | 导入 / Import |
| `ArrowUpload` | ⬆ Arrow Upload | 导出 / Export |
| `Save` | 💾 Save | 保存 / Save |
| `ArrowUndo` | ↩ Arrow Undo | 撤销 / Undo |
| `ArrowRedo` | ↪ Arrow Redo | 重做 / Redo |
| `Add` | ➕ Add | 添加 / Add |
| `Delete` | 🗑 Delete | 删除 / Delete |
| `ArrowSync` | 🔄 Arrow Sync | 刷新 / Refresh |

---

## How to Find Icons

All available icon names can be listed via:

```csharp
using System.Reflection;
var asm = Assembly.LoadFrom("FluentIcons.Common.dll");
var t = asm.GetType("FluentIcons.Common.Icon");
foreach (var n in Enum.GetNames(t)) Console.WriteLine(n);
```

Search the official Fluent UI System Icons catalog at:  
https://aka.ms/fluentui-system-icons
