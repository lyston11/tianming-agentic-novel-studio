namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class ExperienceSuggestion
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string ObservationId { get; set; } = string.Empty;
    public string SuggestionType { get; set; } = string.Empty;
    public string ProposedChangeJson { get; set; } = "{}";
    public string Rationale { get; set; } = string.Empty;
    public string SuppressionFingerprint { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string? DecisionReason { get; set; }
    public string? EffectiveGoalId { get; set; }
    public int DecisionVersion { get; set; }
    public DateTime? DecidedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
