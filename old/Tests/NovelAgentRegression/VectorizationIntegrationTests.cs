using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Testcontainers.Qdrant;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Vectorization;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using Xunit;

namespace TM.Tests.NovelAgentRegression;

/// <summary>
/// Integration tests for the vectorization pipeline.
/// Tests the end-to-end flow from material creation to vector storage in Qdrant.
/// </summary>
public class VectorizationIntegrationTests : IAsyncLifetime
{
    private NovelAgentDbContext _db = null!;
    private QdrantClient _qdrantClient = null!;
    private OutboxMaterialVectorIndexingService _service = null!;
    private QdrantContainer? _qdrantContainer;
    private ILoggerFactory _loggerFactory = null!;
    private string _testUserId = null!;
    private string _testProjectId = null!;

    public async Task InitializeAsync()
    {
        // Setup in-memory database
        var dbOptions = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(databaseName: $"VectorizationIntegrationTest_{Guid.NewGuid()}")
            .Options;

        _db = new NovelAgentDbContext(dbOptions);
        await _db.Database.EnsureCreatedAsync();

        // Start Qdrant container
        _qdrantContainer = new QdrantBuilder("qdrant/qdrant:v1.18.1")
            .Build();

        await _qdrantContainer.StartAsync();

        // Create Qdrant client
        var host = _qdrantContainer.Hostname;
        var port = _qdrantContainer.GetMappedPublicPort(6334); // gRPC port
        _qdrantClient = new QdrantClient(host, port);

        // Create logger factory
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var mockEmbedding = new FixedEmbeddingService();

        // Create material chunker
        var chunker = new MaterialChunker();

        var configValues = new Dictionary<string, string?>
        {
            ["Qdrant:Host"] = host,
            ["Qdrant:Port"] = port.ToString(),
            ["Qdrant:VectorDimension"] = "512",
            ["Qdrant:BatchSize"] = "100"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        // Create collection manager
        var collectionManager = new QdrantCollectionManager(
            _qdrantClient,
            configuration,
            _loggerFactory.CreateLogger<QdrantCollectionManager>()
        );

        var vectorStore = new QdrantVectorStore(
            _qdrantClient,
            configuration,
            _loggerFactory.CreateLogger<QdrantVectorStore>());

        // Create outbox material indexing executor
        _service = new OutboxMaterialVectorIndexingService(
            _db,
            vectorStore,
            mockEmbedding,
            chunker,
            collectionManager,
            new ContentDocumentService(_db),
            _loggerFactory.CreateLogger<OutboxMaterialVectorIndexingService>()
        );

        // Create test user and project
        _testUserId = Guid.NewGuid().ToString();
        _testProjectId = Guid.NewGuid().ToString();

        var testUser = new User
        {
            Id = _testUserId,
            Username = "test_user",
            Email = "test@example.com",
            PasswordHash = "hash",
            Role = "author",
            IsActive = true
        };

        var testProject = new NovelProject
        {
            Id = _testProjectId,
            UserId = _testUserId,
            Title = "Test Project",
            Genre = "Fantasy",
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(testUser);
        _db.NovelProjects.Add(testProject);
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (_qdrantContainer != null)
        {
            await _qdrantContainer.StopAsync();
            await _qdrantContainer.DisposeAsync();
        }

        await _db.DisposeAsync();
        _loggerFactory?.Dispose();
    }

    [Fact]
    public async Task IndexMaterial_CreatesChunksInQdrant()
    {
        // Arrange
        var material = await CreateTestMaterialAsync(_testUserId, _testProjectId, "Test material content with enough text to create chunks. " + string.Join(" ", Enumerable.Range(1, 1000).Select(i => $"word{i}")));

        // Act
        await _service.IndexMaterialAsync(material.Id, _testUserId);

        // Assert
        var updatedMaterial = await _db.Materials.FindAsync(material.Id);
        Assert.NotNull(updatedMaterial);
        Assert.True(updatedMaterial.VectorChunkCount > 0, "Material should have vector chunks after vectorization");

        // Verify vectors exist in Qdrant
        var collectionName = $"novel_agent_{_testUserId}";
        var collectionInfo = await _qdrantClient.GetCollectionInfoAsync(collectionName);
        Assert.NotNull(collectionInfo);
        Assert.True(collectionInfo.PointsCount > 0, "Qdrant collection should contain vectors");
    }

    [Fact]
    public async Task IndexMaterial_WithContent_Success()
    {
        // Arrange
        var content = "This is a test material with inline content. " + string.Join(" ", Enumerable.Range(1, 500).Select(i => $"word{i}"));
        var material = await CreateTestMaterialAsync(_testUserId, _testProjectId, content);

        // Act
        await _service.IndexMaterialAsync(material.Id, _testUserId);

        // Assert
        var updatedMaterial = await _db.Materials.FindAsync(material.Id);
        Assert.NotNull(updatedMaterial);
        Assert.True(updatedMaterial.VectorChunkCount > 0);
    }

    [Fact]
    public async Task IndexMaterial_UpdatesVectorChunkCount()
    {
        // Arrange
        var material = await CreateTestMaterialAsync(_testUserId, _testProjectId, "Content with 100 words. " + string.Join(" ", Enumerable.Range(1, 100).Select(i => $"word{i}")));

        Assert.Equal(0, material.VectorChunkCount);

        // Act
        await _service.IndexMaterialAsync(material.Id, _testUserId);

        // Assert
        var updatedMaterial = await _db.Materials.FindAsync(material.Id);
        Assert.NotNull(updatedMaterial);
        Assert.True(updatedMaterial.VectorChunkCount > 0);
    }

    [Fact]
    public async Task IndexMaterial_NonExistentMaterial_ThrowsKeyNotFoundException()
    {
        // Arrange
        var nonExistentMaterialId = Guid.NewGuid().ToString();

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
        {
            await _service.IndexMaterialAsync(nonExistentMaterialId, _testUserId);
        });
    }

    [Fact]
    public async Task IndexMaterial_WrongUserId_ThrowsKeyNotFoundException()
    {
        // Arrange
        var material = await CreateTestMaterialAsync(_testUserId, _testProjectId, "Test content");

        var wrongUserId = Guid.NewGuid().ToString();

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
        {
            await _service.IndexMaterialAsync(material.Id, wrongUserId);
        });
    }

