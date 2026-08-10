namespace SeatFlow.Infrastructure.Tests.Exporters;

public class CsvSeatingExporterTests
{
    [Fact]
    public async Task ExportAsync_ShouldCreateCsvFile ()
    {
        var plan = new SeatingPlan
        {
            Assignments = new Dictionary<string , string>
            {
                { "seat1", "student1" },
                { "seat2", "student2" }
            }
        };
        var exporter = new CsvSeatingExporter();
        var path = Path.GetTempFileName() + ".csv";

        try
        {
            await exporter.ExportAsync(plan , path , CancellationToken.None);
            var content = await File.ReadAllTextAsync(path , TestContext.Current.CancellationToken);
            content.Should().Contain("seat1,student1");
            content.Should().Contain("seat2,student2");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ExportAsync_WithAnonymize_ShouldMaskStudentId ()
    {
        var plan = new SeatingPlan
        {
            Assignments = new Dictionary<string , string>
            {
                { "seat1", "student1" }
            }
        };
        var exporter = new CsvSeatingExporter();
        var path = Path.GetTempFileName() + ".csv";

        try
        {
            var options = new ExportOptions { Format = ExportFormat.Csv , Anonymize = true };
            await exporter.ExportAsync(plan , path , options , CancellationToken.None);
            var content = await File.ReadAllTextAsync(path , TestContext.Current.CancellationToken);
            content.Should().Contain("***");
            content.Should().NotContain("student1");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ExportLayoutAsync_TeacherView_ShouldReverseRowsAndColumns ()
    {
        // 学生视角：讲台（列A,列B,列C）、第一排（张三,李四,王五）、第二排（赵六,孙七,周八）
        var model = new LayoutSeatingExportModel
        {
            LayoutName = "教师视角测试" ,
            LayoutType = LayoutType.Grid ,
            Rows =
            [
                new ExportRow { Cells = [new ExportCell { Text = "" } , new ExportCell { IsPodium = true , Text = "讲台" } , new ExportCell { Text = "" }] } ,
                new ExportRow { Cells = [new ExportCell { IsSeat = true , Text = "张三" } , new ExportCell { IsSeat = true , Text = "李四" } , new ExportCell { IsSeat = true , Text = "王五" }] } ,
                new ExportRow { Cells = [new ExportCell { IsSeat = true , Text = "赵六" } , new ExportCell { IsSeat = true , Text = "孙七" } , new ExportCell { IsSeat = true , Text = "周八" }] }
            ]
        };
        // 模拟 ApplicationFacade 中 TeacherView 时的行列反转
        model.Rows.Reverse();
        foreach (var row in model.Rows)
            row.Cells.Reverse();

        var exporter = new CsvSeatingExporter();
        var path = Path.GetTempFileName() + ".csv";

        try
        {
            var options = new ExportOptions { Format = ExportFormat.Csv , Perspective = LayoutPerspective.TeacherView };
            await exporter.ExportLayoutAsync(model , path , options , CancellationToken.None);
            var content = await File.ReadAllTextAsync(path , TestContext.Current.CancellationToken);

            // 行反转验证：第二排（赵六/孙七/周八）应在第一排（张三/李四/王五）之前
            var row2Index = content.IndexOf("周八" , StringComparison.Ordinal);
            var row1Index = content.IndexOf("王五" , StringComparison.Ordinal);
            row2Index.Should().BeLessThan(row1Index);
            // 讲台应在最后
            var podiumIndex = content.IndexOf("讲台" , StringComparison.Ordinal);
            podiumIndex.Should().BeGreaterThan(row1Index);

            // 列镜像验证：每行内列顺序应左右颠倒
            // 反转后第二排应为 "周八,孙七,赵六"（而非原始 "赵六,孙七,周八"）
            var zhouBaIndex = content.IndexOf("周八" , StringComparison.Ordinal);
            var sunQiIndex = content.IndexOf("孙七" , StringComparison.Ordinal);
            var zhaoLiuIndex = content.IndexOf("赵六" , StringComparison.Ordinal);
            // 周八（原最右）→ 最左，赵六（原最左）→ 最右
            zhouBaIndex.Should().BeLessThan(sunQiIndex);
            sunQiIndex.Should().BeLessThan(zhaoLiuIndex);

            // 第一排同理：应为 "王五,李四,张三"（而非原始 "张三,李四,王五"）
            var wangWuIndex = content.IndexOf("王五" , StringComparison.Ordinal);
            var liSiIndex = content.IndexOf("李四" , StringComparison.Ordinal);
            var zhangSanIndex = content.IndexOf("张三" , StringComparison.Ordinal);
            wangWuIndex.Should().BeLessThan(liSiIndex);
            liSiIndex.Should().BeLessThan(zhangSanIndex);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}