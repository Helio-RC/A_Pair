using System.Text.Json;
using System.Text.Json.Nodes;
using SeatFlow.Core.Models;
using SeatFlow.Infrastructure.Migration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Infrastructure.Providers
{
    /// <summary>
    /// 策略按数据集维度的配置仓储。
    /// 每份配置对应一个 (策略, 人员数据集, 会场) 组合，
    /// 存储于 {configDir}/{strategyId}/{filename}.config.json。
    /// </summary>
    public class StrategyDatasetConfigRepository (
        string baseDir ,
        FileMigrationService migration ,
        ILogger<StrategyDatasetConfigRepository>? logger = null)
    {
        private readonly string _baseDir = baseDir ?? throw new ArgumentNullException(nameof(baseDir));
        private readonly FileMigrationService _migration = migration ?? throw new ArgumentNullException(nameof(migration));
        private readonly ILogger<StrategyDatasetConfigRepository> _logger = logger ?? NullLogger<StrategyDatasetConfigRepository>.Instance;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase ,
            PropertyNameCaseInsensitive = true ,
            WriteIndented = true
        };

        /// <summary>
        /// 加载指定策略的所有数据集配置。
        /// </summary>
        public async Task<List<StrategyDatasetConfig>> LoadAllAsync (string strategyId , CancellationToken ct = default)
        {
            var results = new List<StrategyDatasetConfig>();
            var dir = GetStrategyDir(strategyId);
            if (!Directory.Exists(dir))
                return results;

            foreach (var filePath in Directory.EnumerateFiles(dir , "*.config.json"))
            {
                var config = await LoadFileAsync(filePath , ct);
                if (config is not null)
                    results.Add(config);
            }

            _logger.LogDebug("已加载 {Count} 个数据集配置" , results.Count);
            return results;
        }

        /// <summary>
        /// 加载单份数据集配置。
        /// </summary>
        public async Task<StrategyDatasetConfig?> LoadAsync (string strategyId , string datasetId , string? venueId , CancellationToken ct = default)
        {
            var fileName = BuildFileName(datasetId , venueId);
            var filePath = Path.Combine(GetStrategyDir(strategyId) , fileName);
            var config = await LoadFileAsync(filePath , ct);
            _logger.LogDebug("数据集配置已加载: {StrategyId}/{FileName}" , strategyId , fileName);
            return config;
        }

        /// <summary>
        /// 保存一份数据集配置，自动计算哈希。
        /// </summary>
        public async Task SaveAsync (
            StrategyDatasetConfig config ,
            string? studentHash ,
            string? venueHash ,
            CancellationToken ct = default)
        {
            var dir = GetStrategyDir(config.StrategyId);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            config.Version = FileVersionInfo.GetCurrentVersion("strategyDatasetConfig");
            config.StudentsHash = studentHash;
            config.ContentHash = venueHash;

            var fileName = BuildFileName(config.DatasetId , config.VenueId);
            var filePath = Path.Combine(dir , fileName);
            var json = JsonSerializer.Serialize(config , JsonOptions);
            await File.WriteAllTextAsync(filePath , json , ct);
            _logger.LogInformation("数据集配置已保存：{StrategyId}/{FileName}" , config.StrategyId , fileName);
        }

        /// <summary>
        /// 删除一份数据集配置。
        /// </summary>
        public async Task DeleteAsync (string strategyId , string datasetId , string? venueId , CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var fileName = BuildFileName(datasetId , venueId);
            var filePath = Path.Combine(GetStrategyDir(strategyId) , fileName);
            if (File.Exists(filePath))
                await Task.Run(() => File.Delete(filePath) , ct);
            _logger.LogInformation("数据集配置已删除: {StrategyId}/{FileName}" , strategyId , fileName);
        }

        private async Task<StrategyDatasetConfig?> LoadFileAsync (string filePath , CancellationToken ct)
        {
            if (!File.Exists(filePath))
                return null;

            var json = await File.ReadAllTextAsync(filePath , ct);
            var node = JsonNode.Parse(json);
            if (node is not null)
            {
                var fileVersion = node["version"]?.GetValue<string>() ?? "1.0";
                node = _migration.Migrate("strategyDatasetConfig" , node , fileVersion ,
                    FileVersionInfo.GetCurrentVersion("strategyDatasetConfig"));
                json = node.ToJsonString();
            }
            return JsonSerializer.Deserialize<StrategyDatasetConfig>(json , JsonOptions);
        }

        private string GetStrategyDir (string strategyId)
        {
            Sanitize(strategyId);
            return Path.Combine(_baseDir , strategyId);
        }

        /// <summary>
        /// 根据数据类型构建文件名。
        /// - 仅 Student: {datasetId}.config.json
        /// - 仅 Venue:   {venueId}.config.json
        /// - Both:       {firstHalf(datasetId)}-{firstHalf(venueId)}.config.json
        /// </summary>
        private static string BuildFileName (string? datasetId , string? venueId)
        {
            if (!string.IsNullOrEmpty(datasetId))
                Sanitize(datasetId);
            if (!string.IsNullOrEmpty(venueId))
                Sanitize(venueId);

            if (!string.IsNullOrEmpty(datasetId) && !string.IsNullOrEmpty(venueId))
            {
                var dHalf = datasetId[..((datasetId.Length / 2) + 1)];
                var vHalf = venueId[..((venueId.Length / 2) + 1)];
                return $"{dHalf}-{vHalf}.config.json";
            }
            if (!string.IsNullOrEmpty(datasetId))
                return $"{datasetId}.config.json";
            if (!string.IsNullOrEmpty(venueId))
                return $"{venueId}.config.json";
            return "default.config.json";
        }

        private static void Sanitize (string id)
        {
            if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || id.Contains(Path.DirectorySeparatorChar)
                || id.Contains(Path.AltDirectorySeparatorChar))
                throw new ArgumentException($"ID 含非法字符: {id}");
        }
    }
}
