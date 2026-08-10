using System.Text.Json.Nodes;
using SeatFlow.Infrastructure.Migration.Migrators;

namespace SeatFlow.Infrastructure.Tests.Migration;

public class VenueMigratorsTests
{
    private static JsonObject MakeVenueJson (
        string layoutTypeString ,
        List<(int Row , int Col)> seats)
    {
        var seatsArray = new JsonArray();
        foreach (var (r , c) in seats)
        {
            seatsArray.Add(new JsonObject
            {
                ["Type"] = "Grid" ,
                ["row"] = r ,
                ["column"] = c ,
                ["id"] = Guid.NewGuid().ToString() ,
                ["isAvailable"] = true ,
                ["isFixed"] = false
            });
        }

        return new JsonObject
        {
            ["version"] = "1.0" ,
            ["venueId"] = "test" ,
            ["layout"] = new JsonObject
            {
                ["layoutType"] = 0 ,                       // 数字枚举
                ["layoutTypeString"] = layoutTypeString ,  // 字符串形式
                ["seats"] = seatsArray
            }
        };
    }

    [Fact]
    public void Migrate_GridWithColumnMajorSeats_ShouldReorderToRowMajor ()
    {
        // 列主序：c1(r1,r2), c2(r1,r2)
        var columnMajor = new List<(int Row , int Col)>
        {
            (1 , 1) , (2 , 1) ,  // col 1
            (1 , 2) , (2 , 2) ,  // col 2
        };
        var root = MakeVenueJson("Grid" , columnMajor);
        var migrator = new VenueMigrators.Step_1_0_to_1_1();

        var result = migrator.Migrate(root);

        result["version"]!.ToString().Should().Be("1.1");
        var seats = result["layout"]!["seats"]!.AsArray();
        seats.Count.Should().Be(4);
        // 行主序：r1(c1,c2), r2(c1,c2)
        seats[0]!["row"]!.GetValue<int>().Should().Be(1);
        seats[0]!["column"]!.GetValue<int>().Should().Be(1);
        seats[1]!["row"]!.GetValue<int>().Should().Be(1);
        seats[1]!["column"]!.GetValue<int>().Should().Be(2);
        seats[2]!["row"]!.GetValue<int>().Should().Be(2);
        seats[2]!["column"]!.GetValue<int>().Should().Be(1);
        seats[3]!["row"]!.GetValue<int>().Should().Be(2);
        seats[3]!["column"]!.GetValue<int>().Should().Be(2);
    }

    [Fact]
    public void Migrate_GridWithIrregularColumnRowCounts_ShouldReorderToRowMajor ()
    {
        // 模拟：8列，单数列4行，双数列5行 (column-major order)
        var columnMajor = new List<(int Row , int Col)>();
        for (int c = 1; c <= 8; c++)
        {
            int rowsForCol = c % 2 == 1 ? 4 : 5;
            for (int r = 1; r <= rowsForCol; r++)
                columnMajor.Add((r , c));
        }
        var root = MakeVenueJson("Grid" , columnMajor);
        var migrator = new VenueMigrators.Step_1_0_to_1_1();

        var result = migrator.Migrate(root);

        result["version"]!.ToString().Should().Be("1.1");
        var seats = result["layout"]!["seats"]!.AsArray();
        seats.Count.Should().Be(36); // 4x4 + 5x4 = 36

        // 验证行主序：r=1 填满8列
        for (int i = 0; i < 8; i++)
        {
            seats[i]!["row"]!.GetValue<int>().Should().Be(1);
            seats[i]!["column"]!.GetValue<int>().Should().Be(i + 1);
        }
        // r=5 仅双数列
        seats[32]!["row"]!.GetValue<int>().Should().Be(5);
        seats[32]!["column"]!.GetValue<int>().Should().Be(2);
        seats[35]!["column"]!.GetValue<int>().Should().Be(8);
    }

    [Fact]
    public void Migrate_PolarLayout_ShouldUpdateVersionOnly ()
    {
        var root = MakeVenueJson("Polar" , [(1 , 1)]);
        var migrator = new VenueMigrators.Step_1_0_to_1_1();

        var result = migrator.Migrate(root);

        result["version"]!.ToString().Should().Be("1.1");
        // seats unchanged
        result["layout"]!["seats"]!.AsArray().Count.Should().Be(1);
    }

    [Fact]
    public void Migrate_FreeformLayout_ShouldUpdateVersionOnly ()
    {
        var root = MakeVenueJson("Freeform" , [(1 , 1)]);
        var migrator = new VenueMigrators.Step_1_0_to_1_1();

        var result = migrator.Migrate(root);

        result["version"]!.ToString().Should().Be("1.1");
    }

    [Fact]
    public void Migrate_EmptySeats_ShouldNotThrow ()
    {
        var root = MakeVenueJson("Grid" , []);
        var migrator = new VenueMigrators.Step_1_0_to_1_1();

        var result = migrator.Migrate(root);

        result["version"]!.ToString().Should().Be("1.1");
        result["layout"]!["seats"]!.AsArray().Count.Should().Be(0);
    }

    [Fact]
    public void Migrate_SingleSeat_ShouldNotThrow ()
    {
        var root = MakeVenueJson("Grid" , [(1 , 1)]);
        var migrator = new VenueMigrators.Step_1_0_to_1_1();

        var result = migrator.Migrate(root);

        result["version"]!.ToString().Should().Be("1.1");
    }
}
