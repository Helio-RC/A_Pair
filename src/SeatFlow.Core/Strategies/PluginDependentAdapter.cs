using SeatFlow.Contracts.Interfaces;
using SeatFlow.Contracts.Models;
using SeatFlow.Core.Models;
using SeatFlow.Core.Workspace;

namespace SeatFlow.Core.Strategies
{
    /// <summary>
    /// 将 <see cref="IPluginDependentSeatingStrategy"/> 适配为 <see cref="IDependentSeatingStrategy"/>，
    /// 使插件依赖策略能够接入 RandomFill 的分配评估循环。
    /// </summary>
    /// <remarks>
    /// 插件依赖策略（manifest <c>isIndependent: false</c>）由本适配器包装后注入
    /// <see cref="RandomFillStrategy.LoadDependentStrategies"/>。
    /// <see cref="Student"/> / <see cref="Seat"/> 本身实现 <see cref="IPluginStudent"/> /
    /// <see cref="IPluginSeat"/>，因此参数直接转发无需映射；
    /// <see cref="IRandomFillContext"/> 被包装为 <see cref="IPluginRandomFillContext"/>，
    /// 日志转发时自动附带策略 ID 与展示名称。
    /// </remarks>
    /// <param name="pluginStrategy">要适配的插件依赖策略实例。</param>
    public class PluginDependentAdapter (IPluginDependentSeatingStrategy pluginStrategy) : IDependentSeatingStrategy
    {
        private readonly IPluginDependentSeatingStrategy _pluginStrategy = pluginStrategy
            ?? throw new ArgumentNullException(nameof(pluginStrategy));

        /// <inheritdoc />
        public string Id => _pluginStrategy.Id;

        /// <inheritdoc />
        public string Name => _pluginStrategy.Name;

        /// <inheritdoc />
        public string DisplayName => _pluginStrategy.Name;

        /// <inheritdoc />
        public int Priority
        {
            get => _pluginStrategy.Priority;
            set => _pluginStrategy.Priority = value;
        }

        /// <inheritdoc />
        public bool IsEnabled
        {
            get => _pluginStrategy.IsEnabled;
            set => _pluginStrategy.IsEnabled = value;
        }

        /// <inheritdoc />
        public async Task<DependentEvaluationResult> EvaluateAsync (
            SeatingWorkspace workspace ,
            Student student ,
            Seat targetSeat ,
            IRandomFillContext context ,
            CancellationToken cancellationToken)
        {
            var pluginContext = new PluginRandomFillContextAdapter(context , _pluginStrategy.Id , _pluginStrategy.Name);
            var result = await _pluginStrategy.EvaluateAsync(workspace , student , targetSeat , pluginContext , cancellationToken);
            return new DependentEvaluationResult
            {
                Approved = result.Approved ,
                AlreadyHandled = result.AlreadyHandled ,
                Message = result.Message
            };
        }

        /// <inheritdoc />
        public ValidationResult ValidateConfiguration ()
            => new() { IsValid = true };

        /// <summary>
        /// 将 <see cref="IRandomFillContext"/> 包装为 <see cref="IPluginRandomFillContext"/>，
        /// 日志转发时自动附带插件策略 ID 与展示名称。
        /// </summary>
        /// <param name="inner">原始 RandomFill 上下文。</param>
        /// <param name="strategyId">插件策略 ID。</param>
        /// <param name="displayName">插件策略展示名称。</param>
        private sealed class PluginRandomFillContextAdapter (IRandomFillContext inner , string strategyId , string displayName) : IPluginRandomFillContext
        {
            /// <inheritdoc />
            public int RerollCount => inner.RerollCount;

            /// <inheritdoc />
            public int MaxRerolls => inner.MaxRerolls;

            /// <inheritdoc />
            public void LogWarning (string messageKey , params object?[] args)
                => inner.LogWarning(strategyId , displayName , messageKey , args);

            /// <inheritdoc />
            public void LogError (string messageKey , params object?[] args)
                => inner.LogError(strategyId , displayName , messageKey , args);

            /// <inheritdoc />
            public void LogInfo (string messageKey , params object?[] args)
                => inner.LogInfo(strategyId , displayName , messageKey , args);
        }
    }
}
