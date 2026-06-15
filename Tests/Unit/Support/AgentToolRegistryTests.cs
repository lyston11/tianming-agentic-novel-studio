using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Services.AgentTools;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

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
