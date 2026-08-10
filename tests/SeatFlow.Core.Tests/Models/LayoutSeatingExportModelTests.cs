namespace SeatFlow.Core.Tests.Models;

public class LayoutSeatingExportModelTests
{
    [Fact]
    public void FromLayout_Grid_Basic_ShouldBuildRows ()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Name = "测试网格" ,
            LayoutType = LayoutType.Grid ,
            Metadata = new GridLayoutMetadata { Rows = 3 , Columns = 4 , HasPodium = false }
        };
        for (int r = 1; r <= 3; r++)
            for (int c = 1; c <= 4; c++)
                layout.Seats.Add(new GridSeat { Row = r , Column = c });

        var model = LayoutSeatingExportModel.FromLayout(layout , [] , []);

        model.LayoutType.Should().Be(LayoutType.Grid);
        model.Rows.Should().HaveCount(3);
        model.Rows[0].Cells.Should().HaveCount(4);
    }

    [Fact]
    public void FromLayout_Grid_WithPodium_ShouldAddPodiumRow ()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Name = "有讲台" ,
            LayoutType = LayoutType.Grid ,
            Metadata = new GridLayoutMetadata { Rows = 2 , Columns = 3 , HasPodium = true }
        };
        for (int r = 1; r <= 2; r++)
            for (int c = 1; c <= 3; c++)
                layout.Seats.Add(new GridSeat { Row = r , Column = c });

        var model = LayoutSeatingExportModel.FromLayout(layout , [] , []);

        model.Rows.Should().HaveCount(3); // podium + 2 seat rows
        model.Rows[0].Cells.Any(c => c.IsPodium).Should().BeTrue();
    }

    [Fact]
    public void FromLayout_Grid_WithAisleColumns_ShouldInsertAisleCells ()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Name = "有过道" ,
            LayoutType = LayoutType.Grid ,
            Metadata = new GridLayoutMetadata
            {
                Rows = 2 ,
                Columns = 4 ,
                AisleAfterColumns = [2] ,
                HasPodium = false
            }
        };
        for (int r = 1; r <= 2; r++)
            for (int c = 1; c <= 4; c++)
                layout.Seats.Add(new GridSeat { Row = r , Column = c });

        var model = LayoutSeatingExportModel.FromLayout(layout , [] , []);

        // col plan: C1, C2, aisle, C3, C4 = 5 cells
        model.Rows[0].Cells.Should().HaveCount(5);
        model.Rows[0].Cells[2].IsAisle.Should().BeTrue();
    }

    [Fact]
    public void FromLayout_Grid_WithStudentNames_ShouldUseNames ()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Name = "测试" ,
            LayoutType = LayoutType.Grid ,
            Metadata = new GridLayoutMetadata { Rows = 1 , Columns = 2 , HasPodium = false }
        };
        var s1 = new GridSeat { Row = 1 , Column = 1 , Id = "s1" };
        var s2 = new GridSeat { Row = 1 , Column = 2 , Id = "s2" };
        layout.Seats.Add(s1);
        layout.Seats.Add(s2);

        var model = LayoutSeatingExportModel.FromLayout(layout ,
            new Dictionary<string , string> { { "s1" , "stu1" } } ,
            new Dictionary<string , string> { { "stu1" , "张三" } });

        model.Rows[0].Cells[0].Text.Should().Be("张三");
        model.Rows[0].Cells[1].Text.Should().Be("未分配"); // no assignment
        model.Rows[0].Cells[1].IsUnassigned.Should().BeTrue();
    }

    [Fact]
    public void FromLayout_Polar_ShouldBuildRingRows ()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Name = "极坐标" ,
            LayoutType = LayoutType.Polar ,
            Metadata = new PolarLayoutMetadata
            {
                RingSeatCounts = [4 , 6] ,
                HasPodium = false ,
                RadiusStep = 40
            }
        };
        for (int r = 1; r <= 2; r++)
        {
            int count = r == 1 ? 4 : 6;
            for (int s = 0; s < count; s++)
                layout.Seats.Add(new PolarSeat { Ring = r , AngleDegrees = s * 360.0 / count });
        }

        var model = LayoutSeatingExportModel.FromLayout(layout , [] , []);

        model.Rows.Should().HaveCount(2);
        // ring 1 has 4 seats, max is 6, so 1 padding on each side → 6 cells
        model.Rows[0].Cells.Should().HaveCount(6);
        model.Rows[0].Cells[0].Text.Should().Be("");
        model.Rows[0].Cells[1].IsSeat.Should().BeTrue();
        model.Rows[0].Cells[4].IsSeat.Should().BeTrue();
        model.Rows[0].Cells[5].Text.Should().Be("");
    }

    [Fact]
    public void FromLayout_Freeform_ShouldListPoints ()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Name = "自由点" ,
            LayoutType = LayoutType.Freeform
        };
        layout.Seats.Add(new FreeformSeat { X = 100 , Y = 200 });
        layout.Obstacles.Add(new Obstacle { X = 300 , Y = 100 , Width = 60 , Height = 40 , Type = "Podium" });

        var model = LayoutSeatingExportModel.FromLayout(layout , [] , []);

        model.Rows.Should().HaveCount(2); // seat + podium
        model.Rows[0].Cells[0].IsSeat.Should().BeTrue();
        model.Rows[1].Cells[0].Text.Should().Contain("Podium");
    }

    [Fact]
    public void FromLayout_Grid_DefaultPerspective_PodiumFirst ()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Name = "默认视角" ,
            LayoutType = LayoutType.Grid ,
            Metadata = new GridLayoutMetadata { Rows = 3 , Columns = 2 , HasPodium = true }
        };
        for (int r = 1; r <= 3; r++)
            for (int c = 1; c <= 2; c++)
                layout.Seats.Add(new GridSeat { Row = r , Column = c });

        var model = LayoutSeatingExportModel.FromLayout(layout , [] , []);

        // 学生视角（默认）：讲台在第一行
        model.Rows[0].Cells.Any(c => c.IsPodium).Should().BeTrue();
    }

    [Fact]
    public void FromLayout_Grid_TeacherView_PodiumLast ()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Name = "教师视角" ,
            LayoutType = LayoutType.Grid ,
            Metadata = new GridLayoutMetadata { Rows = 3 , Columns = 2 , HasPodium = true }
        };
        for (int r = 1; r <= 3; r++)
            for (int c = 1; c <= 2; c++)
                layout.Seats.Add(new GridSeat { Row = r , Column = c });

        var model = LayoutSeatingExportModel.FromLayout(layout , [] , []);
        // 教师视角：行前后反转 + 列左右镜像
        model.Rows.Reverse();
        foreach (var row in model.Rows)
            row.Cells.Reverse();

        // 行反转：讲台在最后一行
        model.Rows[^1].Cells.Any(c => c.IsPodium).Should().BeTrue();
        // 列镜像：讲台行 cells 左右颠倒，讲台应移至最左侧
        model.Rows[^1].Cells[0].IsPodium.Should().BeTrue();
    }

    [Fact]
    public void FromLayout_Polar_TeacherView_PodiumLast ()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Name = "极坐标教师视角" ,
            LayoutType = LayoutType.Polar ,
            Metadata = new PolarLayoutMetadata
            {
                RingSeatCounts = [4 , 6] ,
                HasPodium = true ,
                PodiumRadius = 20 ,
                RadiusStep = 40
            }
        };
        for (int r = 1; r <= 2; r++)
        {
            int count = r == 1 ? 4 : 6;
            for (int s = 0; s < count; s++)
                layout.Seats.Add(new PolarSeat { Ring = r , AngleDegrees = s * 360.0 / count });
        }

        var model = LayoutSeatingExportModel.FromLayout(layout , [] , []);
        // 教师视角：行前后反转 + 列左右镜像
        model.Rows.Reverse();
        foreach (var row in model.Rows)
            row.Cells.Reverse();

        // 教师视角：讲台在最后一行（Polar 讲台居中有填充，不做列级断言）
        model.Rows[^1].Cells.Any(c => c.IsPodium).Should().BeTrue();
    }
}
