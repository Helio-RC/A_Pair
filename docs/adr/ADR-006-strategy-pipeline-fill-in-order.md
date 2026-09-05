# ADR-006: 策略管道采用"按优先级填空"模型

## 状态
已接受

## 日期
2026-05-31

## 背景

管道最初设计为 **"后可覆盖"（override）** 模型：低 Priority 策略先执行建立基线，高 Priority 策略后执行可推翻前序分配。架构文档和接口注释均以此模型描述。

但在实现验证中发现，该模型在当前 Workspace API 下不可行：

1. `GetEmptySeats()` 仅返回 `IsAvailable == true && !IsFixed` 的座位
2. `TryAssignSeat()` 拒绝 `IsAvailable == false` 的座位
3. 只有 `FixedSeatStrategy` 通过直接操控 `OccupantId=null; IsAvailable=true` 绕过了门禁
4. `FrontRowRotationStrategy` 和 `DeskMateStrategy` 使用标准的 `GetEmptySeats+TryAssignSeat` 路径，无法覆盖已占座位

实际管道行为：
```
RandomFill(1)     → GetEmptySeats=全空 → 填满所有座位
FrontRowRotation(30) → GetEmptySeats=0 → 跳过（死代码）
DeskMate(50)       → GetEmptySeats=0 → 跳过（死代码）
FixedSeat(100)     → 直接操控 OccupantId → 覆盖成功
```

最终排座结果 = RandomFill 随机分布 + FixedSeat 硬覆盖。前排轮换和同桌分组从未实际生效。

## 决策

采用 **"按优先级填空"（Fill-in-Order）** 模型：

- 策略按 Priority 降序执行（数值越大越先执行）
- 先执行的策略从空座中优先挑选，后执行的在剩余空座中择优
- **不存在覆盖**——先占的座位不会被推翻
- IsFixed 标志是唯一的保护机制（FixedSeat 最先执行，标记座位为 IsFixed=true，后续策略的 GetEmptySeats 自动排除这些座位）

### 新 Priority

| 策略 | Priority | 类型 | 执行顺序 | 职责 |
|------|----------|------|----------|------|
| FixedSeatStrategy | 100 | 独立 | 第1 | 锁定固定座位，IsFixed=true 自动保护 |
| FrontRowRotationStrategy | 50 | 独立 | 第2 | 在非固定空座中填前排 |
| DeskMateStrategy | 50 | 依赖 | — | 在 RandomFill 上下文中执行，检查同桌关系并协调相邻分配 |
| RandomFillStrategy | 1 | 独立+宿主 | 最后 | 填满所有剩余空座；作为依赖策略宿主 |

### 冲突解决

独立策略间的座位冲突 = Priority 数值决定（先到先得）。DeskMate 作为依赖策略在 RandomFill 内部运行，其请求重掷（Reroll）机制允许它与 RandomFill 协作寻找合适的连续块。用户可通过调整独立策略的 Priority 控制资源分配顺序，依赖策略的内部优先级独立管理。

## 考虑的替代方案

### 方案 B：给 Workspace 加 ForceAssignSeat + 孤儿追踪
- 优点：保留"后可覆盖"语义
- 缺点：FrontRowRotation 和 DeskMate 需要完全重写分配逻辑（~100+ 行改动）；需要孤儿学生追踪机制 + RandomFill 收容；管道复杂度大幅上升
- 拒绝：对 1.0-preview 风险过高，且 DeskMate 和 FrontRowRotation 在"后被覆盖"场景下的前置条件（完整连续块、空前排座）更难满足

### 方案 C：保留原 Priority 不变，只修复注释
- 优点：零代码改动
- 缺点：FrontRowRotation 和 DeskMate 仍然是死代码，管道的核心价值（前排轮换+同桌分组）无法交付
- 拒绝：不可接受的 1.0 质量问题

## 后果

### 正面
- 前排轮换和同桌分组策略现在在管道中**实际生效**
- 改动量小（仅 Priority 常量 + 文档），风险低
- 逻辑简单明了："先到先得"，易于理解和验证
- 策略间冲突由 Priority 数值显式决定，可调试

### 负面
- 失去了"后可覆盖"的灵活性——用户无法配置"即使前排已有人，也要替换为近视学生"
- DeskMate 使用前排的连续块后，FrontRowRotation 就没有前排座可用——反之亦然
- 未来如果需要真正的"覆盖"语义，需要重新设计 Workspace API（加入 ForceAssign、Swap 等操作）

### 未来方向
- 加入管道的端到端集成测试，验证 Fill-in-Order 顺序下的实际行为
- UI 层暴露 Priority 拖拽排序，让用户显式控制策略间的资源分配优先级
- 如果用户反馈需要"覆盖"行为，在 Workspace 中引入 `ForceAssignSeat` 或 `SwapSeats` 方法，同时追踪被替换学生

