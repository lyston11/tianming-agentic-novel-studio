using System.Text.Json;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.AgentRuntime;

internal static class AgentRuntimeEventFanoutInstance
{
    public static readonly string Id = Guid.NewGuid().ToString("N");
}

public interface IAgentRuntimeEventFanout
{
    Task PublishAsync(string userId, string sessionId, AgentSseEvent evt, CancellationToken ct = default);
    Task<IReadOnlyList<AgentSseEvent>> ReplayAsync(string userId, string sessionId, string? afterEventId = null, int limit = 100, CancellationToken ct = default);
}

public sealed class RedisAgentRuntimeEventFanout : IAgentRuntimeEventFanout
{
    public const string ChannelName = "agent_runtime:events";
    private readonly IConnectionMultiplexer? _redis;
    private readonly string _instanceName;
    private readonly int _replayLimit;
    private readonly TimeSpan _replayTtl;
    private readonly ILogger<RedisAgentRuntimeEventFanout> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public RedisAgentRuntimeEventFanout(
        IEnumerable<IConnectionMultiplexer> redisConnections,
        IConfiguration configuration,
        ILogger<RedisAgentRuntimeEventFanout> logger)
    {
        _redis = redisConnections.FirstOrDefault();
        _instanceName = configuration["Redis:InstanceName"] ?? "NovelAgent:";
        _replayLimit = Math.Clamp(configuration.GetValue("RuntimeEvents:ReplayLimit", 200), 20, 1000);
        if (!TimeSpan.TryParse(configuration["RuntimeEvents:ReplayTtl"], out _replayTtl))
            _replayTtl = TimeSpan.FromMinutes(10);
        _logger = logger;
    }

    public async Task PublishAsync(string userId, string sessionId, AgentSseEvent evt, CancellationToken ct = default)
    {
        if (_redis == null)
            return;

        ct.ThrowIfCancellationRequested();
        try
        {
            var scopeKey = BuildScopeKey(userId, sessionId);
            evt.SessionId = sessionId;
            var payload = JsonSerializer.Serialize(
                new AgentRuntimeEventFanoutEnvelope(AgentRuntimeEventFanoutInstance.Id, evt),
                _jsonOptions);
            var database = _redis.GetDatabase();
            var replayKey = BuildReplayKey(scopeKey);
            await database.ListRightPushAsync(replayKey, payload, When.Always, CommandFlags.None)
                .ConfigureAwait(false);
            await database.ListTrimAsync(replayKey, -_replayLimit, -1, CommandFlags.None)
                .ConfigureAwait(false);
            await database.KeyExpireAsync(replayKey, _replayTtl, flags: CommandFlags.None)
                .ConfigureAwait(false);
            await database.StreamAddAsync(
                    BuildStreamKey(scopeKey),
                    "payload",
                    payload,
                    messageId: null,
                    maxLength: _replayLimit,
                    useApproximateMaxLength: true,
                    flags: CommandFlags.None)
                .ConfigureAwait(false);
            await _redis.GetSubscriber()
                .PublishAsync(RedisChannel.Literal(ChannelName), payload)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis runtime event fanout publish failed for session {SessionId}.", sessionId);
        }
    }

    public async Task<IReadOnlyList<AgentSseEvent>> ReplayAsync(
        string userId,
        string sessionId,
        string? afterEventId = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        if (_redis == null)
            return Array.Empty<AgentSseEvent>();

        ct.ThrowIfCancellationRequested();
        try
        {
            var scopeKey = BuildScopeKey(userId, sessionId);
            var values = await _redis.GetDatabase()
                .ListRangeAsync(BuildReplayKey(scopeKey), 0, -1, CommandFlags.None)
                .ConfigureAwait(false);
            var requestedLimit = Math.Clamp(limit, 1, _replayLimit);
            var events = new List<AgentSseEvent>();
            var cursorSeen = string.IsNullOrWhiteSpace(afterEventId);

            foreach (var value in values)
            {
                ct.ThrowIfCancellationRequested();
                var evt = DeserializeEvent(value);
                if (evt == null || !string.Equals(evt.SessionId, sessionId, StringComparison.Ordinal))
                    continue;

                if (!cursorSeen)
                {
                    if (string.Equals(evt.EventId, afterEventId, StringComparison.Ordinal))
                        cursorSeen = true;
                    continue;
                }

                events.Add(evt);
                if (events.Count >= requestedLimit)
                    break;
            }

            if (events.Count == 0)
            {
                var streamEvents = await ReplayFromStreamAsync(scopeKey, sessionId, afterEventId, requestedLimit, ct)
                    .ConfigureAwait(false);
                events.AddRange(streamEvents);
            }

            return events;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis runtime event replay failed for session {SessionId}.", sessionId);
            return Array.Empty<AgentSseEvent>();
        }
    }

