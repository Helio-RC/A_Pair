using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Resources;
using SeatFlow.Core.Models;
using SeatFlow.Core.Providers;
using SeatFlow.Core.Telemetry;

namespace SeatFlow.Presentation.Avalonia.Telemetry;

/// <summary>
/// 遥测服务实现。封装 OpenTelemetry ActivitySource（跟踪）、Meter + Counter（指标）、
/// 以及事件去重/采样逻辑，通过 TelemetryHttpClient 异步批量上报。
/// </summary>
public sealed class TelemetryService : ITelemetryService, IDisposable
{
    private readonly IAppSettingsRepository _settingsRepo;
    private readonly TelemetryHttpClient _httpClient;
    private readonly ILogger<TelemetryService> _logger;
    private bool _enabled;

    // ── OpenTelemetry 仪器 ──
    private static readonly Meter Meter = new("SeatFlow.App.Metrics");
    private readonly Counter<long> _launchCounter;
    private readonly Counter<long> _seatingGenerationCounter;
    private readonly Counter<long> _exportCounter;
    private readonly Counter<long> _errorCounter;

    // ── 去重追踪（线程安全）──
    private readonly ConcurrentDictionary<string, DateTime> _lastEventTime = new();

    // ── ActivitySource 实例 ──
    private static readonly ActivitySource AppActivitySource = new("SeatFlow.App");
    private static readonly ActivitySource UiActivitySource = new("SeatFlow.UI");
    private static readonly ActivitySource FeatureActivitySource = new("SeatFlow.Features");

    // ── 采样率配置缓存 ──
    private double _pageViewSampleRate = 0.2;
    private int _pageViewCoalesceSeconds = 60;

    public bool IsEnabled => _enabled;

    public TelemetryService(
        IAppSettingsRepository settingsRepo,
        TelemetryHttpClient httpClient,
        ILogger<TelemetryService> logger)
    {
        _settingsRepo = settingsRepo;
        _httpClient = httpClient;
        _logger = logger;

        // 创建计数器
        _launchCounter = Meter.CreateCounter<long>(
            TelemetryInstrumentNames.AppLaunches, "次", "应用启动次数");
        _seatingGenerationCounter = Meter.CreateCounter<long>(
            TelemetryInstrumentNames.SeatingGenerations, "次", "排座生成次数");
        _exportCounter = Meter.CreateCounter<long>(
            TelemetryInstrumentNames.Exports, "次", "导出操作次数");
        _errorCounter = Meter.CreateCounter<long>(
            TelemetryInstrumentNames.Errors, "次", "错误次数");

        // 异步加载配置（fire-and-forget，启用前所有方法为安全 no-op）
        _ = ReloadConfigAsync();
    }

    private async Task ReloadConfigAsync()
    {
        try
        {
            var settings = await _settingsRepo.LoadAsync();
            var cfg = settings.Telemetry;
            _enabled = cfg.Enabled;
            _pageViewSampleRate = cfg.PageViewSampleRate;
            _pageViewCoalesceSeconds = cfg.PageViewCoalesceSeconds;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "加载遥测配置失败，使用默认值");
            _enabled = false;
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return;
        _enabled = enabled;

        // 异步持久化到 AppSettings（fire-and-forget，避免阻塞调用方）
        _ = PersistEnabledAsync(enabled);
    }

