using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SeatFlow.Application.Interfaces;
using SeatFlow.Presentation.Avalonia.Lang;
using SeatFlow.Presentation.Avalonia.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

public partial class MainShellViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly IApplicationFacade _facade;
    private readonly IOnboardingService _onboarding;
    private readonly ILogger<MainShellViewModel> _logger;

    [ObservableProperty]
    public partial ViewModelBase CurrentViewModel { get; set; } = default!;

    [ObservableProperty]
    public partial PageKey CurrentPage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarCollapsed))]
    public partial bool IsSidebarExpanded { get; set; } = true;

    public bool IsSidebarCollapsed => !IsSidebarExpanded;

    [ObservableProperty]
    public partial double SidebarWidth { get; set; } = 140;

    [ObservableProperty]
    public partial double PageOpacity { get; set; } = 1.0;

    /// <summary>是否处于首次启动引导模式。</summary>
    [ObservableProperty]
    public partial bool IsOnboardingActive { get; set; }

    private readonly Dictionary<string , bool> _pageNav = [];
    private bool _userWantsExpanded = true;
    private CancellationTokenSource? _pageLoadCts;

    /// <summary>加载页面导航配置（page_navigation.json 嵌入资源）。</summary>
    private Dictionary<string , bool> LoadPageNav ()
    {
        try
        {
            var assembly = typeof(MainShellViewModel).Assembly;
            const string resourceName = "SeatFlow.Presentation.Avalonia.Data.page_navigation.json";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return [];
            using var doc = JsonDocument.Parse(stream);
            var pages = doc.RootElement.GetProperty("pages");
            var result = new Dictionary<string , bool>();
            foreach (var p in pages.EnumerateObject())
                result[p.Name] = p.Value.GetBoolean();
            return result;
        }
        catch
        {
            _logger?.LogWarning("无法加载 page_navigation.json 嵌入资源");
            return [];
        }
    }

    private bool IsPageEnabled (string key) =>
        !_pageNav.TryGetValue(key , out var enabled) || enabled;

    /// <summary>页面淡出时长。</summary>
    private static readonly TimeSpan FadeOutDuration = TimeSpan.FromMilliseconds(200);
    /// <summary>新旧页切换间隙。</summary>
    private static readonly TimeSpan StaggerDelay = TimeSpan.FromMilliseconds(100);

    public MainShellViewModel (INavigationService navigation , IApplicationFacade facade , IOnboardingService onboarding , ILogger<MainShellViewModel>? logger = null)
    {
        _navigation = navigation;
        _facade = facade;
        _onboarding = onboarding;
        _logger = logger ?? NullLogger<MainShellViewModel>.Instance;
        _pageNav = LoadPageNav();
        _navigation.CurrentViewModelChanged += () => _ = RunTransitionAsync();
        CurrentViewModel = _navigation.CurrentViewModel;
        CurrentPage = _navigation.CurrentPage;
        // 不在此触发页面引导：初始页是 Home，无对应的 pageGuide；
        // 且此时 _config 尚未加载，触发 TryShowPageGuide 会提前消耗 LoadConfig，
        // 导致后续 StartOnboarding 在 _config 已非 null 时跳过 FlattenStartupSteps。
        // 页面引导的触发统一在 RunTransitionAsync 末尾处理。
    }

    /// <summary>页面切换：旧页淡出 → 内容切换 → 新页淡入。</summary>
    private async Task RunTransitionAsync ()
    {
        _pageLoadCts?.Cancel();
        _pageLoadCts = new CancellationTokenSource();
        var ct = _pageLoadCts.Token;

        var newVm = _navigation.CurrentViewModel;
        var newPage = _navigation.CurrentPage;

        // 引导模式：OnboardingNavigateTo 已同步设置 CurrentViewModel，
        // 跳过动画以避免 PageOpacity=0 闪烁并确保 View 立即可用。
        if (IsOnboardingActive)
        {
            SchedulePageGuideCheck();
            return;
        }

        try
        {
            PageOpacity = 0;
            await Task.Delay(FadeOutDuration , ct);

            CurrentViewModel = newVm;
            CurrentPage = newPage;

            PageOpacity = 1;
            await Task.Delay(StaggerDelay , ct);
        }
        catch (OperationCanceledException)
        {
            PageOpacity = 1;
            return;
        }

        // 页面切换完成后，检查是否需要展示页面引导
        SchedulePageGuideCheck();
    }

    /// <summary>延迟触发页面引导检查（等页面渲染完成）。</summary>
    private void SchedulePageGuideCheck ()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _onboarding.TryShowPageGuide(CurrentPage);
        } , DispatcherPriority.Background);
    }

    public void OnWindowWidthChanged (double windowWidth)
    {
        // 引导期间不自动折叠侧边栏
        if (IsOnboardingActive)
            return;

        if (windowWidth < 750)
            IsSidebarExpanded = false;
        else
            IsSidebarExpanded = _userWantsExpanded;
    }

    partial void OnIsSidebarExpandedChanged (bool value)
        => SidebarWidth = value ? 140 : 64;

    [RelayCommand]
    private void ToggleSidebar ()
    {
        _userWantsExpanded = !_userWantsExpanded;
        IsSidebarExpanded = _userWantsExpanded;
    }

    [RelayCommand]
    private async Task NavigateAsync (string pageName)
    {
        if (Enum.TryParse<PageKey>(pageName , out var key))
        {
            if (!IsPageEnabled(pageName))
                return;
            await _navigation.NavigateToAsync(key);
        }
    }

    /// <summary>强制展开侧边栏（引导期间使用）。</summary>
    public void EnsureSidebarExpanded ()
    {
        _userWantsExpanded = true;
        IsSidebarExpanded = true;
    }

    /// <summary>引导模式下的页面导航（同步，无动画）。</summary>
    /// <remarks>
    /// NavigateTo 触发 CurrentViewModelChanged → RunTransitionAsync 检测到
    /// IsOnboardingActive 并立即返回。然后直接设置 CurrentViewModel 触发 ViewLocator
    /// 同步创建新页面 View，确保目标解析时 NameScope 可用。
    /// </remarks>
    public void OnboardingNavigateTo (PageKey page)
    {
        _navigation.NavigateTo(page);
        // NavigateTo 的 CurrentViewModelChanged 事件已触发 RunTransitionAsync，
        // 后者因 IsOnboardingActive=true 在 PageOpacity=0 之前立即返回。
        // 在此直接设置 CurrentViewModel → ViewLocator 同步创建 View。
        CurrentViewModel = _navigation.CurrentViewModel;
        CurrentPage = page;
        PageOpacity = 1;
    }

    /// <summary>完成引导：关闭引导模式，持久化标记，导航到指定页面。</summary>
    public async Task CompleteOnboardingAsync (PageKey navigateTo = PageKey.Home)
    {
        IsOnboardingActive = false;

        try
        {
            var settings = await _facade.LoadAppSettingsAsync();
            if (settings.IsFirstLaunch)
            {
                settings.IsFirstLaunch = false;
                await _facade.SaveAppSettingsAsync(settings);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "无法持久化引导完成标记");
        }

        _navigation.NavigateTo(navigateTo);
    }
}
