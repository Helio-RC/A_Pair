namespace SeatFlow.Core.Tests;

public class SeatGeometryHelperTests
{
    [Fact]
    public void GetPosition_GridSeat_ShouldCalculateCorrectly ()
    {
        // 桌面模式：每桌 2 人，桌内距 10，桌间距 30，垂直间距 20
        var seat = new GridSeat { Row = 2 , Column = 3 };
        var meta = new GridLayoutMetadata
        {
            SeatsPerDesk = 2 ,
            IntraDeskSpacing = 10 ,
            InterDeskSpacing = 30 ,
            VerticalSpacing = 20 ,
            OriginX = 0 ,
            OriginY = 0
        };
        var (x , y) = SeatGeometryHelper.GetPosition(seat , meta);
        // Col=3: c=1→intra(10), c=2→inter(30), x=40
        x.Should().Be(40.0);
        // Row=2: r=1→VerticalSpacing(20), y=20
        y.Should().Be(20.0);
    }

    [Fact]
    public void GetPosition_GridSeat_UniformSingleDesk_ShouldBeEven ()
    {
        // 均匀模式：每桌 1 人（对应传统均匀网格）
        var seat = new GridSeat { Row = 1 , Column = 4 };
        var meta = new GridLayoutMetadata
        {
            SeatsPerDesk = 1 ,
            IntraDeskSpacing = 0 ,   // 回退到 12
            InterDeskSpacing = 10 ,
            VerticalSpacing = 8 ,
            OriginX = 10 ,
            OriginY = 20
        };
        var (x , y) = SeatGeometryHelper.GetPosition(seat , meta);
        // SeatsPerDesk=1: 每列都是桌边界, c=1,2,3 各加 inter(10), x=10+30=40
        x.Should().Be(40.0);
        y.Should().Be(20.0);
    }

    [Fact]
    public void GetPosition_PolarSeat_ShouldConvertToCartesian ()
    {
        var seat = new PolarSeat { Radius = 2.0 , AngleDegrees = 90 };
        var meta = new PolarLayoutMetadata { OriginX = 0 , OriginY = 0 };
        var (x , y) = SeatGeometryHelper.GetPosition(seat , meta);
        x.Should().BeApproximately(0 , 1e-9);
        y.Should().BeApproximately(2.0 , 1e-9);
    }

    [Fact]
    public void GetPosition_FreeformSeat_ShouldReturnExact ()
    {
        var seat = new FreeformSeat { X = 3.3 , Y = 4.4 };
        var (x , y) = SeatGeometryHelper.GetPosition(seat , new LayoutMetadata());
        x.Should().Be(3.3);
        y.Should().Be(4.4);
    }
}