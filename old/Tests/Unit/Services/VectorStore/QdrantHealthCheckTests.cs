using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using Xunit;

namespace Tests.Unit.Services.VectorStore;

public sealed class QdrantHealthCheckTests
{
    [Fact]
    public async Task IsHealthyAsync_UsesConfiguredHttpBaseUrl()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Qdrant:BaseUrl"] = "http://qdrant-http:6333",
                ["Qdrant:Host"] = "qdrant-grpc",
                ["Qdrant:Port"] = "6334"
            })
            .Build();
        var factory = new CapturingHttpClientFactory(new Uri("http://qdrant-http:6333/healthz"));
        var healthCheck = new QdrantHealthCheck(factory, NullLogger<QdrantHealthCheck>.Instance, configuration);

        var healthy = await healthCheck.IsHealthyAsync();

        Assert.True(healthy);
        Assert.Equal(new Uri("http://qdrant-http:6333/healthz"), factory.LastRequestUri);
    }

    private sealed class CapturingHttpClientFactory : IHttpClientFactory
    {
        private readonly Uri _expectedUri;
        public Uri? LastRequestUri { get; private set; }

        public CapturingHttpClientFactory(Uri expectedUri)
        {
            _expectedUri = expectedUri;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new Handler(this, _expectedUri));
        }

        private sealed class Handler : HttpMessageHandler
        {
            private readonly CapturingHttpClientFactory _factory;
            private readonly Uri _expectedUri;

            public Handler(CapturingHttpClientFactory factory, Uri expectedUri)
            {
                _factory = factory;
                _expectedUri = expectedUri;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                _factory.LastRequestUri = request.RequestUri;
                var status = request.RequestUri == _expectedUri
                    ? HttpStatusCode.OK
                    : HttpStatusCode.NotFound;
                return Task.FromResult(new HttpResponseMessage(status));
            }
        }
    }
}
