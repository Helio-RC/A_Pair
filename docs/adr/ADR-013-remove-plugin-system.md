# ADR-013: 移除插件系统

## 状态
已接受

## 日期
2026-09-05

## 背景
插件系统（ADR-003 的插件化扩展部分、ADR-007、ADR-012）从 1.x 版本开始引入，
包含：插件运行时（PluginManager / PluginLoadContext / ALC 卸载）、插件 SDK
（SeatFlow.Plugins.Sdk）、脚本策略（Lua / C#）、插件管理页面、`.ap-plugin` 包格式、
`src/plugin-examples/` 示例与完整文档。

实际使用中：

- 无外部插件作者接入，插件生态未形成
- 复杂度持续累积：ALC 卸载需要压紧式 GC 与 NoInlining 测试隔离，
  脚本沙箱无法真正强制中断，插件与内置策略的配置路由加深了对策略管道的理解成本
- **安全/分发风险**：动态加载外部 DLL、内嵌 Lua（NLua）与 Roslyn 脚本引擎（C# Script）
  在安装目录生成并执行未签名代码，显著提高了杀毒软件误报风险（heur/动态行为检测），
  对外分发与用户安装体验造成负面影响；会话中亦记录有 Lua 沙箱超时导致的 SIGABRT 崩溃根因
  正是脚本引擎与 ALC 生命周期的组合复杂度
- 移除后核心排座流程（7 个内置策略）不受影响

因此决定整体移除插件系统，聚焦核心排座功能：降低杀软误报风险、降低工程复杂度、
收敛策略扩展入口（新策略以内置实现加入，见 `new-strategy` issue 模板）。

## 决策

1. 删除插件运行时全部代码：`PluginManager`、`PluginLoadContext`、包清单与配置服务、
   `IPluginManager` 等（Application/Plugins 目录）
2. 删除脚本引擎：`LuaScriptStrategy` / `CSharpScriptStrategy`（Application/Scripting 目录）
   及其 NuGet 依赖 `NLua`、`Microsoft.CodeAnalysis.CSharp.Scripting`
3. 删除 `SeatFlow.Contracts` 项目（其全部内容均为插件契约：`IPluginSeatingStrategy`、
   `IPluginWorkspace`、`IPluginSeat`、`IPluginStudent` 等）与 `SeatFlow.Plugins.Sdk` 项目
4. 删除能力系统（`Capability.cs` / `IFixedSeatCapability` / `RegisterCapabilities` /
   `TryMarkFixed` 校验）。固定座位由 `FixedSeatStrategy` 直接设置 `Seat.IsFixed`（公开 setter）
5. 删除插件管理页面（`PluginManagementViewModel/View`）、`PageKey.PluginManagement`、
   相关 i18n 键（`Plugin_*`、`Nav_PluginManagement`、`Nav_PluginDisabled`、`Guide_Plugin_*`）
6. 删除 `SeatFlow.Plugin.TestFixture`、`src/plugin-examples/` 与对应测试
7. 删除相关文档：`docs/sdk/`、ADR-007、ADR-012；ADR-003 修订为纯分层架构
8. `IApplicationFacade` 移除全部 `Plugin*` 方法；策略管道仅接收内置策略
9. 用户数据目录中的遗留 `Plugins/` 目录不做删除（防破坏性操作），仅停止读取

## 后果

- 策略管道显著简化：单一 `ISeatingStrategy` / `IDependentSeatingStrategy`，
  无插件混排与适配层
- 项目数量从 10 减为 6（移除 Contracts / Plugins.Sdk / Plugin.TestFixture，
  plugin-examples 从未进入 slnx）
- NuGet 依赖移除 `NLua`、`Microsoft.CodeAnalysis.CSharp.Scripting`，
  安装目录不再含脚本引擎与动态加载代码，杀软误报面收敛
- 版本升至 2.0.0（major）：从 1.4.1 升级用户如曾安装插件，
  其插件包目录将不再被读取
- 收尾：插件相关分支（plugin-system-refactor / plugin-management-ui-refactor）
  随本次合入 mian 后删除；README/接口文档同步清理；新增 `new-strategy`
  issue 模板承接策略扩展请求
- 未来若需扩展策略，可直接在 Core 层新增内置策略实现，无需插件机制
- 与 ADR-006 无冲突：fill-in-order 管道模型完全保留
