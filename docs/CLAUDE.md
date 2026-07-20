# CLAUDE.md

本文件为 Claude Code（claude.ai/code）在此仓库中工作时提供指导。

## 对话与编码语言

你需要使用中文来进行对话和思考、编写注释。

## 开发环境

在执行需要 GUI 的操作前，先确认当前是否处于无头环境（检查 `DISPLAY` / `WAYLAND_DISPLAY` 等环境变量），以及是否安装了 .NET 10 SDK。avdt（Avalonia DevTools）仅在桌面环境下可用。

## 构建与测试

```bash
dotnet build                    # 构建全部 9 个项目（使用 .slnx，需要 .NET 10 SDK）
dotnet test                     # 运行全部测试（xUnit v3，Microsoft.Testing.Platform）
dotnet test --filter "FullyQualifiedName~TestName"  # 运行单个测试
dotnet run --project SeatFlow.Presentation.Avalonia   # 启动桌面应用
```

**测试技术栈**：xUnit v3 + FluentAssertions + NSubstitute。测试分布在 3 个项目中：`*.Core.Tests`、`*.Application.Tests`、`*.Infrastructure.Tests`。每个项目都启用了 `<ImplicitUsings>enable</ImplicitUsings>`（自动引入 `System`、`System.Collections.Generic`、`System.Linq`、`System.Threading.Tasks`）。项目特定的全局 using 在 `Usings.cs`（Application.Tests 中为 `Using.cs`）中定义。

**没有 `Directory.Build.props` 或 `Directory.Packages.props`** — 包版本在每个 `.csproj` 中直接管理。

**dotnet 工具**：`avaloniaui.developertools`（avdt）已安装在仓库根目录的 `dotnet-tools.json` 中。使用前先确认当前环境是否有桌面显示支持，若无头则跳过 avdt。构建时无需此工具。

## 架构

SeatFlow 是一个 .NET 10 跨平台桌面座位编排系统，使用 Avalonia UI 12（MVVM）+ CommunityToolkit.Mvvm 8.4。解决方案文件为 `SeatFlow.slnx`（新的 XML 格式）。

**分层架构（自底向上）**：

- **Core** — 领域实体（`Student`、`Seat`、`ClassroomLayoutDefinition`、`SeatingWorkspace`、`SeatingPlan`），策略接口（`ISeatingStrategy`、`IDependentSeatingStrategy`）+ 七种内置实现（4 个独立策略 + 3 个依赖策略），能力系统（`Capability.cs` — 常量 + `IFixedSeatCapability`），`DomainServices/` 中的领域服务（`ObstacleProcessor`、`SeatGeometryHelper`、`StrategyManifestProvider`、`SeatAdjacencyHelper`），`Utilities/` 中的工具类（`CircularHistory<T>` — 环形缓冲区，`Student.RecentSeatHistory` 上容量为 10；`AttributeBag`），`Workspace/` 中的工作区（`SeatingWorkspace`），以及数据提供者接口（`IStudentProvider`、`IVenueRepository` 等）。
- **Contracts** — 跨层接口，供插件使用（`IPluginSeatingStrategy`）
- **Infrastructure** — 文件 I/O（`CsvStudentProvider`、`XlsxStudentProvider`（使用 **EPPlus 8**）、`JsonStudentProvider`，以及作为主 `IStudentProvider` 注册的组合 `CompositeStudentProvider`），导出器（`ExcelSeatingExporter`、`CsvSeatingExporter`、`PdfSeatingExporter`、`ImageSeatingExporter`），布局构建器（`GridLayoutBuilder`、`PolarLayoutBuilder`、`FreeformLayoutBuilder`），仓库（`JsonVenueRepository`、`JsonAppSettingsRepository`、`StrategyConfigFileRepository`、`SeatingSnapshotRepository`、`JsonStudentDatasetRepository`），写入器（`JsonStudentWriter`、`CsvStudentWriter`、`XlsxStudentWriter`），序列化，迁移系统（`FileMigrationService`、`IFileMigrator`）
- **Application** — `IApplicationFacade`（UI 的统一入口），`StrategyExecutionPipeline`，命令模式（`IUndoableCommand` / `CommandHistory`），插件管理器（`PluginManager`、`PluginLoadContext`），脚本适配器（Lua/C#），DI 注册
- **Plugins.Sdk** — 轻量级程序集，供外部插件作者使用
- **Presentation.Avalonia** — Avalonia 12 桌面应用，使用 CommunityToolkit.Mvvm 的 MVVM 模式

**日志**：使用 **Serilog 4** + `Microsoft.Extensions.Logging.ILogger<T>` 贯穿 Application 层。通过 `Serilog.Sinks.File` 输出到文件，使用自定义输出模板（`[Level] [SourceContext]`）。支持分模块日志等级覆盖（`CategoryOverrides` in AppSettings.json）。生产环境默认 `Information`；调试器附加时自动降为 `Debug`（`Debugger.IsAttached`）。完整文档：`docs/LOGGING.md`。

**依赖注入**：Application 层的 `ServiceCollectionExtensions.AddSeatFlowApplication(snapshotBasePath, pluginsPath)` 注册所有服务（策略、导出器、提供者、仓库、插件管理器）。在 `Program.cs` 中，UI 层调用此方法后添加自己的单例：`INavigationService`、`IFileService`、`IDialogService`、`MainWindow`、`MainShellViewModel` 以及所有页面 ViewModel。

**导航**：`INavigationService` + `MainShellViewModel` 通过 `PageKey` 枚举管理 10 个页面（`Home`、`MemberManagement`、`VenueConfiguration`、`FreeformManagement`、`StrategyConfiguration`、`SeatingArrangement`、`SnapshotHistory`、`PluginManagement`、`Settings`、`About`）。`ViewLocator` 按约定自动解析 `XXXViewModel` → `XXXView`：通过反射将类型名中的 `"ViewModel"` 替换为 `"View"`。

**项目依赖链**：`Presentation.Avalonia` → `Application` → (`Core`、`Contracts`、`Infrastructure`)。`Plugins.Sdk` 仅被外部插件引用。`Application` 负责编排；`Infrastructure` 实现提供者/导出器/布局/仓库；`Core` 持有实体、策略接口和工作区。

