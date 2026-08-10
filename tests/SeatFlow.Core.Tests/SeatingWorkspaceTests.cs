namespace SeatFlow.Core.Tests;

public class SeatingWorkspaceTests
{
    private static Student CreateStudent (string id , string name = "Test") =>
        new() { Id = id , Name = name };

    private static GridSeat CreateSeat (string id , int row , int col) =>
        new() { Id = id , Row = row , Column = col };

    [Fact]
    public void TryAssignSeat_Success ()
    {
        var students = new[] { CreateStudent("s1") };
        var seats = new[] { CreateSeat("seat1" , 1 , 1) };
        var ws = new SeatingWorkspace(students , seats);

        var ok = ws.TryAssignSeat("seat1" , "s1" , out var error);
        ok.Should().BeTrue();
        error.Should().BeEmpty();
        seats[0].OccupantId.Should().Be("s1");
        seats[0].IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void TryAssignSeat_AlreadyOccupied_ShouldFail ()
    {
        var students = new[] { CreateStudent("s1") , CreateStudent("s2") };
        var seats = new[] { CreateSeat("seat1" , 1 , 1) };
        var ws = new SeatingWorkspace(students , seats);
        ws.TryAssignSeat("seat1" , "s1" , out _);

        var ok = ws.TryAssignSeat("seat1" , "s2" , out var error);
        ok.Should().BeFalse();
        error.Should().Be("Seat not available");
    }

    [Fact]
    public void TryAssignSeat_SameStudentToDifferentSeats_ShouldFail ()
    {
        var students = new[] { CreateStudent("s1") };
        var seats = new[] { CreateSeat("seat1" , 1 , 1) , CreateSeat("seat2" , 1 , 2) };
        var ws = new SeatingWorkspace(students , seats);
        ws.TryAssignSeat("seat1" , "s1" , out _);

        var ok = ws.TryAssignSeat("seat2" , "s1" , out var error);
        ok.Should().BeFalse();
        error.Should().Be("Student already assigned to another seat");
    }

    [Fact]
    public void GetEmptySeats_ShouldReturnAvailableSeats ()
    {
        var students = new[] { CreateStudent("s1") };
        var seats = new[] { CreateSeat("seat1" , 1 , 1) , CreateSeat("seat2" , 1 , 2) };
        var ws = new SeatingWorkspace(students , seats);
        ws.TryAssignSeat("seat1" , "s1" , out _);

        var empty = ws.GetEmptySeats().ToList();
        empty.Should().ContainSingle().Which.Id.Should().Be("seat2");
    }

    [Fact]
    public void BuildSeatingPlan_ShouldReflectAssignments ()
    {
        var students = new[] { CreateStudent("s1") };
        var seats = new[] { CreateSeat("seat1" , 1 , 1) };
        var ws = new SeatingWorkspace(students , seats);
        ws.TryAssignSeat("seat1" , "s1" , out _);

        var plan = ws.BuildSeatingPlan();
        plan.Assignments.Should().ContainKey("seat1").WhoseValue.Should().Be("s1");
    }

    [Fact]
    public void ApplySnapshotAssignments_ShouldResetAndApply ()
    {
        var students = new[] { CreateStudent("s1") , CreateStudent("s2") };
        var seats = new[] { CreateSeat("seat1" , 1 , 1) , CreateSeat("seat2" , 1 , 2) };
        var ws = new SeatingWorkspace(students , seats);
        ws.TryAssignSeat("seat1" , "s1" , out _);

        var snapshot = new Dictionary<string , string> { { "seat2" , "s2" } };
        ws.ApplySnapshotAssignments(snapshot);

        seats[0].OccupantId.Should().BeNull();
        seats[0].IsAvailable.Should().BeTrue();
        seats[1].OccupantId.Should().Be("s2");
        seats[1].IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void TryAssignSeat_FixedSeat_ShouldEnforceFixed ()
    {
        var students = new[] { CreateStudent("s1") , CreateStudent("s2") };
        var seats = new[] { CreateSeat("seat1" , 1 , 1) };
        seats[0].IsFixed = true;
        seats[0].OccupantId = "s1"; // fixed to s1
        var ws = new SeatingWorkspace(students , seats);

        var ok = ws.TryAssignSeat("seat1" , "s2" , out var error);
        ok.Should().BeFalse();
        error.Should().Be("Seat is fixed by another student");
    }

    // ═══════════════ 能力系统测试 ═══════════════

    [Fact]
    public void TryMarkFixed_DeclaredCapability_Succeeds ()
    {
        var students = new[] { CreateStudent("s1") };
        var seats = new[] { CreateSeat("seat1" , 1 , 1) };
        var ws = new SeatingWorkspace(students , seats);
        ws.RegisterCapabilities("TestStrategy" , ["MarkFixedSeat"]);

        var ok = ((IFixedSeatCapability)ws).TryMarkFixed("seat1" , null , "TestStrategy" , "测试策略" , out var error);

        ok.Should().BeTrue();
        error.Should().BeEmpty();
        seats[0].IsFixed.Should().BeTrue();
    }

    [Fact]
    public void TryMarkFixed_UndeclaredCapability_Fails ()
    {
        var students = new[] { CreateStudent("s1") };
        var seats = new[] { CreateSeat("seat1" , 1 , 1) };
        var ws = new SeatingWorkspace(students , seats);
        // 未注册任何能力

        var ok = ((IFixedSeatCapability)ws).TryMarkFixed("seat1" , null , "TestStrategy" , "测试策略" , out var error);

        ok.Should().BeFalse();
        error.Should().Contain("未声明");
        seats[0].IsFixed.Should().BeFalse();
    }

    [Fact]
    public void TryMarkFixed_WithStudent_AssignsAndFixes ()
    {
        var students = new[] { CreateStudent("s1") };
        var seats = new[] { CreateSeat("seat1" , 1 , 1) };
        var ws = new SeatingWorkspace(students , seats);
        ws.RegisterCapabilities("TestStrategy" , ["MarkFixedSeat"]);

        var ok = ((IFixedSeatCapability)ws).TryMarkFixed("seat1" , "s1" , "TestStrategy" , "测试策略" , out _);

        ok.Should().BeTrue();
        seats[0].IsFixed.Should().BeTrue();
        seats[0].OccupantId.Should().Be("s1");
        seats[0].IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void TryMarkFixed_NonexistentSeat_Fails ()
    {
        var ws = new SeatingWorkspace([] , []);
        ws.RegisterCapabilities("TestStrategy" , ["MarkFixedSeat"]);

        var ok = ((IFixedSeatCapability)ws).TryMarkFixed("ghost" , null , "TestStrategy" , "测试策略" , out var error);

        ok.Should().BeFalse();
        error.Should().Contain("不存在");
    }

    [Fact]
    public void TryMarkFixed_DifferentStrategyId_Fails ()
    {
        var ws = new SeatingWorkspace([] , [new GridSeat { Id = "seat1" }]);
        ws.RegisterCapabilities("StrategyA" , ["MarkFixedSeat"]);

        // StrategyB 没有注册能力
        var ok = ((IFixedSeatCapability)ws).TryMarkFixed("seat1" , null , "StrategyB" , "策略B" , out var error);

        ok.Should().BeFalse();
        error.Should().Contain("未声明");
    }
}