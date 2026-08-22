namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class ExperienceObservation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string? GoalId { get; set; }
    public string ObservationType { get; set; } = string.Empty;
    public string EvidenceJson { get; set; } = "{}";
    public string MetricsJson { get; set; } = "{}";
    public string Status { get; set; } = "observed";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
