using SeatFlow.Core.Workspace;

namespace SeatFlow.Core.Strategies
{
    /// <summary>
    /// 座位安排策略接口，定义所有排座策略的通用契约。
    /// </summary>
    /// <remarks>
    /// <b>执行模型："按优先级填空"（Fill-in-Order）</b>
    /// <para>
    /// 所有策略操作同一个 <see cref="Workspace.SeatingWorkspace"/> 实例，
    /// 按 <see cref="Priority"/> 降序依次执行（数值越大越先执行）。
    /// 先执行的策略从空座中选择，后执行的策略在剩余空座中择优。
    /// 不存在"覆盖"——先占的座不会被推翻。
    /// </para>
    /// <list type="bullet">
    /// <item>高 Priority（先执行）= 优先挑选座位。IsFixed=true 的座位被 GetEmptySeats() 自动排除，形成天然保护</item>
    /// <item>低 Priority（后执行）= 在剩余空座中工作。最终兜底策略确保全场填满</item>
    /// </list>
    /// <para>
    /// 内置策略：FixedSeat(100) → FrontRowRotation(50) → DeskMate(50) → RandomFill(1)。
    /// 策略间的冲突解决 = Priority 数值本身——先到先得。
    /// 参见 docs/adr/ADR-006.md。
    /// </para>
    /// </remarks>
    public interface ISeatingStrategy
    {
        /// <summary>策略唯一标识符。</summary>
        string Id { get; }

        /// <summary>策略名称（用于日志和 UI 展示）。</summary>
        string Name { get; }

        /// <summary>
        /// 执行优先级，数值越大越先执行（降序）。
        /// 管道采用"按优先级填空"模型：先执行的策略先占用座位，后执行的在剩余空座中择优。
        /// 内置策略：FixedSeat=100（锁定固定座）, FrontRowRotation=50（填前排）,
        /// DeskMate=50（同桌分组）, RandomFill=1（最终兜底）。
        /// </summary>
        int Priority { get; set; }

        /// <summary>策略是否启用。</summary>
        bool IsEnabled { get; set; }

        /// <summary>
        /// 执行策略逻辑，操作 <see cref="SeatingWorkspace"/> 进行座位分配。
        /// </summary>
        /// <param name="workspace">当前工作区，包含学生和座位数据。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>执行结果，包含成功状态和消息。</returns>
        Task<StrategyExecutionResult> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken);

        /// <summary>
        /// 验证策略配置是否有效。
        /// </summary>
        /// <returns>验证结果。</returns>
        ValidationResult ValidateConfiguration ();
    }

    /// <summary>
    /// 策略执行结果，包含成功状态和描述消息。
    /// </summary>
    public record StrategyExecutionResult
    {
        /// <summary>是否执行成功。</summary>
        public bool Success { get; init; }

        /// <summary>执行结果描述消息。</summary>
        public string Message { get; init; } = string.Empty;
    }

    /// <summary>
    /// 配置验证结果。
    /// </summary>
    public record ValidationResult
    {
        /// <summary>配置是否有效。</summary>
        public bool IsValid { get; init; } = true;

        /// <summary>验证失败时的错误描述。</summary>
        public string? Error { get; init; }
    }
}