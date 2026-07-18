using System;
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

        // 源优先级：API 网关 → GitHub 兜底
        var sourcePriority = new (string, Func<UpdateManager>)[]
        {
            ("oss_api", CreateApiManager),
            ("github", CreateGitHubManager),
        };

        // metadata 为 null（API 不可达）或 IsFallback 为 true 时跳过 API 源
        bool primaryHealthy = metadata is not null && !metadata.IsFallback;

        foreach (var (sourceKey, createManager) in sourcePriority)
        {
            if (!primaryHealthy && sourceKey != "github")
            {
                _logger.LogDebug("主源不健康，跳过 {Source}", sourceKey);
                continue;
            }

            _logger.LogDebug("尝试从源 {Source} 检查更新", sourceKey);
            try
            {
                var manager = createManager();
                var updateInfo = await manager.CheckForUpdatesAsync();
                lock (_updateLock)
                {
                    _lastUpdateInfo = updateInfo;
                    _currentManager = manager;
                }

                Status = sourceKey == "github"
                    ? UpdateServiceStatus.Fallback
                    : UpdateServiceStatus.Healthy;

                var result = updateInfo is null
                    ? new UpdateCheckResult
                    {
                        HasUpdate = false,
                        CurrentVersion = GetCurrentVersion(),
                        ServiceStatus = Status,
                        Message = metadata?.Message,
                    }
                    : new UpdateCheckResult
                    {
                        HasUpdate = true,
                        NewVersion = updateInfo.TargetFullRelease.Version.ToString(),
                        CurrentVersion = GetCurrentVersion(),
                        ServiceStatus = Status,
                        Message = metadata?.Message,
                    };

                _logger.LogInformation(
                    "源 {Source} 检查完成: HasUpdate={HasUpdate}, NewVersion={NewVersion}, Status={Status}",
                    sourceKey, result.HasUpdate, result.NewVersion ?? "N/A", Status);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "源 {Source} 检查更新失败 (Type={ExceptionType})", sourceKey, ex.GetType().Name);
                lock (_updateLock)
                    _currentManager = null;
            }
        }

        _logger.LogError("所有更新源均不可用 (Channel={Channel})", GetChannel());
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
        _logger.LogDebug("创建 API UpdateManager: {Url}", url);
        return new UpdateManager(url);
    }

    /// <summary>
    /// 创建基于 GitHub Releases 的 UpdateManager（兜底源）。
    /// 注意：未认证的 GitHub API 限制为 60 req/h/IP。
    /// 桌面应用的更新检查频率较低（手动触发），通常不会达到此限制。
    /// </summary>
    private UpdateManager CreateGitHubManager()
    {
        _logger.LogDebug("创建 GitHub UpdateManager: {Repo}", GitHubRepoUrl);
        return new UpdateManager(
            new GithubSource(GitHubRepoUrl, accessToken: null, prerelease: false));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
