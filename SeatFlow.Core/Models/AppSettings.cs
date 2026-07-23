using System.Text.Json.Serialization;

namespace SeatFlow.Core.Models
{
    /// <summary>
    /// 应用程序全局设置，持久化存储于 AppSettings.json 文件。
    /// </summary>
    public class AppSettings
    {
        /// <summary>文件格式版本号。</summary>
        public string Version { get; set; } = "1.1";

        /// <summary>窗口位置与大小设置。</summary>
        public WindowStateSettings WindowState { get; set; } = new();

        /// <summary>主题模式。</summary>
        public ThemeMode Theme { get; set; } = ThemeMode.System;

        /// <summary>界面语言，为空时跟随系统。</summary>
        public string Language { get; set; } = string.Empty;

        /// <summary>用户数据目录路径，为空时使用默认位置。</summary>
        public string DataDirectory { get; set; } = string.Empty;

        /// <summary>最近打开的文件列表。</summary>
        public List<string> RecentFiles { get; set; } = [];

        /// <summary>上次打开的文件路径。</summary>
        public string? LastOpenedFilePath { get; set; }

        /// <summary>上次使用的会场 ID。</summary>
        public string? LastVenueId { get; set; }

        /// <summary>是否在清除数据前弹出确认对话框。</summary>
        public bool ConfirmBeforeClear { get; set; } = true;

        /// <summary>座位图默认缩放比例。</summary>
        public double DefaultZoomLevel { get; set; } = 1.0;

        /// <summary>每个会场最大快照保存数（默认 30，0=不限）。</summary>
        public int MaxSnapshotsPerVenue { get; set; } = 30;

        /// <summary>是否已抑制运行环境警告（用户选择"不再提醒"）。</summary>
        public bool SuppressEnvironmentWarning { get; set; }

        /// <summary>是否为首次启动（控制首次引导的显示）。</summary>
        public bool IsFirstLaunch { get; set; }

        /// <summary>已展示过引导的页面（PageKey 名称 → true，或 true 表示该页面的独立引导已展示）。</summary>
        public Dictionary<string , bool> CompletedPageGuides { get; set; } = [];

        /// <summary>日志配置（不暴露到设置 UI）。</summary>
        public LogSettings Logging { get; set; } = new();

        /// <summary>遥测配置。</summary>
        public TelemetryConfig Telemetry { get; set; } = new();

        /// <summary>自动更新模式（默认：仅检查）。</summary>
        public AutoUpdateMode AutoUpdate { get; set; } = AutoUpdateMode.AutoUpdate;

        /// <summary>键盘快捷键配置。</summary>
        public KeyboardShortcutConfig KeyboardShortcuts { get; set; } = new();

        /// <summary>键盘快捷键配置。</summary>
        public KeyboardShortcutConfig KeyboardShortcuts { get; set; } = new();

    }

    /// <summary>
    /// 遥测配置。控制 OpenTelemetry 数据采集与上报行为。
    /// </summary>
    public sealed class TelemetryConfig
    {
        /// <summary>是否启用遥测上报（opt-in，默认关闭）。</summary>
        public bool Enabled { get; set; } = false;

        /// <summary>是否已展示过同意弹窗。</summary>
        public bool ConsentShown { get; set; } = false;

        /// <summary>Web API 遥测端点 URL。</summary>
        public string ServerUrl { get; set; } = "https://seatflow.work/api/app/telemetry";

        /// <summary>正常批量发送间隔（秒）。</summary>
        public int FlushIntervalSeconds { get; set; } = 60;

        /// <summary>单次批量最大事件数。</summary>
        public int MaxBatchSize { get; set; } = 100;

        /// <summary>页面浏览采样率（0.0-1.0）。</summary>
        public double PageViewSampleRate { get; set; } = 0.2;

        /// <summary>同名页面去重窗口（秒）。</summary>
        public int PageViewCoalesceSeconds { get; set; } = 60;

        /// <summary>计数器聚合窗口（秒）。</summary>
        public int MetricSnapshotIntervalSeconds { get; set; } = 120;

        /// <summary>是否启用 Gzip 压缩。</summary>
        public bool EnableCompression { get; set; } = true;
    }

    /// <summary>
    /// 键盘快捷键开关配置。控制各全局快捷键的启用/禁用状态。
    /// </summary>
    public sealed class KeyboardShortcutConfig
    {
        /// <summary>Ctrl+Z：撤销上一步操作。</summary>
        public bool UndoEnabled { get; set; } = true;

        /// <summary>Ctrl+Y：重做被撤销的操作。</summary>
        public bool RedoEnabled { get; set; } = true;

        /// <summary>Ctrl+S：保存当前数据。</summary>
        public bool SaveEnabled { get; set; } = true;

        /// <summary>Ctrl+滚轮：在座位图画布中缩放视图。</summary>
        public bool ZoomWithCtrlEnabled { get; set; } = true;

        /// <summary>Delete：删除选中的项目。</summary>
        public bool DeleteEnabled { get; set; } = true;

        /// <summary>Esc：取消当前选择或关闭弹窗。</summary>
        public bool EscapeEnabled { get; set; } = true;
    }

    /// <summary>
    /// 日志配置，存放于 AppSettings.json 中，不在设置页面展示。
    /// </summary>
    public sealed class LogSettings
    {
        /// <summary>最低日志级别：Debug / Information / Warning / Error。默认 Information，调试运行时自动降为 Debug。</summary>
        public string MinimumLevel { get; set; } = "Information";

        /// <summary>单个日志文件最大字节数，默认 10MB。</summary>
        public long FileSizeLimitBytes { get; set; } = 10_485_760;

        /// <summary>保留的日志文件数量上限。</summary>
        public int RetainedFileCountLimit { get; set; } = 30;

        /// <summary>
        /// 分模块日志等级覆盖。Key 为去掉 "SeatFlow." 前缀的命名空间前缀（如 "Core.Strategies"），
        /// Value 为日志等级字符串（Debug / Information / Warning / Error / Fatal）。
        /// 未匹配的模块沿用 <see cref="MinimumLevel"/>。
        /// </summary>
        public Dictionary<string, string> CategoryOverrides { get; set; } = new();
    }

    /// <summary>
    /// 窗口状态设置。
    /// </summary>
    public class WindowStateSettings
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; } = 1200;
        public double Height { get; set; } = 800;
        public bool IsMaximized { get; set; }
    }

    /// <summary>
    /// 主题模式枚举。
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ThemeMode
    {
        /// <summary>跟随系统主题。</summary>
        System,
        /// <summary>浅色主题。</summary>
        Light,
        /// <summary>深色主题。</summary>
        Dark
    }

    /// <summary>
    /// 自动更新模式。
    /// </summary>
    public enum AutoUpdateMode
    {
        /// <summary>关闭自动更新检查。</summary>
        Off,
        /// <summary>启动时仅检查更新，不下載。</summary>
        CheckOnly,
        /// <summary>检查更新并在确认后下载。</summary>
        AutoUpdate,
    }

}
