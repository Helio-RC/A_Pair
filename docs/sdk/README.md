# SeatFlow.Plugins.Sdk — 插件开发 SDK

SeatFlow 座位安排系统的插件开发工具包。引用此 SDK 即可开发自定义排座策略插件，无需依赖主程序集。

> 本指南对应 **插件包格式 v2**（`plugins[]` + `kind`）与插件一级类型架构（ADR-012）。

---

## 插件架构概述

插件系统采用 **双层清单架构**（ADR-007 + ADR-012），一个插件包可承载多个插件子组件：

```
Plugins/{packageId}/
├── plugins-manifest.json        ← 包级清单（元数据 + plugins[] 加载指令，v2）
├── strategy_a/                  ← 策略子目录
│   ├── manifest.json            ← 策略元数据（与内置策略 StrategyManifest 格式一致）
│   └── StrategyA.dll
├── strategy_b/
│   ├── manifest.json
│   └── strategy.lua
└── data/
    └── enables.json             ← 运行时启用状态
```

**插件策略是策略管线的一级类型**：`IPluginSeatingStrategy` 直接被
`StrategyExecutionPipeline` 执行（与内置策略按 Priority 混排），不存在适配器包装层。

接口层次（通用到具体）：

```
IPlugin                          ← 所有插件的身份契约（Id, Name, Version, Category）
  ├── IPluginSeatingStrategy     ← 排座策略插件契约（+ Priority, IsEnabled, ExecuteAsync）
  └── IPluginDependentSeatingStrategy  ← 依赖策略契约（RandomFill 上下文内评估）

IPluginLifecycle                 ← 可选生命周期管理（InitializeAsync, DisposeAsync）
IPluginHost                      ← 插件初始化时获取的宿主服务入口
```

插件类型（`plugins-manifest.json` 的 `kind` 字段）：

| kind | 状态 | 说明 |
|------|------|------|
| `"strategy"` | **已实现** | 排座策略插件（独立或依赖） |
| `"data-provider"` | 预留 | 数据导入插件 |
| `"exporter"` | 预留 | 导出器插件 |

---

## 快速开始

### 1. 创建插件项目

```bash
dotnet new classlib -n MyPlugin
cd MyPlugin
dotnet add reference /path/to/SeatFlow/src/SeatFlow.Plugins.Sdk/SeatFlow.Plugins.Sdk.csproj
```

> 真实插件作者可改用 NuGet 包 `SeatFlow.Plugins.Sdk`。
> 项目需 `<TargetFramework>net10.0</TargetFramework>` 并建议设置
> `<EnableDynamicLoading>true</EnableDynamicLoading>`（生成 runtimeconfig，依赖解析更可靠）。
> **契约程序集（SeatFlow.Contracts）不应随插件输出**——运行时由宿主提供，保证类型同一性。
> 若使用 ProjectReference 引用 SDK，可在 csproj 增加：
>
> ```xml
> <Target Name="StripSharedContractAssemblies" AfterTargets="Build">
>   <Delete Files="$(OutDir)SeatFlow.Contracts.dll" />
> </Target>
> ```

### 2. 实现策略

```csharp
using SeatFlow.Contracts.Interfaces;
using SeatFlow.Contracts.Models;
using SeatFlow.Plugins.Sdk.Abstractions;
using SeatFlow.Plugins.Sdk.Attributes;

namespace MyPlugin;

[Plugin("my-first-plugin", Name = "我的第一个插件", Priority = 50)]
public class MyStrategy : PluginStrategyBase
{
    public override Task<PluginStrategyResult> ExecuteAsync(
        IPluginWorkspace workspace,
        CancellationToken cancellationToken)
    {
        var emptySeats = workspace.GetEmptySeats().ToList();
        var assigned = workspace.GetAssignments().Values.ToHashSet();
        var unassigned = workspace.Students
            .Where(s => !assigned.Contains(s.Id))
            .ToList();

        for (int i = 0; i < Math.Min(unassigned.Count, emptySeats.Count); i++)
            workspace.TryAssignSeat(emptySeats[i].Id, unassigned[i].Id, out _);

        return Task.FromResult(new PluginStrategyResult
        {
            Success = true,
            Message = $"已分配 {unassigned.Count} 名学生"
        });
    }
}
```

也可以不继承基类，直接实现 `IPluginSeatingStrategy`。

### 3. 创建清单文件（v2 格式）

需要两个清单文件：

**包级清单** — 项目根目录下 `plugins-manifest.json`：

```json
{
  "id": "my-first-package",
  "name": "我的第一个插件包",
  "version": "1.0.0",
  "author": "Your Name",
  "description": "一个简单的排座策略示例包",
  "type": "strategy",
  "plugins": [
    {
      "kind": "strategy",
      "path": "simple-fill",
      "manifest": "simple-fill/manifest.json",
      "assembly": "MyPlugin.dll",
      "entryType": "MyPlugin.MyStrategy"
    }
  ]
}
```

**策略清单** — `simple-fill/manifest.json`（策略子目录下，与内置策略格式一致）：

