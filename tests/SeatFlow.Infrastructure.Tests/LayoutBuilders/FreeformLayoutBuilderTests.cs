namespace SeatFlow.Infrastructure.Tests.LayoutBuilders;

public class FreeformLayoutBuilderTests
{
    [Fact]
    public void BuildFreeform_ShouldCreateSeatsAtGivenPoints ()
    {
        var points = new List<(double X , double Y)> { (1.0 , 2.0) , (3.0 , 4.0) };
        var layout = FreeformLayoutBuilder.BuildFreeform(points);
        layout.Seats.Should().HaveCount(2);
        layout.LayoutType.Should().Be(LayoutType.Freeform);
        var seat = layout.Seats[0] as FreeformSeat;
        seat.Should().NotBeNull();
        seat!.X.Should().Be(1.0);
        seat.Y.Should().Be(2.0);
    }
}