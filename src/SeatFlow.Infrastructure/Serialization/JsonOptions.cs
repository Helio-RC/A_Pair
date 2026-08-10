using System.Text.Json;

namespace SeatFlow.Infrastructure.Serialization;

/// <summary>
/// 共享的 <see cref="JsonSerializerOptions"/> 实例池。
/// 避免每次序列化/反序列化时重复分配，减少 GC 压力。
/// </summary>
public static class JsonOptions
{
    /// <summary>缩进格式化输出。</summary>
    public static readonly JsonSerializerOptions WriteIndented = new()
    {
        WriteIndented = true
    };

    /// <summary>缩进格式化 + camelCase 命名策略。</summary>
    public static readonly JsonSerializerOptions WriteIndentedCamelCase = new()
    {
        WriteIndented = true ,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>大小写不敏感的读取。</summary>
    public static readonly JsonSerializerOptions CaseInsensitiveRead = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>大小写不敏感读取 + camelCase 写入。</summary>
    public static readonly JsonSerializerOptions CamelCaseReadWrite = new()
    {
        PropertyNameCaseInsensitive = true ,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
