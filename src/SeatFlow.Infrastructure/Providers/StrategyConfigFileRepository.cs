using System.Text.Json;
using System.Text.Json.Nodes;
using SeatFlow.Core.Models;
using SeatFlow.Infrastructure.Migration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Infrastructure.Providers
{
    /// <param name="configDir">配置目录路径。</param>
    /// <param name="migration">文件版本迁移服务。</param>
    /// <param name="logger">日志记录器。</param>
    public class StrategyConfigFileRepository (
        string configDir ,
        FileMigrationService migration ,
        ILogger<StrategyConfigFileRepository>? logger = null)
    {
        private readonly string _configDir = configDir ?? throw new ArgumentNullException(nameof(configDir));
        private readonly ILogger<StrategyConfigFileRepository> _logger = logger ?? NullLogger<StrategyConfigFileRepository>.Instance;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true ,
            WriteIndented = true
        };

        public async Task<StrategyConfig?> LoadAsync (string strategyId , CancellationToken ct = default)
        {
            var filePath = GetFilePath(strategyId);
            if (!File.Exists(filePath))
                return null;

            var json = await File.ReadAllTextAsync(filePath , ct);
            var config = DeserializeWithMigration(json);
            _logger.LogInformation("策略配置已加载: {StrategyId}" , strategyId);
            return config;
        }

        public async Task SaveAsync (string strategyId , StrategyConfig config , CancellationToken ct = default)
        {
            if (!Directory.Exists(_configDir))
                Directory.CreateDirectory(_configDir);

            config.Version = FileVersionInfo.GetCurrentVersion("strategyConfig");
            var filePath = GetFilePath(strategyId);
            var json = JsonSerializer.Serialize(config , JsonOptions);
            await File.WriteAllTextAsync(filePath , json , ct);
            _logger.LogInformation("策略配置已保存：{Id} → {Path}" , strategyId , filePath);
        }

        public async Task<Dictionary<string , StrategyConfig>> LoadAllAsync (CancellationToken ct = default)
        {
            var results = new Dictionary<string , StrategyConfig>();
            if (!Directory.Exists(_configDir))
                return results;

            foreach (var filePath in Directory.EnumerateFiles(_configDir , "*.config.json"))
            {
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                var strategyId = Path.GetFileNameWithoutExtension(fileName);

                var json = await File.ReadAllTextAsync(filePath , ct);
                var config = DeserializeWithMigration(json);
                if (config is not null)
                    results[strategyId] = config;
            }

            _logger.LogDebug("已加载 {Count} 个策略配置" , results.Count);
            return results;
        }

        private StrategyConfig? DeserializeWithMigration (string json)
        {
            var node = JsonNode.Parse(json);
            if (node is not null)
            {
                var fileVersion = node["version"]?.GetValue<string>() ?? "1.0";
                node = migration.Migrate("strategyConfig" , node , fileVersion , FileVersionInfo.GetCurrentVersion("strategyConfig"));
                json = node.ToJsonString();
            }
            return JsonSerializer.Deserialize<StrategyConfig>(json , JsonOptions);
        }

        private string GetFilePath (string strategyId)
        {
            if (strategyId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || strategyId.Contains(Path.DirectorySeparatorChar)
                || strategyId.Contains(Path.AltDirectorySeparatorChar))
                throw new ArgumentException($"策略 ID 含非法字符: {strategyId}");
            return Path.Combine(_configDir , $"{strategyId}.config.json");
        }
    }
}
