namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class DomainEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string GoalId { get; set; } = string.Empty;
    public string? TaskId { get; set; }
    public string? BranchId { get; set; }
    public string AggregateType { get; set; } = string.Empty;
    public string AggregateId { get; set; } = string.Empty;
    public long AggregateVersion { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string ArtifactRefsJson { get; set; } = "[]";
    public string EvidenceRefsJson { get; set; } = "[]";
    public string? CausationId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string? ModelExecutionId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
