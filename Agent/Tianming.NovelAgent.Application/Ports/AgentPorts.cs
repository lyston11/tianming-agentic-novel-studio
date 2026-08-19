using System.Text.Json;
using Tianming.NovelAgent.Contracts.Conversation;
using Tianming.NovelAgent.Contracts.Models;
using Tianming.NovelAgent.Contracts.Streaming;
using Tianming.NovelAgent.Contracts.Workflow;
using Tianming.NovelAgent.Domain.Events;
using Tianming.NovelAgent.Domain.Goals;
using Tianming.NovelAgent.Domain.Production;
using ProductionAggregate = Tianming.NovelAgent.Domain.Production.Production;

namespace Tianming.NovelAgent.Application.Ports;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IIdGenerator
{
    string NewId();
}

public interface IUserScope
{
    string? CurrentUserId { get; }
    IDisposable Enter(string userId);
}

public interface IContractHasher
{
    string Hash(GoalContract contract);
}

public interface IAgentUnitOfWork
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);
}

public interface IConversationAgentRuntime
{
    Task<ConversationRuntimeResult> RunTurnAsync(ConversationTurnContext context, CancellationToken cancellationToken);
}

public interface IWorkflowCommandPort
{
    Task<ConfirmGoalProposalResult> ConfirmProposalAsync(
        string userId,
        string proposalId,
        string actorId,
        ConfirmGoalProposalRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record AgentToolContext(
    string UserId,
    string ProjectId,
    string SessionId,
    string? ProposalId,
    string IdempotencyKey,
    string CorrelationId);

public sealed record AgentToolExecutionResult(
    string Name,
    bool Succeeded,
    string Message,
    ConfirmGoalProposalResult? Confirmation = null,
    string? Error = null);

public interface IAgentTool
{
    string Name { get; }
    string Description { get; }

    Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolContext context,
        IReadOnlyDictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken);
}

public interface IAgentToolRegistry
{
    Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolCall call,
        AgentToolContext context,
        CancellationToken cancellationToken);
}

public interface IConversationTextCompletionPort
{
    Task<string> CompleteAsync(
        string userId,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken);
}

public sealed record ConversationTurnContext(
    string UserId,
    string ProjectId,
    string SessionId,
    string Message,
    IReadOnlyList<string> AttachmentIds,
    string CorrelationId);

public sealed record ConversationRuntimeResult(
    string AssistantMessage,
    ConversationDecisionKind DecisionKind,
    GoalContract? ProposedContract,
    IReadOnlyList<string> TokenDeltas,
    string? Reason = null,
    IReadOnlyList<AgentToolCall>? ToolCalls = null);

