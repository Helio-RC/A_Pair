using SeatFlow.Infrastructure.Migration;
namespace SeatFlow.Infrastructure.Tests.Repositories;

public class SeatingSnapshotRepositoryTests : IDisposable
{
    private readonly string _testDir;

    public SeatingSnapshotRepositoryTests ()
    {
        _testDir = Path.Combine(Path.GetTempPath() , Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose ()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir , true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task SaveAndLoad_ShouldRoundTrip ()
    {
        var repo = new SeatingSnapshotRepository(_testDir , new FileMigrationService([]));
        var snapshot = new SeatingSnapshot
        {
            Description = "Test" ,
            LayoutId = "venue1" ,
            SeatAssignments = new Dictionary<string , string> { { "s1" , "p1" } }
        };
        await repo.SaveAsync(snapshot , TestContext.Current.CancellationToken);
        var loaded = repo.Load(snapshot.Id);
        loaded.Should().NotBeNull();
        loaded!.Description.Should().Be("Test");
        loaded.SeatAssignments["s1"].Should().Be("p1");
    }

    [Fact]
    public async Task ListByVenueAsync_ShouldFilterCorrectly ()
    {
        var repo = new SeatingSnapshotRepository(_testDir , new FileMigrationService([]));
        var snap1 = new SeatingSnapshot { LayoutId = "venue1" };
        var snap2 = new SeatingSnapshot { LayoutId = "venue2" };
        await repo.SaveAsync(snap1 , TestContext.Current.CancellationToken);
        await repo.SaveAsync(snap2 , TestContext.Current.CancellationToken);

        var list = await repo.ListByVenueAsync("venue1" , TestContext.Current.CancellationToken);
        list.Should().HaveCount(1);
        list[0].LayoutId.Should().Be("venue1");
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveFile ()
    {
        var repo = new SeatingSnapshotRepository(_testDir , new FileMigrationService([]));
        var snap = new SeatingSnapshot();
        await repo.SaveAsync(snap , TestContext.Current.CancellationToken);
        await repo.DeleteAsync(snap.Id , TestContext.Current.CancellationToken);
        repo.Load(snap.Id).Should().BeNull();
    }

    /// <summary>
    /// 跨会话场景：磁盘上已有旧快照文件，新会话中 SaveAsync 后
    /// DeleteAsync 仍应能通过全盘扫描找到并删除旧快照。
    /// 修复前失败——SaveAsync 错误设置 _indexBuilt=true 阻止了 BuildIndex 全盘扫描。
    /// </summary>
    [Fact]
    public async Task DeleteAsync_ShouldDeleteOldSnapshotAfterNewSave ()
    {
        // 1) 第一个会话：保存旧快照
        var repo1 = new SeatingSnapshotRepository(_testDir , new FileMigrationService([]));
        var oldSnapshot = new SeatingSnapshot { LayoutId = "venue-a" , Description = "old" };
        await repo1.SaveAsync(oldSnapshot , TestContext.Current.CancellationToken);

        // 2) 第二个会话：新 repo 实例（模拟应用重启），先 SaveAsync 新快照
        var repo2 = new SeatingSnapshotRepository(_testDir , new FileMigrationService([]));
        var newSnapshot = new SeatingSnapshot { LayoutId = "venue-a" , Description = "new" };
        await repo2.SaveAsync(newSnapshot , TestContext.Current.CancellationToken);

        // 3) 删除旧快照——应通过全盘扫描找到并删除
        await repo2.DeleteAsync(oldSnapshot.Id , TestContext.Current.CancellationToken);

        repo2.Load(oldSnapshot.Id).Should().BeNull();
        // 新快照不受影响
        repo2.Load(newSnapshot.Id).Should().NotBeNull();
    }

    /// <summary>
    /// SaveAsync 后立即 LoadAsync / DeleteAsync 应能正常工作，
    /// 因为 SaveAsync 调用 BuildIndex() 确保索引完整性。
    /// </summary>
    [Fact]
    public async Task SaveAndDelete_ShouldWorkWithoutPriorIndexBuild ()
    {
        var repo = new SeatingSnapshotRepository(_testDir , new FileMigrationService([]));

        // 仅 SaveAsync（不经过 Load/Delete 等其他触发 BuildIndex 的操作）
        var snap = new SeatingSnapshot { LayoutId = "venue-x" };
        await repo.SaveAsync(snap , TestContext.Current.CancellationToken);

        // Delete 应能通过 BuildIndex 全盘扫描找到并删除
        await repo.DeleteAsync(snap.Id , TestContext.Current.CancellationToken);
        repo.Load(snap.Id).Should().BeNull();
    }
}