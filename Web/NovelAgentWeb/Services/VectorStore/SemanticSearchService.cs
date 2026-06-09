using Qdrant.Client;
using Qdrant.Client.Grpc;
using TM.Services.Framework.AI.Embedding;

namespace TM.Web.NovelAgentWeb.Services.VectorStore;

/// <summary>
/// Provides semantic search capabilities using vector embeddings and Qdrant vector store.
/// Enables finding similar content within a project's knowledge base.
/// </summary>
public sealed class SemanticSearchService
{
    private readonly QdrantClient _qdrant;
    private readonly IMicroEmbeddingService _embedding;
    private readonly ILogger<SemanticSearchService> _logger;

    // Initial search size for pattern detection - larger pool allows better filtering by threshold
    private const int InitialPatternSearchSize = 20;

    public SemanticSearchService(
        QdrantClient qdrant,
        IMicroEmbeddingService embedding,
        ILogger<SemanticSearchService> logger)
    {
        _qdrant = qdrant;
        _embedding = embedding;
        _logger = logger;
    }

    /// <summary>
    /// Searches for semantically similar content within a specific project.
    /// </summary>
    /// <param name="userId">The user ID (must not be null or empty)</param>
    /// <param name="projectId">The project ID to search within (must not be null or empty)</param>
    /// <param name="query">The search query text (must not be null or empty)</param>
    /// <param name="topK">Maximum number of results to return (must be positive)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of search results ordered by relevance score, or empty list on error</returns>
    public async Task<List<SemanticSearchResult>> SearchInProjectAsync(
        string userId,
        string projectId,
        string query,
        int topK = 10,
        CancellationToken ct = default)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
        if (string.IsNullOrWhiteSpace(projectId))
            throw new ArgumentException("Project ID cannot be null or empty", nameof(projectId));
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be null or empty", nameof(query));
        if (topK <= 0)
            throw new ArgumentOutOfRangeException(nameof(topK), "topK must be positive");

        try
        {
            // Note: Collection naming pattern matches QdrantCollectionManager convention
            var collectionName = $"novel_agent_{userId}";
            var queryVector = await _embedding.EncodeAsync(query, EmbeddingMode.Query, ct);

            var filter = new Filter
            {
                Must =
                {
                    new Condition
                    {
                        Field = new FieldCondition
                        {
                            Key = "project_id",
                            Match = new Match { Keyword = projectId }
                        }
                    }
                }
            };

            var results = await _qdrant.SearchAsync(
                collectionName: collectionName,
                vector: queryVector.ToArray(),
                filter: filter,
                limit: (ulong)topK,
                cancellationToken: ct);

            return results.Select(r =>
            {
                // Safe payload access with fallback values
                var payload = r.Payload;
                payload.TryGetValue("chunk_id", out var chunkIdValue);
                payload.TryGetValue("content", out var contentValue);
                payload.TryGetValue("entity_type", out var entityTypeValue);
                payload.TryGetValue("entity_id", out var entityIdValue);

                return new SemanticSearchResult
                {
                    ChunkId = chunkIdValue?.StringValue ?? string.Empty,
                    Content = contentValue?.StringValue ?? string.Empty,
                    Score = r.Score,
                    EntityType = entityTypeValue?.StringValue ?? string.Empty,
                    EntityId = entityIdValue?.StringValue ?? string.Empty
                };
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Semantic search failed for user {UserId}, project {ProjectId}, query: {Query}",
                userId, projectId, query);
            return new List<SemanticSearchResult>();
        }
    }

    /// <summary>
    /// Detects content patterns similar to the provided example, filtered by similarity threshold.
    /// </summary>
    /// <param name="userId">The user ID (must not be null or empty)</param>
    /// <param name="projectId">The project ID to search within (must not be null or empty)</param>
    /// <param name="pattern">The pattern text to match against (must not be null or empty)</param>
    /// <param name="threshold">Minimum similarity score (0.0 to 1.0)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of results meeting the threshold, or empty list on error</returns>
    public async Task<List<SemanticSearchResult>> DetectSimilarPatternsAsync(
        string userId,
        string projectId,
        string pattern,
        float threshold = 0.85f,
        CancellationToken ct = default)
    {
        var results = await SearchInProjectAsync(userId, projectId, pattern, InitialPatternSearchSize, ct);
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
