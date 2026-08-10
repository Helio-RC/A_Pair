using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Enums;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.Lang;
using SeatFlow.Presentation.Avalonia.ViewModels;
using SeatFlow.Presentation.Avalonia.Views;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CodeWF.AvaloniaControls.Controls;
using Microsoft.Extensions.Logging;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// 引导服务——纯机械式桥接 JSON 配置与 Guide 控件。
/// 支持启动引导（startupPhases）和页面独立引导块（pageGuides，首次访问触发）。
/// </summary>
public sealed class OnboardingService : IOnboardingService, IOnboardingStarter
{
    private readonly INavigationService _navigation;
    private readonly IApplicationFacade _facade;
    private readonly IDialogService _dialog;
    private readonly ILogger<OnboardingService> _logger;

    private OnboardingConfig? _config;
    // 当前活跃的步骤定义列表（启动引导=全量平铺，页面引导=单页面步骤）
    private List<OnboardingStepDefinition> _activeStepDefs = [];
    // 各阶段在 _activeStepDefs 中的起始索引（仅启动引导有效）
    private List<int> _activePhaseBoundaries = [];
    private Dictionary<string , bool> _completedPageGuides = []; // PageKey名称 → true
    private bool _completedPageGuidesLoaded; // LoadCompletedPageGuidesAsync 是否已完成
    private string? _currentPageGuide; // null=启动引导, 非null=页面引导的PageKey名称
    private Guide? _guide;
    private bool _isCompleting;
    /// <summary>窗口失焦/最小化时设为 true，静默关闭 Popup 防孤儿窗口。</summary>
    private bool _isWindowObscured;

    /// <summary>MemberManagement 演示数据是否已实际注入（用于 ClearPageData 判断是否需要清理）。</summary>
    private static bool _memberManagementDemoInjected;

    /// <summary>演示数据集的固定 ID，用于注入和清理时识别。</summary>
    private const string DemoDatasetId = "guide-demo-ds";

    /// <summary>MemberManagement 用户状态快照（引导前保存，引导后恢复）。null 表示首次使用无需恢复。</summary>
    private static List<Student>? _savedMemberStudents;
    private static List<StudentDatasetInfo>? _savedMemberDatasets;
    private static bool _savedMemberIsEmpty;

    public bool IsActive { get; private set; }

    public OnboardingService (
        INavigationService navigation ,
        IApplicationFacade facade ,
        IDialogService dialog ,
        ILogger<OnboardingService> logger)
    {
        _navigation = navigation;
        _facade = facade;
        _dialog = dialog;
        _logger = logger;
    }

    // ──────────────────────── IOnboardingStarter (旧接口桥接) ────────────────────────
    void IOnboardingStarter.StartOnboarding () => StartOnboarding();

    // ──────────────────────── 公开 API ────────────────────────

