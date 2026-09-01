using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.DomainEvents;
using TM.Web.NovelAgentWeb.Services.Goals;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Services.Canon;
using System.Text.Json;
using TM.Web.NovelAgentWeb.Services.Rag;
using TM.Web.NovelAgentWeb.Services.Kernels;
using Xunit;

namespace Tests.Unit.Services.DomainEvents;

public sealed class DomainReducerTests
{
    [Fact]
    public async Task ApplyAsync_LightweightCanonEventMaterializesCandidateChapterAndCanonEvidence()
    {
        var webJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        await using var db = CreateDb();
        var claim = await SeedRunningTaskAsync(db, "ExtractContinuitySummary", "branch-1");
        db.Chapters.Add(new Chapter
        {
            Id = "chapter-1",
            ProjectId = claim.ProjectId,
            ChapterNumber = 1,
            Title = "第一章"
        });
        db.CanonBranches.Add(new CanonBranch
        {
            Id = "branch-1",
            UserId = claim.UserId,
            ProjectId = claim.ProjectId,
            GoalId = claim.GoalId,
            StartChapterNumber = 1,
            EndChapterNumber = 3
        });
        db.KernelArtifacts.AddRange(
            new KernelArtifact
            {
                Id = "reviewed-1",
                UserId = claim.UserId,
                ProjectId = claim.ProjectId,
                GoalId = claim.GoalId,
                TaskId = "graph-1:manual-rework-test-adopt",
                BranchId = claim.BranchId,
                ArtifactType = "ReviewedCandidateChapter",
                ContentJson = JsonSerializer.Serialize(new ChapterDraftArtifact
                {
                    ChapterId = "chapter-1",
                    DraftContent = "林岚进入旧城区。"
                }),
                ContentHash = "reviewed-hash"
            },
            new KernelArtifact
            {
                Id = "review-continuity",
                UserId = claim.UserId,
                ProjectId = claim.ProjectId,
                GoalId = claim.GoalId,
                TaskId = "graph-1:manual-rework-test-continuity-review",
                BranchId = claim.BranchId,
                ArtifactType = "ContinuityReview",
                ContentJson = "{}",
                ContentHash = "review-1-hash"
            },
            new KernelArtifact
            {
                Id = "review-literary",
                UserId = claim.UserId,
                ProjectId = claim.ProjectId,
                GoalId = claim.GoalId,
                TaskId = "graph-1:manual-rework-test-literary-review",
                BranchId = claim.BranchId,
                ArtifactType = "LiteraryReview",
                ContentJson = "{}",
                ContentHash = "review-2-hash"
            },
            new KernelArtifact
            {
                Id = "evidence-1",
                UserId = claim.UserId,
                ProjectId = claim.ProjectId,
                GoalId = claim.GoalId,
                TaskId = "graph-1:chapter-1-context",
                BranchId = claim.BranchId,
                ArtifactType = "EvidenceBundle",
                ContentJson = JsonSerializer.Serialize(new EvidenceBundle(
                    new RagQueryPlan([RagRoute.Knowledge], ["旧城区"], [], [], true),
                    [new EvidenceItem(claim.UserId, claim.ProjectId, "knowledge_entry", "knowledge-entry-1", "旧城区资料", 1, ["fts"]) ])),
                ContentHash = "evidence-hash"
            });
        db.KernelTasks.Add(new KernelTask
        {
            Id = "graph-1:manual-rework-test-adopt",
            UserId = claim.UserId,
            ProjectId = claim.ProjectId,
            GoalId = claim.GoalId,
            TaskGraphVersionId = claim.TaskGraphVersionId,
            BranchId = claim.BranchId,
            KernelName = "tianming_writing",
            TaskType = "DirectedRework",
            Status = "completed",
            InputArtifactIdsJson = "[\"context-contract-1\"]",
            IdempotencyKey = "manual-adopt"
        });
        db.KernelArtifacts.Add(new KernelArtifact
        {
            Id = "context-contract-1",
            UserId = claim.UserId,
            ProjectId = claim.ProjectId,
            GoalId = claim.GoalId,
            TaskId = "graph-1:chapter-1-context",
            BranchId = claim.BranchId,
            ArtifactType = "ChapterContextContract",
            ContentJson = "{}",
            ContentHash = "context-contract-hash"
        });
        db.ReworkIntents.Add(new ReworkIntent
        {
            Id = "intent-1",
            UserId = claim.UserId,
            ProjectId = claim.ProjectId,
            GoalId = claim.GoalId,
            BranchId = claim.BranchId!,
            CandidateChapterId = "candidate-original",
            CandidateVersion = 1,
            Problem = "动机不清",
            DesiredEffect = "强化因果",
            AcceptanceCriteriaJson = "[\"动机有证据\"]",
            Status = "executing"
        });
        db.KernelArtifacts.Add(new KernelArtifact
        {
            Id = "intent-artifact-1",
            UserId = claim.UserId,
            ProjectId = claim.ProjectId,
            GoalId = claim.GoalId,
            TaskId = "graph-1:manual-rework-test-draft",
            BranchId = claim.BranchId,
            ArtifactType = "ReworkIntent",
            ContentJson = JsonSerializer.Serialize(new ReworkIntentArtifactContract(
                "intent-1", "chapter", null, null, "动机不清", "强化因果", [], [], [], ["动机有证据"]), webJson),
            ContentHash = "intent-artifact-hash",
            Authorship = "human"
        });
        db.KnowledgeEntries.Add(new KnowledgeEntry
        {
            Id = "knowledge-entry-1",
            UserId = claim.UserId,
            ProjectId = claim.ProjectId,
            LogicalKnowledgeId = "knowledge-logical-1",
            DocumentBlobId = "blob-1",
            KnowledgeVersion = 2,
            Version = 1,
            EntryType = "reference",
            Title = "旧城区资料",
            Content = "旧城区资料",
            Status = "active"
        });
        db.ProjectKnowledgeUsages.Add(new ProjectKnowledgeUsage
        {
            Id = "usage-1",
            UserId = claim.UserId,
            ProjectId = claim.ProjectId,
            KnowledgeId = "knowledge-logical-1",
            Status = "referenced",
            FirstSeenAt = DateTime.UtcNow.AddDays(-1),
            UsageCount = 1,
            UsedByChaptersJson = "[\"chapter-0\"]",
            UsageIdempotencyKeysJson = "[\"older-use\"]"
        });
        await db.SaveChangesAsync();
        var reducer = CreateReducer(db);
        var canon = new LightweightCanonArtifact(
            "chapter_body",
            "reviewed-1",
            [new LightweightSummaryItemDraft("summary-1", "location", "林岚进入旧城区", new CanonEvidenceSpan(0, 8, "林岚进入旧城区"))],
            [new CanonChangeDraft("change-1", "location", "林岚", "进入旧城区", new CanonEvidenceSpan(0, 8, "林岚进入旧城区"))],
            new Dictionary<string, string>());

        await reducer.ApplyAsync(
            claim,
            [new KernelArtifactProposal("ContinuitySummary", 1, JsonSerializer.Serialize(canon), "canon-hash", "agent", false)],
            [new DomainEventProposal(
                "candidate_chapter",
                "chapter-1",
                1,
                "CandidateLightweightCanonProposed",
                ["@artifact:0"],
                ["reviewed-1"],
                "{}",
                "candidate-projection-1")]);

        var candidate = Assert.Single(await db.CandidateChapters.ToListAsync());
        Assert.Equal("reviewed-1", candidate.CurrentArtifactId);
        Assert.Equal(["review-continuity", "review-literary"],
            JsonSerializer.Deserialize<string[]>(candidate.ReviewArtifactIdsJson)?.Order().ToArray());
        Assert.NotNull(candidate.ContinuitySummaryId);
        Assert.Single(await db.ContinuitySummaries.ToListAsync());
        Assert.Single(await db.CanonChanges.ToListAsync());
        var citation = Assert.Single(await db.KnowledgeCitations.ToListAsync());
        Assert.Equal("knowledge-entry-1", citation.KnowledgeEntryId);
        Assert.Equal($"candidate:{candidate.Id}", citation.ChapterVersionId);
        var usage = Assert.Single(await db.ProjectKnowledgeUsages.ToListAsync());
        Assert.Equal("knowledge-logical-1", usage.KnowledgeId);
        Assert.Equal("referenced", usage.Status);
        Assert.Equal(2, usage.UsageCount);
        Assert.Equal(["chapter-0", "chapter-1"],
            JsonSerializer.Deserialize<string[]>(usage.UsedByChaptersJson!)?.Order().ToArray());
        Assert.Contains("older-use", usage.UsageIdempotencyKeysJson);
        Assert.Equal("resolved", (await db.ReworkIntents.SingleAsync()).Status);
    }

