using SeatFlow.Core.Models;

namespace SeatFlow.Infrastructure.Providers;

/// <summary>
/// 导入表格模糊字段匹配引擎。
/// 格式无关——CSV/XLSX 各自构建 <c>string?[,]</c> 网格传入。
/// 通过扫描单元格中已知字段名来推测数据布局（列式/行式），
/// 支持双列名单聚合、合并单元格处理、错误容忍。
/// </summary>
internal static partial class FuzzyColumnMatcher
{
    private enum LayoutType { Columnar, RowBased }

    /// <summary>
    /// 模糊匹配的返回结果。
    /// <see cref="Students"/> 为 null 表示检测到标准模板格式，调用方应走快速路径。
    /// </summary>
    internal sealed class FuzzyParseResult
    {
        /// <summary>
        /// 解析出的学生列表。null = 标准模板信号。
        /// </summary>
        public List<Student>? Students { get; init; }

        /// <summary>是否检测到标准模板格式（字段全在第 0 行）。</summary>
        public bool IsStandardTemplate { get; init; }

        /// <summary>是否在表格中检测到了"姓名/Name"字段。</summary>
        public bool HasNameField { get; init; }

        /// <summary>文件实际数据行数。</summary>
        public int ActualDataRows { get; init; }

        /// <summary>文件实际数据列数。</summary>
        public int ActualDataCols { get; init; }
    }

    /// <summary>
    /// 入口方法。扫描整个网格，检测字段位置、布局方向，读取数据。
    /// </summary>
    /// <param name="cells">单元格二维数组 [row, col]。</param>
    /// <param name="rows">实际数据行数。</param>
    /// <param name="cols">实际数据列数。</param>
    /// <returns>
    /// <see cref="FuzzyParseResult.Students"/> 为 null → 标准模板；
    /// 空列表 → 无可识别字段或无可解析数据。
    /// </returns>
    internal static FuzzyParseResult TryParse (string?[,] cells , int rows , int cols)
    {
        var hits = FindFieldHits(cells , rows , cols);
        var hasName = hits.ContainsKey("Name");

        if (IsStandardTemplate(hits))
        {
            return new FuzzyParseResult
            {
                IsStandardTemplate = true ,
                HasNameField = hasName ,
                ActualDataRows = rows ,
                ActualDataCols = cols
            };
        }

        if (hits.Count == 0)
        {
            return new FuzzyParseResult
            {
                Students = [] ,
                HasNameField = false ,
                ActualDataRows = rows ,
                ActualDataCols = cols
            };
        }

        var layout = DetectLayout(hits);
        var students = layout == LayoutType.Columnar
            ? ParseColumnar(cells , rows , cols , hits)
            : ParseRowBased(cells , rows , cols , hits);

        return new FuzzyParseResult
        {
            Students = students ,
            IsStandardTemplate = false ,
            HasNameField = hasName ,
            ActualDataRows = rows ,
            ActualDataCols = cols
        };
    }

    // ═══════════════════════════════════════════════
    //  Phase 1: 字段检测
    // ═══════════════════════════════════════════════

