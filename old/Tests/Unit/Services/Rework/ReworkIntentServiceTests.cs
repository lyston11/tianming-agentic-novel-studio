using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Rework;
using Xunit;

namespace Tests.Unit.Services.Rework;

public sealed class ReworkIntentServiceTests
{
    [Fact]
    public async Task CompileAsync_UsesSelectionAndConversationMeaningToPersistExplicitContract()
    {
        await using var db = CreateDb();
        var candidate = await SeedCandidateAsync(db);
        var model = new StubReworkIntentModelClient(new ReworkIntentDraft(
            "selection",
            "对话缺少潜台词",
            "让威胁藏在礼貌措辞下",
            ["人物仍在谈判桌前"],
            ["选区内对白和动作"],
            ["不得改变交易结果", "不得改写选区外正文"],
            ["选区内不直说威胁", "交易结果保持不变"],
            "copy",
            "none"));
        var service = new ReworkIntentService(db, new StubCurrentUserService("user-1"), model, new ReworkBudgetPolicy());

        var intent = await service.CompileAsync(new CompileReworkIntentRequest(
            candidate.Id,
            candidate.Version,
            "session-1",
            "这段对白太直白了，让双方表面客气但都知道对方在威胁自己。",
            2,
            8,
            "你最好现在答应"));

        Assert.Equal("selection", intent.TargetScope);
        Assert.Equal(2, intent.SelectionStart);
        Assert.Equal(8, intent.SelectionEnd);
        Assert.Equal("proposed", intent.Status);
        Assert.Equal("copy", intent.ImpactLevel);
        Assert.Equal(new[] { "不得改变交易结果", "不得改写选区外正文" },
            JsonSerializer.Deserialize<string[]>(intent.MustNotChangeJson));
        Assert.Equal("这段对白太直白了，让双方表面客气但都知道对方在威胁自己。", model.LastContext!.UserDescription);
        Assert.Contains("候选正文", model.LastContext.CandidateContent);
    }

    [Fact]
    public async Task AutomaticAttempts_ArePersistedAndStopAtScopeBudget()
    {
        await using var db = CreateDb();
        var candidate = await SeedCandidateAsync(db);
        var model = new StubReworkIntentModelClient(new ReworkIntentDraft(
            "selection",
            "对白直白",
            "增加潜台词",
            ["交易结果"],
            ["选区对白"],
            ["选区外正文"],
            ["不直说威胁"],
            "copy",
            "不传播"));
        var service = new ReworkIntentService(
            db,
            new StubCurrentUserService("user-1"),
            model,
            new ReworkBudgetPolicy());
        var intent = await service.CompileAsync(new CompileReworkIntentRequest(
            candidate.Id,
            candidate.Version,
            "session-1",
            "增加潜台词",
            2,
            8,
            "你最好现在答应"));

        await service.StartAutomaticAttemptAsync(intent.Id);
        await service.RecordAttemptOutcomeAsync(
            intent.Id,
            problemResolved: false,
            problemImproved: true,
            impactExpanded: false);
        await service.StartAutomaticAttemptAsync(intent.Id);
        var exhausted = await service.RecordAttemptOutcomeAsync(
            intent.Id,
            problemResolved: false,
            problemImproved: true,
            impactExpanded: false);

        Assert.Equal(2, exhausted.AttemptCount);
        Assert.Equal("needs_decision", exhausted.Status);
    }

    [Fact]
    public async Task CompileAsync_RejectsSessionOutsideGoalSourceConversation()
    {
        await using var db = CreateDb();
        var candidate = await SeedCandidateAsync(db);
        var model = new StubReworkIntentModelClient(new ReworkIntentDraft(
            "chapter",
            "节奏松散",
            "收紧冲突",
            [],
            ["章节正文"],
            ["既定结果"],
            ["冲突更集中"],
            "local_fact",
            "revalidate_following"));
        var service = new ReworkIntentService(db, new StubCurrentUserService("user-1"), model, new ReworkBudgetPolicy());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompileAsync(
            new CompileReworkIntentRequest(
                candidate.Id,
                candidate.Version,
                "another-session",
                "把这一章的冲突收紧。",
                null,
                null,
                string.Empty)));

        Assert.Contains("来源会话", exception.Message);
        Assert.Empty(await db.ReworkIntents.ToListAsync());
        Assert.Null(model.LastContext);
    }

    [Fact]
    public void BudgetPolicy_LimitsLocalAndChapterAttemptsAndEscalatesRegression()
    {
        var policy = new ReworkBudgetPolicy();
        var local = new ReworkIntent { TargetScope = "selection", AttemptCount = 1, Status = "proposed" };
        var chapter = new ReworkIntent { TargetScope = "chapter", AttemptCount = 1, Status = "proposed" };

        Assert.True(policy.CanStartAutomaticAttempt(local));
        Assert.False(policy.CanStartAutomaticAttempt(chapter));
        Assert.Equal("needs_decision", policy.EvaluateOutcome(local, problemImproved: false, impactExpanded: false));
        Assert.Equal("needs_decision", policy.EvaluateOutcome(local, problemImproved: true, impactExpanded: true));
        Assert.Equal("none", policy.GetPropagation("copy"));
        Assert.Equal("revalidate_following", policy.GetPropagation("local_fact"));
        Assert.Equal("revision_plan_required", policy.GetPropagation("key_plot"));
        Assert.Equal("block_merge", policy.GetPropagation("hard_conflict"));
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static async Task<CandidateChapter> SeedCandidateAsync(NovelAgentDbContext db)
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
            HumanReadableObjective = "写一章",
            CanonBaselineVersion = "canon-1",
            KnowledgeSnapshotVersion = "knowledge-1",
            QualityContractVersion = "quality-1",
            StyleProfileVersion = "style-1",
            IdempotencyKey = "goal-1"
        });
        db.CanonBranches.Add(new CanonBranch
        {
            Id = "branch-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            CanonBaselineVersion = "canon-1",
            StartChapterNumber = 1,
            EndChapterNumber = 1
        });
        db.KernelArtifacts.Add(new KernelArtifact
        {
            Id = "artifact-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            TaskId = "task-1",
            BranchId = "branch-1",
            ArtifactType = "CandidateChapterDraft",
            ContentJson = JsonSerializer.Serialize(new ChapterDraftArtifact
            {
                ChapterId = "chapter-1",
                DraftContent = "第一章候选正文：你最好现在答应。"
            }),
            ContentHash = "hash-1"
        });
        var candidate = new CandidateChapter
        {
            Id = "candidate-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            BranchId = "branch-1",
            ChapterId = "chapter-1",
            ChapterNumber = 1,
            Version = 1,
            CurrentArtifactId = "artifact-1"
        };
        db.CandidateChapters.Add(candidate);
        await db.SaveChangesAsync();
        return candidate;
    }

    private sealed class StubReworkIntentModelClient(ReworkIntentDraft result) : IReworkIntentModelClient
    {
        public ReworkIntentCompilationContext? LastContext { get; private set; }

        public Task<ReworkIntentDraft> CompileAsync(
            ReworkIntentCompilationContext context,
            CancellationToken cancellationToken = default)
        {
            LastContext = context;
            return Task.FromResult(result);
        }
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
