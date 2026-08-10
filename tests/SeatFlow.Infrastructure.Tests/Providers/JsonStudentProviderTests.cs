using System.Text;
using System.Text.Json;

namespace SeatFlow.Infrastructure.Tests.Providers;

public class JsonStudentProviderTests
{
    private static string CreateTempJson (string json)
    {
        var path = Path.GetTempFileName() + ".json";
        File.WriteAllText(path , json , Encoding.UTF8);
        return path;
    }

    private static string CreateRosterJson (List<Student> students)
    {
        var roster = new RosterFile { Version = "1.0" , Students = students };
        return JsonSerializer.Serialize(roster);
    }

    [Fact]
    public async Task LoadAsync_ValidRosterFile_ShouldReturnStudents ()
    {
        var students = new List<Student>
        {
            new() { Id = "1", Name = "Alice" },
            new() { Id = "2", Name = "Bob" }
        };
        var path = CreateTempJson(CreateRosterJson(students));
        try
        {
            var provider = new JsonStudentProvider();
            var result = await provider.LoadAsync(path , CancellationToken.None);
            result.Should().HaveCount(2);
            result[0].Id.Should().Be("1");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_EmptyRosterFile_ShouldReturnEmptyList ()
    {
        var path = CreateTempJson(CreateRosterJson([]));
        try
        {
            var provider = new JsonStudentProvider();
            var result = await provider.LoadAsync(path , CancellationToken.None);
            result.Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_FileNotFound_ShouldReturnEmptyList ()
    {
        var provider = new JsonStudentProvider();
        var students = await provider.LoadAsync("nonexistent.json" , CancellationToken.None);
        students.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_ShouldReturnEmptyList ()
    {
        var path = CreateTempJson("not valid json at all");
        try
        {
            var provider = new JsonStudentProvider();
            var result = await provider.LoadAsync(path , CancellationToken.None);
            result.Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
