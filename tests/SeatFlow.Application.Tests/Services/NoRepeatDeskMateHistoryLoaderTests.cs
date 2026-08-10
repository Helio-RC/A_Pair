using System.Text.Json;
using SeatFlow.Core.Providers;
using SeatFlow.Infrastructure.Serialization;

namespace SeatFlow.Application.Tests.Services;

public class NoRepeatDeskMateHistoryLoaderTests
{
    private static readonly JsonSerializerOptions VenueFileWriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    static NoRepeatDeskMateHistoryLoaderTests ()
    {
        VenueFileWriteOptions.Converters.Add(new SeatJsonConverter());
    }

    private static string BuildVenueFileJson (ClassroomLayoutDefinition layout)
    {
        var venueFile = new VenueFile { Layout = layout , Version = "1.1" };
        return JsonSerializer.Serialize(venueFile , VenueFileWriteOptions);
    }

    private static SeatingSnapshot CreateSnapshot (
        string id , ClassroomLayoutDefinition layout , Dictionary<string , string> assignments)
    {
        return new SeatingSnapshot
        {
            Id = id ,
            CreatedAt = DateTime.Now ,
            LayoutId = "v1" ,
            SeatAssignments = assignments ,
            Metadata = new Dictionary<string , object>
            {
                ["venueFile"] = BuildVenueFileJson(layout)
            }
        };
    }

    private static (ClassroomLayoutDefinition Layout , GridSeat S1 , GridSeat S2) Create2SeatGridLayout ()
    {
        var s1 = new GridSeat { Id = "seat-1" , Row = 1 , Column = 1 };
        var s2 = new GridSeat { Id = "seat-2" , Row = 1 , Column = 2 };
        var layout = new ClassroomLayoutDefinition
        {
            LayoutType = LayoutType.Grid ,
            Metadata = new GridLayoutMetadata { SeatsPerDesk = 2 , FrontRowCount = 1 } ,
            Seats = [s1 , s2]
        };
        return (layout , s1 , s2);
    }

    // ── PopulateDeskMateHistoryAsync ──

    [Fact]
    public async Task PopulateHistory_AllStudentsPresent_ProducesPairs ()
    {
        var (layout , s1 , s2) = Create2SeatGridLayout();
        var snapshot = CreateSnapshot("snap-1" , layout , new Dictionary<string , string>
        {
            [s1.Id] = "s1" ,
            [s2.Id] = "s2"
        });

        var snapshotRepo = Substitute.For<ISeatingSnapshotRepository>();
        snapshotRepo.ListByVenueAsync("v1" , Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SeatingSnapshot>>([snapshot]));

        var loader = new NoRepeatDeskMateHistoryLoader(snapshotRepo);

        var students = new List<Student>
        {
            new() { Id = "s1" , Name = "s1" } ,
            new() { Id = "s2" , Name = "s2" }
        };
        var workspace = new SeatingWorkspace(students , [s1 , s2]);
        var strategy = new NoRepeatDeskMateStrategy();

        await loader.PopulateDeskMateHistoryAsync(workspace , "v1" , 10 , strategy , CancellationToken.None);

        // 验证：s1-s2 是历史同桌对，s2 在相邻座位时 s1 应被拒绝
        workspace.TryAssignSeat(s2.Id , "s2" , out _);
        var context = new TestRandomFillContext(rerollCount: 0 , maxRerolls: 10);
        var result = await strategy.EvaluateAsync(
            workspace , students[0] , s1 , context , CancellationToken.None);

        result.Approved.Should().BeFalse();
    }