public interface IConversationStore
{
    Task<ConversationTurnResult?> FindTurnResultAsync(
        string userId,
        string sessionId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task SaveTurnAsync(
        string userId,
        string projectId,
        string sessionId,
        string idempotencyKey,
        string userMessageId,
        string userMessage,
        string assistantMessageId,
        ConversationRuntimeResult runtimeResult,
        GoalProposal? proposal,
        ConversationTurnResult result,
        CancellationToken cancellationToken);

    Task UpdateTurnResultAsync(
        string userId,
        string sessionId,
        string idempotencyKey,
        ConversationTurnResult result,
        CancellationToken cancellationToken) => Task.CompletedTask;

    Task<GoalProposal?> GetProposalAsync(string userId, string proposalId, CancellationToken cancellationToken);
    Task UpdateProposalAsync(GoalProposal proposal, CancellationToken cancellationToken);
}

public interface IGoalRepository
{
    Task<ConfirmGoalProposalResult?> FindConfirmationResultAsync(
        string userId,
        string projectId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<CreativeGoal?> GetAsync(string userId, string goalId, CancellationToken cancellationToken);
    Task AddAsync(CreativeGoal goal, CancellationToken cancellationToken);
    Task UpdateAsync(CreativeGoal goal, CancellationToken cancellationToken);
}

public interface IProductionRepository
{
    Task<ProductionAggregate?> GetAsync(string userId, string productionId, CancellationToken cancellationToken);
    Task<ProductionAggregate?> FindByGoalIdAsync(string userId, string goalId, CancellationToken cancellationToken);
    Task AddAsync(
        ProductionAggregate production,
        GoalContract contract,
        FrozenContextReference context,
        CancellationToken cancellationToken);
    Task UpdateAsync(ProductionAggregate production, CancellationToken cancellationToken);
}

public interface IContextFreezer
{
    Task<FrozenContextReference> FreezeAsync(
        string userId,
        string projectId,
        GoalContract contract,
        CancellationToken cancellationToken);
}

public interface IAgentEventWriter
{
    Task AppendAsync(
        AgentDomainEvent domainEvent,
        AgentStreamKind streamKind,
        string streamId,
        object publicData,
        CancellationToken cancellationToken);
}

public interface IStreamEventReader
{
    Task<IReadOnlyList<AgentEventEnvelope<JsonElement>>> ReadAsync(
        string userId,
        AgentStreamKind streamKind,
        string streamId,
        long afterSequence,
        int limit,
        CancellationToken cancellationToken);
}

public interface IWorkflowReadModel
{
    Task<WorkflowProjectView> GetProjectAsync(
        string userId,
        string projectId,
        CancellationToken cancellationToken);
}

public interface ITransientAgentStream
{
    Task PublishTokenDeltaAsync(
        string userId,
        string projectId,
        string sessionId,
        string correlationId,
        string delta,
        CancellationToken cancellationToken);
}

public interface IModelGateway
{
    Task<ModelResult> CompleteAsync(ModelRequest request, CancellationToken cancellationToken);
}

public interface IModelProviderAdapter
{
    string Provider { get; }
    ModelProviderCapabilities Capabilities { get; }
    Task<ModelResult> CompleteAsync(ModelRequest request, CancellationToken cancellationToken);
}

public sealed record ModelExecutionReservation(string ExecutionId, ModelResult? CompletedResult);

public interface IModelExecutionStore
{
    Task<ModelExecutionReservation> ReserveAsync(ModelRequest request, CancellationToken cancellationToken);
    Task MarkStartedAsync(string executionId, CancellationToken cancellationToken);
    Task CompleteAsync(string executionId, ModelResult result, CancellationToken cancellationToken);
    Task MarkOutcomeUnknownAsync(string executionId, string error, CancellationToken cancellationToken);
}

public interface ICanonLeaseManager
{
    Task<CanonWriteLease> AcquireAsync(
        string userId,
        string projectId,
        string productionId,
        string owner,
        CancellationToken cancellationToken);
}

public sealed record CanonMergeRequest(
    string RequestId,
    string UserId,
    string ProjectId,
    string GoalId,
    string ProductionId,
    string BranchId,
    int AcceptedThroughChapter,
    CanonWriteLease Lease,
    string CorrelationId,
    string IdempotencyKey);

public interface ICanonMergePort
{
    Task<CanonMergeRequest?> FindByIdempotencyKeyAsync(
        string userId,
        string productionId,
        string idempotencyKey,
        CancellationToken cancellationToken);
    Task<CanonMergeRequest?> GetAsync(string userId, string requestId, CancellationToken cancellationToken);
    Task RequestMergeAsync(CanonMergeRequest request, CancellationToken cancellationToken);
}

public sealed record LegacyProjectSnapshot(
    string ProjectId,
    IReadOnlyList<string> FormalChapterVersionIds,
    IReadOnlyList<string> ConfirmedDecisionIds,
    IReadOnlyList<string> KnowledgeIds,
    string CanonBaselineVersion,
    string KnowledgeSnapshotVersion,
    string QualityContractVersion,
    string StyleProfileVersion,
    IReadOnlyDictionary<string, string> ModelVersions,
    IReadOnlyDictionary<string, string> ProtocolVersions);

public interface ILegacyProjectSnapshotReader
{
    Task<LegacyProjectSnapshot> ReadRecoverableSnapshotAsync(
        string userId,
        string projectId,
        CancellationToken cancellationToken);
}
