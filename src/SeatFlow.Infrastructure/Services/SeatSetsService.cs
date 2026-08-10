using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SeatFlow.Core.Interfaces;
using SeatFlow.Core.Models.SeatSets;
using SeatFlow.Core.Utilities;
using SeatFlow.Infrastructure.Serialization;
using SeatFlow.Infrastructure.Utils;

namespace SeatFlow.Infrastructure.Services;

/// <summary>
/// .seatsets 数据包文件的核心服务实现。
/// 负责导出（收集文件→构建存档→计算哈希→序列化）、
/// 导入（校验→解析→逐文件恢复）、校验、自动发现和探测类别。
/// </summary>
public class SeatSetsService : ISeatSetsService
{
    private readonly string _effectiveDataPath;
    private readonly string _settingsFilePath;
    private readonly ILogger<SeatSetsService> _logger;

    /// <summary>
    /// 初始化 .seatsets 服务。
    /// </summary>
    /// <param name="effectiveDataPath">有效数据目录路径（Venues/Rosters/Assignments/StrategyConfig 的父目录）。</param>
    /// <param name="settingsFilePath">AppSettings.json 文件的完整路径。</param>
    /// <param name="logger">日志记录器。</param>
    public SeatSetsService (
        string effectiveDataPath ,
        string settingsFilePath ,
        ILogger<SeatSetsService>? logger = null)
    {
        _effectiveDataPath = effectiveDataPath;
        _settingsFilePath = settingsFilePath;
        _logger = logger ?? NullLogger<SeatSetsService>.Instance;
    }

    // ──────────────────────────────── 导出 ────────────────────────────────

    /// <inheritdoc />
    public async Task<int> ExportAsync (string outputPath , SeatSetsExportSelection selection ,
        CancellationToken ct = default)
    {
        _logger.LogInformation("开始导出数据包: {Path}, 选择: {@Selection}" , outputPath , selection);

        var archive = new SeatSetsArchive
        {
            Version = SeatSetsConstants.CurrentFormatVersion ,
            AppVersion = GetAppVersion() ,
            CreatedAt = DateTime.UtcNow.ToString("O")
        };

        int totalFiles = 0;

        // 逐类别收集文件
        if (selection.IncludeAppSettings)
            totalFiles += AddAppSettingsChunk(archive);

        if (selection.IncludeVenues)
            totalFiles += AddVenuesChunk(archive , ct);

        if (selection.IncludeRosters)
            totalFiles += AddRostersChunk(archive , ct);

        if (selection.IncludeSnapshots)
            totalFiles += AddSnapshotsChunk(archive , ct);

        if (selection.IncludeStrategyConfig)
            totalFiles += AddStrategyConfigChunk(archive , ct);

        if (totalFiles == 0)
        {
            _logger.LogWarning("没有可导出的数据");
            return 0;
        }

        // 计算每块的哈希
        foreach (var (category , chunk) in archive.Chunks)
        {
            chunk.Hash = ComputeChunkHash(chunk.Files);
            _logger.LogDebug("Chunk {Category}: {FileCount} 个文件, Hash={Hash}" ,
                category , chunk.Files.Count , chunk.Hash);
        }

        // 计算整体归档哈希
        archive.ArchiveHash = ComputeArchiveHash(archive.Chunks);
        _logger.LogInformation("归档哈希: {Hash}, 总文件数: {Total}" , archive.ArchiveHash , totalFiles);

        // 序列化并写入文件
        var json = JsonSerializer.Serialize(archive , JsonOptions.WriteIndentedCamelCase);
        await File.WriteAllTextAsync(outputPath , json , ct);

        _logger.LogInformation("数据包导出完成: {Path}, {FileCount} 个文件, 大小 {Size} 字节" ,
            outputPath , totalFiles , new FileInfo(outputPath).Length);

        return totalFiles;
    }

    // ──────────────────────────────── 导入 ────────────────────────────────

