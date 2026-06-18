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
        var resilientConnectionString = NormalizeConnectionString(redisConnectionString);

        if (redisEnabled && !string.IsNullOrWhiteSpace(resilientConnectionString))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = resilientConnectionString;
                options.InstanceName = redisInstanceName ?? "NovelAgent:";
            });

            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(resilientConnectionString));
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

    internal static string? NormalizeConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return connectionString;

        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;
        return options.ToString();
    }
}
