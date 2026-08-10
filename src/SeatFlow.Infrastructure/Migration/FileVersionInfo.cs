using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;

namespace SeatFlow.Infrastructure.Migration;

/// <summary>
/// 当前程序支持的各文件类型版本号，从嵌入资源 file_versions.json 加载。
/// </summary>
public static class FileVersionInfo
{
    private static readonly FrozenDictionary<string , string> _versions;

    static FileVersionInfo ()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "SeatFlow.Infrastructure.Migration.file_versions.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"嵌入资源未找到: {resourceName}");
        var dict = JsonSerializer.Deserialize<Dictionary<string , string>>(stream)
            ?? [];
        _versions = dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>获取指定文件类型的当前版本号。</summary>
    public static string GetCurrentVersion (string fileType)
        => _versions.TryGetValue(fileType , out var v) ? v : "1.0";

    /// <summary>所有文件类型及其当前版本号。</summary>
    public static IReadOnlyDictionary<string , string> All => _versions;
}
