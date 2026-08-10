using System.Text.Json;
using System.Text.Json.Nodes;
using SeatFlow.Core.Models;
using SeatFlow.Core.Providers;
using SeatFlow.Infrastructure.Migration;
using SeatFlow.Infrastructure.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Infrastructure.Providers
{
    public class JsonAppSettingsRepository (
        string filePath ,
        FileMigrationService migration ,
        ILogger<JsonAppSettingsRepository>? logger = null) : IAppSettingsRepository
    {
        private readonly string _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        private readonly FileMigrationService _migration = migration ?? throw new ArgumentNullException(nameof(migration));
        private readonly ILogger<JsonAppSettingsRepository> _logger = logger ?? NullLogger<JsonAppSettingsRepository>.Instance;

        public string SettingsFilePath => _filePath;

        public async Task<AppSettings> LoadAsync (CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogDebug("AppSettings 文件不存在，使用默认设置：{Path}" , _filePath);
                return new AppSettings();
            }

            var json = await File.ReadAllTextAsync(_filePath , cancellationToken);
            var node = JsonNode.Parse(json);
            if (node is not null)
            {
                var fileVersion = node["version"]?.GetValue<string>() ?? "1.0";
                node = _migration.Migrate("appSettings" , node , fileVersion , FileVersionInfo.GetCurrentVersion("appSettings"));
                json = node.ToJsonString();
            }
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            if (settings is null)
            {
                _logger.LogWarning("AppSettings 反序列化结果为 null：{Path}" , _filePath);
                return new AppSettings();
            }
            _logger.LogInformation("AppSettings 已加载：{Path}" , _filePath);
            return settings;
        }

        public async Task SaveAsync (AppSettings settings , CancellationToken cancellationToken = default)
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            settings.Version = FileVersionInfo.GetCurrentVersion("appSettings");
            var options = JsonOptions.WriteIndented;
            var json = JsonSerializer.Serialize(settings , options);
            await File.WriteAllTextAsync(_filePath , json , cancellationToken);
            _logger.LogInformation("AppSettings 已保存：{Path}" , _filePath);
        }
    }
}