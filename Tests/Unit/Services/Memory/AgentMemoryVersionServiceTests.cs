using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Memory;
using Xunit;

namespace Tests.Unit.Services.Memory;

public class AgentMemoryVersionServiceTests
{
    [Fact]
    public async Task BumpAsync_IncrementsScopedVersionAndInvalidatesContextCaches()
    {
        await using var db = CreateDbContext();
        var redisCache = new Mock<IDistributedCacheService>();
        var memoryCache = new Mock<IMemoryCacheService>();
        var service = new AgentMemoryVersionService(db, redisCache.Object, memoryCache.Object);

        var first = await service.BumpAsync("user-1", "project-1", "session-1", "project");
        var second = await service.BumpAsync("user-1", "project-1", "session-1", "project");

        Assert.Equal(1, first);
        Assert.Equal(2, second);

        var saved = await db.AgentMemoryVersions.SingleAsync();
        Assert.Equal("user-1", saved.UserId);
        Assert.Equal("project-1", saved.ProjectId);
        Assert.Equal("session-1", saved.SessionId);
        Assert.Equal("project", saved.Scope);
        Assert.Equal(2, saved.Version);

        memoryCache.Verify(x => x.RemoveByPrefix("memory-context:user-1:session-1:project-1"), Times.Exactly(2));
        memoryCache.Verify(x => x.RemoveByPrefix("toolcache:user-1:session-1:project-1"), Times.Exactly(2));
        redisCache.Verify(x => x.RemoveByPrefixAsync("memory-context:user-1:session-1:project-1", It.IsAny<CancellationToken>()), Times.Exactly(2));
        redisCache.Verify(x => x.RemoveByPrefixAsync("toolcache:user-1:session-1:project-1", It.IsAny<CancellationToken>()), Times.Exactly(2));
        redisCache.Verify(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetCombinedVersionAsync_ChangesAfterBump()
    {
        await using var db = CreateDbContext();
        var service = new AgentMemoryVersionService(
            db,
            Mock.Of<IDistributedCacheService>(),
            Mock.Of<IMemoryCacheService>());

        var empty = await service.GetCombinedVersionAsync("user-1", "project-1", "session-1");
        await service.BumpAsync("user-1", null, null, "author");
        await service.BumpAsync("user-1", "project-1", null, "project");
        await service.BumpAsync("user-1", "project-1", "session-1", "session");

        var combined = await service.GetCombinedVersionAsync("user-1", "project-1", "session-1");

        Assert.Equal("none=0", empty);
        Assert.Equal("author:*:*=1|project:project-1:*=1|session:project-1:session-1=1", combined);
    }

    [Fact]
    public async Task AppendAsync_PersistsMemoryEventAndBumpsScopeVersion()
    {
        await using var db = CreateDbContext();
        var versionService = new AgentMemoryVersionService(
            db,
            Mock.Of<IDistributedCacheService>(),
            Mock.Of<IMemoryCacheService>());
        var eventService = new AgentMemoryEventService(db, versionService);

        await eventService.AppendAsync(
            "user-1",
            "project-1",
            "session-1",
            "run-1",
            "knowledge_used",
            "tool_call",
            "project",
            "project.referenced_knowledge_ids",
            new { knowledgeId = "knowledge-1" });

        var savedEvent = await db.AgentMemoryEvents.SingleAsync();
        Assert.Equal("user-1", savedEvent.UserId);
        Assert.Equal("project-1", savedEvent.ProjectId);
        Assert.Equal("session-1", savedEvent.SessionId);
        Assert.Equal("run-1", savedEvent.RunId);
        Assert.Equal("knowledge_used", savedEvent.SourceType);
        Assert.Equal("tool_call", savedEvent.TriggerType);
        Assert.Equal("project", savedEvent.MemoryScope);
        Assert.Equal("project.referenced_knowledge_ids", savedEvent.MemoryKey);
        Assert.Contains("\"knowledgeId\":\"knowledge-1\"", savedEvent.PayloadJson);
        Assert.False(string.IsNullOrWhiteSpace(savedEvent.Id));

        var savedVersion = await db.AgentMemoryVersions.SingleAsync();
        Assert.Equal("project", savedVersion.Scope);
        Assert.Equal(1, savedVersion.Version);
    }

    [Fact]
    public void MemoryCacheService_RemoveByPrefix_RemovesFullPrefixMatches()
    {
        using var innerCache = new MemoryCache(new MemoryCacheOptions());
        var service = new MemoryCacheService(innerCache, NullLogger<MemoryCacheService>.Instance);

        service.Set("memory-context:u:s:p:v1", "old-context", TimeSpan.FromMinutes(5));
        service.Set("memory-context:u:s:p2:v1", "sibling-context", TimeSpan.FromMinutes(5));
        service.Set("memory-context:u:s:other:v1", "other-context", TimeSpan.FromMinutes(5));
        service.Set("toolcache:u:s:p:phase:v1", "old-tool", TimeSpan.FromMinutes(5));

        service.RemoveByPrefix("memory-context:u:s:p");

        Assert.Null(service.Get<string>("memory-context:u:s:p:v1"));
        Assert.Equal("sibling-context", service.Get<string>("memory-context:u:s:p2:v1"));
        Assert.Equal("other-context", service.Get<string>("memory-context:u:s:other:v1"));
        Assert.Equal("old-tool", service.Get<string>("toolcache:u:s:p:phase:v1"));
    }

    [Fact]
    public async Task DistributedCache_RemoveByPrefixAsync_RemovesVersionedPrefixMatches()
    {
        var cache = new PrefixAwareDistributedCache();
        await cache.SetAsync("memory-context:u:s:p:v1", new object());
        await cache.SetAsync("memory-context:u:s:p2:v1", new object());
        await cache.SetAsync("toolcache:u:s:p:phase:v1", new object());

        await cache.RemoveByPrefixAsync("memory-context:u:s:p");

        Assert.False(await cache.ExistsAsync("memory-context:u:s:p:v1"));
        Assert.True(await cache.ExistsAsync("memory-context:u:s:p2:v1"));
        Assert.True(await cache.ExistsAsync("toolcache:u:s:p:phase:v1"));

        await cache.RemoveByPrefixAsync("toolcache:u:s:p");

        Assert.False(await cache.ExistsAsync("toolcache:u:s:p:phase:v1"));
    }

    [Fact]
    public async Task BumpAsync_RemovesVersionedDistributedCacheEntriesByPrefix()
    {
        await using var db = CreateDbContext();
        var distributedCache = new PrefixAwareDistributedCache();
        var service = new AgentMemoryVersionService(
            db,
            distributedCache,
            Mock.Of<IMemoryCacheService>());

        await distributedCache.SetAsync("memory-context:user-1:session-1:project-1:v1", new object());
        await distributedCache.SetAsync("memory-context:user-1:session-1:project-2:v1", new object());
        await distributedCache.SetAsync("toolcache:user-1:session-1:project-1:search:v1", new object());

        await service.BumpAsync("user-1", "project-1", "session-1", "project");

        Assert.False(await distributedCache.ExistsAsync("memory-context:user-1:session-1:project-1:v1"));
        Assert.True(await distributedCache.ExistsAsync("memory-context:user-1:session-1:project-2:v1"));
        Assert.False(await distributedCache.ExistsAsync("toolcache:user-1:session-1:project-1:search:v1"));
    }

    [Fact]
    public async Task BumpAsync_ConcurrentSqliteCallsDoNotLoseIncrements()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        var options = CreateSqliteOptions(connection.ConnectionString);

        await using (var setupDb = new NovelAgentDbContext(options))
        {
            await setupDb.Database.EnsureCreatedAsync();
            SeedUserAndProject(setupDb);
            await setupDb.SaveChangesAsync();
        }

        const int bumpCount = 16;
        var tasks = Enumerable.Range(0, bumpCount).Select(async _ =>
        {
            await using var db = new NovelAgentDbContext(options);
            var service = new AgentMemoryVersionService(
                db,
                Mock.Of<IDistributedCacheService>(),
                Mock.Of<IMemoryCacheService>());

            return await service.BumpAsync("user-1", "project-1", "session-1", "project");
        });

        var versions = await Task.WhenAll(tasks);

        await using var verifyDb = new NovelAgentDbContext(options);
        var savedVersion = await verifyDb.AgentMemoryVersions
            .Where(v => v.UserId == "user-1" && v.ProjectId == "project-1" && v.SessionId == "session-1" && v.Scope == "project")
            .Select(v => v.Version)
            .SingleAsync();

        Assert.Equal(bumpCount, savedVersion);
        Assert.Equal(Enumerable.Range(1, bumpCount).Select(v => (long)v).ToArray(), versions.OrderBy(v => v).ToArray());
    }

    [Fact]
    public async Task AppendAsync_RollsBackEventAndVersionWhenVersionInvalidationFails()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        var options = CreateSqliteOptions(connection.ConnectionString);

        await using var db = new NovelAgentDbContext(options);
        await db.Database.EnsureCreatedAsync();
        SeedUserAndProject(db);
        await db.SaveChangesAsync();

        var versionService = new AgentMemoryVersionService(
            db,
            new ThrowingPrefixDistributedCache(),
            Mock.Of<IMemoryCacheService>());
        var eventService = new AgentMemoryEventService(db, versionService);

        await Assert.ThrowsAsync<InvalidOperationException>(() => eventService.AppendAsync(
            "user-1",
            "project-1",
            "session-1",
            "run-1",
            "knowledge_used",
            "tool_call",
            "project",
            "project.referenced_knowledge_ids",
            new { knowledgeId = "knowledge-1" }));

        Assert.Equal(0, await db.AgentMemoryEvents.CountAsync());
        Assert.Equal(0, await db.AgentMemoryVersions.CountAsync());
    }

