namespace SeatFlow.Infrastructure.Tests.Providers;

public class FuzzyColumnMatcherTests
{
    // ═══════════════════════════════════════════════
    //  标准模板检测
    // ═══════════════════════════════════════════════

    [Fact]
    public void TryParse_StandardTemplate_ShouldReturnIsStandardTemplateTrue ()
    {
        // 所有字段都在第 0 行 → 标准模板
        var grid = new string?[3 , 4];
        grid[0 , 0] = "姓名";
        grid[0 , 1] = "身高";
        grid[0 , 2] = "性别";
        grid[0 , 3] = "需要前排";
        grid[1 , 0] = "Alice";
        grid[1 , 1] = "165";
        grid[1 , 2] = "女";
        grid[2 , 0] = "Bob";
        grid[2 , 1] = "180";
        grid[2 , 2] = "男";

        var result = FuzzyColumnMatcher.TryParse(grid , 3 , 4);

        // 快速路径——归入标准模板
        result.IsStandardTemplate.Should().BeTrue();
        result.HasNameField.Should().BeTrue();
    }

    [Fact]
    public void TryParse_StandardTemplateEnglishHeaders_ShouldDetect ()
    {
        var grid = new string?[3 , 4];
        grid[0 , 0] = "Name";
        grid[0 , 1] = "Height";
        grid[0 , 2] = "Gender";
        grid[0 , 3] = "NeedsFrontRow";
        grid[1 , 0] = "Alice";

        var result = FuzzyColumnMatcher.TryParse(grid , 2 , 4);

        result.IsStandardTemplate.Should().BeTrue();
        result.HasNameField.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════
    //  列式布局 — 非标准表头位置
    // ═══════════════════════════════════════════════

    [Fact]
    public void TryParse_HeadersAtRow3_ShouldDetectAndParse ()
    {
        // 前 3 行为空/杂项，表头在第 3 行（0-indexed）
        var grid = new string?[7 , 4];
        grid[3 , 0] = "姓名";
        grid[3 , 1] = "身高";
        grid[3 , 2] = "性别";
        // col 3 无映射字段
        grid[4 , 0] = "Alice";
        grid[4 , 1] = "165";
        grid[4 , 2] = "女";
        grid[5 , 0] = "Bob";
        grid[5 , 1] = "180";
        grid[5 , 2] = "男";

        var result = FuzzyColumnMatcher.TryParse(grid , 7 , 4);

        result.IsStandardTemplate.Should().BeFalse();
        result.HasNameField.Should().BeTrue();
        result.Students.Should().NotBeNull();
        result.Students.Should().HaveCount(2);
        result.Students![0].Name.Should().Be("Alice");
        result.Students[0].Height.Should().Be(165);
        result.Students[1].Name.Should().Be("Bob");
        result.Students[1].Gender.Should().Be(Core.Enums.Gender.Male);
    }

    [Fact]
    public void TryParse_HeadersAtRow5_ShouldSkipHeaderRows ()
    {
        var grid = new string?[8 , 3];
        grid[5 , 0] = "Name";
        grid[5 , 1] = "Gender";
        grid[6 , 0] = "Charlie";
        grid[6 , 1] = "Male";
        grid[7 , 0] = "Diana";
        grid[7 , 1] = "Female";

        var result = FuzzyColumnMatcher.TryParse(grid , 8 , 3);

        result.Students.Should().NotBeNull();
        result.Students.Should().HaveCount(2);
        result.Students![0].Name.Should().Be("Charlie");
        result.Students[1].Name.Should().Be("Diana");
    }

    // ═══════════════════════════════════════════════
    //  大小写与空格容错
    // ═══════════════════════════════════════════════

    [Fact]
    public void TryParse_CaseInsensitiveHeaders_ShouldMatch ()
    {
        var grid = new string?[2 , 3];
        grid[0 , 0] = "NAME";
        grid[0 , 1] = "gender";
        grid[0 , 2] = "HEIGHT";
        grid[1 , 0] = "Eve";

        var result = FuzzyColumnMatcher.TryParse(grid , 2 , 3);

        // 所有字段在 row 0 → 标准模板信号
        result.IsStandardTemplate.Should().BeTrue();
        result.HasNameField.Should().BeTrue();                     // 大小写不敏感匹配
    }

    [Fact]
    public void TryParse_TrimmedHeaders_ShouldMatch ()
    {
        var grid = new string?[2 , 2];
        grid[0 , 0] = "  姓名  ";
        grid[0 , 1] = " 性别 ";
        grid[1 , 0] = "Frank";

        var result = FuzzyColumnMatcher.TryParse(grid , 2 , 2);

        // 所有字段在 row 0 → 标准模板信号
        result.IsStandardTemplate.Should().BeTrue();
        result.HasNameField.Should().BeTrue();                     // 空格容错匹配
    }

    // ═══════════════════════════════════════════════
    //  双列名单聚合
    // ═══════════════════════════════════════════════

    [Fact]
    public void TryParse_DoubleColumnList_ShouldAggregate ()
    {
        // | 名字 | 性别 | 名字 | 性别 |
        var grid = new string?[3 , 4];
        grid[0 , 0] = "姓名";
        grid[0 , 1] = "性别";
        grid[0 , 2] = "姓名";
        grid[0 , 3] = "性别";
        grid[1 , 0] = "Alice";
        grid[1 , 1] = "女";
        grid[1 , 2] = "Bob";
        grid[1 , 3] = "男";
        grid[2 , 0] = "Charlie";
        grid[2 , 1] = "男";
        grid[2 , 2] = "Diana";
        grid[2 , 3] = "女";

        var result = FuzzyColumnMatcher.TryParse(grid , 3 , 4);

        result.IsStandardTemplate.Should().BeFalse();
        result.Students.Should().HaveCount(4);
        result.Students![0].Name.Should().Be("Alice");
        result.Students[0].Gender.Should().Be(Core.Enums.Gender.Female);
        result.Students[1].Name.Should().Be("Bob");
        result.Students[1].Gender.Should().Be(Core.Enums.Gender.Male);
        result.Students[2].Name.Should().Be("Charlie");
        result.Students[2].Gender.Should().Be(Core.Enums.Gender.Male);
        result.Students[3].Name.Should().Be("Diana");
        result.Students[3].Gender.Should().Be(Core.Enums.Gender.Female);
    }

    [Fact]
    public void TryParse_DoubleColumnIrregularPattern_ShouldHandle ()
    {
        // | 名字 | 身高 | 名字 | 性别 |
        // Group 0: Name(0), Height(1)
        // Group 1: Name(2), Gender(3)
        var grid = new string?[3 , 4];
        grid[0 , 0] = "姓名";
        grid[0 , 1] = "身高";
        grid[0 , 2] = "姓名";
        grid[0 , 3] = "性别";
        grid[1 , 0] = "Alice";
        grid[1 , 1] = "165";
        grid[1 , 2] = "Bob";
        grid[1 , 3] = "男";
        grid[2 , 0] = "Charlie";
        grid[2 , 1] = "170";
        grid[2 , 2] = "";
        grid[2 , 3] = "";

        var result = FuzzyColumnMatcher.TryParse(grid , 3 , 4);

        // 不规则模式：Alice 得到全部三个属性（Group 0），Bob 仅 Name（Group 1），Charlie 有 Name+Height
        result.Students.Should().HaveCount(3);
        result.Students![0].Name.Should().Be("Alice");
        result.Students[0].Height.Should().Be(165);
        result.Students[0].Gender.Should().Be(Core.Enums.Gender.Male);  // col 3 的 Gender 在 Group 0
        result.Students[1].Name.Should().Be("Bob");                     // col 2 在 Group 1（仅 Name）
        result.Students[2].Name.Should().Be("Charlie");
        result.Students[2].Height.Should().Be(170);
    }

    // ═══════════════════════════════════════════════
    //  2-连续空终止
    // ═══════════════════════════════════════════════

    [Fact]
    public void TryParse_TwoConsecutiveEmptyRows_ShouldTerminate ()
    {
        // 表头在非第 0 行，让模糊匹配运行
        var grid = new string?[7 , 2];
        grid[1 , 0] = "姓名";
        grid[1 , 1] = "身高";
        grid[2 , 0] = "Alice";
        grid[2 , 1] = "165";
        grid[3 , 0] = "";       // 空行 1
        grid[4 , 0] = "";       // 空行 2 → 终止
        grid[5 , 0] = "Bob";    // 不应被读取
        grid[6 , 0] = "Charlie";

        var result = FuzzyColumnMatcher.TryParse(grid , 7 , 2);

        result.Students.Should().HaveCount(1);
        result.Students![0].Name.Should().Be("Alice");
    }

    [Fact]
    public void TryParse_SingleEmptyRow_ShouldContinue ()
    {
        // 表头不在第 0 行，让模糊匹配真正运行
        var grid = new string?[6 , 2];
        grid[1 , 0] = "姓名";
        grid[1 , 1] = "身高";
        grid[2 , 0] = "Alice";
        grid[2 , 1] = "165";
        grid[3 , 0] = "";       // 空行 1（仅一个）
        grid[4 , 0] = "Bob";    // 应被读取
        grid[5 , 0] = "";

        var result = FuzzyColumnMatcher.TryParse(grid , 6 , 2);

        result.Students.Should().NotBeNull();
        result.Students.Should().HaveCount(2);
        result.Students![0].Name.Should().Be("Alice");
        result.Students[1].Name.Should().Be("Bob");
    }

    [Fact]
    public void TryParse_TwoConsecutiveEmptyPerColumn_ShouldTerminateIndependently ()
    {
        // 表头在非第 0 行，让模糊匹配运行
        var grid = new string?[7 , 2];
        grid[1 , 0] = "姓名";
        grid[1 , 1] = "身高";
        grid[2 , 0] = "Alice";
        grid[2 , 1] = "165";
        grid[3 , 0] = "Bob";
        grid[3 , 1] = "170";
        grid[4 , 0] = "Charlie";
        grid[4 , 1] = "";       // Height 空 1
        grid[5 , 0] = "Diana";
        grid[5 , 1] = "";       // Height 空 2 → Height 列终止，但 Name 继续
        grid[6 , 0] = "";       // Name 空 1

        var result = FuzzyColumnMatcher.TryParse(grid , 7 , 2);

        result.Students.Should().HaveCount(4);
        result.Students![0].Name.Should().Be("Alice");
        result.Students[0].Height.Should().Be(165);
        result.Students[1].Name.Should().Be("Bob");
        result.Students[1].Height.Should().Be(170);
        result.Students[2].Name.Should().Be("Charlie");
        result.Students[2].Height.Should().BeNull();
        result.Students[3].Name.Should().Be("Diana");
        result.Students[3].Height.Should().BeNull();
    }

    // ═══════════════════════════════════════════════
    //  行式布局
    // ═══════════════════════════════════════════════

    [Fact]
    public void TryParse_RowBasedLayout_ShouldDetectAndParse ()
    {
        // | 姓名   | Alice | Bob   |
        // | 身高   | 165   | 180   |
        // | 性别   | 女    | 男    |
        var grid = new string?[3 , 3];
        grid[0 , 0] = "姓名";
        grid[1 , 0] = "身高";
        grid[2 , 0] = "性别";
        grid[0 , 1] = "Alice";
        grid[1 , 1] = "165";
        grid[2 , 1] = "女";
        grid[0 , 2] = "Bob";
        grid[1 , 2] = "180";
        grid[2 , 2] = "男";

        var result = FuzzyColumnMatcher.TryParse(grid , 3 , 3);

        result.Students.Should().HaveCount(2);
        result.Students![0].Name.Should().Be("Alice");
        result.Students[0].Height.Should().Be(165);
        result.Students[0].Gender.Should().Be(Core.Enums.Gender.Female);
        result.Students[1].Name.Should().Be("Bob");
        result.Students[1].Height.Should().Be(180);
        result.Students[1].Gender.Should().Be(Core.Enums.Gender.Male);
    }

    // ═══════════════════════════════════════════════
    //  无匹配字段
    // ═══════════════════════════════════════════════

    [Fact]
    public void TryParse_NoRecognizableFields_ShouldReturnEmpty ()
    {
        var grid = new string?[3 , 3];
        grid[0 , 0] = "工号";
        grid[0 , 1] = "部门";
        grid[0 , 2] = "职位";
        grid[1 , 0] = "001";
        grid[1 , 1] = "技术部";
        grid[1 , 2] = "工程师";

        var result = FuzzyColumnMatcher.TryParse(grid , 2 , 3);

        result.HasNameField.Should().BeFalse();
        result.Students.Should().NotBeNull();
        result.Students.Should().BeEmpty();
    }

    [Fact]
    public void TryParse_EmptyGrid_ShouldReturnEmpty ()
    {
        var grid = new string?[0 , 0];

        var result = FuzzyColumnMatcher.TryParse(grid , 0 , 0);

        result.HasNameField.Should().BeFalse();
        result.Students.Should().NotBeNull();
        result.Students.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════
    //  仅部分字段
    // ═══════════════════════════════════════════════

    [Fact]
    public void TryParse_OnlyNameColumn_ShouldReturnStudentsWithName ()
    {
        var grid = new string?[4 , 2];
        grid[2 , 1] = "姓名";    // 非标准位置
        grid[3 , 1] = "Alice";
        // 只有一行数据

        var result = FuzzyColumnMatcher.TryParse(grid , 4 , 2);

        result.HasNameField.Should().BeTrue();
        result.Students.Should().HaveCount(1);
        result.Students![0].Name.Should().Be("Alice");
    }

    // ═══════════════════════════════════════════════
    //  中英文混合表头
    // ═══════════════════════════════════════════════

    [Fact]
    public void TryParse_MixedLanguageHeaders_ShouldMatch ()
    {
        var grid = new string?[2 , 4];
        grid[0 , 0] = "姓名";
        grid[0 , 1] = "Height";
        grid[0 , 2] = "性别";
        grid[0 , 3] = "NeedsFrontRow";
        grid[1 , 0] = "测试学生";
        grid[1 , 1] = "175";
        grid[1 , 2] = "男";
        grid[1 , 3] = "是";

        var result = FuzzyColumnMatcher.TryParse(grid , 2 , 4);

        result.IsStandardTemplate.Should().BeTrue(); // 都在第 0 行
        result.HasNameField.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════
    //  ActualDataRows / ActualDataCols 回传
    // ═══════════════════════════════════════════════

    [Fact]
    public void TryParse_ShouldReturnActualDimensions ()
    {
        var grid = new string?[5 , 3];
        grid[0 , 0] = "姓名";
        grid[1 , 0] = "Alice";

        var result = FuzzyColumnMatcher.TryParse(grid , 5 , 3);

        result.ActualDataRows.Should().Be(5);
        result.ActualDataCols.Should().Be(3);
    }

    // ═══════════════════════════════════════════════
    //  注释行跳过
    // ═══════════════════════════════════════════════

    [Fact]
    public void TryParse_CommentRowAfterHeader_ShouldBeSkipped ()
    {
        // 模拟：表头（非第 0 行） → 全空注释行 → 数据
        var grid = new string?[7 , 2];
        grid[2 , 0] = "姓名";
        grid[2 , 1] = "身高";
        grid[3 , 0] = "";        // 全空注释行（会被 IsMostlyEmptyRow 跳过）
        grid[3 , 1] = "";
        grid[4 , 0] = "Alice";
        grid[4 , 1] = "165";
        grid[5 , 0] = "Bob";
        grid[5 , 1] = "180";
        grid[6 , 0] = "";
        grid[6 , 1] = "";

        var result = FuzzyColumnMatcher.TryParse(grid , 7 , 2);

        // 全空行在表头后被跳过，数据从 row 4 开始
        result.IsStandardTemplate.Should().BeFalse();
        result.Students.Should().HaveCount(2);
        result.Students![0].Name.Should().Be("Alice");
        result.Students[1].Name.Should().Be("Bob");
    }
}
