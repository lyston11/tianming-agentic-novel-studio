using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TM.Web.NovelAgentWeb.Services.Embedding;
using TM.Web.NovelAgentWeb.Services.Health;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using Xunit;

namespace Tests.Unit.Services.Health;

public sealed class RuntimeHealthServiceTests
{
    [Fact]
    public async Task CheckAsync_ReturnsRedisQdrantAndEmbeddingEntries()
    {
        var redis = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var service = new RuntimeHealthService(
            redis,
            new StubQdrantHealthProbe(true),
            new EmbeddingRuntimeStatus(),
            NullLogger<RuntimeHealthService>.Instance);

        var report = await service.CheckAsync();

        Assert.Equal(RuntimeHealthStatuses.Healthy, report.Status);
        Assert.Equal(RuntimeHealthStatuses.Healthy, report.Entries["redis"].Status);
        Assert.Equal(RuntimeHealthStatuses.Healthy, report.Entries["qdrant"].Status);
        Assert.Equal(RuntimeHealthStatuses.Healthy, report.Entries["embedding"].Status);
        Assert.Null(report.Entries["embedding"].Reason);
    }

    [Fact]
    public async Task CheckAsync_ReportsQdrantReasonWhenProbeFails()
    {
        var redis = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var service = new RuntimeHealthService(
            redis,
            new StubQdrantHealthProbe(false),
            new EmbeddingRuntimeStatus(),
            NullLogger<RuntimeHealthService>.Instance);

        var report = await service.CheckAsync();

        Assert.Equal(RuntimeHealthStatuses.Degraded, report.Entries["qdrant"].Status);
        Assert.Contains("Qdrant", report.Entries["qdrant"].Reason);
    }

    private sealed class StubQdrantHealthProbe : IQdrantHealthProbe
    {
        private readonly bool _healthy;

        public StubQdrantHealthProbe(bool healthy)
        {
            _healthy = healthy;
        }

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_healthy);
    }
}
