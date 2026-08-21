using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Context;
using TM.Web.NovelAgentWeb.Services.Goals;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using TM.Web.NovelAgentWeb.Services.Memory;
using Xunit;

namespace Tests.Unit.Services.Context;

public sealed class ConversationContextAssemblerTests
{
    [Fact]
    public async Task BuildAsync_Unbound_DoesNotCallProjectMemoryOrKnowledge()
    {
        await using var db = CreateDb();
        db.AgentSessions.Add(new AgentSession
        {
            Id = "session-unbound",
            UserId = "user-1",
            ProjectId = null,
            BindingVersion = 4,
            SessionData = "{}",
            UpdatedAt = Utc(1),
        });
        db.NovelProjects.AddRange(
            Project("project-1", "user-1", "灯城", Utc(2)),
            Project("project-other", "user-2", "他人的书", Utc(3)));
        await db.SaveChangesAsync();

        var projectContexts = new AgentContextAssembler(
            db,
            new ThrowingMemoryStore(),
            new ThrowingKnowledgeQueryTool());
        var history = new StubChatHistoryRepository(new ChatPromptWindowDto(
            null,
            [],
            [new ChatHistoryTurnDto("user", "我想讨论一个会遗忘名字的城市。", Utc(4), "turn-1", 1)]));
        var assembler = new ConversationContextAssembler(db, history, projectContexts);

        var result = await assembler.BuildAsync(new ConversationContextRequest(
            "user-1",
            "session-unbound",
            "先聊设定"));

        var unbound = Assert.IsType<UnboundConversationContext>(result);
        Assert.Equal(4, unbound.BindingVersion);
        var project = Assert.Single(unbound.AccessibleProjects);
        Assert.Equal("project-1", project.ProjectId);
        Assert.Equal("灯城", project.Title);
        Assert.Equal("active", project.Status);
        Assert.Equal(Utc(2), project.UpdatedAt);
        Assert.Single(unbound.Transcript.RecentMessages);
        Assert.All(unbound.GeneralCapabilities, capability => Assert.True(capability.IsReadOnly));
    }

    [Fact]
    public async Task BuildAsync_Bound_OnlyMeaningfulSourceChangesMoveSnapshotVersion()
    {
        await using var db = CreateDb();
        db.AgentSessions.Add(new AgentSession
        {
            Id = "session-bound",
            UserId = "user-1",
            ProjectId = "project-1",
            BindingVersion = 6,
            SessionData = "{}",
            UpdatedAt = Utc(5)
        });
        db.NovelProjects.Add(Project("project-1", "user-1", "灯城", Utc(6)));
        await db.SaveChangesAsync();

        var memory = new RecordingMemoryStore();
        var knowledge = new RecordingKnowledgeQueryTool();
        var projectContexts = new AgentContextAssembler(db, memory, knowledge);
        var assembler = new ConversationContextAssembler(
            db,
            new StubChatHistoryRepository(new ChatPromptWindowDto(null, [], [])),
            projectContexts);

        var first = Assert.IsType<BoundConversationContext>(await assembler.BuildAsync(
            new ConversationContextRequest("user-1", "session-bound", "检查项目状态")));
        var unchanged = Assert.IsType<BoundConversationContext>(await assembler.BuildAsync(
            new ConversationContextRequest("user-1", "session-bound", "检查项目状态")));

        Assert.Equal(6, first.BindingVersion);
        Assert.Equal("6", first.Binding.Version);
        Assert.Equal("project-1", first.Binding.ProjectId);
        Assert.Equal("project-1", memory.ProjectId);
        Assert.Equal("project-1", knowledge.Request!.ProjectId);
        Assert.Equal(first.Binding.Version, Assert.Single(first.ProjectSnapshot.Sources, source => source.SourceType == "binding").Version);
        Assert.Equal(first.ProjectSnapshot.Version, unchanged.ProjectSnapshot.Version);
        Assert.Equal(first.ProjectSnapshot.AllowedTools, first.ProjectTools);
        Assert.NotEmpty(first.ProjectSnapshot.Version);

        var session = await db.AgentSessions.SingleAsync(item => item.Id == "session-bound");
        session.UpdatedAt = Utc(7);
        await db.SaveChangesAsync();

        var changed = Assert.IsType<BoundConversationContext>(await assembler.BuildAsync(
            new ConversationContextRequest("user-1", "session-bound", "检查项目状态")));

        Assert.Equal(first.Binding.Version, changed.Binding.Version);
        Assert.Equal(first.ProjectSnapshot.Version, changed.ProjectSnapshot.Version);

        var project = await db.NovelProjects.SingleAsync(item => item.Id == "project-1");
        project.UpdatedAt = Utc(8);
        await db.SaveChangesAsync();

        var projectChanged = Assert.IsType<BoundConversationContext>(await assembler.BuildAsync(
            new ConversationContextRequest("user-1", "session-bound", "检查项目状态")));

        Assert.Equal(first.Binding.Version, projectChanged.Binding.Version);
        Assert.NotEqual(first.ProjectSnapshot.Version, projectChanged.ProjectSnapshot.Version);
    }

    [Fact]
    public async Task BuildAsync_BoundProjectNoLongerAccessible_IsRejectedBeforeTranscriptOrProjectLoading()
    {
        await using var db = CreateDb();
        db.AgentSessions.Add(new AgentSession
        {
            Id = "session-revoked",
            UserId = "user-1",
            ProjectId = "project-revoked",
            SessionData = "{}",
            UpdatedAt = Utc(5)
        });
        db.NovelProjects.Add(Project("project-revoked", "user-2", "他人的书", Utc(6)));
        await db.SaveChangesAsync();

        var history = new StubChatHistoryRepository(new ChatPromptWindowDto(null, [], []));
        var assembler = new ConversationContextAssembler(
            db,
            history,
            new AgentContextAssembler(
                db,
                new ThrowingMemoryStore(),
                new ThrowingKnowledgeQueryTool()));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => assembler.BuildAsync(
            new ConversationContextRequest("user-1", "session-revoked", "不应加载项目")));

