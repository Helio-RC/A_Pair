namespace SeatFlow.Application.Tests.Plugins;

public class PluginConfigurationServiceTests : IDisposable
{
    private readonly string _tempDir;

    public PluginConfigurationServiceTests ()
    {
        _tempDir = Path.Combine(Path.GetTempPath() , Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose ()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir , true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task SaveAndLoad_ShouldRoundTrip ()
    {
        var service = new PluginConfigurationService(_tempDir);
        var config = new TestPluginConfig { Name = "MyPlugin" , Value = 42 };
        await service.SaveConfigurationAsync("plugin1" , config , CancellationToken.None);

        var loaded = await service.LoadConfigurationAsync<TestPluginConfig>("plugin1" , CancellationToken.None);
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("MyPlugin");
        loaded.Value.Should().Be(42);
    }

    [Fact]
    public async Task LoadAsync_FileMissing_ReturnsNewInstance ()
    {
        var service = new PluginConfigurationService(_tempDir);
        var loaded = await service.LoadConfigurationAsync<TestPluginConfig>("missing" , CancellationToken.None);
        loaded.Should().NotBeNull();
        loaded!.Name.Should().BeNull(); // 默认实例
    }

    public class TestPluginConfig
    {
        public string? Name { get; set; }
        public int Value { get; set; }
    }
}