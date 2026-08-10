namespace SeatFlow.Application.Tests.Services;

public class CommandHistoryTests
{
    [Fact]
    public async Task ExecuteAsync_Success_ShouldPushUndo ()
    {
        var cmd = Substitute.For<IUndoableCommand>();
        cmd.ExecuteAsync(Arg.Any<SeatingWorkspace>() , Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var history = new CommandHistory();
        var ws = new SeatingWorkspace(new List<Student>() , new List<Seat>());
        var result = await history.ExecuteAsync(cmd , ws , TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        history.CanUndo.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_Failure_ShouldNotPush ()
    {
        var cmd = Substitute.For<IUndoableCommand>();
        cmd.ExecuteAsync(Arg.Any<SeatingWorkspace>() , Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var history = new CommandHistory();
        var ws = new SeatingWorkspace(new List<Student>() , new List<Seat>());

        await history.ExecuteAsync(cmd , ws , CancellationToken.None);

        history.CanUndo.Should().BeFalse();
    }

    [Fact]
    public async Task Undo_ShouldCallUndoAndPushRedo ()
    {
        var cmd = Substitute.For<IUndoableCommand>();
        cmd.ExecuteAsync(Arg.Any<SeatingWorkspace>() , Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        cmd.UndoAsync(Arg.Any<SeatingWorkspace>() , Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var history = new CommandHistory();
        var ws = new SeatingWorkspace(new List<Student>() , new List<Seat>());

        await history.ExecuteAsync(cmd , ws , TestContext.Current.CancellationToken);
        var result = await history.UndoAsync(ws , TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        history.CanRedo.Should().BeTrue();
        await cmd.Received(1).UndoAsync(ws , Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Redo_ShouldReExecute ()
    {
        var cmd = Substitute.For<IUndoableCommand>();
        cmd.ExecuteAsync(Arg.Any<SeatingWorkspace>() , Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        cmd.UndoAsync(Arg.Any<SeatingWorkspace>() , Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var history = new CommandHistory();
        var ws = new SeatingWorkspace(new List<Student>() , new List<Seat>());

        await history.ExecuteAsync(cmd , ws , TestContext.Current.CancellationToken);
        await history.UndoAsync(ws , TestContext.Current.CancellationToken);
        var result = await history.RedoAsync(ws , TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        history.CanUndo.Should().BeTrue(); // 应该又回到 undo 栈
        // ExecuteAsync 被调用了两次（初次执行 + redo）
        await cmd.Received(2).ExecuteAsync(ws , Arg.Any<CancellationToken>());
    }
}