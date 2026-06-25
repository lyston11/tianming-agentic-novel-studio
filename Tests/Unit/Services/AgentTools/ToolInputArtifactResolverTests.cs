using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.AgentTools;
using TM.Web.NovelAgentWeb.Support;
using Xunit;
using Chapter = TM.Web.NovelAgentWeb.Data.Entities.Chapter;
using NovelProject = TM.Web.NovelAgentWeb.Data.Entities.NovelProject;
using OutboxEvent = TM.Web.NovelAgentWeb.Data.Entities.OutboxEvent;
using RevisionPlan = TM.Web.NovelAgentWeb.Data.Entities.RevisionPlan;
using TianmingPackage = TM.Web.NovelAgentWeb.Data.Entities.TianmingPackage;
using User = TM.Web.NovelAgentWeb.Data.Entities.User;

namespace Tests.Unit.Services.AgentTools;

public class ToolInputArtifactResolverTests
{
    [Fact]
    public async Task ResolveAsync_ProduceChapterWithoutRun_ReturnsMissingChapterPlanRun()
    {
        await using var db = CreateDb();
        var resolver = new ToolInputArtifactResolver(db);

        var resolution = await resolver.ResolveAsync(new ToolInputArtifactResolutionRequest(
            ProduceChapterTool(),
            new AgentToolCall { Name = "ProduceChapter" },
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1"
            },
            new StoryBibleDocument(),
            "project-1"));

