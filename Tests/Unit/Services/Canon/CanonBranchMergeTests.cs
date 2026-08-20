using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Canon;
using TM.Web.NovelAgentWeb.Services.Content;
using Tests.Unit.Support;
using Xunit;

namespace Tests.Unit.Services.Canon;

public sealed class CanonBranchMergeTests
{
    [Fact]
    public async Task Accept_PersistsAcceptanceDecisionArtifactForWorkflowEvidence()
    {
        await using var db = CreateDb();
        await SeedProjectAsync(db);
        var currentUser = new StubCurrentUserService("user-1");
        var branches = new CanonBranchService(db, currentUser, new LegacyControlPlaneCommandTestDouble(db));
        var branch = await branches.CreateAsync("goal-1", 1, 1);
        await AddManualWorkflowTasksAsync(db, branch.Id);
        var candidate = await AddCandidateAsync(db, branches, branch.Id, 1, null);

        var acceptance = await branches.AcceptAsync(candidate.Id, candidate.Version);

        var artifact = await db.KernelArtifacts.SingleAsync(item => item.ArtifactType == "AcceptanceDecision");
        Assert.Equal("user-1", artifact.UserId);
        Assert.Equal("project-1", artifact.ProjectId);
        Assert.Equal("goal-1", artifact.GoalId);
        Assert.Equal(branch.Id, artifact.BranchId);
        Assert.Equal("graph-1:acceptance-gate", artifact.TaskId);
        Assert.Equal("adopted", artifact.Status);
        Assert.Equal("human", artifact.Authorship);
        using var content = JsonDocument.Parse(artifact.ContentJson);
        Assert.Equal(acceptance.Id, content.RootElement.GetProperty("acceptanceId").GetString());
        Assert.Equal(candidate.Id, content.RootElement.GetProperty("candidateChapterId").GetString());
        Assert.Equal(candidate.Version, content.RootElement.GetProperty("candidateVersion").GetInt32());
        Assert.Equal("user-1", content.RootElement.GetProperty("decidedByUserId").GetString());
    }

    [Fact]
    public async Task PrefixMerge_PersistsMergeRecordArtifactWithAcceptanceEvidence()
    {
        await using var db = CreateDb();
        await SeedProjectAsync(db);
        var currentUser = new StubCurrentUserService("user-1");
        var branches = new CanonBranchService(db, currentUser, new LegacyControlPlaneCommandTestDouble(db));
        var merger = CreateMerger(db, currentUser);
        var branch = await branches.CreateAsync("goal-1", 1, 1);
        await AddManualWorkflowTasksAsync(db, branch.Id);
        var candidate = await AddCandidateAsync(db, branches, branch.Id, 1, null);
        await branches.AcceptAsync(candidate.Id, candidate.Version);
        var acceptanceArtifact = await db.KernelArtifacts.SingleAsync(item => item.ArtifactType == "AcceptanceDecision");

        var merge = await merger.MergeAcceptedPrefixAsync(branch.Id);

        var artifact = await db.KernelArtifacts.SingleAsync(item => item.ArtifactType == "MergeRecord");
        Assert.Equal("user-1", artifact.UserId);
        Assert.Equal("project-1", artifact.ProjectId);
        Assert.Equal("goal-1", artifact.GoalId);
        Assert.Equal(branch.Id, artifact.BranchId);
        Assert.Equal("graph-1:prefix-merge", artifact.TaskId);
        Assert.Equal("adopted", artifact.Status);
        Assert.Equal("human", artifact.Authorship);
        using var content = JsonDocument.Parse(artifact.ContentJson);
        Assert.Equal(merge.Id, content.RootElement.GetProperty("mergeRecordId").GetString());
        Assert.Equal(1, content.RootElement.GetProperty("startChapterNumber").GetInt32());
        Assert.Equal(1, content.RootElement.GetProperty("endChapterNumber").GetInt32());
        Assert.Equal("canon-1", content.RootElement.GetProperty("previousCanonVersion").GetString());
        Assert.Equal(merge.NewCanonVersion, content.RootElement.GetProperty("newCanonVersion").GetString());
        Assert.Equal("user-1", content.RootElement.GetProperty("mergedByUserId").GetString());
        Assert.Contains(
            acceptanceArtifact.Id,
            content.RootElement.GetProperty("acceptanceDecisionArtifactIds")
                .EnumerateArray()
                .Select(item => item.GetString()));
        Assert.Equal(
            candidate.Version,
            content.RootElement.GetProperty("candidateVersions").GetProperty("1").GetInt32());
    }

