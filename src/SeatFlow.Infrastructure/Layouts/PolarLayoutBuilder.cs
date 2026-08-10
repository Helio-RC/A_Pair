using SeatFlow.Core.Models;

namespace SeatFlow.Infrastructure.Layouts
{
    /// <summary>
    /// 极坐标（环形）布局构建器，创建基于半径和角度的环形座位排列。
    /// 支持半圆/扇形角度范围、径向通道、环间通道、每环独立座位数、讲台和 LogicalGroup。
    /// </summary>
    public class PolarLayoutBuilder
    {
        /// <summary>
        /// 根据完整元数据构建极坐标布局。
        /// </summary>
        public static ClassroomLayoutDefinition BuildPolar (PolarLayoutMetadata metadata)
        {
            var layout = new ClassroomLayoutDefinition
            {
                LayoutType = LayoutType.Polar ,
                Metadata = metadata
            };

            // 确定每环座位数：优先使用 RingSeatCounts，否则回退 Rings×SeatsPerRing
            List<int> ringSeatCounts;
            if (metadata.RingSeatCounts is { Count: > 0 })
                ringSeatCounts = metadata.RingSeatCounts;
            else
            {
                int rings = metadata.Rings > 0 ? metadata.Rings : 1;
                int spr = metadata.SeatsPerRing > 0 ? metadata.SeatsPerRing : 8;
                ringSeatCounts = Enumerable.Repeat(spr , rings).ToList();
            }

            // 构建禁用座位集合（按环 + 角度舍入到 2 位小数匹配）
            var emptySet = new HashSet<(int Ring , double AngleDegrees)>(
                (metadata.EmptyPositions ?? []).Select(p => (p.Ring , Math.Round(p.AngleDegrees , 2))));

            // 构建段（segments）：将扫描角度范围按径向通道切分
            var segments = BuildSegments(metadata);

            if (segments.Count == 0)
                return layout;

            double totalAngularRange = segments.Sum(s => s.End - s.Start);
            var circularAisleSet = new HashSet<int>(metadata.AisleCircularAfterRings ?? []);

            for (int ringIdx = 0; ringIdx < ringSeatCounts.Count; ringIdx++)
            {
                int ringNum = ringIdx + 1;
                int totalSeats = ringSeatCounts[ringIdx];
                if (totalSeats <= 0) continue;

                // 计算此环的半径（累加环间通道）
                int aislesBefore = circularAisleSet.Count(r => r < ringNum);
                double radius = (ringNum * metadata.RadiusStep) + (aislesBefore * metadata.AisleCircularWidth);

                // 按各段角宽度比例分配座位
                var segSeatCounts = DistributeSeats(totalSeats , segments , totalAngularRange);

                for (int segIdx = 0; segIdx < segments.Count; segIdx++)
                {
                    var (Start , End) = segments[segIdx];
                    int segSeats = segSeatCounts[segIdx];
                    if (segSeats <= 0) continue;

                    double segRange = End - Start;
                    string logicalGroup = $"R{ringNum}S{segIdx}";

                    for (int j = 0; j < segSeats; j++)
                    {
                        double angle = Start + (segRange * (j + 0.5) / segSeats);
                        // 规范化到 [0, 360)
                        angle = ((angle % 360) + 360) % 360;

                        if (emptySet.Contains((ringNum , Math.Round(angle , 2))))
                            continue;

                        layout.Seats.Add(new PolarSeat
                        {
                            Ring = ringNum ,
                            Radius = radius ,
                            AngleDegrees = angle ,
                            LogicalGroup = logicalGroup
                        });
                    }
                }
            }

            // 讲台 Obstacle
            if (metadata.HasPodium && metadata.PodiumRadius > 0)
            {
                double r = metadata.PodiumRadius;
                layout.Obstacles.Add(new Obstacle
                {
                    X = metadata.OriginX - r ,
                    Y = metadata.OriginY - r ,
                    Width = r * 2 ,
                    Height = r * 2 ,
                    Type = "Podium"
                });
            }

            return layout;
        }

