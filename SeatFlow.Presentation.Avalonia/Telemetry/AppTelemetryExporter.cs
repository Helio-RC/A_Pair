using System.Collections.Generic;
using System.Diagnostics;
using OpenTelemetry;

namespace SeatFlow.Presentation.Avalonia.Telemetry;

/// <summary>
/// 自定义 OpenTelemetry Activity 导出器。将 Activity 转换为 TelemetryEvent 并写入发送队列。
/// </summary>
public sealed class AppTelemetryExporter : BaseExporter<Activity>
{
    private readonly TelemetryHttpClient _httpClient;

    public AppTelemetryExporter(TelemetryHttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public override ExportResult Export(in Batch<Activity> batch)
    {
        foreach (var activity in batch)
        {
            if (activity == null) continue;

            var payload = new Dictionary<string, object?>
            {
                ["duration_ms"] = activity.Duration.TotalMilliseconds
            };

            // 提取 Tag 属性
            foreach (var tag in activity.TagObjects)
            {
                if (tag.Value != null)
                    payload[tag.Key] = tag.Value;
            }

            // 状态
            payload["status"] = activity.Status == ActivityStatusCode.Error ? "error" : "ok";

            var evt = new TelemetryEvent
            {
                Type = activity.DisplayName,
                Timestamp = activity.StartTimeUtc,
                Payload = payload
            };

            _httpClient.TryEnqueue(evt);
        }

        return ExportResult.Success;
    }
}