    [Fact]
    public async Task PrefixMerge_WhenFullyMergedRequestIsRetried_ReturnsOriginalEvidenceWithoutDuplicateWrites()
    {
        await using var db = CreateDb();
        await SeedProjectAsync(db);
        var currentUser = new StubCurrentUserService("user-1");
        var branches = new CanonBranchService(db, currentUser, new LegacyControlPlaneCommandTestDouble(db));
        var merger = CreateMerger(db, currentUser);
        var branch = await branches.CreateAsync("goal-1", 1, 1);
        await AddManualWorkflowTasksAsync(db, branch.Id);
        var candidate = await AddCandidateAsync(db, branches, branch.Id, 1, null);
        await branches.AcceptAsync(candidate.Id, candidate.Version);
        var first = await merger.MergeAcceptedPrefixAsync(branch.Id);

        var retry = await merger.MergeAcceptedPrefixAsync(branch.Id);

        Assert.Equal(first.Id, retry.Id);
        Assert.Single(await db.BranchMergeRecords.Where(item => item.BranchId == branch.Id).ToListAsync());
        Assert.Single(await db.KernelArtifacts.Where(item => item.ArtifactType == "MergeRecord").ToListAsync());
        Assert.Single(await db.ChapterVersions.ToListAsync());
        Assert.Single(await db.ContentDocuments.ToListAsync());
        Assert.Single(await db.OutboxEvents.ToListAsync());
    }

    [Fact]
    public async Task PrefixMerge_WhenPostCommitCachePublicationFails_KeepsCommittedMerge()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new NovelAgentDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await SeedProjectAsync(db);
        var currentUser = new StubCurrentUserService("user-1");
        var branches = new CanonBranchService(db, currentUser, new LegacyControlPlaneCommandTestDouble(db));
        var branch = await branches.CreateAsync("goal-1", 1, 1);
        await AddManualWorkflowTasksAsync(db, branch.Id);
        var candidate = await AddCandidateAsync(db, branches, branch.Id, 1, null);
        await branches.AcceptAsync(candidate.Id, candidate.Version);
        var publicationError = new InvalidOperationException("cache publication failed");
        var redis = new Mock<IDistributedCacheService>(MockBehavior.Strict);
        redis.Setup(service => service.RemoveByPrefixAsync(
                "content:text:user-1:project-1:",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(publicationError);
        var merger = new PrefixMergeService(
            db,
            currentUser,
            new ContentDocumentService(db, redis.Object),
            new StubMergeConflictModel(CanonMergeConflictReview.NoConflict()));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => merger.MergeAcceptedPrefixAsync(branch.Id));

        Assert.Same(publicationError, error);
        db.ChangeTracker.Clear();
        Assert.Single(await db.BranchMergeRecords.Where(item => item.BranchId == branch.Id).ToListAsync());
        Assert.Single(await db.ChapterVersions.ToListAsync());
        Assert.Single(await db.ContentDocuments.ToListAsync());
        Assert.Equal("merged", (await db.CanonBranches.SingleAsync(item => item.Id == branch.Id)).Status);
    }

