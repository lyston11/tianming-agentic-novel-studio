using Tianming.NovelAgent.Domain.Common;

namespace Tianming.NovelAgent.Domain.Goals;

public enum GoalProposalStatus
{
    Draft,
    Proposed,
    Confirmed,
    Rejected,
    Superseded,
    Discarded
}

public sealed class GoalProposal
{
    public GoalProposal(string id, string userId, string projectId, string sourceSessionId, GoalContract contract)
        : this(id, userId, projectId, sourceSessionId, contract, GoalProposalStatus.Draft, null)
    {
    }

    private GoalProposal(
        string id,
        string userId,
        string projectId,
        string sourceSessionId,
        GoalContract contract,
        GoalProposalStatus status,
        string? decisionReason)
    {
        contract.Validate();
        Id = Require(id, nameof(id));
        UserId = Require(userId, nameof(userId));
        ProjectId = Require(projectId, nameof(projectId));
        SourceSessionId = Require(sourceSessionId, nameof(sourceSessionId));
        Contract = contract;
        Status = status;
        DecisionReason = decisionReason;
    }

    public string Id { get; }
    public string UserId { get; }
    public string ProjectId { get; }
    public string SourceSessionId { get; }
    public GoalContract Contract { get; }
    public GoalProposalStatus Status { get; private set; }
    public string? DecisionReason { get; private set; }

    public static GoalProposal Restore(
        string id,
        string userId,
        string projectId,
        string sourceSessionId,
        GoalContract contract,
        GoalProposalStatus status,
        string? decisionReason = null) =>
        new(id, userId, projectId, sourceSessionId, contract, status, decisionReason);

    public void Propose()
    {
        Ensure(GoalProposalStatus.Draft);
        Status = GoalProposalStatus.Proposed;
    }

    public void Confirm()
    {
        Ensure(GoalProposalStatus.Proposed);
        Status = GoalProposalStatus.Confirmed;
    }

    public void Reject(string reason)
    {
        Ensure(GoalProposalStatus.Proposed);
        DecisionReason = Require(reason, nameof(reason));
        Status = GoalProposalStatus.Rejected;
    }

    public void Supersede(string reason)
    {
        if (Status is not (GoalProposalStatus.Draft or GoalProposalStatus.Proposed))
            throw InvalidTransition(GoalProposalStatus.Superseded);
        DecisionReason = Require(reason, nameof(reason));
        Status = GoalProposalStatus.Superseded;
    }

    private void Ensure(GoalProposalStatus expected)
    {
        if (Status != expected)
            throw InvalidTransition(expected);
    }

    private DomainRuleException InvalidTransition(GoalProposalStatus target) =>
        new("proposal.transition.invalid", $"Proposal {Id} cannot transition from {Status} to {target}.");

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new DomainRuleException("value.required", $"{name} is required.")
            : value;
}
