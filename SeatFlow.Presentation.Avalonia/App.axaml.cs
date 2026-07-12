using System;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Core.Providers;
using SeatFlow.Core.Telemetry;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.Telemetry;
using SeatFlow.Presentation.Avalonia.ViewModels;
using SeatFlow.Presentation.Avalonia.Views;
using Avalonia;
using Avalonia.Controls;
using Serilog;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AvaloniaApplication = Avalonia.Application;

namespace SeatFlow.Presentation.Avalonia
{
    // AVLN3001: DI requires parameterized constructor, no public parameterless ctor
#pragma warning disable AVLN3001
    public partial class App (IServiceProvider serviceProvider , bool isFirstInstance = true) : AvaloniaApplication
    {
        private readonly IServiceProvider _serviceProvider = serviceProvider;
        private readonly bool _isFirstInstance = isFirstInstance;

        /// <summary>命令行传入的 .seatsets 文件路径（双击打开或命令行导入）。</summary>
        internal static string? PendingSeatSetsFilePath { get; set; }

        /// <summary>在 AppData 创建前自动扫描到的 .seatsets 文件路径（首次启动数据恢复）。</summary>
        internal static string? AutoImportSeatSetsPath { get; set; }

        internal IServiceProvider ServiceProvider => _serviceProvider;

        public override void Initialize ()
        {
            ApplyLanguageFromSettings();
            AvaloniaXamlLoader.Load(this);
        }

        private async Task RestoreSettingsAsync ()
        {
            try
            {
                var facade = _serviceProvider.GetRequiredService<IApplicationFacade>();
                var settings = await facade.LoadAppSettingsAsync();
                ApplyTheme(settings.Theme);
                ApplyLanguage(settings.Language);

                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    && desktop.MainWindow is { } window)
                {
                    var ws = settings.WindowState;
                    // 先设置最大化状态，避免先设尺寸再最大化导致的闪烁
                    if (ws.IsMaximized)
                        window.WindowState = WindowState.Maximized;

                    // 非最大化时才恢复尺寸（最大化状态下尺寸由系统管理）
                    if (!ws.IsMaximized && ws.Width > 0 && ws.Height > 0)
                    {
                        window.Width = ws.Width;
                        window.Height = ws.Height;
                    }

                    // 始终恢复窗口位置，包括 (0,0)（它是合法的屏幕坐标）
                    window.Position = new PixelPoint((int)ws.Left , (int)ws.Top);
                }

                // 仅在配置文件不存在时创建默认文件，防止覆盖已有设置
                var repo = _serviceProvider.GetRequiredService<Core.Providers.IAppSettingsRepository>();
                if (repo is Infrastructure.Providers.JsonAppSettingsRepository jsonRepo
                    && !File.Exists(jsonRepo.SettingsFilePath))
                {
                    await facade.SaveAppSettingsAsync(settings);
                }

                var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
                logger.LogInformation("窗口设置已恢复");
            }
            catch (Exception ex)
            {
                var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
                logger.LogWarning(ex, "恢复窗口设置失败");
                try
                {
                    _serviceProvider.GetRequiredService<ITelemetryService>()
                        .RecordError("startup", $"RestoreSettings: {ex.Message}");
                }
                catch { /* 遥测可能未启用 */ }
            }
        }

        private void ApplyLanguageFromSettings ()
        {
            try
            {
                var facade = _serviceProvider.GetRequiredService<IApplicationFacade>();
                // Task.Run 跳到线程池（无 SynchronizationContext），避免在 UI 调度器未就绪时死锁
                var settings = Task.Run(() => facade.LoadAppSettingsAsync(CancellationToken.None)).GetAwaiter().GetResult();
                ApplyLanguage(settings.Language);
            }
            catch (Exception ex)
            {
                var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
                logger.LogWarning(ex, "应用语言设置失败");
            }
        }

        private static void ApplyLanguage (string language)
        {
            try
            {
                var culture = string.IsNullOrEmpty(language)
                    ? CultureInfo.InstalledUICulture
                    : new CultureInfo(language);

                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                Lang.Resources.Culture = culture;
            }
            catch (CultureNotFoundException)
            {
                // 无效的语言代码，保持系统默认
            }
        }

