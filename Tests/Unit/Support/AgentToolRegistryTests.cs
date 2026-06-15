using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.AgentTools;
using TM.Web.NovelAgentWeb.Support;
using Xunit;
using AgentRunEntity = TM.Web.NovelAgentWeb.Data.Entities.AgentRun;
using ChapterEntity = TM.Web.NovelAgentWeb.Data.Entities.Chapter;
using KnowledgeBaseEntity = TM.Web.NovelAgentWeb.Data.Entities.KnowledgeBase;
using NovelProjectEntity = TM.Web.NovelAgentWeb.Data.Entities.NovelProject;
using UserEntity = TM.Web.NovelAgentWeb.Data.Entities.User;
using VolumeEntity = TM.Web.NovelAgentWeb.Data.Entities.Volume;

namespace Tests.Unit.Support;

public class AgentToolRegistryTests
{
    [Fact]
    public void ListToolSchemasForPhase_ExposesProjectResolutionTool()
    {
        var registry = new AgentToolRegistry(
            new UserSettingsManager(string.Empty, "test-project", null, null),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<AgentToolRegistry>.Instance);

        var conversationTools = registry.ListToolSchemasForPhase(ConversationPhase.Conversation);
        var planningTools = registry.ListToolSchemasForPhase(ConversationPhase.Planning);

        Assert.Contains(conversationTools, tool => tool.Name == "ResolveNovelProject");
        Assert.Contains(planningTools, tool => tool.Name == "ResolveNovelProject");
    }

    [Fact]
    public void ToolPolicy_AllowsDiscoveredProjectResolutionTool()
    {
        var policy = new ToolPolicyEngine();
        var context = new AgentObservationContext
        {
            AvailableTools = new List<AgentToolDefinition>
            {
                new()
                {
                    Name = "ResolveNovelProject",
                    Risk = "Low"
                }
            }
        };

        var result = policy.BeforeCall(
            new AgentToolCall
            {
                Name = "ResolveNovelProject",
                Arguments = new Dictionary<string, string>
                {
                    ["mode"] = "bind_existing",
                    ["projectTitle"] = "天命"
                }
            },
            new AgentSession { UserId = "user-1", SessionId = "session-1" },
            new StoryBibleDocument(),
            context,
            confirmed: false);

        Assert.True(result.AllowsExecution);
    }

    [Fact]
    public async Task ToolSearchAll_DoesNotExposeLegacyProjectStartTool()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        var registry = new AgentToolRegistry(
            new UserSettingsManager(string.Empty, "test-project", null, null),
            services.BuildServiceProvider(),
            NullLogger<AgentToolRegistry>.Instance);

        var result = await registry.ExecuteAsync(
            new AgentToolCall
            {
                Name = "tool_search",
                Arguments = new Dictionary<string, string>
                {
                    ["phase"] = "All"
                }
            },
            new AgentSession { UserId = "user-1", SessionId = "session-1" },
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("ResolveNovelProject", result.Message);
        Assert.DoesNotContain("StartNewNovelProject", result.Message);
    }

    [Fact]
    public async Task QueryWorkspaceState_ProjectlessAdminReadsVisibleWorkspaceWithoutBindingProject()
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString("N");
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddSingleton<IToolSearchCacheService, RecordingToolSearchCacheService>();
        var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            db.Users.AddRange(
                new UserEntity
                {
                    Id = "admin-1",
                    Username = "admin",
                    Email = "admin@example.com",
                    PasswordHash = "hash",
                    Role = "admin"
                },
                new UserEntity
                {
                    Id = "user-1",
                    Username = "author",
                    Email = "author@example.com",
                    PasswordHash = "hash",
                    Role = "author"
                });
            db.NovelProjects.Add(new NovelProjectEntity
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "天命旧书",
                Genre = "玄幻",
                Status = "draft",
                UpdatedAt = DateTime.UtcNow
            });
            db.Volumes.Add(new VolumeEntity
            {
                Id = "volume-1",
                ProjectId = "project-1",
                Title = "第一卷",
                VolumeNumber = 1
            });
            db.Chapters.Add(new ChapterEntity
            {
                Id = "chapter-1",
                ProjectId = "project-1",
                Title = "第一章",
                ChapterNumber = 1,
                Status = "committed"
            });
            db.KnowledgeBases.Add(new KnowledgeBaseEntity
            {
                Id = "knowledge-1",
                UserId = "user-1",
                EntryType = "style",
                Title = "爽点节奏",
                Content = "三段式推进"
            });
            db.AgentRuns.Add(new AgentRunEntity
            {
                Id = "run-1",
                UserId = "user-1",
                ProjectId = "project-1",
                RunType = "chapter_generation",
                Status = "running",
                StartedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var registry = new AgentToolRegistry(
            new UserSettingsManager(string.Empty, "test-project", null, null),
            provider,
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "admin-1",
            SessionId = "session-1",
            ActiveProjectId = string.Empty
        };

        var result = await registry.ExecuteAsync(
            new AgentToolCall { Name = "QueryWorkspaceState" },
            session,
            new StoryBibleDocument(),
            confirmed: true,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(string.Empty, session.ActiveProjectId);
        var state = Assert.IsType<AgentWorkspaceState>(result.Data);
        var project = Assert.Single(state.VisibleProjects);
        Assert.Equal("天命旧书", project.Title);
        Assert.Equal("user-1", project.OwnerUserId);
        Assert.False(project.IsOwnedByCurrentUser);
        Assert.Equal(1, project.ChapterCount);
        Assert.Equal(1, project.CommittedChapterCount);
        Assert.Equal(1, state.KnowledgeBase.TotalCount);
        Assert.Equal(1, state.Workflow.ActiveRunCount);
        Assert.Contains("不会自动绑定", string.Join("\n", state.Notes));
    }

    private sealed class RecordingToolSearchCacheService : IToolSearchCacheService
    {
        public Task<ToolSearchCacheLookup> GetAsync(
            AgentSession session,
            string phase,
            CancellationToken ct = default) =>
            Task.FromResult(ToolSearchCacheLookup.Miss);

        public Task SaveAsync(
            AgentSession session,
            string phase,
            IReadOnlyList<ToolSchema> tools,
            CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
