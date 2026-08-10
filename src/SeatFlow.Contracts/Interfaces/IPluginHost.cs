namespace SeatFlow.Contracts.Interfaces;

/// <summary>
/// 插件宿主，在插件初始化时通过 <see cref="IPluginLifecycle.InitializeAsync"/> 传递给插件，
/// 提供宿主服务的访问入口。
/// </summary>
public interface IPluginHost
{
    /// <summary>获取插件配置服务，用于读写 <c>config.json</c>。</summary>
    IPluginConfigurationService Configuration { get; }

    /// <summary>获取插件所在的目录路径。</summary>
    string PluginDirectory { get; }
}