        Assert.Equal(0, history.PromptWindowRequests);
    }

    [Fact]
    public async Task BuildAsync_ArchivedSession_IsRejectedBeforeTranscriptOrProjectLoading()
    {
        await using var db = CreateDb();
        db.AgentSessions.Add(new AgentSession
        {
            Id = "session-archived",
            UserId = "user-1",
            ProjectId = "project-1",
            IsArchived = true,
            SessionData = "{}",
            UpdatedAt = Utc(5)
        });
        await db.SaveChangesAsync();

        var history = new StubChatHistoryRepository(new ChatPromptWindowDto(null, [], []));
        var assembler = new ConversationContextAssembler(
            db,
            history,
            new AgentContextAssembler(
                db,
                new ThrowingMemoryStore(),
                new ThrowingKnowledgeQueryTool()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => assembler.BuildAsync(
            new ConversationContextRequest("user-1", "session-archived", "不应继续")));

        Assert.Equal(0, history.PromptWindowRequests);
    }

    [Fact]
    public async Task ProjectScopedBuilders_RejectMissingProjectIdBeforeLoadingDependencies()
    {
        await using var db = CreateDb();
        var assembler = new AgentContextAssembler(
            db,
            new ThrowingMemoryStore(),
            new ThrowingKnowledgeQueryTool());

        await Assert.ThrowsAsync<ArgumentException>(() => assembler.BuildAsync(new AgentContextRequest(
            AgentContextProfile.GoalCommit,
            "user-1",
            "",
            "session-1",
            "commit")));

        var claim = new KernelTaskClaim(
            "task-1",
            "user-1",
            "",
            "goal-1",
            "graph-1",
            null,
            "chapter",
            "write",
            1,
            "worker-1",
            Utc(8));
        await Assert.ThrowsAsync<InvalidOperationException>(() => assembler.BuildKernelExecutionAsync(claim));
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static NovelProject Project(string id, string userId, string title, DateTime updatedAt) => new()
    {
        Id = id,
        UserId = userId,
        Title = title,
        Status = "active",
        CreatedAt = updatedAt.AddMinutes(-1),
        UpdatedAt = updatedAt
    };

    private static DateTime Utc(int minute) =>
        new(2026, 8, 20, 0, minute, 0, DateTimeKind.Utc);

    private sealed class ThrowingMemoryStore : IMemoryStore
    {
        public Task<AgentMemoryBundle> ReadAsync(
            string userId,
            string projectId,
            string sessionId,
            string? goalId,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Project memory must not be loaded.");
    }

    private sealed class RecordingMemoryStore : IMemoryStore
    {
        public string? ProjectId { get; private set; }

        public Task<AgentMemoryBundle> ReadAsync(
            string userId,
            string projectId,
            string sessionId,
            string? goalId,
            CancellationToken cancellationToken = default)
        {
            ProjectId = projectId;
            return Task.FromResult(new AgentMemoryBundle(
                new AgentMemoryContextDto(
                    new ChatMemoryContext(null, [], []),
                    new SessionMemory(),
                    new ProjectMemory(),
                    new TM.Web.NovelAgentWeb.Services.Memory.AuthorMemory(),
                    new ExecutionMemory()),
                []));
        }
    }

    private sealed class ThrowingKnowledgeQueryTool : IKnowledgeQueryTool
    {
        public string Name => KnowledgeQueryTool.ToolName;

        public Task<KnowledgeQueryResult> ExecuteAsync(
            KnowledgeQueryRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Project knowledge must not be loaded.");
    }

    private sealed class RecordingKnowledgeQueryTool : IKnowledgeQueryTool
    {
        public string Name => KnowledgeQueryTool.ToolName;
        public KnowledgeQueryRequest? Request { get; private set; }

        public Task<KnowledgeQueryResult> ExecuteAsync(
            KnowledgeQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new KnowledgeQueryResult(
                new AgentKnowledgeContext(
                    Name,
                    "retrieve",
                    "current_project",
                    "knowledge-v1",
                    "catalog-v1",
                    request.Query ?? string.Empty,
                    0,
                    [],
                    [],
                    false),
                [],
                [],
                []));
        }
    }

    private sealed class StubChatHistoryRepository(ChatPromptWindowDto promptWindow) : IChatHistoryRepository
    {
        public int PromptWindowRequests { get; private set; }

        public Task AppendAsync(string userId, string? projectId, string sessionId, string role, string content, CancellationToken ct = default, AgentKnowledgeContext? knowledge = null) =>
            Task.CompletedTask;

        public Task<bool> ReplaceLastAssistantTurnAsync(string userId, string? projectId, string sessionId, string expectedContent, string replacementContent, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task SaveSummaryAsync(string userId, string? projectId, string sessionId, int startTurn, int endTurn, string summaryType, string content, IReadOnlyList<string> keyDecisions, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<ChatPromptWindowDto> GetPromptWindowAsync(string userId, string? projectId, string sessionId, CancellationToken ct = default)
        {
            PromptWindowRequests++;
            return Task.FromResult(promptWindow);
        }

        public Task<IReadOnlyList<ChatHistoryTurnDto>> GetHotWindowAsync(string userId, string? projectId, string sessionId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ChatHistoryTurnDto>>(promptWindow.RecentMessages);
    }
}
