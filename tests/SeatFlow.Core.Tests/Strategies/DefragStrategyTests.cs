namespace SeatFlow.Core.Tests.Strategies;

public class DefragStrategyTests
{
    // ═══════════════ ExecuteAsync 测试 ═══════════════

    [Fact]
    public async Task ExecuteAsync_ShouldMoveStudentForwardToGap ()
    {
        // Row1 有空座，Row2 有无约束学生 → 前移
        var student = new Student { Id = "s1" , Name = "s1" };
        var seats = new Seat[]
        {
            new GridSeat { Id = "r1c1" , Row = 1 , Column = 1 , IsAvailable = true } ,
            new GridSeat { Id = "r2c1" , Row = 2 , Column = 1 , IsAvailable = false , OccupantId = "s1" }
        };
        var ws = new SeatingWorkspace([student] , seats);

        var strategy = new DefragStrategy();
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        seats.First(s => s.Id == "r1c1").OccupantId.Should().Be("s1");
        seats.First(s => s.Id == "r1c1").IsAvailable.Should().BeFalse();
        seats.First(s => s.Id == "r2c1").OccupantId.Should().BeNull();
        seats.First(s => s.Id == "r2c1").IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldMoveCrossColumn ()
    {
        // Row1 Col1 空座，Row2 Col2 有无约束学生 → 跨列前移
        var student = new Student { Id = "s1" , Name = "s1" };
        var seats = new Seat[]
        {
            new GridSeat { Id = "r1c1" , Row = 1 , Column = 1 , IsAvailable = true } ,
            new GridSeat { Id = "r1c2" , Row = 1 , Column = 2 , IsAvailable = false , OccupantId = "other" } ,
            new GridSeat { Id = "r2c2" , Row = 2 , Column = 2 , IsAvailable = false , OccupantId = "s1" }
        };
        var ws = new SeatingWorkspace([student , new Student { Id = "other" , Name = "other" }] , seats);

        var strategy = new DefragStrategy();
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        // s1 应从 (2,2) 跨列移到 (1,1)
        seats.First(s => s.Id == "r1c1").OccupantId.Should().Be("s1");
        seats.First(s => s.Id == "r2c2").OccupantId.Should().BeNull();
        seats.First(s => s.Id == "r2c2").IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSkipWhenNoGaps ()
    {
        // 所有座位都被占用 → 跳过
        var student = new Student { Id = "s1" , Name = "s1" };
        var seats = new Seat[]
        {
            new GridSeat { Id = "r1c1" , Row = 1 , Column = 1 , IsAvailable = false , OccupantId = "s1" }
        };
        var ws = new SeatingWorkspace([student] , seats);

        var strategy = new DefragStrategy();
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        // 无变更
        seats.First(s => s.Id == "r1c1").OccupantId.Should().Be("s1");
        ws.Messages.Should().Contain(m => m.MessageKey == "Defrag_NoGaps");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSkipWhenOnlyGapsInBack ()
    {
        // 空座在 Row2，学生在 Row1（前方） → 无"后方"学生可移
        var student = new Student { Id = "s1" , Name = "s1" };
        var seats = new Seat[]
        {
            new GridSeat { Id = "r1c1" , Row = 1 , Column = 1 , IsAvailable = false , OccupantId = "s1" } ,
            new GridSeat { Id = "r2c1" , Row = 2 , Column = 1 , IsAvailable = true }
        };
        var ws = new SeatingWorkspace([student] , seats);

        var strategy = new DefragStrategy();
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        // s1 仍在原位
        seats.First(s => s.Id == "r1c1").OccupantId.Should().Be("s1");
        ws.Messages.Should().Contain(m => m.MessageKey == "Defrag_NoGaps");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotMoveFixedSeatStudent ()
    {
        // 后排有 IsFixed 座位上的学生 → 不移动
        var student = new Student { Id = "s1" , Name = "s1" };
        var seats = new Seat[]
        {
            new GridSeat { Id = "r1c1" , Row = 1 , Column = 1 , IsAvailable = true } ,
            new GridSeat { Id = "r2c1" , Row = 2 , Column = 1 , IsAvailable = false , OccupantId = "s1" , IsFixed = true }
        };
        var ws = new SeatingWorkspace([student] , seats);

        var strategy = new DefragStrategy();
        strategy.SetConstrainedStudentIds(["s1"]);
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        // s1 不动，前排仍空
        seats.First(s => s.Id == "r2c1").OccupantId.Should().Be("s1");
        seats.First(s => s.Id == "r1c1").OccupantId.Should().BeNull();
        seats.First(s => s.Id == "r1c1").IsAvailable.Should().BeTrue();
        ws.Messages.Should().Contain(m => m.MessageKey == "Defrag_NoGaps");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotMoveDeskMateStudent ()
    {
        // 约束集合包含某学生 → 不移动
        var student = new Student { Id = "s1" , Name = "s1" };
        var seats = new Seat[]
        {
            new GridSeat { Id = "r1c1" , Row = 1 , Column = 1 , IsAvailable = true } ,
            new GridSeat { Id = "r2c1" , Row = 2 , Column = 1 , IsAvailable = false , OccupantId = "s1" }
        };
        var ws = new SeatingWorkspace([student] , seats);

        var strategy = new DefragStrategy();
        strategy.SetConstrainedStudentIds(["s1"]);
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        // s1 不动
        seats.First(s => s.Id == "r2c1").OccupantId.Should().Be("s1");
        ws.Messages.Should().Contain(m => m.MessageKey == "Defrag_NoEligible");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFillMultipleGaps ()
    {
        // Row1 有3空座，Row2-3 有3无约束学生 → 全填
        var s1 = new Student { Id = "s1" , Name = "s1" };
        var s2 = new Student { Id = "s2" , Name = "s2" };
        var s3 = new Student { Id = "s3" , Name = "s3" };
        var seats = new Seat[]
        {
            new GridSeat { Id = "r1c1" , Row = 1 , Column = 1 , IsAvailable = true } ,
            new GridSeat { Id = "r1c2" , Row = 1 , Column = 2 , IsAvailable = true } ,
            new GridSeat { Id = "r1c3" , Row = 1 , Column = 3 , IsAvailable = true } ,
            new GridSeat { Id = "r2c1" , Row = 2 , Column = 1 , IsAvailable = false , OccupantId = "s1" } ,
            new GridSeat { Id = "r2c2" , Row = 2 , Column = 2 , IsAvailable = false , OccupantId = "s2" } ,
            new GridSeat { Id = "r3c1" , Row = 3 , Column = 1 , IsAvailable = false , OccupantId = "s3" }
        };
        var ws = new SeatingWorkspace([s1 , s2 , s3] , seats);

        var strategy = new DefragStrategy();
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        // Row1 所有座位被填满
        seats.OfType<GridSeat>().Where(s => s.Row == 1).All(s => !s.IsAvailable).Should().BeTrue();
        // 3 名学生均被前移
        ws.Messages.Should().Contain(m => m.MessageKey == "Defrag_Moved");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFillPartialWhenMoreGapsThanCandidates ()
    {
        // Row1 有3空座，仅1个无约束学生 → 填1个
        var s1 = new Student { Id = "s1" , Name = "s1" };
        var seats = new Seat[]
        {
            new GridSeat { Id = "r1c1" , Row = 1 , Column = 1 , IsAvailable = true } ,
            new GridSeat { Id = "r1c2" , Row = 1 , Column = 2 , IsAvailable = true } ,
            new GridSeat { Id = "r1c3" , Row = 1 , Column = 3 , IsAvailable = true } ,
            new GridSeat { Id = "r2c1" , Row = 2 , Column = 1 , IsAvailable = false , OccupantId = "s1" }
        };
        var ws = new SeatingWorkspace([s1] , seats);

        var strategy = new DefragStrategy();
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        // 1 个被填
        var filled = seats.OfType<GridSeat>().Count(s => s.Row == 1 && !s.IsAvailable);
        filled.Should().Be(1);
        ws.Messages.Should().Contain(m => m.MessageKey == "Defrag_Moved");
        ws.Messages.Should().Contain(m => m.MessageKey == "Defrag_EffectivenessNote");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldMoveFromBackmostFirst ()
    {
        // Row2和Row3各有学生，Row1有1空座 → Row3（更靠后）的学生被优先移动
        var s2 = new Student { Id = "s2" , Name = "s2" };
        var s3 = new Student { Id = "s3" , Name = "s3" };
        var seats = new Seat[]
        {
            new GridSeat { Id = "r1c1" , Row = 1 , Column = 1 , IsAvailable = true } ,
            new GridSeat { Id = "r2c1" , Row = 2 , Column = 1 , IsAvailable = false , OccupantId = "s2" } ,
            new GridSeat { Id = "r3c1" , Row = 3 , Column = 1 , IsAvailable = false , OccupantId = "s3" }
        };
        var ws = new SeatingWorkspace([s2 , s3] , seats);

        var strategy = new DefragStrategy();
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        // s3 (Row3) 被前移（最远的优先）
        seats.First(s => s.Id == "r1c1").OccupantId.Should().Be("s3");
    }

    [Fact]
    public async Task ExecuteAsync_PolarSeats_ShouldMoveByRing ()
    {
        // Ring1空，Ring2有无约束学生 → 内移
        var student = new Student { Id = "s1" , Name = "s1" };
        var seats = new List<Seat>
        {
            new PolarSeat { Id = "ring1" , Ring = 1 , Radius = 1 , AngleDegrees = 0 , IsAvailable = true } ,
            new PolarSeat { Id = "ring2" , Ring = 2 , Radius = 2 , AngleDegrees = 0 , IsAvailable = false , OccupantId = "s1" }
        };
        var ws = new SeatingWorkspace([student] , seats);

        var strategy = new DefragStrategy();
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        seats.First(s => s.Id == "ring1").OccupantId.Should().Be("s1");
        seats.First(s => s.Id == "ring1").IsAvailable.Should().BeFalse();
        seats.First(s => s.Id == "ring2").OccupantId.Should().BeNull();
        seats.First(s => s.Id == "ring2").IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_FreeformSeats_ShouldMoveByRow ()
    {
        // Row1空，Row2有无约束学生（Freeform） → 前移
        var student = new Student { Id = "s1" , Name = "s1" };
        var seats = new List<Seat>
        {
            new FreeformSeat { Id = "f1" , X = 0 , Y = 0 , Row = 1 , IsAvailable = true } ,
            new FreeformSeat { Id = "f2" , X = 0 , Y = 1 , Row = 2 , IsAvailable = false , OccupantId = "s1" }
        };
        var ws = new SeatingWorkspace([student] , seats);

        var strategy = new DefragStrategy();
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        seats.First(s => s.Id == "f1").OccupantId.Should().Be("s1");
        seats.First(s => s.Id == "f2").OccupantId.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldLogEffectivenessNote ()
    {
        // 任何移动都应记录副作用警告
        var student = new Student { Id = "s1" , Name = "s1" };
        var seats = new Seat[]
        {
            new GridSeat { Id = "r1c1" , Row = 1 , Column = 1 , IsAvailable = true } ,
            new GridSeat { Id = "r2c1" , Row = 2 , Column = 1 , IsAvailable = false , OccupantId = "s1" }
        };
        var ws = new SeatingWorkspace([student] , seats);

        var strategy = new DefragStrategy();
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        ws.Messages.Should().Contain(m => m.MessageKey == "Defrag_EffectivenessNote");
    }

    [Fact]
    public async Task ExecuteAsync_Cancelled_ShouldThrowOperationCanceledException ()
    {
        var strategy = new DefragStrategy();
        var seats = new Seat[]
        {
            new GridSeat { Id = "r1c1" , Row = 1 , Column = 1 }
        };
        var ws = new SeatingWorkspace([] , seats);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await strategy.ExecuteAsync(ws , cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAsync_NullWorkspace_ShouldThrowArgumentNullException ()
    {
        var strategy = new DefragStrategy();
        var act = async () => await strategy.ExecuteAsync(null! , CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ═══════════════ 属性测试 ═══════════════

    [Fact]
    public void Properties_ShouldHaveExpectedDefaults ()
    {
        var strategy = new DefragStrategy();
        strategy.Id.Should().Be("Defrag");
        strategy.Name.Should().Be("Defrag");
        strategy.Priority.Should().Be(0);
        strategy.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void ValidateConfiguration_ShouldBeValid ()
    {
        var strategy = new DefragStrategy();
        var result = strategy.ValidateConfiguration();
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void SetConstrainedStudentIds_Null_ShouldNotThrow ()
    {
        var strategy = new DefragStrategy();
        var act = () => strategy.SetConstrainedStudentIds(null!);
        act.Should().NotThrow();
    }

    [Fact]
    public void SetConstrainedStudentIds_EmptySet_AllowsAll ()
    {
        var strategy = new DefragStrategy();
        strategy.SetConstrainedStudentIds([]);
        // 无异常即通过
    }

    [Fact]
    public void Constructor_NullConfig_ShouldThrowArgumentNullException ()
    {
        var act = () => new DefragStrategy(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