    [Fact]
    public async Task PrefixMerge_StopsAtFirstUnacceptedChapterAndCanResumeLater()
    {
        await using var db = CreateDb();
        await SeedProjectAsync(db);
        var currentUser = new StubCurrentUserService("user-1");
        var branches = new CanonBranchService(db, currentUser, new LegacyControlPlaneCommandTestDouble(db));
        var merger = CreateMerger(db, currentUser);
        var branch = await branches.CreateAsync("goal-1", 1, 3);
        await AddManualWorkflowTasksAsync(db, branch.Id);
        var candidate1 = await AddCandidateAsync(db, branches, branch.Id, 1, null);
        var candidate2 = await AddCandidateAsync(db, branches, branch.Id, 2, candidate1.Id);
        var candidate3 = await AddCandidateAsync(db, branches, branch.Id, 3, candidate2.Id);
        var candidateSummary = new ContinuitySummary
        {
            Id = "summary-1",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = candidate1.ChapterId,
            ChapterVersionId = $"candidate:{candidate1.Id}",
            BranchId = branch.Id,
            SummaryJson = "{}",
            EvidenceRefsJson = "[]",
            Status = "candidate"
        };
        var candidateChange = new CanonChange
        {
            Id = "change-1",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = candidate1.ChapterId,
            ChapterVersionId = $"candidate:{candidate1.Id}",
            BranchId = branch.Id,
            ChangeType = "identity_reveal",
            Subject = "主角",
            Status = "candidate"
        };
        candidate1.ContinuitySummaryId = candidateSummary.Id;
        db.ContinuitySummaries.Add(candidateSummary);
        db.CanonChanges.Add(candidateChange);
        await db.SaveChangesAsync();
        await branches.AcceptAsync(candidate1.Id, candidate1.Version);
        await branches.AcceptAsync(candidate3.Id, candidate3.Version);

        var firstMerge = await merger.MergeAcceptedPrefixAsync(branch.Id);

        Assert.Equal(1, firstMerge.StartChapterNumber);
        Assert.Equal(1, firstMerge.EndChapterNumber);
        Assert.Single(await db.ChapterVersions.ToListAsync());
        Assert.Equal("merged", (await db.CandidateChapters.FindAsync(candidate1.Id))!.Status);
        Assert.Equal("candidate", (await db.CandidateChapters.FindAsync(candidate3.Id))!.Status);
        var committedVersion = await db.ChapterVersions.SingleAsync();
        Assert.Equal("committed", (await db.ContinuitySummaries.FindAsync(candidateSummary.Id))!.Status);
        Assert.Equal(committedVersion.Id, (await db.ContinuitySummaries.FindAsync(candidateSummary.Id))!.ChapterVersionId);
        Assert.Equal("committed", (await db.CanonChanges.FindAsync(candidateChange.Id))!.Status);
        Assert.Equal(committedVersion.Id, (await db.CanonChanges.FindAsync(candidateChange.Id))!.ChapterVersionId);

        await branches.AcceptAsync(candidate2.Id, candidate2.Version);
        var secondMerge = await merger.MergeAcceptedPrefixAsync(branch.Id);

        Assert.Equal(2, secondMerge.StartChapterNumber);
        Assert.Equal(3, secondMerge.EndChapterNumber);
        Assert.Equal(3, await db.ChapterVersions.CountAsync());
        Assert.Equal(3, await db.ContentDocuments.CountAsync());
        Assert.Equal(3, await db.OutboxEvents.CountAsync());
        Assert.Equal(2, await db.BranchMergeRecords.CountAsync());
        Assert.Equal("merged", (await db.CanonBranches.FindAsync(branch.Id))!.Status);
    }

    [Fact]
    public async Task CandidateChapters_CanDependOnEarlierCandidateContentWithoutChangingFormalCanon()
    {
        await using var db = CreateDb();
        await SeedProjectAsync(db);
        var branches = new CanonBranchService(
            db,
            new StubCurrentUserService("user-1"),
            new LegacyControlPlaneCommandTestDouble(db));
        var branch = await branches.CreateAsync("goal-1", 1, 3);
        var first = await AddCandidateAsync(db, branches, branch.Id, 1, null);
        var second = await AddCandidateAsync(db, branches, branch.Id, 2, first.Id);

        Assert.Equal(first.Id, second.DependsOnCandidateChapterId);
        Assert.Empty(await db.ChapterVersions.ToListAsync());
        Assert.Empty(await db.ContinuitySummaries.ToListAsync());
    }

