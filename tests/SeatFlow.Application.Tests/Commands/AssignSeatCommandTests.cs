namespace SeatFlow.Application.Tests.Commands;

public class AssignSeatCommandTests
{
    [Fact]
    public async Task ExecuteAsync_ValidAssignment_ShouldSucceed ()
    {
        var student = new Student { Id = "s1" };
        var seat = new GridSeat { Id = "seat1" };
        var ws = new SeatingWorkspace([student] , new Seat[] { seat });

        var cmd = new AssignSeatCommand("seat1" , "s1");
        var result = await cmd.ExecuteAsync(ws , TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        seat.OccupantId.Should().Be("s1");
    }

    [Fact]
    public async Task ExecuteAsync_DuplicateStudent_ShouldFail ()
    {
        var student = new Student { Id = "s1" };
        var seat1 = new GridSeat { Id = "seat1" };
        var seat2 = new GridSeat { Id = "seat2" };
        var ws = new SeatingWorkspace([student] , new Seat[] { seat1 , seat2 });
        // 预先分配 seat1 给 s1
        ws.TryAssignSeat("seat1" , "s1" , out _);

        var cmd = new AssignSeatCommand("seat2" , "s1");
        var result = await cmd.ExecuteAsync(ws , TestContext.Current.CancellationToken);

        result.Should().BeFalse();
        seat2.OccupantId.Should().BeNull();
    }

    [Fact]
    public async Task UndoAsync_ShouldClearOccupant ()
    {
        var student = new Student { Id = "s1" };
        var seat = new GridSeat { Id = "seat1" };
        var ws = new SeatingWorkspace([student] , new Seat[] { seat });

        var cmd = new AssignSeatCommand("seat1" , "s1");
        await cmd.ExecuteAsync(ws , TestContext.Current.CancellationToken);
        var result = await cmd.UndoAsync(ws , TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        seat.OccupantId.Should().BeNull();
        seat.IsAvailable.Should().BeTrue();
    }
}