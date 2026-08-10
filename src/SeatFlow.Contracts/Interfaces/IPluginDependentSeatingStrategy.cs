using SeatFlow.Contracts.Models;

namespace SeatFlow.Contracts.Interfaces;

/// <summary>
/// 插件依赖策略接口，供插件实现 RandomFill 上下文内的评估逻辑。
/// 与 <see cref="IPluginSeatingStrategy"/> 不同，依赖策略不加入外部执行管道，
/// 而是在 RandomFill 每次随机分配 (student, seat) 对时被调用，
/// 可以批准（Approve）、拒绝并请求重掷（Reject）、或自行完成分配（Handled）。
/// 通过 <see cref="IPluginWorkspace"/> 访问受限 API，通过
/// <see cref="IPluginRandomFillContext"/> 了解重掷状态并记录消息。
/// </summary>
/// <remarks>
/// 宿主侧由 Core 层的适配器包装为 <see cref="SeatFlow.Core.Strategies.IDependentSeatingStrategy"/>
/// 后注入 RandomFill 的分配循环。插件 manifest 中 <c>isIndependent: false</c> 表示依赖策略。
/// </remarks>
public interface IPluginDependentSeatingStrategy : IPlugin
{
    /// <inheritdoc cref="IPlugin.Category"/>
    string IPlugin.Category => "strategy";

    /// <inheritdoc cref="IPlugin.Version"/>
    string IPlugin.Version => "1.0.0";

    /// <summary>上下文内部优先级，数值越大越先被评估。</summary>
    int Priority { get; set; }

    /// <summary>策略是否启用。</summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// 评估 RandomFill 提议的 (student, seat) 分配对。
    /// 依赖策略可以批准、拒绝（请求重掷）或自行完成分配。
    /// </summary>
    /// <param name="workspace">当前工作区（插件受限视图）。</param>
    /// <param name="student">提议分配的学生。</param>
    /// <param name="targetSeat">提议分配的目标座位。</param>
    /// <param name="context">RandomFill 上下文，提供重掷计数和日志接口。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>评估结果。</returns>
    Task<PluginDependentEvaluationResult> EvaluateAsync (
        IPluginWorkspace workspace ,
        IPluginStudent student ,
        IPluginSeat targetSeat ,
        IPluginRandomFillContext context ,
        CancellationToken ct);
}
