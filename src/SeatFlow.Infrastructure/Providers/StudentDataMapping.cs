using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SeatFlow.Core.Enums;
using SeatFlow.Core.Models;

namespace SeatFlow.Infrastructure.Providers;

/// <summary>
/// 学生数据导入的列名映射和值转换工具。
/// 字段映射由嵌入式资源 <c>Data/field_mappings.json</c> 驱动，支持运行时扩展。
/// </summary>
internal static class StudentDataMapping
{
    /// <summary>第 2 行为注释行，数据从第 3 行开始（标准模板）。</summary>
    public const int DataStartRow = 3;

    private static readonly Lazy<Dictionary<string , string>> _columnMapLazy = new(() =>
    {
        var map = new Dictionary<string , string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var config = LoadFieldMappingConfig();
            if (config?.FieldMappings != null)
            {
                foreach (var (propertyName , fieldDef) in config.FieldMappings)
                {
                    foreach (var label in fieldDef.Labels)
                    {
                        if (!string.IsNullOrWhiteSpace(label))
                            map[label.Trim()] = propertyName;
                    }
                }
            }
        }
        catch (Exception)
        {
            // 加载失败时走回退——下方 if 处理
        }

        if (map.Count == 0)
            PopulateFallbackMappings(map);

        return map;
    });

    private static Dictionary<string , string> ColumnMap => _columnMapLazy.Value;

    /// <summary>所有已知字段的标签列表（属性名 → 标签集），供外部查询。</summary>
    internal static IReadOnlyDictionary<string , IReadOnlyList<string>> KnownFieldLabels => _knownFieldLabelsLazy.Value;

    private static readonly Lazy<IReadOnlyDictionary<string , IReadOnlyList<string>>> _knownFieldLabelsLazy = new(() =>
    {
        try
        {
            var config = LoadFieldMappingConfig();
            if (config?.FieldMappings == null)
                return new Dictionary<string , IReadOnlyList<string>>();

            return config.FieldMappings.ToDictionary(
                kv => kv.Key ,
                kv => (IReadOnlyList<string>)kv.Value.Labels);
        }
        catch (Exception)
        {
            return new Dictionary<string , IReadOnlyList<string>>();
        }
    });

    // ═══════════════════════════════════════════════
    //  JSON 模型
    // ═══════════════════════════════════════════════

    private sealed class FieldMappingConfig
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0";

        [JsonPropertyName("fieldMappings")]
        public Dictionary<string , FieldDefinition> FieldMappings { get; set; } = [];
    }

    private sealed class FieldDefinition
    {
        [JsonPropertyName("labels")]
        public List<string> Labels { get; set; } = [];

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;
    }

    // ═══════════════════════════════════════════════
    //  加载与回退
    // ═══════════════════════════════════════════════

    /// <summary>已知的最大映射配置版本。若 JSON 中的版本号更高则发出调试警告。</summary>
    private const string MaxKnownFieldMappingVersion = "1.0";

    private static FieldMappingConfig? LoadFieldMappingConfig ()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "SeatFlow.Infrastructure.Data.field_mappings.json";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return null;

        var config = JsonSerializer.Deserialize<FieldMappingConfig>(stream ,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (config != null &&
            !string.IsNullOrEmpty(config.Version) &&
            Version.TryParse(config.Version , out var fileVer) &&
            Version.TryParse(MaxKnownFieldMappingVersion , out var maxVer) &&
            fileVer > maxVer)
        {
            Debug.WriteLine(
                $"[SeatFlow] field_mappings.json 版本 {config.Version} 超过已知最大版本 " +
                $"{MaxKnownFieldMappingVersion}，可能存在不兼容的字段定义。");
        }

        return config;
    }

    private static void PopulateFallbackMappings (
        Dictionary<string , string> map)
    {
        // 英文
        map["Name"] = "Name";
        map["Height"] = "Height";
        map["Gender"] = "Gender";
        map["NeedsFrontRow"] = "NeedsFrontRow";
        // 中文
        map["姓名"] = "Name";
        map["身高"] = "Height";
        map["性别"] = "Gender";
        map["需要前排"] = "NeedsFrontRow";
        map["前排"] = "NeedsFrontRow";
    }

    // ═══════════════════════════════════════════════
    //  公共 API
    // ═══════════════════════════════════════════════

    /// <summary>解析列名，返回对应的 Student 属性名。</summary>
    public static string? ResolveProperty (string columnName)
    {
        var trimmed = columnName.Trim();
        return ColumnMap.TryGetValue(trimmed , out var prop) ? prop : null;
    }

    /// <summary>
    /// 将单元格值设置到 Student 对象的对应属性上。
    /// </summary>
    public static void SetProperty (Student student , string propertyName , string? rawValue)
    {
        switch (propertyName)
        {
            case "Name":
                student.Name = rawValue?.Trim() ?? string.Empty;
                break;
            case "Height":
                if (float.TryParse(rawValue , out var h))
                    student.Height = h;
                break;
            case "Gender":
                student.Gender = ParseGender(rawValue?.Trim());
                break;
            case "NeedsFrontRow":
                student.NeedsFrontRow = ParseBool(rawValue?.Trim());
                break;
        }
    }

    private static Gender? ParseGender (string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value switch
        {
            "Male" or "male" or "男" or "男性" => Gender.Male,
            "Female" or "female" or "女" or "女性" => Gender.Female,
            "Other" or "other" or "其他" => Gender.Other,
            _ => null
        };
    }

    private static bool ParseBool (string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value switch
        {
            "true" or "True" or "TRUE" or "是" or "Y" or "y" or "1" => true,
            _ => false
        };
    }
}
