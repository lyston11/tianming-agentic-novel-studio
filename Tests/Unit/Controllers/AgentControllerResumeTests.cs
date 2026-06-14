using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TM.Web.NovelAgentWeb.Controllers;
using TM.Web.NovelAgentWeb.Models.AgentSessions;
using TM.Web.NovelAgentWeb.Services.AgentSessions;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Controllers;

public class AgentControllerResumeTests
{
    [Fact]
    public async Task ResumeSession_ReturnsLightweightResumePayload()
    {
        var expected = new AgentSessionResumeResponse
        {
            SessionId = "session-1",
            ActiveProjectId = "project-1",
            ActiveRunId = "run-1",
            DiscoveredPhase = "Planning",
            DiscoveredTools = new[]
            {
                new ToolSchema { Name = "PlanChapter", Description = "规划章节" }
            },
            ToolSearchCacheFresh = true,
            HasPendingConfirmation = true
        };
        var resume = new Mock<IAgentSessionResumeService>();
        resume
            .Setup(x => x.ResumeAsync("session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var controller = new AgentController(
            router: null!,
            sessionManager: null!,
            workspaceFactory: null!,
            projectScope: null!,
            agentSessionService: null!,
            currentUserService: Mock.Of<ICurrentUserService>(),
            resumeService: resume.Object,
            scopeFactory: Mock.Of<IServiceScopeFactory>());

        var result = await controller.ResumeSession("session-1", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(expected, ok.Value);
    }

    [Fact]
    public async Task ResumeSession_ReturnsNotFoundForMissingSession()
    {
        var resume = new Mock<IAgentSessionResumeService>();
        resume
            .Setup(x => x.ResumeAsync("missing-session", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());

        var controller = new AgentController(
            router: null!,
            sessionManager: null!,
            workspaceFactory: null!,
            projectScope: null!,
            agentSessionService: null!,
            currentUserService: Mock.Of<ICurrentUserService>(),
            resumeService: resume.Object,
            scopeFactory: Mock.Of<IServiceScopeFactory>());

        var result = await controller.ResumeSession("missing-session", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }
}
