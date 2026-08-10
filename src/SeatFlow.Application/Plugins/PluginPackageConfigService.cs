using System.Text.Json;
using System.Text.Json.Nodes;
using SeatFlow.Core.Models;
using SeatFlow.Infrastructure.Migration;
using Microsoft.Extensions.Logging;

namespace SeatFlow.Application.Plugins
{
    /// <summary>
    /// 插件包策略的配置读写服务，管理插件策略在 <c>Plugins/{packageId}/{strategyPath}/</c> 下的配置。
    /// 复用与内置策略配置相同的序列化逻辑（版本号、迁移管道、哈希计算），仅基路径不同。
    /// </summary>
    /// <remarks>
    /// <para>配置路径对照：</para>
    /// <list type="bullet">
    ///   <item>运行时配置：<c>Plugins/{pkgId}/{strategyPath}/{strategyId}.config.json</c></item>
    ///   <item>数据集配置：<c>Plugins/{pkgId}/{strategyPath}/{strategyId}/{...}.config.json</c></item>
    /// </list>
    /// </remarks>
    public class PluginPackageConfigService (
        IPluginManager pluginManager ,
        FileMigrationService migration ,
        ILogger<PluginPackageConfigService> logger)
    {
        private readonly IPluginManager _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
        private readonly FileMigrationService _migration = migration ?? throw new ArgumentNullException(nameof(migration));
        private readonly ILogger<PluginPackageConfigService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true ,
            WriteIndented = true
        };

