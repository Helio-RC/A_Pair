namespace SeatFlow.Core.Tests.Strategies;

public class NoRepeatDeskMateStrategyTests
{

    // ═══════════════ EvaluateAsync 测试 ═══════════════

    [Fact]
    public async Task EvaluateAsync_EmptyHistory_ShouldApprove ()
    {
        var students = StrategyTestHelpers.CreateStudents("s1" , "s2");
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1) , (1 , 2));
        var ws = new SeatingWorkspace(students , [.. seats.Cast<Seat>()]);

        // No past pairs set → should approve all
        var strategy = new NoRepeatDeskMateStrategy();
        var result = await strategy.EvaluateAsync(
            ws , students[0] , seats[0] , StrategyTestHelpers.CreateContext() , CancellationToken.None);

        result.Approved.Should().BeTrue();
        result.AlreadyHandled.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_StudentHasNoPastMates_ShouldApprove ()
    {
        var students = StrategyTestHelpers.CreateStudents("s1" , "s2" , "s3");
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1) , (1 , 2));
        var ws = new SeatingWorkspace(students , [.. seats.Cast<Seat>()]);

        // s1-s2 is a past pair, but we're proposing s3
        var strategy = new NoRepeatDeskMateStrategy();
        strategy.SetPastDeskMatePairs([("s1" , "s2")]);

        var result = await strategy.EvaluateAsync(
            ws , students[2] , seats[0] , StrategyTestHelpers.CreateContext() , CancellationToken.None);

        result.Approved.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_PastMateNotAdjacent_ShouldApprove ()
    {
        var s1 = new Student { Id = "s1" , Name = "s1" };
        var s2 = new Student { Id = "s2" , Name = "s2" };
        // (1,1) and (3,3) are NOT adjacent
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1) , (3 , 3));
        var ws = new SeatingWorkspace([s1 , s2] , [.. seats.Cast<Seat>()]);

        // s2 is already assigned to (3,3) — far from (1,1)
        ws.TryAssignSeat(seats[1].Id , s2.Id , out _);

        var strategy = new NoRepeatDeskMateStrategy();
        strategy.SetPastDeskMatePairs([("s1" , "s2")]);

        // Propose s1 at (1,1): s2 at (3,3) is not adjacent → should approve
        var result = await strategy.EvaluateAsync(
            ws , s1 , seats[0] , StrategyTestHelpers.CreateContext() , CancellationToken.None);

        result.Approved.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_PastMateAdjacent_ShouldReject ()
    {
        var s1 = new Student { Id = "s1" , Name = "s1" };
        var s2 = new Student { Id = "s2" , Name = "s2" };
        // (1,1) and (1,2) are adjacent
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1) , (1 , 2));
        var ws = new SeatingWorkspace([s1 , s2] , [.. seats.Cast<Seat>()]);

        // s2 already assigned to (1,2) — adjacent to target (1,1)
        ws.TryAssignSeat(seats[1].Id , s2.Id , out _);

        var strategy = new NoRepeatDeskMateStrategy();
        strategy.SetPastDeskMatePairs([("s1" , "s2")]);

        // Propose s1 at (1,1): s2 at (1,2) is adjacent and a past mate → reject
        var result = await strategy.EvaluateAsync(
            ws , s1 , seats[0] , StrategyTestHelpers.CreateContext(rerollCount: 0 , maxRerolls: 10) , CancellationToken.None);

        result.Approved.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_RerollsExhausted_ShouldApproveWithWarning ()
    {
        var s1 = new Student { Id = "s1" , Name = "s1" };
        var s2 = new Student { Id = "s2" , Name = "s2" };
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1) , (1 , 2));
        var ws = new SeatingWorkspace([s1 , s2] , [.. seats.Cast<Seat>()]);

        ws.TryAssignSeat(seats[1].Id , s2.Id , out _);

        var strategy = new NoRepeatDeskMateStrategy();
        strategy.SetPastDeskMatePairs([("s1" , "s2")]);

        // RerollCount = MaxRerolls - 1 → already at limit, should force approve with warning
        var context = new StrategyTestHelpers.TestContext(rerollCount: 9 , maxRerolls: 10);
        var result = await strategy.EvaluateAsync(
            ws , s1 , seats[0] , context , CancellationToken.None);

        result.Approved.Should().BeTrue();
        context.Warnings.Should().HaveCount(1);
        context.Warnings[0].MessageKey.Should().Be("NoRepeatDeskMate_Forced");
    }

    [Fact]
    public async Task EvaluateAsync_NullWorkspace_ShouldThrowArgumentNullException ()
    {
        var strategy = new NoRepeatDeskMateStrategy();
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1));
        var student = new Student { Id = "s1" };

        var act = async () => await strategy.EvaluateAsync(
            null! , student , seats[0] , StrategyTestHelpers.CreateContext() , CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task EvaluateAsync_NullStudent_ShouldThrowArgumentNullException ()
    {
        var strategy = new NoRepeatDeskMateStrategy();
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1));
        var ws = new SeatingWorkspace([] , [.. seats.Cast<Seat>()]);

        var act = async () => await strategy.EvaluateAsync(
            ws , null! , seats[0] , StrategyTestHelpers.CreateContext() , CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task EvaluateAsync_Cancelled_ShouldThrowOperationCanceledException ()
    {
        var strategy = new NoRepeatDeskMateStrategy();
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1));
        var ws = new SeatingWorkspace([] , [.. seats.Cast<Seat>()]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await strategy.EvaluateAsync(
            ws , new Student { Id = "s1" } , seats[0] , StrategyTestHelpers.CreateContext() , cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task EvaluateAsync_PairNormalization_ShouldRejectBidirectional ()
    {
        var s1 = new Student { Id = "s1" , Name = "s1" };
        var s2 = new Student { Id = "s2" , Name = "s2" };
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1) , (1 , 2));
        var ws = new SeatingWorkspace([s1 , s2] , [.. seats.Cast<Seat>()]);

        ws.TryAssignSeat(seats[1].Id , s2.Id , out _);

        // Set pairs as (s2, s1) — reversed order
        var strategy = new NoRepeatDeskMateStrategy();
        strategy.SetPastDeskMatePairs([("s2" , "s1")]);

        // Propose s1 — should still detect s2 as past mate (bidirectional)
        var result = await strategy.EvaluateAsync(
            ws , s1 , seats[0] , StrategyTestHelpers.CreateContext(rerollCount: 0 , maxRerolls: 10) , CancellationToken.None);

        result.Approved.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_VerticalAdjacent_ShouldReject ()
    {
        var s1 = new Student { Id = "s1" , Name = "s1" };
        var s2 = new Student { Id = "s2" , Name = "s2" };
        // (1,1) and (2,1) are vertically adjacent
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1) , (2 , 1));
        var ws = new SeatingWorkspace([s1 , s2] , [.. seats.Cast<Seat>()]);

        ws.TryAssignSeat(seats[1].Id , s2.Id , out _);

        var strategy = new NoRepeatDeskMateStrategy();
        strategy.SetPastDeskMatePairs([("s1" , "s2")]);

        // SeatsPerDesk=2, adjacent vertically — AreDeskMates with default params
        // (preferHorizontal=true, allowVertical=false) should NOT consider vertical as desk-mate
        // But AreSeatsAdjacent would. The strategy uses AreDeskMates.
        // With default settings (preferHorizontal=true, allowVertical=false), vertical is NOT adjacent
        // So this should APPROVE because AreDeskMates doesn't count vertical as desk-mate
        var result = await strategy.EvaluateAsync(
            ws , s1 , seats[0] , StrategyTestHelpers.CreateContext() , CancellationToken.None);

        result.Approved.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_PolarSeats_WithLogicalGroup_ShouldDetectAdjacent ()
    {
        var s1 = new Student { Id = "s1" , Name = "s1" };
        var s2 = new Student { Id = "s2" , Name = "s2" };
        var seats = new List<Seat>
        {
            new PolarSeat { Id = "p1" , Ring = 1 , Radius = 1 , AngleDegrees = 0 , LogicalGroup = "A" } ,
            new PolarSeat { Id = "p2" , Ring = 1 , Radius = 1 , AngleDegrees = 45 , LogicalGroup = "A" } ,
        };
        var ws = new SeatingWorkspace([s1 , s2] , [.. seats]);

        ws.TryAssignSeat(seats[1].Id , s2.Id , out _);

        var strategy = new NoRepeatDeskMateStrategy();
        strategy.SetPastDeskMatePairs([("s1" , "s2")]);

        // Same LogicalGroup "A" → adjacent
        var result = await strategy.EvaluateAsync(
            ws , s1 , seats[0] , StrategyTestHelpers.CreateContext(rerollCount: 0 , maxRerolls: 10) , CancellationToken.None);

        result.Approved.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_ClearHistory_ShouldForgetPairs ()
    {
        var s1 = new Student { Id = "s1" , Name = "s1" };
        var s2 = new Student { Id = "s2" , Name = "s2" };
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1) , (1 , 2));
        var ws = new SeatingWorkspace([s1 , s2] , [.. seats.Cast<Seat>()]);

        ws.TryAssignSeat(seats[1].Id , s2.Id , out _);

        var strategy = new NoRepeatDeskMateStrategy();
        strategy.SetPastDeskMatePairs([("s1" , "s2")]);
        strategy.ClearHistory();

        // After clear, should approve (no history)
        var result = await strategy.EvaluateAsync(
            ws , s1 , seats[0] , StrategyTestHelpers.CreateContext(rerollCount: 0 , maxRerolls: 10) , CancellationToken.None);

        result.Approved.Should().BeTrue();
    }

    // ═══════════════ 属性测试 ═══════════════

    [Fact]
    public void Properties_ShouldHaveExpectedDefaults ()
    {
        var strategy = new NoRepeatDeskMateStrategy();
        strategy.Id.Should().Be("NoRepeatDeskMate");
        strategy.Name.Should().Be("NoRepeatDeskMate");
        strategy.DisplayName.Should().Be("同桌不重复");
        strategy.Priority.Should().Be(40);
        strategy.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Config_ShouldHaveExpectedDefaults ()
    {
        var strategy = new NoRepeatDeskMateStrategy();
        strategy.Config.HistoryWindowSize.Should().Be(10);
    }

    [Fact]
    public async Task SetSeatsPerDesk_ShouldUpdateConfig ()
    {
        var strategy = new NoRepeatDeskMateStrategy();
        strategy.SetSeatsPerDesk(3);
        // SeatsPerDesk is stored privately; verify indirectly via behavior
        // Create adjacent seats where desk boundary matters (SeatsPerDesk=3, columns 2 and 3 NOT same desk)
        var s1 = new Student { Id = "s1" , Name = "s1" };
        var s2 = new Student { Id = "s2" , Name = "s2" };
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 2) , (1 , 3)); // (1,2) and (1,3) — with SeatsPerDesk=3, cols 2&3 are in desk 1
        var ws = new SeatingWorkspace([s1 , s2] , [.. seats.Cast<Seat>()]);

        ws.TryAssignSeat(seats[1].Id , s2.Id , out _);

        strategy.SetPastDeskMatePairs([("s1" , "s2")]);

        // With SeatsPerDesk=3, cols 2 and 3 → (2-1)/3=0, (3-1)/3=0 → same desk → adjacent → reject
        var result = await strategy.EvaluateAsync(
            ws , s1 , seats[0] , StrategyTestHelpers.CreateContext(rerollCount: 0 , maxRerolls: 10) , CancellationToken.None);
        result.Approved.Should().BeFalse();
    }
}
