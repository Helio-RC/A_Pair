using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.DomainServices;
using SeatFlow.Core.Models;
using SeatFlow.Infrastructure.Layouts;
using SeatFlow.Presentation.Avalonia.Lang;
using SeatFlow.Presentation.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

public partial class VenueConfigurationViewModel : ViewModelBase
{
    private readonly IApplicationFacade _facade;
    private readonly INavigationService _navigation;
    private readonly ILogger<VenueConfigurationViewModel> _logger;

    public string Title { get; } = Resources.Venue_Title;

    [ObservableProperty]
    public partial ObservableCollection<VenueItem> VenueItems { get; set; } = [];

    private bool _suppressAutoLoad;
    private bool _suppressPreviewRegen;
    private CancellationTokenSource? _selectVenueCts;
    private bool _isDirty;

    /// <summary>已加载会场的座位位置→ID 映射，用于保存时保留旧 ID 避免快照失效。</summary>
    private Dictionary<(int Row , int Col) , string>? _existingGridSeatMap;

    /// <summary>Polar 会场的 (环号, 角度) → ID 映射。</summary>
    private Dictionary<(int Ring , double Angle) , string>? _existingPolarSeatMap;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedVenue))]
    [NotifyPropertyChangedFor(nameof(SelectedVenueId))]
    public partial VenueItem? SelectedVenueItem { get; set; }

    public string? SelectedVenueId => SelectedVenueItem?.Id;
    public bool HasSelectedVenue => SelectedVenueItem != null;

    [ObservableProperty]
    public partial string LayoutName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGridSelected))]
    [NotifyPropertyChangedFor(nameof(IsPolarSelected))]
    [NotifyPropertyChangedFor(nameof(IsFreeformSelected))]
    public partial LayoutType SelectedLayoutType { get; set; } = LayoutType.Grid;

    public bool IsGridSelected => SelectedLayoutType == LayoutType.Grid;
    public bool IsPolarSelected => SelectedLayoutType == LayoutType.Polar;
    public bool IsFreeformSelected => SelectedLayoutType == LayoutType.Freeform;
    public bool IsDoorPanelVisible => IsGridSelected || IsPolarSelected || IsFreeformSelected;

    [ObservableProperty]
    public partial bool IsFreeformVenue { get; set; }

    public bool CanChangeLayoutType => !IsFreeformVenue;

    partial void OnIsFreeformVenueChanged (bool value)
    {
        OnPropertyChanged(nameof(CanChangeLayoutType));
    }

    private List<FreeformSeat> _freeformPreviewSeats = [];
    private List<Obstacle> _freeformPreviewObstacles = [];

    // ── 侧边栏折叠 ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarCollapsed))]
    public partial bool IsSidebarExpanded { get; set; } = true;

    public bool IsSidebarCollapsed => !IsSidebarExpanded;

    [ObservableProperty]
    public partial double SidebarListWidth { get; set; } = 240;

    private bool _userWantsSidebarExpanded = true;

    public void OnWindowWidthChanged (double windowWidth)
    {
        if (windowWidth < 750)
            IsSidebarExpanded = false;
        else
            IsSidebarExpanded = _userWantsSidebarExpanded;
    }

    partial void OnIsSidebarExpandedChanged (bool value)
        => SidebarListWidth = value ? 240 : 78;

    [RelayCommand]
    private void ToggleSidebar ()
    {
        _userWantsSidebarExpanded = !_userWantsSidebarExpanded;
        IsSidebarExpanded = _userWantsSidebarExpanded;
    }

    // ── Grid 基础参数 ──
    [ObservableProperty]
    public partial int GridRows { get; set; } = 5;

    [ObservableProperty]
    public partial int GridColumns { get; set; } = 8;

    [ObservableProperty]
    public partial double GridHorizontalSpacing { get; set; } = 40;

    [ObservableProperty]
    public partial double GridVerticalSpacing { get; set; } = 36;

    [ObservableProperty]
    public partial double GridOriginX { get; set; } = 200;

    [ObservableProperty]
    public partial double GridOriginY { get; set; } = 200;

    // ── Grid 桌面配置 ──
    [ObservableProperty]
    public partial int GridSeatsPerDesk { get; set; } = 2;

    [ObservableProperty]
    public partial double GridIntraDeskSpacing { get; set; } = 12;

    [ObservableProperty]
    public partial double GridInterDeskSpacing { get; set; } = 40;

    // ── Grid 过道配置 ──
    [ObservableProperty]
    public partial string GridAisleAfterColumns { get; set; } = "";

    [ObservableProperty]
    public partial string GridAisleAfterRows { get; set; } = "";

    [ObservableProperty]
    public partial double GridAisleWidth { get; set; } = 60;

    [ObservableProperty]
    public partial ObservableCollection<AisleOption> AisleColumnOptions { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<AisleOption> AisleRowOptions { get; set; } = [];

    // ── Grid 教室特征 ──
    [ObservableProperty]
    public partial int GridFrontRowCount { get; set; } = 1;

    [ObservableProperty]
    public partial bool GridHasPodium { get; set; } = true;

    [ObservableProperty]
    public partial bool GridHasFrontDoor { get; set; }

    [ObservableProperty]
    public partial double GridPodiumWidth { get; set; } = 60;

    [ObservableProperty]
    public partial double GridPodiumHeight { get; set; } = 40;

    // ── Grid 每列行数 & 禁用座位 ──
    [ObservableProperty]
    public partial string GridColumnRowCountsSpec { get; set; } = "";

    [ObservableProperty]
    public partial string GridEmptyPositionsSpec { get; set; } = "";

    // ── 门配置（支持多门、自定义位置）──
    [ObservableProperty]
    public partial ObservableCollection<DoorItem> DoorItems { get; set; } = [];

    // ── Polar 参数 ──
    [ObservableProperty]
    public partial int PolarRings { get; set; } = 3;

    [ObservableProperty]
    public partial int PolarSeatsPerRing { get; set; } = 12;

    [ObservableProperty]
    public partial double PolarRadiusStep { get; set; } = 40;

    [ObservableProperty]
    public partial double PolarStartAngle { get; set; } = 0;

    [ObservableProperty]
    public partial double PolarEndAngle { get; set; } = 360;

    [ObservableProperty]
    public partial double PolarOriginX { get; set; } = 200;

    [ObservableProperty]
    public partial double PolarOriginY { get; set; } = 200;

    [ObservableProperty]
    public partial string PolarRingSeatCountsSpec { get; set; } = "";

    [ObservableProperty]
    public partial string PolarEmptyPositionsSpec { get; set; } = "";

    [ObservableProperty]
    public partial bool PolarHasPodium { get; set; } = true;

    [ObservableProperty]
    public partial double PolarPodiumRadius { get; set; } = 30;

    [ObservableProperty]
    public partial string PolarAisleRadialAngles { get; set; } = "";

    [ObservableProperty]
    public partial double PolarAisleRadialWidth { get; set; } = 5;

    [ObservableProperty]
    public partial string PolarAisleCircularRings { get; set; } = "";

    [ObservableProperty]
    public partial double PolarAisleCircularWidth { get; set; } = 20;

    [ObservableProperty]
    public partial int PolarFrontRowCount { get; set; } = 1;

    // ── 预览 ──
    [ObservableProperty]
    public partial ObservableCollection<SeatPreview> PreviewSeats { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<SeatPreview> PreviewOverlays { get; set; } = [];

    [ObservableProperty]
    public partial double CanvasWidth { get; set; } = 600;

    [ObservableProperty]
    public partial double CanvasHeight { get; set; } = 600;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    public VenueConfigurationViewModel (IApplicationFacade facade , INavigationService navigation , ILogger<VenueConfigurationViewModel>? logger = null)
    {
        _facade = facade;
        _navigation = navigation;
        _logger = logger ?? NullLogger<VenueConfigurationViewModel>.Instance;
        _ = LoadVenueList();
        RegenerateAisleOptions();
        SubscribeToDoorCollection(DoorItems);
    }

    // ═══════════════════════════════════════════════
    // 会场列表
    // ═══════════════════════════════════════════════

    [RelayCommand]
    private async Task LoadVenueList ()
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
            _logger?.LogInformation("已加载 {Count} 个会场", items.Count);
            StatusMessage = string.Format(Resources.Venue_VenuesLoadedFmt , items.Count);
        });
    }

    [RelayCommand]
    private void NewVenue ()
    {
        _selectVenueCts?.Cancel();
        _suppressAutoLoad = true;
        var id = Guid.NewGuid().ToString("N")[..8];
        var item = new VenueItem(id , string.Format(Resources.Venue_NewVenueFmt , id));
        LayoutName = item.Name;
        IsFreeformVenue = false;
        SelectedLayoutType = LayoutType.Grid;
        _existingGridSeatMap = null;
        _existingPolarSeatMap = null;
        _suppressPreviewRegen = true;
        ResetParameters();
        _suppressPreviewRegen = false;
        _isDirty = false;
        VenueItems.Add(item);
        SelectedVenueItem = item;
        RegeneratePreview();
        StatusMessage = Resources.Venue_New;
        _suppressAutoLoad = false;
    }

    [RelayCommand]
    private async Task DeleteVenue ()
    {
        if (SelectedVenueItem == null) return;
        var item = SelectedVenueItem;
        var confirmed = await Dialog.ShowConfirmAsync(Resources.Venue_DeleteConfirm ,
            string.Format(Resources.Venue_DeleteConfirmMsgFmt , item.Name));
        if (!confirmed) return;

        await SafeExecuteAsync(async () =>
        {
            await _facade.DeleteVenueAsync(item.Id);
            _logger?.LogInformation("会场已删除: {VenueId}", item.Id);
            SelectedVenueItem = null;
            PreviewSeats.Clear();
            PreviewOverlays.Clear();
            LayoutName = string.Empty;
            await LoadVenueList();
            StatusMessage = string.Format(Resources.Venue_DeletedFmt , item.Name);
        } , Resources.Venue_DeleteFailed);
    }

    private async Task SelectVenueAsync (VenueItem item , CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await SafeExecuteAsync(async () =>
        {
            var layout = await _facade.LoadVenueAsync(item.Id);
            if (ct.IsCancellationRequested) return;
            if (layout == null) { StatusMessage = string.Format(Resources.Venue_LoadFailedFmt , item.Name); return; }

            _suppressPreviewRegen = true;

            LayoutName = layout.Name;
            SelectedLayoutType = layout.LayoutType;
            IsFreeformVenue = layout.LayoutType == LayoutType.Freeform;

            switch (layout.Metadata)
            {
                case GridLayoutMetadata g:
                    _existingGridSeatMap = layout.Seats.OfType<GridSeat>()
                        .ToDictionary(s => (s.Row , s.Column) , s => s.Id);
                    _existingPolarSeatMap = null;
                    PopulateGridFromMetadata(g);
                    break;
                case PolarLayoutMetadata p:
                    _existingPolarSeatMap = layout.Seats.OfType<PolarSeat>()
                        .ToDictionary(s => (s.Ring , Math.Round(s.AngleDegrees , 2)) , s => s.Id);
                    _existingGridSeatMap = null;
                    PopulatePolarFromMetadata(p);
                    break;
                case FreeformLayoutMetadata:
                    _existingGridSeatMap = null;
                    _existingPolarSeatMap = null;
                    _freeformPreviewSeats = [.. layout.Seats.OfType<FreeformSeat>()];
                    _freeformPreviewObstacles = [.. layout.Obstacles];
                    break;
            }

            // 恢复障碍物配置（门等）
            RestoreObstaclesFromLayout(layout);

            _suppressPreviewRegen = false;
            _isDirty = false;
            RegeneratePreview();
            StatusMessage = string.Format(Resources.Venue_LoadedFmt , layout.Name , layout.Seats.Count);
        });
    }

    [RelayCommand]
    private async Task SaveVenue ()
    {
        if (SelectedVenueItem == null) return;
        var item = SelectedVenueItem;

        await SafeExecuteAsync(async () =>
        {
            var layout = BuildLayoutDefinition();
            await _facade.SaveVenueAsync(item.Id , layout);
            _isDirty = false;
            _logger?.LogInformation("会场已保存: {VenueId} - {SeatCount} 座", item.Id, layout.Seats.Count);
            await LoadVenueList();
            SelectedVenueItem = VenueItems.FirstOrDefault(v => v.Id == item.Id);
            StatusMessage = string.Format(Resources.Venue_SavedFmt , layout.Name , layout.Seats.Count);
        } , Resources.Venue_SaveFailed);
    }

    [RelayCommand]
    private void SelectLayoutType (string type)
    {
        if (IsFreeformVenue) return;
        SelectedLayoutType = type switch
        {
            "Polar" => LayoutType.Polar,
            "Freeform" => LayoutType.Freeform,
            _ => LayoutType.Grid,
        };
    }

    [RelayCommand]
    private void AddDoor ()
    {
        double dx = GridOriginX - 50;
        double dy = GridOriginY - 20;
        DoorItems.Add(new DoorItem(dx , dy , string.Format(Resources.Venue_DoorFmt , DoorItems.Count + 1)));
    }

    [RelayCommand]
    private void RemoveDoor (DoorItem door)
    {
        DoorItems.Remove(door);
    }

    [RelayCommand]
    private async Task NavigateToFreeformAsync () => await _navigation.NavigateToAsync(PageKey.FreeformManagement);

    // ═══════════════════════════════════════════════
    // 预览 & 构建
    // ═══════════════════════════════════════════════

    [RelayCommand]
    private void RegeneratePreview ()
    {
        var seats = new List<SeatPreview>();
        var overlays = new List<SeatPreview>();

        if (SelectedLayoutType == LayoutType.Grid)
        {
            var meta = BuildGridMetadata();
            var layout = GridLayoutBuilder.BuildGrid(meta);

            double SpacingTimes = 0.8;

            // 仅缩小同桌间距（IntraDeskSpacing），桌间和行间保持原样

            var previewMeta = new GridLayoutMetadata
            {
                Rows = meta.Rows ,
                Columns = meta.Columns ,
                OriginX = meta.OriginX ,
                OriginY = meta.OriginY ,
                SeatsPerDesk = meta.SeatsPerDesk ,
                IntraDeskSpacing = meta.IntraDeskSpacing * SpacingTimes ,
                InterDeskSpacing = meta.InterDeskSpacing ,
                HorizontalSpacing = meta.HorizontalSpacing ,
                VerticalSpacing = meta.VerticalSpacing ,
                AisleAfterColumns = meta.AisleAfterColumns ,
                AisleAfterRows = meta.AisleAfterRows ,
                AisleWidth = meta.AisleWidth ,
                ColumnRowCounts = meta.ColumnRowCounts ,
                FrontRowCount = meta.FrontRowCount ,
                HasPodium = meta.HasPodium ,
                PodiumWidth = meta.PodiumWidth ,
                PodiumHeight = meta.PodiumHeight ,
                HasFrontDoor = meta.HasFrontDoor ,
                EmptyPositions = meta.EmptyPositions ,
            };
            double seatW = 20, seatH = 14;

            foreach (GridSeat s in layout.Seats.Cast<GridSeat>())
            {
                var (x , y) = SeatGeometryHelper.GetPosition(s , previewMeta);
                bool isFront = s.Row <= meta.FrontRowCount;
                int deskNum = ((s.Column - 1) / meta.SeatsPerDesk) + 1;
                seats.Add(new SeatPreview
                {
                    X = x ,
                    Y = y ,
                    Width = seatW ,
                    Height = seatH ,
                    Label = string.Format(Resources.Venue_GridLabelFmt , s.Row , s.Column , deskNum) ,
                    ElementType = PreviewElementType.Seat ,
                    IsFrontRow = isFront
                });
            }

            // 讲台（水平居中于网格）
            if (meta.HasPodium && meta.PodiumWidth > 0 && meta.PodiumHeight > 0)
            {
                double gridLeft = seats.Min(s => s.X);
                double gridRight = seats.Max(s => s.X + s.Width);
                double podiumW = meta.PodiumWidth;
                double podiumH = meta.PodiumHeight;
                double podiumX = ((gridLeft + gridRight) / 2) - (podiumW / 2);
                double podiumY = meta.OriginY - meta.PodiumHeight - meta.VerticalSpacing;
                overlays.Add(new SeatPreview
                {
                    X = podiumX ,
                    Y = podiumY ,
                    Width = podiumW ,
                    Height = podiumH ,
                    ElementType = PreviewElementType.Podium ,
                    Label = Resources.Freeform_Podium
                });
            }

            // 禁用座位标记（红色半透明）
            foreach (var empty in meta.EmptyPositions ?? [])
            {
                var virtualSeat = new GridSeat { Row = empty.Row , Column = empty.Column };
                var (ex , ey) = SeatGeometryHelper.GetPosition(virtualSeat , previewMeta);
                overlays.Add(new SeatPreview
                {
                    X = ex ,
                    Y = ey ,
                    Width = seatW ,
                    Height = seatH ,
                    Label = string.Format(Resources.Venue_GridDisabledFmt , empty.Row , empty.Column) ,
                    ElementType = PreviewElementType.Aisle ,
                    BackgroundColor = "#80CC4444"
                });
            }
        }
        else if (SelectedLayoutType == LayoutType.Polar)
        {
            var meta = BuildPolarMetadata();
            var layout = PolarLayoutBuilder.BuildPolar(meta);
            int totalRings = meta.RingSeatCounts.Count > 0 ? meta.RingSeatCounts.Count : meta.Rings;

            double seatR = 7;  // 座位圆点半径
            foreach (PolarSeat s in layout.Seats.Cast<PolarSeat>())
            {
                var (cx , cy) = SeatGeometryHelper.GetPosition(s , meta);
                bool isFront = s.Ring > totalRings - meta.FrontRowCount;
                seats.Add(new SeatPreview
                {
                    X = cx - seatR ,
                    Y = cy - seatR ,
                    Width = seatR * 2 ,
                    Height = seatR * 2 ,
                    Label = $"R{s.Ring} {s.AngleDegrees:F0}° ({s.LogicalGroup})" ,
                    ElementType = PreviewElementType.Seat ,
                    IsFrontRow = isFront ,
                    CornerRadius = new(seatR) ,
                    IsCircle = true
                });
            }

            // 讲台（圆心处，完整圆）
            if (meta.HasPodium && meta.PodiumRadius > 0)
            {
                double pr = meta.PodiumRadius;
                overlays.Add(new SeatPreview
                {
                    X = meta.OriginX - pr ,
                    Y = meta.OriginY - pr ,
                    Width = pr * 2 ,
                    Height = pr * 2 ,
                    ElementType = PreviewElementType.Podium ,
                    Label = Resources.Freeform_Podium ,
                    CornerRadius = new(pr) ,
                    IsCircle = true ,
                    BackgroundColor = "#4080D0E0"
                });
            }

            // 禁用座位标记（红色半透明圆点，复用上方 seatR 变量）
            if (meta.EmptyPositions is { Count: > 0 })
            {
                var circularAisleSet = new HashSet<int>(meta.AisleCircularAfterRings ?? []);
                foreach (var empty in meta.EmptyPositions)
                {
                    int aislesBefore = circularAisleSet.Count(r => r < empty.Ring);
                    double radius = (empty.Ring * meta.RadiusStep) + (aislesBefore * meta.AisleCircularWidth);
                    double rad = empty.AngleDegrees * Math.PI / 180.0;
                    double cx = meta.OriginX + (radius * Math.Cos(rad));
                    double cy = meta.OriginY + (radius * Math.Sin(rad));
                    overlays.Add(new SeatPreview
                    {
                        X = cx - seatR ,
                        Y = cy - seatR ,
                        Width = seatR * 2 ,
                        Height = seatR * 2 ,
                        Label = string.Format(Resources.Venue_PolarDisabledFmt , empty.Ring , empty.AngleDegrees) ,
                        ElementType = PreviewElementType.Aisle ,
                        CornerRadius = new(seatR) ,
                        IsCircle = true ,
                        BackgroundColor = "#80CC4444"
                    });
                }
            }

        }
        else if (SelectedLayoutType == LayoutType.Freeform)
        {
            double seatSize = 18;
            foreach (var s in _freeformPreviewSeats)
            {
                seats.Add(new SeatPreview
                {
                    X = s.X - (seatSize / 2) ,
                    Y = s.Y - (seatSize / 2) ,
                    Width = seatSize ,
                    Height = seatSize ,
                    Label = s.Row.HasValue && s.Column.HasValue
                        ? $"R{s.Row}C{s.Column}"
                        : $"({s.X:F0}, {s.Y:F0})" ,
                    ElementType = PreviewElementType.Seat ,
                    CornerRadius = new(seatSize / 2) ,
                    IsCircle = true
                });
            }

            foreach (var obs in _freeformPreviewObstacles.Where(o => o.Type != "Door"))
            {
                var elementType = obs.Type == "Podium" ? PreviewElementType.Podium : PreviewElementType.Obstacle;
                double w = obs.Width > 0 ? obs.Width : 60;
                double h = obs.Height > 0 ? obs.Height : 40;
                overlays.Add(new SeatPreview
                {
                    X = obs.X - (w / 2) ,
                    Y = obs.Y - (h / 2) ,
                    Width = w ,
                    Height = h ,
                    Label = obs.Type ?? Resources.Seating_Obstacle ,
                    ElementType = elementType ,
                    CornerRadius = elementType == PreviewElementType.Podium ? new(w / 2) : new(4) ,
                    IsCircle = elementType == PreviewElementType.Podium ,
                    BackgroundColor = elementType == PreviewElementType.Podium ? "#4080D0E0" : "#60DD6666"
                });
            }
        }

        // 门（两种布局共用 DoorItems）
        foreach (var door in DoorItems)
        {
            overlays.Add(new SeatPreview
            {
                X = door.X ,
                Y = door.Y ,
                Width = 36 ,
                Height = 24 ,
                ElementType = PreviewElementType.Door ,
                Label = door.Label
            });
        }

        // 内容居中：margin 按内容范围的 25% 计算，最小 60px
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = 0, maxY = 0;
        foreach (var s in seats) { minX = Math.Min(minX , s.X); minY = Math.Min(minY , s.Y); maxX = Math.Max(maxX , s.X + s.Width); maxY = Math.Max(maxY , s.Y + s.Height); }
        foreach (var o in overlays) { minX = Math.Min(minX , o.X); minY = Math.Min(minY , o.Y); maxX = Math.Max(maxX , o.X + o.Width); maxY = Math.Max(maxY , o.Y + o.Height); }
        if (seats.Count > 0 || overlays.Count > 0)
        {
            double cw = maxX - minX, ch = maxY - minY;
            double mx = Math.Max(60 , cw * 0.25), my = Math.Max(60 , ch * 0.25);
            double ox = mx - minX, oy = my - minY;
            foreach (var s in seats) { s.X += ox; s.Y += oy; }
            foreach (var o in overlays) { o.X += ox; o.Y += oy; }
        }

        // 动态 Canvas 大小
        double canvasW = 600, canvasH = 600;
        if (seats.Count > 0 || overlays.Count > 0)
        {
            double cmaxX = 0, cmaxY = 0;
            foreach (var s in seats) { cmaxX = Math.Max(cmaxX , s.X + s.Width); cmaxY = Math.Max(cmaxY , s.Y + s.Height); }
            foreach (var o in overlays) { cmaxX = Math.Max(cmaxX , o.X + o.Width); cmaxY = Math.Max(cmaxY , o.Y + o.Height); }
            canvasW = Math.Max(600 , cmaxX + 40);
            canvasH = Math.Max(600 , cmaxY + 40);
        }
        CanvasWidth = canvasW;
        CanvasHeight = canvasH;

        PreviewSeats = new ObservableCollection<SeatPreview>(seats);
        PreviewOverlays = new ObservableCollection<SeatPreview>(overlays);
        StatusMessage = string.Format(Resources.Venue_PreviewSeatsFmt , seats.Count);
    }

    /// <summary>不改变数据，仅重新绘制预览区域。</summary>
    public void RefreshPreview () => RegeneratePreview();

    private void RegenerateAisleOptions ()
    {
        var prevCols = new HashSet<int>(ParseIntList(GridAisleAfterColumns));
        var prevRows = new HashSet<int>(ParseIntList(GridAisleAfterRows));
        int spd = GridSeatsPerDesk > 0 ? GridSeatsPerDesk : 1;

        // 列过道选项：以桌列为单位
        int deskCols = GridColumns / spd;
        var colOptions = new List<AisleOption>();
        for (int d = 1; d < deskCols; d++)
        {
            int seatCol = d * spd; // 过道在该座位列索引之后
            int leftStart = ((d - 1) * spd) + 1;
            int leftEnd = d * spd;
            int rightStart = (d * spd) + 1;
            int rightEnd = Math.Min((d + 1) * spd , GridColumns);
            string label = string.Format(Resources.Venue_ColAisleFmt , leftStart , leftEnd , rightStart , rightEnd);
            colOptions.Add(new AisleOption(label , seatCol , prevCols.Contains(seatCol)));
        }
        AisleColumnOptions = new ObservableCollection<AisleOption>(colOptions);
        foreach (var opt in AisleColumnOptions)
            opt.PropertyChanged += (_ , _) => SyncAisleColumnsFromOptions();

        // 行过道选项
        var rowOptions = new List<AisleOption>();
        for (int r = 1; r < GridRows; r++)
        {
            string label = string.Format(Resources.Venue_RowAisleFmt , r , r + 1);
            rowOptions.Add(new AisleOption(label , r , prevRows.Contains(r)));
        }
        AisleRowOptions = new ObservableCollection<AisleOption>(rowOptions);
        foreach (var opt in AisleRowOptions)
            opt.PropertyChanged += (_ , _) => SyncAisleRowsFromOptions();
    }

    /// <summary>过道勾选状态变化时同步回字符串。</summary>
    private void SyncAisleColumnsFromOptions ()
    {
        var selected = AisleColumnOptions.Where(o => o.IsSelected).Select(o => o.SeatColumn);
        GridAisleAfterColumns = string.Join("," , selected);
    }

    private void SyncAisleRowsFromOptions ()
    {
        var selected = AisleRowOptions.Where(o => o.IsSelected).Select(o => o.SeatColumn);
        GridAisleAfterRows = string.Join("," , selected);
    }

    private ClassroomLayoutDefinition BuildLayoutDefinition ()
    {
        ClassroomLayoutDefinition layout;
        switch (SelectedLayoutType)
        {
            case LayoutType.Grid:
                var meta = BuildGridMetadata();
                layout = GridLayoutBuilder.BuildGrid(meta);
                // 按位置匹配旧座位 ID，避免快照中 assignment 引用失效
                if (_existingGridSeatMap is { Count: > 0 } map)
                {
                    foreach (var s in layout.Seats.OfType<GridSeat>())
                    {
                        if (map.TryGetValue((s.Row , s.Column) , out var oldId))
                            s.Id = oldId;
                    }
                }
                layout.Name = LayoutName;
                layout.Id = SelectedVenueItem?.Id ?? "";
                // 将讲台/前门作为 Obstacle 写入（讲台居中于网格）
                if (meta.HasPodium && meta.PodiumWidth > 0 && meta.PodiumHeight > 0)
                {
                    double podiumW = meta.PodiumWidth;
                    double gridMidX = layout.Seats.Count > 0
                        ? (layout.Seats.Min(s => s is GridSeat g ? SeatGeometryHelper.GetPosition(s , meta).X : 0)
                         + layout.Seats.Max(s => s is GridSeat g ? SeatGeometryHelper.GetPosition(s , meta).X : 0)) / 2
                        : meta.OriginX;
                    layout.Obstacles.Add(new Obstacle
                    {
                        X = gridMidX - (podiumW / 2) ,
                        Y = meta.OriginY - meta.PodiumHeight - meta.VerticalSpacing ,
                        Width = podiumW ,
                        Height = meta.PodiumHeight ,
                        Type = "Podium"
                    });
                }
                foreach (var door in DoorItems)
                {
                    layout.Obstacles.Add(new Obstacle
                    {
                        X = door.X ,
                        Y = door.Y ,
                        Width = 36 ,
                        Height = 24 ,
                        Type = "Door"
                    });
                }
                break;

            case LayoutType.Polar:
                var polarMeta = BuildPolarMetadata();
                layout = PolarLayoutBuilder.BuildPolar(polarMeta);
                if (_existingPolarSeatMap is { Count: > 0 } polarMap)
                {
                    foreach (var s in layout.Seats.OfType<PolarSeat>())
                    {
                        if (polarMap.TryGetValue((s.Ring , Math.Round(s.AngleDegrees , 2)) , out var oldId))
                            s.Id = oldId;
                    }
                }
                layout.Name = LayoutName;
                layout.Id = SelectedVenueItem?.Id ?? "";
                foreach (var door in DoorItems)
                {
                    layout.Obstacles.Add(new Obstacle
                    {
                        X = door.X ,
                        Y = door.Y ,
                        Width = 36 ,
                        Height = 24 ,
                        Type = "Door"
                    });
                }
                break;

            case LayoutType.Freeform:
                var seatPoints = _freeformPreviewSeats
                    .Select(s => (s.X , s.Y , (int?)s.Row , (int?)s.Column , GroupId: (int?)null))
                    .ToList();
                var obstaclePoints = _freeformPreviewObstacles
                    .Where(o => o.Type != "Door")
                    .Select(o => (o.X , o.Y , Math.Max(o.Width , 60) , Math.Max(o.Height , 40) , o.Type ?? "Podium"))
                    .ToList();
                foreach (var d in DoorItems)
                    obstaclePoints.Add((d.X , d.Y , 36.0 , 24.0 , "Door"));
                layout = FreeformLayoutBuilder.BuildFreeform(
                    seatPoints , obstaclePoints.Count > 0 ? obstaclePoints : null);
                layout.Id = SelectedVenueItem?.Id ?? "";
                layout.Name = LayoutName;
                break;

            default:
                layout = new ClassroomLayoutDefinition { Name = LayoutName };
                break;
        }

        return layout;
    }

    // ═══════════════════════════════════════════════
    // 辅助
    // ═══════════════════════════════════════════════

    private GridLayoutMetadata BuildGridMetadata ()
    {
        return new GridLayoutMetadata
        {
            Rows = GridRows ,
            Columns = GridColumns ,
            HorizontalSpacing = GridHorizontalSpacing ,
            VerticalSpacing = GridVerticalSpacing ,
            OriginX = GridOriginX ,
            OriginY = GridOriginY ,
            SeatsPerDesk = GridSeatsPerDesk ,
            IntraDeskSpacing = GridIntraDeskSpacing ,
            InterDeskSpacing = GridInterDeskSpacing ,
            AisleAfterColumns = ParseIntList(GridAisleAfterColumns) ,
            AisleAfterRows = ParseIntList(GridAisleAfterRows) ,
            AisleWidth = GridAisleWidth ,
            FrontRowCount = GridFrontRowCount ,
            HasPodium = GridHasPodium ,
            PodiumWidth = GridPodiumWidth ,
            PodiumHeight = GridPodiumHeight ,
            ColumnRowCounts = ParseIntList(GridColumnRowCountsSpec) ,
            HasFrontDoor = GridHasFrontDoor ,
            EmptyPositions = FilterGridEmptyPositions(
                ParseGridEmptyPositions(GridEmptyPositionsSpec) , GridColumns , GridRows ,
                ParseIntList(GridColumnRowCountsSpec))
        };
    }

    private PolarLayoutMetadata BuildPolarMetadata ()
    {
        return new PolarLayoutMetadata
        {
            Rings = PolarRings ,
            SeatsPerRing = PolarSeatsPerRing ,
            RadiusStep = PolarRadiusStep ,
            StartAngleDegrees = PolarStartAngle ,
            EndAngleDegrees = PolarEndAngle ,
            OriginX = PolarOriginX ,
            OriginY = PolarOriginY ,
            RingSeatCounts = ParseIntList(PolarRingSeatCountsSpec) ,
            HasPodium = PolarHasPodium ,
            PodiumRadius = PolarPodiumRadius ,
            AisleRadialAngles = ParseDoubleList(PolarAisleRadialAngles) ,
            AisleRadialWidthDegrees = PolarAisleRadialWidth ,
            AisleCircularAfterRings = ParseIntList(PolarAisleCircularRings) ,
            AisleCircularWidth = PolarAisleCircularWidth ,
            FrontRowCount = PolarFrontRowCount ,
            EmptyPositions = FilterPolarEmptyPositions(
                ParsePolarEmptyPositions(PolarEmptyPositionsSpec) ,
                ParseIntList(PolarRingSeatCountsSpec) , PolarRings)
        };
    }

    private void RestoreObstaclesFromLayout (ClassroomLayoutDefinition layout)
    {
        var doors = layout.Obstacles.Where(o => o.Type == "Door").ToList();
        DoorItems = new ObservableCollection<DoorItem>(
            doors.Select((d , i) => new DoorItem(d.X , d.Y , string.Format(Resources.Venue_DoorFmt , i + 1))));

        if (layout.Metadata is GridLayoutMetadata gridMeta)
            GridHasFrontDoor = gridMeta.HasFrontDoor;
    }

    private void PopulateGridFromMetadata (GridLayoutMetadata g)
    {
        GridRows = g.Rows > 0 ? g.Rows : 5;
        GridColumns = g.Columns > 0 ? g.Columns : 8;
        GridHorizontalSpacing = g.HorizontalSpacing > 0 ? g.HorizontalSpacing : 40;
        GridVerticalSpacing = g.VerticalSpacing > 0 ? g.VerticalSpacing : 36;
        GridOriginX = g.OriginX > 0 ? g.OriginX : 200;
        GridOriginY = g.OriginY > 0 ? g.OriginY : 200;
        GridSeatsPerDesk = g.SeatsPerDesk > 0 ? g.SeatsPerDesk : 2;
        GridIntraDeskSpacing = g.IntraDeskSpacing > 0 ? g.IntraDeskSpacing : 12;
        GridInterDeskSpacing = g.InterDeskSpacing > 0 ? g.InterDeskSpacing : 40;
        GridAisleAfterColumns = string.Join("," , g.AisleAfterColumns ?? []);
        GridAisleAfterRows = string.Join("," , g.AisleAfterRows ?? []);
        GridAisleWidth = g.AisleWidth > 0 ? g.AisleWidth : 60;
        GridFrontRowCount = g.FrontRowCount > 0 ? g.FrontRowCount : 1;
        GridHasPodium = g.HasPodium;
        GridPodiumWidth = g.PodiumWidth > 0 ? g.PodiumWidth : 60;
        GridPodiumHeight = g.PodiumHeight > 0 ? g.PodiumHeight : 40;
        GridColumnRowCountsSpec = g.ColumnRowCounts is { Count: > 0 } ? string.Join("," , g.ColumnRowCounts) : "";
        GridEmptyPositionsSpec = g.EmptyPositions is { Count: > 0 }
            ? string.Join(";" , g.EmptyPositions.Select(p => $"{p.Row},{p.Column}"))
            : "";

        RegenerateAisleOptions();
    }

    private void PopulatePolarFromMetadata (PolarLayoutMetadata p)
    {
        PolarRings = p.Rings > 0 ? p.Rings : 3;
        PolarSeatsPerRing = p.SeatsPerRing > 0 ? p.SeatsPerRing : 12;
        PolarRadiusStep = p.RadiusStep > 0 ? p.RadiusStep : 40;
        PolarStartAngle = p.StartAngleDegrees;
        PolarEndAngle = p.EndAngleDegrees > 0 ? p.EndAngleDegrees : 360;
        PolarOriginX = p.OriginX > 0 ? p.OriginX : 200;
        PolarOriginY = p.OriginY > 0 ? p.OriginY : 200;
        PolarRingSeatCountsSpec = p.RingSeatCounts is { Count: > 0 } ? string.Join("," , p.RingSeatCounts) : "";
        PolarHasPodium = p.HasPodium;
        PolarPodiumRadius = p.PodiumRadius > 0 ? p.PodiumRadius : 30;
        PolarAisleRadialAngles = p.AisleRadialAngles is { Count: > 0 } ? string.Join("," , p.AisleRadialAngles.Select(a => a.ToString("F1"))) : "";
        PolarAisleRadialWidth = p.AisleRadialWidthDegrees > 0 ? p.AisleRadialWidthDegrees : 5;
        PolarAisleCircularRings = p.AisleCircularAfterRings is { Count: > 0 } ? string.Join("," , p.AisleCircularAfterRings) : "";
        PolarAisleCircularWidth = p.AisleCircularWidth > 0 ? p.AisleCircularWidth : 20;
        PolarFrontRowCount = p.FrontRowCount > 0 ? p.FrontRowCount : 1;
        PolarEmptyPositionsSpec = p.EmptyPositions is { Count: > 0 }
            ? string.Join(";" , p.EmptyPositions.Select(e => $"{e.Ring},{e.AngleDegrees:F2}"))
            : "";
    }

    private void ResetParameters ()
    {
        GridRows = 5; GridColumns = 8;
        GridHorizontalSpacing = 64; GridVerticalSpacing = 56;
        GridOriginX = 200; GridOriginY = 200;
        GridSeatsPerDesk = 2;
        GridIntraDeskSpacing = 40; GridInterDeskSpacing = 64;
        GridAisleAfterColumns = ""; GridAisleAfterRows = "";
        GridAisleWidth = 60;
        GridFrontRowCount = 1;
        GridHasPodium = true; GridPodiumWidth = 100; GridPodiumHeight = 40;
        GridColumnRowCountsSpec = ""; GridEmptyPositionsSpec = "";
        DoorItems.Clear();
        PolarRings = 3; PolarSeatsPerRing = 12;
        PolarRadiusStep = 40; PolarStartAngle = 0; PolarEndAngle = 360;
        PolarOriginX = 200; PolarOriginY = 200;
        PolarRingSeatCountsSpec = "";
        PolarHasPodium = true; PolarPodiumRadius = 30;
        PolarAisleRadialAngles = ""; PolarAisleRadialWidth = 5;
        PolarAisleCircularRings = ""; PolarAisleCircularWidth = 20;
        PolarFrontRowCount = 1;
        PolarEmptyPositionsSpec = "";
        _freeformPreviewSeats = [];
        _freeformPreviewObstacles = [];
    }

    private static List<int> ParseIntList (string csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return [];
        return [.. csv.Split(',' , StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s.Trim() , out var n) ? n : -1)
            .Where(n => n > 0)];
    }

    private static List<double> ParseDoubleList (string csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return [];
        return [.. csv.Split(',' , StringSplitOptions.RemoveEmptyEntries)
            .Select(s => double.TryParse(s.Trim() , out var n) ? n : -1)
            .Where(n => n >= 0)];
    }

    private static List<GridPosition> ParseGridEmptyPositions (string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return [];
        return [.. spec.Split(';' , StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
            {
                var parts = part.Split(',');
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim() , out var row)
                    && int.TryParse(parts[1].Trim() , out var col)
                    && row > 0 && col > 0)
                    return new GridPosition { Row = row , Column = col };
                return null;
            })
            .Where(p => p != null)
            .Cast<GridPosition>()];
    }

    private static List<PolarRingAngle> ParsePolarEmptyPositions (string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return [];
        return [.. spec.Split(';' , StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
            {
                var parts = part.Split(',');
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim() , out var ring)
                    && double.TryParse(parts[1].Trim() , out var angle)
                    && ring > 0)
                    return new PolarRingAngle { Ring = ring , AngleDegrees = angle };
                return null;
            })
            .Where(p => p != null)
            .Cast<PolarRingAngle>()];
    }

    /// <summary>过滤掉行列号超出有效范围的禁用位置。</summary>
    private static List<GridPosition> FilterGridEmptyPositions (List<GridPosition> raw , int columns , int defaultRows , List<int> columnRowCounts)
    {
        if (raw.Count == 0) return raw;
        return [.. raw.Where(p =>
        {
            int maxRows = columnRowCounts is { Count: > 0 } && p.Column <= columnRowCounts.Count
                ? columnRowCounts[p.Column - 1] : defaultRows;
            return p.Row >= 1 && p.Column >= 1 && p.Row <= maxRows && p.Column <= columns;
        })];
    }

    /// <summary>过滤掉环号超出有效范围的禁用位置。</summary>
    private static List<PolarRingAngle> FilterPolarEmptyPositions (List<PolarRingAngle> raw , List<int> ringSeatCounts , int defaultRings)
    {
        if (raw.Count == 0) return raw;
        int totalRings = ringSeatCounts.Count > 0 ? ringSeatCounts.Count : defaultRings;
        return [.. raw.Where(p => p.Ring >= 1 && p.Ring <= totalRings)];
    }

    partial void OnSelectedVenueItemChanged (VenueItem? value)
    {
        if (!_suppressAutoLoad && value != null)
        {
            _selectVenueCts?.Cancel();
            _selectVenueCts = new CancellationTokenSource();
            _ = SelectVenueAsync(value , _selectVenueCts.Token);
        }
    }

    partial void OnSelectedLayoutTypeChanged (LayoutType value) => RegeneratePreviewIfNotSuppressed();

    // ── Grid 参数变更 → 立即刷新预览 ──
    partial void OnGridRowsChanged (int value) { RegenerateAisleOptions(); RegeneratePreviewIfNotSuppressed(); }
    partial void OnGridColumnsChanged (int value) { RegenerateAisleOptions(); RegeneratePreviewIfNotSuppressed(); }
    partial void OnGridHorizontalSpacingChanged (double value) => RegeneratePreviewIfNotSuppressed();
    partial void OnGridVerticalSpacingChanged (double value) => RegeneratePreviewIfNotSuppressed();
    partial void OnGridOriginXChanged (double value) => RegeneratePreviewIfNotSuppressed();
    partial void OnGridOriginYChanged (double value) => RegeneratePreviewIfNotSuppressed();
    partial void OnGridSeatsPerDeskChanged (int value) { RegenerateAisleOptions(); RegeneratePreviewIfNotSuppressed(); }
    partial void OnGridIntraDeskSpacingChanged (double value) => RegeneratePreviewIfNotSuppressed();
    partial void OnGridInterDeskSpacingChanged (double value) => RegeneratePreviewIfNotSuppressed();
    partial void OnGridAisleAfterColumnsChanged (string value) => RegeneratePreviewIfNotSuppressed();
    partial void OnGridAisleAfterRowsChanged (string value) => RegeneratePreviewIfNotSuppressed();
    partial void OnGridAisleWidthChanged (double value) => RegeneratePreviewIfNotSuppressed();
    partial void OnGridFrontRowCountChanged (int value) => RegeneratePreviewIfNotSuppressed();
    partial void OnGridHasPodiumChanged (bool value) => RegeneratePreviewIfNotSuppressed();
    partial void OnGridPodiumWidthChanged (double value) => RegeneratePreviewIfNotSuppressed();
    partial void OnGridPodiumHeightChanged (double value) => RegeneratePreviewIfNotSuppressed();
    partial void OnGridColumnRowCountsSpecChanged (string value) => RegeneratePreviewIfNotSuppressed();
    partial void OnGridEmptyPositionsSpecChanged (string value) => RegeneratePreviewIfNotSuppressed();

    // ── Polar 参数变更 → 立即刷新预览 ──
    partial void OnPolarRingsChanged (int value) => RegeneratePreviewIfNotSuppressed();
    partial void OnPolarSeatsPerRingChanged (int value) => RegeneratePreviewIfNotSuppressed();
    partial void OnPolarRadiusStepChanged (double value) => RegeneratePreviewIfNotSuppressed();
    partial void OnPolarStartAngleChanged (double value) => RegeneratePreviewIfNotSuppressed();
    partial void OnPolarEndAngleChanged (double value) => RegeneratePreviewIfNotSuppressed();
    partial void OnPolarOriginXChanged (double value) => RegeneratePreviewIfNotSuppressed();
    partial void OnPolarOriginYChanged (double value) => RegeneratePreviewIfNotSuppressed();
    partial void OnPolarRingSeatCountsSpecChanged (string value) => RegeneratePreviewIfNotSuppressed();
    partial void OnPolarEmptyPositionsSpecChanged (string value) => RegeneratePreviewIfNotSuppressed();
    partial void OnPolarHasPodiumChanged (bool value) => RegeneratePreviewIfNotSuppressed();
    partial void OnPolarPodiumRadiusChanged (double value) => RegeneratePreviewIfNotSuppressed();
    partial void OnPolarAisleRadialAnglesChanged (string value) => RegeneratePreviewIfNotSuppressed();
    partial void OnPolarAisleRadialWidthChanged (double value) => RegeneratePreviewIfNotSuppressed();
    partial void OnPolarAisleCircularRingsChanged (string value) => RegeneratePreviewIfNotSuppressed();
    partial void OnPolarAisleCircularWidthChanged (double value) => RegeneratePreviewIfNotSuppressed();
    partial void OnPolarFrontRowCountChanged (int value) => RegeneratePreviewIfNotSuppressed();

    // ── 门变更 → 立即刷新预览 ──
    partial void OnDoorItemsChanged (ObservableCollection<DoorItem> value) => SubscribeToDoorCollection(value);

    partial void OnLayoutNameChanged (string value) => _isDirty = true;

    private void SubscribeToDoorCollection (ObservableCollection<DoorItem> doors)
    {
        doors.CollectionChanged += (_ , e) =>
        {
            if (e.NewItems != null)
            {
                foreach (DoorItem item in e.NewItems)
                    item.PropertyChanged += OnDoorItemPropertyChanged;
            }
            if (e.OldItems != null)
            {
                foreach (DoorItem item in e.OldItems)
                    item.PropertyChanged -= OnDoorItemPropertyChanged;
            }
            RegeneratePreviewIfNotSuppressed();
        };
        foreach (var door in doors)
            door.PropertyChanged += OnDoorItemPropertyChanged;
    }

    private void OnDoorItemPropertyChanged (object? sender , System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DoorItem.X) or nameof(DoorItem.Y))
            RegeneratePreviewIfNotSuppressed();
    }

    /// <summary>在不被抑制时立即刷新预览，同时标记脏状态。</summary>
    private void RegeneratePreviewIfNotSuppressed ()
    {
        if (!_suppressPreviewRegen)
        {
            _isDirty = true;
            RegeneratePreview();
        }
    }

    public override async Task<bool> CanLeaveAsync ()
    {
        if (!_isDirty || SelectedVenueItem is null)
        {
            ClearVenueState();
            return true;
        }

        var choice = await Dialog.ShowMultiOptionAsync(
            Resources.Venue_UnsavedChanges ,
            Resources.Venue_UnsavedChangesMsg ,
            Resources.Common_Save ,
            Resources.Common_Discard ,
            Resources.Common_Cancel);

        switch (choice)
        {
            case 0: // 保存
                await SaveVenue();
                break;
            case 1: // 放弃
                break;
            default: // 取消
                return false;
        }

        ClearVenueState();
        return true;
    }

    private void ClearVenueState ()
    {
        SelectedVenueItem = null;
        _isDirty = false;
        PreviewSeats.Clear();
        PreviewOverlays.Clear();
        LayoutName = string.Empty;
        _existingGridSeatMap = null;
        _existingPolarSeatMap = null;
        _freeformPreviewSeats = [];
        _freeformPreviewObstacles = [];
    }
}

