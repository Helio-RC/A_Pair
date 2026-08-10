using System;
using System.Runtime.InteropServices;
using System.Text;
using SeatFlow.Presentation.Avalonia.Lang;

namespace SeatFlow.Presentation.Avalonia.Services;

internal static class StartupGuard
{
    public static (bool HasWarning , string Message) CheckEnvironment ()
    {
        var sb = new StringBuilder();
        var major = Environment.Version.Major;

        if (major < 10)
        {
            sb.AppendLine(string.Format(Resources.Startup_DotNetVersion , Environment.Version));
        }

        if (!IsSupportedOS())
        {
            sb.Append(string.Format(Resources.Startup_UnsupportedOS , RuntimeInformation.OSDescription));
        }

        var message = sb.ToString().TrimEnd();
        return (message.Length > 0 , message);
    }

    private static bool IsSupportedOS ()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Environment.OSVersion.Version.Major >= 10;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return Environment.OSVersion.Version.Major >= 12;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return true;
        return false;
    }
}
