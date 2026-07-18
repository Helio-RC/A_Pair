using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using SeatFlow.Presentation.Avalonia.Lang;
using AvaloniaApplication = Avalonia.Application;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    public string Greeting { get; } = Resources.Home_Greeting;
    public string Subtitle { get; } = Resources.Home_Subtitle;

    public string UserName { get; }
    public string UserInitialChar { get; }
    public IImage? UserAvatar { get; }
    public bool HasUserAvatar => UserAvatar is not null;
    public string GreetingLine { get; }
    public string VersionLabel { get; }

    public string DocsUrl { get; }
    public string QuickStartUrl { get; }
    public string FaqUrl { get; }

    public List<ReleaseLine> ReleaseSections { get; }

    public HomeViewModel ()
    {
        var data = LoadAboutData();

        UserName = Environment.UserName;
        UserInitialChar = ToMathBold(UserName.FirstOrDefault());

        var avatarPath = FindUserAvatar();
        if (avatarPath is not null)
        {
            try { UserAvatar = new Bitmap(avatarPath); }
            catch { /* 加载失败则回退到首字符头像 */ }
        }

        // 问候语：时段 + 用户名 + 品牌欢迎语合并一行
        var hour = DateTime.Now.Hour;
        var template = hour switch
        {
            < 12 => Resources.Home_Greeting_Morning,
            < 18 => Resources.Home_Greeting_Afternoon,
            _    => Resources.Home_Greeting_Evening,
        };
        var personalized = string.Format(template, UserName);
        var sep = CultureInfo.CurrentUICulture.Name.StartsWith("zh") ? "！" : "! ";
        GreetingLine = $"{personalized}{sep}{Greeting}";

        VersionLabel = string.Format(Resources.Home_Version, VersionInfo.Version);

        DocsUrl = data.DocsUrl;
        QuickStartUrl = data.QuickStartUrl;
        FaqUrl = data.FaqUrl;

        ReleaseSections = LoadReleaseNotes();
    }

    // ═══════════════════════════════════════════════
    //  RELEASE.md 嵌入资源读取 + 逐行解析
    // ═══════════════════════════════════════════════

    private static List<ReleaseLine> LoadReleaseNotes ()
    {
        var assembly = typeof(HomeViewModel).Assembly;
        const string resourceName = "SeatFlow.Presentation.Avalonia.Data.release.md";

        try
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
                return [new ReleaseLine("RELEASE.md not found", ReleaseLineKind.Paragraph)];

            using var reader = new StreamReader(stream);
            return ParseReleaseNotes(reader.ReadToEnd());
        }
        catch
        {
            return [new ReleaseLine("无法加载发布说明", ReleaseLineKind.Paragraph)];
        }
    }

    private static List<ReleaseLine> ParseReleaseNotes (string markdown)
    {
        var lines = new List<ReleaseLine>();
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd();
            var kind = Classify(line, out var text);
            lines.Add(new ReleaseLine(text, kind));
        }
        return lines;
    }

    private static ReleaseLineKind Classify (string line, out string text)
    {
        if (line.Length == 0)
        {
            text = "";
            return ReleaseLineKind.Empty;
        }
        if (line.StartsWith("### "))
        {
            text = line[4..];
            return ReleaseLineKind.SubHeading;
        }
        if (line.StartsWith("## "))
        {
            text = line[3..];
            return ReleaseLineKind.Heading;
        }
        if (line.StartsWith("# "))
        {
            text = line[2..];
            return ReleaseLineKind.Title;
        }
        if (line.StartsWith("- "))
        {
            text = line[2..];
            return ReleaseLineKind.ListItem;
        }
        text = line;
        return ReleaseLineKind.Paragraph;
    }

    // ═══════════════════════════════════════════════
    //  about.json 读取（复用现有模式）
    // ═══════════════════════════════════════════════

    private static AboutPageData LoadAboutData ()
    {
        var assembly = typeof(AboutViewModel).Assembly; // about.json 属于 About 页
        const string resourceName = "SeatFlow.Presentation.Avalonia.Data.about.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"Embedded resource not found: {resourceName}");

        var all = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, AboutPageData>>(stream, _jsonOptions)
                  ?? new Dictionary<string, AboutPageData>();

        var culture = CultureInfo.CurrentUICulture;
        if (all.TryGetValue(culture.Name, out var match)) return match;
        if (all.TryGetValue(culture.TwoLetterISOLanguageName, out match)) return match;
        if (all.TryGetValue("zh-CN", out match)) return match;
        return all.Values.FirstOrDefault() ?? new AboutPageData();
    }

    private static readonly System.Text.Json.JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class AboutPageData
    {
        public string DocsUrl { get; set; } = "";
        public string QuickStartUrl { get; set; } = "";
        public string FaqUrl { get; set; } = "";
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
            .Where(f => f.Length > 1024)
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

            const string prefix = "Picture: ";
            var idx = output.IndexOf(prefix, StringComparison.Ordinal);
            if (idx >= 0)
            {
                var path = output[(idx + prefix.Length)..].Split('\n')[0].Trim();
                if (File.Exists(path))
                    return path;
            }
        }
        catch { }
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
    //  打开链接
    // ═══════════════════════════════════════════════

    [RelayCommand]
    private static async Task OpenUrl (string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        if (AvaloniaApplication.Current?.ApplicationLifetime is
            global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } mainWindow)
        {
            var launcher = TopLevel.GetTopLevel(mainWindow)?.Launcher;
            if (launcher is not null)
            {
                await launcher.LaunchUriAsync(new Uri(url));
                return;
            }
        }

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
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

// ═══════════════════════════════════════════════
//  发布说明行模型
// ═══════════════════════════════════════════════

public enum ReleaseLineKind { Title, Heading, SubHeading, ListItem, Paragraph, Empty }

public record ReleaseLine (string Text, ReleaseLineKind Kind)
{
    public bool IsTitle      => Kind == ReleaseLineKind.Title;
    public bool IsHeading    => Kind == ReleaseLineKind.Heading;
    public bool IsSubHeading => Kind == ReleaseLineKind.SubHeading;
    public bool IsListItem   => Kind == ReleaseLineKind.ListItem;
    public bool IsParagraph  => Kind == ReleaseLineKind.Paragraph;
    public bool IsEmpty      => Kind == ReleaseLineKind.Empty;
}
