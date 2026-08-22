using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.ChapterBlueprints;

/// <summary>
/// Service for managing persisted chapter blueprints.
/// Replaces the current List&lt;string&gt; approach with structured storage that supports
/// version tracking, knowledge dependencies, and design rule application.
/// </summary>
public interface IChapterBlueprintService
{
    /// <summary>
    /// Creates a new chapter blueprint or supersedes an existing active one.
    /// If an active blueprint exists for the chapter, it is marked as Superseded
    /// and a new blueprint is created with incremented version.
    /// </summary>
    Task<ChapterBlueprint> CreateOrUpdateAsync(
        ChapterBlueprintCreateRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the active blueprint for a chapter.
    /// Returns null if no active blueprint exists.
    /// </summary>
    Task<ChapterBlueprint?> GetActiveBlueprintAsync(
        string userId,
        string projectId,
        string chapterId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all active blueprints for a project, ordered by chapter index.
    /// </summary>
    Task<IReadOnlyList<ChapterBlueprint>> GetProjectBlueprintsAsync(
        string userId,
        string projectId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the version history for a specific chapter blueprint.
    /// </summary>
    Task<IReadOnlyList<ChapterBlueprint>> GetBlueprintHistoryAsync(
        string userId,
        string projectId,
        string chapterId,
        CancellationToken ct = default);

    /// <summary>
    /// Approves a blueprint (changes status from Draft to Approved).
    /// </summary>
    Task<bool> ApproveBlueprintAsync(
        string userId,
        string blueprintId,
        CancellationToken ct = default);

    /// <summary>
    /// Associates a context package ID with the blueprint.
    /// Called after ChapterPackageBuilder generates the production package.
    /// </summary>
    Task<bool> AttachContextPackageAsync(
        string userId,
        string blueprintId,
        string contextPackageId,
        CancellationToken ct = default);
}

/// <summary>
/// Request to create a new chapter blueprint.
/// </summary>
public sealed record ChapterBlueprintCreateRequest(
    string UserId,
    string ProjectId,
    string ChapterId,
    int ChapterIndex,
    string Title,
    string Intent,
    string? VolumeId = null,
    IReadOnlyList<string>? KeyEvents = null,
    IReadOnlyList<string>? Characters = null,
    string? ConflictNote = null,
    string? EndingNote = null,
    IReadOnlyList<string>? RequiredKnowledgeIds = null,
    IReadOnlyList<string>? AppliedDesignRuleIds = null,
    IReadOnlyList<string>? DependencyChapterIds = null,
    int? TargetWordCount = null);