        private static readonly JsonSerializerOptions DatasetJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase ,
            PropertyNameCaseInsensitive = true ,
            WriteIndented = true
        };

        /// <summary>
        /// 加载插件策略的运行时配置（优先级、启用状态、参数）。
        /// </summary>
        public async Task<StrategyConfig?> LoadConfigAsync (string strategyId , CancellationToken ct = default)
        {
            var filePath = GetConfigFilePath(strategyId);
            if (filePath == null || !File.Exists(filePath))
                return null;

            var json = await File.ReadAllTextAsync(filePath , ct);
            return DeserializeConfigWithMigration(json);
        }

        /// <summary>
        /// 保存插件策略的运行时配置。
        /// </summary>
        public async Task SaveConfigAsync (string strategyId , StrategyConfig config , CancellationToken ct = default)
        {
            var filePath = GetConfigFilePath(strategyId) ?? throw new InvalidOperationException($"无法确定插件策略 {strategyId} 的配置路径");
            var dir = Path.GetDirectoryName(filePath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            config.Version = FileVersionInfo.GetCurrentVersion("strategyConfig");
            var json = JsonSerializer.Serialize(config , JsonOptions);
            await File.WriteAllTextAsync(filePath , json , ct);
            _logger.LogDebug("插件策略配置已保存：{Id} → {Path}" , strategyId , filePath);
        }

        /// <summary>
        /// 加载指定插件策略的所有数据集配置。
        /// </summary>
        public async Task<List<StrategyDatasetConfig>> LoadDatasetConfigsAsync (string strategyId , CancellationToken ct = default)
        {
            var results = new List<StrategyDatasetConfig>();
            var dir = GetDatasetConfigDir(strategyId);
            if (dir == null || !Directory.Exists(dir))
                return results;

            foreach (var filePath in Directory.EnumerateFiles(dir , "*.config.json"))
            {
                var config = await LoadDatasetFileAsync(filePath , ct);
                if (config is not null)
                    results.Add(config);
            }

            return results;
        }

        /// <summary>
        /// 加载单份数据集配置。
        /// </summary>
        public async Task<StrategyDatasetConfig?> LoadDatasetConfigAsync (
            string strategyId , string datasetId , string? venueId , CancellationToken ct = default)
        {
            var dir = GetDatasetConfigDir(strategyId);
            if (dir == null) return null;

            var fileName = BuildFileName(datasetId , venueId);
            var filePath = Path.Combine(dir , fileName);
            return await LoadDatasetFileAsync(filePath , ct);
        }

        /// <summary>
        /// 保存一份数据集配置，自动写入哈希。
        /// </summary>
        public async Task SaveDatasetConfigAsync (
            StrategyDatasetConfig config ,
            string? studentHash ,
            string? venueHash ,
            CancellationToken ct = default)
        {
            var dir = GetDatasetConfigDir(config.StrategyId) ?? throw new InvalidOperationException($"无法确定插件策略 {config.StrategyId} 的配置路径");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            config.Version = FileVersionInfo.GetCurrentVersion("strategyDatasetConfig");
            config.StudentsHash = studentHash;
            config.ContentHash = venueHash;

            var fileName = BuildFileName(config.DatasetId , config.VenueId);
            var filePath = Path.Combine(dir , fileName);
            var json = JsonSerializer.Serialize(config , DatasetJsonOptions);
            await File.WriteAllTextAsync(filePath , json , ct);
            _logger.LogDebug("插件数据集配置已保存：{StrategyId}/{FileName}" , config.StrategyId , fileName);
        }

        /// <summary>
        /// 删除一份数据集配置。
        /// </summary>
        public async Task DeleteDatasetConfigAsync (
            string strategyId , string datasetId , string? venueId , CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var dir = GetDatasetConfigDir(strategyId);
            if (dir == null) return;

            var fileName = BuildFileName(datasetId , venueId);
            var filePath = Path.Combine(dir , fileName);
            if (File.Exists(filePath))
                await Task.Run(() => File.Delete(filePath) , ct);
        }

        // ─── 路径计算 ───

        /// <summary>
        /// 计算插件策略的运行时配置文件路径。
        /// 格式：Plugins/{packageId}/{strategyPath}/{strategyId}.config.json
        /// </summary>
        private string? GetConfigFilePath (string strategyId)
        {
            var (pkg , plugin) = _pluginManager.FindStrategy(strategyId);
            if (pkg == null) return null;

            var strategyPath = plugin?.Entry?.Path ?? string.Empty;
            var configDir = string.IsNullOrEmpty(strategyPath)
                ? pkg.PackagePath
                : Path.Combine(pkg.PackagePath , strategyPath);

            return Path.Combine(configDir , $"{strategyId}.config.json");
        }

        /// <summary>
        /// 计算插件策略的数据集配置目录路径。
        /// 格式：Plugins/{packageId}/{strategyPath}/{strategyId}/
        /// </summary>
        private string? GetDatasetConfigDir (string strategyId)
        {
            var (pkg , plugin) = _pluginManager.FindStrategy(strategyId);
            if (pkg == null) return null;

            var strategyPath = plugin?.Entry?.Path ?? string.Empty;
            var baseDir = string.IsNullOrEmpty(strategyPath)
                ? pkg.PackagePath
                : Path.Combine(pkg.PackagePath , strategyPath);

            return Path.Combine(baseDir , strategyId);
        }

        // ─── 序列化与迁移 ───

        private StrategyConfig? DeserializeConfigWithMigration (string json)
        {
            var node = JsonNode.Parse(json);
            if (node is not null)
            {
                var fileVersion = node["version"]?.GetValue<string>() ?? "1.0";
                node = _migration.Migrate("strategyConfig" , node , fileVersion ,
                    FileVersionInfo.GetCurrentVersion("strategyConfig"));
                json = node.ToJsonString();
            }
            return JsonSerializer.Deserialize<StrategyConfig>(json , JsonOptions);
        }

        private async Task<StrategyDatasetConfig?> LoadDatasetFileAsync (string filePath , CancellationToken ct)
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
            return JsonSerializer.Deserialize<StrategyDatasetConfig>(json , DatasetJsonOptions);
        }

        /// <summary>
        /// 根据数据类型构建数据集配置文件名。
        /// 复用与 <see cref="SeatFlow.Infrastructure.Providers.StrategyDatasetConfigRepository"/> 相同的命名规则。
        /// </summary>
        private static string BuildFileName (string? datasetId , string? venueId)
        {
            Sanitize(datasetId);
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

        private static void Sanitize (string? id)
        {
            if (id == null) return;
            if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || id.Contains(Path.DirectorySeparatorChar)
                || id.Contains(Path.AltDirectorySeparatorChar))
                throw new ArgumentException($"ID 含非法字符: {id}");
        }
    }
}
