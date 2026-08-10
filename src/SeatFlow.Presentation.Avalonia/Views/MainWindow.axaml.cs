using System;
using System.Threading.Tasks;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;
using Avalonia;
using Avalonia.Controls;
using CodeWF.AvaloniaControls.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace SeatFlow.Presentation.Avalonia.Views
{
    public partial class MainWindow : Window
    {
        private readonly IOnboardingService _onboarding;
        private readonly INavigationService _navigation;
        private bool _isClosingConfirmed;

        public MainWindow (IOnboardingService onboarding , INavigationService navigation)
        {
            _onboarding = onboarding;
            _navigation = navigation;
            InitializeComponent();

            // 引导期间窗口最小化/Alt+Tab 时，Guide 的 Popup
            // (ShouldUseOverlayLayer=False) 可能残留为孤儿 OS 窗口。
            // 订阅窗口激活/失活事件以同步隐藏/恢复。
            Activated += (_ , _) => _onboarding.HandleWindowActivated();
            Deactivated += (_ , _) => _onboarding.HandleWindowDeactivated();
        }

        /// <summary>Guide 步骤全部完成（用户点击最后一步的"完成"按钮）。</summary>
        private void OnGuideCompleted (object? sender , EventArgs e)
            => _onboarding.HandleGuideCompleted();

        /// <summary>Guide 被用户关闭（点击 × 或按 Esc）。</summary>
        private async void OnGuideClosed (object? sender , EventArgs e)
        {
            if (!await _onboarding.HandleGuideClosedAsync())
                OnboardingGuide.Show();
        }

        /// <summary>Guide 步骤切换前，解析 Target、处理跨阶段页面导航。</summary>
        private void OnGuideStepOpening (object? sender , GuideStepEventArgs e)
            => _onboarding.HandleStepOpening(e.Index , e.Step);

        protected override void OnPropertyChanged (AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == BoundsProperty && DataContext is MainShellViewModel vm)
                vm.OnWindowWidthChanged(Bounds.Width);
        }

        protected override async void OnClosing (WindowClosingEventArgs e)
        {
            // 已确认关闭：保存窗口状态并真正退出
            if (_isClosingConfirmed)
            {
                var state = new WindowStateSettings
                {
                    Left = Position.X ,
                    Top = Position.Y ,
                    Width = Width ,
                    Height = Height ,
                    IsMaximized = WindowState == WindowState.Maximized
                };

                SaveWindowStateBlocking(state);
                base.OnClosing(e);
                return;
            }

            // 首次关闭：拦截并检查当前页面是否有未保存的更改
            e.Cancel = true;

            try
            {
                var vm = _navigation.CurrentViewModel;
                if (vm != null && !await vm.CanLeaveAsync())
                    return; // 用户取消关闭
            }
            catch
            {
                // 检查失败时允许关闭
            }

            _isClosingConfirmed = true;
            Close();
        }

        /// <summary>同步保存窗口状态，确保在窗口关闭前完成 I/O 写入。</summary>
        /// <remarks>
        /// 不能使用 fire-and-forget 异步：Avalonia dispatcher 在窗口关闭后停止，
        /// 异步 continuation 无法回发到 UI 线程，导致写入丢失。
        /// 使用 Task.Run 将整个操作放到线程池，避免阻塞 UI 线程时发生死锁。
        /// </remarks>
        private static void SaveWindowStateBlocking (WindowStateSettings state)
        {
            try
            {
                if (global::Avalonia.Application.Current is not App appInstance) return;
                var facade = appInstance.ServiceProvider.GetRequiredService<IApplicationFacade>();
                Task.Run(async () =>
                {
                    var settings = await facade.LoadAppSettingsAsync();
                    settings.WindowState = state;
                    await facade.SaveAppSettingsAsync(settings);
                }).GetAwaiter().GetResult();
            }
            catch
            {
                // 关闭时保存失败不应阻止退出
            }
        }
    }
}
