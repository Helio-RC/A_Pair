using SeatFlow.Core.Workspace;

namespace SeatFlow.Application.Commands;

/// <summary>
/// 分配座位命令，将指定学生分配到指定座位。
/// 支持撤销操作，撤销时恢复座位原始状态。
/// </summary>
public class AssignSeatCommand (string seatId , string studentId) : IUndoableCommand
{
    private bool _originalIsAvailable;

    /// <summary>命令唯一标识符。</summary>
    public string Id { get; } = Guid.NewGuid().ToString();

    /// <summary>目标座位 ID。</summary>
    public string SeatId { get; } = seatId;

    /// <summary>要分配的学生 ID。</summary>
    public string StudentId { get; } = studentId;

    /// <summary>
    /// 执行分配：调用 <see cref="SeatingWorkspace.TryAssignSeat"/> 进行分配。
    /// 执行前保存座位的原始可用状态，供撤销时恢复。
    /// </summary>
    public Task<bool> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken = default)
    {
        var seat = workspace.FindSeats(s => s.Id == SeatId).FirstOrDefault();
        if (seat != null)
            _originalIsAvailable = seat.IsAvailable;

        var ok = workspace.TryAssignSeat(SeatId , StudentId , out _);
        return Task.FromResult(ok);
    }

    /// <summary>
    /// 撤销分配：清除指定座位的占用者，恢复原始可用状态。
    /// 仅在当前占用者与命令记录的学生 ID 一致时执行撤销。
    /// </summary>
    public Task<bool> UndoAsync (SeatingWorkspace workspace , CancellationToken cancellationToken = default)
    {
        var seat = workspace.FindSeats(s => s.Id == SeatId).FirstOrDefault();
        if (seat == null) return Task.FromResult(false);
        if (seat.OccupantId == StudentId)
        {
            seat.OccupantId = null;
            seat.IsAvailable = _originalIsAvailable;
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }
}
