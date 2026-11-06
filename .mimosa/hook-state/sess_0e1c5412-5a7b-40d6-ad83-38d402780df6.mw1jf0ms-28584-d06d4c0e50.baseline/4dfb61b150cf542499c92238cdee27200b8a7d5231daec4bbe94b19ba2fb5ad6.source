using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using WeeklyReport.Application;

namespace WeeklyReport.Infrastructure.Ai;

public sealed class DeepSeekClient : IAiDraftService, IDisposable
{
    public static readonly Uri Endpoint = new("https://api.deepseek.com/chat/completions");
    private const int MaximumResponseBytes = 2 * 1024 * 1024;
    private readonly HttpClient _client;
    private readonly TimeSpan _timeout;

    public DeepSeekClient(HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(90);
        if (_timeout <= TimeSpan.Zero || _timeout.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        _client = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public async Task<string> GenerateAsync(
        AiGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        try
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey);
        }
        catch (FormatException exception)
        {
            throw new AiServiceException(AiFailureKind.InvalidRequest, "DeepSeek API Key 格式无效。", exception);
        }

        var payload = new
        {
            model = request.Model,
            messages = new[]
            {
                new { role = "system", content = request.Instruction },
                new { role = "user", content = request.Content }
            },
            stream = false
        };
        message.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(payload));
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

        try
        {
            using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateHttpError(response.StatusCode);
            }

            var bytes = await ReadLimitedAsync(response.Content, timeout.Token);
            return ParseResponse(bytes);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiServiceException(AiFailureKind.Timeout, "DeepSeek 请求超时，请稍后手动重试。", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new AiServiceException(AiFailureKind.Network, "无法连接 DeepSeek，请检查网络后手动重试。", exception);
        }
        catch (IOException exception)
        {
            throw new AiServiceException(AiFailureKind.Network, "读取 DeepSeek 响应失败，请稍后手动重试。", exception);
        }
    }

    private static AiServiceException CreateHttpError(HttpStatusCode status) => (int)status switch
    {
        400 or 422 => new(AiFailureKind.InvalidRequest, "DeepSeek 请求参数无效，请检查模型和输入内容。"),
        401 or 403 => new(AiFailureKind.Authentication, "DeepSeek 认证失败，请检查 API Key 和账号权限。"),
        402 => new(AiFailureKind.InsufficientBalance, "DeepSeek 账号余额不足，请检查账号额度。"),
        429 => new(AiFailureKind.RateLimit, "DeepSeek 请求过于频繁，请稍后手动重试。"),
        >= 500 => new(AiFailureKind.ServiceUnavailable, "DeepSeek 服务暂不可用，请稍后手动重试。"),
        >= 300 and < 400 => new(AiFailureKind.ServiceUnavailable, "DeepSeek 返回重定向，未向其他地址发送请求。"),
        _ => new(AiFailureKind.ServiceUnavailable, $"DeepSeek 请求失败（HTTP {(int)status}）。")
    };

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaximumResponseBytes)
        {
            throw new AiServiceException(AiFailureKind.InvalidResponse, "DeepSeek 返回内容过大，未应用结果。");
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var block = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(block.AsMemory(), cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaximumResponseBytes)
            {
                throw new AiServiceException(AiFailureKind.InvalidResponse, "DeepSeek 返回内容过大，未应用结果。");
            }

            buffer.Write(block, 0, read);
        }

        return buffer.ToArray();
    }

    private static string ParseResponse(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                throw new AiServiceException(AiFailureKind.InvalidResponse, "DeepSeek 返回格式无效，未应用结果。");
            }

            var choice = choices[0];
            if (choice.ValueKind != JsonValueKind.Object
                || !choice.TryGetProperty("finish_reason", out var finish)
                || finish.ValueKind != JsonValueKind.String
                || finish.GetString() != "stop")
            {
                throw new AiServiceException(AiFailureKind.InvalidResponse, "DeepSeek 返回未完成的内容，请调整输入后重试。");
            }

            if (!choice.TryGetProperty("message", out var message)
                || message.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(content.GetString()))
            {
                throw new AiServiceException(AiFailureKind.InvalidResponse, "DeepSeek 未返回有效正文，未应用结果。");
            }

            return content.GetString()!;
        }
        catch (JsonException exception)
        {
            throw new AiServiceException(AiFailureKind.InvalidResponse, "DeepSeek 返回格式无效，未应用结果。", exception);
        }
    }

    public void Dispose() => _client.Dispose();
}
