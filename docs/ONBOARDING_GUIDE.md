# 引导系统设计文档

> 版本 3.2 — 启动引导 + 页面引导，JSON 声明式 seedData，中间过渡阶段。
> 最后更新: 2026-06-27

## 概述

SeatFlow 的引导系统帮助新用户快速上手核心功能的操作。引导分为两类：

1. **启动引导** (`startupPhases`) — 首次启动时触发，逐步引导用户完成完整的排座工作流（导入→会场→策略→生成→导出）
2. **页面引导** (`pageGuides`) — 用户首次进入某个页面时触发，仅展示该页面的关键操作

两类引导共享同一个 `OnboardingService` 和 `Guide` 控件，但在触发时机、步骤范围和完成行为上有所不同。

## 架构

```
┌──────────────────────────────────────────────────────────────┐
│                    Guide 控件 (CodeWF)                         │
│  步骤列表 + 高亮定位 + 前进/后退 + 完成/关闭                     │
└──────────────────────────┬───────────────────────────────────┘
                           │ StepsSource, Show(), Close()
                           │ StepOpening 事件 → 解析 Target
┌──────────────────────────▼───────────────────────────────────┐
│                   OnboardingService                           │
│  • StartOnboarding() — 启动引导                                │
│  • TryShowPageGuide(page) — 页面引导（首次访问触发）              │
│  • BuildStepsFromDefs() — JSON → GuideStepOption 纯机械转换     │
└──────────────────────────┬───────────────────────────────────┘
                           │
┌──────────────────────────▼───────────────────────────────────┐
│               onboarding_config.json (嵌入资源)                │
│  { startupPhases: [...], pageGuides: {...} }                  │
└──────────────────────────────────────────────────────────────┘
```

### 关键文件

| 文件 | 作用 |
|------|------|
| `Data/onboarding_config.json` | **唯一的数据源**——所有阶段、步骤、资源键引用 |
| `Services/IOnboardingService.cs` | 接口：`StartOnboarding`, `TryShowPageGuide`, `MarkPageGuideShownAsync` |
| `Services/OnboardingService.cs` | 核心实现——加载 JSON、构建 GuideStepOption、处理事件 |
| `Services/OnboardingPhaseDefinition.cs` | 数据模型：`OnboardingConfig`, `OnboardingPhaseDefinition`, `OnboardingStepDefinition` |
| `Views/MainWindow.axaml` | Guide 控件声明（`<codewf:Guide x:Name="OnboardingGuide" .../>`） |
| `Views/MainWindow.axaml.cs` | Thin wrapper：3 个事件处理器转发到 `IOnboardingService` |
| `App.axaml.cs` | 启动时调用 `CheckAndStartOnboardingAsync()` |
| `ViewModels/MainShellViewModel.cs` | 导航完成后触发 `TryShowPageGuide` |
| `ViewModels/SettingsViewModel.cs` | "重新开始引导"按钮 |
| `Core/Models/AppSettings.cs` | `IsFirstLaunch` + `CompletedPageGuides` 字典 |
| `Styles/Guide.axaml` | Guide 控件的 ControlTheme（遮罩、卡片、按钮样式） |
| `Lang/Resources.resx` / `.en-US.resx` / `Designer.cs` | 引导文本的 i18n 资源 |

### DI 注册链

```
Program.cs:
  services.AddSingleton<IOnboardingService, OnboardingService>()
  services.AddSingleton<IOnboardingStarter>(sp => (IOnboardingStarter)sp.GetRequiredService<IOnboardingService>())
  services.AddSingleton<MainWindow>()            // 依赖 IOnboardingService
  services.AddSingleton<MainShellViewModel>()     // 依赖 IOnboardingService
  services.AddSingleton<SettingsViewModel>()      // 依赖 IOnboardingService（通过 IOnboardingStarter 桥接）
```

## JSON Schema

### 顶层结构

```json
{
  "version": "3.0",
  "startupPhases": [ OnboardingPhaseDefinition, ... ],
  "pageGuides": {
    "FreeformManagement": OnboardingPhaseDefinition,
  }
}
```

### OnboardingPhaseDefinition

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `page` | string? | 否 | 要导航到的页面（PageKey 枚举名称）。`null` = 留在当前页（用于 Home 欢迎阶段和结尾阶段） |
| `seedData` | bool | 否 | 跨阶段导航时是否注入演示数据。默认 `false`。用于 MemberManagement Phase 2 及后续需演示数据的阶段 |
| `steps` | array | 是 | `OnboardingStepDefinition` 数组 |

