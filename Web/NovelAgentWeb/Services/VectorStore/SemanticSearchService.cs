using Qdrant.Client;
using Qdrant.Client.Grpc;
using TM.Services.Framework.AI.Embedding;

namespace TM.Web.NovelAgentWeb.Services.VectorStore;

public sealed class SemanticSearchService
{
    private readonly QdrantClient _qdrant;
    private readonly IMicroEmbeddingService _embedding;
    private readonly ILogger<SemanticSearchService> _logger;

    public SemanticSearchService(
        QdrantClient qdrant,
        IMicroEmbeddingService embedding,
        ILogger<SemanticSearchService> logger)
    {
        _qdrant = qdrant;
        _embedding = embedding;
        _logger = logger;
    }

    public async Task<List<SemanticSearchResult>> SearchInProjectAsync(
        string userId,
        string projectId,
        string query,
        int topK = 10,
        CancellationToken ct = default)
    {
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

        return results.Select(r => new SemanticSearchResult
        {
            ChunkId = r.Payload["chunk_id"].StringValue,
            Content = r.Payload["content"].StringValue,
            Score = r.Score,
            EntityType = r.Payload["entity_type"].StringValue,
            EntityId = r.Payload["entity_id"].StringValue
        }).ToList();
    }

    public async Task<List<SemanticSearchResult>> DetectSimilarPatternsAsync(
        string userId,
        string projectId,
        string pattern,
        float threshold = 0.85f,
        CancellationToken ct = default)
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
