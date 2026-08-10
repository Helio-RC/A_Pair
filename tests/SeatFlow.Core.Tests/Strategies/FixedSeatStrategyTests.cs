namespace SeatFlow.Core.Tests.Strategies;

public class FixedSeatStrategyTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldApplyFixedAssignments ()
    {
        var config = new FixedSeatConfiguration
        {
            FixedAssignments = new Dictionary<string , string> { { "seat1" , "s1" } }
        };
        var students = new[] { new Student { Id = "s1" } };
        var seats = new[] { new GridSeat { Id = "seat1" } };
        var ws = new SeatingWorkspace(students , seats);
        ws.RegisterCapabilities("FixedSeat" , ["MarkFixedSeat"]);

        var strategy = new FixedSeatStrategy(config);
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        seats[0].IsFixed.Should().BeTrue();
        seats[0].OccupantId.Should().Be("s1");
    }

    [Fact]
    public async Task ExecuteAsync_AlreadyFixed_ShouldNotOverride ()
    {
        var config = new FixedSeatConfiguration();
        var students = new[] { new Student { Id = "s1" } };
        var seats = new[] { new GridSeat { Id = "seat1" , IsFixed = true , OccupantId = "s1" } };
        var ws = new SeatingWorkspace(students , seats);

        var strategy = new FixedSeatStrategy(config);
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        seats[0].IsFixed.Should().BeTrue();
        seats[0].OccupantId.Should().Be("s1"); // unchanged
    }

    [Fact]
    public void ValidateConfiguration_Valid_ShouldPass ()
    {
        var config = new FixedSeatConfiguration
        {
            FixedAssignments = new Dictionary<string , string> { { "s1" , "student1" } }
        };
        var strategy = new FixedSeatStrategy(config);
        var result = strategy.ValidateConfiguration();
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateConfiguration_NullAssignments_ShouldFail ()
    {
        var config = new FixedSeatConfiguration { FixedAssignments = null! };
        var strategy = new FixedSeatStrategy(config);
        var result = strategy.ValidateConfiguration();
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Constructor_NullConfig_ShouldThrowArgumentNullException ()
    {
        var act = () => new FixedSeatStrategy(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteAsync_NullWorkspace_ShouldThrowArgumentNullException ()
    {
        var strategy = new FixedSeatStrategy();
        var act = async () => await strategy.ExecuteAsync(null! , CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteAsync_SeatNotFound_ShouldSkipWithWarning ()
    {
        var config = new FixedSeatConfiguration
        {
            FixedAssignments = new Dictionary<string , string> { { "nonexistent" , "s1" } }
        };
        var students = new[] { new Student { Id = "s1" } };
        var seats = new[] { new GridSeat { Id = "seat1" } };
        var ws = new SeatingWorkspace(students , seats);

        var strategy = new FixedSeatStrategy(config);
        var result = await strategy.ExecuteAsync(ws , CancellationToken.None);

        result.Success.Should().BeTrue();
        // Nonexistent seat skipped — no assignments made
        ws.BuildSeatingPlan().Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_OccupiedNonFixedSeat_ShouldClearAndReassign ()
    {
        var config = new FixedSeatConfiguration
        {
            FixedAssignments = new Dictionary<string , string> { { "seat1" , "s2" } }
        };
        var students = new[] { new Student { Id = "s1" } , new Student { Id = "s2" } };
        var seats = new[] { new GridSeat { Id = "seat1" } };
        var ws = new SeatingWorkspace(students , seats);

        // s1 already occupies seat1 (non-fixed)
        ws.TryAssignSeat("seat1" , "s1" , out _);
        ws.RegisterCapabilities("FixedSeat" , ["MarkFixedSeat"]);

        var strategy = new FixedSeatStrategy(config);
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        seats[0].OccupantId.Should().Be("s2");
        seats[0].IsFixed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_EmptyStudentId_ShouldOnlyMarkFixed ()
    {
        var config = new FixedSeatConfiguration
        {
            FixedAssignments = new Dictionary<string , string> { { "seat1" , "" } }
        };
        var students = new[] { new Student { Id = "s1" } };
        var seats = new[] { new GridSeat { Id = "seat1" } };
        var ws = new SeatingWorkspace(students , seats);
        ws.RegisterCapabilities("FixedSeat" , ["MarkFixedSeat"]);

        var strategy = new FixedSeatStrategy(config);
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        seats[0].IsFixed.Should().BeTrue();
        seats[0].OccupantId.Should().BeNull();
    }
}