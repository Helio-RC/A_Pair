# ADR-011: 全局键盘快捷键系统

## Status
Accepted

## Date
2026-07-23

## Context

SeatFlow 作为桌面应用，缺乏标准键盘快捷键支持。用户希望在排座操作中能用键盘完成撤销/重做/保存/删除/取消等操作，并能在设置中按需开关每个快捷键。

需要在以下约束下实现：
- 不与 TextBox 等文本控件的内置快捷键冲突
- 支持设置页面中独立开关每个快捷键
- 不影响现有的拖放交互和 Canvas 缩放

## Decision

### 1. 全局快捷键采用静态 Behavior + Tunnel 路由

在 `MainWindow` 级别注册 `KeyDownEvent`（`RoutingStrategies.Tunnel`），通过 `MainShellViewModel.CurrentViewModel` 判断当前活跃页，将快捷键分派到对应 ViewModel 的命令。

**选择理由**：
- 与现有的 `ChineseInputNormalizer.Attach(mainWindow)` 模式一致，在 `App.axaml.cs` 中注册
- Tunnel 路由保证在子控件处理之前拦截，但 TextBox 焦点检查确保文本编辑不受影响
- 不修改任何 View 文件，零侵入性

**文件**：`Behaviors/KeyboardShortcutHandler.cs`

### 2. 保存命令通过反射分派（不引入接口）

各 ViewModel 的保存命令由 CommunityToolkit.Mvvm 源生成器自动生成，名称因 ViewModel 而异（`SaveCommand`、`SaveVenueCommand`、`SaveLayoutCommand` 等）。通过反射按优先级查找已约定的属性名，对 `CancellationToken` 参数的命令传 `null`（CommunityToolkit.Mvvm 自动替换为 `CancellationToken.None`）。

**选择理由**：
- 不强制所有 ViewModel 实现统一接口（避免不必要的耦合）
- 新增 ViewModel 只需在 `SaveCommandNames` 数组添加一行
- 反射仅在用户按 Ctrl+S 时触发，非热路径

### 3. Ctrl+滚轮缩放独立开关

`ZoomOnScroll` behavior 原本不检查 Ctrl 修饰键（任意滚轮即缩放），修改为仅在 `e.KeyModifiers.HasFlag(KeyModifiers.Control)` 且配置开关启用时缩放。普通滚轮的 `e.Handled` 保持 `false`，ScrollViewer 正常滚动。

**文件**：`Behaviors/ZoomOnScroll.cs`

### 4. 快捷键开关通过静态配置同步

`KeyboardShortcutHandler` 持有 `internal static KeyboardShortcutConfig ShortcutConfig` 属性。`SettingsViewModel` 在加载和保存设置时调用 `SyncShortcutConfig()` 将 ViewModel 属性同步到此静态配置。ZoomOnScroll 和 KeyboardShortcutHandler 在执行前检查配置。

**选择理由**：
- 避免在按键热路径上执行异步 I/O
- 静态属性在 AppDomain 内全局可见，无需 DI

### 5. 引导阶段点按页面而非步骤呈现

Guide 控件（CodeWF.AvaloniaControls）内部通过 `SyncIndicator()` 将 `Guide.StepCount` 和 `CurrentIndex` 同步到 `GuideIndicator`。查阅源码后发现 `Guide.StepCount` 是 `DirectProperty` 且仅有 getter 注册、CLR setter 为 `private`，外部无法修改；且 `RefreshStepCollection()` 在每次 `StepsSource` 赋值时强制重置为 `_activeSteps.Count`。

方案：在 `OnStepOpened` 事件中（`SyncIndicator` 执行之后），通过 `Dispatcher.UIThread.Post(..., Background)` 延迟覆盖 `_guide.Indicator.StepCount` 和 `_guide.Indicator.ActiveIndex` 为阶段数量和当前阶段索引。不影响 Guide 内部的步骤导航（Next/Previous 仍按步骤切换）。

**文件**：`Services/OnboardingService.cs`

## Alternatives Considered

### Avalonia KeyBinding（XAML 声明式）
```xml
<UserControl.KeyBindings>
    <KeyBinding Gesture="Ctrl+Z" Command="{Binding UndoCommand}" />
</UserControl.KeyBindings>
```
- **拒绝原因**：命令绑定在 UserControl 的 DataContext 上，但 `UndoCommand` 在深层嵌套的 `CurrentViewModel` 中，路径复杂。且无法区分 TextBox 焦点状态，会与文本编辑快捷键冲突。

### 抽象 ISaveable 接口
- **拒绝原因**：各 ViewModel 的保存方法签名不同（有的异步、有的用 CancellationToken、有的是 void），强行统一接口增加维护成本却无实质收益。

### 设置 Guide.StepCount（CLR setter 反射）
- **拒绝原因**：`GoTo(int index)` 内部校验 `index < StepCount`，设置 `StepCount=9` 会导致第 9 步之后的步骤无法导航。必须保持 `StepCount=24` 以保证导航正确。

## Consequences

- 6 个快捷键全部可在设置页面独立开关，默认全部启用
- 新增快捷键只需在 `KeyboardShortcutHandler.OnKeyDown` 的 switch 中添加 case
- 新增 ViewModel 的 Ctrl+S 支持只需在 `SaveCommandNames` 数组添加一行
- 快捷键开关持久化到 `AppSettings.json` 的 `keyboardShortcuts` 节点（默认全 true，零迁移成本）
- 引导系统阶段点渲染依赖 `DispatcherPriority.Background` 延迟覆盖——若 Guide 控件未来版本修改 `SyncIndicator` 调用时机，可能需要调整延迟优先级
