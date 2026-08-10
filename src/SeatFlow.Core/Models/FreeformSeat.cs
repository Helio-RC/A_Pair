namespace SeatFlow.Core.Models
{
    /// <summary>
    /// 自由点布局中的座位，通过平面直角坐标 (X, Y) 定位。
    /// 适用于不规则教室或自定义座位排列。
    /// </summary>
    public class FreeformSeat : Seat
    {
        /// <summary>水平坐标。</summary>
        public double X { get; set; }

        /// <summary>垂直坐标。</summary>
        public double Y { get; set; }

        /// <summary>行号（可选，用于前排轮换策略）。</summary>
        public int? Row { get; set; }

        /// <summary>列号（可选，用于列分组）。</summary>
        public int? Column { get; set; }

        /// <summary>座位类型：Freeform。</summary>
        public override SeatType Type => SeatType.Freeform;

        /// <summary>返回自由点坐标几何数据。</summary>
        public override ISeatGeometry GeometryData => new FreeformPosition { X = X , Y = Y };
    }

    /// <summary>
    /// 自由点布局中的座位几何位置（平面直角坐标）。
    /// </summary>
    public class FreeformPosition : ISeatGeometry
    {
        /// <summary>水平坐标。</summary>
        public double X { get; set; }

        /// <summary>垂直坐标。</summary>
        public double Y { get; set; }
    }
}