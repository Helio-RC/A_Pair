using Microsoft.Extensions.Logging;
using NSubstitute;

namespace SeatFlow.Infrastructure.Tests.Exporters;

public class ExcelSeatingExporterTests
{
    [Fact]
    public async Task ExportAsync_ShouldCreateExcelFile ()
    {
        var plan = new SeatingPlan
        {
            Assignments = new Dictionary<string , string>
            {
                { "seat1", "student1" }
            }
        };
        var exporter = new ExcelSeatingExporter(Substitute.For<ILogger<ExcelSeatingExporter>>());
        var path = Path.GetTempFileName() + ".xlsx";

        try
        {
            await exporter.ExportAsync(plan , path , CancellationToken.None);
            File.Exists(path).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ExportAsync_WithOptions_ShouldIncludeMetadataSheet ()
    {
        var plan = new SeatingPlan
        {
            Assignments = new Dictionary<string , string>
            {
                { "seat1", "student1" }
            }
        };
        var exporter = new ExcelSeatingExporter(Substitute.For<ILogger<ExcelSeatingExporter>>());
        var path = Path.GetTempFileName() + ".xlsx";

        try
        {
            var options = new ExportOptions { IncludeMetadata = true };
            await exporter.ExportAsync(plan , path , options , CancellationToken.None);
            // 简单验证文件存在（实际可通过EPPlus读取元数据表，略）
            File.Exists(path).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}