using System.Globalization;
using SeatFlow.Core.Exporters;
using SeatFlow.Core.Models;
using SeatFlow.Core.Workspace;
using CsvHelper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Infrastructure.Exporters
{
    public class CsvSeatingExporter (ILogger<CsvSeatingExporter>? logger = null) : ISeatingPlanExporter
    {
        private readonly ILogger<CsvSeatingExporter> _logger = logger ?? NullLogger<CsvSeatingExporter>.Instance;

        public ExportFormat Format => ExportFormat.Csv;
        public async Task ExportAsync (SeatingPlan plan , string path , CancellationToken cancellationToken = default)
        {
            await ExportAsync(plan , path , new ExportOptions { Format = ExportFormat.Csv } , cancellationToken);
        }

        public async Task ExportAsync (SeatingPlan plan , string path , ExportOptions options , CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("CSV 座位导出开始：{Path}（{Count} 条记录）" , path , plan.Assignments.Count);
            var records = plan.Assignments.Select(kv => new SeatAssignmentRecord
            {
                SeatId = kv.Key ,
                StudentId = options.Anonymize ? "***" : kv.Value
            }).ToList();

            await using var writer = new StreamWriter(path , false , new System.Text.UTF8Encoding(true));
            await using var csv = new CsvWriter(writer , CultureInfo.InvariantCulture);
            await csv.WriteRecordsAsync(records , cancellationToken);
            _logger.LogInformation("CSV 座位导出完成: {Path}，{Count} 条记录" , path , plan.Assignments.Count);
        }

        public async Task ExportLayoutAsync (LayoutSeatingExportModel model , string path , ExportOptions options , CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation("CSV 布局导出开始: {Path}" , path);

            await using var writer = new StreamWriter(path , false , new System.Text.UTF8Encoding(true));
            await writer.WriteLineAsync($"# {model.LayoutName}");
            foreach (var row in model.Rows)
                await writer.WriteLineAsync(string.Join("," , row.Cells.Select(c => EscapeCsv(c.Text))));
            await writer.FlushAsync(cancellationToken);
            _logger.LogInformation("CSV 布局导出完成: {Path}" , path);
        }

        private static string EscapeCsv (string text)
        {
            if (text.Contains(',') || text.Contains('"') || text.Contains('\n'))
                return $"\"{text.Replace("\"" , "\"\"")}\"";
            return text;
        }

        private class SeatAssignmentRecord
        {
            public string SeatId { get; set; } = string.Empty;
            public string StudentId { get; set; } = string.Empty;
        }
    }
}
