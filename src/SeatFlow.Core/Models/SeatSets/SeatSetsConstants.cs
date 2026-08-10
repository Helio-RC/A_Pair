namespace SeatFlow.Core.Models.SeatSets;

/// <summary>
/// .seatsets 数据包文件格式的全局常量定义。
/// </summary>
public static class SeatSetsConstants
{
    /// <summary>文件扩展名（含点号）。</summary>
    public const string FileExtension = ".seatsets";

    /// <summary>MIME 类型（用于文件关联注册）。</summary>
    public const string MimeType = "application/x-seatflow-seatsets";

    /// <summary>允许导入的最大文件大小（200 MB）。</summary>
    public const long MaxFileSizeBytes = 200 * 1024 * 1024;

    /// <summary>当前 .seatsets 文件格式版本。</summary>
    public const string CurrentFormatVersion = "1.0";

    // 数据类别常量
    public const string CategoryAppSettings = "appSettings";
    public const string CategoryVenues = "venues";
    public const string CategoryRosters = "rosters";
    public const string CategorySnapshots = "snapshots";
    public const string CategoryStrategyConfig = "strategyConfig";

    /// <summary>所有数据类别的有序列表。</summary>
    public static readonly IReadOnlyList<string> AllCategories =
        [CategoryAppSettings , CategoryVenues , CategoryRosters , CategorySnapshots , CategoryStrategyConfig];
}
