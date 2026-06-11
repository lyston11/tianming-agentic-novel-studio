namespace TM.Web.NovelAgentWeb.Services.VectorStore;

/// <summary>
/// Vector store interface for managing embeddings and similarity search.
/// Collections are named novel_agent_{userId} for multi-tenant isolation.
/// Project-level filtering is done via payload filters.
/// </summary>
public interface IVectorStore
{
    /// <summary>
    /// Initialize a collection for a user. Collection name: novel_agent_{userId}
    /// </summary>
    Task InitializeUserCollectionAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Upsert vectors with metadata payload. Collection is derived from vector.UserId.
    /// </summary>
    Task UpsertVectorsAsync(string userId, List<VectorData> vectors, CancellationToken ct = default);

    /// <summary>
    /// Search for similar vectors with filtering by user_id and optional project_id.
    /// </summary>
    Task<List<SearchResult>> SearchSimilarAsync(
        string userId,
        float[] queryVector,
        int topK = 10,
        Dictionary<string, object>? filters = null,
        CancellationToken ct = default);

    /// <summary>
    /// Delete a user's collection and all its vectors.
    /// </summary>
    Task DeleteUserCollectionAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Check if a collection exists for a user.
    /// </summary>
    Task<bool> CollectionExistsAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Get collection statistics.
    /// </summary>
    Task<CollectionInfo?> GetCollectionInfoAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Delete vectors by filter within a user's collection.
    /// </summary>
    Task DeleteVectorsByFilterAsync(
        string userId,
        Dictionary<string, object> filters,
        CancellationToken ct = default);
}

/// <summary>
/// Vector data with payload for storage.
/// </summary>
public class VectorData
{
    public required string Id { get; set; }
    public required float[] Vector { get; set; }
    public required string UserId { get; set; }
    public required string ProjectId { get; set; }
    public required string SourceType { get; set; }
    public required string SourceId { get; set; }
    public string? ChapterId { get; set; }
    public int? ChunkIndex { get; set; }
    public string? Content { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Search result with score and payload.
/// </summary>
public class SearchResult
{
    public required string Id { get; set; }
    public required float Score { get; set; }
    public string? UserId { get; set; }
    public string? ProjectId { get; set; }
    public string? SourceType { get; set; }
    public string? SourceId { get; set; }
    public string? ChapterId { get; set; }
    public int? ChunkIndex { get; set; }
    public string? Content { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Collection information and statistics.
/// </summary>
public class CollectionInfo
{
    public required string Name { get; set; }
    public required long VectorCount { get; set; }
    public required int VectorDimension { get; set; }
    public required string DistanceMetric { get; set; }
    public string? IndexType { get; set; }
}
