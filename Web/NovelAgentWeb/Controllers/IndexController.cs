using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Production;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Route("api/index")]
[Authorize]
public sealed class IndexController : ControllerBase
{
    private readonly IProductionOutboxAdminService _outbox;
    private readonly ICurrentUserService _currentUser;

    public IndexController(
        IProductionOutboxAdminService outbox,
        ICurrentUserService currentUser)
    {
        _outbox = outbox;
        _currentUser = currentUser;
    }

    [HttpGet("outbox")]
    public async Task<IActionResult> GetOutbox(
        [FromQuery] string? projectId,
        [FromQuery] string? status,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        if (!_currentUser.IsAdmin())
            return AdminRequired();

        var result = await _outbox.ListAsync(projectId, status, limit, ct).ConfigureAwait(false);
        return Ok(result);
    }

    [HttpPost("outbox/{eventId}/retry")]
    public async Task<IActionResult> RetryOutbox(string eventId, CancellationToken ct)
    {
        if (!_currentUser.IsAdmin())
            return AdminRequired();

        try
        {
            var result = await _outbox.RetryAsync(
                    eventId,
                    HttpContext?.Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyKey) == true
                        ? idempotencyKey.ToString()
                        : string.Empty,
                    ct)
                .ConfigureAwait(false);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiErrors.NotFound(
                "Outbox event not found.",
                code: "OUTBOX_EVENT_NOT_FOUND",
                recommendedAction: "请确认 outbox event id 后重试。"));
        }
    }

    private ObjectResult AdminRequired() =>
        StatusCode(StatusCodes.Status403Forbidden, ApiErrors.Create(
            "ADMIN_REQUIRED",
            "该接口仅管理员可用。",
            "authorization",
            recoverable: false,
            recommendedAction: "请使用管理员账号或改用普通项目查询接口。"));
}
