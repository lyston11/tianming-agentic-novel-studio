using StackExchange.Redis;

namespace TM.Web.NovelAgentWeb.Services.Caching;

public sealed record DistributedLockLease(
    string Key,
    string Token,
    string Owner,
    DateTime AcquiredAt,
    DateTime ExpiresAt);

public interface IDistributedLockService
{
    Task<DistributedLockLease?> TryAcquireAsync(
        string key,
        TimeSpan ttl,
        string owner,
        CancellationToken ct = default);

    Task<DistributedLockLease?> ExtendAsync(
        DistributedLockLease lease,
        TimeSpan ttl,
        CancellationToken ct = default);

    Task ReleaseAsync(DistributedLockLease lease, CancellationToken ct = default);
}

public sealed class RedisDistributedLockService : IDistributedLockService
{
    private static readonly LuaScript ReleaseScript = LuaScript.Prepare(
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end");
    private static readonly LuaScript ExtendScript = LuaScript.Prepare(
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('pexpire', KEYS[1], ARGV[2]) else return 0 end");

    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<RedisDistributedLockService> _logger;
    private readonly string _instanceName;

    public RedisDistributedLockService(
        IConfiguration configuration,
        IEnumerable<IConnectionMultiplexer> redisConnections,
        ILogger<RedisDistributedLockService> logger)
    {
        _redis = redisConnections.FirstOrDefault();
        _logger = logger;
        _instanceName = configuration["Redis:InstanceName"] ?? "NovelAgent:";
    }

    public async Task<DistributedLockLease?> TryAcquireAsync(
        string key,
        TimeSpan ttl,
        string owner,
        CancellationToken ct = default)
    {
        if (_redis == null)
        {
            _logger.LogWarning("Redis distributed lock unavailable for key {Key}.", key);
            return null;
        }

        ct.ThrowIfCancellationRequested();
        var normalizedTtl = ttl <= TimeSpan.Zero ? TimeSpan.FromMinutes(5) : ttl;
        var token = $"{Normalize(owner)}:{Guid.NewGuid():N}";
        var redisKey = BuildRedisKey(key);
        var database = _redis.GetDatabase();
        var acquired = await database
            .StringSetAsync(redisKey, token, normalizedTtl, When.NotExists)
            .ConfigureAwait(false);
        if (!acquired)
            return null;

        var now = DateTime.UtcNow;
        return new DistributedLockLease(
            Key: key,
            Token: token,
            Owner: owner,
            AcquiredAt: now,
            ExpiresAt: now.Add(normalizedTtl));
    }

    public async Task<DistributedLockLease?> ExtendAsync(
        DistributedLockLease lease,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        if (_redis == null)
            return null;

        ct.ThrowIfCancellationRequested();
        var normalizedTtl = ttl <= TimeSpan.Zero ? TimeSpan.FromMinutes(5) : ttl;
        try
        {
            var database = _redis.GetDatabase();
            var result = await database
                .ScriptEvaluateAsync(
                    ExtendScript,
                    new
                    {
                        KEYS = new RedisKey[] { BuildRedisKey(lease.Key) },
                        ARGV = new RedisValue[] { lease.Token, (long)normalizedTtl.TotalMilliseconds }
                    })
                .ConfigureAwait(false);
            var extended = (int)result == 1;
            if (!extended)
                return null;

            return lease with { ExpiresAt = DateTime.UtcNow.Add(normalizedTtl) };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis distributed lock extend failed for key {Key}.", lease.Key);
            return null;
        }
    }

    public async Task ReleaseAsync(DistributedLockLease lease, CancellationToken ct = default)
    {
        if (_redis == null)
            return;

        ct.ThrowIfCancellationRequested();
        try
        {
            var database = _redis.GetDatabase();
            await database
                .ScriptEvaluateAsync(
                    ReleaseScript,
                    new { KEYS = new RedisKey[] { BuildRedisKey(lease.Key) }, ARGV = new RedisValue[] { lease.Token } })
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis distributed lock release failed for key {Key}.", lease.Key);
        }
    }

    private RedisKey BuildRedisKey(string key) => $"{_instanceName}{key}";

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().Replace(' ', '_');
}
