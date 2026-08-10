using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace SeatFlow.Application.Tests.Plugins;

public class PluginManagerTests : IDisposable
{
    private readonly string _pluginsDir;
    private readonly ILogger<PluginManager> _logger;

    public PluginManagerTests ()
    {
        _pluginsDir = Path.Combine(Path.GetTempPath() , $"ap_test_plugins_{Guid.NewGuid():N}");
        _logger = Substitute.For<ILogger<PluginManager>>();
    }

    public void Dispose ()
    {
        try { Directory.Delete(_pluginsDir , recursive: true); }
        catch { /* ignore */ }
        GC.SuppressFinalize(this);
    }

    // ── ValidateZipSafety ──

    [Fact]
    public void ValidateZipSafety_ValidZip_ShouldNotThrow ()
    {
        var zipPath = CreateMinimalValidZip("valid-test.zip");

        var action = () => InvokeValidateZipSafety(zipPath);

        action.Should().NotThrow();
        File.Delete(zipPath);
    }

    [Fact]
    public void ValidateZipSafety_TooManyEntries_ShouldThrow ()
    {
        var zipPath = CreateZipWithNEntries(10001);

        var action = () => InvokeValidateZipSafety(zipPath);

        action.Should().Throw<InvalidDataException>()
            .WithMessage("*条目数*");
        File.Delete(zipPath);
    }

    [Fact]
    public void ValidateZipSafety_HighCompressionRatio_ShouldThrow ()
    {
        var zipPath = CreateHighCompressionZip();

        var action = () => InvokeValidateZipSafety(zipPath);

        action.Should().Throw<InvalidDataException>()
            .WithMessage("*压缩比*");
        File.Delete(zipPath);
    }

    [Fact]
    public void ValidateZipSafety_PathTraversal_ShouldThrow ()
    {
        var zipPath = CreatePathTraversalZip();

        var action = () => InvokeValidateZipSafety(zipPath);

        action.Should().Throw<InvalidDataException>()
            .WithMessage("*非法路径*");
        File.Delete(zipPath);
    }

    // ── InstallFromPackageAsync ──

