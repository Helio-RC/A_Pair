using System.Globalization;
using SeatFlow.Core.Models;
using SeatFlow.Core.Providers;
using CsvHelper;
using CsvHelper.Configuration;

namespace SeatFlow.Infrastructure.Providers;

/// <summary>
/// CSV 格式的学生数据提供器，使用 CsvHelper 解析以正确处理引号字段和嵌入换行。
/// 支持标准模板（第 1 行列名、第 2 行注释）与任意布局的模糊字段匹配。
/// </summary>
public class CsvStudentProvider : IStudentProvider
{
    private static readonly CsvConfiguration FullReadConfig = new(CultureInfo.InvariantCulture)
    {
        // 不跳过任何行——全部读入网格供模糊匹配分析
        IgnoreBlankLines = false   // 保留空行以维持行号对齐（兼容标准模板的行 2=注释行）
    };

    private static readonly CsvConfiguration StandardConfig = new(CultureInfo.InvariantCulture)
    {
        ShouldSkipRecord = args => args.Row?.Context?.Parser?.Row == 2 // 跳过第 2 行（注释行）
    };

    // ═══════════════════════════════════════════════
    //  IStudentProvider 实现
    // ═══════════════════════════════════════════════

    public Task<List<Student>> LoadAsync (string source , CancellationToken cancellationToken = default)
    {
        return LoadAsync(source , 0 , 0 , cancellationToken);
    }

    /// <inheritdoc />
    public Task<List<Student>> LoadAsync (string source , int maxRows , int maxCols , CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(source) || !File.Exists(source))
            return Task.FromResult(new List<Student>());

        // Phase 1: 读取为二维网格（可限制范围）
        var (cells , totalRows , totalCols) = BuildCellGrid(source , maxRows , maxCols , ct);
        if (totalRows == 0)
            return Task.FromResult(new List<Student>());

        // Phase 2: 模糊字段匹配
        var result = FuzzyColumnMatcher.TryParse(cells , totalRows , totalCols);

        if (result.IsStandardTemplate)
        {
            // 快速路径：表头在 row 0，数据从 row 2 开始（row 1=注释行）
            return Task.FromResult(ParseStandardTemplate(cells , totalRows , totalCols , ct));
        }

        if (result.Students != null)
            return Task.FromResult(result.Students);

        // 回退：模糊匹配未找到有效数据 → 尝试标准模板解析
        return Task.FromResult(ParseStandardTemplate(cells , totalRows , totalCols , ct));
    }

    /// <inheritdoc />
    public Task<(int Rows , int Cols)> GetDimensionsAsync (string source , CancellationToken ct = default)
    {
        return Task.FromResult(GetDimensions(source));
    }

    /// <summary>
    /// 获取文件维度（行数 × 最大列数），不解析学生数据。
    /// </summary>
    internal static (int Rows , int Cols) GetDimensions (string source)
    {
        if (string.IsNullOrEmpty(source) || !File.Exists(source))
            return (0 , 0);

        int rowCount = 0;
        int maxCols = 0;

        using var reader = new StreamReader(source);
        using var csv = new CsvReader(reader , FullReadConfig);

        while (csv.Read())
        {
            maxCols = Math.Max(maxCols , csv.ColumnCount);
            rowCount++;
        }

        return (rowCount , maxCols);
    }

    // ═══════════════════════════════════════════════
    //  内部方法
    // ═══════════════════════════════════════════════

    private static (string?[,] Cells , int Rows , int Cols) BuildCellGrid (
        string source , int maxRows , int maxCols , CancellationToken ct)
    {
        var rows = new List<string?[]>();
        int scannedMaxCols = 0;
        int rowLimit = maxRows > 0 ? maxRows : int.MaxValue;
        int colLimit = maxCols > 0 ? maxCols : int.MaxValue;

        using var reader = new StreamReader(source);
        using var csv = new CsvReader(reader , FullReadConfig);

        while (csv.Read() && rows.Count < rowLimit)
        {
            ct.ThrowIfCancellationRequested();
            int cols = Math.Min(csv.ColumnCount , colLimit);
            var row = new string?[cols];
            for (int i = 0; i < cols; i++)
            {
                csv.TryGetField(i , out string? value);
                row[i] = value;
            }

            rows.Add(row);
            scannedMaxCols = Math.Max(scannedMaxCols , cols);
        }

        if (rows.Count == 0)
            return (new string?[0 , 0] , 0 , 0);

        int totalRows = rows.Count;
        int totalCols = scannedMaxCols;
        var cells = new string?[totalRows , totalCols];

        for (int r = 0; r < totalRows; r++)
        {
            for (int c = 0; c < rows[r].Length; c++)
            {
                cells[r , c] = rows[r][c];
            }
        }

        return (cells , totalRows , totalCols);
    }

    /// <summary>
    /// 标准模板快速路径：row 0 = 表头，row 1 = 注释行（跳过），row 2+ = 数据。
    /// </summary>
    private static List<Student> ParseStandardTemplate (
        string?[,] cells , int totalRows , int totalCols , CancellationToken ct = default)
    {
        var list = new List<Student>();

        // 建立列名→属性映射
        var columnMap = new Dictionary<int , string>();
        for (int c = 0; c < totalCols; c++)
        {
            var header = cells[0 , c];
            if (!string.IsNullOrWhiteSpace(header))
            {
                var prop = StudentDataMapping.ResolveProperty(header.Trim());
                if (prop != null)
                    columnMap[c] = prop;
            }
        }

        if (columnMap.Count == 0)
            return list;

        // 从 row 2 开始读取（跳过 row 1 注释行）
        for (int r = 2; r < totalRows; r++)
        {
            ct.ThrowIfCancellationRequested();
            var student = new Student();
            foreach (var (col , prop) in columnMap)
            {
                var raw = cells[r , col];
                StudentDataMapping.SetProperty(student , prop , raw);
            }

            if (!string.IsNullOrWhiteSpace(student.Name))
                list.Add(student);
        }

        return list;
    }
}
