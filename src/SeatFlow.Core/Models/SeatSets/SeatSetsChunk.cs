using System.Text.Json;

namespace SeatFlow.Core.Models.SeatSets;

/// <summary>
/// 单个数据块，对应一种数据类别（如 venues、rosters）。
/// 每块独立哈希校验，一个块的损坏不影响其他块的导入。
/// </summary>
public class SeatSetsChunk
{
    /// <summary>该块的 SHA256 哈希（覆盖 <see cref="Files"/> 的确定性 JSON）。</summary>
    public string? Hash { get; set; }

    /// <summary>
    /// 文件字典。Key 为相对于 AppData 根目录的文件路径（使用 "/" 分隔），
    /// Value 为文件的 JSON 内容。
    /// </summary>
    public Dictionary<string , JsonElement> Files { get; set; } = [];
}
