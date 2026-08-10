using System;
using System.Threading;
using System.Threading.Tasks;
using SeatFlow.Presentation.Avalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;

namespace SeatFlow.Presentation.Avalonia.Views;

/// <summary>
/// 更新对话框窗口。展示 release notes（Markdown 渲染）、下载进度，
/// 并提供"立即更新"/"安装并重启"操作。
/// </summary>
internal partial class UpdateDialogWindow : Window
{
    private UpdateDialogViewModel _vm = null!;

    /// <summary>用户点击了"安装并重启"并且更新已就绪。</summary>
    public bool Confirmed { get; private set; }

    /// <summary>更新已下载完成（但用户可能点击了"稍后再说"）。</summary>
    public bool Downloaded { get; private set; }

    public UpdateDialogWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 静态工厂方法：通过 DI 创建 ViewModel，初始化并绑定。
    /// </summary>
    public static async Task<UpdateDialogWindow> CreateAsync(
        IServiceProvider serviceProvider,
        string newVersion,
        bool allowDownload,
        CancellationToken ct = default)
    {
        var vm = ActivatorUtilities.CreateInstance<UpdateDialogViewModel>(serviceProvider);
        var window = new UpdateDialogWindow { DataContext = vm };
        window._vm = vm;

        await vm.InitializeAsync(newVersion, allowDownload, ct);

        // 订阅窗口关闭信号
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(UpdateDialogViewModel.Confirmed) && vm.Confirmed)
            {
                window.Confirmed = true;
                window.Downloaded = vm.Downloaded;
                window.Close(true);
            }
        };

        return window;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // 将"关闭"按钮连接到直接关闭窗口
        CloseBtn.Click += (_, _) =>
        {
            Downloaded = _vm.Downloaded;
            Close(false);
        };

        // 当 Confirm/Close 属性变化时跟踪 Downloaded
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(UpdateDialogViewModel.Downloaded))
                Downloaded = _vm.Downloaded;
        };
    }
}
