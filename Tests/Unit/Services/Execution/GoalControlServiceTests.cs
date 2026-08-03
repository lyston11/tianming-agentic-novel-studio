using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Moq;
using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Canon;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.DomainEvents;
using TM.Web.NovelAgentWeb.Services.Execution;
using TM.Web.NovelAgentWeb.Services.Goals;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using Xunit;

namespace Tests.Unit.Services.Execution;

public sealed class GoalControlServiceTests
{
    [Fact]
    public async Task PauseAtSafePoint_PersistsUnadoptedArtifactAndDoesNotAdvanceTask()
    {
        await using var db = CreateDb();
        SeedGoalBranchAndTask(db, "goal-pause", "branch-pause", "task-pause");
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var status = await service.RequestPauseAsync("user-1", "goal-pause");

        Assert.Equal("pause_requested", status);
        var claim = Claim("goal-pause", "branch-pause", "task-pause");

        var result = await service.ReachSafePointAsync(
            claim,
            [new KernelArtifactProposal("ChapterDraft", 1, "{\"text\":\"候选正文\"}", "hash-1", "agent", false)]);

        Assert.Equal(GoalSafePointDisposition.Paused, result.Disposition);
        var artifact = Assert.Single(await db.KernelArtifacts.ToListAsync());
        Assert.Equal("unadopted", artifact.Status);
        var task = await db.KernelTasks.SingleAsync();
        Assert.Equal("paused", task.Status);
        Assert.Null(task.LeaseOwner);
        Assert.Empty(await db.DomainEvents.ToListAsync());
        Assert.Equal("paused", (await db.CreativeGoals.SingleAsync()).Status);
    }

    [Fact]
    public async Task RequestPauseAsync_WithoutRunningTask_PausesImmediately()
    {
        await using var db = CreateDb();
        SeedGoalBranchAndTask(db, "goal-quiescent", "branch-quiescent", "task-quiescent");
        var task = db.KernelTasks.Local.Single();
        task.Status = "ready";
        task.LeaseOwner = null;
        task.LeaseExpiresAt = null;
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var status = await service.RequestPauseAsync("user-1", "goal-quiescent");

        Assert.Equal("paused", status);
        Assert.Equal("paused", (await db.CreativeGoals.SingleAsync()).Status);
        Assert.Equal("paused", (await db.KernelTasks.SingleAsync()).Status);
    }

    [Fact]
    public async Task RequestPauseAsync_ExistingRequestWithoutRunningTask_ReconcilesToPaused()
    {
        await using var db = CreateDb();
        SeedGoalBranchAndTask(db, "goal-reconcile", "branch-reconcile", "task-reconcile");
        var task = db.KernelTasks.Local.Single();
        task.Status = "ready";
        task.LeaseOwner = null;
        task.LeaseExpiresAt = null;
        db.CreativeGoals.Local.Single().Status = "pause_requested";
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var status = await service.RequestPauseAsync("user-1", "goal-reconcile");

        Assert.Equal("paused", status);
        Assert.Equal("paused", (await db.CreativeGoals.SingleAsync()).Status);
        Assert.Equal("paused", (await db.KernelTasks.SingleAsync()).Status);
    }

    [Fact]
    public async Task BudgetExceededAtSafePoint_PersistsInFlightResultWithoutAdoptingIt()
    {
        await using var db = CreateDb();
        SeedGoalBranchAndTask(db, "goal-budget", "branch-budget", "task-budget");
        await db.SaveChangesAsync();
        var goal = await db.CreativeGoals.SingleAsync();
        goal.Status = "budget_exceeded";
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.ReachSafePointAsync(
            Claim("goal-budget", "branch-budget", "task-budget"),
            [new KernelArtifactProposal("ChapterDraft", 1, "{\"text\":\"在途结果\"}", "budget-hash", "agent", false)]);

        Assert.Equal(GoalSafePointDisposition.BudgetExceeded, result.Disposition);
        Assert.Equal("unadopted", (await db.KernelArtifacts.SingleAsync()).Status);
        Assert.Equal("budget_exceeded", (await db.KernelTasks.SingleAsync()).Status);
        Assert.Empty(await db.DomainEvents.ToListAsync());
    }