```json
{
  "id": "simple-fill",
  "name": "SimpleFill",
  "displayName": "简单填充",
  "version": "1.0.0",
  "manifestVersion": "1.0",
  "description": "将未分配学生按顺序填入空座位",
  "author": "Your Name",
  "category": "assignment",
  "defaultPriority": 50,
  "defaultEnabled": true,
  "visible": true,
  "isIndependent": true,
  "parameters": [],
  "codeBlocks": [],
  "messages": {}
}
```

> **字段说明**：`displayName` 是**字符串**（非字典）；优先级字段为 `defaultPriority`。
> 程序集插件用 `assembly` + `entryType`；脚本插件改用 `scriptFile` + `scriptType`
> （见下文"脚本插件"）。可参考仓库内 `src/plugin-examples/` 的真实示例。

### 4. 构建、打包与安装

```bash
dotnet build -c Release
```

- **手动部署**：把整个包目录复制到 SeatFlow 插件目录
  （开发环境 `{exeDir}/Plugins/`，安装版 `{RootAppDir}/Plugins/`）。
- **打包分发**：把包目录打成 ZIP（根含 `plugins-manifest.json`），扩展名 `.ap-plugin`，
  在插件管理页点击"安装插件"或直接拖放安装。
- 参考 `src/plugin-examples/build.sh` 的打包流程。

---

## 接口参考

### IPlugin（基础身份）

```csharp
public interface IPlugin
{
    string Id { get; }
    string Name { get; }
    string Version { get; }
    string Category { get; }
}
```

### IPluginSeatingStrategy（独立策略）

```csharp
public interface IPluginSeatingStrategy : IPlugin
{
    string IPlugin.Category => "strategy";
    string IPlugin.Version => "1.0.0";
    int Priority { get; set; }        // 越大越先执行（与内置策略共享优先级空间）
    bool IsEnabled { get; set; }
    Task<PluginStrategyResult> ExecuteAsync(IPluginWorkspace workspace, CancellationToken ct);
}
```

### IPluginDependentSeatingStrategy（依赖策略，v2 新增）

`isIndependent: false` 的插件实现此接口。**不加入外部管道**，而是在
RandomFill 每次随机分配 (student, seat) 对时被评估：

```csharp
public interface IPluginDependentSeatingStrategy : IPlugin
{
    int Priority { get; set; }        // 上下文内优先级（DeskMate 50 → Gender 45 → NoRepeat 40）
    bool IsEnabled { get; set; }
    Task<PluginDependentEvaluationResult> EvaluateAsync(
        IPluginWorkspace workspace,
        IPluginStudent student,        // 提议分配的学生
        IPluginSeat targetSeat,        // 提议分配的目标座位
        IPluginRandomFillContext context,  // 重掷计数 + 日志
        CancellationToken ct);
}
```

评估结果三态（`PluginDependentResult` 工厂）：

| 结果 | 行为 |
|------|------|
| `Approve()` | 批准该分配，RandomFill 继续 |
| `Reject(reason)` | 拒绝并请求重掷（有上限，超限后强制） |
| `Handled(msg)` | 已自行完成分配（含连携），RandomFill 跳过 |

`IPluginRandomFillContext` 暴露 `RerollCount`/`MaxRerolls` 与 `LogWarning/LogError/LogInfo(messageKey, ...)`。

> 依赖策略实现示例见 `src/plugin-examples/src/DeskPairPlugin`。

### IPluginWorkspace（插件视角的工作区）

`SeatingWorkspace` 实现此接口，插件执行时直接获得：

```csharp
public interface IPluginWorkspace
{
    IReadOnlyList<IPluginStudent> Students { get; }
    bool TryAssignSeat(string seatId, string studentId, out string error);
    IEnumerable<IPluginSeat> GetEmptySeats();
    IEnumerable<IPluginSeat> FindSeats(Func<IPluginSeat, bool> predicate);
    IReadOnlyDictionary<string, string> GetAssignments();  // 座位 ID → 学生 ID
    void LogInfo / LogWarning / LogError(string strategyId, string displayName, string messageKey, params object?[] args);
    bool TryMarkFixed(string seatId, string? studentId, string strategyId, string displayName, out string error); // 能力：MarkFixedSeat
}
```

数据视图（只读）：

```csharp
public interface IPluginStudent
{
    string Id { get; }
    string Name { get; }
    float? Height { get; }
    bool NeedsFrontRow { get; }
    int FrontRowPreferenceScore { get; }
}

public interface IPluginSeat
{
    string Id { get; }
    bool IsAvailable { get; }
    bool IsFixed { get; }        // 只读：固定座唯一修改通道是 TryMarkFixed
    string? OccupantId { get; }
}
```

> **座位保护**：声明 `"MarkFixedSeat"` 能力并调用 `TryMarkFixed` 可将座位标记为
> `IsFixed`（后续策略与碎片整理自动排除）。未声明能力时调用返回 false 并记录警告。

### IPluginLifecycle / IPluginHost

