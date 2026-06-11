using TM.Services.Modules.ProjectData.Interfaces;

namespace TM.Web.NovelAgentWeb.Services.VectorStore;

/// <summary>
/// Service for performing vector similarity search via Qdrant with user isolation.
/// Collections are named novel_agent_{userId}. Project-level filtering via payload.
/// </summary>
public class QdrantSearchService
{
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<QdrantSearchService> _logger;

    public QdrantSearchService(IVectorStore vectorStore, ILogger<QdrantSearchService> logger)
    {
        _vectorStore = vectorStore;
        _logger = logger;
    }

    public async Task<IReadOnlyList<VectorSearchHit>> SearchChunksAsync(
        string projectId, string userId, float[] queryVector, int topK, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(projectId) || queryVector == null || topK <= 0)
            return Array.Empty<VectorSearchHit>();

        try
        {
            var filters = new Dictionary<string, object>
            {
                ["project_id"] = projectId,
                ["source_type"] = "chunk"
            };
            var results = await _vectorStore.SearchSimilarAsync(userId, queryVector, topK, filters, ct);
            return results.Select(r => new VectorSearchHit(r.Id, r.Score)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chunk search failed for user {UserId}, project {ProjectId}", userId, projectId);
            return Array.Empty<VectorSearchHit>();
        }
    }

    public async Task<IReadOnlyList<VectorSearchHit>> SearchChaptersAsync(
        string projectId, string userId, float[] queryVector, int topK, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(projectId) || queryVector == null || topK <= 0)
            return Array.Empty<VectorSearchHit>();

        try
        {
            var filters = new Dictionary<string, object>
            {
                ["project_id"] = projectId,
                ["source_type"] = "chapter"
            };
            var results = await _vectorStore.SearchSimilarAsync(userId, queryVector, topK, filters, ct);
            return results.Select(r => new VectorSearchHit(r.Id, r.Score)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chapter search failed for user {UserId}, project {ProjectId}", userId, projectId);
            return Array.Empty<VectorSearchHit>();
        }
    }

    public async Task<IReadOnlyList<VectorSearchHit>> SearchWithinChaptersAsync(
        string projectId, string userId, float[] queryVector, IReadOnlySet<string> chapterIds, int topK, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(projectId) || queryVector == null || topK <= 0 || chapterIds == null || chapterIds.Count == 0)
            return Array.Empty<VectorSearchHit>();

        try
        {
            var filters = new Dictionary<string, object>
            {
                ["project_id"] = projectId,
                ["source_type"] = "chunk"
            };
            var results = await _vectorStore.SearchSimilarAsync(userId, queryVector, topK * 3, filters, ct);

            return results
                .Where(r => !string.IsNullOrEmpty(r.ChapterId) && chapterIds.Contains(r.ChapterId))
                .Take(topK)
                .Select(r => new VectorSearchHit(r.Id, r.Score))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Within-chapters search failed for user {UserId}, project {ProjectId}", userId, projectId);
            return Array.Empty<VectorSearchHit>();
        }
    }

    public async Task<bool> IsAvailableForUserAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        try
        {
            if (!await _vectorStore.CollectionExistsAsync(userId, ct)) return false;
            var info = await _vectorStore.GetCollectionInfoAsync(userId, ct);
            return info != null && info.VectorCount > 0;
        }
        catch
        {
            return false;
        }
    }
}
