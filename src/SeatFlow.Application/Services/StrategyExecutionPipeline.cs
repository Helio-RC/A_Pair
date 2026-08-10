using SeatFlow.Application.Interfaces;
using SeatFlow.Contracts.Interfaces;
using SeatFlow.Core.Strategies;
using SeatFlow.Core.Workspace;
using Microsoft.Extensions.Logging;

namespace SeatFlow.Application.Services
{
    /// <summary>
    /// 策略执行管道，按优先级降序依次执行座位分配策略。
    /// </summary>
    /// <remarks>
    /// <b>执行模型："按优先级填空"（Fill-in-Order）</b>
    /// <para>
    /// 所有策略操作同一个 <see cref="SeatingWorkspace"/> 实例。
    /// 按 <see cref="ISeatingStrategy.Priority"/> 降序执行：数值越大的策略越先执行，
    /// 从空座中优先挑选。后执行的策略在剩余空座中择优。不存在"覆盖"——先占的座不会被推翻。
    /// </para>
    /// <list type="number">
    /// <item><b>FixedSeat(100)</b>：锁定固定座位，标记 IsFixed=true。后续策略的 GetEmptySeats() 自动排除</item>
    /// <item><b>FrontRowRotation(50)</b>：在非固定空座中填前排</item>
    /// <item><b>DeskMate(50)</b>：在剩余空座中拼连续块</item>
    /// <item><b>RandomFill(1)</b>：最终兜底，填满所有剩余空座</item>
    /// </list>
    /// <para>
    /// 内置策略（<see cref="ISeatingStrategy"/>）与插件策略（<see cref="IPluginSeatingStrategy"/>）
    /// 为一级类型混排执行：两者共享同一 Priority 空间，按数值统一排序。
    /// 插件策略通过 <see cref="IPluginWorkspace"/>（<see cref="SeatingWorkspace"/> 实现）访问受限 API。
    /// </para>
    /// <para>
    /// 策略间冲突解决 = Priority 数值（先到先得）。该设计是一个有意妥协——"后可覆盖"模型
    /// 因 Workspace API 限制（GetEmptySeats+TryAssignSeat 仅支持填空语义）无法实现。
    /// 详见 docs/adr/ADR-006.md。
    /// </para>
    /// </remarks>
    public class StrategyExecutionPipeline
    {
        /// <summary>
        /// 管道内部统一执行项，屏蔽内置策略与插件策略的类型差异。
        /// </summary>
        private sealed record PipelineStrategyItem (
            string Id ,
            string Name ,
            int Priority ,
            bool IsEnabled ,
            Func<SeatingWorkspace , CancellationToken , Task<(bool Success , string Message)>> Execute);

        private readonly List<PipelineStrategyItem> _strategies = [];
        private readonly ILogger<StrategyExecutionPipeline>? _logger;
        private readonly bool _throwOnFailure;

        /// <summary>
        /// 初始化策略执行管道（仅内置策略），并按优先级排序策略列表。
        /// </summary>
        /// <param name="strategies">要执行的内置策略集合。</param>
        /// <param name="logger">日志记录器。</param>
        /// <param name="throwOnFailure">是否在策略失败时抛出异常。</param>
        public StrategyExecutionPipeline (
            IEnumerable<ISeatingStrategy> strategies ,
            ILogger<StrategyExecutionPipeline>? logger = null ,
            bool throwOnFailure = false)
            : this(strategies , pluginStrategies: null , logger , throwOnFailure)
        {
        }

        /// <summary>
        /// 初始化策略执行管道（仅插件策略），并按优先级排序策略列表。
        /// </summary>
        /// <param name="pluginStrategies">要执行的插件策略集合。</param>
        /// <param name="logger">日志记录器。</param>
        /// <param name="throwOnFailure">是否在策略失败时抛出异常。</param>
        public StrategyExecutionPipeline (
            IEnumerable<IPluginSeatingStrategy> pluginStrategies ,
            ILogger<StrategyExecutionPipeline>? logger = null ,
            bool throwOnFailure = false)
            : this(strategies: null , pluginStrategies , logger , throwOnFailure)
        {
        }

        /// <summary>
        /// 初始化策略执行管道（内置 + 插件混排），并按优先级排序策略列表。
        /// </summary>
        /// <param name="strategies">要执行的内置策略集合（可为 null）。</param>
        /// <param name="pluginStrategies">要执行的插件策略集合（可为 null）。</param>
        /// <param name="logger">日志记录器。</param>
        /// <param name="throwOnFailure">是否在策略失败时抛出异常。</param>
        public StrategyExecutionPipeline (
            IEnumerable<ISeatingStrategy>? strategies ,
            IEnumerable<IPluginSeatingStrategy>? pluginStrategies ,
            ILogger<StrategyExecutionPipeline>? logger = null ,
            bool throwOnFailure = false)
        {
            if (strategies != null)
            {
                _strategies.AddRange(strategies.Select(s => new PipelineStrategyItem(
                    s.Id , s.Name , s.Priority , s.IsEnabled ,
                    async (w , ct) =>
                    {
                        var r = await s.ExecuteAsync(w , ct);
                        return (r.Success , r.Message);
                    })));
            }
            if (pluginStrategies != null)
            {
                _strategies.AddRange(pluginStrategies.Select(s => new PipelineStrategyItem(
                    s.Id , s.Name , s.Priority , s.IsEnabled ,
                    async (w , ct) =>
                    {
                        var r = await s.ExecuteAsync(w , ct);
                        return (r.Success , r.Message);
                    })));
            }
            _logger = logger;
            _throwOnFailure = throwOnFailure;
        }

        /// <summary>
        /// 依次执行所有已启用的策略，并返回最终的座位安排计划。
        /// </summary>
        /// <param name="workspace">当前座位工作区，所有策略将在此工作区上操作。</param>
        /// <param name="progress">用于报告执行进度的对象。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>执行完成后生成的座位安排计划。</returns>
        public async Task<SeatingPlan> ExecuteAsync (
            SeatingWorkspace workspace ,
            IProgress<SeatingProgress>? progress = null ,
            CancellationToken cancellationToken = default)
        {
            var enabledStrategies = _strategies.Where(s => s.IsEnabled).OrderByDescending(s => s.Priority).ToList();
            int total = enabledStrategies.Count;
            int current = 0;
            var failedStrategies = new List<string>();

            foreach (var strategy in enabledStrategies)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress?.Report(new SeatingProgress
                {
                    CurrentStep = ++current ,
                    TotalSteps = total ,
                    StatusMessage = $"正在执行策略: {strategy.Name}"
                });

                var result = await strategy.Execute(workspace , cancellationToken);
                if (!result.Success)
                {
                    failedStrategies.Add($"{strategy.Name}({strategy.Id}): {result.Message}");
                    workspace.LogError(strategy.Id , strategy.Name , "Pipeline_ExecFailed" , result.Message);
                    _logger?.LogError("策略执行失败: {StrategyName} ({StrategyId}) - {Message}" ,
                        strategy.Name , strategy.Id , result.Message);
                }
            }

            if (failedStrategies.Count != 0 && _throwOnFailure)
            {
                throw new StrategyExecutionException($"以下策略执行失败: {string.Join("; " , failedStrategies)}");
            }

            return workspace.BuildSeatingPlan();
        }
    }

    public class StrategyExecutionException (string message) : System.Exception(message)
    {
    }
}
