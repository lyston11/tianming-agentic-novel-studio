using System.Reflection;
using Tianming.NovelAgent.Application.Conversation;
using Tianming.NovelAgent.Contracts.Streaming;
using Tianming.NovelAgent.Domain.Goals;

namespace Tests.AgentArchitecture;

public sealed class ArchitectureDependencyTests
{
    [Fact]
    public void Domain_has_no_outbound_project_or_framework_dependencies()
    {
        var references = ReferenceNames(typeof(CreativeGoal).Assembly);

        Assert.DoesNotContain(references, IsForbiddenFramework);
        Assert.DoesNotContain(references, IsTianmingProject);
    }

    [Fact]
    public void Contracts_has_no_outbound_project_or_framework_dependencies()
    {
        var references = ReferenceNames(typeof(AgentEventEnvelope<>).Assembly);

        Assert.DoesNotContain(references, IsForbiddenFramework);
        Assert.DoesNotContain(references, IsTianmingProject);
    }

    [Fact]
    public void Application_depends_only_on_domain_and_contracts()
    {
        var references = ReferenceNames(typeof(ConversationApplicationService).Assembly);
        var projectReferences = references.Where(IsTianmingProject).Order().ToArray();

        Assert.Equal(
            ["Tianming.NovelAgent.Contracts", "Tianming.NovelAgent.Domain"],
            projectReferences);
        Assert.DoesNotContain(references, IsForbiddenFramework);
    }

    private static string[] ReferenceNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(x => x.Name!).ToArray();

    private static bool IsTianmingProject(string name) => name.StartsWith("Tianming.NovelAgent.", StringComparison.Ordinal);

    private static bool IsForbiddenFramework(string name) =>
        name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.Agents", StringComparison.Ordinal)
        || name.StartsWith("OpenAI", StringComparison.Ordinal)
        || name.Contains("NovelAgentWeb", StringComparison.Ordinal);
}
