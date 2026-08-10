namespace SeatFlow.Core.Tests.Strategies;

/// <summary>
/// 策略测试共享辅助类，供 DeskMate、NoRepeatDeskMate 等依赖策略测试共用。
/// </summary>
public static class StrategyTestHelpers
{
    public static List<Student> CreateStudents (params string[] ids)
    {
        return [.. ids.Select(id => new Student { Id = id , Name = id })];
    }

    public static List<GridSeat> CreateGridSeats (params (int row , int col)[] positions)
    {
        return [.. positions.Select(p => new GridSeat
        {
            Id = $"seat_{p.row}_{p.col}" ,
            Row = p.row ,
            Column = p.col
        })];
    }

    /// <summary>创建一个 IRandomFillContext 测试桩。</summary>
    public static IRandomFillContext CreateContext (int rerollCount = 0 , int maxRerolls = 10)
        => new TestContext(rerollCount , maxRerolls);

    /// <summary>
    /// IRandomFillContext 测试桩，同时收集 LogWarning / LogError 调用便于断言。
    /// </summary>
    public sealed class TestContext (int rerollCount , int maxRerolls) : IRandomFillContext
    {
        public int RerollCount => rerollCount;
        public int MaxRerolls => maxRerolls;

        public List<(string StrategyId , string DisplayName , string MessageKey , object?[] Args)> Warnings { get; } = [];
        public List<(string StrategyId , string DisplayName , string MessageKey , object?[] Args)> Errors { get; } = [];

        public void LogWarning (string strategyId , string displayName , string messageKey , params object?[] args)
            => Warnings.Add((strategyId , displayName , messageKey , args));

        public void LogError (string strategyId , string displayName , string messageKey , params object?[] args)
            => Errors.Add((strategyId , displayName , messageKey , args));

        public void LogInfo (string strategyId , string displayName , string messageKey , params object?[] args)
        {
            // 测试桩，无需记录 Info
        }
    }
}
