using System.Text.Json.Serialization;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// /updates/metadata 端点响应。
/// 字段名与后端 SeatFlow.Web.Api 的 UpdateGatewayApi 一致。
/// </summary>
internal sealed class MetadataResponse
{
    [JsonPropertyName("isFallback")]
    public bool IsFallback { get; set; }

    [JsonPropertyName("recommendedSource")]
    public string? RecommendedSource { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("isChinaRegion")]
    public bool IsChinaRegion { get; set; }
}

[JsonSerializable(typeof(MetadataResponse))]
internal partial class MetadataResponseJsonContext : JsonSerializerContext
{
}
