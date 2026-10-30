using System.Net;
using System.Text;
using System.Text.Json;
using WeeklyReport.Application;
using WeeklyReport.Infrastructure.Ai;

namespace WeeklyReport.IntegrationTests;

public class DeepSeekClientTests
{
    [Fact]
    public async Task Generate_SendsOnlyTheConfirmedRequestToTheOfficialEndpoint()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Response(
            HttpStatusCode.OK, """{"choices":[{"finish_reason":"stop","message":{"content":"周报草稿"}}]}""")));
        using var client = new DeepSeekClient(handler);
        var request = Request();

        var result = await client.GenerateAsync(request);

        Assert.Equal("周报草稿", result);
        Assert.Equal(DeepSeekClient.Endpoint, handler.Uri);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("synthetic-key", handler.AuthorizationParameter);
        Assert.Equal("application/json", handler.ContentType);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("deepseek-flash", body.RootElement.GetProperty("model").GetString());
        Assert.False(body.RootElement.GetProperty("stream").GetBoolean());
        var messages = body.RootElement.GetProperty("messages");
        Assert.Equal("整理周报", messages[0].GetProperty("content").GetString());
        Assert.Equal("测试工作记录", messages[1].GetProperty("content").GetString());
        Assert.DoesNotContain("synthetic-key", request.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("测试工作记录", request.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Request_RequiresExplicitUserConfirmation()
    {
        Assert.Throws<InvalidOperationException>(
            () => new AiGenerationRequest("synthetic-key", "deepseek-flash", "整理", "内容", false));
    }

    [Theory]
    [InlineData(400, AiFailureKind.InvalidRequest)]
    [InlineData(401, AiFailureKind.Authentication)]
    [InlineData(402, AiFailureKind.InsufficientBalance)]
    [InlineData(403, AiFailureKind.Authentication)]
    [InlineData(429, AiFailureKind.RateLimit)]
    [InlineData(500, AiFailureKind.ServiceUnavailable)]
    [InlineData(302, AiFailureKind.ServiceUnavailable)]
    public async Task Generate_MapsHttpFailuresWithoutExposingResponseBody(int status, AiFailureKind expected)
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Response((HttpStatusCode)status, "synthetic-private-body")));
        using var client = new DeepSeekClient(handler);

        var exception = await Assert.ThrowsAsync<AiServiceException>(() => client.GenerateAsync(Request()));

        Assert.Equal(expected, exception.Kind);
        Assert.DoesNotContain("synthetic-private-body", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"choices\":[42]}")]
    [InlineData("{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":\"partial\"}}]}")]
    [InlineData("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"\"}}]}")]
    public async Task Generate_RejectsMalformedOrIncompleteResponses(string response)
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK, response)));
        using var client = new DeepSeekClient(handler);

        var exception = await Assert.ThrowsAsync<AiServiceException>(() => client.GenerateAsync(Request()));

        Assert.Equal(AiFailureKind.InvalidResponse, exception.Kind);
    }

    [Fact]
    public async Task Generate_EnforcesTheExactResponseSizeLimit()
    {
        const int maximumBytes = 2 * 1024 * 1024;
        const string prefix = "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"";
        const string suffix = "\"}}]}";
        var textLength = maximumBytes - prefix.Length - suffix.Length;
        var valid = prefix + new string('a', textLength) + suffix;
        var oversized = prefix + new string('a', textLength + 1) + suffix;
        var calls = 0;
        var handler = new FakeHandler((_, _) =>
            Task.FromResult(Response(HttpStatusCode.OK, calls++ == 0 ? valid : oversized)));
        using var client = new DeepSeekClient(handler);

        Assert.Equal(textLength, (await client.GenerateAsync(Request())).Length);
        var exception = await Assert.ThrowsAsync<AiServiceException>(() => client.GenerateAsync(Request()));
        Assert.Equal(AiFailureKind.InvalidResponse, exception.Kind);
    }

    [Fact]
    public async Task Generate_EnforcesLimitForUnknownLengthStreams()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new NonSeekableStream(new byte[2 * 1024 * 1024 + 1]))
        }));
        using var client = new DeepSeekClient(handler);

        var exception = await Assert.ThrowsAsync<AiServiceException>(() => client.GenerateAsync(Request()));

        Assert.Equal(AiFailureKind.InvalidResponse, exception.Kind);
    }

    [Fact]
    public async Task Generate_ReportsTimeoutWithoutRetrying()
    {
        var handler = new FakeHandler(WaitUntilCancelled);
        using var client = new DeepSeekClient(handler, TimeSpan.FromMilliseconds(50));

        var exception = await Assert.ThrowsAsync<AiServiceException>(() => client.GenerateAsync(Request()));

        Assert.Equal(AiFailureKind.Timeout, exception.Kind);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Generate_PropagatesUserCancellation()
    {
        var handler = new FakeHandler(WaitUntilCancelled);
        using var client = new DeepSeekClient(handler);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GenerateAsync(Request(), cancellation.Token));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Generate_ReportsNetworkFailureWithoutExposingDetails()
    {
        var handler = new FakeHandler((_, _) => throw new HttpRequestException("synthetic-private-detail"));
        using var client = new DeepSeekClient(handler);

        var exception = await Assert.ThrowsAsync<AiServiceException>(() => client.GenerateAsync(Request()));

        Assert.Equal(AiFailureKind.Network, exception.Kind);
        Assert.DoesNotContain("synthetic-private-detail", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generate_InvalidHeaderDoesNotSendARequest()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK, "{}")));
        using var client = new DeepSeekClient(handler);
        var request = new AiGenerationRequest("bad\r\nheader", "deepseek-flash", "整理", "内容", true);

        var exception = await Assert.ThrowsAsync<AiServiceException>(() => client.GenerateAsync(request));

        Assert.Equal(AiFailureKind.InvalidRequest, exception.Kind);
        Assert.Equal(0, handler.CallCount);
    }

    private static AiGenerationRequest Request() =>
        new("synthetic-key", "deepseek-flash", "整理周报", "测试工作记录", true);

    private static HttpResponseMessage Response(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static async Task<HttpResponseMessage> WaitUntilCancelled(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("The delay should have been cancelled.");
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public Uri? Uri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string? ContentType { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            Uri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return await respond(request, cancellationToken);
        }
    }

    private sealed class NonSeekableStream(byte[] content) : Stream
    {
        private readonly MemoryStream _inner = new(content);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
