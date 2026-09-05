using System;
using System.Threading.Tasks;
using SeatFlow.Core.Telemetry;
using SeatFlow.Presentation.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SeatFlow.Presentation.Avalonia.Services;

public class NavigationService : INavigationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NavigationService> _logger;

    public ViewModelBase CurrentViewModel { get; private set; } = default!;
    public PageKey CurrentPage { get; private set; } = (PageKey)(-1);
    public event Action? CurrentViewModelChanged;

    public NavigationService (IServiceProvider serviceProvider , ILogger<NavigationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        NavigateTo(PageKey.Home);
    }

    public void NavigateTo (PageKey page)
    {
        if (page == CurrentPage) return;
        SwitchToPage(page);
    }

    public async Task<bool> NavigateToAsync (PageKey page)
    {
        if (page == CurrentPage) return false;

        if (CurrentViewModel is not null && !await CurrentViewModel.CanLeaveAsync())
        {
            _logger.LogInformation("导航被拦截：{From} → {To}，当前页面拒绝离开" , CurrentPage , page);
            return false;
        }

        SwitchToPage(page);
        return true;
    }

    private void SwitchToPage (PageKey page)
    {
        CurrentPage = page;
        CurrentViewModel = page switch
        {
            PageKey.Home => _serviceProvider.GetRequiredService<HomeViewModel>(),
            PageKey.MemberManagement => _serviceProvider.GetRequiredService<MemberManagementViewModel>(),
            PageKey.VenueConfiguration => _serviceProvider.GetRequiredService<VenueConfigurationViewModel>(),
            PageKey.FreeformManagement => _serviceProvider.GetRequiredService<FreeformManagementViewModel>(),
            PageKey.StrategyConfiguration => _serviceProvider.GetRequiredService<StrategyConfigurationViewModel>(),
            PageKey.SeatingArrangement => _serviceProvider.GetRequiredService<SeatingArrangementViewModel>(),
            PageKey.SnapshotHistory => _serviceProvider.GetRequiredService<SnapshotHistoryViewModel>(),
            PageKey.Settings => _serviceProvider.GetRequiredService<SettingsViewModel>(),
            PageKey.About => _serviceProvider.GetRequiredService<AboutViewModel>(),
            _ => throw new ArgumentOutOfRangeException(nameof(page))
        };
        _logger.LogInformation("导航切换：{From} → {To}" , CurrentPage , page);

        // 记录页面访问遥测
        try
        {
            var telemetry = _serviceProvider.GetRequiredService<ITelemetryService>();
            telemetry.RecordPageView(page.ToString());
        }
        catch { /* 遥测未注册时静默处理 */ }

        CurrentViewModelChanged?.Invoke();
    }
}
