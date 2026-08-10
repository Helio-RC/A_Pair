namespace SeatFlow.Core.Models.SeatSets;

/// <summary>
/// .seatsets 数据包文件的顶层模型。
/// 分块记录各类应用数据，每块有独立的哈希校验，顶层有整体哈希。
/// </summary>
public class SeatSetsArchive
{
    /// <summary>文件格式版本（"1.0"）。</summary>
    public string Version { get; set; } = "1.0";

    /// <summary>创建时的应用版本号。</summary>
    public string AppVersion { get; set; } = string.Empty;

    /// <summary>创建时间（ISO 8601 UTC）。</summary>
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");

    /// <summary>用户可选的描述信息。</summary>
    public string? Description { get; set; }

    /// <summary>
    /// 数据块字典。Key 为类别名（见 <see cref="SeatSetsConstants"/> 的 Category 常量），
    /// Value 为该类别下所有文件的 JSON 内容。
    /// </summary>
    public Dictionary<string , SeatSetsChunk> Chunks { get; set; } = [];

    /// <summary>
    /// 整体归档哈希（SHA256，覆盖除自身外的所有字段）。
    /// 验证时重新计算并与该值比对，确保文件未被篡改。
    /// </summary>
    public string? ArchiveHash { get; set; }
}
