using System.Text;

namespace SeatFlow.Infrastructure.Tests.Providers;

public class CsvStudentProviderTests
{
    private static string CreateTempCsv (string content)
    {
        var path = Path.GetTempFileName() + ".csv";
        File.WriteAllText(path , content , Encoding.UTF8);
        return path;
    }

    [Fact]
    public async Task LoadAsync_ValidCsv_ShouldReturnStudents ()
    {
        var csvContent =
            "Name,Height,Gender,NeedsFrontRow\n" +
            "必填,cm,男/女,是/否\n" +
            "Alice,165,Female,false\n" +
            "Bob,180,Male,true";
        var path = CreateTempCsv(csvContent);
        try
        {
            var provider = new CsvStudentProvider();
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
    public async Task LoadAsync_ChineseHeaders_ShouldReturnStudents ()
    {
        var csvContent =
            "姓名,身高,性别,需要前排\n" +
            "必填,厘米,男/女/其他,是/否\n" +
            "Alice,165,女,否\n" +
            "Bob,180,男,是";
        var path = CreateTempCsv(csvContent);
        try
        {
            var provider = new CsvStudentProvider();
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
    public async Task LoadAsync_EmptyFile_ShouldReturnEmptyList ()
    {
        var path = CreateTempCsv("Name,Height\n备注,cm\n");
        try
        {
            var provider = new CsvStudentProvider();
            var students = await provider.LoadAsync(path , CancellationToken.None);
            students.Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_FileNotFound_ShouldReturnEmptyList ()
    {
        var provider = new CsvStudentProvider();
        var students = await provider.LoadAsync("nonexistent.csv" , CancellationToken.None);
        students.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_HeadersAtRow5_ShouldDetectAndParse ()
    {
        // 前 4 行为空，表头在第 5 行（标准格式：第 1 行列名）
        // 但这里是模糊匹配场景——表头在 row 5，没有单独的注释行
        var content = "\n\n\n\n姓名,身高,性别\nAlice,165,女\nBob,180,男";
        var path = CreateTempCsv(content);
        try
        {
            var provider = new CsvStudentProvider();
            var students = await provider.LoadAsync(path , CancellationToken.None);
            students.Should().HaveCount(2);
            students[0].Name.Should().Be("Alice");
            students[0].Height.Should().Be(165);
            students[1].Name.Should().Be("Bob");
            students[1].Gender.Should().Be(Core.Enums.Gender.Male);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_DoubleColumnList_ShouldAggregate ()
    {
        var content = "姓名,性别,姓名,性别\nAlice,女,Bob,男\nCharlie,男,Diana,女";
        var path = CreateTempCsv(content);
        try
        {
            var provider = new CsvStudentProvider();
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

    // ═══════════════════════════════════════════════
    //  GetDimensionsAsync
    // ═══════════════════════════════════════════════

    [Fact]
    public async Task GetDimensionsAsync_ValidCsv_ShouldReturnCorrectDimensions ()
    {
        var content = "Name,Height,Gender\n备注,cm,类型\nAlice,165,Female\nBob,180,Male";
        var path = CreateTempCsv(content);
        try
        {
            var provider = new CsvStudentProvider();
            var (rows , cols) = await provider.GetDimensionsAsync(path , CancellationToken.None);
            rows.Should().Be(4); // header + comment + 2 data rows
            cols.Should().Be(3);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task GetDimensionsAsync_EmptyFile_ShouldReturnZero ()
    {
        var path = CreateTempCsv("");
        try
        {
            var provider = new CsvStudentProvider();
            var (rows , cols) = await provider.GetDimensionsAsync(path , CancellationToken.None);
            rows.Should().Be(0);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task GetDimensionsAsync_FileNotFound_ShouldReturnZero ()
    {
        var provider = new CsvStudentProvider();
        var (rows , cols) = await provider.GetDimensionsAsync("nonexistent.csv" , CancellationToken.None);
        rows.Should().Be(0);
        cols.Should().Be(0);
    }

    // ═══════════════════════════════════════════════
    //  范围限制 LoadAsync
    // ═══════════════════════════════════════════════

    [Fact]
    public async Task LoadAsync_WithMaxRows_ShouldLimitResults ()
    {
        var content = "Name\n\nAlice\nBob\nCharlie\nDiana";
        var path = CreateTempCsv(content);
        try
        {
            var provider = new CsvStudentProvider();
            var students = await provider.LoadAsync(path , maxRows: 3 , maxCols: 0 , CancellationToken.None);
            // maxRows=3: 只读取前 3 行（header + comment + 1 data = Alice only）
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
        var content = "Name,Height,Gender\n\nAlice,165,Female";
        var path = CreateTempCsv(content);
        try
        {
            var provider = new CsvStudentProvider();
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
        var content = "Name\n\nAlice\nBob";
        var path = CreateTempCsv(content);
        try
        {
            var provider = new CsvStudentProvider();
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
