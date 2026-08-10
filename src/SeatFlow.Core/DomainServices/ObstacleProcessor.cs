using SeatFlow.Core.Models;

namespace SeatFlow.Core.DomainServices
{
    /// <summary>
    /// 障碍物处理器，检测并标记与障碍物冲突的座位为不可用。
    /// 在布局构建完成后调用，确保座位不会出现在柱子、讲台等障碍物区域内。
    /// </summary>
    public static class ObstacleProcessor
    {
        /// <summary>
        /// 遍历布局中的所有座位，将位于障碍物区域内的座位标记为不可用。
        /// 使用 <see cref="SeatGeometryHelper.GetPosition"/> 计算座位的物理坐标，
        /// 然后检测该坐标是否落在任意障碍物的矩形区域内。
        /// </summary>
        /// <param name="layout">教室布局定义。</param>
        public static void ApplyObstacles (ClassroomLayoutDefinition layout)
        {
            if (layout.Obstacles?.Count > 0)
            {
                foreach (var seat in layout.Seats)
                {
                    if (!seat.IsAvailable) continue; // 已不可用则跳过

                    var (px , py) = SeatGeometryHelper.GetPosition(seat , layout.Metadata);
                    if (IsInsideAnyObstacle(px , py , layout.Obstacles))
                    {
                        seat.IsAvailable = false;
                    }
                }
            }
        }

        /// <summary>
        /// 判断坐标 (x, y) 是否位于任意障碍物的矩形区域内。
        /// 障碍物矩形由左上角 (X, Y) 和宽高 (Width, Height) 定义。
        /// </summary>
        private static bool IsInsideAnyObstacle (double x , double y , List<Obstacle> obstacles)
        {
            foreach (var obs in obstacles)
            {
                // 假定 Obstacle.X,Y 为左上角，Width 向右，Height 向下
                if (x >= obs.X && x <= obs.X + obs.Width &&
                    y >= obs.Y && y <= obs.Y + obs.Height)
                {
                    return true;
                }
            }
            return false;
        }
    }
}