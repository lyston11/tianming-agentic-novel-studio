using Moq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.AgentTools;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Services.Memory;

public class ToolSearchCacheServiceTests
{
    private const string ToolCatalogSignatureValue = "catalog-v1";

    [Fact]
    public void ToolCatalogSignature_ChangesWhenToolSemanticCardOrSchemaChanges()
    {
        var original = ToolCatalogSignature.Compute(new[]
        {
            new ToolSchema
            {
                Name = "ProduceChapter",
                Description = "章节生产闭环",
                Risk = "High",
                RequiresConfirmation = true,
                Parameters = new Dictionary<string, string> { ["runId"] = "string" },
                Semantic = new AgentToolSemanticSpec
                {
                    DomainSurface = "TianmingKernel",
                    OutputKind = "FinalArtifact",
                    SideEffectLevel = "production_final_write",
                    ImpactScope = "current_project/chapter",
                    FailureContract = "returns failed_stage and recoverable actions",
                    ReadsFrom = { "StoryBible" },
                    WritesTo = { "NovelLibrary" },
                    UserVisibleWhere = "工作流 / 小说书城",
                    ResultSemantics = "生成并提交章节"
                }
            }
        });
        var changed = ToolCatalogSignature.Compute(new[]
        {
            new ToolSchema
            {
                Name = "ProduceChapter",
                Description = "章节生产闭环",
                Risk = "High",
                RequiresConfirmation = true,
                Parameters = new Dictionary<string, string>
                {
                    ["runId"] = "string",
                    ["commitPolicy"] = "string"
                },
                Semantic = new AgentToolSemanticSpec
                {
                    DomainSurface = "TianmingKernel",
                    OutputKind = "ProcessArtifact",
                    SideEffectLevel = "production_process_write",
                    ImpactScope = "current_project/chapter",
                    FailureContract = "returns failed_stage and recoverable actions",
                    ReadsFrom = { "StoryBible" },
                    WritesTo = { "Workflow" },
                    UserVisibleWhere = "工作流",
                    ResultSemantics = "生成章节草稿"
                }
            }
        });

        Assert.NotEqual(original, changed);
    }

    [Fact]
    public void ToolCatalogSignature_ChangesWhenSemanticSideEffectLevelChanges()
    {
        static ToolSchema Schema(string sideEffectLevel) => new()
        {
            Name = "QueryProjectContent",
            Description = "读取项目内容",
            Risk = "Low",
            Parameters = new Dictionary<string, string> { ["chapterId"] = "string" },
            Semantic = new AgentToolSemanticSpec
            {
                DomainSurface = "小说书城/工作流",
                OutputKind = "StateSnapshot",
                SideEffectLevel = sideEffectLevel,
                ImpactScope = "current_project/content",
                FailureContract = "read-only failure without writes",
                ReadsFrom = { "chapters" },
                WritesTo = { "none_read_only" },
                UserVisibleWhere = "Agent 对话",
                ResultSemantics = "读取章节正文"
            }
        };

        var original = ToolCatalogSignature.Compute(new[] { Schema("read_only") });
        var changed = ToolCatalogSignature.Compute(new[] { Schema("production_process_write") });

        Assert.NotEqual(original, changed);
    }

