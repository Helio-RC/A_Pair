namespace SeatFlow.Infrastructure.Tests.LayoutBuilders;

public class PolarLayoutBuilderTests
{
    [Fact]
    public void BuildPolar_ShouldCreateCorrectNumberOfSeats ()
    {
        var layout = PolarLayoutBuilder.BuildPolar(1.0 , 2 , 8);
        layout.Seats.Should().HaveCount(16);
        layout.LayoutType.Should().Be(LayoutType.Polar);
        var meta = layout.Metadata as PolarLayoutMetadata;
        meta.Should().NotBeNull();
        meta!.Rings.Should().Be(2);
        meta.SeatsPerRing.Should().Be(8);
    }

    [Fact]
    public void BuildPolar_FullCircle_NoAisles_ShouldGenerateUniformSeats ()
    {
        var meta = new PolarLayoutMetadata
        {
            Rings = 3 ,
            SeatsPerRing = 8 ,
            RadiusStep = 40 ,
            StartAngleDegrees = 0 ,
            EndAngleDegrees = 360 ,
            OriginX = 200 ,
            OriginY = 200
        };
        var layout = PolarLayoutBuilder.BuildPolar(meta);
        layout.Seats.Count.Should().Be(24);
        layout.Seats.All(s => s is PolarSeat).Should().BeTrue();
    }

    [Fact]
    public void BuildPolar_HalfCircle_ShouldRespectAngleRange ()
    {
        var meta = new PolarLayoutMetadata
        {
            Rings = 1 ,
            SeatsPerRing = 6 ,
            RadiusStep = 40 ,
            StartAngleDegrees = 0 ,
            EndAngleDegrees = 180 ,
            OriginX = 200 ,
            OriginY = 200
        };
        var layout = PolarLayoutBuilder.BuildPolar(meta);
        layout.Seats.Count.Should().Be(6);
        foreach (PolarSeat s in layout.Seats.Cast<PolarSeat>())
        {
            s.AngleDegrees.Should().BeGreaterThanOrEqualTo(0);
            s.AngleDegrees.Should().BeLessThan(180);
        }
    }

    [Fact]
    public void BuildPolar_WithRadialAisles_ShouldAssignLogicalGroup ()
    {
        var meta = new PolarLayoutMetadata
        {
            RingSeatCounts = new List<int> { 12 } ,
            RadiusStep = 40 ,
            StartAngleDegrees = 0 ,
            EndAngleDegrees = 360 ,
            OriginX = 200 ,
            OriginY = 200 ,
            AisleRadialAngles = new List<double> { 0 , 90 , 180 , 270 } ,
            AisleRadialWidthDegrees = 5
        };
        var layout = PolarLayoutBuilder.BuildPolar(meta);
        var groups = layout.Seats.Cast<PolarSeat>().Select(s => s.LogicalGroup).Distinct().ToList();
        groups.Count.Should().Be(4);
    }

    [Fact]
    public void BuildPolar_WithCircularAisles_ShouldIncreaseRadius ()
    {
        var meta = new PolarLayoutMetadata
        {
            Rings = 3 ,
            SeatsPerRing = 8 ,
            RadiusStep = 40 ,
            AisleCircularAfterRings = new List<int> { 1 , 2 } ,
            AisleCircularWidth = 20 ,
            HasPodium = false
        };
        var layout = PolarLayoutBuilder.BuildPolar(meta);
        var seats = layout.Seats.Cast<PolarSeat>().ToList();
        seats.First(s => s.Ring == 1).Radius.Should().BeApproximately(40 , 1e-9);
        seats.First(s => s.Ring == 2).Radius.Should().BeApproximately(100 , 1e-9);
        seats.First(s => s.Ring == 3).Radius.Should().BeApproximately(160 , 1e-9);
    }

