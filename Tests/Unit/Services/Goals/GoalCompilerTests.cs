using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Goals;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Goals;

public sealed class GoalCompilerTests
{
    [Fact]
    public async Task CompileAsync_BuildsRequiredBatchSkeletonAndPersistsVersionedGraph()
    {
        await using var db = CreateDb();
        await SeedGoalAsync(db);
        var compiler = CreateCompiler(db);

        var result = await compiler.CompileAsync("goal-1");

        Assert.Equal(1, result.Version);
        Assert.Contains(result.Nodes, node => node.Id == "freeze-baselines");
        var creativeAnalysis = Assert.Single(result.Nodes.Where(node => node.Id == "analyze-creative-requirements"));
        Assert.Equal("CreativeRequirements", Assert.Single(creativeAnalysis.ProducesArtifacts));
        var batchPlan = Assert.Single(result.Nodes.Where(node => node.Id == "compile-batch-plan"));
        Assert.Contains("analyze-creative-requirements", batchPlan.DependsOn);
        Assert.Contains("CreativeRequirements", batchPlan.RequiresArtifacts);
        foreach (var chapter in Enumerable.Range(1, 3))
        {
            Assert.Contains(result.Nodes, node => node.Id == $"chapter-{chapter}-plan");
            Assert.Contains(result.Nodes, node => node.Id == $"chapter-{chapter}-context");
            Assert.Contains(result.Nodes, node => node.Id == $"chapter-{chapter}-write");
            Assert.Contains(result.Nodes, node => node.Id == $"chapter-{chapter}-continuity-review");
            Assert.Contains(result.Nodes, node => node.Id == $"chapter-{chapter}-literary-review");
            Assert.Contains(result.Nodes, node => node.Id == $"chapter-{chapter}-directed-rework");
            Assert.Contains(result.Nodes, node => node.Id == $"chapter-{chapter}-continuity-summary");
        }
        Assert.Contains(result.Nodes, node => node.Id == "batch-impact-analysis");
        Assert.Contains(result.Nodes, node => node.Id == "user-acceptance");
        Assert.Contains(result.Nodes, node => node.Id == "prefix-merge");
        Assert.Single(await db.TaskGraphVersions.ToListAsync());
        Assert.Equal(result.Nodes.Count, await db.KernelTasks.CountAsync());
        var branch = Assert.Single(await db.CanonBranches.ToListAsync());
        Assert.Equal((1, 3), (branch.StartChapterNumber, branch.EndChapterNumber));
        Assert.Equal([1, 2, 3], await db.Chapters.OrderBy(chapter => chapter.ChapterNumber)
            .Select(chapter => chapter.ChapterNumber).ToArrayAsync());
        Assert.All(await db.KernelTasks.Where(task => task.Id.Contains(":chapter-")).ToListAsync(),
            task => Assert.Equal(branch.Id, task.BranchId));

        var directedRework = result.Nodes.Single(node => node.Id == "chapter-1-directed-rework");
        Assert.Contains("chapter-1-context", directedRework.DependsOn);
        Assert.Contains("chapter-1-write", directedRework.DependsOn);
        Assert.Contains("ChapterContextContract", directedRework.RequiresArtifacts);
        Assert.Contains("CandidateChapterDraft", directedRework.RequiresArtifacts);
    }

    [Fact]
    public void Validator_RejectsCyclesMissingArtifactsUnauthorizedWritesAndMissingReviews()
    {
        var validator = new TaskGraphValidator();

        var cycle = new TaskGraphDefinition("goal", 1,
        [
            Node("a", ["b"]),
            Node("b", ["a"])
        ]);
        Assert.Contains(validator.Validate(cycle), error => error.Code == "TASK_GRAPH_CYCLE");

        var missingArtifact = new TaskGraphDefinition("goal", 1,
        [
            Node("root", [], produces: ["OtherArtifact"]),
            Node("consumer", ["root"], requires: ["RequiredArtifact"])
        ]);
        Assert.Contains(validator.Validate(missingArtifact), error => error.Code == "TASK_GRAPH_MISSING_ARTIFACT");

        var unauthorized = new TaskGraphDefinition("goal", 1,
        [
            Node("kernel-write", [], authorityMutation: AuthorityMutation.CommitCanon)
        ]);
        Assert.Contains(validator.Validate(unauthorized), error => error.Code == "KERNEL_AUTHORITY_WRITE_FORBIDDEN");

        var missingReview = new TaskGraphDefinition("goal", 1,
        [
            Node("chapter-1-write", [], taskType: "WriteCandidate", produces: ["CandidateChapterDraft"]),
            Node("user-acceptance", ["chapter-1-write"], taskType: "UserAcceptance")
        ]);
        Assert.Contains(validator.Validate(missingReview), error => error.Code == "CHAPTER_REVIEW_REQUIRED");
    }

