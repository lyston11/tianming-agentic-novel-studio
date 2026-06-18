using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TM.Web.NovelAgentWeb.Controllers;
using TM.Web.NovelAgentWeb.DTOs;
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

    [Fact]
    public void AgentChatResponsePublicProjection_StripsInternalDebugPayload()
    {
        var response = new AgentChatResponse(
            "真实进度回复",
            Array.Empty<string>(),
            "session-1",
            "run-1",
            "queued",
            Decision: new AgentDecisionTrace { Intent = "debug-intent" },
            Rag: new AgentRagContext { Used = true },
            Memory: new AgentWorkingMemorySnapshot
            {
                MissionPlan = new AgentMissionPlan { ProjectId = "project-1" },
                PendingConfirmation = new AgentPendingConfirmation { ImpactSummary = "提交章节" }
            },
            RuntimeTrace: new[] { new AgentRuntimeStep { StepIndex = 1 } });

        var publicResponse = AgentChatResponsePublicProjection.ToPublic(response);

        Assert.Equal("真实进度回复", publicResponse.Reply);
        Assert.Equal("project-1", publicResponse.ActiveProjectId);
        Assert.NotNull(publicResponse.PendingConfirmation);
        Assert.Null(publicResponse.Decision);
        Assert.Null(publicResponse.Rag);
        Assert.Null(publicResponse.Memory);
        Assert.Null(publicResponse.RuntimeTrace);
        Assert.Null(publicResponse.MissionPlan);
    }

    [Fact]
    public void AgentChatResponsePublicProjection_JsonOmitsNullInternalDebugFields()
    {
        var response = new AgentChatResponse(
            "真实进度回复",
            Array.Empty<string>(),
            "session-1",
            Phase: "validated",
            Decision: new AgentDecisionTrace { Intent = "debug-intent" },
            Rag: new AgentRagContext { Used = true },
            Memory: new AgentWorkingMemorySnapshot(),
            RuntimeTrace: new[] { new AgentRuntimeStep { StepIndex = 1 } },
            MissionPlan: new AgentMissionPlan());

        var publicResponse = AgentChatResponsePublicProjection.ToPublic(response);
        var json = JsonSerializer.Serialize(publicResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        Assert.DoesNotContain("decision", json);
        Assert.DoesNotContain("rag", json);
        Assert.DoesNotContain("memory", json);
        Assert.DoesNotContain("runtimeTrace", json);
        Assert.DoesNotContain("missionPlan", json);
        Assert.Contains("activeProjectId", json);
    }
}