    /// <summary>
    /// 扫描所有单元格，使用 <see cref="StudentDataMapping.ResolveProperty"/> 匹配已知字段名。
    /// </summary>
    /// <returns>属性名 → 命中位置列表。</returns>
    private static Dictionary<string , List<(int Row , int Col)>> FindFieldHits (
        string?[,] cells , int rows , int cols)
    {
        var hits = new Dictionary<string , List<(int Row , int Col)>>();

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                var value = cells[r , c];
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                var prop = StudentDataMapping.ResolveProperty(value.Trim());
                if (prop == null)
                    continue;

                if (!hits.ContainsKey(prop))
                    hits[prop] = [];
                hits[prop].Add((r , c));
            }
        }

        return hits;
    }

    // ═══════════════════════════════════════════════
    //  Phase 2: 标准模板快速路径检测
    // ═══════════════════════════════════════════════

    /// <summary>
    /// 所有命中都在第 0 行 且 每个属性最多击中一次 → 标准模板格式。
    /// 排除双列名单（同一字段重复出现）等需要模糊聚合的场景。
    /// </summary>
    private static bool IsStandardTemplate (
        Dictionary<string , List<(int Row , int Col)>> hits)
    {
        if (hits.Count == 0)
            return false;

        foreach (var (prop , positions) in hits)
        {
            foreach (var (row , _) in positions)
            {
                if (row != 0)
                    return false;
            }

            // 同一属性出现多次（如双列名单 → |名字|性别|名字|性别|）→ 非标准模板
            if (positions.Select(p => p.Col).Distinct().Count() > 1)
                return false;
        }

        return true;
    }

    // ═══════════════════════════════════════════════
    //  Phase 3: 布局方向判定
    // ═══════════════════════════════════════════════

    /// <summary>
    /// 按命中集中在行/列的比例判定布局方向。
    /// </summary>
    private static LayoutType DetectLayout (
        Dictionary<string , List<(int Row , int Col)>> hits)
    {
        var hitsByRow = new Dictionary<int , int>();
        var hitsByCol = new Dictionary<int , int>();

        foreach (var positions in hits.Values)
        {
            foreach (var (row , col) in positions)
            {
                hitsByRow.TryGetValue(row , out var rc);
                hitsByRow[row] = rc + 1;

                hitsByCol.TryGetValue(col , out var cc);
                hitsByCol[col] = cc + 1;
            }
        }

        int totalHits = hitsByRow.Values.Sum();
        if (totalHits == 0)
            return LayoutType.Columnar; // 默认

        // top-2 行命中数
        var topRows = hitsByRow.OrderByDescending(kv => kv.Value).Take(2).Sum(kv => kv.Value);
        // top-2 列命中数
        var topCols = hitsByCol.OrderByDescending(kv => kv.Value).Take(2).Sum(kv => kv.Value);

        double rowRatio = (double)topRows / totalHits;
        double colRatio = (double)topCols / totalHits;

        // 偏向列式（更常见）
        return rowRatio >= colRatio ? LayoutType.Columnar : LayoutType.RowBased;
    }

    // ═══════════════════════════════════════════════
    //  Phase 4: 列式数据读取
    // ═══════════════════════════════════════════════

    /// <summary>
    /// 列式布局：字段名分布在同一行（表头行），数据向下延伸。
    /// </summary>
    private static List<Student> ParseColumnar (
        string?[,] cells , int totalRows , int totalCols ,
        Dictionary<string , List<(int Row , int Col)>> hits)
    {
        // 1. 属性名 → 去重排序后的列号
        var propertyColumns = ComputePropertyColumns(hits);

        // 2. 数据起始行 = 最后命中行 + 1
        int maxHeaderRow = hits.Values.SelectMany(ps => ps).Max(p => p.Row);
        int dataStartRow = maxHeaderRow + 1;

        // 跳过紧跟在表头后的一行（如果是全空/注释行）
        if (dataStartRow < totalRows && IsCompletelyEmptyRow(cells , dataStartRow , totalCols))
            dataStartRow++;

        // 3. 构建列组（用于双列聚合）
        var columnGroups = ComputeColumnGroups(propertyColumns);
        if (columnGroups.Count == 0)
            return [];

        // 4. 收集字段（列） → 值列表
        var fieldValues = ReadFieldValuesColumnar(cells , totalRows , totalCols ,
            columnGroups , dataStartRow);

        // 5. 聚合为学生列表
        return BuildStudentsFromFieldValues(fieldValues , columnGroups);
    }

    /// <summary>
    /// 检查行是否完全为空（扫描前 20 列），用于跳过表头后的纯空行。
    /// 与 <see cref="IsNearlyEmptyCol"/> 不同——行检查要求完全为空（更严格），
    /// 因为单列中的非空值就可能是一个有效数据行的起点。
    /// </summary>
    private static bool IsCompletelyEmptyRow (string?[,] cells , int row , int totalCols)
    {
        int nonEmptyCount = 0;
        int scanned = Math.Min(totalCols , 20);
        for (int c = 0; c < scanned; c++)
        {
            if (!string.IsNullOrWhiteSpace(cells[row , c]))
                nonEmptyCount++;
        }

        // 全空 → 跳过；至少有一个非空值 → 不是空行
        return nonEmptyCount == 0;
    }

    /// <summary>
    /// 属性名 → 去重排序后的列号列表。
    /// 例：{ Name: [0,3], Gender: [1,4], Height: [2] }
    /// </summary>
    private static Dictionary<string , List<int>> ComputePropertyColumns (
        Dictionary<string , List<(int Row , int Col)>> hits)
    {
        var result = new Dictionary<string , List<int>>();

        foreach (var (prop , positions) in hits)
        {
            var cols = positions
                .Select(p => p.Col)
                .Distinct()
                .OrderBy(c => c)
                .ToList();
            result[prop] = cols;
        }

        return result;
    }

    /// <summary>
    /// 构建列组——用于双列/多列名单聚合。
    /// 以重复次数最多的字段列作为锚点划定组边界，按列的空间位置分配。
    /// </summary>
    private static List<List<(string Property , int Col)>> ComputeColumnGroups (
        Dictionary<string , List<int>> propertyColumns)
    {
        if (propertyColumns.Count == 0)
            return [];

        // 1. 找锚点字段（列数最多的属性）
        var anchorEntry = propertyColumns.MaxBy(kv => kv.Value.Count);
        var anchorCols = anchorEntry.Value; // 已排序

        // 2. 初始化空组
        int maxGroups = anchorCols.Count;
        var groups = new List<List<(string Property , int Col)>>();
        for (int g = 0; g < maxGroups; g++)
            groups.Add([]);

        // 3. 将所有 (属性, 列) 按空间位置分配到对应组
        foreach (var (prop , cols) in propertyColumns)
        {
            foreach (var col in cols)
            {
                int groupIndex = FindGroupIndex(col , anchorCols);
                groups[groupIndex].Add((prop , col));
            }
        }

        // 4. 移除空组
        return groups.Where(g => g.Count > 0).ToList();
    }

    /// <summary>
    /// 根据锚点边界确定列/行所属的组索引。
    /// 列在锚点之间 → 前一个锚点的组；在第一锚点之前 → Group 0；在最后锚点之后 → 最后一组。
    /// </summary>
    private static int FindGroupIndex (int pos , List<int> anchorPositions)
    {
        if (pos < anchorPositions[0])
            return 0;

        for (int g = 0; g < anchorPositions.Count - 1; g++)
        {
            if (pos >= anchorPositions[g] && pos < anchorPositions[g + 1])
                return g;
        }

        return anchorPositions.Count - 1;
    }

    /// <summary>
    /// 按列读取所有字段的数据值，每列独立跟踪 2-连续空终止。
    /// </summary>
    private static Dictionary<int , List<string?>> ReadFieldValuesColumnar (
        string?[,] cells , int totalRows , int totalCols ,
        List<List<(string Property , int Col)>> columnGroups , int dataStartRow)
    {
        // 收集所有相关的列
        var allCols = new HashSet<int>();
        foreach (var group in columnGroups)
            foreach (var (_, col) in group)
                allCols.Add(col);

        // 每个列的连续空计数器
        var colConsecutiveEmpty = new Dictionary<int , int>();
        var colExhausted = new HashSet<int>();
        var colValues = new Dictionary<int , List<string?>>();

        foreach (int col in allCols)
            colValues[col] = [];

        for (int r = dataStartRow; r < totalRows; r++)
        {
            bool allExhausted = true;

            foreach (int col in allCols)
            {
                if (colExhausted.Contains(col))
                    continue;

                allExhausted = false;

                var value = cells[r , col];
                if (string.IsNullOrWhiteSpace(value))
                {
                    colConsecutiveEmpty.TryGetValue(col , out var cnt);
                    colConsecutiveEmpty[col] = cnt + 1;
                    if (cnt + 1 >= 2)
                        colExhausted.Add(col);
                    colValues[col].Add(null);
                }
                else
                {
                    colConsecutiveEmpty[col] = 0;
                    colValues[col].Add(value);
                }
            }

            if (allExhausted)
                break;
        }

        return colValues;
    }

    /// <summary>
    /// 将按列收集的字段值聚合为学生列表，按行对齐。
    /// </summary>
    private static List<Student> BuildStudentsFromFieldValues (
        Dictionary<int , List<string?>> colValues ,
        List<List<(string Property , int Col)>> columnGroups)
    {
        // 找出各组中的最大行数
        int maxRows = 0;
        foreach (var group in columnGroups)
        {
            int groupRows = group.Count > 0
                ? group.Max(g => colValues.TryGetValue(g.Col , out var vals) ? vals.Count : 0)
                : 0;
            maxRows = Math.Max(maxRows , groupRows);
        }

        var students = new List<Student>();

        for (int r = 0; r < maxRows; r++)
        {
            foreach (var group in columnGroups)
            {
                var student = new Student();
                bool hasData = false;

                foreach (var (property , col) in group)
                {
                    if (colValues.TryGetValue(col , out var vals) && r < vals.Count)
                    {
                        var value = vals[r];
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            StudentDataMapping.SetProperty(student , property , value);
                            hasData = true;
                        }
                    }
                }

                if (hasData && !string.IsNullOrWhiteSpace(student.Name))
                    students.Add(student);
            }
        }

        return students;
    }
}