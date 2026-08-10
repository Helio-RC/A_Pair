using SeatFlow.Core.Models;

namespace SeatFlow.Infrastructure.Layouts
{
    /// <summary>
    /// 自由形式布局构建器，根据指定的坐标点列表创建任意排列的座位布局。
    /// </summary>
    public class FreeformLayoutBuilder
    {
        /// <summary>
        /// 根据坐标点列表构建自由形式布局（简单模式，无分组）。
        /// </summary>
        public static ClassroomLayoutDefinition BuildFreeform (IEnumerable<(double X , double Y)> points)
        {
            var layout = new ClassroomLayoutDefinition
            {
                LayoutType = LayoutType.Freeform ,
                Metadata = new FreeformLayoutMetadata()
            };

            foreach (var (x , y) in points)
            {
                layout.Seats.Add(new FreeformSeat { X = x , Y = y });
            }

            return layout;
        }

        /// <summary>
        /// 根据带元数据的点列表构建自由形式布局（支持分组、行列）。
        /// </summary>
        /// <param name="seatPoints">座位点列表（X/Y/Row/Column/GroupId）。</param>
        /// <param name="obstaclePoints">障碍物点列表（讲台/门的 X/Y/Width/Height/Type）。</param>
        public static ClassroomLayoutDefinition BuildFreeform (
            IEnumerable<(double X , double Y , int? Row , int? Column , int? GroupId)> seatPoints ,
            IEnumerable<(double X , double Y , double Width , double Height , string Type)>? obstaclePoints = null)
        {
            var layout = new ClassroomLayoutDefinition
            {
                LayoutType = LayoutType.Freeform ,
                Metadata = new FreeformLayoutMetadata()
            };

            foreach (var (x , y , row , col , groupId) in seatPoints)
            {
                var seat = new FreeformSeat
                {
                    X = x ,
                    Y = y ,
                    Row = row ,
                    Column = col
                };
                if (groupId.HasValue)
                    seat.LogicalGroup = $"G{groupId.Value}";
                layout.Seats.Add(seat);
            }

            if (obstaclePoints != null)
            {
                foreach (var (x , y , w , h , type) in obstaclePoints)
                {
                    layout.Obstacles.Add(new Obstacle
                    {
                        X = x ,
                        Y = y ,
                        Width = w ,
                        Height = h ,
                        Type = type
                    });
                }
            }

            return layout;
        }
    }
}