using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Testcontainers.Qdrant;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
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
    private MaterialVectorizationService _service = null!;
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
        _qdrantContainer = new QdrantBuilder()
            .WithImage("qdrant/qdrant:v1.8.0")
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

        // Create stub embedding service
        var mockEmbedding = new TM.Web.NovelAgentWeb.Services.Embedding.StubEmbeddingService(
            _loggerFactory.CreateLogger<TM.Web.NovelAgentWeb.Services.Embedding.StubEmbeddingService>()
        );

        // Create material chunker
        var chunker = new MaterialChunker();

        // Create collection manager
        var collectionManager = new QdrantCollectionManager(
            _qdrantClient,
            _loggerFactory.CreateLogger<QdrantCollectionManager>()
        );

        // Create vectorization service
        _service = new MaterialVectorizationService(
            _db,
            _qdrantClient,
            mockEmbedding,
            chunker,
            collectionManager,
            _loggerFactory.CreateLogger<MaterialVectorizationService>()
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
    public async Task VectorizeMaterial_CreatesChunksInQdrant()
    {
        // Arrange
        var material = CreateTestMaterial(_testUserId, _testProjectId, "Test material content with enough text to create chunks. " + string.Join(" ", Enumerable.Range(1, 1000).Select(i => $"word{i}")));
        _db.Materials.Add(material);
        await _db.SaveChangesAsync();

        // Act
        await _service.VectorizeMaterialAsync(material.Id, _testUserId);

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
    public async Task VectorizeMaterial_WithContent_Success()
    {
        // Arrange
        var content = "This is a test material with inline content. " + string.Join(" ", Enumerable.Range(1, 500).Select(i => $"word{i}"));
        var material = CreateTestMaterial(_testUserId, _testProjectId, content);
        _db.Materials.Add(material);
        await _db.SaveChangesAsync();

        // Act
        await _service.VectorizeMaterialAsync(material.Id, _testUserId);

        // Assert
        var updatedMaterial = await _db.Materials.FindAsync(material.Id);
        Assert.NotNull(updatedMaterial);
        Assert.True(updatedMaterial.VectorChunkCount > 0);
    }

    [Fact]
    public async Task VectorizeMaterial_UpdatesVectorChunkCount()
    {
        // Arrange
        var material = CreateTestMaterial(_testUserId, _testProjectId, "Content with 100 words. " + string.Join(" ", Enumerable.Range(1, 100).Select(i => $"word{i}")));
        _db.Materials.Add(material);
        await _db.SaveChangesAsync();

        Assert.Equal(0, material.VectorChunkCount);

        // Act
        await _service.VectorizeMaterialAsync(material.Id, _testUserId);

        // Assert
        var updatedMaterial = await _db.Materials.FindAsync(material.Id);
        Assert.NotNull(updatedMaterial);
        Assert.True(updatedMaterial.VectorChunkCount > 0);
    }

    [Fact]
    public async Task VectorizeMaterial_NonExistentMaterial_ThrowsKeyNotFoundException()
    {
        // Arrange
        var nonExistentMaterialId = Guid.NewGuid().ToString();

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
        {
            await _service.VectorizeMaterialAsync(nonExistentMaterialId, _testUserId);
        });
    }

    [Fact]
    public async Task VectorizeMaterial_WrongUserId_ThrowsKeyNotFoundException()
    {
        // Arrange
        var material = CreateTestMaterial(_testUserId, _testProjectId, "Test content");
        _db.Materials.Add(material);
        await _db.SaveChangesAsync();

        var wrongUserId = Guid.NewGuid().ToString();

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
        {
            await _service.VectorizeMaterialAsync(material.Id, wrongUserId);
        });
    }

    [Fact]
    public async Task VectorizeMaterial_ReVectorization_ReplacesOldVectors()
    {
        // Arrange
        var material = CreateTestMaterial(_testUserId, _testProjectId, "Initial content. " + string.Join(" ", Enumerable.Range(1, 500).Select(i => $"word{i}")));
        _db.Materials.Add(material);
        await _db.SaveChangesAsync();

        // Act - First vectorization
        await _service.VectorizeMaterialAsync(material.Id, _testUserId);
        var firstChunkCount = (await _db.Materials.FindAsync(material.Id))!.VectorChunkCount;

        // Act - Second vectorization (should replace)
        await _service.VectorizeMaterialAsync(material.Id, _testUserId);
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
    public async Task VectorizeAllMaterials_VectorizesMultipleMaterials()
    {
        // Arrange
        var material1 = CreateTestMaterial(_testUserId, _testProjectId, "Material 1 content. " + string.Join(" ", Enumerable.Range(1, 300).Select(i => $"word{i}")));
        var material2 = CreateTestMaterial(_testUserId, _testProjectId, "Material 2 content. " + string.Join(" ", Enumerable.Range(1, 300).Select(i => $"word{i}")));
        var material3 = CreateTestMaterial(_testUserId, _testProjectId, "Material 3 content. " + string.Join(" ", Enumerable.Range(1, 300).Select(i => $"word{i}")));

        _db.Materials.AddRange(material1, material2, material3);
        await _db.SaveChangesAsync();

        // Act
        var successCount = await _service.VectorizeAllMaterialsAsync(_testProjectId, _testUserId);

        // Assert
        Assert.Equal(3, successCount);

        var materials = await _db.Materials
            .Where(m => m.ProjectId == _testProjectId)
            .ToListAsync();

        Assert.All(materials, m => Assert.True(m.VectorChunkCount > 0));
    }

    [Fact]
    public async Task VectorizeAllMaterials_EmptyProject_ReturnsZero()
    {
        // Arrange
        var emptyProjectId = Guid.NewGuid().ToString();

        // Act
        var successCount = await _service.VectorizeAllMaterialsAsync(emptyProjectId, _testUserId);

        // Assert
        Assert.Equal(0, successCount);
    }

    [Fact]
    public async Task VectorizeMaterial_CreatesCorrectPayloadInQdrant()
    {
        // Arrange
        var material = CreateTestMaterial(_testUserId, _testProjectId, "Payload test content. " + string.Join(" ", Enumerable.Range(1, 300).Select(i => $"word{i}")));
        material.Category = "research";
        _db.Materials.Add(material);
        await _db.SaveChangesAsync();

        // Act
        await _service.VectorizeMaterialAsync(material.Id, _testUserId);

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
        Assert.Equal("material", point.Payload["entity_type"].StringValue);
        Assert.Equal(material.Id, point.Payload["entity_id"].StringValue);
        Assert.Equal(material.Title, point.Payload["title"].StringValue);
        Assert.Equal("research", point.Payload["category"].StringValue);
    }

    private static Material CreateTestMaterial(string userId, string projectId, string content)
    {
        return new Material
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            ProjectId = projectId,
            Title = "Test Material",
            Category = "general",
            ContentType = "text/plain",
            Content = content,
            CreatedAt = DateTime.UtcNow
        };
    }
}
