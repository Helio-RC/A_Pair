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
    /// 注册 OpenTelemetry 遥测管线：TracerProvider、MeterProvider、ITelemetryService。
    /// 必须在 AddSeatFlowApplication() 之后调用（依赖 IAppSettingsRepository）。
    /// </summary>
    public static IServiceCollection AddSeatFlowTelemetry(this IServiceCollection services)
    {
        // 1. 注册 Http 客户端（通道 + 批处理 + 退避）
        services.AddSingleton(sp =>
        {
            var settingsRepo = sp.GetRequiredService<IAppSettingsRepository>();
            var logger = sp.GetRequiredService<ILogger<TelemetryHttpClient>>();

            var config = LoadTelemetryConfigSafe(settingsRepo);

            return new TelemetryHttpClient(
                config.ServerUrl,
                config.FlushIntervalSeconds,
                config.MaxBatchSize,
                config.EnableCompression,
                logger);
        });

        // 2. 注册遥测服务
        services.AddSingleton<ITelemetryService, TelemetryService>();

        // 3. 注册自定义导出器
        services.AddSingleton<AppTelemetryExporter>();
        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<TelemetryHttpClient>();
            var settingsRepo = sp.GetRequiredService<IAppSettingsRepository>();
            var config = LoadTelemetryConfigSafe(settingsRepo);
            return new AppTelemetryMetricExporter(httpClient, config.MetricSnapshotIntervalSeconds);
        });

        // 4. 构建 TracerProvider
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

        // 5. 构建 MeterProvider
        services.AddSingleton(sp =>
        {
            var exporter = sp.GetRequiredService<AppTelemetryMetricExporter>();
            var settingsRepo = sp.GetRequiredService<IAppSettingsRepository>();
            var config = LoadTelemetryConfigSafe(settingsRepo);

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

    /// <summary>构建 OpenTelemetry Resource，描述此遥测来源。</summary>
    private static ResourceBuilder CreateResource()
    {
        return ResourceBuilder.CreateDefault()
            .AddAttributes(new Dictionary<string, object>
            {
                ["service.name"] = "SeatFlow",
                ["service.version"] = VersionInfo.Version
            });
    }

    /// <summary>安全加载遥测配置，首次启动时文件可能不存在，回退到默认值。</summary>
    private static TelemetryConfig LoadTelemetryConfigSafe(IAppSettingsRepository repo)
    {
        try
        {
            var settings = System.Threading.Tasks.Task.Run(() => repo.LoadAsync()).GetAwaiter().GetResult();
            return settings.Telemetry;
        }
        catch
        {
            return new TelemetryConfig();
        }
    }
}
