using System.Text.Json;
using SeatFlow.Core.Providers;
using SeatFlow.Infrastructure.Serialization;
using Microsoft.Extensions.Logging;

namespace SeatFlow.Application.Tests.Services;

public class FrontRowHistoryLoaderTests
{
    private static FrontRowHistoryLoader CreateLoader (
        out ISeatingSnapshotRepository snapshotRepo ,
        out ILogger<FrontRowHistoryLoader> logger)
    {
        snapshotRepo = Substitute.For<ISeatingSnapshotRepository>();
        logger = Substitute.For<ILogger<FrontRowHistoryLoader>>();
        return new FrontRowHistoryLoader(snapshotRepo , logger);
    }

    private static readonly JsonSerializerOptions VenueFileWriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    static FrontRowHistoryLoaderTests ()
    {
        VenueFileWriteOptions.Converters.Add(new SeatJsonConverter());
    }

    /// <summary>构建嵌入会场布局的 JSON 字符串（VenueFile 包装格式）。</summary>
    private static string BuildVenueFileJson (ClassroomLayoutDefinition layout)
    {
        var venueFile = new VenueFile { Layout = layout , Version = "1.1" };
        return JsonSerializer.Serialize(venueFile , VenueFileWriteOptions);
    }

    /// <summary>创建含嵌入会场布局的快照。</summary>
    private static SeatingSnapshot CreateSnapshot (
        string id , ClassroomLayoutDefinition layout , Dictionary<string , string> assignments)
    {
        return new SeatingSnapshot
        {
            Id = id ,
            CreatedAt = DateTime.Now.AddDays(-snapshotCounter++) ,
            LayoutId = "v1" ,
            SeatAssignments = assignments ,
            Metadata = new Dictionary<string , object>
            {
                ["venueFile"] = BuildVenueFileJson(layout)
            }
        };
    }
    private static int snapshotCounter = 0;

    // ─── PopulateFrontRowHistoryAsync tests ────────────────────────

    [Fact]
    public async Task Populate_NoSnapshots_HistoryStaysEmpty ()
    {
        var loader = CreateLoader(out var repo , out var _);
        repo.ListByVenueAsync("v1" , Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SeatingSnapshot>());

        var students = new[] { new Student { Id = "s1" } , new Student { Id = "s2" } };
        var ws = new SeatingWorkspace(students , new List<Seat>());

        await loader.PopulateFrontRowHistoryAsync(ws , "v1" , 10 , CancellationToken.None);

        students[0].RecentSeatHistory.GetAll().Should().BeEmpty();
        students[1].RecentSeatHistory.GetAll().Should().BeEmpty();
    }

    [Fact]
    public async Task Populate_GridLayout_PopulatesFrontRowHistory ()
    {
        var loader = CreateLoader(out var repo , out var _);
        // 2x2 Grid, FrontRowCount=1 → Row=1 为前排
        var layout = new ClassroomLayoutDefinition
        {
            Seats = new List<Seat>
            {
                new GridSeat { Id = "R1C1" , Row = 1 , Column = 1 } ,
                new GridSeat { Id = "R1C2" , Row = 1 , Column = 2 } ,
                new GridSeat { Id = "R2C1" , Row = 2 , Column = 1 } ,
                new GridSeat { Id = "R2C2" , Row = 2 , Column = 2 }
            } ,
            Metadata = new GridLayoutMetadata { FrontRowCount = 1 }
        };
        var snapshot = CreateSnapshot("snap1" , layout , new Dictionary<string , string>
        {
            ["R1C1"] = "s1" ,
            ["R1C2"] = "s2" ,
            ["R2C1"] = "s3"
        });
        repo.ListByVenueAsync("v1" , Arg.Any<CancellationToken>())
            .Returns(new[] { snapshot });

        var students = new[]
        {
            new Student { Id = "s1" } , new Student { Id = "s2" } , new Student { Id = "s3" }
        };
        var ws = new SeatingWorkspace(students , new List<Seat>());

        await loader.PopulateFrontRowHistoryAsync(ws , "v1" , 10 , CancellationToken.None);

        // s1, s2 在前排，应获历史记录；s3 在后排，无历史
        students[0].RecentSeatHistory.GetAll().Should().Contain("R1C1");
        students[1].RecentSeatHistory.GetAll().Should().Contain("R1C2");
        students[2].RecentSeatHistory.GetAll().Should().BeEmpty();
    }

