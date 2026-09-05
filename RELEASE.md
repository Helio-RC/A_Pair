# SeatFlow v2.0.0 发布说明

SeatFlow 发布 2.0.0——本次为重大版本更新（major）：**彻底移除插件系统**，聚焦核心排座功能，工程结构显著精简。

## 移除

- **插件系统整体移除（ADR-013）**：
  - 删除插件运行时（`PluginManager`、`AssemblyLoadContext` 加载/卸载、包清单与配置服务）与 Lua/C# 脚本策略；移除 `NLua`、`Microsoft.CodeAnalysis.CSharp.Scripting` 两个 NuGet 依赖
  - 删除 `SeatFlow.Contracts`、`SeatFlow.Plugins.Sdk`、`SeatFlow.Plugin.TestFixture` 项目与 `src/plugin-examples/` 示例仓库
  - 删除能力系统（`Capability.cs` / `IFixedSeatCapability` / `TryMarkFixed` 校验），固定座位由 `FixedSeatStrategy` 直接设置 `Seat.IsFixed`
  - 删除插件管理页面与相关 i18n 资源、引导步骤
  - 删除插件 SDK 文档与 ADR-007/012；ADR-003 修订为纯分层架构；新增 ADR-013 记录本次决策
- 附带影响：项目数量从 10 减为 6，策略管道简化为单一内置策略类型；老用户曾安装的插件包目录不再被读取（数据目录不做破坏性清理）

## 工程

- App 版本升至 **2.0.0**；`version.json` 各文件格式版本校验通过
- 清理了 i18n 资源前缀、CI 路径过滤器与文档中全部插件引用（CHANGELOG 历史条目保留原样）

## 迁移说明

- 1.x 升级至 2.0.0：无需手动迁移，所有数据（会场/名单/快照/策略配置）兼容
- 如果曾安装过插件（`.ap-plugin`），插件将不再被加载，安装根目录下的 `Plugins/` 目录可手动删除
