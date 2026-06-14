using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.AgentSessions;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Workspace;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class AgentController : ControllerBase
{
    private readonly AgentRouter _router;
    private readonly AgentSessionManager _sessionManager;
    private readonly IWorkspaceFactory _workspaceFactory;
    private readonly ProjectScopedExecutor _projectScope;
    private readonly IAgentSessionService _agentSessionService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAgentSessionResumeService _resumeService;
    private readonly IServiceScopeFactory _scopeFactory;

    public AgentController(
        AgentRouter router,
        AgentSessionManager sessionManager,
        IWorkspaceFactory workspaceFactory,
        ProjectScopedExecutor projectScope,
        IAgentSessionService agentSessionService,
        ICurrentUserService currentUserService,
        IAgentSessionResumeService resumeService,
        IServiceScopeFactory scopeFactory)
    {
        _router = router;
        _sessionManager = sessionManager;
        _workspaceFactory = workspaceFactory;
        _projectScope = projectScope;
        _agentSessionService = agentSessionService;
        _currentUserService = currentUserService;
        _resumeService = resumeService;
        _scopeFactory = scopeFactory;
    }

    [HttpPost("agent/chat")]
    public async Task<IActionResult> Chat([FromBody] AgentChatRequest request, CancellationToken ct)
    {
        var sessionId = request.SessionId;
        var response = await _router.HandleAsync(sessionId, request.Message ?? "", ct);
        return Ok(response);
    }

    [HttpGet("agent/sse/{sessionId}")]
    public async Task StreamEvents(string sessionId, [FromQuery] string? token, CancellationToken ct)
    {
        // Token is handled by OnMessageReceived in Program.cs JWT configuration

        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        var reader = _sessionManager.GetEventReader(sessionId);

        try
        {
            await foreach (var evt in reader.ReadAllAsync(ct))
            {
                var json = JsonSerializer.Serialize(evt, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                });
                await Response.WriteAsync($"data: {json}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) { /* client disconnected */ }
    }

    [HttpGet("agent/session/{sessionId}")]
    public async Task<IActionResult> GetSession(string sessionId, CancellationToken ct)
    {
        try
        {
            var userId = _currentUserService.GetUserId();
            var isAdmin = _currentUserService.IsAdmin();
            var session = await _agentSessionService.GetSessionByIdAsync(sessionId, userId, isAdmin, ct);
            return Ok(session);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("agent/sessions")]
    public async Task<IActionResult> ListSessions(CancellationToken ct)
    {
        var userId = _currentUserService.GetUserId();
        var isAdmin = _currentUserService.IsAdmin();

        var sessions = await _agentSessionService.ListUserSessionsAsync(userId, isAdmin, includeArchived: false, ct);
        return Ok(sessions);
    }

    [HttpGet("agent/sessions/{sessionId}/resume")]
    public async Task<IActionResult> ResumeSession(string sessionId, CancellationToken ct)
    {
        try
        {
            var session = await _resumeService.ResumeAsync(sessionId, ct);
            return Ok(session);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost("agent/session")]
    public async Task<IActionResult> CreateSession([FromQuery] string? projectId, CancellationToken ct)
    {
        var userId = _currentUserService.GetUserId();
        var session = await _agentSessionService.GetOrCreateSessionAsync(null, userId, projectId, ct);
        return Ok(session);
    }

    [HttpPatch("agent/session/{sessionId}")]
    public async Task<IActionResult> UpdateSession(string sessionId, [FromBody] AgentSessionUpdateRequest request, CancellationToken ct)
    {
        try
        {
            var userId = _currentUserService.GetUserId();
            var isAdmin = _currentUserService.IsAdmin();

            var updateRequest = new Models.AgentSessions.UpdateAgentSessionRequest
            {
                Title = request.Title,
                IsArchived = request.IsArchived
            };

            var session = await _agentSessionService.UpdateSessionAsync(sessionId, updateRequest, userId, isAdmin, ct);
            return Ok(session);
        }
        catch (KeyNotFoundException)
        {
            return NotFound("Session not found.");
        }
    }

    [HttpPost("agent/step/{sessionId}/rollback")]
    public async Task<IActionResult> RollbackStep(
        string sessionId,
        [FromBody] RollbackStepRequest request,
        CancellationToken ct)
    {
        var session = await _sessionManager.GetSessionAsync(sessionId, ct);
        if (session == null) return NotFound("Session not found.");

        var userId = _currentUserService.GetUserId();
        var projectId = session.ActiveProjectId;

        if (string.IsNullOrEmpty(projectId))
            return BadRequest("Session has no active project.");

        var workspaceEntry = await _workspaceFactory.AcquireAsync(userId, projectId, ct);
        try
        {
            var catalog = new NovelProjectCatalog(workspaceEntry.Workspace, _scopeFactory);
            workspaceEntry.Workspace.SetRequestContext();
            ProjectScopedExecutor.SetCatalog(catalog);
            var bible = await _projectScope.RunSessionAsync(session,
                () => workspaceEntry.Workspace.Orchestrator.GetStoryBibleAsync(ct), ct);

            var run = bible.AgentRuns.FirstOrDefault(r => r.RunId == request.RunId);
            if (run == null) return NotFound("Run not found.");

            // Find the target step and reset all steps after it
            var stepIndex = run.Steps.FindIndex(s => s.Id == request.StepId);
            if (stepIndex < 0) return NotFound("Step not found.");

            // Reset steps from the target onwards
            for (var i = stepIndex; i < run.Steps.Count; i++)
            {
                run.Steps[i].Status = i == stepIndex ? NovelAgentStepStatus.Pending : NovelAgentStepStatus.Pending;
            }
            run.Status = NovelAgentRunStatus.Planning;
            session.ActiveRunId = run.RunId;

            await _sessionManager.SendEventAsync(sessionId, new AgentSseEvent
            {
                Type = AgentSseEventType.RunUpdate,
                RunId = run.RunId,
                Message = $"已回退到步骤: {run.Steps[stepIndex].Name}",
                Data = run,
            }, ct);

            return Ok(new { success = true, message = $"已回退到步骤: {run.Steps[stepIndex].Name}" });
        }
        finally
        {
            ProjectScopedExecutor.ClearCatalog();
            workspaceEntry.Workspace.ClearRequestContext();
            if (!string.IsNullOrEmpty(projectId))
                _workspaceFactory.Release(userId, projectId);
        }
    }

    private static AgentSessionSummary ToSummary(AgentSession session) => new(
        session.SessionId,
        session.Title,
        session.Phase,
        session.ActiveProjectId,
        session.ActiveRunId,
        session.IsArchived,
        session.UpdatedAt.ToString("O"),
        session.ChatHistory.Count);

    private static AgentSessionDetail ToDetail(AgentSession session) => new(
        session.SessionId,
        session.Title,
        session.Phase,
        session.ActiveProjectId,
        session.ActiveRunId,
        session.IsArchived,
        session.RunHistory,
        session.CreatedAt.ToString("O"),
        session.UpdatedAt.ToString("O"),
        session.ChatHistory.Select(turn => new AgentConversationTurnView(
            turn.Role,
            turn.Content,
            turn.CreatedAt.ToString("O"))).ToList(),
        AgentWorkingMemorySnapshot.From(session.WorkingMemory));
}