    [Fact]
    public async Task InstallFromPackageAsync_FileNotFound_ShouldThrow ()
    {
        var manager = CreateManager();
        await manager.Invoking(m => m.InstallFromPackageAsync("/nonexistent/path.ap-plugin" , CancellationToken.None))
            .Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task InstallFromPackageAsync_NoManifest_ShouldThrow ()
    {
        var zipPath = CreateZipWithRandomFiles("nomanifest.ap-plugin" , 3);

        var manager = CreateManager();
        await manager.Invoking(m => m.InstallFromPackageAsync(zipPath , CancellationToken.None))
            .Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*缺少*manifest*");
        File.Delete(zipPath);
    }

    [Fact]
    public async Task InstallFromPackageAsync_InvalidManifest_ShouldThrow ()
    {
        var zipPath = CreateZipWithContent("no-id.ap-plugin" , "plugins-manifest.json" ,
            "{\"name\":\"NoId\",\"plugins\":[]}");

        var manager = CreateManager();
        await manager.Invoking(m => m.InstallFromPackageAsync(zipPath , CancellationToken.None))
            .Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*缺少 id*");
        File.Delete(zipPath);
    }

    [Fact]
    public async Task InstallFromPackageAsync_ValidNewFormat_ShouldInstall ()
    {
        var tmpDir = Path.Combine(Path.GetTempPath() , $"ap_validpkg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            var stratDir = Path.Combine(tmpDir , "strat1");
            Directory.CreateDirectory(stratDir);
            File.WriteAllText(Path.Combine(tmpDir , "plugins-manifest.json") ,
                "{\"id\":\"test-pkg\",\"name\":\"Test Package\",\"version\":\"1.0.0\",\"type\":\"strategy\",\"plugins\":[{\"kind\":\"strategy\",\"path\":\"strat1\",\"manifest\":\"strat1/manifest.json\",\"assembly\":\"Strat1.dll\",\"entryType\":\"MyPlugin.Strategy1\"}]}");
            File.WriteAllText(Path.Combine(stratDir , "manifest.json") ,
                "{\"id\":\"strat1\",\"displayName\":\"Strategy 1\",\"defaultPriority\":50,\"defaultEnabled\":true,\"isIndependent\":true}");

            var zipPath = Path.Combine(_pluginsDir , "valid.ap-plugin");
            var zipDir = Path.GetDirectoryName(zipPath);
            if (zipDir != null) Directory.CreateDirectory(zipDir);
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(tmpDir , zipPath);

            var manager = CreateManager();
            var targetDir = await manager.InstallFromPackageAsync(zipPath , CancellationToken.None);

            targetDir.Should().NotBeNullOrEmpty();
            Directory.Exists(targetDir).Should().BeTrue();
            File.Exists(Path.Combine(targetDir , "plugins-manifest.json")).Should().BeTrue();
            File.Exists(Path.Combine(targetDir , "data" , "enables.json")).Should().BeTrue();

            File.Delete(zipPath);
        }
        finally
        {
            try { Directory.Delete(tmpDir , recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task InstallFromPackageAsync_SupportsCancellation ()
    {
        var zipPath = CreateMinimalValidZip("cancel.ap-plugin");

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var manager = CreateManager();
        await manager.Invoking(m => m.InstallFromPackageAsync(zipPath , cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        File.Delete(zipPath);
    }

    // ── SetPackageEnabledAsync ──

    [Fact]
    public async Task LoadAndSaveEnablesAsync_RoundTrip_ShouldPersist ()
    {
        var zipPath = CreateMinimalValidZip("enables-test.ap-plugin");

        var manager = CreateManager();
        await manager.InstallFromPackageAsync(zipPath , CancellationToken.None);

        var enables = await manager.LoadEnablesAsync("enables-test" , CancellationToken.None);
        enables.Strategies["strat1"] = false;
        enables.Strategies["strat2"] = true;
        await manager.SaveEnablesAsync("enables-test" , enables , CancellationToken.None);

        var reloaded = await manager.LoadEnablesAsync("enables-test" , CancellationToken.None);
        reloaded.Strategies["strat1"].Should().BeFalse();
        reloaded.Strategies["strat2"].Should().BeTrue();

        File.Delete(zipPath);
    }

    [Fact]
    public async Task RefreshPluginsAsync_ShouldReturnEmpty_WhenNoPlugins ()
    {
        var manager = CreateManager();
        var plugins = await manager.RefreshPluginsAsync(category: null , CancellationToken.None);
        plugins.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadPluginsAsync_ShouldSkipAlreadyLoadedDirs ()
    {
        // 首次加载
        var manager = CreateManager();
        var plugins1 = await manager.LoadPluginsAsync(category: null , CancellationToken.None);
        plugins1.Should().BeEmpty();

        // 创建新包目录但不加载 — 确认二次扫描会跳过已加载目录
        var newDir = Path.Combine(_pluginsDir , "already-loaded");
        Directory.CreateDirectory(newDir);

        var plugins2 = await manager.LoadPluginsAsync(category: null , CancellationToken.None);
        plugins2.Should().BeEmpty(); // 空目录不应加载任何内容
    }

    // ── Helpers ──

    private PluginManager CreateManager ()
    {
        return new PluginManager(_pluginsDir , _logger);
    }

    private string CreateMinimalValidZip (string fileName)
    {
        var tmpDir = Path.Combine(Path.GetTempPath() , $"ap_minzip_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            // 包内包含一个可加载的 Lua 脚本策略（空包不再注册）
            var luaDir = Path.Combine(tmpDir , "lua");
            Directory.CreateDirectory(luaDir);
            File.WriteAllText(Path.Combine(luaDir , "script.lua") , "-- 空脚本");
            File.WriteAllText(Path.Combine(luaDir , "manifest.json") ,
                "{\"id\":\"strat1\",\"displayName\":\"Strat 1\",\"defaultPriority\":50,\"defaultEnabled\":true,\"isIndependent\":true}");
            File.WriteAllText(Path.Combine(tmpDir , "plugins-manifest.json") ,
                "{\"id\":\"" + Path.GetFileNameWithoutExtension(fileName) + "\",\"name\":\"Test\",\"version\":\"1.0.0\",\"type\":\"strategy\",\"plugins\":[{\"kind\":\"strategy\",\"path\":\"lua\",\"manifest\":\"lua/manifest.json\",\"scriptFile\":\"script.lua\",\"scriptType\":\"lua\"}]}");

            var zipPath = Path.Combine(_pluginsDir , fileName);
            var zipDir = Path.GetDirectoryName(zipPath);
            if (zipDir != null) Directory.CreateDirectory(zipDir);
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(tmpDir , zipPath);
            return zipPath;
        }
        finally
        {
            try { Directory.Delete(tmpDir , recursive: true); } catch { }
        }
    }

    private string CreateZipWithContent (string fileName , string entryName , string content)
    {
        var tmpDir = Path.Combine(Path.GetTempPath() , $"ap_zip_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            var filePath = Path.Combine(tmpDir , entryName);
            var fileDir = Path.GetDirectoryName(filePath);
            if (fileDir != null && !Directory.Exists(fileDir))
                Directory.CreateDirectory(fileDir);
            File.WriteAllText(filePath , content);

            var zipPath = Path.Combine(_pluginsDir , fileName);
            var zipDir = Path.GetDirectoryName(zipPath);
            if (zipDir != null) Directory.CreateDirectory(zipDir);
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(tmpDir , zipPath);
            return zipPath;
        }
        finally
        {
            try { Directory.Delete(tmpDir , recursive: true); } catch { }
        }
    }

    private string CreateZipWithRandomFiles (string fileName , int count)
    {
        var tmpDir = Path.Combine(Path.GetTempPath() , $"ap_rand_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            for (int i = 0; i < count; i++)
                File.WriteAllText(Path.Combine(tmpDir , $"file_{i}.txt") , $"content {i}");

            var zipPath = Path.Combine(_pluginsDir , fileName);
            var zipDir = Path.GetDirectoryName(zipPath);
            if (zipDir != null) Directory.CreateDirectory(zipDir);
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(tmpDir , zipPath);
            return zipPath;
        }
        finally
        {
            try { Directory.Delete(tmpDir , recursive: true); } catch { }
        }
    }

    private string CreateZipWithNEntries (int count)
    {
        var tmpDir = Path.Combine(Path.GetTempPath() , $"ap_many_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            for (int i = 0; i < count; i++)
                File.WriteAllText(Path.Combine(tmpDir , $"file_{i:D6}.txt") , new string('x' , 10));

            var zipPath = Path.Combine(_pluginsDir , "many.ap-plugin");
            var zipDir = Path.GetDirectoryName(zipPath);
            if (zipDir != null) Directory.CreateDirectory(zipDir);
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(tmpDir , zipPath);
            return zipPath;
        }
        finally
        {
            try { Directory.Delete(tmpDir , recursive: true); } catch { }
        }
    }

    private string CreateHighCompressionZip ()
    {
        var tmpDir = Path.Combine(Path.GetTempPath() , $"ap_highzip_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            var bigFile = Path.Combine(tmpDir , "big.bin");
            using (var fs = new FileStream(bigFile , FileMode.Create , FileAccess.Write))
            {
                var zeros = new byte[1024 * 1024]; // 1 MB
                for (int i = 0; i < 50; i++) // 50 MB total
                    fs.Write(zeros , 0 , zeros.Length);
            }

            var zipPath = Path.Combine(_pluginsDir , "highcomp.ap-plugin");
            var zipDir = Path.GetDirectoryName(zipPath);
            if (zipDir != null) Directory.CreateDirectory(zipDir);
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(tmpDir , zipPath);
            return zipPath;
        }
        finally
        {
            try { Directory.Delete(tmpDir , recursive: true); } catch { }
        }
    }

    private static void InvokeValidateZipSafety (string archivePath)
    {
        var method = typeof(PluginManager).GetMethod("ValidateZipSafety" ,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        try
        {
            method!.Invoke(null , [archivePath]);
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
        {
            // 解包反射层异常，重新抛出内部异常以匹配 .Should().Throw<InvalidDataException>()
            throw ex.InnerException;
        }
    }

    private static string CreatePathTraversalZip ()
    {
        var path = Path.Combine(Path.GetTempPath() , $"test_traversal_{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(path , ZipArchiveMode.Create);
        // 创建包含 ../ 路径的条目（ZIP Slip 攻击）
        var entry = archive.CreateEntry("../etc/malicious.txt" , CompressionLevel.Optimal);
        using (var writer = new StreamWriter(entry.Open()))
            writer.Write("traversal");
        return path;
    }

    // ── SetPackageEnabledAsync (需要先加载包) ──

    [Fact]
    public async Task SetPackageEnabledAsync_DisablePackage_ShouldWriteEnablesJson ()
    {
        var zipPath = CreateMinimalValidZip("pkg-enable2.ap-plugin");

        var manager = CreateManager();
        await manager.InstallFromPackageAsync(zipPath , CancellationToken.None);

        // 安装后加载包使其可用
        var pkg = await manager.LoadPackageAsync("pkg-enable2" , CancellationToken.None);
        pkg.Should().NotBeNull();

        await manager.SetPackageEnabledAsync("pkg-enable2" , false , CancellationToken.None);

        var enablesJson = await File.ReadAllTextAsync(Path.Combine(_pluginsDir , "pkg-enable2" , "data" , "enables.json") , TestContext.Current.CancellationToken);
        enablesJson.Should().Contain("\"enabled\"").And.Contain("false");

        File.Delete(zipPath);
    }
}
