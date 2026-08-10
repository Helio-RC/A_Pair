namespace SeatFlow.Application.Plugins
{
    /// <summary>
    /// 插件配置架构定义，描述插件可配置字段的元数据。
    /// </summary>
    /// <remarks>
    /// 用于在 UI 层动态生成配置表单，支持字符串、数字、布尔值和选择框等字段类型。
    /// </remarks>
    public class PluginConfigurationSchema
    {
        /// <summary>
        /// 获取或设置配置字段列表。
        /// </summary>
        public List<ConfigField> Fields { get; set; } = [];
    }

    /// <summary>
    /// 表示插件配置中的一个字段定义。
    /// </summary>
    public class ConfigField
    {
        /// <summary>
        /// 获取或设置字段的内部名称。
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 获取或设置字段的显示名称（用于 UI 标签）。
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// 获取或设置字段类型。支持的值：<c>"string"</c>、<c>"number"</c>、<c>"boolean"</c>、<c>"select"</c>。
        /// </summary>
        public string Type { get; set; } = "string";

        /// <summary>
        /// 获取或设置字段的默认值。
        /// </summary>
        public object? DefaultValue { get; set; }

        /// <summary>
        /// 获取或设置选择框（<c>Type = "select"</c>）的选项字典。
        /// </summary>
        public Dictionary<string , object>? Options { get; set; }
    }
}