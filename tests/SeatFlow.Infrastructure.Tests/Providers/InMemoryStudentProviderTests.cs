namespace SeatFlow.Infrastructure.Tests.Providers;

public class InMemoryStudentProviderTests
{
    [Fact]
    public async Task LoadAsync_ShouldReturnThreeDefaultStudents ()
    {
        var provider = new InMemoryStudentProvider();
        var students = await provider.LoadAsync("ignored" , CancellationToken.None);
        students.Should().HaveCount(3);
        students.Should().Contain(s => s.Name == "Alice");
        students.Should().Contain(s => s.Name == "Bob");
        students.Should().Contain(s => s.Name == "Charlie");
    }
}