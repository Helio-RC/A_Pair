using SeatFlow.Core.Models;
using SeatFlow.Core.Providers;
using OfficeOpenXml;

namespace SeatFlow.Infrastructure.Providers
{
    /// <summary>
    /// XLSX 格式的学生数据写入器，使用 EPPlus 库将学生列表导出为 Excel 文件。
    /// 第 1 行为列名（支持中英文），第 2 行为注释行，数据从第 3 行开始。
    /// </summary>
    public class XlsxStudentWriter : IStudentWriter
    {
        public async Task WriteAsync (string path , IEnumerable<Student> students , CancellationToken cancellationToken = default)
        {
            ExcelPackage.License.SetNonCommercialPersonal("SeatFlow");
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Students");

            // 第 1 行：列名
            ws.Cells[1 , 1].Value = "姓名";
            ws.Cells[1 , 2].Value = "身高";
            ws.Cells[1 , 3].Value = "性别";
            ws.Cells[1 , 4].Value = "需要前排";

            // 第 2 行：注释
            ws.Cells[2 , 1].Value = "必填";
            ws.Cells[2 , 2].Value = "身高（厘米），如 170.5";
            ws.Cells[2 , 3].Value = "男 / 女 / 其他";
            ws.Cells[2 , 4].Value = "是 / 否（或 true/false）";

            // 第 3 行起：数据
            int row = 3;
            foreach (var s in students)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ws.Cells[row , 1].Value = s.Name;
                ws.Cells[row , 2].Value = s.Height;
                ws.Cells[row , 3].Value = s.Gender switch
                {
                    Core.Enums.Gender.Male => "男",
                    Core.Enums.Gender.Female => "女",
                    Core.Enums.Gender.Other => "其他",
                    _ => ""
                };
                ws.Cells[row , 4].Value = s.NeedsFrontRow ? "是" : "否";
                row++;
            }

            await package.SaveAsAsync(new FileInfo(path) , cancellationToken);
        }
    }
}
