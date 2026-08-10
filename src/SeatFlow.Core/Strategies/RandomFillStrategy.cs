using SeatFlow.Core.Models;
using SeatFlow.Core.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Core.Strategies
{
    /// <summary>
    /// 随机填充策略（Priority=1，最后执行，填满所有剩余座位）。
    /// 作为管道中的兜底策略，在所有独立策略完成分配后运行。
    /// </summary>
    /// <remarks>
    /// <b>依赖策略上下文（v2）</b>
    /// <para>
    /// 当加载了 <see cref="IDependentSeatingStrategy"/> 时，RandomFill 的分配循环变为上下文驱动的：
    /// 每次随机选出 (student, seat) 对后，依次调用所有依赖策略进行评估。
    /// 依赖策略可以批准、拒绝（请求重掷）或自行完成分配（包括连携修改相邻座位）。
    /// 重掷有上限，超过后兜底强制分配。
    /// </para>
    /// <para>
    /// 若没有依赖策略，回退到原始的 Fisher-Yates 洗牌 + 顺序分配路径（快速路径）。
    /// </para>
    /// </remarks>
    public class RandomFillStrategy (Random random , ILogger<RandomFillStrategy>? logger = null) : ISeatingStrategy
    {
        private readonly Random _random = random ?? throw new ArgumentNullException(nameof(random));
        private readonly ILogger<RandomFillStrategy> _logger = logger ?? NullLogger<RandomFillStrategy>.Instance;
        private readonly List<IDependentSeatingStrategy> _dependentStrategies = [];

        /// <summary>每个 (student, seat) 对的最大重掷次数。</summary>
        private const int DefaultMaxRerolls = 10;

        /// <summary>
        /// 使用默认随机数种子创建实例。
        /// </summary>
        public RandomFillStrategy () : this(new Random()) { }

        /// <summary>RandomFill 策略 ID 常量，供 ViewModel 等层引用。</summary>
        public const string StrategyId = "RandomFill";

        /// <inheritdoc />
        public string Id { get; } = StrategyId;

        /// <inheritdoc />
        public string Name { get; } = StrategyId;

        /// <inheritdoc />
        public int Priority { get; set; } = 1;

        /// <inheritdoc />
        public bool IsEnabled { get; set; } = true;

        /// <summary>策略展示名称。</summary>
        public const string DisplayNameConst = "随机填充";

        /// <summary>
        /// 加载依赖策略列表。由 ApplicationFacade 在管道执行前调用。
        /// 依赖策略按 Priority 降序排列（数值越大越先评估）。
        /// </summary>
        public void LoadDependentStrategies (IEnumerable<IDependentSeatingStrategy> strategies)
        {
            _dependentStrategies.Clear();
            _dependentStrategies.AddRange(strategies.OrderByDescending(s => s.Priority));
            _logger.LogInformation("RandomFill：已加载 {Count} 个依赖策略" , _dependentStrategies.Count);
        }

        /// <summary>
        /// 是否有依赖策略已加载且至少一个启用。
        /// </summary>
        public bool HasActiveDependents => _dependentStrategies.Count > 0
            && _dependentStrategies.Any(s => s.IsEnabled);

        /// <inheritdoc />
        public async Task<StrategyExecutionResult> ExecuteAsync (
            SeatingWorkspace workspace , CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(workspace);
            cancellationToken.ThrowIfCancellationRequested();

            var emptySeats = workspace.GetEmptySeats().ToList();
            var assignedIds = workspace.BuildSeatingPlan().Assignments.Values.ToHashSet();
            var unassignedStudents = workspace.Students
                .Where(s => !assignedIds.Contains(s.Id))
                .ToList();

            _logger.LogInformation(
                "RandomFill 策略开始执行：{EmptySeats} 个空座位，{Unassigned} 名未分配学生，{Dependent} 个依赖策略" ,
                emptySeats.Count , unassignedStudents.Count ,
                _dependentStrategies.Count(s => s.IsEnabled));

            if (unassignedStudents.Count == 0 || emptySeats.Count == 0)
            {
                _logger.LogDebug("RandomFill：无未分配学生或空座位，跳过");
                return new StrategyExecutionResult { Success = true };
            }

            // ── 快速路径：无依赖策略 → 原始 Fisher-Yates + 顺序分配 ──
            if (!HasActiveDependents)
            {
                FastPathAssign(workspace , unassignedStudents , emptySeats , cancellationToken);
                LogCompletion(workspace);
                return new StrategyExecutionResult { Success = true };
            }

            // ── 上下文路径：依赖策略参与评估 ──
            await ContextPathAssignAsync(workspace , emptySeats , unassignedStudents , cancellationToken);
            LogCompletion(workspace);
            return new StrategyExecutionResult { Success = true };
        }

        /// <summary>
        /// 快速路径：Fisher-Yates 洗牌 + 顺序分配。
        /// 无依赖策略时使用，避免上下文循环开销。
        /// </summary>
        private void FastPathAssign (
            SeatingWorkspace workspace ,
            List<Student> students ,
            List<Seat> seats ,
            CancellationToken ct)
        {
            // Fisher-Yates 洗牌
            FisherYatesShuffle(students);

            int assignCount = Math.Min(seats.Count , students.Count);
            for (int i = 0; i < assignCount && !ct.IsCancellationRequested; i++)
            {
                workspace.TryAssignSeat(seats[i].Id , students[i].Id , out _);
            }

            _logger.LogInformation("RandomFill 快速路径完成：分配 {Assigned} 名学生" , assignCount);
        }

        /// <summary>
        /// 上下文路径：每次随机出 (student, seat) 对后调用依赖策略评估。
        /// 支持重掷和连携修改。
        /// </summary>
        private async Task ContextPathAssignAsync (
            SeatingWorkspace workspace ,
            List<Seat> initialEmptySeats ,
            List<Student> initialUnassigned ,
            CancellationToken ct)
        {
            var emptySeats = new List<Seat>(initialEmptySeats);
            var unassignedStudents = new List<Student>(initialUnassigned);
            var enabledDependents = _dependentStrategies.Where(s => s.IsEnabled).ToList();

            // 收集有特殊约束的学生 ID，优先分配以减少重掷
            var constrainedIds = new HashSet<string>();
            foreach (var dep in enabledDependents)
            {
                foreach (var id in dep.GetConstrainedStudentIds())
                    constrainedIds.Add(id);
            }

            if (constrainedIds.Count > 0)
            {
                var constrained = unassignedStudents.Where(s => constrainedIds.Contains(s.Id)).ToList();
                var unconstrained = unassignedStudents.Where(s => !constrainedIds.Contains(s.Id)).ToList();

                // 两组各自洗牌
                FisherYatesShuffle(constrained);
                FisherYatesShuffle(unconstrained);

                unassignedStudents = [.. constrained , .. unconstrained];
                _logger.LogInformation(
                    "RandomFill：约束学生优先——总约束 {Total} 人，待分配 {Constrained} 人，其余 {Unconstrained} 人" ,
                    constrainedIds.Count , constrained.Count , unconstrained.Count);
            }

            // 快照前序策略（FixedSeat/FrontRowRotation）已分配的学生 ID，传递给依赖策略
            // 依赖策略（DeskMate）腾挪时不会移动这些学生
            var priorAssignedIds = workspace.BuildSeatingPlan().Assignments.Values.ToHashSet();
            foreach (var dep in enabledDependents)
                dep.SetPriorAssignedStudentIds(priorAssignedIds);

            int maxRerolls = DefaultMaxRerolls;
            int totalRerolls = 0;

            while (emptySeats.Count > 0 && unassignedStudents.Count > 0 && !ct.IsCancellationRequested)
            {
                // 随机选一个学生
                var student = unassignedStudents[_random.Next(unassignedStudents.Count)];

                // 随机选一个座位
                var seat = emptySeats[_random.Next(emptySeats.Count)];

                int rerollCount = 0;
                bool assigned = false;

                while (rerollCount < maxRerolls && !ct.IsCancellationRequested)
                {
                    var context = new RandomFillContextImpl(workspace , rerollCount , maxRerolls);
                    bool alreadyHandled = false;
                    bool allApproved = true;

                    // 依次调用依赖策略评估
                    foreach (var dep in enabledDependents)
                    {
                        ct.ThrowIfCancellationRequested();

                        // 前序依赖策略已处理分配，后续策略仅能做检查（拒绝/处理均忽略）
                        if (alreadyHandled)
                        {
                            await dep.EvaluateAsync(workspace , student , seat , context , ct);
                            continue;
                        }

                        var result = await dep.EvaluateAsync(
                            workspace , student , seat , context , ct);

                        if (!result.Approved)
                        {
                            // 策略拒绝，触发重掷
                            rerollCount++;
                            totalRerolls++;
                            allApproved = false;

                            // 选一个不同的座位重试
                            var otherSeats = emptySeats
                                .Where(s => s.Id != seat.Id)
                                .ToList();
                            if (otherSeats.Count == 0)
                            {
                                // 没有其他座位可选，强制退出重试循环
                                rerollCount = maxRerolls;
                            }
                            else
                            {
                                seat = otherSeats[_random.Next(otherSeats.Count)];
                            }

                            _logger.LogDebug(
                                "RandomFill：策略 {StrategyId} 拒绝 ({Reason})，重掷 #{Reroll}/{Max}，学生 {Student}" ,
                                dep.Id , result.Message ?? "无原因" ,
                                rerollCount , maxRerolls , student.Name);
                            break; // 重新开始依赖策略评估循环
                        }

                        if (result.AlreadyHandled)
                        {
                            // 依赖策略已自行完成分配（含连携修改）
                            alreadyHandled = true;
                            _logger.LogDebug(
                                "RandomFill：策略 {StrategyId} 已处理分配，学生 {Student} → {Seat}" ,
                                dep.Id , student.Name , seat.Id);
                            // 不 break — 继续让后续依赖策略检查结果
                        }
                    }

                    if (allApproved)
                    {
                        // 所有依赖策略批准，执行分配
                        if (!alreadyHandled)
                        {
                            workspace.TryAssignSeat(seat.Id , student.Id , out _);
                        }
                        else
                        {
                            // 安全检查：依赖策略声称已处理，但学生可能未实际分配
                            // 这种情况下补调 TryAssignSeat 防止无限循环
                            var alreadyAssigned = workspace.BuildSeatingPlan()
                                .Assignments.ContainsValue(student.Id);
                            if (!alreadyAssigned)
                            {
                                _logger.LogWarning(
                                    "RandomFill：策略声称已处理但学生 {Student} 未分配，执行回退分配" ,
                                    student.Name);
                                workspace.TryAssignSeat(seat.Id , student.Id , out _);
                            }
                        }

                        // 刷新状态：依赖策略可能已分配多名学生（连携修改）
                        RefreshState(workspace , ref emptySeats , ref unassignedStudents);
                        assigned = true;
                        break;
                    }
                }

                if (!assigned)
                {
                    // 重掷上限：兜底强制分配，随机选一个空座
                    if (emptySeats.Count == 0)
                    {
                        _logger.LogWarning("RandomFill：无剩余空座，学生 {Student} 无法分配" , student.Name);
                        break;
                    }

                    var fallbackSeat = emptySeats[_random.Next(emptySeats.Count)];

                    _logger.LogWarning(
                        "RandomFill：重掷次数达上限 {MaxRerolls}，学生 {Student} 强制分配到座位 {Seat}" ,
                        maxRerolls , student.Name , fallbackSeat.Id);

                    workspace.LogWarning(
                        Id , DisplayNameConst , "RandomFill_RerollExhausted" ,
                        maxRerolls , student.Id , fallbackSeat.Id);

                    workspace.TryAssignSeat(fallbackSeat.Id , student.Id , out _);
                    RefreshState(workspace , ref emptySeats , ref unassignedStudents);
                }
            }

            _logger.LogInformation(
                "RandomFill 上下文路径完成：剩余空座 {RemainingSeats}，剩余学生 {RemainingStudents}，总重掷 {TotalRerolls} 次" ,
                emptySeats.Count , unassignedStudents.Count , totalRerolls);
        }

        /// <summary>
        /// 刷新空座列表和未分配学生列表，复用 BuildSeatingPlan 结果避免重复调用。
        /// </summary>
        private static void RefreshState (
            SeatingWorkspace workspace ,
            ref List<Seat> emptySeats ,
            ref List<Student> unassignedStudents)
        {
            emptySeats = [.. workspace.GetEmptySeats()];
            var plan = workspace.BuildSeatingPlan();
            var assignedIds = plan.Assignments.Values.ToHashSet();
            unassignedStudents = [.. workspace.Students.Where(s => !assignedIds.Contains(s.Id))];
        }

        /// <summary>Fisher-Yates 洗牌，原地修改列表。</summary>
        private void FisherYatesShuffle<T> (IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (list[j] , list[i]) = (list[i] , list[j]);
            }
        }

        private void LogCompletion (SeatingWorkspace workspace)
        {
            var remainingSeats = workspace.GetEmptySeats().Count();
            var remainingStudents = workspace.Students.Count(s =>
                !workspace.BuildSeatingPlan().Assignments.ContainsValue(s.Id));
            _logger.LogInformation(
                "RandomFill 策略完成：{Unfilled} 个座位空置，{Unassigned} 名学生未分配" ,
                remainingSeats , remainingStudents);
        }

        /// <inheritdoc />
        public ValidationResult ValidateConfiguration () => new() { IsValid = true };

        // ═══════════════ RandomFillContext 内部实现 ═══════════════

        /// <summary>
        /// <see cref="IRandomFillContext"/> 的内部实现，代理日志调用到 workspace。
        /// </summary>
        private sealed class RandomFillContextImpl (SeatingWorkspace workspace , int rerollCount , int maxRerolls) : IRandomFillContext
        {
            private readonly SeatingWorkspace _workspace = workspace;

            /// <inheritdoc />
            public int RerollCount { get; } = rerollCount;

            /// <inheritdoc />
            public int MaxRerolls { get; } = maxRerolls;

            /// <inheritdoc />
            public void LogWarning (string strategyId , string displayName , string messageKey , params object?[] args)
                => _workspace.LogWarning(strategyId , displayName , messageKey , args);

            /// <inheritdoc />
            public void LogError (string strategyId , string displayName , string messageKey , params object?[] args)
                => _workspace.LogError(strategyId , displayName , messageKey , args);

            /// <inheritdoc />
            public void LogInfo (string strategyId , string displayName , string messageKey , params object?[] args)
                => _workspace.LogInfo(strategyId , displayName , messageKey , args);
        }
    }
}