    [Fact]
    public async Task CancelAsync_OffersPreserveMergePrefixAndDiscardStrategies()
    {
        await using var db = CreateDb();
        SeedGoalBranchAndTask(db, "goal-preserve", "branch-preserve", "task-preserve");
        SeedGoalBranchAndTask(db, "goal-merge", "branch-merge", "task-merge");
        SeedGoalBranchAndTask(db, "goal-discard", "branch-discard", "task-discard");
        db.CandidateChapters.Add(new CandidateChapter
        {
            Id = "candidate-discard",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-discard",
            BranchId = "branch-discard",
            ChapterId = "chapter-1",
            ChapterNumber = 1,
            CurrentArtifactId = "artifact-discard"
        });
        db.KernelArtifacts.Add(new KernelArtifact
        {
            Id = "artifact-discard",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-discard",
            TaskId = "task-discard",
            BranchId = "branch-discard",
            ArtifactType = "ChapterDraft",
            ContentJson = "{\"text\":\"discard me\"}",
            ContentHash = "discard-hash"
        });
        await db.SaveChangesAsync();
        var prefix = new Mock<IPrefixMergeService>();
        prefix.Setup(service => service.MergeAcceptedPrefixAsync("branch-merge", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BranchMergeRecord { Id = "merge-record", BranchId = "branch-merge" });
        var cache = new Mock<IDistributedCacheService>();
        cache.Setup(service => service.RemoveByPrefixAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var vectors = new Mock<IVectorStore>();
        vectors.Setup(service => service.DeleteVectorsByFilterAsync(
                "user-1",
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var service = new GoalControlService(
            db,
            prefix.Object,
            cache.Object,
            vectors.Object,
            Mock.Of<IContentDocumentService>());

        await service.CancelAsync("user-1", "goal-preserve", GoalCancellationStrategy.PreserveCandidateBranch);
        await service.CancelAsync("user-1", "goal-merge", GoalCancellationStrategy.MergeAcceptedPrefix);
        await service.CancelAsync("user-1", "goal-discard", GoalCancellationStrategy.DiscardCandidateBranch);

        Assert.Equal("preserved", (await db.CanonBranches.SingleAsync(item => item.Id == "branch-preserve")).Status);
        prefix.Verify(service => service.MergeAcceptedPrefixAsync("branch-merge", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("preserved", (await db.CanonBranches.SingleAsync(item => item.Id == "branch-merge")).Status);
        Assert.Equal("discarded", (await db.CanonBranches.SingleAsync(item => item.Id == "branch-discard")).Status);
        Assert.Empty(await db.CandidateChapters.Where(item => item.GoalId == "goal-discard").ToListAsync());
        Assert.Empty(await db.KernelArtifacts.Where(item => item.GoalId == "goal-discard").ToListAsync());
        cache.Verify(service => service.RemoveByPrefixAsync("goal:user-1:goal-discard", It.IsAny<CancellationToken>()), Times.Once);
        vectors.Verify(service => service.DeleteVectorsByFilterAsync(
            "user-1",
            It.Is<Dictionary<string, object>>(filters => Equals(filters["branch_id"], "branch-discard")),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.All(await db.CreativeGoals.ToListAsync(), goal => Assert.Equal("canceled", goal.Status));
        Assert.All(await db.KernelTasks.ToListAsync(), task =>
        {
            Assert.Equal("canceled", task.Status);
            Assert.Null(task.LeaseOwner);
            Assert.Null(task.LeaseExpiresAt);
        });
    }

    [Fact]
    public async Task CancelAsync_MergePrefixRollsBackMergeWhenFinalCancellationWriteFails()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FailingCancellationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new FailingCancellationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await SeedMergeCancellationAsync(db);
        var prefix = new PrefixMergeService(
            db,
            new StubCurrentUserService(),
            new ContentDocumentService(db),
            new NoConflictMergeModel());
        var service = new GoalControlService(
            db,
            prefix,
            Mock.Of<IDistributedCacheService>(),
            Mock.Of<IVectorStore>(),
            new ContentDocumentService(db));
        db.FailCanceledGoalWrites = true;

        await Assert.ThrowsAsync<DbUpdateException>(() => service.CancelAsync(
            "user-1",
            "goal-atomic",
            GoalCancellationStrategy.MergeAcceptedPrefix));

        db.ChangeTracker.Clear();
        Assert.Equal("running", (await db.CreativeGoals.SingleAsync()).Status);
        Assert.Equal("active", (await db.CanonBranches.SingleAsync()).Status);
        Assert.Equal("candidate", (await db.CandidateChapters.SingleAsync()).Status);
        Assert.Equal("ready", (await db.KernelTasks.SingleAsync(task => task.Id == "pending-task")).Status);
        Assert.Empty(await db.BranchMergeRecords.ToListAsync());
        Assert.Empty(await db.KernelArtifacts.Where(artifact => artifact.ArtifactType == "MergeRecord").ToListAsync());
        Assert.Empty(await db.ChapterVersions.ToListAsync());
        Assert.Empty(await db.ContentDocuments.ToListAsync());
    }

    private static GoalControlService CreateService(NovelAgentDbContext db) => new(
        db,
        Mock.Of<IPrefixMergeService>(),
        Mock.Of<IDistributedCacheService>(),
        Mock.Of<IVectorStore>(),
        Mock.Of<IContentDocumentService>());

    private static KernelTaskClaim Claim(string goalId, string branchId, string taskId) => new(
        taskId,
        "user-1",
        "project-1",
        goalId,
        $"graph-{goalId}",
        branchId,
        "tianming_writing",
        "WriteChapter",
        1,
        "worker-1",
        DateTime.UtcNow.AddMinutes(1));

    private static void SeedGoalBranchAndTask(
        NovelAgentDbContext db,
        string goalId,
        string branchId,
        string taskId)
    {
        db.CreativeGoals.Add(new CreativeGoal
        {
            Id = goalId,
            UserId = "user-1",
            ProjectId = "project-1",
            HumanReadableObjective = goalId,
            TotalCostLimit = 10,
            Status = "running",
            IdempotencyKey = goalId
        });
        db.CanonBranches.Add(new CanonBranch
        {
            Id = branchId,
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = goalId,
            Status = "active",
            StartChapterNumber = 1,
            EndChapterNumber = 1
        });
        db.KernelTasks.Add(new KernelTask
        {
            Id = taskId,
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = goalId,
            TaskGraphVersionId = $"graph-{goalId}",
            BranchId = branchId,
            KernelName = "tianming_writing",
            TaskType = "WriteChapter",
            Status = "running",
            LeaseOwner = "worker-1",
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(1),
            MaxAttempts = 2,
            IdempotencyKey = taskId
        });
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static async Task SeedMergeCancellationAsync(NovelAgentDbContext db)
    {
        db.Users.Add(new User
        {
            Id = "user-1",
            Username = "author",
            Email = "author@example.test",
            PasswordHash = "hash",
            Role = "author"
        });
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-1",
            UserId = "user-1",
            Title = "project"
        });
        db.CreativeGoals.Add(new CreativeGoal
        {
            Id = "goal-atomic",
            UserId = "user-1",
            ProjectId = "project-1",
            HumanReadableObjective = "write",
            TotalCostLimit = 10,
            Status = "running",
            IdempotencyKey = "goal-atomic"
        });
        db.CanonBranches.Add(new CanonBranch
        {
            Id = "branch-atomic",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-atomic",
            CanonBaselineVersion = "canon-1",
            Status = "active",
            StartChapterNumber = 1,
            EndChapterNumber = 1
        });
        db.Chapters.Add(new Chapter
        {
            Id = "chapter-1",
            ProjectId = "project-1",
            ChapterNumber = 1,
            Title = "chapter"
        });
        db.KernelArtifacts.Add(new KernelArtifact
        {
            Id = "artifact-atomic",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-atomic",
            TaskId = "write-task",
            BranchId = "branch-atomic",
            ArtifactType = "CandidateChapterDraft",
            ContentJson = JsonSerializer.Serialize(new ChapterDraftArtifact
            {
                ArtifactId = "draft-atomic",
                ChapterId = "chapter-1",
                DraftContent = "候选正文"
            }),
            ContentHash = "artifact-hash"
        });
        db.CandidateChapters.Add(new CandidateChapter
        {
            Id = "candidate-atomic",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-atomic",
            BranchId = "branch-atomic",
            ChapterId = "chapter-1",
            ChapterNumber = 1,
            Version = 1,
            CurrentArtifactId = "artifact-atomic",
            Status = "candidate"
        });
        db.CandidateAcceptances.Add(new CandidateAcceptance
        {
            Id = "acceptance-atomic",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-atomic",
            BranchId = "branch-atomic",
            CandidateChapterId = "candidate-atomic",
            CandidateVersion = 1,
            Decision = "accepted",
            DecidedByUserId = "user-1"
        });
        db.KernelArtifacts.Add(new KernelArtifact
        {
            Id = "acceptance-decision:acceptance-atomic",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-atomic",
            TaskId = "graph-atomic:acceptance-gate",
            BranchId = "branch-atomic",
            ArtifactType = "AcceptanceDecision",
            ContentJson = "{}",
            ContentHash = "acceptance-hash",
            Status = "adopted",
            Authorship = "human"
        });
        db.TaskGraphVersions.Add(new TaskGraphVersion
        {
            Id = "graph-atomic",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-atomic",
            Version = 1,
            Status = "active",
            GraphJson = "{}",
            ContentHash = "graph-hash"
        });
        db.BookProductions.Add(new BookProduction
        {
            Id = "production-atomic",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-atomic",
            ExecutionStrategy = "interactive_batch",
            Status = "running",
            TargetStartChapterNumber = 1,
            TargetEndChapterNumber = 1,
            NextChapterNumber = 1,
            BatchSize = 1,
            CurrentBatchNumber = 1
        });
        db.ProductionBatches.Add(new ProductionBatch
        {
            Id = "batch-atomic",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-atomic",
            BookProductionId = "production-atomic",
            BatchNumber = 1,
            StartChapterNumber = 1,
            EndChapterNumber = 1,
            Status = "accepting",
            AcceptanceActor = "user",
            TaskGraphVersionId = "graph-atomic",
            CanonBranchId = "branch-atomic"
        });
        db.KernelTasks.AddRange(
        new KernelTask
        {
            Id = "pending-task",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-atomic",
            TaskGraphVersionId = "graph-atomic",
            BranchId = "branch-atomic",
            KernelName = "test",
            TaskType = "test",
            Status = "ready",
            IdempotencyKey = "pending-task"
        },
        new KernelTask
        {
            Id = "graph-atomic:prefix-merge",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-atomic",
            TaskGraphVersionId = "graph-atomic",
            BranchId = "branch-atomic",
            KernelName = "domain_reducer",
            TaskType = "PrefixMerge",
            Status = "blocked",
            IdempotencyKey = "graph-atomic:prefix-merge"
        });
        await db.SaveChangesAsync();
    }

    private sealed class FailingCancellationDbContext(DbContextOptions<FailingCancellationDbContext> options)
        : NovelAgentDbContext(options)
    {
        public bool FailCanceledGoalWrites { get; set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (FailCanceledGoalWrites && ChangeTracker.Entries<CreativeGoal>().Any(entry =>
                    entry.State == EntityState.Modified && entry.Entity.Status == "canceled"))
            {
                throw new DbUpdateException("injected cancellation failure");
            }
            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class StubCurrentUserService : ICurrentUserService
    {
        public string GetUserId() => "user-1";
        public string GetUsername() => "author";
        public string GetEmail() => "author@example.test";
        public string GetRole() => "author";
        public bool IsAdmin() => false;
        public bool IsAuthenticated() => true;
        public string? TryGetUserId() => "user-1";
    }

    private sealed class NoConflictMergeModel : ICanonMergeConflictModelClient
    {
        public Task<CanonMergeConflictReview> ReviewAsync(
            CanonMergeConflictReviewRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CanonMergeConflictReview.NoConflict());
    }
}
