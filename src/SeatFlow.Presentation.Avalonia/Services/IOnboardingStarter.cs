namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>引导启动入口，供 SettingsViewModel 等调用，避免 ViewModel 直接依赖 View。</summary>
public interface IOnboardingStarter
{
    void StartOnboarding ();
}
