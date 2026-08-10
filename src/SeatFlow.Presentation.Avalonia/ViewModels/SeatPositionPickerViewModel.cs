using CommunityToolkit.Mvvm.ComponentModel;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

/// <summary>
/// 座位定位器 ViewModel。根据布局类型显示不同的定位控件。
/// </summary>
public partial class SeatPositionPickerViewModel : ViewModelBase
{
    /// <summary>布局类型：Grid / Polar / Freeform。</summary>
    [ObservableProperty]
    public partial string LayoutType { get; set; } = "Grid";

    // ── Grid ──

    [ObservableProperty]
    public partial int Row { get; set; } = 1;

    [ObservableProperty]
    public partial int Column { get; set; } = 1;

    // ── Polar ──

    [ObservableProperty]
    public partial int Ring { get; set; } = 1;

    [ObservableProperty]
    public partial double Angle { get; set; }

    // ── Freeform ──

    [ObservableProperty]
    public partial double X { get; set; }

    [ObservableProperty]
    public partial double Y { get; set; }

    // ── 动态范围（由会场 LayoutMetadata 决定，默认值为无约束时的上限） ──

    /// <summary>Grid 布局的最大行号。</summary>
    [ObservableProperty]
    public partial int GridMaxRow { get; set; } = 99;

    /// <summary>Grid 布局的最大列号。</summary>
    [ObservableProperty]
    public partial int GridMaxColumn { get; set; } = 99;

    /// <summary>Polar 布局的最大环号。</summary>
    [ObservableProperty]
    public partial int PolarMaxRing { get; set; } = 99;

    /// <summary>Freeform 布局的最大 X 坐标。</summary>
    [ObservableProperty]
    public partial double FreeformMaxX { get; set; } = 9999;

    /// <summary>Freeform 布局的最大 Y 坐标。</summary>
    [ObservableProperty]
    public partial double FreeformMaxY { get; set; } = 9999;

    // ── 类型判断 ──

    public bool IsGrid => LayoutType == "Grid";
    public bool IsPolar => LayoutType == "Polar";
    public bool IsFreeform => LayoutType == "Freeform";

    partial void OnLayoutTypeChanged (string value)
    {
        OnPropertyChanged(nameof(IsGrid));
        OnPropertyChanged(nameof(IsPolar));
        OnPropertyChanged(nameof(IsFreeform));
    }
}
