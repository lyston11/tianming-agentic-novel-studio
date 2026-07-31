using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Goals;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Services.Goals;

public sealed class TargetArchitectureDirectorTests
{
    [Fact]
    public async Task TryHandleAsync_AssessesFullDialogueWithoutStartingLegacyProduction()
    {
        await using var db = CreateDb();
        db.NovelProjects.Add(new TM.Web.NovelAgentWeb.Data.Entities.NovelProject
        {
            Id = "project-1",
            UserId = "user-1",
            Title = "灯城",
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.AgentSessions.Add(new TM.Web.NovelAgentWeb.Data.Entities.AgentSession
        {
            Id = "session-1",
            UserId = "user-1",
            ProjectId = "project-1",
            SessionData = "{}"
        });
        await db.SaveChangesAsync();

        var currentUser = new StubCurrentUserService("user-1");
        var chat = new RecordingChatHistoryRepository([
            new ChatHistoryTurnDto("user", "先讨论主角的代价", DateTime.UtcNow, "turn-1", 1),
            new ChatHistoryTurnDto("assistant", "可以先比较三种代价", DateTime.UtcNow, "turn-2", 2)
        ]);
        var assessment = new CommitmentAssessment(
            DialogueCommitmentState.Proposed,
            GoalAuthorizationKind.None,
            0.91,
            true,
            "目标已形成，但尚未授权。",
            new CreativeGoalContract(
                "chapter_batch",
                "coauthor",
                "写出三章候选，突出能力代价",
                "{\"start\":1,\"end\":3}",
                ["代价逐章升级"],
                ["主角动机"],
                ["第三章揭示代价来源"],
                ["不得改写世界核心规则"],
                "{}",
                "{}"));
        var commitments = new RecordingCommitmentService(assessment);
        var sessions = new AgentSessionManager(db, currentUser, chat);
        var director = new TargetArchitectureDirector(
            sessions,
            currentUser,
            chat,
            new CollaborationMemoryService(db),
            commitments,
            db);

        var result = await director.TryHandleAsync("session-1", "那就按第二种方案形成三章目标", CancellationToken.None);

        Assert.False(result.StartBackground);
        Assert.NotNull(result.Response);
        Assert.Equal("goal_proposed", result.Response!.Phase);
        Assert.Equal(DialogueCommitmentState.Proposed, result.Response.Director!.State);
        Assert.Contains(commitments.Request!.Dialogue, item => item.Content == "先讨论主角的代价");
        Assert.Contains(commitments.Request.Dialogue, item => item.Content == "那就按第二种方案形成三章目标");
        Assert.Empty(await db.CreativeGoals.ToListAsync());
        var dialogueState = Assert.Single(await db.SessionDialogueStates.ToListAsync());
        Assert.Equal(CollaborationMemoryKind.CommitmentJudgment.ToString(), dialogueState.MemoryKind);
        Assert.Equal("pending", dialogueState.Status);
        Assert.Equal(4, chat.Appended.Count);
        Assert.Equal("assistant", chat.Appended[^1].Role);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private sealed class RecordingCommitmentService(CommitmentAssessment response) : ICommitmentAssessmentService
    {
        public CommitmentAssessmentRequest? Request { get; private set; }

        public Task<CommitmentAssessment> AssessAsync(
            CommitmentAssessmentRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(response);
        }
    }

    private sealed class RecordingChatHistoryRepository(IReadOnlyList<ChatHistoryTurnDto> initial) : IChatHistoryRepository
    {
        public List<ChatHistoryTurnDto> Appended { get; } = initial.ToList();

        public Task AppendAsync(string userId, string? projectId, string sessionId, string role, string content, CancellationToken ct = default)
        {
            Appended.Add(new ChatHistoryTurnDto(role, content, DateTime.UtcNow, $"turn-{Appended.Count + 1}", Appended.Count + 1));
            return Task.CompletedTask;
        }

        public Task<bool> ReplaceLastAssistantTurnAsync(string userId, string? projectId, string sessionId, string expectedContent, string replacementContent, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task SaveSummaryAsync(string userId, string? projectId, string sessionId, int startTurn, int endTurn, string summaryType, string content, IReadOnlyList<string> keyDecisions, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<ChatPromptWindowDto> GetPromptWindowAsync(string userId, string? projectId, string sessionId, CancellationToken ct = default) =>
            Task.FromResult(new ChatPromptWindowDto(null, [], Appended.ToArray()));

        public Task<IReadOnlyList<ChatHistoryTurnDto>> GetHotWindowAsync(string userId, string? projectId, string sessionId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ChatHistoryTurnDto>>(Appended.ToArray());
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
