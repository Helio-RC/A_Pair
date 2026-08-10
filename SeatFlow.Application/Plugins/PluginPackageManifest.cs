using System.Text.Json.Serialization;

namespace SeatFlow.Application.Plugins
{
    /// <summary>
    /// 插件包清单，对应 <c>plugins-manifest.json</c> 文件。
    /// 描述插件包的元数据及其包含的所有策略子组件。
    /// </summary>
    /// <remarks>
    /// 包清单不包含单一策略的加载指令，
    /// 而是通过 <see cref="Strategies"/> 数组声明多个策略子组件，
    /// 每个子组件有独立的 manifest 文件和加载方式。
    /// </remarks>
    public class PluginPackageManifest
    {
        /// <summary>
        /// 插件包显示名称。
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 插件包唯一标识符。
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 插件包语义化版本号。
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";

        /// <summary>
        /// 插件包作者。
        /// </summary>
        [JsonPropertyName("author")]
        public string Author { get; set; } = string.Empty;

        /// <summary>
        /// 插件包描述。
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 插件包仓库 URL（可选）。
        /// </summary>
        [JsonPropertyName("repository")]
        public string? Repository { get; set; }

        /// <summary>
        /// 插件包网站 URL（可选）。
        /// </summary>
        [JsonPropertyName("website")]
        public string? Website { get; set; }

        /// <summary>
        /// 上次更新日期（ISO 8601 格式，可选）。
        /// </summary>
        [JsonPropertyName("lastUpdate")]
        public string? LastUpdate { get; set; }

        /// <summary>
        /// 插件包功能类别。内置类别：<c>"strategy"</c>（已实现）、<c>"provider"</c>（预留）、<c>"exporter"</c>（预留）。
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "strategy";

        /// <summary>
        /// 插件包内包含的插件条目列表（v2 格式，<c>plugins[]</c>）。
        /// 每个条目对应一个独立加载的插件（当前支持 <c>kind: "strategy"</c>）。
        /// </summary>
        [JsonPropertyName("plugins")]
        public List<PluginEntry> Plugins { get; set; } = [];
    }
}
