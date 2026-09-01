using TM.Web.NovelAgentWeb.Services.VectorStore;

namespace TM.Tests.NovelAgentRegression.VectorStore;

public sealed class ProjectVectorStoreAdapter
{
    private readonly IVectorStore _inner;

    public ProjectVectorStoreAdapter(IVectorStore inner)
    {
        _inner = inner;
    }

    public Task InitializeProjectCollectionAsync(string userId, CancellationToken ct = default)
        => _inner.InitializeUserCollectionAsync(userId, ct);

    public Task DeleteCollectionAsync(string userId, CancellationToken ct = default)
        => _inner.DeleteUserCollectionAsync(userId, ct);

    public Task<bool> CollectionExistsAsync(string userId, CancellationToken ct = default)
        => _inner.CollectionExistsAsync(userId, ct);

    public Task<CollectionInfo?> GetCollectionInfoAsync(string userId, CancellationToken ct = default)
        => _inner.GetCollectionInfoAsync(userId, ct);

    public Task UpsertVectorsAsync(string userId, List<VectorData> vectors, CancellationToken ct = default)
        => _inner.UpsertVectorsAsync(userId, vectors, ct);

    public Task<List<SearchResult>> SearchSimilarAsync(
        string userId,
        float[] queryVector,
        int topK = 10,
        Dictionary<string, object>? filters = null,
        CancellationToken ct = default)
        => _inner.SearchSimilarAsync(userId, queryVector, topK, filters, ct);

    public Task DeleteVectorsByFilterAsync(
        string userId,
        Dictionary<string, object> filters,
        CancellationToken ct = default)
        => _inner.DeleteVectorsByFilterAsync(userId, filters, ct);
}
