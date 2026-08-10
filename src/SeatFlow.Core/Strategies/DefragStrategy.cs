using SeatFlow.Core.Models;
using SeatFlow.Core.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Core.Strategies
{
    /// <summary>
    /// 碎片整理策略（Defrag, Priority=0, 最后执行）。
    /// 在所有策略执行完毕后，将后排无约束的学生前移到前排空位，
    /// 使分散的座位结果趋近前排。不限同列。
    /// </summary>
    /// <remarks>
    /// <b>警告：</b>此策略可能导致前面的策略安排结果（同桌分组、同桌不重复等）失效。
    /// 最优解是使会场座位数和人员数量相近/匹配，以从根本上规避碎片化座位安排结果。
    /// </remarks>
    public class DefragStrategy (
        DefragConfiguration config ,
        ILogger<DefragStrategy>? logger = null) : ISeatingStrategy
    {
        private readonly DefragConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));
        private readonly ILogger<DefragStrategy> _logger = logger ?? NullLogger<DefragStrategy>.Instance;
        private HashSet<string> _constrainedStudentIds = [];

        /// <summary>策略展示名称（与 manifest displayName 一致）。</summary>
        public const string DisplayNameConst = "碎片整理";

        /// <summary>获取策略配置对象。</summary>
        public DefragConfiguration Config => _config;

        /// <summary>使用默认配置创建实例。</summary>
        public DefragStrategy () : this(new DefragConfiguration()) { }

        /// <summary>策略 ID："Defrag"。</summary>
        public string Id { get; } = "Defrag";

        /// <summary>策略名称："Defrag"。</summary>
        public string Name { get; } = "Defrag";

        /// <summary>执行优先级：0（在 RandomFill(1) 之后最后执行）。</summary>
        public int Priority { get; set; } = 0;

        /// <summary>是否启用。</summary>
        public bool IsEnabled { get; set; } = false;

        /// <summary>
        /// 设置约束学生 ID 集合（固定座位学生 + DeskMate 组内学生）。
        /// 由 ApplicationFacade 在管道执行前调用。
        /// </summary>
        public void SetConstrainedStudentIds (HashSet<string> ids)
        {
            _constrainedStudentIds = ids ?? [];
        }

        /// <inheritdoc />
        public Task<StrategyExecutionResult> ExecuteAsync (
            SeatingWorkspace workspace , CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(workspace);
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("Defrag 策略开始执行");

            // ── 步骤1：收集空座位（非 IsFixed），按"靠前度"升序 ──
            var emptySeats = workspace.FindSeats(s => s.IsAvailable && !s.IsFixed)
                .Where(s => GetFrontnessValue(s) < double.MaxValue)
                .OrderBy(GetFrontnessValue)
                .ToList();

            if (emptySeats.Count == 0)
            {
                _logger.LogDebug("Defrag：无可用空座，跳过");
                workspace.LogWarning(Id , DisplayNameConst , "Defrag_NoGaps");
                return Task.FromResult(new StrategyExecutionResult { Success = true });
            }

            // ── 步骤2：收集被占座位（排除 IsFixed），按"靠前度"降序（最后优先） ──
            var occupiedSeats = workspace.FindSeats(s =>
                    !s.IsAvailable && !s.IsFixed && s.OccupantId is not null)
                .Where(s => GetFrontnessValue(s) < double.MaxValue)
                .OrderByDescending(GetFrontnessValue)
                .ToList();

            if (occupiedSeats.Count == 0)
            {
                _logger.LogDebug("Defrag：无被占非固定座位，跳过");
                workspace.LogWarning(Id , DisplayNameConst , "Defrag_NoGaps");
                return Task.FromResult(new StrategyExecutionResult { Success = true });
            }

            // 学生映射
            var studentMap = workspace.Students.ToDictionary(s => s.Id);

            // ── 步骤3：构建候选人池（后排无约束学生） ──
            var candidates = new List<(Seat Seat , Student Student)>();
            foreach (var seat in occupiedSeats)
            {
                if (seat.OccupantId is null) continue;
                if (_constrainedStudentIds.Contains(seat.OccupantId)) continue;
                if (!studentMap.TryGetValue(seat.OccupantId , out var student)) continue;
                candidates.Add((seat , student));
            }

            if (candidates.Count == 0)
            {
                _logger.LogDebug("Defrag：所有后排学生均受约束，跳过");
                workspace.LogWarning(Id , DisplayNameConst , "Defrag_NoEligible");
                return Task.FromResult(new StrategyExecutionResult { Success = true });
            }

            // ── 步骤4：逐空座处理（从前到后），将后排学生前移 ──
            int movedCount = PerformForwardFill(workspace , emptySeats , candidates , cancellationToken);

            // ── 步骤5：日志 ──
            if (movedCount > 0)
            {
                _logger.LogInformation("Defrag 策略完成：已将 {Count} 名学生前移" , movedCount);
                workspace.LogWarning(Id , DisplayNameConst , "Defrag_Moved" , movedCount);
                workspace.LogWarning(Id , DisplayNameConst , "Defrag_EffectivenessNote");
            }
            else
            {
                _logger.LogDebug("Defrag：无前排空位有后方学生可填，跳过");
                workspace.LogWarning(Id , DisplayNameConst , "Defrag_NoGaps");
            }

            return Task.FromResult(new StrategyExecutionResult { Success = true });
        }

        /// <summary>
        /// 逐空座从前到后处理，将后方无约束学生前移填入。
        /// 每个候选人只被移动一次（从池中移除），旧座位不复用。
        /// </summary>
        private int PerformForwardFill (
            SeatingWorkspace workspace ,
            List<Seat> emptySeats ,
            List<(Seat Seat , Student Student)> candidates ,
            CancellationToken ct)
        {
            int moved = 0;

            foreach (var emptySeat in emptySeats)
            {
                if (ct.IsCancellationRequested) break;

                // 跳过已被本轮填充的座位
                if (!emptySeat.IsAvailable) continue;

                // 在候选人中找位于此空座后方的学生
                int candidateIndex = -1;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (IsBehind(candidates[i].Seat , emptySeat))
                    {
                        candidateIndex = i;
                        break;
                    }
                }

                if (candidateIndex < 0)
                    continue; // 此空座后方无可移动学生

                var (oldSeat , student) = candidates[candidateIndex];
                candidates.RemoveAt(candidateIndex);

                // 安全检查：旧座位是否仍被该学生占用
                if (oldSeat.OccupantId != student.Id)
                    continue;

                // 释放旧座位
                oldSeat.OccupantId = null;
                oldSeat.IsAvailable = true;

                // 分配到前排空座
                if (workspace.TryAssignSeat(emptySeat.Id , student.Id , out var error))
                {
                    moved++;
                    _logger.LogDebug(
                        "Defrag：将学生 {Student} 从 {OldSeat} 前移到 {NewSeat}" ,
                        student.Name , oldSeat.Id , emptySeat.Id);
                }
                else
                {
                    // 分配失败，回滚旧座位
                    oldSeat.OccupantId = student.Id;
                    oldSeat.IsAvailable = false;
                    _logger.LogWarning(
                        "Defrag：移动学生 {Student} 到 {Seat} 失败：{Error}，已回滚" ,
                        student.Id , emptySeat.Id , error);
                }
            }

            return moved;
        }

        /// <summary>
        /// 获取座位的"靠前度"数值，用于排序和比较。
        /// 数值越小越靠前。无法确定靠前度的座位返回 double.MaxValue。
        /// </summary>
        internal static double GetFrontnessValue (Seat seat)
        {
            return seat switch
            {
                GridSeat g => g.Row,
                PolarSeat p => p.Ring,
                FreeformSeat f => f.Row ?? double.MaxValue,
                _ => double.MaxValue
            };
        }

        /// <summary>
        /// 判断 candidate 座位是否在 reference 座位的后方。
        /// 不限同列：任何布局类型中位置更靠后的都算后方。
        /// </summary>
        internal static bool IsBehind (Seat candidate , Seat reference)
        {
            if (candidate is GridSeat cg && reference is GridSeat rg)
                return cg.Row > rg.Row;
            if (candidate is PolarSeat cp && reference is PolarSeat rp)
                return cp.Ring > rp.Ring;
            if (candidate is FreeformSeat cf && reference is FreeformSeat rf
                && cf.Row.HasValue && rf.Row.HasValue)
                return cf.Row > rf.Row;
            return false;
        }

        /// <inheritdoc />
        public ValidationResult ValidateConfiguration ()
            => new() { IsValid = true };
    }

    /// <summary>
    /// 碎片整理策略的配置（零参数策略，空壳保持架构一致性）。
    /// </summary>
    public class DefragConfiguration
    {
    }
}