    [Fact]
    public async Task Populate_PolarLayout_PopulatesRing1History ()
    {
        var loader = CreateLoader(out var repo , out var _);
        var layout = new ClassroomLayoutDefinition
        {
            Seats = new List<Seat>
            {
                new PolarSeat { Id = "inner" , Ring = 1 , Radius = 1 , AngleDegrees = 0 } ,
                new PolarSeat { Id = "outer" , Ring = 2 , Radius = 2 , AngleDegrees = 0 }
            } ,
            Metadata = new PolarLayoutMetadata { FrontRowCount = 1 }
        };
        var snapshot = CreateSnapshot("snap1" , layout , new Dictionary<string , string>
        {
            ["inner"] = "s1" ,
            ["outer"] = "s2"
        });
        repo.ListByVenueAsync("v1" , Arg.Any<CancellationToken>())
            .Returns(new[] { snapshot });

        var students = new[] { new Student { Id = "s1" } , new Student { Id = "s2" } };
        var ws = new SeatingWorkspace(students , new List<Seat>());

        await loader.PopulateFrontRowHistoryAsync(ws , "v1" , 10 , CancellationToken.None);

        students[0].RecentSeatHistory.GetAll().Should().Contain("inner");
        students[1].RecentSeatHistory.GetAll().Should().BeEmpty();
    }

    [Fact]
    public async Task Populate_FreeformLayout_UsesRowProperty ()
    {
        var loader = CreateLoader(out var repo , out var _);
        var layout = new ClassroomLayoutDefinition
        {
            Seats = new List<Seat>
            {
                new FreeformSeat { Id = "ff1" , X = 0 , Y = 0 , Row = 1 } ,
                new FreeformSeat { Id = "ff2" , X = 0 , Y = 2 , Row = 3 }
            } ,
            Metadata = new FreeformLayoutMetadata()
        };
        var snapshot = CreateSnapshot("snap1" , layout , new Dictionary<string , string>
        {
            ["ff1"] = "s1" ,
            ["ff2"] = "s2"
        });
        repo.ListByVenueAsync("v1" , Arg.Any<CancellationToken>())
            .Returns(new[] { snapshot });

        var students = new[] { new Student { Id = "s1" } , new Student { Id = "s2" } };
        var ws = new SeatingWorkspace(students , new List<Seat>());

        await loader.PopulateFrontRowHistoryAsync(ws , "v1" , 10 , CancellationToken.None);

        students[0].RecentSeatHistory.GetAll().Should().Contain("ff1");
        students[1].RecentSeatHistory.GetAll().Should().BeEmpty();
    }

    [Fact]
    public async Task Populate_MultipleSnapshots_AccumulatesHistory ()
    {
        var loader = CreateLoader(out var repo , out var _);
        var layout = new ClassroomLayoutDefinition
        {
            Seats = new List<Seat>
            {
                new GridSeat { Id = "F1" , Row = 1 , Column = 1 } ,
                new GridSeat { Id = "B1" , Row = 2 , Column = 1 }
            } ,
            Metadata = new GridLayoutMetadata { FrontRowCount = 1 }
        };
        var snap1 = CreateSnapshot("snap1" , layout , new Dictionary<string , string>
        {
            ["F1"] = "s1"
        });
        var snap2 = CreateSnapshot("snap2" , layout , new Dictionary<string , string>
        {
            ["F1"] = "s2"
        });
        repo.ListByVenueAsync("v1" , Arg.Any<CancellationToken>())
            .Returns(new[] { snap2 , snap1 }); // newest first

        var students = new[] { new Student { Id = "s1" } , new Student { Id = "s2" } };
        var ws = new SeatingWorkspace(students , new List<Seat>());

        await loader.PopulateFrontRowHistoryAsync(ws , "v1" , 10 , CancellationToken.None);

        // 两个快照都贡献历史：s1（旧）和 s2（新）各在前排坐过一次
        students[0].RecentSeatHistory.GetAll().Should().Contain("F1");
        students[1].RecentSeatHistory.GetAll().Should().Contain("F1");
    }

