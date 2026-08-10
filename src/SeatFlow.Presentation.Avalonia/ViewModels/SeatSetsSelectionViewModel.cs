using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SeatFlow.Presentation.Avalonia.Lang;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

/// <summary>
/// .seatsets 数据包导出/导入选择对话框的 ViewModel。
/// 包含五个数据类别的复选框和一个全选/取消全选命令。
/// 区分导出模式（IsExport=true）和导入模式（IsExport=false）。
/// </summary>
public partial class SeatSetsSelectionViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial bool IncludeAppSettings { get; set; } = true;

    [ObservableProperty]
    public partial bool IncludeVenues { get; set; } = true;

    [ObservableProperty]
    public partial bool IncludeRosters { get; set; } = true;

    [ObservableProperty]
    public partial bool IncludeSnapshots { get; set; } = true;

    [ObservableProperty]
    public partial bool IncludeStrategyConfig { get; set; } = true;

    /// <summary>是否为导出模式（false 表示导入模式）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial bool IsExport { get; set; } = true;

    /// <summary>对话框标题。</summary>
    public string Title => IsExport
        ? Resources.SeatSets_ExportTitle
        : Resources.SeatSets_ImportTitle;

    /// <summary>全选/取消全选按钮文本。</summary>
    [ObservableProperty]
    public partial string ToggleAllText { get; set; } = Resources.SeatSets_DeselectAll;

    /// <summary>是否所有类别都已选中。</summary>
    public bool IsAllSelected =>
        IncludeAppSettings && IncludeVenues && IncludeRosters
        && IncludeSnapshots && IncludeStrategyConfig;

    /// <summary>是否有任何类别被选中。</summary>
    public bool IsAnySelected =>
        IncludeAppSettings || IncludeVenues || IncludeRosters
        || IncludeSnapshots || IncludeStrategyConfig;

    /// <summary>
    /// 根据可用类别更新复选框状态。用于导入模式下预填。
    /// </summary>
    public void SetAvailableCategories (
        bool appSettings , bool venues , bool rosters ,
        bool snapshots , bool strategyConfig)
    {
        IncludeAppSettings = appSettings;
        IncludeVenues = venues;
        IncludeRosters = rosters;
        IncludeSnapshots = snapshots;
        IncludeStrategyConfig = strategyConfig;
        UpdateToggleAllText();
    }

    /// <summary>
    /// 构建用户选择的数据类别对象。
    /// </summary>
    public Core.Models.SeatSets.SeatSetsExportSelection ToSelection () => new()
    {
        IncludeAppSettings = IncludeAppSettings ,
        IncludeVenues = IncludeVenues ,
        IncludeRosters = IncludeRosters ,
        IncludeSnapshots = IncludeSnapshots ,
        IncludeStrategyConfig = IncludeStrategyConfig
    };

    [RelayCommand]
    private void ToggleAll ()
    {
        bool all = IsAllSelected;
        IncludeAppSettings = !all;
        IncludeVenues = !all;
        IncludeRosters = !all;
        IncludeSnapshots = !all;
        IncludeStrategyConfig = !all;
        UpdateToggleAllText();
    }

    partial void OnIncludeAppSettingsChanged (bool value) => UpdateToggleAllText();
    partial void OnIncludeVenuesChanged (bool value) => UpdateToggleAllText();
    partial void OnIncludeRostersChanged (bool value) => UpdateToggleAllText();
    partial void OnIncludeSnapshotsChanged (bool value) => UpdateToggleAllText();
    partial void OnIncludeStrategyConfigChanged (bool value) => UpdateToggleAllText();

    private void UpdateToggleAllText ()
    {
        ToggleAllText = IsAllSelected
            ? Resources.SeatSets_DeselectAll
            : Resources.SeatSets_SelectAll;
    }
}
