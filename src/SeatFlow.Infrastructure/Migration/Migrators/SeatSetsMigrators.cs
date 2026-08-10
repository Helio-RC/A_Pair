using System.Text.Json.Nodes;

namespace SeatFlow.Infrastructure.Migration.Migrators;

/// <summary>
/// .seatsets 文件格式的版本迁移器集合。
/// 当前仅含占位迁移器，为未来格式升级预留扩展点。
/// </summary>
public static class SeatSetsMigrators
{
    /// <summary>
    /// 占位迁移器：1.0 → 1.1（当前为 no-op，结构未变）。
    /// 未来若格式变更（如新增字段、改变 chunk 结构），在此实现迁移逻辑。
    /// </summary>
    public sealed class Step_1_0_to_1_1 : IFileMigrator
    {
        public string FileType => "seatsets";
        public string FromVersion => "1.0";
        public string ToVersion => "1.1";

        public JsonNode Migrate (JsonNode root)
        {
            // 当前格式无变更，仅更新版本号
            root["formatVersion"] = "1.1";
            return root;
        }
    }
}
