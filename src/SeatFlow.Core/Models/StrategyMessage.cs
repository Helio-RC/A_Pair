namespace SeatFlow.Core.Models;

/// <summary>
/// 策略执行消息的严重级别。
/// </summary>
public enum StrategyMessageSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// 策略执行期间产生的消息。通过 <see cref="Workspace.SeatingWorkspace.LogWarning"/> /
/// <see cref="Workspace.SeatingWorkspace.LogError"/> 写入。
/// </summary>
/// <param name="Severity">消息严重级别。</param>
/// <param name="StrategyId">产生消息的策略内部 ID（如 "DeskMate"）。</param>
/// <param name="StrategyDisplayName">策略展示名称（如 "同桌分组"），便于 UI 层直接使用无需反查。</param>
/// <param name="MessageKey">对应 manifest messages 中的 i18n 键。</param>
/// <param name="Args">string.Format 参数。</param>
public record StrategyMessage (
    StrategyMessageSeverity Severity ,
    string StrategyId ,
    string StrategyDisplayName ,
    string MessageKey ,
    object?[] Args
);
