using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using Xunit;

namespace Tests.Unit.Services.Knowledge;

public class KnowledgeServiceTests
{
    [Fact]
    public async Task SearchKnowledgeAsync_FallsBackToDatabaseRowsWhenVectorSearchIsEmpty()
    {
        await using var db = CreateDb();
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
        db.KnowledgeBases.AddRange(
            new KnowledgeBase
            {
                Id = "knowledge-1",
                ProjectId = "project-1",
                EntryType = "ReaderPromise",
                Title = "胜利代价原则",
                Content = "主角每次胜利都必须付出清晰代价，避免无成本升级。",
                Tags = """["代价"]""",
                Weight = 8,
                CreatedAt = DateTime.UtcNow
            },
            new KnowledgeBase
            {
                Id = "knowledge-2",
                ProjectId = "project-1",
                EntryType = "TropePattern",
                Title = "无关条目",
                Content = "轻松日常桥段。",
                Weight = 5,
                CreatedAt = DateTime.UtcNow.AddMinutes(-1)
            });
        await db.SaveChangesAsync();

        var service = CreateService(db, "user-1");

        var results = await service.SearchKnowledgeAsync(new SearchKnowledgeRequest
        {
            ProjectId = "project-1",
            Query = "主角胜利的代价",
            TopK = 5
        });

        var hit = Assert.Single(results);
        Assert.Equal("knowledge-1", hit.Id);
        Assert.Equal("ReaderPromise", hit.EntryType);
        Assert.Equal("胜利代价原则", hit.Title);
        Assert.Contains("清晰代价", hit.Content);
        Assert.True(hit.Score > 0);
    }

    [Fact]
    public async Task SearchKnowledgeAsync_RespectsEntryTypeWhenUsingDatabaseFallback()
    {
        await using var db = CreateDb();
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
        db.KnowledgeBases.Add(new KnowledgeBase
        {
            Id = "knowledge-1",
            ProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "胜利代价原则",
            Content = "主角每次胜利都必须付出清晰代价。",
            Weight = 8,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, "user-1");

        var results = await service.SearchKnowledgeAsync(new SearchKnowledgeRequest
        {
            ProjectId = "project-1",
            Query = "胜利代价",
            EntryType = "TropePattern",
            TopK = 5
        });

        Assert.Empty(results);
    }

    [Fact]
    public async Task CreateKnowledgeAsync_StoresVectorIdAfterUpsert()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        var vectorStore = new RecordingVectorStore();
        var service = CreateService(db, "user-1", vectorStore);

        var response = await service.CreateKnowledgeAsync(new CreateKnowledgeRequest
        {
            ProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "胜利代价原则",
            Content = "主角每次胜利都必须付出清晰代价。"
        });

        var saved = await db.KnowledgeBases.FindAsync(response.Id);
        Assert.NotNull(saved);
        Assert.Equal($"knowledge_{response.Id}", saved.VectorId);
        Assert.Equal(saved.VectorId, response.VectorId);
        Assert.Single(vectorStore.Upserted);
        Assert.Equal(saved.VectorId, vectorStore.Upserted[0].Id);
        Assert.Equal("knowledge", vectorStore.Upserted[0].SourceType);
        Assert.Equal(response.Id, vectorStore.Upserted[0].SourceId);
    }

