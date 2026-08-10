using System.Text.Json.Serialization;
using SeatFlow.Contracts.Models;
using SeatFlow.Core.Utilities;

namespace SeatFlow.Core.Models
{
    /// <summary>
    /// 座位布局类型枚举，标识座位的几何排列方式。
    /// </summary>
    public enum SeatType
    {
        /// <summary>网格布局（行列对齐）。</summary>
        Grid,
        /// <summary>极坐标布局（环形/扇形）。</summary>
        Polar,
        /// <summary>自由点布局（任意坐标）。</summary>
        Freeform
    }

    /// <summary>
    /// 座位抽象基类，定义所有座位类型的公共属性。
    /// </summary>
    public abstract class Seat : IPluginSeat
    {
        /// <summary>座位唯一标识符，默认自动生成 GUID。</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>座位类型（Grid / Polar / Freeform），由派生类实现。</summary>
        public abstract SeatType Type { get; }

        /// <summary>逻辑分区标识，用于将座位分组（如"第一组""A区"）。</summary>
        public string LogicalGroup { get; set; } = string.Empty;

        /// <summary>几何数据（如行列、极坐标等），用于渲染和碰撞检测。</summary>
        [JsonIgnore]
        public abstract ISeatGeometry GeometryData { get; }

        /// <summary>座位是否可用（未被占用且未被障碍物阻挡）。</summary>
        public bool IsAvailable { get; set; } = true;

        /// <summary>是否为固定座位（固定座位不受普通策略影响）。</summary>
        public bool IsFixed { get; set; }

        /// <summary>当前占用该座位的学生 ID，null 表示无人占用。</summary>
        public string? OccupantId { get; set; }

        /// <summary>扩展属性挂载点，供插件或自定义逻辑附加额外数据。</summary>
        public AttributeBag Extensions { get; set; } = new();
    }

    /// <summary>
    /// 网格布局中的座位，通过行列坐标定位。
    /// </summary>
    public class GridSeat : Seat
    {
        /// <summary>行号（从 1 开始）。</summary>
        public int Row { get; set; }

        /// <summary>列号（从 1 开始）。</summary>
        public int Column { get; set; }

        /// <summary>座位类型：Grid。</summary>
        public override SeatType Type => SeatType.Grid;

        /// <summary>返回网格坐标几何数据。</summary>
        public override ISeatGeometry GeometryData => new GridPosition { Row = Row , Column = Column };
    }

    /// <summary>
    /// 极坐标布局中的座位，通过半径和角度定位。
    /// </summary>
    public class PolarSeat : Seat
    {
        /// <summary>环号（1-based，1=最内环），用于前排识别等策略。</summary>
        public int Ring { get; set; }

        /// <summary>半径距离（从原点起算）。</summary>
        public double Radius { get; set; }

        /// <summary>角度（度），0° 为右侧水平方向，逆时针递增。</summary>
        public double AngleDegrees { get; set; }

        /// <summary>座位类型：Polar。</summary>
        public override SeatType Type => SeatType.Polar;

        /// <summary>返回极坐标几何数据。</summary>
        public override ISeatGeometry GeometryData => new PolarPosition { Radius = Radius , AngleDegrees = AngleDegrees };
    }
}
