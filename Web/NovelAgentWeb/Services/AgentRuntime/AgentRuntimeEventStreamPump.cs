using Microsoft.Extensions.Hosting;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.AgentRuntime;

public interface IAgentRuntimeEventStreamPump
{
    Task<int> PumpActiveSessionsAsync(int maxSessions = 100, CancellationToken ct = default);
}

public sealed class AgentRuntimeEventStreamPump : IAgentRuntimeEventStreamPump
{
    private readonly IAgentRuntimeRunService _runs;
    private readonly IAgentRuntimeEventStreamConsumer _consumer;
    private readonly AgentSseEventBus _events;
    private readonly IAgentRuntimeEventService _runtimeEvents;
    private readonly ILogger<AgentRuntimeEventStreamPump> _logger;
    private readonly string _groupName;
    private readonly string _consumerName;
    private readonly int _batchSize;
    private readonly TimeSpan _pendingIdleTime;
    private readonly int _maxPendingDeliveryCount;

    public AgentRuntimeEventStreamPump(
        IAgentRuntimeRunService runs,
        IAgentRuntimeEventStreamConsumer consumer,
        AgentSseEventBus events,
        IAgentRuntimeEventService runtimeEvents,
        IConfiguration configuration,
        ILogger<AgentRuntimeEventStreamPump> logger)
    {
        _runs = runs;
        _consumer = consumer;
        _events = events;
        _runtimeEvents = runtimeEvents;
        _logger = logger;
        _groupName = Normalize(configuration["RuntimeEvents:StreamConsumerGroup"], "agent-runtime-sse");
        _consumerName = $"{Environment.MachineName}-{AgentRuntimeEventFanoutInstance.Id[..8]}";
        _batchSize = Math.Clamp(configuration.GetValue("RuntimeEvents:StreamBatchSize", 50), 1, 200);
        _maxPendingDeliveryCount = Math.Clamp(configuration.GetValue("RuntimeEvents:MaxPendingDeliveryCount", 5), 1, 100);
        if (!TimeSpan.TryParse(configuration["RuntimeEvents:PendingIdleTime"], out _pendingIdleTime))
            _pendingIdleTime = TimeSpan.FromSeconds(30);
    }

    public async Task<int> PumpActiveSessionsAsync(int maxSessions = 100, CancellationToken ct = default)
    {
        var sessions = await _runs
            .ListActiveSessionCursorsAsync(Math.Clamp(maxSessions, 1, 500), ct)
            .ConfigureAwait(false);
        var delivered = 0;
        foreach (var session in sessions)
        {
            ct.ThrowIfCancellationRequested();
            delivered += await PumpSessionAsync(session, ct).ConfigureAwait(false);
        }

        return delivered;
    }

    private async Task<int> PumpSessionAsync(AgentRuntimeActiveSessionCursor session, CancellationToken ct)
    {
        var sessionId = session.SessionId;
        try
        {
            await _consumer.EnsureConsumerGroupAsync(sessionId, _groupName, ct).ConfigureAwait(false);
            var pendingEvents = await _consumer
                .ClaimPendingAsync(sessionId, _groupName, _consumerName, _pendingIdleTime, _batchSize, ct)
                .ConfigureAwait(false);
            var newEvents = await _consumer
                .ReadGroupAsync(sessionId, _groupName, _consumerName, _batchSize, ct)
                .ConfigureAwait(false);

            var delivered = 0;
            foreach (var streamEvent in pendingEvents.Concat(newEvents))
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(streamEvent.Event.SessionId))
                    streamEvent.Event.SessionId = sessionId;

                if (streamEvent.IsPendingRecovery && streamEvent.DeliveryCount >= _maxPendingDeliveryCount)
                {
                    await RecordPoisonEventAsync(session, streamEvent, ct).ConfigureAwait(false);
                    await _consumer.AcknowledgeAsync(sessionId, _groupName, streamEvent.StreamId, ct).ConfigureAwait(false);
                    continue;
                }

                await _events.SendAsync(sessionId, streamEvent.Event, ct).ConfigureAwait(false);
                await _consumer.AcknowledgeAsync(sessionId, _groupName, streamEvent.StreamId, ct).ConfigureAwait(false);
                delivered++;
            }

            return delivered;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis runtime event stream pump failed for session {SessionId}.", sessionId);
            return 0;
        }
    }

    private async Task RecordPoisonEventAsync(
        AgentRuntimeActiveSessionCursor session,
        AgentRuntimeStreamEvent streamEvent,
        CancellationToken ct)
    {
        await _runtimeEvents.AppendAsync(
            new CreateAgentRuntimeEventRequest(
                RuntimeRunId: session.RuntimeRunId,
                UserId: session.UserId,
                SessionId: session.SessionId,
                ProjectId: session.ProjectId,
                Type: "runtime_event_delivery_failed",
                Message: "运行时事件多次投递失败，已写入失败记录并停止 Redis Stream 重试。",
                Data: new
                {
                    streamId = streamEvent.StreamId,
                    originalEventId = streamEvent.Event.EventId,
                    originalType = streamEvent.Event.Type,
                    deliveryCount = streamEvent.DeliveryCount,
                    consumerGroup = _groupName,
                    consumerName = _consumerName
                },
                Stage: "runtime_event_stream_pump",
                Status: "failed",
                ArtifactType: "runtime_event",
                ArtifactId: streamEvent.Event.EventId,
                DisplaySurface: AgentRuntimeEventSurface.AdminDebug,
                DisplayPolicy: AgentRuntimeEventDisplayPolicy.DebugOnly),
            ct).ConfigureAwait(false);
    }

    private static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}

public sealed class AgentRuntimeEventStreamPumpHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentRuntimeEventStreamPumpHostedService> _logger;
    private readonly TimeSpan _interval;
    private readonly int _maxSessions;

    public AgentRuntimeEventStreamPumpHostedService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<AgentRuntimeEventStreamPumpHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        if (!TimeSpan.TryParse(configuration["RuntimeEvents:StreamPumpInterval"], out _interval))
            _interval = TimeSpan.FromSeconds(2);
        _maxSessions = Math.Clamp(configuration.GetValue("RuntimeEvents:StreamPumpMaxSessions", 100), 1, 500);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var pump = scope.ServiceProvider.GetRequiredService<IAgentRuntimeEventStreamPump>();
                await pump.PumpActiveSessionsAsync(_maxSessions, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis runtime event stream pump hosted service tick failed.");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
