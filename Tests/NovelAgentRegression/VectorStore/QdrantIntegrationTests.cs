using System.Diagnostics;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using Xunit;

namespace TM.Tests.NovelAgentRegression.VectorStore;

/// <summary>
/// Integration tests for Qdrant vector store operations.
/// Tests use real Qdrant instance via Testcontainers.
/// </summary>
public class QdrantIntegrationTests : IClassFixture<QdrantTestFixture>
{
    private readonly IVectorStore _vectorStore;
    private const int VectorDimension = 512;

    public QdrantIntegrationTests(QdrantTestFixture fixture)
    {
        _vectorStore = fixture.VectorStore;
    }

    /// <summary>
    /// Generate unique project ID for test isolation.
    /// </summary>
    private static string GenerateTestProjectId() => $"test_project_{Guid.NewGuid():N}";

    /// <summary>
    /// Generate random vector with specified dimension.
    /// </summary>
    private static float[] GenerateRandomVector(int dimension)
    {
        var random = new Random();
        var vector = new float[dimension];
        for (int i = 0; i < dimension; i++)
        {
            vector[i] = (float)random.NextDouble();
        }
        return vector;
    }

    [Fact]
    public async Task Test_CreateCollection_Success()
    {
        // Arrange
        var projectId = GenerateTestProjectId();

        try
        {
            // Act
            await _vectorStore.InitializeProjectCollectionAsync(projectId);

            // Assert
            var exists = await _vectorStore.CollectionExistsAsync(projectId);
            Assert.True(exists);
        }
        finally
        {
            // Cleanup
            await _vectorStore.DeleteCollectionAsync(projectId);
        }
    }

    [Fact]
    public async Task Test_GetCollectionInfo_ReturnsCorrectConfig()
    {
        // Arrange
        var projectId = GenerateTestProjectId();

        try
        {
            await _vectorStore.InitializeProjectCollectionAsync(projectId);

            // Act
            var collectionInfo = await _vectorStore.GetCollectionInfoAsync(projectId);

            // Assert
            Assert.NotNull(collectionInfo);
            Assert.Equal($"project_{projectId}", collectionInfo.Name);
            Assert.Equal(VectorDimension, collectionInfo.VectorDimension);
            Assert.Equal("Cosine", collectionInfo.DistanceMetric);
            Assert.Equal("HNSW", collectionInfo.IndexType);
            Assert.Equal(0, collectionInfo.VectorCount); // Empty collection
        }
        finally
        {
            // Cleanup
            await _vectorStore.DeleteCollectionAsync(projectId);
        }
    }

    [Fact]
    public async Task Test_UpsertVectors_Single_Success()
    {
        // Arrange
        var projectId = GenerateTestProjectId();
        var userId = "user_001";
        var vector = GenerateRandomVector(VectorDimension);

        try
        {
            await _vectorStore.InitializeProjectCollectionAsync(projectId);

            var vectorData = new VectorData
            {
                Id = Guid.NewGuid().ToString(),
                Vector = vector,
                UserId = userId,
                ProjectId = projectId,
                SourceType = "chapter",
                SourceId = "chapter_001",
                ChapterId = "chapter_001",
                Content = "Test chapter content"
            };

            // Act
            await _vectorStore.UpsertVectorsAsync(projectId, new List<VectorData> { vectorData });

            // Assert
            var collectionInfo = await _vectorStore.GetCollectionInfoAsync(projectId);
            Assert.NotNull(collectionInfo);
            Assert.Equal(1, collectionInfo.VectorCount);
        }
        finally
        {
            // Cleanup
            await _vectorStore.DeleteCollectionAsync(projectId);
        }
    }

    [Fact]
    public async Task Test_UpsertVectors_Batch_Success()
    {
        // Arrange
        var projectId = GenerateTestProjectId();
        var userId = "user_002";
        var batchSize = 50;

        try
        {
            await _vectorStore.InitializeProjectCollectionAsync(projectId);

            var vectors = new List<VectorData>();
            for (int i = 0; i < batchSize; i++)
            {
                vectors.Add(new VectorData
                {
                    Id = Guid.NewGuid().ToString(),
                    Vector = GenerateRandomVector(VectorDimension),
                    UserId = userId,
                    ProjectId = projectId,
                    SourceType = "chapter",
                    SourceId = $"chapter_{i:D3}",
                    ChapterId = $"chapter_{i:D3}",
                    Content = $"Test content for chapter {i}"
                });
            }

            // Act
            await _vectorStore.UpsertVectorsAsync(projectId, vectors);

            // Assert
            var collectionInfo = await _vectorStore.GetCollectionInfoAsync(projectId);
            Assert.NotNull(collectionInfo);
            Assert.Equal(batchSize, collectionInfo.VectorCount);
        }
        finally
        {
            // Cleanup
            await _vectorStore.DeleteCollectionAsync(projectId);
        }
    }

