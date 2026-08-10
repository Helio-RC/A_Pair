# ADR-012: 插件一级类型架构与插件系统重构

- **状态**: Accepted
- **日期**: 2026-08-10
- **决策者**: Helio-RC
- **影响范围**: 插件系统、SDK、策略管线、脚本宿主、配置存储

---

## 背景

插件系统自 ADR-007（双层清单）以来存在以下问题（按严重度）：

1. **双重适配包装链**：脚本策略实现 `ISeatingStrategy` → `Lua/CSharpScriptPluginAdapter` 包装为 `IPluginSeatingStrategy` → 再经 `PluginStrategyAdapter` 包装回 `ISeatingStrategy` 才进入管线。插件不是一级策略类型。
2. **脚本插件 ID 断裂（缺陷 A）**：脚本策略 `Id = Guid.NewGuid()`，与 manifest id 不一致 → 配置保存/加载路由错误。
3. **AssemblyLoadContext 卸载泄漏（缺陷 B）**：单包卸载/刷新从不调用 `context.Unload()`；`_contexts` 列表只增不减；失败路径泄漏 context。
4. **UI 安装通道缺失**：`.ap-plugin` 安装 API 存在但无 UI 入口。
5. **能力系统双通道**：`IPluginSeat.IsFixed` setter 允许绕过能力声明直接修改固定状态。
6. **包格式 v1 无法表达插件类型**（无 kind 字段），无法为数据提供器/导出器预留扩展点。

此时没有任何外部插件存在，允许破坏性变更。

## 决策

### 1. 插件成为一级策略类型（去包装链）

- 删除 `PluginStrategyAdapter`、`LuaScriptPluginAdapter`、`CSharpScriptPluginAdapter` 三个类。
- `StrategyExecutionPipeline` 直接接受 `IEnumerable<ISeatingStrategy>` + `IEnumerable<IPluginSeatingStrategy>`，
  内部统一为私有执行项（Id/Name/Priority/IsEnabled/Execute 委托）按 Priority 混排执行。
- `LuaScriptStrategy` / `CSharpScriptStrategy` 改为直接实现 `IPluginSeatingStrategy`，
  构造注入 manifest id（修复缺陷 A）；脚本内部仅通过 `IPluginWorkspace` 表面操作。
- 依赖链事实修正：`Core` 已引用 `Contracts`，`SeatingWorkspace : IPluginWorkspace`——
  插件执行时直接传入 workspace，无需类型转换。

### 2. 插件依赖策略接入（Contracts 新接口 + Core 适配器）

- `IPluginDependentSeatingStrategy`（Contracts）：`EvaluateAsync(IPluginWorkspace, IPluginStudent, IPluginSeat, IPluginRandomFillContext, ct)`。
- `IPluginRandomFillContext`：暴露 `RerollCount`/`MaxRerolls` + 无 strategyId 的 Log 方法。
- `PluginDependentAdapter`（Core，因 Core 已引用 Contracts）：包装为 `IDependentSeatingStrategy`
  注入 RandomFill 的评估循环；`Student : IPluginStudent`、`Seat : IPluginSeat` 直接转发无需映射。
- `ApplicationFacade` 装配：`isIndependent: false` 的插件 → 适配器 → `RandomFill.LoadDependentStrategies`。

### 3. 包格式 v2：`plugins[]` + `kind`（不兼容 v1）

- `plugins-manifest.json` 的 `strategies[]` → `plugins[]`，每个条目增加 `kind`：
  `"strategy"`（已实现）、`"data-provider"`/`"exporter"`（预留）。
- 未支持的 kind 加载时 LogWarning 并跳过；包内所有条目被跳过时不注册包。
- **不保留 v1 兼容层**（无现存插件，避免死代码）。
- 删除包级 entry 的 priority 语义：优先级唯一来源 = 策略 manifest `defaultPriority`。
- 插件策略 manifest 版本校验：复用 `StrategyManifestProvider.CompareVersions`
  （提升为 public）与 `MaxManifestVersion`，超限警告（兼容模式加载）。

### 4. ALC 卸载生命周期（官方模式）

- `_contexts` 从 `List<PluginLoadContext>` 改为 `List<LoadedContext>`
  （Context + PackageId + `WeakReference(trackResurrection: true)`）。
- 统一 `UnloadContexts(packageId?)`：**先清内部字典强引用**（`RemovePackageFromDictionaries`）→ `Unload()` → 弱引用循环探测。
- 加载失败路径（入口类型不符）立即 `Unload()` 并移除。
- **关键经验**：collectible ALC 的 LoaderAllocator 仅在**压缩式强制 GC**
  （`GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true)`）中释放，
  普通 `GC.Collect()` 无法完成回收（实测验证）。
- **关键经验**：async 方法体的 JIT 状态机字段会保留局部引用（即使显式置 null），
  阻止 ALC 回收——回收验证必须放在 NoInlining 同步隔离方法中执行。

参考：https://learn.microsoft.com/en-us/dotnet/standard/assembly/unloadability

### 5. 能力单通道

`IPluginSeat.IsFixed` 移除 setter（只读）；固定座唯一修改通道 `IPluginWorkspace.TryMarkFixed`
（需要 manifest `capabilities` 声明 `"MarkFixedSeat"`）。

### 6. UI 安装通道

`PluginManagementViewModel` 实现 `IFileDropHandler`（`.ap-plugin` 拖放安装）
+ "安装插件"按钮（`IFileService` 文件选择器）→ `InstallPluginPackageAsync`。

### 7. 脚本安全边界（务实立场）

- Lua 沙箱：禁用 `io/os/package/debug/require`，覆盖 `import = function() end`
  （阻止 NLua 的 .NET 程序集访问通道，官方推荐做法）。
- **超时无法强制中断脚本**：实测证明 Lua `lua_error`（longjmp）跨托管栈导致进程崩溃（SIGABRT）、
  托管异常不传播、Task 取消无效——与 Roslyn C# 脚本同理（.NET 进程内无线程 abort）。
  因此超时语义为：返回失败 + 脚本后台继续运行至自行结束（或进程退出），
  期间**绝不与运行中的 Lua state 并发 Dispose**（由执行线程 finally 负责）。
- 白名单/禁库是**功能限制而非安全边界**：脚本在宿主进程内 FullTrust 执行，
  仅应从可信来源安装（SDK 文档声明）。
- 参考：NLua 官方 README "Sandboxing" 章节；微软 "Security and On-the-Fly Code Generation" 文档。

### 8. 代码收敛

`ValidateZipSafety`（压缩炸弹/路径遍历）收敛到 `Contracts.Utilities.PluginArchiveSafety`，
宿主（Application）与打包工具（SDK）共享，消除双份逻辑漂移。

## 后果

### 正面

- 插件策略为一级类型：pipeline 直接执行，无包装层心智负担。
- 缺陷 A/B/C/F 全部修复；脚本插件配置路由正确；热重载可回收旧程序集。
- 包格式 v2 为数据提供器/导出器预留类型扩展点。
- 依赖策略通道打通（此前 TODO 警告"默认批准一切"已移除）。

### 负面/权衡

- v2 格式破坏 v1 兼容（当前无插件，风险可接受）。
- 脚本超时无法真正中断（宿主的根本限制，文档已声明）。
- 插件卸载的循环 GC（≤10 轮压缩收集）在低频操作（刷新/卸载）可接受。
