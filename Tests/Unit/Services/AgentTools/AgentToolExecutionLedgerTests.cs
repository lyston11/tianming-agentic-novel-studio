using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;
using TM.Web.NovelAgentWeb.Data;
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
            Call: new AgentToolCall { Name = "GenerateChapterWithChanges" }));

        await ledger.CompleteAsync(
            started.Id,
            new AgentToolExecutionResult
            {
                Success = false,
                Message = "生成正文前必须先构建章节上下文包。",
                IsRepairable = true,
                RecommendedToolName = "BuildChapterContextPackage",
                MissingPrerequisite = "chapter_context_package"
            });

        var row = await db.AgentToolExecutions.SingleAsync();
        Assert.Equal("failed", row.Status);
        Assert.Equal("GenerateChapterWithChanges", row.ToolName);
        Assert.Equal("BuildChapterContextPackage", row.RecommendedNextTool);
        Assert.Equal("chapter_context_package", row.MissingPrerequisite);
        Assert.Contains("上下文包", row.ErrorMessage);
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