        private void ApplyTheme (ThemeMode mode)
        {
            RequestedThemeVariant = mode switch
            {
                ThemeMode.Light => ThemeVariant.Light,
                ThemeMode.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
        }

        public override void OnFrameworkInitializationCompleted ()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainShell = _serviceProvider.GetRequiredService<MainShellViewModel>();
                var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
                mainWindow.DataContext = mainShell;
                desktop.MainWindow = mainWindow;

                _serviceProvider.GetRequiredService<IFileService>().SetTopLevel(mainWindow);
                _serviceProvider.GetRequiredService<IDialogService>().SetTopLevel(mainWindow);

                ViewModelBase.InitializeDialogService(_serviceProvider.GetRequiredService<IDialogService>());
                ViewModelBase.InitializeLogger(_serviceProvider.GetRequiredService<ILogger<ViewModelBase>>());

                // 启动检查
                _ = RunStartupChecksAsync(desktop);

                // 启动看门狗，防止 UI 卡死无法退出
                WatchdogService.SetDialogService(_serviceProvider.GetRequiredService<IDialogService>());
                var watchdog = _serviceProvider.GetRequiredService<WatchdogService>();
                watchdog.Start();
                var pingTimer = new global::Avalonia.Threading.DispatcherTimer(
                    TimeSpan.FromSeconds(3) ,
                    global::Avalonia.Threading.DispatcherPriority.Background ,
                    (_ , _) => watchdog.Ping());
                pingTimer.Start();

                // 全角字符输入转换（全角数字/符号 → 半角）
                Behaviors.ChineseInputNormalizer.Attach(mainWindow);

                // 退出看门狗：关闭信号发出后 20s 内未退出则强制终止
                desktop.ShutdownRequested += (_ , _) =>
                {
                    // 记录应用退出遥测（fire-and-forget，不阻塞退出）
                    try
                    {
                        var telemetry = _serviceProvider.GetRequiredService<ITelemetryService>();
                        telemetry.RecordEvent(TelemetryEventTypes.AppExit);
                        // 后台刷新，不等待结果，避免阻塞退出
                        _ = Task.Run(() => telemetry.FlushAsync(TimeSpan.FromSeconds(2)));
                    }
                    catch { /* 遥测退出失败静默处理 */ }

                    // 手动关闭 Serilog（因为 DI 注册使用了 dispose: false 避免竞态）
                    Log.CloseAndFlush();

                    var exitLogger = _serviceProvider.GetRequiredService<ILogger<App>>();
                    Task.Run(async () =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(20));
                        exitLogger.LogCritical("程序退出超时（20s），强制终止进程");
                        Environment.Exit(0);
                    });
                };

                // 启动命名管道服务器：接收第二个进程转发的 .seatsets 文件路径
                StartSeatSetsPipeServer();

                // 按序执行：先检查自动导入 → 引导 → 恢复设置
                _ = InitializeAsync(mainWindow);

                // 处理双击 .seatsets 文件（延迟到 UI 就绪后执行）
                HandlePendingSeatSetsFile();
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>
        /// 后台监听命名管道，接收第二个进程转发的 .seatsets 文件路径。
        /// 收到有效路径后通过 Dispatcher 排程到 UI 线程处理。
        /// </summary>
        private void StartSeatSetsPipeServer ()
        {
            _ = Task.Run(async () =>
            {
                var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
                while (true)
                {
                    try
                    {
                        using var server = new System.IO.Pipes.NamedPipeServerStream(
                            Program.SeatSetsPipeName , PipeDirection.In , 1);
                        await server.WaitForConnectionAsync();
                        using var reader = new StreamReader(server);
                        var path = await reader.ReadLineAsync();

                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        {
                            logger.LogInformation("[SeatSets] 管道收到文件路径: {Path}" , path);
                            Dispatcher.UIThread.Post(() =>
                            {
                                PendingSeatSetsFilePath = path;
                                HandlePendingSeatSetsFile();
                            } , DispatcherPriority.Background);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex , "[SeatSets] 管道服务器异常，1s 后重试");
                        await Task.Delay(1000);
                    }
                }
            });
        }

        /// <summary>
        /// 处理待导入的 .seatsets 文件（来自命令行参数、双击打开或管道转发）。
        /// 延迟到 Background 优先级执行，确保 UI 已完全初始化。
        /// </summary>
        private void HandlePendingSeatSetsFile ()
        {
            var filePath = PendingSeatSetsFilePath;
            if (string.IsNullOrEmpty(filePath))
                return;

            // 清理静态状态，防止重复处理
            PendingSeatSetsFilePath = null;

            Dispatcher.UIThread.Post(async () =>
            {
                await HandleSeatSetsFileOpenAsync(filePath);
            }, DispatcherPriority.Background);
        }

        private async Task RunStartupChecksAsync (IClassicDesktopStyleApplicationLifetime desktop)
        {
            var dialog = _serviceProvider.GetRequiredService<IDialogService>();

            // 单实例检查
            if (!_isFirstInstance)
            {
                await DialogServiceShim.ShowWarningAsync(dialog ,
                    Lang.Resources.App_AlreadyRunning ,
                    Lang.Resources.App_AlreadyRunningMessage);
                desktop.Shutdown();
                return;
            }

            // 运行环境检查
            var settingsRepo = _serviceProvider.GetRequiredService<IAppSettingsRepository>();
            AppSettings settings;
            try
            {
                settings = await settingsRepo.LoadAsync();
            }
            catch
            {
                settings = new AppSettings();
            }

            if (!settings.SuppressEnvironmentWarning)
            {
                var (hasWarning , envMessage) = StartupGuard.CheckEnvironment();
                if (hasWarning)
                {
                    var result = await DialogServiceShim.ShowEnvironmentWarningAsync(dialog , envMessage);
                    if (result is 1) // "不再提醒" / "Don't remind again" 按钮
                    {
                        settings.SuppressEnvironmentWarning = true;
                        try
                        {
                            await settingsRepo.SaveAsync(settings);
                        }
                        catch
                        {
                            // 保存失败忽略
                        }
                    }
                }
            }
        }

        private async Task<bool> DetectAndMarkFirstLaunchAsync()
        {
            try
            {
                var repo = _serviceProvider.GetRequiredService<IAppSettingsRepository>();
                var isTrueFirstLaunch = repo is Infrastructure.Providers.JsonAppSettingsRepository jsonRepo
                    && !File.Exists(jsonRepo.SettingsFilePath);

                var facade = _serviceProvider.GetRequiredService<IApplicationFacade>();
                var settings = await facade.LoadAppSettingsAsync();

                if (isTrueFirstLaunch || settings.IsFirstLaunch)
                {
                    // 立即标记完成，防止崩溃导致反复触发
                    settings.IsFirstLaunch = false;
                    await facade.SaveAppSettingsAsync(settings);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
                logger.LogError(ex, "[Onboarding] DetectAndMarkFirstLaunchAsync 异常");
                return false;
            }
        }

        private void StartOnboardingDeferred()
        {
            try
            {
                var onboarding = _serviceProvider.GetRequiredService<IOnboardingService>();
                Dispatcher.UIThread.Post(() =>
                {
                    onboarding.StartOnboarding();
                }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
                logger.LogError(ex, "[Onboarding] StartOnboardingDeferred 异常");
            }
        }

        private async Task InitializeAsync (MainWindow mainWindow)
        {
            // 在 AppData 创建前先检查自动导入 .seatsets（仅在 AppData 不存在时生效）
            await CheckSeatSetsAutoImportAsync();

            await RestoreSettingsAsync();

            // 1. 检测首次启动（在任何遥测保存操作之前，确保 isTrueFirstLaunch 不受后续文件创建影响）
            var needsOnboarding = await DetectAndMarkFirstLaunchAsync();

            // 2. 遥测同意弹窗（在引导之前，避免引导 Popup 覆盖）
            await ShowTelemetryConsentIfNeededAsync();

            // 3. 启动引导（在遥测弹窗之后）
            if (needsOnboarding)
                StartOnboardingDeferred();

            // 记录应用启动遥测
            RecordAppStartTelemetry();
        }

        /// <summary>
        /// 在 AppData 目录尚未包含用户数据时（首次启动），
        /// 自动导入 exe 目录中预先放置的 .seatsets 文件。
        /// 文件路径在 DI 初始化前由 Program.DiscoverAutoImportSeatSetsFile 完成扫描，
        /// 避免了 AddSeatFlowApplication 创建 AppData/Logs 导致的误判。
        /// </summary>
        private async Task CheckSeatSetsAutoImportAsync ()
        {
            var seatsetsPath = AutoImportSeatSetsPath;
            if (string.IsNullOrEmpty(seatsetsPath))
                return;

            try
            {
                var facade = _serviceProvider.GetRequiredService<IApplicationFacade>();
                var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
                logger.LogInformation("[SeatSets] 自动发现数据包: {Path}，准备导入...", seatsetsPath);

                // 校验文件
                var validation = await facade.ValidateSeatSetsAsync(seatsetsPath, CancellationToken.None);
                if (!validation.IsValid)
                {
                    logger.LogWarning("[SeatSets] 自动发现的数据包校验失败: {Errors}",
                        string.Join("; ", validation.ValidationErrors));
                    return;
                }

                // 全量导入
                var selection = new SeatFlow.Core.Models.SeatSets.SeatSetsExportSelection
                {
                    IncludeAppSettings = true,
                    IncludeVenues = true,
                    IncludeRosters = true,
                    IncludeSnapshots = true,
                    IncludeStrategyConfig = true
                };

                var result = await facade.ImportSeatSetsAsync(seatsetsPath, selection,
                    progress: null, CancellationToken.None);

                if (result.Success)
                {
                    logger.LogInformation("[SeatSets] 自动导入成功: {Restored} 个文件", result.Restored);
                }
                else
                {
                    logger.LogWarning("[SeatSets] 自动导入部分完成: {Restored}/{Total} 成功, {Failed} 失败",
                        result.Restored, result.TotalFiles, result.Failed);
                    if (result.Errors.Count > 0)
                        logger.LogWarning("[SeatSets] 导入错误: {Errors}",
                            string.Join("; ", result.Errors.Take(5)));
                }
            }
            catch (Exception ex)
            {
                var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
                logger.LogError(ex, "[SeatSets] 自动导入异常");
            }
        }

        /// <summary>
        /// 首次启动时弹出遥测同意弹窗。仅在 ConsentShown==false 时触发。
        /// </summary>
        private async Task ShowTelemetryConsentIfNeededAsync()
        {
            try
            {
                var facade = _serviceProvider.GetRequiredService<IApplicationFacade>();
                var settings = await facade.LoadAppSettingsAsync();

                // 已经展示过同意弹窗，跳过
                if (settings.Telemetry.ConsentShown)
                    return;

                var dialog = _serviceProvider.GetRequiredService<IDialogService>();
                var telemetry = _serviceProvider.GetRequiredService<ITelemetryService>();

                var result = await dialog.ShowMultiOptionAsync(
                    Lang.Resources.Telemetry_ConsentTitle,
                    Lang.Resources.Telemetry_ConsentMessage,
                    Lang.Resources.Telemetry_ConsentEnable,
                    Lang.Resources.Telemetry_ConsentLater,
                    cancelText: "");

                // 用户做出选择后再持久化
                settings.Telemetry.ConsentShown = true;

                if (result == 0) // "开启"
                {
                    settings.Telemetry.Enabled = true;
                    telemetry.SetEnabled(true);
                }

                // 保存 ConsetShown + 可能 TelemetryEnabled
                await facade.SaveAppSettingsAsync(settings);
            }
            catch (Exception ex)
            {
                var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
                logger.LogDebug(ex, "遥测同意弹窗异常");
            }
        }

        /// <summary>
        /// 记录应用启动遥测事件（仅在遥测启用时）。
        /// </summary>
        private void RecordAppStartTelemetry()
        {
            try
            {
                var telemetry = _serviceProvider.GetRequiredService<ITelemetryService>();
                if (telemetry.IsEnabled)
                {
                    telemetry.RecordAppLaunch();
                }
            }
            catch { /* 遥测启动失败静默处理 */ }
        }

        /// <summary>
        /// 处理双击打开或命令行传入的 .seatsets 文件。
        /// 显示选择对话框让用户确认导入类别。
        /// </summary>
        private async Task HandleSeatSetsFileOpenAsync (string filePath)
        {
            var dialog = _serviceProvider.GetRequiredService<IDialogService>();
            var facade = _serviceProvider.GetRequiredService<IApplicationFacade>();
            var logger = _serviceProvider.GetRequiredService<ILogger<App>>();

            try
            {
                logger.LogInformation("[SeatSets] 处理打开的文件: {Path}", filePath);

                // 校验文件
                var validation = await facade.ValidateSeatSetsAsync(filePath, CancellationToken.None);
                if (!validation.IsValid)
                {
                    var errors = string.Join("\n", validation.ValidationErrors);
                    await DialogServiceShim.ShowWarningAsync(dialog,
                        Lang.Resources.SeatSets_InvalidFile,
                        string.IsNullOrEmpty(errors)
                            ? Lang.Resources.SeatSets_IntegrityFailed
                            : errors);
                    return;
                }

                // 探测并显示选择对话框
                var categories = await facade.ProbeSeatSetsCategoriesAsync(filePath, CancellationToken.None);
                var selectionWindow = new Views.SeatSetsSelectionWindow
                {
                    IsExport = false
                };
                selectionWindow.SetAvailableCategories(
                    categories.IncludeAppSettings,
                    categories.IncludeVenues,
                    categories.IncludeRosters,
                    categories.IncludeSnapshots,
                    categories.IncludeStrategyConfig);

                // 获取 MainWindow 用于 ShowDialog
                var mainWindow = (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                if (mainWindow == null) return;

                var confirmed = await selectionWindow.ShowDialog<bool>(mainWindow);
                if (!confirmed) return;

                var selection = selectionWindow.ViewModel.ToSelection();

                // 执行导入
                var result = await facade.ImportSeatSetsAsync(filePath, selection,
                    progress: null, CancellationToken.None);

                // 显示结果
                if (result.Success)
                {
                    await dialog.ShowInfoAsync(Lang.Resources.SeatSets_ImportTitle,
                        string.Format(Lang.Resources.SeatSets_ImportSuccess, result.Restored));
                }
                else if (result.Failed > 0 && result.Restored > 0)
                {
                    var errorDetails = result.Errors.Count > 0
                        ? "\n\n" + string.Join("\n", result.Errors.Take(5))
                        : "";
                    await dialog.ShowWarningAsync(Lang.Resources.SeatSets_ImportTitle,
                        string.Format(Lang.Resources.SeatSets_ImportPartial,
                            result.Restored, result.TotalFiles, result.Failed) + errorDetails);
                }
                else
                {
                    var errorDetails = result.Errors.Count > 0
                        ? "\n" + string.Join("\n", result.Errors.Take(10))
                        : "";
                    await dialog.ShowErrorAsync(Lang.Resources.SeatSets_ImportTitle,
                        string.Join("\n", result.Errors.Take(10)) + errorDetails);
                }

                // 导入后刷新设置（主题/语言）并导航到主页
                if (result.Restored > 0)
                    await RefreshAfterImportAsync(_serviceProvider);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[SeatSets] 处理打开文件异常: {Path}", filePath);
                await DialogServiceShim.ShowWarningAsync(dialog,
                    Lang.Resources.Common_OperationFailed, ex.Message);
            }
        }

        /// <summary>
        /// 导入 SeatsSets 后刷新应用状态：重新加载设置、应用主题和语言、导航到主页。
        /// 确保导入的 AppSettings 立即生效，且所有页面数据反映最新状态。
        /// </summary>
        internal static async Task RefreshAfterImportAsync (IServiceProvider serviceProvider)
        {
            try
            {
                var facade = serviceProvider.GetRequiredService<IApplicationFacade>();
                var navigation = serviceProvider.GetRequiredService<INavigationService>();
                var settings = await facade.LoadAppSettingsAsync();

                // 应用主题
                if (AvaloniaApplication.Current is { } app)
                {
                    app.RequestedThemeVariant = settings.Theme switch
                    {
                        ThemeMode.Light => ThemeVariant.Light,
                        ThemeMode.Dark => ThemeVariant.Dark,
                        _ => ThemeVariant.Default
                    };
                }

                // 应用语言
                try
                {
                    var culture = string.IsNullOrEmpty(settings.Language)
                        ? CultureInfo.InstalledUICulture
                        : new CultureInfo(settings.Language);
                    CultureInfo.CurrentCulture = culture;
                    CultureInfo.CurrentUICulture = culture;
                    CultureInfo.DefaultThreadCurrentCulture = culture;
                    CultureInfo.DefaultThreadCurrentUICulture = culture;
                    Lang.Resources.Culture = culture;
                }
                catch (CultureNotFoundException) { /* 无效语言代码，保持当前 */ }

                // 导航到主页
                navigation.NavigateTo(PageKey.Home);
            }
            catch (Exception ex)
            {
                var logger = serviceProvider.GetRequiredService<ILogger<App>>();
                logger.LogWarning(ex, "[SeatSets] 导入后刷新异常");
            }
        }
    }

    internal static class DialogServiceShim
    {
        public static async Task ShowWarningAsync (IDialogService dialog , string title , string message)
        {
            try
            {
                await dialog.ShowWarningAsync(title , message);
            }
            catch
            {
                // 对话框显示失败时静默处理
            }
        }

        /// <returns>0=确定, 1=不再提醒, null=关闭窗口</returns>
        public static async Task<int?> ShowEnvironmentWarningAsync (IDialogService dialog , string message)
        {
            try
            {
                return await dialog.ShowMultiOptionAsync(
                    Lang.Resources.App_EnvironmentWarning ,
                    message ,
                    Lang.Resources.Common_OK ,
                    Lang.Resources.Common_DontRemind);
            }
            catch
            {
                return null;
            }
        }
    }
}
