using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SeatFlow.Application.Commands;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.DomainServices;
using SeatFlow.Core.Models;
using SeatFlow.Core.Strategies;
using SeatFlow.Core.Workspace;
using SeatFlow.Infrastructure.Serialization;
using SeatFlow.Presentation.Avalonia.Lang;
using SeatFlow.Presentation.Avalonia.Services;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

public partial class SeatingArrangementViewModel : ViewModelBase
{
    private readonly IApplicationFacade _facade;
    private readonly IFileService _fileService;
    private readonly IArrangementCounterService _counterService;
    private readonly ILogger<SeatingArrangementViewModel> _logger;

    // ── 内部状态 ──
    private SeatingWorkspace? _workspace;
    private ClassroomLayoutDefinition? _currentLayout;
    private SeatingPlan? _currentPlan;
    private SeatDisplayItem? _swapSourceSeat;
    private CancellationTokenSource? _generateCts;

    // ── 操作历史 ──
    private readonly ObservableCollection<HistoryEntry> _historyEntries = [];
    private int _currentHistoryIndex = -1;
    private int _lastSavedIndex = -1;
    public IReadOnlyList<HistoryEntry> HistoryEntries => _historyEntries;
    public bool HasUnsavedChanges => _currentHistoryIndex >= 0 && _currentHistoryIndex != _lastSavedIndex;
    public bool HasHistory => _historyEntries.Count > 0;

