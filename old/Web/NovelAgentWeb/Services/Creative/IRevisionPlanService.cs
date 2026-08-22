namespace TM.Web.NovelAgentWeb.Services.Creative;

public interface IRevisionPlanService
{
    Task<RevisionPlanItem?> CreateAsync(
        CreateRevisionPlanRequest request,
        CancellationToken cancellationToken = default);

    Task<RevisionPlanQueryResult> QueryAsync(
        QueryRevisionPlansRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record CreateRevisionPlanRequest(
    string UserId,
    string ProjectId,
    string SessionId,
    string RunId,
    string IdempotencyKey,
    string Source,
    string PlanType,
    string TargetScope,
    string TargetVolumeId,
    string TargetChapterId,
    string CreativeIntentId,
    string KnowledgeConflictReportId,
    string Status,
    string RequirementsJson,
    string ContinuityRequirementsJson,
    string ImpactAnalysisJson,
    string AffectedChapterIdsJson,
    string InvalidatedPackageIdsJson,
    string RiskLevel,
    string Recommendation);

public sealed record QueryRevisionPlansRequest(
    string UserId,
    string ProjectId,
    string Status,
    string TargetChapterId,
    int Limit);

public sealed class RevisionPlanQueryResult
{
    public string ProjectId { get; set; } = string.Empty;
    public string Status { get; set; } = "all";
    public string TargetChapterId { get; set; } = string.Empty;
    public List<RevisionPlanItem> Items { get; set; } = new();
}

public sealed class RevisionPlanItem
{
    public string Id { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string CreativeIntentId { get; set; } = string.Empty;
    public string KnowledgeConflictReportId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string PlanType { get; set; } = string.Empty;
    public string TargetScope { get; set; } = string.Empty;
    public string TargetVolumeId { get; set; } = string.Empty;
    public string TargetChapterId { get; set; } = string.Empty;
    public string TargetChapterLogicalId { get; set; } = string.Empty;
    public string TargetChapterDisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string RequirementsJson { get; set; } = "[]";
    public string ContinuityRequirementsJson { get; set; } = "[]";
    public string ImpactAnalysisJson { get; set; } = "{}";
    public string AffectedChapterIdsJson { get; set; } = "[]";
    public string InvalidatedPackageIdsJson { get; set; } = "[]";
    public string RiskLevel { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
