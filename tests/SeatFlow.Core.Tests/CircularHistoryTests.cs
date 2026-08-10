namespace SeatFlow.Core.Tests;

public class CircularHistoryTests
{
    [Fact]
    public void Add_ShouldStoreItems_WithinCapacity ()
    {
        var history = new CircularHistory<int>(3);
        history.Add(1);
        history.Add(2);
        history.Add(3);

        history.GetAll().Should().BeEquivalentTo([1 , 2 , 3]);
    }

    [Fact]
    public void Add_OverCapacity_ShouldOverwriteOldest ()
    {
        var history = new CircularHistory<int>(2);
        history.Add(1);
        history.Add(2);
        history.Add(3);

        history.GetAll().Should().BeEquivalentTo([2 , 3]);
    }

    [Fact]
    public void GetAll_Empty_ShouldReturnEmpty ()
    {
        var history = new CircularHistory<int>(5);
        history.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void Constructor_ZeroCapacity_ShouldThrow ()
    {
        Action act = () => new CircularHistory<int>(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}