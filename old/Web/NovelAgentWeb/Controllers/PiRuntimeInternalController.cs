using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Tianming.NovelAgent.Application.Conversation;
using TM.Web.NovelAgentWeb.Services.Agent;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("internal/pi/conversations/{sessionId}")]
public sealed class PiRuntimeInternalController(
    IOptions<PiRuntimeOptions> options,
    IPiRuntimeContextProvider contextProvider,
    ProjectContextApplicationService projectContexts) : ControllerBase
{
    [HttpGet("context")]
    public async Task<IActionResult> GetContext(
        string sessionId,
        [FromQuery] string userId,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized()) return Unauthorized();
        try
        {
            return Ok(await contextProvider.BuildAsync(userId, sessionId, null, cancellationToken));
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { code = "conversation_unavailable" });
        }
    }

    [HttpGet("projects")]
    public async Task<IActionResult> ListProjects(
        string sessionId,
        [FromQuery] string userId,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized()) return Unauthorized();
        try
        {
            _ = await contextProvider.BuildAsync(userId, sessionId, null, cancellationToken);
            var projects = await projectContexts.ListAccessibleProjectsAsync(userId, cancellationToken);
            return Ok(projects.Select(project => new
            {
                project.ProjectId,
                project.Title,
                project.Status,
                project.UpdatedAt
            }));
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { code = "conversation_unavailable" });
        }
    }

    [HttpPost("project-context")]
    public async Task<IActionResult> ActivateProject(
        string sessionId,
        [FromQuery] string userId,
        [FromBody] PiRuntimeActivationRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized()) return Unauthorized();
        if (!long.TryParse(request.ExpectedBindingVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version))
            return BadRequest(new { code = "invalid_request", reason = "Expected binding version must be an integer." });

        const string sourcePrefix = "conversation_user_message:";
        var sourceUserMessageId = request.ConfirmationSource.StartsWith(sourcePrefix, StringComparison.Ordinal)
            ? request.ConfirmationSource[sourcePrefix.Length..]
            : null;
        var result = await projectContexts.ActivateAsync(
            new ActivateProjectContextCommand(
                userId,
                sessionId,
                request.ProjectId,
                request.IdempotencyKey,
                version,
                sourceUserMessageId),
            cancellationToken);
        var body = new
        {
            code = result.Code,
            projectId = result.ProjectId,
            bindingVersion = result.BindingVersion.ToString(CultureInfo.InvariantCulture),
            reason = result.Message
        };
        if (result.Succeeded) return Ok(body);
        return result.Code switch
        {
            "conversation_unavailable" => NotFound(body),
            "project_unavailable" => StatusCode(StatusCodes.Status403Forbidden, body),
            "version_conflict" => Conflict(body),
            _ => BadRequest(body)
        };
    }

    private bool IsAuthorized()
    {
        var expected = options.Value.InternalApiKey;
        if (string.IsNullOrWhiteSpace(expected)
            || !Request.Headers.TryGetValue("x-pi-runtime-key", out var supplied))
            return false;
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied.ToString()));
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }
}

public sealed record PiRuntimeActivationRequest(
    string ProjectId,
    string ConfirmationSource,
    string IdempotencyKey,
    string ExpectedBindingVersion);
