namespace SeatFlow.Core.Tests.Utilities;

public class CircularHistoryTests
{
    [Fact]
    public void Resize_Expand_KeepsAllEntries ()
    {
        var history = new CircularHistory<string>(3);
        history.Add("a");
        history.Add("b");
        history.Add("c");

        history.Resize(5);

        history.GetAll().Should().BeEquivalentTo(["a" , "b" , "c"]);
    }

    [Fact]
    public void Resize_Shrink_KeepsMostRecent ()
    {
        var history = new CircularHistory<string>(5);
        history.Add("a");
        history.Add("b");
        history.Add("c");
        history.Add("d");
        history.Add("e");

        history.Resize(3);

        // 缩容后仅保留最近 3 条
        history.GetAll().Should().BeEquivalentTo(["c" , "d" , "e"]);
    }

    [Fact]
    public void Resize_SameCapacity_NoOp ()
    {
        var history = new CircularHistory<string>(3);
        history.Add("x");
        history.Add("y");

        history.Resize(3);

        history.GetAll().Should().BeEquivalentTo(["x" , "y"]);
    }

    [Fact]
    public void Resize_NegativeCapacity_Throws ()
    {
        var history = new CircularHistory<string>(3);
        var act = () => history.Resize(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Resize_ZeroCapacity_Throws ()
    {
        var history = new CircularHistory<string>(3);
        var act = () => history.Resize(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Resize_ShrinkToExactlyCount_KeepsAll ()
    {
        var history = new CircularHistory<string>(5);
        history.Add("a");
        history.Add("b");
        history.Add("c");

        history.Resize(3);

        history.GetAll().Should().BeEquivalentTo(["a" , "b" , "c"]);
    }
}
