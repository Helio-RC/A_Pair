namespace SeatFlow.Core.Telemetry;

/// <summary>
/// 遥测事件类型常量。遵循 {领域}.{动作} 命名约定，匹配 Web API 的 TelemetryEvent.Type 字段。
/// </summary>
public static class TelemetryEventTypes
{
    public const string AppStart = "app.start";
    public const string AppExit = "app.exit";
    public const string PageView = "app.page_view";
    public const string FeatureUsage = "app.feature_usage";
    public const string SeatingGeneration = "seatflow.generation";
    public const string Export = "seatflow.export";
    public const string Error = "app.error";
    public const string MetricsSnapshot = "metrics.snapshot";
}

/// <summary>
/// 指标仪器名称常量。
/// </summary>
public static class TelemetryInstrumentNames
{
    public const string AppLaunches = "seatflow.app.launches";
    public const string SeatingGenerations = "seatflow.seating.generations";
    public const string Exports = "seatflow.exports";
    public const string Errors = "seatflow.errors";
}

/// <summary>
/// OpenTelemetry 语义约定属性键（snake_case）。
/// </summary>
public static class TelemetryAttributeKeys
{
    public const string AppVersion = "app.version";
    public const string AppCommit = "app.commit";
    public const string OsType = "os.type";
    public const string OsDescription = "os.description";
    public const string RuntimeName = "process.runtime.name";
    public const string RuntimeVersion = "process.runtime.version";
    public const string HostArch = "host.arch";
    public const string PageName = "page.name";
    public const string FeatureName = "feature.name";
    public const string GenerationSuccess = "seatflow.generation.success";
    public const string VenueCount = "seatflow.venue.count";
    public const string StudentCount = "seatflow.student.count";
    public const string StrategyCount = "seatflow.strategy.count";
    public const string GenerationDurationMs = "seatflow.generation.duration_ms";
    public const string ExportFormat = "seatflow.export.format";
    public const string ExportSuccess = "seatflow.export.success";
    public const string ErrorCategory = "error.category";
    public const string ErrorMessage = "error.message";
    public const string MetricWindowSeconds = "metrics.window_seconds";
}
