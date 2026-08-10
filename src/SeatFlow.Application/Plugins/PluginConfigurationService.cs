using System.Text.Json;
using SeatFlow.Contracts.Interfaces;
using SeatFlow.Infrastructure.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Application.Plugins
{
    public class PluginConfigurationService (string pluginsBasePath , ILogger<PluginConfigurationService>? logger = null) : IPluginConfigurationService
    {
        private readonly string _pluginsBasePath = pluginsBasePath;
        private readonly Dictionary<string , FileSystemWatcher> _watchers = [];
        private readonly ILogger<PluginConfigurationService> _logger = logger ?? NullLogger<PluginConfigurationService>.Instance;

        /// <inheritdoc />
        public async Task<T?> LoadConfigurationAsync<T> (string pluginId , CancellationToken cancellationToken = default) where T : class, new()
        {
            var configPath = GetConfigPath(pluginId);
            if (!File.Exists(configPath))
            {
                _logger.LogDebug("插件配置文件不存在：{PluginId}，返回默认配置" , pluginId);
                return new T();
            }

            var json = await File.ReadAllTextAsync(configPath , cancellationToken);
            _logger.LogInformation("插件配置已加载：{PluginId}" , pluginId);
            return JsonSerializer.Deserialize<T>(json);
        }

        /// <inheritdoc />
        public async Task SaveConfigurationAsync<T> (string pluginId , T configuration , CancellationToken cancellationToken = default) where T : class
        {
            var configPath = GetConfigPath(pluginId);
            var configDir = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
                Directory.CreateDirectory(configDir);

            var json = JsonSerializer.Serialize(configuration , JsonOptions.WriteIndented);
            await File.WriteAllTextAsync(configPath , json , cancellationToken);
            _logger.LogInformation("插件配置已保存：{PluginId}" , pluginId);
        }

        /// <inheritdoc />
        public void WatchConfiguration (string pluginId , Action<string> onChange)
        {
            var pluginDir = Path.Combine(_pluginsBasePath , pluginId);
            if (!Directory.Exists(pluginDir))
                return;

            // 替换前释放旧监视器
            if (_watchers.TryGetValue(pluginId , out var existing))
                existing.Dispose();

            var watcher = new FileSystemWatcher(pluginDir , "config.json")
            {
                NotifyFilter = NotifyFilters.LastWrite
            };
            watcher.Changed += (sender , e) => onChange(pluginId);
            watcher.EnableRaisingEvents = true;
            _watchers[pluginId] = watcher;
        }

        /// <inheritdoc />
        public void StopWatching (string pluginId)
        {
            if (_watchers.TryGetValue(pluginId , out var watcher))
            {
                watcher.Dispose();
                _watchers.Remove(pluginId);
            }
        }

        /// <summary>
        /// 获取指定插件配置文件的完整路径。
        /// </summary>
        /// <param name="pluginId">插件唯一标识符。</param>
        /// <returns>配置文件的完整路径。</returns>
        private string GetConfigPath (string pluginId)
        {
            return Path.Combine(_pluginsBasePath , pluginId , "config.json");
        }
    }
}