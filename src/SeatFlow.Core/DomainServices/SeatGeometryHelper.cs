using SeatFlow.Core.Models;

namespace SeatFlow.Core.DomainServices
{
    /// <summary>
    /// 座位几何辅助类，根据座位类型和布局元数据计算座位的物理坐标。
    /// 用于障碍物检测、渲染定位等需要物理坐标的场景。
    /// </summary>
    public static class SeatGeometryHelper
    {
        /// <summary>
        /// 根据座位类型和布局元数据计算座位的物理坐标 (X, Y)。
        /// 支持 GridSeat（网格）、PolarSeat（极坐标）和 FreeformSeat（自由点）三种类型。
        /// </summary>
        /// <param name="seat">座位实例。</param>
        /// <param name="metadata">布局元数据，必须与座位类型匹配。</param>
        /// <returns>物理坐标 (X, Y)。</returns>
        /// <exception cref="ArgumentException">座位类型不支持或元数据不匹配时抛出。</exception>
        public static (double X , double Y) GetPosition (Seat seat , LayoutMetadata metadata)
        {
            return seat switch
            {
                GridSeat grid => GetGridPosition(grid , metadata as GridLayoutMetadata),
                PolarSeat polar => GetPolarPosition(polar , metadata as PolarLayoutMetadata),
                FreeformSeat free => (free.X , free.Y),
                _ => throw new ArgumentException($"Unsupported seat type: {seat.GetType().Name}")
            };
        }

        /// <summary>
        /// 计算网格座位的物理坐标。
        /// 支持桌面分组（IntraDeskSpacing / InterDeskSpacing）、过道（AisleAfterColumns / AisleAfterRows / AisleWidth）。
        /// </summary>
        private static (double X , double Y) GetGridPosition (GridSeat seat , GridLayoutMetadata? gridMeta)
        {
            if (gridMeta == null)
                throw new ArgumentException("Grid seat requires GridLayoutMetadata.");

            double intra = gridMeta.IntraDeskSpacing > 0 ? gridMeta.IntraDeskSpacing : 12.0;
            double inter = gridMeta.InterDeskSpacing > 0 ? gridMeta.InterDeskSpacing : 40.0;
            double aisle = gridMeta.AisleWidth > 0 ? gridMeta.AisleWidth : 60.0;
            var aisleAfterCols = new HashSet<int>(gridMeta.AisleAfterColumns ?? []);
            var aisleAfterRows = new HashSet<int>(gridMeta.AisleAfterRows ?? []);
            int spd = gridMeta.SeatsPerDesk > 0 ? gridMeta.SeatsPerDesk : 1;

            // X 坐标：逐列累加，区分桌内间距和桌间间距
            double x = gridMeta.OriginX;
            for (int c = 1; c < seat.Column; c++)
            {
                if (c % spd == 0)  // 当前是桌子的最后一个座位，后面是桌间/过道间距
                    x += aisleAfterCols.Contains(c) ? aisle : inter;
                else               // 桌内相邻座位间距
                    x += intra;
            }

            // Y 坐标：累加前行产生的间距
            double y = gridMeta.OriginY;
            for (int r = 1; r < seat.Row; r++)
                y += aisleAfterRows.Contains(r) ? aisle : gridMeta.VerticalSpacing;

            return (x , y);
        }

        /// <summary>
        /// 计算极坐标座位的物理坐标。
        /// 公式：X = OriginX + Radius × cos(Angle)
        ///       Y = OriginY + Radius × sin(Angle)
        /// 角度从度转换为弧度进行计算。
        /// </summary>
        private static (double X , double Y) GetPolarPosition (PolarSeat seat , PolarLayoutMetadata? polarMeta)
        {
            if (polarMeta == null)
                throw new ArgumentException("Polar seat requires PolarLayoutMetadata.");

            double rad = seat.AngleDegrees * Math.PI / 180.0;
            double x = polarMeta.OriginX + (seat.Radius * Math.Cos(rad));
            double y = polarMeta.OriginY + (seat.Radius * Math.Sin(rad));
            return (x , y);
        }

        /// <summary>
        /// 从座位集合中识别前排座位 ID 集合。
        /// 被 <see cref="FrontRowRotationStrategy"/>（空座子集）和
        /// <see cref="FrontRowHistoryLoader"/>（全部座位）共用。
        /// </summary>
        /// <param name="seats">座位集合。</param>
        /// <param name="frontRowCount">前排行数。</param>
        /// <returns>前排座位 ID 的集合。</returns>
        public static HashSet<string> IdentifyFrontRowSeats (IEnumerable<Seat> seats , int frontRowCount)
        {
            ArgumentNullException.ThrowIfNull(seats);
            var seatList = seats as IReadOnlyCollection<Seat> ?? seats.ToList();
            var frontRowSeats = new HashSet<string>();

            // Grid 座位：Row 最小的 N 行为前排
            var gridSeats = seatList.OfType<GridSeat>().ToList();
            if (gridSeats.Count > 0)
            {
                int frontRowMin = gridSeats.Min(s => s.Row);
                int frontRowMax = frontRowMin + frontRowCount - 1;
                foreach (var s in gridSeats.Where(s => s.Row >= frontRowMin && s.Row <= frontRowMax))
                    frontRowSeats.Add(s.Id);
            }

            // Polar 座位：Ring=1 为最内环（靠近讲台），即前排
            var polarSeats = seatList.OfType<PolarSeat>().ToList();
            if (polarSeats.Count > 0)
            {
                int frontRingMax = frontRowCount;
                foreach (var s in polarSeats.Where(s => s.Ring <= frontRingMax))
                    frontRowSeats.Add(s.Id);
            }

            // Freeform 座位：有 Row 属性的座位中，Row 最小的 N 行为前排
            var freeformSeats = seatList.OfType<FreeformSeat>().Where(s => s.Row.HasValue).ToList();
            if (freeformSeats.Count > 0)
            {
                int frontRowMin = freeformSeats.Min(s => s.Row!.Value);
                int frontRowMax = frontRowMin + frontRowCount - 1;
                foreach (var s in freeformSeats.Where(s => s.Row >= frontRowMin && s.Row <= frontRowMax))
                    frontRowSeats.Add(s.Id);
            }

            return frontRowSeats;
        }
    }
}