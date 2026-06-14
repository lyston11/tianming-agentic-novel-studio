using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Vectorization;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using Xunit;

namespace Tests.Unit.Services.Vectorization;

public class MaterialVectorizationServiceTests
{
    [Fact]
    public async Task VectorizeMaterialAsync_MarksContentVectorPointsCompletedWithRealQdrantIds()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        var material = new Material
        {
            Id = "material-1",
            UserId = "user-1",
            ProjectId = "project-1",
            Title = "素材",
            ContentType = "text/plain",
            CreatedAt = DateTime.UtcNow
        };
        db.Materials.Add(material);
        await db.SaveChangesAsync();
        var contentDocument = await new ContentDocumentService(db).SaveOrReplaceTextAsync(
            "user-1",
            "project-1",
            "material",
            material.Id,
            "material_raw",
            material.Title,
            "素材内容 用于 向量化");
        material.RawDocumentId = contentDocument.Id;
        await db.SaveChangesAsync();

        var vectorStore = new RecordingVectorStore();
        var service = new MaterialVectorizationService(
            db,
            vectorStore,
            new FixedEmbeddingService(),
            new MaterialChunker(),
            new RecordingCollectionManager(),
            new ContentDocumentService(db),
            NullLogger<MaterialVectorizationService>.Instance);

        await service.VectorizeMaterialAsync(material.Id, "user-1");

        var point = await db.ContentVectorPoints.SingleAsync(p => p.DocumentId == contentDocument.Id);
        var vector = Assert.Single(vectorStore.Upserted);
        Assert.Equal("completed", point.IndexStatus);
        Assert.Equal(vector.Id, point.QdrantPointId);
        Assert.True(Guid.TryParse(point.QdrantPointId, out _));
        Assert.Equal(nameof(FixedEmbeddingService), point.VectorModel);
        Assert.NotNull(point.IndexedAt);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static void SeedUserProject(NovelAgentDbContext db)
    {
        db.Users.Add(new User
        {
            Id = "user-1",
            Username = "author",
            Email = "author@example.com",
            PasswordHash = "hash",
            Role = "author"
        });
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-1",
            UserId = "user-1",
            Title = "测试项目",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    private sealed class FixedEmbeddingService : IMicroEmbeddingService
    {
        public int Dimension => 3;

        public Task<float[]> EncodeAsync(string text, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default) =>
            Task.FromResult(new[] { 0.1f, 0.2f, 0.3f });

        public Task<float[][]> EncodeBatchAsync(IReadOnlyList<string> texts, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default) =>
            Task.FromResult(texts.Select(_ => new[] { 0.1f, 0.2f, 0.3f }).ToArray());

        public bool IsModelReady() => true;
        public void ReleaseSession() { }
    }

    private sealed class RecordingCollectionManager : IQdrantCollectionManager
    {
        public Task<bool> EnsureUserCollectionAsync(string userId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> CollectionExistsAsync(string collectionName, CancellationToken ct = default) => Task.FromResult(true);
        public Task DeleteUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class RecordingVectorStore : IVectorStore
    {
        public List<VectorData> Upserted { get; } = new();

        public Task InitializeUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> CollectionExistsAsync(string userId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<CollectionInfo?> GetCollectionInfoAsync(string userId, CancellationToken ct = default) => Task.FromResult<CollectionInfo?>(null);
        public Task<List<SearchResult>> SearchSimilarAsync(string userId, float[] queryVector, int topK = 10, Dictionary<string, object>? filters = null, CancellationToken ct = default) => Task.FromResult(new List<SearchResult>());
        public Task DeleteVectorsByFilterAsync(string userId, Dictionary<string, object> filters, CancellationToken ct = default) => Task.CompletedTask;

        public Task UpsertVectorsAsync(string userId, List<VectorData> vectors, CancellationToken ct = default)
        {
            Upserted.AddRange(vectors);
            return Task.CompletedTask;
        }
    }
}
