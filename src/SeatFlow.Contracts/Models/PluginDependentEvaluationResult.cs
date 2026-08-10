namespace SeatFlow.Contracts.Models;

/// <summary>
/// 插件依赖策略的评估结果。
/// </summary>
public class PluginDependentEvaluationResult
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
/// <see cref="PluginDependentEvaluationResult"/> 的便捷工厂方法。
/// </summary>
public static class PluginDependentResult
{
    /// <summary>批准该分配。</summary>
    public static PluginDependentEvaluationResult Approve () => new() { Approved = true };

    /// <summary>拒绝该分配，请求重掷。</summary>
    public static PluginDependentEvaluationResult Reject (string? reason = null)
        => new() { Approved = false , Message = reason };

    /// <summary>已自行处理分配，RandomFill 跳过 TryAssignSeat。</summary>
    public static PluginDependentEvaluationResult Handled (string? message = null)
        => new() { Approved = true , AlreadyHandled = true , Message = message };
}
