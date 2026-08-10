using SeatFlow.Contracts.Interfaces;
using SeatFlow.Contracts.Models;
using SeatFlow.Plugins.Sdk.Abstractions;
using SeatFlow.Plugins.Sdk.Attributes;

namespace DeskPairPlugin;

/// <summary>
/// 依赖策略插件：同桌配对。
/// 在 RandomFill 的分配评估循环内运行（manifest <c>isIndependent: false</c>）：
/// 当被提议的学生尚未落座时，尝试寻找一个空邻座并自行完成分配（Handled），
/// 使两名学生坐在一起。若没有空邻座则批准 RandomFill 的原始提议。
/// </summary>
[Plugin("desk-pair" , Name = "同桌配对" , Version = "1.0.0" , Priority = 45 , Enabled = true)]
public class DeskPairStrategy : PluginStrategyBase , IPluginDependentSeatingStrategy
{
    /// <inheritdoc />
    public override Task<PluginStrategyResult> ExecuteAsync (IPluginWorkspace workspace , CancellationToken cancellationToken)
        => Task.FromResult(new PluginStrategyResult { Success = true });

    /// <inheritdoc />
    public Task<PluginDependentEvaluationResult> EvaluateAsync (
        IPluginWorkspace workspace ,
        IPluginStudent student ,
        IPluginSeat targetSeat ,
        IPluginRandomFillContext context ,
        CancellationToken ct)
    {
        // 学生已有座位（被前序分配）时不再干预
        if (workspace.GetAssignments().Values.Contains(student.Id))
            return Task.FromResult(PluginDependentResult.Approve());

        // 寻找与目标座位相邻的空座（ID 相邻的座位视为同桌候选），完成配对
        var mateSeat = FindAdjacentEmptySeat(workspace , targetSeat.Id , student.Id);
        if (mateSeat != null)
        {
            workspace.TryAssignSeat(targetSeat.Id , student.Id , out _);
            workspace.TryAssignSeat(mateSeat.Id , FindUnassignedStudent(workspace , student.Id) , out _);
            context.LogInfo("DeskPair_Handled" , student.Name , mateSeat.Id);
            return Task.FromResult(PluginDependentResult.Handled("同桌配对完成"));
        }

        return Task.FromResult(PluginDependentResult.Approve());
    }

    /// <summary>查找与指定座位相邻（ID 相差 1）的空座位。</summary>
    private static IPluginSeat? FindAdjacentEmptySeat (IPluginWorkspace workspace , string seatId , string excludeStudentId)
    {
        if (!int.TryParse(seatId.Replace("seat" , "") , out var n)) return null;
        foreach (var candidate in new[] { n - 1 , n + 1 })
        {
            var seat = workspace.FindSeats(s => s.Id == $"seat{candidate}").FirstOrDefault();
            if (seat is { IsAvailable: true , IsFixed: false })
                return seat;
        }
        return null;
    }

    private static string FindUnassignedStudent (IPluginWorkspace workspace , string excludeId)
    {
        var assigned = workspace.GetAssignments().Values.ToHashSet();
        return workspace.Students
            .Where(s => !assigned.Contains(s.Id) && s.Id != excludeId)
            .Select(s => s.Id)
            .FirstOrDefault() ?? string.Empty;
    }
}
