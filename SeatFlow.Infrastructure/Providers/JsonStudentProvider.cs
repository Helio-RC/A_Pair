using System.Text.Json;
using SeatFlow.Core.Models;
using SeatFlow.Core.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Infrastructure.Providers;

public class JsonStudentProvider (ILogger<JsonStudentProvider>? logger = null) : IStudentProvider
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<JsonStudentProvider> _logger = logger ?? NullLogger<JsonStudentProvider>.Instance;

    public Task<List<Student>> LoadAsync (string source , CancellationToken cancellationToken = default)
    {
        return LoadAsync(source , 0 , 0 , cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<Student>> LoadAsync (string source , int maxRows , int maxCols , CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(source) || !File.Exists(source)) return [];

        try
        {
            await using var stream = File.OpenRead(source);
            var roster = await JsonSerializer.DeserializeAsync<RosterFile>(stream , Options , ct);
            _logger.LogInformation("JSON 学生数据已加载：{Source}（{Count} 人）" ,
                source , roster?.Students.Count ?? 0);
            var students = roster?.Students ?? [];

            if (maxRows > 0 && students.Count > maxRows)
                students = students.Take(maxRows).ToList();

            return students;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex , "JSON 学生数据解析失败：{Source}" , source);
            return [];
        }
    }

    /// <inheritdoc />
    public Task<(int Rows , int Cols)> GetDimensionsAsync (string source , CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(source) || !File.Exists(source))
            return Task.FromResult((0 , 0));

        try
        {
            using var stream = File.OpenRead(source);
            var roster = JsonSerializer.Deserialize<RosterFile>(stream , Options);
            int count = roster?.Students.Count ?? 0;
            return Task.FromResult((count , 1));
        }
        catch (Exception)
        {
            return Task.FromResult((0 , 0));
        }
    }
}