    public void StartOnboarding ()
    {
        _logger.LogInformation("[Onboarding] StartOnboarding 开始");

        if (_config is null)
        {
            _config = LoadConfig();
            _logger.LogInformation("[Onboarding] 配置已加载，StartupPhases={Count}" , _config.StartupPhases.Count);
            _ = LoadCompletedPageGuidesAsync();
        }
        // 始终重新平铺——_config 可能已被 TryShowPageGuide 提前加载（MainShellViewModel 构造时触发），
        // 此时 _activeStepDefs 存的是页面引导的步骤而非启动引导步骤，必须重建
        FlattenStartupSteps();
        _logger.LogInformation("[Onboarding] 步骤已平铺，共 {Count} 步" , _activeStepDefs.Count);

        _isCompleting = false;
        _currentPageGuide = null;
        IsActive = true;

        var mainWindow = GetMainWindow();
        _logger.LogInformation("[Onboarding] MainWindow={NotNull}" , mainWindow is not null);
        if (mainWindow?.DataContext is MainShellViewModel vm)
        {
            _logger.LogInformation("[Onboarding] 设置 IsOnboardingActive=true，导航到 Home");
            vm.IsOnboardingActive = true;
            vm.EnsureSidebarExpanded();
            vm.OnboardingNavigateTo(PageKey.Home);
        }
        else
        {
            _logger.LogWarning("[Onboarding] MainShellViewModel 不可用！DataContext={Type}" , mainWindow?.DataContext?.GetType().FullName);
        }

        _guide = mainWindow?.OnboardingGuide;
        _logger.LogInformation("[Onboarding] Guide 控件={NotNull}" , _guide is not null);
        if (_guide is not null)
        {
            _guide.StepOpening += OnStepOpening;
            _guide.StepOpened += OnStepOpened;
        }
        else
            _logger.LogError("[Onboarding] OnboardingGuide 控件为 null！无法显示引导");

        // 使用 Background 优先级（而非 Loaded），因为 Loaded 依赖布局 pass，
        // 但如果当前页面已是 Home，NavigateTo 会跳过导航，不触发布局 pass，
        // 导致 Loaded 回调永远不执行。Guide 控件自带 TargetResolveDelay 重试。
        Dispatcher.UIThread.Post(() =>
        {
            _logger.LogInformation("[Onboarding] Post 回调执行，_guide={NotNull}" , _guide is not null);
            if (_guide is null) return;
            var steps = BuildAllStartupSteps();
            _logger.LogInformation("[Onboarding] 构建了 {Count} 个 GuideStepOption" , steps.Count);
            _guide.StepsSource = steps;
            _guide.GoTo(0);
            _guide.IsVisible = true;
            _guide.Show();
            // 首次出场：卡片缩放弹出动画（无延迟）
            _logger.LogInformation("[Onboarding] Guide.Show() 已调用，IsOpen={IsOpen}" , _guide.IsOpen);
        } , DispatcherPriority.Background);
    }

    /// <summary>检查并触发页面的独立引导块（首次访问时）。返回 true 表示触发了引导。</summary>
    public bool TryShowPageGuide (PageKey page)
    {
        if (IsActive) return false;

        // 延迟加载：首次访问页面时可能尚未触发过启动引导
        if (_config is null)
        {
            _config = LoadConfig();
            _ = LoadCompletedPageGuidesAsync();
        }

        var pageKey = page.ToString();
        if (!_config.PageGuides.TryGetValue(pageKey , out var guideBlock))
            return false;

        // 已展示过则跳过。若 CompletedPageGuides 尚未加载完成，
        // 内存中的 _completedPageGuides 为空，首次访问会触发引导；
        // 后续访问时 LoadCompletedPageGuidesAsync 已完成，正确跳过。
        if (_completedPageGuidesLoaded && _completedPageGuides.ContainsKey(pageKey))
            return false;

        var mainWindow = GetMainWindow();
        var guideControl = mainWindow?.OnboardingGuide;
        if (guideControl is null) return false;

        _logger.LogInformation("[Onboarding] 触发页面引导: {PageKey}", pageKey);

        _isCompleting = false;
        _currentPageGuide = pageKey;
        IsActive = true;

        // 构建仅此页面的步骤
        _activeStepDefs.Clear();
        _activePhaseBoundaries.Clear();
        _activeStepDefs.AddRange(guideBlock.Steps);

        _guide = guideControl;
        _guide.StepOpening += OnStepOpening;
        _guide.StepOpened += OnStepOpened;

        Dispatcher.UIThread.Post(() =>
        {
            if (_guide is null) return;
            _guide.StepsSource = BuildStepsFromDefs(_activeStepDefs);
            _guide.GoTo(0);
            _guide.IsVisible = true;
            _guide.Show();
        } , DispatcherPriority.Background);

        return true;
    }

    /// <summary>标记页面引导已完成并持久化。</summary>
    public async Task MarkPageGuideShownAsync (PageKey page)
    {
        var pageKey = page.ToString();
        _completedPageGuides[pageKey] = true;

        try
        {
            var settings = await _facade.LoadAppSettingsAsync();
            settings.CompletedPageGuides[pageKey] = true;
            await _facade.SaveAppSettingsAsync(settings);
        }
        catch
        {
            // 持久化失败不影响用户体验
        }
    }

