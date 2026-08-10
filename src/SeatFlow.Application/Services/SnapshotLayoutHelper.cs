using System.Text.Json;
using SeatFlow.Core.Models;
using SeatFlow.Infrastructure.Serialization;

namespace SeatFlow.Application.Services;

/// <summary>
/// 快照会场布局反序列化辅助类，供 <see cref="ApplicationFacade"/> 和 <see cref="FrontRowHistoryLoader"/> 共用。
/// </summary>
internal static class SnapshotLayoutHelper
{
    private static readonly JsonSerializerOptions VenueFileReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    static SnapshotLayoutHelper ()
    {
        VenueFileReadOptions.Converters.Add(new SeatJsonConverter());
    }

    /// <summary>
    /// 从快照元数据中嵌入的会场 JSON 反序列化会场布局定义。
    /// 同时兼容 VenueFile 包装格式和 ClassroomLayoutDefinition 直接序列化的旧格式。
    /// </summary>
    internal static ClassroomLayoutDefinition? DeserializeVenueFromEmbeddedJson (string json)
    {
        // venueFile 格式（VenueFile 包装）
        var venueFile = JsonSerializer.Deserialize<VenueFile>(json , VenueFileReadOptions);
        if (venueFile?.Layout != null)
            return venueFile.Layout;
        // venueLayout 旧格式（ClassroomLayoutDefinition 直接序列化，兼容旧快照）
        return JsonSerializer.Deserialize<ClassroomLayoutDefinition>(json , VenueFileReadOptions);
    }

    /// <summary>
    /// 从快照元数据字典中安全提取字符串值。
    /// 兼容 <see cref="string"/> 和 <see cref="System.Text.Json.JsonElement"/> 两种值类型。
    /// </summary>
    internal static string? GetMetaStringFromMetadata (Dictionary<string , object> meta , string key)
    {
        if (!meta.TryGetValue(key , out var value) || value is null) return null;
        return value switch
        {
            string s => s,
            System.Text.Json.JsonElement je => je.ValueKind == System.Text.Json.JsonValueKind.String
                ? je.GetString()
                : je.GetRawText(),
            _ => null
        };
    }
}