    [Fact]
    public async Task Test_SearchSimilar_ReturnsResults()
    {
        // Arrange
        var projectId = GenerateTestProjectId();
        var userId = "user_003";
        var testVector = GenerateRandomVector(VectorDimension);

        try
        {
            await _vectorStore.InitializeProjectCollectionAsync(projectId);

            // Insert test vectors
            var vectors = new List<VectorData>();
            for (int i = 0; i < 10; i++)
            {
                vectors.Add(new VectorData
                {
                    Id = Guid.NewGuid().ToString(),
                    Vector = GenerateRandomVector(VectorDimension),
                    UserId = userId,
                    ProjectId = projectId,
                    SourceType = "chapter",
                    SourceId = $"chapter_{i:D3}",
                    ChapterId = $"chapter_{i:D3}",
                    Content = $"Test content {i}"
                });
            }

            await _vectorStore.UpsertVectorsAsync(projectId, vectors);

            // Wait for indexing
            await Task.Delay(500);

            // Act
            var results = await _vectorStore.SearchSimilarAsync(
                projectId,
                testVector,
                topK: 5
            );

            // Assert
            Assert.NotNull(results);
            Assert.NotEmpty(results);
            Assert.True(results.Count <= 5);
            Assert.All(results, r =>
            {
                Assert.Equal(userId, r.UserId);
                Assert.Equal(projectId, r.ProjectId);
                Assert.Equal("chapter", r.SourceType);
                Assert.NotNull(r.Content);
            });
        }
        finally
        {
            // Cleanup
            await _vectorStore.DeleteCollectionAsync(projectId);
        }
    }

    [Fact]
    public async Task Test_SearchWithUserFilter_IsolatesData()
    {
        // Arrange
        var projectId = GenerateTestProjectId();
        var user1Id = "user_alice";
        var user2Id = "user_bob";

        try
        {
            await _vectorStore.InitializeProjectCollectionAsync(projectId);

            // Insert vectors for user 1
            var user1Vectors = new List<VectorData>();
            for (int i = 0; i < 5; i++)
            {
                user1Vectors.Add(new VectorData
                {
                    Id = Guid.NewGuid().ToString(),
                    Vector = GenerateRandomVector(VectorDimension),
                    UserId = user1Id,
                    ProjectId = projectId,
                    SourceType = "chapter",
                    SourceId = $"chapter_user1_{i}",
                    ChapterId = $"chapter_user1_{i}",
                    Content = $"User 1 content {i}"
                });
            }

            // Insert vectors for user 2
            var user2Vectors = new List<VectorData>();
            for (int i = 0; i < 5; i++)
            {
                user2Vectors.Add(new VectorData
                {
                    Id = Guid.NewGuid().ToString(),
                    Vector = GenerateRandomVector(VectorDimension),
                    UserId = user2Id,
                    ProjectId = projectId,
                    SourceType = "chapter",
                    SourceId = $"chapter_user2_{i}",
                    ChapterId = $"chapter_user2_{i}",
                    Content = $"User 2 content {i}"
                });
            }

            await _vectorStore.UpsertVectorsAsync(projectId, user1Vectors);
            await _vectorStore.UpsertVectorsAsync(projectId, user2Vectors);

            // Wait for indexing
            await Task.Delay(500);

            var queryVector = GenerateRandomVector(VectorDimension);

            // Act - Search with user 1 filter
            var user1Results = await _vectorStore.SearchSimilarAsync(
                projectId,
                queryVector,
                topK: 10,
                filters: new Dictionary<string, object> { ["user_id"] = user1Id }
            );

            // Act - Search with user 2 filter
            var user2Results = await _vectorStore.SearchSimilarAsync(
                projectId,
                queryVector,
                topK: 10,
                filters: new Dictionary<string, object> { ["user_id"] = user2Id }
            );

            // Assert - User 1 results only contain user 1 data
            Assert.NotNull(user1Results);
            Assert.NotEmpty(user1Results);
            Assert.All(user1Results, r => Assert.Equal(user1Id, r.UserId));
            Assert.All(user1Results, r => Assert.Contains("user1", r.SourceId!));

            // Assert - User 2 results only contain user 2 data
            Assert.NotNull(user2Results);
            Assert.NotEmpty(user2Results);
            Assert.All(user2Results, r => Assert.Equal(user2Id, r.UserId));
            Assert.All(user2Results, r => Assert.Contains("user2", r.SourceId!));

            // Assert - No cross-contamination
            Assert.DoesNotContain(user1Results, r => r.UserId == user2Id);
            Assert.DoesNotContain(user2Results, r => r.UserId == user1Id);
        }
        finally
        {
            // Cleanup
            await _vectorStore.DeleteCollectionAsync(projectId);
        }
    }

