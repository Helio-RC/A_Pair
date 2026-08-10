namespace SeatFlow.Core.Models
{
    /// <summary>
    /// 教室中的障碍物，用于标记不可放置座位的区域（如柱子、讲台、门等）。
    /// 障碍物以矩形区域表示，由左上角坐标 (X, Y) 和宽高定义。
    /// </summary>
    public class Obstacle
    {
        /// <summary>障碍物唯一标识符。</summary>
        public string Id { get; set; } = System.Guid.NewGuid().ToString();

        /// <summary>障碍物左上角 X 坐标。</summary>
        public double X { get; set; }

        /// <summary>障碍物左上角 Y 坐标。</summary>
        public double Y { get; set; }

        /// <summary>障碍物宽度（向右延伸）。</summary>
        public double Width { get; set; }

        /// <summary>障碍物高度（向下延伸）。</summary>
        public double Height { get; set; }

        /// <summary>障碍物类型描述（如"柱子""讲台""门"）。</summary>
        public string Type { get; set; } = string.Empty;
    }
}