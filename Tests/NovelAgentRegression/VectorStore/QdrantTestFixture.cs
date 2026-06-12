using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Testcontainers.Qdrant;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using Xunit;

namespace TM.Tests.NovelAgentRegression.VectorStore;

/// <summary>
/// xUnit test fixture for managing Qdrant container lifecycle.
/// Starts Qdrant container before tests and stops it after all tests complete.
/// </summary>
public class QdrantTestFixture : IAsyncLifetime
{
    private QdrantContainer? _container;
    private ILoggerFactory? _loggerFactory;

    /// <summary>
    /// Qdrant vector store instance for testing.
    /// </summary>
    public ProjectVectorStoreAdapter VectorStore { get; private set; } = null!;

    /// <summary>
    /// Connection string for the Qdrant container.
    /// </summary>
    public string ConnectionString { get; private set; } = null!;

    /// <summary>
    /// Initialize Qdrant container and vector store.
    /// </summary>
    public async Task InitializeAsync()
    {
        // Build Qdrant container with v1.8.0 image
        _container = new QdrantBuilder()
            .WithImage("qdrant/qdrant:v1.8.0")
            .Build();

        // Start container
        await _container.StartAsync();

        // Get connection details
        var host = _container.Hostname;
        var port = _container.GetMappedPublicPort(6334); // gRPC port
        ConnectionString = $"{host}:{port}";

        // Create configuration for QdrantVectorStore
        var configValues = new Dictionary<string, string?>
        {
            ["Qdrant:Host"] = host,
            ["Qdrant:Port"] = port.ToString(),
            ["Qdrant:VectorDimension"] = "512", // bge-small-zh-v1.5
            ["Qdrant:BatchSize"] = "100"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        // Create logger
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var logger = _loggerFactory.CreateLogger<QdrantVectorStore>();
        var client = new QdrantClient(host, port);

        // Initialize vector store
        VectorStore = new ProjectVectorStoreAdapter(new QdrantVectorStore(client, configuration, logger));
    }

    /// <summary>
    /// Stop and dispose Qdrant container.
    /// </summary>
    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }

        _loggerFactory?.Dispose();
    }
}
