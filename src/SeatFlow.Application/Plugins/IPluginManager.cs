using SeatFlow.Contracts.Interfaces;

namespace SeatFlow.Application.Plugins;

/// <summary>
/// 插件管理器接口，负责插件的发现、加载、卸载和安装。
/// 使用 <c>plugins-manifest.json</c> + 策略 <c>manifest.json</c> 双层清单架构。
/// </summary>
public interface IPluginManager
{
    /// <summary>
    /// 扫描插件目录并异步加载所有排座策略插件。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>已加载的策略插件信息集合（扁平列表，所有包的全部策略）。</returns>
    Task<IEnumerable<LoadedPluginInfo>> LoadStrategyPluginsAsync (CancellationToken ct = default);

    /// <summary>
    /// 扫描插件目录并异步加载所有插件，可按类别过滤。
    /// </summary>
    /// <param name="category">
    /// 要加载的插件类别。为 <c>null</c> 时加载所有插件。
    /// 内置类别：<c>"strategy"</c>、<c>"provider"</c>（预留）、<c>"exporter"</c>（预留）。
    /// </param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>已加载的插件信息集合（扁平列表，所有包的全部策略）。</returns>
    Task<IEnumerable<LoadedPluginInfo>> LoadPluginsAsync (string? category = null , CancellationToken ct = default);

    /// <summary>
    /// 异步卸载所有已加载的插件并释放资源（包括调用 <see cref="IPluginLifecycle.DisposeAsync"/>）。
    /// </summary>
    Task UnloadAllAsync ();

    /// <summary>
    /// 强制刷新：先卸载所有已加载的插件，然后重新扫描并加载。
    /// </summary>
    Task<IEnumerable<LoadedPluginInfo>> RefreshPluginsAsync (string? category = null , CancellationToken ct = default);

    /// <summary>
    /// 从插件包文件（<c>.ap-plugin</c>）安装插件。自动检测格式并安装到插件目录。
    /// </summary>
    /// <param name="packagePath">包文件路径。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>安装后的插件包目录路径。</returns>
    Task<string> InstallFromPackageAsync (string packagePath , CancellationToken ct = default);

    /// <summary>
    /// 获取所有已加载插件包的清单字典（键为包 ID）。
    /// </summary>
    IReadOnlyDictionary<string , LoadedPackageInfo> LoadedPackages { get; }

    /// <summary>
    /// 从缓存中获取已加载的插件信息。
    /// </summary>
    LoadedPluginInfo? GetLoadedPlugin (string pluginId);

    /// <summary>
    /// 注册脚本语言适配器。内置已注册 <c>"lua"</c> 和 <c>"csharp"</c>。
    /// </summary>
    /// <param name="scriptType">脚本类型标识符（如 "lua"、"csharp"）。</param>
    /// <param name="factory">创建策略实例的工厂委托。参数：脚本代码、策略 ID（manifest id）、策略名称、策略版本、优先级。</param>
    void RegisterScriptAdapter (string scriptType , Func<string , string , string , string , int , IPluginSeatingStrategy> factory);

    /// <summary>
    /// 根据策略 ID 查找其所属的包和策略加载信息。
    /// </summary>
    (LoadedPackageInfo? Package , LoadedPluginInfo? Plugin) FindStrategy (string strategyId);

    /// <summary>
    /// 加载指定插件包。若已加载则返回缓存。
    /// </summary>
    Task<LoadedPackageInfo?> LoadPackageAsync (string packageId , CancellationToken ct = default);

    /// <summary>
    /// 热重载指定包：卸载其所有策略，重新从磁盘加载。
    /// </summary>
    Task RefreshPackageAsync (string packageId , CancellationToken ct = default);

    /// <summary>
    /// 卸载指定包及其所有策略。
    /// </summary>
    Task UnloadPackageAsync (string packageId);

    /// <summary>
    /// 设置指定包的整体启用/禁用状态（写入 <c>data/enables.json</c>）。
    /// </summary>
    Task SetPackageEnabledAsync (string packageId , bool enabled , CancellationToken ct = default);

    /// <summary>
    /// 设置指定策略的启用/禁用状态（写入 <c>data/enables.json</c>）。
    /// </summary>
    Task SetStrategyEnabledAsync (string strategyId , bool enabled , CancellationToken ct = default);

    /// <summary>
    /// 加载指定包的 <c>data/enables.json</c>。
    /// </summary>
    Task<PluginEnables> LoadEnablesAsync (string packageId , CancellationToken ct = default);

    /// <summary>
    /// 保存指定包的 <c>data/enables.json</c>。
    /// </summary>
    Task SaveEnablesAsync (string packageId , PluginEnables enables , CancellationToken ct = default);
}
