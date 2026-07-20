# ADR-009：排座次数计数器的客户端实现

## Status
Accepted

## Date
2026-07-15

## Context

SeatFlow 官网首页展示一个公开的"排座次数"统计卡片，由后端 API（`seatflow.work/api/counters`）维护累加计数器。API 使用 token+nonce 防滥用机制：

```
GET  /api/counters/token?name=arrangements  → { token, nonce, expiresAt }
POST /api/counters/public/increment         → Body: { name, value, token, nonce }
```

桌面客户端需要在每次成功生成座位安排后递增计数，并在离开座位安排页面时上报累计值。

核心约束：
- **独立性**：此计数器独立于 OpenTelemetry 遥测系统（遥测需用户同意，计数器始终启用）
- **非阻塞**：网络上报不应阻塞页面导航或关闭窗口
- **容错**：网络故障时静默丢弃当前批次（计数器为近似统计，非精确账本）
- **单例生命周期**：计数器在应用生命周期内累积；跨页面离开边界上报并重置

## Decision

**在 Presentation 层实现 `IArrangementCounterService` / `ArrangementCounterService`，作为单例 DI 服务。**

### 组件设计

| 组件 | 职责 |
|------|------|
| `IArrangementCounterService` | 接口：`Increment()` + `ReportAndResetAsync()` |
| `ArrangementCounterService` | 实现：`Interlocked` 原子操作 + 两阶段 HTTP 上报 |
| `SeatingArrangementViewModel` | 调用方：生成成功后 `Increment()`，`CanLeaveAsync` 返回 true 时 `ReportAndResetAsync()` |

### 数据流

```
GenerateSeatingAsync() 成功
  → _counterService.Increment()          // Interlocked.Increment

CanLeaveAsync() 返回 true
  → _ = _counterService.ReportAndResetAsync()  // fire-and-forget
      → Interlocked.Exchange(ref _count, 0)    // 原子读取+重置
      → if count > 0:
           GET  /api/counters/token?name=arrangements
           POST /api/counters/public/increment  { name, value, token, nonce }
      → 失败: Debug 日志, 静默丢弃
```

### 线程安全

- `Increment()`：`Interlocked.Increment` — 可从任意线程安全调用
- `ReportAndResetAsync()`：`Interlocked.Exchange` — 原子读取并重置，防止与并发 `Increment()` 竞争
- 并发调用 `ReportAndResetAsync()`（快速连续离开页面两次）各获取其时点的原子快照，无重复计数

### HTTP 设计

- `HttpClient` 在构造函数中创建一次（单例字段复用），匹配 `TelemetryHttpClient` 模式
- `BaseAddress` 带尾部斜杠：`"https://seatflow.work/api/"`
- 超时 10 秒，`Accept: application/json`
- Token 过期检查：`ExpiresAt <= DateTimeOffset.UtcNow` 时跳过 POST
- 计数为 0 时零 HTTP 调用（提前返回）

## Alternatives Considered

### 方案 A：在 ApplicationFacade 中计数

在 `ApplicationFacade.GenerateSeatingAsync` 中直接 `Increment()`，ViewModel 无感知。

- **Pros**：集中化，所有生成路径（UI/CLI/脚本）都覆盖
- **Cons**：Application 层需要 `IArrangementCounterService` 依赖，增加了对 Presentation 层服务的耦合；需要额外机制通知"页面离开"事件来触发上报
- **Rejected**：计数器上报的触发点（页面离开）是 UI 层概念，放在 Application 层需要倒置控制流

### 方案 B：扩展 ITelemetryService

在现有 `ITelemetryService` 中添加 `RecordArrangementCounter()` 方法，复用遥测基础设施（批量、压缩、退避）。

- **Pros**：复用已有的 `TelemetryHttpClient`、批次、压缩、熔断逻辑
- **Cons**：遥测是选择性加入的（`TelemetryConfig.Enabled = false`），而计数器应始终启用；两者的 API 端点不同（`/api/app/telemetry` vs `/api/counters`）；遥测是单向 event 流，计数器需要 token+nonce 双向交互
- **Rejected**：语义和启停策略不同，耦合会增加复杂度而非减少

### 方案 C：纯本地累加，退出时一次性上报

计数器不区分页面访问——每次生成都直接累加，仅在应用退出时上报一次。

- **Pros**：最简单的设计
- **Cons**：若应用崩溃则所有计数丢失；长时间运行会话的计数值延迟数小时才可见
- **Rejected**：页面离开是自然的上报边界，且提供了合理的上报频率（用户完成工作→切换页面→即时更新统计）

### 方案 D：每次生成后立即实时上报

不做累积，每次 `Increment()` 后立即发送 HTTP POST。

- **Pros**：数据最实时
- **Cons**：用户快速连续生成时产生大量 HTTP 请求；token+nonce 机制使每次上报都需要两次 API 调用（GET + POST）；网络抖动时增加失败率
- **Rejected**：页面离开时批量上报在数据新鲜度和网络开销之间取得了更好的平衡

## Consequences

- **正面**：
  - 计数器始终启用，独立于遥测选择加入机制
  - 网络故障不影响核心功能（导航、关闭窗口）
  - 原子操作保证线程安全，无锁设计
  - 与现有服务模式一致（接口/实现分离、单例 DI、直接 `HttpClient`）
- **负面**：
  - 网络故障时当前批次的累积计数丢失（近似统计的可接受代价）
  - 应用崩溃时内存计数丢失（同上）
  - Token 有效期依赖——当前 token 获取后立即使用（同一方法调用），若将来逻辑演化为延迟上报，需注意 token 过期窗口
- **中性**：
  - API 基址硬编码在服务中（`internal const string`）——与 `TelemetryHttpClient` 的配置驱动模式不同（该模式通过 `TelemetryConfig.ServerUrl` 从 `AppSettings.json` 读取）。若未来需要可配置性，改为构造函数参数即可，无需修改接口
