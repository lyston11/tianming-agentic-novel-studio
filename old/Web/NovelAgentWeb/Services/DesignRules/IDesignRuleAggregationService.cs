using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.DesignRules;

/// <summary>
/// Service for aggregating knowledge classifications into design rules.
/// Design rules serve as the middle layer between knowledge entries and chapter production packages.
/// </summary>
public interface IDesignRuleAggregationService
{
    /// <summary>
    /// Aggregates all active knowledge classifications for a project into design rules.
    /// Called after knowledge classification completes, or manually via Agent tool.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="projectId">Project ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of design rules (existing + newly aggregated).</returns>
    Task<IReadOnlyList<ProjectDesignRule>> AggregateFromKnowledgeAsync(
        string userId,
        string projectId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all active design rules for a project.
    /// Used by ChapterPackageBuilder when constructing production packages.
    /// </summary>
    Task<IReadOnlyList<ProjectDesignRule>> GetActiveDesignRulesAsync(
        string userId,
        string projectId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets design rules of a specific type for a project.
    /// </summary>
    Task<IReadOnlyList<ProjectDesignRule>> GetDesignRulesByTypeAsync(
        string userId,
        string projectId,
        string ruleType,
        CancellationToken ct = default);
}
