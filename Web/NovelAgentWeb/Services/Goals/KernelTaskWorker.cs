using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.Services.Goals;

public sealed class KernelTaskWorker : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<KernelTaskWorker> _logger;
    private readonly string _workerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    public KernelTaskWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<KernelTaskWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_configuration.GetValue("TargetArchitecture:ExecutionEnabled", false))
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            var scheduler = scope.ServiceProvider.GetRequiredService<IKernelTaskScheduler>();
            var executor = scope.ServiceProvider.GetRequiredService<IKernelTaskExecutor>();
            var progress = scope.ServiceProvider.GetRequiredService<IGoalProgressEventPublisher>();
            var claim = await scheduler.ClaimNextAsync(
                _workerId,
                LeaseDuration,
                stoppingToken);
            if (claim == null)
            {
                await Task.Delay(IdleDelay, stoppingToken);
                continue;
            }

            var backgroundUser = scope.ServiceProvider.GetRequiredService<IBackgroundUserContext>();
            using var userScope = backgroundUser.Push(claim.UserId);
            using var heartbeatStop = new CancellationTokenSource();
            using var leaseLost = new CancellationTokenSource();
            using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                stoppingToken,
                leaseLost.Token);
            var heartbeatTask = RenewLeaseUntilStoppedAsync(
                claim,
                heartbeatStop.Token,
                leaseLost,
                stoppingToken);
            try
            {
                await progress.PublishAsync(new GoalProgressEventRequest(
                    claim.UserId,
                    claim.GoalId,
                    AgentSseEventType.GoalTaskStarted,
                    $"开始执行 {claim.TaskType}。",
                    TaskId: claim.TaskId), stoppingToken);
                var result = await executor.ExecuteAsync(claim, executionCancellation.Token);
                if (result.Disposition == KernelTaskExecutionDisposition.Adopted)
                {
                    await scheduler.CompleteAsync(claim, result.ArtifactIds, stoppingToken);
                    await progress.PublishAsync(new GoalProgressEventRequest(
                        claim.UserId,
                        claim.GoalId,
                        AgentSseEventType.GoalTaskCompleted,
                        $"{claim.TaskType} 已完成。",
                        TaskId: claim.TaskId,
                        ArtifactIds: result.ArtifactIds), stoppingToken);
                    await PublishCandidateChangesAsync(
                        scope.ServiceProvider,
                        progress,
                        claim,
                        result.ArtifactIds,
                        stoppingToken);
                }
                else
                {
                    await progress.PublishAsync(new GoalProgressEventRequest(
                        claim.UserId,
                        claim.GoalId,
                        AgentSseEventType.GoalStateChanged,
                        $"{claim.TaskType} 已到达 {result.Disposition} 安全点。",
                        TaskId: claim.TaskId,
                        ArtifactIds: result.ArtifactIds,
                        Action: result.Disposition.ToString().ToLowerInvariant()), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (leaseLost.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Kernel task execution stopped after lease ownership was lost. TaskId={TaskId} UserId={UserId}",
                    claim.TaskId,
                    claim.UserId);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Kernel task failed. TaskId={TaskId} UserId={UserId} Kernel={Kernel}",
                    claim.TaskId,
                    claim.UserId,
                    claim.KernelName);
                try
                {
                    await scheduler.FailAsync(
                        claim,
                        new KernelTaskFailure(
                            KernelTaskFailurePolicy.Classify(exception),
                            exception.Message),
                        stoppingToken);
                    await progress.PublishAsync(new GoalProgressEventRequest(
                        claim.UserId,
                        claim.GoalId,
                        AgentSseEventType.GoalTaskFailed,
                        $"{claim.TaskType} 执行失败。",
                        TaskId: claim.TaskId,
                        Error: exception.Message), stoppingToken);
                }
                catch (InvalidOperationException leaseException)
                {
                    _logger.LogWarning(
                        leaseException,
                        "Kernel task failure was not recorded because the lease is no longer owned. TaskId={TaskId}",
                        claim.TaskId);
                }
            }
            finally
            {
                heartbeatStop.Cancel();
                await heartbeatTask;
            }
        }
    }

    private static async Task PublishCandidateChangesAsync(
        IServiceProvider services,
        IGoalProgressEventPublisher progress,
        KernelTaskClaim claim,
        IReadOnlyList<string> artifactIds,
        CancellationToken cancellationToken)
    {
        if (artifactIds.Count == 0)
            return;
        var db = services.GetRequiredService<NovelAgentDbContext>();
        var candidates = await db.CandidateChapters.AsNoTracking()
            .Where(item =>
                item.UserId == claim.UserId &&
                item.ProjectId == claim.ProjectId &&
                item.GoalId == claim.GoalId &&
                artifactIds.Contains(item.CurrentArtifactId))
            .OrderBy(item => item.ChapterNumber)
            .ThenBy(item => item.Version)
            .ToListAsync(cancellationToken);
        foreach (var candidate in candidates)
        {
            await progress.PublishAsync(new GoalProgressEventRequest(
                claim.UserId,
                claim.GoalId,
                AgentSseEventType.GoalCandidateChanged,
                $"第 {candidate.ChapterNumber} 章候选版本 v{candidate.Version} 已更新。",
                TaskId: claim.TaskId,
                CandidateChapterId: candidate.Id,
                CandidateVersion: candidate.Version,
                ChapterNumber: candidate.ChapterNumber,
                BranchId: candidate.BranchId,
                ArtifactIds: [candidate.CurrentArtifactId]), cancellationToken);
        }
    }

    private async Task RenewLeaseUntilStoppedAsync(
        KernelTaskClaim claim,
        CancellationToken heartbeatToken,
        CancellationTokenSource leaseLost,
        CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromTicks(LeaseDuration.Ticks / 3));
            while (await timer.WaitForNextTickAsync(heartbeatToken))
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var backgroundUser = scope.ServiceProvider.GetRequiredService<IBackgroundUserContext>();
                using var userScope = backgroundUser.Push(claim.UserId);
                var scheduler = scope.ServiceProvider.GetRequiredService<IKernelTaskScheduler>();
                if (!await scheduler.RenewAsync(claim, LeaseDuration, heartbeatToken))
                {
                    leaseLost.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (heartbeatToken.IsCancellationRequested || stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Kernel task lease heartbeat failed. TaskId={TaskId}", claim.TaskId);
            leaseLost.Cancel();
        }
    }
}