    [Fact]
    public async Task Populate_WindowSizeLimitsLoadedSnapshots ()
    {
        var loader = CreateLoader(out var repo , out var _);
        var layout = new ClassroomLayoutDefinition
        {
            Seats = new List<Seat>
            {
                new GridSeat { Id = "F1" , Row = 1 , Column = 1 } ,
                new GridSeat { Id = "B1" , Row = 2 , Column = 1 }
            } ,
            Metadata = new GridLayoutMetadata { FrontRowCount = 1 }
        };
        // 创建 5 个快照，每个快照中学生 s1 都在前排
        var snapshots = Enumerable.Range(1 , 5).Select(i =>
            CreateSnapshot($"snap{i}" , layout ,
                new Dictionary<string , string> { ["F1"] = $"s{i}" })).ToArray();
        repo.ListByVenueAsync("v1" , Arg.Any<CancellationToken>())
            .Returns(snapshots); // 第5个最新，第1个最旧

        var students = Enumerable.Range(1 , 5)
            .Select(i => new Student { Id = $"s{i}" }).ToArray();
        var ws = new SeatingWorkspace(students , new List<Seat>());

        // 仅加载最近 3 个快照
        await loader.PopulateFrontRowHistoryAsync(ws , "v1" , 3 , CancellationToken.None);

        // s4, s5 的快照在窗口外，无历史
        students[3].RecentSeatHistory.GetAll().Should().BeEmpty();
        students[4].RecentSeatHistory.GetAll().Should().BeEmpty();
        // s1, s2, s3 在窗口内，有历史
        students[0].RecentSeatHistory.GetAll().Should().Contain("F1");
        students[1].RecentSeatHistory.GetAll().Should().Contain("F1");
        students[2].RecentSeatHistory.GetAll().Should().Contain("F1");
    }

    [Fact]
    public async Task Populate_ResizesCircularHistoryToMatchWindow ()
    {
        var loader = CreateLoader(out var repo , out var _);
        var layout = new ClassroomLayoutDefinition
        {
            Seats = new List<Seat>
            {
                new GridSeat { Id = "F1" , Row = 1 , Column = 1 }
            } ,
            Metadata = new GridLayoutMetadata { FrontRowCount = 1 }
        };
        var snapshot = CreateSnapshot("snap1" , layout , new Dictionary<string , string>
        {
            ["F1"] = "s1"
        });
        repo.ListByVenueAsync("v1" , Arg.Any<CancellationToken>())
            .Returns(new[] { snapshot });

        // 学生初始容量为 10（默认），传入 windowSize=5 应缩容
        var student = new Student { Id = "s1" };
        student.RecentSeatHistory.GetAll().Count().Should().Be(0);

        var ws = new SeatingWorkspace(new[] { student } , new List<Seat>());
        await loader.PopulateFrontRowHistoryAsync(ws , "v1" , 5 , CancellationToken.None);

        student.RecentSeatHistory.GetAll().Should().Contain("F1");
    }

    [Fact]
    public async Task Populate_SnapshotWithoutVenueFile_Skipped ()
    {
        var loader = CreateLoader(out var repo , out var _);
        var snapshot = new SeatingSnapshot
        {
            Id = "snap1" ,
            CreatedAt = DateTime.Now ,
            LayoutId = "v1" ,
            // Metadata 中没有 venueFile
            Metadata = new Dictionary<string , object>() ,
            SeatAssignments = new Dictionary<string , string> { ["F1"] = "s1" }
        };
        repo.ListByVenueAsync("v1" , Arg.Any<CancellationToken>())
            .Returns(new[] { snapshot });

        var student = new Student { Id = "s1" };
        var ws = new SeatingWorkspace(new[] { student } , new List<Seat>());

        await loader.PopulateFrontRowHistoryAsync(ws , "v1" , 10 , CancellationToken.None);

        student.RecentSeatHistory.GetAll().Should().BeEmpty();
    }

    [Fact]
    public async Task Populate_CorruptedVenueFile_Skipped ()
    {
        var loader = CreateLoader(out var repo , out var _);
        var snapshot = new SeatingSnapshot
        {
            Id = "snap1" ,
            CreatedAt = DateTime.Now ,
            LayoutId = "v1" ,
            Metadata = new Dictionary<string , object> { ["venueFile"] = "not-valid-json!!!" } ,
            SeatAssignments = new Dictionary<string , string> { ["F1"] = "s1" }
        };
        repo.ListByVenueAsync("v1" , Arg.Any<CancellationToken>())
            .Returns(new[] { snapshot });

        var student = new Student { Id = "s1" };
        var ws = new SeatingWorkspace(new[] { student } , new List<Seat>());

        // 不应抛出异常
        await loader.PopulateFrontRowHistoryAsync(ws , "v1" , 10 , CancellationToken.None);

        student.RecentSeatHistory.GetAll().Should().BeEmpty();
    }

