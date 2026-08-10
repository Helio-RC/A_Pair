namespace SeatFlow.Contracts.Models;

/// <summary>
/// 插件视角的工作区契约，暴露排座策略所需的最小 API 表面。
/// Core 层的 <c>SeatingWorkspace</c> 实现此接口。
/// </summary>
public interface IPluginWorkspace
{
    /// <summary>学生列表（只读）。</summary>
    IReadOnlyList<IPluginStudent> Students { get; }

    /// <summary>尝试将学生分配到座位。</summary>
    bool TryAssignSeat (string seatId , string studentId , out string error);

    /// <summary>获取所有空座位。</summary>
    IEnumerable<IPluginSeat> GetEmptySeats ();

    /// <summary>按条件查找座位。</summary>
    IEnumerable<IPluginSeat> FindSeats (Func<IPluginSeat , bool> predicate);

    /// <summary>获取当前座位分配（座位 ID → 学生 ID）。</summary>
    IReadOnlyDictionary<string , string> GetAssignments ();

    /// <summary>
    /// 记录一条信息消息，执行结束后展示在 UI 侧栏中。
    /// </summary>
    /// <param name="strategyId">策略内部 ID。</param>
    /// <param name="displayName">策略展示名称。</param>
    /// <param name="messageKey">对应 manifest messages 中的 i18n 键。</param>
    /// <param name="args">string.Format 参数。</param>
    void LogInfo (string strategyId , string displayName , string messageKey , params object?[] args);

    /// <summary>
    /// 记录一条警告消息，执行结束后展示在 UI 侧栏中。
    /// </summary>
    /// <param name="strategyId">策略内部 ID。</param>
    /// <param name="displayName">策略展示名称。</param>
    /// <param name="messageKey">对应 manifest messages 中的 i18n 键。</param>
    /// <param name="args">string.Format 参数。</param>
    void LogWarning (string strategyId , string displayName , string messageKey , params object?[] args);

    /// <summary>
    /// 记录一条错误消息，执行结束后展示在 UI 侧栏中。
    /// </summary>
    void LogError (string strategyId , string displayName , string messageKey , params object?[] args);

    /// <summary>
    /// 【能力：MarkFixedSeat】将指定座位标记为固定并（可选）分配学生。
    /// 需要在 manifest <c>capabilities</c> 中声明 <c>"MarkFixedSeat"</c>。
    /// 未声明时调用返回 false 并自动记录 LogWarning。
    /// </summary>
    /// <param name="seatId">座位 ID。</param>
    /// <param name="studentId">学生 ID（可为 null，仅标记座位固定不分配学生）。</param>
    /// <param name="strategyId">调用策略 ID。</param>
    /// <param name="displayName">策略展示名称。</param>
    /// <param name="error">失败原因。</param>
    bool TryMarkFixed (string seatId , string? studentId , string strategyId , string displayName , out string error);
}
