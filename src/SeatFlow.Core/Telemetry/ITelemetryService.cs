using System.Diagnostics;

namespace SeatFlow.Core.Telemetry;

/// <summary>
/// 遥测服务接口。提供 OpenTelemetry 兼容的跟踪（ActivitySource）、事件记录和指标（Meter）功能。
/// 所有方法在遥测禁用时均为安全空操作。
/// </summary>
public interface ITelemetryService
{
    /// <summary>遥测是否已启用。</summary>
    bool IsEnabled { get; }

    /// <summary>在运行时切换遥测启用状态。</summary>
    void SetEnabled(bool enabled);

    // ── 跟踪（Tracing） ──

    /// <summary>启动一个跟踪活动（span），返回 IDisposable，dispose 时自动停止。禁用时返回 null。</summary>
    Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal);

    // ── 事件（Events） ──

    /// <summary>记录一个即时事件（fire-and-forget）。</summary>
    void RecordEvent(string eventType, System.Collections.Generic.Dictionary<string, object?>? attributes = null);

    // ── 指标（Metrics） ──

    /// <summary>记录应用启动。</summary>
    void RecordAppLaunch();

    /// <summary>记录排座生成操作结果。</summary>
    void RecordSeatingGeneration(bool success, int studentCount, int venueCount = 0, int strategyCount = 0, long durationMs = 0);

    /// <summary>记录导出操作。</summary>
    void RecordExport(string format, bool success);

    /// <summary>记录错误。</summary>
    void RecordError(string category, string message);

    /// <summary>记录页面访问（含采样和去重）。</summary>
    void RecordPageView(string pageName);

    /// <summary>记录功能使用。</summary>
    void RecordFeatureUsage(string featureName);

    // ── 生命周期 ──

    /// <summary>强制刷新所有缓冲事件并等待发送完成（有超时限制）。</summary>
    System.Threading.Tasks.Task FlushAsync(TimeSpan? timeout = null);
}
