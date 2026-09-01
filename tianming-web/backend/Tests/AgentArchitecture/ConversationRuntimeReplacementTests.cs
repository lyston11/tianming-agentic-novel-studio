using System.Xml.Linq;
using System.Text.Json;
using Tianming.NovelAgent.Application.Conversation;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Contracts.Conversation;
using Tianming.NovelAgent.Domain.Goals;
using Tianming.NovelAgent.Infrastructure.Persistence;

namespace Tests.AgentArchitecture;

public sealed class ConversationRuntimeReplacementTests
{
    /// <summary>
    /// Unparseable provider output degrades to DiscussOnly with the raw text and a
    /// reason, rather than throwing. This is deliberate and differs from the retired
    /// MAF adapter, which threw: a malformed model reply must not surface as a server
    /// error, and it must not be mistaken for an executable decision.
    /// </summary>
    [Fact]
    public async Task Structured_adapter_degrades_unstructured_output_to_discussion()
    {
        IConversationAgentRuntime runtime = new StructuredConversationAgentRuntime(new StubCompletion("not-json"));

        var result = await runtime.RunTurnAsync(
            new ConversationTurnContext(
                "user",
                new UnboundConversationBinding(),
                "session",
                "hello",
                [],
                "correlation"),
            CancellationToken.None);

        Assert.Equal(ConversationDecisionKind.DiscussOnly, result.DecisionKind);
        Assert.Null(result.ProposedContract);
        Assert.Empty(result.ToolCalls ?? []);
        Assert.Equal("not-json", result.AssistantMessage);
        Assert.Contains("not a valid executable decision contract", result.Reason);
    }

    [Fact]
    public async Task Structured_adapter_does_not_require_project_contracts_for_unbound_turns()
    {
        var response = JsonSerializer.Serialize(new
        {
            kind = "proposeGoal",
            message = "Continue clarifying",
            reason = "Malformed project-only decision"
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        IConversationAgentRuntime runtime = new StructuredConversationAgentRuntime(new StubCompletion(response));

        var result = await runtime.RunTurnAsync(
            new ConversationTurnContext(
                "user",
                new UnboundConversationBinding(),
                "session",
                "hello",
                [],
                "correlation"),
            CancellationToken.None);

        Assert.Equal(ConversationDecisionKind.ProposeGoal, result.DecisionKind);
        Assert.Null(result.ProposedContract);
    }

    [Fact]
    public async Task Structured_adapter_maps_workflow_tool_calls()
    {
        var response = JsonSerializer.Serialize(new
        {
            kind = "proposeGoal",
            message = "The goal is ready for confirmation.",
            reason = "Explicit commitment",
            contract = NewContract(),
            toolCalls = new[] { new { name = "confirm_creative_goal", arguments = new { } } }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        IConversationAgentRuntime runtime = new StructuredConversationAgentRuntime(new StubCompletion(response));

        var result = await runtime.RunTurnAsync(
            new ConversationTurnContext(
                "user",
                new BoundConversationBinding("project"),
                "session",
                "start",
                [],
                "correlation"),
            CancellationToken.None);

        Assert.Equal("confirm_creative_goal", Assert.Single(result.ToolCalls!).Name);
        Assert.Equal(ConversationDecisionKind.ProposeGoal, result.DecisionKind);
        Assert.Equivalent(NewContract(), result.ProposedContract, strict: true);
    }

    /// <summary>
    /// The Microsoft Agent Framework adapter was retired on 2026-09-01 (task
    /// 09-01-retire-legacy-runtimes) because it had zero production callers.
    /// Covers Infrastructure too, not just Application: the package reference lived
    /// in Infrastructure, so an Application-only assertion would miss it coming back.
    /// </summary>
    [Fact]
    public void Conversation_runtime_assemblies_do_not_reference_the_agent_framework()
    {
        // StructuredConversationAgentRuntime lives in Application, so it cannot
        // stand in for Infrastructure here; use a type that really is Infrastructure.
        var assemblies = new[]
        {
            typeof(IConversationAgentRuntime).Assembly,           // Application
            typeof(AgentControlDbContext).Assembly,               // Infrastructure
            typeof(CreativeGoal).Assembly,                        // Domain
        };

        foreach (var assembly in assemblies)
        {
            var referenced = assembly.GetReferencedAssemblies().Select(x => x.Name!).ToArray();
            Assert.DoesNotContain(referenced, name => name!.StartsWith("Microsoft.Agents", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// The assembly-level guard above only sees references the compiler actually
    /// emitted, so a PackageReference that nothing uses yet slips past it. This
    /// checks the project files directly, which is what catches the dependency
    /// being reintroduced before any code depends on it.
    /// </summary>
    [Fact]
    public void Agent_control_projects_do_not_declare_the_agent_framework_package()
    {
        var backendRoot = BackendRoot();
        var projects = new[]
        {
            "Tianming.NovelAgent.Domain",
            "Tianming.NovelAgent.Contracts",
            "Tianming.NovelAgent.Application",
            "Tianming.NovelAgent.Infrastructure",
        };

        foreach (var project in projects)
        {
            var path = Path.Combine(backendRoot, project, $"{project}.csproj");
            Assert.True(File.Exists(path), $"Project file not found: {path}");

            var packages = XDocument.Load(path)
                .Descendants("PackageReference")
                .Select(x => x.Attribute("Include")?.Value ?? string.Empty)
                .ToArray();

            Assert.DoesNotContain(
                packages,
                name => name.StartsWith("Microsoft.Agents", StringComparison.Ordinal));
        }
    }

    private static string BackendRoot()
    {
        // Markers hold only at tianming-web/backend.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null &&
               (!File.Exists(Path.Combine(directory.FullName, "global.json")) ||
                !Directory.Exists(Path.Combine(directory.FullName, "Tianming.Web"))))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Backend root not found.");
    }

    private static GoalContract NewContract() => new(
        "Write chapter one",
        ProductionMode.SingleChapter,
        new ChapterRange(1, 1),
        ["Complete chapter"],
        [],
        [],
        [],
        "human",
        "directed",
        1m,
        "canon-v1",
        "knowledge-v1",
        "quality-v1",
        "style-v1",
        new Dictionary<string, string>(),
        new Dictionary<string, string>());

    private sealed class StubCompletion(string response) : IConversationTextCompletionPort
    {
        public Task<string> CompleteAsync(
            string userId,
            string systemPrompt,
            string userPrompt,
            CancellationToken cancellationToken) => Task.FromResult(response);
    }
}
