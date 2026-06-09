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
        string message,
        string? userId,
        string? projectId)
    {
        var requestId = context.TraceIdentifier;
        var path = context.Request.Path;

        var violation = violationTracker.RecordViolation(
            userId ?? "unknown",
            projectId ?? "unknown",
            requestId,
            path,
            type,
            message);

        if (LogViolations)
        {
            _logger.LogWarning(
                "Workspace violation: {Type} | {Severity} | {Endpoint} | {Method} | UserId={UserId} | ProjectId={ProjectId} | Count={Count} | {Message}",
                type, violation.Severity, path, context.Request.Method, userId, projectId, violation.Count, message);
        }

        if (IsStrictMode)
        {
            context.Response.StatusCode = 400;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "WorkspaceViolation",
                type = type.ToString(),
                severity = violation.Severity.ToString(),
                message = message,
                count = violation.Count
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

        // Check if workspace is active (only if both userId and projectId are present)
        if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(projectId))
        {
            var status = workspaceFactory.GetWorkspaceStatus(userId, projectId);

            // Workspace not acquired
            if (status == WorkspaceStatus.NotFound)
            {
                var canContinue = await HandleViolationAsync(
                    context, violationTracker,
                    ViolationType.WorkspaceNotAcquired,
                    "Workspace not acquired before use (will be created on demand)",
                    userId, projectId);

                if (!canContinue) return;
            }
        }
        else
        {
            // Missing userId or projectId - cannot check workspace status
            if (string.IsNullOrEmpty(userId))
            {
                var canContinue = await HandleViolationAsync(
                    context, violationTracker,
                    ViolationType.WorkspaceNotAcquired,
                    "User ID missing in request context",
                    userId, projectId);

                if (!canContinue) return;
            }

            if (string.IsNullOrEmpty(projectId))
            {
                var canContinue = await HandleViolationAsync(
                    context, violationTracker,
                    ViolationType.WorkspaceNotAcquired,
                    "Project ID missing in request",
                    userId, projectId);

                if (!canContinue) return;
            }
        }

        await _next(context);
    }
}
