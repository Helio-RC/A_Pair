namespace SeatFlow.Core.Tests;

public class ClassroomLayoutDefinitionTests
{
    [Fact]
    public void NewLayout_ShouldHaveEmptySeats ()
    {
        var layout = new ClassroomLayoutDefinition();
        layout.Seats.Should().BeEmpty();
        layout.Obstacles.Should().BeEmpty();
        layout.LayoutType.Should().Be(LayoutType.Grid);
    }

    [Fact]
    public void Layout_WithSeats_ShouldExposeReadOnly ()
    {
        var layout = new ClassroomLayoutDefinition();
        var seat = new GridSeat();
        layout.Seats.Add(seat);
        ((IClassroomLayout)layout).Seats.Should().HaveCount(1);
    }
}