    [Fact]
    public void ToolCatalogSignature_ChangesWhenToolDecisionMapFieldsChange()
    {
        static ToolSchema Schema(
            string averageDuration,
            IReadOnlyList<string> progressEvents,
            IReadOnlyList<string> nextTools) => new()
        {
            Name = "ProduceChapter",
            Description = "章节生产闭环",
            Risk = "High",
            RequiresConfirmation = true,
            Parameters = new Dictionary<string, string> { ["runId"] = "string" },
            Semantic = new AgentToolSemanticSpec
            {
                DisplayName = "生产章节闭环",
                DomainSurface = "创作工作流 / 小说书城",
                OutputKind = "workflow_process_artifact",
                SideEffectLevel = "production_final_write",
                ImpactScope = "current_project/chapter/run/library",
                FailureContract = "returns failed_stage, produced_artifacts, recoverable_actions, requires_user_decision",
                RequiresProject = true,
                SupportsNoProjectSession = false,
                AverageDuration = averageDuration,
                ProgressEventContract = progressEvents.ToList(),
                NextPossibleTools = nextTools.ToList(),
                ReadsFrom = { "story_bible", "knowledge_base" },
                WritesTo = { "chapters", "production_events" },
                UserVisibleWhere = "创作工作流和小说书城",
                ResultSemantics = "生产并提交章节"
            }
        };

        var original = ToolCatalogSignature.Compute(new[]
        {
            Schema("long:60-180s", new[] { "chapter_context_package", "kernel_gate" }, new[] { "QueryProjectContent" })
        });
        var changedDuration = ToolCatalogSignature.Compute(new[]
        {
            Schema("medium:10-60s", new[] { "chapter_context_package", "kernel_gate" }, new[] { "QueryProjectContent" })
        });
        var changedProgress = ToolCatalogSignature.Compute(new[]
        {
            Schema("long:60-180s", new[] { "chapter_context_package", "agent_review" }, new[] { "QueryProjectContent" })
        });
        var changedNextTools = ToolCatalogSignature.Compute(new[]
        {
            Schema("long:60-180s", new[] { "chapter_context_package", "kernel_gate" }, new[] { "QueryNovelProductionState" })
        });

        Assert.NotEqual(original, changedDuration);
        Assert.NotEqual(original, changedProgress);
        Assert.NotEqual(original, changedNextTools);
    }

    [Fact]
    public void ToolCatalogSignature_ChangesWhenArtifactContractOrRecoveryPolicyChanges()
    {
        static ToolSchema Schema(
            IReadOnlyList<string> inputArtifacts,
            IReadOnlyList<string> outputArtifacts,
            string idempotencyPolicy,
            string rollbackPolicy) => new()
        {
            Name = "ProduceChapter",
            Description = "章节生产闭环",
            Risk = "High",
            RequiresConfirmation = true,
            Parameters = new Dictionary<string, string> { ["runId"] = "string" },
            Semantic = new AgentToolSemanticSpec
            {
                DisplayName = "生产章节闭环",
                DomainSurface = "创作工作流 / 小说书城",
                OutputKind = "workflow_process_artifact",
                SideEffectLevel = "production_final_write",
                ImpactScope = "current_project/chapter/run/library",
                FailureContract = "returns failed_stage, produced_artifacts, recoverable_actions, requires_user_decision",
                RequiresProject = true,
                SupportsNoProjectSession = false,
                AverageDuration = "long:60-180s",
                ProgressEventContract = { "chapter_context_package", "kernel_gate" },
                NextPossibleTools = { "QueryProjectContent" },
                ReadsFrom = { "story_bible", "knowledge_base" },
                WritesTo = { "chapters", "production_events" },
                InputArtifacts = inputArtifacts.ToList(),
                OutputArtifacts = outputArtifacts.ToList(),
                IdempotencyPolicy = idempotencyPolicy,
                RollbackPolicy = rollbackPolicy,
                UserVisibleWhere = "创作工作流和小说书城",
                ResultSemantics = "生产并提交章节"
            }
        };

        var original = ToolCatalogSignature.Compute(new[]
        {
            Schema(
                new[] { "chapter_plan_run", "continuity_pack" },
                new[] { "chapter_commit", "chapter_version" },
                "Uses runId + commitPolicy.",
                "Recover through ChapterVersion rollback.")
        });
        var changedInput = ToolCatalogSignature.Compute(new[]
        {
            Schema(
                new[] { "chapter_plan_run", "knowledge_binding_snapshot" },
                new[] { "chapter_commit", "chapter_version" },
                "Uses runId + commitPolicy.",
                "Recover through ChapterVersion rollback.")
        });
        var changedOutput = ToolCatalogSignature.Compute(new[]
        {
            Schema(
                new[] { "chapter_plan_run", "continuity_pack" },
                new[] { "chapter_draft", "chapter_version" },
                "Uses runId + commitPolicy.",
                "Recover through ChapterVersion rollback.")
        });
        var changedPolicy = ToolCatalogSignature.Compute(new[]
        {
            Schema(
                new[] { "chapter_plan_run", "continuity_pack" },
                new[] { "chapter_commit", "chapter_version" },
                "Uses targetChapterId only.",
                "Recover through ChapterVersion rollback.")
        });
        var changedRollback = ToolCatalogSignature.Compute(new[]
        {
            Schema(
                new[] { "chapter_plan_run", "continuity_pack" },
                new[] { "chapter_commit", "chapter_version" },
                "Uses runId + commitPolicy.",
                "Manual database repair required.")
        });

        Assert.NotEqual(original, changedInput);
        Assert.NotEqual(original, changedOutput);
        Assert.NotEqual(original, changedPolicy);
        Assert.NotEqual(original, changedRollback);
    }

