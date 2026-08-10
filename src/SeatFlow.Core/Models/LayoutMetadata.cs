using System.Text.Json.Serialization;

namespace SeatFlow.Core.Models
{
    /// <summary>
    /// 布局元数据基类，每种布局类型有其对应的派生元数据类。
    /// </summary>
    [JsonDerivedType(typeof(GridLayoutMetadata) , "Grid")]
    [JsonDerivedType(typeof(PolarLayoutMetadata) , "Polar")]
    [JsonDerivedType(typeof(FreeformLayoutMetadata) , "Freeform")]
    public class LayoutMetadata { }

    /// <summary>
    /// 网格布局的元数据，定义行列数、间距、桌面配置、过道、教室特征等。
    /// </summary>
    public class GridLayoutMetadata : LayoutMetadata
    {
        /// <summary>总行数。</summary>
        public int Rows { get; set; }

        /// <summary>总列数。</summary>
        public int Columns { get; set; }

        /// <summary>水平间距（相邻列之间的基准距离，像素）。</summary>
        public double HorizontalSpacing { get; set; } = 1.0;

        /// <summary>垂直间距（相邻行之间的基准距离，像素）。</summary>
        public double VerticalSpacing { get; set; } = 1.0;

        /// <summary>原点 X 坐标（第一行第一列的左上角位置）。</summary>
        public double OriginX { get; set; } = 50.0;

        /// <summary>原点 Y 坐标。</summary>
        public double OriginY { get; set; } = 50.0;

        /// <summary>每桌座位数，默认 2。支持 1~6。</summary>
        public int SeatsPerDesk { get; set; } = 2;

        /// <summary>同桌内相邻座位间距（像素），默认 12。</summary>
        public double IntraDeskSpacing { get; set; } = 12.0;

        /// <summary>相邻桌边界间距（像素），默认 40。</summary>
        public double InterDeskSpacing { get; set; } = 40.0;

        /// <summary>哪些列索引后是过道（列从 1 开始计数）。如 {3,6} 表示第 3 列和第 6 列后有过道。</summary>
        public List<int> AisleAfterColumns { get; set; } = [];

        /// <summary>哪些行索引后是过道（行从 1 开始计数）。</summary>
        public List<int> AisleAfterRows { get; set; } = [];

        /// <summary>过道宽度（像素），默认 60。</summary>
        public double AisleWidth { get; set; } = 60.0;

        /// <summary>不放置桌子的网格位置列表。每项包含行列号。</summary>
        public List<GridPosition> EmptyPositions { get; set; } = [];

        /// <summary>每列各自的行数（索引 0 = 第 1 列）。非空时覆盖全局 <see cref="Rows"/> 用于各列的座位生成。</summary>
        public List<int> ColumnRowCounts { get; set; } = [];

        /// <summary>前排的行数（从第 1 行开始计数），默认 1。</summary>
        public int FrontRowCount { get; set; } = 1;

        /// <summary>是否有讲台。</summary>
        public bool HasPodium { get; set; } = true;

        /// <summary>讲台宽度（像素），默认 60。</summary>
        public double PodiumWidth { get; set; } = 40.0;

        /// <summary>讲台高度（像素），默认 40。</summary>
        public double PodiumHeight { get; set; } = 40.0;

        /// <summary>是否有前门。</summary>
        public bool HasFrontDoor { get; set; } = false;
    }

    /// <summary>
    /// 极坐标布局的元数据，定义环数、每环座位数、半径步长、角度范围、通道和教室特征。
    /// </summary>
    public class PolarLayoutMetadata : LayoutMetadata
    {
        /// <summary>环数（旧属性，保留兼容）。若 <see cref="RingSeatCounts"/> 为空则使用 Rings × SeatsPerRing 均匀生成。</summary>
        public int Rings { get; set; } = 1;

        /// <summary>每环的座位数（旧属性，保留兼容）。</summary>
        public int SeatsPerRing { get; set; } = 8;

        /// <summary>相邻环之间的半径增量（像素）。</summary>
        public double RadiusStep { get; set; } = 40;

        /// <summary>起始角度（度），0° 为右侧水平方向，逆时针递增。</summary>
        public double StartAngleDegrees { get; set; } = 0.0;

        /// <summary>结束角度（度），360=全圆，180=半圆，90=1/4圆。</summary>
        public double EndAngleDegrees { get; set; } = 180;

        /// <summary>原点 X 坐标（圆心水平位置，像素）。</summary>
        public double OriginX { get; set; } = 300;

        /// <summary>原点 Y 坐标（圆心垂直位置，像素）。</summary>
        public double OriginY { get; set; } = 200;

        /// <summary>每环座位数列表（索引0=最内环）。非空时优先使用，忽略 Rings/SeatsPerRing。</summary>
        public List<int> RingSeatCounts { get; set; } = [];

        /// <summary>中心是否有讲台。</summary>
        public bool HasPodium { get; set; } = true;

        /// <summary>讲台半径（像素），圆形区域。</summary>
        public double PodiumRadius { get; set; } = 30;

        /// <summary>径向通道所在角度列表（度），通道中心线的角度。</summary>
        public List<double> AisleRadialAngles { get; set; } = [];

        /// <summary>径向通道的角宽度（度），默认 5°。</summary>
        public double AisleRadialWidthDegrees { get; set; } = 5;

        /// <summary>哪些环之后有环间通道（1=第1环后，即第1和第2环之间）。</summary>
        public List<int> AisleCircularAfterRings { get; set; } = [];

        /// <summary>环间通道的径向宽度（像素）。</summary>
        public double AisleCircularWidth { get; set; } = 20;

        /// <summary>前排环数（从最外层环向内计数），默认 1。</summary>
        public int FrontRowCount { get; set; } = 1;

        /// <summary>极坐标布局中禁用的座位列表（按环 + 角度标识）。Builder 生成时会跳过这些座位。</summary>
        public List<PolarRingAngle> EmptyPositions { get; set; } = [];
    }

    /// <summary>
    /// 自由点布局的元数据。
    /// 自由点布局依赖 <see cref="ClassroomLayoutDefinition.Seats"/> 中提供的显式坐标点，
    /// 因此元数据本身无需额外参数。
    /// </summary>
    public class FreeformLayoutMetadata : LayoutMetadata
    {
    }
}
