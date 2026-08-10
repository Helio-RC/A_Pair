using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SeatFlow.Presentation.Avalonia.Telemetry;

/// <summary>
/// 单条遥测事件记录。序列化为 JSON 后通过 HTTP POST 上报至 Web API。
/// </summary>
public sealed class TelemetryEvent
{
    /// <summary>事件类型，如 "app.start"、"app.page_view"。</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    /// <summary>事件发生时间戳（UTC）。</summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>事件附加数据。</summary>
    [JsonPropertyName("payload")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object?>? Payload { get; init; }
}

/// <summary>
/// 批量上报请求体（匹配 Web API TelemetryBatch 契约）。
/// </summary>
public sealed class TelemetryBatchRequest
{
    [JsonPropertyName("events")]
    public List<TelemetryEvent> Events { get; init; } = [];
}

/// <summary>
/// 批量上报响应（匹配 Web API TelemetryAcceptedResponse 契约）。
/// </summary>
/// <param name="Accepted">成功接收的事件数量</param>
public sealed record TelemetryBatchResponse(int Accepted);
