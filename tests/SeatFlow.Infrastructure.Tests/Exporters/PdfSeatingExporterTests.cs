namespace SeatFlow.Infrastructure.Tests.Exporters;

public class PdfSeatingExporterTests
{
    [Fact]
    public async Task ExportAsync_ShouldCreatePdfFile ()
    {
        var plan = new SeatingPlan
        {
            Assignments = new Dictionary<string , string>
            {
                { "seat1", "student1" }
            }
        };
        var exporter = new PdfSeatingExporter();
        var path = Path.GetTempFileName() + ".pdf";

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
}