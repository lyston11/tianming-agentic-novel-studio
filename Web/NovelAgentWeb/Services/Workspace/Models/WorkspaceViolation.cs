// WorkspaceViolation.cs
namespace TM.Web.NovelAgentWeb.Services.Workspace.Models;

public sealed class WorkspaceViolation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ViolationType Type { get; set; }
    public ViolationSeverity Severity { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Endpoint { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? ProjectId { get; set; }
    public string Message { get; set; } = string.Empty;
}
