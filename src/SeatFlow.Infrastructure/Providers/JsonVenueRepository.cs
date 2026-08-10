using System.Text.Json;
using System.Text.Json.Nodes;
using SeatFlow.Core.Models;
using SeatFlow.Core.Providers;
using SeatFlow.Infrastructure.Migration;
using SeatFlow.Infrastructure.Serialization;
using SeatFlow.Infrastructure.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Infrastructure.Providers
{
    /// <summary>
    /// JSON 格式的会场（场地）仓储，将 <see cref="ClassroomLayoutDefinition"/> 以 JSON 格式持久化到本地文件。
    /// </summary>
    /// <remarks>
    /// 每个会场保存为独立的 <c>*.venue.json</c> 文件，文件名格式为 <c><venueId>.venue.json</c>。
    /// 使用 <see cref="SeatJsonConverter"/> 支持 <see cref="Seat"/> 派生类的多态序列化。
    /// </remarks>
    public class JsonVenueRepository : IVenueRepository
    {
        private readonly string _venuesFolder;
        private readonly FileMigrationService _migration;
        private readonly ILogger<JsonVenueRepository> _logger;

        /// <summary>
        /// 初始化 JSON 会场仓储，确保存储目录存在。
        /// </summary>
        public JsonVenueRepository (
            string venuesFolder ,
            FileMigrationService migration ,
            ILogger<JsonVenueRepository>? logger = null)
        {
            _venuesFolder = venuesFolder ?? throw new ArgumentNullException(nameof(venuesFolder));
            _migration = migration ?? throw new ArgumentNullException(nameof(migration));
            _logger = logger ?? NullLogger<JsonVenueRepository>.Instance;
            Directory.CreateDirectory(_venuesFolder);
        }

        /// <inheritdoc />
        public async Task SaveAsync (string venueId , ClassroomLayoutDefinition layout , CancellationToken cancellationToken = default)
        {
            var filePath = GetFilePath(venueId);
            var venueFile = new VenueFile
            {
                Version = FileVersionInfo.GetCurrentVersion("venue") ,
                VenueId = venueId ,
                Layout = layout
            };
            var options = SerializerOptions;
            // 首次序列化（不含哈希）用于计算内容哈希
            var json = JsonSerializer.Serialize(venueFile , options);
            venueFile.ContentHash = ContentHashHelper.ComputeSha256(json);
            // 二次序列化（含哈希）用于保存
            json = JsonSerializer.Serialize(venueFile , options);
            await File.WriteAllTextAsync(filePath , json , cancellationToken);
            _logger.LogInformation("会场已保存：{VenueId} → {Path}" , venueId , filePath);
        }

        /// <inheritdoc />
        public async Task<ClassroomLayoutDefinition?> LoadAsync (string venueId , CancellationToken cancellationToken = default)
        {
            var filePath = GetFilePath(venueId);
            if (!File.Exists(filePath))
                return null;

            var json = await File.ReadAllTextAsync(filePath , cancellationToken);
            var node = JsonNode.Parse(json);
            if (node is not null)
            {
                var fileVersion = node["version"]?.GetValue<string>() ?? "1.0";
                node = _migration.Migrate("venue" , node , fileVersion , FileVersionInfo.GetCurrentVersion("venue"));
                json = node.ToJsonString();
            }
            var options = SerializerOptions;
            var venueFile = JsonSerializer.Deserialize<VenueFile>(json , options);
            _logger.LogInformation("场馆已加载: {VenueId}" , venueId);
            return venueFile?.Layout;
        }

        /// <inheritdoc />
        public async Task<string?> GetContentHashAsync (string venueId , CancellationToken ct = default)
        {
            var filePath = GetFilePath(venueId);
            if (!File.Exists(filePath)) return null;
            var json = await File.ReadAllTextAsync(filePath , ct);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("contentHash" , out var h) ? h.GetString() : null;
        }

        /// <inheritdoc />
        public async Task<string?> GetRawVenueFileAsync (string venueId , CancellationToken ct = default)
        {
            var filePath = GetFilePath(venueId);
            if (!File.Exists(filePath)) return null;
            return await File.ReadAllTextAsync(filePath , ct);
        }

        /// <inheritdoc />
        public Task DeleteAsync (string venueId , CancellationToken cancellationToken = default)
        {
            var filePath = GetFilePath(venueId);
            if (File.Exists(filePath))
                File.Delete(filePath);
            _logger.LogInformation("场馆已删除: {VenueId}" , venueId);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<IEnumerable<string>> ListVenueIdsAsync (CancellationToken cancellationToken = default)
        {
            var files = Directory.GetFiles(_venuesFolder , "*.venue.json");
            var ids = files.Select(f => Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(f)));
            _logger.LogDebug("列出 {Count} 个场馆 ID" , ids.Count());
            return Task.FromResult(ids);
        }

        /// <summary>
        /// 获取指定会场的文件路径。
        /// </summary>
        /// <param name="venueId">会场 ID。</param>
        /// <returns>会场文件的完整路径。</returns>
        private string GetFilePath (string venueId)
        {
            if (venueId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || venueId.Contains(Path.DirectorySeparatorChar)
                || venueId.Contains(Path.AltDirectorySeparatorChar))
                throw new ArgumentException($"会场 ID 含非法字符: {venueId}");
            return Path.Combine(_venuesFolder , $"{venueId}.venue.json");
        }

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true ,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        static JsonVenueRepository ()
        {
            SerializerOptions.Converters.Add(new SeatJsonConverter());
        }
    }
}