    internal static AgentSseEvent? DeserializeEvent(RedisValue value, JsonSerializerOptions jsonOptions)
    {
        var payload = value.ToString();
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        var envelope = JsonSerializer.Deserialize<AgentRuntimeEventFanoutEnvelope>(payload, jsonOptions);
        return envelope?.Event ?? JsonSerializer.Deserialize<AgentSseEvent>(payload, jsonOptions);
    }

    private AgentSseEvent? DeserializeEvent(RedisValue value) => DeserializeEvent(value, _jsonOptions);

    private RedisKey BuildReplayKey(string scopeKey) => $"{_instanceName}agent_runtime:events:{scopeKey}";

    private RedisKey BuildStreamKey(string scopeKey) => $"{_instanceName}agent_runtime:events:stream:{scopeKey}";

    private static string BuildScopeKey(string userId, string sessionId) =>
        $"{NormalizeScopePart(userId, nameof(userId))}:{NormalizeScopePart(sessionId, nameof(sessionId))}";

    private static string NormalizeScopePart(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{name} is required for runtime event fanout scope.", name);

        return value.Trim();
    }

    private async Task<IReadOnlyList<AgentSseEvent>> ReplayFromStreamAsync(
        string scopeKey,
        string sessionId,
        string? afterEventId,
        int limit,
        CancellationToken ct)
    {
        var entries = await _redis!.GetDatabase()
            .StreamRangeAsync(BuildStreamKey(scopeKey), null, null, null, Order.Ascending, CommandFlags.None)
            .ConfigureAwait(false);
        var events = new List<AgentSseEvent>();
        var cursorSeen = string.IsNullOrWhiteSpace(afterEventId);

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var payload = entry["payload"];
            var evt = DeserializeEvent(payload);
            if (evt == null || !string.Equals(evt.SessionId, sessionId, StringComparison.Ordinal))
                continue;

            if (!cursorSeen)
            {
                if (string.Equals(evt.EventId, afterEventId, StringComparison.Ordinal))
                    cursorSeen = true;
                continue;
            }

            events.Add(evt);
            if (events.Count >= limit)
                break;
        }

        return events;
    }
}

public sealed class RedisAgentRuntimeEventFanoutBridge : BackgroundService
{
    private readonly IEnumerable<IConnectionMultiplexer> _redisConnections;
    private readonly AgentSseEventBus _events;
    private readonly ILogger<RedisAgentRuntimeEventFanoutBridge> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public RedisAgentRuntimeEventFanoutBridge(
        IEnumerable<IConnectionMultiplexer> redisConnections,
        AgentSseEventBus events,
        ILogger<RedisAgentRuntimeEventFanoutBridge> logger)
    {
        _redisConnections = redisConnections;
        _events = events;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunAsync(stoppingToken).ConfigureAwait(false);
    }

    internal async Task RunAsync(CancellationToken stoppingToken)
    {
        var redis = _redisConnections.FirstOrDefault();
        if (redis == null)
            return;

        var subscriber = redis.GetSubscriber();
        try
        {
            await subscriber.SubscribeAsync(
                RedisChannel.Literal(RedisAgentRuntimeEventFanout.ChannelName),
                async (_, value) =>
                {
                    try
                    {
                        var evt = DeserializeEvent(value);
                        if (evt == null || string.IsNullOrWhiteSpace(evt.SessionId))
                            return;

                        await _events.SendAsync(evt.SessionId, evt, stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Redis runtime event fanout bridge failed to deliver event.");
                    }
                }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis runtime event fanout bridge is unavailable; falling back to local SSE only.");
            return;
        }

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            try
            {
                await subscriber.UnsubscribeAsync(RedisChannel.Literal(RedisAgentRuntimeEventFanout.ChannelName))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis runtime event fanout bridge failed to unsubscribe cleanly.");
            }
        }
    }

    private AgentSseEvent? DeserializeEvent(RedisValue value)
    {
        var payload = value.ToString();
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        var envelope = JsonSerializer.Deserialize<AgentRuntimeEventFanoutEnvelope>(payload, _jsonOptions);
        if (envelope?.Event != null)
        {
            return envelope.OriginId == AgentRuntimeEventFanoutInstance.Id
                ? null
                : envelope.Event;
        }

        return JsonSerializer.Deserialize<AgentSseEvent>(payload, _jsonOptions);
    }
}

internal sealed record AgentRuntimeEventFanoutEnvelope(string OriginId, AgentSseEvent Event);
