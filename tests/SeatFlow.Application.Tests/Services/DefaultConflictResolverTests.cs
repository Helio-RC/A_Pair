namespace SeatFlow.Application.Tests.Services;

public class DefaultConflictResolverTests
{
    [Fact]
    public void Resolve_DuplicateAssignment_ShouldClearDuplicates ()
    {
        var students = new[] { new Student { Id = "s1" } };
        var seat1 = new GridSeat { Id = "seat1" };
        var seat2 = new GridSeat { Id = "seat2" };
        var workspace = new SeatingWorkspace(students , [seat1 , seat2]);

        seat1.OccupantId = "s1";
        seat1.IsAvailable = false;
        seat2.OccupantId = "s1";
        seat2.IsAvailable = false;

        var resolver = new DefaultConflictResolver();
        var result = resolver.Resolve(workspace);

        result.Conflicts.Should().Contain(c => c.Type == ConflictType.DuplicateAssignment);
        seat2.OccupantId.Should().BeNull();
        seat2.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void Resolve_FixedSeatMissingStudent_ShouldReportConflict ()
    {
        var seat = new GridSeat { Id = "seat1" , IsFixed = true };
        var workspace = new SeatingWorkspace(new List<Student>() , [seat]);

        var resolver = new DefaultConflictResolver();
        var result = resolver.Resolve(workspace);

        result.Conflicts.Should().Contain(c => c.Type == ConflictType.FixedSeatMismatch && c.SeatId == "seat1");
    }
}