**策略管道**：独立策略采用 **fill-in-order**（按序填充）模型。依赖策略通过 `IDependentSeatingStrategy` 在 RandomFill 的分配循环内执行。所有策略操作同一个 `SeatingWorkspace`。独立策略按 **Priority 降序**执行（数值越大 = 越早执行 = 优先占用空座位）。没有"覆盖"语义；先占座位的策略保留该座位。`IsFixed=true`（由 FixedSeat 设置）会使 `GetEmptySeats()` 排除这些座位，提供天然保护。

| 顺序 | 策略 | Priority | 类型 | 职责 |
|-------|----------|----------|------|------|
| 第1 | `FixedSeatStrategy` | 100 | 独立 | 锁定固定座位（IsFixed=true），后续所有 GetEmptySeats 均排除 |
| 第2 | `FrontRowRotationStrategy` | 50 | 独立 | 从剩余非固定空座位中填充前排座位 |
| — | `DeskMateStrategy` | 50（上下文） | 依赖 | 在 RandomFill 内执行：检查每个 (学生, 座位) 对上的同桌分组，协调相邻分配（仅水平/同桌），必要时请求重试 |
| — | `GenderRestrictedSeatStrategy` | 45（上下文） | 依赖 | 在 RandomFill 内 DeskMate 之后执行：检查目标座位是否有性别限制。不匹配时将学生重定向到匹配性别的受限空座位；无可选座位时拒绝以触发重试；重试耗尽后强制分配并警告 |
| — | `NoRepeatDeskMateStrategy` | 40（上下文） | 依赖 | 在 RandomFill 内 DeskMate 之后执行：检查目标座位相邻已占用座位是否包含历史同桌记录。检测到重复时拒绝以触发重试；重试耗尽后强制分配并警告 |
| 第3 | `RandomFillStrategy` | 1 | 独立 + 宿主 | 填充剩余座位；在其分配循环中托管依赖策略。受约束学生（DeskMate 分组）优先处理以减少重试次数。逐出操作尊重先前策略的分配结果 |
| 第4 | `DefragStrategy` | 0 | 独立 | "扫地僧" — 所有策略执行完毕后，将后排无约束学生前移填空隙。允许跨列。跳过 FixedSeat 和 DeskMate 分组学生。记录有效性警告（可能使先前策略结果部分失效） |

冲突解决 = Priority 数值（先到先得）。依赖策略在 RandomFill 上下文中有自己的内部优先级排序（DeskMate 50 → GenderRestrictedSeat 45 → NoRepeatDeskMate 40）。Defrag（0）最后执行，可能部分使先前策略结果失效 — 参见其有效性警告。Handled 的分配仍会运行剩余依赖策略以供检查/警告。详见 `docs/adr/ADR-006-strategy-pipeline-fill-in-order.md`。

**策略消息**：策略可在执行期间通过 `workspace.LogWarning(strategyId, displayName, messageKey, args)` 和 `workspace.LogError(strategyId, displayName, messageKey, args)` 报告警告/错误。`messageKey` 对应清单文件中 `messages` 字典的键（内联 i18n：`{ "zh-CN": "...", "en-US": "..." }`）。消息收集在 `SeatingWorkspace.Messages` 中（含 `StrategyId`、`StrategyDisplayName`、`MessageKey` 和 `Args`），管道执行后在 UI 侧栏展示。插件策略通过 `IPluginWorkspace` 访问相同方法。

**声明式策略配置**：所有策略特定配置（除 Priority/IsEnabled 外）均由清单 JSON 文件（`SeatFlow.Core/Strategies/Manifests/*.json`）驱动。核心顶层字段：

- **`visible`** —（可选，默认 `true`）控制策略是否参与管道。设为 `false` 后从 UI（配置页、排座侧栏）和执行中均排除 — 管道跳过不可见策略。
- **`isIndependent`** —（可选，默认 `true`）`true` = 独立策略（由外部管道执行）；`false` = 依赖策略（在 RandomFill 的分配循环内执行）。DeskMate、GenderRestrictedSeat 和 NoRepeatDeskMate 为 `false`。
- **`manifestVersion`** —（可选，默认 `"1.0"`）清单格式版本，用于运行时兼容性检查。嵌入资源不经过 FileMigrationService，因此版本超过最大已知版本时提供者会警告。
- **`capabilities[]`** —（可选）策略能力声明。每项为 `SeatFlow.Core.Strategies.Capability` 中定义的能力常量（如 `"MarkFixedSeat"`）。策略必须先声明能力，才能在运行时调用相应的接口方法。未声明的能力调用将被拒绝并记录警告。当前支持：`MarkFixedSeat` → `IFixedSeatCapability.TryMarkFixed()`。可扩展 — 在 `Capability.cs` 中添加新常量 + 接口。
- **`parameters[]`** — 策略级全局参数。每个参数声明 `fieldType`（`NumberInput`、`TextInput`、`ToggleSwitch`、`Dropdown`）、`label`（Dictionary<string,string> 用于 i18n）、`defaultValue` 以及可选的 `minValue`/`maxValue`。UI 将其渲染为标准输入控件。
- **`codeBlocks[]`** — 按数据集/会场的配置块。每个块声明 `dataType`（`Student`、`Venue`、`Both`）、`displayMode`（`Table`、`ValuePair`）、可选的 `showSeatPosition`（默认 true，对于 DeskMate 等自动匹配策略设为 false）、可选的 `showStudentPicker`/`showVenuePicker`（覆盖 DataType 自动检测）、可选的 `studentPickerCount`（默认 1）、可选的 `seatsPerDeskFromVenue`（设为 true 时从会场的 GridLayoutMetadata.SeatsPerDesk 读取学生数）、可选的 `preventDuplicateInRow`（设为 true 时阻止同行学生选择器重复值 — DeskMate）、可选的 `preventDuplicateAcrossRows`（设为 true 时阻止跨行学生选择器重复值 — FixedSeat），以及可选的 `loadTrigger`（默认 `Both` — 两个选择器都需匹配；`Any` — 任一选择器有值时模糊匹配）。UI 渲染数据集选择器 + 含学生选择器和/或座位位置选择器的配置行。
  - **DeskMate**（依赖，`isIndependent: false`）：在 RandomFill 的分配循环内通过 `IDependentSeatingStrategy` 执行。当 RandomFill 提议 (学生, 座位) 时，DeskMate 检查该学生是否属于同桌分组。若是，则尝试协调分配：将该学生及其组员安排在同行相邻座位上（同行+邻列+同 SeatsPerDesk 分组 = 同桌）。逐出可能移动已分配的 RandomFill 学生，但不会移动先前策略（FixedSeat/FrontRowRotation）分配的学生或固定座位。若目标座位附近空座不足，则部分分配并发出警告。无参数 — 相邻始终为水平/同桌面。`dataType: "Both"`，`showSeatPosition: false`，`preventDuplicateInRow: true`。每行学生选择器数量由会场的 `GridLayoutMetadata.SeatsPerDesk` 动态确定。
  - **GenderRestrictedSeat**（依赖，`isIndependent: false`）：在 RandomFill 的分配循环内 DeskMate 之后执行（Priority 45）。检查目标座位是否通过 codeBlock 配置了性别限制。性别不匹配时触发重定向优化：学生立即被放入匹配性别的随机受限空座位（Handled，不消耗重试次数）。若无匹配的受限座位可用，则拒绝以触发重试；重试耗尽后强制分配并警告。无参数 — 限制通过 codeBlock 按座位配置。`dataType: "Venue"`，`showSeatPosition: true`，`showStudentPicker: false`，`showGenderPicker: true`，`preventDuplicateAcrossRows: true`。性别值存储在 `CustomValues["Gender"]` 中。
  - **NoRepeatDeskMate**（依赖，`isIndependent: false`）：在 RandomFill 的分配循环内 DeskMate 之后执行（Priority 40）。检查目标座位的相邻已占用座位是否包含历史同桌记录（由 `NoRepeatDeskMateHistoryLoader` 从最近快照加载）。检测到重复时拒绝以触发重试；重试耗尽后强制分配并警告。不干扰 DeskMate 分组放置（优先级较低）或固定座位。参数：`HistoryWindowSize`（1–30，默认 10）。无 codeBlocks。
  - **FixedSeat**：`dataType: "Both"`，`preventDuplicateAcrossRows: true`。每行有一个学生选择器 + 座位位置选择器，用于显式分配。所有行的学生选择器互相排除对方已选学生。
  - **FrontRowRotation**：无 codeBlocks — `NeedsFrontRow` 已是 Student 模型属性（从 CSV/XLSX 导入）。按成绩选择学生后，使用 Fisher-Yates 洗牌随机分布到前排各列。
  - **RandomFill**：无参数，无 codeBlocks。
  - **Defrag**（独立，Priority=0）："扫地僧"角色 — 所有其他策略之后执行。从前到后扫描空座位，将每个空隙后面的无约束学生（不在固定座位或 DeskMate 分组中）前移填空。允许跨列。记录 `Defrag_EffectivenessNote` 警告，提示先前策略结果可能被部分推翻。零参数 — 行为纯位置驱动。默认禁用。

