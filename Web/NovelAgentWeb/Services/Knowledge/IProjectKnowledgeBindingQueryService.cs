using System.Text.Json.Serialization;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

public interface IProjectKnowledgeBindingQueryService
{
    Task<ProjectKnowledgeBindingsQueryResult?> QueryBindingsAsync(
        string userId,
        string projectId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BoundKnowledgeSnapshot>> GetBoundKnowledgeAsync(
        string userId,
        string projectId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetProjectHardFactLinesAsync(
        string userId,
        string projectId,
        CancellationToken cancellationToken = default);
}

public sealed class ProjectKnowledgeBindingsQueryResult
{
    [JsonPropertyName("projectId")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("bindingCount")]
    public int BindingCount { get; set; }

    [JsonPropertyName("hardFactCount")]
    public int HardFactCount { get; set; }

    [JsonPropertyName("canonCount")]
    public int CanonCount { get; set; }

    [JsonPropertyName("statusSummary")]
    public ProjectKnowledgeBindingStatusSummary StatusSummary { get; set; } = new();

    [JsonPropertyName("bindings")]
    public List<BoundKnowledgeSnapshot> Bindings { get; set; } = new();

    [JsonPropertyName("hardFacts")]
    public List<string> HardFacts { get; set; } = new();

    [JsonPropertyName("canonLedger")]
    public List<ProjectKnowledgeCanonLedgerSummary> CanonLedger { get; set; } = new();

    [JsonPropertyName("conflictReports")]
    public List<ProjectKnowledgeConflictReportSummary> ConflictReports { get; set; } = new();
}

public sealed class ProjectKnowledgeBindingStatusSummary
{
    [JsonPropertyName("importedCount")]
    public int ImportedCount { get; set; }

    [JsonPropertyName("referencedCount")]
    public int ReferencedCount { get; set; }

    [JsonPropertyName("classifiedCount")]
    public int ClassifiedCount { get; set; }

    [JsonPropertyName("pendingClassificationCount")]
    public int PendingClassificationCount { get; set; }

    [JsonPropertyName("shouldEnterGateCount")]
    public int ShouldEnterGateCount { get; set; }

    [JsonPropertyName("shouldEnterBlueprintCount")]
    public int ShouldEnterBlueprintCount { get; set; }

    [JsonPropertyName("shouldEnterFactSnapshotCount")]
    public int ShouldEnterFactSnapshotCount { get; set; }

    [JsonPropertyName("canonLedgerCount")]
    public int CanonLedgerCount { get; set; }

    [JsonPropertyName("conflictCanonLedgerCount")]
    public int ConflictCanonLedgerCount { get; set; }

    [JsonPropertyName("openConflictCount")]
    public int OpenConflictCount { get; set; }
}

public sealed class ProjectKnowledgeCanonLedgerSummary
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("rationale")]
    public string Rationale { get; set; } = string.Empty;

    [JsonPropertyName("impactScope")]
    public string ImpactScope { get; set; } = string.Empty;

    [JsonPropertyName("conflictCheck")]
    public string ConflictCheck { get; set; } = string.Empty;
}

public sealed class ProjectKnowledgeConflictReportSummary
{
    [JsonPropertyName("conflictId")]
    public string ConflictId { get; set; } = string.Empty;

    [JsonPropertyName("knowledgeId")]
    public string KnowledgeId { get; set; } = string.Empty;

    [JsonPropertyName("conflictingKnowledgeIds")]
    public List<string> ConflictingKnowledgeIds { get; set; } = new();

    [JsonPropertyName("conflictType")]
    public string ConflictType { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = string.Empty;

    [JsonPropertyName("impactScope")]
    public string ImpactScope { get; set; } = string.Empty;

    [JsonPropertyName("explanation")]
    public string Explanation { get; set; } = string.Empty;

    [JsonPropertyName("recommendedAction")]
    public string RecommendedAction { get; set; } = string.Empty;

    [JsonPropertyName("requiresUserDecision")]
    public bool RequiresUserDecision { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("resolutionNote")]
    public string ResolutionNote { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("resolvedAt")]
    public DateTime? ResolvedAt { get; set; }
}
