namespace TM.Web.NovelAgentWeb.Data.Entities;

/// <summary>
/// Represents a persisted chapter blueprint (chapter plan/brief).
/// Replaces the current List&lt;string&gt; approach with structured storage.
/// </summary>
public class ChapterBlueprint
{
    /// <summary>
    /// Unique identifier for this blueprint.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Project this blueprint belongs to.
    /// </summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// User who owns the project.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Volume ID (if applicable).
    /// </summary>
    public string? VolumeId { get; set; }

    /// <summary>
    /// Chapter ID this blueprint is for.
    /// </summary>
    public string ChapterId { get; set; } = string.Empty;

    /// <summary>
    /// Chapter index (e.g., 1, 2, 3...).
    /// </summary>
    public int ChapterIndex { get; set; }

    /// <summary>
    /// Candidate chapter title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// High-level intent for this chapter (what it achieves in the story arc).
    /// </summary>
    public string Intent { get; set; } = string.Empty;

    /// <summary>
    /// Key events/beats to cover (JSON array of strings).
    /// </summary>
    public string KeyEventsJson { get; set; } = "[]";

    /// <summary>
    /// Main characters involved (JSON array of character names).
    /// </summary>
    public string CharactersJson { get; set; } = "[]";

    /// <summary>
    /// Conflict escalation note.
    /// </summary>
    public string? ConflictNote { get; set; }

    /// <summary>
    /// Ending state/hook for next chapter.
    /// </summary>
    public string? EndingNote { get; set; }

    /// <summary>
    /// IDs of knowledge entries required for this chapter (JSON array).
    /// </summary>
    public string RequiredKnowledgeIdsJson { get; set; } = "[]";

    /// <summary>
    /// IDs of design rules that apply to this chapter (JSON array).
    /// </summary>
    public string AppliedDesignRuleIdsJson { get; set; } = "[]";

    /// <summary>
    /// IDs of previous chapters this chapter depends on (JSON array).
    /// Used for long-distance recall and continuity checks.
    /// </summary>
    public string DependencyChapterIdsJson { get; set; } = "[]";

    /// <summary>
    /// Context package ID associated with this blueprint (if generated).
    /// </summary>
    public string? ContextPackageId { get; set; }

    /// <summary>
    /// Blueprint version (incremented when blueprint is regenerated).
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Blueprint status.
    /// Values: Draft, Approved, Superseded
    /// </summary>
    public string Status { get; set; } = "Draft";

    /// <summary>
    /// Target word count for this chapter.
    /// </summary>
    public int? TargetWordCount { get; set; }

    /// <summary>
    /// When this blueprint was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this blueprint was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// ID of the previous version of this blueprint (if any).
    /// </summary>
    public string? PreviousVersionId { get; set; }
}