        Assert.True(resolution.BlocksExecution);
        Assert.Equal("chapter_plan_run", resolution.MissingPrerequisite);
        Assert.Equal("TOOL_INPUT_ARTIFACT_MISSING", resolution.FailureCode);
        Assert.Contains("ProduceChapter 需要", resolution.Reason);
    }

    [Fact]
    public async Task ResolveAsync_ProduceChapterWithStalePackageAndNoRevisionPlan_BlocksWithStaleState()
    {
        await using var db = CreateDb();
        db.TianmingPackages.Add(new TianmingPackage
        {
            Id = "pkg-stale-1",
            UserId = "user-1",
            ProjectId = "project-1",
            RuntimeRunId = "run-stale",
            ChapterId = "chapter-002",
            Status = "stale",
            InputJson = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var resolver = new ToolInputArtifactResolver(db);

        var resolution = await resolver.ResolveAsync(new ToolInputArtifactResolutionRequest(
            ProduceChapterTool(),
            new AgentToolCall
            {
                Name = "ProduceChapter",
                Arguments = new Dictionary<string, string>
                {
                    ["runId"] = "run-stale"
                }
            },
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1"
            },
            StoryBibleWithChapterRun("run-stale", "chapter-002"),
            "project-1"));

        Assert.True(resolution.BlocksExecution);
        Assert.Equal("revision_plan_optional", resolution.MissingPrerequisite);
        Assert.Equal("TOOL_INPUT_ARTIFACT_STALE", resolution.FailureCode);
        var stale = Assert.Single(resolution.InputArtifacts, artifact => artifact.ArtifactName == "tianming_package");
        Assert.Equal("stale", stale.Status);
        Assert.Equal("pkg-stale-1", stale.ArtifactId);
        Assert.True(stale.BlocksExecution);
    }

    [Fact]
    public async Task ResolveAsync_ProduceChapterBlocksWhenPreviousPostCommitOutboxIsPending()
    {
        await using var db = CreateDb();
        SeedProjectWithCommittedChapters(db);
        db.OutboxEvents.Add(PostCommitOutbox("outbox-finalize-001", "pending"));
        await db.SaveChangesAsync();
        var resolver = new ToolInputArtifactResolver(db);

        var resolution = await resolver.ResolveAsync(new ToolInputArtifactResolutionRequest(
            ProduceChapterTool(),
            new AgentToolCall
            {
                Name = "ProduceChapter",
                Arguments = new Dictionary<string, string>
                {
                    ["runId"] = "run-next"
                }
            },
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1"
            },
            StoryBibleWithChapterRun("run-next", "chapter-002"),
            "project-1"));

        Assert.True(resolution.BlocksExecution);
        Assert.Equal("previous_chapter_post_commit_outbox", resolution.MissingPrerequisite);
        Assert.Equal("TOOL_INPUT_ARTIFACT_BLOCKED", resolution.FailureCode);
        var outbox = Assert.Single(resolution.InputArtifacts, artifact => artifact.ArtifactName == "post_commit_outbox");
        Assert.Equal("pending", outbox.Status);
        Assert.Equal("outbox-finalize-001", outbox.ArtifactId);
        Assert.True(outbox.BlocksExecution);
        Assert.Contains("RetryProductionOutbox", outbox.RecommendedActions);
    }

    [Fact]
    public async Task ResolveAsync_ProduceChapterDoesNotBlockWhenPreviousPostCommitOutboxCompleted()
    {
        await using var db = CreateDb();
        SeedProjectWithCommittedChapters(db);
        db.OutboxEvents.Add(PostCommitOutbox("outbox-finalize-001", "completed"));
        await db.SaveChangesAsync();
        var resolver = new ToolInputArtifactResolver(db);

        var resolution = await resolver.ResolveAsync(new ToolInputArtifactResolutionRequest(
            ProduceChapterTool(),
            new AgentToolCall
            {
                Name = "ProduceChapter",
                Arguments = new Dictionary<string, string>
                {
                    ["runId"] = "run-next"
                }
            },
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1"
            },
            StoryBibleWithChapterRun("run-next", "chapter-002"),
            "project-1"));

        Assert.False(resolution.BlocksExecution);
    }

    [Fact]
    public async Task ResolveAsync_ProduceChapterWithMissingRevisionPlan_BlocksExecution()
    {
        await using var db = CreateDb();
        db.TianmingPackages.Add(StalePackage());
        await db.SaveChangesAsync();
        var resolver = new ToolInputArtifactResolver(db);

        var resolution = await resolver.ResolveAsync(new ToolInputArtifactResolutionRequest(
            ProduceChapterTool(),
            ProduceChapterCallWithRevisionPlan("revision-plan-missing"),
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1"
            },
            StoryBibleWithChapterRun("run-stale", "chapter-002"),
            "project-1"));

        Assert.True(resolution.BlocksExecution);
        Assert.Equal("revision_plan", resolution.MissingPrerequisite);
        Assert.Equal("TOOL_INPUT_ARTIFACT_INVALID", resolution.FailureCode);
        Assert.Contains("revision-plan-missing", resolution.Reason);
    }

    [Fact]
    public async Task ResolveAsync_ProduceChapterWithRevisionPlanFromOtherProject_BlocksExecution()
    {
        await using var db = CreateDb();
        db.TianmingPackages.Add(StalePackage());
        db.RevisionPlans.Add(ReadyRevisionPlan(projectId: "project-other"));
        await db.SaveChangesAsync();
        var resolver = new ToolInputArtifactResolver(db);

        var resolution = await resolver.ResolveAsync(new ToolInputArtifactResolutionRequest(
            ProduceChapterTool(),
            ProduceChapterCallWithRevisionPlan("revision-plan-1"),
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1"
            },
            StoryBibleWithChapterRun("run-stale", "chapter-002"),
            "project-1"));

        Assert.True(resolution.BlocksExecution);
        Assert.Equal("revision_plan", resolution.MissingPrerequisite);
        Assert.Equal("TOOL_INPUT_ARTIFACT_INVALID", resolution.FailureCode);
        Assert.Contains("不属于当前项目", resolution.Reason);
    }

    [Fact]
    public async Task ResolveAsync_ProduceChapterWithDraftRevisionPlan_BlocksExecution()
    {
        await using var db = CreateDb();
        db.TianmingPackages.Add(StalePackage());
        db.RevisionPlans.Add(ReadyRevisionPlan(status: "draft"));
        await db.SaveChangesAsync();
        var resolver = new ToolInputArtifactResolver(db);

        var resolution = await resolver.ResolveAsync(new ToolInputArtifactResolutionRequest(
            ProduceChapterTool(),
            ProduceChapterCallWithRevisionPlan("revision-plan-1"),
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1"
            },
            StoryBibleWithChapterRun("run-stale", "chapter-002"),
            "project-1"));

        Assert.True(resolution.BlocksExecution);
        Assert.Equal("revision_plan", resolution.MissingPrerequisite);
        Assert.Equal("TOOL_INPUT_ARTIFACT_INVALID", resolution.FailureCode);
        Assert.Contains("ready_for_rebuild", resolution.Reason);
    }

    [Fact]
    public async Task ResolveAsync_ProduceChapterWithRevisionPlanForDifferentChapter_BlocksExecution()
    {
        await using var db = CreateDb();
        db.TianmingPackages.Add(StalePackage());
        db.RevisionPlans.Add(ReadyRevisionPlan(targetChapterId: "chapter-003"));
        await db.SaveChangesAsync();
        var resolver = new ToolInputArtifactResolver(db);

        var resolution = await resolver.ResolveAsync(new ToolInputArtifactResolutionRequest(
            ProduceChapterTool(),
            ProduceChapterCallWithRevisionPlan("revision-plan-1"),
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1"
            },
            StoryBibleWithChapterRun("run-stale", "chapter-002"),
            "project-1"));

        Assert.True(resolution.BlocksExecution);
        Assert.Equal("revision_plan", resolution.MissingPrerequisite);
        Assert.Equal("TOOL_INPUT_ARTIFACT_INVALID", resolution.FailureCode);
        Assert.Contains("chapter-002", resolution.Reason);
    }

    [Fact]
    public async Task ResolveAsync_ProduceChapterWithValidRevisionPlan_AllowsExecution()
    {
        await using var db = CreateDb();
        db.TianmingPackages.Add(StalePackage());
        db.RevisionPlans.Add(ReadyRevisionPlan());
        await db.SaveChangesAsync();
        var resolver = new ToolInputArtifactResolver(db);

        var resolution = await resolver.ResolveAsync(new ToolInputArtifactResolutionRequest(
            ProduceChapterTool(),
            ProduceChapterCallWithRevisionPlan("revision-plan-1"),
            new AgentSession
            {
                UserId = "user-1",
                SessionId = "session-1",
                ActiveProjectId = "project-1"
            },
            StoryBibleWithChapterRun("run-stale", "chapter-002"),
            "project-1"));

        Assert.False(resolution.BlocksExecution);
        Assert.Empty(resolution.MissingPrerequisite);
        Assert.Empty(resolution.FailureCode);
    }

    [Fact]
    public async Task ResolveAsync_ReadOnlyTool_DoesNotBlock()
    {
        await using var db = CreateDb();
        var resolver = new ToolInputArtifactResolver(db);

        var resolution = await resolver.ResolveAsync(new ToolInputArtifactResolutionRequest(
            new AgentToolDefinition
            {
                Name = "QueryNovelProductionState",
                Semantic = new AgentToolSemanticSpec
                {
                    InputArtifacts = { "production_state_query" }
                }
            },
            new AgentToolCall { Name = "QueryNovelProductionState" },
            new AgentSession { UserId = "user-1", SessionId = "session-1" },
            new StoryBibleDocument(),
            string.Empty));

        Assert.False(resolution.BlocksExecution);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static AgentToolDefinition ProduceChapterTool() => new()
    {
        Name = "ProduceChapter",
        Risk = "High",
        Semantic = new AgentToolSemanticSpec
        {
            InputArtifacts = { "chapter_plan_run", "continuity_pack", "knowledge_binding_snapshot" }
        }
    };

    private static AgentToolCall ProduceChapterCallWithRevisionPlan(string revisionPlanId) => new()
    {
        Name = "ProduceChapter",
        Arguments = new Dictionary<string, string>
        {
            ["runId"] = "run-stale",
            ["revisionPlanId"] = revisionPlanId
        }
    };

    private static TianmingPackage StalePackage() => new()
    {
        Id = "pkg-stale-1",
        UserId = "user-1",
        ProjectId = "project-1",
        RuntimeRunId = "run-stale",
        ChapterId = "chapter-002",
        Status = "stale",
        InputJson = "{}",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static RevisionPlan ReadyRevisionPlan(
        string projectId = "project-1",
        string status = "ready_for_rebuild",
        string targetChapterId = "chapter-002") => new()
    {
        Id = "revision-plan-1",
        UserId = "user-1",
        ProjectId = projectId,
        Source = "user_request",
        PlanType = "chapter_rewrite",
        TargetScope = "chapter",
        TargetChapterId = targetChapterId,
        Status = status,
        RequirementsJson = "[\"重写章节\"]",
        ContinuityRequirementsJson = "[]",
        ImpactAnalysisJson = "{}",
        AffectedChapterIdsJson = $"[\"{targetChapterId}\"]",
        InvalidatedPackageIdsJson = "[\"pkg-stale-1\"]",
        RiskLevel = "medium",
        Recommendation = "按用户修订要求重写本章。",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static StoryBibleDocument StoryBibleWithChapterRun(string runId, string chapterId) => new()
    {
        AgentRuns =
        {
            new NovelAgentRun
            {
                RunId = runId,
                Intent = NovelAgentIntent.PlanChapter,
                TargetChapterId = chapterId
            }
        }
    };

    private static void SeedProjectWithCommittedChapters(NovelAgentDbContext db)
    {
        db.Users.Add(new User
        {
            Id = "user-1",
            Username = "author",
            Email = "author@example.com",
            PasswordHash = "hash",
            Role = "author"
        });
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-1",
            UserId = "user-1",
            Title = "Artifact Resolver 依赖测试书",
            Status = "Writing",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.Chapters.AddRange(
            new Chapter
            {
                Id = "project-1-chapter-001",
                ProjectId = "project-1",
                Title = "第一章",
                ChapterNumber = 1,
                Status = "committed",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Chapter
            {
                Id = "project-1-chapter-002",
                ProjectId = "project-1",
                Title = "第二章",
                ChapterNumber = 2,
                Status = "planned",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
    }

    private static OutboxEvent PostCommitOutbox(string id, string status) => new()
    {
        Id = id,
        UserId = "user-1",
        ProjectId = "project-1",
        RuntimeRunId = "run-001",
        EventType = "finalize_chapter_commit_metadata",
        AggregateType = "chapter",
        AggregateId = "project-1-chapter-001",
        Status = status,
        PayloadJson = "{}",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
