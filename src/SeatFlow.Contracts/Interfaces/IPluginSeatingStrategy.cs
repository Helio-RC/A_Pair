using SeatFlow.Contracts.Models;

namespace SeatFlow.Contracts.Interfaces;

/// <summary>
/// 插件策略接口，供外部插件实现自定义排座逻辑。
/// 继承自 <see cref="IPlugin"/> 提供基础元数据，并扩展策略执行相关的成员。
/// </summary>
public interface IPluginSeatingStrategy : IPlugin
{
    /// <inheritdoc cref="IPlugin.Category"/>
    string IPlugin.Category => "strategy";

    /// <inheritdoc cref="IPlugin.Version"/>
    string IPlugin.Version => "1.0.0";

    /// <summary>执行优先级，数值越大越先执行。</summary>
    int Priority { get; set; }

    /// <summary>策略是否启用。</summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// 执行插件策略逻辑。
    /// </summary>
    /// <param name="workspace">当前工作区，通过 <see cref="IPluginWorkspace"/> 提供受限 API。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>执行结果。</returns>
    Task<PluginStrategyResult> ExecuteAsync (IPluginWorkspace workspace , CancellationToken ct);
}
