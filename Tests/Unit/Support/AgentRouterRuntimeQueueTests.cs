using Microsoft.EntityFrameworkCore;
using Moq;
using System.Runtime.CompilerServices;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.AgentTools;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Support;

public class AgentRouterRuntimeQueueTests
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

        var router = new AgentRouter(sessions, ledger, currentUser.Object, runs, interrupts, queue, new StubForegroundTurnRunner(AgentForegroundTurnResult.Background()));

        var response = await router.HandleAsync("session-1", "把女主改聪明一点", CancellationToken.None);

        Assert.Equal("interrupt_queued", response.Phase);
        Assert.Equal(active.Id, response.RunId);
        Assert.Empty(queue.EnqueuedRunIds);
        Assert.Single(await db.AgentInterrupts.ToListAsync());
        Assert.Equal(1, await db.AgentRuntimeRuns.CountAsync());
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

        var router = new AgentRouter(sessions, ledger, currentUser.Object, runs, interrupts, queue, new StubForegroundTurnRunner(AgentForegroundTurnResult.Background()));

        var response = await router.HandleAsync("session-1", "现在到底在干嘛？", CancellationToken.None);

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

        var router = new AgentRouter(sessions, ledger, currentUser.Object, runs, interrupts, queue, new StubForegroundTurnRunner(AgentForegroundTurnResult.Background()));

        var response = await router.HandleAsync("session-1", "现在执行到哪了？", CancellationToken.None);

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

        var router = new AgentRouter(sessions, ledger, currentUser.Object, runs, interrupts, queue, foreground);

        var response = await router.HandleAsync("session-1", "开一本新小说", CancellationToken.None);

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

        var router = new AgentRouter(sessions, ledger, currentUser.Object, runs, interrupts, queue, foreground);

        var response = await router.HandleAsync("session-1", "你确定不认识我吗", CancellationToken.None);

        Assert.Equal("当然认识你，lyston。", response.Reply);
        Assert.Equal("idle", response.Phase);
        Assert.Empty(queue.EnqueuedRunIds);
        Assert.Empty(await db.AgentRuntimeRuns.ToListAsync());
        Assert.Equal(1, foreground.CallCount);
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

        var router = new AgentRouter(sessions, ledger, currentUser.Object, runs, interrupts, queue, foreground);

        var response = await router.HandleAsync("session-1", "开一本新小说", CancellationToken.None);

        Assert.Equal("queued", response.Phase);
        Assert.Single(queue.EnqueuedRunIds);
        Assert.Single(await db.AgentRuntimeRuns.ToListAsync());
        Assert.Equal(1, foreground.CallCount);
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

        var router = new AgentRouter(sessions, ledger, currentUser.Object, runs, interrupts, queue, foreground);

        var response = await router.HandleAsync("session-1", "我真的叫这个名字吗", CancellationToken.None);

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
}
