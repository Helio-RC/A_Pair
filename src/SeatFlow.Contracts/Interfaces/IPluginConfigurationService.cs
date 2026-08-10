namespace SeatFlow.Contracts.Interfaces;

/// <summary>
/// 插件配置服务接口，提供插件配置的加载、保存和文件监视功能。
/// </summary>
/// <remarks>
/// 每个插件在 <c>plugins/&lt;pluginId&gt;/config.json</c> 路径下维护自己的配置文件。
/// 支持通过 <see cref="WatchConfiguration"/> 监视配置文件变更并自动通知插件。
/// </remarks>
public interface IPluginConfigurationService
{
    /// <summary>
    /// 加载指定插件的配置。
    /// </summary>
    /// <typeparam name="T">配置对象的类型。</typeparam>
    /// <param name="pluginId">插件唯一标识符。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>反序列化后的配置对象；若配置文件不存在则返回默认实例。</returns>
    Task<T?> LoadConfigurationAsync<T> (string pluginId , CancellationToken cancellationToken = default) where T : class, new();

    /// <summary>
    /// 保存指定插件的配置到 JSON 文件。
    /// </summary>
    /// <typeparam name="T">配置对象的类型。</typeparam>
    /// <param name="pluginId">插件唯一标识符。</param>
    /// <param name="configuration">要保存的配置对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SaveConfigurationAsync<T> (string pluginId , T configuration , CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// 监视指定插件的配置文件变更，当文件被修改时触发回调。
    /// 重复调用将替换旧监视器。
    /// </summary>
    /// <param name="pluginId">插件唯一标识符。</param>
    /// <param name="onChange">配置文件变更时调用的回调，参数为插件 ID。</param>
    void WatchConfiguration (string pluginId , Action<string> onChange);

    /// <summary>
    /// 停止监视指定插件的配置文件变更。
    /// </summary>
    /// <param name="pluginId">插件唯一标识符。</param>
    void StopWatching (string pluginId);
}
