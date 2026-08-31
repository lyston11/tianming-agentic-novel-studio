namespace TM.Web.NovelAgentWeb.Services.Goals;

public sealed record KernelTaskClaim(
    string TaskId,
    string UserId,
    string ProjectId,
    string GoalId,
    string TaskGraphVersionId,
    string? BranchId,
    string KernelName,
    string TaskType,
    int Attempt,
    string LeaseOwner,
    DateTime LeaseExpiresAt);

public sealed record KernelTaskFailure(
    KernelTaskFailureCategory Category,
    string Message);

public interface IKernelTaskScheduler
{
    Task<KernelTaskClaim?> ClaimNextAsync(
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        KernelTaskClaim claim,
        IReadOnlyList<string> artifactIds,
        CancellationToken cancellationToken = default);

    Task<bool> RenewAsync(
        KernelTaskClaim claim,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task FailAsync(
        KernelTaskClaim claim,
        string error,
        CancellationToken cancellationToken = default);

    Task FailAsync(
        KernelTaskClaim claim,
        KernelTaskFailure failure,
        CancellationToken cancellationToken = default) =>
        FailAsync(claim, failure.Message, cancellationToken);
}

public enum KernelTaskExecutionDisposition
{
    Adopted,
    Paused,
    Canceled,
    BudgetExceeded
}

public sealed record KernelTaskExecutionResult(
    IReadOnlyList<string> ArtifactIds,
    KernelTaskExecutionDisposition Disposition = KernelTaskExecutionDisposition.Adopted);

public interface IKernelTaskExecutor
{
    Task<KernelTaskExecutionResult> ExecuteAsync(
        KernelTaskClaim claim,
        CancellationToken cancellationToken = default);
}