    public void HandleStepOpening (int stepIndex , IGuideStepOption step)
    {
        if (stepIndex < 0 || stepIndex >= _activeStepDefs.Count) return;

        var stepDef = _activeStepDefs[stepIndex];

        // 1. 先处理跨阶段页面导航（在解析 Target 之前），
        //    确保目标控件所在的新页面 View 已创建，NameScope 可用。
        //    OnboardingNavigateTo 同步设置 CurrentViewModel 触发 ViewLocator，
        //    RunTransitionAsync 因 IsOnboardingActive=true 提前返回，无闪烁。
        bool isPhaseTransition = false;
        PageKey targetPage = default;
        var phaseIndex = GetPhaseIndex(stepIndex);
        if (_currentPageGuide is null)
        {
            if (phaseIndex > 0 && _activePhaseBoundaries[phaseIndex] == stepIndex)
            {
                var phase = _config!.StartupPhases[phaseIndex];
                if (phase.Page is not null
                    && Enum.TryParse<PageKey>(phase.Page , out var pageKey))
                {
                    isPhaseTransition = true;
                    targetPage = pageKey;

                    // 仅在目标页面与当前页面不同时才执行导航
                    //（同页面连续两个 Phase 不需要导航，但仍需注入数据）
                    if (_navigation.CurrentPage != pageKey)
                    {
                        var mainWindow = GetMainWindow();
                        if (mainWindow?.DataContext is MainShellViewModel vm)
                            vm.OnboardingNavigateTo(pageKey);
                        else
                            _navigation.NavigateTo(pageKey);
                    }
                }
            }
        }

        // 注入示例数据：由 phase.SeedData 声明式控制（仅启动引导的跨阶段导航）
        if (isPhaseTransition)
        {
            var phase = _config!.StartupPhases[phaseIndex];
            if (phase.SeedData)
            {
                // MemberManagement 第二次进入：先代码内离开-重入，强制页面重建后再注入
                if (targetPage == PageKey.MemberManagement)
                {
                    var mainWindow = GetMainWindow();
                    if (mainWindow?.DataContext is MainShellViewModel shell)
                    {
                        shell.OnboardingNavigateTo(PageKey.Home);
                        shell.OnboardingNavigateTo(PageKey.MemberManagement);
                    }
                }

                SeedPageData(targetPage);
            }
        }

        // 2. 解析 Target 控件名 → 实际 Control
        //    此时新页面 View 已在可视化树中（步骤 1 的同步导航确保），NameScope 可用。
        if (!string.IsNullOrEmpty(stepDef.Target))
        {
            var ctrl = ResolveTarget(stepDef.Target);
            step.Target = ctrl;
        }
    }

    public void HandleGuideCompleted ()
    {
        _isCompleting = true;
        _ = CompleteOnboardingAsync();
    }

    public async Task<bool> HandleGuideClosedAsync ()
    {
        if (_isCompleting) return true;
        if (_isWindowObscured) return true;

        try
        {
            var confirmed = await _dialog.ShowConfirmAsync(
                Resources.Guide_CloseConfirm_Title ,
                Resources.Guide_CloseConfirm_Message);
            if (!confirmed) return false;
        }
        catch { }

        _ = CompleteOnboardingAsync();
        return true;
    }

    /// <summary>窗口失活（最小化/Alt+Tab）时静默隐藏 Guide Popup，不结束引导。</summary>
    public void HandleWindowDeactivated ()
    {
        if (!IsActive || _isCompleting || _guide is null || !_guide.IsOpen)
            return;

        _isWindowObscured = true;
        _guide.Close();
    }

