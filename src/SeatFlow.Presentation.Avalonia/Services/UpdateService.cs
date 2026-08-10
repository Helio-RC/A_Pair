using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// Velopack 更新服务实现。
/// 先通过 /updates/metadata 获取源状态，优先使用 API 网关，
/// API 不可用时自动降级到 GitHub Release。
/// 开发环境（未通过 Velopack 安装）静默返回 NotInstalled。
/// </summary>
internal sealed class UpdateService : IUpdateService, IDisposable
{
    private readonly ILogger<UpdateService> _logger;
    private readonly HttpClient _httpClient;

    private const string UpdateApiBase = "https://download.seatflow.work/";
    private const string GitHubRepoUrl = "https://github.com/SeatFlow/SeatFlow";
    private const string ReleaseNotesApiBase = "https://seatflow.work/api/app/note/";

    private UpdateInfo? _lastUpdateInfo;
    private UpdateManager? _currentManager;
    private bool _isInstalled;
    private readonly object _updateLock = new();

    public UpdateServiceStatus Status { get; private set; } = UpdateServiceStatus.Unavailable;

    public bool UpdatePendingRestart
    {
        get
        {
            lock (_updateLock)
                return _currentManager?.UpdatePendingRestart is not null;
        }
    }

    public UpdateService(ILogger<UpdateService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(UpdateApiBase),
            Timeout = TimeSpan.FromSeconds(10),
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"SeatFlow/{GetCurrentVersion()} ({RuntimeInformation.OSDescription})");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        _logger.LogDebug(
            "UpdateService 已初始化: BaseUrl={BaseUrl}, Channel={Channel}, Version={Version}, OS={OS}",
            UpdateApiBase, GetChannel(), GetCurrentVersion(), RuntimeInformation.OSDescription);
    }

    private static string GetChannel()
    {
        if (OperatingSystem.IsWindows()) return "win-x64";
        if (OperatingSystem.IsLinux()) return "linux-x64";
        if (OperatingSystem.IsMacOS())
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "osx-arm64",
                _ => "osx-x64",
            };
        }
        return "win-x64";
    }

    private static string GetCurrentVersion()
    {
        var v = VersionInfo.Version;
        return string.IsNullOrEmpty(v) ? "1.0.0" : v;
    }

    // ============================================================
    // IUpdateService
    // ============================================================

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("开始检查更新 (当前版本: {Version})", GetCurrentVersion());

        MetadataResponse? metadata = null;
        try
        {
            metadata = await FetchMetadataAsync(ct);
            if (metadata is not null)
            {
                _logger.LogDebug(
                    "元数据获取成功: IsFallback={IsFallback}, RecommendedSource={RecommendedSource}, IsChinaRegion={IsChinaRegion}, Message={Message}",
                    metadata.IsFallback, metadata.RecommendedSource, metadata.IsChinaRegion, metadata.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "无法获取更新源元数据 (Type={ExceptionType})", ex.GetType().Name);
        }

        // 检测是否通过 Velopack 安装（首次调用的副作用：标记安装状态）
        if (!_isInstalled)
        {
            try { CheckInstalled(); }
            catch (Exception ex)
            {
                _isInstalled = false;
                _logger.LogDebug(ex, "Velopack 安装检测失败");
            }
        }

        if (!_isInstalled)
        {
            _logger.LogInformation("Velopack 未安装（开发模式），跳过更新检查");
            Status = UpdateServiceStatus.NotInstalled;
            return new UpdateCheckResult
            {
                HasUpdate = false,
                CurrentVersion = GetCurrentVersion(),
                ServiceStatus = UpdateServiceStatus.NotInstalled,
            };
        }

        // API 源不可达时直接报告不可用（无自动 GitHub 兜底）
        if (metadata is null || metadata.IsFallback)
        {
            _logger.LogWarning("API 更新源不可达 (IsFallback={IsFallback})", metadata?.IsFallback);
            Status = UpdateServiceStatus.Unavailable;
            return new UpdateCheckResult
            {
                HasUpdate = false,
                CurrentVersion = GetCurrentVersion(),
                ServiceStatus = UpdateServiceStatus.Unavailable,
            };
        }

        _logger.LogDebug("尝试从 API 源检查更新");
        try
        {
            var manager = CreateApiManager();
            var updateInfo = await manager.CheckForUpdatesAsync();
            lock (_updateLock)
            {
                _lastUpdateInfo = updateInfo;
                _currentManager = manager;
            }

            Status = UpdateServiceStatus.Healthy;

            var result = updateInfo is null
                ? new UpdateCheckResult
                {
                    HasUpdate = false,
                    CurrentVersion = GetCurrentVersion(),
                    ServiceStatus = Status,
                    Message = metadata.Message,
                }
                : new UpdateCheckResult
                {
                    HasUpdate = true,
                    NewVersion = updateInfo.TargetFullRelease.Version.ToString(),
                    CurrentVersion = GetCurrentVersion(),
                    ServiceStatus = Status,
                    Message = metadata.Message,
                };

            _logger.LogInformation(
                "API 源检查完成: HasUpdate={HasUpdate}, NewVersion={NewVersion}, Status={Status}",
                result.HasUpdate, result.NewVersion ?? "N/A", Status);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "API 源检查更新失败 (Type={ExceptionType})", ex.GetType().Name);
            lock (_updateLock)
                _currentManager = null;
        }

        _logger.LogError("API 更新源不可用 (Channel={Channel})", GetChannel());
        Status = UpdateServiceStatus.Unavailable;
        return new UpdateCheckResult
        {
            HasUpdate = false,
            CurrentVersion = GetCurrentVersion(),
            ServiceStatus = UpdateServiceStatus.Unavailable,
        };
    }

    public async Task DownloadUpdatesAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        UpdateInfo? info;
        UpdateManager? mgr;
        lock (_updateLock)
        {
            info = _lastUpdateInfo;
            mgr = _currentManager;
        }

        if (info is null || mgr is null)
        {
            await CheckForUpdatesAsync(ct);
            lock (_updateLock)
            {
                info = _lastUpdateInfo;
                mgr = _currentManager;
            }
        }

        if (info is null || mgr is null)
            return;

        _logger.LogInformation("开始下载更新 {Version}", info.TargetFullRelease.Version);

        Action<int>? onProgress = progress is null
            ? null
            : p => progress.Report(p);

        await mgr.DownloadUpdatesAsync(
            info,
            onProgress,
            cancelToken: ct);

        _logger.LogInformation("更新下载完成");
    }

    public void ApplyUpdatesAndRestart()
    {
        UpdateInfo? info;
        UpdateManager? mgr;
        lock (_updateLock)
        {
            info = _lastUpdateInfo;
            mgr = _currentManager;
        }

        if (info?.TargetFullRelease is null || mgr is null)
        {
            _logger.LogWarning("无法应用更新：更新信息不完整");
            return;
        }

        _logger.LogInformation("应用更新并重启: {Version}", info.TargetFullRelease.Version);
        mgr.ApplyUpdatesAndRestart(info.TargetFullRelease);
    }

    public async Task<string?> FetchReleaseNotesAsync(string? version = null, CancellationToken ct = default)
    {
        var targetVersion = version ?? _lastUpdateInfo?.TargetFullRelease.Version.ToString();
        if (string.IsNullOrEmpty(targetVersion))
        {
            _logger.LogDebug("FetchReleaseNotesAsync: 未指定版本且没有待处理的更新信息");
            return null;
        }

        var url = $"{ReleaseNotesApiBase}{targetVersion}";
        _logger.LogInformation("获取发布说明: {Url}", url);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(
                $"SeatFlow/{GetCurrentVersion()} ({RuntimeInformation.OSDescription})");
            request.Headers.Accept.ParseAdd("application/json");

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("发布说明端点返回 {StatusCode} ({ReasonPhrase}): {Url}",
                    (int)response.StatusCode, response.ReasonPhrase, url);
                return null;
            }

            var doc = await response.Content.ReadFromJsonAsync<ReleaseNotesResponse>(
                UpdateServiceJsonContext.Default.ReleaseNotesResponse, ct);
            return doc?.Content;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "获取发布说明失败: {Url}", url);
            return null;
        }
    }

    // ============================================================
    // 私有方法
    // ============================================================

    /// <summary>
    /// 通过 VelopackLocator 检测当前应用是否通过 Velopack 安装。
    /// 未安装时 <see cref="IVelopackLocator.CurrentlyInstalledVersion"/> 为 null。
    /// </summary>
    private void CheckInstalled()
    {
        _isInstalled = VelopackLocator.IsCurrentSet
            && VelopackLocator.Current.CurrentlyInstalledVersion is not null;
    }

    private async Task<MetadataResponse?> FetchMetadataAsync(CancellationToken ct)
    {
        var requestUrl = "/updates/metadata";
        _logger.LogDebug("请求元数据: URL={Url}", requestUrl);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(requestUrl, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex,
                "元数据 HTTP 请求失败: URL={Url}, StatusCode={StatusCode}, HResult=0x{HResult:X}",
                requestUrl, ex.StatusCode, ex.HResult);
            throw;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "元数据端点返回 {StatusCode} ({ReasonPhrase}): URL={Url}, Server={Server}, ContentType={ContentType}",
                (int)response.StatusCode, response.ReasonPhrase, requestUrl,
                response.Headers.Server?.ToString() ?? "N/A",
                response.Content.Headers.ContentType?.ToString() ?? "N/A");

            // 403/5xx 时读取响应体（前 500 字符）帮助定位
            if ((int)response.StatusCode is 403 or >= 500)
            {
                try
                {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    var truncated = body.Length > 500 ? body[..500] + "..." : body;
                    _logger.LogWarning("元数据端点响应体 (前500字符): {Body}", truncated);
                }
                catch { /* 读取失败不影响主流程 */ }
            }

            return null;
        }

        _logger.LogDebug("元数据响应 200 OK: ContentType={ContentType}",
            response.Content.Headers.ContentType?.ToString() ?? "N/A");

        return await response.Content.ReadFromJsonAsync(
            MetadataResponseJsonContext.Default.MetadataResponse, ct);
    }

    private UpdateManager CreateApiManager()
    {
        var url = $"{UpdateApiBase}updates/";
        var downloader = new LoggingFileDownloader(_logger);
        var source = new SimpleWebSource(url, downloader);
        _logger.LogDebug("创建 API UpdateManager: {Url}", url);
        return new UpdateManager(source);
    }

    public string GetGitHubReleasesUrl(string? version = null)
    {
        if (!string.IsNullOrEmpty(version))
            return $"{GitHubRepoUrl}/releases/tag/v{version}";
        return $"{GitHubRepoUrl}/releases";
    }

    // ── 日志下载器（用于调试 Velopack 内部 HTTP 请求）──

    /// <summary>
    /// 包装 <see cref="HttpClientFileDownloader"/>，将每次 HTTP 请求的
    /// URL、状态码和响应内容前缀输出到 Debug 日志。
    /// </summary>
    private sealed class LoggingFileDownloader : HttpClientFileDownloader
    {
        private readonly ILogger _logger;

        public LoggingFileDownloader(ILogger logger)
        {
            _logger = logger;
        }

        public override async Task<string> DownloadString(
            string url, IDictionary<string, string>? headers, double timeout)
        {
            _logger.LogDebug("[Velopack] DownloadString: {Url}", url);
            try
            {
                var result = await base.DownloadString(url, headers, timeout);
                var preview = result.Length > 500 ? result[..500] + "..." : result;
                _logger.LogDebug("[Velopack] DownloadString OK: {Url}, Length={Len}, Body={Body}",
                    url, result.Length, preview);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Velopack] DownloadString FAIL: {Url}", url);
                throw;
            }
        }

        public override async Task<byte[]> DownloadBytes(
            string url, IDictionary<string, string>? headers, double timeout)
        {
            _logger.LogDebug("[Velopack] DownloadBytes: {Url}", url);
            try
            {
                var result = await base.DownloadBytes(url, headers, timeout);
                _logger.LogDebug("[Velopack] DownloadBytes OK: {Url}, Length={Len}", url, result.Length);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Velopack] DownloadBytes FAIL: {Url}", url);
                throw;
            }
        }

        public override async Task DownloadFile(
            string url, string targetFile, Action<int>? progress,
            IDictionary<string, string>? headers, double timeout,
            CancellationToken cancelToken)
        {
            _logger.LogDebug("[Velopack] DownloadFile: {Url} -> {File}", url, targetFile);
            try
            {
                await base.DownloadFile(url, targetFile, progress!, headers, timeout, cancelToken);
                _logger.LogDebug("[Velopack] DownloadFile OK: {Url}", url);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Velopack] DownloadFile FAIL: {Url}", url);
                throw;
            }
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

/// <summary>
/// API 端点 /api/app/releases/{version}/notes 的响应 DTO。
/// </summary>
internal sealed class ReleaseNotesResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("content")]
    public string? Content { get; set; }
}

/// <summary>
/// UpdateService 内部使用的 JSON 源生成上下文。
/// </summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(ReleaseNotesResponse))]
internal sealed partial class UpdateServiceJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}
