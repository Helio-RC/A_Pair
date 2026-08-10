namespace SeatFlow.Core.Tests.Strategies;

public class FrontRowRotationStrategyTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldPrioritizeStudentsWithHighScore ()
    {
        var students = new[]
        {
        new Student { Id = "s1", NeedsFrontRow = true, FrontRowPreferenceScore = 0 },
        new Student { Id = "s2", NeedsFrontRow = false, FrontRowPreferenceScore = 100 }
    };
        var seats = new[]
        {
        new GridSeat { Id = "front1", Row = 1, Column = 1 },
        new GridSeat { Id = "back1", Row = 2, Column = 1 }
    };
        var ws = new SeatingWorkspace(students , seats);

        var strategy = new FrontRowRotationStrategy();
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        var frontSeat = seats.First(s => s.Id == "front1");
        frontSeat.OccupantId.Should().Be("s1");   // 修正此处
    }

    [Fact]
    public async Task ExecuteAsync_WithHistory_ShouldReduceScore ()
    {
        // s1 has heavy front-row history, s2 has none.
        // s1 score: 0 + 0 - (2 * 10) = -20
        // s2 score: 0 + 0 - 0 = 0
        // s2 should get the front-row seat, s1 gets the back.
        var s1 = new Student { Id = "s1" , FrontRowPreferenceScore = 0 };
        s1.RecentSeatHistory.Add("f1");
        s1.RecentSeatHistory.Add("f1");

        var s2 = new Student { Id = "s2" , FrontRowPreferenceScore = 0 };

        var seats = new Seat[]
        {
            new GridSeat { Id = "f1", Row = 1, Column = 1 },
            new GridSeat { Id = "b1", Row = 2, Column = 1 },
        };
        var ws = new SeatingWorkspace(new[] { s1 , s2 } , seats);

        var strategy = new FrontRowRotationStrategy();
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        // s2 (no history, score 0) beats s1 (history penalty, score -20)
        seats.First(s => s.Id == "f1").OccupantId.Should().Be("s2");
        // s1 gets no front-row seat — strategy only fills front rows, back rows left to RandomFill
        seats.First(s => s.Id == "b1").OccupantId.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_HistoryPenalty_ShouldFavorNoHistory ()
    {
        var s1 = new Student { Id = "s1" , FrontRowPreferenceScore = 0 };
        s1.RecentSeatHistory.Add("f1");
        s1.RecentSeatHistory.Add("f1");
        var s2 = new Student { Id = "s2" , FrontRowPreferenceScore = 0 };

        var seats = new[]
        {
            new GridSeat { Id = "f1", Row = 1, Column = 1 },
            new GridSeat { Id = "b1", Row = 2, Column = 1 }
        };
        var ws = new SeatingWorkspace(new[] { s1 , s2 } , seats);

        var strategy = new FrontRowRotationStrategy();
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        seats.First(s => s.Id == "f1").OccupantId.Should().Be("s2");
    }

    [Fact]
    public void ValidateConfiguration_AlwaysValid ()
    {
        var strategy = new FrontRowRotationStrategy();
        var result = strategy.ValidateConfiguration();
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void SetFrontRowCount_DoesNotThrow ()
    {
        var strategy = new FrontRowRotationStrategy();
        strategy.SetFrontRowCount(3);
        strategy.ValidateConfiguration().IsValid.Should().BeTrue();
    }

    [Fact]
    public void Constructor_NullConfig_ShouldThrowArgumentNullException ()
    {
        var act = () => new FrontRowRotationStrategy(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteAsync_NullWorkspace_ShouldThrowArgumentNullException ()
    {
        var strategy = new FrontRowRotationStrategy();
        var act = async () => await strategy.ExecuteAsync(null! , CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteAsync_WithNeedsFrontRow_ShouldPrioritizeOverNormalStudent ()
    {
        var s1 = new Student { Id = "s1" , NeedsFrontRow = true , FrontRowPreferenceScore = 0 };
        var s2 = new Student { Id = "s2" , NeedsFrontRow = false , FrontRowPreferenceScore = 0 };

        var seats = new Seat[]
        {
            new GridSeat { Id = "f1", Row = 1, Column = 1 },
            new GridSeat { Id = "b1", Row = 2, Column = 1 },
        };
        var ws = new SeatingWorkspace(new[] { s1 , s2 } , seats);

        var strategy = new FrontRowRotationStrategy();
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        seats.First(s => s.Id == "f1").OccupantId.Should().Be("s1");
    }

    [Fact]
    public async Task ExecuteAsync_WithFrontRowPreferenceScore_ShouldAffectOrdering ()
    {
        var s1 = new Student { Id = "s1" , FrontRowPreferenceScore = 50 };
        var s2 = new Student { Id = "s2" , FrontRowPreferenceScore = 100 };

        var seats = new Seat[]
        {
            new GridSeat { Id = "f1", Row = 1, Column = 1 },
            new GridSeat { Id = "b1", Row = 2, Column = 1 },
        };
        var ws = new SeatingWorkspace(new[] { s1 , s2 } , seats);

        var strategy = new FrontRowRotationStrategy();
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        seats.First(s => s.Id == "f1").OccupantId.Should().Be("s2");
    }

    [Fact]
    public async Task ExecuteAsync_WithHistoryWeightZero_ShouldNotApplyPenalty ()
    {
        var s1 = new Student { Id = "s1" , FrontRowPreferenceScore = 100 };
        s1.RecentSeatHistory.Add("f1");
        s1.RecentSeatHistory.Add("f1");

        var s2 = new Student { Id = "s2" , FrontRowPreferenceScore = 0 };

        var seats = new Seat[]
        {
            new GridSeat { Id = "f1", Row = 1, Column = 1 },
            new GridSeat { Id = "b1", Row = 2, Column = 1 },
        };
        var ws = new SeatingWorkspace(new[] { s1 , s2 } , seats);

        var config = new FrontRowRotationStrategy.FrontRowRotationConfiguration { HistoryWeight = 0 };
        var strategy = new FrontRowRotationStrategy(config);
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        seats.First(s => s.Id == "f1").OccupantId.Should().Be("s1");
    }

    [Fact]
    public async Task ExecuteAsync_NoFrontRowSeats_ShouldSkipWithoutError ()
    {
        // FreeformSeats without Row set have no front-row concept → no front rows identified
        var student = new Student { Id = "s1" };
        var seats = new Seat[]
        {
            new FreeformSeat { Id = "ff1", X = 1, Y = 1 },
            new FreeformSeat { Id = "ff2", X = 2, Y = 1 },
        };
        var ws = new SeatingWorkspace(new[] { student } , seats);

        var strategy = new FrontRowRotationStrategy();
        var result = await strategy.ExecuteAsync(ws , CancellationToken.None);

        result.Success.Should().BeTrue();
        ws.BuildSeatingPlan().Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_PolarSeats_ShouldTreatInnermostRingAsFrontRow ()
    {
        var students = new[]
        {
            new Student { Id = "s1", FrontRowPreferenceScore = 100 },
            new Student { Id = "s2", FrontRowPreferenceScore = 0 },
        };
        // Ring=1 为最内环（靠近讲台），即前排
        var seats = new Seat[]
        {
            new PolarSeat { Id = "p_inner", Ring = 1, Radius = 1, AngleDegrees = 0 },
            new PolarSeat { Id = "p_outer", Ring = 3, Radius = 3, AngleDegrees = 0 },
        };
        var ws = new SeatingWorkspace(students , seats);

        var strategy = new FrontRowRotationStrategy();
        strategy.SetFrontRowCount(1);
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        seats.First(s => s.Id == "p_inner").OccupantId.Should().Be("s1");
    }

    [Fact]
    public async Task ExecuteAsync_FreeformSeats_ShouldUseRowPropertyForFrontRow ()
    {
        var students = new[]
        {
            new Student { Id = "s1", FrontRowPreferenceScore = 100 },
            new Student { Id = "s2", FrontRowPreferenceScore = 0 },
        };
        var seats = new Seat[]
        {
            new FreeformSeat { Id = "ff_front", X = 0, Y = 0, Row = 1 },
            new FreeformSeat { Id = "ff_back", X = 0, Y = 2, Row = 3 },
        };
        var ws = new SeatingWorkspace(students , seats);

        var strategy = new FrontRowRotationStrategy();
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        seats.First(s => s.Id == "ff_front").OccupantId.Should().Be("s1");
    }

    [Fact]
    public async Task ExecuteAsync_MultipleFrontRows_ShouldFillAllFrontRows ()
    {
        var students = new[]
        {
            new Student { Id = "s1", FrontRowPreferenceScore = 100 },
            new Student { Id = "s2", FrontRowPreferenceScore = 50 },
            new Student { Id = "s3", FrontRowPreferenceScore = 0 },
        };
        var seats = new Seat[]
        {
            new GridSeat { Id = "r1", Row = 1, Column = 1 },
            new GridSeat { Id = "r2", Row = 2, Column = 1 },
            new GridSeat { Id = "r3", Row = 3, Column = 1 },
        };
        var ws = new SeatingWorkspace(students , seats);

        var config = new FrontRowRotationStrategy.FrontRowRotationConfiguration { FrontRowCount = 2 };
        var strategy = new FrontRowRotationStrategy(config);
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        // s1 and s2 should get rows 1 and 2 (both front), s3 gets row 3
        // 注意：分配是随机的，不断言具体哪个学生坐哪个前排座位
        ws.BuildSeatingPlan().Assignments.Should().HaveCount(2);
        var frontOccupants = new[] { seats[0].OccupantId , seats[1].OccupantId };
        frontOccupants.Should().Contain("s1");
        frontOccupants.Should().Contain("s2");
        seats.First(s => s.Id == "r3").OccupantId.Should().BeNull();
    }

    [Fact]
    public void SetFrontRowCount_Zero_ShouldClampToOne ()
    {
        var strategy = new FrontRowRotationStrategy();
        strategy.SetFrontRowCount(0);
        strategy.Config.FrontRowCount.Should().Be(1);
    }

    [Fact]
    public void ValidateConfiguration_NegativeHistoryWeight_ShouldFail ()
    {
        var config = new FrontRowRotationStrategy.FrontRowRotationConfiguration { HistoryWeight = -1 };
        var strategy = new FrontRowRotationStrategy(config);
        var result = strategy.ValidateConfiguration();
        result.IsValid.Should().BeFalse();
    }
}