**插件座位保护**：插件通过在清单 `capabilities` 中声明 `"MarkFixedSeat"` 并调用 `IPluginWorkspace.TryMarkFixed()` 来保护其分配的座位。工作区验证能力声明后设置 `IsFixed=true` 并记录操作。`GetEmptySeats()` 和 Defrag 的座位扫描均自动排除 `IsFixed` 座位。内置策略通过 `IFixedSeatCapability` 使用相同机制。能力常量和接口集中在 `SeatFlow.Core/Strategies/Capability.cs` — 在此添加新常量 + 接口以支持未来能力。

所有用户可见文本使用内联 i18n：`{ "zh-CN": "...", "en-US": "..." }` 字典（而非 .resx 键）。Presentation 中的 `LocalizeHelper.Resolve(dict)` 根据 `CultureInfo.CurrentUICulture` 解析，回退到 zh-CN。内置策略和插件均适用。

**配置加载行为**：加载持久化配置行时，匹配过滤器采用"以有值的选择器为准"的策略：`(SelectedDataset is null || match) && (SelectedVenue is null || match)`。这意味着对于 `dataType: "Both"`，仅选择数据集时立即加载配置（会场在选择前视为通配符）。当用户随后选择会场时，过滤器用两个值重新运行，精确匹配。学生选择器的选择通过 `_pendingSelections` 延迟到学生列表加载完成后，避免过早 `SelectById` 调用导致选择丢失。

新建模型类型（均在 `SeatFlow.Core.Models` 中）：
- `StrategyParameterDefinition` / `StrategyCodeBlock` / `StrategyFieldDefinition` + 枚举（`StrategyFieldType`、`StrategyDataType`、`StrategyDisplayMode`）
- `StrategyDatasetConfig` + `StrategyConfigRow` — 持久化模型，存储在 `{DataDirectory}/StrategyConfig/{strategyId}/` 下。

**数据存储**：所有用户数据存储在 OS 标准应用数据目录（`AppEnvironment.DefaultDataDirectory`）：
- Windows: `%APPDATA%\SeatFlow\` / Linux: `~/.local/share/SeatFlow/` / macOS: `~/Library/Application Support/SeatFlow/`
- 用户可通过 `AppSettings.json` 的 `DataDirectory` 选项自定义
- 插件目录：Velopack 安装时 `RootAppDir/Plugins/`，开发模式 `{exeDir}/Plugins/`

**项目配置**：Avalonia csproj 中 `AvaloniaUseCompiledBindingsByDefault` 为 `true` — 所有绑定均为编译绑定，除非显式退出。关键 csproj 设置：
- `<AssemblyName>SeatFlow</AssemblyName>` — 输出 EXE 为 `SeatFlow.exe`，而非 `SeatFlow.Presentation.Avalonia.exe`
- `<NoWarn>AVLN3001</NoWarn>` — 抑制"DI 需要参数化构造函数"警告（所有 ViewModel 使用 DI 构造函数注入，无需无参构造函数）
- `<Compile Remove="Lang\Resources.Designer.cs" Condition="!Exists('Lang\Resources.Designer.cs')" />` — 防止 Designer.cs 尚未生成时构建失败（运行 `python3 scripts/i18n.py sync` 创建）
- `<ApplicationManifest>app.manifest</ApplicationManifest>` — Windows 上的 DPI 感知

**应用启动序列**：
1. `StartupGuard.CheckEnvironment()` — 验证 .NET 运行时 >= 10 和支持的操作系统（Windows 10+、macOS 12+、Linux 任意）。不满足时显示警告对话框并退出。
2. `App.Initialize()` — `ApplyLanguageFromSettings()` 设置 `CurrentUICulture` + `Resources.Culture`，然后 `AvaloniaXamlLoader.Load(this)`（语言必须在 XAML 加载前设置，以便 `{x:Static}` 正确解析）
3. `OnFrameworkInitializationCompleted` — 从 DI 解析 `MainShellViewModel`/`MainWindow`，绑定 DataContext
4. 使用 MainWindow 调用 `IFileService.SetTopLevel()` 和 `IDialogService.SetTopLevel()`
5. 初始化 `ViewModelBase.Dialog`（静态）和 `ViewModelBase` 日志记录器
6. 启动 `WatchdogService`，使用 3 秒 DispatcherTimer 心跳
7. 附加 `ChineseInputNormalizer` 行为（全角数字/符号 → 半角）
8. `RestoreSettingsAsync()` — 恢复主题、窗口位置/大小（语言已在步骤 1 中应用）

## 关键模式

### CommunityToolkit.Mvvm 源代码生成器
- 私有字段上的 `[ObservableProperty]` 生成公共属性，并附带 `On<PropertyName>Changed` 分部方法钩子
- 方法上的 `[RelayCommand]` 生成 `ICommand` 属性
- `[NotifyPropertyChangedFor(nameof(OtherProp))]` 为依赖属性触发变更通知
- 所有 ViewModel 继承 `ViewModelBase`（扩展 `ObservableObject`）

### ViewModelBase.SafeExecuteAsync

两个重载：

```csharp
// 简单版：try-catch，自动弹出错误对话框。errorTitle 默认为本地化的 Resources.Common_OperationFailed
protected async Task<bool> SafeExecuteAsync(Func<Task> action, string? errorTitle = null)

