using SeatFlow.Contracts.Models;
using SeatFlow.Plugins.Sdk.Abstractions;
using SeatFlow.Plugins.Sdk.Attributes;

namespace HeightSortPlugin;

/// <summary>
/// 按身高降序填充空座的独立策略：
/// 有身高数据的学生按身高从高到低优先入座，无身高数据的学生最后分配。
/// </summary>
[Plugin("height-sort" , Name = "按身高排序" , Version = "1.0.0" , Priority = 80 , Enabled = true)]
public class HeightSortStrategy : PluginStrategyBase
{
    /// <inheritdoc />
    public override Task<PluginStrategyResult> ExecuteAsync (IPluginWorkspace workspace , CancellationToken cancellationToken)
    {
        var assignedIds = workspace.GetAssignments().Values.ToHashSet();
        var emptySeats = workspace.GetEmptySeats().ToList();

        // 未分配学生：有身高者在前（降序），无身高者最后
        var students = workspace.Students
            .Where(s => !assignedIds.Contains(s.Id))
            .OrderByDescending(s => s.Height.HasValue)
            .ThenByDescending(s => s.Height ?? float.MinValue)
            .ThenBy(s => s.Name)
            .ToList();

        var count = Math.Min(students.Count , emptySeats.Count);
        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!workspace.TryAssignSeat(emptySeats[i].Id , students[i].Id , out var error))
            {
                workspace.LogWarning(Id , Name , "HeightSort_AssignFailed" , students[i].Id , emptySeats[i].Id , error);
            }
        }

        workspace.LogInfo(Id , Name , "HeightSort_Assigned" , count);
        return Task.FromResult(new PluginStrategyResult { Success = true });
    }
}
