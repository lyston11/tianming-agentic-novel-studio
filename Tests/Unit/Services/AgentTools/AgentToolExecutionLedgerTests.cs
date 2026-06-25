using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.AgentTools;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Services.AgentTools;

public class AgentToolExecutionLedgerTests
{
    [Fact]
    public async Task StartAndCompleteAsync_PersistsSqliteTruthAndRedisHotState()
    {
        await using var db = CreateDb();
        var redis = new Mock<IDistributedCacheService>();
        var ledger = new AgentToolExecutionLedger(db, redis.Object, NullLogger<AgentToolExecutionLedger>.Instance);
        var call = new AgentToolCall
        {
            Name = "PlanChapter",
            Arguments = new Dictionary<string, string>
            {
                ["creativeBrief"] = "主角第一次付出代价",
                ["chapterId"] = "chapter-001"
            }
        };

        var started = await ledger.StartAsync(new AgentToolExecutionStart(
            UserId: "user-1",
            ProjectId: "project-1",
            SessionId: "session-1",
            RunId: "run-1",
            Phase: "Planning",
            Risk: "Medium",
            Call: call,
            SideEffects: new AgentToolSideEffectSpec
            {
                WritesMemoryScopes = { "session", "execution" },
                WritesSqliteEntities = { "agent_runs" }
            }));

        Assert.Equal("running", started.Status);
        Assert.Equal("PlanChapter", started.ToolName);
        Assert.False(string.IsNullOrWhiteSpace(started.ArgumentsHash));

        await ledger.CompleteAsync(
            started.Id,
            new AgentToolExecutionResult
            {
                Success = true,
                RunId = "run-1",
                Phase = "chapter_candidates",
                Message = "章节候选已生成"
            });

        var row = await db.AgentToolExecutions.SingleAsync();
        Assert.Equal("succeeded", row.Status);
        Assert.Equal("chapter_candidates", row.ResultPhase);
        Assert.Equal("章节候选已生成", row.ResultMessage);
        var sideEffects = JsonSerializer.Deserialize<AgentToolSideEffectSpec>(row.SideEffectsJson);
        Assert.NotNull(sideEffects);
        Assert.Contains("agent_runs", sideEffects!.WritesSqliteEntities);
        Assert.Contains("execution", sideEffects.WritesMemoryScopes);
        Assert.NotNull(row.CompletedAt);
        redis.Verify(x => x.SetAsync(
                It.Is<string>(key => key == "tool:recent:user-1:session-1:project-1"),
                It.IsAny<IReadOnlyList<AgentToolExecutionSnapshot>>(),
                It.Is<TimeSpan?>(ttl => ttl == TimeSpan.FromMinutes(30)),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task StartAsync_PersistsToolSemanticArtifactContract()
    {
        await using var db = CreateDb();
        var redis = new Mock<IDistributedCacheService>();
        var ledger = new AgentToolExecutionLedger(db, redis.Object, NullLogger<AgentToolExecutionLedger>.Instance);

        await ledger.StartAsync(new AgentToolExecutionStart(
            UserId: "user-1",
            ProjectId: "project-1",
            SessionId: "session-1",
            RunId: "run-1",
            Phase: "Production",
            Risk: "High",
            Call: new AgentToolCall { Name = "ProduceChapter" },
            SideEffects: new AgentToolSideEffectSpec
            {
                ReadsSqliteEntities = { "agent_runs" },
                WritesSqliteEntities = { "chapters" }
            },
            SemanticContract: new AgentToolSemanticSpec
            {
                DisplayName = "生产章节闭环",
                InputArtifacts = { "chapter_plan_run", "continuity_pack", "knowledge_binding_snapshot" },
                OutputArtifacts = { "chapter_commit", "chapter_version" },
                IdempotencyPolicy = "Uses runId + targetChapterId + commitPolicy.",
                RollbackPolicy = "Recover through ChapterVersion rollback."
            }));

        var row = await db.AgentToolExecutions.SingleAsync();
        using var document = JsonDocument.Parse(row.SemanticContractJson);
        var root = document.RootElement;
        Assert.Equal("生产章节闭环", root.GetProperty("displayName").GetString());
        Assert.Contains(root.GetProperty("inputArtifacts").EnumerateArray(), item => item.GetString() == "continuity_pack");
        Assert.Contains(root.GetProperty("outputArtifacts").EnumerateArray(), item => item.GetString() == "chapter_version");
        Assert.Contains("commitPolicy", root.GetProperty("idempotencyPolicy").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ChapterVersion", root.GetProperty("rollbackPolicy").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteAsync_FailedResultStoresFailureForRecovery()
    {
        await using var db = CreateDb();
        var redis = new Mock<IDistributedCacheService>();
        var ledger = new AgentToolExecutionLedger(db, redis.Object, NullLogger<AgentToolExecutionLedger>.Instance);

        var started = await ledger.StartAsync(new AgentToolExecutionStart(
            UserId: "user-1",
            ProjectId: "project-1",
            SessionId: "session-1",
            RunId: null,
            Phase: "Creation",
            Risk: "High",
            Call: new AgentToolCall { Name = "ProduceChapter" }));

        await ledger.CompleteAsync(
            started.Id,
            new AgentToolExecutionResult
            {
                Success = false,
                Message = "生成正文前必须先构建章节上下文包。",
                IsRepairable = true,
                RecommendedToolName = "ProduceChapter",
                MissingPrerequisite = "chapter_context_package"
            });

        var row = await db.AgentToolExecutions.SingleAsync();
        Assert.Equal("failed", row.Status);
        Assert.Equal("ProduceChapter", row.ToolName);
        Assert.Equal("ProduceChapter", row.RecommendedNextTool);
        Assert.Equal("chapter_context_package", row.MissingPrerequisite);
        Assert.Contains("上下文包", row.ErrorMessage);
    }

    [Fact]
    public async Task CompleteAsync_PersistsStructuredToolFailureAndArtifactKind()
    {
        await using var db = CreateDb();
        var redis = new Mock<IDistributedCacheService>();
        var ledger = new AgentToolExecutionLedger(db, redis.Object, NullLogger<AgentToolExecutionLedger>.Instance);

        var started = await ledger.StartAsync(new AgentToolExecutionStart(
            UserId: "user-1",
            ProjectId: "project-1",
            SessionId: "session-1",
            RunId: "run-1",
            Phase: "Production",
            Risk: "High",
            Call: new AgentToolCall { Name = "ProduceChapter" }));

        await ledger.CompleteAsync(
            started.Id,
            new AgentToolExecutionResult
            {
                Success = false,
                Phase = "changes_extracted",
                Message = "CHANGES JSON 不是可解析对象。",
                Failure = new AgentToolFailure
                {
                    Code = "CHANGES_PARSE_FAILED",
                    FailedStage = "ChangesExtracted",
                    Reason = "CHANGES JSON 不是可解析对象。",
                    Recoverable = true,
                    RecommendedAction = "ExtractChanges",
                    ArtifactIds = new[] { "draft-1" }
                },
                Artifact = new AgentToolArtifact
                {
                    ArtifactType = "chapter_draft",
                    ArtifactId = "draft-1",
                    OutputKind = AgentToolOutputKind.ProcessArtifact,
                    UserVisibleWhere = new[] { AgentRuntimeEventSurface.Workflow },
                    Summary = "已保留可恢复草稿。"
                },
                Suggestions = new[] { "重新抽取 CHANGES", "基于草稿继续修订" },
                RequiresConfirmation = true
            });

        var row = await db.AgentToolExecutions.SingleAsync();
        Assert.Equal("failed", row.Status);
        Assert.Equal("CHANGES_PARSE_FAILED", row.ErrorType);
        Assert.Contains("\"failedStage\":\"ChangesExtracted\"", row.FailureJson);
        Assert.Contains("\"artifactIds\":[\"draft-1\"]", row.FailureJson);
        using var failureJson = JsonDocument.Parse(row.FailureJson);
        var failureRoot = failureJson.RootElement;
        Assert.True(failureRoot.GetProperty("requiresUserDecision").GetBoolean());
        var producedArtifact = failureRoot.GetProperty("producedArtifacts").EnumerateArray().Single();
        Assert.Equal("chapter_draft", producedArtifact.GetProperty("artifactType").GetString());
        Assert.Equal("draft-1", producedArtifact.GetProperty("artifactId").GetString());
        Assert.Equal("已保留可恢复草稿。", producedArtifact.GetProperty("summary").GetString());
        var recoverableActions = failureRoot.GetProperty("recoverableActions")
            .EnumerateArray()
            .Select(x => x.GetString())
            .ToArray();
        Assert.Equal(new[] { "重新抽取 CHANGES", "基于草稿继续修订" }, recoverableActions);
        Assert.Contains("\"outputKind\":\"ProcessArtifact\"", row.ArtifactJson);
        Assert.Contains("\"userVisibleWhere\":[\"workflow\"]", row.ArtifactJson);
    }

    [Fact]
    public async Task CompleteAsync_FailedInputArtifactResolutionPersistsInputArtifactStates()
    {
        await using var db = CreateDb();
        var redis = new Mock<IDistributedCacheService>();
        var ledger = new AgentToolExecutionLedger(db, redis.Object, NullLogger<AgentToolExecutionLedger>.Instance);

        var started = await ledger.StartAsync(new AgentToolExecutionStart(
            UserId: "user-1",
            ProjectId: "project-1",
            SessionId: "session-1",
            RunId: "run-next",
            Phase: "Production",
            Risk: "High",
            Call: new AgentToolCall { Name = "ProduceChapter" }));

        await ledger.CompleteAsync(
            started.Id,
            new AgentToolExecutionResult
            {
                Success = false,
                RunId = "run-next",
                Phase = "semantic_precondition",
                Message = "上一章提交后后台沉淀尚未完成，不能继续生产下一章。",
                MissingPrerequisite = "previous_chapter_post_commit_outbox",
                Failure = new AgentToolFailure
                {
                    Code = "TOOL_INPUT_ARTIFACT_BLOCKED",
                    FailedStage = "semantic_precondition",
                    Reason = "上一章提交后后台沉淀尚未完成，不能继续生产下一章。",
                    Recoverable = true,
                    RecommendedAction = "QueryProductionOutbox",
                    RecoverableActions = new[] { "QueryNovelProductionState", "QueryProductionOutbox", "RetryProductionOutbox" }
                },
                Data = new ToolInputArtifactResolution
                {
                    BlocksExecution = true,
                    MissingPrerequisite = "previous_chapter_post_commit_outbox",
                    FailureCode = "TOOL_INPUT_ARTIFACT_BLOCKED",
                    Reason = "上一章提交后后台沉淀尚未完成，不能继续生产下一章。",
                    RunId = "run-next",
                    InputArtifacts = new[]
                    {
                        new ToolInputArtifactState(
                            "post_commit_outbox",
                            "pending",
                            "outbox-finalize-001",
                            "上一章提交后事实沉淀仍在等待处理。",
                            true,
                            new[] { "QueryNovelProductionState", "QueryProductionOutbox", "RetryProductionOutbox" })
                    }
                }
            });

        var row = await db.AgentToolExecutions.SingleAsync();
        using var failureJson = JsonDocument.Parse(row.FailureJson);
        var inputArtifact = failureJson.RootElement.GetProperty("inputArtifacts").EnumerateArray().Single();
        Assert.Equal("post_commit_outbox", inputArtifact.GetProperty("artifactName").GetString());
        Assert.Equal("pending", inputArtifact.GetProperty("status").GetString());
        Assert.Equal("outbox-finalize-001", inputArtifact.GetProperty("artifactId").GetString());
        Assert.True(inputArtifact.GetProperty("blocksExecution").GetBoolean());
        var actions = inputArtifact.GetProperty("recommendedActions")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        Assert.Contains("RetryProductionOutbox", actions);
    }

    [Fact]
    public async Task FailRunningForSessionAsync_ClosesOnlyMatchingRunningExecutions()
    {
        await using var db = CreateDb();
        var redis = new Mock<IDistributedCacheService>();
        var ledger = new AgentToolExecutionLedger(db, redis.Object, NullLogger<AgentToolExecutionLedger>.Instance);

        var matching = await ledger.StartAsync(new AgentToolExecutionStart(
            UserId: "user-1",
            ProjectId: "project-1",
            SessionId: "session-1",
            RunId: "chapter-run-1",
            Phase: "Drafting",
            Risk: "Medium",
            Call: new AgentToolCall { Name = "ProduceChapter" }));
        var otherSession = await ledger.StartAsync(new AgentToolExecutionStart(
            UserId: "user-1",
            ProjectId: "project-1",
            SessionId: "session-2",
            RunId: "chapter-run-2",
            Phase: "Drafting",
            Risk: "Medium",
            Call: new AgentToolCall { Name = "ProduceChapter" }));
        await ledger.CompleteAsync(matching.Id, new AgentToolExecutionResult
        {
            Success = true,
            Phase = "draft_generated",
            Message = "先完成一条"
        });
        var stillRunning = await ledger.StartAsync(new AgentToolExecutionStart(
            UserId: "user-1",
            ProjectId: "project-1",
            SessionId: "session-1",
            RunId: "chapter-run-3",
            Phase: "Drafting",
            Risk: "Medium",
            Call: new AgentToolCall { Name = "ProduceChapter" }));

        var closed = await ledger.FailRunningForSessionAsync(
            "user-1",
            "session-1",
            "project-1",
            "后台执行被应用停止取消。");

        Assert.Equal(1, closed);
        var rows = await db.AgentToolExecutions.ToDictionaryAsync(x => x.Id);
        Assert.Equal("succeeded", rows[matching.Id].Status);
        Assert.Equal("failed", rows[stillRunning.Id].Status);
        Assert.Equal("runtime_cancelled", rows[stillRunning.Id].ResultPhase);
        Assert.Contains("应用停止", rows[stillRunning.Id].ErrorMessage);
        Assert.NotNull(rows[stillRunning.Id].CompletedAt);
        Assert.Equal("running", rows[otherSession.Id].Status);
    }

    [Fact]
    public async Task FailAllRunningAsync_ClosesStaleRunningExecutionsOnStartup()
    {
        await using var db = CreateDb();
        var redis = new Mock<IDistributedCacheService>();
        var ledger = new AgentToolExecutionLedger(db, redis.Object, NullLogger<AgentToolExecutionLedger>.Instance);

        var staleOne = await ledger.StartAsync(new AgentToolExecutionStart(
            UserId: "user-1",
            ProjectId: "project-1",
            SessionId: "session-1",
            RunId: "chapter-run-1",
            Phase: "Drafting",
            Risk: "Medium",
            Call: new AgentToolCall { Name = "ProduceChapter" }));
        var staleTwo = await ledger.StartAsync(new AgentToolExecutionStart(
            UserId: "user-2",
            ProjectId: null,
            SessionId: "session-2",
            RunId: null,
            Phase: "Conversation",
            Risk: "Low",
            Call: new AgentToolCall { Name = "tool_search" }));
        await ledger.CompleteAsync(staleTwo.Id, new AgentToolExecutionResult
        {
            Success = true,
            Phase = "tool_search",
            Message = "已完成"
        });
        var stillStale = await ledger.StartAsync(new AgentToolExecutionStart(
            UserId: "user-2",
            ProjectId: null,
            SessionId: "session-2",
            RunId: null,
            Phase: "Conversation",
            Risk: "Low",
            Call: new AgentToolCall { Name = "QueryWorkspaceState" }));

        var closed = await ledger.FailAllRunningAsync("应用启动时清理上次未收尾的工具执行。");

        Assert.Equal(2, closed);
        var rows = await db.AgentToolExecutions.ToDictionaryAsync(x => x.Id);
        Assert.Equal("failed", rows[staleOne.Id].Status);
        Assert.Equal("succeeded", rows[staleTwo.Id].Status);
        Assert.Equal("failed", rows[stillStale.Id].Status);
        Assert.Equal("runtime_cancelled", rows[staleOne.Id].ResultPhase);
        Assert.Equal("runtime_cancelled", rows[stillStale.Id].ErrorType);
    }

    [Fact]
    public async Task CompleteAsync_BumpsToolExecutionMemoryVersion()
    {
        await using var db = CreateDb();
        var versions = new Mock<IAgentMemoryVersionService>();
        versions.Setup(x => x.BumpAsync("user-1", "project-1", "session-1", "tool_execution", It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var ledger = new AgentToolExecutionLedger(
            db,
            Mock.Of<IDistributedCacheService>(),
            NullLogger<AgentToolExecutionLedger>.Instance,
            versions.Object);

        var started = await ledger.StartAsync(new AgentToolExecutionStart(
            UserId: "user-1",
            ProjectId: "project-1",
            SessionId: "session-1",
            RunId: "run-1",
            Phase: "Planning",
            Risk: "Low",
            Call: new AgentToolCall { Name = "SearchCreativeKnowledge" }));

        await ledger.CompleteAsync(started.Id, new AgentToolExecutionResult
        {
            Success = true,
            Phase = "knowledge_retrieved",
            Message = "知识已召回"
        });

        versions.Verify(x => x.BumpAsync("user-1", "project-1", "session-1", "tool_execution", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RebindProjectAsync_MovesProjectCreatingExecutionIntoNewProjectHotState()
    {
        await using var db = CreateDb();
        var redis = new Mock<IDistributedCacheService>();
        var versions = new Mock<IAgentMemoryVersionService>();
        versions.Setup(x => x.BumpAsync("user-1", "project-2", "session-1", "tool_execution", It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var ledger = new AgentToolExecutionLedger(
            db,
            redis.Object,
            NullLogger<AgentToolExecutionLedger>.Instance,
            versions.Object);

        var started = await ledger.StartAsync(new AgentToolExecutionStart(
            UserId: "user-1",
            ProjectId: null,
            SessionId: "session-1",
            RunId: null,
            Phase: "Conversation",
            Risk: "Low",
            Call: new AgentToolCall { Name = "ResolveNovelProject" }));

        await ledger.RebindProjectAsync(started.Id, "project-2");
        await ledger.CompleteAsync(started.Id, new AgentToolExecutionResult
        {
            Success = true,
            Phase = "awaiting_user_foundation",
            Message = "已创建新小说"
        });

        var row = await db.AgentToolExecutions.SingleAsync();
        Assert.Equal("project-2", row.ProjectId);

        var recent = await ledger.GetRecentAsync("user-1", "session-1", "project-2");
        Assert.Single(recent);
        Assert.Equal("ResolveNovelProject", recent[0].ToolName);
        redis.Verify(x => x.SetAsync(
                "tool:recent:user-1:session-1:project-2",
                It.Is<IReadOnlyList<AgentToolExecutionSnapshot>>(items =>
                    items.Count == 1 && items[0].ToolName == "ResolveNovelProject"),
                TimeSpan.FromMinutes(30),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        versions.Verify(x => x.BumpAsync("user-1", "project-2", "session-1", "tool_execution", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsRedisHotStateWhenAvailable()
    {
        await using var db = CreateDb();
        var cached = new List<AgentToolExecutionSnapshot>
        {
            new()
            {
                Id = "execution-1",
                ToolName = "PlanChapter",
                Status = "succeeded",
                Phase = "Planning",
                ResultPhase = "chapter_candidates",
                ResultMessage = "章节候选已生成",
                StartedAt = DateTime.UtcNow.AddSeconds(-2),
                CompletedAt = DateTime.UtcNow
            }
        };
        var redis = new Mock<IDistributedCacheService>();
        redis
            .Setup(x => x.GetAsync<IReadOnlyList<AgentToolExecutionSnapshot>>(
                "tool:recent:user-1:session-1:project-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);
        var ledger = new AgentToolExecutionLedger(db, redis.Object, NullLogger<AgentToolExecutionLedger>.Instance);

        var result = await ledger.GetRecentAsync("user-1", "session-1", "project-1");

        Assert.Single(result);
        Assert.Equal("PlanChapter", result[0].ToolName);
        Assert.Equal("chapter_candidates", result[0].ResultPhase);
        Assert.Empty(await db.AgentToolExecutions.ToListAsync());
    }

    [Fact]
    public async Task GetRecentAsync_FallsBackToSqliteAndRefreshesRedisOnCacheMiss()
    {
        await using var db = CreateDb();
        db.AgentToolExecutions.Add(new()
        {
            Id = "execution-1",
            UserId = "user-1",
            ProjectId = "project-1",
            SessionId = "session-1",
            RunId = "run-1",
            ToolName = "PlanChapter",
            Phase = "Planning",
            Risk = "Medium",
            ArgumentsHash = "hash",
            Status = "succeeded",
            ResultPhase = "chapter_candidates",
            ResultMessage = "章节候选已生成",
            StartedAt = DateTime.UtcNow.AddSeconds(-3),
            CompletedAt = DateTime.UtcNow.AddSeconds(-1)
        });
        await db.SaveChangesAsync();

        var redis = new Mock<IDistributedCacheService>();
        redis
            .Setup(x => x.GetAsync<IReadOnlyList<AgentToolExecutionSnapshot>>(
                "tool:recent:user-1:session-1:project-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AgentToolExecutionSnapshot>?)null);
        var ledger = new AgentToolExecutionLedger(db, redis.Object, NullLogger<AgentToolExecutionLedger>.Instance);

        var result = await ledger.GetRecentAsync("user-1", "session-1", "project-1");

        Assert.Single(result);
        Assert.Equal("PlanChapter", result[0].ToolName);
        redis.Verify(x => x.SetAsync(
                "tool:recent:user-1:session-1:project-1",
                It.Is<IReadOnlyList<AgentToolExecutionSnapshot>>(items =>
                    items.Count == 1 &&
                    items[0].ToolName == "PlanChapter" &&
                    items[0].ResultPhase == "chapter_candidates"),
                TimeSpan.FromMinutes(30),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }
}
