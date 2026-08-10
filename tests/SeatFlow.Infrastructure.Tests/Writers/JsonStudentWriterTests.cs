using System.Text.Json;

namespace SeatFlow.Infrastructure.Tests.Writers;

public class JsonStudentWriterTests
{
    [Fact]
    public async Task WriteAsync_ShouldCreateRosterFile ()
    {
        var students = new List<Student>
    {
        new() { Id = "1", Name = "Alice" },
        new() { Id = "2", Name = "Bob" }
    };
        var path = Path.GetTempFileName() + ".json";
        try
        {
            var writer = new JsonStudentWriter();
            await writer.WriteAsync(path , students , CancellationToken.None);

            var json = await File.ReadAllTextAsync(path , TestContext.Current.CancellationToken);
            // camelCase 命名策略，验证包含 "name": "Alice"
            json.Should().Contain("\"name\": \"Alice\"");
            var roster = JsonSerializer.Deserialize<RosterFile>(json , JsonOptions.CaseInsensitiveRead);
            roster.Should().NotBeNull();
            roster!.Students.Should().HaveCount(2);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}