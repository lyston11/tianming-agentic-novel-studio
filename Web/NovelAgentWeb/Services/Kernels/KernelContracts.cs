using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.DomainEvents;
using TM.Web.NovelAgentWeb.Services.Goals;

namespace TM.Web.NovelAgentWeb.Services.Kernels;

public sealed record KernelInputArtifact(
    string Id,
    string ArtifactType,
    int SchemaVersion,
    string ContentJson,
    string ContentHash,
    string Authorship = "agent",
    bool IsProtected = false);

public sealed record KernelExecutionContext(
    KernelTaskClaim Claim,
    GoalContextSnapshot GoalSnapshot,
    KernelGoalContract GoalContract,
    IReadOnlyList<KernelInputArtifact> Inputs);

public sealed record KernelGoalContract(
    string GoalType,
    string CollaborationMode,
    string HumanReadableObjective,
    string TargetChapterRangeJson,
    string SuccessCriteriaJson,
    string MustPreserveJson,
    string MustHappenJson,
    string MustNotChangeJson,
    string AcceptancePolicyJson,
    string ReworkPolicyJson);

public sealed record KernelExecutionOutput(
    IReadOnlyList<KernelArtifactProposal> Artifacts,
    IReadOnlyList<DomainEventProposal> Events);

public sealed record TianmingChapterContextInput(
    NovelAgentRun Run,
    ChapterContextPackageSummary Package);

public sealed record ReworkIntentArtifactContract(
    string IntentId,
    string TargetScope,
    int? SelectionStart,
    int? SelectionEnd,
    string Problem,
    string DesiredEffect,
    IReadOnlyList<string> Preserve,
    IReadOnlyList<string> MayChange,
    IReadOnlyList<string> MustNotChange,
    IReadOnlyList<string> AcceptanceCriteria);

public interface IKernel
{
    string Name { get; }

    Task<KernelExecutionOutput> ExecuteAsync(
        KernelExecutionContext context,
        CancellationToken cancellationToken = default);
}

public interface IKernelStructuredModelClient
{
    Task<string> GenerateAsync(
        string kernelName,
        KernelExecutionContext context,
        CancellationToken cancellationToken = default);
}
