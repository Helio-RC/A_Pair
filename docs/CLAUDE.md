# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Chat & Coding Language

你需要使用中文来进行对话和思考、编写注释。

## Development Environment

在执行需要 GUI 的操作前，先确认当前是否处于无头环境（检查 `DISPLAY` / `WAYLAND_DISPLAY` 等环境变量），以及是否安装了 .NET 10 SDK。avdt（Avalonia DevTools）仅在桌面环境下可用。

##  Build & Test

```bash
dotnet build                    # Build all 9 projects (uses .slnx, requires .NET 10 SDK)
dotnet test                     # Run all tests (xUnit v3, Microsoft.Testing.Platform)
dotnet test --filter "FullyQualifiedName~TestName"  # Run a single test
dotnet run --project SeatFlow.Presentation.Avalonia   # Launch the desktop app
```

**Test stack**: xUnit v3 + FluentAssertions + NSubstitute. Tests are in 3 projects: `*.Core.Tests`, `*.Application.Tests`, `*.Infrastructure.Tests`. Each has `<ImplicitUsings>enable</ImplicitUsings>` (provides `System`, `System.Collections.Generic`, `System.Linq`, `System.Threading.Tasks`). Project-specific global usings are in `Usings.cs` (or `Using.cs` in Application.Tests).

**No `Directory.Build.props` or `Directory.Packages.props`** — package versions are managed directly in each `.csproj`.

**dotnet tools**: `avaloniaui.developertools` (avdt) is installed in repo-root `dotnet-tools.json`. 使用前先确认当前环境是否有桌面显示支持，若无头则跳过 avdt。构建时无需此工具。

## Architecture

SeatFlow is a .NET 10 cross-platform desktop seating arrangement system using Avalonia UI 12 (MVVM) + CommunityToolkit.Mvvm 8.4. The solution file is `SeatFlow.slnx` (the new XML-based format).

