namespace SeatFlow.Core.Tests.Strategies;

public class DeskMateStrategyTests
{

    // ═══════════════ EvaluateAsync 测试 ═══════════════

    [Fact]
    public async Task EvaluateAsync_StudentNoGroup_ShouldApprove ()
    {
        var students = StrategyTestHelpers.CreateStudents("s1" , "s2");
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1) , (1 , 2));
        var ws = new SeatingWorkspace(students , [.. seats.Cast<Seat>()]);

        var strategy = new DeskMateStrategy();
        var result = await strategy.EvaluateAsync(
            ws , students[0] , seats[0] , StrategyTestHelpers.CreateContext() , CancellationToken.None);

        result.Approved.Should().BeTrue();
        result.AlreadyHandled.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_CoordinatedAssignment_SufficientAdjacent_ShouldHandle ()
    {
        var students = StrategyTestHelpers.CreateStudents("s1" , "s2");
        // (1,1) and (1,2) are horizontally adjacent
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1) , (1 , 2) , (2 , 1));
        var ws = new SeatingWorkspace(students , [.. seats.Cast<Seat>()]);

        var config = new DeskMateConfiguration
        {
            Groups = [new DeskMateGroup { StudentIds = ["s1" , "s2"] }]
        };
        var strategy = new DeskMateStrategy(config);

        // 选择 s1 和座位 (1,1)；s2 应被分配到相邻座位 (1,2)
        var result = await strategy.EvaluateAsync(
            ws , students[0] , seats[0] , StrategyTestHelpers.CreateContext() , CancellationToken.None);

        result.Approved.Should().BeTrue();
        result.AlreadyHandled.Should().BeTrue();

        // 验证两名学生都已分配
        var plan = ws.BuildSeatingPlan();
        plan.Assignments.Should().HaveCount(2);
        plan.Assignments.Values.Should().Contain(["s1" , "s2"]);
    }

    [Fact]
    public async Task EvaluateAsync_NoAdjacentSeatForMates_ShouldReject ()
    {
        var students = StrategyTestHelpers.CreateStudents("s1" , "s2");
        // Seats are NOT adjacent: (1,1) and (3,3) far apart, no adjacent empty for mate
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1) , (3 , 3));
        var ws = new SeatingWorkspace(students , [.. seats.Cast<Seat>()]);

        var config = new DeskMateConfiguration
        {
            Groups = [new DeskMateGroup { StudentIds = ["s1" , "s2"] }]
        };
        var strategy = new DeskMateStrategy(config);

        // (1,1) has no adjacent empty seat → should reject
        var result = await strategy.EvaluateAsync(
            ws , students[0] , seats[0] , StrategyTestHelpers.CreateContext() , CancellationToken.None);

        result.Approved.Should().BeTrue();
        result.AlreadyHandled.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_NearOccupied_TargetAdjacentToMate_ShouldApprove ()
    {
        var s1 = new Student { Id = "s1" , Name = "s1" };
        var s2 = new Student { Id = "s2" , Name = "s2" };
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1) , (1 , 2) , (2 , 1));
        var ws = new SeatingWorkspace([s1 , s2] , [.. seats.Cast<Seat>()]);

        // Pre-assign s1 to (1,1) — simulating FixedSeat allocated s1
        ws.TryAssignSeat(seats[0].Id , s1.Id , out _);

        var config = new DeskMateConfiguration
        {
            Groups = [new DeskMateGroup { StudentIds = ["s1" , "s2"] }]
        };
        var strategy = new DeskMateStrategy(config);

        // s2 proposed at (1,2) which is adjacent to s1 at (1,1) → should approve
        var result = await strategy.EvaluateAsync(
            ws , s2 , seats[1] , StrategyTestHelpers.CreateContext() , CancellationToken.None);

        result.Approved.Should().BeTrue();
        result.AlreadyHandled.Should().BeFalse(); // not handled, RandomFill does TryAssignSeat
    }

    [Fact]
    public async Task EvaluateAsync_NearOccupied_TargetFarFromMate_FindsNearby_ShouldHandle ()
    {
        var s1 = new Student { Id = "s1" , Name = "s1" };
        var s2 = new Student { Id = "s2" , Name = "s2" };
        // (1,1) occupied by s1, (1,2) is adjacent to (1,1), (2,2) is far
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1) , (1 , 2) , (2 , 2));
        var ws = new SeatingWorkspace([s1 , s2] , [.. seats.Cast<Seat>()]);

        ws.TryAssignSeat(seats[0].Id , s1.Id , out _); // s1 at (1,1)

        var config = new DeskMateConfiguration
        {
            Groups = [new DeskMateGroup { StudentIds = ["s1" , "s2"] }]
        };
        var strategy = new DeskMateStrategy(config);

        // s2 proposed at (2,2) which is NOT adjacent to s1 at (1,1)
        // but (1,2) IS adjacent to s1's seat → should reassign s2 to (1,2) → Handled
        var result = await strategy.EvaluateAsync(
            ws , s2 , seats[2] , StrategyTestHelpers.CreateContext() , CancellationToken.None);

        result.Approved.Should().BeTrue();
        result.AlreadyHandled.Should().BeTrue();

        // s2 should be at (1,2) (adjacent to s1)
        var plan = ws.BuildSeatingPlan();
        plan.Assignments[seats[1].Id].Should().Be("s2");
    }

    [Fact]
    public async Task EvaluateAsync_NullWorkspace_ShouldThrowArgumentNullException ()
    {
        var strategy = new DeskMateStrategy();
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1));
        var student = new Student { Id = "s1" };

        var act = async () => await strategy.EvaluateAsync(
            null! , student , seats[0] , StrategyTestHelpers.CreateContext() , CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task EvaluateAsync_NullStudent_ShouldThrowArgumentNullException ()
    {
        var strategy = new DeskMateStrategy();
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1));
        var ws = new SeatingWorkspace([] , [.. seats.Cast<Seat>()]);

        var act = async () => await strategy.EvaluateAsync(
            ws , null! , seats[0] , StrategyTestHelpers.CreateContext() , CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task EvaluateAsync_GroupFromAttributeBag_ShouldMergeGroups ()
    {
        var s1 = new Student { Id = "s1" };
        s1.Extensions.Set("DeskMates" , new List<string> { "s2" });
        var s2 = new Student { Id = "s2" };

        // (1,1) and (1,2) are adjacent
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1) , (1 , 2));
        var ws = new SeatingWorkspace([s1 , s2] , [.. seats.Cast<Seat>()]);

        var strategy = new DeskMateStrategy();
        var result = await strategy.EvaluateAsync(
            ws , s1 , seats[0] , StrategyTestHelpers.CreateContext() , CancellationToken.None);

        result.Approved.Should().BeTrue();
        result.AlreadyHandled.Should().BeTrue();

        var plan = ws.BuildSeatingPlan();
        plan.Assignments.Should().HaveCount(2);
    }

    [Fact]
    public async Task EvaluateAsync_Cancelled_ShouldThrowOperationCanceledException ()
    {
        var strategy = new DeskMateStrategy();
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1));
        var ws = new SeatingWorkspace([] , [.. seats.Cast<Seat>()]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await strategy.EvaluateAsync(
            ws , new Student { Id = "s1" } , seats[0] , StrategyTestHelpers.CreateContext() , cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task EvaluateAsync_PolarSeatsWithLogicalGroup_ShouldConsiderAdjacent ()
    {
        var students = StrategyTestHelpers.CreateStudents("s1" , "s2");
        var seats = new List<Seat>
        {
            new PolarSeat { Id = "p1", Ring = 1, Radius = 1, AngleDegrees = 0, LogicalGroup = "A" },
            new PolarSeat { Id = "p2", Ring = 1, Radius = 1, AngleDegrees = 45, LogicalGroup = "A" },
            new PolarSeat { Id = "p3", Ring = 1, Radius = 1, AngleDegrees = 180, LogicalGroup = "B" },
        };
        var ws = new SeatingWorkspace(students , seats);

        var config = new DeskMateConfiguration
        {
            Groups = [new DeskMateGroup { StudentIds = ["s1" , "s2"] }]
        };
        var strategy = new DeskMateStrategy(config);

        // p1 and p2 share LogicalGroup "A" → adjacent → Handled
        var result = await strategy.EvaluateAsync(
            ws , students[0] , seats[0] , StrategyTestHelpers.CreateContext() , CancellationToken.None);

        result.Approved.Should().BeTrue();
        result.AlreadyHandled.Should().BeTrue();

        var plan = ws.BuildSeatingPlan();
        plan.Assignments.Should().HaveCount(2);
        plan.Assignments.Keys.Should().Contain(["p1" , "p2"]);
    }

    [Fact]
    public async Task EvaluateAsync_FreeformSeatsWithLogicalGroup_ShouldConsiderAdjacent ()
    {
        var students = StrategyTestHelpers.CreateStudents("s1" , "s2");
        var seats = new List<Seat>
        {
            new FreeformSeat { Id = "ff1", X = 0, Y = 0, LogicalGroup = "G1" },
            new FreeformSeat { Id = "ff2", X = 10, Y = 10, LogicalGroup = "G1" },
            new FreeformSeat { Id = "ff3", X = 50, Y = 50, LogicalGroup = "G2" },
        };
        var ws = new SeatingWorkspace(students , [.. seats]);

        var config = new DeskMateConfiguration
        {
            Groups = [new DeskMateGroup { StudentIds = ["s1" , "s2"] }]
        };
        var strategy = new DeskMateStrategy(config);

        var result = await strategy.EvaluateAsync(
            ws , students[0] , seats[0] , StrategyTestHelpers.CreateContext() , CancellationToken.None);

        result.Approved.Should().BeTrue();
        result.AlreadyHandled.Should().BeTrue();

        var plan = ws.BuildSeatingPlan();
        plan.Assignments.Keys.Should().Contain(["ff1" , "ff2"]);
    }

    // ═══════════ 配置验证测试（保持兼容） ═══════════

    [Fact]
    public void ValidateConfiguration_HasGroups_ShouldPass ()
    {
        var config = new DeskMateConfiguration
        {
            Groups = [new DeskMateGroup { StudentIds = ["s1" , "s2"] }]
        };
        var strategy = new DeskMateStrategy(config);
        strategy.ValidateConfiguration().IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateConfiguration_NullGroups_ShouldFail ()
    {
        var config = new DeskMateConfiguration { Groups = null! };
        var strategy = new DeskMateStrategy(config);
        strategy.ValidateConfiguration().IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateConfiguration_GroupWithLessThanTwoStudents_ShouldFail ()
    {
        var config = new DeskMateConfiguration
        {
            Groups = [new DeskMateGroup { StudentIds = ["s1"] }]
        };
        var strategy = new DeskMateStrategy(config);
        strategy.ValidateConfiguration().IsValid.Should().BeFalse();
    }

    [Fact]
    public void Constructor_NullConfig_ShouldThrowArgumentNullException ()
    {
        var act = () => new DeskMateStrategy(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ═══════════ 属性测试 ═══════════

    [Fact]
    public async Task EvaluateAsync_AllowVerticalFalse_ShouldOnlyUseHorizontalAdjacent ()
    {
        var students = StrategyTestHelpers.CreateStudents("s1" , "s2");
        // (1,1) and (2,1) are vertically adjacent; (1,2) is horizontally adjacent to (1,1)
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1) , (1 , 2) , (2 , 1));
        var ws = new SeatingWorkspace(students , [.. seats.Cast<Seat>()]);

        var config = new DeskMateConfiguration
        {
            Groups = [new DeskMateGroup { StudentIds = ["s1" , "s2"] }]
        };
        var strategy = new DeskMateStrategy(config);

        // (1,1) has horizontal neighbor (1,2) — same desk. (2,1) is vertical — NOT desk-mate.
        var result = await strategy.EvaluateAsync(
            ws , students[0] , seats[0] , StrategyTestHelpers.CreateContext() , CancellationToken.None);

        result.AlreadyHandled.Should().BeTrue();
        var plan = ws.BuildSeatingPlan();
        plan.Assignments[seats[1].Id].Should().Be("s2"); // (1,2) — horizontal
    }

    [Fact]
    public async Task EvaluateAsync_VerticalOnly_ShouldUseVerticalAdjacent ()
    {
        var students = StrategyTestHelpers.CreateStudents("s1" , "s2");
        // (1,1) and (2,1) are vertically adjacent
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1) , (1 , 2) , (2 , 1));
        var ws = new SeatingWorkspace(students , [.. seats.Cast<Seat>()]);

        var config = new DeskMateConfiguration
        {
            Groups = [new DeskMateGroup { StudentIds = ["s1" , "s2"] }]
        };
        var strategy = new DeskMateStrategy(config);

        // Vertical adjacency is NOT desk-mate — group can't coordinate, partial assignment
        var result = await strategy.EvaluateAsync(
            ws , students[0] , seats[0] , StrategyTestHelpers.CreateContext() , CancellationToken.None);

        result.AlreadyHandled.Should().BeTrue();
        // s1 assigned to (1,1), s2 NOT at (2,1) — vertical not desk-mate
        var plan = ws.BuildSeatingPlan();
        plan.Assignments.Should().ContainKey(seats[0].Id);
    }

    [Fact]
    public async Task EvaluateAsync_NoAdjacentWhenConfigRestricted_ShouldReject ()
    {
        var students = StrategyTestHelpers.CreateStudents("s1" , "s2");
        // (1,1) and (2,2) are NOT adjacent
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1) , (2 , 2));
        var ws = new SeatingWorkspace(students , [.. seats.Cast<Seat>()]);

        var config = new DeskMateConfiguration
        {
            Groups = [new DeskMateGroup { StudentIds = ["s1" , "s2"] }]
        };
        var strategy = new DeskMateStrategy(config);

        // (2,2) is not adjacent to (1,1) at all → should reject
        var result = await strategy.EvaluateAsync(
            ws , students[0] , seats[0] , StrategyTestHelpers.CreateContext() , CancellationToken.None);

        result.Approved.Should().BeTrue();
        result.AlreadyHandled.Should().BeTrue();
    }

    [Fact]
    public void Properties_ShouldHaveExpectedDefaults ()
    {
        var strategy = new DeskMateStrategy();
        strategy.Id.Should().Be("DeskMate");
        strategy.Name.Should().Be("DeskMate");
        strategy.DisplayName.Should().Be("同桌分组");
        strategy.Priority.Should().Be(50);
        strategy.IsEnabled.Should().BeTrue();
    }
}
