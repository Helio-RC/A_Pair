using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using SeatFlow.Core.Telemetry;

namespace SeatFlow.Presentation.Avalonia.Telemetry;

/// <summary>
/// 遥测禁用时的空操作实现。所有方法均为 no-op，不消耗任何资源。
/// </summary>
internal sealed class NullTelemetryService : ITelemetryService
{
    public bool IsEnabled => false;

    public void SetEnabled(bool enabled) { }

    public Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal) => null;

    public void RecordEvent(string eventType, Dictionary<string, object?>? attributes = null) { }

    public void RecordAppLaunch() { }
    public void RecordSeatingGeneration(bool success, int studentCount, int venueCount = 0, int strategyCount = 0, long durationMs = 0) { }
    public void RecordExport(string format, bool success) { }
    public void RecordError(string category, string message) { }
    public void RecordPageView(string pageName) { }
    public void RecordFeatureUsage(string featureName) { }

    public Task FlushAsync(TimeSpan? timeout = null) => Task.CompletedTask;
}
