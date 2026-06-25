using Microsoft.EntityFrameworkCore;
using Moq;
using System.Runtime.CompilerServices;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.AgentTools;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Creative;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Support;

public class AgentTurnCoordinatorRuntimeQueueTests
{
    [Fact]
    public async Task HandleAsync_WithActiveRuntimeRun_QueuesInterruptInsteadOfStartingAnotherRun()
    {
        await using var db = CreateDb();
        var currentUser = FixedUser("user-1");
        var chat = new Mock<IChatHistoryRepository>();
        chat.Setup(x => x.GetHotWindowAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChatHistoryTurnDto>());
        var sessions = new AgentSessionManager(db, currentUser.Object, chat.Object);
        var runs = new AgentRuntimeRunService(db);
        var interrupts = new AgentInterruptService(db);
        var queue = new RecordingRuntimeQueue();
        var ledger = new AgentToolExecutionLedger(
            db,
            Mock.Of<IDistributedCacheService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentToolExecutionLedger>.Instance);

        var session = await sessions.GetOrCreateSessionAsync("session-1");
        await sessions.SaveSessionAsync(session);
        var active = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            "user-1",
            "session-1",
            null,
            "开始写小说"));

        var coordinator = new AgentTurnCoordinator(sessions, ledger, currentUser.Object, runs, interrupts, queue, new StubForegroundTurnRunner(AgentForegroundTurnResult.Background()));

        var response = await coordinator.HandleAsync("session-1", "把女主改聪明一点", CancellationToken.None);

        Assert.Equal("interrupt_queued", response.Phase);
        Assert.Equal(active.Id, response.RunId);
        Assert.Empty(queue.EnqueuedRunIds);
        Assert.Single(await db.AgentInterrupts.ToListAsync());
        Assert.Equal(1, await db.AgentRuntimeRuns.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_WithActiveRuntimeRun_UsesAgentInterruptDecisionKind()
    {
        await using var db = CreateDb();
        var currentUser = FixedUser("user-1");
        var chat = new Mock<IChatHistoryRepository>();
        chat.Setup(x => x.GetHotWindowAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChatHistoryTurnDto>());
        var sessions = new AgentSessionManager(db, currentUser.Object, chat.Object);
        var runs = new AgentRuntimeRunService(db);
        var interrupts = new AgentInterruptService(db);
        var queue = new RecordingRuntimeQueue();
        var ledger = new AgentToolExecutionLedger(
            db,
            Mock.Of<IDistributedCacheService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentToolExecutionLedger>.Instance);
        var decider = new StubInterruptDecisionService(new AgentInterruptDecision(
            Kind: "soft_requirement",
            Priority: 5,
            Reason: "用户是在补充当前章节要求",
            UserVisibleAcknowledgement: "我会把这条作为补充要求交给当前执行。"));

        var session = await sessions.GetOrCreateSessionAsync("session-1");
        await sessions.SaveSessionAsync(session);
        var active = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            "user-1",
            "session-1",
            null,
            "开始写小说"));

        var coordinator = new AgentTurnCoordinator(
            sessions,
            ledger,
            currentUser.Object,
            runs,
            interrupts,
            queue,
            new StubForegroundTurnRunner(AgentForegroundTurnResult.Background()),
            decider);

        var response = await coordinator.HandleAsync("session-1", "不要让女主现在喜欢男主", CancellationToken.None);

        var interrupt = await db.AgentInterrupts.SingleAsync();
        Assert.Equal("interrupt_queued", response.Phase);
        Assert.Equal(active.Id, response.RunId);
        Assert.Equal("soft_requirement", interrupt.Kind);
        Assert.Equal(5, interrupt.Priority);
        Assert.Contains("补充要求", response.Reply);
        Assert.Equal(1, decider.CallCount);
    }

    [Fact]
    public async Task HandleAsync_WithActiveRuntimeRun_StatusDecisionAnswersProgressWithoutQueueingInterrupt()
    {
        await using var db = CreateDb();
        var currentUser = FixedUser("user-1");
        var chat = new Mock<IChatHistoryRepository>();
        chat.Setup(x => x.GetHotWindowAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChatHistoryTurnDto>());
        var sessions = new AgentSessionManager(db, currentUser.Object, chat.Object);
        var runs = new AgentRuntimeRunService(db);
        var interrupts = new AgentInterruptService(db);
        var queue = new RecordingRuntimeQueue();
        var ledger = new AgentToolExecutionLedger(
            db,
            Mock.Of<IDistributedCacheService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentToolExecutionLedger>.Instance);
        var decider = new StubInterruptDecisionService(new AgentInterruptDecision(
            Kind: "status",
            Priority: 0,
            Reason: "用户只是在问当前执行进度",
            UserVisibleAcknowledgement: "我看一下当前进度。"));

        var session = await sessions.GetOrCreateSessionAsync("session-1");
        await sessions.SaveSessionAsync(session);
        var active = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            "user-1",
            "session-1",
            null,
            "开始写小说"));
        await runs.UpdateProgressAsync(
            active.Id,
            "draft_generation",
            "正在生成章节正文。",
            "ProduceChapter");

        var coordinator = new AgentTurnCoordinator(
            sessions,
            ledger,
            currentUser.Object,
            runs,
            interrupts,
            queue,
            new StubForegroundTurnRunner(AgentForegroundTurnResult.Background()),
            decider);

        var response = await coordinator.HandleAsync("session-1", "现在执行到哪了？", CancellationToken.None);

        Assert.Equal("status_answered", response.Phase);
        Assert.Equal(active.Id, response.RunId);
        Assert.Contains("当前状态", response.Reply);
        Assert.Empty(await db.AgentInterrupts.ToListAsync());
        Assert.Equal(1, await db.AgentRuntimeRuns.CountAsync());
        Assert.Equal(1, decider.CallCount);
    }

    [Fact]
    public async Task HandleAsync_WithActiveRuntimeRun_CancelDecisionRequestsRuntimeCancellation()
    {
        await using var db = CreateDb();
        var currentUser = FixedUser("user-1");
        var chat = new Mock<IChatHistoryRepository>();
        chat.Setup(x => x.GetHotWindowAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChatHistoryTurnDto>());
        var sessions = new AgentSessionManager(db, currentUser.Object, chat.Object);
        var runs = new AgentRuntimeRunService(db);
        var interrupts = new AgentInterruptService(db, runtimeRuns: runs);
        var queue = new RecordingRuntimeQueue();
        var ledger = new AgentToolExecutionLedger(
            db,
            Mock.Of<IDistributedCacheService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentToolExecutionLedger>.Instance);
        var decider = new StubInterruptDecisionService(new AgentInterruptDecision(
            Kind: "cancel",
            Priority: 100,
            Reason: "用户要求停止当前执行",
            UserVisibleAcknowledgement: "我已请求暂停/取消当前后台执行。"));

        var session = await sessions.GetOrCreateSessionAsync("session-1");
        await sessions.SaveSessionAsync(session);
        var active = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            "user-1",
            "session-1",
            null,
            "开始写小说"));

        var coordinator = new AgentTurnCoordinator(
            sessions,
            ledger,
            currentUser.Object,
            runs,
            interrupts,
            queue,
            new StubForegroundTurnRunner(AgentForegroundTurnResult.Background()),
            decider);

        var response = await coordinator.HandleAsync("session-1", "先停一下，我要改方向", CancellationToken.None);

        var interrupt = await db.AgentInterrupts.SingleAsync();
        var reloadedRun = await db.AgentRuntimeRuns.SingleAsync(x => x.Id == active.Id);
        Assert.Equal("cancel", interrupt.Kind);
        Assert.Equal(100, interrupt.Priority);
        Assert.True(reloadedRun.CancelRequested);
        Assert.Contains("暂停/取消", response.Reply);
    }

    [Fact]
    public async Task HandleAsync_WithActiveRuntimeRun_DirectionChangeDecisionQueuesWithoutCancellingRuntimeRun()
    {
        await using var db = CreateDb();
        var currentUser = FixedUser("user-1");
        var chat = new Mock<IChatHistoryRepository>();
        chat.Setup(x => x.GetHotWindowAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChatHistoryTurnDto>());
        var sessions = new AgentSessionManager(db, currentUser.Object, chat.Object);
        var runs = new AgentRuntimeRunService(db);
        var interrupts = new AgentInterruptService(db, runtimeRuns: runs);
        var queue = new RecordingRuntimeQueue();
        var ledger = new AgentToolExecutionLedger(
            db,
            Mock.Of<IDistributedCacheService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentToolExecutionLedger>.Instance);
        var decider = new StubInterruptDecisionService(new AgentInterruptDecision(
            Kind: "direction_change",
            Priority: 90,
            Reason: "用户要求改变当前任务方向",
            UserVisibleAcknowledgement: "我收到你的改方向要求了，会让当前执行停在安全边界后重新决策。"));

        var session = await sessions.GetOrCreateSessionAsync("session-1");
        await sessions.SaveSessionAsync(session);
        var active = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            "user-1",
            "session-1",
            null,
            "开始写小说"));

        var coordinator = new AgentTurnCoordinator(
            sessions,
            ledger,
            currentUser.Object,
            runs,
            interrupts,
            queue,
            new StubForegroundTurnRunner(AgentForegroundTurnResult.Background()),
            decider);

        var response = await coordinator.HandleAsync("session-1", "方向改一下，别写恋爱，改成打怪升级", CancellationToken.None);

        var interrupt = await db.AgentInterrupts.SingleAsync();
        var reloadedRun = await db.AgentRuntimeRuns.SingleAsync(x => x.Id == active.Id);
        Assert.Equal("direction_change", interrupt.Kind);
        Assert.Equal(90, interrupt.Priority);
        Assert.False(reloadedRun.CancelRequested);
        Assert.Contains("改方向", response.Reply);
    }

    [Fact]
    public async Task HandleAsync_WithProjectDirectionChange_CreatesCreativeIntentCandidate()
    {
        await using var db = CreateDb();
        SeedProject(db, "user-1", "project-1");
        var currentUser = FixedUser("user-1");
        var chat = new Mock<IChatHistoryRepository>();
        chat.Setup(x => x.GetHotWindowAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChatHistoryTurnDto>());
        var sessions = new AgentSessionManager(db, currentUser.Object, chat.Object);
        var runs = new AgentRuntimeRunService(db);
        var interrupts = new AgentInterruptService(db, runtimeRuns: runs);
        var queue = new RecordingRuntimeQueue();
        var ledger = new AgentToolExecutionLedger(
            db,
            Mock.Of<IDistributedCacheService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentToolExecutionLedger>.Instance);
        var decider = new StubInterruptDecisionService(new AgentInterruptDecision(
            Kind: "direction_change",
            Priority: 90,
            Reason: "用户要求改变当前任务方向",
            UserVisibleAcknowledgement: "我收到你的改方向要求了，会让当前执行停在安全边界后重新决策。"));

        var session = await sessions.GetOrCreateSessionAsync("session-1");
        session.ActiveProjectId = "project-1";
        await sessions.SaveSessionAsync(session);
        var active = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            "user-1",
            "session-1",
            "project-1",
            "开始写小说"));

        var coordinator = new AgentTurnCoordinator(
            sessions,
            ledger,
            currentUser.Object,
            runs,
            interrupts,
            queue,
            new StubForegroundTurnRunner(AgentForegroundTurnResult.Background()),
            decider,
            new CreativeIntentService(db));

        await coordinator.HandleAsync("session-1", "方向改一下，别写恋爱，改成打怪升级", CancellationToken.None);
        await coordinator.HandleAsync("session-1", "方向改一下，别写恋爱，改成打怪升级", CancellationToken.None);

        var intent = await db.CreativeIntents.SingleAsync();
        Assert.Equal("candidate", intent.Status);
        Assert.Equal("chat", intent.Source);
        Assert.Equal("chapter_rewrite", intent.ImpactLevel);
        Assert.Equal("project-1", intent.ProjectId);
        Assert.Equal("session-1", intent.SessionId);
        Assert.Equal(active.Id, intent.RuntimeRunId);
        Assert.Contains("打怪升级", intent.RawContent);
        Assert.Contains("direction_change", intent.MetadataJson);
    }

    [Fact]
    public async Task HandleAsync_WithActiveRuntimeRun_DoesNotExposeInternalPhaseOrToolNamesInInterruptReply()
    {
        await using var db = CreateDb();
        var currentUser = FixedUser("user-1");
        var chat = new Mock<IChatHistoryRepository>();
        chat.Setup(x => x.GetHotWindowAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChatHistoryTurnDto>());
        var sessions = new AgentSessionManager(db, currentUser.Object, chat.Object);
        var runs = new AgentRuntimeRunService(db);
        var interrupts = new AgentInterruptService(db);
        var queue = new RecordingRuntimeQueue();
        var ledger = new AgentToolExecutionLedger(
            db,
            Mock.Of<IDistributedCacheService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentToolExecutionLedger>.Instance);

        var session = await sessions.GetOrCreateSessionAsync("session-1");
        await sessions.SaveSessionAsync(session);
        var active = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            "user-1",
            "session-1",
            null,
            "开始写小说"));
        await runs.UpdateProgressAsync(
            active.Id,
            "foundation_candidates",
            "PlanStoryFoundation completed: foundation_candidates 已完成。",
            "PlanStoryFoundation");

        var coordinator = new AgentTurnCoordinator(sessions, ledger, currentUser.Object, runs, interrupts, queue, new StubForegroundTurnRunner(AgentForegroundTurnResult.Background()));

        var response = await coordinator.HandleAsync("session-1", "现在到底在干嘛？", CancellationToken.None);

        Assert.Equal("interrupt_queued", response.Phase);
        Assert.DoesNotContain("foundation_candidates", response.Reply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PlanStoryFoundation", response.Reply, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("故事地基", response.Reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_WithActiveRuntimeRun_DoesNotReturnMemoryDebugPayload()
    {
        await using var db = CreateDb();
        var currentUser = FixedUser("user-1");
        var chat = new Mock<IChatHistoryRepository>();
        chat.Setup(x => x.GetHotWindowAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChatHistoryTurnDto>());
        var sessions = new AgentSessionManager(db, currentUser.Object, chat.Object);
        var runs = new AgentRuntimeRunService(db);
        var interrupts = new AgentInterruptService(db);
        var queue = new RecordingRuntimeQueue();
        var ledger = new AgentToolExecutionLedger(
            db,
            Mock.Of<IDistributedCacheService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentToolExecutionLedger>.Instance);

        var session = await sessions.GetOrCreateSessionAsync("session-1");
        await sessions.SaveSessionAsync(session);
        await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            "user-1",
            "session-1",
            null,
            "开始写小说"));

        var coordinator = new AgentTurnCoordinator(sessions, ledger, currentUser.Object, runs, interrupts, queue, new StubForegroundTurnRunner(AgentForegroundTurnResult.Background()));

        var response = await coordinator.HandleAsync("session-1", "现在执行到哪了？", CancellationToken.None);

        Assert.Equal("interrupt_queued", response.Phase);
        Assert.Null(response.Memory);
        Assert.Null(response.RuntimeTrace);
        Assert.Null(response.Decision);
        Assert.Null(response.Rag);
    }

    [Fact]
    public async Task HandleAsync_WhenForegroundTurnRequestsBackground_DoesNotReturnMemoryDebugPayload()
    {
        await using var db = CreateDb();
        var currentUser = FixedUser("user-1");
        var chat = new Mock<IChatHistoryRepository>();
        chat.Setup(x => x.GetHotWindowAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChatHistoryTurnDto>());
        var sessions = new AgentSessionManager(db, currentUser.Object, chat.Object);
        var runs = new AgentRuntimeRunService(db);
        var interrupts = new AgentInterruptService(db);
        var queue = new RecordingRuntimeQueue();
        var ledger = new AgentToolExecutionLedger(
            db,
            Mock.Of<IDistributedCacheService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentToolExecutionLedger>.Instance);
        var foreground = new StubForegroundTurnRunner(AgentForegroundTurnResult.Background());

        var session = await sessions.GetOrCreateSessionAsync("session-1");
        await sessions.SaveSessionAsync(session);

        var coordinator = new AgentTurnCoordinator(sessions, ledger, currentUser.Object, runs, interrupts, queue, foreground);

        var response = await coordinator.HandleAsync("session-1", "开一本新小说", CancellationToken.None);

        Assert.Equal("queued", response.Phase);
        Assert.Null(response.Memory);
        Assert.Null(response.RuntimeTrace);
        Assert.Null(response.Decision);
        Assert.Null(response.Rag);
    }

    [Fact]
    public async Task HandleAsync_WhenForegroundTurnAnswers_DoesNotCreateBackgroundRun()
    {
        await using var db = CreateDb();
        var currentUser = FixedUser("user-1");
        var chat = new Mock<IChatHistoryRepository>();
        chat.Setup(x => x.GetHotWindowAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChatHistoryTurnDto>());
        var sessions = new AgentSessionManager(db, currentUser.Object, chat.Object);
        var runs = new AgentRuntimeRunService(db);
        var interrupts = new AgentInterruptService(db);
        var queue = new RecordingRuntimeQueue();
        var ledger = new AgentToolExecutionLedger(
            db,
            Mock.Of<IDistributedCacheService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentToolExecutionLedger>.Instance);
        var foreground = new StubForegroundTurnRunner(AgentForegroundTurnResult.Reply(new AgentChatResponse(
            "当然认识你，lyston。",
            Array.Empty<string>(),
            "session-1",
            null,
            "idle")));

        var session = await sessions.GetOrCreateSessionAsync("session-1");
        await sessions.SaveSessionAsync(session);

        var coordinator = new AgentTurnCoordinator(sessions, ledger, currentUser.Object, runs, interrupts, queue, foreground);

        var response = await coordinator.HandleAsync("session-1", "你确定不认识我吗", CancellationToken.None);

        Assert.Equal("当然认识你，lyston。", response.Reply);
        Assert.Equal("idle", response.Phase);
        Assert.Empty(queue.EnqueuedRunIds);
        Assert.Empty(await db.AgentRuntimeRuns.ToListAsync());
        Assert.Equal(1, foreground.CallCount);
    }

    [Fact]
    public async Task HandleAsync_WhenForegroundReportsLlmAuthFailure_ReturnsReadinessGateMessage()
    {
        await using var db = CreateDb();
        var currentUser = FixedUser("user-1");
        var chat = new Mock<IChatHistoryRepository>();
        chat.Setup(x => x.GetHotWindowAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChatHistoryTurnDto>());
        var sessions = new AgentSessionManager(db, currentUser.Object, chat.Object);
        var runs = new AgentRuntimeRunService(db);
        var interrupts = new AgentInterruptService(db);
        var queue = new RecordingRuntimeQueue();
        var ledger = new AgentToolExecutionLedger(
            db,
            Mock.Of<IDistributedCacheService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentToolExecutionLedger>.Instance);
        var foreground = new StubForegroundTurnRunner(AgentForegroundTurnResult.Reply(new AgentChatResponse(
            "API 认证失败，请在用户设置中检查 API Key。",
            Array.Empty<string>(),
            "session-1",
            null,
            "idle",
            MemoryAudit: new AgentMemoryAuditSummary(
                new[]
                {
                    new AgentMemoryReadAuditSummary(
                        "read-1",
                        "",
                        "session-1",
                        "",
                        "author",
                        new[] { "author.display_name" },
                        "read",
                        "agent",
                        DateTime.UtcNow)
                },
                Array.Empty<AgentMemoryPromotionAuditSummary>()))));
        var readinessGate = new StubBackgroundRunReadinessGate(AgentBackgroundRunReadiness.Blocked(
            "llm_not_ready",
            "失败阶段：authentication\n下一步：请在用户设置中检查 API Key。",
            new[] { "打开用户设置", "重新检测模型" }));

        var session = await sessions.GetOrCreateSessionAsync("session-1");
        await sessions.SaveSessionAsync(session);

        var coordinator = new AgentTurnCoordinator(
            sessions,
            ledger,
            currentUser.Object,
            runs,
            interrupts,
            queue,
            foreground,
            backgroundReadinessGate: readinessGate);

        var response = await coordinator.HandleAsync("session-1", "帮我写一本小说", CancellationToken.None);

        Assert.Equal("llm_not_ready", response.Phase);
        Assert.Contains("authentication", response.Reply);
        Assert.Null(response.MemoryAudit);
        Assert.Empty(queue.EnqueuedRunIds);
        Assert.Empty(await db.AgentRuntimeRuns.ToListAsync());
        Assert.Equal(1, readinessGate.CallCount);
    }

    [Fact]
    public async Task HandleAsync_WhenForegroundAuthFailureWasPersisted_ReplacesPersistedAssistantTurnWithReadinessMessage()
    {
        await using var db = CreateDb();
        var currentUser = FixedUser("user-1");
        var chat = new ChatHistoryRepository(
            db,
            new NoopDistributedCacheService(),
            new NoopMemoryCacheService(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ChatHistoryRepository>.Instance);
        var sessions = new AgentSessionManager(db, currentUser.Object, chat);
        var runs = new AgentRuntimeRunService(db);
        var interrupts = new AgentInterruptService(db);
        var queue = new RecordingRuntimeQueue();
        var ledger = new AgentToolExecutionLedger(
            db,
            Mock.Of<IDistributedCacheService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentToolExecutionLedger>.Instance);
        var foreground = new PersistingAuthFailureForeground(chat, currentUser.Object);
        var readinessGate = new StubBackgroundRunReadinessGate(AgentBackgroundRunReadiness.Blocked(
            "llm_not_ready",
            "我现在不能开始后台写作任务，因为模型连接未就绪。\n失败阶段：authentication\n下一步：请在用户设置中检查 API Key。",
            new[] { "打开用户设置", "重新检测模型" }));

        var session = await sessions.GetOrCreateSessionAsync("session-1");
        await sessions.SaveSessionAsync(session);

        var coordinator = new AgentTurnCoordinator(
            sessions,
            ledger,
            currentUser.Object,
            runs,
            interrupts,
            queue,
            foreground,
            backgroundReadinessGate: readinessGate);

        var response = await coordinator.HandleAsync("session-1", "帮我写一本小说", CancellationToken.None);

        var assistantTurn = await db.AgentChatTurns.SingleAsync(t => t.SessionId == "session-1" && t.Role == "assistant");
        Assert.Equal(response.Reply, assistantTurn.Content);
        Assert.Contains("模型连接未就绪", assistantTurn.Content);
        Assert.DoesNotContain("API 认证失败，请在用户设置中检查 API Key。", assistantTurn.Content);
        Assert.Empty(queue.EnqueuedRunIds);
        Assert.Empty(await db.AgentRuntimeRuns.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_WhenForegroundTurnRequestsBackground_QueuesBackgroundRun()
    {
        await using var db = CreateDb();
        var currentUser = FixedUser("user-1");
        var chat = new Mock<IChatHistoryRepository>();
        chat.Setup(x => x.GetHotWindowAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChatHistoryTurnDto>());
        var sessions = new AgentSessionManager(db, currentUser.Object, chat.Object);
        var runs = new AgentRuntimeRunService(db);
        var interrupts = new AgentInterruptService(db);
        var queue = new RecordingRuntimeQueue();
        var ledger = new AgentToolExecutionLedger(
            db,
            Mock.Of<IDistributedCacheService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentToolExecutionLedger>.Instance);
        var foreground = new StubForegroundTurnRunner(AgentForegroundTurnResult.Background());

        var session = await sessions.GetOrCreateSessionAsync("session-1");
        await sessions.SaveSessionAsync(session);

        var coordinator = new AgentTurnCoordinator(sessions, ledger, currentUser.Object, runs, interrupts, queue, foreground);

        var response = await coordinator.HandleAsync("session-1", "开一本新小说", CancellationToken.None);

        Assert.Equal("queued", response.Phase);
        Assert.Single(queue.EnqueuedRunIds);
        Assert.Single(await db.AgentRuntimeRuns.ToListAsync());
        Assert.Equal(1, foreground.CallCount);
    }

    [Fact]
    public async Task HandleAsync_WhenBackgroundReadinessGateBlocks_DoesNotCreateBackgroundRun()
    {
        await using var db = CreateDb();
        var currentUser = FixedUser("user-1");
        var chat = new Mock<IChatHistoryRepository>();
        chat.Setup(x => x.GetHotWindowAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChatHistoryTurnDto>());
        var sessions = new AgentSessionManager(db, currentUser.Object, chat.Object);
        var runs = new AgentRuntimeRunService(db);
        var interrupts = new AgentInterruptService(db);
        var queue = new RecordingRuntimeQueue();
        var ledger = new AgentToolExecutionLedger(
            db,
            Mock.Of<IDistributedCacheService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentToolExecutionLedger>.Instance);
        var foreground = new StubForegroundTurnRunner(AgentForegroundTurnResult.Background());
        var readinessGate = new StubBackgroundRunReadinessGate(AgentBackgroundRunReadiness.Blocked(
            "llm_not_ready",
            "模型接口认证失败，请先检查 API Key。",
            new[] { "打开用户设置", "重新检测模型" }));

        var session = await sessions.GetOrCreateSessionAsync("session-1");
        await sessions.SaveSessionAsync(session);

        var coordinator = new AgentTurnCoordinator(
            sessions,
            ledger,
            currentUser.Object,
            runs,
            interrupts,
            queue,
            foreground,
            backgroundReadinessGate: readinessGate);

        var response = await coordinator.HandleAsync("session-1", "开一本新小说", CancellationToken.None);

        Assert.Equal("llm_not_ready", response.Phase);
        Assert.Contains("认证失败", response.Reply);
        Assert.Empty(queue.EnqueuedRunIds);
        Assert.Empty(await db.AgentRuntimeRuns.ToListAsync());
        Assert.Equal(1, foreground.CallCount);
        Assert.Equal(1, readinessGate.CallCount);
    }

    [Fact]
    public async Task HandleAsync_WhenForegroundTurnReturnsConfirmationReply_DoesNotQueueBackgroundRun()
    {
        await using var db = CreateDb();
        var currentUser = FixedUser("user-1");
        var chat = new Mock<IChatHistoryRepository>();
        chat.Setup(x => x.GetHotWindowAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChatHistoryTurnDto>());
        var sessions = new AgentSessionManager(db, currentUser.Object, chat.Object);
        var runs = new AgentRuntimeRunService(db);
        var interrupts = new AgentInterruptService(db);
        var queue = new RecordingRuntimeQueue();
        var ledger = new AgentToolExecutionLedger(
            db,
            Mock.Of<IDistributedCacheService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentToolExecutionLedger>.Instance);
        var foreground = new StubForegroundTurnRunner(AgentForegroundTurnResult.Reply(new AgentChatResponse(
            "修订已提交章节并覆盖书城正文。如果确认这样推进，请回复“确认”；如果要放弃这次操作，请回复“取消”。",
            Array.Empty<string>(),
            "session-1",
            null,
            "awaiting_confirmation")));

        var session = await sessions.GetOrCreateSessionAsync("session-1");
        await sessions.SaveSessionAsync(session);

        var coordinator = new AgentTurnCoordinator(sessions, ledger, currentUser.Object, runs, interrupts, queue, foreground);

        var response = await coordinator.HandleAsync("session-1", "修订并覆盖第6章", CancellationToken.None);

        Assert.Equal("awaiting_confirmation", response.Phase);
        Assert.Contains("确认", response.Reply);
        Assert.Contains("取消", response.Reply);
        Assert.Empty(queue.EnqueuedRunIds);
        Assert.Empty(await db.AgentRuntimeRuns.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_WhenForegroundTurnDoesNotRequestBackground_DoesNotQueueBackgroundRun()
    {
        await using var db = CreateDb();
        var currentUser = FixedUser("user-1");
        var chat = new Mock<IChatHistoryRepository>();
        chat.Setup(x => x.GetHotWindowAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChatHistoryTurnDto>());
        var sessions = new AgentSessionManager(db, currentUser.Object, chat.Object);
        var runs = new AgentRuntimeRunService(db);
        var interrupts = new AgentInterruptService(db);
        var queue = new RecordingRuntimeQueue();
        var ledger = new AgentToolExecutionLedger(
            db,
            Mock.Of<IDistributedCacheService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentToolExecutionLedger>.Instance);
        var foreground = new StubForegroundTurnRunner(AgentForegroundTurnResult.NoBackground(new AgentChatResponse(
            "这一轮没有启动后台任务。",
            Array.Empty<string>(),
            "session-1",
            null,
            "idle")));

        var session = await sessions.GetOrCreateSessionAsync("session-1");
        await sessions.SaveSessionAsync(session);

        var coordinator = new AgentTurnCoordinator(sessions, ledger, currentUser.Object, runs, interrupts, queue, foreground);

        var response = await coordinator.HandleAsync("session-1", "我真的叫这个名字吗", CancellationToken.None);

        Assert.Equal("idle", response.Phase);
        Assert.Empty(queue.EnqueuedRunIds);
        Assert.Empty(await db.AgentRuntimeRuns.ToListAsync());
        Assert.Equal(1, foreground.CallCount);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static Mock<ICurrentUserService> FixedUser(string userId)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(x => x.GetUserId()).Returns(userId);
        currentUser.Setup(x => x.TryGetUserId()).Returns(userId);
        currentUser.Setup(x => x.IsAuthenticated()).Returns(true);
        return currentUser;
    }

    private static void SeedProject(NovelAgentDbContext db, string userId, string projectId)
    {
        db.Users.Add(new TM.Web.NovelAgentWeb.Data.Entities.User
        {
            Id = userId,
            Username = userId,
            Email = $"{userId}@example.test",
            PasswordHash = "hash",
            Role = "author",
            CreatedAt = DateTime.UtcNow
        });
        db.NovelProjects.Add(new TM.Web.NovelAgentWeb.Data.Entities.NovelProject
        {
            Id = projectId,
            UserId = userId,
            Title = "测试小说",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    private sealed class RecordingRuntimeQueue : IAgentRuntimeQueue
    {
        public List<string> EnqueuedRunIds { get; } = new();

        public ValueTask EnqueueAsync(string runtimeRunId, CancellationToken ct = default)
        {
            EnqueuedRunIds.Add(runtimeRunId);
            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<string> DequeueAllAsync([EnumeratorCancellation] CancellationToken ct = default)
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

        public int CallCount { get; private set; }

        public Task<AgentForegroundTurnResult> TryHandleAsync(string sessionId, string userMessage, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class PersistingAuthFailureForeground : IAgentForegroundTurnRunner
    {
        private const string AuthFailureReply = "API 认证失败，请在用户设置中检查 API Key。";
        private readonly IChatHistoryRepository _chat;
        private readonly ICurrentUserService _currentUser;

        public PersistingAuthFailureForeground(IChatHistoryRepository chat, ICurrentUserService currentUser)
        {
            _chat = chat;
            _currentUser = currentUser;
        }

        public async Task<AgentForegroundTurnResult> TryHandleAsync(string sessionId, string userMessage, CancellationToken ct)
        {
            await _chat.AppendAsync(_currentUser.GetUserId(), null, sessionId, "assistant", AuthFailureReply, ct);
            return AgentForegroundTurnResult.Reply(new AgentChatResponse(
                AuthFailureReply,
                Array.Empty<string>(),
                sessionId,
                null,
                "idle"));
        }
    }

    private sealed class StubInterruptDecisionService : IAgentInterruptDecisionService
    {
        private readonly AgentInterruptDecision _decision;

        public StubInterruptDecisionService(AgentInterruptDecision decision)
        {
            _decision = decision;
        }

        public int CallCount { get; private set; }

        public Task<AgentInterruptDecision> DecideAsync(
            AgentSession session,
            TM.Web.NovelAgentWeb.Data.Entities.AgentRuntimeRun activeRun,
            string userMessage,
            CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(_decision);
        }
    }

    private sealed class StubBackgroundRunReadinessGate : IAgentBackgroundRunReadinessGate
    {
        private readonly AgentBackgroundRunReadiness _readiness;

        public StubBackgroundRunReadinessGate(AgentBackgroundRunReadiness readiness)
        {
            _readiness = readiness;
        }

        public int CallCount { get; private set; }

        public Task<AgentBackgroundRunReadiness> CheckAsync(
            string sessionId,
            string userMessage,
            CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(_readiness);
        }
    }

    private sealed class NoopDistributedCacheService : IDistributedCacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class => Task.FromResult<T?>(null);
        public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken ct = default) where T : class => Task.CompletedTask;
        public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveByPrefixAsync(string keyPrefix, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(false);
    }

    private sealed class NoopMemoryCacheService : IMemoryCacheService
    {
        public Task<T?> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan expiration, CancellationToken cancellationToken = default) =>
            factory().ContinueWith(task => (T?)task.Result, cancellationToken);

        public T? Get<T>(string key) => default;
        public void Set<T>(string key, T value, TimeSpan expiration) { }
        public void Remove(string key) { }
        public void RemoveByPrefix(string keyPrefix) { }
    }
}