    [Fact]
    public async Task CompileAsync_IncrementsGraphVersionWithoutMutatingPreviousGraph()
    {
        await using var db = CreateDb();
        await SeedGoalAsync(db);
        var compiler = CreateCompiler(db);
        var first = await compiler.CompileAsync("goal-1");
        var firstJson = (await db.TaskGraphVersions.AsNoTracking().SingleAsync()).GraphJson;

        var second = await compiler.CompileAsync("goal-1");

        Assert.Equal(2, second.Version);
        Assert.Equal(2, await db.TaskGraphVersions.CountAsync());
        Assert.Equal(firstJson, (await db.TaskGraphVersions.AsNoTracking()
            .SingleAsync(graph => graph.Version == first.Version)).GraphJson);
    }

    [Fact]
    public async Task CompileAsync_SupersedesPreviousGraphAndCancelsItsNonTerminalTasks()
    {
        await using var db = CreateDb();
        await SeedGoalAsync(db);
        var compiler = CreateCompiler(db);
        await compiler.CompileAsync("goal-1");
        var previousGraph = await db.TaskGraphVersions.SingleAsync();
        var previousTasks = await db.KernelTasks
            .Where(task => task.TaskGraphVersionId == previousGraph.Id)
            .ToListAsync();
        previousTasks[0].Status = "completed";
        previousTasks[1].Status = "running";
        previousTasks[1].LeaseOwner = "worker-1";
        previousTasks[1].LeaseExpiresAt = DateTime.UtcNow.AddMinutes(2);
        await db.SaveChangesAsync();

        await compiler.CompileAsync("goal-1");

        Assert.Equal("superseded", previousGraph.Status);
        Assert.Equal("completed", previousTasks[0].Status);
        Assert.All(previousTasks.Skip(1), task => Assert.Equal("cancelled", task.Status));
        Assert.Null(previousTasks[1].LeaseOwner);
        Assert.Null(previousTasks[1].LeaseExpiresAt);
        var activeGraph = await db.TaskGraphVersions.SingleAsync(graph => graph.Status == "active");
        Assert.Equal(2, activeGraph.Version);
    }

    [Fact]
    public async Task RecompileForRevision_ReusesOnlyTasksOutsideAffectedDescendantSubgraph()
    {
        await using var db = CreateDb();
        await SeedGoalAsync(db);
        var compiler = CreateCompiler(db);
        await compiler.CompileAsync("goal-1");
        var firstGraph = await db.TaskGraphVersions.SingleAsync();
        var reusableTask = await db.KernelTasks.SingleAsync(task =>
            task.TaskGraphVersionId == firstGraph.Id && task.Id.EndsWith(":chapter-1-plan"));
        reusableTask.Status = "completed";
        reusableTask.OutputArtifactIdsJson = "[\"artifact-chapter-1-plan\"]";
        db.GoalRevisions.Add(new GoalRevision
        {
            Id = "revision-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            RevisionNumber = 1,
            Reason = "修改第二章冲突",
            ConstraintChangesJson = "{}"
        });
        await db.SaveChangesAsync();

        var revised = await compiler.RecompileForRevisionAsync(
            "goal-1",
            "revision-1",
            ["chapter-2-write"]);

        var revisedGraph = await db.TaskGraphVersions.SingleAsync(graph => graph.Version == revised.Version);
        var revisedTasks = await db.KernelTasks
            .Where(task => task.TaskGraphVersionId == revisedGraph.Id)
            .ToListAsync();
        var reused = revisedTasks.Single(task => task.Id.EndsWith(":chapter-1-plan"));
        var invalidated = revisedTasks.Single(task => task.Id.EndsWith(":chapter-2-write"));
        var affectedLater = revisedTasks.Single(task => task.Id.EndsWith(":chapter-3-plan"));
        Assert.Equal("reused", reused.Status);
        Assert.Equal("[\"artifact-chapter-1-plan\"]", reused.OutputArtifactIdsJson);
        Assert.NotEqual("reused", invalidated.Status);
        Assert.NotEqual("reused", affectedLater.Status);
        Assert.Equal("revision-1", revisedGraph.GoalRevisionId);
    }

