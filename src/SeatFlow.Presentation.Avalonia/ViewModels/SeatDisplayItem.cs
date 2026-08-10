using SeatFlow.Presentation.Avalonia.Lang;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

public partial class SeatDisplayItem : ObservableObject
{
    // ── 几何（不可变） ──
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; init; } = 22;
    public double Height { get; init; } = 18;
    public CornerRadius CornerRadius { get; init; } = new(3);
    public string SeatId { get; init; } = string.Empty;
    public string SeatLabel { get; init; } = string.Empty;

    // ── 座位类型标签（固定/前排） ──
    public bool IsFrontRow { get; init; }

    // ── 占有者（可观察） ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    [NotifyPropertyChangedFor(nameof(TooltipText))]
    [NotifyPropertyChangedFor(nameof(IsOccupied))]
    public partial string? StudentName { get; set; }

    [ObservableProperty]
    public partial string? StudentId { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    [NotifyPropertyChangedFor(nameof(TooltipText))]
    public partial bool IsOccupied { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BorderBrush))]
    public partial bool IsFixed { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackgroundBrush))]
    [NotifyPropertyChangedFor(nameof(BorderBrush))]
    public partial SeatOccupancyStatus OccupancyStatus { get; set; }

    // ── 交互状态 ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BorderBrush))]
    [NotifyPropertyChangedFor(nameof(BackgroundBrush))]
    public partial bool IsSelectedForSwap { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BorderBrush))]
    [NotifyPropertyChangedFor(nameof(BackgroundBrush))]
    public partial bool IsDataStale { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackgroundBrush))]
    [NotifyPropertyChangedFor(nameof(BorderBrush))]
    public partial bool IsDragHover { get; set; }

    // ── 计算属性 ──
    public string DisplayText => IsOccupied ? (StudentName ?? "") : SeatLabel;

    // ── Tooltip ──
    public string TooltipText => IsOccupied
        ? $"{StudentName} - {SeatLabel}"
        : string.Format(Resources.Seating_EmptySeatFmt , SeatLabel);

    // ── 颜色 ──
    private static readonly SolidColorBrush EmptyBg = new(Color.FromArgb(0x20 , 0xA0 , 0xA0 , 0xA0));
    private static readonly SolidColorBrush EmptyBorder = new(Color.FromArgb(0x80 , 0xA0 , 0xA0 , 0xA0));
    private static readonly SolidColorBrush OccupiedBg = new(Color.FromArgb(0x60 , 0x16 , 0xA3 , 0x4A));
    private static readonly SolidColorBrush OccupiedBorder = new(Color.FromArgb(0xFF , 0x16 , 0xA3 , 0x4A));
    private static readonly SolidColorBrush FixedBg = new(Color.FromArgb(0x20 , 0x25 , 0x63 , 0xEB));
    private static readonly SolidColorBrush FixedBorder = new(Color.FromArgb(0xFF , 0x25 , 0x63 , 0xEB));
    private static readonly SolidColorBrush SwapBg = new(Color.FromArgb(0x40 , 0xF9 , 0x73 , 0x16));
    private static readonly SolidColorBrush SwapBorder = new(Color.FromArgb(0xFF , 0xF9 , 0x73 , 0x16));
    private static readonly SolidColorBrush StaleBg = new(Color.FromArgb(0x30 , 0xF9 , 0xA8 , 0x25));
    private static readonly SolidColorBrush StaleBorder = new(Color.FromArgb(0xFF , 0xF9 , 0xA8 , 0x25));
    private static readonly SolidColorBrush DropTargetBg = new(Color.FromArgb(0x40 , 0x00 , 0x78 , 0xD4));
    private static readonly SolidColorBrush DropTargetBorder = new(Color.FromArgb(0xFF , 0x00 , 0x78 , 0xD4));

    public IBrush BackgroundBrush => IsDragHover ? DropTargetBg :
        IsSelectedForSwap ? SwapBg :
        IsDataStale ? StaleBg : OccupancyStatus switch
        {
            SeatOccupancyStatus.Occupied => OccupiedBg,
            SeatOccupancyStatus.Fixed => FixedBg,
            _ => EmptyBg
        };

    public IBrush BorderBrush => IsDragHover ? DropTargetBorder :
        IsSelectedForSwap ? SwapBorder :
        IsDataStale ? StaleBorder : OccupancyStatus switch
        {
            SeatOccupancyStatus.Occupied => OccupiedBorder,
            SeatOccupancyStatus.Fixed => FixedBorder,
            _ => EmptyBorder
        };
}

public enum SeatOccupancyStatus
{
    Empty,
    Occupied,
    Fixed
}
