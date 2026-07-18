using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SeatFlow.Presentation.Avalonia.Lang;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    public string Greeting { get; } = Resources.Home_Greeting;
    public string Subtitle { get; } = Resources.Home_Subtitle;

    public string UserName { get; }
    public string UserInitialChar { get; }
    public IImage? UserAvatar { get; }
    public bool HasUserAvatar => UserAvatar is not null;
    public string PersonalizedGreeting { get; }
    public string VersionLabel { get; }

    public HomeViewModel ()
    {
        UserName = Environment.UserName;
        UserInitialChar = ToMathBold(UserName.FirstOrDefault());

        var avatarPath = FindUserAvatar();
        if (avatarPath is not null)
        {
            try { UserAvatar = new Bitmap(avatarPath); }
            catch { /* 加载失败则回退到首字符头像 */ }
        }

        var hour = DateTime.Now.Hour;
        var template = hour switch
        {
            < 12 => Resources.Home_Greeting_Morning,
            < 18 => Resources.Home_Greeting_Afternoon,
            _    => Resources.Home_Greeting_Evening,
        };
        PersonalizedGreeting = string.Format(template, UserName);
        VersionLabel = string.Format(Resources.Home_Version, VersionInfo.Version);
    }

    // ═══════════════════════════════════════════════
    //  平台头像查找
    // ═══════════════════════════════════════════════

    private static string? FindUserAvatar ()
    {
        if (OperatingSystem.IsWindows())
            return FindWindowsAvatar();
        if (OperatingSystem.IsMacOS())
            return FindMacAvatar();
        if (OperatingSystem.IsLinux())
            return FindLinuxAvatar();
        return null;
    }

    private static string? FindWindowsAvatar ()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "AccountPictures");

        if (!Directory.Exists(dir))
            return null;

        return Directory.GetFiles(dir)
            .Select(f => new FileInfo(f))
            .Where(f => f.Length > 1024) // 跳过缩略图
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }

    private static string? FindMacAvatar ()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dscl",
                    Arguments = $". -read /Users/{Environment.UserName} Picture",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            // dscl 输出格式: "Picture: /path/to/picture"
            const string prefix = "Picture: ";
            var idx = output.IndexOf(prefix, StringComparison.Ordinal);
            if (idx >= 0)
            {
                var path = output[(idx + prefix.Length)..].Split('\n')[0].Trim();
                if (File.Exists(path))
                    return path;
            }
        }
        catch { /* 回退到首字符 */ }
        return null;
    }

    private static string? FindLinuxAvatar ()
    {
        var home = Environment.GetEnvironmentVariable("HOME");
        if (home is not null)
        {
            var face = Path.Combine(home, ".face");
            if (File.Exists(face))
                return face;
        }

        var accountsIcon = $"/var/lib/AccountsService/icons/{Environment.UserName}";
        if (File.Exists(accountsIcon))
            return accountsIcon;

        return null;
    }

    // ═══════════════════════════════════════════════
    //  装饰 Unicode 首字符
    // ═══════════════════════════════════════════════

    private static string ToMathBold (char c)
    {
        return c switch
        {
            >= 'A' and <= 'Z' => char.ConvertFromUtf32(0x1D400 + (c - 'A')),
            >= 'a' and <= 'z' => char.ConvertFromUtf32(0x1D41A + (c - 'a')),
            _                  => c.ToString()
        };
    }
}