    [Fact]
    public async Task IndexMaterial_ReplacesOldVectors()
    {
        // Arrange
        var material = await CreateTestMaterialAsync(_testUserId, _testProjectId, "Initial content. " + string.Join(" ", Enumerable.Range(1, 500).Select(i => $"word{i}")));

        // Act - First vectorization
        await _service.IndexMaterialAsync(material.Id, _testUserId);
        var firstChunkCount = (await _db.Materials.FindAsync(material.Id))!.VectorChunkCount;

        // Act - Second vectorization (should replace)
        await _service.IndexMaterialAsync(material.Id, _testUserId);
        var secondChunkCount = (await _db.Materials.FindAsync(material.Id))!.VectorChunkCount;

        // Assert
        Assert.Equal(firstChunkCount, secondChunkCount);

        // Verify Qdrant doesn't have duplicates
        var collectionName = $"novel_agent_{_testUserId}";
        var collectionInfo = await _qdrantClient.GetCollectionInfoAsync(collectionName);
        Assert.NotNull(collectionInfo);
        Assert.Equal((ulong)secondChunkCount, collectionInfo.PointsCount);
    }

    [Fact]
    public async Task IndexMaterial_CreatesCorrectPayloadInQdrant()
    {
        // Arrange
        var material = await CreateTestMaterialAsync(_testUserId, _testProjectId, "Payload test content. " + string.Join(" ", Enumerable.Range(1, 300).Select(i => $"word{i}")));
        material.Category = "research";
        await _db.SaveChangesAsync();

        // Act
        await _service.IndexMaterialAsync(material.Id, _testUserId);

        // Assert - Query Qdrant to verify payload
        var collectionName = $"novel_agent_{_testUserId}";

        // Wait a bit for Qdrant to index
        await Task.Delay(500);

        var scrollResponse = await _qdrantClient.ScrollAsync(
            collectionName,
            limit: 1
        );

        Assert.NotNull(scrollResponse);
        Assert.NotEmpty(scrollResponse.Result);

        var point = scrollResponse.Result.First();
        Assert.NotNull(point.Payload);
        Assert.Equal(_testUserId, point.Payload["user_id"].StringValue);
        Assert.Equal(_testProjectId, point.Payload["project_id"].StringValue);
        Assert.Equal("material", point.Payload["source_type"].StringValue);
        Assert.Equal(material.Id, point.Payload["source_id"].StringValue);
        Assert.Contains("Payload test content", point.Payload["content"].StringValue);
    }

    private async Task<Material> CreateTestMaterialAsync(string userId, string projectId, string content)
    {
        var material = new Material
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            ProjectId = projectId,
            Title = "Test Material",
            Category = "general",
            ContentType = "text/plain",
            CreatedAt = DateTime.UtcNow
        };

        _db.Materials.Add(material);
        await _db.SaveChangesAsync();
        await new ContentDocumentService(_db).SaveOrReplaceTextAsync(
            userId,
            projectId,
            "material",
            material.Id,
            "material_raw",
            material.Title,
            content);
        return material;
    }

    private sealed class FixedEmbeddingService : IMicroEmbeddingService
    {
        public int Dimension => 512;

        public Task<float[]> EncodeAsync(string text, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default)
        {
            var vector = new float[Dimension];
            var seed = string.IsNullOrWhiteSpace(text) ? 1 : Math.Abs(text.GetHashCode());
            vector[seed % Dimension] = 1f;
            return Task.FromResult(vector);
        }

        public async Task<float[][]> EncodeBatchAsync(IReadOnlyList<string> texts, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default)
        {
            var vectors = new float[texts.Count][];
            for (var i = 0; i < texts.Count; i++)
                vectors[i] = await EncodeAsync(texts[i], mode, ct).ConfigureAwait(false);
            return vectors;
        }

        public bool IsModelReady() => true;

        public void ReleaseSession()
        {
        }
    }
}
