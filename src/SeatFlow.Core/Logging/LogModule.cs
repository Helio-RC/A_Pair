namespace SeatFlow.Core.Logging;

/// <summary>
/// 标准日志模块前缀常量，用于 <see cref="Models.AppSettings.LogSettings.CategoryOverrides"/> 配置。
/// 这些前缀对应去掉 "SeatFlow." 后的命名空间层级，代码会自动补全完整前缀。
/// </summary>
public static class LogModule
{
    /// <summary>排座策略模块：FixedSeat / RandomFill / Defrag / DeskMate / GenderRestrictedSeat / NoRepeatDeskMate / FrontRowRotation</summary>
    public const string Strategies = "Core.Strategies";

    /// <summary>导出模块：Excel / CSV / PDF / Image</summary>
    public const string Exporters = "Infrastructure.Exporters";

    /// <summary>数据提供者模块：StudentProvider / VenueRepository / AppSettingsRepository 等</summary>
    public const string Providers = "Infrastructure.Providers";

    /// <summary>仓储模块：SeatingSnapshotRepository</summary>
    public const string Repositories = "Infrastructure.Repositories";

    /// <summary>文件迁移模块</summary>
    public const string Migration = "Infrastructure.Migration";

    /// <summary>SeatSets 数据包服务模块</summary>
    public const string SeatSets = "Infrastructure.Services";

    /// <summary>策略管道与外观服务模块</summary>
    public const string Pipeline = "Application.Services";

    /// <summary>插件管理模块</summary>
    public const string Plugins = "Application.Plugins";

    /// <summary>脚本引擎模块：Lua / C# Script</summary>
    public const string Scripting = "Application.Scripting";

    /// <summary>UI ViewModel 模块</summary>
    public const string ViewModels = "Presentation.Avalonia.ViewModels";

    /// <summary>导航与 UI 服务模块</summary>
    public const string Navigation = "Presentation.Avalonia.Services";

    /// <summary>引导系统模块</summary>
    public const string Onboarding = "Presentation.Avalonia.Services.OnboardingService";

    /// <summary>看门狗服务模块</summary>
    public const string Watchdog = "Presentation.Avalonia.Services.WatchdogService";
}
