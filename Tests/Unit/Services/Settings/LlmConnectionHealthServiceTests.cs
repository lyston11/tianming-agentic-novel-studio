using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TM.Web.NovelAgentWeb.Services.Settings;
using Xunit;

namespace Tests.Unit.Services.Settings;

public sealed class LlmConnectionHealthServiceTests
{
    [Fact]
    public async Task CheckAsync_WhenAnthropicReturnsUnauthorizedReportsAuthenticationFailureWithoutLeakingKey()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("invalid api key sk-secret")
        });
        var service = new LlmConnectionHealthService(new SingleHttpClientFactory(handler));

        var result = await service.CheckAsync(new LlmConnectionHealthInput(
            Provider: "anthropic",
            BaseUrl: "https://example.test/anthropic",
            Model: "mimo-v2.5-pro",
            ApiKey: "sk-secret"));

        Assert.True(result.IsConfigured);
        Assert.False(result.IsAuthenticated);
        Assert.Equal("authentication_failed", result.Status);
        Assert.Equal("authentication", result.FailureStage);
        Assert.Equal(401, result.StatusCode);
        Assert.Equal("anthropic", result.Provider);
        Assert.Equal("mimo-v2.5-pro", result.Model);
        Assert.Equal("https://example.test/anthropic", result.BaseUrl);
        Assert.Contains("用户设置", result.RecommendedAction);
        Assert.DoesNotContain("sk-secret", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsync_WhenApiKeyMissingReturnsMissingConfigWithoutCallingNetwork()
    {
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("network should not be called"));
        var service = new LlmConnectionHealthService(new SingleHttpClientFactory(handler));

        var result = await service.CheckAsync(new LlmConnectionHealthInput(
            Provider: "anthropic",
            BaseUrl: "https://example.test/anthropic",
            Model: "mimo-v2.5-pro",
            ApiKey: ""));

        Assert.False(result.IsConfigured);
        Assert.False(result.IsReachable);
        Assert.False(result.IsAuthenticated);
        Assert.Equal("missing_config", result.Status);
        Assert.Equal("config", result.FailureStage);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task CheckAsync_WhenOpenAiCompatibleProviderSucceedsReportsReady()
    {
        Uri? requestedUri = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            requestedUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"content":"pong"}}]}""")
            };
        });
        var service = new LlmConnectionHealthService(new SingleHttpClientFactory(handler));

        var result = await service.CheckAsync(new LlmConnectionHealthInput(
            Provider: "openai",
            BaseUrl: "https://example.test/v1",
            Model: "gpt-test",
            ApiKey: "sk-ok"));

        Assert.Equal("ready", result.Status);
        Assert.True(result.IsConfigured);
        Assert.True(result.IsReachable);
        Assert.True(result.IsAuthenticated);
        Assert.Equal("https://example.test/v1/chat/completions", requestedUri?.ToString());
    }

    private sealed class SingleHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public SingleHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_respond(request));
        }
    }
}
