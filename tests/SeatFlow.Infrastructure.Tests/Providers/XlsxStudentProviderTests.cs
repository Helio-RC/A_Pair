using OfficeOpenXml;

namespace SeatFlow.Infrastructure.Tests.Providers;

public class XlsxStudentProviderTests
{
    private static string CreateTempXlsx (string[,] data)
    {
        var path = Path.GetTempFileName() + ".xlsx";
        ExcelPackage.License.SetNonCommercialPersonal("SeatFlow.Test");
        using (var package = new ExcelPackage(new FileInfo(path)))
        {
            var ws = package.Workbook.Worksheets.Add("Students");
            for (int r = 0; r <= data.GetUpperBound(0); r++)
            {
                for (int c = 0; c <= data.GetUpperBound(1); c++)
                {
                    ws.Cells[r + 1 , c + 1].Value = data[r , c];
                }
            }
            package.Save();
        }
        return path;
    }

    [Fact]
    public async Task LoadAsync_ValidXlsx_ShouldReturnStudents ()
    {
        var data = new string[,]
        {
            { "姓名", "身高", "性别", "需要前排" },        // row 1: header
            { "必填", "cm", "男/女", "是/否" },              // row 2: comment (skipped)
            { "Alice", "165", "女", "否" },                   // row 3: data
            { "Bob", "180", "男", "是" }                      // row 4: data
        };
        var path = CreateTempXlsx(data);
        try
        {
            var provider = new XlsxStudentProvider();
            var students = await provider.LoadAsync(path , CancellationToken.None);
            students.Should().HaveCount(2);
            students[0].Name.Should().Be("Alice");
            students[1].NeedsFrontRow.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_EnglishHeaders_ShouldReturnStudents ()
    {
        var data = new string[,]
        {
            { "Name", "Height", "Gender", "NeedsFrontRow" }, // row 1: header
            { "Required", "cm", "Male/Female", "true/false" }, // row 2: comment (skipped)
            { "Alice", "165", "Female", "false" },            // row 3: data
            { "Bob", "180", "Male", "true" }                  // row 4: data
        };
        var path = CreateTempXlsx(data);
        try
        {
            var provider = new XlsxStudentProvider();
            var students = await provider.LoadAsync(path , CancellationToken.None);
            students.Should().HaveCount(2);
            students[0].Name.Should().Be("Alice");
            students[1].NeedsFrontRow.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_EmptySheet_ShouldReturnEmptyList ()
    {
        var data = new string[,] { { "姓名" , "身高" } };
        var path = CreateTempXlsx(data);
        try
        {
            var provider = new XlsxStudentProvider();
            var students = await provider.LoadAsync(path , CancellationToken.None);
            students.Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_HeadersAtRow5_ShouldDetectAndParse ()
    {
        // 前 4 行为空，表头在第 5 行
        var data = new string[7 , 3];
        data[4 , 0] = "姓名";
        data[4 , 1] = "身高";
        data[4 , 2] = "性别";
        data[5 , 0] = "Alice";
        data[5 , 1] = "165";
        data[5 , 2] = "女";
        data[6 , 0] = "Bob";
        data[6 , 1] = "180";
        data[6 , 2] = "男";

        var path = CreateTempXlsxFromGrid(data);
        try
        {
            var provider = new XlsxStudentProvider();
            var students = await provider.LoadAsync(path , CancellationToken.None);
            students.Should().HaveCount(2);
            students[0].Name.Should().Be("Alice");
            students[1].Name.Should().Be("Bob");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_MergedHeaderCell_ShouldExpandAndDetect ()
    {
        var path = Path.GetTempFileName() + ".xlsx";
        ExcelPackage.License.SetNonCommercialPersonal("SeatFlow.Test");
        try
        {
            using (var package = new ExcelPackage(new FileInfo(path)))
            {
                var ws = package.Workbook.Worksheets.Add("Students");
                // 合并 "姓名" 跨 A1:B1
                ws.Cells["A1"].Value = "姓名";
                ws.Cells["A1:B1"].Merge = true;
                ws.Cells[1 , 3].Value = "性别";
                ws.Cells[2 , 1].Value = "Alice";
                ws.Cells[2 , 2].Value = "";
                ws.Cells[2 , 3].Value = "女";
                ws.Cells[3 , 1].Value = "Bob";
                ws.Cells[3 , 3].Value = "男";
                package.Save();
            }

            var provider = new XlsxStudentProvider();
            var students = await provider.LoadAsync(path , CancellationToken.None);
            students.Should().HaveCount(2);
            students[0].Name.Should().Be("Alice");
            students[1].Name.Should().Be("Bob");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_DoubleColumnList_ShouldAggregate ()
    {
        // | 姓名 | 性别 | 姓名 | 性别 |
        var data = new string[3 , 4];
        data[0 , 0] = "姓名";
        data[0 , 1] = "性别";
        data[0 , 2] = "姓名";
        data[0 , 3] = "性别";
        data[1 , 0] = "Alice";
        data[1 , 1] = "女";
        data[1 , 2] = "Bob";
        data[1 , 3] = "男";
        data[2 , 0] = "Charlie";
        data[2 , 1] = "男";
        data[2 , 2] = "Diana";
        data[2 , 3] = "女";

        var path = CreateTempXlsxFromGrid(data);
        try
        {
            var provider = new XlsxStudentProvider();
            var students = await provider.LoadAsync(path , CancellationToken.None);
            students.Should().HaveCount(4);
            students[0].Name.Should().Be("Alice");
            students[1].Name.Should().Be("Bob");
            students[2].Name.Should().Be("Charlie");
            students[3].Name.Should().Be("Diana");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static string CreateTempXlsxFromGrid (string[,] data)
    {
        var path = Path.GetTempFileName() + ".xlsx";
        ExcelPackage.License.SetNonCommercialPersonal("SeatFlow.Test");
        using (var package = new ExcelPackage(new FileInfo(path)))
        {
            var ws = package.Workbook.Worksheets.Add("Students");
            for (int r = 0; r < data.GetLength(0); r++)
            {
                for (int c = 0; c < data.GetLength(1); c++)
                {
                    if (data[r , c] != null)
                        ws.Cells[r + 1 , c + 1].Value = data[r , c];
                }
            }
            package.Save();
        }
        return path;
    }

    // ═══════════════════════════════════════════════
    //  GetDimensionsAsync
    // ═══════════════════════════════════════════════

    [Fact]
    public async Task GetDimensionsAsync_ValidXlsx_ShouldReturnCorrectDimensions ()
    {
        var data = new string[4 , 3];
        data[0 , 0] = "姓名";
        data[0 , 1] = "身高";
        data[0 , 2] = "性别";
        data[1 , 0] = "Alice";
        data[1 , 1] = "165";
        data[1 , 2] = "女";
        data[2 , 0] = "Bob";
        data[2 , 1] = "180";
        data[2 , 2] = "男";
        data[3 , 0] = "Charlie";

        var path = CreateTempXlsxFromGrid(data);
        try
        {
            var provider = new XlsxStudentProvider();
            var (rows , cols) = await provider.GetDimensionsAsync(path , CancellationToken.None);
            rows.Should().Be(4);
            cols.Should().Be(3);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task GetDimensionsAsync_FileNotFound_ShouldReturnZero ()
    {
        var provider = new XlsxStudentProvider();
        var (rows , cols) = await provider.GetDimensionsAsync("nonexistent.xlsx" , CancellationToken.None);
        rows.Should().Be(0);
        cols.Should().Be(0);
    }

    // ═══════════════════════════════════════════════
    //  范围限制 LoadAsync
    // ═══════════════════════════════════════════════

    [Fact]
    public async Task LoadAsync_WithMaxRows_ShouldLimitResults ()
    {
        var data = new string[5 , 3];
        data[0 , 0] = "姓名";
        data[0 , 1] = "身高";
        data[0 , 2] = "性别";
        data[1 , 0] = "备注";
        data[1 , 1] = "cm";
        data[2 , 0] = "Alice";
        data[2 , 1] = "165";
        data[2 , 2] = "女";
        data[3 , 0] = "Bob";
        data[3 , 1] = "180";
        data[3 , 2] = "男";
        data[4 , 0] = "Charlie";

        var path = CreateTempXlsxFromGrid(data);
        try
        {
            var provider = new XlsxStudentProvider();
            // maxRows=3: rows 0-2 only (header + comment + 1 data row)
            var students = await provider.LoadAsync(path , maxRows: 3 , maxCols: 0 , CancellationToken.None);
            students.Should().HaveCount(1);
            students[0].Name.Should().Be("Alice");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_WithMaxCols_ShouldLimitColumns ()
    {
        var data = new string[3 , 3];
        data[0 , 0] = "Name";
        data[0 , 1] = "Height";
        data[0 , 2] = "Gender";
        data[1 , 0] = "备注";
        data[2 , 0] = "Alice";
        data[2 , 1] = "165";
        data[2 , 2] = "Female";

        var path = CreateTempXlsxFromGrid(data);
        try
        {
            var provider = new XlsxStudentProvider();
            var students = await provider.LoadAsync(path , maxRows: 0 , maxCols: 1 , CancellationToken.None);
            students.Should().HaveCount(1);
            students[0].Name.Should().Be("Alice");
            students[0].Height.Should().BeNull(); // Height column excluded
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_ZeroLimits_ShouldBeSameAsNoLimit ()
    {
        var data = new string[4 , 3];
        data[0 , 0] = "姓名";
        data[0 , 1] = "身高";
        data[0 , 2] = "性别";
        data[1 , 0] = "备注";
        data[2 , 0] = "Alice";
        data[2 , 1] = "165";
        data[2 , 2] = "女";
        data[3 , 0] = "Bob";
        data[3 , 1] = "180";
        data[3 , 2] = "男";

        var path = CreateTempXlsxFromGrid(data);
        try
        {
            var provider = new XlsxStudentProvider();
            var unlimited = await provider.LoadAsync(path , CancellationToken.None);
            var zeroLimited = await provider.LoadAsync(path , 0 , 0 , CancellationToken.None);
            zeroLimited.Should().HaveCount(unlimited.Count);
            zeroLimited[0].Name.Should().Be(unlimited[0].Name);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
