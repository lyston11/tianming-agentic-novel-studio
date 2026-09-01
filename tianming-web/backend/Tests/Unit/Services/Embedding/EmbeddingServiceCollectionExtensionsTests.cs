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
    public void CreateRuntimeStatus_DefaultsToRealBgeSmallZh()
    {
        var status = EmbeddingServiceCollectionExtensions.CreateRuntimeStatus(
            BuildConfiguration(),
            new TestHostEnvironment("Development"));

        Assert.Equal("bge-small-zh", status.Provider);
        Assert.Equal("bge-small-zh-v1.5", status.Model);
        Assert.False(status.IsDegraded);
        Assert.True(status.RealEmbeddingsRequired);
        Assert.Equal("model", status.SemanticQuality);
        Assert.Equal(string.Empty, status.Warning);
    }

    [Fact]
    public void AddNovelAgentEmbedding_RejectsStubProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "stub"
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddNovelAgentEmbedding(configuration, new TestHostEnvironment("Development")));

        Assert.Contains("deterministic stub embeddings are not available", ex.Message);
    }

    [Fact]
    public void AddNovelAgentEmbedding_RejectsUnregisteredRealProviderInsteadOfOverclaiming()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "other-real-provider"
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddNovelAgentEmbedding(configuration, new TestHostEnvironment("Production")));

        Assert.Contains("not available in this build", ex.Message);
    }

    [Fact]
    public void AddNovelAgentEmbedding_RegistersBgeEmbeddingAndRuntimeStatus()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNovelAgentEmbedding(BuildConfiguration(), new TestHostEnvironment("Development"));

        using var provider = services.BuildServiceProvider();
        var status = provider.GetRequiredService<EmbeddingRuntimeStatus>();
        var embedding = provider.GetRequiredService<IMicroEmbeddingService>();

        Assert.False(status.IsDegraded);
        Assert.Equal("BgeSmallZhEmbeddingService", embedding.GetType().Name);
        Assert.True(embedding.IsModelReady());
    }

    [Fact]
    public async Task RegisteredBgeEmbedding_EncodesNonZeroChineseVector()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNovelAgentEmbedding(BuildConfiguration(), new TestHostEnvironment("Development"));

        using var provider = services.BuildServiceProvider();
        var embedding = provider.GetRequiredService<IMicroEmbeddingService>();

        var vector = await embedding.EncodeAsync("银蓝邮徽只能辨认旧邮路，不能攻击。", EmbeddingMode.Passage);

        Assert.Equal(512, vector.Length);
        Assert.Contains(vector, value => Math.Abs(value) > 0.000001f);
    }

    [Fact]
    public void EmbeddingHealthResponse_ExposesRealBgeMode()
    {
        var status = EmbeddingServiceCollectionExtensions.CreateRuntimeStatus(
            BuildConfiguration(),
            new TestHostEnvironment("Development"));

        var response = EmbeddingHealthResponse.From(status);

        Assert.Equal("bge-small-zh", response.Provider);
        Assert.False(response.Degraded);
        Assert.Equal("model", response.SemanticQuality);
        Assert.Equal(string.Empty, response.Warning);
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