### OnboardingStepDefinition

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `titleKey` | string | (必填) | `Resources.resx` 中标题文本的键名 |
| `descKey` | string | (必填) | `Resources.resx` 中描述文本的键名 |
| `target` | string | `""` | 高亮目标控件的 `x:Name`。支持分号分隔的候选项（取第一个找到的）。空字符串 = 居中模态 |
| `placement` | string | `"Right"` | 弹窗方向：`Top`, `Bottom`, `Left`, `Right`, `Center`（大小写不敏感） |
| `showMask` | bool | `true` | 是否显示深色遮罩。居中纯文本步骤可设为 `false` |
| `showArrow` | bool | `true` | 是否显示指向箭头。居中纯文本步骤可设为 `false` |

### 当前引导内容概览

**启动引导 (startupPhases)** — 8 阶段 20 步：

| 阶段 | 页面 | seedData | 步骤数 | 目标控件 |
|------|------|----------|--------|---------|
| 欢迎 | Home | — | 2 | (centered), ToggleSidebarButton |
| 成员管理（导入） | MemberManagement | — | 2 | ExportTemplateButton, ImportButton |
| 成员管理（更新） | MemberManagement | `true` | 3 | UpdateFromFileButton, StudentListBox, NewStudentRow |
| 会场配置 | VenueConfiguration | `true` | 3 | NewVenueButton, LayoutTypePanel, SaveVenueButton |
| 策略配置 | StrategyConfiguration | `true` | 4 | StrategyListBox, EditEnabledSwitch, (centered), SaveAllButton |
| 排座生成 | SeatingArrangement | `true` | 4 | VenueListBox, DatasetListBox, GenerateButton, ExportButton |
| 快照历史 | SnapshotHistory | `true` | 2 | VenueComboBox, SnapshotListBox |
| 结束 | Home | — | 1 | (centered) |

**页面引导 (pageGuides)** — 首次访问触发：

| 页面 | 步骤数 | 目标控件 |
|------|--------|---------|
| FreeformManagement | 3 | ImportCsvButton, AddPointButton, SaveLayoutButton |

## 触发流程

### 启动引导

```
App.axaml.cs → CheckAndStartOnboardingAsync()
  ├── 检测 IsFirstLaunch == true 或设置文件不存在
  ├── 立即持久化 IsFirstLaunch = false（崩溃安全）
  └── Dispatcher.UIThread.Post(Background) → onboarding.StartOnboarding()
       ├── 加载 JSON 配置 + 平铺步骤列表
       ├── 设置 MainShellViewModel.IsOnboardingActive = true
       ├── 展开侧边栏，导航到 Home
       ├── 订阅 Guide.StepOpening 事件
       └── Dispatcher.UIThread.Post(Loaded) → Guide.Show()
```

### 页面引导

```
MainShellViewModel.SchedulePageGuideCheck()  ← 导航完成后触发
  └── Dispatcher.UIThread.Post(Background) → onboarding.TryShowPageGuide(page)
       ├── 守卫：IsActive == false → 启动引导或已有页面引导活跃则跳过
       ├── 守卫：该 page 在 pageGuides 中有定义 → 无定义则跳过
       ├── 守卫：CompletedPageGuides 中不存在 → 已展示过则跳过
       ├── 延迟加载 config（如尚未加载）
       ├── 设置 IsActive = true, _currentPageGuide = pageKey
       ├── 构建该页面的步骤列表
       └── Guide.Show()
```

### 关闭确认

用户点击 × 或按 Esc：
- 若已正常完成（`_isCompleting == true`）→ 直接关闭
- 否则弹出 `ShowConfirmAsync` 确认对话框
- 用户确认 → 执行 `CompleteOnboardingAsync()`
- 用户取消 → 调用 `Guide.Show()` 恢复显示

### 完成流程

```
OnboardingService.CompleteOnboardingAsync()
  ├── 取消订阅 StepOpening 事件
  ├── 捕获 wasPageGuide（在 await 前保存，防竞态）
  ├── 立即设置 IsActive = false, _currentPageGuide = null（在 await 前，防竞态）
  ├── 若是页面引导 → 持久化到 AppSettings.CompletedPageGuides
  ├── 若是启动引导 → 调用 vm.CompleteOnboardingAsync() 恢复 UI
  └── 关闭 Guide 控件，清空 StepsSource
```

