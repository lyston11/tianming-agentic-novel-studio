using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Moq;
using TM.Web.NovelAgentWeb.Controllers;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Filters;
using TM.Web.NovelAgentWeb.Models.AgentSessions;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.AgentSessions;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Support;
using Tianming.NovelAgent.Application.Conversation;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Contracts.Conversation;
using Xunit;

namespace Tests.Unit.Controllers;

public class AgentControllerResumeTests
{
    [Fact]
    public async Task Chat_DelegatesToApplicationConversationAndReturnsDecisionReply()
    {
        var conversations = BuildConversationService(
            "当然认识你，lyston。",
            out var runtime);
        var controller = new AgentController(
            conversations,
            sessions: null!,
            currentUserService: CurrentUser("user-1").Object,
            resumeService: null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.Request.Headers["Idempotency-Key"] = "chat-key-1";

        var result = await controller.Chat(new AgentChatRequest("你知道我是谁吗", "session-1", "chat-key-1"), CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        Assert.True(envelope.Success);
        Assert.Equal("v1", envelope.ApiVersion);
        var data = Assert.IsType<AgentChatResponse>(envelope.Data);
        Assert.Equal("当然认识你，lyston。", data.Reply);
        Assert.Equal("session-1", data.SessionId);
        runtime.Verify(x => x.RunTurnAsync(It.IsAny<ConversationTurnContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Chat_RejectsTurnWithoutIdempotencyKey()
    {
        var conversations = BuildConversationService("unused", out _);
        var controller = new AgentController(
            conversations,
            sessions: null!,
            currentUserService: CurrentUser("user-1").Object,
            resumeService: null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await controller.Chat(new AgentChatRequest("你好", "session-1"), CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        Assert.False(envelope.Success);
    }

    [Fact]
    public async Task Chat_ReturnsNotFoundEnvelopeForMissingSession()
    {
        var bindings = new Mock<IConversationSessionBindingReader>();
        bindings.Setup(x => x.GetBindingAsync("user-1", "missing-session", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());
        var conversations = BuildConversationService("unused", out _, bindings.Object);
        var controller = new AgentController(
            conversations,
            sessions: null!,
            currentUserService: CurrentUser("user-1").Object,
            resumeService: null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.Request.Headers["Idempotency-Key"] = "chat-key-2";

        var result = await controller.Chat(new AgentChatRequest("你好", "missing-session", "chat-key-2"), CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        Assert.False(envelope.Success);
        Assert.Equal("SESSION_NOT_FOUND", envelope.Error!.Code);
    }

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
            conversations: null!,
            sessions: null!,
            currentUserService: Mock.Of<ICurrentUserService>(),
            resumeService: resume.Object);

        var result = await controller.ResumeSession("session-1", CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        Assert.True(envelope.Success);
        Assert.Equal("v1", envelope.ApiVersion);
        Assert.Same(expected, envelope.Data);
    }

    [Fact]
    public async Task CreateSession_ReadsIdempotencyKeyHeaderIntoService()
    {
        string? capturedIdempotencyKey = null;
        var agentSessions = new Mock<IAgentSessionApplicationService>();
        agentSessions
            .Setup(x => x.CreateUnboundSessionAsync(
                "user-1",
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string?, CancellationToken>((_, key, _) => capturedIdempotencyKey = key)
            .ReturnsAsync(new AgentSessionResponse
            {
                SessionId = "session-1",
                Title = "新会话",
                ActiveProjectId = string.Empty,
                Phase = "idle",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        var controller = new AgentController(
            conversations: null!,
            sessions: agentSessions.Object,
            currentUserService: CurrentUser("user-1").Object,
            resumeService: null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.Request.Headers["Idempotency-Key"] = "session-create-key-001";

        var result = await controller.CreateSession(CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        Assert.True(envelope.Success);
        Assert.Equal("session-create-key-001", capturedIdempotencyKey);
    }

    [Fact]
    public async Task CreateSession_WithLegacyProjectQuery_ReturnsExplicitCompatibilityError()
    {
        var agentSessions = new Mock<IAgentSessionApplicationService>(MockBehavior.Strict);
        var controller = new AgentController(
            conversations: null!,
            sessions: agentSessions.Object,
            currentUserService: CurrentUser("user-1").Object,
            resumeService: null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.Request.QueryString = new QueryString("?projectId=project-1");

        var result = await controller.CreateSession(CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        Assert.False(envelope.Success);
        Assert.Equal("SESSION_PROJECT_QUERY_UNSUPPORTED", envelope.Error!.Code);
        agentSessions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResumeSession_ReturnsNotFoundForMissingSession()
    {
        var resume = new Mock<IAgentSessionResumeService>();
        resume
            .Setup(x => x.ResumeAsync("missing-session", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());

        var controller = new AgentController(
            conversations: null!,
            sessions: null!,
            currentUserService: Mock.Of<ICurrentUserService>(),
            resumeService: resume.Object);

        var result = await controller.ResumeSession("missing-session", CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        Assert.False(envelope.Success);
        Assert.Equal("SESSION_NOT_FOUND", envelope.Error!.Code);
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
    public void AgentChatResponsePublicProjection_PreservesSafeMemoryAuditSummary()
    {
        var response = new AgentChatResponse(
            "真实进度回复",
            Array.Empty<string>(),
            "session-1",
            "run-1",
            "completed",
            Memory: new AgentWorkingMemorySnapshot
            {
                ProjectMemory = new AgentProjectMemory
                {
                    LongTermGoal = "不应该把完整项目记忆暴露到公开响应"
                }
            },
            MemoryAudit: new AgentMemoryAuditSummary(
                new[]
                {
                    new AgentMemoryReadAuditSummary(
                        "read-1",
                        "project-1",
                        "session-1",
                        "run-1",
                        "author",
                        new[] { "display_name", "style_likes" },
                        "memory_repository",
                        "AgentObservationBuilder",
                        DateTime.UtcNow)
                },
                new[]
                {
                    new AgentMemoryPromotionAuditSummary(
                        "promotion-1",
                        "project-1",
                        "session-1",
                        "run-1",
                        "session",
                        "project",
                        "user_preferences",
                        "project.constraints",
                        "用户明确要求后续章节保持打怪升级节奏。",
                        DateTime.UtcNow)
                }));

        var publicResponse = AgentChatResponsePublicProjection.ToPublic(response);

        Assert.Null(publicResponse.Memory);
        Assert.NotNull(publicResponse.MemoryAudit);
        var read = Assert.Single(publicResponse.MemoryAudit!.Reads);
        Assert.Equal("author", read.MemoryScope);
        Assert.Equal(new[] { "display_name", "style_likes" }, read.MemoryKeys);
        var promotion = Assert.Single(publicResponse.MemoryAudit.Promotions);
        Assert.Equal("session", promotion.SourceScope);
        Assert.Equal("project", promotion.TargetScope);
        Assert.Equal("project.constraints", promotion.TargetMemoryKey);
        Assert.Equal("用户明确要求后续章节保持打怪升级节奏。", promotion.PromotionReason);
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

    [Fact]
    public async Task StreamEvents_WhenRedisReplayMissesFallsBackToSqliteRuntimeEvents()
    {
        await using var db = CreateDb();
        var currentUser = CurrentUser("user-1");
        var chat = new Mock<IChatHistoryRepository>();
        var sessions = new AgentSessionManager(db, currentUser.Object, chat.Object);
        var runtimeEvents = new AgentRuntimeEventService(db);
        var first = await runtimeEvents.AppendAsync(new CreateAgentRuntimeEventRequest(
            RuntimeRunId: "run-1",
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            Type: "production_progress",
            Message: "第一步"));
        var second = await runtimeEvents.AppendAsync(new CreateAgentRuntimeEventRequest(
            RuntimeRunId: "run-1",
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            Type: "production_progress",
            Message: "第二步"));
        var fanout = new Mock<IAgentRuntimeEventFanout>();
        fanout.Setup(x => x.ReplayAsync("user-1", "session-1", first.Id, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AgentSseEvent>());
        var sessionService = new Mock<IAgentSessionApplicationService>();
        sessionService
            .Setup(x => x.GetSessionByIdAsync("session-1", "user-1", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentSessionResponse { SessionId = "session-1" });
        sessionService
            .Setup(x => x.SubscribeEvents("user-1", "session-1", false))
            .Returns(() => sessions.SubscribeEvents("user-1", "session-1", false));
        var controller = new AgentController(
            conversations: null!,
            sessions: sessionService.Object,
            currentUserService: currentUser.Object,
            resumeService: null!,
            runtimeEventFanout: fanout.Object,
            runtimeEvents: runtimeEvents)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        await using var body = new MemoryStream();
        controller.Response.Body = body;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await controller.StreamEvents("session-1", first.Id, cts.Token);

        body.Position = 0;
        var output = await new StreamReader(body).ReadToEndAsync();
        Assert.DoesNotContain(first.Id, output);
        Assert.Contains(second.Id, output);
    }

    [Fact]
    public async Task StreamEvents_WhenSessionDoesNotBelongToUserStopsBeforeReplay()
    {
        var currentUser = CurrentUser("user-1");
        var sessionService = new Mock<IAgentSessionApplicationService>(MockBehavior.Strict);
        sessionService
            .Setup(x => x.GetSessionByIdAsync("session-owned-by-user-2", "user-1", false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Session not found"));
        var fanout = new Mock<IAgentRuntimeEventFanout>(MockBehavior.Strict);
        var controller = new AgentController(
            conversations: null!,
            sessions: sessionService.Object,
            currentUserService: currentUser.Object,
            resumeService: null!,
            runtimeEventFanout: fanout.Object,
            runtimeEvents: null)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        await using var body = new MemoryStream();
        controller.Response.Body = body;

        await controller.StreamEvents("session-owned-by-user-2", null, CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, controller.Response.StatusCode);
        fanout.VerifyNoOtherCalls();
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static Mock<ICurrentUserService> CurrentUser(string userId)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(x => x.GetUserId()).Returns(userId);
        currentUser.Setup(x => x.TryGetUserId()).Returns(userId);
        currentUser.Setup(x => x.IsAuthenticated()).Returns(true);
        currentUser.Setup(x => x.IsAdmin()).Returns(false);
        return currentUser;
    }

    private static async Task<ApiEnvelope<object>> ApplyEnvelopeAsync(IActionResult result)
    {
        var filter = new ApiEnvelopeResultFilter();
        var httpContext = new DefaultHttpContext();
        httpContext.TraceIdentifier = "test-trace";
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var executing = new ResultExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            result,
            controller: new object());

        await filter.OnResultExecutionAsync(
            executing,
            () => Task.FromResult(new ResultExecutedContext(
                actionContext,
                new List<IFilterMetadata>(),
                result,
                controller: new object())));

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        return Assert.IsType<ApiEnvelope<object>>(objectResult.Value);
    }

    private static ConversationApplicationService BuildConversationService(
        string assistantReply,
        out Mock<IConversationAgentRuntime> runtime,
        IConversationSessionBindingReader? bindings = null)
    {
        runtime = new Mock<IConversationAgentRuntime>();
        runtime.Setup(x => x.RunTurnAsync(It.IsAny<ConversationTurnContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationRuntimeResult(
                assistantReply,
                ConversationDecisionKind.DiscussOnly,
                null,
                Array.Empty<string>()));
        var bindingReader = bindings is null ? new Mock<IConversationSessionBindingReader>() : null;
        if (bindingReader is not null)
        {
            bindingReader.Setup(x => x.GetBindingAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UnboundConversationBinding());
        }
        var unitOfWork = new Mock<IAgentUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteAsync(
                It.IsAny<Func<CancellationToken, Task<ConversationTurnResult>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<ConversationTurnResult>> body, CancellationToken ct) => body(ct));
        return new ConversationApplicationService(
            runtime.Object,
            bindings ?? bindingReader!.Object,
            new Mock<IConversationStore>().Object,
            new Mock<ITransientAgentStream>().Object,
            new Mock<IAgentEventWriter>().Object,
            unitOfWork.Object,
            new Mock<IIdGenerator>().Object,
            new Mock<IContractHasher>().Object,
            new Mock<IClock>().Object);
    }
}
