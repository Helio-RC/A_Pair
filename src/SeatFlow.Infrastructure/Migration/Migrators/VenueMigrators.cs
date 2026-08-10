using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Infrastructure.Migration.Migrators;

/// <summary>
/// Venue 文件（.venue.json）各版本迁移器。
/// </summary>
public static class VenueMigrators
{
    /// <summary>
    /// 1.0 → 1.1：将 Grid 布局座位的 JSON 数组从列主序重排为行主序（按 Row → Column 排序）。
    /// </summary>
    public sealed class Step_1_0_to_1_1 (ILogger<VenueMigrators.Step_1_0_to_1_1>? logger = null) : IFileMigrator
    {
        private readonly ILogger<Step_1_0_to_1_1> _logger = logger ?? NullLogger<Step_1_0_to_1_1>.Instance;

        public string FileType => "venue";
        public string FromVersion => "1.0";
        public string ToVersion => "1.1";

        public JsonNode Migrate (JsonNode root)
        {
            var layoutStr = root["layout"]?["layoutTypeString"]?.ToString();
            if (layoutStr != "Grid")
            {
                root["version"] = "1.1";
                return root;
            }

            var seats = root["layout"]?["seats"]?.AsArray();
            if (seats is null || seats.Count <= 1)
            {
                root["version"] = "1.1";
                return root;
            }

            static int SafeInt (JsonNode? node , int fallback)
                => node is JsonValue v && v.TryGetValue<int>(out var n) ? n : fallback;

            var sorted = seats
                .OfType<JsonObject>()
                .Select(s => new
                {
                    Node = s ,
                    Row = SafeInt(s["row"] , int.MaxValue) ,
                    Column = SafeInt(s["column"] , int.MaxValue)
                })
                .OrderBy(s => s.Row)
                .ThenBy(s => s.Column)
                .Select(s => s.Node)
                .ToList();

            seats.Clear();
            foreach (var s in sorted)
                seats.Add(s);

            root["version"] = "1.1";
            _logger.LogInformation("Venue 座位已重排为行主序，版本 1.0 → 1.1");
            return root;
        }
    }
}
