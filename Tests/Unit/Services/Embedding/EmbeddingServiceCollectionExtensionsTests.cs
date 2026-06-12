using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Services.Embedding;
using Xunit;

namespace Tests.Unit.Services.Embedding;

public class EmbeddingServiceCollectionExtensionsTests
{
    [Fact]
    public void CreateRuntimeStatus_DefaultsToDegradedStub()
    {
        var status = EmbeddingServiceCollectionExtensions.CreateRuntimeStatus(
            BuildConfiguration(),
            new TestHostEnvironment("Development"));

        Assert.Equal("stub", status.Provider);
        Assert.True(status.IsDegraded);
        Assert.True(status.UsesDeterministicStub);
        Assert.Equal("degraded", status.SemanticQuality);
        Assert.Contains("deterministic hash vectors", status.Warning);
    }

    [Fact]
    public void AddNovelAgentEmbedding_ThrowsWhenRealEmbeddingsAreRequiredWithStub()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "stub",
            ["Embedding:RequireRealEmbeddings"] = "true"
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddNovelAgentEmbedding(configuration, new TestHostEnvironment("Production")));

        Assert.Contains("RequireRealEmbeddings=true", ex.Message);
    }

    [Fact]
    public void AddNovelAgentEmbedding_RejectsUnregisteredRealProviderInsteadOfOverclaiming()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "bge-small-zh"
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddNovelAgentEmbedding(configuration, new TestHostEnvironment("Production")));

        Assert.Contains("not available in this build", ex.Message);
    }

    [Fact]
    public void AddNovelAgentEmbedding_RegistersStubAndRuntimeStatusForTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNovelAgentEmbedding(BuildConfiguration(), new TestHostEnvironment("Development"));

        using var provider = services.BuildServiceProvider();
        var status = provider.GetRequiredService<EmbeddingRuntimeStatus>();
        var embedding = provider.GetRequiredService<IMicroEmbeddingService>();

        Assert.True(status.IsDegraded);
        Assert.IsType<StubEmbeddingService>(embedding);
    }

    [Fact]
    public void EmbeddingHealthResponse_ExposesDegradedStubMode()
    {
        var status = EmbeddingServiceCollectionExtensions.CreateRuntimeStatus(
            BuildConfiguration(),
            new TestHostEnvironment("Development"));

        var response = EmbeddingHealthResponse.From(status);

        Assert.Equal("stub", response.Provider);
        Assert.True(response.Degraded);
        Assert.True(response.DeterministicStub);
        Assert.Equal("degraded", response.SemanticQuality);
        Assert.Contains("deterministic hash vectors", response.Warning);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?>? values = null)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
            .Build();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
            ApplicationName = "Tests";
            ContentRootPath = AppContext.BaseDirectory;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; }
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class NullFileProvider : IFileProvider
    {
        public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;
        public IFileInfo GetFileInfo(string subpath) => new NotFoundFileInfo(subpath);
        public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
    }
}
