using SeatFlow.Contracts.Interfaces;
using SeatFlow.Contracts.Models;
using SeatFlow.Plugins.Sdk.Attributes;

namespace SeatFlow.Plugins.Sdk.Abstractions;

/// <summary>
/// 排座策略插件的抽象基类，继承 <see cref="PluginBase"/> 并实现 <see cref="IPluginSeatingStrategy"/>。
/// 减少样板代码，只需实现 <see cref="ExecuteAsync"/> 方法。
/// </summary>
/// <remarks>
/// <para>从 <see cref="PluginBase"/> 继承：<see cref="IPlugin.Id"/>、<see cref="IPlugin.Name"/>、
/// <see cref="IPlugin.Version"/>、<see cref="IPlugin.Category"/></para>
/// <para>新增成员：<see cref="Priority"/>（默认 50，可通过 <see cref="PluginAttribute.Priority"/> 配置）、
/// <see cref="IsEnabled"/>（默认 true，可通过 <see cref="PluginAttribute.Enabled"/> 配置）</para>
/// </remarks>
public abstract class PluginStrategyBase : PluginBase, IPluginSeatingStrategy
{
    /// <summary>
    /// 初始化基类，从 <see cref="PluginAttribute"/> 读取策略相关配置。
    /// 复用 <see cref="PluginBase"/> 已反射的特性，避免重复反射。
    /// </summary>
    protected PluginStrategyBase ()
    {
        var attr = ResolvedAttribute;
        if (attr is not null)
        {
            Priority = attr.Priority;
            IsEnabled = attr.Enabled;
        }
        else
        {
            Priority = 50;
            IsEnabled = true;
        }
    }

    /// <inheritdoc />
    public int Priority { get; set; }

    /// <inheritdoc />
    public bool IsEnabled { get; set; }

    /// <inheritdoc />
    public abstract Task<PluginStrategyResult> ExecuteAsync (
        IPluginWorkspace workspace ,
        CancellationToken cancellationToken);
}