    [Fact]
    public async Task ApplyAsync_PersistsArtifactEventAndOutboxAsOneAdoption()
    {
        await using var db = CreateDb();
        var claim = await SeedRunningTaskAsync(db);
        var reducer = CreateReducer(db);

        var result = await reducer.ApplyAsync(
            claim,
            [new KernelArtifactProposal("ChapterPlan", 1, "{\"chapter\":1}", "hash-plan-1", "agent", false)],
            [new DomainEventProposal(
                "candidate_chapter",
                "candidate-1",
                1,
                "ChapterPlanProduced",
                ["@artifact:0"],
                [],
                "{\"chapterNumber\":1}",
                "event-idempotency-1")]);

        Assert.Single(result.ArtifactIds);
        Assert.Single(result.EventIds);
        var artifact = Assert.Single(await db.KernelArtifacts.ToListAsync());
        var domainEvent = Assert.Single(await db.DomainEvents.ToListAsync());
        var outbox = Assert.Single(await db.OutboxEvents.ToListAsync());
        Assert.Equal(claim.UserId, artifact.UserId);
        Assert.Equal(artifact.Id, domainEvent.ArtifactRefsJson.Trim('[', ']', '"'));
        Assert.Equal(domainEvent.Id, outbox.AggregateId);
        Assert.Equal("domain-event:event-idempotency-1", outbox.IdempotencyKey);
    }

