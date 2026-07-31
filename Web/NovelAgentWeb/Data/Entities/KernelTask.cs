namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class KernelTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string GoalId { get; set; } = string.Empty;
    public string TaskGraphVersionId { get; set; } = string.Empty;
    public string? BranchId { get; set; }
    public string KernelName { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public string Status { get; set; } = "queued";
    public string DependencyTaskIdsJson { get; set; } = "[]";
    public string InputArtifactIdsJson { get; set; } = "[]";
    public string OutputArtifactIdsJson { get; set; } = "[]";
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? LeaseOwner { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public string? FailureKind { get; set; }
    public int Attempt { get; set; }
    public int MaxAttempts { get; set; } = 1;
    public int Priority { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
