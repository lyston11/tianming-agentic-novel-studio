using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace TM.Web.NovelAgentWeb.Services.Caching;

public class RedisCacheService : IDistributedCacheService
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly TimeSpan _defaultExpiration;
    private readonly JsonSerializerOptions _jsonOptions;

    public RedisCacheService(
        IDistributedCache cache,
        IConfiguration configuration,
        ILogger<RedisCacheService> logger)
    {
        _cache = cache;
        _logger = logger;

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
}
