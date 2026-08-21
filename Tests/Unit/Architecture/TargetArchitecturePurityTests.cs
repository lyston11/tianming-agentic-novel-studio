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
    public void KernelTaskScheduler_WritesOnlyThroughAgentControlOwner()
    {
        var source = Read("Web/NovelAgentWeb/Services/Goals/PostgresKernelTaskScheduler.cs");

        Assert.Contains("AgentControlDbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NovelAgentDbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IBookProductionTransitionService", source, StringComparison.Ordinal);
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
    public void Director_ReadsModelContextOnlyThroughContextAssembler()
    {
        var source = Read("Web/NovelAgentWeb/Services/Goals/TargetArchitectureDirector.cs");

        Assert.Contains("IConversationContextAssembler", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NovelAgentDbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IKnowledgeQueryTool", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectCollaborationDecisions", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GoalWorkflowController_SubmitsProductionChangesThroughTransitionService()
    {
        var source = Read("Web/NovelAgentWeb/Controllers/GoalWorkflowController.cs");

        Assert.Contains("IBookProductionTransitionService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IGoalControlService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IBookProductionService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentChatCompatEntry_PersistsOnlyThroughApplicationConversation()
    {
        var source = Read("Web/NovelAgentWeb/Controllers/AgentController.cs");

        Assert.Contains("ConversationApplicationService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentTurnCoordinator", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IChatHistoryRepository", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IAgentForegroundTurnRunner", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentSessionResume_ReplaysFromDurableConversationMessages()
    {
        var source = Read("Web/NovelAgentWeb/Services/AgentSessions/AgentSessionService.cs");

        Assert.Contains("ReadMessageRecordsAsync", source, StringComparison.Ordinal);
        Assert.Contains("pi.assistant.v1", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentChatTurns", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyControlPlaneCompatibilityWriters_DelegateWithoutSavingThroughWebDbContext()
    {
        var paths = new[]
        {
            "Web/NovelAgentWeb/Services/Goals/CreativeGoalService.cs",
            "Web/NovelAgentWeb/Services/Goals/GoalCompiler.cs",
            "Web/NovelAgentWeb/Services/Goals/BookProductionTransitionService.cs",
            "Web/NovelAgentWeb/Controllers/GoalWorkflowController.cs"
        };

        var forbiddenAdds = new[]
        {
            "CreativeGoals.Add",
            "GoalRevisions.Add",
            "BookProductions.Add",
            "ProductionBatches.Add",
            "TaskGraphVersions.Add",
            "KernelTasks.Add",
            "KernelArtifacts.Add"
        };
        foreach (var path in paths)
        {
            var source = Read(path);
            Assert.Contains("ILegacyControlPlaneCommands", source, StringComparison.Ordinal);
            Assert.All(forbiddenAdds, mutation => Assert.DoesNotContain(mutation, source, StringComparison.Ordinal));
            if (!path.EndsWith("GoalCompiler.cs", StringComparison.Ordinal))
                Assert.DoesNotContain("SaveChangesAsync", source, StringComparison.Ordinal);
        }

        var controller = Read("Web/NovelAgentWeb/Controllers/GoalWorkflowController.cs");
        Assert.DoesNotContain("KernelArtifacts.Add", controller, StringComparison.Ordinal);
        var branches = Read("Web/NovelAgentWeb/Services/Canon/CanonBranchService.cs");
        Assert.Contains("CreateArtifactAsync", branches, StringComparison.Ordinal);
        Assert.DoesNotContain("new KernelArtifact", branches, StringComparison.Ordinal);
    }

    [Fact]
    public void GoalProgressPublisher_WritesDurableOutboxWithoutDirectLiveDelivery()
    {
        var source = Read("Web/NovelAgentWeb/Services/Goals/GoalProgressEventPublisher.cs");
        var publisher = source[..source.IndexOf("public interface IGoalProgressEventDelivery", StringComparison.Ordinal)];

        Assert.Contains("_db.OutboxEvents.Add", publisher, StringComparison.Ordinal);
        Assert.Contains("publish_goal_progress", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentSseEventBus", publisher, StringComparison.Ordinal);
        Assert.DoesNotContain("IAgentRuntimeEventFanout", publisher, StringComparison.Ordinal);
    }

    [Fact]
    public void ConversationContext_IncludesPendingRecoveryIntents()
    {
        var assembler = Read("Web/NovelAgentWeb/Services/Context/AgentContextAssembler.cs");
        var director = Read("Web/NovelAgentWeb/Services/Goals/TargetArchitectureDirector.cs");

        Assert.Contains("item.RequiresConfirmation", assembler, StringComparison.Ordinal);
        Assert.Contains("AgentPendingIntentContext", assembler, StringComparison.Ordinal);
        Assert.Contains("snapshot.PendingIntents", director, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelRouter_ReadsExecutionContextOnlyThroughContextAssembler()
    {
        var source = Read("Web/NovelAgentWeb/Services/Kernels/KernelTaskExecutionRouter.cs");

        Assert.Contains("IAgentContextAssembler", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NovelAgentDbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelArtifacts.AsNoTracking", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GoalContextSnapshots.AsNoTracking", source, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelPrompts_AlwaysContainEffectiveGoalContract()
    {
        var router = Read("Web/NovelAgentWeb/Services/Kernels/KernelTaskExecutionRouter.cs");
        var contexts = Read("Web/NovelAgentWeb/Services/Context/AgentContextAssembler.cs");
        var client = Read("Web/NovelAgentWeb/Services/Kernels/DefaultKernelStructuredModelClient.cs");

        Assert.Contains("IAgentContextAssembler", router, StringComparison.Ordinal);
        Assert.DoesNotContain("NovelAgentDbContext", router, StringComparison.Ordinal);
        Assert.Contains("CreativeGoalRevisionProjector.Project", contexts, StringComparison.Ordinal);
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