    [Fact]
    public async Task PrefixMerge_RebasesWhenInterveningCanonChangesDoNotOverlapCandidateRange()
    {
        await using var db = CreateDb();
        await SeedProjectAsync(db);
        var currentUser = new StubCurrentUserService("user-1");
        var branches = new CanonBranchService(db, currentUser, new LegacyControlPlaneCommandTestDouble(db));
        var merger = CreateMerger(db, currentUser);
        var branch = await branches.CreateAsync("goal-1", 1, 1);
        await AddManualWorkflowTasksAsync(db, branch.Id);
        var candidate = await AddCandidateAsync(db, branches, branch.Id, 1, null);
        await branches.AcceptAsync(candidate.Id, candidate.Version);
        await AddInterveningMergeAsync(db, 10, 10, "canon-1", "canon-external");

        var merge = await merger.MergeAcceptedPrefixAsync(branch.Id);

        Assert.Equal("canon-external", merge.PreviousCanonVersion);
        Assert.Equal(1, merge.StartChapterNumber);
        Assert.Equal(1, merge.EndChapterNumber);
    }

    [Fact]
    public async Task PrefixMerge_EntersNeedsDecisionWhenInterveningCanonChangesOverlapCandidateRange()
    {
        await using var db = CreateDb();
        await SeedProjectAsync(db);
        var currentUser = new StubCurrentUserService("user-1");
        var branches = new CanonBranchService(db, currentUser, new LegacyControlPlaneCommandTestDouble(db));
        var merger = CreateMerger(db, currentUser);
        var branch = await branches.CreateAsync("goal-1", 1, 1);
        await AddManualWorkflowTasksAsync(db, branch.Id);
        var candidate = await AddCandidateAsync(db, branches, branch.Id, 1, null);
        await branches.AcceptAsync(candidate.Id, candidate.Version);
        await AddInterveningMergeAsync(db, 1, 1, "canon-1", "canon-external");

        var error = await Assert.ThrowsAsync<CanonMergeConflictException>(
            () => merger.MergeAcceptedPrefixAsync(branch.Id));

        Assert.Equal("canon-1", error.GoalBaselineVersion);
        Assert.Equal("canon-external", error.CurrentCanonVersion);
        Assert.Equal(new[] { 1 }, error.ConflictingChapterNumbers);
        Assert.Equal("needs_decision", (await db.CanonBranches.FindAsync(branch.Id))!.Status);
        Assert.Empty(await db.ChapterVersions.ToListAsync());
        Assert.Empty(await db.BranchMergeRecords.Where(record => record.BranchId == branch.Id).ToListAsync());
    }

