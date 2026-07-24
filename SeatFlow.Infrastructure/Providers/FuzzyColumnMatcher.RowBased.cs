using SeatFlow.Core.Models;

namespace SeatFlow.Infrastructure.Providers;

// ═══════════════════════════════════════════════
//  Phase 5: 行式数据读取
// ═══════════════════════════════════════════════

internal static partial class FuzzyColumnMatcher
{
    /// <summary>
    /// 行式布局：字段名分布在同一列（表头列），数据向右延伸。
    /// 支持多组行聚合——如两组字段标签分别在不同行时。
    /// </summary>
    private static List<Student> ParseRowBased (
        string?[,] cells , int totalRows , int totalCols ,
        Dictionary<string , List<(int Row , int Col)>> hits)
    {
        // 属性名 → 去重排序后的行号
        var propertyRows = new Dictionary<string , List<int>>();
        foreach (var (prop , positions) in hits)
        {
            propertyRows[prop] = positions
                .Select(p => p.Row)
                .Distinct()
                .OrderBy(r => r)
                .ToList();
        }

        int maxHeaderCol = hits.Values.SelectMany(ps => ps).Max(p => p.Col);
        int dataStartCol = maxHeaderCol + 1;

        // 跳过紧跟在表头后的一列（如果是接近全空）
        if (dataStartCol < totalCols && IsNearlyEmptyCol(cells , dataStartCol , totalRows))
            dataStartCol++;

        // 构建行组（双组聚合）
        int maxGroups = propertyRows.Values.Max(rs => rs.Count);
        var rowGroups = new List<List<(string Property , int Row)>>();

        for (int g = 0; g < maxGroups; g++)
        {
            var group = new List<(string Property , int Row)>();
            foreach (var (prop , rows) in propertyRows)
            {
                if (g < rows.Count)
                    group.Add((prop , rows[g]));
            }

            if (group.Count > 0)
                rowGroups.Add(group);
        }

        if (rowGroups.Count == 0)
            return [];

        // 收集所有相关行
        var allRows = new HashSet<int>();
        foreach (var group in rowGroups)
            foreach (var (_, row) in group)
                allRows.Add(row);

        // 每行独立跟踪 2-连续空终止
        var rowConsecutiveEmpty = new Dictionary<int , int>();
        var rowExhausted = new HashSet<int>();
        var rowValues = new Dictionary<int , List<string?>>();

        foreach (int row in allRows)
            rowValues[row] = [];

        for (int c = dataStartCol; c < totalCols; c++)
        {
            bool allExhausted = true;

            foreach (int row in allRows)
            {
                if (rowExhausted.Contains(row))
                    continue;

                allExhausted = false;

                if (row >= totalRows)
                {
                    rowConsecutiveEmpty.TryGetValue(row , out var cnt);
                    rowConsecutiveEmpty[row] = cnt + 1;
                    if (cnt + 1 >= 2)
                        rowExhausted.Add(row);
                    rowValues[row].Add(null);
                    continue;
                }

                var value = cells[row , c];
                if (string.IsNullOrWhiteSpace(value))
                {
                    rowConsecutiveEmpty.TryGetValue(row , out var cnt);
                    rowConsecutiveEmpty[row] = cnt + 1;
                    if (cnt + 1 >= 2)
                        rowExhausted.Add(row);
                    rowValues[row].Add(null);
                }
                else
                {
                    rowConsecutiveEmpty[row] = 0;
                    rowValues[row].Add(value);
                }
            }

            if (allExhausted)
                break;
        }

        // 从行式数据构建学生列表
        int maxCols = 0;
        foreach (var group in rowGroups)
        {
            int groupCols = group.Count > 0
                ? group.Max(g => rowValues.TryGetValue(g.Row , out var vals) ? vals.Count : 0)
                : 0;
            maxCols = Math.Max(maxCols , groupCols);
        }

        var students = new List<Student>();

        for (int c = 0; c < maxCols; c++)
        {
            foreach (var group in rowGroups)
            {
                var student = new Student();
                bool hasData = false;

                foreach (var (property , row) in group)
                {
                    if (rowValues.TryGetValue(row , out var vals) && c < vals.Count)
                    {
                        var value = vals[c];
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

    /// <summary>
    /// 检查列是否接近全空（扫描前 10 行），用于跳过行式布局中的分隔列。
    /// 容忍至多 1 个非空单元格——行式布局中分隔列可能含有一两个杂散值。
    /// 与 <see cref="IsCompletelyEmptyRow"/> 不同——列检查更宽松。
    /// </summary>
    private static bool IsNearlyEmptyCol (string?[,] cells , int col , int totalRows)
    {
        int emptyCount = 0;
        int scanned = Math.Min(totalRows , 10);
        for (int r = 0; r < scanned; r++)
        {
            if (string.IsNullOrWhiteSpace(cells[r , col]))
                emptyCount++;
        }

        return emptyCount >= scanned - 1;
    }
}