**Layers (bottom-up)**:
- **Core** — Domain entities (`Student`, `Seat`, `ClassroomLayoutDefinition`, `SeatingWorkspace`, `SeatingPlan`), strategy interfaces (`ISeatingStrategy`, `IDependentSeatingStrategy`) + seven built-in implementations (4 independent, 3 dependent), capability system (`Capability.cs` — constants + `IFixedSeatCapability`), domain services in `DomainServices/` (`ObstacleProcessor`, `SeatGeometryHelper`, `StrategyManifestProvider`, `SeatAdjacencyHelper`), utilities in `Utilities/` (`CircularHistory<T>` — ring buffer with capacity=10 on `Student.RecentSeatHistory`, `AttributeBag`), workspace in `Workspace/` (`SeatingWorkspace`), and data provider interfaces (`IStudentProvider`, `IVenueRepository`, etc.)
- **Contracts** — Cross-layer interface for plugins (`IPluginSeatingStrategy`)
- **Infrastructure** — File I/O (`CsvStudentProvider`, `XlsxStudentProvider` (uses **EPPlus 8**), `JsonStudentProvider`, and the composite `CompositeStudentProvider` registered as the primary `IStudentProvider`), exporters (`ExcelSeatingExporter`, `CsvSeatingExporter`, `PdfSeatingExporter`, `ImageSeatingExporter`), layout builders (`GridLayoutBuilder`, `PolarLayoutBuilder`, `FreeformLayoutBuilder`), repositories (`JsonVenueRepository`, `JsonAppSettingsRepository`, `StrategyConfigFileRepository`, `SeatingSnapshotRepository`, `JsonStudentDatasetRepository`), writers (`JsonStudentWriter`, `CsvStudentWriter`, `XlsxStudentWriter`), serialization, migration system (`FileMigrationService`, `IFileMigrator`)
- **Application** — `IApplicationFacade` (UI's single entry point), `StrategyExecutionPipeline`, command pattern (`IUndoableCommand` / `CommandHistory`), plugin manager (`PluginManager`, `PluginLoadContext`), script adapters (Lua/C#), DI registration
- **Plugins.Sdk** — Lightweight assembly for external plugin authors
- **Presentation.Avalonia** — Avalonia 12 desktop app, MVVM with CommunityToolkit.Mvvm

**Logging**: Uses **Serilog 4** + `Microsoft.Extensions.Logging.ILogger<T>` throughout the Application layer. Sinks to file via `Serilog.Sinks.File` with a custom output template (`[Level] [SourceContext]`). Supports per-module log level overrides via `CategoryOverrides` in AppSettings.json. Defaults to `Information` in production; auto-downgrades to `Debug` when a debugger is attached (`Debugger.IsAttached`). Full documentation: `docs/LOGGING.md`.

**DI**: `ServiceCollectionExtensions.AddSeatFlowApplication(snapshotBasePath, pluginsPath)` in Application layer registers all services (strategies, exporters, providers, repositories, plugin manager). In `Program.cs`, the UI layer calls this then adds its own singletons: `INavigationService`, `IFileService`, `IDialogService`, `MainWindow`, `MainShellViewModel`, and all page ViewModels.

**Navigation**: `INavigationService` + `MainShellViewModel` manages 10 pages via `PageKey` enum (`Home`, `MemberManagement`, `VenueConfiguration`, `FreeformManagement`, `StrategyConfiguration`, `SeatingArrangement`, `SnapshotHistory`, `PluginManagement`, `Settings`, `About`). `ViewLocator` auto-resolves `XXXViewModel` → `XXXView` by convention: replaces `"ViewModel"` with `"View"` in the type name via reflection.

**Project dependency chain**: `Presentation.Avalonia` → `Application` → `Core` → `Contracts`（Core 已引用 Contracts，`SeatingWorkspace : IPluginWorkspace`）. `Plugins.Sdk` is referenced only by external plugins. `Application` orchestrates; `Infrastructure` implements providers/exporters/layouts/repos; `Core` owns entities, strategy interfaces, and the workspace.

**Strategy pipeline**: Uses a **fill-in-order** model for independent strategies. Dependent strategies execute inside RandomFill's assignment loop via `IDependentSeatingStrategy`. All strategies operate on the same `SeatingWorkspace`. Independent strategies execute in **descending Priority order** (higher = earlier = dibs on empty seats). No "override" semantics; first to fill a seat keeps it. `IsFixed=true` (set by FixedSeat) causes `GetEmptySeats()` to exclude those seats, providing natural protection.

| Order | Strategy | Priority | Type | Role |
|-------|----------|----------|------|------|
| 1st | `FixedSeatStrategy` | 100 | Independent | Locks fixed seats (IsFixed=true), excluded from all later GetEmptySeats |
| 2nd | `FrontRowRotationStrategy` | 50 | Independent | Fills front-row seats from remaining empty non-fixed seats |
| — | `DeskMateStrategy` | 50 (context) | Dependent | Runs inside RandomFill: checks desk-mate groups on each (student,seat) pair, coordinates adjacent assignments (horizontal/same-desk only), requests reroll |
| — | `GenderRestrictedSeatStrategy` | 45 (context) | Dependent | Runs inside RandomFill after DeskMate: checks if target seat has a gender restriction. Redirects mismatched students to matching restricted empty seats; rejects otherwise; forces with warning on reroll exhaustion |
| — | `NoRepeatDeskMateStrategy` | 40 (context) | Dependent | Runs inside RandomFill after DeskMate: checks adjacent occupied seats for past desk-mate repeats. Rejects to trigger reroll; forces with warning on reroll exhaustion |
| 3rd | `RandomFillStrategy` | 1 | Independent + Host | Fills remaining seats; hosts dependent strategies in its assignment loop. Constrained students (DeskMate groups) are prioritized first to reduce rerolls. Eviction respects prior-strategy assignments |
| 4th | `DefragStrategy` | 0 | Independent | "扫地僧" — after all strategies, moves unconstrained students from back rows forward to fill front-row gaps. Cross-column allowed. Skips FixedSeat and DeskMate group students. Logs effectiveness warning (may invalidate prior strategy results) |

Conflict resolution = Priority number (first-come-first-served). Dependent strategies have their own internal priority ordering within RandomFill's context (DeskMate 50 → GenderRestrictedSeat 45 → NoRepeatDeskMate 40). Defrag (0) runs last and may partially invalidate prior strategy results — see its effectiveness warning. Handled assignments still run remaining dependents for inspection/warnings. See `docs/adr/ADR-006-strategy-pipeline-fill-in-order.md`.

**Plugin system (ADR-012)**: Plugins are **first-class strategies** — `IPluginSeatingStrategy` is executed directly by `StrategyExecutionPipeline` (no adapter wrappers; deleted `PluginStrategyAdapter`/`LuaScriptPluginAdapter`/`CSharpScriptPluginAdapter`). Script strategies (`LuaScriptStrategy`/`CSharpScriptStrategy`) implement the interface directly with the manifest id injected as `Id` (fixes config-routing). Package format **v2**: `plugins-manifest.json` uses `plugins[]` with `kind` (`"strategy"` implemented; `"data-provider"`/`"exporter"` reserved; v1 not supported). Plugin dependent strategies (`isIndependent: false`) implement `IPluginDependentSeatingStrategy` (Contracts) and are bridged into RandomFill by `PluginDependentAdapter` (Core). ALC lifecycle follows the official collectible-unloading pattern: dictionaries cleared **before** `Unload()`, WeakReference probing loop, and **compacting forced GC** (`GC.Collect(2, Forced, blocking, compacting)`) — plain `GC.Collect()` does NOT reclaim collectible ALCs; unload tests must run in `NoInlining` sync isolation (async method state-machine fields pin references). `.ap-plugin` install: drag-drop or Install button on PluginManagement page. Script sandbox: Lua disables `io/os/package/debug/require` + overrides `import`; timeouts CANNOT forcibly interrupt scripts (same as Roslyn) — script returns failure and keeps running in background; docs warn to install scripts only from trusted sources. `ValidateZipSafety` consolidated into `Contracts.Utilities.PluginArchiveSafety`. See ADR-012.

**Strategy messaging**: Strategies can report warnings/errors during execution via `workspace.LogWarning(strategyId, displayName, messageKey, args)` and `workspace.LogError(strategyId, displayName, messageKey, args)`. `messageKey` corresponds to a key in the manifest's `messages` dictionary (inline i18n: `{ "zh-CN": "...", "en-US": "..." }`). Messages are collected in `SeatingWorkspace.Messages` (with `StrategyId`, `StrategyDisplayName`, `MessageKey`, and `Args`) and surfaced to the UI sidebar after pipeline execution. Plugin strategies access the same methods through `IPluginWorkspace`.

**Declarative strategy configuration**: All strategy-specific configuration (beyond Priority/IsEnabled) is driven by the manifest JSON files (`SeatFlow.Core/Strategies/Manifests/*.json`). Three top-level fields:

- **`visible`** — (optional, default `true`) Controls whether the strategy participates in the pipeline. Set to `false` to exclude it from both the UI (configuration page, seating sidebar) and execution — the pipeline skips invisible strategies.
- **`isIndependent`** — (optional, default `true`) `true` = independent strategy (executed by external pipeline); `false` = dependent strategy (executed inside RandomFill's assignment loop). DeskMate, GenderRestrictedSeat, and NoRepeatDeskMate are `false`.
- **`manifestVersion`** — (optional, default `"1.0"`) Manifest format version for runtime compatibility checks. Embedded resources don't go through FileMigrationService, so the provider warns if version exceeds max known.
- **`capabilities[]`** — (optional) Strategy capability declarations. Each entry is a capability constant defined in `SeatFlow.Core.Strategies.Capability` (e.g. `"MarkFixedSeat"`). Strategies must declare a capability before calling its corresponding interface method at runtime. Undeclared capability calls are rejected with a logged warning. Currently supported: `MarkFixedSeat` → `IFixedSeatCapability.TryMarkFixed()`. Extensible — add const + interface to `Capability.cs`.
- **`parameters[]`** — strategy-level global params. Each parameter declares a `fieldType` (`NumberInput`, `TextInput`, `ToggleSwitch`, `Dropdown`), a `label` (Dictionary<string,string> for i18n), `defaultValue`, and optional `minValue`/`maxValue`. UI renders these as standard input controls.
- **`codeBlocks[]`** — per-dataset/per-venue config blocks. Each block declares `dataType` (`Student`, `Venue`, `Both`), `displayMode` (`Table`, `ValuePair`), optional `showSeatPosition` (default true, set false for auto-matching strategies like DeskMate), optional `showStudentPicker`/`showVenuePicker` (overrides DataType auto-detection), optional `studentPickerCount` (default 1), optional `seatsPerDeskFromVenue` (set true to read student count from venue's GridLayoutMetadata.SeatsPerDesk), optional `preventDuplicateInRow` (set true to prevent same-row student picker duplicate values — DeskMate), optional `preventDuplicateAcrossRows` (set true to prevent cross-row student picker duplicate values — FixedSeat), and optional `loadTrigger` (default `Both` — both selectors required for exact match; `Any` — fuzzy match on whichever selector has a value). UI renders a dataset selector + config rows with student pickers and/or seat position pickers.
  - **DeskMate** (dependent, `isIndependent: false`): Executes inside RandomFill's assignment loop via `IDependentSeatingStrategy`. When RandomFill proposes (student, seat), DeskMate checks if the student belongs to a desk-mate group. If so, it attempts coordinated assignment: places the student and their groupmates in adjacent seats on the same desk（同行+邻列+同 SeatsPerDesk 分组 = 同桌）. Eviction may move already-assigned RandomFill students but will NOT move students placed by prior strategies (FixedSeat/FrontRowRotation) or fixed seats. If the target seat lacks enough adjacent empty seats, partial assignment proceeds with a warning. No parameters — adjacency is always horizontal/same-desk. `dataType: "Both"`, `showSeatPosition: false`, `preventDuplicateInRow: true`. Number of student pickers per row dynamically determined by venue's `GridLayoutMetadata.SeatsPerDesk`.
  - **GenderRestrictedSeat** (dependent, `isIndependent: false`): Executes inside RandomFill's assignment loop after DeskMate (Priority 45). Checks if the target seat has a gender restriction configured via codeBlock. Gender mismatches trigger a redirect optimization: the student is immediately placed into a random matching-gender restricted empty seat (Handled, no reroll consumed). If no matching restricted seats are available, rejects to trigger reroll; forces assignment with warning on reroll exhaustion. No parameters — restrictions are configured per-seat via codeBlock. `dataType: "Venue"`, `showSeatPosition: true`, `showStudentPicker: false`, `showGenderPicker: true`, `preventDuplicateAcrossRows: true`. Gender value stored in `CustomValues["Gender"]`.
  - **NoRepeatDeskMate** (dependent, `isIndependent: false`): Executes inside RandomFill's assignment loop after DeskMate (Priority 40). Checks if the target seat's adjacent occupied seats contain a past desk-mate (loaded from recent snapshots by `NoRepeatDeskMateHistoryLoader`). If a repeat is detected, rejects to trigger reroll; forces assignment with warning on reroll exhaustion. Does NOT interfere with DeskMate group placements (lower priority) or fixed seats. Parameter: `HistoryWindowSize` (1–30, default 10). No codeBlocks.
  - **FixedSeat**: `dataType: "Both"`, `preventDuplicateAcrossRows: true`. Each row has a student picker + seat position picker for explicit assignment. Student pickers across all rows exclude each other's selected students from their dropdowns.
  - **FrontRowRotation**: No codeBlocks — `NeedsFrontRow` is already a Student model property (imported from CSV/XLSX). After selecting students by score, applies Fisher-Yates shuffle for random distribution across front-row columns.
  - **RandomFill**: No parameters, no codeBlocks.
  - **Defrag** (independent, Priority=0): "扫地僧" role — executes after all other strategies. Scans empty seats front-to-back, moves unconstrained students (those not in fixed seats or DeskMate groups) from behind each gap forward to fill it. Cross-column allowed. Logs `Defrag_EffectivenessNote` warning that prior strategy results may be invalidated. Zero parameters — behavior is purely position-driven. Default disabled.

**Plugin seat protection**: Plugins protect their assigned seats by declaring `"MarkFixedSeat"` in their manifest `capabilities` and calling `IPluginWorkspace.TryMarkFixed()`. The workspace validates the capability declaration, sets `IsFixed=true`, and logs the operation. `GetEmptySeats()` and Defrag's seat scanning both exclude `IsFixed` seats automatically. Built-in strategies use the same mechanism via `IFixedSeatCapability`. Capability constants and interfaces are centralized in `SeatFlow.Core/Strategies/Capability.cs` — add new const + interface there for future capabilities.

All user-visible text uses inline i18n: `{ "zh-CN": "...", "en-US": "..." }` dictionaries (not .resx keys). `LocalizeHelper.Resolve(dict)` in Presentation resolves per `CultureInfo.CurrentUICulture`, falling back to zh-CN. This works for both built-in strategies and plugins.

**Config loading behavior**: When loading persisted config rows, the matching filter uses a "match on whichever selectors have values" strategy: `(SelectedDataset is null || match) && (SelectedVenue is null || match)`. This means for `dataType: "Both"`, selecting only the dataset immediately loads the config (venue is treated as a wildcard until selected). When the user subsequently selects a venue, the filter re-runs with both values and narrows to the exact match. Student picker selections are deferred via `_pendingSelections` until the student list is loaded, avoiding lost selections from premature `SelectById` calls.

New model types (all in `SeatFlow.Core.Models`):
- `StrategyParameterDefinition` / `StrategyCodeBlock` / `StrategyFieldDefinition` + enums (`StrategyFieldType`, `StrategyDataType`, `StrategyDisplayMode`)
- `StrategyDatasetConfig` + `StrategyConfigRow` — persistence models stored under `{DataDirectory}/StrategyConfig/{strategyId}/`.

**Data storage**: All user data is stored in OS-standard application data directories via `AppEnvironment.DefaultDataDirectory`:
- Windows: `%APPDATA%\SeatFlow\`
- Linux: `~/.local/share/SeatFlow/` (XDG_DATA_HOME)
- macOS: `~/Library/Application Support/SeatFlow/`

Directory structure:
```
{DataDirectory}/
├── AppSettings.json              # Global settings
├── Logs/                         # Log files
│   └── SeatFlow_{yyyyMMdd-HHmmss}.log
├── Venues/                       # Venue files (*.venue.json)
├── Rosters/                      # Student rosters (*.roster.json)
├── Assignments/                  # Seating snapshots
│   └── {venueId}/{yyyyMMdd}/*.json
└── StrategyConfig/               # Strategy config files
    ├── {strategyId}.config.json
    └── {strategyId}/*.config.json
```

Users can override via `DataDirectory` in AppSettings.json. Plugins are stored in `RootAppDir/Plugins/` (Velopack install) or `{exeDir}/Plugins/` (dev/portable).

**Project config**: `AvaloniaUseCompiledBindingsByDefault` is `true` in the Avalonia csproj — all bindings are compiled unless explicitly opted out. Key csproj settings:
- `<AssemblyName>SeatFlow</AssemblyName>` — output EXE is `SeatFlow.exe`, not `SeatFlow.Presentation.Avalonia.exe`
- `<NoWarn>AVLN3001</NoWarn>` — suppresses "DI requires parameterized constructor" warning (all ViewModels use DI constructor injection, no parameterless ctors needed)
- `<Compile Remove="Lang\Resources.Designer.cs" Condition="!Exists('Lang\Resources.Designer.cs')" />` — prevents build failure when Designer.cs hasn't been generated yet (run `python3 scripts/i18n.py sync` to create it)
- `<ApplicationManifest>app.manifest</ApplicationManifest>` — DPI awareness on Windows

**App startup sequence**:
1. `StartupGuard.CheckEnvironment()` — validates .NET runtime >= 10 and supported OS (Windows 10+, macOS 12+, Linux any). Shows warning dialog and exits if unsupported.
2. `App.Initialize()` — `ApplyLanguageFromSettings()` sets `CurrentUICulture` + `Resources.Culture`, then `AvaloniaXamlLoader.Load(this)` (language MUST be set before XAML loading so `{x:Static}` resolves correctly)
3. `OnFrameworkInitializationCompleted` — Resolve `MainShellViewModel`/`MainWindow` from DI, wire DataContext
4. Call `IFileService.SetTopLevel()` and `IDialogService.SetTopLevel()` with MainWindow
5. Initialize `ViewModelBase.Dialog` (static) and `ViewModelBase` logger
6. Start `WatchdogService` with a 3s DispatcherTimer ping
7. Attach `ChineseInputNormalizer` behavior (全角数字/符号 → 半角)
7.5. Attach `KeyboardShortcutHandler` behavior (全局键盘快捷键)
8. `RestoreSettingsAsync()` — restore theme, window position/size (language already applied in step 1)

## Key Patterns

### CommunityToolkit.Mvvm Source Generators
- `[ObservableProperty]` on a private field generates a public property with `On<PropertyName>Changed` partial method hooks
- `[RelayCommand]` on a method generates an `ICommand` property
- `[NotifyPropertyChangedFor(nameof(OtherProp))]` triggers change notification for dependent properties
- All ViewModels inherit `ViewModelBase` (extends `ObservableObject`)

### ViewModelBase.SafeExecuteAsync

Two overloads:

```csharp
// Simple: try-catch, auto error dialog. errorTitle defaults to localized Resources.Common_OperationFailed
protected async Task<bool> SafeExecuteAsync(Func<Task> action, string? errorTitle = null)

// With timeout: auto-cancels via CancellationTokenSource, shows timeout dialog
protected async Task<bool> SafeExecuteAsync(Func<CancellationToken, Task> action, TimeSpan timeout, string? errorTitle = null)
```

The timeout overload aborts the operation when exceeded — prefer it for long-running exports or imports. Keep the timeout well under the WatchdogService threshold (45s).

`ViewModelBase` uses a static `IDialogService` — `App.axaml.cs` must call `ViewModelBase.InitializeDialogService(dialog)` at startup before any ViewModel uses `SafeExecuteAsync`. **If you add a new window or test ViewModels in isolation, Dialog must be initialized first.**

### ViewModelBase.CanLeaveAsync
```csharp
public virtual Task<bool> CanLeaveAsync()
```
Called by `NavigationService` before navigating away. Override to prompt user about unsaved changes.

### Theme & Custom Resources
- **Theme dictionaries**: App.axaml defines `ResourceDictionary.ThemeDictionaries` with Light/Dark variants for sidebar colors, semantic colors (Success/Warning/Error/Info), surface colors, and shadows.
- **Brushes**: SolidColorBrush resources reference theme colors via `DynamicResource`. Always use these brush resources (e.g., `{StaticResource SuccessBrush}`) — never hardcode hex colors.
- **Style includes**: `Typography.axaml`, `Spacing.axaml`, `Colors.axaml` are included after FluentTheme.
- **Font**: Global `Window` style sets `FontFamily` to `Inter,Microsoft YaHei UI,PingFang SC,Noto Sans CJK SC,WenQuanYi Micro Hei,sans-serif` for CJK support.
- **BoxShadows**: `CardShadowNone`, `CardShadowLarge`, `CardShadowSmall` are defined as `BoxShadows` resources.

### UI Services (`SeatFlow.Presentation.Avalonia/Services/`)
- **INavigationService** — Page switching with `PageKey` enum. `NavigateTo()` is synchronous, `NavigateToAsync()` runs `CanLeaveAsync()` first.
- **IDialogService** — Shows error/info dialogs. Requires `SetTopLevel(TopLevel)` before use.
- **IFileService** — File open/save pickers. Also requires `SetTopLevel()`.
- **WatchdogService** — Detects UI thread hangs via a background poll loop. Default timeout is 45 seconds; on expiry it dumps thread/process diagnostics to `err_<timestamp>.log` and force-exits the app. UI thread must call `Ping()` regularly (a `DispatcherTimer` does this in `App.axaml.cs`).

### Utility Windows
- `InputWindow` — modal dialog for single-line text input (returns the entered string)
- `DialogWindow` — general-purpose modal content host with title bar and close button

### Behaviors (`SeatFlow.Presentation.Avalonia/Behaviors/`)
- `CanvasZoomPan` — Pan and zoom for Canvas-based previews. **拖放座位时通过 NaN 哨兵机制跳过平移**（详见 `docs/DragDrop.md`）
- `ZoomOnScroll` — Ctrl+Scroll to zoom
- `ChineseInputNormalizer` — Converts full-width numbers/symbols to half-width on text input
- `KeyboardShortcutHandler` — 全局键盘快捷键处理（Ctrl+Z/Y 撤销/重做、Ctrl+S 保存、Delete 删除、Esc 取消）。通过 `KeyboardShortcutConfig` 开关控制，设置页面可配。详见 `docs/adr/ADR-011-keyboard-shortcuts.md`。
- `FileDropHandler` — Global OS file drag-drop import. Intercepts `DragDrop.DragOverEvent`/`DropEvent` with `RoutingStrategies.Tunnel` on MainWindow. Routes to page ViewModels that implement `IFileDropHandler` (in `Services/`).

### File Drag-Drop Import

Page ViewModels opt into file drag-drop by implementing `IFileDropHandler` (`Services/IFileDropHandler.cs`):

```csharp
public interface IFileDropHandler
{
    IReadOnlyList<string> AcceptedFileExtensions { get; }
    Task<bool> HandleFileDropAsync(IReadOnlyList<string> filePaths, CancellationToken ct);
}
```

**Routing table:**

| Page | Accepted Extensions | Handler |
|------|-------------------|---------|
| MemberManagement | `.csv`, `.xlsx`, `.json` | `ImportFromPathAsync` |
| FreeformManagement | `.csv`, `.json` | `ImportCsvCoreAsync` / `ImportJsonCoreAsync` |
| Home | `.seatsets` | Delegates to `SeatSetsImportHelper` |
| Settings | `.seatsets` | Delegates to `SeatSetsImportHelper` |
| Other pages | (none) | Shows "该页面无可导入的数据" dialog |

`SeatSetsImportHelper` (`Services/SeatSetsImportHelper.cs`) centralizes the `.seatsets` import flow (validate → probe → category selection dialog → import → refresh). It is shared by `App.HandleSeatSetsFileOpenAsync`, `SettingsViewModel`, and `HomeViewModel`.

**To add drag-drop support to a new page:**
1. Implement `IFileDropHandler` on the ViewModel
2. Return accepted extensions from `AcceptedFileExtensions`
3. Call existing import methods from `HandleFileDropAsync`

**Cross-platform**: OS file drop detection uses reflection to access `IDataTransfer` APIs that differ between Avalonia versions. The fallback chain tries `GetFiles()` → common data format IDs (`"files"`, `"FileDrop"`, `"FileNameW"`, `"text/uri-list"`). Wayland partial support; `DragEffects.None` fallback when file paths cannot be resolved.

### Adding a New Page
1. Add a new value to `PageKey` enum in `INavigationService.cs`
2. Create `ViewModels/NewThingViewModel.cs` (inherit `ViewModelBase`)
3. Create `Views/NewThingView.axaml` + `.axaml.cs` (set `x:DataType="vm:NewThingViewModel"`)
4. Register both in `Program.cs`: `services.AddSingleton<NewThingViewModel>()`
5. Add navigation button in `MainWindow.axaml` sidebar

### Axaml Bindings
- Always use `x:DataType` on the root element for compiled bindings
- Icons: `<fic:FluentIcon Icon="{x:Static ficEnum:Icon.{Name}}" FontSize="18"/>` (see `SeatFlow.Presentation.Avalonia/docs/Fluent_Icons.md`)
- Converters: `BoolConverters.cs` (Negate, TrueWhenNull, etc.) and `ValueConverters.cs`

### Sidebar
- Width: 140px expanded / 64px collapsed (controlled by `MainShellViewModel.SidebarWidth`)
- Auto-collapses when window width < 750px
- `MainShellViewModel.ToggleSidebar()` command for manual toggle

### i18n / Localization (`Lang/`)

Uses standard .NET `.resx` resource files in `SeatFlow.Presentation.Avalonia/Lang/`:
- `Resources.resx` — neutral language (zh-CN), ~700 keys
- `Resources.en-US.resx` — English satellite
- `Resources.Designer.cs` — hand-maintained typed accessor class (Visual Studio's `PublicResXFileCodeGenerator` doesn't work with `dotnet build`)

**Adding a new language**: create `Resources.xx-XX.resx` with translations, no code changes needed.

**Usage in XAML** (attribute syntax only — element content won't resolve):
```xml
<TextBlock Text="{x:Static lang:Resources.Settings_Title}" />
<Button Content="{x:Static lang:Resources.Common_OK}" />
```
Namespace: `xmlns:lang="using:SeatFlow.Presentation.Avalonia.Lang"`

**Usage in C#**:
```csharp
StatusMessage = Resources.Settings_Saved;
StatusMessage = string.Format(Resources.Snapshot_VenuesLoadedFmt, count);
```
**Important**: In classes inheriting from `Window` (DialogWindow, InputWindow), `Resources` resolves to `Window.Resources` (IResourceDictionary). Use fully-qualified `Lang.Resources.xxx` in those files.

**Key naming**: `{Page}_{Element}` with PascalCase, e.g. `Settings_Title`, `Nav_Home`, `Common_OK`. Format strings use `{0}` placeholders.

**Managing resources**: Use `python3 scripts/i18n.py` for all CRUD operations on .resx keys — it keeps the three files (zh-CN .resx, en-US .resx, Designer.cs) in sync. See `scripts/ToolsCollection.md` for full usage guide. Common commands:
```bash
python3 scripts/i18n.py list                     # List all keys
python3 scripts/i18n.py list --missing-en        # Find untranslated keys
python3 scripts/i18n.py check                    # Validate consistency
python3 scripts/i18n.py add KEY --zh "中" --en "EN"  # Add a key
python3 scripts/i18n.py sync                     # Regenerate Designer.cs from .resx
```
Backups are auto-created in `Lang/.backup/` (gitignored).

**Language switching**: `App.ApplyLanguageFromSettings()` (called in `Initialize()` before XAML loading). Sets `CultureInfo.CurrentUICulture` and `Resources.Culture`.

### Scripts (`scripts/`)

Besides `scripts/i18n.py` (documented above), the following scripts exist — all documented in `scripts/ToolsCollection.md`:

| Script | Purpose |
|--------|---------|
| `scripts/i18n.py` | i18n .resx resource CRUD + Designer.cs sync (45 unit tests) |
| `scripts/version.py` | Unified version management across 15+ files — App, file format, strategy manifest, onboarding config versions. Subcommands: `show`, `check`, `bump-app`, `bump-file`, `bump-strategy`, `bump-onboarding`, `sync` (26 unit tests) |
| `scripts/build/publish.sh` / `scripts/build/publish.ps1` | Multi-platform TUI/CLI publishing (self-contained + framework-dependent, trimming, AOT, SHA256 table) |
| `scripts/build/clean.sh` / `scripts/build/clean.ps1` | Recursive bin/obj cleanup |
| `scripts/update_version_info.py` | MSBuild helper — reads version.json + git → generates VersionInfo.g.cs at build time |
| `scripts/release/release.py` | Release orchestrator — build → package → OSS upload → GitHub Release |

Unit tests are in `scripts/tests/`.

### About Page Data (`Data/about.json`)

Multi-language JSON with top-level culture keys:
```json
{ "zh-CN": { "description": "...", "dependencies": [...] },
  "en-US": { "description": "...", "dependencies": [...] } }
```
`AboutViewModel.LoadAboutData()` selects by `CultureInfo.CurrentUICulture.Name`, falls back to `"zh-CN"`.

### Dialog Windows

- `DialogWindow` — Confirm/Error/Warning/Info/MultiOption dialogs. Buttons use `Content="{x:Static}"` attribute syntax. Code-behind only controls visibility and MultiOption custom text. **Never** use `{x:Static}` as element content inside `<Button>...</Button>`.
- `InputWindow` — Text input dialog. Same button pattern.

## File Versions & Migration

All persisted JSON files carry a `version` field. Current versions are defined in `SeatFlow.Infrastructure/Migration/file_versions.json` (embedded resource, compiled into the assembly). `FileVersionInfo.GetCurrentVersion(fileType)` reads the latest version at runtime.

| File type | Version | Location | Wrapper class |
|---|---|---|---|
| Venue | `1.1` | `{data}/Venues/*.venue.json` | `VenueFile` |
| Roster | `1.1` | `{data}/Rosters/*.roster.json` | `RosterFile` |
| Snapshot | `1.0` | `{data}/Assignments/{venueId}/{date}/*.json` | `SeatingSnapshot` |
| VenueInfo | `1.0` | `{data}/Assignments/{venueId}/_venue.json` | `VenueSnapshotInfo` |
| AppSettings | `1.1` | `{data}/AppSettings.json` | `AppSettings` |
| StrategyConfig | `1.0` | `{data}/StrategyConfig/{strategyId}.config.json` | `StrategyConfig` |
| StrategyDatasetConfig | `1.0` | `{data}/StrategyConfig/{strategyId}/*.config.json` | `StrategyDatasetConfig` |

### Migration pipeline

On load, each repository reads the file as `JsonNode`, calls `FileMigrationService.Migrate(fileType, node, fileVersion, targetVersion)`, then deserializes. Migration is **forward-only** — no version rollback. The service finds registered `IFileMigrator` implementations and chains them by matching `FromVersion`/`ToVersion`.

### 何时需要迁移器 vs 依赖模型默认值

**不需要迁移器**（模型默认值即可）：
- 新增属性/配置节，且所有字段都有合理的默认值（`bool` → `false`、`int` → `0`、`string` → `""`、引用类型 → `new()`）
- 旧 JSON 缺少该节点时，`System.Text.Json` 反序列化会保留属性的默认值
- 示例：新增 `TelemetryConfig`、`KeyboardShortcutConfig` 到 `AppSettings`，所有字段有默认值，旧文件无需迁移

**必须写迁移器**：
- 数据格式变化：如 `VenueMigrators 1.0→1.1` 将座位从列主序重排为行主序
- 字段语义变化：同一个 JSON key 的含义改变，需要重新计算/转换
- 字段重命名且不能丢失旧数据：需要将旧 key 的值复制到新 key
- 数据结构重组：嵌套层级变化（如 `SeatSetsMigrators`）

**原则**：只有旧数据**不转换就会出错或丢失信息**时才写迁移器。能用默认值兜底就不要写。

### Adding a migration

1. Add a nested class in `Migration/Migrators/{FileType}Migrators.cs` (one file per file type):
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
2. Register in `ServiceCollectionExtensions.cs`: `services.AddSingleton<IFileMigrator, VenueMigrators.Step_1_0_to_1_1>()`
3. Bump the version in `file_versions.json`
4. Update the model's default `Version` property in Core
5. Add tests in `Infrastructure.Tests/Migration/{FileType}MigratorsTests.cs`

### Existing migrators

- `VenueMigrators.Step_1_0_to_1_1` — Reorders Grid layout seats from column-major to row-major (sorted by `Row` then `Column`)

### JSON field conventions

- Serialization uses `JsonNamingPolicy.CamelCase` — all fields are lowercase in JSON (`row`, `column`, `layoutTypeString`, `logicalGroup`)
- `ClassroomLayoutDefinition.LayoutType` is serialized as **both** a number (`layoutType`: 0=Grid, 1=Polar, 2=Freeform) and a string (`layoutTypeString`: "Grid"/"Polar"/"Freeform"). Migrators should read `layoutTypeString` for clarity.
- `Seat` polymorphic serialization uses `SeatJsonConverter` which writes a `Type` discriminator (capital T, string: "Grid"/"Polar"/"Freeform") alongside each seat object. The `type` field (lowercase, camelCase of `SeatType` enum) is a separate integer.
- `SeatingSnapshot.Version` and `VenueSnapshotInfo.Version` were added in this migration round — old snapshots without the field default to `"1.0"`.
- `VenueFile.ContentHash` and `RosterFile.ContentHash` — SHA256 hashes computed on save (ContentHash null → serialize → hash → set → re-serialize). Student dataset hash excludes `importedAt`/`originalFileName` (unstable timestamps).

### Grid seat ordering

`GridLayoutBuilder.BuildGrid` creates seats in **row-major** order (outer loop: rows, inner loop: columns). This ensures `RandomFillStrategy` fills seats row-by-row (left-to-right, top-to-bottom). For irregular grids with `ColumnRowCounts`, `maxRows = ColumnRowCounts.Max()` and each column checks `r <= rowsForCol`.

### Snapshot venue layout embedding

Snapshots store the full `ClassroomLayoutDefinition` (JSON-serialized via `SeatJsonConverter`) in `Metadata["venueLayout"]` at creation time. The snapshot preview (`BuildPreviewAsync`) reads this embedded layout first; old snapshots without it fall back to loading the venue file. This ensures snapshots are self-contained — editing or deleting the venue file doesn't break existing snapshot previews.

### Snapshot integrity detection

`BuildPreviewAsync` compares `Metadata["venueHash"]` with the current venue file's `ContentHash`:
- **Venue deleted** → red warning bar "会场已删除，无法预览", rollback button disabled
- **Venue changed** → yellow warning bar "会场布局已更改，回滚可能失败"
- **Data changed** (student IDs missing from current datasets) → yellow bar "数据已更改", affected seats highlighted in yellow

`RollbackAsync` checks venue integrity before rolling back:
- Venue deleted → dialog → restore venue from snapshot's `venueLayout`
- Venue changed → dialog → import snapshot's venue as new venue

### Snapshot rotation

`AppSettings.MaxSnapshotsPerVenue` (default 30, 0=unlimited). After saving a snapshot, `RotateSnapshotsAsync` deletes the oldest snapshots if the count exceeds the limit. Sidebar status bar shows `"{n}/{max}"` via `SnapshotQuotaDisplay`.

### Venue editing & seat ID preservation

`VenueConfigurationViewModel` preserves seat IDs across edits: on load, records `(Row, Column) → Id` and `(Ring, Angle) → Id` maps; on save, newly-built seats match old seats by position and reuse their IDs. This prevents snapshot `SeatAssignments` from breaking after venue edits.

### Student dataset rename

`RenameStudentDatasetAsync` renames in-place (updates `RosterFile.Description` only, preserves ID). Previously it deleted the old file and created a new one with a new ID.

### Page navigability management

A JSON config file `Data/page_navigation.json` (embedded resource) controls which navigation pages are enabled. Format:

```json
{ "version": "1.0", "pages": { "Home": true, "PluginManagement": false, ... } }
```

Key = `PageKey` enum value name. `MainShellViewModel.LoadPageNav()` loads it via `Assembly.GetManifestResourceStream` (same pattern as `about.json`). For each disabled page, add two properties to `MainShellViewModel`:

```csharp
public double PluginManagementOpacity => IsPageEnabled("PluginManagement") ? 1.0 : 0.4;
public string? PluginManagementDisabledTip => IsPageEnabled("PluginManagement") ? null : Resources.Nav_PluginDisabled;
```

Bind to sidebar buttons with `Opacity` (not `IsEnabled` — disabled controls hide ToolTips in Avalonia) and `ToolTip.Tip`. `NavigateAsync` already checks `IsPageEnabled()` and returns early for disabled pages. Disabled message goes in `.resx` with key pattern `Nav_{PageName}Disabled`.

### Onboarding Guide System

Fully data-driven via `Data/onboarding_config.json` (v3.2). See `docs/ONBOARDING_GUIDE.md` for full details. See `docs/adr/ADR-008-onboarding-demo-data-injection.md` for the demo data injection decision.

**Two types of guides:**
- **启动引导 (`startupPhases`)** — 9 阶段（~24 子步骤）完整工作流：Welcome → MemberManagement(导出模板→导入→更新→管理) → VenueConfiguration(新建→布局→保存) → StrategyConfiguration(列表→调整→冲突提示→保存) → SeatingArrangement(选择→生成→导出) → SnapshotHistory → **Settings(快捷键配置)** → Closing。左下角步骤点按阶段呈现（9 个点），通过延迟 Dispatch 覆盖 Guide Indicator 实现（详见 ADR-011）。
- **页面引导 (`pageGuides`)** — Triggered on first visit to a page (FreeformManagement, PluginManagement). Tracked in `AppSettings.CompletedPageGuides`.

**声明式示例数据注入 (v3.2):** `OnboardingPhaseDefinition.SeedData` (bool, 默认 false) 控制跨阶段导航时是否注入演示数据。原运行状态标志 `_memberManagementDataSeeded` 已删除，改为 JSON 声明式控制。MemberManagement 分两次进入（中间隔 Home 过渡阶段），第一次不注入（ImportButton 可见），第二次注入（UpdateFromFileButton 可见）。`ClearPageData` 使用 `_memberManagementDemoInjected` 静态标志判断是否实际注入过。

**Key classes:** `IOnboardingService` / `OnboardingService` (implements both `IOnboardingService` and `IOnboardingStarter`), `OnboardingPhaseDefinition` / `OnboardingStepDefinition` (models). `MainWindow.axaml.cs` has 5 thin event wrappers — all logic in `OnboardingService`.

**Navigation ordering (Phase 1 fix):** `HandleStepOpening` must navigate to the new page **before** resolving the target control's x:Name. The original order (resolve → navigate) caused `ContentPresenter.Child` to reference the old page, failing NameScope lookups for the first step of each phase. Together with `OnboardingNavigateTo`'s synchronous `CurrentViewModel` setting (skipping `RunTransitionAsync` animation via `IsOnboardingActive` guard), targets resolve correctly on the first attempt.

**JSON-driven code:** `BuildStepsFromDefs()` converts `OnboardingStepDefinition` → `GuideStepOption` via pure `ResourceManager.GetString(step.titleKey)`. Zero key-name inference in C#. Target resolution deferred to Guide's `StepOpening` event. Each step explicitly declares `titleKey`, `descKey`, `target`, `placement`, `showMask`, `showArrow`.

**Adding/modifying guide steps:** Edit `onboarding_config.json` + add resx keys + update `Designer.cs`. No C# changes needed. If a target control is missing `x:Name`, add it to the `.axaml` file.

**Demo data seeding (v3.2):** `OnboardingService.SeedPageData()` injects pure in-memory demo data into page ViewModels during startup guide phase transitions, controlled declaratively by `OnboardingPhaseDefinition.SeedData` (JSON bool, default false). Cleared by `ClearPageData()` on guide completion (guarded by `_memberManagementDemoInjected` static flag — only cleans if demo was actually injected). For ViewModels with fire-and-forget async init in constructors (SeatingArrangement, VenueConfiguration, StrategyConfiguration), injection is deferred via `Dispatcher.UIThread.Post(..., DispatcherPriority.Background)` to run after the async `LoadXxxAsync()` overwrites. Uses only Core models + ViewModel public APIs — no Infrastructure-layer or disk I/O dependencies. See ADR-008.

**Window state sync (v3.1):** `MainWindow` subscribes to `Activated`/`Deactivated` events → forwarded to `OnboardingService.HandleWindowActivated()`/`HandleWindowDeactivated()`. On deactivate (minimize/Alt+Tab): `_isWindowObscured=true`, `Guide.Close()` silently closes Popups (no confirm dialog, no completion). On activate (restore): re-opens Guide from preserved `CurrentIndex`. Prevents the 3 Popups (`ShouldUseOverlayLayer=False`, native OS windows) from lingering as orphan windows.

**DI:** `services.AddSingleton<IOnboardingService, OnboardingService>()`, with `IOnboardingStarter` bridged to the same instance. `MainShellViewModel` injects `IOnboardingService` to trigger page guides after navigation.

### MemberManagement dataset flow

**Click-to-load**: `OnSelectedDatasetChanged` auto-loads the dataset via `SwitchToDatasetAsync()`. No separate "Load" button.

**Dirty tracking**: Uses JSON serialization snapshot comparison. `_originalStudentsJson` stores the state after load/save; `IsDirty` compares current `SerializeStudents()` against it. `MarkClean()` / `MarkDirty()` manage the snapshot.

```csharp
private bool IsDirty =>
    IsNewStudentDirty ||
    (_originalStudentsJson != null && SerializeStudents() != _originalStudentsJson);
```

**NewStudent dirty**: `IsNewStudentDirty` checks if any field on the bottom "add row" has been filled (name, height, gender, or front-row flag). Must be included in `IsDirty` so unsaved new-row data triggers the switch-dataset dialog.

**Switch dataset flow**: `SwitchToDatasetAsync(target)` → if dirty → 3-btn dialog (Save / Discard / Cancel). Cancel reverts `SelectedDataset` to `_previousDataset` via `_suppressDatasetLoad` guard. After save or discard, `NewStudent` is reset to prevent data leaking between datasets.

**Save flow**: `SaveAsync` checks `IsNewStudentDirty` first → if true, shows "Discard & Save" / "Cancel" dialog. If `CurrentDatasetId` is null (imported data), delegates to `RenameSaveAsync` (Save As). Otherwise calls `SaveInternalAsync` which deletes old file + saves new one without confirmation dialog. Both call `MarkClean()` after success.

**Validation**: `ValidateStudents()` skips completely blank rows (name empty AND height null AND gender null AND needsFrontRow false). Partial rows with empty name still flagged.

### About page version

Version comes from `about.json` → `AboutData.Version` field, appended with git commit hash:

```
Version = $"{data.Version ?? "1.0.0"}+{GitCommit.Hash}"
```

`GitCommit.Hash` is a `const string` in auto-generated `GitCommit.g.cs`, produced by an MSBuild target (`GenerateGitCommit`) that runs `git rev-parse --short HEAD` before each build. Falls back to `"unknown-commit-id"` when git is unavailable. The generated file lives in `$(IntermediateOutputPath)Generated\` and is NOT committed.

### Deterministic builds

csproj settings for reproducible output across time/machines:

```xml
<Deterministic>true</Deterministic>
<PathMap>$([System.IO.Path]::GetFullPath('$(MSBuildProjectDirectory)'))=./</PathMap>
```

Same source code → same DLL hash regardless of build time or absolute path.

### VenueConfiguration: NewVenue race condition

`NewVenue()` must cancel any in-flight `SelectVenueAsync` from the previous selected venue before calling `ResetParameters()`. Otherwise the running async load can overwrite the reset parameters with the old venue's data. Pattern: `_selectVenueCts?.Cancel()` at the top of `NewVenue()`, and `ct.IsCancellationRequested` checks inside `SelectVenueAsync` before setting VM state.

### DockPanel child order

In Avalonia's `DockPanel`, `LastChildFill="True"` (default) means the LAST child fills remaining space. If the last child has `DockPanel.Dock="..."`, the previous undocked child fills instead. Always place `Dock` children BEFORE the filling child (typically a `ScrollViewer` or `ListBox`).

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

### RELEASE.md — 发布说明

**路径**: `RELEASE.md`（项目根目录）。独立于 `CHANGELOG.md`，由开发者在每次发布前手动编写。`release.py` 读取该文件作为 GitHub Release body（末尾自动附加 SHA256 表格）。

格式：
```markdown
# SeatFlow v1.3.0 发布说明

本次发布主要修复了...

## 新增
- ...

## 修复
- ...
```

### 版本真相源 (`version.json`)

**路径**: `version.json`（项目根目录）。App 版本号的唯一来源。字段：`version`, `commitId`, `buildDate`。

- `version` → 由 `version.py bump-app` 管理
- `commitId` / `buildDate` → 编译时由 `update_version_info.py` 从 git 实时获取，生成 `VersionInfo.g.cs` 供 C# 使用
- `AboutViewModel` 使用 `VersionInfo.Version` + `VersionInfo.CommitId` 显示版本

### 发布 (`scripts/build/publish.sh` / `scripts/build/publish.ps1`)

```bash
cd scripts/build
./publish.sh                    # TUI 交互模式（多选平台/选项）

# CLI 模式参数: <类型> <配置> <版本> <选项>...
# 类型: both | sc | fde     (全部 / 独立 / 框架依赖)
# 选项: clean aot trim
./publish.sh both Release "" "1.2.1" clean aot   # 全平台独立+框架依赖，裁剪+AOT，版本 1.2.1
```

### 发布流水线 (`scripts/release/release.py`)

```bash
python3 scripts/release/release.py --dry-run        # 预览全流程
python3 scripts/release/release.py                  # 完整发布
python3 scripts/release/release.py --skip-build     # 跳过构建，仅打包和发布
```

**流程**: dotnet publish (4 RID) → zip/tar.gz 打包 → SHA256 → OSS 上传 + releases.json 更新 → GitHub REST API 创建 Release。

**配置文件**: `scripts/release/config.json`（gitignored，含 OSS AccessKey + GitHub token）。模板见 `config.json.example`。

**依赖**: `pip install oss2 requests`

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

## Documents

> **重要**: `docs/CLAUDE.md` 是根 CLAUDE.md 的人类可读副本。**每次修改根 CLAUDE.md 后必须同步更新 `docs/CLAUDE.md`**（参见 `docs/INDEX.md` 联动规则）。

- `docs/INDEX.md` — Documentation map & cross-reference (read first before modifying docs; includes doc responsibility matrix and change-scenario linkage table)
- `ARCHITECTURE.md` — Project goals & architecture design
- `docs/Phases.md` — Implementation phases & detailed planning
- `CONTRIBUTING.md` — Dev environment, conventions, version migration flow
- `CHANGELOG.md` — Version changelog (Keep a Changelog format)
- `docs/ONBOARDING_GUIDE.md` — Onboarding guide system design (JSON-driven, startup + page guides)
- `docs/StrategyDataResilience.md` — Strategy data persistence & fault tolerance analysis
- `docs/adr/` — Architecture Decision Records (ADR-001 ~ ADR-012). Key ones: ADR-002 (MVVM + IMessenger planned for cross-ViewModel communication), ADR-006 (strategy pipeline fill-in-order), ADR-012 (plugin first-class architecture — 插件一级类型、plugins-manifest v2、ALC 卸载模式、脚本安全边界)
- `SeatFlow.Presentation.Avalonia/docs/Design_Spec.md` — FluentUI design spec (colors, typography, spacing, icons)
- `SeatFlow.Presentation.Avalonia/docs/DragDrop.md` — Avalonia 12 drag-drop patterns, pitfalls, CanvasZoomPan interaction
- `SeatFlow.Presentation.Avalonia/docs/Fluent_Icons.md` — All FluentUI icon names in use
- `SeatFlow.Plugins.Sdk/docs/README.md` — Plugin SDK development guide (interfaces, 2-tier manifest format v2, packaging, dependent strategies, scripting sandbox)
- `examples/plugins/` — Example plugins (height-sort / desk-pair / Lua / C# script / multi-strategy package) + `build.sh`
- `scripts/ToolsCollection.md` — Full reference for `i18n.py` and `version.py` scripts
