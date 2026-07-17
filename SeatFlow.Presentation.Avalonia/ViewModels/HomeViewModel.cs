using System;
using SeatFlow.Presentation.Avalonia.Lang;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    public string Greeting { get; } = Resources.Home_Greeting;
    public string Subtitle { get; } = Resources.Home_Subtitle;

    public string TimeBasedGreeting { get; }
    public string VersionLabel { get; }

    public HomeViewModel ()
    {
        var hour = DateTime.Now.Hour;
        TimeBasedGreeting = hour switch
        {
            < 12 => Resources.Home_Greeting_Morning,
            < 18 => Resources.Home_Greeting_Afternoon,
            _    => Resources.Home_Greeting_Evening,
        };

        VersionLabel = string.Format(Resources.Home_Version, VersionInfo.Version);
    }
}
