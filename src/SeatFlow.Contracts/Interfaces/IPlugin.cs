namespace SeatFlow.Contracts.Interfaces;

/// <summary>
/// 所有插件的通用身份契约，定义了插件的基本元数据。
/// 每个插件类型（策略、数据提供者、导出器等）都应实现此接口。
/// </summary>
public interface IPlugin
{
    /// <summary>插件唯一标识符。</summary>
    string Id { get; }

    /// <summary>插件显示名称。</summary>
    string Name { get; }

    /// <summary>插件版本号。</summary>
    string Version { get; }

    /// <summary>
    /// 插件功能类别。
    /// 内置类别：<c>"strategy"</c>（排座策略）、<c>"provider"</c>（数据提供者，预留）、<c>"exporter"</c>（导出器，预留）。
    /// 外部插件可使用自定义类别字符串。
    /// </summary>
    string Category { get; }
}
