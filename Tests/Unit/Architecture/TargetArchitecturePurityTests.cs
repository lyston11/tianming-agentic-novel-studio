using System.Xml.Linq;
using Xunit;

namespace Tests.Unit.Architecture;

public sealed class TargetArchitecturePurityTests
{
    [Fact]
    public void LegacyLoopProductionComponents_AreAbsent()
    {
        var root = RepositoryRoot();
        var obsolete = new[]
        {
            "Web/NovelAgentWeb/Support/AgentRuntime.cs",
            "Web/NovelAgentWeb/Support/AgentKernel.cs",
            "Web/NovelAgentWeb/Support/AgentToolCallingClient.cs",
            "Web/NovelAgentWeb/Support/AgentToolRegistry.cs",
            "Web/NovelAgentWeb/Services/AgentRuntime/AgentRuntimeQueue.cs",
            "Web/NovelAgentWeb/Services/AgentRuntime/AgentInterruptService.cs",
            "Web/NovelAgentWeb/Services/AgentRuntime/AgentRuntimeEventStreamPump.cs",
            "Web/NovelAgentWeb/Services/AgentRuntime/AgentRuntimeRunService.cs",
            "Web/NovelAgentWeb/Services/AgentRuntime/AgentRuntimeWorker.cs",
            "Web/NovelAgentWeb/Services/AgentTools/AgentToolExecutionLedger.cs",
            "Web/NovelAgentWeb/Services/AgentTools/ToolSearchCacheService.cs",
            "Web/NovelAgentWeb/Services/Production/ProductionTruthChapterIdentityMigrationService.cs",
            "Web/NovelAgentWeb/Services/Repositories/StoryBibleRepository.cs"
        };

        Assert.All(obsolete, path => Assert.False(File.Exists(Path.Combine(root, path)), path));
    }

    [Fact]
    public void Program_RegistersDurableGoalKernelAndUnifiedExecutionEnvelopes()
    {
        var source = Read("Web/NovelAgentWeb/Program.cs");

        Assert.Contains("AddScoped<IGoalCompiler, GoalCompiler>", source, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IKernelTaskScheduler, PostgresKernelTaskScheduler>", source, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IKernelTaskExecutor, KernelTaskExecutionRouter>", source, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IGoalModelExecutionEnvelope, GoalModelExecutionEnvelope>", source, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IGoalEmbeddingExecutionEnvelope, GoalEmbeddingExecutionEnvelope>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentRuntimeWorker", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentToolRegistry", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GoalCompiler_IsDeterministicAndMovesCreativeAnalysisIntoDurableDag()
    {
        var source = Read("Web/NovelAgentWeb/Services/Goals/GoalCompiler.cs");

        Assert.Contains("AnalyzeCreativeRequirements", source, StringComparison.Ordinal);
        Assert.Contains("CreativeRequirements", source, StringComparison.Ordinal);
        Assert.Contains("KernelTaskFailurePolicy.MaxAttempts", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IWritingModelCompletionService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IGoalTaskSupplementModelClient", source, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelPrompts_AlwaysContainEffectiveGoalContract()
    {
        var router = Read("Web/NovelAgentWeb/Services/Kernels/KernelTaskExecutionRouter.cs");
        var client = Read("Web/NovelAgentWeb/Services/Kernels/DefaultKernelStructuredModelClient.cs");

        Assert.Contains("CreativeGoalRevisionProjector.Project", router, StringComparison.Ordinal);
        Assert.Contains("context.GoalContract", client, StringComparison.Ordinal);
        Assert.Contains("context.GoalContract", router, StringComparison.Ordinal);
    }

    [Fact]
    public void Sse_UsesOwnedSessionAndAuthorizationHeaderWithoutQueryToken()
    {
        var controller = Read("Web/NovelAgentWeb/Controllers/AgentController.cs");
        var frontend = Read("Web/NovelAgentWeb.Frontend/src/api/index.ts");
        var program = Read("Web/NovelAgentWeb/Program.cs");

        Assert.Contains("GetSessionByIdAsync(sessionId, userId, isAdmin", controller, StringComparison.Ordinal);
        Assert.Contains("SubscribeEvents(userId, sessionId", controller, StringComparison.Ordinal);
        Assert.Contains("headers.Authorization", frontend, StringComparison.Ordinal);
        Assert.Contains("fetch(url", frontend, StringComparison.Ordinal);
        Assert.DoesNotContain("new EventSource", frontend, StringComparison.Ordinal);
        Assert.DoesNotContain("params.set('token'", frontend, StringComparison.Ordinal);
        Assert.DoesNotContain("OnMessageReceived", program, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthenticationLogging_DoesNotSerializeTokens()
    {
        var auth = Read("Web/NovelAgentWeb/Controllers/AuthController.cs");
        var program = Read("Web/NovelAgentWeb/Program.cs");

        Assert.DoesNotContain("JsonSerializer.Serialize(response", auth, StringComparison.Ordinal);
        Assert.DoesNotContain("Response JSON", auth, StringComparison.Ordinal);
        Assert.DoesNotContain("JWT Token Received", program, StringComparison.Ordinal);
        Assert.DoesNotContain("authToken.Substring", program, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectFiles_DoNotCompileSourcesOutsideRepository()
    {
        var root = RepositoryRoot();
        var projectFiles = Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path));
        var offenders = new List<string>();
        foreach (var projectFile in projectFiles)
        {
            var projectDirectory = Path.GetDirectoryName(projectFile)!;
            var document = XDocument.Load(projectFile);
            foreach (var compile in document.Descendants().Where(element => element.Name.LocalName == "Compile"))
            {
                var include = compile.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(include) || include.Contains('*', StringComparison.Ordinal))
                    continue;
                var fullPath = Path.GetFullPath(Path.Combine(projectDirectory, include));
                if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    offenders.Add($"{projectFile}: {include}");
            }
        }

        Assert.Empty(offenders);
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null &&
               (!File.Exists(Path.Combine(directory.FullName, "README.md")) ||
                !Directory.Exists(Path.Combine(directory.FullName, "Web", "NovelAgentWeb"))))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static bool IsBuildOutput(string path)
    {
        var normalized = path.Replace(Path.DirectorySeparatorChar, '/');
        return normalized.Contains("/bin/", StringComparison.Ordinal) ||
               normalized.Contains("/obj/", StringComparison.Ordinal);
    }
}
