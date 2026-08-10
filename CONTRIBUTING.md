# 参与开发

## 环境搭建

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows / macOS / Linux
- 推荐 IDE：Rider、VS Code + C# 扩展、Visual Studio 2022+

```bash
git clone <repo-url> && cd SeatFlow
dotnet restore
```

## 常用命令

| 命令 | 说明 |
|------|------|
| `dotnet restore` | 还原 NuGet 依赖 |
| `dotnet build` | 构建全部 9 个项目 |
| `dotnet test` | 运行所有测试 |
| `dotnet test --filter "FullyQualifiedName~TestName"` | 运行单个测试 |
| `dotnet run --project src/SeatFlow.Presentation.Avalonia` | 启动桌面应用 |

测试栈：xUnit v3 + FluentAssertions + NSubstitute。测试项目分布在 `*.Core.Tests`、`*.Application.Tests`、`*.Infrastructure.Tests`。

## 项目结构

```
SeatFlow.slnx
├── src/SeatFlow.Core/                  → 领域核心：实体、策略接口、领域服务
├── src/SeatFlow.Contracts/             → 共享契约：跨层接口（插件契约等）
├── src/SeatFlow.Application/           → 应用层：外观、策略管道、命令历史、插件管理、DI
├── src/SeatFlow.Infrastructure/        → 基础设施：数据提供者、导出器、布局构建器、仓储
├── src/SeatFlow.Plugins.Sdk/           → 插件 SDK（供外部插件引用）
├── src/SeatFlow.Presentation.Avalonia/ → Avalonia 12 桌面应用
├── tests/SeatFlow.Core.Tests/
├── tests/SeatFlow.Application.Tests/
└── tests/SeatFlow.Infrastructure.Tests/
```

**分层依赖**：`Presentation.Avalonia` → `Application` → (`Core`, `Contracts`, `Infrastructure`)

包版本在每个 `.csproj` 中直接管理，无 `Directory.Build.props` 或 `Directory.Packages.props`。

## 架构要点

参见 [ARCHITECTURE.md](ARCHITECTURE.md)（架构总览）、[Phases.md](Phases.md)（阶段划分）和 [docs/adr/](docs/adr/)（架构决策记录）。

关键模式：
- **外观模式** — `IApplicationFacade` 是 UI 层唯一入口
- **策略模式** — `ISeatingStrategy` 按优先级管道执行
- **命令模式** — `IUndoableCommand` + `CommandHistory` 撤销/重做
- **插件隔离** — `AssemblyLoadContext`（`isCollectible: true`）独立加载外部 DLL，卸载遵循官方可回收模式（弱引用探测 + 压缩式强制 GC）

## 编码约定

- 面向接口编程，通过 DI 容器管理生命周期
- ViewModel 继承 `ViewModelBase`，使用 `[ObservableProperty]` / `[RelayCommand]` 源代码生成器
- Axaml 绑定使用 `x:DataType` 启用编译绑定（项目已开启 `AvaloniaUseCompiledBindingsByDefault`）
- 异步操作通过 `SafeExecuteAsync` 包装，自动处理异常和超时
- 无头环境中无法使用 Avalonia DevTools

## 国际化 (i18n)

使用 .NET `.resx` 资源文件，位于 `src/SeatFlow.Presentation.Avalonia/Lang/`：

- `Resources.resx` — 中性语言 (zh-CN)，~570 键
- `Resources.en-US.resx` — 英文卫星资源
- `Resources.Designer.cs` — 手动维护的强类型访问器类

**XAML 用法**（仅属性语法，元素内容不解析）：
```xml
<TextBlock Text="{x:Static lang:Resources.Settings_Title}" />
<Button Content="{x:Static lang:Resources.Common_OK}" />
```

**C# 用法**：
```csharp
StatusMessage = Resources.Settings_Saved;
StatusMessage = string.Format(Resources.Snapshot_VenuesLoadedFmt, count);
```

