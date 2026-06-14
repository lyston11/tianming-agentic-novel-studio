using Microsoft.Extensions.Caching.Distributed;
using TM.Web.NovelAgentWeb.Services.Embedding;
using TM.Web.NovelAgentWeb.Services.VectorStore;

namespace TM.Web.NovelAgentWeb.Services.Health;

public sealed class RuntimeHealthService
{
    private readonly IDistributedCache _redis;
    private readonly IQdrantHealthProbe _qdrant;
    private readonly EmbeddingRuntimeStatus _embedding;
    private readonly ILogger<RuntimeHealthService> _logger;

    public RuntimeHealthService(
        IDistributedCache redis,
        IQdrantHealthProbe qdrant,
        EmbeddingRuntimeStatus embedding,
        ILogger<RuntimeHealthService> logger)
    {
        _redis = redis;
        _qdrant = qdrant;
        _embedding = embedding;
        _logger = logger;
    }

    public async Task<RuntimeHealthReport> CheckAsync(CancellationToken cancellationToken = default)
    {
        var entries = new Dictionary<string, RuntimeHealthEntry>(StringComparer.Ordinal)
        {
            ["redis"] = await CheckRedisAsync(cancellationToken),
            ["qdrant"] = await CheckQdrantAsync(cancellationToken),
            ["embedding"] = CheckEmbedding()
        };

        var status = entries.Values.Any(entry => entry.Status != RuntimeHealthStatuses.Healthy)
            ? RuntimeHealthStatuses.Degraded
            : RuntimeHealthStatuses.Healthy;

        return new RuntimeHealthReport(status, entries);
    }

    private async Task<RuntimeHealthEntry> CheckRedisAsync(CancellationToken cancellationToken)
    {
        var key = $"health:{Guid.NewGuid():N}";

        try
        {
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(15)
            };

            await _redis.SetStringAsync(key, "ok", options, cancellationToken);
            var value = await _redis.GetStringAsync(key, cancellationToken);
            await _redis.RemoveAsync(key, cancellationToken);

            if (value == "ok")
            {
                return RuntimeHealthEntry.Healthy();
            }

            return RuntimeHealthEntry.Degraded("Redis probe value mismatch.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis health probe failed");
            return RuntimeHealthEntry.Degraded(ex.Message);
        }
    }

    private async Task<RuntimeHealthEntry> CheckQdrantAsync(CancellationToken cancellationToken)
    {
        var healthy = await _qdrant.IsHealthyAsync(cancellationToken);
        return healthy
            ? RuntimeHealthEntry.Healthy()
            : RuntimeHealthEntry.Degraded("Qdrant /healthz returned non-success or was unreachable.");
    }

    private RuntimeHealthEntry CheckEmbedding()
    {
        var data = EmbeddingHealthResponse.From(_embedding);
        return _embedding.IsDegraded
            ? RuntimeHealthEntry.Degraded(_embedding.Warning, data)
            : RuntimeHealthEntry.Healthy(data);
    }
}

public sealed record RuntimeHealthReport(
    string Status,
    IReadOnlyDictionary<string, RuntimeHealthEntry> Entries);

public sealed record RuntimeHealthEntry(
    string Status,
    string? Reason = null,
    object? Data = null)
{
    public static RuntimeHealthEntry Healthy(object? data = null) =>
        new(RuntimeHealthStatuses.Healthy, Data: data);

    public static RuntimeHealthEntry Degraded(string reason, object? data = null) =>
        new(RuntimeHealthStatuses.Degraded, reason, data);
}

public static class RuntimeHealthStatuses
{
    public const string Healthy = "Healthy";
    public const string Degraded = "Degraded";
}
