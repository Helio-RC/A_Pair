namespace SeatFlow.Core.Tests.Models;

public class VenueSnapshotInfoTests
{
    [Fact]
    public void Default_HasEmptyValues ()
    {
        var v = new VenueSnapshotInfo();
        v.Name.Should().BeEmpty();
        v.LayoutType.Should().Be(default);
        v.SeatCount.Should().Be(0);
        v.ObstacleCount.Should().Be(0);
    }

    [Fact]
    public void CanSetAllProperties ()
    {
        var v = new VenueSnapshotInfo
        {
            Name = "教室A" ,
            LayoutType = LayoutType.Grid ,
            SeatCount = 40 ,
            ObstacleCount = 2
        };
        v.Name.Should().Be("教室A");
        v.LayoutType.Should().Be(LayoutType.Grid);
        v.SeatCount.Should().Be(40);
        v.ObstacleCount.Should().Be(2);
    }
}
