using SeatFlow.Core.Workspace;

namespace SeatFlow.Application.Commands;

/// <summary>
/// 移除学生命令，将指定座位上的学生移除（清空座位）。
/// 支持撤销操作，撤销时恢复原学生到座位。
/// </summary>
public class RemoveStudentCommand (string seatId) : IUndoableCommand
{
    private string? _capturedStudentId;

    /// <summary>命令唯一标识符。</summary>
    public string Id { get; } = Guid.NewGuid().ToString();

    /// <summary>目标座位 ID。</summary>
    public string SeatId { get; } = seatId;

    /// <summary>
    /// 执行移除：查找座位 → 记录当前占用者 → 清空座位。
    /// </summary>
    public Task<bool> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken = default)
    {
        var seat = workspace.FindSeats(s => s.Id == SeatId).FirstOrDefault();
        if (seat == null || seat.OccupantId == null)
            return Task.FromResult(false);

        _capturedStudentId = seat.OccupantId;
        seat.OccupantId = null;
        seat.IsAvailable = true;
        return Task.FromResult(true);
    }

    /// <summary>
    /// 撤销移除：将学生恢复到原座位（仅当座位当前为空时）。
    /// </summary>
    public Task<bool> UndoAsync (SeatingWorkspace workspace , CancellationToken cancellationToken = default)
    {
        if (_capturedStudentId == null) return Task.FromResult(false);

        var seat = workspace.FindSeats(s => s.Id == SeatId).FirstOrDefault();
        if (seat == null) return Task.FromResult(false);

        // 仅当座位当前为空时才恢复
        if (seat.OccupantId != null) return Task.FromResult(false);

        seat.OccupantId = _capturedStudentId;
        seat.IsAvailable = false;
        return Task.FromResult(true);
    }
}
