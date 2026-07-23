using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Avalonia.Behaviors;

/// <summary>
/// 全局键盘快捷键处理行为。
/// 在 <see cref="App.axaml.cs"/> 中通过 <c>KeyboardShortcutHandler.Attach(mainWindow)</c> 注册到 MainWindow。
/// </summary>
internal static class KeyboardShortcutHandler
{
    /// <summary>保存命令的候选属性名（CommunityToolkit.Mvvm 从 [RelayCommand] 方法生成）。</summary>
    private static readonly string[] SaveCommandNames =
    [
        "SaveCommand" ,             // MemberManagementViewModel (SaveAsync)
        "SaveVenueCommand" ,        // VenueConfigurationViewModel (SaveVenue)
        "SaveLayoutCommand" ,       // FreeformManagementViewModel (SaveLayout)
        "SaveCurrentConfigCommand" ,// StrategyConfigurationViewModel (SaveCurrentConfigAsync)
        "SaveSettingsCommand" ,     // SettingsViewModel (SaveSettingsAsync)
        "SaveToSnapshotCommand"     // SeatingArrangementViewModel (SaveToSnapshotAsync)
    ];

    /// <summary>向指定窗口注册全局 KeyDown 处理（Tunnel 路由）。</summary>
    public static void Attach (Window window)
    {
        window.AddHandler(InputElement.KeyDownEvent , OnKeyDown , RoutingStrategies.Tunnel);
    }

    private static void OnKeyDown (object? sender , KeyEventArgs e)
    {
        if (sender is not Window window) return;
        if (window.DataContext is not MainShellViewModel shell) return;

        var currentVm = shell.CurrentViewModel;
        var modifiers = e.KeyModifiers;
        var isCtrl = modifiers.HasFlag(KeyModifiers.Control);

        switch (e.Key)
        {
            // ── Ctrl+Z: 撤销 ──
            case Key.Z when isCtrl && !IsTextInputFocused(window):
                if (currentVm is SeatingArrangementViewModel saVm)
                {
                    saVm.UndoCommand.Execute(null);
                    e.Handled = true;
                }
                break;

            // ── Ctrl+Y: 重做 ──
            case Key.Y when isCtrl && !IsTextInputFocused(window):
                if (currentVm is SeatingArrangementViewModel saVm2)
                {
                    saVm2.RedoCommand.Execute(null);
                    e.Handled = true;
                }
                break;

            // ── Ctrl+S: 保存当前数据 ──
            case Key.S when isCtrl && !IsTextInputFocused(window):
                HandleSave(currentVm);
                e.Handled = true;
                break;

            // ── Delete: 删除选中的已分配学生 ──
            case Key.Delete when !IsTextInputFocused(window):
                if (currentVm is SeatingArrangementViewModel saVm3)
                {
                    saVm3.RemoveToTrashCommand.Execute(null);
                    e.Handled = true;
                }
                break;

            // ── Esc: 取消选择 / 关闭弹窗 ──
            case Key.Escape:
                e.Handled = HandleEscape(shell , currentVm);
                break;
        }
    }

    /// <summary>焦点是否位于文本输入控件中（此时不应拦截编辑快捷键）。</summary>
    private static bool IsTextInputFocused (TopLevel topLevel)
    {
        var focused = topLevel.FocusManager?.GetFocusedElement();
        return focused is TextBox or AutoCompleteBox;
    }

    /// <summary>向当前页面的保存命令分派 Ctrl+S。</summary>
    private static void HandleSave (ViewModelBase? vm)
    {
        if (vm == null) return;

        var type = vm.GetType();
        foreach (var name in SaveCommandNames)
        {
            if (type.GetProperty(name)?.GetValue(vm) is IRelayCommand cmd
                && cmd.CanExecute(null))
            {
                cmd.Execute(null);
                return;
            }
        }
    }

    /// <summary>处理 Esc 键：取消交换模式、取消选择。返回 <c>true</c> 表示事件已消费。</summary>
    private static bool HandleEscape (MainShellViewModel shell , ViewModelBase? currentVm)
    {
        // 引导弹窗激活时，让 Guide 控件自行处理 Esc 关闭
        if (shell.IsOnboardingActive)
            return false;

        if (currentVm is SeatingArrangementViewModel saVm)
        {
            // 优先取消交换模式
            if (saVm.IsSwapMode)
            {
                saVm.CancelSwapCommand.Execute(null);
                return true;
            }

            // 取消未分配学生选中
            if (saVm.SelectedUnassignedStudent != null)
            {
                saVm.SelectedUnassignedStudent = null;
                return true;
            }
        }

        return false;
    }
}
