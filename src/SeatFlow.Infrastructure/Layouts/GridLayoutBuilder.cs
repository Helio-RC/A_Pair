using SeatFlow.Core.Models;

namespace SeatFlow.Infrastructure.Layouts
{
    /// <summary>
    /// 网格布局构建器，创建基于行和列的矩形座位排列。
    /// 支持桌面分组、空位跳过、过道配置。
    /// </summary>
    public class GridLayoutBuilder
    {
        /// <summary>
        /// 构建指定行数和列数的传统均匀网格布局（向后兼容入口）。
        /// </summary>
        public static ClassroomLayoutDefinition BuildGrid (int rows , int columns)
        {
            return BuildGrid(new GridLayoutMetadata
            {
                Rows = rows ,
                Columns = columns ,
                SeatsPerDesk = 1 ,
                HorizontalSpacing = 1.0 ,
                VerticalSpacing = 1.0
            });
        }

        /// <summary>
        /// 根据完整元数据构建网格布局。
        /// 支持桌面分组、空位跳过、逻辑组标识。
        /// </summary>
        public static ClassroomLayoutDefinition BuildGrid (GridLayoutMetadata metadata)
        {
            var layout = new ClassroomLayoutDefinition
            {
                LayoutType = LayoutType.Grid ,
                Metadata = metadata
            };

            var emptySet = new HashSet<(int Row , int Col)>(
                (metadata.EmptyPositions ?? []).Select(p => (p.Row , p.Column)));

            int maxRows = metadata.ColumnRowCounts is { Count: > 0 }
                ? metadata.ColumnRowCounts.Max()
                : metadata.Rows;

            for (int r = 1; r <= maxRows; r++)
            {
                for (int c = 1; c <= metadata.Columns; c++)
                {
                    int rowsForCol = (metadata.ColumnRowCounts is { Count: > 0 } && c <= metadata.ColumnRowCounts.Count)
                        ? metadata.ColumnRowCounts[c - 1] : metadata.Rows;

                    if (r > rowsForCol)
                        continue;
                    if (emptySet.Contains((r , c)))
                        continue;

                    var seat = new GridSeat { Row = r , Column = c };

                    // 桌面分组：同桌面座位共享 LogicalGroup
                    if (metadata.SeatsPerDesk > 1)
                    {
                        int deskNumber = ((c - 1) / metadata.SeatsPerDesk) + 1;
                        seat.LogicalGroup = $"Desk_R{r}_D{deskNumber}";
                    }

                    layout.Seats.Add(seat);
                }
            }

            return layout;
        }
    }
}
