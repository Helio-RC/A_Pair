namespace SeatFlow.Core.Strategies
{
    /// <summary>
    /// RandomFill 执行上下文，提供给依赖策略的评估接口。
    /// 依赖策略通过此上下文了解当前重试状态、记录警告/错误消息。
    /// </summary>
    public interface IRandomFillContext
    {
        /// <summary>当前分配对已重掷的次数。</summary>
        int RerollCount { get; }

        /// <summary>每个 (student, seat) 对的最大重掷次数，超过后兜底强制分配。</summary>
        int MaxRerolls { get; }

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
        /// 记录一条信息消息，执行结束后展示在 UI 侧栏中。
        /// </summary>
        /// <param name="strategyId">策略内部 ID。</param>
        /// <param name="displayName">策略展示名称。</param>
        /// <param name="messageKey">对应 manifest messages 中的 i18n 键。</param>
        /// <param name="args">string.Format 参数。</param>
        void LogInfo (string strategyId , string displayName , string messageKey , params object?[] args);
    }
}
