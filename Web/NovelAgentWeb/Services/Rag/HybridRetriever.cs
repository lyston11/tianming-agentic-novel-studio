using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Services.Execution;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using Grpc.Core;

namespace TM.Web.NovelAgentWeb.Services.Rag;

public sealed class HybridRetriever : IHybridRetriever
{
    private static readonly IReadOnlyDictionary<RagRoute, string[]> SourceTypesByRoute =
        new Dictionary<RagRoute, string[]>
        {
            [RagRoute.Setting] = ["chapter", "continuity_summary", "canon_change"],
            [RagRoute.Character] = ["chapter", "continuity_summary", "canon_change"],
            [RagRoute.Continuity] = ["chapter", "continuity_summary", "canon_change"],
            [RagRoute.Promise] = ["chapter", "continuity_summary", "canon_change"],
            [RagRoute.Knowledge] = ["knowledge_document", "knowledge_section", "knowledge_chunk", "knowledge_entry"],
            [RagRoute.Style] = ["style_profile"]
        };

    private readonly IQueryPlanner _planner;
    private readonly IVectorStore _vectors;
    private readonly IMicroEmbeddingService _embedding;
    private readonly IGoalEmbeddingExecutionEnvelope _embeddingExecutions;
    private readonly IPostgresFullTextRetriever _fullText;
    private readonly IEntityDependencyRetriever _dependencies;
    private readonly ReciprocalRankFusion _fusion;
    private readonly EvidenceBundleCompiler _compiler;
    private readonly ILogger<HybridRetriever> _logger;

    public HybridRetriever(
        IQueryPlanner planner,
        IVectorStore vectors,
        IMicroEmbeddingService embedding,
        IGoalEmbeddingExecutionEnvelope embeddingExecutions,
        IPostgresFullTextRetriever fullText,
        IEntityDependencyRetriever dependencies,
        ReciprocalRankFusion fusion,
        EvidenceBundleCompiler compiler,
        ILogger<HybridRetriever> logger)
    {
        _planner = planner;
        _vectors = vectors;
        _embedding = embedding;
        _embeddingExecutions = embeddingExecutions;
        _fullText = fullText;
        _dependencies = dependencies;
        _fusion = fusion;
        _compiler = compiler;
        _logger = logger;
    }

    public async Task<EvidenceBundle> RetrieveAsync(
        RagRetrievalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserMessage);
        if (request.TopK is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(request.TopK), "TopK 必须在 1 到 100 之间。");

        var plan = await _planner.PlanAsync(new RagQueryPlanningRequest(
            request.UserId,
            request.ProjectId,
            request.UserMessage,
            request.ConversationContext), cancellationToken);

        var denseTask = SearchDenseAsync(request, plan, cancellationToken);
        var fullText = await _fullText.SearchAsync(
            request.UserId,
            request.ProjectId,
            plan,
            request.TopK,
            request.Snapshot,
            cancellationToken);
        var dependencies = await _dependencies.SearchAsync(
            request.UserId,
            request.ProjectId,
            plan,
            request.TopK,
            request.Snapshot,
            cancellationToken);
        var dense = await denseTask;

        var all = dense.Concat(fullText).Concat(dependencies);
        var fused = _fusion.Fuse(all, request.TopK);
        var bundle = await _compiler.CompileAsync(
            request.UserId,
            request.ProjectId,
            plan,
            fused,
            request.Snapshot,
            cancellationToken);
        _logger.LogInformation(
            "RAG retrieved {EvidenceCount} authoritative evidence items from {CandidateCount} fused candidates for user {UserId} project {ProjectId}",
            bundle.Items.Count,
            fused.Count,
            request.UserId,
            request.ProjectId);
        return bundle;
    }

    private async Task<IReadOnlyList<RetrievalCandidate>> SearchDenseAsync(
        RagRetrievalRequest request,
        RagQueryPlan plan,
        CancellationToken cancellationToken)
    {
        var sourceTypes = plan.Routes
            .SelectMany(route => SourceTypesByRoute[route])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var result = new List<RetrievalCandidate>();
        for (var queryIndex = 0; queryIndex < plan.SemanticQueries.Count; queryIndex++)
        {
            var vector = await _embeddingExecutions.ExecuteAsync(
                plan.SemanticQueries[queryIndex],
                EmbeddingMode.Query,
                ct => _embedding.EncodeAsync(
                    plan.SemanticQueries[queryIndex],
                    EmbeddingMode.Query,
                    ct),
                cancellationToken);
            foreach (var sourceType in sourceTypes)
            {
                var filters = new Dictionary<string, object> { ["source_type"] = sourceType };
                if (IsProjectScoped(sourceType))
                    filters["project_id"] = request.ProjectId;
                if (request.Snapshot != null && IsKnowledgeScoped(sourceType))
                    filters["knowledge_version"] = new VectorNumericRange(
                        LessThanOrEqual: request.Snapshot.KnowledgeVersion);
                if (request.Snapshot != null && IsProjectScoped(sourceType))
                    filters["created_at_unix"] = new VectorNumericRange(
                        LessThanOrEqual: new DateTimeOffset(request.Snapshot.FrozenAt).ToUnixTimeSeconds());
                List<SearchResult> matches;
                try
                {
                    matches = await _vectors.SearchSimilarAsync(
                        request.UserId,
                        vector,
                        request.TopK,
                        filters,
                        cancellationToken);
                }
                catch (RpcException exception) when (exception.StatusCode == StatusCode.NotFound)
                {
                    _logger.LogWarning(
                        "Qdrant collection is not initialized for user {UserId}; continuing with PostgreSQL retrieval channels.",
                        request.UserId);
                    return [];
                }
                for (var rank = 0; rank < matches.Count; rank++)
                {
                    var match = matches[rank];
                    if (!string.Equals(match.SourceType, sourceType, StringComparison.Ordinal) ||
                        string.IsNullOrWhiteSpace(match.SourceId))
                    {
                        continue;
                    }
                    var metadata = match.Metadata == null
                        ? new Dictionary<string, object>()
                        : new Dictionary<string, object>(match.Metadata, StringComparer.Ordinal);
                    if (match.ChunkIndex.HasValue)
                        metadata["chunk_index"] = match.ChunkIndex.Value;
                    result.Add(new RetrievalCandidate(
                        sourceType,
                        match.SourceId,
                        $"dense:{queryIndex}:{sourceType}",
                        rank + 1,
                        match.Score,
                        metadata));
                }
            }
        }
        return result;
    }

    private static bool IsProjectScoped(string sourceType) => sourceType is
        "chapter" or "continuity_summary" or "canon_change";

    private static bool IsKnowledgeScoped(string sourceType) => sourceType is
        "knowledge_document" or "knowledge_section" or "knowledge_chunk" or "knowledge_entry" or "style_profile";
}
