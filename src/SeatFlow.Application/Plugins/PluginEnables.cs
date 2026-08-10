using System.Text.Json.Serialization;

namespace SeatFlow.Application.Plugins
{
    /// <summary>
    /// 插件包的启用状态文件（<c>data/enables.json</c>），管理包及各策略的运行时启用/禁用状态。
    /// </summary>
    public class PluginEnables
    {
        /// <summary>
        /// 插件包整体是否启用。当设为 <c>false</c> 时，包内所有策略均不参与执行管道。
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 插件包功能类别，与 <see cref="PluginPackageManifest.Type"/> 对应。
        /// 内置类别：<c>"strategy"</c>、<c>"provider"</c>（预留）、<c>"exporter"</c>（预留）。
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "strategy";

        /// <summary>
        /// 各策略的启用状态字典。Key 为策略 ID（对应策略 <c>manifest.json</c> 中的 <c>id</c> 字段），
        /// Value 为该策略是否启用。未在字典中出现的策略默认跟随 <see cref="Enabled"/>（包级状态）。
        /// </summary>
        [JsonPropertyName("strategies")]
        public Dictionary<string , bool> Strategies { get; set; } = [];
    }
}
