using SeatFlow.Core.Workspace;

namespace SeatFlow.Application.Interfaces
{
    /// <summary>
    /// 座位冲突解决器接口，检测并处理策略执行后可能产生的冲突。
    /// 冲突类型包括：重复分配、固定座位不匹配、座位超容等。
    /// 默认实现 <see cref="Services.DefaultConflictResolver"/> 处理常见的冲突场景。
    /// </summary>
    public interface IConflictResolver
    {
        /// <summary>
        /// 检测并解决工作区中的冲突。
        /// </summary>
        /// <param name="workspace">当前工作区。</param>
        /// <returns>解决结果及冲突详情。</returns>
        ConflictResolutionResult Resolve (SeatingWorkspace workspace);
    }

    /// <summary>
    /// 冲突解决结果，包含成功状态、冲突列表和已采取的操作。
    /// </summary>
    public class ConflictResolutionResult
    {
        /// <summary>是否所有冲突已解决。</summary>
        public bool Success { get; set; }

        /// <summary>检测到的冲突列表。</summary>
        public List<ConflictInfo> Conflicts { get; set; } = [];

        /// <summary>为解决冲突而采取的操作描述列表。</summary>
        public List<string> ActionsTaken { get; set; } = [];
    }

    /// <summary>
    /// 冲突信息，描述单个冲突的详情。
    /// </summary>
    public class ConflictInfo
    {
        /// <summary>冲突类型。</summary>
        public ConflictType Type { get; set; }

        /// <summary>关联的座位 ID。</summary>
        public string SeatId { get; set; } = string.Empty;

        /// <summary>关联的学生 ID。</summary>
        public string StudentId { get; set; } = string.Empty;

        /// <summary>冲突描述。</summary>
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// 冲突类型枚举。
    /// </summary>
    public enum ConflictType
    {
        /// <summary>固定座位分配的学生与配置不符。</summary>
        FixedSeatMismatch,
        /// <summary>学生被重复分配到多个座位。</summary>
        DuplicateAssignment,
        /// <summary>座位容量超限。</summary>
        SeatOverCapacity,
        /// <summary>座位无效。</summary>
        InvalidSeat
    }
}