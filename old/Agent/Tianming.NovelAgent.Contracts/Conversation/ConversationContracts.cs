using System.Text.Json;

namespace Tianming.NovelAgent.Contracts.Conversation;

public enum ConversationDecisionKind
{
    DiscussOnly,
    ProposeGoal,
    ProposeRevision,
    NeedClarification,
    RejectUnsafe
}

public sealed record AppendConversationTurnRequest(
    string IdempotencyKey,
    string Content,
    IReadOnlyList<string>? AttachmentIds = null);

public sealed record ConversationDecisionDto(
    ConversationDecisionKind Kind,
    string Message,
    string? ProposalId,
    string? ProposalJson,
    string? ProposalHash,
    bool AutoConfirmRequested = false);

public sealed record AgentToolCall(
    string Name,
    IReadOnlyDictionary<string, JsonElement>? Arguments = null);

public sealed record ConversationTurnResult(
    string MessageId,
    ConversationDecisionDto Decision,
    string CorrelationId,
    ConfirmGoalProposalResult? Confirmation = null,
    string? ToolError = null);

public sealed record ConfirmGoalProposalRequest(string IdempotencyKey, string? ConfirmationNote = null);

public sealed record ConfirmGoalProposalResult(
    string GoalId,
    string GoalRevisionId,
    string ProductionId,
    string CorrelationId);

public enum NovelAgentProductionMode
{
    SingleChapter,
    InteractiveBatch,
    AutonomousBook
}

public sealed record CreateLegacyRecoveryProposalRequest(
    string SessionId,
    string IdempotencyKey,
    string Objective,
    NovelAgentProductionMode Mode,
    int StartChapter,
    int EndChapter,
    IReadOnlyList<string> SuccessCriteria,
    IReadOnlyList<string>? MustPreserve = null,
    IReadOnlyList<string>? MustHappen = null,
    IReadOnlyList<string>? MustNotChange = null,
    string AcceptancePolicy = "human_prefix_acceptance",
    string ReworkPolicy = "create_revision",
    decimal TotalCostLimit = 0,
    string? MissionPlanId = null,
    string? RuntimeRunId = null,
    IReadOnlyList<string>? PendingToolExecutionIds = null);

public sealed record CreateLegacyRecoveryProposalResult(
    string ProposalId,
    string ContractHash,
    LegacyRecoveryEvidence Evidence,
    string CorrelationId);

public sealed record LegacyRecoveryEvidence(
    IReadOnlyList<string> FormalChapterVersionIds,
    IReadOnlyList<string> ConfirmedDecisionIds,
    IReadOnlyList<string> KnowledgeIds);
