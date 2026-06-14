using TM.Services.Framework.AI.Embedding;

namespace TM.Web.NovelAgentWeb.Services.VectorStore;

/// <summary>
/// Semantic search using IVectorStore. Collections named novel_agent_{userId}.
/// </summary>
public sealed class SemanticSearchService
{
    private readonly IVectorStore _vectorStore;
    private readonly IMicroEmbeddingService _embedding;
    private readonly ILogger<SemanticSearchService> _logger;

    public SemanticSearchService(
        IVectorStore vectorStore,
        IMicroEmbeddingService embedding,
        ILogger<SemanticSearchService> logger)
    {
        _vectorStore = vectorStore;
        _embedding = embedding;
        _logger = logger;
    }

    public async Task<List<SemanticSearchResult>> SearchInProjectAsync(
        string userId, string projectId, string query, int topK = 10, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("User ID required", nameof(userId));
        if (string.IsNullOrWhiteSpace(projectId)) throw new ArgumentException("Project ID required", nameof(projectId));
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("Query required", nameof(query));

        try
        {
            var queryVector = await _embedding.EncodeAsync(query, EmbeddingMode.Query, ct);
            var filters = new Dictionary<string, object> { ["project_id"] = projectId };
            var results = await _vectorStore.SearchSimilarAsync(userId, queryVector, topK, filters, ct);

            return results.Select(r => new SemanticSearchResult
            {
                ChunkId = r.SourceId ?? r.Id,
                Content = r.Content ?? "",
                Score = r.Score,
                EntityType = r.SourceType ?? "",
                EntityId = r.SourceId ?? "",
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Semantic search failed for user {UserId}, project {ProjectId}", userId, projectId);
            return new List<SemanticSearchResult>();
        }
    }

    public async Task<List<SemanticSearchResult>> SearchKnowledgeAsync(
        string userId, string query, int topK = 10, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("User ID required", nameof(userId));
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("Query required", nameof(query));

        try
        {
            var queryVector = await _embedding.EncodeAsync(query, EmbeddingMode.Query, ct);
            var filters = new Dictionary<string, object> { ["source_type"] = "knowledge" };
            var results = await _vectorStore.SearchSimilarAsync(userId, queryVector, topK, filters, ct);

            return results.Select(r => new SemanticSearchResult
            {
                ChunkId = r.SourceId ?? r.Id,
                Content = r.Content ?? "",
                Score = r.Score,
                EntityType = r.SourceType ?? "",
                EntityId = r.SourceId ?? "",
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Knowledge semantic search failed for user {UserId}", userId);
            return new List<SemanticSearchResult>();
        }
    }

    public async Task<List<SemanticSearchResult>> DetectSimilarPatternsAsync(
        string userId, string projectId, string pattern, float threshold = 0.85f, CancellationToken ct = default)
    {
        var results = await SearchInProjectAsync(userId, projectId, pattern, 20, ct);
        return results.Where(r => r.Score >= threshold).ToList();
    }
}

public class SemanticSearchResult
{
    public string ChunkId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public float Score { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
}
