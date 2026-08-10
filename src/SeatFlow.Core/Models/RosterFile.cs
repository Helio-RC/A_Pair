namespace SeatFlow.Core.Models
{
    /// <summary>
    /// 人员名单文件 (.roster.json) 的数据结构。
    /// 用于导入/导出学生名单，支持版本管理和附加元数据。
    /// </summary>
    public class RosterFile
    {
        /// <summary>文件格式版本号，用于向后兼容。</summary>
        public string Version { get; set; } = "1.1";

        /// <summary>名单描述（如"2026级计算机1班"）。</summary>
        public string? Description { get; set; }

        /// <summary>学生列表。</summary>
        public List<Student> Students { get; set; } = [];

        /// <summary>附加元数据。</summary>
        public Dictionary<string , object> Metadata { get; set; } = [];

        /// <summary>学生列表内容的 SHA256 哈希（基于按 Id 排序后的 JSON 序列化数组）。</summary>
        public string? StudentsHash { get; set; }
    }
}