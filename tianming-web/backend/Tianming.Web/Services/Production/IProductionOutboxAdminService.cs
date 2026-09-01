namespace TM.Web.NovelAgentWeb.Services.Production;

public interface IProductionOutboxAdminService
{
    Task<OutboxAdminListResponse> ListAsync(
        string? projectId,
        string? status,
        int limit,
        CancellationToken cancellationToken = default);

    Task<OutboxAdminRetryResponse> RetryAsync(
        string eventId,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);
}

public sealed class OutboxAdminListResponse
{
    public int Count { get; set; }
    public List<OutboxAdminItem> Items { get; set; } = new();
}

public sealed class OutboxAdminRetryResponse
{
    public string EventId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public string LastError { get; set; } = string.Empty;
    public DateTime? NextAttemptAt { get; set; }
    public int DispatchAttempted { get; set; }
    public int PendingCount { get; set; }
}

public sealed class OutboxAdminItem
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string RuntimeRunId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string AggregateType { get; set; } = string.Empty;
    public string AggregateId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public string LastError { get; set; } = string.Empty;
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
