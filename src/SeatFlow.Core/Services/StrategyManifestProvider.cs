using System.Text.Json;
using SeatFlow.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Core.Services;

/// <summary>
/// 从嵌入资源加载内置策略的 Manifest，提供不可变的策略元数据。
/// 首次读取后缓存结果，避免重复的嵌入资源 I/O。
/// </summary>
public class StrategyManifestProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true ,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private const string ManifestResourcePrefix = "SeatFlow.Core.Strategies.Manifests.";

    /// <summary>当前程序支持的 Manifest 最大版本号。</summary>
    public const string MaxManifestVersion = "1.0";

    private readonly Lazy<IReadOnlyList<StrategyManifest>> _manifests;
    private readonly ILogger<StrategyManifestProvider> _logger;

    public StrategyManifestProvider (ILogger<StrategyManifestProvider>? logger = null)
    {
        _logger = logger ?? NullLogger<StrategyManifestProvider>.Instance;
        _manifests = new Lazy<IReadOnlyList<StrategyManifest>>(LoadBuiltInManifests);
    }

    // 无参构造函数供 NSubstitute 等动态代理框架使用
    public StrategyManifestProvider () : this(null) { }

    /// <summary>
    /// 从嵌入资源加载所有内置策略的 Manifest（首次调用时加载，后续返回缓存）。
    /// </summary>
    public IReadOnlyList<StrategyManifest> GetBuiltInManifests ()
    {
        return _manifests.Value;
    }

    /// <summary>
    /// 根据 ID 获取内置策略的 Manifest。
    /// </summary>
    public StrategyManifest? GetBuiltInManifest (string id)
    {
        return _manifests.Value.FirstOrDefault(m => m.Id == id);
    }

    private IReadOnlyList<StrategyManifest> LoadBuiltInManifests ()
    {
        var assembly = typeof(StrategyManifestProvider).Assembly;
        var results = new List<StrategyManifest>();
        var failed = 0;

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(ManifestResourcePrefix) || !resourceName.EndsWith(".json"))
                continue;

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                _logger.LogWarning("无法读取嵌入资源：{ResourceName}" , resourceName);
                failed++;
                continue;
            }

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            try
            {
                var manifest = JsonSerializer.Deserialize<StrategyManifest>(json , JsonOptions);
                if (manifest is not null)
                {
                    ValidateManifestVersion(manifest , resourceName);
                    results.Add(manifest);
                }
                else
                {
                    _logger.LogWarning("Manifest 反序列化结果为 null：{ResourceName}" , resourceName);
                    failed++;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex , "Manifest JSON 反序列化失败：{ResourceName}" , resourceName);
                failed++;
            }
        }

        _logger.LogInformation("加载内置策略清单：成功 {Success} 个，失败 {Failed} 个" , results.Count , failed);
        return results.OrderByDescending(m => m.DefaultPriority).ToList().AsReadOnly();
    }

    /// <summary>
    /// 校验 manifest 的版本号是否在当前程序支持的范围内。
    /// 若版本大于已知最大版本则警告（仍加载）；小于或等于则正常。
    /// </summary>
    private void ValidateManifestVersion (StrategyManifest manifest , string resourceName)
    {
        var version = manifest.ManifestVersion;
        if (string.IsNullOrEmpty(version)) return;

        if (CompareVersions(version , MaxManifestVersion) > 0)
        {
            _logger.LogWarning(
                "策略 Manifest 版本 {ManifestVersion} 高于当前程序支持的最大版本 {MaxVersion}，" +
                "策略 {StrategyId}（{ResourceName}）可能包含不受支持的字段，将以兼容模式加载" ,
                version , MaxManifestVersion , manifest.Id , resourceName);
        }
    }

    /// <summary>
    /// 比较两个语义化版本号字符串（如 "1.0" vs "2.0"）。
    /// 返回值：&lt;0 = a 小于 b，0 = 相等，&gt;0 = a 大于 b。
    /// </summary>
    public static int CompareVersions (string? a , string? b)
    {
        if (a is null && b is null) return 0;
        if (a is null) return -1;
        if (b is null) return 1;

        var aParts = a.Split('.');
        var bParts = b.Split('.');
        int maxLen = Math.Max(aParts.Length , bParts.Length);

        for (int i = 0; i < maxLen; i++)
        {
            int aNum = i < aParts.Length && int.TryParse(aParts[i] , out var av) ? av : 0;
            int bNum = i < bParts.Length && int.TryParse(bParts[i] , out var bv) ? bv : 0;
            if (aNum != bNum) return aNum.CompareTo(bNum);
        }
        return 0;
    }
}
