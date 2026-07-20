using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SeatFlow.Presentation.Avalonia.Lang;
using SeatFlow.Presentation.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

/// <summary>
/// 更新对话框的 ViewModel。展示 release notes、控制下载进度、
/// 并向外暴露 <see cref="Confirmed"/> 和 <see cref="Downloaded"/> 标志。
/// </summary>
internal partial class UpdateDialogViewModel : ObservableObject
{
    private readonly IUpdateService _updateService;
    private readonly ILogger<UpdateDialogViewModel> _logger;

    private string _newVersion = "";
    private bool _allowDownload;

    /// <summary>用户点击了"安装并重启"。</summary>
    public bool Confirmed { get; private set; }

    /// <summary>更新包已下载完成（但用户可能点击了"稍后再说"）。</summary>
    public bool Downloaded { get; private set; }

    // ── 绑定属性 ──

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    public partial string Subtitle { get; set; } = Resources.Update_ReleaseNotes;

    [ObservableProperty]
    public partial List<MdBlock> ReleaseBlocks { get; set; } = [];

    [ObservableProperty]
    public partial bool IsLoadingNotes { get; set; } = true;

    [ObservableProperty]
    public partial bool NotesLoadFailed { get; set; }

    [ObservableProperty]
    public partial bool IsDownloading { get; set; }

    [ObservableProperty]
    public partial int DownloadProgress { get; set; }

    [ObservableProperty]
    public partial bool IsInstallReady { get; set; }

    /// <summary>"立即更新"按钮是否可见（CheckOnly 模式下隐藏）。</summary>
    [ObservableProperty]
    public partial bool IsUpdateButtonVisible { get; set; } = true;

    /// <summary>"稍后再说"/"关闭"按钮文本。</summary>
    [ObservableProperty]
    public partial string CloseButtonText { get; set; } = Resources.Update_DownloadLater;

    /// <summary>更新按钮文本。</summary>
    [ObservableProperty]
    public partial string UpdateButtonText { get; set; } = Resources.Update_UpdateNow;

    [ObservableProperty]
    public partial bool IsUpdateButtonEnabled { get; set; } = true;

    public UpdateDialogViewModel(IUpdateService updateService, ILogger<UpdateDialogViewModel> logger)
    {
        _updateService = updateService;
        _logger = logger;
    }

    /// <summary>
    /// 初始化 ViewModel。必须在对话框显示前调用。
    /// </summary>
    /// <param name="newVersion">新版本号。</param>
    /// <param name="allowDownload">是否允许下载（CheckOnly 模式下为 false）。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task InitializeAsync(string newVersion, bool allowDownload, CancellationToken ct = default)
    {
        _newVersion = newVersion;
        _allowDownload = allowDownload;

        Title = string.Format(Resources.Update_NewVersionTitle, newVersion);

        if (!allowDownload)
        {
            IsUpdateButtonVisible = false;
            CloseButtonText = Resources.Common_Close;
        }

        await LoadReleaseNotesAsync(ct);
    }

    private async Task LoadReleaseNotesAsync(CancellationToken ct)
    {
        IsLoadingNotes = true;
        NotesLoadFailed = false;

        try
        {
            var markdown = await _updateService.FetchReleaseNotesAsync(_newVersion, ct);
            if (!string.IsNullOrEmpty(markdown))
            {
                ReleaseBlocks = MarkdownRenderer.Render(markdown);
            }
            else
            {
                ReleaseBlocks = [new MdBlock(Resources.Update_NoReleaseNotes, MdBlockKind.Paragraph)];
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载发布说明失败: {Version}", _newVersion);
            NotesLoadFailed = true;
        }
        finally
        {
            IsLoadingNotes = false;
        }
    }

    /// <summary>"立即更新" → 下载更新，完成后按钮切换为"安装并重启"。</summary>
    [RelayCommand]
    private async Task UpdateNowAsync()
    {
        if (!_allowDownload || IsDownloading) return;

        IsUpdateButtonEnabled = false;
        IsDownloading = true;
        DownloadProgress = 0;

        try
        {
            var progress = new Progress<int>(p => DownloadProgress = p);

            await _updateService.DownloadUpdatesAsync(progress);

            Downloaded = true;
            IsDownloading = false;
            IsInstallReady = true;
            IsUpdateButtonEnabled = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "下载更新失败");
            IsDownloading = false;
            IsUpdateButtonEnabled = true;
            UpdateButtonText = Resources.Update_UpdateNow;
            DownloadProgress = 0;
        }
    }

    /// <summary>"安装并重启" → 设置确认标志，通知调用方应用更新。</summary>
    [RelayCommand]
    private void Install()
    {
        Confirmed = true;
    }

    /// <summary>
    /// 主操作命令——根据 <see cref="IsInstallReady"/> 选择下载或安装。
    /// </summary>
    [RelayCommand]
    private async Task PrimaryActionAsync()
    {
        if (IsInstallReady)
            Install();
        else
            await UpdateNowAsync();
    }

    partial void OnIsInstallReadyChanged(bool value)
    {
        UpdateButtonText = value ? Resources.Update_InstallAndRestart : Resources.Update_UpdateNow;
    }
}
