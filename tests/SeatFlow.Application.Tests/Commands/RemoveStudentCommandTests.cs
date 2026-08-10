namespace SeatFlow.Application.Tests.Commands;

public class RemoveStudentCommandTests
{
    [Fact]
    public async Task ExecuteAsync_OccupiedSeat_ShouldSucceed ()
    {
        var student = new Student { Id = "s1" };
        var seat = new GridSeat { Id = "seat1" };
        var ws = new SeatingWorkspace([student] , new Seat[] { seat });
        ws.TryAssignSeat("seat1" , "s1" , out _);

        var cmd = new RemoveStudentCommand("seat1");
        var result = await cmd.ExecuteAsync(ws , TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        seat.OccupantId.Should().BeNull();
        seat.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_EmptySeat_ShouldFail ()
    {
        var seat = new GridSeat { Id = "seat1" };
        var ws = new SeatingWorkspace([] , new Seat[] { seat });

        var cmd = new RemoveStudentCommand("seat1");
        var result = await cmd.ExecuteAsync(ws , TestContext.Current.CancellationToken);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_NonExistentSeat_ShouldFail ()
    {
        var ws = new SeatingWorkspace([] , []);
        var cmd = new RemoveStudentCommand("nonexistent");
        var result = await cmd.ExecuteAsync(ws , TestContext.Current.CancellationToken);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UndoAsync_ShouldRestoreOccupant ()
    {
        var student = new Student { Id = "s1" };
        var seat = new GridSeat { Id = "seat1" };
        var ws = new SeatingWorkspace([student] , new Seat[] { seat });
        ws.TryAssignSeat("seat1" , "s1" , out _);

        var cmd = new RemoveStudentCommand("seat1");
        await cmd.ExecuteAsync(ws , TestContext.Current.CancellationToken);
        var result = await cmd.UndoAsync(ws , TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        seat.OccupantId.Should().Be("s1");
        seat.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task UndoAsync_SeatReoccupied_ShouldFail ()
    {
        var student1 = new Student { Id = "s1" };
        var student2 = new Student { Id = "s2" };
        var seat = new GridSeat { Id = "seat1" };
        var ws = new SeatingWorkspace([student1 , student2] , new Seat[] { seat });
        ws.TryAssignSeat("seat1" , "s1" , out _);

        // 移除 s1
        var cmd = new RemoveStudentCommand("seat1");
        await cmd.ExecuteAsync(ws , TestContext.Current.CancellationToken);

        // 另一个操作把 s2 放到这个座位
        ws.TryAssignSeat("seat1" , "s2" , out _);

        // 撤销应该失败，因为座位已被其他人占据
        var result = await cmd.UndoAsync(ws , TestContext.Current.CancellationToken);
        result.Should().BeFalse();
        seat.OccupantId.Should().Be("s2"); // 未被覆盖
    }
}
