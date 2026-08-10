namespace SeatFlow.Contracts.Models;

/// <summary>
/// 插件策略执行结果。
/// </summary>
public class PluginStrategyResult
{
    /// <summary>是否执行成功。</summary>
    public bool Success { get; set; }

    /// <summary>结果描述消息。</summary>
    public string Message { get; set; } = string.Empty;
}
