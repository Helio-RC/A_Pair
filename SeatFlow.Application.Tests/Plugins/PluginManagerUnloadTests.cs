using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Application.Tests.Plugins;

public class PluginManagerUnloadTests : IDisposable
{
    private readonly string _pluginsDir;
    private readonly ILogger<PluginManager> _logger;

    public PluginManagerUnloadTests ()
    {
        _pluginsDir = Path.Combine(Path.GetTempPath() , $"ap_unload_tests_{Guid.NewGuid():N}");
        _logger = Substitute.For<ILogger<PluginManager>>();
    }

    public void Dispose ()
    {
        try { Directory.Delete(_pluginsDir , recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task UnloadPackageAsync_AssemblyLoadContextIsReclaimed ()
    {
        // 加载+卸载在 NoInlining 同步隔离方法内完成：async 方法体的 JIT 状态机字段
        // 会保留局部引用（即使显式置 null），阻止 collectible ALC 回收
        CreateFixturePackage();
        var weakRefs = await Task.Run(() => LoadAndUnloadInIsolation(_pluginsDir));

        weakRefs.Should().ContainSingle("fixture 包应创建一个 AssemblyLoadContext");
        AssertReclaimed(weakRefs);
    }

    [Fact]
    public async Task RefreshPackageAsync_OldContextsAreUnloaded ()
    {
        CreateFixturePackage();
        var weakRefs = await Task.Run(() => RefreshInIsolation(_pluginsDir));

        weakRefs.Should().ContainSingle();
        AssertReclaimed(weakRefs);
    }

    [Fact]
    public async Task UnloadAllAsync_AllContextsReclaimed ()
    {
        CreateFixturePackage();
        var weakRefs = await Task.Run(() => UnloadAllInIsolation(_pluginsDir));

        weakRefs.Should().ContainSingle();
        AssertReclaimed(weakRefs);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static List<WeakReference> LoadAndUnloadInIsolation (string pluginsDir)
    {
        var manager = new PluginManager(pluginsDir , NullLogger<PluginManager>.Instance);
        var plugins = manager.LoadPluginsAsync(category: null , CancellationToken.None)
            .GetAwaiter().GetResult().ToList();
        plugins.Count.Should().Be(1);

        var weakRefs = CaptureContextWeakRefs(manager);
        plugins.Clear();
        manager.UnloadPackageAsync("fixture-pkg").GetAwaiter().GetResult();
        return weakRefs;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static List<WeakReference> RefreshInIsolation (string pluginsDir)
    {
        var manager = new PluginManager(pluginsDir , NullLogger<PluginManager>.Instance);
        var plugins = manager.LoadPluginsAsync(category: null , CancellationToken.None)
            .GetAwaiter().GetResult().ToList();
        plugins.Count.Should().Be(1);

        var weakRefs = CaptureContextWeakRefs(manager);
        plugins.Clear();
        manager.RefreshPackageAsync("fixture-pkg" , CancellationToken.None).GetAwaiter().GetResult();

        var newContexts = CaptureContextWeakRefs(manager);
        newContexts.Should().ContainSingle("刷新后应有新 ALC 存活");
        return weakRefs;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static List<WeakReference> UnloadAllInIsolation (string pluginsDir)
    {
        var manager = new PluginManager(pluginsDir , NullLogger<PluginManager>.Instance);
        var plugins = manager.LoadPluginsAsync(category: null , CancellationToken.None)
            .GetAwaiter().GetResult().ToList();
        plugins.Count.Should().Be(1);

        var weakRefs = CaptureContextWeakRefs(manager);
        plugins.Clear();
        manager.UnloadAllAsync().GetAwaiter().GetResult();
        return weakRefs;
    }

    [Fact]
    public async Task LoadInvalidEntryType_DoesNotLeakLoadContext ()
    {
        var manager = new PluginManager(_pluginsDir , _logger);
        CreatePackageWithInvalidEntryType();

        var plugins = await manager.LoadPluginsAsync(category: null , CancellationToken.None);

        plugins.Should().BeEmpty();
        GetContextCount(manager).Should().Be(0 , "入口类型无效的加载失败路径不得泄漏 AssemblyLoadContext");
    }

    [Fact]
    public async Task LoadCtorThrows_DoesNotLeakLoadContext ()
    {
        // 类型存在但构造函数抛异常：实例化失败路径同样必须清理 ALC（回归：此前异常直接向上传播）
        var manager = new PluginManager(_pluginsDir , _logger);
        CreatePackageWithBrokenStrategy();

        var plugins = await manager.LoadPluginsAsync(category: null , CancellationToken.None);

        plugins.Should().BeEmpty();
        GetContextCount(manager).Should().Be(0 , "实例化失败的加载路径不得泄漏 AssemblyLoadContext");
    }

    [Fact]
    public async Task InstallPackageAsync_WithTraversalId_ThrowsAndWritesNothing ()
    {
        // Critical：packageId 来自不可信清单，路径遍历 id（../）必须被拒绝，不得写出插件目录
        var manager = new PluginManager(_pluginsDir , _logger);
        // 恶意 id 为路径遍历（../escape），zip 文件名本身必须是安全路径
        var zipPath = CreateZipWithId("escape.ap-plugin" , "../../escape");

        await manager.Invoking(m => m.InstallFromPackageAsync(zipPath , CancellationToken.None))
            .Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*id 字段无效*");

        // 插件目录外（临时根）不得出现 escape 目录
        var escapeDir = Path.Combine(Path.GetTempPath() , "escape");
        Directory.Exists(escapeDir).Should().BeFalse("路径遍历的包 id 不得写出插件目录");
        File.Delete(zipPath);
    }

    [Fact]
    public async Task LoadPackageAsync_MissingManifestDir_LoadsNothing ()
    {
        var manager = new PluginManager(_pluginsDir , _logger);

        var pkg = await manager.LoadPackageAsync("nonexistent" , CancellationToken.None);

        pkg.Should().BeNull();
    }

    // ── 反射辅助 ──

    private static object GetContextsField (PluginManager manager)
    {
        var field = typeof(PluginManager).GetField("_contexts" ,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return field!.GetValue(manager)!;
    }

    private static List<WeakReference> CaptureContextWeakRefs (PluginManager manager)
    {
        var result = new List<WeakReference>();
        var list = (System.Collections.IList)GetContextsField(manager);
        foreach (var item in list)
        {
            // 新实现中元素为 LoadedContext（含 public Context 属性）
            var context = (System.Runtime.Loader.AssemblyLoadContext)item.GetType()
                .GetProperty("Context")!.GetValue(item)!;
            result.Add(new WeakReference(context , trackResurrection: true));
        }
        return result;
    }

    private static int GetContextCount (PluginManager manager)
        => ((System.Collections.IList)GetContextsField(manager)).Count;

    private static void AssertReclaimed (List<WeakReference> weakRefs)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            for (int round = 0; round < 10 && weakRefs.Any(r => r.IsAlive); round++)
            {
                // collectible ALC 的 LoaderAllocator 仅在压缩式强制收集（compacting）中释放
                GC.Collect(2 , GCCollectionMode.Forced , blocking: true , compacting: true);
                GC.WaitForPendingFinalizers();
            }
            if (weakRefs.All(r => !r.IsAlive))
                return;
        }
        weakRefs.Any(r => r.IsAlive).Should().BeFalse("插件 AssemblyLoadContext 应在卸载后回收");
    }

    // ── 包布置 ──

    private void CreateFixturePackage ()
    {
        var fixtureDll = Path.Combine(AppContext.BaseDirectory , "fixtures" , "SeatFlow.Plugin.TestFixture.dll");
        File.Exists(fixtureDll).Should().BeTrue("fixture DLL 应由构建目标复制到测试输出");

        var packageDir = Path.Combine(_pluginsDir , "fixture-pkg");
        var stratDir = Path.Combine(packageDir , "strat1");
        Directory.CreateDirectory(stratDir);

        File.WriteAllText(Path.Combine(packageDir , "plugins-manifest.json") ,
            """
            {"id":"fixture-pkg","name":"Fixture Package","version":"1.0.0","type":"strategy",
             "plugins":[{"kind":"strategy","path":"strat1","manifest":"strat1/manifest.json",
                            "assembly":"SeatFlow.Plugin.TestFixture.dll",
                            "entryType":"SeatFlow.Plugin.TestFixture.FixtureSortStrategy"}]}
            """);
        File.WriteAllText(Path.Combine(stratDir , "manifest.json") ,
            """
            {"id":"fixture-sort","displayName":"Fixture Sort","defaultPriority":40,
             "defaultEnabled":true,"isIndependent":true}
            """);
        File.Copy(fixtureDll , Path.Combine(stratDir , "SeatFlow.Plugin.TestFixture.dll"));
    }

    private void CreatePackageWithInvalidEntryType ()
    {
        var packageDir = Path.Combine(_pluginsDir , "bad-pkg");
        var stratDir = Path.Combine(packageDir , "strat1");
        Directory.CreateDirectory(stratDir);

        File.WriteAllText(Path.Combine(packageDir , "plugins-manifest.json") ,
            """
            {"id":"bad-pkg","name":"Bad Package","version":"1.0.0","type":"strategy",
             "plugins":[{"kind":"strategy","path":"strat1","manifest":"strat1/manifest.json",
                            "assembly":"SeatFlow.Plugin.TestFixture.dll",
                            "entryType":"SeatFlow.Plugin.TestFixture.NonexistentType"}]}
            """);
        File.WriteAllText(Path.Combine(stratDir , "manifest.json") ,
            """
            {"id":"bad-strat","displayName":"Bad","defaultPriority":40,
             "defaultEnabled":true,"isIndependent":true}
            """);
        File.Copy(Path.Combine(AppContext.BaseDirectory , "fixtures" , "SeatFlow.Plugin.TestFixture.dll") ,
            Path.Combine(stratDir , "SeatFlow.Plugin.TestFixture.dll"));
    }

    private void CreatePackageWithBrokenStrategy ()
    {
        var packageDir = Path.Combine(_pluginsDir , "broken-pkg");
        var stratDir = Path.Combine(packageDir , "strat1");
        Directory.CreateDirectory(stratDir);

        File.WriteAllText(Path.Combine(packageDir , "plugins-manifest.json") ,
            """
            {"id":"broken-pkg","name":"Broken Package","version":"1.0.0","type":"strategy",
             "plugins":[{"kind":"strategy","path":"strat1","manifest":"strat1/manifest.json",
                            "assembly":"SeatFlow.Plugin.TestFixture.dll",
                            "entryType":"SeatFlow.Plugin.TestFixture.FixtureBrokenStrategy"}]}
            """);
        File.WriteAllText(Path.Combine(stratDir , "manifest.json") ,
            """
            {"id":"fixture-broken","displayName":"Broken","defaultPriority":40,
             "defaultEnabled":true,"isIndependent":true}
            """);
        File.Copy(Path.Combine(AppContext.BaseDirectory , "fixtures" , "SeatFlow.Plugin.TestFixture.dll") ,
            Path.Combine(stratDir , "SeatFlow.Plugin.TestFixture.dll"));
    }

    private string CreateZipWithId (string fileName , string id)
    {
        var tmpDir = Path.Combine(Path.GetTempPath() , $"ap_traversal_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllText(Path.Combine(tmpDir , "plugins-manifest.json") ,
                "{\"id\":\"" + id + "\",\"name\":\"Escape\",\"version\":\"1.0.0\",\"type\":\"strategy\",\"plugins\":[]}");

            var zipPath = Path.Combine(_pluginsDir , fileName);
            var zipDir = Path.GetDirectoryName(zipPath);
            if (zipDir != null) Directory.CreateDirectory(zipDir);
            ZipFile.CreateFromDirectory(tmpDir , zipPath);
            return zipPath;
        }
        finally
        {
            try { Directory.Delete(tmpDir , recursive: true); } catch { }
        }
    }
}
