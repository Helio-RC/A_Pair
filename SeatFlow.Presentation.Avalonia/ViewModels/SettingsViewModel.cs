using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Core.Telemetry;
using SeatFlow.Core.Utilities;
using SeatFlow.Presentation.Avalonia.Lang;
using SeatFlow.Presentation.Avalonia.Services;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using AvaloniaApplication = Avalonia.Application;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IApplicationFacade _facade;
    private readonly IDialogService _dialog;
    private readonly IOnboardingService _onboarding;
    private readonly IFileService _fileService;
    private readonly ITelemetryService _telemetry;
    private readonly IUpdateService _updateService;
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty]
    public partial ThemeMode Theme { get; set; }

    [ObservableProperty]
    public partial int ThemeIndex { get; set; }
    public List<string> ThemeOptions { get; } = [Resources.Theme_System , Resources.Theme_Light , Resources.Theme_Dark];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedLanguage))]
    public partial string Language { get; set; } = string.Empty;
    public List<LanguageOption> LanguageOptions { get; } =
    [
        new("", () => Resources.Lang_System) ,
        new("zh-CN", () => Resources.Lang_zhCN) ,
        new("en-US", () => Resources.Lang_enUS) ,
    ];

    private LanguageOption? _selectedLanguage;

    public LanguageOption? SelectedLanguage
    {
        get => _selectedLanguage ?? LanguageOptions.Find(static o => o.Code == "");
        set
        {
            if (SetProperty(ref _selectedLanguage , value))
                Language = value?.Code ?? "";
        }
    }

    [ObservableProperty]
    public partial string DataDirectory { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ConfirmBeforeClear { get; set; } = true;

    [ObservableProperty]
    public partial int ZoomIndex { get; set; } = 1;
    public List<string> ZoomOptions { get; } = [Resources.Zoom_75 , Resources.Zoom_100 , Resources.Zoom_125 , Resources.Zoom_150];

    private double _defaultZoomLevel = 1.0;
    private int _dialogLock;
    private string _originalLanguage = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int MaxSnapshotsPerVenue { get; set; } = 30;

    [ObservableProperty]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    public partial bool TelemetryEnabled { get; set; }

    [ObservableProperty]
    public partial bool SuppressEnvironmentWarning { get; set; }

    [ObservableProperty]
    public partial int LogLevelIndex { get; set; } = 1;
    public List<string> LogLevelOptions { get; } =
    [
        Resources.Settings_LogLevel_Debug,
        Resources.Settings_LogLevel_Info,
        Resources.Settings_LogLevel_Warning,
        Resources.Settings_LogLevel_Error
    ];

    // ---- 更新相关属性 ----

    [ObservableProperty]
    public partial string UpdateStatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsCheckingUpdate { get; set; }

    [ObservableProperty]
    public partial bool IsUpdateAvailable { get; set; }

    [ObservableProperty]
    public partial string? UpdateVersionText { get; set; }

    [ObservableProperty]
    public partial int UpdateDownloadProgress { get; set; }

    [ObservableProperty]
    public partial bool IsDownloading { get; set; }

    /// <summary>是否有已下载但未应用的更新（重启即可应用）。</summary>
    [ObservableProperty]
    public partial bool HasPendingUpdate { get; set; }

    public SettingsViewModel (IApplicationFacade facade , IDialogService dialog , IOnboardingService onboarding , IFileService fileService , ITelemetryService telemetry , IUpdateService updateService , ILogger<SettingsViewModel>? logger = null)
    {
        _facade = facade;
        _dialog = dialog;
        _onboarding = onboarding;
        _fileService = fileService;
        _telemetry = telemetry;
        _updateService = updateService;
        _logger = logger ?? NullLogger<SettingsViewModel>.Instance;
        _ = LoadAsync(CancellationToken.None);
    }

    private async Task LoadAsync (CancellationToken ct)
    {
        try
        {
            var settings = await _facade.LoadAppSettingsAsync(ct);

            Theme = settings.Theme;
            ThemeIndex = Theme switch { ThemeMode.Light => 1, ThemeMode.Dark => 2, _ => 0 };

            Language = settings.Language;
            _originalLanguage = settings.Language;
            _selectedLanguage = LanguageOptions.FirstOrDefault(o => o.Code == Language);
            OnPropertyChanged(nameof(SelectedLanguage));

            DataDirectory = settings.DataDirectory;

            ConfirmBeforeClear = settings.ConfirmBeforeClear;

            _defaultZoomLevel = settings.DefaultZoomLevel;
            ZoomIndex = _defaultZoomLevel switch { 0.75 => 0, 1.0 => 1, 1.25 => 2, 1.5 => 3, _ => 1 };

            MaxSnapshotsPerVenue = settings.MaxSnapshotsPerVenue;

            TelemetryEnabled = settings.Telemetry.Enabled;

            SuppressEnvironmentWarning = settings.SuppressEnvironmentWarning;
            var logLevel = settings.Logging.MinimumLevel;
            LogLevelIndex = logLevel switch { "Debug" => 0 , "Warning" => 2 , "Error" => 3 , _ => 1 };

            // 检查是否有已下载但未应用的更新
            RefreshPendingUpdateState();

        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "加载设置失败");
            StatusMessage = Resources.Settings_LoadFailed;
        }
    }

    partial void OnLanguageChanged (string value)
    {
        var option = LanguageOptions.FirstOrDefault(o => o.Code == value);
        if (option != null && !ReferenceEquals(option , _selectedLanguage))
        {
            _selectedLanguage = option;
            OnPropertyChanged(nameof(SelectedLanguage));
        }
    }

    partial void OnThemeIndexChanged (int value)
    {
        var mode = value switch { 1 => ThemeMode.Light, 2 => ThemeMode.Dark, _ => ThemeMode.System };
        if (Theme == mode) return;
        Theme = mode;

        if (AvaloniaApplication.Current is { } app)
        {
            app.RequestedThemeVariant = mode switch
            {
                ThemeMode.Light => ThemeVariant.Light,
                ThemeMode.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
        }
    }

    partial void OnZoomIndexChanged (int value)
    {
        var zoom = value switch { 0 => 0.75, 1 => 1.0, 2 => 1.25, 3 => 1.5, _ => 1.0 };
        _defaultZoomLevel = zoom;
    }

    partial void OnLogLevelIndexChanged (int value) { }

    [RelayCommand]
    private async Task SaveSettingsAsync (CancellationToken ct)
    {
        try
        {
            IsSaving = true;
            StatusMessage = Resources.Settings_Saving;

            var settings = await _facade.LoadAppSettingsAsync(ct);

            // 直接在现有对象上修改，保留所有其他字段（CompletedPageGuides、Logging、Telemetry 等）
            settings.Theme = Theme;
            settings.Language = Language;
            settings.DataDirectory = DataDirectory;
            settings.ConfirmBeforeClear = ConfirmBeforeClear;
            settings.DefaultZoomLevel = _defaultZoomLevel;
            settings.MaxSnapshotsPerVenue = MaxSnapshotsPerVenue;
            settings.Telemetry.Enabled = TelemetryEnabled;
            settings.SuppressEnvironmentWarning = SuppressEnvironmentWarning;
            settings.Logging.MinimumLevel = LogLevelIndex switch { 0 => "Debug", 2 => "Warning", 3 => "Error", _ => "Information" };

            await _facade.SaveAppSettingsAsync(settings , ct);

            // 同步内存中的遥测状态（SetEnabled 内部也会持久化，此时 settings 已完整）
            _telemetry.SetEnabled(TelemetryEnabled);

            var langChanged = !string.Equals(_originalLanguage , Language , StringComparison.Ordinal);
            _originalLanguage = Language;

            if (langChanged)
            {
                var clicked = await _dialog.ShowMultiOptionAsync(
                    Resources.Settings_LangChangedTitle ,
                    Resources.Settings_LangChangedMessage ,
                    Resources.Settings_LangChangedRestart ,
                    Resources.Common_Later);
                if (clicked == 0)
                {
                    Process.Start(Environment.ProcessPath!);
                    if (AvaloniaApplication.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                        desktop.Shutdown();
                    return;
                }
            }

            StatusMessage = Resources.Settings_Saved;
        }
        catch (Exception ex)
        {
            StatusMessage = Resources.Settings_SaveFailed;
            await _dialog.ShowErrorAsync(Resources.Settings_SaveFailed , ex.Message);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task ResetDefaultsAsync ()
    {
        var confirmed = await _dialog.ShowConfirmAsync(Resources.Settings_ResetTitle , Resources.Settings_ResetConfirm);
        if (!confirmed) return;

        ThemeIndex = 0;
        Language = "";
        DataDirectory = string.Empty;
        ConfirmBeforeClear = true;
        ZoomIndex = 1;
        SuppressEnvironmentWarning = false;
        LogLevelIndex = 1;
        StatusMessage = Resources.Settings_ResetDone;
    }

    [RelayCommand]
    private async Task BrowseDataDirectoryAsync (CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _dialogLock , 1 , 0) != 0) return;
        try
        {
            try
            {
                if (AvaloniaApplication.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                    return;

                var storageProvider = desktop.MainWindow?.StorageProvider;
                if (storageProvider is null) return;

                var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = Resources.Settings_FolderTitle ,
                    AllowMultiple = false
                });

                if (folders.Count > 0)
                    DataDirectory = folders[0].Path.LocalPath;
            }
            catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException) { _logger.LogDebug(ex , "目录选择取消"); }
            catch (Exception ex)
            {
                await _dialog.ShowErrorAsync(Resources.Settings_FolderFailed , ex.Message);
            }
        }
        finally { await Task.Delay(150 , CancellationToken.None); Interlocked.Exchange(ref _dialogLock , 0); }
    }

    [RelayCommand]
    private async Task RestartGuideAsync ()
    {
        try
        {
            var settings = await _facade.LoadAppSettingsAsync();
            settings.IsFirstLaunch = true;
            await _facade.SaveAppSettingsAsync(settings);

            _onboarding.StartOnboarding();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "重新启动引导失败");
        }
    }

    [RelayCommand]
    private async Task ExportSeatSetsAsync (CancellationToken ct)
    {
        string? exportPath = null;
        try
        {
            // 1. 显示选择对话框（导出模式）
            var selectionWindow = new Views.SeatSetsSelectionWindow
            {
                IsExport = true
            };

            if (AvaloniaApplication.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return;

            var confirmed = await selectionWindow.ShowDialog<bool>(desktop.MainWindow!);
            if (!confirmed) return;

            var selection = selectionWindow.ViewModel.ToSelection();

            // 2. 文件保存对话框
            var defaultFileName = string.Format(Resources.SeatSets_DefaultFileName)
                + $"_{DateTime.Now:yyyyMMdd_HHmm}";
            var seatSetsFilter = new FilePickerFileType("SeatFlow Data Package")
            {
                Patterns = ["*.seatsets"]
            };

            var file = await _fileService.SaveFileAsync(
                Resources.SeatSets_ExportTitle,
                [seatSetsFilter],
                defaultFileName);

            if (file is null) return;

            // 3. 执行导出
            StatusMessage = Resources.SeatSets_Processing;
            exportPath = file.Path.LocalPath;
            var count = await _facade.ExportSeatSetsAsync(exportPath, selection, ct);

            // 4. 显示结果
            if (count > 0)
            {
                StatusMessage = string.Format(Resources.SeatSets_ExportSuccess, count);
                await _dialog.ShowInfoAsync(Resources.SeatSets_ExportTitle,
                    string.Format(Resources.SeatSets_ExportSuccess, count));
            }
            else
            {
                StatusMessage = Resources.SeatSets_NoDataAvailable;
                await _dialog.ShowWarningAsync(Resources.SeatSets_ExportTitle,
                    Resources.SeatSets_NoDataAvailable);
            }
        }
        catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
        {
            _logger.LogDebug(ex, "导出操作取消");
        }
        catch (Exception ex)
        {
            StatusMessage = Resources.SeatSets_ExportFailed;
            _logger.LogError(ex, "导出数据包失败: {Path}", exportPath);
            await _dialog.ShowErrorAsync(Resources.SeatSets_ExportFailed, ex.Message);
        }
    }

    [RelayCommand]
    private async Task ImportSeatSetsAsync (CancellationToken ct)
    {
        string? importPath = null;
        try
        {
            // 1. 文件选择对话框
            var seatSetsFilter = new FilePickerFileType("SeatFlow Data Package")
            {
                Patterns = ["*.seatsets"]
            };

            var file = await _fileService.OpenFileAsync(
                Resources.SeatSets_ImportTitle,
                [seatSetsFilter]);

            if (file is null) return;

            importPath = file.Path.LocalPath;

            // 2. 校验文件
            StatusMessage = Resources.SeatSets_Processing;
            var validation = await _facade.ValidateSeatSetsAsync(importPath, ct);

            if (!validation.IsValid)
            {
                var errors = string.Join("\n", validation.ValidationErrors);
                await _dialog.ShowErrorAsync(Resources.SeatSets_InvalidFile,
                    string.IsNullOrEmpty(errors) ? Resources.SeatSets_IntegrityFailed : errors);
                StatusMessage = "";
                return;
            }

            // 3. 探测并显示选择对话框（导入模式）
            var categories = await _facade.ProbeSeatSetsCategoriesAsync(importPath, ct);
            var selectionWindow = new Views.SeatSetsSelectionWindow
            {
                IsExport = false
            };
            selectionWindow.SetAvailableCategories(
                categories.IncludeAppSettings,
                categories.IncludeVenues,
                categories.IncludeRosters,
                categories.IncludeSnapshots,
                categories.IncludeStrategyConfig);

            if (AvaloniaApplication.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return;

            var confirmed = await selectionWindow.ShowDialog<bool>(desktop.MainWindow!);
            if (!confirmed) return;

            var selection = selectionWindow.ViewModel.ToSelection();

            // 4. 执行导入
            StatusMessage = Resources.SeatSets_Processing;
            var result = await _facade.ImportSeatSetsAsync(importPath, selection, progress: null, ct);

            // 5. 显示结果
            if (result.Success)
            {
                StatusMessage = string.Format(Resources.SeatSets_ImportSuccess, result.Restored);
                await _dialog.ShowInfoAsync(Resources.SeatSets_ImportTitle,
                    string.Format(Resources.SeatSets_ImportSuccess, result.Restored));
            }
            else if (result.Failed > 0 && result.Restored > 0)
            {
                StatusMessage = string.Format(Resources.SeatSets_ImportPartial,
                    result.Restored, result.TotalFiles, result.Failed);
                var errorDetails = result.Errors.Count > 0
                    ? "\n\n" + string.Join("\n", result.Errors.Take(5))
                    : "";
                await _dialog.ShowWarningAsync(Resources.SeatSets_ImportTitle,
                    string.Format(Resources.SeatSets_ImportPartial,
                        result.Restored, result.TotalFiles, result.Failed) + errorDetails);
            }
            else
            {
                StatusMessage = Resources.SeatSets_ImportPartial;
                var errorDetails = result.Errors.Count > 0
                    ? "\n" + string.Join("\n", result.Errors.Take(5))
                    : "";
                await _dialog.ShowErrorAsync(Resources.SeatSets_ImportTitle,
                    string.Join("\n", result.Errors.Take(10)) + errorDetails);
            }

            // 6. 导入后刷新：应用新设置（主题/语言）并导航到主页
            if (result.Restored > 0 && importPath != null)
            {
                try
                {
                    if (AvaloniaApplication.Current is App app)
                        await App.RefreshAfterImportAsync(app.ServiceProvider);
                }
                catch { /* 刷新失败不影响导入结果 */ }
            }
        }
        catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
        {
            _logger.LogDebug(ex, "导入操作取消");
        }
        catch (Exception ex)
        {
            StatusMessage = Resources.SeatSets_ImportTitle + ": " + ex.Message;
            _logger.LogError(ex, "导入数据包失败: {Path}", importPath);
            await _dialog.ShowErrorAsync(Resources.SeatSets_ImportTitle, ex.Message);
        }
    }

    [RelayCommand]
    private void OpenDataDirectory ()
    {
        var path = string.IsNullOrWhiteSpace(DataDirectory)
            ? AppEnvironment.DefaultDataDirectory
            : DataDirectory;

        try
        {
            if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else
                _ = _dialog.ShowWarningAsync(Resources.Settings_DirNotFound ,
                    string.Format(Resources.Settings_DirNotFoundFormat , path));
        }
        catch (Exception ex)
        {
            _ = _dialog.ShowErrorAsync(Resources.Settings_OpenDirFailed , ex.Message);
        }
    }

    // ---- 更新命令 ----

    [RelayCommand]
    private async Task CheckForUpdateAsync ()
    {
        if (IsCheckingUpdate)
            return;

        try
        {
            IsCheckingUpdate = true;
            IsUpdateAvailable = false;
            UpdateStatusMessage = Resources.Settings_UpdateChecking;

            var result = await _updateService.CheckForUpdatesAsync();

            if (result.ServiceStatus == UpdateServiceStatus.NotInstalled)
            {
                UpdateStatusMessage = Resources.Settings_UpdateNotInstalled;
                return;
            }

            if (result.ServiceStatus == UpdateServiceStatus.Fallback)
                UpdateStatusMessage = Resources.Settings_UpdateFallback;

            if (result.HasUpdate)
            {
                IsUpdateAvailable = true;
                UpdateVersionText = result.NewVersion;
                UpdateStatusMessage = string.Format(Resources.Settings_UpdateAvailable, result.NewVersion ?? "");
            }
            else if (result.ServiceStatus != UpdateServiceStatus.Fallback)
            {
                UpdateStatusMessage = Resources.Settings_UpdateLatest;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "更新检查失败");
            UpdateStatusMessage = Resources.Settings_UpdateFailed;
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    /// <summary>
    /// 检查是否有已下载但尚未应用的更新。
    /// 在页面加载时调用，避免用户下载后关闭应用再打开时丢失待应用状态。
    /// </summary>
    private void RefreshPendingUpdateState ()
    {
        HasPendingUpdate = _updateService.UpdatePendingRestart;
        if (HasPendingUpdate)
        {
            UpdateStatusMessage = Resources.Settings_UpdatePendingRestart;
            IsUpdateAvailable = true;
        }
    }

    [RelayCommand]
    private async Task DownloadAndApplyUpdateAsync ()
    {
        if (IsDownloading)
            return;

        try
        {
            IsDownloading = true;
            UpdateDownloadProgress = 0;

            var progress = new Progress<int>(p =>
            {
                UpdateDownloadProgress = p;
                UpdateStatusMessage = string.Format(Resources.Settings_UpdateDownloading, p);
            });

            await _updateService.DownloadUpdatesAsync(progress);

            UpdateDownloadProgress = 100;
            UpdateStatusMessage = Resources.Settings_UpdateApply;

            // 下载完成后确认重启——避免丢失未保存的工作
            var restart = await _dialog.ShowConfirmAsync(
                Resources.Settings_UpdateApply,
                Resources.Settings_UpdateRestartConfirm);
            if (!restart) return;

            // 应用更新并重启
            _updateService.ApplyUpdatesAndRestart();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "更新下载失败");
            UpdateStatusMessage = Resources.Settings_UpdateFailed;
        }
        finally
        {
            IsDownloading = false;
        }
    }

    /// <summary>
    /// 应用已下载的更新并重启（无需重新下载）。
    /// </summary>
    [RelayCommand]
    private async Task ApplyPendingUpdateAndRestartAsync ()
    {
        if (!_updateService.UpdatePendingRestart)
            return;

        var restart = await _dialog.ShowConfirmAsync(
            Resources.Settings_UpdateApply,
            Resources.Settings_UpdateRestartConfirm);
        if (!restart) return;

        _updateService.ApplyUpdatesAndRestart();
    }
}

public sealed record LanguageOption
{
    private readonly Func<string> _displayNameProvider;

    public string Code { get; }
    public string DisplayName => _displayNameProvider();

    public LanguageOption (string code , Func<string> displayNameProvider)
    {
        Code = code;
        _displayNameProvider = displayNameProvider;
    }

    public override string ToString () => DisplayName;

    public bool Equals (LanguageOption? other) => other is not null && Code == other.Code;
    public override int GetHashCode () => Code.GetHashCode();
}