    /// <inheritdoc />
    public async Task<SeatSetsImportResult> ImportAsync (string filePath , SeatSetsExportSelection selection ,
        IProgress<double>? progress = null , CancellationToken ct = default)
    {
        _logger.LogInformation("开始导入数据包: {Path}, 选择: {@Selection}" , filePath , selection);

        var result = new SeatSetsImportResult();

        // 1. 校验文件
        var validation = await ValidateAsync(filePath , ct);
        if (!validation.IsValid)
        {
            result.Errors.AddRange(validation.ValidationErrors);
            return result;
        }

        // 2. 解析存档
        var json = await File.ReadAllTextAsync(filePath , ct);
        var archive = JsonSerializer.Deserialize<SeatSetsArchive>(json , JsonOptions.CaseInsensitiveRead);
        if (archive?.Chunks == null)
        {
            result.Errors.Add("无法解析存档文件");
            return result;
        }

        // 3. 对每个选中的类别逐文件恢复
        var selectedCategories = selection.GetSelectedCategories();
        var filesToRestore = new List<(string Category , string RelativePath , JsonElement Content)>();

        foreach (var category in selectedCategories)
        {
            if (!archive.Chunks.TryGetValue(category , out var chunk))
                continue;

            // 验证 chunk 哈希
            var computedHash = ComputeChunkHash(chunk.Files);
            if (!string.IsNullOrEmpty(chunk.Hash) &&
                !string.Equals(computedHash , chunk.Hash , StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Chunk {Category} 哈希不匹配，跳过该块全部 {Count} 个文件" ,
                    category , chunk.Files.Count);
                result.Skipped += chunk.Files.Count;
                result.Errors.Add($"数据块 [{category}] 哈希校验失败，已跳过");
                continue;
            }

            foreach (var (relPath , content) in chunk.Files)
            {
                filesToRestore.Add((category , relPath , content));
            }
        }

        result.TotalFiles = filesToRestore.Count;
        _logger.LogInformation("共 {Count} 个文件待恢复" , result.TotalFiles);

        // 4. 逐文件恢复（尽力而为，单个失败不中断）
        int processed = 0;
        foreach (var (category , relPath , content) in filesToRestore)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var targetPath = ResolveTargetPath(category , relPath);

                // 确保目标目录存在
                var dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                // 原子写入：先写临时文件，再重命名
                var tempPath = targetPath + ".tmp";
                var rawJson = content.GetRawText();

                await File.WriteAllTextAsync(tempPath , rawJson , ct);
                File.Move(tempPath , targetPath , overwrite: true);

                result.Restored++;
                _logger.LogDebug("已恢复: {Path}" , relPath);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{relPath}: {ex.Message}");
                _logger.LogError(ex , "恢复文件失败: {Path}" , relPath);
            }

