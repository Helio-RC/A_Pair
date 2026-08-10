using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SeatFlow.Application.Commands;
using SeatFlow.Core.Workspace;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

/// <summary>
/// 座位交换命令，实现 <see cref="IUndoableCommand"/>，支持撤销/重做。
/// 支持空座位参与交换（一端为空 = 移动学生到空位）。
/// </summary>
/// <param name="seatA">源座位（SeatId, StudentId，StudentId为null表示空位）。</param>
/// <param name="seatB">目标座位。</param>
public class SwapSeatCommand (
    (string SeatId , string? StudentId) seatA ,
    (string SeatId , string? StudentId) seatB) : IUndoableCommand
{
    public string Id { get; } = Guid.NewGuid().ToString();

    private readonly (string SeatId , string? StudentId) _seatA = seatA;
    private readonly (string SeatId , string? StudentId) _seatB = seatB;

    /// <inheritdoc />
    public Task<bool> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken = default)
    {
        if (!TryClearBoth(workspace)) return Task.FromResult(false);

        if (_seatA.StudentId != null)
            workspace.TryAssignSeat(_seatB.SeatId , _seatA.StudentId , out _);
        if (_seatB.StudentId != null)
            workspace.TryAssignSeat(_seatA.SeatId , _seatB.StudentId , out _);

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> UndoAsync (SeatingWorkspace workspace , CancellationToken cancellationToken = default)
    {
        if (!TryClearBoth(workspace)) return Task.FromResult(false);

        if (_seatA.StudentId != null)
            workspace.TryAssignSeat(_seatA.SeatId , _seatA.StudentId , out _);
        if (_seatB.StudentId != null)
            workspace.TryAssignSeat(_seatB.SeatId , _seatB.StudentId , out _);

        return Task.FromResult(true);
    }

    private bool TryClearBoth (SeatingWorkspace workspace)
    {
        var seatA = workspace.FindSeats(s => s.Id == _seatA.SeatId).FirstOrDefault();
        var seatB = workspace.FindSeats(s => s.Id == _seatB.SeatId).FirstOrDefault();
        if (seatA == null || seatB == null) return false;

        seatA.OccupantId = null;
        seatA.IsAvailable = true;
        seatB.OccupantId = null;
        seatB.IsAvailable = true;
        return true;
    }
}
