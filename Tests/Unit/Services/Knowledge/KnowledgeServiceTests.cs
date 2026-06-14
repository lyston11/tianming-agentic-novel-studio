using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using Xunit;

namespace Tests.Unit.Services.Knowledge;

public class KnowledgeServiceTests
{
    private const string UserSearchPrefix = "knowledge:search:user-1:";
    private const string SearchPrefix = "knowledge:search:user-1:project-1";
    private const string InventoryKey = "knowledge:inventory:user-1:project-1";

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
                UserId = "user-1",
                SourceProjectId = "project-1",
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
                UserId = "user-1",
                SourceProjectId = "project-1",
                EntryType = "TropePattern",
                Title = "无关条目",
                Content = "轻松日常桥段。",
                Weight = 5,
                CreatedAt = DateTime.UtcNow.AddMinutes(-1)
            });
        await db.SaveChangesAsync();

        var service = CreateServiceWithUsage(db, "user-1");

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
            UserId = "user-1",
            SourceProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "胜利代价原则",
            Content = "主角每次胜利都必须付出清晰代价。",
            Weight = 8,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateServiceWithUsage(db, "user-1");

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
    public async Task SearchKnowledgeAsync_SearchesUserKnowledgeAcrossProjectsButKeepsUsagePerProject()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-2",
            UserId = "user-1",
            Title = "另一个项目",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.KnowledgeBases.Add(new KnowledgeBase
        {
            Id = "knowledge-1",
            UserId = "user-1",
            SourceProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "胜利代价原则",
            Content = "主角每次胜利都必须付出清晰代价。",
            Weight = 8,
            CreatedAt = DateTime.UtcNow
        });
        db.ProjectKnowledgeUsages.Add(new ProjectKnowledgeUsage
        {
            Id = "usage-1",
            UserId = "user-1",
            ProjectId = "project-1",
            KnowledgeId = "knowledge-1",
            Status = "referenced",
            UsageCount = 2,
            FirstSeenAt = DateTime.UtcNow.AddHours(-1),
            LastUsedAt = DateTime.UtcNow.AddMinutes(-5)
        });
        await db.SaveChangesAsync();
        var service = CreateServiceWithUsage(db, "user-1");

        var results = await service.SearchKnowledgeAsync(new SearchKnowledgeRequest
        {
            ProjectId = "project-2",
            Query = "胜利代价",
            TopK = 5
        });

        var hit = Assert.Single(results);
        Assert.Equal("knowledge-1", hit.Id);
        Assert.Equal("none", hit.ProjectUsageStatus);
        Assert.Equal(0, hit.ProjectUsageCount);
    }

    [Fact]
    public async Task ListKnowledgeAsync_IncludesProjectUsageFields()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        var lastUsedAt = DateTime.UtcNow.AddMinutes(-5);
        db.KnowledgeBases.Add(new KnowledgeBase
        {
            Id = "knowledge-1",
            UserId = "user-1",
            SourceProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "胜利代价原则",
            Content = "主角每次胜利都必须付出清晰代价。",
            Weight = 8,
            CreatedAt = DateTime.UtcNow
        });
        db.ProjectKnowledgeUsages.Add(new ProjectKnowledgeUsage
        {
            Id = "usage-1",
            UserId = "user-1",
            ProjectId = "project-1",
            KnowledgeId = "knowledge-1",
            Status = "referenced",
            UsageCount = 3,
            FirstSeenAt = DateTime.UtcNow.AddHours(-1),
            LastUsedAt = lastUsedAt
        });
        await db.SaveChangesAsync();
        var service = CreateServiceWithUsage(db, "user-1");

        var response = Assert.Single(await service.ListKnowledgeAsync("project-1"));

        Assert.Equal("referenced", response.ProjectUsageStatus);
        Assert.Equal(3, response.ProjectUsageCount);
        Assert.Equal(lastUsedAt, response.ProjectLastUsedAt);
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
        Assert.True(Guid.TryParse(saved.VectorId, out _));
        Assert.Equal(saved.VectorId, response.VectorId);
        Assert.Single(vectorStore.Upserted);
        Assert.Equal(saved.VectorId, vectorStore.Upserted[0].Id);
        Assert.Equal("knowledge", vectorStore.Upserted[0].SourceType);
        Assert.Equal(response.Id, vectorStore.Upserted[0].SourceId);
    }

    [Fact]
    public async Task CreateKnowledgeAsync_DoesNotAutomaticallyBindKnowledgeToProjectUsage()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        var service = CreateServiceWithUsage(db, "user-1");

        var response = await service.CreateKnowledgeAsync(new CreateKnowledgeRequest
        {
            ProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "全局素材原则",
            Content = "这个知识可以被多个项目显式使用。"
        });

        Assert.NotNull(await db.KnowledgeBases.FindAsync(response.Id));
        Assert.Empty(await db.ProjectKnowledgeUsages.ToListAsync());
        Assert.Equal("none", response.ProjectUsageStatus);
    }

    [Fact]
    public async Task CreateExtractedKnowledgeAsync_DoesNotAutomaticallyBindKnowledgeToProjectUsage()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        var service = CreateServiceWithUsage(db, "user-1");

        var response = await service.CreateExtractedKnowledgeAsync(new CreateExtractedKnowledgeRequest
        {
            ProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "上传提取原则",
            Content = "上传知识进入用户级知识库，项目使用需要显式绑定。",
            SourceUploadTaskId = "task-1"
        });

        var saved = await db.KnowledgeBases.FindAsync(response.Id);
        Assert.NotNull(saved);
        Assert.Equal("task-1", saved!.SourceUploadTaskId);
        Assert.Empty(await db.ProjectKnowledgeUsages.ToListAsync());
        Assert.Equal("none", response.ProjectUsageStatus);
    }

    [Fact]
    public async Task UpdateKnowledgeAsync_ReusesVectorIdAndRefreshesVectorContent()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        db.KnowledgeBases.Add(new KnowledgeBase
        {
            Id = "knowledge-1",
            UserId = "user-1",
            SourceProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "旧标题",
            Content = "旧内容",
            VectorId = "11111111-1111-1111-1111-111111111111",
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
        Assert.Equal("11111111-1111-1111-1111-111111111111", vectorStore.Upserted[0].Id);
        Assert.Equal("knowledge-1", vectorStore.Upserted[0].SourceId);
        Assert.Equal("新内容", vectorStore.Upserted[0].Content);
    }

    [Fact]
    public async Task DeleteKnowledgeAsync_DeletesKnowledgeVectorsBeforeRemovingRow()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        db.KnowledgeBases.Add(new KnowledgeBase { Id = "knowledge-1", UserId = "user-1", SourceProjectId = "project-1",
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
        Assert.False(delete.ContainsKey("project_id"));
        Assert.Equal("knowledge", delete["source_type"]);
        Assert.Equal("knowledge-1", delete["source_id"]);
    }

    [Fact]
    public async Task SearchKnowledgeAsync_CachesResultsByUserProjectQueryAndTopK()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        db.KnowledgeBases.Add(new KnowledgeBase { Id = "knowledge-1", UserId = "user-1", SourceProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "胜利代价原则",
            Content = "主角每次胜利都必须付出清晰代价。",
            Weight = 8,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var redis = new PrefixAwareDistributedCache();
        var memory = new PrefixAwareMemoryCache();
        var vectorStore = new RecordingVectorStore();
        var service = CreateService(db, "user-1", vectorStore, redis, memory);

        var first = await service.SearchKnowledgeAsync(new SearchKnowledgeRequest
        {
            ProjectId = "project-1",
            Query = "胜利代价",
            TopK = 5
        });
        db.KnowledgeBases.RemoveRange(db.KnowledgeBases);
        await db.SaveChangesAsync();

        var second = await service.SearchKnowledgeAsync(new SearchKnowledgeRequest
        {
            ProjectId = "project-1",
            Query = "胜利代价",
            TopK = 5
        });

        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal("knowledge-1", second[0].Id);
        Assert.Equal(1, vectorStore.SearchCount);
    }

    [Fact]
    public async Task KnowledgeMutations_InvalidatesProjectSearchCaches()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        db.KnowledgeBases.Add(new KnowledgeBase { Id = "knowledge-1", UserId = "user-1", SourceProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "旧标题",
            Content = "旧内容",
            VectorId = "knowledge_knowledge-1",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var redis = new Mock<IDistributedCacheService>();
        var memory = new Mock<IMemoryCacheService>();
        redis.Setup(x => x.RemoveByPrefixAsync(SearchPrefix, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var service = CreateService(db, "user-1", new RecordingVectorStore(), redis.Object, memory.Object);

        await service.CreateKnowledgeAsync(new CreateKnowledgeRequest
        {
            ProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "新知识",
            Content = "新内容"
        });
        await service.UpdateKnowledgeAsync("knowledge-1", new UpdateKnowledgeRequest { Title = "新标题" });
        await service.IncrementUsageAsync("knowledge-1", "project-1");
        await service.DeleteKnowledgeAsync("knowledge-1");

        memory.Verify(x => x.RemoveByPrefix(UserSearchPrefix), Times.Exactly(3));
        memory.Verify(x => x.RemoveByPrefix(SearchPrefix), Times.Exactly(2));
        memory.Verify(x => x.Remove(InventoryKey), Times.Exactly(2));
        redis.Verify(x => x.RemoveByPrefixAsync(UserSearchPrefix, It.IsAny<CancellationToken>()), Times.Exactly(3));
        redis.Verify(x => x.RemoveByPrefixAsync(SearchPrefix, It.IsAny<CancellationToken>()), Times.Exactly(2));
        redis.Verify(x => x.RemoveAsync(InventoryKey, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task KnowledgeMutations_InvalidatesRealMemorySearchCache()
    {
        await using var db = CreateDb();
        SeedUserProject(db);
        db.KnowledgeBases.Add(new KnowledgeBase { Id = "knowledge-1", UserId = "user-1", SourceProjectId = "project-1",
            EntryType = "ReaderPromise",
            Title = "胜利代价原则",
            Content = "主角每次胜利都必须付出清晰代价。",
            VectorId = "knowledge_knowledge-1",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var memory = new MemoryCacheService(
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<MemoryCacheService>.Instance);
        var service = CreateService(db, "user-1", new RecordingVectorStore(), Mock.Of<IDistributedCacheService>(), memory);

        var first = await service.SearchKnowledgeAsync(new SearchKnowledgeRequest
        {
            ProjectId = "project-1",
            Query = "胜利代价",
            TopK = 5
        });
        await service.UpdateKnowledgeAsync("knowledge-1", new UpdateKnowledgeRequest
        {
            Title = "新标题",
            Content = "完全不同的内容"
        });

        var second = await service.SearchKnowledgeAsync(new SearchKnowledgeRequest
        {
            ProjectId = "project-1",
            Query = "胜利代价",
            TopK = 5
        });

        Assert.Single(first);
        Assert.Empty(second);
    }

    [Fact]
    public async Task KnowledgeSearchPrefix_RemovesActualSearchCacheKeys()
    {
        const string prefix = "knowledge:search:user-1:project-1";
        const string key = "knowledge:search:user-1:project-1:*:5:abc";
        var memory = new MemoryCacheService(
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<MemoryCacheService>.Instance);
        memory.Set(key, new List<KnowledgeSearchResult>(), TimeSpan.FromMinutes(5));

        memory.RemoveByPrefix(prefix);

        Assert.Null(memory.Get<List<KnowledgeSearchResult>>(key));
        Assert.True(RedisCacheService.IsPrefixMatch(key, prefix));
        Assert.True(RedisCacheService.IsPrefixMatch(key, "knowledge:search:user-1:"));
        Assert.False(RedisCacheService.IsPrefixMatch(key, "knowledge:search:user-10:"));
        var deleted = new List<RedisKey>();
        var deletedCount = await RedisCacheService.DeletePrefixMatchesInBatchesAsync(
            new RedisKey[] { key, "knowledge:search:user-1:project-2:*:5:abc" },
            prefix,
            RedisCacheService.PrefixDeleteBatchSize,
            batch =>
            {
                deleted.AddRange(batch);
                return Task.FromResult((long)batch.Count);
            });
        Assert.Equal(1, deletedCount);
        Assert.Equal(key, deleted.Single().ToString());
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

    private static KnowledgeService CreateService(
        NovelAgentDbContext db,
        string userId,
        IVectorStore? vectorStore = null,
        IDistributedCacheService? redisCache = null,
        IMemoryCacheService? memoryCache = null)
    {
        vectorStore ??= new RecordingVectorStore();
        redisCache ??= Mock.Of<IDistributedCacheService>();
        memoryCache ??= Mock.Of<IMemoryCacheService>();
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
            NullLogger<KnowledgeService>.Instance,
            null,
            redisCache,
            memoryCache);
    }

    private static KnowledgeService CreateServiceWithUsage(NovelAgentDbContext db, string userId)
    {
        var usage = new ProjectKnowledgeUsageService(
            db,
            Mock.Of<IAgentMemoryEventService>(),
            null,
            NullLogger<ProjectKnowledgeUsageService>.Instance,
            Mock.Of<IDistributedCacheService>(),
            Mock.Of<IMemoryCacheService>());

        var vectorStore = new RecordingVectorStore();
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
            NullLogger<KnowledgeService>.Instance,
            usage,
            Mock.Of<IDistributedCacheService>(),
            Mock.Of<IMemoryCacheService>());
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
        public int SearchCount { get; private set; }

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
            CancellationToken ct = default)
        {
            SearchCount++;
            return Task.FromResult(new List<SearchResult>());
        }

        public Task DeleteUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> CollectionExistsAsync(string userId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<CollectionInfo?> GetCollectionInfoAsync(string userId, CancellationToken ct = default) => Task.FromResult<CollectionInfo?>(null);
        public Task DeleteVectorsByFilterAsync(string userId, Dictionary<string, object> filters, CancellationToken ct = default)
        {
            DeletedFilters.Add(new Dictionary<string, object>(filters));
            return Task.CompletedTask;
        }
    }

    private sealed class PrefixAwareDistributedCache : IDistributedCacheService
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class =>
            Task.FromResult(_values.TryGetValue(key, out var value) ? value as T : null);

        public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken ct = default) where T : class
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task RemoveByPrefixAsync(string keyPrefix, CancellationToken ct = default)
        {
            foreach (var key in _values.Keys.Where(k => k.StartsWith(keyPrefix, StringComparison.Ordinal)).ToList())
                _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(_values.ContainsKey(key));
    }

    private sealed class PrefixAwareMemoryCache : IMemoryCacheService
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

        public Task<T?> GetOrSetAsync<T>(
            string key,
            Func<Task<T>> factory,
            TimeSpan expiration,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public T? Get<T>(string key) =>
            _values.TryGetValue(key, out var value) ? (T)value : default;

        public void Set<T>(string key, T value, TimeSpan expiration)
        {
            if (value != null)
                _values[key] = value;
        }

        public void Remove(string key) => _values.Remove(key);

        public void RemoveByPrefix(string keyPrefix)
        {
            foreach (var key in _values.Keys.Where(k => k.StartsWith(keyPrefix, StringComparison.Ordinal)).ToList())
                _values.Remove(key);
        }
    }
}
