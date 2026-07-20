using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using SeatFlow.Core.Telemetry;

namespace SeatFlow.Presentation.Avalonia.Telemetry;

/// <summary>
/// 自定义 OpenTelemetry Metric 导出器。将 Metric 快照转换为聚合 TelemetryEvent 并写入发送队列。
/// 计数器在本地预聚合，每 MetricSnapshotIntervalSeconds 产生 1 条快照事件。
/// </summary>
public sealed class AppTelemetryMetricExporter : BaseExporter<Metric>
{
    private readonly TelemetryHttpClient _httpClient;
    private readonly int _windowSeconds;

    public AppTelemetryMetricExporter(TelemetryHttpClient httpClient, int windowSeconds)
    {
        _httpClient = httpClient;
        _windowSeconds = windowSeconds;
    }

    public override ExportResult Export(in Batch<Metric> batch)
    {
        var payload = new Dictionary<string, object?>
        {
            [TelemetryAttributeKeys.MetricWindowSeconds] = _windowSeconds
        };

        var hasData = false;

        foreach (var metric in batch)
        {
            if (metric == null) continue;

            foreach (var metricPoint in metric.GetMetricPoints())
            {
                var value = metric.MetricType switch
                {
                    MetricType.LongSum => metricPoint.GetSumLong(),
                    MetricType.DoubleSum => metricPoint.GetSumDouble(),
                    MetricType.LongGauge => metricPoint.GetGaugeLastValueLong(),
                    MetricType.DoubleGauge => metricPoint.GetGaugeLastValueDouble(),
                    MetricType.Histogram => metricPoint.GetHistogramSum(),
                    _ => 0.0
                };

                payload[metric.Name] = value;
                hasData = true;
            }
        }

        if (hasData)
        {
            var evt = new TelemetryEvent
            {
                Type = TelemetryEventTypes.MetricsSnapshot,
                Timestamp = DateTimeOffset.UtcNow,
                Payload = payload
            };

            _httpClient.TryEnqueue(evt);
        }

        return ExportResult.Success;
    }
}
