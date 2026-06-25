using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Models.Chapters;

/// <summary>
/// Response model for chapter data.
/// </summary>
public class ChapterResponse
{
    /// <summary>
    /// Unique chapter identifier.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Project ID that this chapter belongs to.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// Volume ID that this chapter belongs to (if any).
    /// </summary>
    public string? VolumeId { get; set; }

    /// <summary>
    /// Chapter title.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Chapter number within the project.
    /// </summary>
    public required int ChapterNumber { get; set; }

    /// <summary>
    /// Chapter status (draft, published, archived).
    /// </summary>
    public required string Status { get; set; }

    /// <summary>
    /// Word count of the chapter content.
    /// </summary>
    public required int WordCount { get; set; }

    /// <summary>
    /// Chapter content in Markdown format (only included in GetById).
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// Compact production chain evidence for library archive and reader views.
    /// </summary>
    public IReadOnlyList<WorkflowProductionChain> ProductionChains { get; set; } =
        Array.Empty<WorkflowProductionChain>();

    /// <summary>
    /// Structured production evidence for the current chapter archive.
    /// </summary>
    public ChapterProductionEvidenceResponse ProductionEvidence { get; set; } = new();

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    public required DateTime CreatedAt { get; set; }

    /// <summary>
    /// Last update timestamp.
    /// </summary>
    public required DateTime UpdatedAt { get; set; }
}

public sealed class ChapterProductionEvidenceResponse
{
    public IReadOnlyList<ChapterRevisionPlanEvidenceResponse> RevisionPlans { get; set; } =
        Array.Empty<ChapterRevisionPlanEvidenceResponse>();

    public ChapterFactSnapshotEvidenceResponse? LatestFactSnapshot { get; set; }

    public IReadOnlyList<ChapterOutboxEvidenceResponse> OutboxEvents { get; set; } =
        Array.Empty<ChapterOutboxEvidenceResponse>();
}

public sealed class ChapterRevisionPlanEvidenceResponse
{
    public required string Id { get; set; }
    public required string Source { get; set; }
    public required string PlanType { get; set; }
    public required string TargetScope { get; set; }
    public required string TargetChapterId { get; set; }
    public required string Status { get; set; }
    public required string RiskLevel { get; set; }
    public required string Recommendation { get; set; }
    public IReadOnlyList<string> AffectedChapterIds { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> InvalidatedPackageIds { get; set; } = Array.Empty<string>();
    public required DateTime UpdatedAt { get; set; }
}

public sealed class ChapterFactSnapshotEvidenceResponse
{
    public required string Id { get; set; }
    public required string ChapterVersionId { get; set; }
    public required int VersionNumber { get; set; }
    public required string Source { get; set; }
    public required string SnapshotPreview { get; set; }
    public required DateTime CreatedAt { get; set; }
}

public sealed class ChapterOutboxEvidenceResponse
{
    public required string Id { get; set; }
    public required string EventType { get; set; }
    public required string AggregateType { get; set; }
    public required string AggregateId { get; set; }
    public required string Status { get; set; }
    public required int Attempts { get; set; }
    public required string LastError { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public required DateTime UpdatedAt { get; set; }
}
