# ADR-004: 使用策略模式实现座位安排引擎

## 状态
已接受

## 日期
2025-12（项目启动时）

## 背景
座位安排系统需要支持多种安排规则，且规则需要满足以下约束：

- 可组合：多个规则按优先级顺序依次执行（例如：先分配固定座位，再安排同桌，最后随机填充）
- 可扩展：允许第三方通过插件添加新规则
- 可配置：每个规则有各自的配置参数，用户可启用/禁用、调整优先级
- 后执行规则可覆盖先执行规则的分配结果

## 决策
使用**策略模式** + **优先级管道执行**：

- `ISeatingStrategy` 接口定义策略契约（`Id`、`Name`、`Priority`、`IsEnabled`、`ExecuteAsync`、`ValidateConfiguration`）
- `StrategyExecutionPipeline` 按 `Priority` 降序排列策略，依次执行
- 每个策略接收 `SeatingWorkspace`（座位视图的隔离副本），可以分配、取消分配座位
- 所有修改记录到工作区的变更日志，最终构建 `SeatingPlan`

## 考虑的替代方案

### 单一算法 + 配置开关
- 优点：实现简单，无接口抽象
- 缺点：条件分支爆炸（`if (fixedSeat) ... if (deskmate) ... if (frontRow) ...`）；无法扩展第三方策略
- 拒绝：不满足可扩展性需求

### 规则引擎（如 NRules）
- 优点：声明式规则定义，支持冲突解决
- 缺点：引入重量级依赖；规则语言学习成本高；性能不确定
- 拒绝：项目只有 4-10 个策略，规则引擎的复杂度远超需求

### 责任链模式
- 优点：每个处理器决定是否处理或传递给下一个
- 缺点：责任链中每个环节要么处理要么跳过，不支持"后执行覆盖前执行"语义
- 拒绝：策略模式的优先级 + 覆盖语义更匹配需求（后执行策略可以覆盖之前的分配）

## 后果
- 最初四个内置策略按执行优先级排序（高→低）：`FixedSeatStrategy`(100) > `FrontRowRotationStrategy`(50) > `DeskMateStrategy`(50, 依赖) > `RandomFillStrategy`(1)。依赖策略在 RandomFill 上下文中运行，详见 ADR-006。后续 ADR-006（补充）新增了 `GenderRestrictedSeatStrategy`(45, 依赖)、`NoRepeatDeskMateStrategy`(40, 依赖) 和 `DefragStrategy`(0, 独立)，当前共 7 个策略（4 独立 + 3 依赖）
- `SeatingWorkspace` 提供隔离的座位视图 — 策略修改的是副本，原始数据不受影响
- 每步执行后记录变更日志，支持细粒度的撤销/重做
