namespace TM.Web.NovelAgentWeb.Data.Entities;

/// <summary>
/// Represents a design rule aggregated from knowledge classifications.
/// Acts as the middle layer between knowledge entries and production rules.
/// </summary>
public class ProjectDesignRule
{
    /// <summary>
    /// Unique identifier for this design rule.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Project this rule belongs to.
    /// </summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// User who owns the project.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Type of design rule.
    /// Values: WorldCoreRule, CharacterPsycheRule, ConflictEngine, WritingTech, ReaderPromise, StyleGuide
    /// </summary>
    public string RuleType { get; set; } = string.Empty;

    /// <summary>
    /// The actual rule content (human-readable text).
    /// </summary>
    public string RuleContent { get; set; } = string.Empty;

    /// <summary>
    /// IDs of knowledge entries that contributed to this rule.
    /// JSON array of knowledge entry IDs.
    /// </summary>
    public string SourceKnowledgeIdsJson { get; set; } = "[]";

    /// <summary>
    /// Constraint level of this rule.
    /// Values: Reference, MustMention, MustSatisfy, Forbidden
    /// </summary>
    public string ConstraintLevel { get; set; } = "Reference";

    /// <summary>
    /// Scope of this rule.
    /// Values: ProjectWide, SpecificVolume, SpecificChapter, SpecificCharacter
    /// </summary>
    public string Scope { get; set; } = "ProjectWide";

    /// <summary>
    /// Specific scope target (e.g., volume ID, chapter ID, character name).
    /// Null if Scope is ProjectWide.
    /// </summary>
    public string? ScopeTarget { get; set; }

    /// <summary>
    /// Priority of this rule (1-100, higher = more important).
    /// </summary>
    public int Priority { get; set; } = 50;

    /// <summary>
    /// Version number of this rule (incremented on update).
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Rule status.
    /// Values: Active, Archived, Superseded
    /// </summary>
    public string Status { get; set; } = "Active";

    /// <summary>
    /// When this rule was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this rule was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// ID of the previous version of this rule (if any).
    /// </summary>
    public string? PreviousVersionId { get; set; }
}