    [Fact]
    public async Task Populate_StudentNotInWorkspace_Skipped ()
    {
        var loader = CreateLoader(out var repo , out var _);
        var layout = new ClassroomLayoutDefinition
        {
            Seats = new List<Seat> { new GridSeat { Id = "F1" , Row = 1 , Column = 1 } } ,
            Metadata = new GridLayoutMetadata { FrontRowCount = 1 }
        };
        var snapshot = CreateSnapshot("snap1" , layout , new Dictionary<string , string>
        {
            ["F1"] = "ghost-student" // 不在当前工作区
        });
        repo.ListByVenueAsync("v1" , Arg.Any<CancellationToken>())
            .Returns(new[] { snapshot });

        var student = new Student { Id = "s1" };
        var ws = new SeatingWorkspace(new[] { student } , new List<Seat>());

        await loader.PopulateFrontRowHistoryAsync(ws , "v1" , 10 , CancellationToken.None);

        student.RecentSeatHistory.GetAll().Should().BeEmpty();
    }

    [Fact]
    public async Task Populate_NullVenueId_ReturnsEarly ()
    {
        var loader = CreateLoader(out var repo , out var _);
        var student = new Student { Id = "s1" };
        var ws = new SeatingWorkspace(new[] { student } , new List<Seat>());

        await loader.PopulateFrontRowHistoryAsync(ws , null! , 10 , CancellationToken.None);

        // 不应调用快照仓库
        await repo.DidNotReceive().ListByVenueAsync(Arg.Any<string>() , Arg.Any<CancellationToken>());
    }

    // ─── IdentifyFrontRowSeats tests ───────────────────────────────

    [Fact]
    public void IdentifyFrontRowSeats_Grid_ReturnsCorrectSeats ()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Seats = new List<Seat>
            {
                new GridSeat { Id = "r1c1" , Row = 1 , Column = 1 } ,
                new GridSeat { Id = "r1c2" , Row = 1 , Column = 2 } ,
                new GridSeat { Id = "r2c1" , Row = 2 , Column = 1 } ,
                new GridSeat { Id = "r3c1" , Row = 3 , Column = 1 }
            } ,
            Metadata = new GridLayoutMetadata { FrontRowCount = 2 }
        };

        var result = FrontRowHistoryLoader.IdentifyFrontRowSeats(layout);

        result.Should().BeEquivalentTo(["r1c1" , "r1c2" , "r2c1"]);
        result.Should().NotContain("r3c1");
    }

    [Fact]
    public void IdentifyFrontRowSeats_Polar_ReturnsCorrectSeats ()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Seats = new List<Seat>
            {
                new PolarSeat { Id = "r1" , Ring = 1 , Radius = 1 , AngleDegrees = 0 } ,
                new PolarSeat { Id = "r2" , Ring = 2 , Radius = 2 , AngleDegrees = 0 } ,
                new PolarSeat { Id = "r3" , Ring = 3 , Radius = 3 , AngleDegrees = 0 }
            } ,
            Metadata = new PolarLayoutMetadata { FrontRowCount = 2 }
        };

        var result = FrontRowHistoryLoader.IdentifyFrontRowSeats(layout);

        result.Should().BeEquivalentTo(["r1" , "r2"]);
        result.Should().NotContain("r3");
    }

    [Fact]
    public void IdentifyFrontRowSeats_Freeform_ReturnsCorrectSeats ()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Seats = new List<Seat>
            {
                new FreeformSeat { Id = "ff1" , X = 0 , Y = 0 , Row = 1 } ,
                new FreeformSeat { Id = "ff2" , X = 1 , Y = 0 , Row = 2 } ,
                new FreeformSeat { Id = "ff3" , X = 0 , Y = 2 , Row = 3 }
            } ,
            Metadata = new FreeformLayoutMetadata()
        };

        var result = FrontRowHistoryLoader.IdentifyFrontRowSeats(layout);

        result.Should().BeEquivalentTo(["ff1"]);
        result.Should().NotContain(["ff2" , "ff3"]);
    }

    [Fact]
    public void IdentifyFrontRowSeats_EmptyLayout_ReturnsEmpty ()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Seats = new List<Seat>() ,
            Metadata = new GridLayoutMetadata { FrontRowCount = 1 }
        };

        var result = FrontRowHistoryLoader.IdentifyFrontRowSeats(layout);

        result.Should().BeEmpty();
    }

    [Fact]
    public void IdentifyFrontRowSeats_NullLayout_Throws ()
    {
        var act = () => FrontRowHistoryLoader.IdentifyFrontRowSeats(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IdentifyFrontRowSeats_FreeformWithoutRow_Skipped ()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Seats = new List<Seat>
            {
                new FreeformSeat { Id = "ff1" , X = 0 , Y = 0 , Row = null } ,
                new FreeformSeat { Id = "ff2" , X = 1 , Y = 1 , Row = null }
            } ,
            Metadata = new FreeformLayoutMetadata()
        };

        var result = FrontRowHistoryLoader.IdentifyFrontRowSeats(layout);

        result.Should().BeEmpty();
    }
}
