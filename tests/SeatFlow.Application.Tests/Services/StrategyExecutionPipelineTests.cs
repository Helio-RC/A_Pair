namespace SeatFlow.Application.Tests.Services;

public class StrategyExecutionPipelineTests
{
    [Fact]
    public async Task ExecuteAsync_StrategiesExecutedInPriorityOrder ()
    {
        // Arrange
        var strat1 = Substitute.For<ISeatingStrategy>();
        strat1.Id.Returns("s1");
        strat1.Name.Returns("Strat1");
        strat1.Priority.Returns(5);
        strat1.IsEnabled.Returns(true);
        strat1.ExecuteAsync(Arg.Any<SeatingWorkspace>() , Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new StrategyExecutionResult { Success = true }));

        var strat2 = Substitute.For<ISeatingStrategy>();
        strat2.Id.Returns("s2");
        strat2.Name.Returns("Strat2");
        strat2.Priority.Returns(10);
        strat2.IsEnabled.Returns(true);
        strat2.ExecuteAsync(Arg.Any<SeatingWorkspace>() , Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new StrategyExecutionResult { Success = true }));

        var pipeline = new StrategyExecutionPipeline([strat1 , strat2]);
        var workspace = new SeatingWorkspace(new List<Student>() , new List<Seat>());

        // Act
        await pipeline.ExecuteAsync(workspace , cancellationToken: CancellationToken.None);

        // Assert: strat2 (priority 10) called before strat1 (priority 5)
        Received.InOrder(() =>
        {
            strat2.ExecuteAsync(workspace , Arg.Any<CancellationToken>());
            strat1.ExecuteAsync(workspace , Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task ExecuteAsync_DisabledStrategyNotCalled ()
    {
        var strat = Substitute.For<ISeatingStrategy>();
        strat.IsEnabled.Returns(false);
        var pipeline = new StrategyExecutionPipeline([strat]);
        var workspace = new SeatingWorkspace(new List<Student>() , new List<Seat>());

        await pipeline.ExecuteAsync(workspace , cancellationToken: CancellationToken.None);

        await strat.DidNotReceive().ExecuteAsync(Arg.Any<SeatingWorkspace>() , Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ProgressReported ()
    {
        var strat1 = Substitute.For<ISeatingStrategy>();
        strat1.Priority.Returns(1);
        strat1.IsEnabled.Returns(true);
        strat1.ExecuteAsync(Arg.Any<SeatingWorkspace>() , Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new StrategyExecutionResult { Success = true }));

        var strat2 = Substitute.For<ISeatingStrategy>();
        strat2.Priority.Returns(2);
        strat2.IsEnabled.Returns(true);
        strat2.ExecuteAsync(Arg.Any<SeatingWorkspace>() , Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new StrategyExecutionResult { Success = true }));

        var pipeline = new StrategyExecutionPipeline([strat1 , strat2]);
        var workspace = new SeatingWorkspace(new List<Student>() , new List<Seat>());
        var progress = Substitute.For<IProgress<SeatingProgress>>();

        await pipeline.ExecuteAsync(workspace , progress , CancellationToken.None);

        // 报告两次进度（每个策略一次）
        progress.Received(2).Report(Arg.Any<SeatingProgress>());
    }

    [Fact]
    public async Task ExecuteAsync_FailureLoggedButNotThrown ()
    {
        var strat = Substitute.For<ISeatingStrategy>();
        strat.Priority.Returns(1);
        strat.IsEnabled.Returns(true);
        strat.ExecuteAsync(Arg.Any<SeatingWorkspace>() , Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new StrategyExecutionResult { Success = false , Message = "Error" }));

        var pipeline = new StrategyExecutionPipeline([strat]);
        var workspace = new SeatingWorkspace(new List<Student>() , new List<Seat>());

        // 默认不抛出异常
        var plan = await pipeline.ExecuteAsync(workspace , cancellationToken: CancellationToken.None);
        plan.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_CancellationThrows ()
    {
        var strat = Substitute.For<ISeatingStrategy>();
        strat.Priority.Returns(1);
        strat.IsEnabled.Returns(true);
        strat.ExecuteAsync(Arg.Any<SeatingWorkspace>() , Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new StrategyExecutionResult { Success = true }));

        var pipeline = new StrategyExecutionPipeline([strat]);
        var workspace = new SeatingWorkspace(new List<Student>() , new List<Seat>());
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await pipeline.Awaiting(p => p.ExecuteAsync(workspace , cancellationToken: cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAsync_PluginStrategiesMixedWithBuiltIn_ExecutedByPriority ()
    {
        // 插件策略 Priority=20（最高），内置策略 Priority=5
        var plugin = Substitute.For<IPluginSeatingStrategy>();
        plugin.Id.Returns("p1");
        plugin.Name.Returns("Plugin1");
        plugin.Priority.Returns(20);
        plugin.IsEnabled.Returns(true);
        plugin.ExecuteAsync(Arg.Any<IPluginWorkspace>() , Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PluginStrategyResult { Success = true }));

        var builtIn = Substitute.For<ISeatingStrategy>();
        builtIn.Id.Returns("s1");
        builtIn.Name.Returns("Strat1");
        builtIn.Priority.Returns(5);
        builtIn.IsEnabled.Returns(true);
        builtIn.ExecuteAsync(Arg.Any<SeatingWorkspace>() , Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new StrategyExecutionResult { Success = true }));

        var pipeline = new StrategyExecutionPipeline([builtIn] , [plugin]);
        var workspace = new SeatingWorkspace(new List<Student>() , new List<Seat>());

        // Act
        await pipeline.ExecuteAsync(workspace , cancellationToken: CancellationToken.None);

        // Assert: 插件（priority 20）先于内置（priority 5）执行
        Received.InOrder(() =>
        {
            plugin.ExecuteAsync(Arg.Any<IPluginWorkspace>() , Arg.Any<CancellationToken>());
            builtIn.ExecuteAsync(workspace , Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task ExecuteAsync_DisabledPluginStrategyNotCalled ()
    {
        var plugin = Substitute.For<IPluginSeatingStrategy>();
        plugin.Id.Returns("p1");
        plugin.Priority.Returns(20);
        plugin.IsEnabled.Returns(false);

        var pipeline = new StrategyExecutionPipeline([plugin]);
        var workspace = new SeatingWorkspace(new List<Student>() , new List<Seat>());

        await pipeline.ExecuteAsync(workspace , cancellationToken: CancellationToken.None);

        await plugin.DidNotReceive().ExecuteAsync(Arg.Any<IPluginWorkspace>() , Arg.Any<CancellationToken>());
    }
}