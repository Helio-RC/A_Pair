namespace SeatFlow.Core.Tests;

public class AttributeBagTests
{
    [Fact]
    public void SetAndGet_ShouldRoundTrip ()
    {
        var bag = new AttributeBag();
        bag.Set("key" , 42);
        bool ok = bag.TryGet<int>("key" , out var value);
        ok.Should().BeTrue();
        value.Should().Be(42);
    }

    [Fact]
    public void Get_MissingKey_ShouldReturnFalse ()
    {
        var bag = new AttributeBag();
        bag.TryGet<string>("missing" , out _).Should().BeFalse();
    }

    [Fact]
    public void GetAll_ShouldReturnAllEntries ()
    {
        var bag = new AttributeBag();
        bag.Set("a" , 1);
        bag.Set("b" , "two");
        var all = bag.GetAll().ToList();
        all.Should().HaveCount(2);
        all.Should().Contain(kv => kv.Key == "a" && (int)kv.Value! == 1);
        all.Should().Contain(kv => kv.Key == "b" && (string)kv.Value! == "two");
    }
}