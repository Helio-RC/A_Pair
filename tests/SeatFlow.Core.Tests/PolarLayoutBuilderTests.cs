namespace SeatFlow.Core.Tests;

public class PolarLayoutBuilderTests
{
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

        layout.Seats.Count.Should().Be(24); // 3×8
        layout.Seats.All(s => s is PolarSeat).Should().BeTrue();
        layout.Seats.Cast<PolarSeat>().Select(s => s.Ring).Distinct().OrderBy(r => r)
            .Should().Equal(1 , 2 , 3);
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

        // 4 aisles at 0/90/180/270 → 4 segments → 4 distinct LogicalGroups
        var groups = layout.Seats.Cast<PolarSeat>().Select(s => s.LogicalGroup).Distinct().ToList();
        groups.Count.Should().Be(4);
        groups.Should().AllSatisfy(g => g.Should().Contain("R1S"));

        // No seat should fall inside an aisle (within 2.5° of aisle center)
        foreach (PolarSeat s in layout.Seats.Cast<PolarSeat>())
        {
            foreach (double aisleAngle in meta.AisleRadialAngles)
            {
                double diff = Math.Abs(s.AngleDegrees - aisleAngle);
                if (diff > 180) diff = 360 - diff;
                diff.Should().BeGreaterThan((meta.AisleRadialWidthDegrees / 2) - 0.01);
            }
        }
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
        double r1 = seats.First(s => s.Ring == 1).Radius;
        double r2 = seats.First(s => s.Ring == 2).Radius;
        double r3 = seats.First(s => s.Ring == 3).Radius;

        // ring1: 1×40 = 40
        // ring2: 2×40 + 1×20 = 100  (aisle after ring 1)
        // ring3: 3×40 + 2×20 = 160  (aisles after ring 1 and 2)
        r1.Should().BeApproximately(40 , 1e-9);
        r2.Should().BeApproximately(100 , 1e-9);
        r3.Should().BeApproximately(160 , 1e-9);
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
        podium.X.Should().Be(170);  // 200 - 30
        podium.Y.Should().Be(170);
        podium.Width.Should().Be(60);   // 30*2
        podium.Height.Should().Be(60);
    }

    [Fact]
    public void BuildPolar_NoPodium_ShouldNotAddObstacle ()
    {
        var meta = new PolarLayoutMetadata
        {
            Rings = 1 ,
            SeatsPerRing = 8 ,
            HasPodium = false
        };

        var layout = PolarLayoutBuilder.BuildPolar(meta);

        layout.Obstacles.Should().BeEmpty();
    }

    [Fact]
    public void BuildPolar_BackwardCompat_RingsAndSeatsPerRing ()
    {
        // Old API: BuildPolar(double, int, int)
        var layout = PolarLayoutBuilder.BuildPolar(40 , 2 , 10);

        layout.Seats.Count.Should().Be(20); // 2×10
        layout.LayoutType.Should().Be(LayoutType.Polar);
        layout.Seats.Cast<PolarSeat>().Select(s => s.Ring).Distinct().OrderBy(r => r)
            .Should().Equal(1 , 2);
    }

    [Fact]
    public void BuildPolar_RingSeatCounts_TakesPrecedence ()
    {
        var meta = new PolarLayoutMetadata
        {
            Rings = 10 ,          // should be ignored
            SeatsPerRing = 100 ,  // should be ignored
            RingSeatCounts = new List<int> { 4 , 6 , 8 } ,
            RadiusStep = 40 ,
            HasPodium = false
        };

        var layout = PolarLayoutBuilder.BuildPolar(meta);

        layout.Seats.Count.Should().Be(18); // 4+6+8, not 10×100
        var byRing = layout.Seats.Cast<PolarSeat>().GroupBy(s => s.Ring).ToDictionary(g => g.Key , g => g.Count());
        byRing[1].Should().Be(4);
        byRing[2].Should().Be(6);
        byRing[3].Should().Be(8);
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

        layout.Seats.Count.Should().Be(10);
        foreach (PolarSeat s in layout.Seats.Cast<PolarSeat>())
        {
            s.AngleDegrees.Should().BeGreaterThanOrEqualTo(0);
            s.AngleDegrees.Should().BeLessThan(90);
        }
    }

    [Fact]
    public void BuildPolar_AllSeatsHaveRingProperty ()
    {
        var meta = new PolarLayoutMetadata
        {
            RingSeatCounts = new List<int> { 6 , 12 } ,
            RadiusStep = 40 ,
            HasPodium = false
        };

        var layout = PolarLayoutBuilder.BuildPolar(meta);

        layout.Seats.Cast<PolarSeat>().Should().AllSatisfy(s =>
            s.Ring.Should().BeGreaterThanOrEqualTo(1));
        layout.Seats.Cast<PolarSeat>().Where(s => s.Ring == 1).Should().HaveCount(6);
        layout.Seats.Cast<PolarSeat>().Where(s => s.Ring == 2).Should().HaveCount(12);
    }

    [Fact]
    public void BuildPolar_NoSegments_AngleRangeTooNarrow_ShouldReturnEmpty ()
    {
        var meta = new PolarLayoutMetadata
        {
            StartAngleDegrees = 0 ,
            EndAngleDegrees = 2 ,
            AisleRadialAngles = new List<double> { 0 } ,
            AisleRadialWidthDegrees = 10 ,  // 10° wide aisle from -5° to 5° covers everything
            HasPodium = false
        };

        var layout = PolarLayoutBuilder.BuildPolar(meta);

        layout.Seats.Count.Should().Be(0);
    }
}