    [Fact]
    public async Task Test_BatchInsert_Performance_Under5Seconds()
    {
        // Arrange
        var projectId = GenerateTestProjectId();
        var userId = "user_perf_test";
        var batchSize = 1000;

        try
        {
            await _vectorStore.InitializeProjectCollectionAsync(projectId);

            var vectors = new List<VectorData>();
            for (int i = 0; i < batchSize; i++)
            {
                vectors.Add(new VectorData
                {
                    Id = Guid.NewGuid().ToString(),
                    Vector = GenerateRandomVector(VectorDimension),
                    UserId = userId,
                    ProjectId = projectId,
                    SourceType = "chapter",
                    SourceId = $"chapter_{i:D4}",
                    ChapterId = $"chapter_{i:D4}",
                    ChunkIndex = i % 10,
                    Content = $"Performance test content {i}",
                    Metadata = new Dictionary<string, object>
                    {
                        ["test_id"] = i,
                        ["batch"] = "performance"
                    }
                });
            }

            // Act
            var stopwatch = Stopwatch.StartNew();
            await _vectorStore.UpsertVectorsAsync(projectId, vectors);
            stopwatch.Stop();

            // Assert
            var elapsed = stopwatch.Elapsed.TotalSeconds;
            Assert.True(elapsed < 5.0, $"Batch insert took {elapsed:F2}s, expected < 5s");

            // Verify all vectors were inserted
            var collectionInfo = await _vectorStore.GetCollectionInfoAsync(projectId);
            Assert.NotNull(collectionInfo);
            Assert.Equal(batchSize, collectionInfo.VectorCount);
        }
        finally
        {
            // Cleanup
            await _vectorStore.DeleteCollectionAsync(projectId);
        }
    }

    [Fact]
    public async Task Test_DeleteCollection_Success()
    {
        // Arrange
        var projectId = GenerateTestProjectId();

        await _vectorStore.InitializeProjectCollectionAsync(projectId);
        var existsBefore = await _vectorStore.CollectionExistsAsync(projectId);
        Assert.True(existsBefore);

        // Act
        await _vectorStore.DeleteCollectionAsync(projectId);

        // Assert
        var existsAfter = await _vectorStore.CollectionExistsAsync(projectId);
        Assert.False(existsAfter);

        var collectionInfo = await _vectorStore.GetCollectionInfoAsync(projectId);
        Assert.Null(collectionInfo);
    }

    [Fact]
    public async Task Test_DeleteVectorsByFilter_Success()
    {
        // Arrange
        var projectId = GenerateTestProjectId();
        var userId = "user_delete_test";

        try
        {
            await _vectorStore.InitializeProjectCollectionAsync(projectId);

            // Insert vectors with different source types
            var vectors = new List<VectorData>
            {
                new VectorData
                {
                    Id = Guid.NewGuid().ToString(),
                    Vector = GenerateRandomVector(VectorDimension),
                    UserId = userId,
                    ProjectId = projectId,
                    SourceType = "chapter",
                    SourceId = "chapter_001",
                    ChapterId = "chapter_001",
                    Content = "Chapter content"
                },
                new VectorData
                {
                    Id = Guid.NewGuid().ToString(),
                    Vector = GenerateRandomVector(VectorDimension),
                    UserId = userId,
                    ProjectId = projectId,
                    SourceType = "character",
                    SourceId = "character_001",
                    Content = "Character content"
                }
            };

            await _vectorStore.UpsertVectorsAsync(projectId, vectors);

            // Wait for indexing
            await Task.Delay(500);

            // Verify initial state
            var infoBeforeDelete = await _vectorStore.GetCollectionInfoAsync(projectId);
            Assert.Equal(2, infoBeforeDelete!.VectorCount);

            // Act - Delete only chapter vectors
            await _vectorStore.DeleteVectorsByFilterAsync(
                projectId,
                new Dictionary<string, object> { ["source_type"] = "chapter" }
            );

            // Wait for deletion
            await Task.Delay(500);

            // Assert
            var infoAfterDelete = await _vectorStore.GetCollectionInfoAsync(projectId);
            Assert.Equal(1, infoAfterDelete!.VectorCount);

            // Verify only character vectors remain
            var remainingVectors = await _vectorStore.SearchSimilarAsync(
                projectId,
                GenerateRandomVector(VectorDimension),
                topK: 10
            );

            Assert.Single(remainingVectors);
            Assert.Equal("character", remainingVectors[0].SourceType);
        }
        finally
        {
            // Cleanup
            await _vectorStore.DeleteCollectionAsync(projectId);
        }
    }

