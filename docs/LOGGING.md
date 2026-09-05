# 日志系统

SeatFlow 使用 **Serilog 4** + `Microsoft.Extensions.Logging.ILogger<T>` 作为日志基础设施，通过文件 Sink 输出结构化日志。本文档描述日志系统的配置、使用规范与最佳实践。

## 架构概览

```
AppSettings.json (Logging.MinimumLevel + CategoryOverrides)
        │
        ▼
ServiceCollectionExtensions.AddSeatFlowApplication()
        │
        ├── 读取 LogSettings
        ├── Debugger.IsAttached? → 自动降级为 Debug
        ├── 构建 LoggerConfiguration
        │     ├── MinimumLevel.Is(logLevel)
        │     ├── Enrich.WithThreadId()
        │     ├── foreach CategoryOverrides → MinimumLevel.Override()
        │     └── WriteTo.File(path, outputTemplate)
        │
        ▼
    Log.Logger (全局静态)
        │
        ▼
    services.AddLogging(b => b.AddSerilog(...))
        │
        ▼
    各层通过 ILogger<T> 构造函数注入
```

## 日志等级

| 等级 | 数值 | 使用场景 | 示例 |
|------|------|---------|------|
| **Critical** | 5 | 不可恢复的致命错误，进程即将终止 | UI 线程卡死强杀进程、退出超时强制终止 |
| **Error** | 4 | 操作失败，但进程可继续运行 | 策略执行失败、文件写入异常、导出回退 |
| **Warning** | 3 | 可恢复的异常，或预期内的降级处理 | 操作超时（已弹窗）、配置缺失、哈希不匹配 |
| **Information** | 2 | 关键生命周期事件 | 策略开始/完成、文件加载/保存、导航切换、快照创建/回滚 |
| **Debug** | 1 | 开发调试信息 | 命令执行细节、内部状态变更、文件对话框交互 |
| **Trace** | 0 | 极细粒度诊断（保留，当前未使用） | 循环内每次迭代、逐座位评估 |

### 等级语义规则

- **Critical** 仅用于进程即将 `Environment.Exit()` 的场景——全库仅 2 处（`WatchdogService`、`App.axaml.cs` 退出超时）
- **Error** 用于意料之外的失败：异常被 catch 但操作无法完成
- **Warning** 用于预期内的降级：超时（已弹窗告知用户）、配置缺失回退默认值、自动修复的冲突
- **Information** 用于用户可感知的操作：文件 I/O、策略执行、导航、快照——默认生产级别
- **Debug** 用于开发者诊断：命令栈变更、内部状态、详细参数
- **Trace** 保留给需要逐迭代日志的极细粒度场景（当前无使用）

## 配置

### AppSettings.json

日志配置存储在 `AppSettings.json` 的 `Logging` 节：

```json
{
  "Logging": {
    "MinimumLevel": "Information",
    "FileSizeLimitBytes": 10485760,
    "RetainedFileCountLimit": 30,
    "CategoryOverrides": {
      "Core.Strategies": "Debug",
      "Infrastructure.Exporters": "Debug",
    }
  }
}
```

### 字段说明

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `MinimumLevel` | string | `"Information"` | 全局最低日志等级。调试运行时（`Debugger.IsAttached`）自动降为 `"Debug"` |
| `FileSizeLimitBytes` | long | `10_485_760` (10 MB) | 单个日志文件大小上限，超出后自动滚动 |
| `RetainedFileCountLimit` | int | `30` | 保留的日志文件数量上限（旧文件自动清理） |
| `CategoryOverrides` | dict | `{}` | 分模块日志等级覆盖（详见下文） |

### 调试运行时自动降级

当调试器附加时（IDE 中 F5 或 `dotnet run` 配合调试器），系统自动将日志等级降为 `Debug`，无需修改配置文件。这使得：

- **开发时**（IDE 调试）→ 自动输出 Debug 及以上日志，包含 ThreadId
- **生产时**（独立运行）→ 默认 Information，日志简洁
- **显式配置** `"MinimumLevel": "Warning"` 且调试器附加 → 仍降为 Debug（因为你正在调试，需要详细信息）

实现：`System.Diagnostics.Debugger.IsAttached && logLevel > LogEventLevel.Debug`

## 分模块等级覆盖

