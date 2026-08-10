using System.Text.Json;
using System.Text.Json.Nodes;
using SeatFlow.Core.Models;
using SeatFlow.Core.Providers;
using SeatFlow.Infrastructure.Migration;
using SeatFlow.Infrastructure.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Infrastructure.Repositories
{
    public class SeatingSnapshotRepository (
        string basePath ,
        FileMigrationService migration ,
        ILogger<SeatingSnapshotRepository>? logger = null) : ISeatingSnapshotRepository
    {
        private readonly string _basePath = basePath;
        private readonly FileMigrationService _migration = migration ?? throw new ArgumentNullException(nameof(migration));
        private readonly Dictionary<string , string> _index = [];
        private bool _indexBuilt;
        private readonly Lock _indexLock = new();
        private readonly ILogger<SeatingSnapshotRepository> _logger = logger ?? NullLogger<SeatingSnapshotRepository>.Instance;

        private static string GetFilePath (string venueId , DateTime date , string id)
            => Path.Combine(venueId , date.ToString("yyyyMMdd") , id + ".json");

        private void BuildIndex ()
        {
            if (_indexBuilt) return;
            lock (_indexLock)
            {
                if (_indexBuilt) return;
                foreach (var venueDir in SafeEnumerateDirectories(_basePath))
                {
                    foreach (var dateDir in SafeEnumerateDirectories(venueDir))
                    {
                        foreach (var file in Directory.EnumerateFiles(dateDir , "*.json"))
                        {
                            var id = Path.GetFileNameWithoutExtension(file);
                            if (!_index.ContainsKey(id))
                                _index[id] = file;
                        }
                    }
                }
                _indexBuilt = true;
            }
        }

        public async Task SaveAsync (SeatingSnapshot snapshot , CancellationToken ct = default)
        {
            BuildIndex();
            var dir = Path.Combine(_basePath , snapshot.LayoutId , snapshot.CreatedAt.ToString("yyyyMMdd"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(_basePath , GetFilePath(snapshot.LayoutId , snapshot.CreatedAt , snapshot.Id));
            snapshot.Version = FileVersionInfo.GetCurrentVersion("snapshot");
            var json = JsonSerializer.Serialize(snapshot , JsonOptions.WriteIndented);
            await File.WriteAllTextAsync(path , json , ct);
            lock (_indexLock)
            {
                _index[snapshot.Id] = path;
            }
            _logger.LogInformation("快照已保存：{SnapshotId} → {Path}" , snapshot.Id , path);
        }

        public async Task SaveVenueInfoAsync (string venueId , VenueSnapshotInfo info , CancellationToken ct = default)
        {
            var dir = Path.Combine(_basePath , venueId);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir , "_venue.json");
            info.Version = FileVersionInfo.GetCurrentVersion("venueInfo");
            var json = JsonSerializer.Serialize(info , JsonOptions.WriteIndented);
            await File.WriteAllTextAsync(path , json , ct);
            _logger.LogInformation("会场快照信息已保存: {VenueId}" , venueId);
        }

        public SeatingSnapshot? Load (string id)
        {
            BuildIndex();
            if (_index.TryGetValue(id , out var path) && File.Exists(path))
                return DeserializeWithMigration(File.ReadAllText(path) , "snapshot");
            return null;
        }

        public async Task<SeatingSnapshot?> LoadAsync (string id , CancellationToken ct = default)
        {
            BuildIndex();
            if (_index.TryGetValue(id , out var path) && File.Exists(path))
            {
                var json = await File.ReadAllTextAsync(path , ct);
                var snapshot = DeserializeWithMigration(json , "snapshot");
                _logger.LogDebug("快照已加载: {SnapshotId}" , id);
                return snapshot;
            }
            return null;
        }

        public Task<IReadOnlyList<SeatingSnapshot>> ListByVenueAsync (string venueId , CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var snapshots = LoadFromDir(Path.Combine(_basePath , venueId));
            _logger.LogDebug("列出会场 {VenueId} 的 {Count} 个快照" , venueId , snapshots.Count);
            return Task.FromResult<IReadOnlyList<SeatingSnapshot>>(
                [.. snapshots.OrderByDescending(s => s.CreatedAt)]);
        }

        public Task<IReadOnlyList<SeatingSnapshot>> ListAllAsync ()
        {
            var snapshots = new List<SeatingSnapshot>();
            foreach (var venueDir in SafeEnumerateDirectories(_basePath))
                snapshots.AddRange(LoadFromDir(venueDir));
            _logger.LogDebug("列出所有 {Count} 个快照" , snapshots.Count);
            return Task.FromResult<IReadOnlyList<SeatingSnapshot>>(
                [.. snapshots.OrderByDescending(s => s.CreatedAt)]);
        }

        public Task DeleteAsync (string id , CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            BuildIndex();
            lock (_indexLock)
            {
                if (_index.TryGetValue(id , out var path) && File.Exists(path))
                {
                    File.Delete(path);
                    _index.Remove(id);
                    _logger.LogInformation("快照已删除：{SnapshotId}" , id);
                }
                else
                    _logger.LogDebug("删除快照未找到：{SnapshotId}" , id);
            }
            return Task.CompletedTask;
        }

        private SeatingSnapshot? DeserializeWithMigration (string json , string fileType)
        {
            var node = JsonNode.Parse(json);
            if (node is not null)
            {
                var fileVersion = node["version"]?.GetValue<string>() ?? "1.0";
                node = _migration.Migrate(fileType , node , fileVersion , FileVersionInfo.GetCurrentVersion(fileType));
                json = node.ToJsonString();
            }
            return JsonSerializer.Deserialize<SeatingSnapshot>(json);
        }

        private List<SeatingSnapshot> LoadFromDir (string venueDir)
        {
            var snapshots = new List<SeatingSnapshot>();
            foreach (var dateDir in SafeEnumerateDirectories(venueDir))
            {
                foreach (var file in Directory.EnumerateFiles(dateDir , "*.json"))
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var snapshot = DeserializeWithMigration(json , "snapshot");
                        if (snapshot is not null) snapshots.Add(snapshot);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex , "跳过损坏的快照文件：{File}" , file);
                    }
                }
            }
            return snapshots;
        }

        private string[] SafeEnumerateDirectories (string path)
        {
            try { return Directory.GetDirectories(path); }
            catch (Exception ex)
            {
                _logger.LogDebug(ex , "枚举目录失败：{Path}" , path);
                return [];
            }
        }
    }
}
