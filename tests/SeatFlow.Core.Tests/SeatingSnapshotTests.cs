namespace SeatFlow.Core.Tests;

public class SeatingSnapshotTests
{
    [Fact]
    public void NewSnapshot_ShouldHaveDefaults ()
    {
        var snap = new SeatingSnapshot();
        snap.Id.Should().NotBeNullOrEmpty();
        snap.CreatedAt.Should().BeCloseTo(DateTime.Now , TimeSpan.FromSeconds(1));
        snap.SeatAssignments.Should().BeEmpty();
    }
}