    private async Task PersistEnabledAsync(bool enabled)
    {
        try
        {
            var settings = await _settingsRepo.LoadAsync();
            settings.Telemetry.Enabled = enabled;
            await _settingsRepo.SaveAsync(settings);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "持久化遥测状态失败");
        }
    }

    // ── Tracing ──

    public Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal)
    {
        if (!_enabled) return null;

        // 根据名称前缀选择 ActivitySource
        var source = name switch
        {
            _ when name.StartsWith("app.") => AppActivitySource,
            _ when name.StartsWith("page.") => UiActivitySource,
            _ => FeatureActivitySource
        };

        return source.StartActivity(name, kind);
    }

    // ── Events ──

    public void RecordEvent(string eventType, Dictionary<string, object?>? attributes = null)
    {
        if (!_enabled) return;

        var evt = new TelemetryEvent
        {
            Type = eventType,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = attributes
        };

        _httpClient.TryEnqueue(evt);
    }

    // ── Metrics ──

    public void RecordAppLaunch()
    {
        if (!_enabled) return;

        _launchCounter.Add(1);

        RecordEvent(TelemetryEventTypes.AppStart, new()
        {
            [TelemetryAttributeKeys.AppVersion] = VersionInfo.Version,
            [TelemetryAttributeKeys.AppCommit] = GitCommit.Hash,
            [TelemetryAttributeKeys.OsType] = GetOsType(),
            [TelemetryAttributeKeys.OsDescription] = RuntimeInformation.OSDescription,
            [TelemetryAttributeKeys.RuntimeName] = ".NET",
            [TelemetryAttributeKeys.RuntimeVersion] = Environment.Version.ToString(),
            [TelemetryAttributeKeys.HostArch] = RuntimeInformation.OSArchitecture.ToString()
        });
    }

    public void RecordSeatingGeneration(bool success, int studentCount, int venueCount, int strategyCount, long durationMs)
    {
        if (!_enabled) return;

        _seatingGenerationCounter.Add(1,
            new KeyValuePair<string, object?>[] { new(TelemetryAttributeKeys.GenerationSuccess, success) });

        RecordEvent(TelemetryEventTypes.SeatingGeneration, new()
        {
            [TelemetryAttributeKeys.GenerationSuccess] = success,
            [TelemetryAttributeKeys.StudentCount] = studentCount,
            [TelemetryAttributeKeys.VenueCount] = venueCount,
            [TelemetryAttributeKeys.StrategyCount] = strategyCount,
            [TelemetryAttributeKeys.GenerationDurationMs] = durationMs
        });
    }

    public void RecordExport(string format, bool success)
    {
        if (!_enabled) return;

        _exportCounter.Add(1,
            new KeyValuePair<string, object?>[] { new(TelemetryAttributeKeys.ExportFormat, format), new(TelemetryAttributeKeys.ExportSuccess, success) });

        RecordEvent(TelemetryEventTypes.Export, new()
        {
            [TelemetryAttributeKeys.ExportFormat] = format,
            [TelemetryAttributeKeys.ExportSuccess] = success
        });
    }

    public void RecordError(string category, string message)
    {
        if (!_enabled) return;

        _errorCounter.Add(1,
            new KeyValuePair<string, object?>[] { new(TelemetryAttributeKeys.ErrorCategory, category) });

        // 截断错误消息，避免包含敏感路径
        var truncated = message.Length > 200 ? message[..200] : message;

        RecordEvent(TelemetryEventTypes.Error, new()
        {
            [TelemetryAttributeKeys.ErrorCategory] = category,
            [TelemetryAttributeKeys.ErrorMessage] = truncated
        });
    }

    public void RecordPageView(string pageName)
    {
        if (!_enabled) return;

        // 去重：同一页面在 coalesce 窗口内只记一次
        var key = $"pv:{pageName}";
        if (_lastEventTime.TryGetValue(key, out var last) &&
            (DateTime.UtcNow - last).TotalSeconds < _pageViewCoalesceSeconds)
            return;

        _lastEventTime[key] = DateTime.UtcNow;

        // 清洁过期去重条目（避免字典无限增长）
        if (_lastEventTime.Count > 50)
            CleanupStaleCoalesceEntries();

        // 采样
        if (_pageViewSampleRate < 1.0 && Random.Shared.NextDouble() >= _pageViewSampleRate)
            return;

        RecordEvent(TelemetryEventTypes.PageView, new()
        {
            [TelemetryAttributeKeys.PageName] = pageName
        });
    }

    public void RecordFeatureUsage(string featureName)
    {
        if (!_enabled) return;

        RecordEvent(TelemetryEventTypes.FeatureUsage, new()
        {
            [TelemetryAttributeKeys.FeatureName] = featureName
        });
    }

    private void CleanupStaleCoalesceEntries()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-_pageViewCoalesceSeconds * 2);
        foreach (var kv in _lastEventTime)
        {
            if (kv.Value < cutoff)
                _lastEventTime.TryRemove(kv.Key, out _);
        }
    }

    // ── Lifecycle ──

    public async Task FlushAsync(TimeSpan? timeout = null)
    {
        await _httpClient.FlushAsync(timeout ?? TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        Meter.Dispose();
    }

    // ── Helpers ──

    private static string GetOsType() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? "windows" : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" : "linux";

    public static ResourceBuilder GetResourceBuilder()
    {
        return ResourceBuilder.CreateDefault()
            .AddAttributes(new Dictionary<string, object>
            {
                ["service.name"] = "SeatFlow",
                ["service.version"] = VersionInfo.Version
            })
            .AddAttributes(new KeyValuePair<string, object>[]
            {
                new(TelemetryAttributeKeys.OsType, GetOsType()),
                new(TelemetryAttributeKeys.OsDescription, RuntimeInformation.OSDescription),
                new(TelemetryAttributeKeys.HostArch, RuntimeInformation.OSArchitecture.ToString()),
                new(TelemetryAttributeKeys.RuntimeName, ".NET"),
                new(TelemetryAttributeKeys.RuntimeVersion, Environment.Version.ToString()),
                new("service.commit_id", GitCommit.Hash)
            });
    }

    // ── Static accessors for OpenTelemetry provider setup ──

    public static ActivitySource AppSource => AppActivitySource;
    public static ActivitySource UiSource => UiActivitySource;
    public static ActivitySource FeatureSource => FeatureActivitySource;
    public static Meter AppMeter => Meter;
}