    [Fact]
    public async Task PrefixMerge_EntersNeedsDecisionForCrossChapterSemanticCanonConflict()
    {
        await using var db = CreateDb();
        await SeedProjectAsync(db);
        var currentUser = new StubCurrentUserService("user-1");
        var branches = new CanonBranchService(db, currentUser, new LegacyControlPlaneCommandTestDouble(db));
        var branch = await branches.CreateAsync("goal-1", 1, 1);
        await AddManualWorkflowTasksAsync(db, branch.Id);
        var candidate = await AddCandidateAsync(db, branches, branch.Id, 1, null);
        var candidateChange = new CanonChange
        {
            Id = "candidate-change",
            UserId = "user-1",
            ProjectId = "project-1",
            BranchId = branch.Id,
            ChapterId = candidate.ChapterId,
            ChapterVersionId = $"candidate:{candidate.Id}",
            ChangeType = "identity",
            Subject = "林岚",
            ChangeJson = "{\"identity\":\"王位继承人\"}",
            Status = "candidate"
        };
        db.CanonChanges.Add(candidateChange);
        db.CanonChanges.Add(new CanonChange
        {
            Id = "current-change",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-2",
            ChapterVersionId = "version-10",
            ChangeType = "identity",
            Subject = "失踪王女",
            ChangeJson = "{\"identity\":\"已确认死亡且无继承资格\"}",
            Status = "committed",
            CreatedAt = branch.CreatedAt.AddMinutes(1)
        });
        await db.SaveChangesAsync();
        await branches.AcceptAsync(candidate.Id, candidate.Version);
        var model = new StubMergeConflictModel(new CanonMergeConflictReview(
            true,
            [candidateChange.Id],
            ["current-change"],
            "同一人物身份状态互斥"));
        var merger = new PrefixMergeService(
            db,
            currentUser,
            new ContentDocumentService(db),
            model);

        var error = await Assert.ThrowsAsync<CanonMergeConflictException>(
            () => merger.MergeAcceptedPrefixAsync(branch.Id));

        Assert.Equal([1], error.ConflictingChapterNumbers);
        Assert.Equal("needs_decision", (await db.CanonBranches.FindAsync(branch.Id))!.Status);
        Assert.NotNull(model.Request);
        Assert.Single(model.Request!.CandidateChanges);
        Assert.Single(model.Request.CurrentChanges);
        Assert.Empty(await db.ChapterVersions.ToListAsync());
    }

    [Fact]
    public async Task AddCandidate_AgentCannotSupersedeLatestProtectedHumanVersion()
    {
        await using var db = CreateDb();
        await SeedProjectAsync(db);
        var branches = new CanonBranchService(
            db,
            new StubCurrentUserService("user-1"),
            new LegacyControlPlaneCommandTestDouble(db));
        var branch = await branches.CreateAsync("goal-1", 1, 1);
        await AddCandidateAsync(db, branches, branch.Id, 1, null, "human", true);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AddCandidateAsync(db, branches, branch.Id, 1, null, "agent", false));

