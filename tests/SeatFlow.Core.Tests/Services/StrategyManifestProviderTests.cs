using SeatFlow.Core.Services;

namespace SeatFlow.Core.Tests.Services;

public class StrategyManifestProviderTests
{
    [Fact]
    public void GetBuiltInManifests_ReturnsAllBuiltIn ()
    {
        var provider = new StrategyManifestProvider();
        var manifests = provider.GetBuiltInManifests();
        manifests.Should().NotBeNull();
        manifests.Should().HaveCount(7);
    }

    [Fact]
    public void Manifests_HaveRequiredFields ()
    {
        var provider = new StrategyManifestProvider();
        foreach (var m in provider.GetBuiltInManifests())
        {
            m.Id.Should().NotBeNullOrEmpty();
            m.DisplayName.Should().NotBeNullOrEmpty();
            m.Category.Should().NotBeNullOrEmpty();
            m.DefaultPriority.Should().BeGreaterThanOrEqualTo(0);
            m.Version.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public void Manifests_HaveUniqueIds ()
    {
        var provider = new StrategyManifestProvider();
        var ids = provider.GetBuiltInManifests().Select(m => m.Id).ToList();
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ContainsExpectedStrategies ()
    {
        var provider = new StrategyManifestProvider();
        var ids = provider.GetBuiltInManifests().Select(m => m.Id).ToHashSet();
        ids.Should().Contain("FixedSeat");
        ids.Should().Contain("DeskMate");
        ids.Should().Contain("FrontRowRotation");
        ids.Should().Contain("RandomFill");
        ids.Should().Contain("NoRepeatDeskMate");
        ids.Should().Contain("Defrag");
    }
}
