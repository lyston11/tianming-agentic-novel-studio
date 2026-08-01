using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Goals;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Goals;

public sealed class CreativeGoalServiceTests
{
    [Fact]
    public async Task ExploringDiscussion_DoesNotCreateGoal()
    {
        await using var db = CreateDb();
        await SeedProjectAsync(db);
        var service = CreateService(db);

        var result = await service.SubmitAsync(
            CreateCommand(),
            new CommitmentAssessment(
                DialogueCommitmentState.Exploring,
                GoalAuthorizationKind.None,
                0.82,
                true,
                "用户仍在比较方案",
                CreateContract()));

        Assert.Equal(GoalSubmissionStatus.NotCommitted, result.Status);
        Assert.Empty(await db.CreativeGoals.ToListAsync());
    }

    [Fact]
    public async Task ExplicitAuthorization_CreatesImmutableGoalWithFrozenBaselines()
    {
        await using var db = CreateDb();
        await SeedProjectAsync(db);
        var service = CreateService(db);

        var result = await service.SubmitAsync(
            CreateCommand(),
            new CommitmentAssessment(
                DialogueCommitmentState.Committed,
                GoalAuthorizationKind.ExplicitAction,
                1,
                false,
                "用户点击执行",
                CreateContract()));

        Assert.Equal(GoalSubmissionStatus.Created, result.Status);
        var goal = Assert.Single(await db.CreativeGoals.ToListAsync());
        var snapshot = Assert.Single(await db.GoalContextSnapshots.ToListAsync());
        Assert.Equal("authenticated-user", goal.UserId);
        Assert.Equal("canon-7", goal.CanonBaselineVersion);
        Assert.Equal("knowledge-11", goal.KnowledgeSnapshotVersion);
        Assert.Equal("goal-idempotency", goal.IdempotencyKey);
        Assert.Equal(goal.Id, snapshot.GoalId);
        Assert.Equal("models-3", snapshot.ModelConfigVersionsJson);
    }

