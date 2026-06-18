using Microsoft.Extensions.Hosting;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.AgentRuntime;

public sealed class AgentRuntimeWorker : BackgroundService
{
    private readonly IAgentRuntimeQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentRuntimeWorker> _logger;

    public AgentRuntimeWorker(
        IAgentRuntimeQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<AgentRuntimeWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var runtimeRunId in _queue.DequeueAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await ExecuteRunAsync(runtimeRunId, stoppingToken).ConfigureAwait(false);
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

    private async Task ExecuteRunAsync(string runtimeRunId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var runs = scope.ServiceProvider.GetRequiredService<IAgentRuntimeRunService>();
        var events = scope.ServiceProvider.GetRequiredService<IAgentRuntimeEventService>();
        var sessions = scope.ServiceProvider.GetRequiredService<AgentSessionManager>();
        var runtime = scope.ServiceProvider.GetRequiredService<Support.AgentRuntime>();
        var backgroundUser = scope.ServiceProvider.GetRequiredService<IBackgroundUserContext>();
        var run = await runs.TryGetAsync(runtimeRunId, ct).ConfigureAwait(false);
        if (run == null)
            return;

        using var _ = backgroundUser.Push(run.UserId);
        await runs.MarkRunningAsync(run.Id, ct).ConfigureAwait(false);
        await PublishAsync(events, sessions, run, AgentSseEventType.RunUpdate, "Agent 已开始后台执行。", new
        {
            runtimeRunId = run.Id,
            status = AgentRuntimeRunStatus.Running
        }, ct).ConfigureAwait(false);

        try
        {
            var response = await runtime.RunAsync(run.SessionId, run.UserMessage, ct).ConfigureAwait(false);
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await runs.MarkFailedAsync(run.Id, "后台执行被应用停止取消。", CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent runtime run {RuntimeRunId} failed", run.Id);
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
        await events.AppendAsync(new CreateAgentRuntimeEventRequest(
            run.Id,
            run.UserId,
            run.SessionId,
            run.ProjectId,
            type,
            message,
            data), ct).ConfigureAwait(false);

        await sessions.SendEventAsync(run.SessionId, new AgentSseEvent
        {
            Type = type,
            RunId = run.Id,
            Message = message,
            Data = data
        }, ct).ConfigureAwait(false);
    }
}
