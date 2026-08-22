using System.Text.Json;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Contracts.Conversation;
using Tianming.NovelAgent.Contracts.Streaming;
using Tianming.NovelAgent.Domain.Events;
using Tianming.NovelAgent.Domain.Goals;

namespace Tianming.NovelAgent.Application.Conversation;

public sealed class LegacyRecoveryApplicationService(
    ILegacyProjectSnapshotReader snapshots,
    IConversationStore conversations,
    IAgentEventWriter events,
    IAgentUnitOfWork unitOfWork,
    IIdGenerator ids,
    IContractHasher hasher,
    IClock clock)
{
    public async Task<CreateLegacyRecoveryProposalResult> CreateProposalAsync(
        string userId,
        string projectId,
        CreateLegacyRecoveryProposalRequest request,
        CancellationToken cancellationToken = default)
    {
        RejectLegacyExecutionState(request);
        Require(userId, nameof(userId));
        Require(projectId, nameof(projectId));
        Require(request.SessionId, nameof(request.SessionId));
        Require(request.IdempotencyKey, nameof(request.IdempotencyKey));

        var snapshot = await snapshots.ReadRecoverableSnapshotAsync(userId, projectId, cancellationToken);
        if (snapshot.ProjectId != projectId)
            throw new InvalidOperationException("The legacy snapshot belongs to another project.");

        var existing = await conversations.FindTurnResultAsync(
            userId,
            request.SessionId,
            request.IdempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.Decision.Kind != ConversationDecisionKind.ProposeGoal
                || string.IsNullOrWhiteSpace(existing.Decision.ProposalId)
                || string.IsNullOrWhiteSpace(existing.Decision.ProposalHash))
                throw new InvalidOperationException("The recovery idempotency key belongs to another conversation command.");
            return Result(existing.Decision.ProposalId, existing.Decision.ProposalHash, snapshot, existing.CorrelationId);
        }

        var contract = new GoalContract(
            request.Objective,
            ToDomain(request.Mode),
            new ChapterRange(request.StartChapter, request.EndChapter),
            request.SuccessCriteria,
            request.MustPreserve ?? [],
            request.MustHappen ?? [],
            request.MustNotChange ?? [],
            request.AcceptancePolicy,
            request.ReworkPolicy,
            request.TotalCostLimit,
            snapshot.CanonBaselineVersion,
            snapshot.KnowledgeSnapshotVersion,
            snapshot.QualityContractVersion,
            snapshot.StyleProfileVersion,
            snapshot.ModelVersions,
            snapshot.ProtocolVersions);
        contract.Validate();

        var correlationId = ids.NewId();
        var proposal = new GoalProposal(ids.NewId(), userId, projectId, request.SessionId, contract);
        proposal.Propose();
        var contractHash = hasher.Hash(contract);
        var decision = new ConversationDecisionDto(
            ConversationDecisionKind.ProposeGoal,
            "A new proposal was created from retained canonical project data. Workflow confirmation is still required.",
            proposal.Id,
            JsonSerializer.Serialize(contract),
            contractHash);
        var turn = new ConversationTurnResult(ids.NewId(), decision, correlationId);
        var result = Result(proposal.Id, contractHash, snapshot, correlationId);
        var runtimeResult = new ConversationRuntimeResult(
            decision.Message,
            ConversationDecisionKind.ProposeGoal,
            contract,
            []);

        return await unitOfWork.ExecuteAsync(async ct =>
        {
            await conversations.SaveTurnAsync(
                userId,
                projectId,
                request.SessionId,
                request.IdempotencyKey,
                turn.MessageId,
                $"Recover project {projectId} from retained canonical data: {request.Objective}",
                ids.NewId(),
                runtimeResult,
                proposal,
                turn,
                ct);
            await events.AppendAsync(
                new AgentDomainEvent(
                    ids.NewId(),
                    "LegacyRecoveryProposalCreated",
                    "GoalProposal",
                    proposal.Id,
                    1,
                    userId,
                    projectId,
                    null,
                    correlationId,
                    turn.MessageId,
                    clock.UtcNow,
                    JsonSerializer.Serialize(result)),
                AgentStreamKind.Conversation,
                request.SessionId,
                result,
                ct);
            return result;
        }, cancellationToken);
    }

    private static CreateLegacyRecoveryProposalResult Result(
        string proposalId,
        string contractHash,
        LegacyProjectSnapshot snapshot,
        string correlationId) => new(
            proposalId,
            contractHash,
            new LegacyRecoveryEvidence(
                snapshot.FormalChapterVersionIds,
                snapshot.ConfirmedDecisionIds,
                snapshot.KnowledgeIds),
            correlationId);

    private static ProductionMode ToDomain(NovelAgentProductionMode mode) => mode switch
    {
        NovelAgentProductionMode.SingleChapter => ProductionMode.SingleChapter,
        NovelAgentProductionMode.InteractiveBatch => ProductionMode.InteractiveBatch,
        NovelAgentProductionMode.AutonomousBook => ProductionMode.AutonomousBook,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };

    private static void RejectLegacyExecutionState(CreateLegacyRecoveryProposalRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.MissionPlanId)
            || !string.IsNullOrWhiteSpace(request.RuntimeRunId)
            || request.PendingToolExecutionIds is { Count: > 0 })
            throw new ArgumentException(
                "MissionPlan, RuntimeRun, and pending tool execution state cannot be restored. Create a new Goal from retained canonical data.",
                nameof(request));
    }

    private static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{name} is required.", name);
    }
}
