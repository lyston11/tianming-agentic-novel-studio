namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class ModelExecution
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string GoalId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string KernelName { get; set; } = string.Empty;
    public string ModelConfigVersionId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string OperationKey { get; set; } = string.Empty;
    public string Status { get; set; } = "reserved";
    public decimal ReservedCost { get; set; }
    public decimal ActualCost { get; set; }
    public string Currency { get; set; } = "USD";
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public string? ProviderRequestId { get; set; }
    public string? ProviderLeaseOwner { get; set; }
    public int Attempt { get; set; }
    public string? ResultJson { get; set; }
    public string? ResultContentHash { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
