using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SeatFlow.Presentation.Avalonia.Lang;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using FluentIcons.Common;
using Microsoft.Extensions.DependencyInjection;
using AvaloniaApp = Avalonia.Application;

namespace SeatFlow.Presentation.Avalonia.Behaviors;

/// <summary>
/// 全局文件拖放导入行为。注册在 MainWindow 上，拦截 OS 文件拖放事件，
/// 根据当前页面的 ViewModel 是否实现 <see cref="IFileDropHandler"/> 来路由处理。
/// 拖入文件时显示遮罩覆盖层（支持/不支持两种状态）。
/// </summary>
internal static class FileDropHandler
{
    private static Window? _window;

    public static void Attach(Window window)
    {
        _window = window;
        DragDrop.AddDragOverHandler(window, OnDragOver);
        DragDrop.AddDropHandler(window, OnDrop);
        DragDrop.AddDragEnterHandler(window, OnDragEnter);
        DragDrop.AddDragLeaveHandler(window, OnDragLeave);
    }

    private static ViewModelBase? ResolveCurrentViewModel()
    {
        if (AvaloniaApp.Current is not App app)
            return null;
        var nav = app.ServiceProvider.GetRequiredService<INavigationService>();
        return nav.CurrentViewModel;
    }

    private static IDialogService? GetDialogService()
    {
        if (AvaloniaApp.Current is App app)
            return app.ServiceProvider.GetService<IDialogService>();
        return null;
    }

    private static string[]? GetDroppedFilePaths(DragEventArgs e)
    {
        if (!e.DataTransfer.Formats.Contains(DataFormat.File))
            return null;

        var files = e.DataTransfer.TryGetFiles();
        if (files is null)
            return null;

        return files.Select(f => f.Path.LocalPath).ToArray();
    }

    /// <summary>判断当前拖入的文件是否被当前页面接受。</summary>
    private static bool IsAcceptedByCurrentPage(DragEventArgs e)
    {
        var vm = ResolveCurrentViewModel();
        if (vm is not IFileDropHandler handler)
            return false;

        if (!e.DataTransfer.Formats.Contains(DataFormat.File))
            return false;

        var filePaths = GetDroppedFilePaths(e);
        if (filePaths is null || filePaths.Length == 0 || filePaths.Length > 1)
            return false;

        var ext = Path.GetExtension(filePaths[0])?.ToLowerInvariant();
        return ext is not null && handler.AcceptedFileExtensions.Contains(ext);
    }

    /// <summary>显示遮罩覆盖层并根据页面对文件的接受情况设置图标、文字和边框颜色。</summary>
    private static void SetOverlayState(bool accepted)
    {
        if (_window is null) return;

        var overlay = _window.FindControl<Border>("FileDropOverlay");
        if (overlay is null) return;

        var icon = _window.FindControl<FluentIcons.Avalonia.FluentIcon>("FileDropOverlayIcon");
        var text = _window.FindControl<TextBlock>("FileDropOverlayText");
        var card = _window.FindControl<Border>("FileDropOverlayCard");

        if (accepted)
        {
            // 支持状态：下载图标 + 主题色边框 + "释放文件以导入"
            if (icon is not null)
            {
                icon.Icon = Icon.ArrowDownload;
                icon.Foreground = _window.FindResource("SystemAccentColor") as IBrush;
            }
            if (text is not null)
                text.Text = Resources.DragDrop_DropHint;
            if (card is not null)
                card.BorderBrush = _window.FindResource("SystemAccentColor") as IBrush;
        }
        else
        {
            // 不支持状态：禁止图标 + 错误色边框 + "该页面不支持此文件类型"
            if (icon is not null)
            {
                icon.Icon = Icon.Dismiss;
                icon.Foreground = _window.FindResource("ErrorBrush") as IBrush;
            }
            if (text is not null)
                text.Text = Resources.DragDrop_UnsupportedDropHint;
            if (card is not null)
                card.BorderBrush = _window.FindResource("ErrorBrush") as IBrush;
        }

        overlay.IsVisible = true;
    }

    private static void HideOverlay()
    {
        if (_window is null) return;
        var overlay = _window.FindControl<Border>("FileDropOverlay");
        if (overlay is not null)
            overlay.IsVisible = false;
    }

    private static void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Formats.Contains(DataFormat.File))
            return;

        SetOverlayState(IsAcceptedByCurrentPage(e));
    }

    private static void OnDragLeave(object? sender, DragEventArgs e)
    {
        HideOverlay();
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        var vm = ResolveCurrentViewModel();
        if (vm is not IFileDropHandler handler)
        {
            if (e.DataTransfer.Formats.Contains(DataFormat.File))
                e.DragEffects = DragDropEffects.None;
            return;
        }

        var filePaths = GetDroppedFilePaths(e);
        if (filePaths is null || filePaths.Length == 0)
            return;

        if (filePaths.Length > 1)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var ext = Path.GetExtension(filePaths[0])?.ToLowerInvariant();
        if (ext is not null && handler.AcceptedFileExtensions.Contains(ext))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private static async void OnDrop(object? sender, DragEventArgs e)
    {
        HideOverlay();

        try
        {
            var filePaths = GetDroppedFilePaths(e);
            if (filePaths is null || filePaths.Length == 0)
                return;

            var vm = ResolveCurrentViewModel();

            if (filePaths.Length > 1)
            {
                var dialog = GetDialogService();
                if (dialog is null) return;
                var confirmed = await dialog.ShowConfirmAsync(
                    Resources.DragDrop_MultipleFilesTitle,
                    string.Format(Resources.DragDrop_MultipleFilesMsg, filePaths.Length));
                if (!confirmed) return;
            }

            var filePath = filePaths[0];
            var ext = Path.GetExtension(filePath)?.ToLowerInvariant();

            if (vm is IFileDropHandler handler)
            {
                if (ext is null || !handler.AcceptedFileExtensions.Contains(ext))
                {
                    var dialog = GetDialogService();
                    if (dialog is not null)
                    {
                        await dialog.ShowWarningAsync(
                            Resources.DragDrop_InvalidFileType,
                            string.Format(Resources.DragDrop_InvalidFileTypeFmt,
                                Path.GetFileName(filePath),
                                string.Join(", ", handler.AcceptedFileExtensions)));
                    }
                    return;
                }

                await handler.HandleFileDropAsync([filePath], CancellationToken.None);
            }
            else
            {
                var dialog = GetDialogService();
                if (dialog is not null)
                {
                    await dialog.ShowInfoAsync(
                        Resources.DragDrop_UnsupportedPage,
                        Resources.DragDrop_UnsupportedPageMsg);
                }
            }
        }
        catch (Exception ex)
        {
            try
            {
                var dialog = GetDialogService();
                dialog?.ShowErrorAsync(Resources.Common_OperationFailed, ex.Message);
            }
            catch { /* 弹窗不可用时静默处理 */ }
        }
    }
}
