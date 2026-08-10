using SeatFlow.Core.Models;
using SeatFlow.Core.Workspace;

namespace SeatFlow.Core.Strategies
{
    /// <summary>
    /// 依赖策略接口，用于在 RandomFill 上下文中进行评估和执行。
    /// 与 <see cref="ISeatingStrategy"/> 不同，依赖策略不直接加入外部执行管道，
    /// 而是在 RandomFill 每次随机分配 (student, seat) 对时被调用，
    /// 可以进行批准（Approve）、拒绝并请求重掷（Reroll）、或自行完成分配（Handled）。
    /// </summary>
    /// <remarks>
    /// <b>执行模型</b>
    /// <para>
    /// RandomFill 内部维护一个依赖策略清单（按 Priority 降序）。
    /// 每次随机选出一对 (student, seat) 后，依次调用每个启用的依赖策略的
    /// <see cref="EvaluateAsync"/>。策略可以返回三种结果：
    /// </para>
    /// <list type="bullet">
    /// <item><b>Approve</b>：批准该分配，RandomFill 继续正常 TryAssignSeat</item>
    /// <item><b>Reject</b>：请求重掷（reroll），RandomFill 重新随机选 (student, seat) 对</item>
    /// <item><b>Handled</b>：已自行处理，RandomFill 跳过 TryAssignSeat</item>
    /// </list>
    /// <para>
    /// 重掷有上限（由 <see cref="IRandomFillContext.MaxRerolls"/> 控制），
    /// 超过上限后兜底强制分配。
    /// </para>
    /// </remarks>
    public interface IDependentSeatingStrategy
    {
        /// <summary>策略唯一标识符。</summary>
        string Id { get; }

        /// <summary>策略内部名称（英文）。</summary>
        string Name { get; }

        /// <summary>策略展示名称。</summary>
        string DisplayName { get; }

        /// <summary>
        /// 上下文内部优先级，数值越大越先被评估。
        /// 此优先级独立于外部管道 <see cref="ISeatingStrategy.Priority"/>。
        /// </summary>
        int Priority { get; set; }

        /// <summary>策略是否启用。</summary>
        bool IsEnabled { get; set; }

        /// <summary>
        /// 评估 RandomFill 提议的 (student, seat) 分配对。
        /// 依赖策略可以批准、拒绝（请求重掷）或自行完成分配。
        /// </summary>
        /// <param name="workspace">当前工作区。</param>
        /// <param name="student">提议分配的学生。</param>
        /// <param name="targetSeat">提议分配的目标座位。</param>
        /// <param name="context">RandomFill 上下文，提供重掷计数和日志接口。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>评估结果。</returns>
        Task<DependentEvaluationResult> EvaluateAsync (
            SeatingWorkspace workspace ,
            Student student ,
            Seat targetSeat ,
            IRandomFillContext context ,
            CancellationToken cancellationToken);

        /// <summary>
        /// 验证策略配置是否有效。
        /// </summary>
        ValidationResult ValidateConfiguration ();

        /// <summary>
        /// 告知策略在 RandomFill 循环开始前已被前序策略（FixedSeat/FrontRowRotation）分配的学生 ID。
        /// 策略在执行时不应修改这些学生的座位。
        /// 默认不处理。
        /// </summary>
        void SetPriorAssignedStudentIds (HashSet<string> ids) { }

        /// <summary>
        /// 返回此策略认为有特殊约束、应优先分配以减少重掷的学生 ID 集合。
        /// 默认返回空集。DeskMate 覆写返回组内学生。
        /// </summary>
        HashSet<string> GetConstrainedStudentIds () => [];
    }

    /// <summary>
    /// 依赖策略的评估结果。
    /// </summary>
    public sealed class DependentEvaluationResult
    {
        /// <summary>是否批准该分配。false 表示请求重掷。</summary>
        public bool Approved { get; init; } = true;

        /// <summary>
        /// 策略是否已自行完成分配（包括连携修改相邻座位）。
        /// 设为 true 时 RandomFill 跳过自己的 TryAssignSeat 调用。
        /// </summary>
        public bool AlreadyHandled { get; init; }

        /// <summary>可选的消息（用于日志记录）。</summary>
        public string? Message { get; init; }
    }

    /// <summary>
    /// <see cref="DependentEvaluationResult"/> 的便捷工厂方法。
    /// </summary>
    public static class DependentResult
    {
        /// <summary>批准该分配。</summary>
        public static DependentEvaluationResult Approve () => new() { Approved = true };

        /// <summary>拒绝该分配，请求重掷。</summary>
        public static DependentEvaluationResult Reject (string? reason = null)
            => new() { Approved = false , Message = reason };

        /// <summary>已自行处理分配，RandomFill 跳过 TryAssignSeat。</summary>
        public static DependentEvaluationResult Handled (string? message = null)
            => new() { Approved = true , AlreadyHandled = true , Message = message };
    }
}
