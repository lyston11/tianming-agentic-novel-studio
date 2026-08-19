using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tianming.NovelAgent.Application.Conversation;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Application.Production;
using Tianming.NovelAgent.Application.Workflow;
using Tianming.NovelAgent.Contracts.Conversation;
using Tianming.NovelAgent.Contracts.Streaming;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.AgentApplication;
using TM.Web.NovelAgentWeb.Services.Workflow;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Authorize]
[Route("api/novel-agent")]
public sealed class NovelAgentApplicationController(
    ICurrentUserService currentUser,
    IUserScope userScope,
    ConversationApplicationService conversations,
    LegacyRecoveryApplicationService legacyRecovery,
    WorkflowApplicationService workflow,
    ProductionApplicationService productions,
    IWorkflowReadModel workflowReadModel,
    IStreamEventReader streams,
    ILegacyProjectSnapshotReader legacySnapshots,
    IWorkflowService legacyWorkflow,
    INovelAgentResourceAuthorizer resources) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [HttpPost("conversations/{sessionId}/turns")]
    public async Task<ActionResult<ConversationTurnResult>> AppendTurn(
        string sessionId,
        [FromQuery] string projectId,
        [FromBody] AppendConversationTurnRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId();
        using var _ = userScope.Enter(userId);
        await resources.RequireConversationAsync(userId, sessionId, projectId, cancellationToken);
        return Ok(await conversations.AppendTurnAsync(
            userId,
            projectId,
            sessionId,
            request,
            cancellationToken));
    }

    [HttpPost("proposals/{proposalId}/confirm")]
    public async Task<ActionResult<ConfirmGoalProposalResult>> ConfirmProposal(
        string proposalId,
        [FromBody] ConfirmGoalProposalRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId();
        using var _ = userScope.Enter(userId);
        return Ok(await workflow.ConfirmProposalAsync(
            userId,
            proposalId,
            userId,
            request,
            cancellationToken));
    }

    [HttpPost("productions/{productionId}/commands/start")]
    public async Task<IActionResult> StartProduction(string productionId, CancellationToken cancellationToken)
    {
        await WithUserScopeAsync((userId, token) => productions.StartAsync(userId, productionId, token), cancellationToken);
        return Ok(new { productionId, status = "running" });
    }

    [HttpPost("productions/{productionId}/commands/pause")]
    public async Task<IActionResult> PauseProduction(
        string productionId,
        [FromBody] PauseProductionRequest request,
        CancellationToken cancellationToken)
    {
        await WithUserScopeAsync(
            (userId, token) => productions.PauseAsync(userId, productionId, request.HasRunningTask, token),
            cancellationToken);
        return Ok(new { productionId, status = request.HasRunningTask ? "pausing" : "paused" });
    }

    [HttpPost("productions/{productionId}/commands/resume")]
    public async Task<IActionResult> ResumeProduction(string productionId, CancellationToken cancellationToken)
    {
        await WithUserScopeAsync((userId, token) => productions.ResumeAsync(userId, productionId, token), cancellationToken);
        return Ok(new { productionId, status = "running" });
    }

    [HttpPost("productions/{productionId}/commands/cancel")]
    public async Task<IActionResult> CancelProduction(
        string productionId,
        [FromBody] CancelProductionRequest request,
        CancellationToken cancellationToken)
    {
        await WithUserScopeAsync(
            (userId, token) => productions.CancelAsync(userId, productionId, request.Reason, token),
            cancellationToken);
        return Ok(new { productionId, status = "cancelled" });
    }

    [HttpPost("productions/{productionId}/commands/accept-prefix")]
    public async Task<IActionResult> AcceptPrefix(
        string productionId,
        [FromBody] AcceptPrefixRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId();
        using var _ = userScope.Enter(userId);
        var requestId = await productions.AcceptPrefixAsync(
            userId,
            productionId,
            request.BranchId,
            request.AcceptedThroughChapter,
            $"workflow:{userId}",
            request.IdempotencyKey,
            request.CorrelationId,
            cancellationToken);
        return Accepted(new { productionId, requestId, status = "mergingCanon" });
    }

    [HttpGet("workflows/projects/{projectId}")]
    public async Task<IActionResult> GetWorkflow(string projectId, CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId();
        using var _ = userScope.Enter(userId);
        await resources.RequireProjectAsync(userId, projectId, cancellationToken);
        var current = await workflowReadModel.GetProjectAsync(userId, projectId, cancellationToken);
        var legacy = await legacyWorkflow.GetProjectWorkflowAsync(projectId, cancellationToken);
        return Ok(new { current, legacy });
    }

    [HttpGet("legacy/projects/{projectId}/recoverable-snapshot")]
    public async Task<IActionResult> GetRecoverableSnapshot(string projectId, CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId();
        using var _ = userScope.Enter(userId);
        await resources.RequireProjectAsync(userId, projectId, cancellationToken);
        return Ok(await legacySnapshots.ReadRecoverableSnapshotAsync(userId, projectId, cancellationToken));
    }

    [HttpPost("legacy/projects/{projectId}/recovery-proposals")]
    public async Task<ActionResult<CreateLegacyRecoveryProposalResult>> CreateRecoveryProposal(
        string projectId,
        [FromBody] CreateLegacyRecoveryProposalRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId();
        using var _ = userScope.Enter(userId);
        await resources.RequireProjectAsync(userId, projectId, cancellationToken);
        return Ok(await legacyRecovery.CreateProposalAsync(userId, projectId, request, cancellationToken));
    }

    [HttpGet("streams/conversations/{sessionId}")]
    public Task StreamConversation(string sessionId, CancellationToken cancellationToken) =>
        StreamAsync(AgentStreamKind.Conversation, sessionId, cancellationToken);

    [HttpGet("streams/workflows/{projectId}")]
    public Task StreamWorkflow(string projectId, CancellationToken cancellationToken) =>
        StreamAsync(AgentStreamKind.Workflow, projectId, cancellationToken);

    private async Task StreamAsync(
        AgentStreamKind kind,
        string streamId,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId();
        using var _ = userScope.Enter(userId);
        if (kind == AgentStreamKind.Conversation)
            await resources.RequireConversationAsync(userId, streamId, projectId: null, cancellationToken);
        else
            await resources.RequireProjectAsync(userId, streamId, cancellationToken);
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        var cursor = ReadCursor(kind, streamId);
        var lastKeepAlive = DateTimeOffset.UtcNow;
        while (!cancellationToken.IsCancellationRequested)
        {
            var events = await streams.ReadAsync(userId, kind, streamId, cursor, 100, cancellationToken);
            foreach (var envelope in events)
            {
                var eventCursor = new AgentEventCursor(kind, streamId, envelope.Sequence).ToString();
                await Response.WriteAsync($"id: {eventCursor}\n", cancellationToken);
                await Response.WriteAsync($"event: {envelope.EventType}\n", cancellationToken);
                await Response.WriteAsync($"data: {JsonSerializer.Serialize(envelope, JsonOptions)}\n\n", cancellationToken);
                cursor = envelope.Sequence;
            }
            if (events.Count > 0)
            {
                await Response.Body.FlushAsync(cancellationToken);
                lastKeepAlive = DateTimeOffset.UtcNow;
                continue;
            }
            if (DateTimeOffset.UtcNow - lastKeepAlive >= TimeSpan.FromSeconds(15))
            {
                await Response.WriteAsync(": keep-alive\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
                lastKeepAlive = DateTimeOffset.UtcNow;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
        }
    }

    private long ReadCursor(AgentStreamKind kind, string streamId)
    {
        var value = Request.Headers["Last-Event-ID"].FirstOrDefault() ?? Request.Query["cursor"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(value))
            return 0;
        if (!AgentEventCursor.TryParse(value, out var cursor)
            || cursor.StreamKind != kind
            || cursor.StreamId != streamId)
            throw new BadHttpRequestException("The SSE cursor does not belong to this stream.");
        return cursor.Sequence;
    }

    private async Task WithUserScopeAsync(
        Func<string, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId();
        using var _ = userScope.Enter(userId);
        await action(userId, cancellationToken);
    }
}

public sealed record PauseProductionRequest(bool HasRunningTask);
public sealed record CancelProductionRequest(string Reason);
public sealed record AcceptPrefixRequest(
    string BranchId,
    int AcceptedThroughChapter,
    string IdempotencyKey,
    string CorrelationId);
