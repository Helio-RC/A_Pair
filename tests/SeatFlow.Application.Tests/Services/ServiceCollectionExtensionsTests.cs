using SeatFlow.Core.Exporters;
using SeatFlow.Core.Providers;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace SeatFlow.Application.Tests.Services;

public class ServiceCollectionExtensionsTests : IDisposable
{
    private readonly string _tempDir;

    public ServiceCollectionExtensionsTests ()
    {
        _tempDir = Path.Combine(Path.GetTempPath() , Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose ()
    {
        Log.CloseAndFlush();
        if (Directory.Exists(_tempDir))
        {
            // 重试几次，等待文件句柄释放
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    Directory.Delete(_tempDir , true);
                    break;
                }
                catch (IOException) when (i < 2)
                {
                    Thread.Sleep(200);
                }
            }
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AddSeatFlowApplication_ShouldRegisterAllExpectedServices ()
    {
        var services = new ServiceCollection();
        var snapshotBasePath = Path.Combine(_tempDir , "Snapshots");
        var pluginsPath = Path.Combine(_tempDir , "Plugins");

        services.AddSeatFlowApplication(snapshotBasePath , pluginsPath);
        var provider = services.BuildServiceProvider();

        // 外观
        provider.GetService<IApplicationFacade>().Should().NotBeNull();
        // 独立策略
        var strategies = provider.GetServices<ISeatingStrategy>().ToList();
        strategies.Should().Contain(s => s is FixedSeatStrategy);
        strategies.Should().Contain(s => s is RandomFillStrategy);
        strategies.Should().Contain(s => s is FrontRowRotationStrategy);

        // 依赖策略（在 RandomFill 上下文中执行）
        var dependentStrategies = provider.GetServices<IDependentSeatingStrategy>().ToList();
        dependentStrategies.Should().Contain(s => s is DeskMateStrategy);
        // 冲突解决器
        provider.GetService<IConflictResolver>().Should().NotBeNull();
        // 导出器
        var exporters = provider.GetServices<ISeatingPlanExporter>().ToList();
        exporters.Should().HaveCount(4);
        // 学生写入器
        var writers = provider.GetServices<IStudentWriter>().ToList();
        writers.Should().HaveCount(3);
        // 快照存储库（接口）
        provider.GetService<ISeatingSnapshotRepository>().Should().NotBeNull();
        // 插件管理器
        provider.GetService<IPluginManager>().Should().NotBeNull();
        // 插件配置服务
        provider.GetService<IPluginConfigurationService>().Should().NotBeNull();
        // 场地仓储
        provider.GetService<IVenueRepository>().Should().NotBeNull();
        // 应用设置仓储
        provider.GetService<IAppSettingsRepository>().Should().NotBeNull();
        // 学生数据集仓储
        provider.GetService<IStudentDatasetRepository>().Should().NotBeNull();
        // 学生数据提供器
        provider.GetService<IStudentProvider>().Should().NotBeNull();
    }

    [Fact]
    public async Task AddSeatFlowApplication_CreatesExpectedDirectories ()
    {
        var services = new ServiceCollection();
        var snapshotBasePath = Path.Combine(_tempDir , "Snapshots");
        var pluginsPath = Path.Combine(_tempDir , "Plugins");

        services.AddSeatFlowApplication(snapshotBasePath , pluginsPath);
        var provider = services.BuildServiceProvider();

        // 解析 IPluginManager 以触发其构造函数创建目录
        provider.GetService<IPluginManager>();
        Directory.Exists(pluginsPath).Should().BeTrue();

        // 快照目录由 Repository 在保存时按需创建
        var snapshotRepo = provider.GetRequiredService<ISeatingSnapshotRepository>();
        await snapshotRepo.SaveAsync(new SeatingSnapshot
        {
            Id = "test_snap" ,
            LayoutId = "venue1" ,
            CreatedAt = DateTime.Now
        } , TestContext.Current.CancellationToken);
        Assert.True(Directory.Exists(Path.Combine(snapshotBasePath , "Assignments" , "venue1")));
    }
}