    [Fact]
    public async Task ExplicitAuthorization_RejectsSourceSessionOwnedByAnotherUser()
    {
        await using var db = CreateDb();
        await SeedProjectAsync(db);
        db.AgentSessions.Add(new AgentSession
        {
            Id = "other-session",
            UserId = "other-user",
            ProjectId = "project-1"
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SubmitAsync(
            CreateCommand("other-session"),
            new CommitmentAssessment(
                DialogueCommitmentState.Committed,
                GoalAuthorizationKind.ExplicitAction,
                1,
                false,
                "用户点击执行",
                CreateContract())));

        Assert.Contains("来源会话", exception.Message);
        Assert.Empty(await db.CreativeGoals.ToListAsync());
    }

    [Fact]
    public async Task Revision_AppendsRevisionWithoutMutatingCommittedGoal()
    {
        await using var db = CreateDb();
        await SeedProjectAsync(db);
        var service = CreateService(db);
        var submitted = await service.SubmitAsync(
            CreateCommand(),
            new CommitmentAssessment(
                DialogueCommitmentState.Committed,
                GoalAuthorizationKind.ExplicitAction,
                1,
                false,
                "用户点击执行",
                CreateContract()));
        var original = await db.CreativeGoals.AsNoTracking().SingleAsync();

        var revision = await service.ReviseAsync(new ReviseCreativeGoalCommand(
            GoalId: submitted.GoalId!,
            Reason: "把批次从三章改成四章",
            ConstraintChangesJson: "{\"targetChapterRangeJson\":\"{\\\"start\\\":1,\\\"end\\\":4}\"}",
            ReusableArtifactIds: ["artifact-1"],
            InvalidatedArtifactIds: ["artifact-2"],
            AffectedNodeIds: ["chapter-4-plan"]));

        var unchanged = await db.CreativeGoals.AsNoTracking().SingleAsync();
        Assert.Equal(original.HumanReadableObjective, unchanged.HumanReadableObjective);
        Assert.Equal(original.TargetChapterRangeJson, unchanged.TargetChapterRangeJson);
        Assert.Equal(original.AggregateVersion, unchanged.AggregateVersion);
        Assert.Equal(1, revision.RevisionNumber);
        Assert.Equal(submitted.GoalId, revision.GoalId);
        Assert.Contains("targetChapterRangeJson", revision.ConstraintChangesJson);
        Assert.Equal("[\"chapter-4-plan\"]", revision.AffectedNodeIdsJson);
    }

    [Fact]
    public async Task CommitmentAssessment_DelegatesConversationSemanticsWithoutKeywordRouting()
    {
        var model = new StubCommitmentModel(new CommitmentAssessment(
            DialogueCommitmentState.Exploring,
            GoalAuthorizationKind.None,
            0.9,
            true,
            "这句话在上下文中是在询问设计，不是授权",
            null));
        var service = new CommitmentAssessmentService(
            model,
            new StubCurrentUserService("authenticated-user"));

        var result = await service.AssessAsync(new CommitmentAssessmentRequest(
            ProjectId: "project-1",
            CollaborationMode: "coauthor",
            Dialogue: [new DialogueMessage("user", "开始和继续这样的词应该怎么理解？")],
            AcceptedDecisionsJson: "[]",
            ProjectStateJson: "{}",
            ExplicitExecutionAction: false,
            ProposedContract: null));

        Assert.Equal(DialogueCommitmentState.Exploring, result.State);
        Assert.Equal(1, model.CallCount);
    }

    [Fact]
    public async Task DefaultCommitmentModelClient_ParsesStructuredAssessmentContract()
    {
        var completion = new StubCompletionService("""
            {
              "state": "Proposed",
              "authorization": "None",
              "confidence": 0.76,
              "requiresConfirmation": true,
              "rationale": "目标已形成，但用户尚未授权执行",
              "proposedContract": {
                "goalType": "write_batch",
                "collaborationMode": "coauthor",
                "humanReadableObjective": "创作前三章候选正文",
                "targetChapterRangeJson": "{\"start\":1,\"end\":3}",
                "successCriteria": ["三章均通过审校"],
                "mustPreserve": [],
                "mustHappen": [],
                "mustNotChange": [],
                "acceptancePolicyJson": "{}",
                "reworkPolicyJson": "{}"
              }
            }
            """);
        var client = new DefaultCommitmentAssessmentModelClient(
            completion,
            NullLogger<DefaultCommitmentAssessmentModelClient>.Instance);

        var result = await client.AssessAsync(
            "authenticated-user",
            new CommitmentAssessmentRequest(
                "project-1",
                "coauthor",
                [new DialogueMessage("user", "我们先讨论前三章怎么写")],
                "[]",
                "{}",
                false,
                null));

        Assert.Equal(DialogueCommitmentState.Proposed, result.State);
        Assert.Equal(GoalAuthorizationKind.None, result.Authorization);
        Assert.True(result.RequiresConfirmation);
        Assert.Equal("write_batch", result.ProposedContract?.GoalType);
    }

    private static CreativeGoalService CreateService(NovelAgentDbContext db)
    {
        var currentUser = new StubCurrentUserService("authenticated-user");
        return new CreativeGoalService(
        db,
        currentUser,
        new StubGoalBaselineProvider(new GoalBaselines(
            "canon-7",
            "knowledge-11",
            "quality-2",
            "style-5",
            "models-3",
            "protocols-4",
            "{\"canon\":\"hash-7\"}")),
        new BookProductionService(db, currentUser, new PassingBookValidationService()));
    }

    private sealed class PassingBookValidationService : IBookValidationService
    {
        public Task<BookValidationReport> ValidateAsync(BookValidationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BookValidationReport { OverallStatus = "validated" });
    }

    private static CreateCreativeGoalCommand CreateCommand(string sourceSessionId = "session-1") => new(
        ProjectId: "project-1",
        SourceSessionId: sourceSessionId,
        IdempotencyKey: "goal-idempotency",
        TotalCostLimit: 20m,
        Contract: CreateContract());

    private static CreativeGoalContract CreateContract() => new(
        GoalType: "write_batch",
        CollaborationMode: "coauthor",
        HumanReadableObjective: "创作前三章候选正文",
        TargetChapterRangeJson: "{\"start\":1,\"end\":3}",
        SuccessCriteria: ["三章均通过连续性审校"],
        MustPreserve: ["主角不知道自己的真实身份"],
        MustHappen: ["第三章出现第一次身份线索"],
        MustNotChange: ["第一人称有限视角"],
        AcceptancePolicyJson: "{\"mode\":\"chapter\"}",
        ReworkPolicyJson: "{\"local\":2,\"chapter\":1}");

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static async Task SeedProjectAsync(NovelAgentDbContext db)
    {
        db.Users.Add(new User
        {
            Id = "authenticated-user",
            Username = "author",
            Email = "author@example.com",
            PasswordHash = "hash",
            Role = "author"
        });
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-1",
            UserId = "authenticated-user",
            Title = "测试小说"
        });
        db.AgentSessions.Add(new AgentSession
        {
            Id = "session-1",
            UserId = "authenticated-user",
            ProjectId = "project-1"
        });
        await db.SaveChangesAsync();
    }

    private sealed class StubGoalBaselineProvider(GoalBaselines baselines) : IGoalBaselineProvider
    {
        public Task<GoalBaselines> CaptureAsync(
            string userId,
            string projectId,
            CancellationToken cancellationToken = default) => Task.FromResult(baselines);
    }

    private sealed class StubCommitmentModel(CommitmentAssessment result) : ICommitmentAssessmentModelClient
    {
        public int CallCount { get; private set; }

        public Task<CommitmentAssessment> AssessAsync(
            string userId,
            CommitmentAssessmentRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class StubCompletionService(string result) : IWritingModelCompletionService
    {
        public Task<string> CompleteAsync(
            string userId,
            string system,
            string user,
            CancellationToken ct = default) => Task.FromResult(result);
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