    [Fact]
    public async Task Test_CreateCollection_Idempotent()
    {
        // Arrange
        var projectId = GenerateTestProjectId();

        try
        {
            // Act - Create collection twice
            await _vectorStore.InitializeProjectCollectionAsync(projectId);
            await _vectorStore.InitializeProjectCollectionAsync(projectId); // Should not throw

            // Assert
            var exists = await _vectorStore.CollectionExistsAsync(projectId);
            Assert.True(exists);
        }
        finally
        {
            // Cleanup
            await _vectorStore.DeleteCollectionAsync(projectId);
        }
    }

    [Fact]
    public async Task Test_SearchWithSourceTypeFilter_ReturnsFilteredResults()
    {
        // Arrange
        var projectId = GenerateTestProjectId();
        var userId = "user_filter_test";

        try
        {
            await _vectorStore.InitializeProjectCollectionAsync(projectId);

            // Insert mixed source types
            var vectors = new List<VectorData>();
            for (int i = 0; i < 3; i++)
            {
                vectors.Add(new VectorData
                {
                    Id = Guid.NewGuid().ToString(),
                    Vector = GenerateRandomVector(VectorDimension),
                    UserId = userId,
                    ProjectId = projectId,
                    SourceType = "chapter",
                    SourceId = $"chapter_{i}",
                    ChapterId = $"chapter_{i}",
                    Content = $"Chapter {i}"
                });

                vectors.Add(new VectorData
                {
                    Id = Guid.NewGuid().ToString(),
                    Vector = GenerateRandomVector(VectorDimension),
                    UserId = userId,
                    ProjectId = projectId,
                    SourceType = "character",
                    SourceId = $"character_{i}",
                    Content = $"Character {i}"
                });
            }

            await _vectorStore.UpsertVectorsAsync(projectId, vectors);

            // Wait for indexing
            await Task.Delay(500);

            // Act - Search with source_type filter
            var chapterResults = await _vectorStore.SearchSimilarAsync(
                projectId,
                GenerateRandomVector(VectorDimension),
                topK: 10,
                filters: new Dictionary<string, object> { ["source_type"] = "chapter" }
            );

            // Assert
            Assert.NotNull(chapterResults);
            Assert.NotEmpty(chapterResults);
            Assert.All(chapterResults, r => Assert.Equal("chapter", r.SourceType));
            Assert.DoesNotContain(chapterResults, r => r.SourceType == "character");
        }
        finally
        {
            // Cleanup
            await _vectorStore.DeleteCollectionAsync(projectId);
        }
    }

    [Fact]
    public async Task Test_UpsertVectors_InvalidDimension_ThrowsException()
    {
        // Arrange
        var projectId = GenerateTestProjectId();
        var userId = "user_invalid";

        try
        {
            await _vectorStore.InitializeProjectCollectionAsync(projectId);

            var invalidVector = new VectorData
            {
                Id = Guid.NewGuid().ToString(),
                Vector = new float[256], // Wrong dimension (should be 512)
                UserId = userId,
                ProjectId = projectId,
                SourceType = "chapter",
                SourceId = "chapter_001",
                Content = "Test"
            };

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await _vectorStore.UpsertVectorsAsync(projectId, new List<VectorData> { invalidVector });
            });
        }
        finally
        {
            // Cleanup
            await _vectorStore.DeleteCollectionAsync(projectId);
        }
    }

    [Fact]
    public async Task Test_SearchSimilar_InvalidQueryDimension_ThrowsException()
    {
        // Arrange
        var projectId = GenerateTestProjectId();

        try
        {
            await _vectorStore.InitializeProjectCollectionAsync(projectId);

            var invalidQueryVector = new float[256]; // Wrong dimension

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await _vectorStore.SearchSimilarAsync(projectId, invalidQueryVector, topK: 5);
            });
        }
        finally
        {
            // Cleanup
            await _vectorStore.DeleteCollectionAsync(projectId);
        }
    }
}
