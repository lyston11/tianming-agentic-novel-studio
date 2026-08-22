using System.Text.Json.Serialization;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Web.NovelAgentWeb.Services.Content;

public interface IProjectContentQueryService
{
    Task<ProjectContentQueryResult?> QueryAsync(
        ProjectContentQueryRequest request,
        CancellationToken cancellationToken = default);

    Task<ProjectChapterIdentity?> ResolveChapterAsync(
        ProjectChapterIdentityRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> IsAdminAsync(
        string userId,
        CancellationToken cancellationToken = default);
}

public sealed record ProjectContentQueryRequest(
    string UserId,
    string ProjectId,
    StoryBibleDocument StoryBible,
    string ChapterId = "",
    int ChapterNumber = 0,
    int VolumeNumber = 0,
    bool IncludeBody = false,
    bool IncludeFacts = true,
    string VersionId = "",
    int VersionNumber = 0);

public sealed record ProjectChapterIdentityRequest(
    string UserId,
    string ProjectId,
    string ChapterId = "",
    int ChapterNumber = 0);

public sealed class ProjectChapterIdentity
{
    [JsonPropertyName("chapterId")]
    public string ChapterId { get; set; } = string.Empty;

    [JsonPropertyName("chapterNumber")]
    public int ChapterNumber { get; set; }

    [JsonPropertyName("chapterTitle")]
    public string ChapterTitle { get; set; } = string.Empty;
}

public sealed class ProjectContentQueryResult
{
    [JsonPropertyName("projectId")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("projectTitle")]
    public string ProjectTitle { get; set; } = string.Empty;

    [JsonPropertyName("items")]
    public List<ProjectContentQueryItem> Items { get; set; } = new();
}

public sealed class ProjectContentQueryItem
{
    [JsonPropertyName("chapterId")]
    public string ChapterId { get; set; } = string.Empty;

    [JsonPropertyName("sourceRunId")]
    public string SourceRunId { get; set; } = string.Empty;

    [JsonPropertyName("chapterNumber")]
    public int ChapterNumber { get; set; }

    [JsonPropertyName("chapterTitle")]
    public string ChapterTitle { get; set; } = string.Empty;

    [JsonPropertyName("volumeId")]
    public string VolumeId { get; set; } = string.Empty;

    [JsonPropertyName("volumeNumber")]
    public int VolumeNumber { get; set; }

    [JsonPropertyName("volumeTitle")]
    public string VolumeTitle { get; set; } = string.Empty;

    [JsonPropertyName("wordCount")]
    public int WordCount { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("currentVersionId")]
    public string CurrentVersionId { get; set; } = string.Empty;

    [JsonPropertyName("currentVersionNumber")]
    public int CurrentVersionNumber { get; set; }

    [JsonPropertyName("packageId")]
    public string PackageId { get; set; } = string.Empty;

    [JsonPropertyName("packageKind")]
    public string PackageKind { get; set; } = string.Empty;

    [JsonPropertyName("promptVersion")]
    public string PromptVersion { get; set; } = string.Empty;

    [JsonPropertyName("kernelVersion")]
    public string KernelVersion { get; set; } = string.Empty;

    [JsonPropertyName("factSnapshotId")]
    public string FactSnapshotId { get; set; } = string.Empty;

    [JsonPropertyName("factSnapshotVersion")]
    public int FactSnapshotVersion { get; set; }

    [JsonPropertyName("factSnapshotJson")]
    public string FactSnapshotJson { get; set; } = string.Empty;

    [JsonPropertyName("productionEvents")]
    public List<ProjectContentProductionEvent> ProductionEvents { get; set; } = new();

    [JsonPropertyName("chapterChanges")]
    public List<ProjectContentChapterChange> ChapterChanges { get; set; } = new();

    [JsonPropertyName("generationGateReports")]
    public List<ProjectContentGenerationGateReport> GenerationGateReports { get; set; } = new();

    [JsonPropertyName("agentReviews")]
    public List<ProjectContentAgentReview> AgentReviews { get; set; } = new();

    [JsonPropertyName("knowledgeBindings")]
    public List<BoundKnowledgeSnapshot> KnowledgeBindings { get; set; } = new();

    [JsonPropertyName("sourceRevisionPlans")]
    public List<RevisionPlanSnapshot> SourceRevisionPlans { get; set; } = new();

