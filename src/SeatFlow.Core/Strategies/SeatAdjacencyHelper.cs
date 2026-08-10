using SeatFlow.Core.Models;

namespace SeatFlow.Core.Strategies
{
    /// <summary>
    /// 共享的座位邻接判定逻辑，供 <see cref="DeskMateStrategy"/>、<see cref="NoRepeatDeskMateStrategy"/>
    /// 以及历史加载器使用。
    /// </summary>
    public static class SeatAdjacencyHelper
    {
        /// <summary>极坐标同环角度差阈值（度），用于判定相邻座位。</summary>
        public const double PolarSameRingAngleThreshold = 45.0;

        /// <summary>极坐标跨环角度差容差，角度几乎相同时视为相邻。</summary>
        public const double PolarCrossRingAngleTolerance = 1e-6;

        /// <summary>自由点座位的欧几里得距离阈值，距离在此之内视为相邻。</summary>
        public const double FreeformDistanceThreshold = 1.5;

        /// <summary>
        /// 判断两个座位是否几何相邻（不含桌边界或方向偏好限制）。
        /// 网格座位：同行左右相邻或同列上下相邻。
        /// 极坐标座位：同 LogicalGroup，或同环角度差 ≤45°，或相邻环角度几乎相同。
        /// 自由点座位：同 LogicalGroup，或欧几里得距离 ≤1.5。
        /// 混合类型座位不视为相邻。
        /// </summary>
        public static bool AreSeatsAdjacent (Seat a , Seat b)
        {
            if (a is GridSeat ga && b is GridSeat gb)
            {
                return (ga.Row == gb.Row && Math.Abs(ga.Column - gb.Column) == 1)
                    || (ga.Column == gb.Column && Math.Abs(ga.Row - gb.Row) == 1);
            }

            if (a is PolarSeat pa && b is PolarSeat pb)
            {
                // 优先：LogicalGroup 判定（由布局构建器设置，反映通道划分）
                bool hasGroups = !string.IsNullOrEmpty(pa.LogicalGroup)
                              && !string.IsNullOrEmpty(pb.LogicalGroup);
                if (hasGroups)
                    return pa.LogicalGroup == pb.LogicalGroup;

                // 回退：几何判定（无 LogicalGroup 的旧数据）
                bool sameRing = Math.Abs(pa.Radius - pb.Radius) < 1e-6;
                if (sameRing)
                {
                    double raw = Math.Abs(pa.AngleDegrees - pb.AngleDegrees);
                    double angleDiff = Math.Min(raw , 360.0 - raw);
                    if (angleDiff <= PolarSameRingAngleThreshold) return true;
                }
                else
                {
                    double raw = Math.Abs(pa.AngleDegrees - pb.AngleDegrees);
                    double angleDiff = Math.Min(raw , 360.0 - raw);
                    if (angleDiff < PolarCrossRingAngleTolerance)
                        return true;
                }
                return false;
            }

            if (a is FreeformSeat fa && b is FreeformSeat fb)
            {
                // 优先：LogicalGroup 判定（由布局构建器设置，反映分组划分）
                bool hasGroups = !string.IsNullOrEmpty(fa.LogicalGroup)
                              && !string.IsNullOrEmpty(fb.LogicalGroup);
                if (hasGroups)
                    return fa.LogicalGroup == fb.LogicalGroup;

                // 回退：欧几里得距离判定
                double dx = fa.X - fb.X;
                double dy = fa.Y - fb.Y;
                double distance = Math.Sqrt((dx * dx) + (dy * dy));
                return distance <= FreeformDistanceThreshold;
            }

            return false;
        }

        /// <summary>
        /// 判断两个座位是否属于同一桌（同桌）。
        /// Grid 布局：同行、相邻列、同一 SeatsPerDesk 分组。
        /// 非 Grid 布局委托给 <see cref="AreSeatsAdjacent"/>（LogicalGroup / 几何判定）。
        /// </summary>
        public static bool AreDeskMates (Seat a , Seat b , int seatsPerDesk)
        {
            // 非 Grid 布局使用通用 adjacency 判定（LogicalGroup / 几何距离）
            if (a is not GridSeat ga || b is not GridSeat gb)
                return AreSeatsAdjacent(a , b);

            // 必须同行且相邻列
            if (ga.Row != gb.Row || Math.Abs(ga.Column - gb.Column) != 1)
                return false;

            // 检查同桌边界：同一桌的座位必须在同一个 SeatsPerDesk 分组内
            if (seatsPerDesk > 1)
            {
                int deskA = (ga.Column - 1) / seatsPerDesk;
                int deskB = (gb.Column - 1) / seatsPerDesk;
                if (deskA != deskB)
                    return false;
            }

            return true;
        }
    }
}
