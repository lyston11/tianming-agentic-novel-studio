using TM.Services.Framework.AI.Embedding;

namespace TM.Web.NovelAgentWeb.Services.Embedding;

/// <summary>
/// Stub implementation of IMicroEmbeddingService for development/testing.
/// Returns random vectors until a proper embedding service is configured.
/// </summary>
public class StubEmbeddingService : IMicroEmbeddingService
{
    private readonly ILogger<StubEmbeddingService> _logger;
    private const int VectorDimension = 1536; // Match QdrantCollectionManager dimension
    private readonly Random _random = new Random(42); // Fixed seed for consistency

    public StubEmbeddingService(ILogger<StubEmbeddingService> logger)
    {
        _logger = logger;
        _logger.LogWarning("Using StubEmbeddingService - vectors will be random. Configure BgeSmallZhEmbeddingService for production.");
    }

    public int Dimension => VectorDimension;

    public Task<float[]> EncodeAsync(string text, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default)
    {
        var vector = new float[VectorDimension];
        for (int i = 0; i < VectorDimension; i++)
        {
            vector[i] = (float)_random.NextDouble() * 2 - 1; // Range [-1, 1]
        }

        // Normalize
        var norm = Math.Sqrt(vector.Sum(v => v * v));
        if (norm > 0)
        {
            for (int i = 0; i < VectorDimension; i++)
            {
                vector[i] /= (float)norm;
            }
        }

        return Task.FromResult(vector);
    }

    public async Task<float[][]> EncodeBatchAsync(IReadOnlyList<string> texts, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default)
    {
        var results = new float[texts.Count][];
        for (int i = 0; i < texts.Count; i++)
        {
            results[i] = await EncodeAsync(texts[i], mode, ct);
        }
        return results;
    }

    public bool IsModelReady()
    {
        return true; // Stub is always ready
    }

    public void ReleaseSession()
    {
        // No-op for stub
    }
}
