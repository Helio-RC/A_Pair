using System;
using System.Collections.Generic;
using System.IO;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SeatFlow.Application.Interfaces;
using SeatFlow.Presentation.Avalonia.Lang;
using SeatFlow.Presentation.Avalonia.Services;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

public partial class PluginManagementViewModel (
    IApplicationFacade facade ,
    IFileService fileService ,
    ILogger<PluginManagementViewModel>? logger = null) : ViewModelBase , IFileDropHandler
{
    private readonly IApplicationFacade _facade = facade;
    private readonly IFileService _fileService = fileService;
    private readonly ILogger<PluginManagementViewModel> _logger = logger ?? NullLogger<PluginManagementViewModel>.Instance;

    /// <inheritdoc />
    public IReadOnlyList<string> AcceptedFileExtensions { get; } = [".ap-plugin"];


    public string PluginCountDisplay => string.Format(Resources.Plugin_FoundFmt , Plugins.Count);
    public string PackageCountDisplay => string.Format(Resources.Plugin_FoundFmt , Packages.Count);
    public string SelectedPluginVersionDisplay => SelectedPlugin != null ? string.Format(Resources.Plugin_VersionFmt , SelectedPlugin.Version) : "";
    public string SelectedPluginAuthorDisplay => SelectedPlugin != null ? string.Format(Resources.Plugin_AuthorFmt , SelectedPlugin.Author) : "";

    /// <summary>页面标题。</summary>
    public string Title { get; } = Resources.Plugin_Title;

    // ── 包列表 ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPackages))]
    public partial ObservableCollection<PluginPackageDisplayInfo> Packages { get; set; } = [];

    /// <summary>是否有已发现的插件包。</summary>
    public bool HasPackages => Packages.Count > 0;

    // ── 选中包 ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPackageSelected))]
    [NotifyPropertyChangedFor(nameof(PackageStrategies))]
    public partial PluginPackageDisplayInfo? SelectedPackage { get; set; }

    /// <summary>是否有选中的包。</summary>
    public bool HasPackageSelected => SelectedPackage != null;

    /// <summary>选中包内的策略列表（展平）。</summary>
    public ObservableCollection<PluginDisplayInfo> PackageStrategies =>
        SelectedPackage != null
            ? new ObservableCollection<PluginDisplayInfo>(SelectedPackage.Strategies)
            : [];

    // ── 插件列表（展平视图，保留向后兼容） ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlugins))]
    [NotifyPropertyChangedFor(nameof(EmptyHintVisible))]
    public partial ObservableCollection<PluginDisplayInfo> Plugins { get; set; } = [];

    /// <summary>是否有已发现的插件。</summary>
    public bool HasPlugins => Plugins.Count > 0;

    /// <summary>空列表提示是否可见。</summary>
    public bool EmptyHintVisible => Plugins.Count == 0;

    // ── 选中项 ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPluginSelected))]
    [NotifyPropertyChangedFor(nameof(HasScript))]
    [NotifyPropertyChangedFor(nameof(ScriptEditorVisible))]
    [NotifyPropertyChangedFor(nameof(ConfigEditorVisible))]
    public partial PluginDisplayInfo? SelectedPlugin { get; set; }

    /// <summary>是否有选中的插件。</summary>
    public bool HasPluginSelected => SelectedPlugin != null;

    /// <summary>当前选中插件是否为脚本插件。</summary>
    public bool HasScript => SelectedPlugin?.ScriptType != null;

    /// <summary>脚本编辑器是否可见。</summary>
    public bool ScriptEditorVisible => HasPluginSelected && HasScript;

    /// <summary>配置编辑器是否可见。</summary>
    public bool ConfigEditorVisible => HasPluginSelected;

    // ── 编辑器内容 ──

    [ObservableProperty]
    public partial string ScriptEditorText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConfigEditorText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsScriptDirty { get; set; }

    [ObservableProperty]
    public partial bool IsConfigDirty { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    private bool _isLoadingContent;
    private CancellationTokenSource? _loadCts;

    // ── 命令 ──

    /// <summary>
    /// 安装插件包命令：通过文件选择器选取 <c>.ap-plugin</c> 并安装。
    /// </summary>
    [RelayCommand]
    private async Task InstallPlugin ()
    {
        var file = await _fileService.OpenFileAsync(
            Resources.Plugin_Install ,
            [new FilePickerFileType("SeatFlow Plugin") { Patterns = ["*.ap-plugin"] }]);
        if (file == null) return;

        await SafeExecuteAsync(async () =>
        {
            await InstallPackageAsync(file.Path.LocalPath);
        } , Resources.Plugin_InstallFailed);
    }

    /// <summary>
    /// 安装插件包（供按钮与拖放共用）。
    /// </summary>
    /// <param name="packagePath">.ap-plugin 文件路径。</param>
    /// <param name="ct">取消令牌（拖放场景传入页面级取消令牌）。</param>
    private async Task InstallPackageAsync (string packagePath , CancellationToken ct = default)
    {
        var targetDir = await _facade.InstallPluginPackageAsync(packagePath , ct);
        _logger?.LogInformation("插件包已安装: {TargetDir}" , targetDir);
        StatusMessage = string.Format(Resources.Plugin_InstalledFmt , Path.GetFileNameWithoutExtension(packagePath));
        await RefreshPlugins();
    }

    /// <inheritdoc />
    public async Task<bool> HandleFileDropAsync (IReadOnlyList<string> filePaths , CancellationToken ct)
    {
        var packagePath = filePaths.FirstOrDefault();
        if (string.IsNullOrEmpty(packagePath))
            return false;

        var installed = false;
        await SafeExecuteAsync(async () =>
        {
            await InstallPackageAsync(packagePath , ct);
            installed = true;
        } , Resources.Plugin_InstallFailed);
        return installed;
    }

    [RelayCommand]
    private async Task RefreshPlugins ()
    {
        await SafeExecuteAsync(async () =>
        {
            // 加载包级数据
            var packages = await _facade.GetPluginPackagesAsync();
            var selectedPkgId = SelectedPackage?.PackageId;

            Packages.Clear();
            foreach (var p in packages.OrderByDescending(p => p.Strategies.FirstOrDefault()?.Priority ?? 0).ThenBy(p => p.PackageName))
                Packages.Add(p);

            // 展平到 Plugins 列表（向后兼容）
            var selectedId = SelectedPlugin?.Id;
            Plugins.Clear();
            foreach (var p in packages)
            {
                foreach (var s in p.Strategies.OrderByDescending(s => s.Priority).ThenBy(s => s.Name))
                    Plugins.Add(s);
            }

            OnPropertyChanged(nameof(HasPlugins));
            OnPropertyChanged(nameof(EmptyHintVisible));
            OnPropertyChanged(nameof(HasPackages));

            if (selectedPkgId != null)
                SelectedPackage = Packages.FirstOrDefault(p => p.PackageId == selectedPkgId);
            if (selectedId != null)
                SelectedPlugin = Plugins.FirstOrDefault(s => s.Id == selectedId);
        });
    }

    /// <summary>选中包变更时刷新策略子列表。</summary>
    partial void OnSelectedPackageChanged (PluginPackageDisplayInfo? value)
    {
        OnPropertyChanged(nameof(PackageStrategies));
        // 自动选中第一个策略
        if (value != null && value.Strategies.Count > 0 && SelectedPlugin == null)
            SelectedPlugin = value.Strategies[0];
    }

    /// <summary>选中插件变更时加载脚本和配置。</summary>
    partial void OnSelectedPluginChanged (PluginDisplayInfo? value)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();

        if (value == null)
        {
            IsScriptDirty = false;
            IsConfigDirty = false;
            ScriptEditorText = string.Empty;
            ConfigEditorText = string.Empty;
            return;
        }

        _loadCts = new CancellationTokenSource();
        _ = LoadPluginContentAsync(value , _loadCts.Token);
    }

    private async Task LoadPluginContentAsync (PluginDisplayInfo plugin , CancellationToken ct)
    {
        _isLoadingContent = true;
        try
        {
            // 加载脚本（仅脚本插件）
            if (plugin.ScriptType != null)
            {
                await SafeExecuteAsync(async () =>
                {
                    var script = await _facade.GetPluginScriptAsync(plugin.Id , ct);
                    if (ct.IsCancellationRequested) return;
                    ScriptEditorText = script;
                } , Resources.Plugin_LoadScriptFailed);
            }
            else
            {
                ScriptEditorText = string.Empty;
            }

            if (ct.IsCancellationRequested) return;

            // 加载配置 JSON
            await SafeExecuteAsync(async () =>
            {
                var config = await _facade.GetPluginConfigJsonAsync(plugin.Id , ct);
                if (ct.IsCancellationRequested) return;
                ConfigEditorText = config;
            } , Resources.Plugin_LoadConfigFailed);
        }
        finally
        {
            _isLoadingContent = false;
            IsScriptDirty = false;
            IsConfigDirty = false;
        }
    }

    [RelayCommand]
    private async Task ToggleEnabled ()
    {
        if (SelectedPlugin == null) return;

        await SafeExecuteAsync(async () =>
        {
            var newEnabled = !SelectedPlugin.IsEnabled;
            await _facade.SetPluginEnabledAsync(SelectedPlugin.Id , newEnabled);
            SelectedPlugin.IsEnabled = newEnabled;
            StatusMessage = string.Format(Resources.Plugin_ToggledFmt , SelectedPlugin.Name , newEnabled ? Resources.Common_Enabled : Resources.Common_Disabled);
        } , Resources.Plugin_ToggleFailed);
    }

    [RelayCommand]
    private async Task TogglePackageEnabled ()
    {
        if (SelectedPackage == null) return;

        await SafeExecuteAsync(async () =>
        {
            var newEnabled = !SelectedPackage.IsEnabled;
            await _facade.SetPluginPackageEnabledAsync(SelectedPackage.PackageId , newEnabled);
            SelectedPackage.IsEnabled = newEnabled;
            StatusMessage = string.Format(Resources.Plugin_ToggledFmt , SelectedPackage.PackageName , newEnabled ? Resources.Common_Enabled : Resources.Common_Disabled);
        } , Resources.Plugin_ToggleFailed);
    }

    [RelayCommand]
    private async Task UninstallPackage ()
    {
        if (SelectedPackage == null) return;

        await SafeExecuteAsync(async () =>
        {
            var packageName = SelectedPackage.PackageName;
            await _facade.UninstallPluginPackageAsync(SelectedPackage.PackageId);
            _logger?.LogInformation("插件包已卸载: {PackageName}", packageName);
            StatusMessage = string.Format(Resources.Plugin_ToggledFmt , packageName , "已卸载");
            await RefreshPlugins();
        } , Resources.Plugin_ToggleFailed);
    }

    [RelayCommand]
    private async Task SaveScript ()
    {
        if (SelectedPlugin == null) return;

        await SafeExecuteAsync(async ct =>
        {
            await _facade.SavePluginScriptAsync(SelectedPlugin.Id , ScriptEditorText , ct);
            IsScriptDirty = false;
            StatusMessage = string.Format(Resources.Plugin_ScriptSavedFmt , SelectedPlugin.Name);
        } , TimeSpan.FromSeconds(30) , Resources.Plugin_ScriptSaveFailed);
    }

    [RelayCommand]
    private async Task SaveConfig ()
    {
        if (SelectedPlugin == null) return;

        await SafeExecuteAsync(async ct =>
        {
            // 预验证 JSON 格式
            try
            {
                JsonDocument.Parse(ConfigEditorText);
            }
            catch (JsonException ex)
            {
                StatusMessage = string.Format(Resources.Plugin_JSONErrorFmt , ex.Message);
                return;
            }

            await _facade.SavePluginConfigJsonAsync(SelectedPlugin.Id , ConfigEditorText , ct);
            IsConfigDirty = false;
            StatusMessage = string.Format(Resources.Plugin_ConfigSavedFmt , SelectedPlugin.Name);
        } , TimeSpan.FromSeconds(30) , Resources.Plugin_ConfigSaveFailed);
    }

    // ── 编辑器内容变更标记 ──

    partial void OnScriptEditorTextChanged (string value)
    {
        if (!_isLoadingContent && SelectedPlugin?.ScriptType != null)
            IsScriptDirty = true;
    }

    partial void OnConfigEditorTextChanged (string value)
    {
        if (!_isLoadingContent && SelectedPlugin != null)
            IsConfigDirty = true;
    }

    public override async Task<bool> CanLeaveAsync ()
    {
        if (!IsScriptDirty && !IsConfigDirty)
        {
            ClearPluginState();
            return true;
        }

        var choice = await Dialog.ShowMultiOptionAsync(
            Resources.Plugin_UnsavedChanges ,
            Resources.Plugin_UnsavedChangesMsg ,
            Resources.Common_Save ,
            Resources.Common_Discard ,
            Resources.Common_Cancel);

        switch (choice)
        {
            case 0: // 保存
                if (IsScriptDirty) await SaveScript();
                if (IsConfigDirty) await SaveConfig();
                break;
            case 1: // 放弃
                break;
            default: // 取消
                return false;
        }

        ClearPluginState();
        return true;
    }

    private void ClearPluginState ()
    {
        SelectedPlugin = null;
        ScriptEditorText = string.Empty;
        ConfigEditorText = string.Empty;
        IsScriptDirty = false;
        IsConfigDirty = false;
    }
}
