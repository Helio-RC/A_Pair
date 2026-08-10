using Avalonia.Controls;
using Avalonia.Interactivity;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Avalonia.Views;

/// <summary>
/// .seatsets 数据包导出/导入的数据类别选择对话框。
/// 用户通过复选框选择要导出/导入的数据类别。
/// </summary>
internal partial class SeatSetsSelectionWindow : Window
{
    private readonly SeatSetsSelectionViewModel _viewModel;

    public SeatSetsSelectionWindow ()
    {
        InitializeComponent();
        _viewModel = new SeatSetsSelectionViewModel();
        DataContext = _viewModel;

        ConfirmButton.Click += OnConfirm;
        CancelButton.Click += OnCancel;
        ToggleAllButton.Click += (_ , _) => _viewModel.ToggleAllCommand.Execute(null);
    }

    /// <summary>是否为导出模式（false 表示导入模式）。</summary>
    public bool IsExport
    {
        get => _viewModel.IsExport;
        set => _viewModel.IsExport = value;
    }

    /// <summary>获取用户的类别选择结果。</summary>
    public SeatSetsSelectionViewModel ViewModel => _viewModel;

    /// <summary>
    /// 根据可用类别预填复选框（用于导入模式）。
    /// </summary>
    public void SetAvailableCategories (
        bool appSettings , bool venues , bool rosters ,
        bool snapshots , bool strategyConfig)
    {
        _viewModel.SetAvailableCategories(appSettings , venues , rosters , snapshots , strategyConfig);
    }

    protected override void OnLoaded (RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Title = _viewModel.Title;
    }

    private void OnConfirm (object? sender , RoutedEventArgs e)
        => Close(_viewModel.IsAnySelected);

    private void OnCancel (object? sender , RoutedEventArgs e)
        => Close(false);
}
