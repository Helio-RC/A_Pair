using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Infrastructure.Migration;

/// <summary>
/// 文件版本迁移服务。根据文件的当前版本和目标版本，查找并执行迁移链路。
/// </summary>
public class FileMigrationService (
    IEnumerable<IFileMigrator> migrators ,
    ILogger<FileMigrationService>? logger = null)
{
    private readonly IReadOnlyDictionary<string , List<IFileMigrator>> _migrators = migrators
            .GroupBy(m => m.FileType , StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key , g => g.ToList() , StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<FileMigrationService> _logger = logger ?? NullLogger<FileMigrationService>.Instance;

    /// <summary>
    /// 将 JSON 节点从 <paramref name="currentVersion"/> 迁移到 <paramref name="targetVersion"/>。
    /// 仅支持向前迁移（current ≤ target），不支持回退。
    /// </summary>
    public JsonNode Migrate (string fileType , JsonNode root , string currentVersion , string targetVersion)
    {
        if (string.IsNullOrEmpty(currentVersion))
            currentVersion = "1.0";

        if (VersionLessOrEqual(targetVersion , currentVersion))
            return root;

        if (!_migrators.TryGetValue(fileType , out var list) || list.Count == 0)
        {
            _logger.LogDebug("文件类型 {FileType} 无注册迁移器，保持版本 {Version}" , fileType , currentVersion);
            return root;
        }

        var current = root;
        var fromVer = currentVersion;

        while (VersionLess(fromVer , targetVersion))
        {
            var step = list.FirstOrDefault(m => VersionEqual(m.FromVersion , fromVer));
            if (step == null)
            {
                _logger.LogWarning("文件类型 {FileType} 缺少 {From} → ? 的迁移器，停止于版本 {Current}" ,
                    fileType , fromVer , fromVer);
                break;
            }

            _logger.LogInformation("执行迁移: {FileType} {From} → {To}" ,
                fileType , step.FromVersion , step.ToVersion);
            current = step.Migrate(current);
            fromVer = step.ToVersion;
        }

        return current;
    }

    private static bool VersionLess (string a , string b)
        => CompareVersions(a , b) < 0;

    private static bool VersionLessOrEqual (string a , string b)
        => CompareVersions(a , b) <= 0;

    private static bool VersionEqual (string a , string b)
        => CompareVersions(a , b) == 0;

    private static int CompareVersions (string? a , string? b)
    {
        if (a == b) return 0;
        if (a is null) return -1;
        if (b is null) return 1;

        var pa = a.Split('.');
        var pb = b.Split('.');
        int maxLen = Math.Max(pa.Length , pb.Length);
        for (int i = 0; i < maxLen; i++)
        {
            int va = i < pa.Length && int.TryParse(pa[i] , out var na) ? na : 0;
            int vb = i < pb.Length && int.TryParse(pb[i] , out var nb) ? nb : 0;
            if (va != vb) return va.CompareTo(vb);
        }
        return 0;
    }
}
