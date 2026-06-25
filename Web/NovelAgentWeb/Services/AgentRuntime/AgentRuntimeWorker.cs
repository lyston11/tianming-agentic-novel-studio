using Microsoft.Extensions.Hosting;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.AgentTools;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.AgentRuntime;

public sealed class AgentRuntimeWorker : BackgroundService
{
    private static readonly TimeSpan RuntimeRunLeaseTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RuntimeRunLeaseRenewalInterval = TimeSpan.FromMinutes(2);
    private readonly IAgentRuntimeQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentRuntimeWorker> _logger;
    private readonly IAgentRuntimeRunLeaseService _leases;

    public AgentRuntimeWorker(
        IAgentRuntimeQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<AgentRuntimeWorker> logger,
        IAgentRuntimeRunLeaseService leases)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _leases = leases;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CloseStaleToolExecutionsAsync(stoppingToken).ConfigureAwait(false);
        await CloseStaleRuntimeRunsAsync(stoppingToken).ConfigureAwait(false);
        await foreach (var runtimeRunId in _queue.DequeueAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await ExecuteRunWithLeaseAsync(runtimeRunId, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled Agent runtime worker error for run {RuntimeRunId}", runtimeRunId);
            }
        }
    }

    internal async Task ExecuteRunWithLeaseAsync(string runtimeRunId, CancellationToken ct)
    {
        await using var lease = await _leases.TryAcquireAsync(runtimeRunId, ct).ConfigureAwait(false);
        if (lease == null)
        {
            _logger.LogInformation("Skipped Agent runtime run {RuntimeRunId} because another worker owns the lease.", runtimeRunId);
            return;
        }

        lease.StartAutoRenewal(RuntimeRunLeaseRenewalInterval, RuntimeRunLeaseTtl, ct);
        await ExecuteRunAsync(runtimeRunId, ct).ConfigureAwait(false);
    }

    private async Task CloseStaleRuntimeRunsAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var runs = scope.ServiceProvider.GetRequiredService<IAgentRuntimeRunService>();
            var closed = await runs
                .FailStaleActiveRunsAsync(
                    TimeSpan.FromMinutes(30),
                    "应用重启或 Redis 心跳丢失后，从数据库 truth source 标记为可恢复失败。",
                    ct)
                .ConfigureAwait(false);
            if (closed > 0)
                _logger.LogWarning("Closed {Count} stale Agent runtime run records on startup.", closed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to close stale Agent runtime run records on startup.");
        }
    }

    private async Task CloseStaleToolExecutionsAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var toolLedger = scope.ServiceProvider.GetRequiredService<IAgentToolExecutionLedger>();
            var closed = await toolLedger.FailAllRunningAsync("应用启动时清理上次未收尾的工具执行。", ct).ConfigureAwait(false);
            if (closed > 0)
                _logger.LogWarning("Closed {Count} stale running Agent tool execution records on startup.", closed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to close stale running Agent tool execution records on startup.");
        }
    }

    private async Task ExecuteRunAsync(string runtimeRunId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var runs = scope.ServiceProvider.GetRequiredService<IAgentRuntimeRunService>();
        var run = await runs.TryGetAsync(runtimeRunId, ct).ConfigureAwait(false);
        if (run == null)
            return;

        var events = scope.ServiceProvider.GetRequiredService<IAgentRuntimeEventService>();
        var sessions = scope.ServiceProvider.GetRequiredService<AgentSessionManager>();
        var runtime = scope.ServiceProvider.GetRequiredService<Support.AgentRuntime>();
        var backgroundUser = scope.ServiceProvider.GetRequiredService<IBackgroundUserContext>();
        var toolLedger = scope.ServiceProvider.GetRequiredService<IAgentToolExecutionLedger>();

        using var _ = backgroundUser.Push(run.UserId);
        await runs.MarkRunningAsync(run.Id, ct).ConfigureAwait(false);
        await PublishAsync(events, sessions, run, AgentSseEventType.RunUpdate, "Agent 已开始后台执行。", new
        {
            runtimeRunId = run.Id,
            status = AgentRuntimeRunStatus.Running
        }, ct).ConfigureAwait(false);

        try
        {
            var response = await runtime.RunAsync(run.SessionId, run.UserMessage, ct, run.Id).ConfigureAwait(false);
            await runs.MarkCompletedAsync(run.Id, response, ct).ConfigureAwait(false);
            await PublishAsync(events, sessions, run, AgentSseEventType.AgentReply, response.Reply, AgentChatResponsePublicProjection.ToPublic(response), ct).ConfigureAwait(false);
            await PublishAsync(events, sessions, run, AgentSseEventType.RunUpdate, "后台执行已完成。", new
            {
                runtimeRunId = run.Id,
                status = AgentRuntimeRunStatus.Completed,
                phase = response.Phase,
                runId = response.RunId
            }, ct).ConfigureAwait(false);
        }
        catch (AgentRuntimeRunCancelledException ex)
        {
            const string reason = "后台执行已取消。";
            await toolLedger.FailRunningForSessionAsync(run.UserId, run.SessionId, run.ProjectId, reason, CancellationToken.None).ConfigureAwait(false);
            await runs.MarkCancelledAsync(run.Id, ex.Stage, reason, CancellationToken.None).ConfigureAwait(false);
            await PublishAsync(events, sessions, run, AgentSseEventType.RunUpdate, reason, new
            {
                runtimeRunId = run.Id,
                status = AgentRuntimeRunStatus.Cancelled,
                phase = ex.Stage
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            const string reason = "后台执行被应用停止取消。";
            await toolLedger.FailRunningForSessionAsync(run.UserId, run.SessionId, run.ProjectId, reason, CancellationToken.None).ConfigureAwait(false);
            await runs.MarkFailedAsync(run.Id, reason, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent runtime run {RuntimeRunId} failed", run.Id);
            await toolLedger.FailRunningForSessionAsync(run.UserId, run.SessionId, run.ProjectId, ex.Message, ct).ConfigureAwait(false);
            await runs.MarkFailedAsync(run.Id, ex.Message, ct).ConfigureAwait(false);
            await PublishAsync(events, sessions, run, AgentSseEventType.StepFail, "后台执行失败。", new
            {
                runtimeRunId = run.Id,
                error = ex.Message
            }, ct).ConfigureAwait(false);
        }
    }

    private static async Task PublishAsync(
        IAgentRuntimeEventService events,
        AgentSessionManager sessions,
        Data.Entities.AgentRuntimeRun run,
        string type,
        string message,
        object? data,
        CancellationToken ct)
    {
        var eventData = MergeRuntimeWorkerEventData(data, run.Id, run.SourceMessageId);
        var saved = await events.AppendAsync(new CreateAgentRuntimeEventRequest(
            run.Id,
            run.UserId,
            run.SessionId,
            run.ProjectId,
            type,
            message,
            eventData), ct).ConfigureAwait(false);

        await sessions.SendEventAsync(run.SessionId, new AgentSseEvent
        {
            EventId = saved.Id,
            Type = type,
            RunId = run.Id,
            SourceMessageId = run.SourceMessageId,
            Stage = saved.Stage,
            Status = saved.Status,
            ArtifactType = saved.ArtifactType,
            ArtifactId = saved.ArtifactId,
            DisplaySurface = saved.DisplaySurface,
            DisplayPolicy = saved.DisplayPolicy,
            Message = message,
            Data = eventData,
            Timestamp = saved.CreatedAt
        }, ct).ConfigureAwait(false);
    }

    private static object? MergeRuntimeWorkerEventData(object? data, string runtimeRunId, string sourceMessageId)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(runtimeRunId))
            result["runtimeRunId"] = runtimeRunId.Trim();
        if (!string.IsNullOrWhiteSpace(sourceMessageId))
            result["sourceMessageId"] = sourceMessageId.Trim();

        if (data == null)
            return result.Count == 0 ? null : result;

        try
        {
            var element = System.Text.Json.JsonSerializer.SerializeToElement(data);
            if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                    result[property.Name] = property.Value.Clone();
            }
            else
            {
                result["payload"] = element.Clone();
            }
        }
        catch
        {
            result["payload"] = data;
        }

        if (!string.IsNullOrWhiteSpace(runtimeRunId))
            result["runtimeRunId"] = runtimeRunId.Trim();
        if (!string.IsNullOrWhiteSpace(sourceMessageId))
            result["sourceMessageId"] = sourceMessageId.Trim();

        return result;
    }
}
