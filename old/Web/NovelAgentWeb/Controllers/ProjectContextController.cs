using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tianming.NovelAgent.Application.Conversation;
using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Authorize]
[Route("api/novel-agent")]
public sealed class ProjectContextController(
    ICurrentUserService currentUser,
    ProjectContextApplicationService projectContexts) : ControllerBase
{
    [HttpGet("projects/accessible")]
    public async Task<ActionResult<IReadOnlyList<AccessibleProjectCatalogItem>>> ListAccessibleProjects(
        CancellationToken cancellationToken)
    {
        var projects = await projectContexts.ListAccessibleProjectsAsync(
            currentUser.GetUserId(),
            cancellationToken);
        return Ok(projects);
    }

    [HttpPost("conversations/{sessionId}/project-context/activate")]
    public async Task<ActionResult<ProjectContextActivationResult>> Activate(
        string sessionId,
        [FromBody] ActivateProjectContextRequest request,
        CancellationToken cancellationToken)
    {
        var result = await projectContexts.ActivateAsync(
            new ActivateProjectContextCommand(
                currentUser.GetUserId(),
                sessionId,
                request.ProjectId,
                Request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty,
                request.ExpectedBindingVersion,
                request.SourceUserMessageId,
                request.ConfirmationActionId),
            cancellationToken);
        return result.Code switch
        {
            "conversation_unavailable" or "not_found" => NotFound(result),
            "version_conflict" or "idempotency_conflict" or "switch_not_supported" => Conflict(result),
            "confirmation_required" or "invalid_request" => BadRequest(result),
            "project_unavailable" => UnprocessableEntity(result),
            _ => Ok(result)
        };
    }
}

public sealed record ActivateProjectContextRequest(
    string ProjectId,
    long ExpectedBindingVersion,
    string? SourceUserMessageId = null,
    string? ConfirmationActionId = null);
