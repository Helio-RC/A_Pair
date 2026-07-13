using System;
using System.Threading;
using System.Threading.Tasks;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// 更新服务状态。
/// </summary>
public enum UpdateServiceStatus
{
    /// <summary>主更新源正常</summary>
    Healthy,
    /// <summary>使用备用源（如 GitHub）</summary>
    Fallback,
    /// <summary>所有更新源不可用</summary>
    Unavailable,
    /// <summary>开发模式（未通过 Velopack 安装）</summary>
    NotInstalled,
}

/// <summary>
/// 更新检查结果。
/// </summary>
public sealed record UpdateCheckResult
{
    /// <summary>是否有可用更新</summary>
    public bool HasUpdate { get; init; }

    /// <summary>新版本号（有更新时）</summary>
    public string? NewVersion { get; init; }

    /// <summary>当前版本号</summary>
    public string CurrentVersion { get; init; } = "";

    /// <summary>当前服务状态</summary>
    public UpdateServiceStatus ServiceStatus { get; init; }

    /// <summary>状态描述信息（人类可读）</summary>
    public string? Message { get; init; }
}

/// <summary>
/// 封装 Velopack 的更新检查、下载和应用操作。
/// </summary>
public interface IUpdateService
{
    /// <summary>当前服务状态</summary>
    UpdateServiceStatus Status { get; }

    /// <summary>是否有已下载但未应用的更新</summary>
    bool UpdatePendingRestart { get; }

    /// <summary>
    /// 检查更新。先调用 /updates/metadata 获取源状态，
    /// 若主源可用则使用 UpdateManager，否则使用 GithubSource 兜底。
    /// 开发环境（未安装）时返回 <see cref="UpdateServiceStatus.NotInstalled"/> 而不抛出异常。
    /// </summary>
    Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken ct = default);

    /// <summary>
    /// 下载更新，带进度回调。
    /// </summary>
    Task DownloadUpdatesAsync(IProgress<int>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// 应用更新并重启应用。调用后进程退出。
    /// </summary>
    void ApplyUpdatesAndRestart();
}
