namespace SeatFlow.Core.Models
{
    /// <summary>
    /// 教室布局的只读接口，定义布局的基本属性和只读访问方式。
    /// 实现类 <see cref="ClassroomLayoutDefinition"/> 提供可变的公共属性。
    /// </summary>
    public interface IClassroomLayout
    {
        /// <summary>布局唯一标识符。</summary>
        string Id { get; }

        /// <summary>布局名称（如"三号阶梯教室"）。</summary>
        string Name { get; }

        /// <summary>布局类型（Grid / Polar / Freeform）。</summary>
        LayoutType LayoutType { get; }

        /// <summary>布局中的座位列表（只读）。</summary>
        IReadOnlyList<Seat> Seats { get; }

        /// <summary>布局中的障碍物列表（只读），如柱子、讲台等。</summary>
        IReadOnlyList<Obstacle> Obstacles { get; }

        /// <summary>布局元数据，包含布局特有的参数（如行列数、间距等）。</summary>
        LayoutMetadata Metadata { get; }
    }

    /// <summary>
    /// 布局类型枚举，标识教室座位的几何排列方式。
    /// </summary>
    public enum LayoutType
    {
        /// <summary>网格布局（行列对齐）。</summary>
        Grid,
        /// <summary>极坐标布局（环形/扇形）。</summary>
        Polar,
        /// <summary>自由点布局（任意坐标）。</summary>
        Freeform
    }
}