## 添加/修改引导步骤

### 添加新步骤

**只需编辑 2 个文件，不改 C# 代码：**

1. 在 `onboarding_config.json` 中添加步骤定义
2. 在 `Resources.resx` 和 `Resources.en-US.resx` 中添加对应文本
3. 在 `Resources.Designer.cs` 中添加强类型访问器属性

**如果新步骤需要高亮某个控件**，先给控件添加 `x:Name`（在对应的 `.axaml` 文件中）。

示例——在启动引导的 MemberManagement 阶段添加一步"保存数据集"：

```json
// 在 Members 阶段的 steps 数组末尾添加
{ "titleKey": "Guide_Members_Save_Title",
  "descKey": "Guide_Members_Save_Desc",
  "target": "SaveButton",
  "placement": "Bottom" }
```

然后在 resx 中添加对应的 `<data>` 条目。

### 添加新页面的页面引导

在 `pageGuides` 字典中添加新条目：

```json
"SnapshotHistory": {
  "page": "SnapshotHistory",
  "steps": [
    { "titleKey": "Guide_Snapshot_XXX_Title", "descKey": "Guide_Snapshot_XXX_Desc", "target": "..." }
  ]
}
```

键名必须与 `PageKey` 枚举值完全匹配（区分大小写）。

### 修改现有步骤

只需编辑 JSON 中的 `titleKey`, `descKey`, `target`, `placement` 等字段，或修改 resx 中的文本内容。无需改 C# 代码。

### 删除步骤

从 JSON 中移除步骤定义即可。旧的 resx 键可以保留（无害），或后续统一清理。

## 资源键命名约定

引导文本的命名模式：`Guide_{Page}_{Action}_{Type}`

例如：
- `Guide_Members_Import_Title` — 成员管理页面，导入操作，标题
- `Guide_Venue_Layout_Desc` — 会场配置页面，布局操作，描述

**不需要遵循代码约定**——资源键完全由 JSON 中声明的 `titleKey`/`descKey` 值决定。代码不做任何键名拼接或推断，只做 `ResourceManager.GetString(jsonKey)`。

## 目标控件清单

### 在 MainWindow 中（可跨页面访问）

| x:Name | 控件 | 用途 |
|--------|------|------|
| `ToggleSidebarButton` | Button | 侧边栏折叠/展开 |

### 在各页面中

| x:Name | 所在视图 | 控件 | 用途 |
|--------|---------|------|------|
| `ExportTemplateButton` | MemberManagementView | Button | 导出导入模板 |
| `ImportButton` | MemberManagementView | Button | 导入学生数据 |
| `StudentListBox` | MemberManagementView | ListBox | 学生列表 |
| `NewStudentRow` | MemberManagementView | Border | 新增学生行 |
| `SaveButton` | MemberManagementView | Button | 保存数据集 |
| `NewVenueButton` | VenueConfigurationView | Button | 新建会场 |
| `LayoutTypePanel` | VenueConfigurationView | StackPanel | 布局类型选择 |
| `SaveVenueButton` | VenueConfigurationView | Button | 保存会场 |
| `StrategyListBox` | StrategyConfigurationView | ListBox | 策略列表 |
| `EditEnabledSwitch` | StrategyConfigurationView | ToggleSwitch | 启用/禁用策略 |
| `SaveAllButton` | StrategyConfigurationView | Button | 保存全部策略 |
| `VenueListBox` | SeatingArrangementView | ListBox | 会场选择 |
| `DatasetListBox` | SeatingArrangementView | ListBox | 数据集选择 |
| `GenerateButton` | SeatingArrangementView | Button | 生成座位 |
| `ExportButton` | SeatingArrangementView | Button | 导出结果 |
| `SaveSnapshotButton` | SeatingArrangementView | Button | 保存为快照 |
| `UndoButton` | SeatingArrangementView | Button | 撤销操作 |
| `VenueComboBox` | SnapshotHistoryView | ComboBox | 选择会场 |
| `SnapshotListBox` | SnapshotHistoryView | ListBox | 快照列表 |
| `RollbackButton` | SnapshotHistoryView | Button | 回滚快照 |
| `ImportCsvButton` | FreeformManagementView | Button | 导入自由布局 |
| `AddPointButton` | FreeformManagementView | Button | 添加坐标点 |
| `SaveLayoutButton` | FreeformManagementView | Button | 保存自由布局 |