    // ── 左侧面板 ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedVenue))]
    public partial ObservableCollection<VenueItem> VenueItems { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedVenue))]
    [NotifyPropertyChangedFor(nameof(CanGenerate))]
    [NotifyPropertyChangedFor(nameof(CanCreateEmpty))]
    public partial VenueItem? SelectedVenue { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDataset))]
    public partial ObservableCollection<StudentDatasetInfo> DatasetItems { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDataset))]
    [NotifyPropertyChangedFor(nameof(CanGenerate))]
    [NotifyPropertyChangedFor(nameof(CanCreateEmpty))]
    public partial StudentDatasetInfo? SelectedDataset { get; set; }

    public bool HasSelectedVenue => SelectedVenue != null;
    public bool HasSelectedDataset => SelectedDataset != null;

    // ── Canvas ──
    [ObservableProperty]
    public partial ObservableCollection<SeatDisplayItem> SeatItems { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<SeatDisplayItem> OverlayItems { get; set; } = [];

    [ObservableProperty]
    public partial double CanvasWidth { get; set; } = 800;

    [ObservableProperty]
    public partial double CanvasHeight { get; set; } = 600;

    private double _contentCenterX, _contentCenterY;
    private double _defaultZoomLevel = 1.0;
    public double ZoomLevel { get; set; } = 1.0;
    public Action<double> ZoomAction => delta => ApplyZoom(delta);
    public void ApplyZoom (double delta) { ZoomLevel = Math.Clamp(ZoomLevel + delta , 0.2 , 3.0); BuildSeatDisplayItems(); }

    /// <summary>不改变数据，仅重新绘制预览区域。</summary>
    public void RefreshPreview () => BuildSeatDisplayItems();

    // ── 工具栏 ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGenerate))]
    [NotifyPropertyChangedFor(nameof(CanCreateEmpty))]
    public partial bool IsGenerating { get; set; }

    [ObservableProperty]
    public partial bool HasGenerated { get; set; }


    public bool CanGenerate => HasSelectedVenue && HasSelectedDataset && !IsGenerating;
    public bool CanCreateEmpty => HasSelectedVenue && HasSelectedDataset && !IsGenerating;
    public bool CanUndo => _currentHistoryIndex > 0;
    public bool CanRedo => _currentHistoryIndex < _historyEntries.Count - 1;

    // ── 右侧面板 ──
    [ObservableProperty]
    public partial ObservableCollection<StrategyDisplayInfo> ActiveStrategies { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<Student> UnassignedStudents { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedUnassignedStudent))]
    public partial Student? SelectedUnassignedStudent { get; set; }

    public bool HasSelectedUnassignedStudent => SelectedUnassignedStudent != null;

    [ObservableProperty]
    public partial bool IsStrategiesExpanded { get; set; } = true;

    [ObservableProperty]
    public partial bool IsUnassignedExpanded { get; set; } = true;

    [ObservableProperty]
    public partial bool IsHistoryExpanded { get; set; } = true;

    [ObservableProperty]
    public partial ObservableCollection<StrategyMessageGroup> MessageGroups { get; set; } = [];

    [ObservableProperty]
    public partial bool IsMessagesExpanded { get; set; } = true;

    public bool HasMessages => MessageGroups.Count > 0;

    // ── 状态栏 ──
    [ObservableProperty]
    public partial string StatusMessage { get; set; } = Resources.Seating_Ready;

    [ObservableProperty]
    public partial int TotalSeats { get; set; }

    [ObservableProperty]
    public partial int AssignedSeats { get; set; }

    public int UnassignedStudentCount => UnassignedStudents.Count;

    // ── 交换模式 ──
    [ObservableProperty]
    public partial bool IsSwapMode { get; set; }

    [ObservableProperty]
    public partial string SwapHintText { get; set; } = string.Empty;

    public SeatingArrangementViewModel (IApplicationFacade facade , IFileService fileService , INavigationService navigation , IArrangementCounterService counterService , ILogger<SeatingArrangementViewModel>? logger = null)
    {
        _facade = facade;
        _fileService = fileService;
        _navigation = navigation;
        _counterService = counterService;
        _logger = logger ?? NullLogger<SeatingArrangementViewModel>.Instance;
        navigation.CurrentViewModelChanged += OnNavigationChanged;
        _ = LoadInitialDataAsync();
    }

    private readonly INavigationService _navigation;

    private void OnNavigationChanged ()
    {
        if (_navigation.CurrentViewModel == this)
        {
            // 推迟到 UI 布局完成后执行，确保 OnLoaded 已触发、SeatItems 绑定已建立
            Dispatcher.UIThread.Post(() => _ = RefreshDataAsync());
        }
    }

    /// <summary>用于抑制 <see cref="OnSelectedVenueChanged"/> 覆盖已恢复的布局。</summary>
    private bool _isRestoringWorkspace;

    // ── 初始化 ──

    private async Task LoadInitialDataAsync ()
    {
        await Task.WhenAll(LoadVenuesAsync() , LoadDatasetsAsync() , LoadDefaultZoomAsync());
        StatusMessage = Resources.Seating_ReadyHint;
    }

    private async Task LoadDefaultZoomAsync ()
    {
        try
        {
            var settings = await _facade.LoadAppSettingsAsync();
            _defaultZoomLevel = settings.DefaultZoomLevel;
            ZoomLevel = _defaultZoomLevel;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "加载默认缩放失败，使用 1.0");
        }
    }

    public async Task RefreshDataAsync ()
    {
        await Task.WhenAll(LoadVenuesAsync() , LoadDatasetsAsync());
        await TryRestoreWorkspaceAsync();
    }

    [RelayCommand]
    private async Task LoadVenuesAsync ()
    {
        await SafeExecuteAsync(async () =>
        {
            var ids = (await _facade.ListVenueIdsAsync()).ToList();
            var items = new List<VenueItem>();
            foreach (var id in ids)
            {
                var layout = await _facade.LoadVenueAsync(id);
                items.Add(new VenueItem(id , layout?.Name ?? id));
            }
            VenueItems = new ObservableCollection<VenueItem>(items);
        });
    }

    [RelayCommand]
    private async Task LoadDatasetsAsync ()
    {
        await SafeExecuteAsync(async () =>
        {
            var datasets = await _facade.ListStudentDatasetsAsync();
            DatasetItems = new ObservableCollection<StudentDatasetInfo>(datasets);
        });
    }

    /// <summary>如果有活跃工作区（如快照回滚后），恢复座位图显示。</summary>
    private async Task TryRestoreWorkspaceAsync ()
    {
        _workspace = await _facade.GetCurrentWorkspaceAsync();
        if (_workspace == null) return;

        _currentLayout = await _facade.GetCurrentLayoutAsync();
        _currentPlan = _workspace.BuildSeatingPlan();

        if (_currentLayout != null)
        {
            _isRestoringWorkspace = true;
            SelectedVenue = VenueItems.FirstOrDefault(v => v.Id == _currentLayout.Id)
                         ?? VenueItems.FirstOrDefault();
            _isRestoringWorkspace = false;

            HasGenerated = true;
        }

        await UpdateRightPanelAsync();
        UpdateStats();
        InitHistory(Resources.Seating_RestoredWorkspace);
        StatusMessage = string.Format(Resources.Seating_RestoredWorkspaceFmt , AssignedSeats , TotalSeats);

        // 强制在 UI 线程上重绘，确保异步 continuation 未切到线程池时也能正确渲染
        Dispatcher.UIThread.Post(RefreshPreview);
    }

    // ── 会场选择 ──
    partial void OnSelectedVenueChanged (VenueItem? value)
    {
        if (value == null || _isRestoringWorkspace) return;
        _ = SafeExecuteAsync(async () =>
        {
            _currentLayout = await _facade.LoadVenueAsync(value.Id);
            if (_currentLayout != null)
            {
                ObstacleProcessor.ApplyObstacles(_currentLayout);
                StatusMessage = string.Format(Resources.Seating_VenueLoadedFmt , _currentLayout.Name , _currentLayout.Seats.Count);
            }
        });
    }

    // ── 生成座位 ──

    [RelayCommand]
    private async Task GenerateSeatingAsync ()
    {
        if (!CanGenerate || _currentLayout == null) return;

        _generateCts?.Cancel();
        _generateCts = new CancellationTokenSource();
        var ct = _generateCts.Token;

        IsGenerating = true;
        HasGenerated = false;
        StatusMessage = Resources.Seating_Generating;

        await SafeExecuteAsync(async () =>
        {
            // 1. 加载学生
            var students = await _facade.LoadStudentDatasetAsync(SelectedDataset!.Id , ct);
            if (students == null || students.Count == 0)
            {
                StatusMessage = Resources.Seating_NoMembers;
                return;
            }

            // 2. 写入临时 JSON 文件（RosterFile 格式）
            var roster = new RosterFile { Version = "1.0" , Students = students };
            var jsonOptions = JsonOptions.WriteIndentedCamelCase;
            var tempPath = Path.Combine(Path.GetTempPath() , $"a_pair_gen_{Guid.NewGuid():N}.json");
            await File.WriteAllTextAsync(tempPath , JsonSerializer.Serialize(roster , jsonOptions) , ct);

            try
            {
                // 3. 调用生成
                var progress = new Progress<SeatingProgress>(p =>
                {
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        StatusMessage = p.StatusMessage;
                    });
                });

                var request = new SeatingRequest
                {
                    LayoutId = SelectedVenue!.Id ,
                    DatasetId = SelectedDataset!.Id ,
                    StudentDataSource = tempPath ,
                    Description = string.Format(Resources.Seating_VenueDatasetDesc , SelectedVenue.Name , SelectedDataset.Name)
                };

                _workspace = await _facade.GenerateSeatingAsync(request , progress , ct);
                _currentPlan = _workspace.BuildSeatingPlan();

                // 4. 构建显示 + 初始化历史
                BuildSeatDisplayItems();
                await UpdateRightPanelAsync();
                UpdateStats();
                InitHistory(Resources.Seating_GenerateDesc);

                HasGenerated = true;
                _counterService.Increment();
                StatusMessage = string.Format(Resources.Seating_GeneratedFmt , AssignedSeats , TotalSeats);
            }
            finally
            {
                // 5. 清理临时文件
                try { File.Delete(tempPath); } catch { /* 忽略 */ }
            }
        } , Resources.Seating_GenerateFailed);

        IsGenerating = false;
    }

    /// <summary>
    /// 创建空白会场：加载座位布局和学生但不执行策略管道。
    /// 所有学生进入未分配列表，座位全部为空。
    /// </summary>
    [RelayCommand]
    private async Task CreateEmptySeatingAsync ()
    {
        if (!CanCreateEmpty || _currentLayout == null) return;

        _generateCts?.Cancel();
        _generateCts = new CancellationTokenSource();
        var ct = _generateCts.Token;

        IsGenerating = true;
        HasGenerated = false;
        StatusMessage = Resources.Seating_CreateEmptyHint;

        await SafeExecuteAsync(async () =>
        {
            // 加载学生
            var students = await _facade.LoadStudentDatasetAsync(SelectedDataset!.Id , ct);
            if (students == null || students.Count == 0)
            {
                StatusMessage = Resources.Seating_NoMembers;
                return;
            }

            // 通过 facade 创建空白工作区（加载布局 + 学生，不执行策略）
            _workspace = await _facade.CreateEmptyWorkspaceAsync(
                SelectedVenue!.Id , SelectedDataset!.Id , ct);
            _currentPlan = _workspace.BuildSeatingPlan();

            // 构建显示 + 初始化历史
            BuildSeatDisplayItems();
            await UpdateRightPanelAsync();
            UpdateStats();
            InitHistory(Resources.Seating_EmptyStartDesc);

            HasGenerated = true;
            StatusMessage = string.Format(Resources.Seating_EmptyCreatedFmt , TotalSeats , UnassignedStudentCount);
        } , Resources.Seating_CreateEmptyFailed);

        IsGenerating = false;
    }

    // ── Canvas 数据构建 ──

    private void BuildSeatDisplayItems ()
    {
        if (_currentLayout == null || _workspace == null || _currentPlan == null) return;

        var metadata = _currentLayout.Metadata;
        var studentMap = _workspace.Students.ToDictionary(s => s.Id , s => s.Name);
        var assignments = _currentPlan.Assignments;
        var (baseW , baseH) = GetSeatDimensions(metadata);

        // 网格布局扩距系数（增大间距防重叠）
        double spread = metadata is GridLayoutMetadata ? 1.8 : 1.0;

        // 第一遍：收集原始坐标范围
        double minX0 = double.MaxValue, minY0 = double.MaxValue;
        double maxX0 = 0, maxY0 = 0;
        var rawPositions = new List<(double cx , double cy , Seat seat)>();
        foreach (var seat in _currentLayout.Seats)
        {
            if (!seat.IsAvailable) continue;
            var (cx , cy) = SeatGeometryHelper.GetPosition(seat , metadata);
            cx *= spread; cy *= spread;
            if (seat is PolarSeat) { cx -= baseW / 2; cy -= baseH / 2; }
            rawPositions.Add((cx , cy , seat));
            minX0 = Math.Min(minX0 , cx);
            minY0 = Math.Min(minY0 , cy);
            maxX0 = Math.Max(maxX0 , cx + baseW);
            maxY0 = Math.Max(maxY0 , cy + baseH);
        }
        // 将障碍物也纳入包围盒
        foreach (var obs in _currentLayout.Obstacles)
        {
            double ow = (obs.Width > 0 ? obs.Width : 60) * spread;
            double oh = (obs.Height > 0 ? obs.Height : 40) * spread;
            double ox = obs.X * spread;
            double oy = obs.Y * spread;
            minX0 = Math.Min(minX0 , ox);
            minY0 = Math.Min(minY0 , oy);
            maxX0 = Math.Max(maxX0 , ox + ow);
            maxY0 = Math.Max(maxY0 , oy + oh);
        }

        // 始终以当前原始坐标中心为参考中心（首次或重置后都正确）
        _contentCenterX = (minX0 + maxX0) / 2;
        _contentCenterY = (minY0 + maxY0) / 2;

        // 第二遍：以中心缩放，座位尺寸固定
        double seatWidth = baseW;
        double seatHeight = baseH;

        var items = new List<SeatDisplayItem>();
        int seatCounter = 0;

        foreach (var (cx , cy , seat) in rawPositions)
        {
            var occupantId = assignments.GetValueOrDefault(seat.Id);
            bool isOccupied = occupantId != null;
            bool isFrontRow = IsFrontRowSeat(seat , metadata);

            double sx = _contentCenterX + ((cx - _contentCenterX) * ZoomLevel);
            double sy = _contentCenterY + ((cy - _contentCenterY) * ZoomLevel);

            seatCounter++;
            items.Add(new SeatDisplayItem
            {
                X = sx ,
                Y = sy ,
                Width = seatWidth ,
                Height = seatHeight ,
                SeatId = seat.Id ,
                SeatLabel = BuildSeatLabel(seat , seatCounter) ,
                IsFrontRow = isFrontRow ,
                StudentName = isOccupied ? studentMap.GetValueOrDefault(occupantId! , "") : null ,
                StudentId = occupantId ,
                IsOccupied = isOccupied ,
                IsFixed = seat.IsFixed ,
                OccupancyStatus = isOccupied
                    ? (seat.IsFixed ? SeatOccupancyStatus.Fixed : SeatOccupancyStatus.Occupied)
                    : SeatOccupancyStatus.Empty
            });
        }

        // Canvas 大小 + 居中偏移
        double margin = 120;
        CanvasWidth = Math.Max(900 , ((maxX0 - minX0) * ZoomLevel) + (margin * 2));
        CanvasHeight = Math.Max(700 , ((maxY0 - minY0) * ZoomLevel) + (margin * 2));
        double offsetX = (CanvasWidth / 2) - _contentCenterX;
        double offsetY = (CanvasHeight / 2) - _contentCenterY;
        foreach (var item in items) { item.X += offsetX; item.Y += offsetY; }
        SeatItems = new ObservableCollection<SeatDisplayItem>(items);

        // 障碍物叠加层（大小按座宽比例动态计算）
        var overlays = new List<SeatDisplayItem>();
        double podiumW = baseW * 2.5;
        double podiumH = baseH * 1.6;
        double doorW = baseW * 1.2;
        double doorH = baseH * 0.9;

        // Grid 讲台 X 坐标：水平居中于座位范围
        double seatMinX = double.MaxValue, seatMaxX = 0;
        foreach (var seat in _currentLayout.Seats)
        {
            if (!seat.IsAvailable) continue;
            var (cx , _) = SeatGeometryHelper.GetPosition(seat , metadata);
            cx *= spread;
            seatMinX = Math.Min(seatMinX , cx);
            seatMaxX = Math.Max(seatMaxX , cx + baseW);
        }

        foreach (var obs in _currentLayout.Obstacles)
        {
            double w = obs.Width > 0 ? obs.Width : (obs.Type == "Podium" ? podiumW : doorW);
            double h = obs.Height > 0 ? obs.Height : (obs.Type == "Podium" ? podiumH : doorH);
            w *= spread; h *= spread;

            double obsX = obs.X * spread;
            double obsY = obs.Y * spread;
            // Grid 讲台强制居中（seatMin/Max 已含 spread）
            if (metadata is GridLayoutMetadata && obs.Type == "Podium" && seatMinX < seatMaxX)
                obsX = ((seatMinX + seatMaxX) / 2) - (w / 2);

            double ox = _contentCenterX + ((obsX - _contentCenterX) * ZoomLevel) + offsetX;
            double oy = _contentCenterY + ((obsY - _contentCenterY) * ZoomLevel) + offsetY;
            overlays.Add(new SeatDisplayItem
            {
                X = ox ,
                Y = oy ,
                Width = w ,
                Height = h ,
                SeatId = obs.Id ,
                SeatLabel = obs.Type ?? Resources.Seating_Obstacle ,
                CornerRadius = obs.Type == "Podium" ? new(w / 2) : new(4) ,
                OccupancyStatus = SeatOccupancyStatus.Empty
            });
        }
        OverlayItems = new ObservableCollection<SeatDisplayItem>(overlays);
    }

    private static (double width , double height) GetSeatDimensions (LayoutMetadata metadata)
        => ComputeSeatSize(metadata);

    /// <summary>按间距计算座位的安全尺寸（宽<最近邻间距的70%）。</summary>
    private static (double w , double h) ComputeSeatSize (LayoutMetadata metadata)
    {
        if (metadata is GridLayoutMetadata gm)
        {
            double intra = gm.IntraDeskSpacing > 0 ? gm.IntraDeskSpacing : 20;
            double inter = gm.InterDeskSpacing > 0 ? gm.InterDeskSpacing : 64;
            double colGap = gm.SeatsPerDesk > 1 ? Math.Min(intra , inter) : inter;
            double rowGap = gm.VerticalSpacing > 0 ? gm.VerticalSpacing : 56;
            double w = Math.Clamp(colGap * 0.95 , 44 , 72);
            double h = Math.Clamp(rowGap * 0.62 , 24 , 44);
            return (w , h);
        }
        if (metadata is PolarLayoutMetadata pm)
        {
            double step = pm.RadiusStep > 0 ? pm.RadiusStep : 40;
            double s = Math.Clamp(step * 0.85 , 28 , 48);
            return (s , s);
        }
        return (42 , 26);
    }

    // ── 座位标签与行列判断 ──

    private static string BuildSeatLabel (Seat seat , int counter)
    {
        return seat switch
        {
            GridSeat g => $"R{g.Row}C{g.Column}",
            PolarSeat p => string.Format(Resources.Seating_PolarLabelFmt , p.Ring , p.AngleDegrees),
            FreeformSeat => $"#{counter}",
            _ => $"#{counter}"
        };
    }

    private static bool IsFrontRowSeat (Seat seat , LayoutMetadata metadata)
    {
        return (seat , metadata) switch
        {
            (GridSeat g, GridLayoutMetadata gm) => g.Row <= gm.FrontRowCount,
            (PolarSeat p, PolarLayoutMetadata pm) => IsPolarFrontRow(p , pm),
            _ => false
        };
    }

    private static bool IsPolarFrontRow (PolarSeat seat , PolarLayoutMetadata meta)
    {
        int totalRings = meta.RingSeatCounts.Count > 0 ? meta.RingSeatCounts.Count : meta.Rings;
        int frontCount = Math.Min(meta.FrontRowCount , totalRings);
        return seat.Ring > (totalRings - frontCount);
    }

    // ── 显示刷新 ──

    private void RefreshSeatAssignments ()
    {
        if (_currentPlan == null || _workspace == null) return;
        var studentMap = _workspace.Students.ToDictionary(s => s.Id , s => s.Name);
        var assignments = _currentPlan.Assignments;

        foreach (var item in SeatItems)
        {
            var occupantId = assignments.GetValueOrDefault(item.SeatId);
            bool isOccupied = occupantId != null;
            item.StudentName = isOccupied ? studentMap.GetValueOrDefault(occupantId! , "") : null;
            item.StudentId = occupantId;
            item.IsOccupied = isOccupied;
            item.OccupancyStatus = isOccupied
                ? (item.IsFixed ? SeatOccupancyStatus.Fixed : SeatOccupancyStatus.Occupied)
                : SeatOccupancyStatus.Empty;
            item.IsSelectedForSwap = false;
        }
    }

    private async Task UpdateRightPanelAsync ()
    {
        // 策略列表
        var allStrategies = await _facade.GetStrategiesAsync();

        // 分离独立策略和依赖策略
        var enabledVisible = allStrategies
            .Where(s => s.IsEnabled && s.Visible)
            .OrderByDescending(s => s.Priority)
            .ToList();
        var independents = enabledVisible.Where(s => s.IsIndependent).ToList();
        var dependents = enabledVisible.Where(s => !s.IsIndependent).ToList();

        // 将依赖策略注入到 RandomFill 的 DependentChildren
        var randomFill = independents.FirstOrDefault(s => s.Id == RandomFillStrategy.StrategyId);
        if (randomFill != null && dependents.Count > 0)
        {
            randomFill.DependentChildren = [.. dependents.OrderByDescending(d => d.Priority)];
        }

        ActiveStrategies = new ObservableCollection<StrategyDisplayInfo>(independents);

        // 未分配学生
        if (_workspace != null && _currentPlan != null)
        {
            var assignedIds = new HashSet<string>(_currentPlan.Assignments.Values.Where(v => v != null)!);
            UnassignedStudents = new ObservableCollection<Student>(
                _workspace.Students.Where(s => !assignedIds.Contains(s.Id)));
            OnPropertyChanged(nameof(UnassignedStudentCount));
        }

        // 策略消息（按 Warning / Error 分组）
        if (_workspace != null)
        {
            // 构建消息模板字典：从各策略的 manifest messages 中解析当前语言模板
            var templates = new Dictionary<string , string>();
            foreach (var s in allStrategies)
            {
                if (s.Messages is null) continue;
                foreach (var (key , dict) in s.Messages)
                    templates[key] = Helpers.LocalizeHelper.Resolve(dict);
            }

            var messages = _workspace.Messages;
            var studentNames = _workspace.Students.ToDictionary(s => s.Id , s => s.Name);
            var errors = messages.Where(m => m.Severity == StrategyMessageSeverity.Error).ToList();
            var warnings = messages.Where(m => m.Severity == StrategyMessageSeverity.Warning).ToList();

            var groups = new ObservableCollection<StrategyMessageGroup>();
            if (errors.Count > 0)
                groups.Add(new StrategyMessageGroup
                {
                    Severity = StrategyMessageSeverity.Error ,
                    Title = $"{Resources.Seating_MessagesGroupError} ({errors.Count})" ,
                    Messages = new ObservableCollection<StrategyMessageItem>(
                        errors.Select(m => new StrategyMessageItem
                        {
                            StrategyName = m.StrategyDisplayName ,
                            Message = FormatStrategyMessage(m , studentNames , templates) ,
                            Severity = m.Severity
                        }))
                });
            if (warnings.Count > 0)
                groups.Add(new StrategyMessageGroup
                {
                    Severity = StrategyMessageSeverity.Warning ,
                    Title = $"{Resources.Seating_MessagesGroupWarning} ({warnings.Count})" ,
                    Messages = new ObservableCollection<StrategyMessageItem>(
                        warnings.Select(m => new StrategyMessageItem
                        {
                            StrategyName = m.StrategyDisplayName ,
                            Message = FormatStrategyMessage(m , studentNames , templates) ,
                            Severity = m.Severity
                        }))
                });

            MessageGroups = groups;
        }
        else
        {
            MessageGroups = [];
        }
        OnPropertyChanged(nameof(HasMessages));
    }

    private void UpdateStats ()
    {
        TotalSeats = SeatItems.Count;
        AssignedSeats = SeatItems.Count(s => s.IsOccupied);
    }

    /// <summary>手动操作后统一刷新显示、更新历史、设置状态消息。</summary>
    private async Task FinalizeManualOperationAsync (string historyDesc , string statusMsg)
    {
        _currentPlan = _workspace!.BuildSeatingPlan();
        RefreshSeatAssignments();
        await UpdateRightPanelAsync();
        UpdateStats();
        AddHistoryEntry(historyDesc);
        StatusMessage = statusMsg;
    }

    // ── 座位点击交换 ──

    [RelayCommand]
    private async Task ClickSeatAsync (SeatDisplayItem? clickedSeat)
    {
        if (clickedSeat == null || _workspace == null) return;

        // ── 从右侧未分配列表点击放置 ──
        if (SelectedUnassignedStudent != null && !clickedSeat.IsOccupied && !clickedSeat.IsFixed)
        {
            var student = SelectedUnassignedStudent;
            var studentName = student.Name;
            var seatLabel = clickedSeat.SeatLabel;

            await SafeExecuteAsync(async () =>
            {
                var assignCmd = new AssignSeatCommand(clickedSeat.SeatId , student.Id);
                var ok = await _facade.ExecuteCommandAsync(assignCmd , recordInHistory: false);
                if (ok)
                {
                    await FinalizeManualOperationAsync(
                        string.Format(Resources.Seating_PlacedFmt , studentName , seatLabel) ,
                        string.Format(Resources.Seating_PlacedFmt , studentName , seatLabel));
                    SelectedUnassignedStudent = null;
                }
            } , Resources.Seating_PlaceTitle);
            return;
        }

        // 首次点击：选择源座位
        if (_swapSourceSeat == null)
        {
            if (!clickedSeat.IsOccupied && !clickedSeat.IsFixed) return;

            _swapSourceSeat = clickedSeat;
            clickedSeat.IsSelectedForSwap = true;
            IsSwapMode = true;
            SwapHintText = string.Format(Resources.Seating_SelectTargetFmt , clickedSeat.StudentName ?? clickedSeat.SeatLabel);
            return;
        }

        var source = _swapSourceSeat;

        // 点击同一座位 = 取消选择
        if (source.SeatId == clickedSeat.SeatId)
        {
            CancelSwap();
            return;
        }

        // 执行交换
        await SafeExecuteAsync(async () =>
        {
            var swapCmd = new SwapSeatCommand(
                (source.SeatId , source.StudentId) ,
                (clickedSeat.SeatId , clickedSeat.IsOccupied ? clickedSeat.StudentId : null));

            var ok = await _facade.ExecuteCommandAsync(swapCmd , recordInHistory: false);
            if (ok)
            {
                await FinalizeManualOperationAsync(
                    string.Format(Resources.Seating_SwapDescFmt , source.StudentName ?? source.SeatLabel , clickedSeat.StudentName ?? Resources.Common_Cancel) ,
                    string.Format(Resources.Seating_SwappedFmt , source.StudentName , clickedSeat.StudentName ?? Resources.Common_Cancel));
                CancelSwap();
            }
        } , Resources.Seating_SwapFailed);
    }

    [RelayCommand]
    private void CancelSwap ()
    {
        _swapSourceSeat?.IsSelectedForSwap = false;
        _swapSourceSeat = null;
        IsSwapMode = false;
        SwapHintText = string.Empty;
    }

    /// <summary>
    /// 将交换模式下选中的源座位学生移除到未分配列表。
    /// </summary>
    [RelayCommand]
    private async Task RemoveToTrashAsync ()
    {
        if (_workspace == null || _swapSourceSeat == null) return;
        if (!_swapSourceSeat.IsOccupied || _swapSourceSeat.IsFixed) return;

        var studentName = _swapSourceSeat.StudentName ?? "";
        var seatLabel = _swapSourceSeat.SeatLabel;

        await SafeExecuteAsync(async () =>
        {
            var removeCmd = new RemoveStudentCommand(_swapSourceSeat.SeatId);
            var ok = await _facade.ExecuteCommandAsync(removeCmd , recordInHistory: false);
            if (ok)
            {
                await FinalizeManualOperationAsync(
                    string.Format(Resources.Seating_RemovedFmt , studentName , seatLabel) ,
                    string.Format(Resources.Seating_RemovedFmt , studentName , seatLabel));
                CancelSwap();
            }
        } , Resources.Seating_RemoveTitle);
    }

    // ── 拖放操作（由 code-behind 调用） ──

    /// <summary>
    /// 获取学生的显示名称（从工作区中查找）。
    /// </summary>
    internal string GetStudentName (string studentId)
        => _workspace?.Students.FirstOrDefault(s => s.Id == studentId)?.Name ?? studentId;

    /// <summary>
    /// 执行拖放放置操作，支持四种情况：
    /// 1. 从未分配列表 → 空座位：AssignSeatCommand
    /// 2. 从座位 → 空座位：SwapSeatCommand（移动）
    /// 3. 从座位 → 已占座位：SwapSeatCommand（交换）
    /// 4. 从未分配列表 → 已占座位：不允许
    /// </summary>
    internal async Task<bool> ExecuteDropAsync (
        string studentId , string? sourceSeatId , string targetSeatId ,
        CancellationToken ct = default)
    {
        if (_workspace == null) return false;

        // 拖到自身 = 无操作
        if (sourceSeatId == targetSeatId) return false;

        var targetItem = SeatItems.FirstOrDefault(s => s.SeatId == targetSeatId);
        if (targetItem == null || targetItem.IsFixed) return false;

        var studentName = GetStudentName(studentId);
        IUndoableCommand cmd;

        if (sourceSeatId == null && !targetItem.IsOccupied)
        {
            // 从未分配列表 → 空座位
            cmd = new AssignSeatCommand(targetSeatId , studentId);
        }
        else if (sourceSeatId != null && !targetItem.IsOccupied)
        {
            // 从座位 → 空座位（移动）
            cmd = new SwapSeatCommand(
                (sourceSeatId , studentId) ,
                (targetSeatId , null));
        }
        else if (sourceSeatId != null && targetItem.IsOccupied)
        {
            // 从座位 → 已占座位（交换）
            cmd = new SwapSeatCommand(
                (sourceSeatId , studentId) ,
                (targetSeatId , targetItem.StudentId));
        }
        else
        {
            // 从未分配列表 → 已占座位：不允许
            return false;
        }

        var ok = await _facade.ExecuteCommandAsync(cmd , ct , recordInHistory: false);
        if (ok)
        {
            await FinalizeManualOperationAsync(
                string.Format(Resources.Seating_PlacedFmt , studentName , targetItem.SeatLabel) ,
                string.Format(Resources.Seating_PlacedFmt , studentName , targetItem.SeatLabel));
            SelectedUnassignedStudent = null;
        }
        return ok;
    }

    /// <summary>
    /// 执行拖放到垃圾桶操作：将座位上的学生移除到未分配列表。
    /// </summary>
    internal async Task<bool> ExecuteRemoveToTrashAsync (string seatId , CancellationToken ct = default)
    {
        if (_workspace == null) return false;

        var item = SeatItems.FirstOrDefault(s => s.SeatId == seatId);
        if (item == null || !item.IsOccupied || item.IsFixed) return false;

        var studentName = item.StudentName ?? "";
        var seatLabel = item.SeatLabel;

        var cmd = new RemoveStudentCommand(seatId);
        var ok = await _facade.ExecuteCommandAsync(cmd , ct , recordInHistory: false);
        if (ok)
        {
            await FinalizeManualOperationAsync(
                string.Format(Resources.Seating_RemovedFmt , studentName , seatLabel) ,
                string.Format(Resources.Seating_RemovedFmt , studentName , seatLabel));
        }
        return ok;
    }

    // ── 撤销/重做（基于历史列表） ──

    [RelayCommand]
    private void Undo ()
    {
        if (_workspace == null || _currentHistoryIndex <= 0) return;
        RestoreToHistoryIndex(_currentHistoryIndex - 1);
    }

    [RelayCommand]
    private void Redo ()
    {
        if (_workspace == null || _currentHistoryIndex >= _historyEntries.Count - 1) return;
        RestoreToHistoryIndex(_currentHistoryIndex + 1);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedHistory))]
    public partial HistoryEntry? SelectedHistory { get; set; }

    public bool HasSelectedHistory => SelectedHistory != null;

    [RelayCommand]
    private void RestoreToSelected ()
    {
        if (SelectedHistory == null || _workspace == null) return;
        var idx = _historyEntries.IndexOf(SelectedHistory);
        if (idx < 0) return;
        RestoreToHistoryIndex(idx);
        StatusMessage = string.Format(Resources.Seating_RestoredToFmt , SelectedHistory.Description);
    }

    // ── 历史管理 ──

    private void InitHistory (string description)
    {
        _historyEntries.Clear();
        var snapshot = CaptureSnapshot();
        _historyEntries.Add(new HistoryEntry(description , snapshot));
        _currentHistoryIndex = 0;
        _lastSavedIndex = -1; // 快照需用户手动保存，初始状态标记为"未保存"
        UpdateHistoryState();
    }

    private void AddHistoryEntry (string description)
    {
        // 删除当前位置之后的所有条目（新分支）
        while (_historyEntries.Count > _currentHistoryIndex + 1)
            _historyEntries.RemoveAt(_historyEntries.Count - 1);

        var snapshot = CaptureSnapshot();
        _historyEntries.Add(new HistoryEntry(description , snapshot));
        _currentHistoryIndex = _historyEntries.Count - 1;
        UpdateHistoryState();
    }

    private Dictionary<string , string> CaptureSnapshot ()
        => _currentPlan != null ? new Dictionary<string , string>(_currentPlan.Assignments) : [];

    private void RestoreToHistoryIndex (int index)
    {
        if (_workspace == null || index < 0 || index >= _historyEntries.Count) return;

        _workspace.ApplySnapshotAssignments(_historyEntries[index].Assignments);
        _currentPlan = _workspace.BuildSeatingPlan();
        _currentHistoryIndex = index;
        UpdateHistoryState();

        _ = UpdateRightPanelAsync();
        RefreshSeatAssignments();
        UpdateStats();
        StatusMessage = string.Format(Resources.Seating_RestoredToFmt , _historyEntries[index].Description);
    }

    private void UpdateHistoryState ()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(HasHistory));
        // 更新各条目的 IsCurrent 标记
        for (int i = 0; i < _historyEntries.Count; i++)
            _historyEntries[i].IsCurrent = i == _currentHistoryIndex;
    }

    // ── 保存到快照 ──

    [RelayCommand]
    private async Task SaveToSnapshotAsync ()
    {
        if (!HasUnsavedChanges) return;

        await SafeExecuteAsync(async () =>
        {
            var snapshot = await _facade.CreateSnapshotAsync(string.Format(Resources.Seating_ManualSnapshotFmt , DateTime.Now.ToString("yyyy-MM-dd HH:mm")));
            if (snapshot != null)
            {
                _lastSavedIndex = _currentHistoryIndex;
                OnPropertyChanged(nameof(HasUnsavedChanges));
                StatusMessage = Resources.Seating_SnapshotSaved;
            }
        } , Resources.Seating_SnapshotFailed);
    }

    // ── 页面离开拦截 ──

    public override async Task<bool> CanLeaveAsync ()
    {
        if (!HasUnsavedChanges)
        {
            _facade.ClearWorkspace();
            _ = _counterService.ReportAndResetAsync();
            return true;
        }

        var result = await Dialog.ShowMultiOptionAsync(Resources.Seating_UnsavedChanges ,
            Resources.Seating_UnsavedChangesMsg ,
            Resources.Seating_SaveAndLeave , Resources.Seating_DiscardAndLeave , "取消");

        switch (result)
        {
            case 0: // 保存
                await SaveToSnapshotAsync();
                break;
            case 1: // 不保存
                break;
            default: // 取消
                return false;
        }

        _facade.ClearWorkspace();
        _ = _counterService.ReportAndResetAsync();
        return true;
    }

    // ── 折叠切换 ──

    [RelayCommand]
    private void ToggleStrategies () => IsStrategiesExpanded = !IsStrategiesExpanded;

    [RelayCommand]
    private void ToggleUnassigned () => IsUnassignedExpanded = !IsUnassignedExpanded;

    [RelayCommand]
    private void ToggleHistory () => IsHistoryExpanded = !IsHistoryExpanded;

    [RelayCommand]
    private void ToggleMessages () => IsMessagesExpanded = !IsMessagesExpanded;

    // ── 导出 ──

    private int _dialogLock;
    private static readonly TimeSpan ExportTimeout = TimeSpan.FromSeconds(30);

    [RelayCommand]
    private async Task ExportExcelAsync () => await ExportAsync(ExportFormat.Excel ,
        [new FilePickerFileType(Resources.Data_ExcelFile) { Patterns = ["*.xlsx"] }] , Resources.Seating_ExcelDefault);

    [RelayCommand]
    private async Task ExportCsvAsync () => await ExportAsync(ExportFormat.Csv ,
        [new FilePickerFileType(Resources.Data_CSVFile) { Patterns = ["*.csv"] }] , Resources.Seating_CsvDefault);

    [RelayCommand]
    private async Task ExportPdfAsync () => await ExportAsync(ExportFormat.Pdf ,
        [new FilePickerFileType(Resources.Seating_PDFFile) { Patterns = ["*.pdf"] }] , Resources.Seating_PDFDefault);

    [RelayCommand]
    private async Task ExportImageAsync () => await ExportAsync(ExportFormat.Png ,
        [new FilePickerFileType(Resources.Seating_PNGFile) { Patterns = ["*.png"] }] , Resources.Seating_PNGDefault);

    // ── 教师视角导出 ──

    [RelayCommand]
    private async Task ExportTeacherExcelAsync () => await ExportAsync(ExportFormat.Excel ,
        [new FilePickerFileType(Resources.Data_ExcelFile) { Patterns = ["*.xlsx"] }] , Resources.Seating_ExcelDefault , LayoutPerspective.TeacherView);

    [RelayCommand]
    private async Task ExportTeacherCsvAsync () => await ExportAsync(ExportFormat.Csv ,
        [new FilePickerFileType(Resources.Data_CSVFile) { Patterns = ["*.csv"] }] , Resources.Seating_CsvDefault , LayoutPerspective.TeacherView);

    [RelayCommand]
    private async Task ExportTeacherPdfAsync () => await ExportAsync(ExportFormat.Pdf ,
        [new FilePickerFileType(Resources.Seating_PDFFile) { Patterns = ["*.pdf"] }] , Resources.Seating_PDFDefault , LayoutPerspective.TeacherView);

    [RelayCommand]
    private async Task ExportTeacherImageAsync () => await ExportAsync(ExportFormat.Png ,
        [new FilePickerFileType(Resources.Seating_PNGFile) { Patterns = ["*.png"] }] , Resources.Seating_PNGDefault , LayoutPerspective.TeacherView);

    private async Task ExportAsync (ExportFormat format , IReadOnlyList<FilePickerFileType> types , string suggestedName , LayoutPerspective perspective = LayoutPerspective.StudentView)
    {
        if (Interlocked.CompareExchange(ref _dialogLock , 1 , 0) != 0) return;
        try
        {
            if (_workspace == null) return;

            if (_currentLayout?.LayoutType == LayoutType.Freeform)
            {
                await Dialog.ShowWarningAsync(Resources.Seating_UnsupportedExport ,
                    Resources.Seating_UnsupportedExportMsg);
                return;
            }

            var baseName = Path.GetFileNameWithoutExtension(suggestedName);
            var ext = Path.GetExtension(suggestedName);
            var perspectiveLabel = perspective == LayoutPerspective.TeacherView
                ? Resources.Seating_TeacherView
                : Resources.Seating_StudentView;
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
            var fullSuggestedName = $"{baseName}_{perspectiveLabel}_{timestamp}{ext}";

            var file = await _fileService.SaveFileAsync(Resources.Seating_ExportTitle , types , fullSuggestedName);
            if (file == null) return;

            var filePath = file.Path.LocalPath;
            var ok = await SafeExecuteAsync(async (ct) =>
            {
                var options = new ExportOptions { Format = format , IncludeMetadata = true , Perspective = perspective };
                await _facade.ExportSeatingPlanAsync(_workspace , _currentLayout , filePath , options , ct);
                StatusMessage = string.Format(Resources.Seating_ExportedFmt , file.Name);
            } , ExportTimeout , Resources.Seating_ExportTitle);

            if (!ok)
            {
                try { File.Delete(filePath); } catch { /* ignore */ }
                StatusMessage = Resources.Seating_ExportTimeout;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex , "导出文件对话框取消或异常");
        }
        finally
        {
            await Task.Delay(150);
            Interlocked.Exchange(ref _dialogLock , 0);
        }
    }

    /// <summary>
    /// 将 StrategyMessage 的 MessageKey + Args 格式化为可读消息。
    /// Args 中的学生 ID 会被解析为姓名。
    /// </summary>
    private static string FormatStrategyMessage (StrategyMessage m ,
        Dictionary<string , string> studentNames ,
        Dictionary<string , string> templates)
    {
        var template = templates.TryGetValue(m.MessageKey , out var t) ? t : m.MessageKey;
        var resolved = m.Args.Select(a =>
        {
            if (a is not string s) return a;
            // 拆分逗号分隔的 ID 列表，逐个解析为姓名
            return string.Join(", " ,
                s.Split(',').Select(part =>
                    studentNames.TryGetValue(part.Trim() , out var n) ? n : part.Trim()));
        }).ToArray();
        try { return string.Format(template , resolved); }
        catch { return template; }
    }
}

public partial class HistoryEntry (string description , Dictionary<string , string> assignments) : ObservableObject
{
    public string Description { get; set; } = description;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public Dictionary<string , string> Assignments { get; set; } = assignments;
    public bool IsCurrent { get; set; }
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

public class StrategyMessageGroup
{
    public StrategyMessageSeverity Severity { get; init; }
    public string Title { get; init; } = "";
    public ObservableCollection<StrategyMessageItem> Messages { get; init; } = [];
}

public class StrategyMessageItem
{
    public string StrategyName { get; init; } = "";
    public string Message { get; init; } = "";
    public StrategyMessageSeverity Severity { get; init; }
}
