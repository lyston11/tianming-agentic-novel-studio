namespace TM.Web.NovelAgentWeb.Services.VectorStore;

/// <summary>
/// Vector store interface for managing embeddings and similarity search.
/// </summary>
public interface IVectorStore
{
    /// <summary>
    /// Initialize a collection for a specific project with HNSW index configuration.
    /// Collection name follows pattern: project_{projectId}
    /// Vector dimension: 512 (bge-small-zh-v1.5 model)
    /// Distance metric: Cosine
    /// </summary>
    Task InitializeProjectCollectionAsync(string projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upsert chapter vectors with metadata payload.
    /// Batch size: 100 vectors per operation.
    /// </summary>
    Task UpsertVectorsAsync(
        string projectId,
        List<VectorData> vectors,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Search for similar vectors with filtering by user_id and project_id.
    /// Returns top-K most similar results.
    /// </summary>
    Task<List<SearchResult>> SearchSimilarAsync(
        string projectId,
        float[] queryVector,
        int topK = 10,
        Dictionary<string, object>? filters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a project collection and all its vectors.
    /// </summary>
    Task DeleteCollectionAsync(string projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a collection exists for a project.
    /// </summary>
    Task<bool> CollectionExistsAsync(string projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get collection statistics (vector count, size, etc.).
    /// </summary>
    Task<CollectionInfo?> GetCollectionInfoAsync(string projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete vectors by filter (e.g., delete all vectors for a specific chapter).
    /// </summary>
    Task DeleteVectorsByFilterAsync(
        string projectId,
        Dictionary<string, object> filters,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Vector data with payload for storage.
/// </summary>
public class VectorData
{
    /// <summary>
    /// Unique identifier for the vector (chapter_uuid or chunk_uuid).
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// 512-dimensional vector from bge-small-zh-v1.5 model.
    /// </summary>
    public required float[] Vector { get; set; }

    /// <summary>
    /// User ID for tenant isolation (required for filtering).
    /// </summary>
    public required string UserId { get; set; }

    /// <summary>
    /// Project ID (required for filtering).
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// Source type: chapter, chunk, or character.
    /// </summary>
    public required string SourceType { get; set; }

    /// <summary>
    /// Source entity ID (chapter ID, character ID, etc.).
    /// </summary>
    public required string SourceId { get; set; }

    /// <summary>
    /// Chapter ID for chapter or chunk vectors.
    /// </summary>
    public string? ChapterId { get; set; }

    /// <summary>
    /// Chunk index for chunk vectors (0-based).
    /// </summary>
    public int? ChunkIndex { get; set; }

    /// <summary>
    /// Original content text.
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// Additional metadata (JSON object).
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Search result with score and payload.
/// </summary>
public class SearchResult
{
    /// <summary>
    /// Vector ID.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Similarity score (cosine distance).
    /// </summary>
    public required float Score { get; set; }

    /// <summary>
    /// User ID from payload.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Project ID from payload.
    /// </summary>
    public string? ProjectId { get; set; }

    /// <summary>
    /// Source type from payload.
    /// </summary>
    public string? SourceType { get; set; }

    /// <summary>
    /// Source ID from payload.
    /// </summary>
    public string? SourceId { get; set; }

    /// <summary>
    /// Chapter ID from payload.
    /// </summary>
    public string? ChapterId { get; set; }

    /// <summary>
    /// Chunk index from payload.
    /// </summary>
    public int? ChunkIndex { get; set; }

    /// <summary>
    /// Content from payload.
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// Additional metadata from payload.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Collection information and statistics.
/// </summary>
public class CollectionInfo
{
    /// <summary>
    /// Collection name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Number of vectors in the collection.
    /// </summary>
    public required long VectorCount { get; set; }

    /// <summary>
    /// Vector dimension.
    /// </summary>
    public required int VectorDimension { get; set; }

    /// <summary>
    /// Distance metric (Cosine, Euclidean, etc.).
    /// </summary>
    public required string DistanceMetric { get; set; }

    /// <summary>
    /// Index type (HNSW, etc.).
    /// </summary>
    public string? IndexType { get; set; }
}
