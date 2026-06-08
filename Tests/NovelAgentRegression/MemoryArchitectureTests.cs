using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace TM.Tests.NovelAgentRegression;

/// <summary>
/// Mock catalog for testing ProjectRouter without file I/O.
/// Tracks projects in memory to support testing of project resolution logic.
/// </summary>
internal sealed class MockNovelProjectCatalog
{
    private readonly NovelProjectCatalogDocument _document = new();

    public Task<NovelProjectCatalogDocument> GetAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_document);
    }

    public Task<NovelProjectInfo> GetActiveAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_document.ActiveProjectId) || _document.Projects.Count == 0)
            throw new InvalidOperationException("No active project");

        return Task.FromResult(_document.Projects.First(p => string.Equals(p.Id, _document.ActiveProjectId, StringComparison.OrdinalIgnoreCase)));
    }

    public Task AddAsync(NovelProjectInfo project, CancellationToken ct = default)
    {
        _document.Projects.Add(project);
        return Task.CompletedTask;
    }

    public Task<NovelProjectInfo> ActivateAsync(string projectId, CancellationToken ct = default)
    {
        var project = _document.Projects.FirstOrDefault(p => string.Equals(p.Id, projectId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Project not found: {projectId}");

        _document.ActiveProjectId = project.Id;
        return Task.FromResult(project);
    }
}

/// <summary>
/// Mock workspace for testing ProjectRouter.
/// Provides minimal workspace properties needed for project routing tests.
/// </summary>
internal sealed class MockNovelAgentWorkspace
{
    public string ProjectName { get; init; } = "TestProject";
    public string StorageRoot { get; init; } = "./test-storage";
}

public sealed class MemoryArchitectureTests
{
    [Fact]
    public void UserProfile_HasExpectedProperties()
    {
        var profile = new UserProfile
        {
            UserId = "user123",
            StylePreferences = new() { ["tone"] = "幽默轻松" },
            GenreHabits = new() { ["科幻"] = 5 },
            ConfirmationTolerance = "medium",
            GlobalConstraints = new() { "禁止血腥暴力描写" },
        };

        Assert.Equal("user123", profile.UserId);
        Assert.Equal("幽默轻松", profile.StylePreferences["tone"]);
        Assert.Equal(5, profile.GenreHabits["科幻"]);
        Assert.Single(profile.GlobalConstraints);
    }

    [Fact]
    public void SessionContext_CanHaveNullActiveProjectId()
    {
        var session = new SessionContext
        {
            SessionId = "sess1",
            ActiveProjectId = null,
        };

        Assert.Null(session.ActiveProjectId);
    }

    [Fact]
    public void AgentRuntimeContext_CombinesAllThreeTiers()
    {
        var context = new AgentRuntimeContext
        {
            User = new UserProfile { UserId = "user1" },
            ActiveProject = null,
            Session = new SessionContext { SessionId = "sess1" },
            Mission = new AgentMissionState { CurrentGoal = "test" },
            MissionPlan = new AgentMissionPlan(),
        };

        Assert.Equal("user1", context.User.UserId);
        Assert.Null(context.ActiveProject);
        Assert.Equal("sess1", context.Session.SessionId);
        Assert.Equal("test", context.Mission.CurrentGoal);
        Assert.NotNull(context.MissionPlan);
    }

    [Fact]
    public async Task ClassifyIntentAsync_NewBookKeyword_ReturnsCreateNew()
    {
        var router = new ProjectRouter(null!, null!);
        var session = new SessionContext { SessionId = "s1" };

        var intent = await router.ClassifyIntentAsync("我要写一本新书", session, CancellationToken.None);

        Assert.Equal(UserProjectIntent.CreateNew, intent);
    }

    [Fact]
    public async Task ClassifyIntentAsync_ContinueKeyword_ReturnsContinueExisting()
    {
        var router = new ProjectRouter(null!, null!);
        var session = new SessionContext { SessionId = "s1" };

        var intent = await router.ClassifyIntentAsync("续写之前的小说", session, CancellationToken.None);

        Assert.Equal(UserProjectIntent.ContinueExisting, intent);
    }

    [Fact]
    public async Task ResolveProjectAsync_CreateNewIntent_CreatesNewProject()
    {
        var catalog = new MockNovelProjectCatalog();
        var workspace = new MockNovelAgentWorkspace();
        var router = new ProjectRouter(catalog, workspace);
        var session = new SessionContext { SessionId = "s1" };

        var result = await router.ResolveProjectAsync("我要写一本新书", session, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Project);
        Assert.Equal(result.Project.Id, session.ActiveProjectId);
    }
}
