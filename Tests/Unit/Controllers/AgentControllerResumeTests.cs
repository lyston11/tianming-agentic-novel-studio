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
using TM.Web.NovelAgentWeb.Services.AgentTools;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Controllers;

public class AgentControllerResumeTests
{
    [Fact]
    public async Task Chat_ReturnsUnifiedEnvelopeForAgentReply()
    {
        await using var db = CreateDb();
        var currentUser = CurrentUser("user-1");
        var chat = new Mock<IChatHistoryRepository>();
        chat.Setup(x => x.GetHotWindowAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChatHistoryTurnDto>());
        var sessions = new AgentSessionManager(db, currentUser.Object, chat.Object);
        var coordinator = new AgentTurnCoordinator(
            sessions,
            new AgentToolExecutionLedger(
                db,
                Mock.Of<IDistributedCacheService>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentToolExecutionLedger>.Instance),
            currentUser.Object,
            new AgentRuntimeRunService(db),
            new AgentInterruptService(db),
            new RecordingRuntimeQueue(),
            new StubForegroundTurnRunner(AgentForegroundTurnResult.Reply(new AgentChatResponse(
                "当然认识你，lyston。",
                Array.Empty<string>(),
                "session-1",
                null,
                "idle"))));

        var controller = new AgentController(
            coordinator,
            sessionManager: null!,
            agentSessionService: null!,
            currentUserService: currentUser.Object,
            resumeService: null!);

        var result = await controller.Chat(new AgentChatRequest("你知道我是谁吗", "session-1"), CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        Assert.True(envelope.Success);
        Assert.Equal("v1", envelope.ApiVersion);
        var data = Assert.IsType<AgentChatResponse>(envelope.Data);
        Assert.Equal("当然认识你，lyston。", data.Reply);
        Assert.Equal("session-1", data.SessionId);
    }

    [Fact]
    public async Task Chat_ReturnsSafeMemoryAuditWithoutFullMemoryInEnvelope()
    {
        await using var db = CreateDb();
        var currentUser = CurrentUser("user-1");
        var chat = new Mock<IChatHistoryRepository>();
        chat.Setup(x => x.GetHotWindowAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChatHistoryTurnDto>());
        var sessions = new AgentSessionManager(db, currentUser.Object, chat.Object);
        var coordinator = new AgentTurnCoordinator(
            sessions,
            new AgentToolExecutionLedger(
                db,
                Mock.Of<IDistributedCacheService>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentToolExecutionLedger>.Instance),
            currentUser.Object,
            new AgentRuntimeRunService(db),
            new AgentInterruptService(db),
            new RecordingRuntimeQueue(),
            new StubForegroundTurnRunner(AgentForegroundTurnResult.Reply(new AgentChatResponse(
                "我读取了你的作者偏好。",
                Array.Empty<string>(),
                "session-1",
                "run-1",
                "completed",
                Memory: new AgentWorkingMemorySnapshot
                {
                    ProjectMemory = new AgentProjectMemory
                    {
                        LongTermGoal = "不应公开的项目记忆正文"
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
                            new[] { "display_name" },
                            "memory_repository",
                            "AgentObservationBuilder",
                            DateTime.UtcNow)
                    },
                    Array.Empty<AgentMemoryPromotionAuditSummary>())))));

        var controller = new AgentController(
            coordinator,
            sessionManager: null!,
            agentSessionService: null!,
            currentUserService: currentUser.Object,
            resumeService: null!);

        var result = await controller.Chat(new AgentChatRequest("你记得我吗", "session-1"), CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        var data = Assert.IsType<AgentChatResponse>(envelope.Data);
        Assert.Null(data.Memory);
        var read = Assert.Single(data.MemoryAudit!.Reads);
        Assert.Equal("author", read.MemoryScope);
        Assert.Equal(new[] { "display_name" }, read.MemoryKeys);

        var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        Assert.Contains("memoryAudit", json);
        Assert.DoesNotContain("不应公开的项目记忆正文", json);
    }

    [Fact]
    public async Task Chat_WhenStartingBackgroundRun_StoresIdempotencyKeyAndClientMessageIdSeparately()
    {
        await using var db = CreateDb();
        var currentUser = CurrentUser("user-1");
        var chat = new Mock<IChatHistoryRepository>();
        chat.Setup(x => x.GetHotWindowAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChatHistoryTurnDto>());
        var sessions = new AgentSessionManager(db, currentUser.Object, chat.Object);
        var coordinator = new AgentTurnCoordinator(
            sessions,
            new AgentToolExecutionLedger(
                db,
                Mock.Of<IDistributedCacheService>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentToolExecutionLedger>.Instance),
            currentUser.Object,
            new AgentRuntimeRunService(db),
            new AgentInterruptService(db),
            new RecordingRuntimeQueue(),
            new StubForegroundTurnRunner(AgentForegroundTurnResult.Background()));
        var controller = new AgentController(
            coordinator,
            sessionManager: null!,
            agentSessionService: null!,
            currentUserService: currentUser.Object,
            resumeService: null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.Request.Headers["Idempotency-Key"] = "message-42";

        var result = await controller.Chat(
            new AgentChatRequest("开一本新小说", "session-1", "user-message-42"),
            CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        var data = Assert.IsType<AgentChatResponse>(envelope.Data);
        Assert.Equal("queued", data.Phase);
        var run = Assert.Single(await db.AgentRuntimeRuns.ToListAsync());
        Assert.Equal("message-42", run.IdempotencyKey);
        Assert.Equal("user-message-42", run.SourceMessageId);
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
            coordinator: null!,
            sessionManager: null!,
            agentSessionService: null!,
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
        var agentSessions = new Mock<IAgentSessionService>();
        agentSessions
            .Setup(x => x.GetOrCreateSessionAsync(
                null,
                "user-1",
                "project-1",
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string?, string, string?, string?, CancellationToken>((_, _, _, key, _) => capturedIdempotencyKey = key)
            .ReturnsAsync(new AgentSessionResponse
            {
                SessionId = "session-1",
                Title = "新会话",
                ActiveProjectId = "project-1",
                Phase = "idle",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        var controller = new AgentController(
            coordinator: null!,
            sessionManager: null!,
            agentSessionService: agentSessions.Object,
            currentUserService: CurrentUser("user-1").Object,
            resumeService: null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.Request.Headers["Idempotency-Key"] = "session-create-key-001";

        var result = await controller.CreateSession("project-1", CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        Assert.True(envelope.Success);
        Assert.Equal("session-create-key-001", capturedIdempotencyKey);
    }

    [Fact]
    public async Task ResumeSession_ReturnsNotFoundForMissingSession()
    {
        var resume = new Mock<IAgentSessionResumeService>();
        resume
            .Setup(x => x.ResumeAsync("missing-session", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());

        var controller = new AgentController(
            coordinator: null!,
            sessionManager: null!,
            agentSessionService: null!,
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
        fanout.Setup(x => x.ReplayAsync("session-1", first.Id, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AgentSseEvent>());
        var controller = new AgentController(
            coordinator: null!,
            sessionManager: sessions,
            agentSessionService: null!,
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

        await controller.StreamEvents("session-1", null, first.Id, cts.Token);

        body.Position = 0;
        var output = await new StreamReader(body).ReadToEndAsync();
        Assert.DoesNotContain(first.Id, output);
        Assert.Contains(second.Id, output);
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

    private sealed class RecordingRuntimeQueue : IAgentRuntimeQueue
    {
        public ValueTask EnqueueAsync(string runtimeRunId, CancellationToken ct = default) => ValueTask.CompletedTask;

        public async IAsyncEnumerable<string> DequeueAllAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class StubForegroundTurnRunner : IAgentForegroundTurnRunner
    {
        private readonly AgentForegroundTurnResult _result;

        public StubForegroundTurnRunner(AgentForegroundTurnResult result)
        {
            _result = result;
        }

        public Task<AgentForegroundTurnResult> TryHandleAsync(string sessionId, string userMessage, CancellationToken ct) =>
            Task.FromResult(_result);
    }
}
