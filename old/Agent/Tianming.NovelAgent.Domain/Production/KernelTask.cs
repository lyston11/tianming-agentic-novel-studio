using Tianming.NovelAgent.Domain.Common;

namespace Tianming.NovelAgent.Domain.Production;

public enum KernelTaskStatus
{
    Pending,
    Ready,
    Leased,
    Running,
    WaitingForHuman,
    Succeeded,
    Failed,
    Cancelled
}

public sealed class KernelTask
{
    public KernelTask(string id, TaskNodeDefinition definition)
    {
        Id = id;
        Definition = definition;
        Status = definition.IsHumanGate ? KernelTaskStatus.WaitingForHuman : KernelTaskStatus.Pending;
    }

    public string Id { get; }
    public TaskNodeDefinition Definition { get; }
    public KernelTaskStatus Status { get; private set; }
    public int Attempt { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    public void MarkReady()
    {
        Ensure(KernelTaskStatus.Pending);
        Status = KernelTaskStatus.Ready;
    }

    public void Claim(string owner, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        Ensure(KernelTaskStatus.Ready);
        if (string.IsNullOrWhiteSpace(owner) || expiresAt <= now)
            throw new DomainRuleException("task.lease.invalid", "A future task lease is required.");
        LeaseOwner = owner;
        LeaseExpiresAt = expiresAt;
        Status = KernelTaskStatus.Leased;
    }

    public void Start(string owner, DateTimeOffset now)
    {
        Ensure(KernelTaskStatus.Leased);
        EnsureLease(owner, now);
        Attempt++;
        Status = KernelTaskStatus.Running;
    }

    public void Succeed(string owner, DateTimeOffset now)
    {
        Ensure(KernelTaskStatus.Running);
        EnsureLease(owner, now);
        ClearLease();
        Status = KernelTaskStatus.Succeeded;
    }

    public void Fail(string owner, DateTimeOffset now, bool retryable)
    {
        Ensure(KernelTaskStatus.Running);
        EnsureLease(owner, now);
        ClearLease();
        Status = retryable && Attempt < Definition.MaxAttempts ? KernelTaskStatus.Ready : KernelTaskStatus.Failed;
    }

    public void CompleteHumanGate()
    {
        Ensure(KernelTaskStatus.WaitingForHuman);
        Status = KernelTaskStatus.Succeeded;
    }

    public void Cancel()
    {
        if (Status is KernelTaskStatus.Succeeded or KernelTaskStatus.Failed or KernelTaskStatus.Cancelled)
            throw new DomainRuleException("task.cancel.invalid", $"Task {Id} is already terminal.");
        ClearLease();
        Status = KernelTaskStatus.Cancelled;
    }

    private void Ensure(KernelTaskStatus expected)
    {
        if (Status != expected)
            throw new DomainRuleException("task.transition.invalid", $"Task {Id} expected {expected} but is {Status}.");
    }

    private void EnsureLease(string owner, DateTimeOffset now)
    {
        if (LeaseOwner != owner || LeaseExpiresAt <= now)
            throw new DomainRuleException("task.lease.lost", $"Task {Id} lease is missing or expired.");
    }

    private void ClearLease()
    {
        LeaseOwner = null;
        LeaseExpiresAt = null;
    }
}
