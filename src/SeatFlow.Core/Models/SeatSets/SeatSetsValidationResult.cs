namespace SeatFlow.Core.Models.SeatSets;

/// <summary>
/// .seatsets 文件校验结果。
/// </summary>
public class SeatSetsValidationResult
{
    /// <summary>文件是否通过校验（大小、JSON 结构、哈希均正常）。</summary>
    public bool IsValid { get; set; }

    /// <summary>文件大小（字节）。</summary>
    public long FileSize { get; set; }

    /// <summary>文件格式版本。</summary>
    public string? Version { get; set; }

    /// <summary>创建时的应用版本。</summary>
    public string? AppVersion { get; set; }

    /// <summary>归档哈希是否匹配。</summary>
    public bool ArchiveHashValid { get; set; }

    /// <summary>文件中实际包含的数据类别列表。</summary>
    public List<string> AvailableCategories { get; set; } = [];

    /// <summary>校验错误消息列表。</summary>
    public List<string> ValidationErrors { get; set; } = [];
}
