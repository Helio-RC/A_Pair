using SeatFlow.Core.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Core.Strategies
{
    /// <summary>
    /// 固定座位策略（Priority=100，最先执行，锁定固定座位）。
    /// 将指定学生强制分配到固定座位，标记 IsFixed=true。
    /// 因为最先执行且通过 IsFixed 锁定，后续策略的 GetEmptySeats() 自动排除这些座位，
    /// 确保固定分配不受其他任何策略影响。适用于特殊需求学生（如残障学生固定前排座位）。
    /// </summary>
    public class FixedSeatStrategy (FixedSeatConfiguration config , ILogger<FixedSeatStrategy>? logger = null) : ISeatingStrategy
    {
        private readonly FixedSeatConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));
        private readonly ILogger<FixedSeatStrategy> _logger = logger ?? NullLogger<FixedSeatStrategy>.Instance;

        /// <summary>
        /// 使用默认空配置创建实例。
        /// </summary>
        public FixedSeatStrategy () : this(new FixedSeatConfiguration()) { }

        /// <summary>获取策略配置对象，供 Application 层读取和修改配置参数。</summary>
        public FixedSeatConfiguration Config => _config;

        /// <summary>策略展示名称（与 manifest displayName 一致）。</summary>
        public const string DisplayNameConst = "固定座位";

        /// <summary>策略 ID："FixedSeat"。</summary>
        public string Id { get; } = "FixedSeat";

        /// <summary>策略名称："FixedSeat"。</summary>
        public string Name { get; } = "FixedSeat";

        /// <summary>执行优先级：100（最先执行，锁定固定座位后 IsFixed=true 自动保护）。</summary>
        public int Priority { get; set; } = 100;

        /// <summary>是否启用。</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 执行固定座位分配：
        /// 1. 根据配置将指定座位标记为 IsFixed 并分配学生。
        /// 2. 确保所有固定座位保持不可用状态（不被其他策略修改）。
        /// </summary>
        public Task<StrategyExecutionResult> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(workspace);
            _logger.LogInformation("FixedSeat 策略开始执行：{AssignmentCount} 个固定分配" ,
                _config.FixedAssignments.Count);

            var assignedCount = 0;
            foreach (var kv in _config.FixedAssignments)
            {
                var seat = workspace.FindSeats(s => s.Id == kv.Key).FirstOrDefault();
                if (seat == null)
                {
                    _logger.LogWarning("FixedSeat：座位 {SeatId} 不存在，跳过" , kv.Key);
                    workspace.LogWarning(Id , DisplayNameConst , "FixedSeat_NotFound" , kv.Key);
                    continue;
                }

                // 先分配再固定，避免 IsFixed 导致分配失败
                if (!string.IsNullOrEmpty(kv.Value))
                {
                    // 如果座位已被其他人占用则清除
                    if (seat.OccupantId != null && seat.OccupantId != kv.Value)
                    {
                        _logger.LogWarning("FixedSeat：座位 {SeatId} 被占用，清除后重新分配" , kv.Key);
                        workspace.LogWarning(Id , DisplayNameConst , "FixedSeat_Occupied" , kv.Key);
                        seat.OccupantId = null;
                        seat.IsAvailable = true;
                    }
                    bool success = workspace.TryAssignSeat(seat.Id , kv.Value , out _);
                    if (success)
                    {
                        ((IFixedSeatCapability)workspace).TryMarkFixed(seat.Id , null , Id , DisplayNameConst , out _);
                        assignedCount++;
                    }
                    else
                    {
                        _logger.LogWarning("FixedSeat：分配座位 {SeatId} 给学生 {StudentId} 失败" , kv.Key , kv.Value);
                        workspace.LogError(Id , DisplayNameConst , "FixedSeat_AssignFailed" , kv.Key , kv.Value);
                    }
                }
                else
                {
                    ((IFixedSeatCapability)workspace).TryMarkFixed(seat.Id , null , Id , DisplayNameConst , out _);
                }
            }

            // 确保管道中此前策略已标记的固定座位（非本策略新设）状态正确。
            // TryAssignSeat 对本策略新分配已处理 IsAvailable，此处仅为已有固定座位做防御性修复。
            var fixedCount = 0;
            foreach (var seat in workspace.FindSeats(s => s.IsFixed))
            {
                if (!string.IsNullOrEmpty(seat.OccupantId))
                {
                    seat.IsAvailable = false;
                    fixedCount++;
                }
            }

            _logger.LogInformation("FixedSeat 策略完成：成功分配 {Assigned} 个，防御性修复 {Fixed} 个" ,
                assignedCount , fixedCount);
            return Task.FromResult(new StrategyExecutionResult { Success = true });
        }

        /// <summary>
        /// 验证配置：FixedAssignments 不能为 null。
        /// </summary>
        public ValidationResult ValidateConfiguration ()
        {
            if (_config.FixedAssignments == null)
            {
                return new ValidationResult { IsValid = false , Error = "FixedAssignments cannot be null." };
            }
            return new ValidationResult { IsValid = true };
        }
    }

    /// <summary>
    /// 固定座位策略的配置，定义座位到学生的固定映射关系。
    /// </summary>
    public class FixedSeatConfiguration
    {
        /// <summary>固定分配字典，Key 为座位 ID，Value 为学生 ID。</summary>
        public Dictionary<string , string> FixedAssignments { get; set; } = [];
    }
}