using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
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

            // 同步加载配置（首次启动时配置文件可能尚不存在，使用默认值）
            TelemetryConfig config;
            try
            {
                var settings = Task.Run(() => settingsRepo.LoadAsync()).GetAwaiter().GetResult();
                config = settings.Telemetry;
            }
            catch
            {
                config = new TelemetryConfig();
            }

            return new TelemetryHttpClient(
                config.ServerUrl,
                config.FlushIntervalSeconds,
                config.MaxBatchSize,
                config.EnableCompression,
                logger);
        });

        // 2. 注册遥测服务
        services.AddSingleton<ITelemetryService, TelemetryService>();

        // 3. 注册自定义导出器（由 TelemetryService 的内部管线和 TelemetryHttpClient 共享）
        services.AddSingleton<AppTelemetryExporter>();
        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<TelemetryHttpClient>();
            var settingsRepo = sp.GetRequiredService<IAppSettingsRepository>();
            int windowSec;
            try
            {
                var settings = Task.Run(() => settingsRepo.LoadAsync()).GetAwaiter().GetResult();
                windowSec = settings.Telemetry.MetricSnapshotIntervalSeconds;
            }
            catch
            {
                windowSec = 120;
            }
            return new AppTelemetryMetricExporter(httpClient, windowSec);
        });

        // 4. 构建 TracerProvider（注册 3 个 ActivitySource + 自定义 Exporter）
        services.AddSingleton(sp =>
        {
            var exporter = sp.GetRequiredService<AppTelemetryExporter>();
            var resource = TelemetryService.GetResourceBuilder().Build();

            return Sdk.CreateTracerProviderBuilder()
                .SetResourceBuilder(ResourceBuilder.CreateDefault()
                    .AddAttributes(new Dictionary<string, object>
                    {
                        ["service.name"] = "SeatFlow",
                        ["service.version"] = VersionInfo.Version
                    }))
                .AddSource("SeatFlow.App")
                .AddSource("SeatFlow.UI")
                .AddSource("SeatFlow.Features")
                .SetSampler(new AlwaysOnSampler())
                .AddProcessor(new SimpleActivityExportProcessor(exporter))
                .Build();
        });

        // 5. 构建 MeterProvider（注册 Meter + PeriodicExportingMetricReader）
        services.AddSingleton(sp =>
        {
            var exporter = sp.GetRequiredService<AppTelemetryMetricExporter>();
            var settingsRepo = sp.GetRequiredService<IAppSettingsRepository>();
            int intervalMs;
            try
            {
                var settings = Task.Run(() => settingsRepo.LoadAsync()).GetAwaiter().GetResult();
                intervalMs = settings.Telemetry.MetricSnapshotIntervalSeconds * 1000;
            }
            catch
            {
                intervalMs = 120_000;
            }

            return Sdk.CreateMeterProviderBuilder()
                .SetResourceBuilder(ResourceBuilder.CreateDefault()
                    .AddAttributes(new Dictionary<string, object>
                    {
                        ["service.name"] = "SeatFlow",
                        ["service.version"] = VersionInfo.Version
                    }))
                .AddMeter("SeatFlow.App.Metrics")
                .AddReader(new PeriodicExportingMetricReader(exporter, intervalMs)
                {
                    TemporalityPreference = MetricReaderTemporalityPreference.Delta
                })
                .Build();
        });

        return services;
    }
}
