namespace SeatFlow.Core.Models
{
    /// <summary>
    /// 座位几何数据标记接口，用于多态存储不同类型的几何位置信息。
    /// </summary>
    public interface ISeatGeometry { }

    /// <summary>
    /// 网格布局中的座位几何位置（行列坐标）。
    /// </summary>
    public class GridPosition : ISeatGeometry
    {
        /// <summary>行号（从 1 开始）。</summary>
        public int Row { get; set; }

        /// <summary>列号（从 1 开始）。</summary>
        public int Column { get; set; }
    }

    /// <summary>
    /// 极坐标布局中的座位几何位置（半径 + 角度）。
    /// </summary>
    public class PolarPosition : ISeatGeometry
    {
        /// <summary>半径距离。</summary>
        public double Radius { get; set; }

        /// <summary>角度（度），0° 为右侧水平方向，逆时针递增。</summary>
        public double AngleDegrees { get; set; }
    }

    /// <summary>
    /// 极坐标布局中按环和角度标识的座位位置，用于标记禁用座位。
    /// 与 <see cref="PolarPosition"/> 不同，它只包含环号和角度，半径由 Builder 在生成时根据环索引计算。
    /// </summary>
    public class PolarRingAngle : ISeatGeometry
    {
        /// <summary>环号（从 1 开始，1 = 最内环）。</summary>
        public int Ring { get; set; }

        /// <summary>角度（度），0° 为右侧水平方向，逆时针递增。</summary>
        public double AngleDegrees { get; set; }
    }
}