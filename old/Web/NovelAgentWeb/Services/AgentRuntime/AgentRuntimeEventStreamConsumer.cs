using System.Text.Json;
using StackExchange.Redis;
using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Services.AgentRuntime;

public interface IAgentRuntimeEventStreamConsumer
{
    Task EnsureConsumerGroupAsync(string userId, string sessionId, string groupName, CancellationToken ct = default);

    Task<IReadOnlyList<AgentRuntimeStreamEvent>> ReadGroupAsync(
        string userId,
        string sessionId,
        string groupName,
        string consumerName,
        int count = 20,
        CancellationToken ct = default);

    Task<IReadOnlyList<AgentRuntimeStreamEvent>> ClaimPendingAsync(
        string userId,
        string sessionId,
        string groupName,
        string consumerName,
        TimeSpan minIdleTime,
        int count = 20,
        CancellationToken ct = default);

    Task<bool> AcknowledgeAsync(
        string userId,
        string sessionId,
        string groupName,
        string streamId,
        CancellationToken ct = default);
}

public sealed record AgentRuntimeStreamEvent(
    string StreamId,
    AgentSseEvent Event,
    int DeliveryCount = 1,
    bool IsPendingRecovery = false);

public sealed class RedisAgentRuntimeEventStreamConsumer : IAgentRuntimeEventStreamConsumer
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly string _instanceName;
    private readonly ILogger<RedisAgentRuntimeEventStreamConsumer> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public RedisAgentRuntimeEventStreamConsumer(
        IEnumerable<IConnectionMultiplexer> redisConnections,
        IConfiguration configuration,
        ILogger<RedisAgentRuntimeEventStreamConsumer> logger)
    {
        _redis = redisConnections.FirstOrDefault();
        _instanceName = configuration["Redis:InstanceName"] ?? "NovelAgent:";
        _logger = logger;
    }

    public async Task EnsureConsumerGroupAsync(string userId, string sessionId, string groupName, CancellationToken ct = default)
    {
        if (_redis == null)
            return;

        ct.ThrowIfCancellationRequested();
        try
        {
            await _redis.GetDatabase()
                .StreamCreateConsumerGroupAsync(
                    BuildStreamKey(userId, sessionId),
                    groupName,
                    "0-0",
                    createStream: true,
                    flags: CommandFlags.None)
                .ConfigureAwait(false);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP", StringComparison.OrdinalIgnoreCase))
        {
            // The group already exists. This is an idempotent ensure operation.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis runtime event stream consumer group ensure failed for session {SessionId}.", sessionId);
        }
    }

    public async Task<IReadOnlyList<AgentRuntimeStreamEvent>> ReadGroupAsync(
        string userId,
        string sessionId,
        string groupName,
        string consumerName,
        int count = 20,
        CancellationToken ct = default)
    {
        if (_redis == null)
            return Array.Empty<AgentRuntimeStreamEvent>();

        ct.ThrowIfCancellationRequested();
        try
        {
            var entries = await _redis.GetDatabase()
                .StreamReadGroupAsync(
                    BuildStreamKey(userId, sessionId),
                    groupName,
                    consumerName,
                    ">",
                    Math.Clamp(count, 1, 200),
                    CommandFlags.None)
                .ConfigureAwait(false);

            var events = new List<AgentRuntimeStreamEvent>();
            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                var evt = RedisAgentRuntimeEventFanout.DeserializeEvent(entry["payload"], _jsonOptions);
                if (evt == null || !string.Equals(evt.SessionId, sessionId, StringComparison.Ordinal))
                    continue;

                events.Add(new AgentRuntimeStreamEvent(entry.Id.ToString(), evt));
            }

            return events;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis runtime event stream read group failed for session {SessionId}.", sessionId);
            return Array.Empty<AgentRuntimeStreamEvent>();
        }
    }

    public async Task<IReadOnlyList<AgentRuntimeStreamEvent>> ClaimPendingAsync(
        string userId,
        string sessionId,
        string groupName,
        string consumerName,
        TimeSpan minIdleTime,
        int count = 20,
        CancellationToken ct = default)
    {
        if (_redis == null)
            return Array.Empty<AgentRuntimeStreamEvent>();

        ct.ThrowIfCancellationRequested();
        try
        {
            var streamKey = BuildStreamKey(userId, sessionId);
            var requestedCount = Math.Clamp(count, 1, 200);
            var pending = await _redis.GetDatabase()
                .StreamPendingMessagesAsync(
                    streamKey,
                    groupName,
                    requestedCount,
                    RedisValue.Null,
                    minId: null,
                    maxId: null,
                    CommandFlags.None)
                .ConfigureAwait(false);
            var minIdleMs = Math.Max(1, (long)minIdleTime.TotalMilliseconds);
            var ids = pending
                .Where(message => message.IdleTimeInMilliseconds >= minIdleMs)
                .Select(message => message.MessageId)
                .ToArray();
            if (ids.Length == 0)
                return Array.Empty<AgentRuntimeStreamEvent>();

            var entries = await _redis.GetDatabase()
                .StreamClaimAsync(
                    streamKey,
                    groupName,
                    consumerName,
                    minIdleMs,
                    ids,
                    CommandFlags.None)
                .ConfigureAwait(false);

            var deliveryCounts = pending.ToDictionary(
                message => message.MessageId.ToString(),
                message => Math.Max(1, message.DeliveryCount + 1),
                StringComparer.Ordinal);
            return DeserializeEntries(sessionId, entries, ct, deliveryCounts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis runtime event stream pending claim failed for session {SessionId}.", sessionId);
            return Array.Empty<AgentRuntimeStreamEvent>();
        }
    }

    public async Task<bool> AcknowledgeAsync(
        string userId,
        string sessionId,
        string groupName,
        string streamId,
        CancellationToken ct = default)
    {
        if (_redis == null)
            return false;

        ct.ThrowIfCancellationRequested();
        try
        {
            var acknowledged = await _redis.GetDatabase()
                .StreamAcknowledgeAsync(BuildStreamKey(userId, sessionId), groupName, streamId, CommandFlags.None)
                .ConfigureAwait(false);
            return acknowledged > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis runtime event stream acknowledge failed for session {SessionId}.", sessionId);
            return false;
        }
    }

    private static IReadOnlyList<AgentRuntimeStreamEvent> DeserializeEntries(
        string sessionId,
        StreamEntry[] entries,
        CancellationToken ct,
        IReadOnlyDictionary<string, int>? deliveryCounts = null)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var events = new List<AgentRuntimeStreamEvent>();
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var evt = RedisAgentRuntimeEventFanout.DeserializeEvent(entry["payload"], options);
            if (evt == null || !string.Equals(evt.SessionId, sessionId, StringComparison.Ordinal))
                continue;

            var streamId = entry.Id.ToString();
            events.Add(new AgentRuntimeStreamEvent(
                streamId,
                evt,
                deliveryCounts?.GetValueOrDefault(streamId) ?? 1,
                deliveryCounts != null));
        }

        return events;
    }

    private RedisKey BuildStreamKey(string userId, string sessionId) =>
        $"{_instanceName}agent_runtime:events:stream:{NormalizeScopePart(userId, nameof(userId))}:{NormalizeScopePart(sessionId, nameof(sessionId))}";

    private static string NormalizeScopePart(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{name} is required for runtime event stream scope.", name);

        return value.Trim();
    }
}
