using System.IO.Compression;
using System.Text.Json;

namespace SeatFlow.Plugins.Sdk.Models;

/// <summary>
/// 插件打包工具，支持将插件包目录打包为 <c>.ap-plugin</c> 文件以及反向解包。
/// </summary>
/// <remarks>
/// <para><c>.ap-plugin</c> 格式规范：</para>
/// <list type="bullet">
///   <item>本质为 ZIP 文件，仅后缀名改为 <c>.ap-plugin</c></item>
///   <item>必须包含 <c>plugins-manifest.json</c>（包级清单）</item>
///   <item>每个策略子组件位于独立子目录，含 <c>manifest.json</c>（策略元数据）</item>
///   <item>可选文件：<c>data/enables.json</c>、<c>icon.png</c></item>
/// </list>
/// </remarks>
public static class PluginPackage
{
    /// <summary>
    /// .ap-plugin 文件的扩展名。
    /// </summary>
    public const string Extension = ".ap-plugin";

    /// <summary>
    /// 所有受支持的插件包扩展名列表。
    /// </summary>
    public static readonly string[] SupportedExtensions = [Extension];

    /// <summary>
    /// 最大压缩比率（解压后大小 / 压缩大小），超过此值视为 ZIP 炸弹。
    /// </summary>
    public const int MaxCompressionRatio = 100;

    /// <summary>
    /// 解压后总大小上限（字节），默认 500 MB。
    /// </summary>
    public const long MaxUncompressedSize = 500 * 1024 * 1024;

    /// <summary>
    /// ZIP 条目数量上限。
    /// </summary>
    public const int MaxEntryCount = 10000;

    /// <summary>
    /// 验证 ZIP 文件是否安全（检查压缩炸弹、总大小、条目数）。
    /// 实现收敛于 <see cref="SeatFlow.Contracts.Utilities.PluginArchiveSafety"/>（宿主与 SDK 共享）。
    /// </summary>
    /// <param name="archivePath">ZIP 文件路径。</param>
    /// <returns>验证失败时返回错误描述；通过时返回 <c>null</c>。</returns>
    public static string? ValidateZipSafety (string archivePath)
    {
        try
        {
            return SeatFlow.Contracts.Utilities.PluginArchiveSafety.Validate(archivePath);
        }
        catch (InvalidDataException)
        {
            return "ZIP 文件格式无效";
        }
    }