        /// <summary>
        /// 旧签名入口（兼容 ApplicationFacade.BuildSeatsFromRequest）。
        /// </summary>
        public static ClassroomLayoutDefinition BuildPolar (double radiusStep , int rings , int seatsPerRing)
        {
            var meta = new PolarLayoutMetadata
            {
                RadiusStep = radiusStep ,
                Rings = rings ,
                SeatsPerRing = seatsPerRing
            };
            return BuildPolar(meta);
        }

        // ── private helpers ──

        /// <summary>
        /// 将扫描角度范围 [StartAngle, EndAngle] 按径向通道切分为段列表。
        /// </summary>
        private static List<(double Start , double End)> BuildSegments (PolarLayoutMetadata meta)
        {
            var segments = new List<(double Start , double End)>();

            double sweepStart = meta.StartAngleDegrees;
            double sweepEnd = meta.EndAngleDegrees;
            double sweepRange = sweepEnd - sweepStart;

            double halfWidth = meta.AisleRadialWidthDegrees / 2.0;
            var aisleRanges = new List<(double Start , double End)>();

            foreach (double rawAngle in meta.AisleRadialAngles ?? [])
            {
                // 规范化到 [sweepStart, sweepStart + 360)
                double angle = rawAngle;
                while (angle < sweepStart) angle += 360;
                while (angle >= sweepStart + 360) angle -= 360;

                double aStart = angle - halfWidth;
                double aEnd = angle + halfWidth;

                // 裁剪到扫描范围
                if (aEnd > sweepStart && aStart < sweepEnd)
                    aisleRanges.Add((Math.Max(aStart , sweepStart) , Math.Min(aEnd , sweepEnd)));
            }

            aisleRanges.Sort((a , b) => a.Start.CompareTo(b.Start));

            if (aisleRanges.Count == 0)
            {
                segments.Add((sweepStart , sweepEnd));
                return segments;
            }

            bool isFullCircle = Math.Abs(sweepRange - 360) < 1e-9;

            if (isFullCircle)
            {
                // 全圆：N 个通道产生 N 个段（首尾相连）
                for (int i = 0; i < aisleRanges.Count; i++)
                {
                    int next = (i + 1) % aisleRanges.Count;
                    double segStart = aisleRanges[i].End;
                    double segEnd = aisleRanges[next].Start;
                    if (segEnd < segStart) segEnd += 360;

                    if (segEnd - segStart > 1e-9)
                        segments.Add((segStart , segEnd));
                }
            }
            else
            {
                // 部分圆弧：通道前/中/后产生段
                if (aisleRanges[0].Start - sweepStart > 1e-9)
                    segments.Add((sweepStart , aisleRanges[0].Start));

                for (int i = 0; i < aisleRanges.Count - 1; i++)
                {
                    if (aisleRanges[i + 1].Start - aisleRanges[i].End > 1e-9)
                        segments.Add((aisleRanges[i].End , aisleRanges[i + 1].Start));
                }

                if (sweepEnd - aisleRanges[^1].End > 1e-9)
                    segments.Add((aisleRanges[^1].End , sweepEnd));
            }

            return segments;
        }

        /// <summary>
        /// 按各段的角宽度比例分配座位（最大余数法，确保总数精确）。
        /// </summary>
        private static List<int> DistributeSeats (int totalSeats , List<(double Start , double End)> segments , double totalAngularRange)
        {
            var result = new List<int>();
            var remainders = new List<(int Index , double Remainder)>();

            int allocated = 0;
            for (int i = 0; i < segments.Count; i++)
            {
                double segRange = segments[i].End - segments[i].Start;
                double exact = totalSeats * segRange / totalAngularRange;
                int floor = (int)Math.Floor(exact);
                result.Add(floor);
                allocated += floor;
                remainders.Add((i , exact - floor));
            }

            // 分配剩余座位给余数最大的段
            int remaining = totalSeats - allocated;
            foreach (var (idx , _) in remainders.OrderByDescending(r => r.Remainder).Take(remaining))
                result[idx]++;

            return result;
        }
    }
}
