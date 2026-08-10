using SeatFlow.Core.Enums;

namespace SeatFlow.Infrastructure.Tests.Writers;

public class XlsxStudentWriterTests
{
    [Fact]
    public async Task WriteAsync_ThenReadBack_ShouldMatch ()
    {
        var students = new List<Student>
        {
            new() { Name = "Alice", Height = 165, Gender = Gender.Female, NeedsFrontRow = true },
            new() { Name = "Bob", Height = 180, Gender = Gender.Male, NeedsFrontRow = false }
        };
        var path = Path.GetTempFileName() + ".xlsx";
        try
        {
            var writer = new XlsxStudentWriter();
            await writer.WriteAsync(path , students , CancellationToken.None);

            var provider = new XlsxStudentProvider();
            var loaded = await provider.LoadAsync(path , CancellationToken.None);
            loaded.Should().HaveCount(2);
            loaded.First().Name.Should().Be("Alice");
            loaded.First().Gender.Should().Be(Gender.Female);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