// 带超时版：通过 CancellationTokenSource 自动取消，超时时显示超时对话框
protected async Task<bool> SafeExecuteAsync(Func<CancellationToken, Task> action, TimeSpan timeout, string? errorTitle = null)
```

超时重载在超时后中止操作 — 对于长时间运行的导出或导入操作应优先使用。保持超时时间远低于 WatchdogService 阈值（45 秒）。

`ViewModelBase` 使用静态 `IDialogService` — `App.axaml.cs` 必须在任何 ViewModel 使用 `SafeExecuteAsync` 之前调用 `ViewModelBase.InitializeDialogService(dialog)`。**如果新增窗口或隔离测试 ViewModel，必须先初始化 Dialog。**

### ViewModelBase.CanLeaveAsync
```csharp
public virtual Task<bool> CanLeaveAsync()
```
由 `NavigationService` 在导航离开前调用。重写以提示用户未保存的更改。

### 主题与自定义资源
- **主题字典**：App.axaml 定义 `ResourceDictionary.ThemeDictionaries`，包含 Light/Dark 变体，涵盖侧栏颜色、语义颜色（Success/Warning/Error/Info）、表面颜色和阴影。
- **画刷**：SolidColorBrush 资源通过 `DynamicResource` 引用主题颜色。始终使用这些画刷资源（如 `{StaticResource SuccessBrush}`）— 切勿硬编码十六进制颜色。
- **样式包含**：`Typography.axaml`、`Spacing.axaml`、`Colors.axaml` 在 FluentTheme 之后包含。
- **字体**：全局 `Window` 样式设置 `FontFamily` 为 `Inter,Microsoft YaHei UI,PingFang SC,Noto Sans CJK SC,WenQuanYi Micro Hei,sans-serif` 以支持 CJK。
- **BoxShadows**：`CardShadowNone`、`CardShadowLarge`、`CardShadowSmall` 定义为 `BoxShadows` 资源。

### UI 服务（`SeatFlow.Presentation.Avalonia/Services/`）
- **INavigationService** — 通过 `PageKey` 枚举切换页面。`NavigateTo()` 同步执行，`NavigateToAsync()` 先运行 `CanLeaveAsync()`。
- **IDialogService** — 显示错误/信息对话框。使用前需要 `SetTopLevel(TopLevel)`。
- **IFileService** — 文件打开/保存选择器。同样需要 `SetTopLevel()`。
- **WatchdogService** — 通过后台轮询循环检测 UI 线程卡顿。默认超时 45 秒；超时后将线程/进程诊断信息写入 `err_<timestamp>.log` 并强制退出应用。UI 线程必须定期调用 `Ping()`（`App.axaml.cs` 中的 `DispatcherTimer` 负责此操作）。

### 工具窗口
- `InputWindow` — 单行文本输入的模态对话框（返回输入的字符串）
- `DialogWindow` — 通用模态内容宿主，带标题栏和关闭按钮

### 行为（`SeatFlow.Presentation.Avalonia/Behaviors/`）
- `CanvasZoomPan` — 基于 Canvas 的预览的平移和缩放。**拖放座位时通过 NaN 哨兵机制跳过平移**（详见 `docs/DragDrop.md`）
- `ZoomOnScroll` — Ctrl+滚轮缩放
- `ChineseInputNormalizer` — 文本输入时将全角数字/符号转换为半角

### 新增页面
1. 在 `INavigationService.cs` 的 `PageKey` 枚举中添加新值
2. 创建 `ViewModels/NewThingViewModel.cs`（继承 `ViewModelBase`）
3. 创建 `Views/NewThingView.axaml` + `.axaml.cs`（设置 `x:DataType="vm:NewThingViewModel"`）
4. 在 `Program.cs` 中注册两者：`services.AddSingleton<NewThingViewModel>()`
5. 在 `MainWindow.axaml` 侧栏添加导航按钮

### Axaml 绑定
- 始终在根元素上使用 `x:DataType` 以启用编译绑定
- 图标：`<fic:FluentIcon Icon="{x:Static ficEnum:Icon.{Name}}" FontSize="18"/>`（参见 `SeatFlow.Presentation.Avalonia/docs/Fluent_Icons.md`）
- 转换器：`BoolConverters.cs`（Negate、TrueWhenNull 等）和 `ValueConverters.cs`

### 侧栏
- 宽度：展开 140px / 折叠 64px（由 `MainShellViewModel.SidebarWidth` 控制）
- 窗口宽度 < 750px 时自动折叠
- `MainShellViewModel.ToggleSidebar()` 命令用于手动切换

### i18n / 本地化（`Lang/`）

使用标准 .NET `.resx` 资源文件，位于 `SeatFlow.Presentation.Avalonia/Lang/`：
- `Resources.resx` — 中性语言（zh-CN），约 700 个键
- `Resources.en-US.resx` — 英文卫星程序集
- `Resources.Designer.cs` — 手动维护的强类型访问器类（Visual Studio 的 `PublicResXFileCodeGenerator` 与 `dotnet build` 不兼容）

**添加新语言**：创建 `Resources.xx-XX.resx` 并填入翻译，无需代码更改。

**在 XAML 中使用**（仅属性语法 — 元素内容无法解析）：
```xml
<TextBlock Text="{x:Static lang:Resources.Settings_Title}" />
<Button Content="{x:Static lang:Resources.Common_OK}" />
```
命名空间：`xmlns:lang="using:SeatFlow.Presentation.Avalonia.Lang"`

**在 C# 中使用**：
```csharp
StatusMessage = Resources.Settings_Saved;
StatusMessage = string.Format(Resources.Snapshot_VenuesLoadedFmt, count);
```
**重要**：在继承自 `Window` 的类（DialogWindow、InputWindow）中，`Resources` 解析为 `Window.Resources`（IResourceDictionary）。在这些文件中使用完全限定的 `Lang.Resources.xxx`。

**键命名**：`{Page}_{Element}` 采用 PascalCase，如 `Settings_Title`、`Nav_Home`、`Common_OK`。格式字符串使用 `{0}` 占位符。

**管理资源**：使用 `python3 scripts/i18n.py` 进行 .resx 键的所有 CRUD 操作 — 它保持三个文件（zh-CN .resx、en-US .resx、Designer.cs）同步。完整使用指南参见 `scripts/ToolsCollection.md`。常用命令：
```bash
python3 scripts/i18n.py list                     # 列出所有键
python3 scripts/i18n.py list --missing-en        # 查找未翻译的键
python3 scripts/i18n.py check                    # 验证一致性
python3 scripts/i18n.py add KEY --zh "中" --en "EN"  # 添加键
python3 scripts/i18n.py sync                     # 从 .resx 重新生成 Designer.cs
```
备份自动创建在 `Lang/.backup/` 中（已 gitignore）。

**语言切换**：`App.ApplyLanguageFromSettings()`（在 `Initialize()` 中 XAML 加载前调用）。设置 `CultureInfo.CurrentUICulture` 和 `Resources.Culture`。

### 脚本工具（`scripts/`）

除上述 `scripts/i18n.py` 外，还有以下脚本 — 均在 `scripts/ToolsCollection.md` 中有完整文档：

| 脚本 | 用途 |
|--------|------|
| `scripts/i18n.py` | i18n .resx 资源 CRUD + Designer.cs 同步（45 个单元测试） |
| `scripts/version.py` | 跨 15+ 个文件的统一版本管理 — App 版本、文件格式版本、策略清单版本、引导配置版本。子命令：`show`、`check`、`bump-app`、`bump-file`、`bump-strategy`、`bump-onboarding`、`sync`（26 个单元测试） |
| `scripts/build/publish.sh` / `scripts/build/publish.ps1` | 多平台 TUI/CLI 发布（独立 + 框架依赖，裁剪，AOT，SHA256 表格） |
| `scripts/build/clean.sh` / `scripts/build/clean.ps1` | 递归清理 bin/obj |
| `scripts/update_version_info.py` | MSBuild 辅助 — 读取 version.json + git → 编译时生成 VersionInfo.g.cs |
| `scripts/release/release.py` | 发布编排器 — 构建→打包→OSS 上传→GitHub Release |

单元测试位于 `scripts/tests/`。

### 关于页面数据（`Data/about.json`）

多语言 JSON，顶层为区域键：
```json
{ "zh-CN": { "description": "...", "dependencies": [...] },
  "en-US": { "description": "...", "dependencies": [...] } }
