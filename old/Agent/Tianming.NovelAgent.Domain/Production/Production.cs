using Tianming.NovelAgent.Domain.Common;
using Tianming.NovelAgent.Domain.Goals;

namespace Tianming.NovelAgent.Domain.Production;

public enum ProductionStatus
{
    Planned,
    Running,
    Pausing,
    Paused,
    AwaitingAcceptance,
    MergingCanon,
    Blocked,
    Failed,
    Cancelled,
    Completed
}

public sealed record CanonWriteLease(string Owner, DateTimeOffset ExpiresAt, long FenceToken)
{
    public bool IsValid(string owner, DateTimeOffset now) => Owner == owner && ExpiresAt > now;
}

public sealed class Production
{
    public Production(string id, string userId, string projectId, string goalId, string goalRevisionId, ProductionMode mode, TaskGraph graph)
        : this(id, userId, projectId, goalId, goalRevisionId, mode, graph, ProductionStatus.Planned, 1, null, null)
    {
    }

    private Production(
        string id,
        string userId,
        string projectId,
        string goalId,
        string goalRevisionId,
        ProductionMode mode,
        TaskGraph graph,
        ProductionStatus status,
        long version,
        CanonWriteLease? canonLease,
        string? terminalReason)
    {
        Id = Require(id, nameof(id));
        UserId = Require(userId, nameof(userId));
        ProjectId = Require(projectId, nameof(projectId));
        GoalId = Require(goalId, nameof(goalId));
        GoalRevisionId = Require(goalRevisionId, nameof(goalRevisionId));
        Mode = mode;
        Graph = graph;
        Status = status;
        Version = version;
        CanonLease = canonLease;
        TerminalReason = terminalReason;
    }

    public string Id { get; }
    public string UserId { get; }
    public string ProjectId { get; }
    public string GoalId { get; }
    public string GoalRevisionId { get; }
    public ProductionMode Mode { get; }
    public TaskGraph Graph { get; }
    public ProductionStatus Status { get; private set; }
    public long Version { get; private set; }
    public CanonWriteLease? CanonLease { get; private set; }
    public string? TerminalReason { get; private set; }

    public static Production Restore(
        string id,
        string userId,
        string projectId,
        string goalId,
        string goalRevisionId,
        ProductionMode mode,
        TaskGraph graph,
        ProductionStatus status,
        long version,
        CanonWriteLease? canonLease,
        string? terminalReason) =>
        new(id, userId, projectId, goalId, goalRevisionId, mode, graph, status, version, canonLease, terminalReason);

    public void Start() => Move(ProductionStatus.Planned, ProductionStatus.Running);

    public void ReachAcceptanceGate() => Move(ProductionStatus.Running, ProductionStatus.AwaitingAcceptance);

    public void AcceptPrefix(CanonWriteLease lease, string owner, DateTimeOffset now)
    {
        if (Status != ProductionStatus.AwaitingAcceptance)
            throw InvalidTransition(ProductionStatus.MergingCanon);
        if (!lease.IsValid(owner, now))
            throw new DomainRuleException("production.canon_lease.invalid", "A valid canon write lease is required.");
        CanonLease = lease;
        Status = ProductionStatus.MergingCanon;
        Version++;
    }

    public void CanonMerged(bool hasMoreBatches)
    {
        Move(ProductionStatus.MergingCanon, hasMoreBatches ? ProductionStatus.Running : ProductionStatus.Completed);
        CanonLease = null;
    }

    public void Pause(bool hasRunningTask)
    {
        if (Status != ProductionStatus.Running)
            throw InvalidTransition(ProductionStatus.Pausing);
        Status = hasRunningTask ? ProductionStatus.Pausing : ProductionStatus.Paused;
        Version++;
    }

    public void ReachSafePoint() => Move(ProductionStatus.Pausing, ProductionStatus.Paused);
    public void Resume() => Move(ProductionStatus.Paused, ProductionStatus.Running);
    public void Block(string reason) => End(ProductionStatus.Blocked, reason);
    public void Fail(string reason) => End(ProductionStatus.Failed, reason);
    public void Cancel(string reason) => End(ProductionStatus.Cancelled, reason);

    private void Move(ProductionStatus expected, ProductionStatus target)
    {
        if (Status != expected)
            throw InvalidTransition(target);
        Status = target;
        Version++;
    }

    private void End(ProductionStatus target, string reason)
    {
        if (IsTerminal(Status))
            throw InvalidTransition(target);
        Status = target;
        TerminalReason = Require(reason, nameof(reason));
        CanonLease = null;
        Version++;
    }

    private static bool IsTerminal(ProductionStatus status) =>
        status is ProductionStatus.Failed or ProductionStatus.Cancelled or ProductionStatus.Completed;

    private DomainRuleException InvalidTransition(ProductionStatus target) =>
        new("production.transition.invalid", $"Production {Id} cannot transition from {Status} to {target}.");

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new DomainRuleException("value.required", $"{name} is required.")
            : value;
}
