using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SeatFlow.Core.Utilities;
using SeatFlow.Presentation.Avalonia.Lang;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// UI 线程看门狗：定期检查 UI 线程心跳，若超过阈值无响应则记录诊断信息并强制退出。
/// </summary>
public sealed class WatchdogService (int timeoutSeconds = 45 , ILogger<WatchdogService>? logger = null) : IDisposable
{
    private readonly int _timeoutSeconds = timeoutSeconds;
    private readonly CancellationTokenSource _cts = new();
    private long _heartbeatTicks = DateTime.UtcNow.Ticks;
    private Task? _watchTask;
    private readonly ILogger<WatchdogService> _logger = logger ?? NullLogger<WatchdogService>.Instance;
    private static IDialogService? _dialog;

    public static void SetDialogService (IDialogService dialog) => _dialog = dialog;

    /// <summary>启动看门狗（后台线程）。</summary>
    public void Start ()
    {
        _logger.LogInformation("看门狗已启动（超时阈值 {Timeout}s）" , _timeoutSeconds);
        _watchTask = Task.Run(WatchLoop);
    }

    public void Ping ()
    {
        _heartbeatTicks = DateTime.UtcNow.Ticks;
    }

    private async Task WatchLoop ()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000 , _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var elapsed = DateTime.UtcNow.Ticks - Interlocked.Read(ref _heartbeatTicks);
            if (new TimeSpan(elapsed).TotalSeconds >= _timeoutSeconds)
            {
                _logger.LogCritical("UI 线程超过 {Timeout}s 无响应，触发诊断转储" , _timeoutSeconds);
                await DumpAndExit(_timeoutSeconds);
            }
        }
    }

    private static async Task DumpAndExit (int timeoutSeconds = 45)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var logPath = Path.Combine(AppEnvironment.ExeDirectory , $"err_{timestamp}.log");

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== UI 线程卡死诊断 ===");
            sb.AppendLine($"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"超时阈值: 45 秒");
            sb.AppendLine();

            // 进程信息
            using var proc = Process.GetCurrentProcess();
            sb.AppendLine("--- 进程信息 ---");
            sb.AppendLine($"进程名: {proc.ProcessName}");
            sb.AppendLine($"PID: {proc.Id}");
            sb.AppendLine($"工作集: {proc.WorkingSet64 / 1024 / 1024} MB");
            sb.AppendLine($"线程数: {proc.Threads.Count}");
            sb.AppendLine($"句柄数: {proc.HandleCount}");
            sb.AppendLine($"启动时间: {proc.StartTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"总 CPU 时间: {proc.TotalProcessorTime}");
            sb.AppendLine();

            // 所有线程堆栈
            sb.AppendLine("--- 线程堆栈 ---");
            foreach (ProcessThread pt in proc.Threads)
            {
                try
                {
                    sb.AppendLine($"线程 ID={pt.Id}, 状态={pt.ThreadState}, CPU时间={pt.TotalProcessorTime}");
                }
                catch { /* 某些线程信息不可读 */ }
            }
            sb.AppendLine();

            // 托管线程堆栈（仅本进程可见的部分）
            sb.AppendLine("--- 托管线程堆栈 ---");
            await Task.Run(() =>
            {
                // 使用 Environment.StackTrace 从当前（看门狗）线程的角度记录
                sb.AppendLine("看门狗线程堆栈:");
                sb.AppendLine(Environment.StackTrace);
                sb.AppendLine();

                // 尝试捕获所有活跃线程的堆栈快照
                sb.AppendLine("所有活跃线程快照:");
                try
                {
                    // 通过 MiniDump 或直接枚举线程来获取信息
                    ThreadPool.GetAvailableThreads(out var worker , out var completion);
                    sb.AppendLine($"线程池可用: Worker={worker}, Completion={completion}");
                    ThreadPool.GetMaxThreads(out var maxWorker , out var maxCompletion);
                    sb.AppendLine($"线程池上限: Worker={maxWorker}, Completion={maxCompletion}");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"无法获取线程池信息: {ex.Message}");
                }
            }).ConfigureAwait(false);

            sb.AppendLine();
            sb.AppendLine("--- 诊断结束 ---");

            await File.WriteAllTextAsync(logPath , sb.ToString()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 最后的兜底：尝试写入最简日志
            try { File.WriteAllText(logPath , $"看门狗诊断失败: {ex.Message}"); } catch { }
        }
        finally
        {
            // 尝试弹窗通知用户
            try
            {
                var msg = string.Format(Resources.Watchdog_MessageFmt , timeoutSeconds , logPath);
                _dialog?.ShowErrorAsync(Resources.Watchdog_Title , msg).Wait(3000);
            }
            catch { /* UI 可能已死锁，弹窗无法显示 */ }

            Environment.Exit(1);
        }
    }

    public void Dispose ()
    {
        _cts.Cancel();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
