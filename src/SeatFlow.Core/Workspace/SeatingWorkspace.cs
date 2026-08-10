using SeatFlow.Contracts.Models;
using SeatFlow.Core.Models;
using SeatFlow.Core.Strategies;
using Microsoft.Extensions.Logging;

namespace SeatFlow.Core.Workspace;

/// <summary>
/// 座位安排工作区，是策略执行的核心数据容器。
/// 包含学生列表和座位列表，提供座位分配、查询和快照恢复功能。
/// 策略通过 <see cref="TryAssignSeat"/> 方法修改工作区状态。
/// </summary>
public class SeatingWorkspace : IPluginWorkspace, IFixedSeatCapability
{
    private readonly List<Student> _students = [];
    private readonly List<Seat> _seats = [];
    private readonly List<StrategyMessage> _messages = [];
    private readonly Dictionary<string , HashSet<string>> _strategyCapabilities = [];
    private readonly ILogger<SeatingWorkspace>? _logger;

    /// <summary>学生列表（只读）。</summary>
    public IReadOnlyList<Student> Students => _students;

    /// <summary>
    /// 工作区上下文信息，包含布局定义、轮换周期等元数据。
    /// </summary>
    public class SeatingContext
    {
        /// <summary>当前使用的教室布局定义。</summary>
        public ClassroomLayoutDefinition? Layout { get; set; }

        /// <summary>当前轮换周期（如"第3周"）。</summary>
        public string? RotationCycle { get; set; }

        /// <summary>座位安排生效日期（本地时区）。</summary>
        public DateTime EffectiveDate { get; set; } = DateTime.Now;

        /// <summary>
        /// 策略间共享的后备元数据字典。常用键应提取为 <see cref="SeatingContext"/> 的强类型属性，
        /// 此字典仅用于未规划的动态场景。
        /// </summary>
        public Dictionary<string , object> Metadata { get; set; } = [];
    }

    /// <summary>
    /// 创建工作区，使用指定的学生和座位数据。
    /// </summary>
    /// <param name="students">学生列表。</param>
    /// <param name="seats">座位列表。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public SeatingWorkspace (IEnumerable<Student> students , IEnumerable<Seat> seats , ILogger<SeatingWorkspace>? logger = null)
    {
        _students.AddRange(students ?? Enumerable.Empty<Student>());
        _seats.AddRange(seats ?? Enumerable.Empty<Seat>());
        _logger = logger;
        _logger?.LogDebug("创建工作区：{StudentCount} 名学生，{SeatCount} 个座位" ,
            _students.Count , _seats.Count);
    }

    /// <summary>
    /// 尝试将学生分配到指定座位。
    /// 执行以下验证：
    /// - 座位和学生必须存在。
    /// - 座位必须可用。
    /// - 固定座位只能分配给指定的学生。
    /// - 同一学生不能分配到多个座位。
    /// 分配成功后，自动更新学生的座位历史记录。
    /// </summary>
    /// <param name="seatId">座位 ID。</param>
    /// <param name="studentId">学生 ID。</param>
    /// <param name="error">分配失败时的错误描述。</param>
    /// <param name="updateHistory">
    /// 是否更新学生的座位历史。中间腾挪操作应传入 <c>false</c> 避免污染历史；
    /// 最终分配和快照恢复应保留默认 <c>true</c>。
    /// </param>
    /// <returns>是否分配成功。</returns>
    public bool TryAssignSeat (string seatId , string studentId , out string error , bool updateHistory = true)
    {
        error = string.Empty;
        var seat = _seats.FirstOrDefault(s => s.Id == seatId);
        var student = _students.FirstOrDefault(s => s.Id == studentId);
        if (seat == null) { error = "Seat not found"; _logger?.LogWarning("TryAssignSeat：座位 {SeatId} 不存在" , seatId); return false; }
        if (student == null) { error = "Student not found"; _logger?.LogWarning("TryAssignSeat：学生 {StudentId} 不存在" , studentId); return false; }
        if (!seat.IsAvailable) { error = "Seat not available"; _logger?.LogDebug("TryAssignSeat：座位 {SeatId} 不可用" , seatId); return false; }
        if (seat.IsFixed && seat.OccupantId != studentId) { error = "Seat is fixed by another student"; _logger?.LogWarning("TryAssignSeat：座位 {SeatId} 被固定给其他学生" , seatId); return false; }

        // 防止同一学生分配到多个座位
        var alreadyAssignedSeat = _seats.FirstOrDefault(s => s.OccupantId == studentId && s.Id != seatId);
        if (alreadyAssignedSeat != null)
        {
            error = "Student already assigned to another seat";
            _logger?.LogWarning("TryAssignSeat：学生 {StudentId} 已分配到座位 {OtherSeat}" , studentId , alreadyAssignedSeat.Id);
            return false;
        }

        seat.OccupantId = studentId;
        seat.IsAvailable = false;
        if (updateHistory)
            student.RecentSeatHistory.Add(seatId);
        return true;
    }

