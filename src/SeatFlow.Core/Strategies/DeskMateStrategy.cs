using SeatFlow.Core.Models;
using SeatFlow.Core.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Core.Strategies
{
    /// <summary>
    /// 同桌组策略（依赖策略，在 RandomFill 上下文中执行）。
    /// 当 RandomFill 随机选出 (student, seat) 对时，此策略检查该学生是否有同桌组。
    /// 若有，则尝试将同组学生分配到相邻座位（连携修改）。
    /// 若目标座位没有足够相邻空座，则请求重掷（Reroll）以尝试其他座位。
    /// </summary>
    /// <remarks>
    /// <b>与旧版的关键区别</b>
    /// <para>
    /// 旧版作为独立策略，在 FixedSeat 和 FrontRowRotation 之后执行，此时座位网格已严重碎片化，
    /// 连续座位块几乎不存在，导致成功率极低。新版作为依赖策略嵌入 RandomFill 的分配循环中，
    /// 在随机填充过程中实时检测同桌关系并协调分配，不再依赖预先存在的连续块。
    /// </para>
    /// </remarks>
    public class DeskMateStrategy (DeskMateConfiguration config , ILogger<DeskMateStrategy>? logger = null , Random? random = null) : IDependentSeatingStrategy
    {
        private readonly DeskMateConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));
        private readonly ILogger<DeskMateStrategy> _logger = logger ?? NullLogger<DeskMateStrategy>.Instance;
        private readonly Random _random = random ?? new Random();
        private HashSet<string> _priorAssignedIds = [];

        /// <summary>获取策略配置对象，供 Application 层读取和修改配置参数。</summary>
        public DeskMateConfiguration Config => _config;

        /// <summary>使用默认配置创建实例。</summary>
        public DeskMateStrategy () : this(new DeskMateConfiguration()) { }

        /// <summary>同步会场每桌座位数，用于同桌边界检查。</summary>
        public void SetSeatsPerDesk (int count) => _config.SeatsPerDesk = Math.Max(1 , count);

        /// <summary>策略展示名称（与 manifest displayName 一致）。</summary>
        public const string DisplayNameConst = "同桌分组";

        /// <inheritdoc />
        public string Id { get; } = "DeskMate";

        /// <inheritdoc />
        public string Name { get; } = "DeskMate";

        /// <inheritdoc />
        public string DisplayName => DisplayNameConst;

        /// <inheritdoc />
        public int Priority { get; set; } = 50;

        /// <inheritdoc />
        public bool IsEnabled { get; set; } = true;

        /// <inheritdoc />
        public Task<DependentEvaluationResult> EvaluateAsync (
            SeatingWorkspace workspace ,
            Student student ,
            Seat targetSeat ,
            IRandomFillContext context ,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(workspace);
            ArgumentNullException.ThrowIfNull(student);
            ArgumentNullException.ThrowIfNull(targetSeat);
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();

            var result = Evaluate(workspace , student , targetSeat , context);
            return Task.FromResult(result);
        }

        private DependentEvaluationResult Evaluate (
            SeatingWorkspace workspace ,
            Student student ,
            Seat targetSeat ,
            IRandomFillContext context)
        {
            // 获取当前已分配的学生 ID
            var assignedIds = workspace.BuildSeatingPlan().Assignments.Values.ToHashSet();

            // 查找该学生所属的同桌组
            var group = FindGroupForStudent(student , assignedIds);
            if (group is null)
                return DependentResult.Approve();

            // 从配置中的原始组获取已分配的组员（用于 near-occupied 检测）
            var originalGroup = _config.Groups.FirstOrDefault(g => g.StudentIds.Contains(student.Id));
            var preAssignedMates = originalGroup?.StudentIds
                .Where(id => id != student.Id && assignedIds.Contains(id))
                .ToList() ?? [];

            // 从未过滤的组中获取未分配的组员
            var unassignedMates = group.StudentIds
                .Where(id => id != student.Id && !assignedIds.Contains(id))
                .ToList();

            _logger.LogDebug(
                "DeskMate 评估：学生 {Student}，目标座位 {Seat}，组大小 {GroupSize}，未分配 {Unassigned}，已分配 {PreAssigned}" ,
                student.Name , targetSeat.Id , group.StudentIds.Count , unassignedMates.Count , preAssignedMates.Count);

            // ── 场景1：有组员已被前序策略分配（near-occupied） ──
            if (preAssignedMates.Count > 0)
            {
                return HandleNearOccupied(
                    workspace , student , targetSeat , preAssignedMates , context);
            }

            // ── 场景2：所有组员均未分配（coordinated assignment） ──
            if (unassignedMates.Count > 0)
            {
                return HandleCoordinatedAssignment(
                    workspace , student , targetSeat , unassignedMates , context);
            }

            // ── 场景3：该学生是组内唯一未分配成员（其余均已在之前迭代中分配） ──
            // 这种情况可能发生在之前的 EvaluateAsync 调用已处理了其他组员
            // 直接批准，由 RandomFill 正常分配
            _logger.LogDebug(
                "DeskMate：学生 {Student} 是组内最后未分配成员，批准由 RandomFill 处理" ,
                student.Name);
            return DependentResult.Approve();
        }

        /// <summary>
        /// 处理 near-occupied 场景：部分组员已被前序策略（FixedSeat/FrontRowRotation）分配。
        /// 若 targetSeat 邻接已分配组员的座位 → 批准。
        /// 否则尝试在已分配组员座位旁找空座 → 切换分配（Handled）。
        /// </summary>
        private DependentEvaluationResult HandleNearOccupied (
            SeatingWorkspace workspace ,
            Student student ,
            Seat targetSeat ,
            List<string> preAssignedMates ,
            IRandomFillContext context)
        {
            // 获取已分配组员占用的座位
            var occupiedSeats = workspace.FindSeats(
                s => s.OccupantId is not null && preAssignedMates.Contains(s.OccupantId)).ToList();

            if (occupiedSeats.Count == 0)
            {
                // 理论上不应发生（已分配学生应有座位），容错处理
                _logger.LogWarning("DeskMate：已分配组员未找到座位，批准当前分配");
                context.LogWarning(Id , DisplayNameConst , "DeskMate_OccupiedSeatNotFound");
                return DependentResult.Approve();
            }

            // 检查 targetSeat 是否与任何组员的座位相邻
            bool isAdjacent = occupiedSeats.Any(occ => SeatAdjacencyHelper.AreDeskMates(occ , targetSeat , _config.SeatsPerDesk));
            if (isAdjacent)
            {
                _logger.LogDebug("DeskMate：目标座位 {SeatId} 与已分配组员相邻，批准" , targetSeat.Id);
                return DependentResult.Approve();
            }

            // targetSeat 不相邻——尝试在已分配组员座位周围找空座
            var emptySeats = workspace.GetEmptySeats().ToList();
            var candidates = new List<Seat>();
            foreach (var occSeat in occupiedSeats)
            {
                var nearby = emptySeats.Where(e => SeatAdjacencyHelper.AreDeskMates(occSeat , e , _config.SeatsPerDesk)).ToList();
                candidates.AddRange(nearby);
            }

            if (candidates.Count > 0)
            {
                var chosen = candidates[_random.Next(candidates.Count)];
                if (workspace.TryAssignSeat(chosen.Id , student.Id , out _))
                {
                    _logger.LogInformation("DeskMate：将学生 {Student} 分配到组员邻座 {SeatId}（原提议 {OriginalSeat} 不邻接）" ,
                        student.Name , chosen.Id , targetSeat.Id);
                    workspace.LogWarning(Id , DisplayNameConst , "DeskMate_NearOccupied" , student.Id , chosen.Id);
                    return DependentResult.Handled();
                }
            }

            // 无相邻空座可用，请求重掷
            _logger.LogDebug("DeskMate：学生 {Student} 无法在已分配组员旁找到空座，请求重掷" , student.Name);
            return DependentResult.Reject();
        }

        /// <summary>
        /// 处理协调分配场景：所有组员均未分配。
        /// 逐级尝试：
        /// 1. 相邻空座足够 → 直接分配全部组员
        /// 2. 空座不足 → 尝试腾挪相邻被占座位（非固定、非组内成员）到附近空座
        /// 3. 仍不足 → 部分分配 + 警告（不再 Reject 拆散组）
        /// </summary>
        private DependentEvaluationResult HandleCoordinatedAssignment (
            SeatingWorkspace workspace ,
            Student student ,
            Seat targetSeat ,
            List<string> unassignedMates ,
            IRandomFillContext context)
        {
            int neededAdjacent = unassignedMates.Count;

            // ── 收集相邻空座和被占座 ──
            var allSeatsAround = workspace.FindSeats(s =>
                s.Id != targetSeat.Id && SeatAdjacencyHelper.AreDeskMates(targetSeat , s , _config.SeatsPerDesk)).ToList();
            var adjacentEmpty = allSeatsAround.Where(s => s.IsAvailable && !s.IsFixed).ToList();
            var adjacentOccupied = allSeatsAround
                .Where(s => !s.IsAvailable && !s.IsFixed && s.OccupantId is not null)
                .ToList();

            // 过滤掉已被组内成员占用的座位（不应腾挪自己的同桌）
            adjacentOccupied = [.. adjacentOccupied.Where(s => !unassignedMates.Contains(s.OccupantId!))];

            _logger.LogDebug(
                "DeskMate：相邻空座 {Empty} 个，相邻被占座 {Occupied} 个，需要 {Needed} 个" ,
                adjacentEmpty.Count , adjacentOccupied.Count , neededAdjacent);

            // ── 层级1：相邻空座足够 → 直接分配 ──
            if (adjacentEmpty.Count >= neededAdjacent)
            {
                return AssignMatesToAdjacentSeats(
                    workspace , student , targetSeat , unassignedMates ,
                    adjacentEmpty , context);
            }

            // ── 层级2：空座不足，尝试腾挪被占座 ──
            int shortage = neededAdjacent - adjacentEmpty.Count;
            var freedSeats = new List<Seat>();

            if (adjacentOccupied.Count > 0 && shortage > 0)
            {
                var allEmpty = workspace.GetEmptySeats()
                    .Where(s => s.Id != targetSeat.Id)
                    .ToList();

                // 打乱被占座顺序，避免总是腾挪同一个位置
                Shuffle(adjacentOccupied , _random);

                foreach (var occSeat in adjacentOccupied)
                {
                    if (freedSeats.Count >= shortage) break;
                    if (occSeat.OccupantId is null) continue;

                    // 不腾挪前序策略（FixedSeat/FrontRowRotation）已安置的学生
                    if (_priorAssignedIds.Contains(occSeat.OccupantId))
                    {
                        _logger.LogDebug(
                            "DeskMate：跳过前序策略已安置的学生 {Student}（座位 {Seat}），不腾挪" ,
                            occSeat.OccupantId , occSeat.Id);
                        continue;
                    }

                    // 在附近找空座给占座者（不需要相邻，任何空座都可以）
                    var candidateEmpty = allEmpty
                        .Where(s => s.IsAvailable && !s.IsFixed)
                        .FirstOrDefault(s => !adjacentEmpty.Contains(s) && !freedSeats.Contains(s));

                    if (candidateEmpty is not null
                        && workspace.TryAssignSeat(candidateEmpty.Id , occSeat.OccupantId , out _ , updateHistory: false))
                    {
                        // 腾出被占座，加入可用列表（中间腾挪不更新历史，防止污染 RecentSeatHistory）
                        occSeat.OccupantId = null;
                        occSeat.IsAvailable = true;
                        freedSeats.Add(occSeat);
                        _logger.LogInformation(
                            "DeskMate：腾挪占座者 {OccupantId} 从 {OldSeat} 到 {NewSeat}，释放座位给同桌" ,
                            occSeat.OccupantId ?? "?" , occSeat.Id , candidateEmpty.Id);
                    }
                }
            }

            // 腾挪尝试结束：若需要腾挪但未释放任何座位，记录原因
            if (shortage > 0 && freedSeats.Count == 0)
            {
                _logger.LogDebug(
                    "DeskMate：需要腾挪 {Shortage} 个座位但无可腾挪的候选（前序分配或无非组占座者）" ,
                    shortage);
            }

            // 合并空座和腾出的座位
            var availableSeats = adjacentEmpty.Concat(freedSeats).Distinct().ToList();

            // ── 层级3：分配所有可用的相邻座位 ──
            var result = AssignMatesToAdjacentSeats(
                workspace , student , targetSeat , unassignedMates ,
                availableSeats , context);

            // 如果腾挪后仍然不足，记录警告但不 Reject（部分分配优于拆散）
            if (availableSeats.Count < neededAdjacent && context.RerollCount == 0)
            {
                context.LogWarning(Id , DisplayNameConst , "DeskMate_NotEnoughSeats" ,
                    student.Id , neededAdjacent , availableSeats.Count);
            }

            return result;
        }

        /// <summary>
        /// 将组员分配到相邻座位列表，返回 Handled 结果。
        /// </summary>
        private DependentEvaluationResult AssignMatesToAdjacentSeats (
            SeatingWorkspace workspace ,
            Student student ,
            Seat targetSeat ,
            List<string> unassignedMates ,
            List<Seat> seatPool ,
            IRandomFillContext context)
        {
            // 先分配 student 到 targetSeat
            if (!workspace.TryAssignSeat(targetSeat.Id , student.Id , out var err))
            {
                _logger.LogWarning("DeskMate：分配学生 {Student} 到 {Seat} 失败：{Error}" ,
                    student.Name , targetSeat.Id , err);
                context.LogWarning(Id , DisplayNameConst , "DeskMate_AssignFailed" ,
                    student.Id , targetSeat.Id , err);
                return DependentResult.Approve(); // 退回给 RandomFill 处理
            }

            // 分配组员
            int assignedMates = 0;
            var unassignedIds = new List<string>(unassignedMates);
            Shuffle(unassignedIds , _random);
            Shuffle(seatPool , _random);

            var pool = new List<Seat>(seatPool);
            foreach (var mateId in unassignedIds)
            {
                bool assigned = false;
                for (int i = 0; i < pool.Count; i++)
                {
                    if (pool[i].IsAvailable && workspace.TryAssignSeat(pool[i].Id , mateId , out _))
                    {
                        assigned = true;
                        pool.RemoveAt(i);
                        break;
                    }
                }
                if (assigned) assignedMates++;
            }

            _logger.LogInformation(
                "DeskMate：协调分配完成——学生 {Student} → {Seat}，组员 {Assigned}/{Total} 人" ,
                student.Name , targetSeat.Id , assignedMates , unassignedMates.Count);

            if (assignedMates < unassignedMates.Count)
            {
                context.LogWarning(Id , DisplayNameConst , "DeskMate_PartialAssign" ,
                    student.Id , assignedMates , unassignedMates.Count);
            }

            return DependentResult.Handled();
        }


        /// <summary>
        /// 查找学生所属的同桌组。
        /// 优先从 <see cref="DeskMateConfiguration.Groups"/> 查找，
        /// 其次从 Student.Extensions["DeskMates"] 查找（向后兼容）。
        /// </summary>
        private DeskMateGroup? FindGroupForStudent (Student student , HashSet<string> assignedIds)
        {
            // 先从配置中的组查找
            foreach (var group in _config.Groups)
            {
                if (group.StudentIds.Contains(student.Id))
                {
                    // 过滤掉已分配的学生（但不排除当前学生本身）
                    var activeIds = group.StudentIds
                        .Where(id => id == student.Id || !assignedIds.Contains(id))
                        .ToList();
                    if (activeIds.Count >= 1)
                        return new DeskMateGroup { GroupId = group.GroupId , StudentIds = activeIds };
                }
            }

            // 从 Extensions["DeskMates"] 查找（向后兼容）
            if (student.Extensions.TryGet<List<string>>("DeskMates" , out var mates) && mates is { Count: > 0 })
            {
                var groupIds = new List<string> { student.Id };
                foreach (var mate in mates)
                {
                    if (!assignedIds.Contains(mate) && !groupIds.Contains(mate))
                        groupIds.Add(mate);
                }
                if (groupIds.Count >= 2)
                    return new DeskMateGroup { StudentIds = groupIds };
            }

            return null;
        }

        /// <summary>
        /// Fisher-Yates 洗牌算法。
        /// </summary>
        private static void Shuffle<T> (IList<T> list , Random random)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (list[j] , list[i]) = (list[i] , list[j]);
            }
        }

        /// <summary>
        /// 验证配置：Groups 不能为 null，每个组至少包含 2 名学生。
        /// </summary>
        public ValidationResult ValidateConfiguration ()
        {
            if (_config.Groups == null)
            {
                return new ValidationResult { IsValid = false , Error = "Groups cannot be null." };
            }

            foreach (var group in _config.Groups)
            {
                if (group.StudentIds == null || group.StudentIds.Count < 2)
                {
                    return new ValidationResult { IsValid = false , Error = $"Group {group.GroupId} must have at least 2 students." };
                }
            }

            return new ValidationResult { IsValid = true };
        }

        /// <inheritdoc />
        public void SetPriorAssignedStudentIds (HashSet<string> ids) => _priorAssignedIds = ids;

        /// <inheritdoc />
        public HashSet<string> GetConstrainedStudentIds ()
        {
            var ids = new HashSet<string>();
            foreach (var g in _config.Groups)
            {
                foreach (var sid in g.StudentIds)
                    ids.Add(sid);
            }
            return ids;
        }
    }

    /// <summary>
    /// 同桌组策略的配置，定义同桌组和分配偏好。
    /// </summary>
    public class DeskMateConfiguration
    {
        /// <summary>同桌组列表。</summary>
        public List<DeskMateGroup> Groups { get; set; } = [];

        /// <summary>每桌座位数（来自会场 GridLayoutMetadata.SeatsPerDesk）。大于 1 时检查同桌边界。</summary>
        public int SeatsPerDesk { get; set; } = 2;
    }

    /// <summary>
    /// 同桌组，包含一组需要坐在一起的学生。
    /// </summary>
    public class DeskMateGroup
    {
        /// <summary>组唯一标识符。</summary>
        public string GroupId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>组内学生 ID 列表。</summary>
        public List<string> StudentIds { get; set; } = [];
    }
}
