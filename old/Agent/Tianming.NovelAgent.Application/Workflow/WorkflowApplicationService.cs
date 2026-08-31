using System.Text.Json;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Contracts.Conversation;
using Tianming.NovelAgent.Contracts.Streaming;
using Tianming.NovelAgent.Domain.Events;
using Tianming.NovelAgent.Domain.Goals;
using Tianming.NovelAgent.Domain.Production;
using ProductionAggregate = Tianming.NovelAgent.Domain.Production.Production;

namespace Tianming.NovelAgent.Application.Workflow;

public sealed class WorkflowApplicationService(
    IConversationStore conversations,
    IGoalRepository goals,
    IProductionRepository productions,
    IContextFreezer contextFreezer,
    IAgentEventWriter events,
    IAgentUnitOfWork unitOfWork,
    IIdGenerator ids,
    IContractHasher hasher,
    IClock clock) : IWorkflowCommandPort
{
    public async Task<ConfirmGoalProposalResult> ConfirmProposalAsync(
        string userId,
        string proposalId,
        string actorId,
        ConfirmGoalProposalRequest request,
        CancellationToken cancellationToken = default)
    {
        var proposal = await conversations.GetProposalAsync(userId, proposalId, cancellationToken)
            ?? throw new KeyNotFoundException($"Proposal {proposalId} was not found.");

        try
        {
            return await unitOfWork.ExecuteAsync(async ct =>
            {
                var existing = await goals.FindConfirmationResultAsync(
                    userId,
                    proposal.ProjectId,
                    request.IdempotencyKey,
                    ct);
                if (existing is not null)
                    return existing;

                var correlationId = ids.NewId();
                var frozen = await contextFreezer.FreezeAsync(
                    userId,
                    proposal.ProjectId,
                    proposal.Contract,
                    ct);
                var revision = new GoalRevision(
                    ids.NewId(),
                    1,
                    proposal.Contract,
                    frozen,
                    new GoalConfirmation(actorId, clock.UtcNow, request.IdempotencyKey, proposal.SourceSessionId, proposal.Id),
                    hasher.Hash(proposal.Contract),
                    "1",
                    request.ConfirmationNote ?? "Initial proposal confirmation",
                    null);
                var goal = CreativeGoal.Confirm(ids.NewId(), proposal, revision);
                var graph = FirstBatchTaskGraphCompiler.Compile(
                    ids.NewId(),
                    proposal.Contract.Mode,
                    proposal.Contract.ChapterRange.Start);
                var production = new ProductionAggregate(
                    ids.NewId(),
                    userId,
                    proposal.ProjectId,
                    goal.Id,
                    revision.Id,
                    proposal.Contract.Mode,
                    graph);

                await conversations.UpdateProposalAsync(proposal, ct);
                await goals.AddAsync(goal, ct);
                await productions.AddAsync(production, proposal.Contract, frozen, ct);
                var result = new ConfirmGoalProposalResult(goal.Id, revision.Id, production.Id, correlationId);
                await events.AppendAsync(
                    new AgentDomainEvent(
                        ids.NewId(),
                        "GoalConfirmed",
                        "CreativeGoal",
                        goal.Id,
                        goal.Version,
                        userId,
                        goal.ProjectId,
                        goal.Id,
                        correlationId,
                        proposal.Id,
                        clock.UtcNow,
                        JsonSerializer.Serialize(result)),
                    AgentStreamKind.Workflow,
                    goal.ProjectId,
                    result,
                    ct);
                return result;
            }, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            var existing = await goals.FindConfirmationResultAsync(
                userId,
                proposal.ProjectId,
                request.IdempotencyKey,
                cancellationToken);
            if (existing is not null)
                return existing;
            throw;
        }
    }
}