    /// <summary>
    /// 获取所有空座位（可用且非固定）。
    /// </summary>
    public IEnumerable<Seat> GetEmptySeats () => _seats.Where(s => s.IsAvailable && !s.IsFixed);

    /// <summary>
    /// 根据条件查找座位。
    /// </summary>
    public IEnumerable<Seat> FindSeats (Func<Seat , bool> predicate) => _seats.Where(predicate);

    /// <summary>
    /// 从当前工作区构建座位安排计划（只读快照）。
    /// </summary>
    public SeatingPlan BuildSeatingPlan ()
    {
        var plan = new SeatingPlan();
        foreach (var seat in _seats)
        {
            if (!string.IsNullOrEmpty(seat.OccupantId))
                plan.Assignments[seat.Id] = seat.OccupantId!;
        }
        return plan;
    }

    /// <summary>
    /// 应用快照中的座位分配，恢复历史状态。
    /// 先清空所有当前分配，再按快照数据重新分配。
    /// 固定座位（<see cref="Seat.IsFixed"/>）不会被清空或修改。
    /// </summary>
    /// <param name="seatAssignments">快照中的座位分配字典（座位 ID → 学生 ID）。</param>
    public void ApplySnapshotAssignments (Dictionary<string , string> seatAssignments)
    {
        _logger?.LogInformation("ApplySnapshotAssignments：应用 {Count} 条分配记录" , seatAssignments.Count);

        // 清空所有非固定座位的当前分配
        foreach (var seat in _seats)
        {
            if (!seat.IsFixed)
            {
                seat.OccupantId = null;
                seat.IsAvailable = true;
            }
        }

        var applied = 0;
        // 应用快照中的分配
        foreach (var kv in seatAssignments)
        {
            var seat = _seats.FirstOrDefault(s => s.Id == kv.Key);
            if (seat == null || seat.IsFixed)
                continue;

            var student = _students.FirstOrDefault(st => st.Id == kv.Value);
            if (student != null)
            {
                seat.OccupantId = kv.Value;
                seat.IsAvailable = false;
                student.RecentSeatHistory.Add(kv.Key);
                applied++;
            }
        }
        _logger?.LogInformation("ApplySnapshotAssignments：成功应用 {Applied} 条" , applied);
    }

    /// <inheritdoc />
    /// <summary>策略执行期间收集的消息（警告/错误），供 UI 展示。</summary>
    public IReadOnlyList<StrategyMessage> Messages => _messages;

    /// <inheritdoc />
    public void LogWarning (string strategyId , string displayName , string messageKey , params object?[] args)
    {
        _messages.Add(new StrategyMessage(StrategyMessageSeverity.Warning , strategyId , displayName , messageKey , args));
        _logger?.LogDebug("[StrategyMsg:Warning][{DisplayName}] {MessageKey} → {Detailed}" ,
            displayName , messageKey , ResolveArgsWithNames(args));
    }

    /// <inheritdoc />
    public void LogError (string strategyId , string displayName , string messageKey , params object?[] args)
    {
        _messages.Add(new StrategyMessage(StrategyMessageSeverity.Error , strategyId , displayName , messageKey , args));
        _logger?.LogDebug("[StrategyMsg:Error][{DisplayName}] {MessageKey} → {Detailed}" ,
            displayName , messageKey , ResolveArgsWithNames(args));
    }

    /// <summary>
    /// 记录一条纯信息性消息（非警告/错误），执行结束后展示在 UI 侧栏中。
    /// </summary>
    /// <param name="strategyId">策略内部 ID。</param>
    /// <param name="displayName">策略展示名称。</param>
    /// <param name="messageKey">对应 manifest messages 中的 i18n 键。</param>
    /// <param name="args">string.Format 参数。</param>
    public void LogInfo (string strategyId , string displayName , string messageKey , params object?[] args)
    {
        _messages.Add(new StrategyMessage(StrategyMessageSeverity.Info , strategyId , displayName , messageKey , args));
        _logger?.LogDebug("[StrategyMsg:Info][{DisplayName}] {MessageKey} → {Detailed}" ,
            displayName , messageKey , ResolveArgsWithNames(args));
    }