    [Fact]
    public async Task PopulateHistory_EmptyVenueId_SkipsLoading ()
    {
        var snapshotRepo = Substitute.For<ISeatingSnapshotRepository>();
        var workspace = new SeatingWorkspace([] , []);
        var strategy = new NoRepeatDeskMateStrategy();

        await new NoRepeatDeskMateHistoryLoader(snapshotRepo)
            .PopulateDeskMateHistoryAsync(workspace , "" , 10 , strategy , CancellationToken.None);

        await snapshotRepo.DidNotReceive().ListByVenueAsync(Arg.Any<string>() , Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PopulateHistory_NoSnapshots_ClearsHistory ()
    {
        var snapshotRepo = Substitute.For<ISeatingSnapshotRepository>();
        snapshotRepo.ListByVenueAsync("v1" , Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SeatingSnapshot>>([]));

        var workspace = new SeatingWorkspace([] , []);
        var strategy = new NoRepeatDeskMateStrategy();

        await new NoRepeatDeskMateHistoryLoader(snapshotRepo)
            .PopulateDeskMateHistoryAsync(workspace , "v1" , 10 , strategy , CancellationToken.None);

        // 无快照 → 策略无历史 → 任意评估应 Approve
        var result = await strategy.EvaluateAsync(
            workspace , new Student { Id = "s1" } ,
            new GridSeat { Id = "seat-1" , Row = 1 , Column = 1 } ,
            new TestRandomFillContext() , CancellationToken.None);
        result.Approved.Should().BeTrue();
    }

    [Fact]
    public async Task PopulateHistory_NoEmbeddedLayout_ReturnsEmpty ()
    {
        var snapshot = new SeatingSnapshot
        {
            Id = "snap-1" ,
            CreatedAt = DateTime.Now ,
            LayoutId = "v1" ,
            SeatAssignments = new Dictionary<string , string> { ["s1"] = "s1" } ,
            Metadata = []
        };

        var snapshotRepo = Substitute.For<ISeatingSnapshotRepository>();
        snapshotRepo.ListByVenueAsync("v1" , Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SeatingSnapshot>>([snapshot]));

        var workspace = new SeatingWorkspace(
            [new() { Id = "s1" }] , [new GridSeat { Id = "seat-1" , Row = 1 , Column = 1 }]);
        var strategy = new NoRepeatDeskMateStrategy();

        await new NoRepeatDeskMateHistoryLoader(snapshotRepo)
            .PopulateDeskMateHistoryAsync(workspace , "v1" , 10 , strategy , CancellationToken.None);

        var result = await strategy.EvaluateAsync(
            workspace , new Student { Id = "s1" } ,
            new GridSeat { Id = "seat-1" , Row = 1 , Column = 1 } ,
            new TestRandomFillContext() , CancellationToken.None);
        result.Approved.Should().BeTrue();
    }

    [Fact]
    public async Task PopulateHistory_NullWorkspace_Throws ()
    {
        var snapshotRepo = Substitute.For<ISeatingSnapshotRepository>();
        var act = async () => await new NoRepeatDeskMateHistoryLoader(snapshotRepo)
            .PopulateDeskMateHistoryAsync(null! , "v1" , 10 , new NoRepeatDeskMateStrategy() , CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PopulateHistory_NullStrategy_Throws ()
    {
        var snapshotRepo = Substitute.For<ISeatingSnapshotRepository>();
        var act = async () => await new NoRepeatDeskMateHistoryLoader(snapshotRepo)
            .PopulateDeskMateHistoryAsync(new SeatingWorkspace([] , []) , "v1" , 10 , null! , CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}

/// <summary>
/// 用于测试依赖策略的 IRandomFillContext 简单实现。
/// </summary>
public class TestRandomFillContext : IRandomFillContext
{
    public int RerollCount { get; }
    public int MaxRerolls { get; }
    public List<(string StrategyId , string StrategyName , string Key , object?[] Args)> Warnings { get; } = [];

    public TestRandomFillContext (int rerollCount = 0 , int maxRerolls = 10)
    {
        RerollCount = rerollCount;
        MaxRerolls = maxRerolls;
    }

    public void LogWarning (string strategyId , string displayName , string messageKey , params object?[] args)
    {
        Warnings.Add((strategyId , displayName , messageKey , args));
    }

    public void LogError (string strategyId , string displayName , string messageKey , params object?[] args)
    {
    }

    public void LogInfo (string strategyId , string displayName , string messageKey , params object?[] args)
    {
    }
}