    [Fact]
    public async Task ApplyAsync_RejectsMismatchedClaimWithoutPartialRows()
    {
        await using var db = CreateDb();
        var claim = await SeedRunningTaskAsync(db);
        var reducer = CreateReducer(db);
        var forged = claim with { UserId = "other-user" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => reducer.ApplyAsync(
            forged,
            [new KernelArtifactProposal("ChapterPlan", 1, "{}", "hash", "agent", false)],
            []));

        Assert.Empty(await db.KernelArtifacts.ToListAsync());
        Assert.Empty(await db.DomainEvents.ToListAsync());
        Assert.Empty(await db.OutboxEvents.ToListAsync());
    }

    [Fact]
    public async Task ApplyAsync_RejectsCanceledGoalWithoutPartialRows()
    {
        await using var db = CreateDb();
        var claim = await SeedRunningTaskAsync(db);
        db.CreativeGoals.Local.Single().Status = "canceled";
        await db.SaveChangesAsync();
        var reducer = CreateReducer(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => reducer.ApplyAsync(
            claim,
            [new KernelArtifactProposal("ChapterPlan", 1, "{}", "hash", "agent", false)],
            []));

        Assert.Empty(await db.KernelArtifacts.ToListAsync());
        Assert.Empty(await db.DomainEvents.ToListAsync());
        Assert.Empty(await db.OutboxEvents.ToListAsync());
    }

    [Fact]
    public async Task ApplyAsync_AssignsNextAggregateVersionAndReusesDuplicateIdempotency()
    {
        await using var db = CreateDb();
        var claim = await SeedRunningTaskAsync(db);
        var reducer = CreateReducer(db);
        var first = new DomainEventProposal(
            "candidate_chapter",
            "candidate-1",
            1,
            "ChapterPlanProduced",
            [],
            [],
            "{}",
            "event-idempotency-1");
        await reducer.ApplyAsync(claim, [], [first]);

        var second = await reducer.ApplyAsync(
            claim,
            [],
            [first with { IdempotencyKey = "event-idempotency-2" }]);
        var repeated = await reducer.ApplyAsync(claim, [], [first]);

        Assert.Single(second.EventIds);
        Assert.Equal(
            new long[] { 1, 2 },
            await db.DomainEvents.OrderBy(item => item.AggregateVersion)
                .Select(item => item.AggregateVersion)
                .ToArrayAsync());
        Assert.Equal(2, await db.OutboxEvents.CountAsync());
        Assert.Single(repeated.EventIds);
    }

    private static DomainReducer CreateReducer(NovelAgentDbContext db)
    {
        var currentUser = new StubCurrentUserService("user-1");
        return new DomainReducer(
            db,
            currentUser,
            new DomainContractValidator(db, currentUser),
            new KernelArtifactStore(db, new Tests.Unit.Support.LegacyControlPlaneCommandTestDouble(db)));
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static async Task<KernelTaskClaim> SeedRunningTaskAsync(
        NovelAgentDbContext db,
        string taskType = "PlanChapter",
        string? branchId = null)
    {
        var task = new KernelTask
        {
            Id = "task-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            TaskGraphVersionId = "graph-1",
            KernelName = "narrative_planning",
            TaskType = taskType,
            BranchId = branchId,
            Status = "running",
            LeaseOwner = "worker-1",
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(2),
            Attempt = 1,
            MaxAttempts = 2,
            IdempotencyKey = "task-1"
        };
        db.CreativeGoals.Add(new CreativeGoal
        {
            Id = task.GoalId,
            UserId = task.UserId,
            ProjectId = task.ProjectId,
            Status = "running",
            IdempotencyKey = "goal-1"
        });
        db.TaskGraphVersions.Add(new TaskGraphVersion
        {
            Id = task.TaskGraphVersionId,
            UserId = task.UserId,
            ProjectId = task.ProjectId,
            GoalId = task.GoalId,
            Version = 1,
            Status = "active",
            ContentHash = "graph-hash"
        });
        db.KernelTasks.Add(task);
        await db.SaveChangesAsync();
        return new KernelTaskClaim(
            task.Id,
            task.UserId,
            task.ProjectId,
            task.GoalId,
            task.TaskGraphVersionId,
            branchId,
            task.KernelName,
            task.TaskType,
            task.Attempt,
            task.LeaseOwner,
            task.LeaseExpiresAt!.Value);
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
