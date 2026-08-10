using System.Text.Json.Serialization;

namespace SeatFlow.Application.Plugins
{
    /// <summary>
    /// 插件类型标识（v2 包格式）。当前仅 <see cref="Strategy"/> 已实现，
    /// <see cref="DataProvider"/> / <see cref="Exporter"/> 为预留扩展类型。
    /// </summary>
    public static class PluginKind
    {
        /// <summary>排座策略插件（当前唯一受支持的加载类型）。</summary>
        public const string Strategy = "strategy";

        /// <summary>学生数据提供器插件（预留，未实现）。</summary>
        public const string DataProvider = "data-provider";

        /// <summary>座位表导出器插件（预留，未实现）。</summary>
        public const string Exporter = "exporter";
    }

    /// <summary>
    /// 插件包中单个插件的加载条目，位于 <c>plugins-manifest.json</c> 的 <c>plugins[]</c> 数组中（v2 格式）。
    /// 包含插件类型、子目录路径、manifest 文件路径、以及加载指令（程序集或脚本二选一）。
    /// </summary>
    public class PluginEntry
    {
        /// <summary>
        /// 插件类型，见 <see cref="PluginKind"/>。未知类型加载时给出警告并跳过。
        /// </summary>
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = PluginKind.Strategy;

        /// <summary>
        /// 插件子目录的相对路径（相对于包根目录）。
        /// </summary>
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// 策略 manifest 文件的路径（相对于包根目录）。
        /// manifest 内容遵循 <see cref="SeatFlow.Core.Models.StrategyManifest"/> 格式。
        /// </summary>
        [JsonPropertyName("manifest")]
        public string Manifest { get; set; } = string.Empty;

        /// <summary>
        /// 程序集文件名（仅程序集插件，相对于 <see cref="Path"/> 或包根目录）。
        /// 与 <c>EntryType</c> 配合使用。
        /// </summary>
        [JsonPropertyName("assembly")]
        public string? Assembly { get; set; }

        /// <summary>
        /// 策略入口类型的完全限定名（仅程序集插件）。
        /// 该类型必须实现 <see cref="SeatFlow.Contracts.Interfaces.IPluginSeatingStrategy"/>。
        /// </summary>
        [JsonPropertyName("entryType")]
        public string? EntryType { get; set; }

        /// <summary>
        /// 脚本文件名（仅脚本插件，相对于 <see cref="Path"/> 或包根目录）。
        /// 与 <c>ScriptType</c> 配合使用。
        /// </summary>
        [JsonPropertyName("scriptFile")]
        public string? ScriptFile { get; set; }

        /// <summary>
        /// 脚本类型。支持的值：<c>"lua"</c> 或 <c>"csharp"</c>。
        /// </summary>
        [JsonPropertyName("scriptType")]
        public string? ScriptType { get; set; }
    }
}
