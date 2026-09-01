using Tianming.NovelAgent.Domain.Common;

namespace Tianming.NovelAgent.Domain.Goals;

public enum CreativeGoalStatus
{
    Confirmed,
    Active,
    Completed,
    Cancelled,
    Blocked,
    Superseded
}

public sealed record GoalConfirmation(
    string ActorId,
    DateTimeOffset ConfirmedAt,
    string IdempotencyKey,
    string SourceSessionId,
    string SourceProposalId);

public sealed record GoalRevision(
    string Id,
    int RevisionNumber,
    GoalContract Contract,
    FrozenContextReference Context,
    GoalConfirmation Confirmation,
    string ContractHash,
    string SchemaVersion,
    string Reason,
    string? PreviousRevisionId);

public sealed class CreativeGoal
{
    private readonly List<GoalRevision> _revisions;

    private CreativeGoal(
        string id,
        string userId,
        string projectId,
        IEnumerable<GoalRevision> revisions,
        CreativeGoalStatus status = CreativeGoalStatus.Confirmed,
        long version = 1)
    {
        Id = id;
        UserId = userId;
        ProjectId = projectId;
        _revisions = revisions.OrderBy(x => x.RevisionNumber).ToList();
        if (_revisions.Count == 0)
            throw new DomainRuleException("goal.revision.required", "A goal requires at least one revision.");
        Status = status;
        Version = version;
    }

    public string Id { get; }
    public string UserId { get; }
    public string ProjectId { get; }
    public CreativeGoalStatus Status { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyList<GoalRevision> Revisions => _revisions;
    public GoalRevision CurrentRevision => _revisions[^1];

    public static CreativeGoal Confirm(string id, GoalProposal proposal, GoalRevision revision)
    {
        if (proposal.Status != GoalProposalStatus.Proposed)
            throw new DomainRuleException("goal.proposal.not_confirmable", "Only a proposed goal can be confirmed.");
        if (revision.RevisionNumber != 1 || revision.Confirmation.SourceProposalId != proposal.Id)
            throw new DomainRuleException("goal.revision.initial.invalid", "The initial revision must reference the source proposal.");

        proposal.Confirm();
        return new CreativeGoal(id, proposal.UserId, proposal.ProjectId, [revision]);
    }

    public static CreativeGoal Restore(
        string id,
        string userId,
        string projectId,
        IEnumerable<GoalRevision> revisions,
        CreativeGoalStatus status,
        long version) =>
        new(id, userId, projectId, revisions, status, version);

    public void Activate()
    {
        Ensure(CreativeGoalStatus.Confirmed);
        Status = CreativeGoalStatus.Active;
        Version++;
    }

    public void AddRevision(GoalRevision revision)
    {
        if (Status is CreativeGoalStatus.Completed or CreativeGoalStatus.Cancelled or CreativeGoalStatus.Superseded)
            throw InvalidTransition(CreativeGoalStatus.Superseded);
        if (revision.RevisionNumber != CurrentRevision.RevisionNumber + 1 || revision.PreviousRevisionId != CurrentRevision.Id)
            throw new DomainRuleException("goal.revision.sequence.invalid", "Goal revisions must be append-only and sequential.");

        _revisions.Add(revision);
        Status = CreativeGoalStatus.Superseded;
        Version++;
    }

    public void Complete() => MoveFromActive(CreativeGoalStatus.Completed);
    public void Cancel() => MoveFromNonTerminal(CreativeGoalStatus.Cancelled);
    public void Block() => MoveFromActive(CreativeGoalStatus.Blocked);

    private void MoveFromActive(CreativeGoalStatus target)
    {
        Ensure(CreativeGoalStatus.Active);
        Status = target;
        Version++;
    }

    private void MoveFromNonTerminal(CreativeGoalStatus target)
    {
        if (Status is CreativeGoalStatus.Completed or CreativeGoalStatus.Cancelled or CreativeGoalStatus.Superseded)
            throw InvalidTransition(target);
        Status = target;
        Version++;
    }

    private void Ensure(CreativeGoalStatus expected)
    {
        if (Status != expected)
            throw InvalidTransition(expected);
    }

    private DomainRuleException InvalidTransition(CreativeGoalStatus target) =>
        new("goal.transition.invalid", $"Goal {Id} cannot transition from {Status} to {target}.");
}
