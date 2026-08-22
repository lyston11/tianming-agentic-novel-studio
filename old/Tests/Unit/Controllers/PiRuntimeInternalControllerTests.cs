using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using Tianming.NovelAgent.Application.Conversation;
using TM.Web.NovelAgentWeb.Controllers;
using TM.Web.NovelAgentWeb.Services.Agent;
using Xunit;

namespace Tests.Unit.Controllers;

public sealed class PiRuntimeInternalControllerTests
{
    [Fact]
    public async Task GetContext_ReturnsUnauthorizedWithoutInternalKey()
    {
        var controller = CreateController("secret-key", out _);
        // No header supplied.

        var response = await controller.GetContext("session-1", "user-1", CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(response);
    }

    [Fact]
    public async Task GetContext_ReturnsUnauthorizedWithWrongKey()
    {
        var controller = CreateController("secret-key", out _, suppliedKey: "wrong-key");

        var response = await controller.GetContext("session-1", "user-1", CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(response);
    }

    [Fact]
    public async Task GetContext_MapsUnavailableConversationToNotFound()
    {
        var controller = CreateController("secret-key", out var provider, suppliedKey: "secret-key");
        provider.ThrowOnBuild = true;

        var response = await controller.GetContext("session-missing", "user-1", CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(response);
        Assert.Equal(StatusCodes.Status404NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task GetContext_ReturnsPayloadForAuthorizedRequest()
    {
        var controller = CreateController("secret-key", out _, suppliedKey: "secret-key");

        var response = await controller.GetContext("session-1", "user-1", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response);
        var payload = Assert.IsType<PiRuntimeContextPayload>(ok.Value);
        Assert.Equal("unbound", payload.Binding.State);
    }

    [Fact]
    public async Task ActivateProject_RejectsNonIntegerBindingVersion()
    {
        var controller = CreateController("secret-key", out _, suppliedKey: "secret-key");

        var response = await controller.ActivateProject(
            "session-1",
            "user-1",
            new PiRuntimeActivationRequest("project-1", "conversation_user_message:m1", "key-1", "not-a-number"),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(response);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
    }

    [Fact]
    public async Task ActivateProject_MapsConfirmationSourceAndReturnsOk()
    {
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
                2,
                DateTimeOffset.UtcNow));
        var controller = CreateController(
            "secret-key",
            out _,
            suppliedKey: "secret-key",
            store: store.Object);

        var response = await controller.ActivateProject(
            "session-1",
            "user-1",
            new PiRuntimeActivationRequest("project-1", "conversation_user_message:msg-42", "key-1", "1"),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(response);
        Assert.NotNull(captured);
        Assert.Equal("msg-42", captured!.SourceUserMessageId);
        Assert.Null(captured.ConfirmationActionId);
        Assert.Equal(1, captured.ExpectedBindingVersion);
    }

    [Fact]
    public async Task ActivateProject_MapsRecoverableCodesToHttpStatuses()
    {
        foreach (var (code, expectedStatus) in new[]
                 {
                     ("conversation_unavailable", StatusCodes.Status404NotFound),
                     ("project_unavailable", StatusCodes.Status403Forbidden),
                     ("version_conflict", StatusCodes.Status409Conflict),
                     ("confirmation_required", StatusCodes.Status400BadRequest),
                 })
        {
            var store = new Mock<IProjectContextStore>();
            store.Setup(port => port.ActivateAsync(It.IsAny<ActivateProjectContextCommand>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProjectContextActivationResult.Failure(code, code, 1));
            var controller = CreateController(
                "secret-key",
                out _,
                suppliedKey: "secret-key",
                store: store.Object);

            var response = await controller.ActivateProject(
                "session-1",
                "user-1",
                new PiRuntimeActivationRequest("project-1", "conversation_user_message:m1", "key-1", "1"),
                CancellationToken.None);

            var objectResult = Assert.IsAssignableFrom<ObjectResult>(response);
            Assert.Equal(expectedStatus, objectResult.StatusCode);
        }
    }

    private static PiRuntimeInternalController CreateController(
        string internalApiKey,
        out StubProvider provider,
        string? suppliedKey = null,
        IProjectContextStore? store = null)
    {
        provider = new StubProvider();
        var options = Options.Create(new PiRuntimeOptions { InternalApiKey = internalApiKey });
        var controller = new PiRuntimeInternalController(
            options,
            provider,
            new ProjectContextApplicationService(store ?? new Mock<IProjectContextStore>().Object))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        if (suppliedKey is not null)
        {
            controller.Request.Headers["x-pi-runtime-key"] = suppliedKey;
        }
        return controller;
    }

    private sealed class StubProvider : IPiRuntimeContextProvider
    {
        public bool ThrowOnBuild { get; set; }

        public Task<PiRuntimeContextPayload> BuildAsync(
            string userId,
            string sessionId,
            string? query,
            CancellationToken cancellationToken)
        {
            if (ThrowOnBuild)
            {
                throw new InvalidOperationException("Conversation unavailable.");
            }
            return Task.FromResult(new PiRuntimeContextPayload(
                new PiRuntimeBinding("unbound", null, "1"),
                "sys",
                [],
                null,
                []));
        }
    }
}
