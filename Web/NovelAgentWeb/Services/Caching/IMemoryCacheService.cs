namespace TM.Web.NovelAgentWeb.Services.Caching;

/// <summary>
/// Service interface for memory caching with typed methods.
/// Wrapper around IMemoryCache to provide better testability and consistent patterns.
/// </summary>
public interface IMemoryCacheService
{
    /// <summary>
    /// Get a cached value or execute factory function to populate cache.
    /// </summary>
    /// <typeparam name="T">Type of cached value</typeparam>
    /// <param name="key">Cache key</param>
    /// <param name="factory">Factory function to create value if not cached</param>
    /// <param name="expiration">Cache expiration time</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Cached or newly created value</returns>
    Task<T?> GetOrSetAsync<T>(
        string key,
        Func<Task<T>> factory,
        TimeSpan expiration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a cached value without populating if missing.
    /// </summary>
    /// <typeparam name="T">Type of cached value</typeparam>
    /// <param name="key">Cache key</param>
    /// <returns>Cached value or default</returns>
    T? Get<T>(string key);

    /// <summary>
    /// Set a value in cache with expiration.
    /// </summary>
    /// <typeparam name="T">Type of value to cache</typeparam>
    /// <param name="key">Cache key</param>
    /// <param name="value">Value to cache</param>
    /// <param name="expiration">Cache expiration time</param>
    void Set<T>(string key, T value, TimeSpan expiration);

    /// <summary>
    /// Remove a value from cache.
    /// </summary>
    /// <param name="key">Cache key to remove</param>
    void Remove(string key);

    /// <summary>
    /// Remove all cached values matching a key prefix.
    /// Useful for cache invalidation of related items.
    /// </summary>
    /// <param name="keyPrefix">Cache key prefix</param>
    void RemoveByPrefix(string keyPrefix);
}