```csharp
public interface IPluginLifecycle
{
    Task InitializeAsync(IPluginHost host, CancellationToken ct);  // 加载时调用
    Task DisposeAsync();                                          // 卸载时调用
}

public interface IPluginHost
{
    IPluginConfigurationService Configuration { get; }  // 包配置的读写/监听
    string PluginDirectory { get; }
}
```

---

## 基类参考

### PluginBase

实现 `IPlugin` 身份元数据，自动从 `[Plugin]` 特性读取：

| 成员 | 来源 |
|------|------|
| `Id` | `[Plugin]` Id，缺省为随机 GUID（**不建议**） |
| `Name` | `[Plugin]` Name，缺省为类型名 |
| `Version` / `Category` | `[Plugin]` 对应字段，缺省 "1.0.0" / "strategy" |

### PluginStrategyBase : PluginBase, IPluginSeatingStrategy

额外提供 `Priority` / `IsEnabled`（从 `[Plugin]` 读取），只需实现 `ExecuteAsync`。

### PluginAttribute

```csharp
[Plugin("strategy-id", Name = "...", Version = "1.0.0",
        Priority = 50, Enabled = true, Category = "strategy")]
```

---

## 脚本插件

`kind: "strategy"` 的条目可使用 `scriptFile` + `scriptType` 代替程序集：

```json
{
  "kind": "strategy",
  "path": "lua",
  "manifest": "lua/manifest.json",
  "scriptFile": "front-row-first.lua",
  "scriptType": "lua"
}
```

| scriptType | 引擎 | 文件扩展名 |
|------------|------|-----------|
| `lua` | NLua（Lua 5.4） | `.lua` |
| `csharp` | Roslyn C# Scripting | `.csx` |

### Lua 脚本

通过全局对象 `workspace` 访问受限 API（**实例方法使用冒号语法**）：

```lua
local unassigned = workspace:GetUnassignedStudentIds()
local empty = workspace:GetEmptySeatIds()
for i = 1, math.min(#unassigned, #empty) do
    workspace:AssignSeat(empty[i], unassigned[i])
end
```

可用方法：`GetUnassignedStudentIds()`、`GetEmptySeatIds()`、`AssignSeat(seatId, studentId)`、
`GetStudent(id)`、`GetSeat(id)`。

### C# 脚本

通过全局对象 `Workspace`（`IPluginWorkspace`）访问；默认导入
`System` / `System.Linq` / `System.Collections.Generic` / `SeatFlow.Contracts.Models` 等命名空间。

```csharp
var assigned = Workspace.GetAssignments().Values.ToHashSet();
var students = Workspace.Students.Where(s => !assigned.Contains(s.Id)).ToList();
var seats = Workspace.GetEmptySeats().ToList();
for (int i = 0; i < Math.Min(students.Count, seats.Count); i++)
    Workspace.TryAssignSeat(seats[i].Id, students[i].Id, out _);
```

### ⚠️ 脚本安全边界（务必阅读）

- 脚本在**宿主进程内以完全信任（FullTrust）执行**。引用白名单与禁用库仅是
  **功能限制，不是安全边界**（C# 脚本可经反射、Lua 可经 NLua 对象逃逸）。
- **超时无法强制中断脚本**：宿主对死循环脚本的语义是"返回超时失败，脚本在后台
  继续运行直至自行结束（或进程退出）"。Roslyn 与 Lua 均无协作式中断通道。
  超时后的后台脚本仍持有工作区引用，可能与此后执行的策略**并发访问座位数据**——
  请勿编写可能死循环的脚本。
- 因此：**只安装来自可信来源的脚本插件**。内置的 `io/os/package/debug/require`
  已被禁用，`import` 被覆盖为空函数（阻止 .NET 程序集访问）。

---

## 安全与限制

| 项目 | 说明 |
|------|------|
| DLL 插件 | 在独立 `AssemblyLoadContext`（可回收）中加载；卸载后由宿主循环 GC 回收 |
| 契约共享 | `SeatFlow.Contracts` 必须由宿主提供（不随插件输出），否则类型不匹配 |
| 脚本插件 | 同进程 FullTrust 执行，仅限可信来源 |
| ZIP 炸弹 | 安装时校验条目数/总大小/压缩比/路径遍历（`PluginArchiveSafety`） |
| 能力声明 | `TryMarkFixed` 需 manifest `capabilities` 声明 `"MarkFixedSeat"` |

## 常见问题

- **"入口类型不存在或未实现 IPluginSeatingStrategy"**：`entryType` 全限定名写错，
  或 `SeatFlow.Contracts.dll` 被打包进插件（应删除）。
- **配置保存后不生效**：确认策略 manifest 的 `id` 与 `plugins-manifest.json` 一致
  （脚本插件加载时以 manifest id 作为策略 ID）。
- **manifest 版本警告**：`manifestVersion` 高于宿主支持版本时会警告（兼容模式加载）。
- **脚本超时但仍在运行**：见上文安全边界——死循环脚本无法被强制中断。

完整可运行示例见仓库 `src/plugin-examples/`（身高排序 / 依赖策略 / Lua / C# / 多策略包）。
