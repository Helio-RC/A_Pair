namespace SeatFlow.Core.Models.SeatSets;

/// <summary>
/// .seatsets 文件导入操作的结果。
/// 采用"尽力而为"策略：单个文件失败不会中断整个导入。
/// </summary>
public class SeatSetsImportResult
{
    /// <summary>整体操作是否成功（无任何错误）。</summary>
    public bool Success => Errors.Count == 0;

    /// <summary>尝试导入的文件总数。</summary>
    public int TotalFiles { get; set; }

    /// <summary>成功恢复的文件数。</summary>
    public int Restored { get; set; }

    /// <summary>跳过的文件数（如哈希不匹配的块）。</summary>
    public int Skipped { get; set; }

    /// <summary>导入失败的文件数（仅统计通过哈希验证但写入失败的文件，跳过的块不计入）。</summary>
    public int Failed => TotalFiles - Restored;

    /// <summary>错误消息列表（每个失败文件一条）。</summary>
    public List<string> Errors { get; set; } = [];
}