public record VenueItem (string Id , string Name);

public partial class DoorItem : ObservableObject
{
    [ObservableProperty]
    public partial double X { get; set; }

    [ObservableProperty]
    public partial double Y { get; set; }

    [ObservableProperty]
    public partial string Label { get; set; } = Resources.Freeform_Door;

    public DoorItem () { }
    public DoorItem (double x , double y , string? label = null)
    {
        X = x;
        Y = y;
        Label = label ?? Resources.Freeform_Door;
    }
}

public partial class AisleOption (string label , int seatColumn , bool selected = false) : ObservableObject
{
    public string Label { get; set; } = label;
    public int SeatColumn { get; set; } = seatColumn;

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = selected;
}

public class SeatPreview
{
    public double X { get; set; }
    public double Y { get; set; }
    public string Label { get; set; } = string.Empty;
    public PreviewElementType ElementType { get; set; } = PreviewElementType.Seat;
    public double Width { get; set; } = 20;
    public double Height { get; set; } = 20;
    public bool IsFrontRow { get; set; }
    public global::Avalonia.CornerRadius CornerRadius { get; set; } = new(2);
    public double Rotation { get; set; }
    public string BackgroundColor { get; set; } = "#800072C6";
    public string BorderColor { get; set; } = "";
    public global::Avalonia.Thickness BorderThickness { get; set; }
    public bool IsCircle { get; set; }
    public string? PathData { get; set; }
    public string PathFill { get; set; } = "";
    public global::Avalonia.Media.StreamGeometry? PathGeometry { get; set; }
}

public enum PreviewElementType
{
    Seat,
    Obstacle,
    Podium,
    Door,
    Aisle
}
