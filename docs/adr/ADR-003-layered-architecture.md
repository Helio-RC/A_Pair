# ADR-003: 采用分层架构

## 状态
已接受（2026-09 修订：移除插件化扩展设计，仅保留分层架构决策）

## 日期
2025-12（项目启动时）；2026-09 修订

## 背景
项目需要满足以下约束：

- 核心业务逻辑（座位安排策略）与 UI 和使用者基础设施解耦
- 支持多种数据源（CSV、Excel、JSON）和导出格式（Excel、PDF、CSV、图片）
- 易于测试 — 每层可独立进行单元测试

## 决策
采用经典三层架构：

```
Presentation.Avalonia → Application → Core
                                  ↘   Infrastructure → Core
```

- **Core** 层零外部依赖（仅 .NET BCL），包含实体、值对象、策略接口、领域服务
- **Application** 层编排业务逻辑：执行管道、外观模式、命令历史
- **Infrastructure** 层实现所有外部交互：数据提供者、导出器、布局构建器、仓储
- **Presentation.Avalonia** 层是桌面 UI（MVVM），通过 `IApplicationFacade` 与业务层通信

## 考虑的替代方案

### 单体项目（无分层）
- 优点：简单，无项目引用复杂度
- 缺点：耦合严重，无法独立测试；编译时间长
- 拒绝：不满足可测试性需求

### Clean Architecture / Onion Architecture
- 优点：严格的依赖反转，Core 完全不依赖任何外层
- 缺点：过度抽象（`IEntity`、`IRepository<T>` 等泛型接口），对于桌面应用而言过于复杂
- 拒绝：项目规模不需要完全的洋葱架构

### 微服务架构
- 优点：独立部署、独立扩展
- 缺点：完全不适用于桌面应用
- 拒绝：这是桌面端系统，不涉及服务端部署

## 后果
- DI 注册集中在 `ServiceCollectionExtensions.AddSeatFlowApplication()`（Application 层）和 `Program.cs`（Presentation 层）
- `IApplicationFacade` 是 UI 层与业务逻辑的唯一接触点，外观模式隐藏内部复杂度
- 测试分为三个独立项目（Core.Tests、Application.Tests、Infrastructure.Tests），对应三层