`CategoryOverrides` 允许对不同模块设置独立的日志等级。Key 为去掉 `SeatFlow.` 前缀的命名空间前缀，代码自动补全。

### 标准模块前缀

| 常量 (`LogModule`) | 前缀 | 覆盖范围 |
|---------------------|------|---------|
| `Strategies` | `Core.Strategies` | FixedSeat、RandomFill、Defrag、DeskMate、GenderRestrictedSeat、NoRepeatDeskMate、FrontRowRotation |
| `Exporters` | `Infrastructure.Exporters` | Excel、CSV、PDF、Image 导出器 |
| `Providers` | `Infrastructure.Providers` | StudentProvider、VenueRepository、AppSettingsRepository、ConfigRepository 等 |
| `Repositories` | `Infrastructure.Repositories` | SeatingSnapshotRepository |
| `Migration` | `Infrastructure.Migration` | FileMigrationService、Migrators |
| `SeatSets` | `Infrastructure.Services` | SeatSetsService |
| `Pipeline` | `Application.Services` | ApplicationFacade、StrategyExecutionPipeline、CommandHistory 等 |
| `ViewModels` | `Presentation.Avalonia.ViewModels` | 所有页面 ViewModel |
| `Navigation` | `Presentation.Avalonia.Services` | NavigationService、DialogService、FileService 等 |
| `Onboarding` | `Presentation.Avalonia.Services.OnboardingService` | 引导系统 |
| `Watchdog` | `Presentation.Avalonia.Services.WatchdogService` | UI 线程看门狗 |

### 覆盖规则

- `MinimumLevel.Override` 使用 Serilog 的 `SourceContext` **前缀匹配**
- `"Core.Strategies"` 匹配 `SeatFlow.Core.Strategies.FixedSeatStrategy`、`SeatFlow.Core.Strategies.DeskMateStrategy` 等所有该命名空间下的类
- 更具体的前缀优先：`"Core.Strategies.DeskMateStrategy"` 仅匹配 DeskMateStrategy
- 未匹配的模块沿用全局 `MinimumLevel`

### 代码引用

```csharp
using SeatFlow.Core.Logging;

// 在 AppSettings.json 中引用常量值
// "CategoryOverrides": {
//   LogModule.Strategies: "Debug",
//   LogModule.Exporters: "Warning"
// }
```

## 日志文件

### 输出位置

```
{DataDirectory}/Logs/SeatFlow_{yyyyMMdd-HHmmss}.log
```

每次启动创建独立文件，避免多实例写入冲突。示例：`SeatFlow_20260628-143052.log`

### 输出模板

**Information 及以上（调试器未附加）：**
```
2026-06-28 14:30:52.123 [Information] [SeatFlow.Core.Strategies.FixedSeatStrategy] FixedSeat 策略开始执行：5 个固定分配
```

**Debug 模式（调试器已附加时自动启用）：**
```
2026-06-28 14:30:52.456 [Debug] [SeatFlow.Core.Strategies.DeskMateStrategy] [Thread:5] DeskMate 评估：学生 张三(S001)，目标座位 A3
```

### 日志轮转

- 单个文件达到 `FileSizeLimitBytes`（默认 10 MB）后自动创建新文件
- 启动时清理超出 `RetainedFileCountLimit`（默认 30）的旧文件
- 写入每 5 秒刷新到磁盘

## ILogger<T> 使用规范

### 构造函数注入

```csharp
// 模式 A：可选 logger，不含日志也能工作
public class MyService
{
    private readonly ILogger<MyService> _logger;

    public MyService(ILogger<MyService>? logger = null)
    {
        _logger = logger ?? NullLogger<MyService>.Instance;
    }
}

// 模式 B：必需 logger（仅用于核心服务）
public class ApplicationFacade(
    ILogger<ApplicationFacade> logger)
{
    // logger 作为主构造参数，不可为 null
}
```

**约定**：Infrastructure 和 Core 层使用模式 A（可选 + NullLogger 回退）；Application 和 Presentation 的核心服务使用模式 B。

### 结构化日志

始终使用命名占位符，禁止字符串拼接：

