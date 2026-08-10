namespace SeatFlow.Infrastructure.Tests.LayoutBuilders;

public class GridLayoutBuilderTests
{
    [Fact]
    public void BuildGrid_ShouldCreateCorrectNumberOfSeats ()
    {
        var layout = GridLayoutBuilder.BuildGrid(3 , 4);
        layout.Seats.Should().HaveCount(12);
        layout.LayoutType.Should().Be(LayoutType.Grid);
        var meta = layout.Metadata as GridLayoutMetadata;
        meta.Should().NotBeNull();
        meta!.Rows.Should().Be(3);
        meta.Columns.Should().Be(4);
    }

    [Fact]
    public void BuildGrid_SeatsShouldHaveCorrectPositions ()
    {
        var layout = GridLayoutBuilder.BuildGrid(2 , 2);
        var seats = layout.Seats.Cast<GridSeat>().ToList();
        // 行优先：第1行列1,列2; 第2行列1,列2
        seats[0].Row.Should().Be(1);
        seats[0].Column.Should().Be(1);
        seats[1].Row.Should().Be(1);
        seats[1].Column.Should().Be(2);
        seats[2].Row.Should().Be(2);
        seats[2].Column.Should().Be(1);
        seats[3].Row.Should().Be(2);
        seats[3].Column.Should().Be(2);
    }

    [Fact]
    public void BuildGrid_WithColumnRowCounts_ShouldUsePerColumnRows ()
    {
        var meta = new GridLayoutMetadata
        {
            Rows = 10 ,
            Columns = 3 ,
            SeatsPerDesk = 1 ,
            ColumnRowCounts = new List<int> { 3 , 2 , 1 }
        };
        var layout = GridLayoutBuilder.BuildGrid(meta);
        layout.Seats.Count.Should().Be(6); // 3+2+1
        var seats = layout.Seats.Cast<GridSeat>().ToList();
        seats.Count(s => s.Column == 1).Should().Be(3);
        seats.Count(s => s.Column == 2).Should().Be(2);
        seats.Count(s => s.Column == 3).Should().Be(1);
        // 行优先：r=1(c1,c2,c3), r=2(c1,c2), r=3(c1)
        seats[0].Row.Should().Be(1);
        seats[0].Column.Should().Be(1);
        seats[1].Row.Should().Be(1);
        seats[1].Column.Should().Be(2);
        seats[2].Row.Should().Be(1);
        seats[2].Column.Should().Be(3);
        seats[3].Row.Should().Be(2);
        seats[3].Column.Should().Be(1);
        seats[4].Row.Should().Be(2);
        seats[4].Column.Should().Be(2);
        seats[5].Row.Should().Be(3);
        seats[5].Column.Should().Be(1);
    }

    [Fact]
    public void BuildGrid_WithEmptyPositions_ShouldSkipThem ()
    {
        var meta = new GridLayoutMetadata
        {
            Rows = 3 ,
            Columns = 3 ,
            SeatsPerDesk = 1 ,
            EmptyPositions = new List<GridPosition>
            {
                new() { Row = 1, Column = 2 },
                new() { Row = 2, Column = 3 }
            }
        };
        var layout = GridLayoutBuilder.BuildGrid(meta);
        layout.Seats.Count.Should().Be(7); // 9 - 2
        layout.Seats.Cast<GridSeat>().Any(s => s.Row == 1 && s.Column == 2).Should().BeFalse();
        layout.Seats.Cast<GridSeat>().Any(s => s.Row == 2 && s.Column == 3).Should().BeFalse();
    }

    [Fact]
    public void BuildGrid_WithColumnRowCounts_BackwardCompat_EmptyListUsesRows ()
    {
        var meta = new GridLayoutMetadata
        {
            Rows = 4 ,
            Columns = 3 ,
            SeatsPerDesk = 1 ,
            ColumnRowCounts = [] // 空列表 -> 回退到 Rows
        };
        var layout = GridLayoutBuilder.BuildGrid(meta);
        layout.Seats.Count.Should().Be(12); // 4*3
    }

    [Fact]
    public void BuildGrid_WithAlternatingColumnRowCounts_ShouldBeRowMajor ()
    {
        // 模拟不规则教室：8列，单数列4行，双数列5行
        var meta = new GridLayoutMetadata
        {
            Rows = 5 ,
            Columns = 8 ,
            SeatsPerDesk = 1 ,
            ColumnRowCounts = new List<int> { 4 , 5 , 4 , 5 , 4 , 5 , 4 , 5 }
        };
        var layout = GridLayoutBuilder.BuildGrid(meta);
        layout.Seats.Count.Should().Be(36); // 4*4 + 5*4
        var seats = layout.Seats.Cast<GridSeat>().ToList();

        // 行优先：r=1 填满 8 列，r=2 填满 8 列，...，r=5 仅双数列
        // r=1: c1..c8
        for (int i = 0; i < 8; i++)
        {
            seats[i].Row.Should().Be(1);
            seats[i].Column.Should().Be(i + 1);
        }
        // r=2: c1..c8
        for (int i = 0; i < 8; i++)
        {
            seats[8 + i].Row.Should().Be(2);
            seats[8 + i].Column.Should().Be(i + 1);
        }
        // r=3: c1..c8
        for (int i = 0; i < 8; i++)
        {
            seats[16 + i].Row.Should().Be(3);
            seats[16 + i].Column.Should().Be(i + 1);
        }
        // r=4: c1..c8
        for (int i = 0; i < 8; i++)
        {
            seats[24 + i].Row.Should().Be(4);
            seats[24 + i].Column.Should().Be(i + 1);
        }
        // r=5: 仅 c2, c4, c6, c8
        seats[32].Row.Should().Be(5);
        seats[32].Column.Should().Be(2);
        seats[33].Row.Should().Be(5);
        seats[33].Column.Should().Be(4);
        seats[34].Row.Should().Be(5);
        seats[34].Column.Should().Be(6);
        seats[35].Row.Should().Be(5);
        seats[35].Column.Should().Be(8);
    }
}