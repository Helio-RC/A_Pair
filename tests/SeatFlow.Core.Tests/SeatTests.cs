namespace SeatFlow.Core.Tests;

public class SeatTests
{
    [Fact]
    public void GridSeat_ShouldHaveCorrectGeometry ()
    {
        var seat = new GridSeat { Row = 2 , Column = 5 };
        seat.Type.Should().Be(SeatType.Grid);
        var geom = seat.GeometryData.Should().BeOfType<GridPosition>().Subject;
        geom.Row.Should().Be(2);
        geom.Column.Should().Be(5);
    }

    [Fact]
    public void PolarSeat_ShouldHaveCorrectGeometry ()
    {
        var seat = new PolarSeat { Radius = 3.0 , AngleDegrees = 90 };
        seat.Type.Should().Be(SeatType.Polar);
        var geom = seat.GeometryData.Should().BeOfType<PolarPosition>().Subject;
        geom.Radius.Should().Be(3.0);
        geom.AngleDegrees.Should().Be(90);
    }

    [Fact]
    public void FreeformSeat_ShouldHaveCorrectGeometry ()
    {
        var seat = new FreeformSeat { X = 1.5 , Y = 2.7 };
        seat.Type.Should().Be(SeatType.Freeform);
        var geom = seat.GeometryData.Should().BeOfType<FreeformPosition>().Subject;
        geom.X.Should().Be(1.5);
        geom.Y.Should().Be(2.7);
    }

    [Fact]
    public void Seat_DefaultAvailable_And_NotFixed ()
    {
        var seat = new GridSeat();
        seat.IsAvailable.Should().BeTrue();
        seat.IsFixed.Should().BeFalse();
        seat.OccupantId.Should().BeNull();
    }
}