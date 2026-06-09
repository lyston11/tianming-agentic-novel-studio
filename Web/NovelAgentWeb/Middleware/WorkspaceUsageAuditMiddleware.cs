using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TM.Web.NovelAgentWeb.Services.Workspace;
using TM.Web.NovelAgentWeb.Services.Workspace.Models;

namespace TM.Web.NovelAgentWeb.Middleware;

/// <summary>
/// Middleware that audits workspace usage and enforces workspace lifecycle compliance.
/// Implements B+ progressive enforcement: strict mode in dev/test, log-only mode in production.
/// </summary>
public class WorkspaceUsageAuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<WorkspaceUsageAuditMiddleware> _logger;
    private readonly IConfiguration _configuration;

    public WorkspaceUsageAuditMiddleware(
        RequestDelegate next,
        ILogger<WorkspaceUsageAuditMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _configuration = configuration;
    }

    private bool IsStrictMode => _configuration.GetValue<bool>("WorkspaceAudit:EnableStrictMode");
    private bool LogViolations => _configuration.GetValue<bool>("WorkspaceAudit:LogViolations", true);
    private bool TrackViolations => _configuration.GetValue<bool>("WorkspaceAudit:TrackViolations", true);

    private static readonly HashSet<string> WorkspaceEndpoints = new()
    {
        "/api/agent",
        "/api/chapters",
        "/api/materials",
        "/api/knowledge",
        "/api/storybible",
        "/api/workflow"
    };

    private bool IsWorkspaceEndpoint(string path)
    {
        return WorkspaceEndpoints.Any(endpoint => path.StartsWith(endpoint, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string?> ExtractProjectIdAsync(HttpContext context)
    {
        // 1. Try route values
        if (context.Request.RouteValues.TryGetValue("projectId", out var routeProjectId))
        {
            return routeProjectId?.ToString();
        }

        // 2. Try request body (for POST/PUT)
        if (context.Request.ContentType?.Contains("application/json") == true &&
            (context.Request.Method == "POST" || context.Request.Method == "PUT"))
        {
            context.Request.EnableBuffering();
            context.Request.Body.Position = 0;

            try
            {
                using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
                var body = await reader.ReadToEndAsync();
                context.Request.Body.Position = 0;

                if (!string.IsNullOrEmpty(body))
                {
                    var jsonDoc = JsonDocument.Parse(body);
                    if (jsonDoc.RootElement.TryGetProperty("projectId", out var projectIdElement))
                    {
                        return projectIdElement.GetString();
                    }
                }
            }
            catch
            {
                context.Request.Body.Position = 0;
            }
        }

        // 3. Try query string
        if (context.Request.Query.TryGetValue("projectId", out var queryProjectId))
        {
            return queryProjectId.ToString();
        }

        // 4. Try headers
        if (context.Request.Headers.TryGetValue("X-Project-Id", out var headerProjectId))
        {
            return headerProjectId.ToString();
        }

        return null;
    }

    private async Task<bool> HandleViolationAsync(
        HttpContext context,
        IWorkspaceViolationTracker violationTracker,
        ViolationType type,
        ViolationSeverity severity,
        string message,
        string? userId,
        string? projectId)
    {
        var violation = new WorkspaceViolation
        {
            Type = type,
            Severity = severity,
            Timestamp = DateTime.UtcNow,
            Endpoint = context.Request.Path,
            Method = context.Request.Method,
            UserId = userId,
            ProjectId = projectId,
            Message = message
        };

        if (LogViolations)
        {
            _logger.LogWarning(
                "Workspace violation: {Type} | {Severity} | {Endpoint} | {Method} | UserId={UserId} | ProjectId={ProjectId} | {Message}",
                type, severity, context.Request.Path, context.Request.Method, userId, projectId, message);
        }

        if (TrackViolations)
        {
            violationTracker.TrackViolation(violation);
        }

        if (IsStrictMode)
        {
            context.Response.StatusCode = 400;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "WorkspaceViolation",
                type = type.ToString(),
                message = message
            });
            return false;
        }

        return true;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IWorkspaceFactory workspaceFactory,
        IWorkspaceViolationTracker violationTracker)
    {
        // Skip non-workspace endpoints
        if (!IsWorkspaceEndpoint(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // Extract userId from HttpContext.Items (set by UserContextMiddleware)
        var userId = context.Items[UserContextMiddleware.UserIdKey] as string;

        // Extract projectId from multiple sources
        var projectId = await ExtractProjectIdAsync(context);

        // Validate userId
        if (string.IsNullOrEmpty(userId))
        {
            var canContinue = await HandleViolationAsync(
                context, violationTracker,
                ViolationType.MissingUserId,
                ViolationSeverity.High,
                "User ID missing in request context",
                userId, projectId);

            if (!canContinue) return;
        }

        // Validate projectId
        if (string.IsNullOrEmpty(projectId))
        {
            var canContinue = await HandleViolationAsync(
                context, violationTracker,
                ViolationType.MissingProjectId,
                ViolationSeverity.Medium,
                "Project ID missing in request",
                userId, projectId);

            if (!canContinue) return;
        }

        // Check workspace is active (only if both userId and projectId are present)
        if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(projectId))
        {
            if (!workspaceFactory.IsWorkspaceActive(userId, projectId))
            {
                var canContinue = await HandleViolationAsync(
                    context, violationTracker,
                    ViolationType.WorkspaceNotActive,
                    ViolationSeverity.Low,
                    "Workspace not active in cache (will be created on demand)",
                    userId, projectId);

                if (!canContinue) return;
            }
        }

        await _next(context);
    }
}
