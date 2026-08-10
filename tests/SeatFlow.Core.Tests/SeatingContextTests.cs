namespace SeatFlow.Core.Tests;

public class SeatingContextTests
{
    [Fact]
    public void Default_HasExpectedValues ()
    {
        var ctx = new SeatingWorkspace.SeatingContext();
        ctx.Layout.Should().BeNull();
        ctx.RotationCycle.Should().BeNull();
        ctx.EffectiveDate.Date.Should().Be(DateTime.Now.Date);
        ctx.Metadata.Should().BeEmpty();
    }

    [Fact]
    public void CanSetLayout ()
    {
        var layout = new ClassroomLayoutDefinition();
        var ctx = new SeatingWorkspace.SeatingContext { Layout = layout };
        ctx.Layout.Should().BeSameAs(layout);
    }

    [Fact]
    public void CanSetRotationCycle ()
    {
        var ctx = new SeatingWorkspace.SeatingContext { RotationCycle = "第3周" };
        ctx.RotationCycle.Should().Be("第3周");
    }

    [Fact]
    public void CanSetEffectiveDate ()
    {
        var date = new DateTime(2026 , 6 , 1);
        var ctx = new SeatingWorkspace.SeatingContext { EffectiveDate = date };
        ctx.EffectiveDate.Should().Be(date);
    }

    [Fact]
    public void Metadata_IsMutable ()
    {
        var ctx = new SeatingWorkspace.SeatingContext();
        ctx.Metadata["key"] = "value";
        ctx.Metadata.Should().ContainKey("key").WhoseValue.Should().Be("value");
    }
}
