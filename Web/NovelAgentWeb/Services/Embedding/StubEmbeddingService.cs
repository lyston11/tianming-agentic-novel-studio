using TM.Services.Framework.AI.Embedding;

namespace TM.Web.NovelAgentWeb.Services.Embedding;

/// <summary>
/// Stub implementation of IMicroEmbeddingService for development/testing.
/// Returns deterministic hash-based vectors (same text = same vector) for basic testing.
/// Replace with BgeSmallZhEmbeddingService for production.
/// </summary>
public class StubEmbeddingService : IMicroEmbeddingService
{
    private readonly ILogger<StubEmbeddingService> _logger;
    private const int VectorDimension = 512; // Match BGE-Small-ZH dimension

    public StubEmbeddingService(ILogger<StubEmbeddingService> logger)
    {
        _logger = logger;
        _logger.LogWarning("Using StubEmbeddingService - vectors are hash-based. Configure BgeSmallZhEmbeddingService for production.");
    }

    public int Dimension => VectorDimension;

    public Task<float[]> EncodeAsync(string text, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default)
    {
        // Deterministic: same text always produces same vector
        var seed = string.IsNullOrEmpty(text) ? 0 : text.GetHashCode();
        var rng = new Random(seed);
        var vector = new float[VectorDimension];
        for (int i = 0; i < VectorDimension; i++)
        {
            vector[i] = (float)rng.NextDouble() * 2 - 1;
        }

        // L2 normalize
        var norm = Math.Sqrt(vector.Sum(v => v * v));
        if (norm > 0)
        {
            for (int i = 0; i < VectorDimension; i++)
                vector[i] /= (float)norm;
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
