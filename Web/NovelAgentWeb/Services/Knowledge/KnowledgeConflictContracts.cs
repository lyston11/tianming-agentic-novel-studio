namespace TM.Web.NovelAgentWeb.Services.Knowledge;

public sealed record KnowledgeConflictDetectionRequest(
    string UserId,
    string ProjectId,
    string KnowledgeId,
    string? SessionId = null,
    string? RunId = null);

public sealed record KnowledgeConflictPrompt(
    string UserId,
    string ProjectId,
    string ProjectTitle,
    KnowledgeConflictKnowledgeItem Candidate,
    IReadOnlyList<KnowledgeConflictKnowledgeItem> ExistingKnowledge);

public sealed class KnowledgeConflictKnowledgeItem
{
    public string KnowledgeId { get; set; } = "";
    public string Title { get; set; } = "";
    public string EntryType { get; set; } = "";
    public string Content { get; set; } = "";
    public string Role { get; set; } = "";
    public string Scope { get; set; } = "";
    public int Priority { get; set; }
    public string ConstraintLevel { get; set; } = "";
    public string PackagePolicy { get; set; } = "";
}

public sealed class KnowledgeConflictDecision
{
    public string Model { get; set; } = "";
    public bool HasConflict { get; set; }
    public string ConflictType { get; set; } = "";
    public string Severity { get; set; } = "";
    public string ImpactScope { get; set; } = "";
    public List<string> ConflictingKnowledgeIds { get; set; } = new();
    public string Explanation { get; set; } = "";
    public string RecommendedAction { get; set; } = "";
    public bool RequiresUserDecision { get; set; }
    public string RawJson { get; set; } = "{}";
}

public sealed class KnowledgeConflictDetectionResult
{
    public string ReportId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string KnowledgeId { get; set; } = "";
    public bool HasConflict { get; set; }
    public string ConflictType { get; set; } = "";
    public string Severity { get; set; } = "";
    public string ImpactScope { get; set; } = "";
    public IReadOnlyList<string> ConflictingKnowledgeIds { get; set; } = Array.Empty<string>();
    public string Explanation { get; set; } = "";
    public string RecommendedAction { get; set; } = "";
    public bool RequiresUserDecision { get; set; }
}

public sealed record KnowledgeConflictResolutionRequest(
    string UserId,
    string ProjectId,
    string ConflictId,
    string Decision,
    string Note,
    string? SessionId = null,
    string? RunId = null);

public sealed class KnowledgeConflictResolutionResult
{
    public string ConflictId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string KnowledgeId { get; set; } = "";
    public string PreviousStatus { get; set; } = "";
    public string Status { get; set; } = "";
    public string Note { get; set; } = "";
    public DateTime ResolvedAt { get; set; }
}

public interface IKnowledgeConflictModelClient
{
    Task<KnowledgeConflictDecision> DetectAsync(
        KnowledgeConflictPrompt prompt,
        CancellationToken ct = default);
}

public interface IKnowledgeConflictDetector
{
    Task<KnowledgeConflictDetectionResult> DetectAsync(
        KnowledgeConflictDetectionRequest request,
        CancellationToken ct = default);
}

public interface IKnowledgeConflictResolver
{
    Task<KnowledgeConflictResolutionResult> ResolveAsync(
        KnowledgeConflictResolutionRequest request,
        CancellationToken ct = default);
}

public sealed record KnowledgeCanonConflictStatusRequest(
    string UserId,
    string ProjectId,
    string KnowledgeId,
    bool HasConflict,
    string ReportId,
    string Severity,
    string ConflictType,
    string Explanation,
    IReadOnlyList<string> ConflictingKnowledgeIds,
    string? RunId = null);

public interface IKnowledgeCanonConflictStatusService
{
    Task ApplyAsync(KnowledgeCanonConflictStatusRequest request, CancellationToken ct = default);

    Task ApplyResolutionAsync(KnowledgeCanonConflictResolutionStatusRequest request, CancellationToken ct = default);
}

public sealed record KnowledgeCanonConflictResolutionStatusRequest(
    string UserId,
    string ProjectId,
    string KnowledgeId,
    string ReportId,
    string ResolutionStatus,
    string ResolutionNote,
    string Severity,
    string ConflictType,
    IReadOnlyList<string> ConflictingKnowledgeIds,
    string? RunId = null);
