using SeatFlow.Infrastructure.Migration;
namespace SeatFlow.Infrastructure.Tests.Repositories;

public class JsonAppSettingsRepositoryTests
{
    private static string GetTempFilePath () => Path.GetTempFileName() + ".json";

    [Fact]
    public async Task LoadAsync_FileMissing_ShouldReturnDefault ()
    {
        var path = GetTempFilePath();
        try
        {
            var repo = new JsonAppSettingsRepository(path , new FileMigrationService([]));
            var settings = await repo.LoadAsync(CancellationToken.None);
            settings.WindowState.Width.Should().Be(1200);
            settings.WindowState.Height.Should().Be(800);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task SaveAndLoad_ShouldRoundTrip ()
    {
        var path = GetTempFilePath();
        try
        {
            var repo = new JsonAppSettingsRepository(path , new FileMigrationService([]));
            var original = new AppSettings
            {
                LastOpenedFilePath = "/test/file.txt" ,
                WindowState = new WindowStateSettings
                {
                    Width = 800 ,
                    Height = 600 ,
                    IsMaximized = true
                }
            };
            await repo.SaveAsync(original , CancellationToken.None);
            var loaded = await repo.LoadAsync(CancellationToken.None);
            loaded.LastOpenedFilePath.Should().Be("/test/file.txt");
            loaded.WindowState.IsMaximized.Should().BeTrue();
            loaded.WindowState.Width.Should().Be(800);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SettingsFilePath_ShouldReturnCorrectPath ()
    {
        var repo = new JsonAppSettingsRepository("C:\\test.json" , new FileMigrationService([]));
        repo.SettingsFilePath.Should().Be("C:\\test.json");
    }
}