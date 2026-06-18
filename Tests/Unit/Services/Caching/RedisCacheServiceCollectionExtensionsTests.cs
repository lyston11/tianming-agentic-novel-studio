using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using TM.Web.NovelAgentWeb.Services.Caching;
using Xunit;

namespace Tests.Unit.Services.Caching;

public class RedisCacheServiceCollectionExtensionsTests
{
    [Fact]
    public void AddNovelAgentDistributedCache_WithStandardConfig_RegistersDistributedCache()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Redis:Enabled"] = "true",
            ["Redis:ConnectionString"] = "localhost:6379",
            ["Redis:InstanceName"] = "NovelAgent:"
        });
        var services = new ServiceCollection();

        services.AddNovelAgentDistributedCache(configuration);

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IDistributedCache>());
    }

    [Fact]
    public void AddNovelAgentDistributedCache_NormalizesRedisConnectionForReconnects()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Redis:Enabled"] = "true",
            ["Redis:ConnectionString"] = "localhost:6379",
            ["Redis:InstanceName"] = "NovelAgent:"
        });
        var services = new ServiceCollection();

        services.AddNovelAgentDistributedCache(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RedisCacheOptions>>().Value;
        Assert.Contains("abortConnect=false", options.Configuration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddNovelAgentDistributedCache_WithExplicitFallback_RegistersMemoryDistributedCache()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Redis:Enabled"] = "false",
            ["Redis:AllowInMemoryFallback"] = "true"
        });
        var services = new ServiceCollection();

        services.AddNovelAgentDistributedCache(configuration);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<MemoryDistributedCache>(provider.GetRequiredService<IDistributedCache>());
    }

    [Fact]
    public void AddNovelAgentDistributedCache_WithoutRedisOrFallback_Throws()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Redis:Enabled"] = "false"
        });
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddNovelAgentDistributedCache(configuration));

        Assert.Contains("Redis is required", exception.Message);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
