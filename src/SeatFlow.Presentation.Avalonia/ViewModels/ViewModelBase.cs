using System;
using System.Threading;
using System.Threading.Tasks;
using SeatFlow.Presentation.Avalonia.Lang;
using SeatFlow.Presentation.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    private static ILogger? _logger;

    /// <summary>全局对话框服务，由 App 启动时注入。</summary>
    internal static IDialogService Dialog { get; private set; } = default!;

    /// <summary>由 DI 在应用启动时调用一次。</summary>
    public static void InitializeDialogService (IDialogService dialog)
    {
        Dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
    }

    /// <summary>由 DI 在应用启动时调用一次，为所有 ViewModel 提供日志记录。</summary>
    public static void InitializeLogger (ILogger<ViewModelBase> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>在 try-catch 中执行操作，出错时弹窗并记录日志。</summary>
    protected static async Task<bool> SafeExecuteAsync (Func<Task> action , string? errorTitle = null)
    {
        errorTitle ??= Resources.Common_OperationFailed;
        try
        {
            await action();
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex , "ViewModel 操作失败：{Title}" , errorTitle);
            await Dialog.ShowErrorAsync(errorTitle , ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 在带超时的 try-catch 中执行操作。超时后自动取消 CancellationToken 并弹窗提示，不会终止程序。
    /// </summary>
    /// <param name="action">接受 CancellationToken 的异步操作，超时后 token 会被取消</param>
    /// <param name="timeout">超时阈值，应小于 UI 看门狗的 45 秒</param>
    /// <param name="errorTitle">错误弹窗标题</param>
    protected static async Task<bool> SafeExecuteAsync (Func<CancellationToken , Task> action , TimeSpan timeout , string? errorTitle = null)
    {
        errorTitle ??= Resources.Common_OperationFailed;
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await action(cts.Token);
            return true;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _logger?.LogWarning("操作超时：{Title}（{Seconds} 秒）" , errorTitle , timeout.TotalSeconds);
            await Dialog.ShowErrorAsync(Resources.Common_OperationTimeout ,
                string.Format(Resources.Common_TimeoutFormat , errorTitle , timeout.TotalSeconds));
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex , "ViewModel 操作失败：{Title}" , errorTitle);
            await Dialog.ShowErrorAsync(errorTitle , ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 导航离开前调用。子类可重写以询问用户是否保存未提交的更改。
    /// 返回 true 表示允许离开，false 表示取消导航。
    /// </summary>
    public virtual Task<bool> CanLeaveAsync () => Task.FromResult(true);
}
