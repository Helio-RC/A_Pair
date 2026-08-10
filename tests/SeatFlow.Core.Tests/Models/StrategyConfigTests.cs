namespace SeatFlow.Core.Tests.Models;

public class StrategyConfigTests
{
    [Fact]
    public void Default_HasEmptyValues ()
    {
        var c = new StrategyConfig();
        c.Priority.Should().Be(0);
        c.IsEnabled.Should().BeTrue();
        c.Parameters.Should().BeEmpty();
    }

    [Fact]
    public void CanSetParameters ()
    {
        var c = new StrategyConfig
        {
            Priority = 50 ,
            IsEnabled = true ,
            Parameters = new Dictionary<string , object?>
            {
                ["group1"] = "Alice,Bob" ,
                ["maxDistance"] = 3
            }
        };
        c.Priority.Should().Be(50);
        c.IsEnabled.Should().BeTrue();
        c.Parameters["group1"].Should().Be("Alice,Bob");
        c.Parameters["maxDistance"].Should().Be(3);
    }

    [Fact]
    public void Parameters_AreMutable ()
    {
        var c = new StrategyConfig();
        c.Parameters["key"] = 42;
        c.Parameters.Should().ContainKey("key").WhoseValue.Should().Be(42);
        c.Parameters.Remove("key");
        c.Parameters.Should().BeEmpty();
    }
}