    [Fact]
    public async Task RecompileForRevision_DoesNotExpandTheActiveProductionBatch()
    {
        await using var db = CreateDb();
        await SeedGoalAsync(db);
        var compiler = CreateCompiler(db);
        await compiler.CompileAsync("goal-1");
        db.GoalRevisions.Add(new GoalRevision
        {
            Id = "revision-expand-range",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            RevisionNumber = 1,
            Reason = "增加第四章",
            ConstraintChangesJson = "{\"targetChapterRangeJson\":\"{\\\"start\\\":1,\\\"end\\\":4}\"}",
            AffectedNodeIdsJson = "[\"chapter-3-plan\"]"
        });
        await db.SaveChangesAsync();

        var revised = await compiler.RecompileForRevisionAsync(
            "goal-1",
            "revision-expand-range",
            ["chapter-3-plan"]);

        Assert.DoesNotContain(revised.Nodes, node => node.Id == "chapter-4-write");
        Assert.Contains(revised.Nodes, node => node.Id == "chapter-3-write");
        Assert.Equal("{\"start\":1,\"end\":3}", (await db.CreativeGoals.SingleAsync()).TargetChapterRangeJson);
    }

    [Fact]
    public void CreativeGoalRevisionProjector_AppliesFrozenRevisionSequenceWithoutMutatingCommittedGoal()
    {
        var committed = new CreativeGoal
        {
            Id = "goal-1",
            UserId = "user-1",
            ProjectId = "project-1",
            HumanReadableObjective = "原目标",
            TargetChapterRangeJson = "{\"start\":1,\"end\":1}",
            TotalCostLimit = 10
        };
        var revisions = new[]
        {
            new GoalRevision
            {
                RevisionNumber = 2,
                ConstraintChangesJson = "{\"mustHappen\":[\"第二版约束\"]}"
            },
            new GoalRevision
            {
                RevisionNumber = 1,
                ConstraintChangesJson = "{\"humanReadableObjective\":\"修订目标\",\"totalCostLimit\":20}"
            }
        };

        var effective = CreativeGoalRevisionProjector.Project(committed, revisions);

        Assert.Equal("修订目标", effective.HumanReadableObjective);
        Assert.Equal(20, effective.TotalCostLimit);
        Assert.Equal(["第二版约束"], JsonSerializer.Deserialize<string[]>(effective.MustHappenJson));
        Assert.Equal("原目标", committed.HumanReadableObjective);
        Assert.Equal(10, committed.TotalCostLimit);
    }

    private static TaskGraphNode Node(
        string id,
        IReadOnlyList<string> dependencies,
        string taskType = "Test",
        IReadOnlyList<string>? requires = null,
        IReadOnlyList<string>? produces = null,
        AuthorityMutation authorityMutation = AuthorityMutation.None) => new(
            id,
            taskType,
            "test_kernel",
            TaskExecutionKind.Kernel,
            dependencies,
            requires ?? [],
            produces ?? [],
            authorityMutation,
            null);

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static async Task SeedGoalAsync(NovelAgentDbContext db)
    {
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-1",
            UserId = "user-1",
            Title = "测试小说",
            Genre = "悬疑",
            Status = "draft"
        });
        db.CreativeGoals.Add(new CreativeGoal
        {
            Id = "goal-1",
            UserId = "user-1",
            ProjectId = "project-1",
            SourceSessionId = "session-1",
            GoalType = "write_batch",
            CollaborationMode = "coauthor",
            HumanReadableObjective = "写前三章",
            TargetChapterRangeJson = "{\"start\":1,\"end\":3}",
            TotalCostLimit = 20,
            CanonBaselineVersion = "canon-1",
            KnowledgeSnapshotVersion = "knowledge-1",
            QualityContractVersion = "quality-1",
            StyleProfileVersion = "style-1",
            IdempotencyKey = "goal-1"
        });
        db.BookProductions.Add(new BookProduction
        {
            Id = "production-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            TargetStartChapterNumber = 1,
            TargetEndChapterNumber = 3,
            NextChapterNumber = 1,
            BatchSize = 3,
            CurrentBatchNumber = 1
        });
        db.ProductionBatches.Add(new ProductionBatch
        {
            Id = "batch-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            BookProductionId = "production-1",
            BatchNumber = 1,
            StartChapterNumber = 1,
            EndChapterNumber = 3
        });
        await db.SaveChangesAsync();
    }

    private static GoalCompiler CreateCompiler(NovelAgentDbContext db)
    {
        var currentUser = new StubCurrentUserService("user-1");
        var productions = new BookProductionService(db, currentUser, new PassingBookValidationService());
        return new GoalCompiler(db, currentUser, new TaskGraphValidator(), productions);
    }

    private sealed class PassingBookValidationService : IBookValidationService
    {
        public Task<BookValidationReport> ValidateAsync(BookValidationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BookValidationReport { OverallStatus = "validated" });
    }

    private sealed class StubCurrentUserService(string userId) : ICurrentUserService
    {
        public string GetUserId() => userId;
        public string GetUsername() => "author";
        public string GetEmail() => "author@example.com";
        public string GetRole() => "author";
        public bool IsAdmin() => false;
        public bool IsAuthenticated() => true;
        public string? TryGetUserId() => userId;
    }
}
