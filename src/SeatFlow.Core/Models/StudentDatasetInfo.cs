namespace SeatFlow.Core.Models;

public class StudentDatasetInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? OriginalFileName { get; set; }
    public int StudentCount { get; set; }
    public DateTime CreatedAt { get; set; }
}
