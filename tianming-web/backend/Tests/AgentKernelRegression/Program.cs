using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Goals;
using TM.Web.NovelAgentWeb.Services.Kernels;

namespace TM.Tests.AgentKernelRegression;

internal static class Program
{
    private static readonly List<(string Name, Func<Task> Test)> Tests =
    [
        ("legacy ReAct production components are absent", LegacyReactProductionComponentsAreAbsent),
        ("program wires durable goal execution", ProgramWiresDurableGoalExecution),
        ("goal compiler contains the complete novel production DAG", GoalCompilerContainsCompleteProductionDag),
        ("kernel failure policy separates retry and human decisions", KernelFailurePolicySeparatesRetryAndHumanDecisions),
        ("Tianming kernel preserves protected human chapters", TianmingKernelPreservesProtectedHumanChapters),
        ("Tianming kernel requires a frozen chapter context", TianmingKernelRequiresFrozenChapterContext)
    ];

    public static async Task<int> Main()
    {
        var failed = 0;
        Console.WriteLine("Target architecture kernel regression suite");
        Console.WriteLine("==========================================");

        foreach (var (name, test) in Tests)
        {
            try
            {
                await test();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failed++;
                Console.WriteLine($"FAIL {name}");
                Console.WriteLine($"     {exception.Message}");
            }
        }

        Console.WriteLine(failed == 0
            ? $"All {Tests.Count} target architecture checks passed."
            : $"{failed} of {Tests.Count} target architecture checks failed.");
        return failed == 0 ? 0 : 1;
    }

    private static Task LegacyReactProductionComponentsAreAbsent()
    {
        var root = RepositoryRoot();
        var obsolete = new[]
        {
            "tianming-web/backend/Tianming.Web/Support/AgentRuntime.cs",
            "tianming-web/backend/Tianming.Web/Support/AgentKernel.cs",
            "tianming-web/backend/Tianming.Web/Support/AgentToolRegistry.cs",
            "tianming-web/backend/Tianming.Web/Services/AgentRuntime/AgentRuntimeWorker.cs",
            "tianming-web/backend/Tianming.Web/Services/AgentRuntime/AgentRuntimeQueue.cs",
            "tianming-web/backend/Tianming.Web/Controllers/GoalsController.cs",
            "tianming-web/backend/Tianming.Web/Controllers/CanonBranchesController.cs"
        };
        foreach (var relativePath in obsolete)
            Check(!File.Exists(Path.Combine(root, relativePath)), $"obsolete component still exists: {relativePath}");
        return Task.CompletedTask;
    }

    private static Task ProgramWiresDurableGoalExecution()
    {
        var source = Read("tianming-web/backend/Tianming.Web/Program.cs");
        Check(source.Contains("AddScoped<IAgentForegroundTurnRunner>(sp => sp.GetRequiredService<TargetArchitectureDirector>())", StringComparison.Ordinal),
            "conversation entry point must use TargetArchitectureDirector");
        Check(source.Contains("AddHostedService<KernelTaskWorker>()", StringComparison.Ordinal),
            "durable KernelTaskWorker must be hosted");
        Check(source.Contains("AddScoped<IGoalModelExecutionEnvelope, GoalModelExecutionEnvelope>()", StringComparison.Ordinal),
            "model calls must use the goal execution envelope");
        Check(source.Contains("AddScoped<IGoalEmbeddingExecutionEnvelope, GoalEmbeddingExecutionEnvelope>()", StringComparison.Ordinal),
            "embedding calls must use the goal execution envelope");
        Check(!source.Contains("AgentToolRegistry", StringComparison.Ordinal),
            "legacy tool registry must not be registered");
        return Task.CompletedTask;
    }

    private static Task GoalCompilerContainsCompleteProductionDag()
    {
        var source = Read("tianming-web/backend/Tianming.Web/Services/Goals/GoalCompiler.cs");
        var orderedStages = new[]
        {
            "FreezeBaselines",
            "AnalyzeCreativeRequirements",
            "CompileBatchPlan",
            "PlanChapter",
            "CompileChapterContext",
            "WriteCandidate",
            "ReviewContinuity",
            "ReviewLiteraryQuality",
            "DirectedRework",
            "ExtractContinuitySummary",
            "BatchImpactAnalysis",
            "AcceptanceGate",
            "PrefixMerge"
        };
        var cursor = -1;
        foreach (var stage in orderedStages)
        {
            var token = stage == "AcceptanceGate"
                ? "BookProductionWorkflow.AcceptanceGate"
                : $"\"{stage}\"";
            var next = source.IndexOf(token, cursor + 1, StringComparison.Ordinal);
            Check(next > cursor, $"DAG stage missing or out of order: {stage}");
            cursor = next;
        }
        Check(source.Contains("AuthorityMutation.MergeAcceptedPrefix", StringComparison.Ordinal),
            "only prefix merge may mutate canonical chapter authority");
        return Task.CompletedTask;
    }