    [Fact]
    public async Task RedisPrefixDeletion_DeleteMatchingKeysInBatchesWithoutDeletingSiblingPrefixes()
    {
        var keys = Enumerable.Range(0, 1001)
            .Select(i => (RedisKey)$"NovelAgent:memory-context:u:s:p:v{i}")
            .Append("NovelAgent:memory-context:u:s:p2:v1")
            .ToList();
        var batchSizes = new List<int>();

        var removed = await RedisCacheService.DeletePrefixMatchesInBatchesAsync(
            keys,
            "NovelAgent:memory-context:u:s:p",
            500,
            batch =>
            {
                batchSizes.Add(batch.Count);
                return Task.FromResult<long>(batch.Count);
            });

        Assert.Equal(1001, removed);
        Assert.Equal(new[] { 500, 500, 1 }, batchSizes);
    }

    [Fact]
    public async Task RedisCacheService_RemoveByPrefixAsync_ThrowsWhenMultiplexerUnavailable()
    {
        var distributedCache = new Mock<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis:DefaultExpiration"] = "00:10:00",
                ["Redis:InstanceName"] = "NovelAgent:"
            })
            .Build();
        var service = new RedisCacheService(
            distributedCache.Object,
            configuration,
            Enumerable.Empty<IConnectionMultiplexer>(),
            NullLogger<RedisCacheService>.Instance);

        await Assert.ThrowsAsync<NotSupportedException>(() => service.RemoveByPrefixAsync("memory-context:u:s:p"));
    }

    private static NovelAgentDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new NovelAgentDbContext(options);
    }

    private static async Task<SqliteConnection> CreateOpenSqliteConnectionAsync()
    {
        var connection = new SqliteConnection($"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared");
        await connection.OpenAsync();
        return connection;
    }

    private static DbContextOptions<NovelAgentDbContext> CreateSqliteOptions(string connectionString)
    {
        return new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connectionString)
            .Options;
    }

    private static void SeedUserAndProject(NovelAgentDbContext db)
    {
        db.Users.Add(new User { Id = "user-1", Username = "u", Email = "u@example.com", PasswordHash = "h", Role = "author" });
        db.NovelProjects.Add(new NovelProject { Id = "project-1", UserId = "user-1", Title = "Project" });
    }

    private class PrefixAwareDistributedCache : IDistributedCacheService
    {
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
        {
            return Task.FromResult<T?>(_keys.Contains(key) ? Activator.CreateInstance<T>() : null);
        }

        public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken ct = default) where T : class
        {
            _keys.Add(key);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            _keys.Remove(key);
            return Task.CompletedTask;
        }

        public virtual Task RemoveByPrefixAsync(string keyPrefix, CancellationToken ct = default)
        {
            foreach (var key in _keys.Where(key => IsPrefixMatch(key, keyPrefix)).ToList())
            {
                _keys.Remove(key);
            }

            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        {
            return Task.FromResult(_keys.Contains(key));
        }

        private static bool IsPrefixMatch(string key, string keyPrefix)
        {
            return key.Length == keyPrefix.Length
                ? string.Equals(key, keyPrefix, StringComparison.Ordinal)
                : key.StartsWith(keyPrefix, StringComparison.Ordinal) && key[keyPrefix.Length] == ':';
        }
    }

    private sealed class ThrowingPrefixDistributedCache : PrefixAwareDistributedCache
    {
        public override Task RemoveByPrefixAsync(string keyPrefix, CancellationToken ct = default)
        {
            throw new InvalidOperationException("prefix invalidation failed");
        }
    }
}
