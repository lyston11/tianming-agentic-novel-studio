using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace TM.Web.NovelAgentWeb.Services.Caching;

public class RedisCacheService : IDistributedCacheService
{
    internal const int PrefixDeleteBatchSize = 500;

    private readonly IDistributedCache _cache;
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly TimeSpan _defaultExpiration;
    private readonly string _instanceName;
    private readonly JsonSerializerOptions _jsonOptions;

    public RedisCacheService(
        IDistributedCache cache,
        IConfiguration configuration,
        IEnumerable<IConnectionMultiplexer> redisConnections,
        ILogger<RedisCacheService> logger)
    {
        _cache = cache;
        _redis = redisConnections.FirstOrDefault();
        _logger = logger;
        _instanceName = configuration["Redis:InstanceName"] ?? "NovelAgent:";

        if (!TimeSpan.TryParse(configuration["Redis:DefaultExpiration"], out _defaultExpiration))
        {
            _defaultExpiration = TimeSpan.FromMinutes(10);
        }

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        try
        {
            var json = await _cache.GetStringAsync(key, ct);
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Distributed cache GET failed for key {Key}, treating as cache miss", key);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken ct = default) where T : class
    {
        try
        {
            var json = JsonSerializer.Serialize(value, _jsonOptions);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration ?? _defaultExpiration
            };

            await _cache.SetStringAsync(key, json, options, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Distributed cache SET failed for key {Key}, continuing without cache", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _cache.RemoveAsync(key, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Distributed cache REMOVE failed for key {Key}", key);
        }
    }

    public async Task RemoveByPrefixAsync(string keyPrefix, CancellationToken ct = default)
    {
        try
        {
            if (_redis == null)
            {
                throw new NotSupportedException("Distributed prefix removal requires a Redis connection multiplexer.");
            }

            var redisPrefix = $"{_instanceName}{keyPrefix}";
            var database = _redis.GetDatabase();
            var removed = 0L;

            foreach (var endpoint in _redis.GetEndPoints())
            {
                ct.ThrowIfCancellationRequested();

                var server = _redis.GetServer(endpoint);
                if (!server.IsConnected || server.IsReplica)
                {
                    continue;
                }

                removed += await RemoveKeysAsync(database, server, redisPrefix, PrefixDeleteBatchSize, ct);
            }

            _logger.LogDebug("Distributed cache prefix remove deleted {Count} keys for prefix {KeyPrefix}", removed, keyPrefix);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NotSupportedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Distributed cache prefix REMOVE failed for prefix {KeyPrefix}", keyPrefix);
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var value = await _cache.GetAsync(key, ct);
            return value != null && value.Length > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Distributed cache EXISTS check failed for key {Key}, returning false", key);
            return false;
        }
    }

    private static async Task<long> RemoveKeysAsync(IDatabase database, IServer server, string redisPrefix, int batchSize, CancellationToken ct)
    {
        var keys = server.Keys(pattern: $"{redisPrefix}:*");
        if (await database.KeyExistsAsync(redisPrefix))
        {
            keys = keys.Prepend(redisPrefix);
        }

        return await DeletePrefixMatchesInBatchesAsync(
            keys,
            redisPrefix,
            batchSize,
            batch => database.KeyDeleteAsync(batch.ToArray()),
            ct);
    }

    internal static async Task<long> DeletePrefixMatchesInBatchesAsync(
        IEnumerable<RedisKey> keys,
        string redisPrefix,
        int batchSize,
        Func<IReadOnlyList<RedisKey>, Task<long>> deleteBatchAsync,
        CancellationToken ct = default)
    {
        var batch = new List<RedisKey>(batchSize);
        var removed = 0L;

        foreach (var key in keys)
        {
            ct.ThrowIfCancellationRequested();
            if (!IsPrefixMatch(key.ToString(), redisPrefix))
            {
                continue;
            }

            batch.Add(key);
            if (batch.Count == batchSize)
            {
                removed += await deleteBatchAsync(batch);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            removed += await deleteBatchAsync(batch);
        }

        return removed;
    }

    internal static bool IsPrefixMatch(string key, string keyPrefix)
    {
        if (keyPrefix.EndsWith(':'))
        {
            return key.StartsWith(keyPrefix, StringComparison.Ordinal);
        }

        return key.Length == keyPrefix.Length
            ? string.Equals(key, keyPrefix, StringComparison.Ordinal)
            : key.StartsWith(keyPrefix, StringComparison.Ordinal) && key[keyPrefix.Length] == ':';
    }
}
