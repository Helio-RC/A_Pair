using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SeatFlow.Application.Plugins;

namespace SeatFlow.Application.Tests.Plugins.Integration;

/// <summary>
/// 插件端到端集成测试：从 <c>src/plugin-examples/dist</c> 安装示例插件包，
/// 验证 v2 格式装配、独立策略执行、依赖策略接入与配置路由。
/// 依赖 <c>src/plugin-examples/build.sh</c> 的构建产物；产物缺失时测试跳过。
/// </summary>
public class PluginIntegrationTests : IDisposable
{
    private readonly string _pluginsDir;
    private readonly ILogger<PluginManager> _logger;

    public PluginIntegrationTests ()
    {
        _pluginsDir = Path.Combine(Path.GetTempPath() , $"ap_integration_{Guid.NewGuid():N}");
        _logger = NullLogger<PluginManager>.Instance;
    }

    public void Dispose ()
    {
        try { Directory.Delete(_pluginsDir , recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private static string DistDir
    {
        get
        {
            var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory ,
                "../../../../../src/plugin-examples/dist"));
            return Directory.Exists(dir) ? dir : string.Empty;
        }
    }

    private void SkipIfNoPackages ()
    {
        if (string.IsNullOrEmpty(DistDir) || !Directory.EnumerateFiles(DistDir , "*.ap-plugin").Any())
            Assert.Skip("缺少示例插件包（请先运行 src/plugin-examples/build.sh）");
    }

    private async Task<PluginManager> CreateManagerWithInstalledPackagesAsync ()
    {
        SkipIfNoPackages();
        var manager = new PluginManager(_pluginsDir , _logger);
        foreach (var packagePath in Directory.EnumerateFiles(DistDir , "*.ap-plugin").OrderBy(p => p))
            await manager.InstallFromPackageAsync(packagePath , CancellationToken.None);
        return manager;
    }

    [Fact]
    public async Task LoadPluginsAsync_AllExamplePackages_AssembleWithManifestIds ()
    {
        var manager = await CreateManagerWithInstalledPackagesAsync();

        var plugins = (await manager.LoadPluginsAsync(category: null , CancellationToken.None)).ToList();

        // height-sort + desk-pair + front-row-first + order-assign + multi-strat-a/b = 6 个策略
        plugins.Should().HaveCount(6);
        var ids = plugins.Select(p => p.Strategy.Id).ToHashSet();
        ids.Should().BeEquivalentTo(
            "height-sort" , "desk-pair" , "front-row-first" , "order-assign" , "multi-strat-a" , "multi-strat-b");

        // 缺陷 A 回归：所有策略 ID 必须为 manifest id（脚本插件不得是随机 Guid）
        foreach (var p in plugins)
            p.Strategy.Id.Should().MatchRegex(@"^[a-z0-9-]+$");

        // 依赖策略标记正确
        var deskPair = plugins.First(p => p.Strategy.Id == "desk-pair");
        deskPair.StrategyManifest!.IsIndependent.Should().BeFalse();
        deskPair.Strategy.Should().BeAssignableTo<IPluginDependentSeatingStrategy>();

        // 插件策略均直接为 IPluginSeatingStrategy（一级类型，无适配器包装）
        plugins.Should().OnlyContain(p => p.Strategy is IPluginSeatingStrategy);
    }

    [Fact]
    public async Task HeightSort_ExecutesThroughPipeline_ByHeightDescending ()
    {
        var manager = await CreateManagerWithInstalledPackagesAsync();
        var plugins = (await manager.LoadPluginsAsync(category: null , CancellationToken.None)).ToList();
        var heightSort = plugins.First(p => p.Strategy.Id == "height-sort").Strategy;

        // 5 名学生（含一名无身高数据）
        var students = new List<Student>
        {
            new() { Id = "s1" , Name = "矮" , Height = 150 },
            new() { Id = "s2" , Name = "高" , Height = 190 },
            new() { Id = "s3" , Name = "中" , Height = 170 },
            new() { Id = "s4" , Name = "未知" },
            new() { Id = "s5" , Name = "较高" , Height = 180 },
        };
        var seats = Enumerable.Range(1 , 5).Select(i => (Seat)new GridSeat { Id = $"seat{i}" }).ToList();
        var workspace = new SeatingWorkspace(students , seats);

        var pipeline = new StrategyExecutionPipeline(pluginStrategies: [heightSort]);
        await pipeline.ExecuteAsync(workspace , cancellationToken: CancellationToken.None);

        // 空座按 GetEmptySeats 顺序（seat1..seat5），身高降序填入
        var assignments = workspace.GetAssignments();
        assignments.Should().HaveCount(5);
        assignments["seat1"].Should().Be("s2"); // 190
        assignments["seat2"].Should().Be("s5"); // 180
        assignments["seat3"].Should().Be("s3"); // 170
        assignments["seat4"].Should().Be("s1"); // 150
        assignments["seat5"].Should().Be("s4"); // 无身高，最后
    }

    [Fact]
    public async Task DeskPair_AsDependentStrategy_ParticipatesInRandomFill ()
    {
        var manager = await CreateManagerWithInstalledPackagesAsync();
        var plugins = (await manager.LoadPluginsAsync(category: null , CancellationToken.None)).ToList();
        var deskPair = plugins.First(p => p.Strategy.Id == "desk-pair").Strategy as IPluginDependentSeatingStrategy;

        // 6 名学生 6 个相邻座位
        var students = Enumerable.Range(1 , 6).Select(i => new Student { Id = $"s{i}" }).ToList();
        var seats = Enumerable.Range(1 , 6).Select(i => (Seat)new GridSeat { Id = $"seat{i}" }).ToList();
        var workspace = new SeatingWorkspace(students , seats);

        var randomFill = new RandomFillStrategy(new Random(42));
        randomFill.LoadDependentStrategies([new PluginDependentAdapter(deskPair!)]);
        await randomFill.ExecuteAsync(workspace , CancellationToken.None);

        // 所有学生均入座
        workspace.GetAssignments().Should().HaveCount(6);
        // 至少存在一对相邻座位（seat{n} 与 seat{n+1}）均被占用 —— 同桌配对发生
        var assignments = workspace.GetAssignments();
        bool hasAdjacentPair = Enumerable.Range(1 , 5)
            .Any(n => assignments.ContainsKey($"seat{n}") && assignments.ContainsKey($"seat{n + 1}"));
        hasAdjacentPair.Should().BeTrue("DeskPair 应在随机分配中制造相邻配对");
    }

    [Fact]
    public async Task FindStrategy_ConfigRoutesToPluginDirectories ()
    {
        var manager = await CreateManagerWithInstalledPackagesAsync();
        await manager.LoadPluginsAsync(category: null , CancellationToken.None);

        // 配置路由：FindStrategy 应命中脚本插件（ID 为 manifest id，非 Guid）
        var (pkg , plugin) = manager.FindStrategy("front-row-first");
        pkg.Should().NotBeNull();
        pkg!.PackageManifest.Id.Should().Be("script-plugins");
        plugin.Should().NotBeNull();
        plugin!.Strategy.Id.Should().Be("front-row-first");
        plugin.Entry!.ScriptType.Should().Be("lua");
    }

    [Fact]
    public async Task UnknownPluginKind_IsSkippedWithWarning ()
    {
        var manager = new PluginManager(_pluginsDir , _logger);
        var packageDir = Path.Combine(_pluginsDir , "future-pkg");
        Directory.CreateDirectory(packageDir);
        File.WriteAllText(Path.Combine(packageDir , "plugins-manifest.json") ,
            """
            {"id":"future-pkg","name":"Future","version":"1.0.0","type":"provider",
             "plugins":[{"kind":"data-provider","path":"p","manifest":"p/manifest.json"}]}
            """);

        var plugins = (await manager.LoadPluginsAsync(category: null , CancellationToken.None)).ToList();

        // 未支持的 kind 策略被跳过（无策略加载），但包本身仍注册——便于 UI 定位与卸载
        plugins.Should().BeEmpty();
        manager.LoadedPackages.Should().ContainKey("future-pkg");
    }
}
