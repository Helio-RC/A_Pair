using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using SeatFlow.Application.Services;
using SeatFlow.Core.Models.SeatSets;
using SeatFlow.Core.Utilities;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.Telemetry;
using SeatFlow.Presentation.Avalonia.ViewModels;
using SeatFlow.Presentation.Avalonia.Views;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Velopack;

[assembly: System.Resources.NeutralResourcesLanguage("zh-CN")]

namespace SeatFlow.Presentation.Avalonia
{
    internal sealed class Program
    {
        /// <summary>用于将 .seatsets 文件路径从第二个进程转发到第一个进程的命名管道名称。</summary>
        internal const string SeatSetsPipeName = "SeatFlow_SeatSetsPipe";

        [STAThread]
        public static void Main (string[] args)
        {
            // Velopack 钩子：必须在 Main() 最开头调用。
            // 正常启动时 Run() 立即返回；安装/更新/卸载钩子模式下，钩子执行完毕从 Run() 内部退出。
            VelopackApp.Build()
                .OnFirstRun(_ => App.IsFirstRunAfterInstall = true)
                .Run();

            // 安装时 hook：复制安装程序同目录下的 .seatsets 文件到应用根目录
            if (args.Any(a => a.StartsWith("--veloapp-install")))
            {
                CopySeatSetsFromInstallerDir();
                return; // hook 模式静默退出
            }

            // 检测命令行参数中的 .seatsets 文件路径（双击打开或命令行导入）
            string? seatsetsFilePath = null;
            if (args.Length > 0)
            {
                var firstArg = args[0];
                if (!string.IsNullOrEmpty(firstArg) &&
                    firstArg.EndsWith(".seatsets" , StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(firstArg))
                {
                    seatsetsFilePath = Path.GetFullPath(firstArg);
                }
            }

            // Windows: 注册 .seatsets 文件关联，使双击文件能启动程序导入
            RegisterSeatSetsFileAssociation();

            // 如果是非首个实例且有 .seatsets 文件，转发给已有实例后静默退出
            if (seatsetsFilePath != null && TryForwardToExistingInstance(seatsetsFilePath))
                return;

            // 在 DI 创建 AppData/Logs 之前扫描自动导入文件
            // AddSeatFlowApplication 会创建 Logs 目录使 AppData 存在，导致后续检查失效
            var autoImportPath = DiscoverAutoImportSeatSetsFile();

            using var mutex = new Mutex(true , @"Global\SeatFlow_SeatingArrangement" , out bool isFirstInstance);

            var services = new ServiceCollection();
            // 使用 OS 标准数据目录（而非 exe 同目录）
            var pluginsDir = GetPluginsDirectory();
            services.AddSeatFlowApplication(
                AppEnvironment.DefaultDataDirectory ,
                pluginsDir);

            // 注册 OpenTelemetry 遥测（必须在 AddSeatFlowApplication 之后，依赖 IAppSettingsRepository）
            services.AddSeatFlowTelemetry();

            // 注册导航服务
            services.AddSingleton<INavigationService , NavigationService>();
            services.AddSingleton<IFileService , FileService>();
            services.AddSingleton<IDialogService , DialogService>();
            services.AddSingleton<WatchdogService>();

            // 注册更新服务（Velopack 自动更新）
            services.AddSingleton<IUpdateService , UpdateService>();

            // 注册排座次数计数器
            services.AddSingleton<IArrangementCounterService , ArrangementCounterService>();

            // 注册 ViewModels
            services.AddSingleton<MainWindow>();
            services.AddSingleton<IOnboardingService , OnboardingService>();
            services.AddSingleton<IOnboardingStarter>(sp => (IOnboardingStarter)sp.GetRequiredService<IOnboardingService>());
            services.AddSingleton<MainShellViewModel>();
            services.AddSingleton<HomeViewModel>();
            services.AddSingleton<MemberManagementViewModel>();
            services.AddSingleton<VenueConfigurationViewModel>();
            services.AddSingleton<FreeformManagementViewModel>();
            services.AddSingleton<StrategyConfigurationViewModel>();
            services.AddSingleton<SeatingArrangementViewModel>();
            services.AddTransient<SnapshotHistoryViewModel>();
            services.AddSingleton<PluginManagementViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<AboutViewModel>();

            // 声明式策略配置子组件
            services.AddTransient<ConfigBlockEditorViewModel>();

            var serviceProvider = services.BuildServiceProvider();

            // 将命令行中的 .seatsets 文件路径传递给 App（用于双击打开导入）
            App.PendingSeatSetsFilePath = seatsetsFilePath;

            // 将自动发现的 .seatsets 文件路径传递给 App（用于首次启动数据恢复）
            App.AutoImportSeatSetsPath = autoImportPath;

            BuildAvaloniaApp(serviceProvider , isFirstInstance)
                .StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp (IServiceProvider serviceProvider , bool isFirstInstance)
            => AppBuilder.Configure(() => new App(serviceProvider , isFirstInstance))
                .UsePlatformDetect()
#if DEBUG
                .WithDeveloperTools()
#endif
                .WithInterFont()
                .LogToTrace();

        /// <summary>
        /// 获取插件目录。Velopack 安装时使用 RootAppDir/Plugins（更新时保留），
        /// 开发/便携模式 fallback 到 exe 同目录。
        /// </summary>
        private static string GetPluginsDirectory ()
        {
            var root = GetInstallRootDirectory();
            return root is not null ? Path.Combine(root , "Plugins") : Path.Combine(AppEnvironment.ExeDirectory , "Plugins");
        }

        /// <summary>
        /// 获取 Velopack 安装根目录，非安装模式返回 null。
        /// </summary>
        private static string? GetInstallRootDirectory ()
        {
            try
            {
                if (Velopack.Locators.VelopackLocator.IsCurrentSet
                    && Velopack.Locators.VelopackLocator.Current.RootAppDir is { Length: > 0 } root)
                {
                    return root;
                }
            }
            catch
            {
                // VelopackLocator 不可用时返回 null
            }
            return null;
        }

        /// <summary>
        /// 安装时 hook：将安装程序同目录下的 .seatsets 文件复制到应用安装根目录。
        /// 由 --veloapp-install CLI 参数触发，必须在 30 秒内完成。
        /// </summary>
        private static void CopySeatSetsFromInstallerDir ()
        {
            try
            {
                var currentDir = Environment.CurrentDirectory;
                if (string.IsNullOrEmpty(currentDir) || !Directory.Exists(currentDir))
                    return;

                var root = GetInstallRootDirectory();
                if (root is null)
                    return;

                Directory.CreateDirectory(root);

                var seatSetsFiles = Directory.GetFiles(currentDir , "*.seatsets" , SearchOption.TopDirectoryOnly);
                foreach (var file in seatSetsFiles)
                {
                    var dest = Path.Combine(root , Path.GetFileName(file));
                    File.Copy(file , dest , overwrite: true);
                }
            }
            catch
            {
                // 安装 hook 中静默处理，不影响安装流程
            }
        }

        [DllImport("user32.dll" , CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW (IntPtr hWnd , string text , string caption , uint type);

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify (int wEventId , uint uFlags , IntPtr dwItem1 , IntPtr dwItem2);

        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST = 0x0000;

        private static void ShowFatalDialog (string title , string message)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    _ = MessageBoxW(IntPtr.Zero , message , title , 0x00000010); // MB_ICONERROR
                }
                else if (OperatingSystem.IsLinux())
                {
                    Process.Start("zenity" , $"--error --width=480 --title=\"{title}\" --text=\"{message.Replace("\"" , "\\\"")}\"");
                }
                else if (OperatingSystem.IsMacOS())
                {
                    var escaped = message.Replace("\\" , "\\\\").Replace("\"" , "\\\"").Replace("\n" , "\\n");
                    Process.Start("osascript" , $"-e \"display dialog \\\"{escaped}\\\" with title \\\"{title}\\\" buttons {{\\\"OK\\\"}} default button 1 with icon stop\"");
                }
                else
                {
                    Console.Error.WriteLine($"{title}: {message}");
                }
            }
            catch
            {
                Console.Error.WriteLine($"{title}: {message}");
            }
        }