    [JsonPropertyName("rebuiltFromPackageIds")]
    public List<string> RebuiltFromPackageIds { get; set; } = new();

    [JsonPropertyName("bodyPreview")]
    public string BodyPreview { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("continuityFacts")]
    public List<ChapterContinuityFacts> ContinuityFacts { get; set; } = new();
}

public sealed class ProjectContentProductionEvent
{
    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("stage")]
    public string Stage { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("artifactType")]
    public string ArtifactType { get; set; } = string.Empty;

    [JsonPropertyName("artifactId")]
    public string ArtifactId { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
}

public sealed class ProjectContentChapterChange
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("runtimeRunId")]
    public string RuntimeRunId { get; set; } = string.Empty;

    [JsonPropertyName("chapterId")]
    public string ChapterId { get; set; } = string.Empty;

    [JsonPropertyName("packageId")]
    public string PackageId { get; set; } = string.Empty;

    [JsonPropertyName("parseStatus")]
    public string ParseStatus { get; set; } = string.Empty;

    [JsonPropertyName("parseError")]
    public string ParseError { get; set; } = string.Empty;

    [JsonPropertyName("appliedToFactSnapshot")]
    public bool AppliedToFactSnapshot { get; set; }

    [JsonPropertyName("appliedAt")]
    public DateTime? AppliedAt { get; set; }

    [JsonPropertyName("changesJson")]
    public string ChangesJson { get; set; } = string.Empty;

    [JsonPropertyName("canonicalChangesJson")]
    public string CanonicalChangesJson { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
}

public sealed class ProjectContentGenerationGateReport
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("runtimeRunId")]
    public string RuntimeRunId { get; set; } = string.Empty;

    [JsonPropertyName("chapterId")]
    public string ChapterId { get; set; } = string.Empty;

    [JsonPropertyName("packageId")]
    public string PackageId { get; set; } = string.Empty;

    [JsonPropertyName("artifactId")]
    public string ArtifactId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("protocolPassed")]
    public bool ProtocolPassed { get; set; }

    [JsonPropertyName("changesDetected")]
    public bool ChangesDetected { get; set; }

    [JsonPropertyName("factSnapshotPassed")]
    public bool FactSnapshotPassed { get; set; }

    [JsonPropertyName("blueprintPassed")]
    public bool BlueprintPassed { get; set; }

    [JsonPropertyName("ragPassed")]
    public bool RagPassed { get; set; }

    [JsonPropertyName("issueCount")]
    public int IssueCount { get; set; }

    [JsonPropertyName("repairHintCount")]
    public int RepairHintCount { get; set; }

    [JsonPropertyName("reportJson")]
    public string ReportJson { get; set; } = string.Empty;

    [JsonPropertyName("validatedAt")]
    public DateTime ValidatedAt { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
}

public sealed class ProjectContentAgentReview
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("runtimeRunId")]
    public string RuntimeRunId { get; set; } = string.Empty;

    [JsonPropertyName("chapterId")]
    public string ChapterId { get; set; } = string.Empty;

    [JsonPropertyName("packageId")]
    public string PackageId { get; set; } = string.Empty;

    [JsonPropertyName("reviewId")]
    public string ReviewId { get; set; } = string.Empty;

    [JsonPropertyName("overallResult")]
    public string OverallResult { get; set; } = string.Empty;

    [JsonPropertyName("validationOverallResult")]
    public string ValidationOverallResult { get; set; } = string.Empty;

    [JsonPropertyName("requiresRewrite")]
    public bool RequiresRewrite { get; set; }

    [JsonPropertyName("qualityScore")]
    public int QualityScore { get; set; }

    [JsonPropertyName("contentLength")]
    public int ContentLength { get; set; }

    [JsonPropertyName("checkCount")]
    public int CheckCount { get; set; }

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("meetsAcceptedCreativeIntents")]
    public bool MeetsAcceptedCreativeIntents { get; set; } = true;

    [JsonPropertyName("continuityRisk")]
    public string ContinuityRisk { get; set; } = string.Empty;

    [JsonPropertyName("chapterPacing")]
    public string ChapterPacing { get; set; } = string.Empty;

    [JsonPropertyName("recommendedAction")]
    public string RecommendedAction { get; set; } = string.Empty;

    [JsonPropertyName("reviewJson")]
    public string ReviewJson { get; set; } = string.Empty;

    [JsonPropertyName("reviewedAt")]
    public DateTime ReviewedAt { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
}
