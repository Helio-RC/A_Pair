using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SeatFlow.Presentation.Avalonia.Telemetry;

/// <summary>
/// 遥测 HTTP 发送器。从 Channel 读取事件，批量发送到 Web API。
/// 实现自适应退避、熔断、Gzip 压缩等流量优化策略。
/// 仅由 Timer 驱动 flush（可选容量阈值触发立即发送）。
/// </summary>
public sealed class TelemetryHttpClient : IDisposable
{
    private readonly Channel<TelemetryEvent> _channel;
    private readonly HttpClient _httpClient;
    private readonly ILogger<TelemetryHttpClient> _logger;
    private readonly Timer _flushTimer;
    private readonly int _maxBatchSize;
    private readonly int _normalFlushIntervalMs;
    private readonly bool _enableCompression;
    private readonly CancellationTokenSource _cts = new();
    private int _consecutiveFailures;
    private bool _circuitBroken;
    private int _flushInProgress; // 0 = idle, 1 = flushing

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public TelemetryHttpClient(
        string serverUrl,
        int flushIntervalSeconds,
        int maxBatchSize,
        bool enableCompression,
        ILogger<TelemetryHttpClient> logger)
    {
        _maxBatchSize = maxBatchSize;
        _normalFlushIntervalMs = flushIntervalSeconds * 1000;
        _enableCompression = enableCompression;
        _logger = logger;

        _channel = Channel.CreateBounded<TelemetryEvent>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(serverUrl),
            Timeout = TimeSpan.FromSeconds(10)
        };
        _httpClient.DefaultRequestHeaders.Add("Referer", "https://seatflow.work");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

        _flushTimer = new Timer(OnFlushTimerTick, null,
            TimeSpan.FromMilliseconds(_normalFlushIntervalMs),
            TimeSpan.FromMilliseconds(_normalFlushIntervalMs));
    }

    /// <summary>尝试将事件写入队列（非阻塞）。队列满时自动 DropOldest。</summary>
    public bool TryEnqueue(TelemetryEvent evt)
    {
        if (_channel.Writer.TryWrite(evt))
        {
            // 容量阈值触发：积压超过 MaxBatchSize 时立即 flush
            if (_channel.Reader.Count >= _maxBatchSize)
            {
                _ = Task.Run(async () =>
                {
                    try { await FlushBatchAsync(_cts.Token); }
                    catch (Exception ex) { _logger.LogDebug(ex, "容量阈值刷新异常"); }
                });
            }
            return true;
        }
        return false;
    }

    /// <summary>强制刷新。熔断状态下也会尝试发送一次。</summary>
    public async Task FlushAsync(TimeSpan timeout)
    {
        _circuitBroken = false; // 允许最后一次尝试
        using var flushCts = new CancellationTokenSource(timeout);
        try
        {
            await FlushBatchAsync(flushCts.Token);
        }
        catch (OperationCanceledException) { /* 超时放弃 */ }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "遥测退出刷新失败");
        }
    }

    private void OnFlushTimerTick(object? state)
    {
        if (_circuitBroken) return;

        // 防止重入：如果上一次 flush 还在进行中，跳过本次
        if (Interlocked.CompareExchange(ref _flushInProgress, 1, 0) != 0) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await FlushBatchAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "遥测定时刷新异常");
            }
            finally
            {
                Interlocked.Exchange(ref _flushInProgress, 0);
            }
        });
    }

    private async Task FlushBatchAsync(CancellationToken ct)
    {
        var batch = new List<TelemetryEvent>(_maxBatchSize);

        while (batch.Count < _maxBatchSize && _channel.Reader.TryRead(out var evt))
            batch.Add(evt);

        if (batch.Count == 0) return;

        await SendBatchAsync(batch, ct);
    }

    private async Task SendBatchAsync(List<TelemetryEvent> batch, CancellationToken ct)
    {
        try
        {
            var request = new TelemetryBatchRequest { Events = batch };

            HttpContent content;
            if (_enableCompression)
            {
                using var uncompressed = new MemoryStream();
                await JsonSerializer.SerializeAsync(uncompressed, request, JsonOptions, ct);
                uncompressed.Position = 0;

                using var compressed = new MemoryStream();
                await using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
                {
                    await uncompressed.CopyToAsync(gzip, ct);
                }

                content = new ByteArrayContent(compressed.ToArray());
                content.Headers.ContentType = new("application/json");
                content.Headers.ContentEncoding.Add("gzip");
            }
            else
            {
                content = JsonContent.Create(request, options: JsonOptions);
            }

            var response = await _httpClient.PostAsync("", content, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<TelemetryBatchResponse>(JsonOptions, ct);
            _logger.LogDebug("遥测上报成功: Accepted={Accepted}, Sent={Sent}",
                result?.Accepted ?? 0, batch.Count);

            // 成功 → 重置退避
            _consecutiveFailures = 0;
            UpdateFlushInterval();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogDebug("遥测被限流(429)，{Count} 条事件丢弃", batch.Count);
            OnSendFailure();
        }
        catch (HttpRequestException)
        {
            _logger.LogDebug("遥测服务器不可达，{Count} 条事件丢弃", batch.Count);
            OnSendFailure();
        }
        catch (TaskCanceledException)
        {
            _logger.LogDebug("遥测请求超时，{Count} 条事件丢弃", batch.Count);
            OnSendFailure();
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or ThreadAbortException))
        {
            _logger.LogDebug(ex, "遥测发送失败，{Count} 条事件丢弃", batch.Count);
            OnSendFailure();
        }
    }

    private void OnSendFailure()
    {
        _consecutiveFailures++;
        if (_consecutiveFailures >= 4)
        {
            if (!_circuitBroken)
            {
                _circuitBroken = true;
                _logger.LogDebug("遥测熔断：连续失败 {Count} 次，停止发送", _consecutiveFailures);
            }
        }
        else
        {
            UpdateFlushInterval();
        }
    }

    private void UpdateFlushInterval()
    {
        var backoffMs = _normalFlushIntervalMs * (int)Math.Pow(2, _consecutiveFailures);
        _flushTimer.Change(TimeSpan.FromMilliseconds(backoffMs), TimeSpan.FromMilliseconds(backoffMs));
    }

    public void Dispose()
    {
        _cts.Cancel();
        _flushTimer.Dispose();
        _httpClient.Dispose();
        _cts.Dispose();
    }
}
