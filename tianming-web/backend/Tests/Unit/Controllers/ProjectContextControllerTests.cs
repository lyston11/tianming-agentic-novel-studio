using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Tianming.NovelAgent.Application.Conversation;
using TM.Web.NovelAgentWeb.Controllers;
using TM.Web.NovelAgentWeb.Services.Auth;
using Xunit;

namespace Tests.Unit.Controllers;

public sealed class ProjectContextControllerTests
{
    [Fact]
    public async Task ActivateProjectContext_UsesAuthenticatedUserAndIdempotencyHeader()
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(service => service.GetUserId()).Returns("user-1");
        ActivateProjectContextCommand? captured = null;
        var store = new Mock<IProjectContextStore>();
        store.Setup(port => port.ActivateAsync(It.IsAny<ActivateProjectContextCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ActivateProjectContextCommand, CancellationToken>((command, _) => captured = command)
            .ReturnsAsync(new ProjectContextActivationResult(
                "activated",
                true,
                false,
                "项目上下文已激活。",
                "project-1",
                1,
                DateTimeOffset.UtcNow));
        var controller = new ProjectContextController(
            currentUser.Object,
            new ProjectContextApplicationService(store.Object))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.Request.Headers["Idempotency-Key"] = "activate-1";

        var response = await controller.Activate(
            "session-1",
            new ActivateProjectContextRequest(
                "project-1",
                0,
                "message-1",
                null),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(response.Result);
        Assert.NotNull(captured);
        Assert.Equal("user-1", captured!.UserId);
        Assert.Equal("session-1", captured.SessionId);
        Assert.Equal("project-1", captured.ProjectId);
        Assert.Equal("activate-1", captured.IdempotencyKey);
        Assert.Equal("message-1", captured.SourceUserMessageId);
    }

    [Fact]
    public async Task ActivateProjectContext_ReturnsNotFoundForUnavailableConversation()
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(service => service.GetUserId()).Returns("user-1");
        var store = new Mock<IProjectContextStore>();
        store.Setup(port => port.ActivateAsync(It.IsAny<ActivateProjectContextCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProjectContextActivationResult.Failure(
                "conversation_unavailable",
                "Conversation 不可用。"));
        var controller = new ProjectContextController(
            currentUser.Object,
            new ProjectContextApplicationService(store.Object))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.Request.Headers["Idempotency-Key"] = "activate-missing";

        var response = await controller.Activate(
            "session-missing",
            new ActivateProjectContextRequest(
                "project-1",
                0,
                null,
                "action-1"),
            CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status404NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task ActivateProjectContext_ReturnsConflictForRecoverableVersionConflict()
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(service => service.GetUserId()).Returns("user-1");
        var store = new Mock<IProjectContextStore>();
        store.Setup(port => port.ActivateAsync(It.IsAny<ActivateProjectContextCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProjectContextActivationResult.Failure(
                "version_conflict",
                "版本冲突。",
                2));
        var controller = new ProjectContextController(
            currentUser.Object,
            new ProjectContextApplicationService(store.Object))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.Request.Headers["Idempotency-Key"] = "activate-stale";

        var response = await controller.Activate(
            "session-1",
            new ActivateProjectContextRequest(
                "project-1",
                1,
                null,
                "action-1"),
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
    }
}
