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
            "tianming-web/backend/Tianming.Web/Support/AgentRuntime.cs",
            "tianming-web/backend/Tianming.Web/Support/AgentKernel.cs",
            "tianming-web/backend/Tianming.Web/Support/AgentToolCallingClient.cs",
            "tianming-web/backend/Tianming.Web/Support/AgentToolRegistry.cs",
            "tianming-web/backend/Tianming.Web/Services/AgentRuntime/AgentRuntimeQueue.cs",
            "tianming-web/backend/Tianming.Web/Services/AgentRuntime/AgentInterruptService.cs",
            "tianming-web/backend/Tianming.Web/Services/AgentRuntime/AgentRuntimeEventStreamPump.cs",
            "tianming-web/backend/Tianming.Web/Services/AgentRuntime/AgentRuntimeRunService.cs",
            "tianming-web/backend/Tianming.Web/Services/AgentRuntime/AgentRuntimeWorker.cs",
            "tianming-web/backend/Tianming.Web/Services/AgentTools/AgentToolExecutionLedger.cs",
            "tianming-web/backend/Tianming.Web/Services/AgentTools/ToolSearchCacheService.cs",
            "tianming-web/backend/Tianming.Web/Services/Production/ProductionTruthChapterIdentityMigrationService.cs",
            "tianming-web/backend/Tianming.Web/Services/Repositories/StoryBibleRepository.cs"
        };

        Assert.All(obsolete, path => Assert.False(File.Exists(Path.Combine(root, path)), path));
    }

    [Fact]
    public void Program_RegistersDurableGoalKernelAndUnifiedExecutionEnvelopes()
    {
        var source = Read("tianming-web/backend/Tianming.Web/Program.cs");

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
        var source = Read("tianming-web/backend/Tianming.Web/Services/Goals/PostgresKernelTaskScheduler.cs");

        Assert.Contains("AgentControlDbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NovelAgentDbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IBookProductionTransitionService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GoalCompiler_IsDeterministicAndMovesCreativeAnalysisIntoDurableDag()
    {
        var source = Read("tianming-web/backend/Tianming.Web/Services/Goals/GoalCompiler.cs");

        Assert.Contains("AnalyzeCreativeRequirements", source, StringComparison.Ordinal);
        Assert.Contains("CreativeRequirements", source, StringComparison.Ordinal);
        Assert.Contains("KernelTaskFailurePolicy.MaxAttempts", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IWritingModelCompletionService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IGoalTaskSupplementModelClient", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Director_ReadsModelContextOnlyThroughContextAssembler()
    {
        var source = Read("tianming-web/backend/Tianming.Web/Services/Goals/TargetArchitectureDirector.cs");

        Assert.Contains("IConversationContextAssembler", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NovelAgentDbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IKnowledgeQueryTool", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectCollaborationDecisions", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GoalWorkflowController_SubmitsProductionChangesThroughTransitionService()
    {
        var source = Read("tianming-web/backend/Tianming.Web/Controllers/GoalWorkflowController.cs");

        Assert.Contains("IBookProductionTransitionService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IGoalControlService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IBookProductionService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentSessionResume_ReplaysFromDurableConversationMessages()
    {
        var source = Read("tianming-web/backend/Tianming.Web/Services/AgentSessions/AgentSessionService.cs");

        Assert.Contains("ReadMessageRecordsAsync", source, StringComparison.Ordinal);
        Assert.Contains("pi.assistant.v1", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentChatTurns", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyControlPlaneCompatibilityWriters_DelegateWithoutSavingThroughWebDbContext()
    {
        var paths = new[]
        {
            "tianming-web/backend/Tianming.Web/Services/Goals/CreativeGoalService.cs",
            "tianming-web/backend/Tianming.Web/Services/Goals/GoalCompiler.cs",
            "tianming-web/backend/Tianming.Web/Services/Goals/BookProductionTransitionService.cs",
            "tianming-web/backend/Tianming.Web/Controllers/GoalWorkflowController.cs"
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

        var controller = Read("tianming-web/backend/Tianming.Web/Controllers/GoalWorkflowController.cs");
        Assert.DoesNotContain("KernelArtifacts.Add", controller, StringComparison.Ordinal);
        var branches = Read("tianming-web/backend/Tianming.Web/Services/Canon/CanonBranchService.cs");
        Assert.Contains("CreateArtifactAsync", branches, StringComparison.Ordinal);
        Assert.DoesNotContain("new KernelArtifact", branches, StringComparison.Ordinal);
    }

    [Fact]
    public void GoalProgressPublisher_WritesDurableOutboxWithoutDirectLiveDelivery()
    {
        var source = Read("tianming-web/backend/Tianming.Web/Services/Goals/GoalProgressEventPublisher.cs");
        var publisher = source[..source.IndexOf("public interface IGoalProgressEventDelivery", StringComparison.Ordinal)];

        Assert.Contains("_db.OutboxEvents.Add", publisher, StringComparison.Ordinal);
        Assert.Contains("publish_goal_progress", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentSseEventBus", publisher, StringComparison.Ordinal);
        Assert.DoesNotContain("IAgentRuntimeEventFanout", publisher, StringComparison.Ordinal);
    }

    [Fact]
    public void ConversationContext_IncludesPendingRecoveryIntents()
    {
        var assembler = Read("tianming-web/backend/Tianming.Web/Services/Context/AgentContextAssembler.cs");
        var director = Read("tianming-web/backend/Tianming.Web/Services/Goals/TargetArchitectureDirector.cs");

        Assert.Contains("item.RequiresConfirmation", assembler, StringComparison.Ordinal);
        Assert.Contains("AgentPendingIntentContext", assembler, StringComparison.Ordinal);
        Assert.Contains("snapshot.PendingIntents", director, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelRouter_ReadsExecutionContextOnlyThroughContextAssembler()
    {
        var source = Read("tianming-web/backend/Tianming.Web/Services/Kernels/KernelTaskExecutionRouter.cs");

        Assert.Contains("IAgentContextAssembler", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NovelAgentDbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelArtifacts.AsNoTracking", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GoalContextSnapshots.AsNoTracking", source, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelPrompts_AlwaysContainEffectiveGoalContract()
    {
        var router = Read("tianming-web/backend/Tianming.Web/Services/Kernels/KernelTaskExecutionRouter.cs");
        var contexts = Read("tianming-web/backend/Tianming.Web/Services/Context/AgentContextAssembler.cs");
        var client = Read("tianming-web/backend/Tianming.Web/Services/Kernels/DefaultKernelStructuredModelClient.cs");

        Assert.Contains("IAgentContextAssembler", router, StringComparison.Ordinal);
        Assert.DoesNotContain("NovelAgentDbContext", router, StringComparison.Ordinal);
        Assert.Contains("CreativeGoalRevisionProjector.Project", contexts, StringComparison.Ordinal);
        Assert.Contains("context.GoalContract", client, StringComparison.Ordinal);
        Assert.Contains("context.GoalContract", router, StringComparison.Ordinal);
    }

    [Fact]
    public void Sse_UsesOwnedSessionAndAuthorizationHeaderWithoutQueryToken()
    {
        var controller = Read("tianming-web/backend/Tianming.Web/Controllers/AgentController.cs");
        var frontend = Read("old/Web/NovelAgentWeb.Frontend/src/api/index.ts");
        var program = Read("tianming-web/backend/Tianming.Web/Program.cs");

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
        var auth = Read("tianming-web/backend/Tianming.Web/Controllers/AuthController.cs");
        var program = Read("tianming-web/backend/Tianming.Web/Program.cs");

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

    /// <summary>
    /// The backend served a hand-committed 2026-08-22 frontend bundle out of wwwroot, and
    /// that bundle called /agent/chat. Retired on 2026-09-01 (task
    /// 09-01-retire-wwwroot-legacy-frontend) after establishing there is no deployment
    /// topology that needs same-origin hosting: old/Dockerfile targets .NET 8 and
    /// /src/Web/NovelAgentWeb, so it cannot build this backend at all.
    ///
    /// Two assertions rather than one, following the lesson from
    /// 09-01-retire-legacy-runtimes: a single layer misses. Deleting the files does not
    /// stop the middleware from being re-added, and removing the middleware does not stop
    /// a bundle from being re-committed. Either one alone lets the pairing come back.
    /// </summary>
    [Fact]
    public void LegacyFrontendBundle_IsNeitherHostedNorCommitted()
    {
        var source = Read("tianming-web/backend/Tianming.Web/Program.cs");

        Assert.DoesNotContain("UseStaticFiles", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UseDefaultFiles", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MapFallbackToFile", source, StringComparison.Ordinal);

        var wwwroot = Path.Combine(RepositoryRoot(), "tianming-web/backend/Tianming.Web/wwwroot");
        Assert.False(Directory.Exists(wwwroot), wwwroot);
    }

    /// <summary>
    /// The /agent/chat compatibility entry bypassed nothing by 2026-09-01 — it already
    /// persisted through ConversationApplicationService — but it was a second door onto
    /// the conversation path with its own idempotency-key reconciliation. Its only callers
    /// were the retired wwwroot bundle and a dead frontend export.
    /// </summary>
    [Fact]
    public void AgentController_NoLongerExposesTheLegacyChatEntry()
    {
        var source = Read("tianming-web/backend/Tianming.Web/Controllers/AgentController.cs");

        Assert.DoesNotContain("agent/chat", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentChatRequest", source, StringComparison.Ordinal);
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath));

    private static string RepositoryRoot()
    {
        // Markers must hold only at the repository root. "tianming-web" contains its own
        // README.md but no nested "tianming-web", so the pair is unambiguous on the walk up.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null &&
               (!File.Exists(Path.Combine(directory.FullName, "README.md")) ||
                !Directory.Exists(Path.Combine(directory.FullName, "tianming-web"))))
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
