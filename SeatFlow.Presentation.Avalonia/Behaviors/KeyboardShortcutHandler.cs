using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Avalonia.Behaviors;

/// <summary>
/// 全局键盘快捷键处理行为。
/// 在 <see cref="App.axaml.cs"/> 中通过 <c>KeyboardShortcutHandler.Attach(mainWindow)</c> 注册到 MainWindow。
/// 快捷键开关通过 <see cref="ShortcutConfig"/> 控制，由设置页面写入。
/// </summary>
internal static class KeyboardShortcutHandler
{
    /// <summary>快捷键开关配置，默认为全部启用。由 <see cref="SettingsViewModel"/> 加载/保存时更新。</summary>
    internal static KeyboardShortcutConfig ShortcutConfig { get; set; } = new();

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

        var cfg = ShortcutConfig;
        var currentVm = shell.CurrentViewModel;
        var modifiers = e.KeyModifiers;
        var isCtrl = modifiers.HasFlag(KeyModifiers.Control);
        var isTextFocused = IsTextInputFocused(window);

        switch (e.Key)
        {
            // ── Ctrl+Z: 撤销 ──
            case Key.Z when isCtrl && cfg.UndoEnabled && !isTextFocused:
                if (currentVm is SeatingArrangementViewModel saVm)
                {
                    saVm.UndoCommand.Execute(null);
                    e.Handled = true;
                }
                break;

            // ── Ctrl+Y: 重做 ──
            case Key.Y when isCtrl && cfg.RedoEnabled && !isTextFocused:
                if (currentVm is SeatingArrangementViewModel saVm2)
                {
                    saVm2.RedoCommand.Execute(null);
                    e.Handled = true;
                }
                break;

            // ── Ctrl+S: 保存当前数据 ──
            case Key.S when isCtrl && cfg.SaveEnabled && !isTextFocused:
                HandleSave(currentVm);
                e.Handled = true;
                break;

            // ── Delete: 删除选中的已分配学生 ──
            case Key.Delete when cfg.DeleteEnabled && !isTextFocused:
                if (currentVm is SeatingArrangementViewModel saVm3)
                {
                    saVm3.RemoveToTrashCommand.Execute(null);
                    e.Handled = true;
                }
                break;

            // ── Esc: 取消选择 / 关闭弹窗 ──
            case Key.Escape:
                e.Handled = cfg.EscapeEnabled && HandleEscape(shell , currentVm);
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
