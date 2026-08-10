namespace SeatFlow.Infrastructure.Tests.Providers;

public class CompositeStudentProviderTests : IDisposable
{
    private readonly string _tempDir;

    public CompositeStudentProviderTests ()
    {
        _tempDir = Path.Combine(Path.GetTempPath() , Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task LoadAsync_EmptySource_ReturnsEmptyList ()
    {
        var provider = new CompositeStudentProvider(new CsvStudentProvider() , new XlsxStudentProvider() , new JsonStudentProvider());
        var result = await provider.LoadAsync("" , TestContext.Current.CancellationToken);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_NonExistentFile_ReturnsEmptyList ()
    {
        var provider = new CompositeStudentProvider(new CsvStudentProvider() , new XlsxStudentProvider() , new JsonStudentProvider());
        var result = await provider.LoadAsync("/nonexistent/file.csv" , TestContext.Current.CancellationToken);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_UnknownExtension_ReturnsEmptyList ()
    {
        var path = Path.Combine(_tempDir , "test.xyz");
        await File.WriteAllTextAsync(path , "data" , TestContext.Current.CancellationToken);
        var provider = new CompositeStudentProvider(new CsvStudentProvider() , new XlsxStudentProvider() , new JsonStudentProvider());
        var result = await provider.LoadAsync(path , TestContext.Current.CancellationToken);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_ValidCsv_ReturnsStudents ()
    {
        var path = Path.Combine(_tempDir , "students.csv");
        await File.WriteAllTextAsync(path , "姓名\n\n张三\n李四" , TestContext.Current.CancellationToken);

        var provider = new CompositeStudentProvider(new CsvStudentProvider() , new XlsxStudentProvider() , new JsonStudentProvider());
        var result = await provider.LoadAsync(path , TestContext.Current.CancellationToken);
        result.Should().HaveCount(2);
        result[0].Name.Should().Be("张三");
        result[1].Name.Should().Be("李四");
    }

    [Fact]
    public async Task LoadAsync_ValidJson_ReturnsStudents ()
    {
        var path = Path.Combine(_tempDir , "students.json");
        var json = """{"version":"1.0","students":[{"id":"1","name":"Alice"},{"id":"2","name":"Bob"}]}""";
        await File.WriteAllTextAsync(path , json , TestContext.Current.CancellationToken);

        var provider = new CompositeStudentProvider(new CsvStudentProvider() , new XlsxStudentProvider() , new JsonStudentProvider());
        var result = await provider.LoadAsync(path , TestContext.Current.CancellationToken);
        result.Should().HaveCount(2);
        result[0].Name.Should().BeOneOf("Alice" , "Bob");
    }

    public void Dispose ()
    {
        try { Directory.Delete(_tempDir , true); } catch { }
        GC.SuppressFinalize(this);
    }
}