            processed++;
            progress?.Report((double)processed / result.TotalFiles);
        }

        _logger.LogInformation("导入完成: 成功 {Restored}/{Total}, 跳过 {Skipped}, 错误 {ErrCount}" ,
            result.Restored , result.TotalFiles , result.Skipped , result.Errors.Count);

        return result;
    }

    // ──────────────────────────────── 校验 ────────────────────────────────

    /// <inheritdoc />
    public async Task<SeatSetsValidationResult> ValidateAsync (string filePath ,
        CancellationToken ct = default)
    {
        var result = new SeatSetsValidationResult();

        try
        {
            // 文件大小检查
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
            {
                result.ValidationErrors.Add("文件不存在");
                return result;
            }

            result.FileSize = fileInfo.Length;
            if (fileInfo.Length > SeatSetsConstants.MaxFileSizeBytes)
            {
                result.ValidationErrors.Add($"文件过大（{fileInfo.Length / (1024.0 * 1024.0):F1} MB），"
                    + $"上限为 {SeatSetsConstants.MaxFileSizeBytes / (1024 * 1024)} MB");
                return result;
            }

            // JSON 解析
            var json = await File.ReadAllTextAsync(filePath , ct);
            SeatSetsArchive? archive;
            try
            {
                archive = JsonSerializer.Deserialize<SeatSetsArchive>(json , JsonOptions.CaseInsensitiveRead);
            }
            catch (JsonException ex)
            {
                result.ValidationErrors.Add($"JSON 格式无效: {ex.Message}");
                return result;
            }

            if (archive == null)
            {
                result.ValidationErrors.Add("存档内容为空");
                return result;
            }

            result.Version = archive.Version;
            result.AppVersion = archive.AppVersion;
            result.AvailableCategories = archive.Chunks.Keys.ToList();

            // 检查版本兼容性
            if (string.IsNullOrEmpty(archive.Version))
            {
                result.ValidationErrors.Add("缺少格式版本号");
                return result;
            }

            // 验证各块的哈希
            foreach (var (category , chunk) in archive.Chunks)
            {
                if (chunk.Files.Count == 0) continue;
                var computed = ComputeChunkHash(chunk.Files);
                if (!string.IsNullOrEmpty(chunk.Hash) &&
                    !string.Equals(computed , chunk.Hash , StringComparison.OrdinalIgnoreCase))
                {
                    result.ValidationErrors.Add($"数据块 [{category}] 哈希不匹配");
                }
            }

            // 路径穿越检测：所有文件路径不得包含 ".." 段
            foreach (var (category , chunk) in archive.Chunks)
            {
                foreach (var relPath in chunk.Files.Keys)
                {
                    if (relPath.Contains(".."))
                    {
                        result.ValidationErrors.Add(
                            $"路径穿越检测: 数据块 [{category}] 中的文件路径包含非法段 '..': {relPath}");
                    }
                }
            }

            // 验证整体哈希
            if (!string.IsNullOrEmpty(archive.ArchiveHash))
            {
                var computedArchiveHash = ComputeArchiveHash(archive.Chunks);
                result.ArchiveHashValid = string.Equals(computedArchiveHash , archive.ArchiveHash ,
                    StringComparison.OrdinalIgnoreCase);
                if (!result.ArchiveHashValid)
                {
                    result.ValidationErrors.Add("归档哈希校验失败，文件可能已损坏");
                }
            }
            else
            {
                result.ArchiveHashValid = true; // 旧格式可能没有归档哈希
            }

            result.IsValid = result.ValidationErrors.Count == 0;
            _logger.LogInformation("校验完成: {Path}, Valid={Valid}, Size={Size}, Categories={Cats}" ,
                filePath , result.IsValid , result.FileSize , result.AvailableCategories.Count);
        }
        catch (Exception ex)
        {
            result.ValidationErrors.Add($"校验异常: {ex.Message}");
            _logger.LogError(ex , "校验文件时发生异常: {Path}" , filePath);
        }

        return result;
    }

    // ──────────────────────────────── 自动发现 ────────────────────────────────

    /// <inheritdoc />
    public Task<string?> DiscoverAsync (CancellationToken ct = default)
    {
        var exeDir = AppEnvironment.ExeDirectory;
        if (!Directory.Exists(exeDir))
        {
            _logger.LogDebug("可执行文件目录不存在: {Dir}" , exeDir);
            return Task.FromResult<string?>(null);
        }

        try
        {
            var files = Directory.GetFiles(exeDir , $"*{SeatSetsConstants.FileExtension}" ,
                SearchOption.TopDirectoryOnly);

            if (files.Length == 0)
            {
                _logger.LogDebug("未在目录中发现 .seatsets 文件: {Dir}" , exeDir);
                return Task.FromResult<string?>(null);
            }

            // 取最新修改的文件
            var latest = files.Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .First();

            _logger.LogInformation("自动发现数据包: {Path} ({Size} 字节, 修改时间 {Time})" ,
                latest.FullName , latest.Length , latest.LastWriteTimeUtc);

            return Task.FromResult<string?>(latest.FullName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex , "自动发现 .seatsets 文件时出错");
            return Task.FromResult<string?>(null);
        }
    }

    // ──────────────────────────────── 探测类别 ────────────────────────────────

    /// <inheritdoc />
    public async Task<SeatSetsExportSelection> ProbeCategoriesAsync (string filePath ,
        CancellationToken ct = default)
    {
        var selection = new SeatSetsExportSelection
        {
            IncludeAppSettings = false ,
            IncludeVenues = false ,
            IncludeRosters = false ,
            IncludeSnapshots = false ,
            IncludeStrategyConfig = false
        };

        try
        {
            var json = await File.ReadAllTextAsync(filePath , ct);
            var archive = JsonSerializer.Deserialize<SeatSetsArchive>(json , JsonOptions.CaseInsensitiveRead);
            if (archive?.Chunks == null)
                return selection;

            foreach (var category in archive.Chunks.Keys)
            {
                switch (category)
                {
                    case SeatSetsConstants.CategoryAppSettings:
                        selection.IncludeAppSettings = true;
                        break;
                    case SeatSetsConstants.CategoryVenues:
                        selection.IncludeVenues = true;
                        break;
                    case SeatSetsConstants.CategoryRosters:
                        selection.IncludeRosters = true;
                        break;
                    case SeatSetsConstants.CategorySnapshots:
                        selection.IncludeSnapshots = true;
                        break;
                    case SeatSetsConstants.CategoryStrategyConfig:
                        selection.IncludeStrategyConfig = true;
                        break;
                }
            }

            _logger.LogDebug("探测完成: {Path}, 类别: {Cats}" ,
                filePath , string.Join(", " , archive.Chunks.Keys));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex , "探测类别时出错: {Path}" , filePath);
        }

        return selection;
    }

    // ──────────────────────────────── 私有辅助方法 ────────────────────────────────

    /// <summary>
    /// 收集 AppSettings.json 文件内容。
    /// </summary>
    private int AddAppSettingsChunk (SeatSetsArchive archive)
    {
        if (!File.Exists(_settingsFilePath))
        {
            _logger.LogDebug("AppSettings 文件不存在: {Path}" , _settingsFilePath);
            return 0;
        }

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            using var doc = JsonDocument.Parse(json);
            var chunk = GetOrCreateChunk(archive , SeatSetsConstants.CategoryAppSettings);
            // AppSettings.json 直接位于 AppData 根目录
            chunk.Files["AppSettings.json"] = doc.RootElement.Clone();
            _logger.LogDebug("已收集 AppSettings");
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex , "读取 AppSettings 失败");
            return 0;
        }
    }

    /// <summary>
    /// 收集所有会场文件 (*.venue.json)。
    /// </summary>
    private int AddVenuesChunk (SeatSetsArchive archive , CancellationToken ct)
    {
        var venuesPath = Path.Combine(_effectiveDataPath , "Venues");
        if (!Directory.Exists(venuesPath))
            return 0;

        return CollectFilesFromDirectory(archive , SeatSetsConstants.CategoryVenues ,
            venuesPath , "*.venue.json" , "Venues" , ct);
    }

    /// <summary>
    /// 收集所有学生名单文件 (*.roster.json)。
    /// </summary>
    private int AddRostersChunk (SeatSetsArchive archive , CancellationToken ct)
    {
        var rostersPath = Path.Combine(_effectiveDataPath , "Rosters");
        if (!Directory.Exists(rostersPath))
            return 0;

        return CollectFilesFromDirectory(archive , SeatSetsConstants.CategoryRosters ,
            rostersPath , "*.roster.json" , "Rosters" , ct);
    }

    /// <summary>
    /// 收集所有快照文件 (Assignments/ 下的所有 .json)。
    /// </summary>
    private int AddSnapshotsChunk (SeatSetsArchive archive , CancellationToken ct)
    {
        var assignmentsPath = Path.Combine(_effectiveDataPath , "Assignments");
        if (!Directory.Exists(assignmentsPath))
            return 0;

        // Assignments 目录结构: {venueId}/{yyyyMMdd}/{snapshotId}.json + _venue.json
        // 使用递归搜索
        return CollectFilesRecursive(archive , SeatSetsConstants.CategorySnapshots ,
            assignmentsPath , "Assignments" , ct);
    }

    /// <summary>
    /// 收集所有策略配置文件 (StrategyConfig/ 下的所有 .config.json)。
    /// </summary>
    private int AddStrategyConfigChunk (SeatSetsArchive archive , CancellationToken ct)
    {
        var configPath = Path.Combine(_effectiveDataPath , "StrategyConfig");
        if (!Directory.Exists(configPath))
            return 0;

        // StrategyConfig 目录结构:
        //   {strategyId}.config.json (根级)
        //   {strategyId}/*.config.json (子目录)
        var count = CollectFilesFromDirectory(archive , SeatSetsConstants.CategoryStrategyConfig ,
            configPath , "*.config.json" , "StrategyConfig" , ct , recursive: false);

        // 收集子目录中的配置
        try
        {
            foreach (var subDir in Directory.GetDirectories(configPath))
            {
                ct.ThrowIfCancellationRequested();
                var dirName = Path.GetFileName(subDir);
                var subFiles = Directory.GetFiles(subDir , "*.config.json");
                foreach (var filePath in subFiles)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var json = File.ReadAllText(filePath);
                        using var doc = JsonDocument.Parse(json);
                        var chunk = GetOrCreateChunk(archive , SeatSetsConstants.CategoryStrategyConfig);
                        var relPath = $"StrategyConfig/{dirName}/{Path.GetFileName(filePath)}";
                        chunk.Files[relPath] = doc.RootElement.Clone();
                        count++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex , "读取策略配置文件失败: {Path}" , filePath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex , "枚举策略配置子目录时出错");
        }

        return count;
    }

    /// <summary>
    /// 从单个目录中收集匹配的文件。
    /// </summary>
    private int CollectFilesFromDirectory (
        SeatSetsArchive archive ,
        string category ,
        string directoryPath ,
        string searchPattern ,
        string relativePrefix ,
        CancellationToken ct ,
        bool recursive = false)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(directoryPath , searchPattern , searchOption);
        int count = 0;

        foreach (var filePath in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var json = File.ReadAllText(filePath);
                using var doc = JsonDocument.Parse(json);
                var chunk = GetOrCreateChunk(archive , category);

                // 计算相对路径（使用 "/" 分隔符）
                var relPath = Path.GetRelativePath(directoryPath , filePath)
                    .Replace('\\' , '/');
                relPath = $"{relativePrefix}/{relPath}";

                chunk.Files[relPath] = doc.RootElement.Clone();
                count++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex , "读取文件失败: {Path}" , filePath);
            }
        }

        _logger.LogDebug("已收集 {Category}: {Count} 个文件" , category , count);
        return count;
    }

    /// <summary>
    /// 递归收集目录下所有 .json 文件（用于 Assignments 快照目录）。
    /// </summary>
    private int CollectFilesRecursive (
        SeatSetsArchive archive ,
        string category ,
        string directoryPath ,
        string relativePrefix ,
        CancellationToken ct)
    {
        int count = 0;
        try
        {
            var files = Directory.GetFiles(directoryPath , "*.json" , SearchOption.AllDirectories);
            foreach (var filePath in files)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var json = File.ReadAllText(filePath);
                    using var doc = JsonDocument.Parse(json);
                    var chunk = GetOrCreateChunk(archive , category);

                    var relPath = Path.GetRelativePath(directoryPath , filePath)
                        .Replace('\\' , '/');
                    relPath = $"{relativePrefix}/{relPath}";

                    chunk.Files[relPath] = doc.RootElement.Clone();
                    count++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex , "读取快照文件失败: {Path}" , filePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex , "枚举快照目录时出错");
        }

        _logger.LogDebug("已收集 {Category}: {Count} 个文件" , category , count);
        return count;
    }

    /// <summary>
    /// 获取或创建指定类别的 Chunk。
    /// </summary>
    private static SeatSetsChunk GetOrCreateChunk (SeatSetsArchive archive , string category)
    {
        if (!archive.Chunks.TryGetValue(category , out var chunk))
        {
            chunk = new SeatSetsChunk();
            archive.Chunks[category] = chunk;
        }
        return chunk;
    }

    /// <summary>
    /// 计算单个 Chunk 的哈希（基于其 Files 字典的确定性 JSON 序列化）。
    /// </summary>
    private static string ComputeChunkHash (Dictionary<string , JsonElement> files)
    {
        if (files.Count == 0)
            return string.Empty;

        // 按 key 排序以保证确定性
        var sorted = new SortedDictionary<string , JsonElement>(files , StringComparer.Ordinal);
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        foreach (var (key , value) in sorted)
        {
            writer.WritePropertyName(key);
            value.WriteTo(writer);
        }
        writer.WriteEndObject();
        writer.Flush();

        var canonical = Encoding.UTF8.GetString(stream.ToArray());
        return ContentHashHelper.ComputeSha256(canonical);
    }

    /// <summary>
    /// 计算归档级别的整体哈希（所有 chunk hash 拼接后取 SHA256）。
    /// </summary>
    private static string ComputeArchiveHash (Dictionary<string , SeatSetsChunk> chunks)
    {
        var hashInput = string.Join("" ,
            chunks.OrderBy(c => c.Key , StringComparer.Ordinal)
                  .Select(c => c.Value.Hash ?? string.Empty));

        return ContentHashHelper.ComputeSha256(hashInput);
    }

    /// <summary>
    /// 根据类别和相对路径解析导入时的目标文件路径。
    /// 内含路径穿越防护：解析后的绝对路径必须位于允许的基目录内。
    /// </summary>
    /// <exception cref="InvalidOperationException">路径穿越检测时抛出。</exception>
    private string ResolveTargetPath (string category , string relPath)
    {
        // 规范化为平台路径分隔符，拒绝含 null 字节的路径
        var normalized = relPath.Replace('/' , Path.DirectorySeparatorChar);
        if (normalized.Contains('\0'))
            throw new InvalidOperationException($"文件路径包含非法字符: {relPath}");

        string fullPath;
        string allowedBase;

        if (category == SeatSetsConstants.CategoryAppSettings)
        {
            fullPath = Path.GetFullPath(_settingsFilePath);
            allowedBase = Path.GetFullPath(Path.GetDirectoryName(_settingsFilePath)!);
        }
        else
        {
            fullPath = Path.GetFullPath(Path.Combine(_effectiveDataPath , normalized));
            allowedBase = Path.GetFullPath(_effectiveDataPath);
        }

        // 路径穿越检测：解析后的绝对路径必须位于允许的基目录内
        if (!fullPath.StartsWith(allowedBase + Path.DirectorySeparatorChar , StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullPath , allowedBase , StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"路径穿越检测: '{relPath}' 解析到 '{fullPath}'，不在允许的基目录 '{allowedBase}' 内");
        }

        return fullPath;
    }

    /// <summary>
    /// 获取应用版本字符串。
    /// </summary>
    private static string GetAppVersion ()
    {
        try
        {
            var version = Assembly.GetEntryAssembly()?.GetName()?.Version;
            if (version != null)
                return $"{version.Major}.{version.Minor}.{version.Build}";
        }
        catch { /* 忽略 */ }
        return "1.0.0";
    }
}
