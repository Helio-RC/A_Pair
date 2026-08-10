namespace SeatFlow.Core.Models
{
    /// <summary>
    /// 座位快照，记录某一时刻的完整座位分配状态。
    /// 用于历史版本管理、回滚操作和审计追踪。
    /// </summary>
    public class SeatingSnapshot
    {
        /// <summary>文件格式版本号。</summary>
        public string Version { get; set; } = "1.0";

        /// <summary>快照唯一标识符。</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>快照创建时间（本地时区）。</summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>快照描述（如"第3周排座""手动调整后"）。</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>关联的教室布局 ID。</summary>
        public string LayoutId { get; set; } = string.Empty;

        /// <summary>座位分配字典，Key 为座位 ID，Value 为学生 ID。</summary>
        public Dictionary<string , string> SeatAssignments { get; set; } = [];

        /// <summary>附加元数据，可用于存储版本标签、操作人等信息。</summary>
        public Dictionary<string , object> Metadata { get; set; } = [];
    }
}
