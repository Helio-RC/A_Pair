namespace SeatFlow.Core.Tests;

public class StudentTests
{
    [Fact]
    public void NewStudent_ShouldHaveDefaultIdAndEmptyName ()
    {
        var s = new Student();
        s.Id.Should().NotBeNullOrEmpty();
        s.Name.Should().BeEmpty();
        s.Height.Should().BeNull();
        s.Gender.Should().BeNull();
        s.NeedsFrontRow.Should().BeFalse();
        s.FrontRowPreferenceScore.Should().Be(0);
        s.RecentSeatHistory.Should().NotBeNull();
        s.Extensions.Should().NotBeNull();
    }

    [Fact]
    public void RecentSeatHistory_ShouldBeCircular ()
    {
        var s = new Student();
        // 默认容量为 10，填充超过容量以验证环形覆盖行为
        for (int i = 1; i <= 12; i++)
            s.RecentSeatHistory.Add($"seat{i}");
        // 最旧的 seat1, seat2 被覆盖，保留最新的 10 条
        var expected = Enumerable.Range(3 , 10).Select(i => $"seat{i}").ToArray();
        s.RecentSeatHistory.GetAll().Should().BeEquivalentTo(expected);
    }
}