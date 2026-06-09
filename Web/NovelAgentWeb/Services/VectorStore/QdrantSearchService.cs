using TM.Services.Modules.ProjectData.Interfaces;

namespace TM.Web.NovelAgentWeb.Services.VectorStore;

/// <summary>
/// Service for performing vector similarity search via Qdrant with user isolation.
/// Wraps IVectorStore to provide chapter/chunk-specific search operations.
/// </summary>
public class QdrantSearchService
{
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<QdrantSearchService> _logger;

    public QdrantSearchService(
        IVectorStore vectorStore,
        ILogger<QdrantSearchService> logger)
    {
        _vectorStore = vectorStore;
        _logger = logger;
    }

    /// <summary>
    /// Search for similar chunks within a project with user isolation.
    /// Returns chunk keys in format: "{chapterId}#{position}"
    /// </summary>
    public async Task<IReadOnlyList<VectorSearchHit>> SearchChunksAsync(
        string projectId,
        string userId,
        float[] queryVector,
        int topK,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(projectId))
            throw new ArgumentException("Project ID is required", nameof(projectId));

        if (string.IsNullOrEmpty(userId))
            throw new ArgumentException("User ID is required", nameof(userId));

        if (queryVector == null || queryVector.Length == 0)
            return Array.Empty<VectorSearchHit>();

        if (topK <= 0)
            return Array.Empty<VectorSearchHit>();

        try
        {
            // Build filters for user and source type isolation
            var filters = new Dictionary<string, object>
            {
                ["user_id"] = userId,
                ["source_type"] = "chunk"
            };

            var results = await _vectorStore.SearchSimilarAsync(
                projectId,
                queryVector,
                topK,
                filters,
                cancellationToken);

            // Map SearchResult to VectorSearchHit format
            return results
                .Select(r => new VectorSearchHit(r.Id, r.Score))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Qdrant chunk search failed for project {ProjectId}, user {UserId}, topK {TopK}",
                projectId, userId, topK);
            return Array.Empty<VectorSearchHit>();
        }
    }

    /// <summary>
    /// Search for similar chapters within a project with user isolation.
    /// Returns chapter IDs directly.
    /// </summary>
    public async Task<IReadOnlyList<VectorSearchHit>> SearchChaptersAsync(
        string projectId,
        string userId,
        float[] queryVector,
        int topK,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(projectId))
            throw new ArgumentException("Project ID is required", nameof(projectId));

        if (string.IsNullOrEmpty(userId))
            throw new ArgumentException("User ID is required", nameof(userId));

        if (queryVector == null || queryVector.Length == 0)
            return Array.Empty<VectorSearchHit>();

        if (topK <= 0)
            return Array.Empty<VectorSearchHit>();

        try
        {
            // Build filters for user and source type isolation
            var filters = new Dictionary<string, object>
            {
                ["user_id"] = userId,
                ["source_type"] = "chapter"
            };

            var results = await _vectorStore.SearchSimilarAsync(
                projectId,
                queryVector,
                topK,
                filters,
                cancellationToken);

            // Map SearchResult to VectorSearchHit format
            return results
                .Select(r => new VectorSearchHit(r.Id, r.Score))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Qdrant chapter search failed for project {ProjectId}, user {UserId}, topK {TopK}",
                projectId, userId, topK);
            return Array.Empty<VectorSearchHit>();
        }
    }

    /// <summary>
    /// Hybrid coarse-to-fine search: chapter-level filtering, then chunk-level retrieval.
    /// Mimics the existing FileBasedVectorIndex pattern used in WriterPlugin.LongDistanceRecall.
    /// </summary>
    public async Task<IReadOnlyList<VectorSearchHit>> SearchWithinChaptersAsync(
        string projectId,
        string userId,
        float[] queryVector,
        IReadOnlySet<string> chapterIds,
        int topK,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(projectId))
            throw new ArgumentException("Project ID is required", nameof(projectId));

        if (string.IsNullOrEmpty(userId))
            throw new ArgumentException("User ID is required", nameof(userId));

        if (queryVector == null || queryVector.Length == 0 || topK <= 0)
            return Array.Empty<VectorSearchHit>();

        if (chapterIds == null || chapterIds.Count == 0)
            return Array.Empty<VectorSearchHit>();

        try
        {
            // Build filters: user_id + source_type=chunk + chapter_id IN (...)
            // Note: Qdrant filter syntax for "IN" may require iterating or using advanced filters
            // For now, we'll search all chunks and filter in-memory (can optimize later)
            var filters = new Dictionary<string, object>
            {
                ["user_id"] = userId,
                ["source_type"] = "chunk"
            };

            var results = await _vectorStore.SearchSimilarAsync(
                projectId,
                queryVector,
                topK * 3, // Fetch more to account for filtering
                filters,
                cancellationToken);

            // Filter by chapter IDs and extract chunk info
            var filtered = new List<VectorSearchHit>();
            foreach (var result in results)
            {
                if (string.IsNullOrEmpty(result.ChapterId))
                    continue;

                if (!chapterIds.Contains(result.ChapterId))
                    continue;

                filtered.Add(new VectorSearchHit(result.Id, result.Score));

                if (filtered.Count >= topK)
                    break;
            }

            return filtered;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Qdrant within-chapters search failed for project {ProjectId}, user {UserId}, chapters {ChapterCount}, topK {TopK}",
                projectId, userId, chapterIds.Count, topK);
            return Array.Empty<VectorSearchHit>();
        }
    }

    /// <summary>
    /// Check if Qdrant collection exists and has vectors for the project.
    /// Used for fallback decision making.
    /// </summary>
    public async Task<bool> IsAvailableForProjectAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(projectId))
            return false;

        try
        {
            var exists = await _vectorStore.CollectionExistsAsync(projectId, cancellationToken);
            if (!exists)
                return false;

            var info = await _vectorStore.GetCollectionInfoAsync(projectId, cancellationToken);
            return info != null && info.VectorCount > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check Qdrant availability for project {ProjectId}", projectId);
            return false;
        }
    }
}
