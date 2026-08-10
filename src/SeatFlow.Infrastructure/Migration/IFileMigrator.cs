using System.Text.Json.Nodes;

namespace SeatFlow.Infrastructure.Migration;

/// <summary>
/// 文件版本迁移器接口。每个实现负责一个版本步进（如 1.0 → 1.1）。
/// 仅支持向前迁移，不支持版本回退。
/// </summary>
public interface IFileMigrator
{
    /// <summary>文件类型标识（"venue"、"roster"、"snapshot" 等，不区分大小写）。</summary>
    string FileType { get; }

    /// <summary>迁移起始版本。</summary>
    string FromVersion { get; }

    /// <summary>迁移目标版本。</summary>
    string ToVersion { get; }

    /// <summary>对 JSON 树执行原地迁移，返回迁移后的根节点。</summary>
    JsonNode Migrate (JsonNode root);
}
