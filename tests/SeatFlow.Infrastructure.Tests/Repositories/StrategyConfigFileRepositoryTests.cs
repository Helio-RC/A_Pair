using SeatFlow.Infrastructure.Migration;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Infrastructure.Tests.Repositories;

public class StrategyConfigFileRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StrategyConfigFileRepository _repo;

    public StrategyConfigFileRepositoryTests ()
    {
        _tempDir = Path.Combine(Path.GetTempPath() , Guid.NewGuid().ToString("N"));
        _repo = new StrategyConfigFileRepository(_tempDir , new FileMigrationService([]) , NullLogger<StrategyConfigFileRepository>.Instance);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsNull ()
    {
        var result = await _repo.LoadAsync("NonExistent" , TestContext.Current.CancellationToken);
        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip ()
    {
        var config = new StrategyConfig { Priority = 50 , IsEnabled = true };
        config.Parameters["key"] = "value";

        await _repo.SaveAsync("TestStrategy" , config , TestContext.Current.CancellationToken);
        var loaded = await _repo.LoadAsync("TestStrategy" , TestContext.Current.CancellationToken);

        loaded.Should().NotBeNull();
        loaded!.Priority.Should().Be(50);
        loaded.IsEnabled.Should().BeTrue();
        loaded.Parameters["key"]!.ToString().Should().Be("value");
    }

    [Fact]
    public async Task LoadAllAsync_EmptyDir_ReturnsEmpty ()
    {
        var result = await _repo.LoadAllAsync(TestContext.Current.CancellationToken);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAllAsync_WithMultipleFiles_ReturnsAll ()
    {
        await _repo.SaveAsync("A" , new StrategyConfig { Priority = 10 } , TestContext.Current.CancellationToken);
        await _repo.SaveAsync("B" , new StrategyConfig { Priority = 20 } , TestContext.Current.CancellationToken);

        var result = await _repo.LoadAllAsync(TestContext.Current.CancellationToken);
        result.Should().HaveCount(2);
        result.Should().ContainKeys("A" , "B");
    }

    [Fact]
    public async Task SaveAsync_Overwrites ()
    {
        await _repo.SaveAsync("X" , new StrategyConfig { Priority = 1 } , TestContext.Current.CancellationToken);
        await _repo.SaveAsync("X" , new StrategyConfig { Priority = 99 } , TestContext.Current.CancellationToken);

        var loaded = await _repo.LoadAsync("X" , TestContext.Current.CancellationToken);
        loaded!.Priority.Should().Be(99);
    }

    public void Dispose ()
    {
        try { Directory.Delete(_tempDir , true); } catch { }
        GC.SuppressFinalize(this);
    }
}
