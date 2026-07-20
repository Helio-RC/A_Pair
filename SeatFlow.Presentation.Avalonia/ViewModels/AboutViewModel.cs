using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using SeatFlow.Presentation.Avalonia.Lang;
using CommunityToolkit.Mvvm.Input;
using AvaloniaApplication = Avalonia.Application;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

public partial class AboutViewModel : ViewModelBase
{
    public string AppName { get; } = "SeatFlow";
    public string Version { get; }
    public string VersionDisplay { get; }
    public string Description { get; }
    public string RuntimeVersion { get; }
    public string AvaloniaVersion { get; }
    public string ProjectUrl { get; }
    public string OfficialSiteUrl { get; }
    public string DocsUrl { get; }
    public string GitHubUrl { get; }
    public string IssuesUrl { get; }
    public string CommitId { get; }
    public string BuildDate { get; }
    public string License { get; }
    public string Copyright { get; }

    public List<DependencyInfo> Dependencies { get; }

    public AboutViewModel ()
    {
        var data = LoadAboutData();

        Version = $"{VersionInfo.Version}-{VersionInfo.CommitId}";
        VersionDisplay = string.Format(Resources.About_Version , Version);

        RuntimeVersion = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        AvaloniaVersion = typeof(AvaloniaApplication).Assembly.GetName().Version?.ToString() ?? "";

        Description = data.Description;
        ProjectUrl = data.ProjectUrl;
        OfficialSiteUrl = data.OfficialSiteUrl;
        DocsUrl = data.DocsUrl;
        GitHubUrl = data.GitHubUrl;
        IssuesUrl = data.IssuesUrl;
        CommitId = VersionInfo.CommitId;
        BuildDate = DateTime.TryParse(VersionInfo.BuildDate, out var dt)
            ? dt.ToString("yyyy-MM-dd")
            : VersionInfo.BuildDate;
        License = data.License;
        Copyright = data.Copyright;
        Dependencies = data.Dependencies
            .Select(d =>
            {
                var pkgId = d.PackageId ?? d.Name;
                var version = PackageVersions.Map.TryGetValue(pkgId , out var v) ? v : "?";
                return new DependencyInfo(d.Name , version , d.Purpose , d.License , d.Url);
            })
            .ToList();
    }

    [RelayCommand]
    private static async Task OpenUrl (string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        // 优先使用 Avalonia 原生 URI 启动器（通过平台 API，避免杀软误报）
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

        // 回退：无头环境 / 测试场景
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static AboutData LoadAboutData ()
    {
        var assembly = typeof(AboutViewModel).Assembly;
        const string resourceName = "SeatFlow.Presentation.Avalonia.Data.about.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"Embedded resource not found: {resourceName}");

        var all = JsonSerializer.Deserialize<Dictionary<string , AboutData>>(stream , _jsonOptions)
                  ?? new Dictionary<string , AboutData>();

        // 按当前语言查找，回退到 zh-CN
        var culture = CultureInfo.CurrentUICulture;
        if (all.TryGetValue(culture.Name , out var match)) return match;
        if (all.TryGetValue(culture.TwoLetterISOLanguageName , out match)) return match;
        if (all.TryGetValue("zh-CN" , out match)) return match;

        // 最后一个回退：取第一个可用语言
        return all.Values.FirstOrDefault() ?? new AboutData();
    }

    private sealed class AboutData
    {
        public string Description { get; set; } = "";
        public string ProjectUrl { get; set; } = "";
        public string OfficialSiteUrl { get; set; } = "";
        public string DocsUrl { get; set; } = "";
        public string GitHubUrl { get; set; } = "";
        public string IssuesUrl { get; set; } = "";
        public string License { get; set; } = "";
        public string Copyright { get; set; } = "";
        public List<DepEntry> Dependencies { get; set; } = [];
    }

    private sealed class DepEntry
    {
        public string Name { get; set; } = "";
        public string? PackageId { get; set; }
        public string Version { get; set; } = "";
        public string Purpose { get; set; } = "";
        public string License { get; set; } = "";
        public string Url { get; set; } = "";
    }
}

public class DependencyInfo (string name , string version , string purpose , string license , string url)
{
    public string Name { get; } = name;
    public string Version { get; } = version;
    public string Purpose { get; } = purpose;
    public string License { get; } = license;
    public string Url { get; } = url;
}
