namespace SeatFlow.Core.Tests;

public class ObstacleProcessorTests
{
    [Fact]
    public void ApplyObstacles_SeatInsideObstacle_ShouldBeUnavailable ()
    {
        var layout = new ClassroomLayoutDefinition
        {
            LayoutType = LayoutType.Grid ,
            Metadata = new GridLayoutMetadata
            {
                SeatsPerDesk = 1 ,
                IntraDeskSpacing = 0 ,
                InterDeskSpacing = 10 ,
                VerticalSpacing = 10 ,
                OriginX = 0 ,
                OriginY = 0
            } ,
            Obstacles = { new Obstacle { X = 5 , Y = 5 , Width = 15 , Height = 15 } }
        };
        // 每桌 1 人，间距 10：col=1 x=0, col=2 x=10, col=3 x=20...
        layout.Seats.Add(new GridSeat { Row = 2 , Column = 2 }); // position (10,10) -> inside obstacle
        layout.Seats.Add(new GridSeat { Row = 3 , Column = 1 }); // position (0,20) -> outside

        ObstacleProcessor.ApplyObstacles(layout);
        layout.Seats[0].IsAvailable.Should().BeFalse();
        layout.Seats[1].IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void ApplyObstacles_NoObstacles_ShouldDoNothing ()
    {
        var layout = new ClassroomLayoutDefinition();
        layout.Seats.Add(new GridSeat());
        ObstacleProcessor.ApplyObstacles(layout);
        layout.Seats[0].IsAvailable.Should().BeTrue();
    }
}