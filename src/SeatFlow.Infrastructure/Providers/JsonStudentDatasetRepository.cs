using System.Text.Json;
using System.Text.Json.Nodes;
using SeatFlow.Core.Models;
using SeatFlow.Core.Providers;
using SeatFlow.Infrastructure.Migration;
using SeatFlow.Infrastructure.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Infrastructure.Providers;

public class JsonStudentDatasetRepository : IStudentDatasetRepository
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true ,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _rostersPath;
    private readonly FileMigrationService _migration;
    private readonly ILogger<JsonStudentDatasetRepository> _logger;

    public JsonStudentDatasetRepository (
        string rostersPath ,
        FileMigrationService migration ,
        ILogger<JsonStudentDatasetRepository>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(rostersPath);
        _rostersPath = rostersPath;
        _migration = migration ?? throw new ArgumentNullException(nameof(migration));
        _logger = logger ?? NullLogger<JsonStudentDatasetRepository>.Instance;
        Directory.CreateDirectory(_rostersPath);
    }

    public async Task SaveAsync (string id , string name , List<Student> students ,
        string? originalFileName = null , CancellationToken ct = default)
    {
        var roster = new RosterFile
        {
            Version = FileVersionInfo.GetCurrentVersion("roster") ,
            Description = name ,
            Students = students ,
            Metadata = new Dictionary<string , object>
            {
                ["importedAt"] = DateTime.Now.ToString("O") ,
                ["studentCount"] = students.Count
            }
        };
        if (originalFileName != null)
            roster.Metadata["originalFileName"] = originalFileName;

        // 按 Id 排序后序列化学生列表，计算 StudentsHash
        roster.Students = [.. roster.Students.OrderBy(s => s.Id)];
        var studentsJson = JsonSerializer.Serialize(roster.Students , WriteOptions);
        roster.StudentsHash = ContentHashHelper.ComputeSha256(studentsJson);

        var path = GetFilePath(id);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream , roster , WriteOptions , ct);
        _logger.LogInformation("学生数据集已保存：{Id}（{Count} 人）→ {Path}" , id , students.Count , path);
    }

    public async Task<List<Student>?> LoadAsync (string id , CancellationToken ct = default)
    {
        var path = GetFilePath(id);
        if (!File.Exists(path)) return null;

        var json = await File.ReadAllTextAsync(path , ct);
        var roster = DeserializeRoster(json);
        _logger.LogInformation("学生数据集已加载: {DatasetId}" , id);
        return roster?.Students;
    }

    public Task<IReadOnlyList<StudentDatasetInfo>> ListAsync (CancellationToken ct = default)
    {
        var results = new List<StudentDatasetInfo>();
        if (!Directory.Exists(_rostersPath))
            return Task.FromResult<IReadOnlyList<StudentDatasetInfo>>(results);

        foreach (var file in Directory.EnumerateFiles(_rostersPath , "*.roster.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var roster = DeserializeRoster(json);
                if (roster is null) continue;

                var info = new StudentDatasetInfo
                {
                    Id = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file)) ,
                    Name = roster.Description ?? "未命名" ,
                    StudentCount = roster.Students.Count ,
                    CreatedAt = ExtractDate(roster , file)
                };

                if (roster.Metadata.TryGetValue("originalFileName" , out var origObj))
                    info.OriginalFileName = origObj?.ToString();

                results.Add(info);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex , "跳过损坏的数据集文件：{File}" , file);
            }
        }

        results.Sort((a , b) => b.CreatedAt.CompareTo(a.CreatedAt));
        _logger.LogDebug("列出 {Count} 个学生数据集" , results.Count);
        return Task.FromResult<IReadOnlyList<StudentDatasetInfo>>(results);
    }

    public Task DeleteAsync (string id , CancellationToken ct = default)
    {
        var path = GetFilePath(id);
        if (File.Exists(path))
            File.Delete(path);
        _logger.LogInformation("学生数据集已删除: {DatasetId}" , id);
        return Task.CompletedTask;
    }

    public async Task RenameAsync (string id , string newName , CancellationToken ct = default)
    {
        var path = GetFilePath(id);
        if (!File.Exists(path))
            throw new FileNotFoundException($"数据集文件不存在：{path}");

        var json = await File.ReadAllTextAsync(path , ct);
        var roster = DeserializeRoster(json) ?? throw new InvalidOperationException($"数据集文件损坏：{path}");
        roster.Description = newName;
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream , roster , WriteOptions , ct);
        _logger.LogInformation("数据集已重命名：{Id} → {Name}" , id , newName);
    }

    private RosterFile? DeserializeRoster (string json)
    {
        var node = JsonNode.Parse(json);
        if (node is not null)
        {
            var fileVersion = node["version"]?.GetValue<string>() ?? "1.0";
            node = _migration.Migrate("roster" , node , fileVersion , FileVersionInfo.GetCurrentVersion("roster"));
            json = node.ToJsonString();
        }
        return JsonSerializer.Deserialize<RosterFile>(json , ReadOptions);
    }

    private string GetFilePath (string id) => Path.Combine(_rostersPath , $"{id}.roster.json");

    private static DateTime ExtractDate (RosterFile roster , string filePath)
    {
        if (roster.Metadata.TryGetValue("importedAt" , out var val) && val is JsonElement je)
        {
            var s = je.GetString();
            if (DateTime.TryParse(s , out var dt))
                return dt;
        }
        return File.GetCreationTimeUtc(filePath);
    }
}
