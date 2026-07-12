using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SeatFlow.Core.Models;
using SeatFlow.Core.Providers;
using SeatFlow.Core.Telemetry;

namespace SeatFlow.Presentation.Avalonia.Telemetry;

/// <summary>
/// 遥测服务 DI 注册扩展方法。
/// </summary>
public static class TelemetryServiceCollectionExtensions
{
    /// <summary>
    /// 注册 OpenTelemetry 遥测管线。如果用户未启用遥测，仅注册空操作桩，不创建任何后台资源。
    /// 必须在 AddSeatFlowApplication() 之后调用（依赖 IAppSettingsRepository）。
    /// </summary>
    public static IServiceCollection AddSeatFlowTelemetry(this IServiceCollection services)
    {
        // 先检查配置，未启用时跳过所有重型基础设施
        var config = LoadTelemetryConfigSafe(services);

        if (!config.Enabled)
        {
            services.AddSingleton<ITelemetryService, NullTelemetryService>();
            return services;
        }

        // 1. Http 客户端（通道 + 批处理 + 退避）
        services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<TelemetryHttpClient>>();
            return new TelemetryHttpClient(
                config.ServerUrl,
                config.FlushIntervalSeconds,
                config.MaxBatchSize,
                config.EnableCompression,
                logger);
        });

        // 2. 遥测服务
        services.AddSingleton<ITelemetryService, TelemetryService>();

        // 3. 自定义导出器
        services.AddSingleton<AppTelemetryExporter>();
        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<TelemetryHttpClient>();
            return new AppTelemetryMetricExporter(httpClient, config.MetricSnapshotIntervalSeconds);
        });

        // 4. TracerProvider
        services.AddSingleton(sp =>
        {
            var exporter = sp.GetRequiredService<AppTelemetryExporter>();
            return Sdk.CreateTracerProviderBuilder()
                .SetResourceBuilder(CreateResource())
                .AddSource("SeatFlow.App")
                .AddSource("SeatFlow.UI")
                .AddSource("SeatFlow.Features")
                .SetSampler(new AlwaysOnSampler())
                .AddProcessor(new SimpleActivityExportProcessor(exporter))
                .Build();
        });

        // 5. MeterProvider
        services.AddSingleton(sp =>
        {
            var exporter = sp.GetRequiredService<AppTelemetryMetricExporter>();
            return Sdk.CreateMeterProviderBuilder()
                .SetResourceBuilder(CreateResource())
                .AddMeter("SeatFlow.App.Metrics")
                .AddReader(new PeriodicExportingMetricReader(exporter, config.MetricSnapshotIntervalSeconds * 1000)
                {
                    TemporalityPreference = MetricReaderTemporalityPreference.Delta
                })
                .Build();
        });

        return services;
    }

    private static ResourceBuilder CreateResource()
    {
        return ResourceBuilder.CreateDefault()
            .AddAttributes(new Dictionary<string, object>
            {
                ["service.name"] = "SeatFlow",
                ["service.version"] = VersionInfo.Version
            });
    }

    /// <summary>安全加载遥测配置。直接读取 AppSettings.json 文件（不使用 DI，避免提前构建 ServiceProvider）。</summary>
    private static TelemetryConfig LoadTelemetryConfigSafe(IServiceCollection services)
    {
        try
        {
            var exeDir = System.IO.Path.GetDirectoryName(Environment.ProcessPath)!;
            var settingsPath = System.IO.Path.Combine(exeDir, "AppData", "AppSettings.json");
            if (!System.IO.File.Exists(settingsPath))
                return new TelemetryConfig();

            var json = System.IO.File.ReadAllText(settingsPath);
            var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return settings?.Telemetry ?? new TelemetryConfig();
        }
        catch
        {
            return new TelemetryConfig();
        }
    }
}
