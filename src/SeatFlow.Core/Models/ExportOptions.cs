namespace SeatFlow.Core.Models
{
    /// <summary>
    /// 导出选项，控制座位安排结果的导出格式和行为。
    /// </summary>
    public class ExportOptions
    {
        /// <summary>导出格式（Excel / Csv / Pdf / Json）。</summary>
        public ExportFormat Format { get; set; } = ExportFormat.Excel;

        /// <summary>是否匿名化导出（学生姓名/ID 替换为 ***）。</summary>
        public bool Anonymize { get; set; }

        /// <summary>是否包含元数据（如导出时间、座位总数等）。</summary>
        public bool IncludeMetadata { get; set; }

        /// <summary>附加设置字典，供导出器扩展使用。</summary>
        public Dictionary<string , object> AdditionalSettings { get; set; } = [];

        /// <summary>导出视角（学生视角/教师视角）。默认学生视角。</summary>
        public LayoutPerspective Perspective { get; set; } = LayoutPerspective.StudentView;
    }

    /// <summary>
    /// 导出格式枚举。
    /// </summary>
    public enum ExportFormat
    {
        /// <summary>Excel (.xlsx)</summary>
        Excel,
        /// <summary>CSV 逗号分隔值</summary>
        Csv,
        /// <summary>PDF 文档</summary>
        Pdf,
        /// <summary>JSON 格式</summary>
        Json,
        /// <summary>PNG 图片</summary>
        Png
    }
}