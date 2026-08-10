namespace SeatFlow.Core.Models;

/// <summary>
/// 快照目录下的会场摘要，便于历史快照页面无需加载原会场文件即可展示关键信息。
/// </summary>
public class VenueSnapshotInfo
{
    /// <summary>文件格式版本号。</summary>
    public string Version { get; set; } = "1.0";

    public string Name { get; set; } = string.Empty;
    public LayoutType LayoutType { get; set; }
    public int SeatCount { get; set; }
    public int ObstacleCount { get; set; }
}
