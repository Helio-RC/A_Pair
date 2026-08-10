namespace SeatFlow.Core.Tests.Models;

public class StrategyDisplayInfoTests
{
    [Fact]
    public void Default_IsBuiltIn ()
    {
        var d = new StrategyDisplayInfo { Source = "builtin" , IsEnabled = true , DefaultEnabled = true };
        d.IsBuiltIn.Should().BeTrue();
        d.IsModified.Should().BeFalse();
    }

    [Fact]
    public void IsModified_WhenPriorityChanged ()
    {
        var d = new StrategyDisplayInfo
        {
            DefaultPriority = 10 ,
            Priority = 20
        };
        d.IsModified.Should().BeTrue();
    }

    [Fact]
    public void IsModified_WhenEnabledChanged ()
    {
        var d = new StrategyDisplayInfo
        {
            DefaultEnabled = true ,
            IsEnabled = false
        };
        d.IsModified.Should().BeTrue();
    }

    [Fact]
    public void IsNotModified_WhenSame ()
    {
        var d = new StrategyDisplayInfo
        {
            DefaultPriority = 10 ,
            Priority = 10 ,
            DefaultEnabled = true ,
            IsEnabled = true
        };
        d.IsModified.Should().BeFalse();
    }

    [Fact]
    public void NonBuiltIn_SourceIsPlugin ()
    {
        var d = new StrategyDisplayInfo { Source = "plugin:MyPlugin" };
        d.IsBuiltIn.Should().BeFalse();
    }
}
