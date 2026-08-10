using SeatFlow.Core.Enums;
using SeatFlow.Core.Models;
using SeatFlow.Core.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Core.Strategies
{
    /// <summary>
    /// 性别限制座位策略（依赖策略，在 RandomFill 上下文中执行）。
    /// 当 RandomFill 随机选出 (student, seat) 对时，此策略检查目标座位是否有性别限制。
    /// 若有且学生性别不匹配，则优先重定向到匹配性别的受限空座（减少无效重掷）；
    /// 无可用匹配空座时请求重掷；重掷次数耗尽后强制分配并记录警告。
    /// </summary>
    /// <remarks>
    /// <b>与其他依赖策略的关系</b>
    /// <para>
    /// 本策略优先级（45）介于 DeskMate（50）和 NoRepeatDeskMate（40）之间。
    /// DeskMate 先处理同桌分组（Handled 时后续依赖策略忽略拒绝），
    /// GenderRestrictedSeat 再检查性别，NoRepeatDeskMate 最后检查历史重复。
    /// </para>
    /// <para>
    /// <b>重定向优化</b>
    /// 性别不匹配时不直接 Reject，而是先查找所有匹配学生性别的受限空座，
    /// 随机选择一个直接分配（Handled），一次性完成不消耗重掷次数。
    /// 仅当无可用的匹配受限空座时才触发 Reject。
    /// </para>
    /// </remarks>
    public class GenderRestrictedSeatStrategy (
        GenderRestrictedSeatConfiguration config ,
        ILogger<GenderRestrictedSeatStrategy>? logger = null ,
        Random? random = null) : IDependentSeatingStrategy
    {
        private readonly GenderRestrictedSeatConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));
        private readonly ILogger<GenderRestrictedSeatStrategy> _logger = logger ?? NullLogger<GenderRestrictedSeatStrategy>.Instance;
        private readonly Random _random = random ?? new Random();
        private HashSet<string> _priorAssignedIds = [];

        /// <summary>策略展示名称（与 manifest displayName 一致）。</summary>
        public const string DisplayNameConst = "性别限制座位";

        /// <inheritdoc />
        public string Id { get; } = "GenderRestrictedSeat";

        /// <inheritdoc />
        public string Name { get; } = "GenderRestrictedSeat";

        /// <inheritdoc />
        public string DisplayName => DisplayNameConst;

        /// <inheritdoc />
        public int Priority { get; set; } = 45;

        /// <inheritdoc />
        public bool IsEnabled { get; set; }

        /// <summary>获取策略配置对象。</summary>
        public GenderRestrictedSeatConfiguration Config => _config;

        /// <summary>使用默认配置创建实例。</summary>
        public GenderRestrictedSeatStrategy () : this(new GenderRestrictedSeatConfiguration()) { }

        /// <summary>
        /// 设置座位性别限制。由 <c>ApplyGenderRestrictionConfig</c> 在管道执行前调用。
        /// </summary>
        public void SetRestrictions (Dictionary<string , Gender> restrictions)
        {
            _config.SeatGenderRestrictions.Clear();
            foreach (var kv in restrictions)
                _config.SeatGenderRestrictions[kv.Key] = kv.Value;
        }

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
            // 1. 固定座位不干涉 — 由 FixedSeatStrategy 全权负责
            if (targetSeat.IsFixed)
            {
                _logger.LogDebug(
                    "GenderRestrictedSeat：座位 {Seat} 为固定座位，跳过检查" , targetSeat.Id);
                return DependentResult.Approve();
            }

            // 2. 检查目标座位是否有性别限制
            if (!_config.SeatGenderRestrictions.TryGetValue(targetSeat.Id , out var requiredGender))
            {
                _logger.LogDebug(
                    "GenderRestrictedSeat：座位 {Seat} 无性别限制，放行" , targetSeat.Id);
                return DependentResult.Approve();
            }

            // 3. 获取学生性别（null → Unknown）
            var studentGender = student.Gender ?? Gender.Unknown;

            // 4. 性别匹配 → 批准
            if (studentGender == requiredGender)
            {
                _logger.LogDebug(
                    "GenderRestrictedSeat：学生 {Student} 性别 {Gender} 匹配座位 {Seat} 限制，批准" ,
                    student.Name , studentGender , targetSeat.Id);
                return DependentResult.Approve();
            }

            // 5. 性别不匹配 → 查找匹配性别的受限空座进行重定向
            _logger.LogDebug(
                "GenderRestrictedSeat：学生 {Student} 性别 {Actual} 不匹配座位 {Seat} 限制 {Required}，尝试重定向" ,
                student.Name , studentGender , targetSeat.Id , requiredGender);

            var matchingEmpty = workspace.GetEmptySeats()
                .Where(s => _config.SeatGenderRestrictions.TryGetValue(s.Id , out var g)
                    && g == studentGender)
                .ToList();

            if (matchingEmpty.Count > 0)
            {
                // 随机选一个匹配性别的受限空座
                var chosen = matchingEmpty[_random.Next(matchingEmpty.Count)];
                if (workspace.TryAssignSeat(chosen.Id , student.Id , out _))
                {
                    _logger.LogInformation(
                        "GenderRestrictedSeat：学生 {Student} 重定向到匹配性别的受限座位 {Seat}（原提议 {Original} 需 {Required}）" ,
                        student.Name , chosen.Id , targetSeat.Id , requiredGender);
                    context.LogWarning(
                        Id , DisplayNameConst , "GenderRestrictedSeat_Redirected" ,
                        student.Id , targetSeat.Id , requiredGender.ToString() , chosen.Id);
                    return DependentResult.Handled();
                }

                _logger.LogWarning(
                    "GenderRestrictedSeat：学生 {Student} 重定向到座位 {Seat} 失败，回退至 Reject" ,
                    student.Name , chosen.Id);
            }

            // 6. 无匹配受限空座 → Reject 或强制分配
            if (context.RerollCount < context.MaxRerolls - 1)
            {
                _logger.LogDebug(
                    "GenderRestrictedSeat：无匹配性别的受限空座，请求重掷");
                return DependentResult.Reject();
            }

            // 7. 重掷耗尽 → 强制分配并警告
            context.LogWarning(
                Id , DisplayNameConst , "GenderRestrictedSeat_Forced" ,
                student.Id , studentGender.ToString() , requiredGender.ToString() , targetSeat.Id);
            _logger.LogInformation(
                "GenderRestrictedSeat：重掷耗尽，学生 {Student}（{Actual}）强制分配到 {Seat}（需 {Required}）" ,
                student.Name , studentGender , targetSeat.Id , requiredGender);
            return DependentResult.Approve();
        }

        /// <inheritdoc />
        public ValidationResult ValidateConfiguration () => new() { IsValid = true };

        /// <inheritdoc />
        public void SetPriorAssignedStudentIds (HashSet<string> ids) => _priorAssignedIds = ids;

        /// <inheritdoc />
        public HashSet<string> GetConstrainedStudentIds () => [];
    }

    /// <summary>
    /// 性别限制座位策略的配置。
    /// </summary>
    public class GenderRestrictedSeatConfiguration
    {
        /// <summary>座位性别限制字典。Key 为座位 ID，Value 为所需的性别。</summary>
        public Dictionary<string , Gender> SeatGenderRestrictions { get; set; } = [];
    }
}