        Assert.Contains("人工保护", error.Message);
        Assert.Single(await db.CandidateChapters.ToListAsync());
    }

    private static async Task<CandidateChapter> AddCandidateAsync(
        NovelAgentDbContext db,
        CanonBranchService service,
        string branchId,
        int chapterNumber,
        string? dependsOn,
        string authorship = "agent",
        bool isProtected = false)
    {
        var artifactId = $"artifact-{chapterNumber}-{Guid.NewGuid():N}";
        var artifact = new KernelArtifact
        {
            Id = artifactId,
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            TaskId = $"task-{chapterNumber}",
            BranchId = branchId,
            ArtifactType = "CandidateChapterDraft",
            SchemaVersion = 1,
            ContentJson = JsonSerializer.Serialize(new ChapterDraftArtifact
            {
                ArtifactId = $"draft-{chapterNumber}",
                ChapterId = $"chapter-{chapterNumber}",
                DraftContent = $"第 {chapterNumber} 章候选正文"
            }),
            ContentHash = $"hash-{chapterNumber}"
        };
        db.KernelArtifacts.Add(artifact);
        await db.SaveChangesAsync();
        return await service.AddCandidateAsync(
            branchId,
            $"chapter-{chapterNumber}",
            chapterNumber,
            artifact.Id,
            dependsOn,
            authorship,
            isProtected);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static PrefixMergeService CreateMerger(
        NovelAgentDbContext db,
        ICurrentUserService currentUser) => new(
        db,
        currentUser,
        new ContentDocumentService(db),
        new StubMergeConflictModel(CanonMergeConflictReview.NoConflict()));

    private static async Task AddInterveningMergeAsync(
        NovelAgentDbContext db,
        int startChapterNumber,
        int endChapterNumber,
        string previousCanonVersion,
        string newCanonVersion)
    {
        db.BranchMergeRecords.Add(new BranchMergeRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "other-goal",
            BranchId = "other-branch",
            StartChapterNumber = startChapterNumber,
            EndChapterNumber = endChapterNumber,
            PreviousCanonVersion = previousCanonVersion,
            NewCanonVersion = newCanonVersion,
            MergedByUserId = "user-1",
            CreatedAt = DateTime.UtcNow.AddSeconds(1)
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedProjectAsync(NovelAgentDbContext db)
    {
        db.Users.Add(new User
        {
            Id = "user-1",
            Username = "author",
            Email = "author@example.com",
            PasswordHash = "hash",
            Role = "author"
        });
        db.NovelProjects.Add(new NovelProject { Id = "project-1", UserId = "user-1", Title = "测试小说" });
        db.CreativeGoals.Add(new CreativeGoal
        {
            Id = "goal-1",
            UserId = "user-1",
            ProjectId = "project-1",
            SourceSessionId = "session-1",
            GoalType = "write_batch",
            HumanReadableObjective = "写三章",
            TotalCostLimit = 20,
            CanonBaselineVersion = "canon-1",
            KnowledgeSnapshotVersion = "knowledge-1",
            QualityContractVersion = "quality-1",
            StyleProfileVersion = "style-1",
            IdempotencyKey = "goal-1"
        });
        for (var number = 1; number <= 3; number++)
        {
            db.Chapters.Add(new Chapter
            {
                Id = $"chapter-{number}",
                ProjectId = "project-1",
                ChapterNumber = number,
                Title = $"第 {number} 章"
            });
        }
        await db.SaveChangesAsync();
    }

    private static async Task AddManualWorkflowTasksAsync(NovelAgentDbContext db, string branchId)
    {
        if (!await db.TaskGraphVersions.AnyAsync(item => item.Id == "graph-1"))
        {
            db.TaskGraphVersions.Add(new TaskGraphVersion
            {
                Id = "graph-1",
                UserId = "user-1",
                ProjectId = "project-1",
                GoalId = "goal-1",
                Version = 1,
                Status = "active",
                GraphJson = "{}",
                ContentHash = "graph-hash"
            });
        }
        if (!await db.BookProductions.AnyAsync(item => item.GoalId == "goal-1"))
        {
            db.BookProductions.Add(new BookProduction
            {
                Id = "production-1",
                UserId = "user-1",
                ProjectId = "project-1",
                GoalId = "goal-1",
                ExecutionStrategy = "interactive_batch",
                Status = "running",
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
                EndChapterNumber = 3,
                Status = "running",
                AcceptanceActor = "user",
                TaskGraphVersionId = "graph-1",
                CanonBranchId = branchId
            });
        }
        db.KernelTasks.AddRange(
            new KernelTask
            {
                Id = "graph-1:acceptance-gate",
                UserId = "user-1",
                ProjectId = "project-1",
                GoalId = "goal-1",
                TaskGraphVersionId = "graph-1",
                BranchId = branchId,
                KernelName = "workflow",
                TaskType = "AcceptanceGate",
                Status = "awaiting_user",
                IdempotencyKey = "graph-1:acceptance-gate"
            },
            new KernelTask
            {
                Id = "graph-1:prefix-merge",
                UserId = "user-1",
                ProjectId = "project-1",
                GoalId = "goal-1",
                TaskGraphVersionId = "graph-1",
                BranchId = branchId,
                KernelName = "domain_reducer",
                TaskType = "PrefixMerge",
                Status = "blocked",
                IdempotencyKey = "graph-1:prefix-merge"
            });
        await db.SaveChangesAsync();
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

    private sealed class StubMergeConflictModel(CanonMergeConflictReview result) : ICanonMergeConflictModelClient
    {
        public CanonMergeConflictReviewRequest? Request { get; private set; }

        public Task<CanonMergeConflictReview> ReviewAsync(
            CanonMergeConflictReviewRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(result);
        }
    }
}
