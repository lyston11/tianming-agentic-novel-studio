using Microsoft.Extensions.Configuration;
using Xunit;

namespace Tests.Unit;

public class ProgramConfigurationTests
{
    [Fact]
    public void StandardConfiguration_UsesRequiredRuntimePortsAndRedis()
    {
        var repoRoot = Path.GetFullPath("../../../../../", AppContext.BaseDirectory);

        var config = new ConfigurationBuilder()
            .SetBasePath(repoRoot)
            .AddJsonFile("Web/NovelAgentWeb/appsettings.json", optional: false)
            .Build();

        Assert.Equal("true", config["Redis:Enabled"], ignoreCase: true);
        Assert.Equal("localhost:6379", config["Redis:ConnectionString"]);
        Assert.Equal("http://localhost:6333", config["Qdrant:BaseUrl"]);
        Assert.Equal("6334", config["Qdrant:Port"]);
    }
}
