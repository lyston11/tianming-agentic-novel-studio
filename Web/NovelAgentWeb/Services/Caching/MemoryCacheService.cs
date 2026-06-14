using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace TM.Web.NovelAgentWeb.Services.Caching;

/// <summary>
/// Implementation of memory caching service using IMemoryCache.
/// Provides thread-safe caching operations with expiration and invalidation support.
/// </summary>
public class MemoryCacheService : IMemoryCacheService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<MemoryCacheService> _logger;

    // Track cache keys by prefix for efficient bulk invalidation
    private readonly ConcurrentDictionary<string, HashSet<string>> _keysByPrefix = new();
    private readonly object _prefixLock = new();

    public MemoryCacheService(
        IMemoryCache cache,
        ILogger<MemoryCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<T?> GetOrSetAsync<T>(
        string key,
        Func<Task<T>> factory,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
    {
        // Try to get from cache first
        if (_cache.TryGetValue(key, out T? cached))
        {
            _logger.LogDebug("Cache hit for key: {CacheKey}", key);
            return cached;
        }

        _logger.LogDebug("Cache miss for key: {CacheKey}", key);

        // Execute factory to create value
        var value = await factory();

        // Store in cache with expiration
        if (value != null)
        {
            Set(key, value, expiration);
        }

        return value;
    }

    public T? Get<T>(string key)
    {
        if (_cache.TryGetValue(key, out T? value))
        {
            _logger.LogDebug("Cache hit for key: {CacheKey}", key);
            return value;
        }

        _logger.LogDebug("Cache miss for key: {CacheKey}", key);
        return default;
    }

    public void Set<T>(string key, T value, TimeSpan expiration)
    {
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration
        };

        // Register callback to remove key from prefix tracking on eviction
        options.RegisterPostEvictionCallback((k, v, reason, state) =>
        {
            RemoveKeyFromPrefixTracking(k.ToString() ?? string.Empty);
        });

        _cache.Set(key, value, options);

        // Track key by prefix
        TrackKeyPrefix(key);

        _logger.LogDebug("Cached value for key: {CacheKey} with expiration: {Expiration}", key, expiration);
    }

    public void Remove(string key)
    {
        _cache.Remove(key);
        RemoveKeyFromPrefixTracking(key);
        _logger.LogDebug("Removed cache entry for key: {CacheKey}", key);
    }

    public void RemoveByPrefix(string keyPrefix)
    {
        lock (_prefixLock)
        {
            var keys = _keysByPrefix.Keys
                .Where(key => IsPrefixMatch(key, keyPrefix))
                .ToList();

            foreach (var key in keys)
            {
                _cache.Remove(key);
                _keysByPrefix.TryRemove(key, out _);
            }

            _logger.LogDebug("Removed {Count} cache entries with prefix: {KeyPrefix}", keys.Count, keyPrefix);
        }
    }

    private static bool IsPrefixMatch(string key, string keyPrefix)
    {
        if (keyPrefix.EndsWith(':'))
        {
            return key.StartsWith(keyPrefix, StringComparison.Ordinal);
        }

        return key.Length == keyPrefix.Length
            ? string.Equals(key, keyPrefix, StringComparison.Ordinal)
            : key.StartsWith(keyPrefix, StringComparison.Ordinal) && key[keyPrefix.Length] == ':';
    }

    private void TrackKeyPrefix(string key)
    {
        lock (_prefixLock)
        {
            if (!_keysByPrefix.TryGetValue(key, out var keys))
            {
                keys = new HashSet<string>();
                _keysByPrefix[key] = keys;
            }

            keys.Add(key);
        }
    }

    private void RemoveKeyFromPrefixTracking(string key)
    {
        lock (_prefixLock)
        {
            if (_keysByPrefix.TryGetValue(key, out var keys))
            {
                keys.Remove(key);

                if (keys.Count == 0)
                {
                    _keysByPrefix.TryRemove(key, out _);
                }
            }
        }
    }
}
