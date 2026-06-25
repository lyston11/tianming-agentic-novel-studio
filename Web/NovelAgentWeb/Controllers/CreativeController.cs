using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Creative;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Route("api/creative/intents")]
[Authorize]
public sealed class CreativeController : ControllerBase
{
    private readonly ICreativeIntentService _creativeIntents;
    private readonly ICurrentUserService _currentUser;

    public CreativeController(
        ICreativeIntentService creativeIntents,
        ICurrentUserService currentUser)
    {
        _creativeIntents = creativeIntents;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> ListIntents(
        [FromQuery] string projectId,
        [FromQuery] string status = "",
        [FromQuery] string targetChapterId = "",
        [FromQuery] int limit = 40,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return BadRequest(ApiErrors.BadRequest("缺少项目 ID。", code: "PROJECT_REQUIRED"));

        var result = await _creativeIntents.QueryAsync(new QueryCreativeIntentsRequest(
            UserId: _currentUser.GetUserId(),
            ProjectId: projectId,
            Status: status,
            TargetChapterId: targetChapterId,
            Limit: limit), ct);

        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> CreateIntent(
        [FromBody] CreateCreativeIntentApiRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId))
            return BadRequest(ApiErrors.BadRequest("缺少项目 ID。", code: "PROJECT_REQUIRED"));
        if (string.IsNullOrWhiteSpace(request.RawContent))
            return BadRequest(ApiErrors.BadRequest("缺少创意原文。", code: "CREATIVE_INTENT_REQUIRED"));

        var item = await _creativeIntents.CreateAsync(new CreateCreativeIntentRequest(
            UserId: _currentUser.GetUserId(),
            ProjectId: request.ProjectId,
            SessionId: request.SessionId,
            RunId: request.RunId,
            IdempotencyKey: Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyKey)
                ? idempotencyKey.ToString()
                : string.Empty,
            RawContent: request.RawContent,
            NormalizedIntent: request.NormalizedIntent,
            Source: request.Source,
            TargetScope: request.TargetScope,
            TargetVolumeId: request.TargetVolumeId,
            TargetChapterId: request.TargetChapterId,
            TargetCharacterName: request.TargetCharacterName,
            ImpactLevel: request.ImpactLevel,
            RequiresConfirmation: request.RequiresConfirmation,
            ConflictStatus: request.ConflictStatus,
            MetadataJson: request.MetadataJson), ct);

        return item == null
            ? NotFound(ApiErrors.NotFound("项目不存在或无权访问。", code: "PROJECT_NOT_FOUND"))
            : Ok(item);
    }

    [HttpPatch("{intentId}/decision")]
    public async Task<IActionResult> DecideIntent(
        string intentId,
        [FromBody] DecideCreativeIntentApiRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId))
            return BadRequest(ApiErrors.BadRequest("缺少项目 ID。", code: "PROJECT_REQUIRED"));
        if (string.IsNullOrWhiteSpace(intentId))
            return BadRequest(ApiErrors.BadRequest("缺少创意 ID。", code: "CREATIVE_INTENT_ID_REQUIRED"));

        var item = await _creativeIntents.DecideAsync(new DecideCreativeIntentRequest(
            UserId: _currentUser.GetUserId(),
            ProjectId: request.ProjectId,
            IntentId: intentId,
            Status: request.Status,
            DecisionReason: request.DecisionReason,
            ConflictStatus: request.ConflictStatus,
            MarkExecuted: request.MarkExecuted), ct);

        return item == null
            ? NotFound(ApiErrors.NotFound("创意不存在或无权访问。", code: "CREATIVE_INTENT_NOT_FOUND"))
            : Ok(item);
    }
}
