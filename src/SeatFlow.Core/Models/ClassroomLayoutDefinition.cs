namespace SeatFlow.Core.Models
{
    /// <summary>
    /// 教室布局定义，实现 <see cref="IClassroomLayout"/> 接口。
    /// 包含座位列表、障碍物列表和布局元数据，是布局构建器的输出结果。
    /// </summary>
    public class ClassroomLayoutDefinition : IClassroomLayout
    {
        /// <summary>布局唯一标识符。</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>布局名称（如"三号阶梯教室"）。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>布局类型枚举。</summary>
        public LayoutType LayoutType { get; set; } = LayoutType.Grid;

        /// <summary>
        /// 布局类型的字符串版本，用于 JSON 序列化兼容。
        /// 反序列化时自动解析为 <see cref="LayoutType"/> 枚举。
        /// </summary>
        public string LayoutTypeString
        {
            get => LayoutType.ToString();
            set => LayoutType = Enum.TryParse<LayoutType>(value , out var result) ? result : LayoutType.Grid;
        }

        /// <summary>座位列表（可变）。</summary>
        public List<Seat> Seats { get; set; } = [];

        /// <summary>障碍物列表，如柱子、讲台等不可用区域。</summary>
        public List<Obstacle> Obstacles { get; set; } = [];

        /// <summary>布局元数据，包含布局特有的参数。</summary>
        public LayoutMetadata Metadata { get; set; } = new GridLayoutMetadata();

        // 显式实现 IClassroomLayout 接口的只读成员
        IReadOnlyList<Seat> IClassroomLayout.Seats => Seats.AsReadOnly();
        IReadOnlyList<Obstacle> IClassroomLayout.Obstacles => Obstacles.AsReadOnly();
        LayoutMetadata IClassroomLayout.Metadata => Metadata;
    }
}