namespace SeatFlow.Core.Tests.Models;

public class FreeformSeatTests
{
    [Fact]
    public void Default_RowAndColumn_AreNull ()
    {
        var s = new FreeformSeat { X = 100 , Y = 200 };
        s.Row.Should().BeNull();
        s.Column.Should().BeNull();
    }

    [Fact]
    public void CanSetRowAndColumn ()
    {
        var s = new FreeformSeat { X = 100 , Y = 200 , Row = 3 , Column = 5 };
        s.Row.Should().Be(3);
        s.Column.Should().Be(5);
    }

    [Fact]
    public void Type_IsFreeform ()
    {
        new FreeformSeat().Type.Should().Be(SeatType.Freeform);
    }

    [Fact]
    public void GeometryData_ReturnsFreeformPosition ()
    {
        var s = new FreeformSeat { X = 10 , Y = 20 };
        var geom = s.GeometryData.Should().BeOfType<FreeformPosition>().Subject;
        geom.X.Should().Be(10);
        geom.Y.Should().Be(20);
    }
}
