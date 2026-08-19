using System.Text.Json;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Application.Workflow;
using Tianming.NovelAgent.Contracts.Conversation;
using Tianming.NovelAgent.Contracts.Streaming;
using Tianming.NovelAgent.Domain.Events;
using Tianming.NovelAgent.Domain.Goals;

namespace Tianming.NovelAgent.Application.Conversation;

public sealed class ConversationApplicationService(
    IConversationAgentRuntime runtime,
    IConversationStore store,
    ITransientAgentStream transientStream,
    IAgentEventWriter events,
    IAgentUnitOfWork unitOfWork,
    IIdGenerator ids,
    IContractHasher hasher,
    IClock clock,
    IAgentToolRegistry? tools = null)
{
    public async Task<ConversationTurnResult> AppendTurnAsync(
        string userId,
        string projectId,
        string sessionId,
        AppendConversationTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        Require(userId, nameof(userId));
        Require(projectId, nameof(projectId));
        Require(sessionId, nameof(sessionId));
        Require(request.IdempotencyKey, nameof(request.IdempotencyKey));
        Require(request.Content, nameof(request.Content));

        var existing = await store.FindTurnResultAsync(userId, sessionId, request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.Confirmation is null
                && existing.Decision.AutoConfirmRequested
                && tools is not null
                && !string.IsNullOrWhiteSpace(existing.Decision.ProposalId))
            {
                return await ExecuteToolAndPersistResultAsync(
                    existing,
                    new AgentToolCall(ConfirmCreativeGoalTool.ToolName),
                    userId,
                    projectId,
                    sessionId,
                    existing.Decision.ProposalId!,
                    request.IdempotencyKey,
                    existing.CorrelationId,
                    cancellationToken);
            }
            return existing;
        }

        var correlationId = ids.NewId();
        var runtimeResult = await runtime.RunTurnAsync(
            new ConversationTurnContext(
                userId,
                projectId,
                sessionId,
                request.Content,
                request.AttachmentIds ?? [],
                correlationId),
            cancellationToken);

        foreach (var delta in runtimeResult.TokenDeltas)
            await transientStream.PublishTokenDeltaAsync(userId, projectId, sessionId, correlationId, delta, cancellationToken);

        var prepared = PrepareTurn(
            userId,
            projectId,
            sessionId,
            request,
            runtimeResult,
            correlationId);
        var persisted = await PersistTurnAsync(prepared, cancellationToken);

        if (!prepared.AutoConfirmRequested || tools is null || prepared.Proposal is null || prepared.RequestedTool is null)
            return persisted;

        return await ExecuteToolAndPersistResultAsync(
            persisted,
            prepared.RequestedTool,
            userId,
            projectId,
            sessionId,
            prepared.Proposal.Id,
            request.IdempotencyKey,
            correlationId,
            cancellationToken);
    }

    private PreparedTurn PrepareTurn(
        string userId,
        string projectId,
        string sessionId,
        AppendConversationTurnRequest request,
        ConversationRuntimeResult runtimeResult,
        string correlationId)
    {
        GoalProposal? proposal = null;
        if (runtimeResult.DecisionKind is ConversationDecisionKind.ProposeGoal or ConversationDecisionKind.ProposeRevision)
        {
            if (runtimeResult.ProposedContract is null)
                throw new InvalidOperationException("A proposal decision requires a goal contract.");
            proposal = new GoalProposal(ids.NewId(), userId, projectId, sessionId, runtimeResult.ProposedContract);
            proposal.Propose();
        }

        var requestedTool = runtimeResult.ToolCalls?.FirstOrDefault();
        var decision = new ConversationDecisionDto(
            runtimeResult.DecisionKind,
            runtimeResult.AssistantMessage,
            proposal?.Id,
            proposal is null ? null : JsonSerializer.Serialize(proposal.Contract),
            proposal is null ? null : hasher.Hash(proposal.Contract),
            proposal is not null
                && runtimeResult.DecisionKind == ConversationDecisionKind.ProposeGoal
                && requestedTool is not null);
        return new PreparedTurn(
            userId,
            projectId,
            sessionId,
            request.IdempotencyKey,
            request.Content,
            runtimeResult,
            proposal,
            requestedTool,
            new ConversationTurnResult(ids.NewId(), decision, correlationId));
    }

    private async Task<ConversationTurnResult> PersistTurnAsync(
        PreparedTurn prepared,
        CancellationToken cancellationToken) =>
        await unitOfWork.ExecuteAsync(async ct =>
        {
            await store.SaveTurnAsync(
                prepared.UserId,
                prepared.ProjectId,
                prepared.SessionId,
                prepared.IdempotencyKey,
                prepared.Result.MessageId,
                prepared.UserMessage,
                ids.NewId(),
                prepared.RuntimeResult,
                prepared.Proposal,
                prepared.Result,
                ct);
            await events.AppendAsync(
                new AgentDomainEvent(
                    ids.NewId(),
                    prepared.Proposal is null ? "ConversationTurnCompleted" : "GoalProposalCreated",
                    "ConversationTurn",
                    prepared.Result.MessageId,
                    1,
                    prepared.UserId,
                    prepared.ProjectId,
                    null,
                    prepared.Result.CorrelationId,
                    prepared.Result.MessageId,
                    clock.UtcNow,
                    JsonSerializer.Serialize(prepared.Result.Decision)),
                AgentStreamKind.Conversation,
                prepared.SessionId,
                prepared.Result.Decision,
                ct);
            return prepared.Result;
        }, cancellationToken);

    private async Task<ConversationTurnResult> ExecuteToolAndPersistResultAsync(
        ConversationTurnResult current,
        AgentToolCall call,
        string userId,
        string projectId,
        string sessionId,
        string proposalId,
        string turnIdempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (tools is null)
            return current;

        var executableCall = call.Name == ConfirmCreativeGoalTool.ToolName
            ? new AgentToolCall(call.Name)
            : call;
        var execution = await tools.ExecuteAsync(
            executableCall,
            new AgentToolContext(
                userId,
                projectId,
                sessionId,
                proposalId,
                $"conversation:{turnIdempotencyKey}",
                correlationId),
            cancellationToken);
        var updated = execution.Succeeded && execution.Confirmation is not null
            ? current with
            {
                Decision = current.Decision with { Message = execution.Message },
                Confirmation = execution.Confirmation,
                ToolError = null
            }
            : current with { ToolError = execution.Error ?? execution.Message };

        return await unitOfWork.ExecuteAsync(async ct =>
        {
            await store.UpdateTurnResultAsync(userId, sessionId, turnIdempotencyKey, updated, ct);
            return updated;
        }, cancellationToken);
    }

    private static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{name} is required.", name);
    }

    private sealed record PreparedTurn(
        string UserId,
        string ProjectId,
        string SessionId,
        string IdempotencyKey,
        string UserMessage,
        ConversationRuntimeResult RuntimeResult,
        GoalProposal? Proposal,
        AgentToolCall? RequestedTool,
        ConversationTurnResult Result)
    {
        public bool AutoConfirmRequested => Result.Decision.AutoConfirmRequested;
    }
}
