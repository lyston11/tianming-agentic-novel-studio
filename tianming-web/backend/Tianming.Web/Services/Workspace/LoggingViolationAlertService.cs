using Microsoft.Extensions.Logging;
using TM.Web.NovelAgentWeb.Services.Workspace.Models;

namespace TM.Web.NovelAgentWeb.Services.Workspace;

public class LoggingViolationAlertService : IViolationAlertService
{
    private readonly ILogger<LoggingViolationAlertService> _logger;

    public LoggingViolationAlertService(ILogger<LoggingViolationAlertService> logger)
    {
        _logger = logger;
    }

    public Task SendAlertAsync(WorkspaceViolation violation)
    {
        _logger.LogWarning(
            "ALERT: High-frequency violation detected - Type: {Type}, User: {UserId}, Project: {ProjectId}, Count: {Count}, Severity: {Severity}",
            violation.Type,
            violation.UserId,
            violation.ProjectId,
            violation.Count,
            violation.Severity);

        // In production, this could send emails, Slack messages, etc.
        return Task.CompletedTask;
    }
}