## 补充 (2026-06-06)：策略可见性字段控制管道参与

在声明式策略配置 manifest 中新增 `visible` 字段（`StrategyManifest.Visible`，默认 `true`）。设为 `false` 时策略**完全排除**：

- 配置页不显示（`StrategyConfigurationViewModel` 过滤）
- 座位安排页侧栏不显示（`SeatingArrangementViewModel` 过滤）
- **执行管道跳过**（`ApplicationFacade` 执行前收集 `visible=false` 的 manifest ID 并过滤策略列表）

实现：`ApplicationFacade` 在准备策略列表时，从 `StrategyManifestProvider` 获取内置 manifest 的 `Visible`，构建不可见 ID 集合，统一过滤。`StrategyExecutionPipeline` 仅执行过滤后的策略列表。

DeskMate 策略（同桌分组）默认 `visible: false`。原因：该策略受前排分配和固定座位影响较大，组员被拆散后仅能就近安插单人，对多数用户场景不适用。高级用户通过配置页手动启用后配置。

## 补充 (2026-06-06)：DeskMate 单人就近安置

DeskMate 策略的有效组过滤阈值从 `>= 2` 放宽为 `>= 1`：当原始同桌组仅剩一名未分配学生时，保留该学生并尝试将其安置到已分配组员的相邻座位。

关键设计选择：

- **统一查找路径**：单人组和多人组共用 `GetPreAssignedMembers()` 方法，通过 `FirstOrDefault` 按 GroupId 查找原始配置组，判断人数是否减少来决定是否执行 near-occupied 分配
- **Fisher-Yates 洗牌**：候选座位列表和学生列表均随机打乱，避免所有拆散学生聚类到同一区域
- **单人组不降级**：仅 `group.StudentIds.Count >= 2` 的组才会尝试水平/垂直/BFS 网格分配；单人组仅通过 near-occupied 路径处理
- **座位查找优化**：`LogWarning`/`LogError` 中 ID→姓名解析改用 `Dictionary.TryGetValue` 替代 `FirstOrDefault` 线性扫描，将 O(n×m) 降为 O(n+m)

## 补充 (2026-06-12)：依赖策略在 RandomFill 上下文中执行

### 背景

DeskMate 作为独立策略在 fill-in-order 模型中始终受前序策略碎片化影响。即使将 `visible` 设为 `false`，
也无法解决根本问题：固定座位和前排轮换必然先占座，破坏连续块。

### 决策

引入 **依赖策略（Dependent Strategy）** 概念和 `IDependentSeatingStrategy` 接口。
依赖策略不在外部管道中执行，而是在 RandomFill 的分配循环中按上下文内部优先级依次评估。

**新接口：**
- `IDependentSeatingStrategy` — 独立于 `ISeatingStrategy`，提供 `EvaluateAsync(workspace, student, targetSeat, context, ct)` 方法
- `IRandomFillContext` — 上下文对象，提供 `RerollCount`、`MaxRerolls`、`LogWarning/LogError`
- `DependentEvaluationResult` — 三种结果：`Approve()`、`Reject(reason)`、`Handled(message)`

**RandomFill 上下文循环算法：**
```
while 还有未分配学生 AND 空座位:
    随机选 (student, seat)
    rerollCount = 0
    loop:
        依次调用依赖策略 EvaluateAsync (按内部 Priority 降序)
        if Reject → rerollCount++; if >= maxRerolls 兜底强制分配 else 换座位重试
        if Handled → 依赖策略已自行分配，跳过 TryAssignSeat
        if 全部 Approve → TryAssignSeat, 刷新列表
```

**Manifest 新字段：**
- `isIndependent` (bool, 默认 true)：false 表示依赖策略
- `manifestVersion` (string, 默认 "1.0")：运行时版本校验

### 后果

**正面：**
- DeskMate 重写为依赖策略，在 RandomFill 分配时实时检测同桌关系，成功率大幅提升
- 扩展性良好——新依赖策略只需实现 `IDependentSeatingStrategy` 并注册到 DI
- DeskMate 的 `visible` 恢复为 `true`，用户可正常使用
- 添加了 manifest 版本检查，未来 manifest 格式变更可安全演进

**负面：**
- 接口数量增加（`IDependentSeatingStrategy` + `IRandomFillContext` + `DependentEvaluationResult`），学习曲线略高
- RandomFill 的上下文循环增加了复杂度
**未来方向：**
- 支持多个依赖策略在同一上下文中协同工作
- `maxRerolls` 可配置化
