namespace Tianming.NovelAgent.Contracts.Workflow;

public sealed record WorkflowProposalView(
    string Id,
    string SourceSessionId,
    string Status,
    string ContractHash,
    DateTimeOffset UpdatedAt);

public sealed record WorkflowGoalView(
    string Id,
    string Status,
    string Objective,
    string? CurrentRevisionId,
    long Version,
    decimal TotalCostLimit,
    decimal ReservedCost,
    decimal ActualCost);

public sealed record WorkflowProductionView(
    string Id,
    string GoalId,
    string GoalRevisionId,
    string Mode,
    string Status,
    string TaskGraphVersionId,
    long Version,
    string? TerminalReason);

public sealed record WorkflowTaskView(
    string Id,
    string GoalId,
    string TaskType,
    string KernelName,
    string Status,
    int Attempt,
    int MaxAttempts,
    int Priority);

public sealed record WorkflowProjectView(
    string ProjectId,
    IReadOnlyList<WorkflowProposalView> Proposals,
    IReadOnlyList<WorkflowGoalView> Goals,
    IReadOnlyList<WorkflowProductionView> Productions,
    IReadOnlyList<WorkflowTaskView> Tasks,
    long LastWorkflowSequence);
