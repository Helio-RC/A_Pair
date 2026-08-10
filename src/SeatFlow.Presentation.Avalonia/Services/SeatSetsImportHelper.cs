using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Models.SeatSets;
using SeatFlow.Presentation.Avalonia.Lang;
using SeatFlow.Presentation.Avalonia.Views;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AvaloniaApplication = Avalonia.Application;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// .seatsets 文件导入的共享逻辑。供 App.HandleSeatSetsFileOpenAsync、
/// SettingsViewModel 和 HomeViewModel 重用，避免三方重复代码。
/// 流程：校验 → 探测分类 → 分类选择弹窗 → 导入 → 显示结果 → 刷新应用状态。
/// </summary>
internal static class SeatSetsImportHelper
{
    /// <summary>
    /// 执行完整的 .seatsets 文件导入流程。
    /// </summary>
    /// <param name="filePath">.seatsets 文件路径。</param>
    /// <param name="serviceProvider">DI 服务提供者。</param>
    /// <param name="dialog">对话框服务。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>true 表示成功导入了至少一个文件；false 表示失败或用户取消。</returns>
    internal static async Task<bool> ImportAsync(
        string filePath,
        IServiceProvider serviceProvider,
        IDialogService dialog,
        ILogger? logger,
        CancellationToken ct)
    {
        var facade = serviceProvider.GetRequiredService<IApplicationFacade>();

        try
        {
            logger?.LogInformation("[SeatSets] 拖放导入文件: {Path}", filePath);

            // 1. 校验文件完整性
            var validation = await facade.ValidateSeatSetsAsync(filePath, ct);
            if (!validation.IsValid)
            {
                var errors = string.Join("\n", validation.ValidationErrors);
                await dialog.ShowErrorAsync(Resources.SeatSets_InvalidFile,
                    string.IsNullOrEmpty(errors)
                        ? Resources.SeatSets_IntegrityFailed
                        : errors);
                return false;
            }

            // 2. 探测可用分类
            var categories = await facade.ProbeSeatSetsCategoriesAsync(filePath, ct);

            // 3. 分类选择对话框
            var selectionWindow = new SeatSetsSelectionWindow { IsExport = false };
            selectionWindow.SetAvailableCategories(
                categories.IncludeAppSettings,
                categories.IncludeVenues,
                categories.IncludeRosters,
                categories.IncludeSnapshots,
                categories.IncludeStrategyConfig);

            if (AvaloniaApplication.Current?.ApplicationLifetime
                    is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is { } mainWindow)
            {
                var confirmed = await selectionWindow.ShowDialog<bool>(mainWindow);
                if (!confirmed) return false;
            }
            else
            {
                return false;
            }

            var selection = selectionWindow.ViewModel.ToSelection();

            // 4. 执行导入
            var result = await facade.ImportSeatSetsAsync(filePath, selection,
                progress: null, ct);

            // 5. 显示结果
            if (result.Success)
            {
                await dialog.ShowInfoAsync(Resources.SeatSets_ImportTitle,
                    string.Format(Resources.SeatSets_ImportSuccess, result.Restored));
            }
            else if (result.Failed > 0 && result.Restored > 0)
            {
                var errorDetails = result.Errors.Count > 0
                    ? "\n\n" + string.Join("\n", result.Errors.Take(5))
                    : "";
                await dialog.ShowWarningAsync(Resources.SeatSets_ImportTitle,
                    string.Format(Resources.SeatSets_ImportPartial,
                        result.Restored, result.TotalFiles, result.Failed) + errorDetails);
            }
            else
            {
                var errorDetails = result.Errors.Count > 0
                    ? "\n" + string.Join("\n", result.Errors.Take(10))
                    : "";
                await dialog.ShowErrorAsync(Resources.SeatSets_ImportTitle,
                    string.Join("\n", result.Errors.Take(10)) + errorDetails);
            }

            // 6. 导入后刷新应用状态（主题/语言/导航）
            if (result.Restored > 0)
            {
                try { await App.RefreshAfterImportAsync(serviceProvider); }
                catch { /* 刷新失败不影响导入结果 */ }
            }

            return result.Restored > 0;
        }
        catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
        {
            logger?.LogDebug(ex, "[SeatSets] 导入操作取消: {Path}", filePath);
            return false;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[SeatSets] 导入数据包失败: {Path}", filePath);
            await dialog.ShowErrorAsync(Resources.Common_OperationFailed, ex.Message);
            return false;
        }
    }
}