    [Fact]
    public async Task UpdateKnowledgeAsync_ReusesVectorIdAndRefreshesVectorContent()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        db.KnowledgeBases.Add(new KnowledgeBase
        {
            Id = "knowledge-1",
            ProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "旧标题",
            Content = "旧内容",
            VectorId = "knowledge_knowledge-1",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var vectorStore = new RecordingVectorStore();
        var service = CreateService(db, "user-1", vectorStore);

        await service.UpdateKnowledgeAsync("knowledge-1", new UpdateKnowledgeRequest
        {
            Title = "新标题",
            Content = "新内容"
        });

        Assert.Single(vectorStore.Upserted);
        Assert.Equal("knowledge_knowledge-1", vectorStore.Upserted[0].Id);
        Assert.Equal("knowledge-1", vectorStore.Upserted[0].SourceId);
        Assert.Equal("新内容", vectorStore.Upserted[0].Content);
    }

    [Fact]
    public async Task DeleteKnowledgeAsync_DeletesKnowledgeVectorsBeforeRemovingRow()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        db.KnowledgeBases.Add(new KnowledgeBase
        {
            Id = "knowledge-1",
            ProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "胜利代价原则",
            Content = "主角每次胜利都必须付出清晰代价。",
            VectorId = "knowledge_knowledge-1",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var vectorStore = new RecordingVectorStore();
        var service = CreateService(db, "user-1", vectorStore);

        await service.DeleteKnowledgeAsync("knowledge-1");

        Assert.Null(await db.KnowledgeBases.FindAsync("knowledge-1"));
        var delete = Assert.Single(vectorStore.DeletedFilters);
        Assert.Equal("project-1", delete["project_id"]);
        Assert.Equal("knowledge", delete["source_type"]);
        Assert.Equal("knowledge-1", delete["source_id"]);
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

    private static KnowledgeService CreateService(NovelAgentDbContext db, string userId, IVectorStore? vectorStore = null)
    {
        vectorStore ??= new RecordingVectorStore();
        var embedding = new FixedEmbeddingService();
        var searchService = new SemanticSearchService(
            vectorStore,
            embedding,
            NullLogger<SemanticSearchService>.Instance);

        return new KnowledgeService(
            db,
            new FixedCurrentUserService(userId),
            searchService,
            vectorStore,
            embedding,
            NullLogger<KnowledgeService>.Instance);
    }

    private sealed class FixedCurrentUserService : ICurrentUserService
    {
        private readonly string _userId;

        public FixedCurrentUserService(string userId)
        {
            _userId = userId;
        }

        public string GetUserId() => _userId;
        public string GetUsername() => "author";
        public string GetEmail() => "author@example.com";
        public string GetRole() => "author";
        public bool IsAdmin() => false;
        public bool IsAuthenticated() => true;
        public string? TryGetUserId() => _userId;
    }

    private sealed class FixedEmbeddingService : IMicroEmbeddingService
    {
        public int Dimension => 3;

        public Task<float[]> EncodeAsync(string text, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default) =>
            Task.FromResult(new[] { 1f, 0f, 0f });

        public Task<float[][]> EncodeBatchAsync(IReadOnlyList<string> texts, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default) =>
            Task.FromResult(texts.Select(_ => new[] { 1f, 0f, 0f }).ToArray());

        public bool IsModelReady() => true;
        public void ReleaseSession() { }
    }

    private sealed class RecordingVectorStore : IVectorStore
    {
        public List<VectorData> Upserted { get; } = new();
        public List<Dictionary<string, object>> DeletedFilters { get; } = new();

        public Task InitializeUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;

        public Task UpsertVectorsAsync(string userId, List<VectorData> vectors, CancellationToken ct = default)
        {
            Upserted.AddRange(vectors);
            return Task.CompletedTask;
        }

        public Task<List<SearchResult>> SearchSimilarAsync(
            string userId,
            float[] queryVector,
            int topK = 10,
            Dictionary<string, object>? filters = null,
            CancellationToken ct = default) =>
            Task.FromResult(new List<SearchResult>());

        public Task DeleteUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> CollectionExistsAsync(string userId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<CollectionInfo?> GetCollectionInfoAsync(string userId, CancellationToken ct = default) => Task.FromResult<CollectionInfo?>(null);
        public Task DeleteVectorsByFilterAsync(string userId, Dictionary<string, object> filters, CancellationToken ct = default)
        {
            DeletedFilters.Add(new Dictionary<string, object>(filters));
            return Task.CompletedTask;
        }
    }
}
