namespace SeatFlow.Core.Tests.Strategies;

public class PluginDependentAdapterTests
{
    private static (PluginDependentAdapter Adapter , IPluginDependentSeatingStrategy Inner) CreateAdapter ()
    {
        var inner = Substitute.For<IPluginDependentSeatingStrategy>();
        inner.Id.Returns("p-dep-1");
        inner.Name.Returns("DeskPair");
        inner.Priority.Returns(45);
        inner.IsEnabled.Returns(true);
        return (new PluginDependentAdapter(inner) , inner);
    }

    [Fact]
    public void Properties_ProxiedToInnerPlugin ()
    {
        var (adapter , inner) = CreateAdapter();

        adapter.Id.Should().Be("p-dep-1");
        adapter.Name.Should().Be("DeskPair");
        adapter.DisplayName.Should().Be("DeskPair");
        adapter.Priority.Should().Be(45);
        adapter.IsEnabled.Should().BeTrue();

        adapter.Priority = 30;
        adapter.IsEnabled = false;
        inner.Received().Priority = 30;
        inner.Received().IsEnabled = false;
    }

    [Fact]
    public async Task EvaluateAsync_ForwardsArgsAndMapsResult ()
    {
        var (adapter , inner) = CreateAdapter();
        inner.EvaluateAsync(Arg.Any<IPluginWorkspace>() , Arg.Any<IPluginStudent>() , Arg.Any<IPluginSeat>() , Arg.Any<IPluginRandomFillContext>() , Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(PluginDependentResult.Reject("不允许")));

        var workspace = new SeatingWorkspace([new Student { Id = "s1" }] , new Seat[] { new GridSeat { Id = "seat1" } });
        var student = new Student { Id = "s1" };
        var seat = new GridSeat { Id = "seat1" };
        var context = Substitute.For<IRandomFillContext>();
        context.RerollCount.Returns(3);
        context.MaxRerolls.Returns(10);

        var result = await adapter.EvaluateAsync(workspace , student , seat , context , CancellationToken.None);

        result.Approved.Should().BeFalse();
        result.AlreadyHandled.Should().BeFalse();
        result.Message.Should().Be("不允许");

        // 参数按原样转发（Student : IPluginStudent、Seat : IPluginSeat）
        await inner.Received(1).EvaluateAsync(
            Arg.Is<IPluginWorkspace>(w => ReferenceEquals(w , workspace)) ,
            Arg.Is<IPluginStudent>(s => ReferenceEquals(s , student)) ,
            Arg.Is<IPluginSeat>(s => ReferenceEquals(s , seat)) ,
            Arg.Any<IPluginRandomFillContext>() ,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvaluateAsync_HandledResult_MapsAlreadyHandled ()
    {
        var (adapter , inner) = CreateAdapter();
        inner.EvaluateAsync(Arg.Any<IPluginWorkspace>() , Arg.Any<IPluginStudent>() , Arg.Any<IPluginSeat>() , Arg.Any<IPluginRandomFillContext>() , Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(PluginDependentResult.Handled("已自行处理")));

        var workspace = new SeatingWorkspace([new Student { Id = "s1" }] , new Seat[] { new GridSeat { Id = "seat1" } });

        var result = await adapter.EvaluateAsync(workspace , new Student { Id = "s1" } , new GridSeat { Id = "seat1" } , Substitute.For<IRandomFillContext>() , CancellationToken.None);

        result.Approved.Should().BeTrue();
        result.AlreadyHandled.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_Context_ExposesRerollStateAndForwardsLogsWithStrategyIdentity ()
    {
        var (adapter , inner) = CreateAdapter();
        IPluginRandomFillContext? captured = null;
        inner.EvaluateAsync(Arg.Any<IPluginWorkspace>() , Arg.Any<IPluginStudent>() , Arg.Any<IPluginSeat>() , Arg.Any<IPluginRandomFillContext>() , Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                captured = ci.ArgAt<IPluginRandomFillContext>(3);
                return PluginDependentResult.Approve();
            });

        var workspace = new SeatingWorkspace([new Student { Id = "s1" }] , new Seat[] { new GridSeat { Id = "seat1" } });
        var context = Substitute.For<IRandomFillContext>();
        context.RerollCount.Returns(3);
        context.MaxRerolls.Returns(10);

        await adapter.EvaluateAsync(workspace , new Student { Id = "s1" } , new GridSeat { Id = "seat1" } , context , CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.RerollCount.Should().Be(3);
        captured.MaxRerolls.Should().Be(10);

        captured.LogWarning("Some_Key" , "a" , 1);
        context.Received(1).LogWarning("p-dep-1" , "DeskPair" , "Some_Key" , "a" , 1);

        captured.LogError("Err_Key");
        context.Received(1).LogError("p-dep-1" , "DeskPair" , "Err_Key");

        captured.LogInfo("Info_Key" , "x");
        context.Received(1).LogInfo("p-dep-1" , "DeskPair" , "Info_Key" , "x");
    }

    [Fact]
    public void ValidateConfiguration_AndDefaults_AreHarmless ()
    {
        var (adapter , _) = CreateAdapter();

        adapter.ValidateConfiguration().IsValid.Should().BeTrue();

        IDependentSeatingStrategy iface = adapter;
        iface.SetPriorAssignedStudentIds(["s9"]);
        iface.GetConstrainedStudentIds().Should().BeEmpty();
    }
}
