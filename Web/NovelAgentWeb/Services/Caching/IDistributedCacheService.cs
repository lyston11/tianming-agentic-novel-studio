namespace TM.Web.NovelAgentWeb.Services.Caching;

/// <summary>
/// Distributed cache service interface (Redis-backed).
/// Second tier in the three-tier caching strategy (SQLite -> Redis -> IMemoryCache).
/// </summary>
public interface IDistributedCacheService
{
    /// <summary>
    /// Get cached value by key.
    /// </summary>
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class;

    /// <summary>
    /// Set cached value with TTL.
    /// </summary>
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken ct = default) where T : class;

    /// <summary>
    /// Remove cached value by key.
    /// </summary>
    Task RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Check if key exists in cache.
    /// </summary>
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
}