    /// <summary>窗口激活（恢复/Alt+Tab回）时重新显示 Guide，从同一步骤继续。</summary>
    public void HandleWindowActivated ()
    {
        if (!IsActive || _guide is null)
            return;
        if (!_isWindowObscured)
            return;

        _isWindowObscured = false;
        if (!_guide.IsOpen)
            _guide.Show();
    }

    // ──────────────────────── 内部实现 ────────────────────────

    // ──────────────────────── 示例数据注入（纯内存，不落盘） ────────────────────────

    /// <summary>根据页面注入示例数据，确保引导期间条件可见的目标控件正常显示。</summary>
    private static void SeedPageData (PageKey page)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow?.DataContext is not MainShellViewModel shell)
            return;

        var pageVm = shell.CurrentViewModel;

        switch (page)
        {
            case PageKey.MemberManagement:
                SeedMemberManagementData(pageVm as MemberManagementViewModel);
                break;
            case PageKey.VenueConfiguration:
                SeedVenueConfigurationData(pageVm as VenueConfigurationViewModel);
                break;
            case PageKey.StrategyConfiguration:
                SeedStrategyConfigurationData(pageVm as StrategyConfigurationViewModel);
                break;
            case PageKey.SeatingArrangement:
                SeedSeatingArrangementData(pageVm as SeatingArrangementViewModel);
                break;
            case PageKey.SnapshotHistory:
                SeedSnapshotHistoryData(pageVm as SnapshotHistoryViewModel);
                break;
        }
    }

    private static void SeedMemberManagementData (MemberManagementViewModel? vm)
    {
        if (vm is null) return;

        // 保存用户原有状态，引导结束后恢复（始终执行，即使跳过注入）
        _savedMemberStudents = [.. vm.Students];
        _savedMemberDatasets = [.. vm.SavedDatasets];
        _savedMemberIsEmpty = vm.IsEmpty;

        // 若用户已在 Phase 1 导入数据，不覆盖
        if (vm.Students.Count > 0) return;

        vm.Students = new ObservableCollection<Student>
        {
            new() { Name = "Alice", Height = 165, Gender = Gender.Female },
            new() { Name = "Bob", Height = 175, Gender = Gender.Male, NeedsFrontRow = true },
            new() { Name = "Charlie", Height = 180, Gender = Gender.Male },
            new() { Name = "Diana", Height = 160, Gender = Gender.Female },
            new() { Name = "Eve", Height = 170, Gender = Gender.Female },
            new() { Name = "Frank", Height = 178, Gender = Gender.Male },
        };
        vm.StudentCount = vm.Students.Count;
        vm.IsEmpty = false;
        vm.IsLoading = false;
        vm.StatusMessage = string.Format(Resources.Member_LoadedFmt , vm.Students.Count);

        // 追加演示数据集到现有列表（而非替换），避免覆盖用户真实数据集
        var demoDataset = new StudentDatasetInfo
        {
            Id = DemoDatasetId ,
            Name = "演示班级" ,
            StudentCount = 6 ,
            CreatedAt = DateTime.Now
        };
        // 仅在演示数据集不存在时才追加，防止重复
        if (!vm.SavedDatasets.Any(d => d.Id == DemoDatasetId))
            vm.SavedDatasets.Add(demoDataset);
        vm.CurrentDatasetId = DemoDatasetId;
        vm.CurrentDatasetName = "演示班级";
        vm.SetGuideDataset(demoDataset); // 安全选中，不触发磁盘加载

        _memberManagementDemoInjected = true;
    }

    private static void SeedVenueConfigurationData (VenueConfigurationViewModel? vm)
    {
        if (vm is null) return;
        // 使用 Background 优先级延迟注入：ViewModel 构造函数中的 LoadVenueList()
        //（fire-and-forget）会异步加载并覆盖 VenueItems。先同步执行 NewVenueCommand
        // 创建会场（该命令同步添加至现有集合），再将命名和状态消息延迟到异步 init 之后。
        vm.NewVenueCommand.Execute(null);
        Dispatcher.UIThread.Post(() =>
        {
            vm.LayoutName = "演示教室";
            vm.StatusMessage = "已创建演示会场（演示数据）";
        } , DispatcherPriority.Background);
    }

    private static void SeedStrategyConfigurationData (StrategyConfigurationViewModel? vm)
    {
        if (vm is null) return;
        // Background 延迟：ViewModel 构造函数中的 LoadAsync() 会异步填充 Strategies。
        // 注入在异步 init 完成后选中第一个策略，触发 OnSelectedStrategyChanged
        // → LoadDetailAsync → SelectedDetail 非空 → HasDetail=true → EditEnabledSwitch 可见。
        Dispatcher.UIThread.Post(() =>
        {
            if (vm.Strategies.Count == 0) return;
            vm.SelectedStrategy = vm.Strategies[0];
        } , DispatcherPriority.Background);
    }

    private static void SeedSeatingArrangementData (SeatingArrangementViewModel? vm)
    {
        if (vm is null) return;
        // 使用 Background 优先级延迟注入：ViewModel 构造函数中的 LoadInitialDataAsync()
        //（fire-and-forget）会异步加载并覆盖 VenueItems/DatasetItems。
        // Background 优先级确保在异步 init 完成后才注入演示数据。
        Dispatcher.UIThread.Post(() =>
        {
            vm.VenueItems.Clear();
            vm.VenueItems.Add(new("demo-v" , "演示教室"));
            vm.DatasetItems.Clear();
            vm.DatasetItems.Add(new StudentDatasetInfo { Id = "demo-ds" , Name = "演示班级" , StudentCount = 6 });
            vm.SelectedVenue = vm.VenueItems.FirstOrDefault();
            vm.SelectedDataset = vm.DatasetItems.FirstOrDefault();

            var names = new[] { "Alice" , "Bob" , "Charlie" , "Diana" , "Eve" , "Frank" };
            var seats = new ObservableCollection<SeatDisplayItem>();
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 3; c++)
                {
                    var idx = (r * 3) + c;
                    seats.Add(new SeatDisplayItem
                    {
                        SeatId = $"R{r}C{c}" ,
                        SeatLabel = $"R{r}C{c}" ,
                        X = 200 + (c * 80) ,
                        Y = 200 + (r * 60) ,
                        Width = 50 ,
                        Height = 30 ,
                        IsOccupied = idx < 6 ,
                        StudentName = idx < 6 ? names[idx] : null ,
                        OccupancyStatus = idx < 6 ? SeatOccupancyStatus.Occupied : SeatOccupancyStatus.Empty
                    });
                }

            vm.SeatItems = seats;
            vm.OverlayItems = new ObservableCollection<SeatDisplayItem>();
            vm.TotalSeats = 12;
            vm.AssignedSeats = 6;
            vm.HasGenerated = true;
            vm.IsGenerating = false;
            vm.StatusMessage = "已分配 6/12 个座位（演示数据）";
        } , DispatcherPriority.Background);
    }

    private static void SeedSnapshotHistoryData (SnapshotHistoryViewModel? vm)
    {
        if (vm is null) return;
        vm.Venues = new ObservableCollection<VenueItem>
        {
            new("demo-v", "演示教室")
        };

        vm.Snapshots = new ObservableCollection<SeatingSnapshot>
        {
            new()
            {
                Id = "demo-snap-1",
                CreatedAt = DateTime.Now.AddDays(-1),
                Description = "演示快照 - 第 3 周",
                LayoutId = "demo-v",
                SeatAssignments = new Dictionary<string, string> { ["R0C0"] = "student-alice" }
            }
        };
        vm.IsLoading = false;
        vm.StatusMessage = "找到 1 个快照（演示数据）";
    }

    /// <summary>清除注入到所有页面 ViewModel 的示例数据。</summary>
    private static void ClearPageData ()
    {
        if (global::Avalonia.Application.Current is not App app) return;
        var sp = app.ServiceProvider;

        if (sp.GetService(typeof(MemberManagementViewModel)) is MemberManagementViewModel memberVm)
        {
            // 仅在实际注入过演示数据时才清理；若 Phase 2 因用户已有数据而跳过注入，不触碰 VM 状态
            if (_memberManagementDemoInjected)
            {
                // ✅ 使用 _suppressDatasetLoad 阻断 SelectedDataset 变化时的副作用，
                //    防止 SavedDatasets 替换触发 OnSelectedDatasetChanged → ClearDataInternalAsync 弹窗。
                memberVm.SetSuppressDatasetLoad(true);
                try
                {
                    if (_savedMemberStudents is not null)
                    {
                        // 引导前有用户数据 → 恢复到原始状态（显式过滤残留的演示数据集）
                        memberVm.Students = new ObservableCollection<Student>(_savedMemberStudents);
                        memberVm.StudentCount = _savedMemberStudents.Count;
                        memberVm.IsEmpty = _savedMemberIsEmpty;
                        memberVm.SavedDatasets = new ObservableCollection<StudentDatasetInfo>(
                            (_savedMemberDatasets ?? []).Where(d => d.Id != DemoDatasetId));
                    }
                    else
                    {
                        // 首次使用（引导前无数据）→ 清空演示数据（显式过滤残留的演示数据集）
                        memberVm.Students.Clear();
                        memberVm.StudentCount = 0;
                        memberVm.IsEmpty = true;
                        memberVm.SavedDatasets = new ObservableCollection<StudentDatasetInfo>(
                            memberVm.SavedDatasets.Where(d => d.Id != DemoDatasetId));
                    }
                    memberVm.SelectedDataset = null;
                    memberVm.CurrentDatasetId = null;
                    memberVm.CurrentDatasetName = null;
                    memberVm.FilePath = string.Empty;
                    memberVm.ResetDirtyState(); // 重置 _originalStudentsJson，防止后续 IsDirty 误判
                }
                finally
                {
                    memberVm.SetSuppressDatasetLoad(false);
                }

                _memberManagementDemoInjected = false;
            }

            // 清除快照
            _savedMemberStudents = null;
            _savedMemberDatasets = null;
        }
        if (sp.GetService(typeof(VenueConfigurationViewModel)) is VenueConfigurationViewModel venueVm)
        {
            venueVm.VenueItems.Clear();
            venueVm.SelectedVenueItem = null;
            venueVm.PreviewSeats.Clear();
            venueVm.PreviewOverlays.Clear();
            venueVm.StatusMessage = string.Empty;
        }
        if (sp.GetService(typeof(StrategyConfigurationViewModel)) is StrategyConfigurationViewModel stratVm)
        {
            stratVm.SelectedStrategy = null;
        }
        if (sp.GetService(typeof(SeatingArrangementViewModel)) is SeatingArrangementViewModel seatVm)
        {
            seatVm.HasGenerated = false;
            seatVm.SeatItems.Clear();
            seatVm.OverlayItems.Clear();
            seatVm.TotalSeats = 0;
            seatVm.AssignedSeats = 0;
            seatVm.VenueItems.Clear();
            seatVm.DatasetItems.Clear();
            seatVm.SelectedVenue = null;
            seatVm.SelectedDataset = null;
        }
        if (sp.GetService(typeof(SnapshotHistoryViewModel)) is SnapshotHistoryViewModel snapVm)
        {
            snapVm.Snapshots.Clear();
            snapVm.Venues.Clear();
        }
    }

    private OnboardingConfig LoadConfig ()
    {
        try
        {
            var assembly = typeof(OnboardingService).Assembly;
            const string resourceName = "SeatFlow.Presentation.Avalonia.Data.onboarding_config.json";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                _logger.LogError("引导配置文件 onboarding_config.json 未找到（嵌入资源）");
                return new OnboardingConfig();
            }
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return JsonSerializer.Deserialize<OnboardingConfig>(json) ?? new OnboardingConfig();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex , "加载引导配置文件失败");
            return new OnboardingConfig();
        }
    }

    private async Task LoadCompletedPageGuidesAsync ()
    {
        try
        {
            var settings = await _facade.LoadAppSettingsAsync();
            _completedPageGuides = settings.CompletedPageGuides ?? [];
        }
        catch
        {
            _completedPageGuides = [];
        }
        finally
        {
            _completedPageGuidesLoaded = true;
        }
    }

    /// <summary>将启动引导的阶段→步骤平铺为全量列表，记录阶段边界。</summary>
    private void FlattenStartupSteps ()
    {
        _activeStepDefs.Clear();
        _activePhaseBoundaries.Clear();

        foreach (var phase in _config!.StartupPhases)
        {
            _activePhaseBoundaries.Add(_activeStepDefs.Count);
            _activeStepDefs.AddRange(phase.Steps);
        }
        _activePhaseBoundaries.Add(_activeStepDefs.Count); // 哨兵
    }

    /// <summary>构造启动引导的全量 GuideStepOption 列表。</summary>
    private List<IGuideStepOption> BuildAllStartupSteps ()
        => BuildStepsFromDefs(_activeStepDefs);

    /// <summary>从步骤定义列表构造 GuideStepOption 列表。纯机械转换。</summary>
    private static List<IGuideStepOption> BuildStepsFromDefs (List<OnboardingStepDefinition> defs)
    {
        var steps = new List<IGuideStepOption>();
        var resMgr = global::SeatFlow.Presentation.Avalonia.Lang.Resources.ResourceManager;
        var culture = global::SeatFlow.Presentation.Avalonia.Lang.Resources.Culture;

        string R (string key)
        {
            try { return resMgr.GetString(key , culture) ?? key; }
            catch { return key; }
        }

        foreach (var stepDef in defs)
        {
            var placement = Enum.TryParse<GuidePlacementMode>(stepDef.Placement , ignoreCase: true , out var p)
                ? p
                : (GuidePlacementMode?)null;

            steps.Add(new GuideStepOption
            {
                Title = R(stepDef.TitleKey) ,
                Description = R(stepDef.DescKey) ,
                Placement = placement ,
                IsShowMask = stepDef.ShowMask ,
                IsArrowVisible = stepDef.ShowArrow ,
            });
        }

        return steps;
    }

    private static Control? ResolveTarget (string name)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow is null) return null;

        var names = name.Split(';');

        foreach (var n in names)
        {
            var trimmed = n.Trim();

            // 1. MainWindow 的 NameScope（ToggleSidebarButton 等）
            var mainScope = global::Avalonia.Controls.NameScope.GetNameScope(mainWindow);
            if (mainScope is not null)
            {
                var element = mainScope.Find(trimmed);
                if (element is Control c) return c;
            }

            // 2. 通过 ContentPresenter → Child → 当前页面 View → NameScope
            var presenter = mainWindow.PageHost.GetVisualDescendants()
                .OfType<ContentPresenter>()
                .FirstOrDefault();
            if (presenter?.Child is Control pageView)
            {
                var pageScope = global::Avalonia.Controls.NameScope.GetNameScope(pageView);
                if (pageScope is not null)
                {
                    var element = pageScope.Find(trimmed);
                    if (element is Control c) return c;
                }
            }
        }

        return null;
    }

    private int GetPhaseIndex (int stepIndex)
    {
        for (int i = _activePhaseBoundaries.Count - 2; i >= 0; i--)
            if (_activePhaseBoundaries[i] <= stepIndex)
                return i;
        return 0;
    }

    /// <summary>
    /// 完成引导：清理事件订阅、恢复 UI 状态、持久化标记。
    /// 关键：IsActive 和 _currentPageGuide 的清理必须在第一个 await 之前，
    /// 否则在 I/O 挂起期间用户触发 RestartGuide 会导致竞态条件。
    /// </summary>
    private async Task CompleteOnboardingAsync ()
    {
        _logger.LogInformation("[Onboarding] 引导已全部完成");

        if (_guide is not null)
        {
            _guide.StepOpening -= OnStepOpening;
            _guide.StepOpened -= OnStepOpened;
        }

        var wasPageGuide = _currentPageGuide;

        // ✅ 在任何 await 之前清理可变状态，防止与 StartOnboarding 竞态
        IsActive = false;
        _currentPageGuide = null;

        // 清除注入的示例数据（纯内存操作，无 I/O）
        ClearPageData();

        // 持久化页面引导标记（可在后台安全执行，不影响竞态）
        if (wasPageGuide is not null)
        {
            _completedPageGuides[wasPageGuide] = true;
            try
            {
                var settings = await _facade.LoadAppSettingsAsync();
                settings.CompletedPageGuides[wasPageGuide] = true;
                await _facade.SaveAppSettingsAsync(settings);
            }
            catch { }
        }

        // 启动引导完成：恢复 UI 状态
        if (wasPageGuide is null)
        {
            var mainWindow = GetMainWindow();
            if (mainWindow?.DataContext is MainShellViewModel vm)
            {
                var navigateTo = ParsePageKey(_config?.CompleteAction) ?? PageKey.Home;
                await vm.CompleteOnboardingAsync(navigateTo);
            }
        }

        if (_guide is not null)
        {
            _guide.Close();
            _guide.IsVisible = false;
            _guide.StepsSource = null;
        }
    }

    private void OnStepOpening (object? sender , GuideStepEventArgs e)
    {
        HandleStepOpening(e.Index , e.Step);
    }

    /// <summary>步骤打开后：同步阶段指示点数量和激活位置。</summary>
    /// <remarks>
    /// Guide 内部 SyncIndicator() 在 StepOpened 之前执行，会将 Indicator.StepCount
    /// 重置为总步骤数。这里用 Background 优先级的延迟 Dispatch 覆盖回去，确保在 Guide
    /// 的所有同步操作完成后，Indicator 显示的是阶段数而非步骤数。
    /// </remarks>
    private void OnStepOpened (object? sender , GuideStepEventArgs e)
    {
        if (_guide is null || _activePhaseBoundaries.Count <= 1)
            return;

        int phaseCount = _activePhaseBoundaries.Count - 1; // 减去哨兵
        int phaseIndex = GetPhaseIndex(e.Index);
        var guide = _guide; // 捕获引用

        Dispatcher.UIThread.Post(() =>
        {
            if (guide.Indicator is null) return;
            guide.Indicator.StepCount = phaseCount;
            guide.Indicator.ActiveIndex = phaseIndex;
        } , DispatcherPriority.Background);
    }

    /// <summary>卡片缩放弹出动画：0.96 → 1.0。</summary>
    private static async Task AnimateCardBounceAsync (Guide guide , int delayMs)
    {
        // 查找模板中的卡片 Border
        var card = FindTemplateChild<Border>(guide , "PART_CardRoot");
        if (card?.RenderTransform is ScaleTransform scale)
        {
            scale.ScaleX = 0.96;
            scale.ScaleY = 0.96;
            await Task.Delay(delayMs);
            scale.ScaleX = 1.0;
            scale.ScaleY = 1.0;
        }
    }

    /// <summary>从控件模板中按名称查找子元素。</summary>
    private static T? FindTemplateChild<T> (Control control , string name) where T : class
    {
        // 遍历视觉树查找命名元素
        var children = control.GetVisualDescendants();
        foreach (var child in children)
        {
            if (child is T typed && child.Name == name)
                return typed;
        }
        return null;
    }

    private static MainWindow? GetMainWindow ()
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow as MainWindow;
        return null;
    }

    /// <summary>将配置中的页面名称解析为 PageKey 枚举值，失败返回 null。</summary>
    private static PageKey? ParsePageKey (string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        if (Enum.TryParse<PageKey>(name, ignoreCase: true, out var key))
            return key;
        return null;
    }
}