        /// <summary>
        /// 在 HKCU 中注册/刷新 .seatsets 文件关联（仅 Windows，无需管理员权限）。
        /// 仅在首次启动（AppData 不存在）或注册表值与当前不一致时写入，
        /// 避免每次启动无意义的磁盘 I/O。写入后通知 Shell 刷新图标缓存。
        /// </summary>
        private static void RegisterSeatSetsFileAssociation ()
        {
            if (!OperatingSystem.IsWindows())
                return;

            try
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                    return;

                var progId = "SeatFlow.seatsets";
                var appDataExists = Directory.Exists(AppEnvironment.DefaultDataDirectory);
                var iconValue = $"\"{exePath}\",0";
                var cmdValue = $"\"{exePath}\" \"%1\"";
                bool changed = false;

                using var extKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    @"Software\Classes\.seatsets");
                var oldProgId = extKey.GetValue("") as string;
                if (!appDataExists || !string.Equals(oldProgId , progId , StringComparison.Ordinal))
                {
                    extKey.SetValue("" , progId);
                    changed = true;
                }

                using var progKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    $@"Software\Classes\{progId}");
                var oldDesc = progKey.GetValue("") as string;
                if (!appDataExists || !string.Equals(oldDesc , "SeatFlow Data Package" , StringComparison.Ordinal))
                {
                    progKey.SetValue("" , "SeatFlow Data Package");
                    changed = true;
                }

                using var iconKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    $@"Software\Classes\{progId}\DefaultIcon");
                var oldIcon = iconKey.GetValue("") as string;
                if (!appDataExists || !string.Equals(oldIcon , iconValue , StringComparison.Ordinal))
                {
                    iconKey.SetValue("" , iconValue);
                    changed = true;
                }

                using var cmdKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    $@"Software\Classes\{progId}\shell\open\command");
                var oldCmd = cmdKey.GetValue("") as string;
                if (!appDataExists || !string.Equals(oldCmd , cmdValue , StringComparison.Ordinal))
                {
                    cmdKey.SetValue("" , cmdValue);
                    changed = true;
                }

                // 仅在确实写入后才通知 Shell 刷新图标缓存
                if (changed)
                    SHChangeNotify(SHCNE_ASSOCCHANGED , SHCNF_IDLIST , IntPtr.Zero , IntPtr.Zero);
            }
            catch
            {
                // 注册失败静默处理——不影响正常启动
            }
        }

        /// <summary>
        /// 在 AppData 目录创建之前扫描 exe 目录中的 .seatsets 文件用于自动导入。
        /// 仅在 AppData 不存在时生效（即真正的首次启动）。
        /// 扫描时进行轻量前置校验：大小合理、可解析为 JSON、含 formatVersion 字段。
        /// </summary>
        private static string? DiscoverAutoImportSeatSetsFile ()
        {
            try
            {
                var appDataPath = AppEnvironment.DefaultDataDirectory;
                if (Directory.Exists(appDataPath))
                    return null;

                // 确定扫描目录：Velopack 安装时扫描 RootAppDir，开发/便携模式扫描 ExeDirectory
                var scanDir = GetInstallRootDirectory() ?? AppEnvironment.ExeDirectory;
                var files = Directory.GetFiles(scanDir , "*.seatsets" , SearchOption.TopDirectoryOnly);
                if (files.Length == 0)
                    return null;

                // 按修改时间降序，取第一个通过前置校验的文件
                var candidates = files
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTimeUtc);

                foreach (var file in candidates)
                {
                    // 大小检查：最少 50 字节（一个最小合法 JSON），最大 200 MB
                    if (file.Length < 50 || file.Length > SeatSetsConstants.MaxFileSizeBytes)
                        continue;

                    // 轻量 JSON 结构检查：确认是合法 JSON 且含 formatVersion
                    try
                    {
                        var json = File.ReadAllText(file.FullName);
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        if (root.ValueKind != JsonValueKind.Object)
                            continue;
                        if (!root.TryGetProperty("formatVersion" , out _))
                            continue;
                    }
                    catch
                    {
                        continue; // JSON 解析失败，跳过
                    }

                    return file.FullName;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 尝试通过命名管道将 .seatsets 文件路径转发给已有实例。
        /// 成功返回 true（调用方应静默退出），失败返回 false（可能是首个实例）。
        /// </summary>
        private static bool TryForwardToExistingInstance (string filePath)
        {
            try
            {
                using var client = new NamedPipeClientStream(
                    "." , SeatSetsPipeName , PipeDirection.Out);
                // 短超时——如果连接不上说明没有已有实例在监听
                client.Connect(2000);
                using var writer = new StreamWriter(client);
                writer.WriteLine(filePath);
                writer.Flush();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
