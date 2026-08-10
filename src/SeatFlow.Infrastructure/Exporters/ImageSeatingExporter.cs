using SeatFlow.Core.Exporters;
using SeatFlow.Core.Models;
using SeatFlow.Core.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace SeatFlow.Infrastructure.Exporters;

public class ImageSeatingExporter (ILogger<ImageSeatingExporter>? logger = null) : ISeatingPlanExporter
{
    private const int CellWidth = 76;
    private const int CellHeight = 32;
    private const int AisleRowHeight = 16;
    private const int Margin = 20;
    private const float TextSize = 11;

    private readonly ILogger<ImageSeatingExporter> _logger = logger ?? NullLogger<ImageSeatingExporter>.Instance;

    /// <summary>跨平台 CJK 字体，通过 MatchCharacter 动态匹配系统可用字体。</summary>
    private static readonly SKTypeface CjkTypeface = ResolveCjkTypeface();

    private static SKTypeface ResolveCjkTypeface ()
    {
        var fm = SKFontManager.Default;
        return fm.MatchCharacter('中') ?? SKTypeface.Default;
    }

    public ExportFormat Format => ExportFormat.Png;

    public Task ExportAsync (SeatingPlan plan , string path , CancellationToken cancellationToken = default)
        => ExportAsync(plan , path , new ExportOptions { Format = ExportFormat.Png } , cancellationToken);

    public Task ExportAsync (SeatingPlan plan , string path , ExportOptions options , CancellationToken cancellationToken = default)
        => Task.CompletedTask; // 图片导出使用 ExportLayoutAsync

    public Task ExportLayoutAsync (LayoutSeatingExportModel model , string path , ExportOptions options , CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("图片座位布局导出开始：{Path}（{RowCount} 行）" , path , model.Rows.Count);

        if (model.Rows.Count == 0) return Task.CompletedTask;

        int maxCols = model.Rows.Max(r => r.Cells.Count);
        int width = (maxCols * CellWidth) + (Margin * 2);
        int height = (model.Rows.Count * CellHeight) + (Margin * 2);

        using var bitmap = new SKBitmap(width , height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        using var borderPaint = new SKPaint
        {
            Color = SKColors.LightGray ,
            Style = SKPaintStyle.Stroke ,
            StrokeWidth = 0.5f ,
            IsAntialias = true
        };
        using var seatFill = new SKPaint { Color = SKColors.White , Style = SKPaintStyle.Fill };
        using var aisleFill = new SKPaint { Color = new SKColor(0xE0 , 0xE0 , 0xE0) , Style = SKPaintStyle.Fill };
        using var podiumFill = new SKPaint { Color = new SKColor(0xE3 , 0xF2 , 0xFD) , Style = SKPaintStyle.Fill };
        using var textPaint = new SKPaint { Color = SKColors.Black , IsAntialias = true };
        using var font = new SKFont(CjkTypeface , TextSize);

        int rowIndex = 0;
        float y = Margin;
        foreach (var row in model.Rows)
        {
            // 每 30 行检查一次取消信号
            if (++rowIndex % 30 == 0)
                cancellationToken.ThrowIfCancellationRequested();

            bool isAisleRow = row.Cells.Count > 0 && row.Cells.All(c => c.IsAisle);
            float rowH = isAisleRow ? AisleRowHeight : CellHeight;
            float x = Margin;

            foreach (var cell in row.Cells)
            {
                var rect = new SKRect(x , y , x + CellWidth , y + rowH);

                var fill = cell.IsPodium ? podiumFill
                    : (cell.IsAisle || isAisleRow) ? aisleFill
                    : seatFill;
                canvas.DrawRect(rect , fill);
                canvas.DrawRect(rect , borderPaint);

                if (!string.IsNullOrEmpty(cell.Text))
                {
                    float textY = y + (rowH / 2) + (TextSize / 3);
                    canvas.DrawText(cell.Text , x + 3 , textY , SKTextAlign.Left , font , textPaint);
                }

                x += CellWidth;
            }

            y += rowH;
        }

        try
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png , 90);
            using var stream = File.OpenWrite(path);
            data.SaveTo(stream);
            _logger.LogInformation("图片座位布局导出完成: {Path}" , path);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "写入图片文件失败: {Path}" , path);
            throw new IOException($"无法写入图片文件，文件可能正在被其他程序占用: {path}" , ex);
        }

        return Task.CompletedTask;
    }
}