```csharp
// ✅ 正确：结构化占位符
_logger.LogInformation("策略执行完成：{StrategyName}，分配 {Count} 名学生", name, count);

// ❌ 错误：字符串拼接
_logger.LogInformation("策略执行完成：" + name + "，分配 " + count + " 名学生");

// ✅ 正确：异常作为首个参数
_logger.LogError(ex, "Excel 导出失败: {Path}", path);

// ❌ 错误：异常丢失
_logger.LogError("Excel 导出失败: {Path}", path);
```

### 等级选择速查

```csharp
// 进程即将被杀
_logger.LogCritical("...");        // WatchdogService、exit timeout

// 操作失败
_logger.LogError(ex, "...");       // 文件 I/O 异常、策略执行异常

// 可恢复降级
_logger.LogWarning("...");         // 超时已处理、配置缺失回退、冲突已自动修复

// 生命周期事件
_logger.LogInformation("...");     // 策略开始/完成、文件保存/加载、导航、快照

// 开发诊断
_logger.LogDebug("...");           // 命令栈变更、文件对话框取消、内部状态
```

### 空 catch 块

**禁止**静默吞异常。最低限度记录一条 Warning：

```csharp
// ✅ 正确
catch (Exception ex)
{
    _logger.LogWarning(ex, "操作失败，回退到默认值");
}

// ❌ 禁止
catch { }
```

## 策略消息系统

策略执行期间产生的警告/错误通过 `SeatingWorkspace` 的消息系统收集，在 UI 侧栏展示。这与 `ILogger` 并行但独立：

| 系统 | 消费者 | 用途 |
|------|--------|------|
| `ILogger<T>` | 日志文件 | 开发者诊断、生产排查 |
| `SeatingWorkspace.Messages` | UI 侧栏 | 向用户展示策略执行中的警告/错误 |

### 消息等级

```csharp
workspace.LogInfo(strategyId, displayName, messageKey, args);     // StrategyMessageSeverity.Info
workspace.LogWarning(strategyId, displayName, messageKey, args);  // StrategyMessageSeverity.Warning
workspace.LogError(strategyId, displayName, messageKey, args);    // StrategyMessageSeverity.Error
```

所有消息同时通过 `ILogger` 以 **Debug** 等级记录到文件（避免与策略自身的 `LogInformation`/`LogWarning` 重复）。

### 双重记录模式

策略应同时使用两套系统：

```csharp
// 1. 生命周期事件 → ILogger（写入日志文件）
_logger.LogInformation("FixedSeat 策略开始执行");

// 2. 策略特定警告 → workspace（展示在 UI 侧栏 + Debug 级文件日志）
workspace.LogWarning(Id, Name, "FixedSeat_NotFound", seatId);
```

## 文件对话框日志

所有文件对话框的 catch 块必须包含操作名称以区分上下文：

```csharp
// ✅ 正确：带操作名
_logger.LogDebug(ex, "文件对话框取消或异常: 导入CSV");

// ❌ 已废弃：无操作名（无法区分）
_logger.LogDebug(ex, "文件对话框取消或异常");
```

## 依赖

| NuGet 包 | 用途 |
|----------|------|
| `Serilog` 4.x | 核心日志库 |
| `Serilog.Extensions.Logging` 10.x | `ILogger<T>` 到 Serilog 的桥接 |
| `Serilog.Sinks.File` 7.x | 文件 Sink |
| `Serilog.Enrichers.Thread` 4.x | 提供 `ThreadId` 属性（Debug 模式输出） |

## 常见问题

### Q: 如何临时开启调试日志？

在 IDE 中以调试模式启动（F5）即可，无需修改配置。或者修改 `AppSettings.json` 中 `Logging.MinimumLevel` 为 `"Debug"`。

### Q: 如何只关注某个模块的详细日志？

在 `AppSettings.json` 的 `CategoryOverrides` 中为特定模块设置 Debug：

```json
{ "CategoryOverrides": { "Core.Strategies": "Debug" } }
```

其他模块保持全局的 `"Information"` 等级。

### Q: 日志文件在哪里？

`{DataDirectory}/Logs/SeatFlow_{启动时间}.log`。可在设置页面查看和修改 DataDirectory。

### Q: 如何减少生产环境的日志量？

- 设置 `"MinimumLevel": "Warning"` 只记录警告和错误
- `"FileSizeLimitBytes"` 减小文件大小上限
- `"RetainedFileCountLimit"` 减少保留文件数
