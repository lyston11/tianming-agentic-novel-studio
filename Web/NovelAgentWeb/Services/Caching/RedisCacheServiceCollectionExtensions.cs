using Microsoft.Extensions.Caching.StackExchangeRedis;
using StackExchange.Redis;

namespace TM.Web.NovelAgentWeb.Services.Caching;

public static class RedisCacheServiceCollectionExtensions
{
    public static IServiceCollection AddNovelAgentDistributedCache(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisEnabled = configuration.GetValue("Redis:Enabled", true);
        var redisAllowFallback = configuration.GetValue("Redis:AllowInMemoryFallback", false);
        var redisConnectionString = configuration["Redis:ConnectionString"];
        var redisInstanceName = configuration["Redis:InstanceName"];

        if (redisEnabled && !string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
                options.InstanceName = redisInstanceName ?? "NovelAgent:";
            });

            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
        }
        else if (redisAllowFallback)
        {
            services.AddDistributedMemoryCache();
        }
        else
        {
            throw new InvalidOperationException("Redis is required. Set Redis:Enabled=true and Redis:ConnectionString, or set Redis:AllowInMemoryFallback=true only for tests.");
        }

        return services;
    }
}
