using SeatFlow.Core.Models;
using SeatFlow.Core.Providers;
using OfficeOpenXml;

namespace SeatFlow.Infrastructure.Providers
{
    /// <summary>
    /// XLSX 格式的学生数据提供器，使用 EPPlus 库从 Excel 文件读取学生列表。
    /// 支持标准模板（第 1 行列名、第 2 行注释）与任意布局的模糊字段匹配。
    /// </summary>
    public class XlsxStudentProvider : IStudentProvider
    {
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

            ExcelPackage.License.SetNonCommercialPersonal("SeatFlow");
            using var stream = File.OpenRead(source);
            using var package = new ExcelPackage(stream);
            var ws = package.Workbook.Worksheets[0];
            if (ws.Dimension == null)
                return Task.FromResult(new List<Student>());

            // Phase 1: 构建二维网格（含合并格扩展，可限制范围）
            var (cells , totalRows , totalCols) = BuildCellGrid(ws , maxRows , maxCols , ct);

            // Phase 2: 模糊字段匹配
            var result = FuzzyColumnMatcher.TryParse(cells , totalRows , totalCols);

            if (result.IsStandardTemplate)
            {
                // 快速路径：表头在 row 0，数据从 row 2 开始（row 1=注释行）
                return Task.FromResult(ParseStandardTemplate(cells , totalRows , totalCols , ct));
            }

            if (result.Students != null)
                return Task.FromResult(result.Students);

            // 回退
            return Task.FromResult(ParseStandardTemplate(cells , totalRows , totalCols , ct));
        }

        /// <inheritdoc />
        public Task<(int Rows , int Cols)> GetDimensionsAsync (string source , CancellationToken ct = default)
        {
            return Task.FromResult(GetDimensions(source));
        }

        /// <summary>
        /// 获取文件维度（行数 × 列数），不解析学生数据。
        /// </summary>
        internal static (int Rows , int Cols) GetDimensions (string source)
        {
            if (string.IsNullOrEmpty(source) || !File.Exists(source))
                return (0 , 0);

            ExcelPackage.License.SetNonCommercialPersonal("SeatFlow");
            using var stream = File.OpenRead(source);
            using var package = new ExcelPackage(stream);
            var ws = package.Workbook.Worksheets[0];
            if (ws.Dimension == null)
                return (0 , 0);

            return (ws.Dimension.End.Row , ws.Dimension.End.Column);
        }

        // ═══════════════════════════════════════════════
        //  内部方法
        // ═══════════════════════════════════════════════

        private static (string?[,] Cells , int Rows , int Cols) BuildCellGrid (
            ExcelWorksheet ws , int maxRows , int maxCols , CancellationToken ct)
        {
            int rowCount = ws.Dimension.End.Row;
            int colCount = ws.Dimension.End.Column;

            int rowLimit = maxRows > 0 ? Math.Min(maxRows , rowCount) : rowCount;
            int colLimit = maxCols > 0 ? Math.Min(maxCols , colCount) : colCount;

            var cells = new string?[rowLimit , colLimit];

            // 读取所有单元格值
            for (int r = 1; r <= rowLimit; r++)
            {
                ct.ThrowIfCancellationRequested();
                for (int c = 1; c <= colLimit; c++)
                {
                    cells[r - 1 , c - 1] = ws.Cells[r , c].GetValue<string>();
                }
            }

            // 扩展合并单元格：若合并格左上角的值命中已知字段名 → 扩展到整个合并范围
            foreach (var mergeAddress in ws.MergedCells)
            {
                if (string.IsNullOrEmpty(mergeAddress))
                    continue;

                var mergedRange = ws.Cells[mergeAddress];
                int startRow = mergedRange.Start.Row;
                int startCol = mergedRange.Start.Column;
                int endRow = Math.Min(mergedRange.End.Row , rowLimit);
                int endCol = Math.Min(mergedRange.End.Column , colLimit);

                // 单格不算"合并"
                if (startRow == endRow && startCol == endCol)
                    continue;

                // 合并范围超出扫描区域 → 跳过
                if (startRow > rowLimit || startCol > colLimit)
                    continue;

                var topLeftValue = cells[startRow - 1 , startCol - 1];
                if (string.IsNullOrWhiteSpace(topLeftValue))
                    continue;

                var prop = StudentDataMapping.ResolveProperty(topLeftValue.Trim());
                if (prop == null)
                    continue;

                // 是已知字段名 → 扩展填充
                for (int r = startRow; r <= endRow; r++)
                {
                    for (int c = startCol; c <= endCol; c++)
                    {
                        cells[r - 1 , c - 1] = topLeftValue;
                    }
                }
            }

            return (cells , rowLimit , colLimit);
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
}
