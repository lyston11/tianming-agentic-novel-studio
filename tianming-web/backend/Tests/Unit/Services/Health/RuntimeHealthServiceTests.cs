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
    public async Task CheckAsync_ReturnsPostgresRedisQdrantAndEmbeddingEntries()
    {
        var redis = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var service = new RuntimeHealthService(
            redis,
            new StubPostgresHealthProbe(true),
            new StubQdrantHealthProbe(true),
            new EmbeddingRuntimeStatus(),
            NullLogger<RuntimeHealthService>.Instance);

        var report = await service.CheckAsync();

        Assert.Equal(RuntimeHealthStatuses.Healthy, report.Status);
        Assert.Equal(RuntimeHealthStatuses.Healthy, report.Entries["postgresql"].Status);
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
            new StubPostgresHealthProbe(true),
            new StubQdrantHealthProbe(false),
            new EmbeddingRuntimeStatus(),
            NullLogger<RuntimeHealthService>.Instance);

        var report = await service.CheckAsync();

        Assert.Equal(RuntimeHealthStatuses.Degraded, report.Entries["qdrant"].Status);
        Assert.Contains("Qdrant", report.Entries["qdrant"].Reason);
    }

    [Fact]
    public async Task CheckAsync_ReportsPostgresReasonWhenProbeFails()
    {
        var redis = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var service = new RuntimeHealthService(
            redis,
            new StubPostgresHealthProbe(false),
            new StubQdrantHealthProbe(true),
            new EmbeddingRuntimeStatus(),
            NullLogger<RuntimeHealthService>.Instance);

        var report = await service.CheckAsync();

        Assert.Equal(RuntimeHealthStatuses.Degraded, report.Status);
        Assert.Equal(RuntimeHealthStatuses.Degraded, report.Entries["postgresql"].Status);
        Assert.Contains("PostgreSQL", report.Entries["postgresql"].Reason);
    }

    private sealed class StubPostgresHealthProbe : IPostgresHealthProbe
    {
        private readonly bool _healthy;

        public StubPostgresHealthProbe(bool healthy)
        {
            _healthy = healthy;
        }

        public Task<PostgresHealthProbeResult> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_healthy
                ? PostgresHealthProbeResult.Healthy()
                : PostgresHealthProbeResult.Degraded("PostgreSQL test probe failed."));
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
