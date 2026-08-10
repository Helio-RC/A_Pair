using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace SeatFlow.Presentation.Avalonia.Views;

public partial class MemberManagementView : UserControl
{
    private ViewModels.MemberManagementViewModel? _vm;

    public MemberManagementView ()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged (EventArgs e)
    {
        base.OnDataContextChanged(e);
        _vm?.PropertyChanged -= OnSidebarWidthChanged;
        _vm = DataContext as ViewModels.MemberManagementViewModel;
        if (_vm != null)
        {
            _vm.PropertyChanged += OnSidebarWidthChanged;
            SyncSidebar(_vm.SidebarListWidth);
        }
    }

    private void OnSidebarWidthChanged (object? sender , PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.MemberManagementViewModel.SidebarListWidth)
            && sender is ViewModels.MemberManagementViewModel vm)
        {
            SyncSidebar(vm.SidebarListWidth);
        }
    }

    private void SyncSidebar (double width)
    {
        var grid = this.FindControl<Grid>("SidebarGrid");
        if (grid != null && grid.ColumnDefinitions.Count > 0)
            grid.ColumnDefinitions[0].Width = new GridLength(width);
    }

    /// <summary>新增行 Name 文本框按 Enter 时触发 AddNewStudentCommand。</summary>
    public void OnNewStudentNameKeyDown (object? sender , KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ViewModels.MemberManagementViewModel vm)
        {
            vm.AddNewStudentCommand.Execute(null);
            e.Handled = true;
        }
    }

    protected override void OnDetachedFromVisualTree (VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _vm?.PropertyChanged -= OnSidebarWidthChanged;
        _vm = null;
    }

    protected override void OnPropertyChanged (AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoundsProperty && DataContext is ViewModels.MemberManagementViewModel vm)
        {
            vm.OnWindowWidthChanged(Bounds.Width);
        }
    }
}
