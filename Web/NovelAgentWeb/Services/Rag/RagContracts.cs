namespace TM.Web.NovelAgentWeb.Services.Rag;

public enum RagRoute
{
    Setting,
    Character,
    Continuity,
    Promise,
    Knowledge,
    Style
}

public sealed record RagQueryPlanningRequest(
    string UserId,
    string ProjectId,
    string UserMessage,
    string ConversationContext);

public sealed record RagQueryPlan(
    IReadOnlyList<RagRoute> Routes,
    IReadOnlyList<string> SemanticQueries,
    IReadOnlyList<string> EntityReferences,
    IReadOnlyList<string> TargetChapterIds,
    bool RequiresLongRangeRecall);

public sealed record RagRetrievalRequest(
    string UserId,
    string ProjectId,
    string UserMessage,
    string ConversationContext,
    int TopK = 12,
    RagSnapshotScope? Snapshot = null);

public sealed record RagSnapshotScope(
    string CanonVersion,
    long KnowledgeVersion,
    DateTime FrozenAt);

public sealed record RetrievalCandidate(
    string SourceType,
    string SourceId,
    string Channel,
    int Rank,
    double Score,
    IReadOnlyDictionary<string, object>? Metadata);

public sealed record FusedRetrievalCandidate(
    string SourceType,
    string SourceId,
    double Score,
    IReadOnlyList<string> Channels,
    IReadOnlyDictionary<string, object>? Metadata);

public sealed record EvidenceItem(
    string UserId,
    string ProjectId,
    string SourceType,
    string SourceId,
    string Content,
    double Score,
    IReadOnlyList<string> Channels,
    string? ExpansionReason = null,
    IReadOnlyDictionary<string, object>? Metadata = null);

public sealed record EvidenceBundle(
    RagQueryPlan Plan,
    IReadOnlyList<EvidenceItem> Items);

public interface IRagQueryPlanningModelClient
{
    Task<RagQueryPlan> PlanAsync(
        RagQueryPlanningRequest request,
        CancellationToken cancellationToken = default);
}

public interface IQueryPlanner
{
    Task<RagQueryPlan> PlanAsync(
        RagQueryPlanningRequest request,
        CancellationToken cancellationToken = default);
}

public interface IPostgresFullTextRetriever
{
    Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(
        string userId,
        string projectId,
        RagQueryPlan plan,
        int topK,
        RagSnapshotScope? snapshot = null,
        CancellationToken cancellationToken = default);
}

public interface IEntityDependencyRetriever
{
    Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(
        string userId,
        string projectId,
        RagQueryPlan plan,
        int topK,
        RagSnapshotScope? snapshot = null,
        CancellationToken cancellationToken = default);
}

public interface IHybridRetriever
{
    Task<EvidenceBundle> RetrieveAsync(
        RagRetrievalRequest request,
        CancellationToken cancellationToken = default);
}
