using SeatFlow.Infrastructure.Migration;
namespace SeatFlow.Infrastructure.Tests.Repositories;

public class JsonStudentDatasetRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonStudentDatasetRepository _repo;

    public JsonStudentDatasetRepositoryTests ()
    {
        _tempDir = Path.Combine(Path.GetTempPath() , Guid.NewGuid().ToString("N"));
        _repo = new JsonStudentDatasetRepository(_tempDir , new FileMigrationService([]));
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip ()
    {
        var students = new List<Student>
        {
            new() { Id = "1", Name = "Alice" },
            new() { Id = "2", Name = "Bob" }
        };

        const string id = "test";
        await _repo.SaveAsync(id , "test" , students , "original.xlsx" , TestContext.Current.CancellationToken);
        var loaded = await _repo.LoadAsync(id , TestContext.Current.CancellationToken);

        loaded.Should().NotBeNull();
        loaded!.Should().HaveCount(2);
        loaded[0].Name.Should().Be("Alice");
    }

    [Fact]
    public async Task LoadAsync_InvalidId_ReturnsNull ()
    {
        var result = await _repo.LoadAsync("nonexistent" , TestContext.Current.CancellationToken);
        result.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_EmptyDir_ReturnsEmpty ()
    {
        var result = await _repo.ListAsync(TestContext.Current.CancellationToken);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_WithSavedDataset_ReturnsInfo ()
    {
        const string id = "mydataset";
        await _repo.SaveAsync(id , "mydataset" , new List<Student> { new() { Id = "1" , Name = "X" } } , null , TestContext.Current.CancellationToken);
        var list = await _repo.ListAsync(TestContext.Current.CancellationToken);
        list.Should().HaveCount(1);
        list[0].Id.Should().Be(id);
        list[0].Name.Should().Be("mydataset");
        list[0].StudentCount.Should().Be(1);
    }

    [Fact]
    public async Task DeleteAsync_RemovesDataset ()
    {
        const string id = "temp";
        await _repo.SaveAsync(id , "temp" , new List<Student>() , null , TestContext.Current.CancellationToken);
        await _repo.DeleteAsync(id , TestContext.Current.CancellationToken);
        var result = await _repo.LoadAsync(id , TestContext.Current.CancellationToken);
        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_SetsStudentsHash ()
    {
        var students = new List<Student>
        {
            new() { Id = "2" , Name = "Bob" },
            new() { Id = "1" , Name = "Alice" }
        };

        const string id = "hash-test";
        await _repo.SaveAsync(id , "hash-test" , students , null , TestContext.Current.CancellationToken);

        var path = Path.Combine(_tempDir , $"{id}.roster.json");
        var json = await File.ReadAllTextAsync(path , TestContext.Current.CancellationToken);
        json.Should().Contain("\"studentsHash\"");
        json.Should().NotContain("\"contentHash\"");
    }

    [Fact]
    public async Task SaveAsync_StudentsHash_IsDeterministic ()
    {
        var students = new List<Student>
        {
            new() { Id = "2" , Name = "Bob" , Height = 170 },
            new() { Id = "1" , Name = "Alice" , Height = 165 }
        };

        const string id = "det-test";
        await _repo.SaveAsync(id , "det-test" , students , null , TestContext.Current.CancellationToken);
        var path = Path.Combine(_tempDir , $"{id}.roster.json");
        var json1 = await File.ReadAllTextAsync(path , TestContext.Current.CancellationToken);

        // 删除后重新保存，哈希应一致
        File.Delete(path);
        await _repo.SaveAsync(id , "det-test" , students , null , TestContext.Current.CancellationToken);
        var json2 = await File.ReadAllTextAsync(path , TestContext.Current.CancellationToken);

        ExtractHash(json1).Should().Be(ExtractHash(json2));
    }

    private static string? ExtractHash (string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("studentsHash" , out var h) ? h.GetString() : null;
    }

    public void Dispose ()
    {
        try { Directory.Delete(_tempDir , true); } catch { }
        GC.SuppressFinalize(this);
    }
}
