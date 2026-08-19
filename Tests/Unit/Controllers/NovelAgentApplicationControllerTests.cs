using Microsoft.AspNetCore.Http;
using Moq;
using Tianming.NovelAgent.Contracts.Conversation;
using Tianming.NovelAgent.Infrastructure.Persistence;
using TM.Web.NovelAgentWeb.Controllers;
using TM.Web.NovelAgentWeb.Services.AgentApplication;
using TM.Web.NovelAgentWeb.Services.Auth;
using Xunit;

namespace Tests.Unit.Controllers;

public sealed class NovelAgentApplicationControllerTests
{
    [Fact]
    public async Task AppendTurn_rejects_an_unowned_conversation_before_application_dispatch()
    {
        var resources = new Mock<INovelAgentResourceAuthorizer>(MockBehavior.Strict);
        resources.Setup(item => item.RequireConversationAsync(
                "user-1", "session-2", "project-2", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Conversation was not found."));
        var controller = CreateController(resources.Object);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => controller.AppendTurn(
            "session-2",
            "project-2",
            new AppendConversationTurnRequest("turn-1", "write"),
            CancellationToken.None));

        resources.VerifyAll();
    }

    [Fact]
    public async Task ConversationStream_authorizes_before_committing_sse_headers()
    {
        var resources = new Mock<INovelAgentResourceAuthorizer>(MockBehavior.Strict);
        resources.Setup(item => item.RequireConversationAsync(
                "user-1", "session-2", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Conversation was not found."));
        var controller = CreateController(resources.Object);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            controller.StreamConversation("session-2", CancellationToken.None));

        Assert.Null(controller.Response.ContentType);
        Assert.False(controller.Response.HasStarted);
        resources.VerifyAll();
    }

    private static NovelAgentApplicationController CreateController(
        INovelAgentResourceAuthorizer resources)
    {
        var currentUser = new Mock<ICurrentUserService>(MockBehavior.Strict);
        currentUser.Setup(item => item.GetUserId()).Returns("user-1");
        var controller = new NovelAgentApplicationController(
            currentUser.Object,
            new AgentUserScope(),
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            resources)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        return controller;
    }
}