## 文本风格指南

引导步骤的文本应遵循**指令式**风格——直接告诉用户要做什么：

| ✅ 推荐（指令式） | ❌ 避免（描述式） |
|---|---|
| "点击此按钮从文件导入学生名单" | "首先需要导入学生数据。点击「导入」按钮..." |
| "在此下拉框选择会场" | "在此页面可以查看历史记录、预览座位表..." |
| "点击此开关启用策略" | "使用开关启用/禁用策略" |

每一步的 `title` 应该提示**要执行的操作**，`description` 应该提供**简短的上下文说明**。

## 示例数据注入（v3.1 新增，v3.2 修订）

引导启动时，`OnboardingService.SeedPageData()` 向各页面 ViewModel 注入纯内存示例数据，使条件可见的目标控件（如 `LayoutTypePanel`、`StudentListBox`）在引导期间正常显示。引导完成时 `ClearPageData()` 清除所有注入数据，不留磁盘痕迹。

**注入由 `OnboardingPhaseDefinition.SeedData`（JSON 声明式 bool，默认 `false`）控制**。仅在 `HandleStepOpening` 检测到阶段过渡且 `phase.SeedData == true` 时调用 `SeedPageData()`。MemberManagement 的 Phase 2（第二次进入）在注入前通过代码内 Home 往返自动离开-重入页面，无需 JSON 过渡阶段。

| 页面 | SeedData | 注入数据 | 延迟策略 |
|------|----------|---------|---------|
| MemberManagement（第二次进入） | `true` | 6 名示例学生 → `Students` ObservableCollection + 演示数据集 → `SavedDatasets` | 同步 |
| VenueConfiguration | `true` | 执行 `NewVenueCommand` 创建演示会场 | 命名/状态消息延迟到 `Background` |
| StrategyConfiguration | `true` | 选中 `Strategies[0]`（首个策略） | 延迟到 `Background` |
| SeatingArrangement | `true` | 演示会场+数据集 + 4×3 座位预览 | 延迟到 `Background` |
| SnapshotHistory | `true` | 1 个演示快照 → `Snapshots` | 同步 |

延迟注入使用 `Dispatcher.UIThread.Post(..., DispatcherPriority.Background)`，确保在 ViewModel 构造函数中的 fire-and-forget 异步初始化完成后执行，防止被覆盖。

`ClearPageData` 使用 `_memberManagementDemoInjected` 静态标志判断 MemberManagement 是否实际注入过演示数据，仅在实际注入时才执行清理，避免误清用户导入的数据。详见 [ADR-008](adr/ADR-008-onboarding-demo-data-injection.md)。

## 窗口状态同步（v3.1 新增）

MainWindow 订阅 `Activated` / `Deactivated` 事件，转发到 `OnboardingService`：

- **失活（最小化/Alt+Tab）**：`HandleWindowDeactivated()` 设 `_isWindowObscured=true`，调用 `Guide.Close()` 静默关闭 Popup。`HandleGuideClosedAsync()` 检测到此标志后跳过确认对话框，不结束引导。
- **激活（恢复/切回）**：`HandleWindowActivated()` 重置标志，调用 `Guide.Show()` 从保留的 `CurrentIndex` 恢复。

这解决了 Guide 的 3 个 Popup（`ShouldUseOverlayLayer=False`，原生 OS 窗口）在窗口最小化时可能残留为孤儿窗口的问题。

## 测试验证

由于引导系统依赖 UI 交互，主要通过以下方式验证：

1. **完整性校验**：交叉验证 JSON 中的资源键、目标控件名、页面引用是否全部有效
2. **构建验证**：`dotnet build` 确保编译通过
3. **测试回归**：`dotnet test` 确保 335 个现有测试无回归
4. **手动验证**（在桌面环境中）：
   - 删除 `AppSettings.json` → 启动 → 验证 18 步启动引导逐一正确
   - 进入 FreeformManagement → 验证页面引导触发 → 关闭后再进入 → 验证不重复
   - 设置页 → "重新开始引导" → 验证完整重启
   - 英文语言 → 验证英文引导文本
