namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class ProjectContextActivation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string? SourceUserMessageId { get; set; }
    public string? ConfirmationActionId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public long PreviousBindingVersion { get; set; }
    public long BindingVersion { get; set; }
    public DateTime ConfirmedAt { get; set; } = DateTime.UtcNow;

    public AgentSession Session { get; set; } = null!;
}
