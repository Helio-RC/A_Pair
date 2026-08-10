using SeatFlow.Contracts.Models;
using SeatFlow.Plugins.Sdk.Abstractions;
using SeatFlow.Plugins.Sdk.Attributes;

namespace SeatFlow.Plugin.TestFixture;

/// <summary>
/// 测试夹具策略：将空座位按 ID 升序分配给未分配学生。
/// 仅用于单元测试（ALC 卸载回收验证），不作为真实插件使用。
/// </summary>
[Plugin("fixture-sort", Name = "Fixture Sort", Version = "1.0.0", Priority = 40, Enabled = true)]
public class FixtureSortStrategy : PluginStrategyBase
{
    /// <inheritdoc />
    public override Task<PluginStrategyResult> ExecuteAsync (IPluginWorkspace workspace , CancellationToken cancellationToken)
    {
        var assignedIds = workspace.GetAssignments().Values.ToHashSet();
        var students = workspace.Students.Where(s => !assignedIds.Contains(s.Id)).ToList();
        var seats = workspace.GetEmptySeats().OrderBy(s => s.Id , StringComparer.Ordinal).ToList();

        for (int i = 0; i < Math.Min(students.Count , seats.Count); i++)
        {
            workspace.TryAssignSeat(seats[i].Id , students[i].Id , out _);
        }

        return Task.FromResult(new PluginStrategyResult { Success = true });
    }
}

/// <summary>
/// 测试夹具策略：构造函数抛异常（用于验证实例化失败路径的 ALC 清理）。
/// </summary>
[Plugin("fixture-broken", Name = "Fixture Broken", Version = "1.0.0", Priority = 40, Enabled = true)]
public class FixtureBrokenStrategy : PluginStrategyBase
{
    public FixtureBrokenStrategy ()
    {
        throw new InvalidOperationException("fixture：构造函数故意抛异常");
    }

    /// <inheritdoc />
    public override Task<PluginStrategyResult> ExecuteAsync (IPluginWorkspace workspace , CancellationToken cancellationToken)
        => Task.FromResult(new PluginStrategyResult { Success = true });
}
