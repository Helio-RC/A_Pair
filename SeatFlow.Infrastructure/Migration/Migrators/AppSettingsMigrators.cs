using System.Text.Json.Nodes;

namespace SeatFlow.Infrastructure.Migration.Migrators;

/// <summary>
/// AppSettings 文件（AppSettings.json）各版本迁移器。
/// </summary>
public static class AppSettingsMigrators
{
    /// <summary>
    /// 1.0 → 1.1：添加遥测配置节（默认关闭）。
    /// </summary>
    public sealed class Step_1_0_to_1_1 : IFileMigrator
    {
        public string FileType => "appSettings";
        public string FromVersion => "1.0";
        public string ToVersion => "1.1";

        public JsonNode Migrate(JsonNode root)
        {
            // 如果 telemetry 节点已存在则跳过（可能来自旧版手动创建）
            if (root["telemetry"] is null)
            {
                root["telemetry"] = new JsonObject
                {
                    ["enabled"] = false,
                    ["consentShown"] = false,
                    ["serverUrl"] = "https://seatflow.work/api/app/telemetry",
                    ["flushIntervalSeconds"] = 60,
                    ["maxBatchSize"] = 100,
                    ["pageViewSampleRate"] = 0.2,
                    ["pageViewCoalesceSeconds"] = 60,
                    ["metricSnapshotIntervalSeconds"] = 120,
                    ["enableCompression"] = true
                };
            }

            root["version"] = "1.1";
            return root;
        }
    }
}
