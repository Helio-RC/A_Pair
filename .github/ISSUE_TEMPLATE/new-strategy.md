---
name: New seating strategy
about: 提议新增一个内置排座策略（新策略以内置实现加入，不作为插件）
title: "[New Strategy]"
labels: enhancement, strategy
assignees: Helio-RC

---

> 插件系统已于 2.0.0 移除（见 [ADR-013](../docs/adr/ADR-013-remove-plugin-system.md)）。
> 新策略将以**内置策略**形式实现：在 `SeatFlow.Core/Strategies/` 新增实现类 +
> `Manifests/{Id}.json` 声明式清单，无需插件运行时。

**策略名称**  
建议的策略 ID 与展示名称（如 `DeskMateNext` / "同桌相邻优先"），ID 需为 PascalCase：
- Id: `例如NoRepeatDeskMate`
- 展示名称: `例如同桌上一次`

**策略类型**（勾选）
- [ ] 独立策略（`ISeatingStrategy`，外部管道按 Priority 顺序执行）
- [ ] 依赖策略（`IDependentSeatingStrategy`，在 RandomFill 的分配循环内评估）

**期望行为**
请描述该策略在排座过程中的具体行为：
- 什么情况下生效（数据条件、布局条件）
- 如何选择座位（优先级、就近、随机、约束排除等）
- 与现有策略的先后顺序 / 优先级建议（可选）

**配置需求**（可选）
- 是否需要策略级参数（`parameters[]`，如窗口大小/开关阈值）
- 是否需要按数据集/会场配置（`codeBlocks[]`，如组列表、座位筛选）

**期望效果与验收**
给出一个简单示例：给定 N 名学生 + 某布局，期望得到什么样的排座结果。

**补充信息**
其余背景、参考实现（若参考了某内置策略可指出）、约束条件等。
