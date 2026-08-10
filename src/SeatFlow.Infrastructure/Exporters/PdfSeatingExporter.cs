using SeatFlow.Core.Exporters;
using SeatFlow.Core.Models;
using SeatFlow.Core.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SeatFlow.Infrastructure.Exporters;

public class PdfSeatingExporter (ILogger<PdfSeatingExporter>? logger = null) : ISeatingPlanExporter
{
    private const float DefaultCellWidth = 22f;
    private const float CompactCellWidth = 11f;
    private const float HeaderRowHeight = 16f;
    private const float DataRowHeight = 10f;
    private const float AisleRowHeight = 6f;
    private const float PageMargin = 10f;
    private const float FooterHeight = 10f;

    private readonly ILogger<PdfSeatingExporter> _logger = logger ?? NullLogger<PdfSeatingExporter>.Instance;

    public ExportFormat Format => ExportFormat.Pdf;

    static PdfSeatingExporter ()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public Task ExportAsync (SeatingPlan plan , string path , CancellationToken cancellationToken = default)
    {
        return ExportAsync(plan , path , new ExportOptions { Format = ExportFormat.Pdf } , cancellationToken);
    }

    public async Task ExportAsync (SeatingPlan plan , string path , ExportOptions options , CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("PDF 座位表导出开始：{Path}（{Count} 条记录）" , path , plan.Assignments.Count);

        await Task.Run(() =>
        {
            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(2 , Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(12));

                    page.Header()
                        .Text(options.Anonymize ? "座位安排表 (匿名)" : "座位安排表")
                        .SemiBold().FontSize(20).AlignCenter();

                    page.Content()
                        .Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });

                            table.Header(header =>
                            {
                                header.Cell().Text("座位ID").Bold();
                                header.Cell().Text(options.Anonymize ? "学生ID (匿名)" : "学生ID").Bold();
                            });

                            foreach (var kv in plan.Assignments.OrderBy(x => x.Key))
                            {
                                table.Cell().Text(kv.Key);
                                table.Cell().Text(options.Anonymize ? "***" : kv.Value);
                            }
                        });

                    page.Footer()
                        .AlignCenter()
                        .Text(x =>
                        {
                            x.Span("生成时间: ");
                            x.Span(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                        });
                });
            }).GeneratePdf(path);
            _logger.LogInformation("PDF 座位表导出完成: {Path}" , path);
        } , cancellationToken);
    }

    public async Task ExportLayoutAsync (LayoutSeatingExportModel model , string path , ExportOptions options , CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("PDF 座位布局导出开始：{Path}（{RowCount} 行）" , path , model.Rows.Count);

        int maxCols = model.Rows.Count > 0 ? model.Rows.Max(r => r.Cells.Count) : 1;
        int rowCount = model.Rows.Count;

        // 根据内容动态计算页面尺寸，不再硬限列数
        float rowH = Math.Max(DataRowHeight , rowCount > 50 ? 7f : 10f);
        float contentWidth = (maxCols * CompactCellWidth) + (PageMargin * 2);
        float contentHeight = (rowCount * rowH) + (PageMargin * 2) + FooterHeight + HeaderRowHeight;
        float pageWidth = Math.Clamp(contentWidth , 297f , 841f);  // A4 landscape ~ A0 portrait
        float pageHeight = Math.Clamp(contentHeight , 210f , 1189f); // A4 landscape ~ A0

        await Task.Run(() =>
        {
            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(pageWidth , pageHeight , Unit.Millimetre);
                    page.MarginHorizontal(PageMargin , Unit.Millimetre);
                    page.MarginVertical(PageMargin , Unit.Millimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(8));

                    page.Header()
                        .Text(model.LayoutName)
                        .SemiBold().FontSize(16).AlignCenter();

                    page.Content()
                        .Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                for (int i = 0; i < maxCols; i++)
                                    columns.ConstantColumn(CompactCellWidth , Unit.Millimetre);
                            });

                            int rowIndex = 0;
                            foreach (var row in model.Rows)
                            {
                                if (++rowIndex % 30 == 0)
                                    cancellationToken.ThrowIfCancellationRequested();

                                bool isFullAisleRow = row.Cells.Count > 0 && row.Cells.All(c => c.IsAisle);
                                int colCount = 0;
                                foreach (var cell in row.Cells)
                                {
                                    var cellElement = table.Cell()
                                        .Border(1).BorderColor(Colors.Grey.Lighten2)
                                        .Background(cell.IsUnassigned ? Colors.Grey.Darken2 :
                                                     cell.IsPodium ? Colors.Blue.Lighten4 :
                                                     cell.IsAisle || isFullAisleRow ? Colors.Grey.Lighten3 :
                                                     cell.IsSeat ? Colors.Green.Lighten5 :
                                                     Colors.White)
                                        .Padding(2)
                                        .MinHeight(isFullAisleRow ? AisleRowHeight : rowH , Unit.Millimetre)
                                        .AlignMiddle()
                                        .AlignCenter();
                                    cellElement.Text(cell.Text);
                                    colCount++;
                                }
                                // 补齐不足列
                                for (int i = colCount; i < maxCols; i++)
                                    table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(1).Text("");
                            }
                        });

                    page.Footer()
                        .AlignCenter()
                        .Text(x =>
                        {
                            x.Span("生成时间: ");
                            x.Span(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                        });
                });
            }).GeneratePdf(path);
            _logger.LogInformation("PDF 座位布局导出完成: {Path}" , path);
        } , cancellationToken);
    }
}
