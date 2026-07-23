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
using Microsoft.Extensions.DependencyInjection;
using AvaloniaApp = Avalonia.Application;

namespace SeatFlow.Presentation.Avalonia.Behaviors;

/// <summary>
/// 全局文件拖放导入行为。注册在 MainWindow 上，拦截 OS 文件拖放事件，
/// 根据当前页面的 ViewModel 是否实现 <see cref="IFileDropHandler"/> 来路由处理。
/// </summary>
internal static class FileDropHandler
{
    public static void Attach(Window window)
    {
        DragDrop.AddDragOverHandler(window, OnDragOver);
        DragDrop.AddDropHandler(window, OnDrop);
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

    /// <summary>从 DataTransfer 中提取拖放文件路径。</summary>
    private static string[]? GetDroppedFilePaths(DragEventArgs e)
    {
        // 使用 Avalonia 标准 API：DataFormat.File + TryGetFiles()
        if (!e.DataTransfer.Formats.Contains(DataFormat.File))
            return null;

        var files = e.DataTransfer.TryGetFiles();
        if (files is null)
            return null;

        return files.Select(f => f.Path.LocalPath).ToArray();
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        var vm = ResolveCurrentViewModel();
        if (vm is not IFileDropHandler handler)
        {
            // 非拖放处理页面：有文件时显示禁止光标
            if (e.DataTransfer.Formats.Contains(DataFormat.File))
                e.DragEffects = DragDropEffects.None;
            return;
        }

        var filePaths = GetDroppedFilePaths(e);
        if (filePaths is null || filePaths.Length == 0)
            return;

        // 只接受单个文件拖放
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
        try
        {
            var filePaths = GetDroppedFilePaths(e);
            if (filePaths is null || filePaths.Length == 0)
                return;

            var vm = ResolveCurrentViewModel();

            // 多文件：警告并只处理第一个
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
                // 扩展名不匹配
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
            // async void 事件处理器中未捕获异常会被静默吞掉，显示错误弹窗
            try
            {
                var dialog = GetDialogService();
                dialog?.ShowErrorAsync(Resources.Common_OperationFailed, ex.Message);
            }
            catch { /* 弹窗不可用时静默处理 */ }
        }
    }
}