**键命名**: `{Page}_{Element}`，如 `Settings_Title`、`Nav_Home`、`Common_OK`。格式化字符串用 `{0}` 占位符。

**新增语言**: 在 `Lang/` 下创建 `Resources.xx-XX.resx`，翻译所有键即可，无需代码变更。

**语言切换**: 在 `AppSettings.Language` 中设置语言代码（如 `en-US`），重启后生效。

## 添加策略配置

内置策略通过 manifest JSON 声明配置 UI（`src/SeatFlow.Core/Strategies/Manifests/{Id}.json`）。

### 声明策略参数（parameters）

```json
{
  "parameters": [
    {
      "name": "MyParam",
      "fieldType": "NumberInput",
      "label": { "zh-CN": "我的参数", "en-US": "My Parameter" },
      "defaultValue": 10,
      "minValue": 0,
      "maxValue": 100
    }
  ]
}
```

支持的 fieldType：`NumberInput`、`TextInput`、`ToggleSwitch`、`Dropdown`。

### 策略可见性（visible）

manifest 顶层 `visible` 字段（可选，默认 `true`）控制策略在配置页面的可见性：

```json
{ "visible": false }
```

设为 `false` 时，该策略在配置页侧栏不显示，不可用。设为 `true` 或不填时正常显示。

### 声明配置块（codeBlocks）

```json
{
  "codeBlocks": [
    {
      "title": { "zh-CN": "配置块标题", "en-US": "Block Title" },
      "description": { "zh-CN": "说明文字", "en-US": "Description" },
      "dataType": "Student",
      "displayMode": "ValuePair",
      "fields": [
        { "name": "mate", "fieldType": "StudentPicker",
          "label": { "zh-CN": "的同桌是", "en-US": "'s desk mate" } }
      ]
    }
  ]
}
```

dataType：`Student`（仅人员）、`Venue`（仅会场）、`Both`（人员+会场）。
displayMode：`Table`（表格）、`ValuePair`（值对行）。
`showSeatPosition`（可选，默认 true）：`false` 时隐藏座位定位器——用于自动匹配策略。
`preventDuplicateInRow`（可选，默认 false）：`true` 时禁止同行内学生选择器值重复，下拉列表互相排除已选学生。
`preventDuplicateAcrossRows`（可选，默认 false）：`true` 时禁止跨行学生选择器值重复，下拉列表互相排除已选学生——用于 FixedSeat。
`loadTrigger`（可选，默认 `Both`）：控制 `dataType:Both` 时配置加载时机——`Both`=两个选择器都需有值后精确匹配加载，`Any`=任一选择器有值即模糊匹配加载。
fieldType 在 codeBlock 中额外支持 `StudentPicker`、`SeatPosition`。

**依赖策略（`isIndependent: false`）**：不同于独立策略通过外部管道执行，依赖策略在 RandomFill 的分配循环中按内部 Priority 评估每个 (student, seat) 对。依赖策略实现 `IDependentSeatingStrategy` 接口（而非 `ISeatingStrategy`），其 `EvaluateAsync` 方法返回 `Approve`、`Reject`（请求重掷）或 `Handled`（已完成分配含连携修改）。在 DI 中需注册为 `IDependentSeatingStrategy`。

**DeskMate 策略**（依赖策略，`isIndependent: false`）：在 RandomFill 上下文中执行。当 RandomFill 随机分配学生时检查同桌关系——若有同桌组，尝试将同组学生分配到相邻座位（连携修改）。若目标座位无足够相邻空座则请求重掷。彻底解决了旧版受前序策略碎片化的问题。

**DeskMate 特殊处理**：当 `dataType: "Both"` 且选中会场后，UI 读取 `GridLayoutMetadata.SeatsPerDesk` 动态决定每行的 StudentPicker 数量。`SeatsPerDesk` 变化时自动清除不兼容的旧配置行。
同行内多个学生选择器通过 `preventDuplicateInRow` 互相排除已选学生，确保一桌多人不能是同一个人。

