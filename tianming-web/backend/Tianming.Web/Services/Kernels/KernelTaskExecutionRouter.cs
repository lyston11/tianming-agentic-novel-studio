using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TM.Web.NovelAgentWeb.Services.Context;
using TM.Web.NovelAgentWeb.Services.DomainEvents;
using TM.Web.NovelAgentWeb.Services.Goals;
using TM.Web.NovelAgentWeb.Services.Execution;

namespace TM.Web.NovelAgentWeb.Services.Kernels;

public sealed class KernelTaskExecutionRouter : IKernelTaskExecutor
{
    private readonly IAgentContextAssembler _contexts;
    private readonly KernelRegistry _registry;
    private readonly IDomainReducer _reducer;
    private readonly IGoalControlService _goalControl;
    private readonly IKernelModelExecutionScopeAccessor? _modelExecutionScopes;

    public KernelTaskExecutionRouter(
        IAgentContextAssembler contexts,
        KernelRegistry registry,
        IDomainReducer reducer,
        IGoalControlService goalControl,
        IKernelModelExecutionScopeAccessor? modelExecutionScopes = null)
    {
        _contexts = contexts;
        _registry = registry;
        _reducer = reducer;
        _goalControl = goalControl;
        _modelExecutionScopes = modelExecutionScopes;
    }

    public async Task<KernelTaskExecutionResult> ExecuteAsync(
        KernelTaskClaim claim,
        CancellationToken cancellationToken = default)
    {
        var context = await _contexts.BuildKernelExecutionAsync(claim, cancellationToken)
            .ConfigureAwait(false);
        KernelExecutionOutput output;
        if (claim.TaskType == "FreezeBaselines")
        {
            output = FreezeBaselines(context);
        }
        else
        {
            using var modelExecutionScope = _modelExecutionScopes?.Push(new KernelModelExecutionScope(
                claim.UserId,
                claim.ProjectId,
                claim.GoalId,
                claim.TaskId,
                claim.KernelName,
                claim.Attempt));
            output = await _registry.GetRequired(claim.KernelName)
                .ExecuteAsync(context, cancellationToken);
        }
        var safePoint = await _goalControl.ReachSafePointAsync(
            claim,
            output.Artifacts,
            cancellationToken);
        if (safePoint.Disposition != GoalSafePointDisposition.Continue)
        {
            return new KernelTaskExecutionResult(
                safePoint.ArtifactIds,
                safePoint.Disposition switch
                {
                    GoalSafePointDisposition.Paused => KernelTaskExecutionDisposition.Paused,
                    GoalSafePointDisposition.BudgetExceeded => KernelTaskExecutionDisposition.BudgetExceeded,
                    _ => KernelTaskExecutionDisposition.Canceled
                });
        }
        var adoption = await _reducer.ApplyAsync(
            claim,
            output.Artifacts,
            output.Events,
            cancellationToken);
        return new KernelTaskExecutionResult(adoption.ArtifactIds);
    }

    private static KernelExecutionOutput FreezeBaselines(KernelExecutionContext context)
    {
        var json = JsonSerializer.Serialize(new
        {
            context.GoalSnapshot.CanonVersion,
            context.GoalSnapshot.KnowledgeVersion,
            context.GoalSnapshot.QualityContractVersion,
            context.GoalSnapshot.StyleProfileVersion,
            context.GoalSnapshot.ModelConfigVersionsJson,
            context.GoalSnapshot.ProtocolVersionsJson,
            context.GoalSnapshot.ContentHashesJson,
            context.GoalContract,
            context.GoalSnapshot.CreatedAt
        });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        return new KernelExecutionOutput(
            [new KernelArtifactProposal("FrozenBaselines", 1, json, hash, "agent", false)],
            []);
    }

}
