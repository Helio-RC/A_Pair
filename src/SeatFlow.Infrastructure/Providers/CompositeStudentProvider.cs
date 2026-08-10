using SeatFlow.Core.Models;
using SeatFlow.Core.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Infrastructure.Providers;

public class CompositeStudentProvider : IStudentProvider
{
    private readonly Dictionary<string , IStudentProvider> _providers;
    private readonly ILogger<CompositeStudentProvider> _logger;

    public CompositeStudentProvider (
        CsvStudentProvider csvProvider ,
        XlsxStudentProvider xlsxProvider ,
        JsonStudentProvider jsonProvider ,
        ILogger<CompositeStudentProvider>? logger = null)
    {
        _logger = logger ?? NullLogger<CompositeStudentProvider>.Instance;
        _providers = new(StringComparer.OrdinalIgnoreCase)
        {
            [".csv"] = csvProvider ,
            [".xlsx"] = xlsxProvider ,
            [".json"] = jsonProvider
        };
    }

    public async Task<List<Student>> LoadAsync (string source , CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(source) || !File.Exists(source))
            return [];

        var ext = Path.GetExtension(source);
        if (_providers.TryGetValue(ext , out var provider))
        {
            _logger.LogDebug("通过 {ProviderType} 加载学生数据: {Source}" , provider.GetType().Name , source);
            return await provider.LoadAsync(source , cancellationToken);
        }

        _logger.LogWarning("不支持的文件格式：{Extension}（{Source}）" , ext , source);
        return [];
    }

    /// <inheritdoc />
    public async Task<List<Student>> LoadAsync (string source , int maxRows , int maxCols , CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(source) || !File.Exists(source))
            return [];

        var ext = Path.GetExtension(source);
        if (_providers.TryGetValue(ext , out var provider))
        {
            _logger.LogDebug("通过 {ProviderType} 加载学生数据（限制 {MaxRows}×{MaxCols}）: {Source}" ,
                provider.GetType().Name , maxRows , maxCols , source);
            return await provider.LoadAsync(source , maxRows , maxCols , ct);
        }

        _logger.LogWarning("不支持的文件格式：{Extension}（{Source}）" , ext , source);
        return [];
    }

    /// <inheritdoc />
    public async Task<(int Rows , int Cols)> GetDimensionsAsync (string source , CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(source) || !File.Exists(source))
            return (0 , 0);

        var ext = Path.GetExtension(source);
        if (_providers.TryGetValue(ext , out var provider))
        {
            _logger.LogDebug("通过 {ProviderType} 获取维度: {Source}" , provider.GetType().Name , source);
            return await provider.GetDimensionsAsync(source , ct);
        }

        _logger.LogWarning("不支持的文件格式：{Extension}（{Source}）" , ext , source);
        return (0 , 0);
    }
}