```
`AboutViewModel.LoadAboutData()` 根据 `CultureInfo.CurrentUICulture.Name` 选择，回退到 `"zh-CN"`。

### 对话框窗口

- `DialogWindow` — 确认/错误/警告/信息/多选项对话框。按钮使用 `Content="{x:Static}"` 属性语法。代码后置仅控制可见性和 MultiOption 自定义文本。**切勿**在 `<Button>...</Button>` 内使用 `{x:Static}` 作为元素内容。
- `InputWindow` — 文本输入对话框。按钮模式相同。

## 文件版本与迁移

所有持久化 JSON 文件均携带 `version` 字段。当前版本定义在 `SeatFlow.Infrastructure/Migration/file_versions.json`（嵌入资源，编译到程序集中）。`FileVersionInfo.GetCurrentVersion(fileType)` 在运行时读取最新版本。

| 文件类型 | 版本 | 位置 | 包装类 |
|---|---|---|---|
| Venue | `1.1` | `{data}/Venues/*.venue.json` | `VenueFile` |
| Roster | `1.1` | `{data}/Rosters/*.roster.json` | `RosterFile` |
| Snapshot | `1.0` | `{data}/Assignments/{venueId}/{date}/*.json` | `SeatingSnapshot` |
| VenueInfo | `1.0` | `{data}/Assignments/{venueId}/_venue.json` | `VenueSnapshotInfo` |
| AppSettings | `1.1` | `{data}/AppSettings.json` | `AppSettings` |
| StrategyConfig | `1.0` | `{data}/StrategyConfig/{strategyId}.config.json` | `StrategyConfig` |
| StrategyDatasetConfig | `1.0` | `{data}/StrategyConfig/{strategyId}/*.config.json` | `StrategyDatasetConfig` |

### 迁移管道

加载时，每个仓库将文件读取为 `JsonNode`，调用 `FileMigrationService.Migrate(fileType, node, fileVersion, targetVersion)`，然后反序列化。迁移是**仅向前**的 — 不支持版本回滚。服务查找已注册的 `IFileMigrator` 实现，通过匹配 `FromVersion`/`ToVersion` 链式执行。

### 何时需要迁移器 vs 依赖模型默认值

**不需要迁移器**（模型默认值即可）：
- 新增属性/配置节，且所有字段都有合理的默认值（`bool` → `false`、`int` → `0`、`string` → `""`、引用类型 → `new()`）
- 旧 JSON 缺少该节点时，`System.Text.Json` 反序列化会保留属性的默认值
- 示例：新增 `TelemetryConfig` 到 `AppSettings`，默认 `Enabled = false`，旧文件无需迁移

**必须写迁移器**：
- 数据格式变化：如 `VenueMigrators 1.0→1.1` 将座位从列主序重排为行主序
- 字段语义变化：同一个 JSON key 的含义改变，需要重新计算/转换
- 字段重命名且不能丢失旧数据：需要将旧 key 的值复制到新 key
- 数据结构重组：嵌套层级变化（如 `SeatSetsMigrators`）

**原则**：只有旧数据**不转换就会出错或丢失信息**时才写迁移器。能用默认值兜底就不要写。

### 添加迁移

1. 在 `Migration/Migrators/{FileType}Migrators.cs` 中添加嵌套类（每种文件类型一个文件）：
   ```csharp
   public static class VenueMigrators
   {
       public sealed class Step_1_0_to_1_1 : IFileMigrator
       {
           public string FileType => "venue";
           public string FromVersion => "1.0";
           public string ToVersion => "1.1";
           public JsonNode Migrate(JsonNode root) { ... }
       }
   }
   ```
2. 在 `ServiceCollectionExtensions.cs` 中注册：`services.AddSingleton<IFileMigrator, VenueMigrators.Step_1_0_to_1_1>()`
3. 在 `file_versions.json` 中提升版本号
4. 在 Core 中更新模型的默认 `Version` 属性
5. 在 `Infrastructure.Tests/Migration/{FileType}MigratorsTests.cs` 中添加测试

### 已有迁移

- `VenueMigrators.Step_1_0_to_1_1` — 将 Grid 布局座位从列主序重排为行主序（先按 `Row` 排序，再按 `Column` 排序）

### JSON 字段约定

- 序列化使用 `JsonNamingPolicy.CamelCase` — JSON 中所有字段为小写（`row`、`column`、`layoutTypeString`、`logicalGroup`）
- `ClassroomLayoutDefinition.LayoutType` 序列化为**同时**包含数字（`layoutType`：0=Grid、1=Polar、2=Freeform）和字符串（`layoutTypeString`："Grid"/"Polar"/"Freeform"）。迁移器应读取 `layoutTypeString` 以保持清晰。
- `Seat` 多态序列化使用 `SeatJsonConverter`，在每个座位对象旁写入 `Type` 鉴别器（大写 T，字符串："Grid"/"Polar"/"Freeform"）。`type` 字段（小写，`SeatType` 枚举的 camelCase）是独立的整数。
- `SeatingSnapshot.Version` 和 `VenueSnapshotInfo.Version` 在此迁移轮次中添加 — 没有该字段的旧快照默认为 `"1.0"`。
- `VenueFile.ContentHash` 和 `RosterFile.ContentHash` — 保存时计算的 SHA256 哈希（ContentHash 为 null → 序列化 → 哈希 → 设置 → 重新序列化）。学生数据集哈希排除 `importedAt`/`originalFileName`（不稳定的时间戳）。

### Grid 座位排序

`GridLayoutBuilder.BuildGrid` 以**行主序**创建座位（外层循环：行，内层循环：列）。这确保 `RandomFillStrategy` 逐行（从左到右，从上到下）填充座位。对于不规则的 `ColumnRowCounts` 网格，`maxRows = ColumnRowCounts.Max()`，每列检查 `r <= rowsForCol`。

### 快照会场布局嵌入

快照在创建时将完整的 `ClassroomLayoutDefinition`（通过 `SeatJsonConverter` JSON 序列化）存储在 `Metadata["venueLayout"]` 中。快照预览（`BuildPreviewAsync`）首先读取此嵌入布局；没有嵌入布局的旧快照回退到加载会场文件。这确保快照是自包含的 — 编辑或删除会场文件不会破坏现有快照预览。

### 快照完整性检测

`BuildPreviewAsync` 比较 `Metadata["venueHash"]` 与当前会场文件的 `ContentHash`：
- **会场已删除** → 红色警告栏"会场已删除，无法预览"，回滚按钮禁用
- **会场已更改** → 黄色警告栏"会场布局已更改，回滚可能失败"
- **数据已更改**（学生 ID 在当前数据集中缺失）→ 黄色栏"数据已更改"，受影响座位高亮为黄色

`RollbackAsync` 在回滚前检查会场完整性：
- 会场已删除 → 对话框 → 从快照的 `venueLayout` 恢复会场
- 会场已更改 → 对话框 → 将快照中的会场作为新会场导入

### 快照轮转

`AppSettings.MaxSnapshotsPerVenue`（默认 30，0=无限制）。保存快照后，`RotateSnapshotsAsync` 在数量超出限制时删除最旧的快照。侧栏状态栏通过 `SnapshotQuotaDisplay` 显示 `"{n}/{max}"`。

### 会场编辑与座位 ID 保留

`VenueConfigurationViewModel` 在编辑间保留座位 ID：加载时记录 `(Row, Column) → Id` 和 `(Ring, Angle) → Id` 映射；保存时，新建的座位按位置匹配旧座位并重用其 ID。这防止会场编辑后快照 `SeatAssignments` 损坏。

### 学生数据集重命名

`RenameStudentDatasetAsync` 原地重命名（仅更新 `RosterFile.Description`，保留 ID）。此前会删除旧文件并以新 ID 创建新文件。

### 页面导航可见性管理

JSON 配置文件 `Data/page_navigation.json`（嵌入资源）控制哪些导航页面启用。格式：

```json
{ "version": "1.0", "pages": { "Home": true, "PluginManagement": false, ... } }
```

键 = `PageKey` 枚举值名称。`MainShellViewModel.LoadPageNav()` 通过 `Assembly.GetManifestResourceStream` 加载（与 `about.json` 模式相同）。对于每个禁用的页面，在 `MainShellViewModel` 中添加两个属性：

```csharp
public double PluginManagementOpacity => IsPageEnabled("PluginManagement") ? 1.0 : 0.4;
public string? PluginManagementDisabledTip => IsPageEnabled("PluginManagement") ? null : Resources.Nav_PluginDisabled;
```

使用 `Opacity`（而非 `IsEnabled` — 禁用控件会在 Avalonia 中隐藏 ToolTip）和 `ToolTip.Tip` 绑定到侧栏按钮。`NavigateAsync` 已检查 `IsPageEnabled()`，对禁用页面直接返回。禁用消息以 `Nav_{PageName}Disabled` 键模式放入 `.resx`。

### 引导系统

完全数据驱动，通过 `Data/onboarding_config.json`（v3.2）配置。完整设计详见 `docs/ONBOARDING_GUIDE.md`。示例数据注入决策详见 `docs/adr/ADR-008-onboarding-demo-data-injection.md`。

**两种引导类型：**
- **启动引导（`startupPhases`）** — 首次启动时的 20 步完整工作流：Home→MemberManagement(ExportTemplate→ImportButton)→[自动 Home 往返]→MemberManagement(UpdateFromFileButton)→VenueConfiguration→StrategyConfiguration（含策略冲突提示居中步骤）→SeatingArrangement→SnapshotHistory→Closing
- **页面引导（`pageGuides`）** — 首次访问页面时触发（FreeformManagement、PluginManagement）。记录在 `AppSettings.CompletedPageGuides` 中。

**声明式示例数据注入 (v3.2):** `OnboardingPhaseDefinition.SeedData`（bool，默认 `false`）控制跨阶段导航时是否注入演示数据。原运行状态标志 `_memberManagementDataSeeded` 已删除，改为 JSON 声明式控制。MemberManagement 分两次进入（中间隔 Home 过渡阶段），第一次不注入（ImportButton 可见），第二次注入（UpdateFromFileButton 可见）。`ClearPageData` 使用 `_memberManagementDemoInjected` 静态标志判断是否实际注入过。

**关键类：** `IOnboardingService` / `OnboardingService`（同时实现 `IOnboardingService` 和 `IOnboardingStarter`），`OnboardingPhaseDefinition` / `OnboardingStepDefinition`（模型）。`MainWindow.axaml.cs` 有 5 个薄事件包装器 — 所有逻辑在 `OnboardingService` 中。

**导航顺序（Phase 1 修复）：** `HandleStepOpening` 必须在解析目标控件的 x:Name **之前**导航到新页面。原始顺序（解析 → 导航）导致 `ContentPresenter.Child` 引用旧页面，使每个阶段第一步的 NameScope 查找失败。配合 `OnboardingNavigateTo` 的同步 `CurrentViewModel` 设置（通过 `IsOnboardingActive` 守卫跳过 `RunTransitionAsync` 动画），目标在首次尝试时即可正确解析。

**JSON 驱动代码：** `BuildStepsFromDefs()` 通过纯粹 `ResourceManager.GetString(step.titleKey)` 将 `OnboardingStepDefinition` 转换为 `GuideStepOption`。C# 中零键名推断。目标解析延迟到 Guide 的 `StepOpening` 事件。每个步骤显式声明 `titleKey`、`descKey`、`target`、`placement`、`showMask`、`showArrow`。

**添加/修改引导步骤：** 编辑 `onboarding_config.json` + 添加 resx 键 + 更新 `Designer.cs`。无需更改 C#。如果目标控件缺少 `x:Name`，将其添加到 `.axaml` 文件。

**示例数据注入（v3.2）：** `OnboardingService.SeedPageData()` 在启动引导阶段转换期间将纯内存示例数据注入页面 ViewModel，由 `OnboardingPhaseDefinition.SeedData`（JSON bool，默认 `false`）声明式控制。引导完成时由 `ClearPageData()` 清除（通过 `_memberManagementDemoInjected` 静态标志守卫——仅在实际注入过时才清理）。对于构造函数中有 fire-and-forget 异步初始化的 ViewModel（SeatingArrangement、VenueConfiguration、StrategyConfiguration），注入通过 `Dispatcher.UIThread.Post(..., DispatcherPriority.Background)` 延迟到异步 `LoadXxxAsync()` 覆盖之后运行。仅使用 Core 模型 + ViewModel 公共 API — 无 Infrastructure 层或磁盘 I/O 依赖。参见 ADR-008。

**窗口状态同步（v3.1）：** `MainWindow` 订阅 `Activated`/`Deactivated` 事件 → 转发给 `OnboardingService.HandleWindowActivated()`/`HandleWindowDeactivated()`。失活时（最小化/Alt+Tab）：`_isWindowObscured=true`，`Guide.Close()` 静默关闭 Popup（无确认对话框，无完成标记）。激活时（恢复）：从保留的 `CurrentIndex` 重新打开 Guide。防止 3 个 Popup（`ShouldUseOverlayLayer=False`，原生 OS 窗口）残留为孤立窗口。

**DI：** `services.AddSingleton<IOnboardingService, OnboardingService>()`，`IOnboardingStarter` 桥接到同一实例。`MainShellViewModel` 注入 `IOnboardingService` 以在导航后触发页面引导。

### 成员管理数据集流程

**点击即加载**：`OnSelectedDatasetChanged` 通过 `SwitchToDatasetAsync()` 自动加载数据集。无需单独的"加载"按钮。

**脏跟踪**：使用 JSON 序列化快照比较。`_originalStudentsJson` 存储加载/保存后的状态；`IsDirty` 将当前 `SerializeStudents()` 与之比较。`MarkClean()` / `MarkDirty()` 管理快照。

```csharp
private bool IsDirty =>
    IsNewStudentDirty ||
    (_originalStudentsJson != null && SerializeStudents() != _originalStudentsJson);
```

**NewStudent 脏检查**：`IsNewStudentDirty` 检查底部"添加行"是否有任何字段被填写（姓名、身高、性别或前排标志）。必须包含在 `IsDirty` 中，以便未保存的新行数据触发切换数据集对话框。

**切换数据集流程**：`SwitchToDatasetAsync(target)` → 如有脏数据 → 3 按钮对话框（保存 / 放弃 / 取消）。取消通过 `_suppressDatasetLoad` 守卫将 `SelectedDataset` 回退到 `_previousDataset`。保存或放弃后，`NewStudent` 被重置以防止数据在数据集间泄漏。

**保存流程**：`SaveAsync` 首先检查 `IsNewStudentDirty` → 如为 true，显示"放弃并保存" / "取消"对话框。如果 `CurrentDatasetId` 为 null（导入的数据），委托给 `RenameSaveAsync`（另存为）。否则调用 `SaveInternalAsync`，删除旧文件 + 保存新文件，无需确认对话框。成功后两者均调用 `MarkClean()`。

**验证**：`ValidateStudents()` 跳过完全空白的行（名称为空 AND 身高为空 AND 性别为空 AND needsFrontRow 为 false）。部分填写但名称为空的行仍会标记。

### 关于页面版本

版本来自 `about.json` → `AboutData.Version` 字段，附加 git 提交哈希：

```
Version = $"{data.Version ?? "1.0.0"}+{GitCommit.Hash}"
```

`GitCommit.Hash` 是自动生成的 `GitCommit.g.cs` 中的 `const string`，由 MSBuild 目标（`GenerateGitCommit`）在每次构建前运行 `git rev-parse --short HEAD` 生成。git 不可用时回退到 `"unknown-commit-id"`。生成的文件位于 `$(IntermediateOutputPath)Generated\` 中，**不**提交。

### 确定性构建

csproj 设置确保跨时间/机器的可重现输出：

```xml
<Deterministic>true</Deterministic>
<PathMap>$([System.IO.Path]::GetFullPath('$(MSBuildProjectDirectory)'))=./</PathMap>
```

相同源码 → 相同 DLL 哈希，无论构建时间或绝对路径。

### VenueConfiguration：NewVenue 竞态条件

`NewVenue()` 必须在调用 `ResetParameters()` 之前取消前一个选中会场的任何进行中的 `SelectVenueAsync`。否则正在运行的异步加载可能用旧会场数据覆盖重置参数。模式：`_selectVenueCts?.Cancel()` 放在 `NewVenue()` 顶部，`SelectVenueAsync` 内部在设置 VM 状态前检查 `ct.IsCancellationRequested`。

### DockPanel 子元素顺序

在 Avalonia 的 `DockPanel` 中，`LastChildFill="True"`（默认）意味着**最后**一个子元素填充剩余空间。如果最后一个子元素有 `DockPanel.Dock="..."`，则前一个未停靠的子元素填充。始终将 `Dock` 子元素放在填充子元素（通常是 `ScrollViewer` 或 `ListBox`）**之前**。

## 辅助工具

所有 Python 脚本从 `scripts/` 目录执行，自动检测项目根目录。Shell 脚本需在 `scripts/` 目录下运行。完整文档见 `scripts/ToolsCollection.md`。

### i18n 资源管理 (`scripts/i18n.py`)

```bash
# 列出与搜索
python3 scripts/i18n.py list                          # 列出所有 key
python3 scripts/i18n.py list --missing-en             # 查找未翻译的 key
python3 scripts/i18n.py list --pattern "Export"       # 正则搜索 key
python3 scripts/i18n.py list --format-strings         # 列出含占位符的 key

# 校验
python3 scripts/i18n.py check                         # 一致性校验
python3 scripts/i18n.py check --fix                   # 自动修复排序问题

# 添加/修改/重命名/删除
python3 scripts/i18n.py add Settings_NewKey --zh "中文" --en "English"
python3 scripts/i18n.py modify Settings_Title --zh "新标题"
python3 scripts/i18n.py rename Old_Key New_Key
python3 scripts/i18n.py delete Obsolete_Key

# 同步 Designer.cs
python3 scripts/i18n.py sync                          # 从 .resx 重新生成

# 批量翻译工作流
python3 scripts/i18n.py export -o translations.csv    # 导出为 CSV（在 Excel 中编辑）
python3 scripts/i18n.py import translations.csv --dry-run  # 预览导入
python3 scripts/i18n.py import translations.csv --force    # 执行导入
```

### 版本号管理 (`scripts/version.py`)

```bash
# 查看与校验
python3 scripts/version.py show                       # 显示全部版本号概览
python3 scripts/version.py check                      # 校验 15+ 处版本一致性

# 调整版本（--dry-run 预览，--force 执行）
python3 scripts/version.py bump-app patch --dry-run   # App 补丁版本 +1
python3 scripts/version.py bump-app minor --force     # App 次版本 +1
python3 scripts/version.py bump-file roster --set 1.2 --force   # 更新 roster 文件格式版本（自动同步 Model 类）
python3 scripts/version.py bump-strategy FixedSeat --set 1.1.0 --force
python3 scripts/version.py bump-onboarding --set 3.1 --force

# 从 file_versions.json 同步所有 Model 类默认值
python3 scripts/version.py sync --force
```

**重要**：`bump-file` 自动同步 `file_versions.json` → 7 个 Model C# 类 → `JsonStudentWriter.cs`，无需手动修改。

### 发布 (`scripts/build/publish.sh` / `scripts/build/publish.ps1`)

```bash
cd scripts/build
./publish.sh                    # TUI 交互模式（多选平台/选项）

# CLI 模式参数: <类型> <配置> <版本> <选项>...
# 类型: both | sc | fde     (全部 / 独立 / 框架依赖)
# 选项: clean aot trim
./publish.sh both Release "" "1.2.1" clean aot   # 全平台独立+框架依赖，裁剪+AOT，版本 1.2.1
```

### 清理 (`scripts/build/clean.sh` / `scripts/build/clean.ps1`)

```bash
cd scripts/build
./clean.sh          # 确认后删除所有 bin/ 和 obj/
./clean.sh -n       # 仅预览（dry-run）
./clean.sh -f       # 直接删除（跳过确认）
```

### 脚本测试

```bash
cd scripts
python3 -m pytest tests/test_i18n.py -v      # i18n 单元测试（45 个）
python3 -m pytest tests/test_version.py -v   # 版本管理单元测试（26 个）
python3 -m pytest tests/ -v                  # 全部脚本测试
```

## 文档

> **重要**：`docs/CLAUDE.md` 是根 CLAUDE.md 的人类可读中文副本。**每次修改根 CLAUDE.md 后必须同步更新本文档**（参见 `docs/INDEX.md` 联动规则）。

- `docs/INDEX.md` — 文档地图与交叉引用（修改文档前先阅读；包含文档职责矩阵和变更场景联动表）
- `ARCHITECTURE.md` — 项目目标与架构设计
- `docs/Phases.md` — 实现阶段与详细规划
- `CONTRIBUTING.md` — 开发环境、编码约定、版本迁移流程
- `CHANGELOG.md` — 版本变更日志（Keep a Changelog 格式）
- `docs/ONBOARDING_GUIDE.md` — 引导系统设计文档（JSON 驱动，启动引导 + 页面引导）
- `docs/StrategyDataResilience.md` — 策略数据持久化与容错分析
- `docs/adr/` — 架构决策记录（ADR-001 ~ ADR-008）。重点：ADR-002（MVVM + 计划使用 IMessenger 进行跨 ViewModel 通信）、ADR-006（策略管道 fill-in-order）
- `SeatFlow.Presentation.Avalonia/docs/Design_Spec.md` — FluentUI 设计规范（色板、字体、间距、图标）
- `SeatFlow.Presentation.Avalonia/docs/DragDrop.md` — Avalonia 12 拖放模式、踩坑记录、CanvasZoomPan 交互
- `SeatFlow.Presentation.Avalonia/docs/Fluent_Icons.md` — 所有已使用的 FluentUI 图标名称
- `SeatFlow.Plugins.Sdk/docs/README.md` — 插件 SDK 开发指南（接口、两层清单格式、打包）
- `scripts/ToolsCollection.md` — `i18n.py` 和 `version.py` 脚本的完整参考
