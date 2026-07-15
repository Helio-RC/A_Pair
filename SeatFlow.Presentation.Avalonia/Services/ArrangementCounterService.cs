using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// 内存中的排座次数计数器实现。<br/>
/// 在应用生命周期内累积排座次数，离开座位安排页面时通过 HTTP 上报。<br/>
/// 使用两阶段 API 调用：GET 获取 token → POST 提交累计值。<br/>
/// 单例 —— 每个应用生命周期只有一个计数器实例。
/// </summary>
public sealed class ArrangementCounterService : IArrangementCounterService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ArrangementCounterService> _logger;
    private int _count;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 计数器 API 基址。
    /// </summary>
    internal const string BaseUrl = "https://seatflow.work/api/";

    public ArrangementCounterService(ILogger<ArrangementCounterService>? logger = null)
    {
        _logger = logger ?? NullLogger<ArrangementCounterService>.Instance;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(10)
        };
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    /// <inheritdoc />
    public void Increment()
    {
        Interlocked.Increment(ref _count);
    }

    /// <inheritdoc />
    public async Task<int> ReportAndResetAsync()
    {
        // 原子读取并重置
        var value = Interlocked.Exchange(ref _count, 0);
        if (value <= 0)
            return 0;

        // 第一步：获取递增令牌
        TokenResponse? tokenResponse;
        try
        {
            tokenResponse = await _httpClient.GetFromJsonAsync<TokenResponse>(
                "/api/counters/token?name=arrangements", JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "获取排座计数器令牌失败 (value={Value})", value);
            return value;
        }

        if (tokenResponse is null || string.IsNullOrEmpty(tokenResponse.Token))
        {
            _logger.LogDebug("排座计数器令牌响应为空");
            return value;
        }

        if (tokenResponse.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _logger.LogDebug("排座计数器令牌已过期 (expiresAt={ExpiresAt})", tokenResponse.ExpiresAt);
            return value;
        }

        // 第二步：提交累计值
        try
        {
            var request = new IncrementRequest
            {
                Name = "arrangements",
                Value = value,
                Token = tokenResponse.Token,
                Nonce = tokenResponse.Nonce
            };

            var response = await _httpClient.PostAsJsonAsync(
                "/api/counters/public/increment", request, JsonOptions);

            response.EnsureSuccessStatusCode();

            var result = await response.Content
                .ReadFromJsonAsync<IncrementResponse>(JsonOptions);

            _logger.LogDebug("排座计数器已上报: value={Value}, newTotal={NewTotal}",
                value, result?.Value);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "上报排座计数器失败 (value={Value})", value);
        }

        return value;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    // ── 内部 DTO ──

    private sealed class TokenResponse
    {
        public string Token { get; init; } = string.Empty;
        public string Nonce { get; init; } = string.Empty;
        public DateTimeOffset ExpiresAt { get; init; }
    }

    private sealed class IncrementRequest
    {
        public string Name { get; init; } = string.Empty;
        public int Value { get; init; }
        public string Token { get; init; } = string.Empty;
        public string Nonce { get; init; } = string.Empty;
    }

    private sealed class IncrementResponse
    {
        public string Name { get; init; } = string.Empty;
        public int Value { get; init; }
    }
}
