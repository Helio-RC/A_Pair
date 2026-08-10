using SeatFlow.Core.Exporters;
using SeatFlow.Core.Models;
using SeatFlow.Core.Workspace;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;

namespace SeatFlow.Infrastructure.Exporters;

public class ExcelSeatingExporter : ISeatingPlanExporter
{
    private readonly ILogger<ExcelSeatingExporter> _logger;

    public ExcelSeatingExporter (ILogger<ExcelSeatingExporter> logger)
    {
        _logger = logger;
        ExcelPackage.License.SetNonCommercialPersonal("SeatFlow");
    }

    public ExportFormat Format => ExportFormat.Excel;

    public async Task ExportAsync (SeatingPlan plan , string path , CancellationToken cancellationToken = default)
    {
        await ExportAsync(plan , path , new ExportOptions { Format = ExportFormat.Excel } , cancellationToken);
    }

    public async Task ExportAsync (SeatingPlan plan , string path , ExportOptions options , CancellationToken cancellationToken = default)
    {
        try
        {
            using var p = new ExcelPackage();
            var ws = p.Workbook.Worksheets.Add("Seating");
            ws.Cells[1 , 1].Value = "SeatId";
            ws.Cells[1 , 2].Value = options.Anonymize ? "StudentId (anonymized)" : "StudentId";
            int r = 2;
            foreach (var kv in plan.Assignments)
            {
                ws.Cells[r , 1].Value = kv.Key;
                ws.Cells[r , 2].Value = options.Anonymize ? "***" : kv.Value;
                r++;
            }

            if (options.IncludeMetadata)
            {
                var metaWs = p.Workbook.Worksheets.Add("Metadata");
                metaWs.Cells[1 , 1].Value = "Property";
                metaWs.Cells[1 , 2].Value = "Value";
                metaWs.Cells[2 , 1].Value = "ExportTime";
                metaWs.Cells[2 , 2].Value = DateTime.Now.ToString("O");
                metaWs.Cells[3 , 1].Value = "SeatCount";
                metaWs.Cells[3 , 2].Value = plan.Assignments.Count;
            }

            var fi = new FileInfo(path);
            await p.SaveAsAsync(fi , cancellationToken);
            _logger.LogInformation("Excel 导出完成: {Path}，{Count} 条记录" , path , plan.Assignments.Count);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            _logger.LogError(ex , "Excel 导出失败，回退为 CSV: {Path}" , path);
            var lines = new System.Collections.Generic.List<string> { "SeatId,StudentId" };
            foreach (var kv in plan.Assignments)
                lines.Add($"{kv.Key},{(options.Anonymize ? "***" : kv.Value)}");
            await File.WriteAllLinesAsync(path , lines , cancellationToken);
        }
    }

    public async Task ExportLayoutAsync (LayoutSeatingExportModel model , string path , ExportOptions options , CancellationToken cancellationToken = default)
    {
        try
        {
            using var p = new ExcelPackage();
            var ws = p.Workbook.Worksheets.Add("Seating");
            ws.Cells[1 , 1].Value = model.LayoutName;
            ws.Cells[1 , 1].Style.Font.Bold = true;
            ws.Cells[1 , 1].Style.Font.Size = 14;

            int r = 3;
            int rowIndex = 0;
            foreach (var row in model.Rows)
            {
                if (++rowIndex % 30 == 0)
                    cancellationToken.ThrowIfCancellationRequested();

                int c = 1;
                bool isFullAisleRow = row.Cells.Count > 0 && row.Cells.All(cell => cell.IsAisle);
                foreach (var cell in row.Cells)
                {
                    ws.Cells[r , c].Value = cell.Text;
                    if (cell.IsUnassigned)
                    {
                        ws.Cells[r , c].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                        ws.Cells[r , c].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.DarkGray);
                    }
                    else if (cell.IsAisle || isFullAisleRow)
                    {
                        ws.Cells[r , c].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                        ws.Cells[r , c].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                    }
                    c++;
                }
                ws.Row(r).Height = isFullAisleRow ? 20 : 28;
                r++;
            }

            if (options.IncludeMetadata)
            {
                var metaWs = p.Workbook.Worksheets.Add("Metadata");
                metaWs.Cells[1 , 1].Value = "Property";
                metaWs.Cells[1 , 2].Value = "Value";
                metaWs.Cells[2 , 1].Value = "ExportTime";
                metaWs.Cells[2 , 2].Value = DateTime.Now.ToString("O");
                metaWs.Cells[3 , 1].Value = "LayoutName";
                metaWs.Cells[3 , 2].Value = model.LayoutName;
            }

            var fi = new FileInfo(path);
            await p.SaveAsAsync(fi , cancellationToken);
            _logger.LogInformation("Excel 布局导出完成: {Path}，{RowCount} 行" , path , model.Rows.Count);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            _logger.LogError(ex , "Excel 布局导出失败，回退为 CSV: {Path}" , path);
            var lines = new System.Collections.Generic.List<string>();
            foreach (var row in model.Rows)
                lines.Add(string.Join("," , row.Cells.Select(c => c.Text)));
            await File.WriteAllLinesAsync(path , lines , cancellationToken);
        }
    }
}