    [Fact]
    public async Task SaveAsync_UsesCanonicalVersionedRedisKey()
    {
        var redis = new Mock<IDistributedCacheService>();
        var memory = new Mock<IMemoryCacheService>();
        var versions = new Mock<IAgentMemoryVersionService>();
        versions.Setup(x => x.GetCombinedVersionAsync("user-1", "project-1", "session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("project=1|session=2|execution=3");
        await using var db = CreateDb();
        var service = new ToolSearchCacheService(redis.Object, memory.Object, versions.Object, db);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };
        var expectedKey = AgentMemoryKeys.ToolCache(
            "user-1",
            "session-1",
            "project-1",
            "global:Planning",
            $"project=1|session=2|tool_catalog={ToolCatalogSignatureValue}");

        await service.SaveAsync(
            session,
            "Planning",
            new[] { new ToolSchema { Name = "PlanChapter" } },
            ToolCatalogSignatureValue,
            CancellationToken.None);

        var setInvocation = Assert.Single(redis.Invocations.Where(i =>
            i.Method.Name == nameof(IDistributedCacheService.SetAsync) &&
            i.Arguments[0] is string key &&
            key == expectedKey));
        Assert.Equal(TimeSpan.FromMinutes(5), setInvocation.Arguments[2]);
    }

    [Fact]
    public async Task SaveAsync_RejectsMissingToolCatalogSignature()
    {
        var redis = new Mock<IDistributedCacheService>();
        var memory = new Mock<IMemoryCacheService>();
        var versions = new Mock<IAgentMemoryVersionService>();
        await using var db = CreateDb();
        var service = new ToolSearchCacheService(redis.Object, memory.Object, versions.Object, db);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1"
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveAsync(
                session,
                "Planning",
                new[] { new ToolSchema { Name = "PlanChapter" } },
                " ",
                CancellationToken.None));

        Assert.Contains("tool catalog signature", ex.Message, StringComparison.OrdinalIgnoreCase);
        versions.Verify(x => x.GetCombinedVersionAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullWhenMemoryVersionChanged()
    {
        var redis = new Mock<IDistributedCacheService>();
        var memory = new Mock<IMemoryCacheService>();
        var versions = new Mock<IAgentMemoryVersionService>();
        versions.Setup(x => x.GetCombinedVersionAsync("user-1", "project-1", "session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("project=2|execution=1");

        await using var db = CreateDb();
        var service = new ToolSearchCacheService(redis.Object, memory.Object, versions.Object, db);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1",
            DiscoveredPhase = "global:Planning",
            ToolSearchCacheVersion = $"project=1|tool_catalog={ToolCatalogSignatureValue}",
            LastToolSearchAt = DateTime.UtcNow,
            DiscoveredTools = new List<ToolSchema>
            {
                new() { Name = "PlanChapter", Description = "规划章节" }
            }
        };

        var result = await service.GetAsync(session, "Planning", ToolCatalogSignatureValue, CancellationToken.None);

        Assert.False(result.Hit);
        Assert.Null(result.Tools);
        Assert.Equal("none", result.Source);
    }

    [Fact]
    public async Task GetAsync_KeepsToolSearchCacheFreshWhenOnlyToolExecutionVersionChanged()
    {
        var redis = new Mock<IDistributedCacheService>();
        var memory = new Mock<IMemoryCacheService>();
        var versions = new Mock<IAgentMemoryVersionService>();
        versions.Setup(x => x.GetCombinedVersionAsync("user-1", "project-1", "session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("project:project-1:*=1|tool_execution:project-1:session-1=7");

        await using var db = CreateDb();
        var service = new ToolSearchCacheService(redis.Object, memory.Object, versions.Object, db);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1",
            DiscoveredPhase = "global:Planning",
            ToolSearchCacheVersion = $"project:project-1:*=1|tool_catalog={ToolCatalogSignatureValue}",
            LastToolSearchAt = DateTime.UtcNow,
            DiscoveredTools = new List<ToolSchema>
            {
                new() { Name = "PlanChapter", Description = "规划章节" }
            }
        };

        var result = await service.GetAsync(session, "Planning", ToolCatalogSignatureValue, CancellationToken.None);

        Assert.True(result.Hit);
        Assert.Equal("session-hot", result.Source);
        Assert.NotNull(result.Tools);
        Assert.Single(result.Tools);
        Assert.Equal("PlanChapter", result.Tools![0].Name);
    }

    [Fact]
    public async Task GetAsync_InvalidatesCacheWhenToolCatalogSignatureChanged()
    {
        var redis = new Mock<IDistributedCacheService>();
        var memory = new Mock<IMemoryCacheService>();
        var versions = new Mock<IAgentMemoryVersionService>();
        versions.Setup(x => x.GetCombinedVersionAsync("user-1", "project-1", "session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("project=1|session=2|execution=3");

        await using var db = CreateDb();
        var service = new ToolSearchCacheService(redis.Object, memory.Object, versions.Object, db);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        await service.SaveAsync(
            session,
            "Planning",
            new[]
            {
                new ToolSchema
                {
                    Name = "PlanChapter",
                    Description = "旧章节规划工具",
                    Parameters = new Dictionary<string, string> { ["creativeBrief"] = "string" }
                }
            },
            "catalog-old",
            CancellationToken.None);

        var reloadedSession = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1",
            DiscoveredPhase = "global:Planning",
            ToolSearchCacheVersion = session.ToolSearchCacheVersion,
            LastToolSearchAt = session.LastToolSearchAt,
            DiscoveredTools = session.DiscoveredTools.ToList()
        };

        var result = await service.GetAsync(
            reloadedSession,
            "Planning",
            "catalog-new",
            CancellationToken.None);

        Assert.False(result.Hit);
        Assert.Equal("none", result.Source);
        Assert.Contains("tool_catalog=", session.ToolSearchCacheVersion);
    }

    [Fact]
    public async Task GetAsync_RestoresToolsFromSqliteSnapshotAndRefreshesRedisOnHotCacheMiss()
    {
        await using var db = CreateDb();
        var redis = new Mock<IDistributedCacheService>();
        var memory = new Mock<IMemoryCacheService>();
        var versions = new Mock<IAgentMemoryVersionService>();
        versions.Setup(x => x.GetCombinedVersionAsync("user-1", "project-1", "session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("project=1|session=2|tool_execution=9");
        var service = new ToolSearchCacheService(redis.Object, memory.Object, versions.Object, db);
        var originalSession = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        await service.SaveAsync(
            originalSession,
            "Planning",
            new[]
            {
                new ToolSchema
                {
                    Name = "PlanChapter",
                    Description = "规划章节",
                    Risk = "Medium",
                    Parameters = new Dictionary<string, string> { ["creativeBrief"] = "string" }
                }
            },
            ToolCatalogSignatureValue,
            CancellationToken.None);

        var reloadedSession = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1",
            DiscoveredPhase = "global:Planning",
            ToolSearchCacheVersion = $"project=1|session=2|tool_catalog={ToolCatalogSignatureValue}",
            LastToolSearchAt = originalSession.LastToolSearchAt
        };

        var result = await service.GetAsync(reloadedSession, "Planning", ToolCatalogSignatureValue, CancellationToken.None);

        Assert.True(result.Hit);
        Assert.Equal("sqlite-snapshot", result.Source);
        Assert.NotNull(result.Tools);
        Assert.Single(result.Tools);
        Assert.Equal("PlanChapter", result.Tools![0].Name);
        Assert.Single(await db.AgentToolSearchSnapshots.ToListAsync());
        redis.Verify(x => x.SetAsync(
                It.Is<string>(key => key == AgentMemoryKeys.ToolCache(
                    "user-1",
                    "session-1",
                    "project-1",
                    "global:Planning",
                    $"project=1|session=2|tool_catalog={ToolCatalogSignatureValue}")),
                It.IsAny<object>(),
                It.Is<TimeSpan?>(ttl => ttl > TimeSpan.Zero && ttl <= TimeSpan.FromMinutes(5)),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task GetAsync_RestoresToolsFromHotCacheBeforeSqliteSnapshot()
    {
        await using var db = CreateDb();
        var redis = new Mock<IDistributedCacheService>();
        var memory = new MemoryCacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<MemoryCacheService>.Instance);
        var versions = new Mock<IAgentMemoryVersionService>();
        versions.Setup(x => x.GetCombinedVersionAsync("user-1", "project-1", "session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("project=1|session=2");
        var service = new ToolSearchCacheService(redis.Object, memory, versions.Object, db);
        var originalSession = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1"
        };

        await service.SaveAsync(
            originalSession,
            "Planning",
            new[] { new ToolSchema { Name = "PlanChapter", Description = "规划章节" } },
            ToolCatalogSignatureValue,
            CancellationToken.None);

        var reloadedSession = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1",
            ActiveProjectId = "project-1",
            DiscoveredPhase = "Planning",
            ToolSearchCacheVersion = $"project=1|session=2|tool_catalog={ToolCatalogSignatureValue}",
            LastToolSearchAt = DateTime.UtcNow.AddMinutes(-10)
        };

        db.AgentToolSearchSnapshots.RemoveRange(db.AgentToolSearchSnapshots);
        await db.SaveChangesAsync();

        var result = await service.GetAsync(reloadedSession, "Planning", ToolCatalogSignatureValue, CancellationToken.None);

        Assert.True(result.Hit);
        Assert.Equal("redis-hot", result.Source);
        Assert.NotNull(result.Tools);
        Assert.Equal("PlanChapter", result.Tools![0].Name);
        Assert.Empty(await db.AgentToolSearchSnapshots.ToListAsync());
    }

    [Fact]
    public async Task SaveAndGetAsync_NormalizesPhaseCase()
    {
        await using var db = CreateDb();
        var redis = new Mock<IDistributedCacheService>();
        var memory = new Mock<IMemoryCacheService>();
        var versions = new Mock<IAgentMemoryVersionService>();
        versions.Setup(x => x.GetCombinedVersionAsync("user-1", null, "session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("session=1");
        var service = new ToolSearchCacheService(redis.Object, memory.Object, versions.Object, db);
        var session = new AgentSession
        {
            UserId = "user-1",
            SessionId = "session-1"
        };

        await service.SaveAsync(
            session,
            "planning",
            new[] { new ToolSchema { Name = "ResolveNovelProject" } },
            ToolCatalogSignatureValue,
            CancellationToken.None);
        session.DiscoveredTools.Clear();

        var result = await service.GetAsync(session, "Planning", ToolCatalogSignatureValue, CancellationToken.None);

        Assert.True(result.Hit);
        Assert.Equal("sqlite-snapshot", result.Source);
        Assert.NotNull(result.Tools);
        Assert.Equal("global:Planning", session.DiscoveredPhase);
        Assert.Equal("ResolveNovelProject", result.Tools![0].Name);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }
}