    /// <summary>
    /// 将插件包目录打包为 <c>.ap-plugin</c> 文件。
    /// </summary>
    /// <param name="packageDir">插件包目录路径（须包含 <c>plugins-manifest.json</c>）。</param>
    /// <param name="outputPath">输出的 <c>.ap-plugin</c> 文件路径。</param>
    /// <exception cref="DirectoryNotFoundException">包目录不存在。</exception>
    /// <exception cref="FileNotFoundException">包清单文件不存在。</exception>
    public static void Create (string packageDir , string outputPath)
    {
        if (!Directory.Exists(packageDir))
            throw new DirectoryNotFoundException($"插件包目录不存在：{packageDir}");

        var manifestPath = Path.Combine(packageDir , "plugins-manifest.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"插件包清单文件不存在：{manifestPath}");

        if (File.Exists(outputPath))
            File.Delete(outputPath);

        ZipFile.CreateFromDirectory(packageDir , outputPath , CompressionLevel.Optimal , includeBaseDirectory: false);
    }

    /// <summary>
    /// 将插件包文件解包到目标目录，自动处理嵌套单层目录。
    /// 解压前自动进行 ZIP 炸弹安全验证。
    /// </summary>
    /// <param name="packagePath">包文件路径（<c>.ap-plugin</c>）。</param>
    /// <param name="targetDir">解包目标目录（不存在则自动创建）。</param>
    /// <param name="stripSingleFolder">
    /// <c>true</c> — 启用防嵌套：若包内恰好只有 1 个目录 + 0 个文件，则剥离外层目录（默认）；
    /// <c>false</c> — 直接解包，不做剥离。
    /// </param>
    /// <exception cref="FileNotFoundException">包文件不存在。</exception>
    /// <exception cref="InvalidDataException">包内缺少清单文件或 ZIP 安全验证失败。</exception>
    public static void Extract (string packagePath , string targetDir , bool stripSingleFolder = true)
    {
        if (!File.Exists(packagePath))
            throw new FileNotFoundException($"插件包文件不存在：{packagePath}");

        var safetyError = ValidateZipSafety(packagePath);
        if (safetyError != null)
            throw new InvalidDataException(safetyError);

        Directory.CreateDirectory(targetDir);

        if (stripSingleFolder)
        {
            var tempDir = Path.Combine(Path.GetTempPath() , $"ap_plugin_extract_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                ZipFile.ExtractToDirectory(packagePath , tempDir , overwriteFiles: true);

                var entries = Directory.GetFileSystemEntries(tempDir);
                if (entries.Length == 1 && Directory.Exists(entries[0]))
                {
                    var innerDir = entries[0];
                    foreach (var item in Directory.GetFileSystemEntries(innerDir))
                    {
                        var dest = Path.Combine(tempDir , Path.GetFileName(item));
                        if (Directory.Exists(item))
                            CopyDirectoryRecursive(item , dest);
                        else
                            File.Copy(item , dest , overwrite: true);
                    }
                    Directory.Delete(innerDir , recursive: true);
                }

                foreach (var item in Directory.GetFileSystemEntries(tempDir))
                {
                    var dest = Path.Combine(targetDir , Path.GetFileName(item));
                    if (Directory.Exists(item))
                        CopyDirectoryRecursive(item , dest);
                    else
                        File.Copy(item , dest , overwrite: true);
                }
            }
            finally
            {
                try { Directory.Delete(tempDir , recursive: true); }
                catch { /* 忽略清理失败 */ }
            }
        }
        else
        {
            ZipFile.ExtractToDirectory(packagePath , targetDir , overwriteFiles: true);
        }

        // 验证包清单存在
        if (!File.Exists(Path.Combine(targetDir , "plugins-manifest.json")))
            throw new InvalidDataException("插件包内缺少 plugins-manifest.json 文件");
    }

    /// <summary>
    /// 验证插件包文件的结构完整性。
    /// </summary>
    /// <param name="packagePath">包文件路径。</param>
    /// <returns>验证结果：成功返回空字符串，失败返回错误描述。</returns>
    public static async Task<string> ValidateAsync (string packagePath)
    {
        if (!File.Exists(packagePath))
            return "插件包文件不存在";

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);

            var hasManifest = archive.Entries.Any(e =>
                string.Equals(e.Name , "plugins-manifest.json" , StringComparison.OrdinalIgnoreCase));

            if (!hasManifest)
                return "插件包内缺少 plugins-manifest.json 文件";

            var manifestEntry = archive.Entries.First(e =>
                string.Equals(e.Name , "plugins-manifest.json" , StringComparison.OrdinalIgnoreCase));
            await using var stream = manifestEntry.Open();

            try
            {
                using var doc = await JsonDocument.ParseAsync(stream);
                var root = doc.RootElement;
                if (!root.TryGetProperty("id" , out var idElem) || idElem.GetString() is not string id || string.IsNullOrEmpty(id))
                    return "plugins-manifest.json 格式无效：缺少 id 字段";

                if (!root.TryGetProperty("plugins" , out var pluginsElem) ||
                    pluginsElem.ValueKind != JsonValueKind.Array ||
                    pluginsElem.GetArrayLength() == 0)
                    return "plugins-manifest.json 格式无效：plugins 数组为空或缺失（v2 格式）";
            }
            catch (JsonException ex)
            {
                return $"plugins-manifest.json JSON 格式无效：{ex.Message}";
            }

            var hasPluginFile = archive.Entries.Any(e =>
            {
                var ext = Path.GetExtension(e.Name).ToLowerInvariant();
                return ext is ".dll" or ".lua" or ".csx";
            });

            if (!hasPluginFile)
                return "插件包内未找到任何插件文件（.dll、.lua 或 .csx）";

            return string.Empty;
        }
        catch (InvalidDataException)
        {
            return "插件包文件格式无效（非有效的 ZIP 文件）";
        }
        catch (IOException ex)
        {
            return $"读取插件包时发生 I/O 错误：{ex.Message}";
        }
    }

    /// <summary>
    /// 不解包直接读取插件包清单，用于预览包内容。
    /// </summary>
    /// <param name="packagePath">包文件路径。</param>
    /// <returns>插件清单信息；如果读取失败则返回 <c>null</c>。</returns>
    public static async Task<PluginManifestStub?> GetManifestAsync (string packagePath)
    {
        if (!File.Exists(packagePath))
            return null;

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);

            var manifestEntry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.Name , "plugins-manifest.json" , StringComparison.OrdinalIgnoreCase));

            if (manifestEntry is null)
                return null;

            await using var stream = manifestEntry.Open();
            using var doc = await JsonDocument.ParseAsync(stream);
            var root = doc.RootElement;

            var stub = new PluginManifestStub();
            if (root.TryGetProperty("id" , out var idElem))
                stub.Id = idElem.GetString() ?? string.Empty;
            if (root.TryGetProperty("name" , out var nameElem))
                stub.Name = nameElem.GetString() ?? string.Empty;
            if (root.TryGetProperty("version" , out var verElem))
                stub.Version = verElem.GetString() ?? "1.0.0";
            if (root.TryGetProperty("type" , out var typeElem))
                stub.Category = typeElem.GetString() ?? "strategy";
            if (root.TryGetProperty("description" , out var descElem))
                stub.Description = descElem.GetString() ?? string.Empty;
            if (root.TryGetProperty("author" , out var authorElem))
                stub.Author = authorElem.GetString() ?? string.Empty;

            return stub;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 递归复制目录（Copy+Delete 模式，兼容跨文件系统场景）。
    /// </summary>
    private static void CopyDirectoryRecursive (string sourceDir , string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir , Path.GetFileName(file));
            File.Copy(file , destFile , overwrite: true);
        }
        foreach (var subDir in Directory.EnumerateDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir , Path.GetFileName(subDir));
            CopyDirectoryRecursive(subDir , destSubDir);
        }
    }
}

/// <summary>
/// 插件清单的轻量级存根，用于解包时快速读取关键字段。
/// </summary>
public class PluginManifestStub
{
    /// <summary>包唯一标识符。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>包显示名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>包版本号。</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>功能类别。</summary>
    public string Category { get; set; } = "strategy";

    /// <summary>描述。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>作者。</summary>
    public string Author { get; set; } = string.Empty;
}
