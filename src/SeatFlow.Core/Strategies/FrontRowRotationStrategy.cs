using SeatFlow.Core.DomainServices;
using SeatFlow.Core.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Core.Strategies
{
    /// <summary>
    /// 前排轮换策略（Priority=50，第二执行，在非固定空座中填前排）。
    /// 在 FixedSeat 锁定固定座位后执行，从剩余空座中识别前排座位，
    /// 按需求分数分配给最需要的学生。分数公式与前相同。
    /// </summary>
    public class FrontRowRotationStrategy (FrontRowRotationStrategy.FrontRowRotationConfiguration config , ILogger<FrontRowRotationStrategy>? logger = null , Random? random = null) : ISeatingStrategy
    {
        private readonly FrontRowRotationConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));
        private readonly ILogger<FrontRowRotationStrategy> _logger = logger ?? NullLogger<FrontRowRotationStrategy>.Instance;
        private readonly Random _random = random ?? new Random();

        /// <summary>
        /// 使用默认配置创建实例。
        /// </summary>
        public FrontRowRotationStrategy () : this(new FrontRowRotationConfiguration()) { }

        /// <summary>获取策略配置对象，供 Application 层读取和修改配置参数。</summary>
        public FrontRowRotationConfiguration Config => _config;

        /// <summary>设置前排行数（从布局元数据同步）。</summary>
        public void SetFrontRowCount (int count) => _config.FrontRowCount = Math.Max(1 , count);

        /// <summary>策略展示名称（与 manifest displayName 一致）。</summary>
        public const string DisplayNameConst = "前排轮换";

        /// <summary>策略 ID："FrontRowRotation"。</summary>
        public string Id { get; } = "FrontRowRotation";

        /// <summary>策略名称："FrontRowRotation"。</summary>
        public string Name { get; } = "FrontRowRotation";

        /// <summary>执行优先级：50（第二执行，在非固定空座中填前排）。</summary>
        public int Priority { get; set; } = 50;

        /// <summary>是否启用。</summary>
        public bool IsEnabled { get; set; } = false;

        /// <summary>
        /// 执行前排轮换：
        /// 1. 识别网格布局最前行 / 极坐标布局最内层环（Ring=1 靠近讲台）。
        /// 2. 计算每个未分配学生的前排需求分数。
        /// 3. 按分数从高到低分配前排座位。
        /// </summary>
        public Task<StrategyExecutionResult> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(workspace);
            _logger.LogInformation("FrontRowRotation 策略开始执行：前排数 {FrontRowCount}" ,
                _config.FrontRowCount);

            var emptySeats = workspace.GetEmptySeats().ToList();
            if (emptySeats.Count == 0)
            {
                _logger.LogDebug("FrontRowRotation：无空座位，跳过");
                return Task.FromResult(new StrategyExecutionResult { Success = true });
            }

            // 收集前排座位（复用 SeatGeometryHelper 共享逻辑）
            var frontRowIds = SeatGeometryHelper.IdentifyFrontRowSeats(emptySeats , _config.FrontRowCount);
            var frontRowSeats = emptySeats.Where(s => frontRowIds.Contains(s.Id)).ToList();

            if (frontRowSeats.Count == 0)
            {
                _logger.LogDebug("FrontRowRotation：未识别到前排座位，跳过");
                workspace.LogWarning(Id , DisplayNameConst , "FrontRow_NoSeats");
                return Task.FromResult(new StrategyExecutionResult { Success = true });
            }

            // 获取尚未分配的学生
            var assignedStudentIds = workspace.BuildSeatingPlan().Assignments.Values.ToHashSet();
            var availableStudents = workspace.Students.Where(s => !assignedStudentIds.Contains(s.Id)).ToList();

            // 计算每个学生对前排的"需求度分数"
            var frontSeatIds = new HashSet<string>(frontRowSeats.Select(s => s.Id));

            var studentScores = availableStudents.Select(s =>
            {
                int frontRowHistoryCount = s.RecentSeatHistory.Count(seatId => frontSeatIds.Contains(seatId));

                int score = (s.NeedsFrontRow ? _config.NeedsFrontRowBonus : 0)
            + s.FrontRowPreferenceScore
            - (frontRowHistoryCount * _config.HistoryWeight);
                return (Student: s , Score: score);
            }).OrderByDescending(x => x.Score).ToList();

            int assignCount = Math.Min(frontRowSeats.Count , studentScores.Count);

            // 随机洗牌：同时打乱座位和学生，确保前排学生均匀分布而非偏向一侧
            Shuffle(frontRowSeats , _random);
            var selectedStudents = studentScores.Take(assignCount).Select(x => x.Student).ToList();
            Shuffle(selectedStudents , _random);

            for (int i = 0; i < assignCount && !cancellationToken.IsCancellationRequested; i++)
            {
                workspace.TryAssignSeat(frontRowSeats[i].Id , selectedStudents[i].Id , out _);
            }

            // 若需要前排的学生多于前排座位数，记录警告
            var needFrontRowCount = studentScores.Count(x => x.Student.NeedsFrontRow);
            if (needFrontRowCount > frontRowSeats.Count)
                workspace.LogWarning(Id , DisplayNameConst , "FrontRow_Overflow" ,
                    needFrontRowCount , frontRowSeats.Count);

            _logger.LogInformation("FrontRowRotation 策略完成：{FrontSeats} 个前排座位，分配 {Assigned} 名学生" ,
                frontRowSeats.Count , assignCount);
            return Task.FromResult(new StrategyExecutionResult { Success = true });
        }

        /// <summary>
        /// 验证配置：HistoryWeight 不能为负数。
        /// </summary>
        public ValidationResult ValidateConfiguration ()
        {
            if (_config.HistoryWeight < 0)
            {
                return new ValidationResult { IsValid = false , Error = "HistoryWeight must be non-negative." };
            }
            return new ValidationResult { IsValid = true };
        }

        /// <summary>
        /// Fisher-Yates 洗牌算法，用于随机化学生在前排座位中的分配。
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
        /// 前排轮换策略的配置参数。
        /// </summary>
        public class FrontRowRotationConfiguration
        {
            /// <summary>
            /// 历史座位权重系数，每次坐过前排扣除的分数。
            /// 值越大，轮换公平性越强（避免同一学生连续坐前排）。
            /// </summary>
            public int HistoryWeight { get; set; } = 10;

            /// <summary>
            /// 特殊需求（如视力不佳）的固定加分。
            /// 设置为较大值（默认 1000）可确保有需求的学生优先获得前排座位。
            /// </summary>
            public int NeedsFrontRowBonus { get; set; } = 1000;

            /// <summary>
            /// 前排行数。默认为 1（仅第 1 行），可根据教室布局配置。
            /// </summary>
            public int FrontRowCount { get; set; } = 1;

            /// <summary>
            /// 参考历史快照个数：从最近多少个快照中读取前排座位记录。
            /// 默认 10，范围 1~30。
            /// </summary>
            public int HistoryWindowSize { get; set; } = 10;
        }
    }
}