    /// <summary>
    /// 将 args 中的学生 ID 解析为 "姓名(ID)" 格式，用于日志记录。
    /// 逗号分隔的 ID 列表会被逐个解析。
    /// </summary>
    private List<string> ResolveArgsWithNames (object?[] args)
    {
        var studentMap = _students.ToDictionary(s => s.Id , s => s);
        return args.Select(a =>
        {
            if (a is not string s) return a?.ToString() ?? "null";
            return string.Join(", " , s.Split(',').Select(part =>
            {
                var id = part.Trim();
                return studentMap.TryGetValue(id , out var student) ? $"{student.Name}({id})" : id;
            }));
        }).ToList();
    }

    public IReadOnlyDictionary<string , string> GetAssignments ()
    {
        return BuildSeatingPlan().Assignments;
    }

    IReadOnlyList<IPluginStudent> IPluginWorkspace.Students => Students;

    bool IPluginWorkspace.TryAssignSeat (string seatId , string studentId , out string error)
        => TryAssignSeat(seatId , studentId , out error , updateHistory: true);

    IEnumerable<IPluginSeat> IPluginWorkspace.GetEmptySeats () => GetEmptySeats();

    IEnumerable<IPluginSeat> IPluginWorkspace.FindSeats (Func<IPluginSeat , bool> predicate)
    {
        return FindSeats(seat => predicate(seat));
    }

    // ═══════════════ 能力系统 ═══════════════

    /// <summary>
    /// 注册策略的能力声明。由 ApplicationFacade 在管道执行前从 manifest 读取并调用。
    /// </summary>
    public void RegisterCapabilities (string strategyId , IEnumerable<string> capabilities)
    {
        if (string.IsNullOrEmpty(strategyId)) return;
        _strategyCapabilities[strategyId] = new HashSet<string>(capabilities ?? []);
    }

    /// <summary>检查策略是否声明了指定能力。</summary>
    private bool HasCapability (string strategyId , string capability)
    {
        return _strategyCapabilities.TryGetValue(strategyId , out var caps)
            && caps.Contains(capability);
    }

    bool IPluginWorkspace.TryMarkFixed (string seatId , string? studentId , string strategyId , string displayName , out string error)
        => TryMarkFixedImpl(seatId , studentId , strategyId , displayName , out error);

    bool IFixedSeatCapability.TryMarkFixed (string seatId , string? studentId , string strategyId , string displayName , out string error)
        => TryMarkFixedImpl(seatId , studentId , strategyId , displayName , out error);

    private bool TryMarkFixedImpl (string seatId , string? studentId , string strategyId , string displayName , out string error)
    {
        error = string.Empty;

        // 校验能力声明
        if (!HasCapability(strategyId , Capability.MarkFixedSeat))
        {
            error = $"策略 {displayName} 未声明 MarkFixedSeat 能力，请在 manifest capabilities 中添加";
            _logger?.LogWarning("[{DisplayName}] 尝试调用未声明的能力 {Capability}（策略 {StrategyId}）" ,
                displayName , Capability.MarkFixedSeat , strategyId);
            return false;
        }

        var seat = _seats.FirstOrDefault(s => s.Id == seatId);
        if (seat == null)
        {
            error = $"座位 {seatId} 不存在";
            _logger?.LogWarning("TryMarkFixed：座位 {SeatId} 不存在" , seatId);
            return false;
        }

        // 若指定学生则先分配
        if (!string.IsNullOrEmpty(studentId))
        {
            // 如果座位已被其他人占用则清除
            if (seat.OccupantId != null && seat.OccupantId != studentId)
            {
                seat.OccupantId = null;
                seat.IsAvailable = true;
            }
            if (!TryAssignSeat(seatId , studentId , out var assignErr))
            {
                error = assignErr;
                return false;
            }
        }

        seat.IsFixed = true;
        _logger?.LogInformation("[{DisplayName}] 通过 MarkFixedSeat 能力将座位 {SeatId} 标记为固定（学生 {StudentId}）" ,
            displayName , seatId , studentId ?? "无");
        return true;
    }
}

/// <summary>
/// 座位安排计划，包含座位到学生的分配映射。
/// 由 <see cref="SeatingWorkspace.BuildSeatingPlan"/> 生成，用于导出和快照。
/// </summary>
public class SeatingPlan
{
    /// <summary>座位分配字典，Key 为座位 ID，Value 为学生 ID。</summary>
    public Dictionary<string , string> Assignments { get; set; } = [];
}