    [Fact]
    public void BuildPolar_WithPodium_ShouldAddObstacle ()
    {
        var meta = new PolarLayoutMetadata
        {
            Rings = 1 ,
            SeatsPerRing = 8 ,
            HasPodium = true ,
            PodiumRadius = 30 ,
            OriginX = 200 ,
            OriginY = 200
        };
        var layout = PolarLayoutBuilder.BuildPolar(meta);
        layout.Obstacles.Should().ContainSingle(o => o.Type == "Podium");
        var podium = layout.Obstacles.First(o => o.Type == "Podium");
        podium.X.Should().Be(170);
        podium.Y.Should().Be(170);
    }

    [Fact]
    public void BuildPolar_NoPodium_ShouldNotAddObstacle ()
    {
        var meta = new PolarLayoutMetadata { Rings = 1 , SeatsPerRing = 8 , HasPodium = false };
        var layout = PolarLayoutBuilder.BuildPolar(meta);
        layout.Obstacles.Should().BeEmpty();
    }

    [Fact]
    public void BuildPolar_BackwardCompat_RingsAndSeatsPerRing ()
    {
        var layout = PolarLayoutBuilder.BuildPolar(40 , 2 , 10);
        layout.Seats.Count.Should().Be(20);
        layout.LayoutType.Should().Be(LayoutType.Polar);
    }

    [Fact]
    public void BuildPolar_RingSeatCounts_TakesPrecedence ()
    {
        var meta = new PolarLayoutMetadata
        {
            Rings = 10 ,
            SeatsPerRing = 100 ,
            RingSeatCounts = new List<int> { 4 , 6 , 8 } ,
            RadiusStep = 40 ,
            HasPodium = false
        };
        var layout = PolarLayoutBuilder.BuildPolar(meta);
        layout.Seats.Count.Should().Be(18);
    }

    [Fact]
    public void BuildPolar_QuarterCircle_ShouldProduceSeatsInRange ()
    {
        var meta = new PolarLayoutMetadata
        {
            Rings = 1 ,
            SeatsPerRing = 10 ,
            RadiusStep = 40 ,
            StartAngleDegrees = 0 ,
            EndAngleDegrees = 90 ,
            HasPodium = false
        };
        var layout = PolarLayoutBuilder.BuildPolar(meta);
        foreach (PolarSeat s in layout.Seats.Cast<PolarSeat>())
        {
            s.AngleDegrees.Should().BeGreaterThanOrEqualTo(0);
            s.AngleDegrees.Should().BeLessThan(90);
        }
    }

    [Fact]
    public void BuildPolar_NoSegments_AngleRangeTooNarrow_ShouldReturnEmpty ()
    {
        var meta = new PolarLayoutMetadata
        {
            StartAngleDegrees = 0 ,
            EndAngleDegrees = 2 ,
            AisleRadialAngles = new List<double> { 0 } ,
            AisleRadialWidthDegrees = 10 ,
            HasPodium = false
        };
        var layout = PolarLayoutBuilder.BuildPolar(meta);
        layout.Seats.Count.Should().Be(0);
    }

    [Fact]
    public void BuildPolar_WithEmptyPositions_ShouldSkipThem ()
    {
        var meta = new PolarLayoutMetadata
        {
            RingSeatCounts = new List<int> { 4 , 4 } ,
            RadiusStep = 40 ,
            StartAngleDegrees = 0 ,
            EndAngleDegrees = 360 ,
            HasPodium = false ,
            EmptyPositions = new List<PolarRingAngle>
            {
                new() { Ring = 1, AngleDegrees = 45 },
                new() { Ring = 2, AngleDegrees = 225 }
            }
        };
        var layout = PolarLayoutBuilder.BuildPolar(meta);
        // 2 rings * 4 seats = 8, minus 2 = 6
        layout.Seats.Count.Should().Be(6);
        layout.Seats.Cast<PolarSeat>().Any(s => s.Ring == 1 && Math.Abs(s.AngleDegrees - 45) < 1e-6).Should().BeFalse();
        layout.Seats.Cast<PolarSeat>().Any(s => s.Ring == 2 && Math.Abs(s.AngleDegrees - 225) < 1e-6).Should().BeFalse();
    }
}