**FixedSeat 特殊处理**：通过 `preventDuplicateAcrossRows` 确保跨所有行的学生选择器值互斥，一个学生只能固定在一个座位。

**配置加载与匹配**：持久化配置的过滤采用"已选定则匹配，未选定则跳过"策略——仅已选定的选择器参与 ID 匹配。对于 `dataType: "Both"`，用户仅选数据集即可加载配置（场馆作为通配符），选场馆后重新精确匹配。学生选择通过 `_pendingSelections` 延迟到列表加载完成后应用。

### 策略执行消息 i18n

策略执行期间可通过 `workspace.LogWarning(id, displayName, messageKey, args)` 记录警告/错误。消息模板在 manifest 的 `messages` 字段中声明：

```json
"messages": {
  "DeskMate_Split": {
    "zh-CN": "同桌组（{0}）中的 {1} 已被前排策略分配，该组已拆散",
    "en-US": "Desk-mate group ({0}) member(s) {1} already assigned to front row, group split"
  }
}
```

内建策略和插件策略共用同一格式——内建策略的 messages 在 `Manifests/{Id}.json` 中，插件策略的在各插件包策略子目录下的 `manifest.json` 中（包级清单为 `plugins-manifest.json`）。

### i18n 约定

所有用户可见文字使用 `{ "zh-CN": "...", "en-US": "..." }` 词典格式，UI 层通过 `LocalizeHelper.Resolve(dict)` 解析。

## 添加新页面

1. 在 `INavigationService.cs` 的 `PageKey` 枚举中添加新值
2. 创建 `ViewModels/XXXViewModel.cs`（继承 `ViewModelBase`）
3. 创建 `Views/XXXView.axaml` + `.axaml.cs`（设置 `x:DataType="vm:XXXViewModel"`）
4. 在 `Program.cs` 中注册 `services.AddSingleton<XXXViewModel>()`
5. 在 `MainWindow.axaml` 侧边栏添加导航按钮

`ViewLocator` 通过命名约定自动解析 `XXXViewModel` → `XXXView`。

## 文件版本管理

所有持久化 JSON 文件携带 `version` 字段，加载时通过 `FileMigrationService` 自动向前迁移。版本号定义在 `src/SeatFlow.Infrastructure/Migration/file_versions.json`（嵌入资源）。

### 添加新版本迁移

1. 在 `Migration/Migrators/{FileType}Migrators.cs` 的容器类中新增嵌套类，实现 `IFileMigrator`：
   ```csharp
   public static class VenueMigrators
   {
       public sealed class Step_1_1_to_1_2 : IFileMigrator
       {
           public string FileType => "venue";
           public string FromVersion => "1.1";
           public string ToVersion => "1.2";
           public JsonNode Migrate(JsonNode root) { /* 迁移逻辑 */ }
       }
   }
   ```
2. 在 `ServiceCollectionExtensions.cs` 注册：`services.AddSingleton<IFileMigrator, VenueMigrators.Step_1_1_to_1_2>()`
3. 在 `file_versions.json` 中提升版本号
4. 在 `Core/Models/` 对应模型类中更新默认 `Version` 属性
5. 在 `Infrastructure.Tests/Migration/{FileType}MigratorsTests.cs` 添加覆盖测试

### JSON 字段约定

- 序列化使用 `JsonNamingPolicy.CamelCase`，所有字段在 JSON 中为小写驼峰
- `layoutType` 为数字枚举值，`layoutTypeString` 为对应字符串 — 迁移器中优先使用 `layoutTypeString`
- `Seat` 多态序列化通过 `SeatJsonConverter` 写入 `Type`（大写）鉴别器字段

## 提交约定

- 提交前确保 `dotnet build` 和 `dotnet test` 通过
- 提交信息使用中文描述改了什么、为什么改

## AI 辅助开发

本项目使用 Claude Code 辅助开发。项目级 AI 配置位于 [CLAUDE.md](CLAUDE.md)。AI 开发者应优先阅读此文件和 `docs/adr/` 中的架构决策记录，以理解既有设计的上下文和权衡。
