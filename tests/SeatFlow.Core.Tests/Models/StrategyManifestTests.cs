namespace SeatFlow.Core.Tests.Models;

public class StrategyManifestTests
{
    [Fact]
    public void Default_HasEmptyValues ()
    {
        var m = new StrategyManifest();
        m.Id.Should().BeEmpty();
        m.Name.Should().BeEmpty();
        m.DisplayName.Should().BeEmpty();
        m.Version.Should().Be("1.0.0");
        m.DefaultPriority.Should().Be(0);
        m.DefaultEnabled.Should().BeTrue();
    }

    [Fact]
    public void CanSetAllProperties ()
    {
        var m = new StrategyManifest
        {
            Id = "RandomFill" ,
            Name = "Random Fill" ,
            DisplayName = "随机填充" ,
            Version = "1.0.0" ,
            Description = "将剩余学生随机填入空位" ,
            Author = "SeatFlow" ,
            Category = "fill" ,
            DefaultPriority = 10 ,
            DefaultEnabled = true
        };
        m.Id.Should().Be("RandomFill");
        m.DisplayName.Should().Be("随机填充");
        m.Category.Should().Be("fill");
        m.DefaultPriority.Should().Be(10);
        m.DefaultEnabled.Should().BeTrue();
    }
}
