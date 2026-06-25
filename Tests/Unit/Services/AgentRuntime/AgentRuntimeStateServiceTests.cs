using Microsoft.EntityFrameworkCore;
using Moq;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Services.AgentRuntime;

public class AgentRuntimeStateServiceTests
{
    [Fact]
    public async Task CreateQueuedAsync_PersistsQueuedRunAndFindsActiveRun()
    {
        await using var db = CreateDb();
        var runs = new AgentRuntimeRunService(db);

        var created = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: null,
            UserMessage: "开始写一本末世小说"));

        Assert.Equal("queued", created.Status);
        Assert.Equal("session-1", created.SessionId);
        Assert.Equal("开始写一本末世小说", created.UserMessage);

        var active = await runs.TryGetActiveAsync("user-1", "session-1");

        Assert.NotNull(active);
        Assert.Equal(created.Id, active!.Id);
    }

    [Fact]
    public async Task ActiveRunLifecycle_WritesRedisSnapshotHeartbeatAndClearsOnCompletion()
    {
        await using var db = CreateDb();
        var redis = new Mock<IDistributedCacheService>();
        var runs = new AgentRuntimeRunService(db, redis.Object);

        var created = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserMessage: "写第一章",
            Mode: AgentRuntimeRunMode.Production));
        await runs.MarkRunningAsync(created.Id);
        await runs.UpdateProgressAsync(
            created.Id,
            "draft_generation",
            "正在生成正文",
            "ProduceChapter",
            2);

        redis.Verify(x => x.SetAsync(
                "agent_runtime:active:user-1:session-1",
                It.Is<AgentRuntimeRunCacheSnapshot>(snapshot =>
                    snapshot.RuntimeRunId == created.Id &&
                    snapshot.Status == AgentRuntimeRunStatus.Running &&
                    snapshot.CurrentPhase == "draft_generation" &&
                    snapshot.ActiveTool == "ProduceChapter" &&
                    snapshot.CurrentStep == 2 &&
                    snapshot.HeartbeatAt != default),
                It.Is<TimeSpan?>(ttl => ttl == TimeSpan.FromMinutes(30)),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        redis.Verify(x => x.SetAsync(
                $"agent_runtime:heartbeat:{created.Id}",
                It.Is<AgentRuntimeRunCacheSnapshot>(snapshot =>
                    snapshot.RuntimeRunId == created.Id &&
                    snapshot.CurrentPhase == "draft_generation"),
                It.Is<TimeSpan?>(ttl => ttl == TimeSpan.FromMinutes(30)),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        await runs.MarkCompletedAsync(created.Id, new { ok = true });

        redis.Verify(x => x.RemoveAsync("agent_runtime:active:user-1:session-1", It.IsAny<CancellationToken>()), Times.Once);
        redis.Verify(x => x.RemoveAsync($"agent_runtime:heartbeat:{created.Id}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TryGetActiveAsync_UsesRedisActiveSnapshotAndHydratesSqliteRun()
    {
        await using var db = CreateDb();
        var run = new TM.Web.NovelAgentWeb.Data.Entities.AgentRuntimeRun
        {
            Id = "run-active",
            UserId = "user-1",
            SessionId = "session-1",
            ProjectId = "project-1",
            Status = AgentRuntimeRunStatus.Running,
            Mode = AgentRuntimeRunMode.Production,
            CurrentPhase = "draft_generation",
            UserMessage = "写第一章",
            LastMessage = "正在生成正文",
            ResultJson = "{}",
            FailureJson = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.AgentRuntimeRuns.Add(run);
        await db.SaveChangesAsync();

        var redis = new Mock<IDistributedCacheService>();
        redis.Setup(x => x.GetAsync<AgentRuntimeRunCacheSnapshot>(
                "agent_runtime:active:user-1:session-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRuntimeRunCacheSnapshot(
                RuntimeRunId: run.Id,
                UserId: run.UserId,
                SessionId: run.SessionId,
                ProjectId: run.ProjectId,
                Status: run.Status,
                Mode: run.Mode,
                CurrentPhase: run.CurrentPhase,
                LastMessage: run.LastMessage,
                ActiveTool: run.ActiveTool,
                CurrentStep: run.CurrentStep,
                CancelRequested: run.CancelRequested,
                UpdatedAt: run.UpdatedAt,
                HeartbeatAt: DateTime.UtcNow));
        var runs = new AgentRuntimeRunService(db, redis.Object);

        var active = await runs.TryGetActiveAsync("user-1", "session-1");

        Assert.NotNull(active);
        Assert.Equal(run.Id, active!.Id);
        redis.Verify(x => x.GetAsync<AgentRuntimeRunCacheSnapshot>(
                "agent_runtime:active:user-1:session-1",
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TryGetActiveStateAsync_ReturnsRedisHeartbeatMetadataWhenSnapshotIsValid()
    {
        await using var db = CreateDb();
        var heartbeatAt = DateTime.UtcNow.AddSeconds(-3);
        var run = new TM.Web.NovelAgentWeb.Data.Entities.AgentRuntimeRun
        {
            Id = "run-active",
            UserId = "user-1",
            SessionId = "session-1",
            ProjectId = "project-1",
            Status = AgentRuntimeRunStatus.Running,
            Mode = AgentRuntimeRunMode.Production,
            CurrentPhase = "draft_generation",
            UserMessage = "写第一章",
            LastMessage = "正在生成正文",
            ResultJson = "{}",
            FailureJson = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.AgentRuntimeRuns.Add(run);
        await db.SaveChangesAsync();

        var redis = new Mock<IDistributedCacheService>();
        redis.Setup(x => x.GetAsync<AgentRuntimeRunCacheSnapshot>(
                "agent_runtime:active:user-1:session-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRuntimeRunCacheSnapshot(
                RuntimeRunId: run.Id,
                UserId: run.UserId,
                SessionId: run.SessionId,
                ProjectId: run.ProjectId,
                Status: run.Status,
                Mode: run.Mode,
                CurrentPhase: run.CurrentPhase,
                LastMessage: run.LastMessage,
                ActiveTool: run.ActiveTool,
                CurrentStep: run.CurrentStep,
                CancelRequested: run.CancelRequested,
                UpdatedAt: run.UpdatedAt,
                HeartbeatAt: heartbeatAt));
        var runs = new AgentRuntimeRunService(db, redis.Object);

        var state = await runs.TryGetActiveStateAsync("user-1", "session-1");

        Assert.NotNull(state);
        Assert.Equal(run.Id, state!.Run.Id);
        Assert.Equal(heartbeatAt, state.HeartbeatAt);
        Assert.True(state.FromDistributedCache);
    }

    [Fact]
    public async Task TryGetActiveAsync_ClearsStaleRedisSnapshotAndFallsBackToSqlite()
    {
        await using var db = CreateDb();
        var stale = await new AgentRuntimeRunService(db).CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserMessage: "旧任务"));
        await new AgentRuntimeRunService(db).MarkCompletedAsync(stale.Id, new { ok = true });
        var current = await new AgentRuntimeRunService(db).CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserMessage: "当前任务"));
        await new AgentRuntimeRunService(db).MarkRunningAsync(current.Id);

        var redis = new Mock<IDistributedCacheService>();
        redis.Setup(x => x.GetAsync<AgentRuntimeRunCacheSnapshot>(
                "agent_runtime:active:user-1:session-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRuntimeRunCacheSnapshot(
                RuntimeRunId: stale.Id,
                UserId: stale.UserId,
                SessionId: stale.SessionId,
                ProjectId: stale.ProjectId,
                Status: AgentRuntimeRunStatus.Running,
                Mode: stale.Mode,
                CurrentPhase: "running",
                LastMessage: "stale",
                ActiveTool: string.Empty,
                CurrentStep: 0,
                CancelRequested: false,
                UpdatedAt: stale.UpdatedAt,
                HeartbeatAt: DateTime.UtcNow));
        var runs = new AgentRuntimeRunService(db, redis.Object);

        var active = await runs.TryGetActiveAsync("user-1", "session-1");

        Assert.NotNull(active);
        Assert.Equal(current.Id, active!.Id);
        redis.Verify(x => x.RemoveAsync("agent_runtime:active:user-1:session-1", It.IsAny<CancellationToken>()), Times.Once);
        redis.Verify(x => x.RemoveAsync($"agent_runtime:heartbeat:{stale.Id}", It.IsAny<CancellationToken>()), Times.Once);
        redis.Verify(x => x.SetAsync(
                "agent_runtime:active:user-1:session-1",
                It.Is<AgentRuntimeRunCacheSnapshot>(snapshot => snapshot.RuntimeRunId == current.Id),
                It.Is<TimeSpan?>(ttl => ttl == TimeSpan.FromMinutes(30)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateQueuedAsync_PersistsModeBudgetAndReturnsSameRunForIdempotencyKey()
    {
        await using var db = CreateDb();
        var runs = new AgentRuntimeRunService(db);

        var request = new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserMessage: "继续写第二章",
            Mode: AgentRuntimeRunMode.Production,
            IdempotencyKey: "msg-1:produce-chapter-2",
            SourceMessageId: "msg-1",
            Budget: new AgentRuntimeBudget(
                MaxLoopIterations: 8,
                MaxToolCalls: 5,
                MaxModelCalls: 12,
                MaxDurationSeconds: 300,
                MaxRevisions: 2,
                MaxRewrites: 1));

        var created = await runs.CreateQueuedAsync(request);
        var duplicate = await runs.CreateQueuedAsync(request);

        Assert.Equal(created.Id, duplicate.Id);
        Assert.Equal(AgentRuntimeRunMode.Production, created.Mode);
        Assert.Equal("msg-1:produce-chapter-2", created.IdempotencyKey);
        Assert.Equal("msg-1", created.SourceMessageId);
        Assert.Contains("\"maxToolCalls\":5", created.BudgetJson);
    }

    [Fact]
    public async Task MarkFailedAsync_PersistsStructuredRecoverableFailure()
    {
        await using var db = CreateDb();
        var runs = new AgentRuntimeRunService(db);
        var run = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserMessage: "写第二章"));

        var failed = await runs.MarkFailedAsync(run.Id, new AgentRuntimeFailure(
            Code: "GATE_FAILED",
            Stage: "KernelGate",
            Message: "章节违反邮徽能力边界",
            Recoverable: true,
            RecommendedAction: "ReviseChapter",
            ArtifactIds: new[] { "draft-1", "gate-1" }));

        Assert.Equal("failed", failed.Status);
        Assert.Equal("章节违反邮徽能力边界", failed.ErrorMessage);
        Assert.Contains("\"code\":\"GATE_FAILED\"", failed.FailureJson);
        Assert.Contains("\"recommendedAction\":\"ReviseChapter\"", failed.FailureJson);
        Assert.Contains("draft-1", failed.FailureJson);
    }

    [Fact]
    public async Task MarkCancelledAsync_PersistsCancelledStatusWithoutFailureError()
    {
        await using var db = CreateDb();
        var runs = new AgentRuntimeRunService(db);
        var run = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserMessage: "写第一章",
            Mode: AgentRuntimeRunMode.Production));
        await runs.MarkRunningAsync(run.Id);
        await runs.RequestCancelAsync(run.Id);

        var cancelled = await runs.MarkCancelledAsync(
            run.Id,
            "draft_generation",
            "后台执行已取消。");

        Assert.Equal(AgentRuntimeRunStatus.Cancelled, cancelled.Status);
        Assert.Equal("draft_generation", cancelled.CurrentPhase);
        Assert.Equal(string.Empty, cancelled.ErrorMessage);
        Assert.Equal("后台执行已取消。", cancelled.LastMessage);
        Assert.NotNull(cancelled.CompletedAt);
        Assert.Contains("\"code\":\"RUNTIME_CANCELLED\"", cancelled.FailureJson);
        Assert.Contains("\"recoverable\":true", cancelled.FailureJson);
    }

    [Fact]
    public async Task UpdateProgressAsync_PromotesRunModeToProductionWhenProductionToolStarts()
    {
        await using var db = CreateDb();
        var runs = new AgentRuntimeRunService(db);
        var run = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserMessage: "请写第六章并提交书城"));

        Assert.Equal(AgentRuntimeRunMode.Inspect, run.Mode);

        var updated = await runs.UpdateProgressAsync(
            run.Id,
            "acting",
            "正在生产并提交章节",
            "ProduceChapter");

        Assert.Equal(AgentRuntimeRunMode.Production, updated.Mode);
        Assert.Equal("ProduceChapter", updated.ActiveTool);
    }

    [Fact]
    public async Task FailStaleActiveRunsAsync_MarksOnlyExpiredActiveRunsAsRecoverableHeartbeatLoss()
    {
        await using var db = CreateDb();
        var runs = new AgentRuntimeRunService(db);
        var stale = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserMessage: "生成第一章",
            Mode: AgentRuntimeRunMode.Production));
        await runs.MarkRunningAsync(stale.Id);
        stale.UpdatedAt = DateTime.UtcNow.AddMinutes(-20);

        var recent = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-2",
            ProjectId: "project-1",
            UserMessage: "生成第二章",
            Mode: AgentRuntimeRunMode.Production));
        await runs.MarkRunningAsync(recent.Id);

        var completed = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-3",
            ProjectId: "project-1",
            UserMessage: "查询章节",
            Mode: AgentRuntimeRunMode.Inspect));
        await runs.MarkCompletedAsync(completed.Id, new { ok = true });
        completed.UpdatedAt = DateTime.UtcNow.AddMinutes(-30);
        await db.SaveChangesAsync();

        var failedCount = await runs.FailStaleActiveRunsAsync(
            staleAfter: TimeSpan.FromMinutes(10),
            reason: "Redis heartbeat missing; recovering from database truth.");

        var reloadedStale = await db.AgentRuntimeRuns.SingleAsync(r => r.Id == stale.Id);
        var reloadedRecent = await db.AgentRuntimeRuns.SingleAsync(r => r.Id == recent.Id);
        var reloadedCompleted = await db.AgentRuntimeRuns.SingleAsync(r => r.Id == completed.Id);

        Assert.Equal(1, failedCount);
        Assert.Equal(AgentRuntimeRunStatus.Failed, reloadedStale.Status);
        Assert.Equal("heartbeat_lost", reloadedStale.CurrentPhase);
        Assert.Contains("RUNTIME_HEARTBEAT_LOST", reloadedStale.FailureJson);
        Assert.Contains("\"recoverable\":true", reloadedStale.FailureJson);
        Assert.Contains("QueryRuntimeRun", reloadedStale.FailureJson);
        Assert.Equal(AgentRuntimeRunStatus.Running, reloadedRecent.Status);
        Assert.Equal(AgentRuntimeRunStatus.Completed, reloadedCompleted.Status);
    }

    [Fact]
    public async Task AppendAsync_PersistsStageArtifactAndDisplaySurface()
    {
        await using var db = CreateDb();
        var runtimeEvents = new AgentRuntimeEventService(db);

        var evt = await runtimeEvents.AppendAsync(new CreateAgentRuntimeEventRequest(
            RuntimeRunId: "run-1",
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            Type: "production_event",
            Message: "门禁校验通过",
            Data: new { score = 91 },
            Stage: "KernelGate",
            Status: "completed",
            ArtifactType: "GateReport",
            ArtifactId: "gate-1",
            DisplaySurface: AgentRuntimeEventSurface.Workflow,
            DisplayPolicy: AgentRuntimeEventDisplayPolicy.Timeline));

        Assert.Equal("KernelGate", evt.Stage);
        Assert.Equal("completed", evt.Status);
        Assert.Equal("GateReport", evt.ArtifactType);
        Assert.Equal("gate-1", evt.ArtifactId);
        Assert.Equal(AgentRuntimeEventSurface.Workflow, evt.DisplaySurface);
        Assert.Equal(AgentRuntimeEventDisplayPolicy.Timeline, evt.DisplayPolicy);
    }

    [Fact]
    public async Task AppendAsync_WhenPublishToSseBroadcastsRuntimeEventContract()
    {
        await using var db = CreateDb();
        var bus = new AgentSseEventBus();
        var reader = bus.GetReader("session-1");
        var runtimeEvents = new AgentRuntimeEventService(db, bus);

        var saved = await runtimeEvents.AppendAsync(new CreateAgentRuntimeEventRequest(
            RuntimeRunId: "run-1",
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            Type: "production_progress",
            Message: "正在等待模型生成正文与修订记录。",
            Data: new { status = "running", stage = "draft_generation" },
            Stage: "draft_generation",
            Status: "running",
            ArtifactType: "chapter_draft",
            ArtifactId: "draft-1",
            DisplaySurface: AgentRuntimeEventSurface.Workflow,
            DisplayPolicy: AgentRuntimeEventDisplayPolicy.Timeline,
            PublishToSse: true));

        Assert.True(reader.TryRead(out var evt));
        Assert.Equal(saved.Id, evt.EventId);
        Assert.Equal("production_progress", evt.Type);
        Assert.Equal("session-1", evt.SessionId);
        Assert.Equal("run-1", evt.RunId);
        Assert.Equal("draft_generation", evt.Stage);
        Assert.Equal("running", evt.Status);
        Assert.Equal("chapter_draft", evt.ArtifactType);
        Assert.Equal("draft-1", evt.ArtifactId);
        Assert.Equal(AgentRuntimeEventSurface.Workflow, evt.DisplaySurface);
        Assert.Equal(AgentRuntimeEventDisplayPolicy.Timeline, evt.DisplayPolicy);
        Assert.Equal("正在等待模型生成正文与修订记录。", evt.Message);
    }

    [Fact]
    public async Task AppendAsync_WithSameRuntimeEventContractReusesExistingEventAndDoesNotRepublish()
    {
        await using var db = CreateDb();
        var bus = new AgentSseEventBus();
        var reader = bus.GetReader("session-1");
        var runtimeEvents = new AgentRuntimeEventService(db, bus);
        var request = new CreateAgentRuntimeEventRequest(
            RuntimeRunId: "run-duplicate-runtime",
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            Type: "production_progress",
            Message: "已执行约 2 分钟，正在等待模型生成正文与修订记录。",
            Data: new { status = "running", stage = "draft_generation" },
            Stage: "draft_generation",
            Status: "running",
            ArtifactType: "chapter_draft",
            ArtifactId: "draft-1",
            DisplaySurface: AgentRuntimeEventSurface.Workflow,
            DisplayPolicy: AgentRuntimeEventDisplayPolicy.Timeline,
            PublishToSse: true);

        var first = await runtimeEvents.AppendAsync(request);
        Assert.True(reader.TryRead(out _));

        var second = await runtimeEvents.AppendAsync(request);

        Assert.Equal(first.Id, second.Id);
        Assert.False(reader.TryRead(out _));
        Assert.Single(await db.AgentRuntimeEvents
            .Where(evt =>
                evt.RuntimeRunId == "run-duplicate-runtime" &&
                evt.Type == "production_progress" &&
                evt.Stage == "draft_generation" &&
                evt.Message == "已执行约 2 分钟，正在等待模型生成正文与修订记录。")
            .ToListAsync());
    }

    [Fact]
    public async Task AppendAsync_WhenPublishToSsePublishesRuntimeEventFanout()
    {
        await using var db = CreateDb();
        var fanout = new Mock<IAgentRuntimeEventFanout>();
        var runtimeEvents = new AgentRuntimeEventService(db, null, fanout.Object);

        await runtimeEvents.AppendAsync(new CreateAgentRuntimeEventRequest(
            RuntimeRunId: "run-1",
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            Type: "production_progress",
            Message: "正在门禁校验。",
            Data: new { status = "running", stage = "gate_validation" },
            Stage: "gate_validation",
            Status: "running",
            ArtifactType: "gate_report",
            ArtifactId: "gate-1",
            DisplaySurface: AgentRuntimeEventSurface.Workflow,
            DisplayPolicy: AgentRuntimeEventDisplayPolicy.Timeline,
            PublishToSse: true));

        fanout.Verify(x => x.PublishAsync(
                "session-1",
                It.Is<AgentSseEvent>(evt =>
                    evt.Type == "production_progress" &&
                    evt.RunId == "run-1" &&
                    evt.Stage == "gate_validation" &&
                    evt.Status == "running" &&
                    evt.ArtifactType == "gate_report" &&
                    evt.ArtifactId == "gate-1" &&
                    evt.DisplaySurface == AgentRuntimeEventSurface.Workflow &&
                    evt.DisplayPolicy == AgentRuntimeEventDisplayPolicy.Timeline &&
                    evt.Message == "正在门禁校验。"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AddAsync_PersistsPendingInterruptForActiveRun()
    {
        await using var db = CreateDb();
        var runs = new AgentRuntimeRunService(db);
        var interrupts = new AgentInterruptService(db);
        var run = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserMessage: "继续推进"));

        var interrupt = await interrupts.AddAsync(new CreateAgentInterruptRequest(
            RuntimeRunId: run.Id,
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            Kind: "freeform",
            Message: "把女主改得更聪明一点",
            Priority: 0));

        Assert.Equal("pending", interrupt.Status);
        Assert.Equal(run.Id, interrupt.RuntimeRunId);

        var pending = await interrupts.GetPendingAsync(run.Id);

        Assert.Single(pending);
        Assert.Equal("把女主改得更聪明一点", pending[0].Message);
    }

    [Fact]
    public async Task AddAsync_WritesRedisPendingInterruptSnapshot()
    {
        await using var db = CreateDb();
        var runs = new AgentRuntimeRunService(db);
        var redis = new Mock<IDistributedCacheService>();
        var interrupts = new AgentInterruptService(db, redis.Object);
        var run = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserMessage: "继续推进"));

        var interrupt = await interrupts.AddAsync(new CreateAgentInterruptRequest(
            RuntimeRunId: run.Id,
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            Kind: "soft_requirement",
            Message: "第二章增加打怪升级",
            Priority: 5));

        redis.Verify(x => x.SetAsync(
                $"agent_runtime:interrupts:{run.Id}",
                It.Is<AgentRuntimeInterruptQueueSnapshot>(snapshot =>
                    snapshot.RuntimeRunId == run.Id &&
                    snapshot.PendingCount == 1 &&
                    snapshot.Pending[0].InterruptId == interrupt.Id &&
                    snapshot.Pending[0].Kind == "soft_requirement" &&
                    snapshot.Pending[0].Message == "第二章增加打怪升级" &&
                    snapshot.Pending[0].Priority == 5),
                It.Is<TimeSpan?>(ttl => ttl == TimeSpan.FromMinutes(30)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TryGetPendingSnapshotAsync_RebuildsRedisSnapshotFromSqliteWhenCacheMisses()
    {
        await using var db = CreateDb();
        var runs = new AgentRuntimeRunService(db);
        var redis = new Mock<IDistributedCacheService>();
        redis.Setup(x => x.GetAsync<AgentRuntimeInterruptQueueSnapshot>(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRuntimeInterruptQueueSnapshot?)null);
        var interrupts = new AgentInterruptService(db, redis.Object);
        var run = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserMessage: "继续推进"));
        await interrupts.AddAsync(new CreateAgentInterruptRequest(
            RuntimeRunId: run.Id,
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            Kind: "status",
            Message: "现在执行到哪了",
            Priority: 2));

        var snapshot = await interrupts.TryGetPendingSnapshotAsync(run.Id);

        Assert.NotNull(snapshot);
        Assert.Equal(run.Id, snapshot!.RuntimeRunId);
        Assert.Equal(1, snapshot.PendingCount);
        Assert.Equal("status", snapshot.Pending[0].Kind);
        redis.Verify(x => x.GetAsync<AgentRuntimeInterruptQueueSnapshot>(
                $"agent_runtime:interrupts:{run.Id}",
                It.IsAny<CancellationToken>()),
            Times.Once);
        redis.Verify(x => x.SetAsync(
                $"agent_runtime:interrupts:{run.Id}",
                It.Is<AgentRuntimeInterruptQueueSnapshot>(rebuilt =>
                    rebuilt.RuntimeRunId == run.Id &&
                    rebuilt.PendingCount == 1 &&
                    rebuilt.Pending[0].Kind == "status"),
                It.Is<TimeSpan?>(ttl => ttl == TimeSpan.FromMinutes(30)),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task MarkConsumedAsync_RemovesRedisPendingSnapshotWhenQueueBecomesEmpty()
    {
        await using var db = CreateDb();
        var runs = new AgentRuntimeRunService(db);
        var redis = new Mock<IDistributedCacheService>();
        var interrupts = new AgentInterruptService(db, redis.Object);
        var run = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserMessage: "继续推进"));
        var interrupt = await interrupts.AddAsync(new CreateAgentInterruptRequest(
            RuntimeRunId: run.Id,
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            Kind: "cancel",
            Message: "先停一下",
            Priority: 10));

        await interrupts.MarkConsumedAsync(interrupt.Id, new { decision = "cancel_requested" });

        redis.Verify(x => x.RemoveAsync(
                $"agent_runtime:interrupts:{run.Id}",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AddAsync_WhenKindIsCancelRequestsRuntimeRunCancellation()
    {
        await using var db = CreateDb();
        var redis = new Mock<IDistributedCacheService>();
        var runs = new AgentRuntimeRunService(db, redis.Object);
        var interrupts = new AgentInterruptService(db, redis.Object, runs);
        var run = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserMessage: "继续推进"));

        await interrupts.AddAsync(new CreateAgentInterruptRequest(
            RuntimeRunId: run.Id,
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            Kind: "cancel",
            Message: "用户显式取消本次后台执行",
            Priority: 100));

        var reloaded = await db.AgentRuntimeRuns.SingleAsync(x => x.Id == run.Id);
        Assert.True(reloaded.CancelRequested);
        Assert.Contains("暂停/取消请求", reloaded.LastMessage);
        redis.Verify(x => x.SetAsync(
                "agent_runtime:active:user-1:session-1",
                It.Is<AgentRuntimeRunCacheSnapshot>(snapshot =>
                    snapshot.RuntimeRunId == run.Id &&
                    snapshot.CancelRequested),
                It.Is<TimeSpan?>(ttl => ttl == TimeSpan.FromMinutes(30)),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task AddAsync_WhenKindIsDirectionChangeDoesNotRequestRuntimeRunCancellation()
    {
        await using var db = CreateDb();
        var redis = new Mock<IDistributedCacheService>();
        var runs = new AgentRuntimeRunService(db, redis.Object);
        var interrupts = new AgentInterruptService(db, redis.Object, runs);
        var run = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserMessage: "继续推进"));

        await interrupts.AddAsync(new CreateAgentInterruptRequest(
            RuntimeRunId: run.Id,
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            Kind: "direction_change",
            Message: "用户要求改成打怪升级方向",
            Priority: 90));

        var reloaded = await db.AgentRuntimeRuns.SingleAsync(x => x.Id == run.Id);
        Assert.False(reloaded.CancelRequested);
        Assert.DoesNotContain("暂停/取消请求", reloaded.LastMessage);
        redis.Verify(x => x.SetAsync(
                "agent_runtime:active:user-1:session-1",
                It.Is<AgentRuntimeRunCacheSnapshot>(snapshot =>
                    snapshot.RuntimeRunId == run.Id &&
                    !snapshot.CancelRequested),
                It.Is<TimeSpan?>(ttl => ttl == TimeSpan.FromMinutes(30)),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ListActiveSessionCursorsAsync_ReturnsLatestActiveRunPerSession()
    {
        await using var db = CreateDb();
        var runs = new AgentRuntimeRunService(db);
        var older = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserMessage: "旧任务"));
        await runs.MarkRunningAsync(older.Id);
        older.UpdatedAt = DateTime.UtcNow.AddMinutes(-5);
        var latest = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserMessage: "新任务"));
        await runs.MarkRunningAsync(latest.Id);
        var otherSession = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-2",
            ProjectId: "project-1",
            UserMessage: "另一个会话"));
        await runs.MarkRunningAsync(otherSession.Id);
        var completed = await runs.CreateQueuedAsync(new CreateAgentRuntimeRunRequest(
            UserId: "user-1",
            SessionId: "session-3",
            ProjectId: "project-1",
            UserMessage: "已完成"));
        await runs.MarkCompletedAsync(completed.Id, new { ok = true });
        await db.SaveChangesAsync();

        var cursors = await runs.ListActiveSessionCursorsAsync();

        Assert.Equal(2, cursors.Count);
        Assert.Contains(cursors, cursor => cursor.SessionId == "session-1" && cursor.RuntimeRunId == latest.Id);
        Assert.Contains(cursors, cursor => cursor.SessionId == "session-2" && cursor.RuntimeRunId == otherSession.Id);
        Assert.DoesNotContain(cursors, cursor => cursor.RuntimeRunId == older.Id || cursor.RuntimeRunId == completed.Id);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }
}