    private static Task KernelFailurePolicySeparatesRetryAndHumanDecisions()
    {
        Check(KernelTaskFailurePolicy.MaxAttempts("FreezeBaselines") == 3,
            "baseline freezing must tolerate transient infrastructure failures");
        Check(KernelTaskFailurePolicy.MaxAttempts("WriteCandidate") == 2,
            "writing calls must have a bounded retry budget");
        Check(KernelTaskFailurePolicy.MaxAttempts("PrefixMerge") == 1,
            "canonical merge must not retry blindly");

        var retry = KernelTaskFailurePolicy.Decide(
            "WriteCandidate", 1, 2, KernelTaskFailureCategory.Transient);
        Check(retry.Disposition == KernelTaskFailureDisposition.Retry,
            "transient writing failure should retry within budget");
        var merge = KernelTaskFailurePolicy.Decide(
            "PrefixMerge", 1, 1, KernelTaskFailureCategory.Transient);
        Check(merge.Disposition == KernelTaskFailureDisposition.AwaitingDecision,
            "merge failure must wait for a human decision");
        var fatal = KernelTaskFailurePolicy.Decide(
            "CompileChapterContext", 1, 3, KernelTaskFailureCategory.Fatal);
        Check(fatal.Disposition == KernelTaskFailureDisposition.FailGoal,
            "invalid contracts must fail the goal");
        return Task.CompletedTask;
    }

    private static async Task TianmingKernelPreservesProtectedHumanChapters()
    {
        var gateway = new RecordingGateway();
        var kernel = new TianmingWritingKernel(gateway);
        var original = new ChapterDraftArtifact
        {
            ArtifactId = "draft-human-1",
            ChapterId = "chapter-1",
            DraftContent = "用户亲自修改并锁定的正文。",
            HasChanges = true
        };
        var contextInput = new TianmingChapterContextInput(
            new NovelAgentRun { RunId = "run-1", TargetChapterId = "chapter-1" },
            new ChapterContextPackageSummary { ChapterId = "chapter-1" });
        var context = Context(
            "DirectedRework",
            new KernelInputArtifact("context-1", "ChapterContextContract", 1, JsonSerializer.Serialize(contextInput), "hash-context"),
            new KernelInputArtifact("draft-1", "CandidateChapterDraft", 1, JsonSerializer.Serialize(original), "hash-draft", "human", true));

        var output = await kernel.ExecuteAsync(context);

        var artifact = Single(output.Artifacts);
        Check(artifact.ArtifactType == "ReviewedCandidateChapter", "protected draft must remain a reviewed candidate");
        Check(artifact.Authorship == "human" && artifact.IsProtected, "human authorship and protection must survive");
        Check(gateway.CallCount == 0, "protected human content must not be sent to the writing model");
    }

    private static async Task TianmingKernelRequiresFrozenChapterContext()
    {
        var kernel = new TianmingWritingKernel(new RecordingGateway());
        try
        {
            await kernel.ExecuteAsync(Context("WriteCandidate"));
            throw new InvalidOperationException("kernel accepted a task without ChapterContextContract");
        }
        catch (InvalidOperationException exception)
        {
            Check(exception.Message.Contains("ChapterContextContract", StringComparison.Ordinal),
                "missing context failure must identify the contract");
        }
    }

    private static KernelExecutionContext Context(string taskType, params KernelInputArtifact[] inputs) =>
        new(
            new KernelTaskClaim(
                "task-1", "user-1", "project-1", "goal-1", "graph-1", "branch-1",
                "tianming_writing", taskType, 1, "worker-1", DateTime.UtcNow.AddMinutes(1)),
            new GoalContextSnapshot { UserId = "user-1", ProjectId = "project-1", GoalId = "goal-1" },
            new KernelGoalContract(
                "chapter_batch", "coauthor", "写第一章", "{\"start\":1,\"end\":1}",
                "[]", "[]", "[]", "[]", "{}", "{}"),
            inputs);

    private static T Single<T>(IReadOnlyList<T> values)
    {
        Check(values.Count == 1, $"expected one item, got {values.Count}");
        return values[0];
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

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class RecordingGateway : ITianmingWritingGateway
    {
        public int CallCount { get; private set; }

        public Task<ChapterDraftArtifact> GenerateAsync(
            string userId,
            string projectId,
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new ChapterDraftArtifact { ChapterId = run.TargetChapterId });
        }
    }
}
