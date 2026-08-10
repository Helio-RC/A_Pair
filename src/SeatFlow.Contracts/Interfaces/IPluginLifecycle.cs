namespace SeatFlow.Contracts.Interfaces;

/// <summary>
/// 插件生命周期管理接口（可选实现）。
/// 实现了此接口的插件，宿主会在加载时调用 <see cref="InitializeAsync"/>，
/// 在卸载时调用 <see cref="DisposeAsync"/>。
/// </summary>
public interface IPluginLifecycle
{
    /// <summary>
    /// 插件初始化，在加载完成后由宿主调用。
    /// </summary>
    /// <param name="host">插件宿主，提供配置服务和插件目录信息。</param>
    /// <param name="ct">取消令牌。</param>
    Task InitializeAsync (IPluginHost host , CancellationToken ct = default);

    /// <summary>
    /// 释放插件持有的资源（文件句柄、网络连接等）。由宿主在卸载时调用。
    /// </summary>
    Task DisposeAsync ();
}
