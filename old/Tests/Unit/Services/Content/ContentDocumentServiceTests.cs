using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Content;
using Xunit;

namespace Tests.Unit.Services.Content;

public class ContentDocumentServiceTests
{
    private const string CacheKey = "content:text:user-1:project-1:knowledge:knowledge-1:raw_upload";

    [Fact]
    public async Task SaveTextAsync_CreatesDocumentChunksAndPendingVectorRows()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new NovelAgentDbContext(options);
        var service = new ContentDocumentService(db);

        var document = await service.SaveTextAsync(
            "user-1",
            "project-1",
            "knowledge",
            "knowledge-1",
            "raw_upload",
            "素材.txt",
            "第一段\n\n第二段",
            CancellationToken.None);

        Assert.Equal("knowledge", document.SourceType);
        Assert.Equal(2, await db.ContentChunks.CountAsync());
        Assert.Equal(2, await db.ContentVectorPoints.CountAsync());
        Assert.All(await db.ContentVectorPoints.ToListAsync(), p => Assert.Equal("pending", p.IndexStatus));
    }

    [Fact]
    public async Task SaveOrReplaceTextAsync_PreservesOldVersionsAndMarksOnlyLatestActive()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new NovelAgentDbContext(options);
        var service = new ContentDocumentService(db);

        var first = await service.SaveOrReplaceTextAsync(
            "user-1",
            "project-1",
            "chapter",
            "chapter-1",
            "chapter_body",
            "第一章",
            "旧正文",
            CancellationToken.None);
        var second = await service.SaveOrReplaceTextAsync(
            "user-1",
            "project-1",
            "chapter",
            "chapter-1",
            "chapter_body",
            "第一章",
            "新正文",
            CancellationToken.None);

        var documents = await db.ContentDocuments
            .Where(d => d.SourceType == "chapter" && d.SourceId == "chapter-1")
            .OrderBy(d => d.Version)
            .ToListAsync();

        Assert.Equal(2, documents.Count);
        Assert.Equal(first.Id, documents[0].Id);
        Assert.Equal("archived", documents[0].Status);
        Assert.Equal(1, documents[0].Version);
        Assert.Equal(second.Id, documents[1].Id);
        Assert.Equal("active", documents[1].Status);
        Assert.Equal(2, documents[1].Version);
        Assert.Equal("新正文", await service.GetTextAsync("user-1", "chapter", "chapter-1", "chapter_body"));
    }

    [Fact]
    public async Task GetTextAsync_UsesProjectScopeWhenSourceIdsOverlapAcrossProjects()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new NovelAgentDbContext(options);
        var service = new ContentDocumentService(db);

        await service.SaveOrReplaceTextAsync(
            "user-1",
            "project-a",
            "chapter",
            "chapter-001",
            "chapter_body",
            "第一章",
            "A 项目正文",
            CancellationToken.None);
        await service.SaveOrReplaceTextAsync(
            "user-1",
            "project-b",
            "chapter",
            "chapter-001",
            "chapter_body",
            "第一章",
            "B 项目正文",
            CancellationToken.None);

        Assert.Equal("A 项目正文", await service.GetTextAsync(
            "user-1",
            "project-a",
            "chapter",
            "chapter-001",
            "chapter_body"));
        Assert.Equal("B 项目正文", await service.GetTextAsync(
            "user-1",
            "project-b",
            "chapter",
            "chapter-001",
            "chapter_body"));
    }

    [Fact]
    public async Task GetTextAsync_ReturnsMemoryCachedTextWithoutReadingSqlite()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new NovelAgentDbContext(options);
        var redis = new Mock<IDistributedCacheService>(MockBehavior.Strict);
        var memory = new Mock<IMemoryCacheService>(MockBehavior.Strict);
        memory.Setup(x => x.Get<string>(CacheKey)).Returns("cached text");

        var service = new ContentDocumentService(
            db,
            redis.Object,
            memory.Object,
            NullLogger<ContentDocumentService>.Instance);

        var result = await service.GetTextAsync("user-1", "project-1", "knowledge", "knowledge-1", "raw_upload");

        Assert.Equal("cached text", result);
        redis.Verify(x => x.GetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetTextAsync_ReturnsRedisCachedTextAndRefreshesMemory()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new NovelAgentDbContext(options);
        var redis = new Mock<IDistributedCacheService>(MockBehavior.Strict);
        var memory = new Mock<IMemoryCacheService>(MockBehavior.Strict);
        memory.Setup(x => x.Get<string>(CacheKey)).Returns((string?)null);
        memory.Setup(x => x.Set(CacheKey, "redis text", It.IsAny<TimeSpan>()));
        redis.Setup(x => x.GetAsync<string>(CacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync("redis text");

        var service = new ContentDocumentService(
            db,
            redis.Object,
            memory.Object,
            NullLogger<ContentDocumentService>.Instance);

        var result = await service.GetTextAsync("user-1", "project-1", "knowledge", "knowledge-1", "raw_upload");

        Assert.Equal("redis text", result);
        memory.Verify(x => x.Set(CacheKey, "redis text", It.IsAny<TimeSpan>()), Times.Once);
    }

    [Fact]
    public async Task SaveTextAsync_RefreshesHotTextCaches()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new NovelAgentDbContext(options);
        var redis = new Mock<IDistributedCacheService>(MockBehavior.Strict);
        var memory = new Mock<IMemoryCacheService>(MockBehavior.Strict);
        memory.Setup(x => x.Set(CacheKey, "正文", It.IsAny<TimeSpan>()));
        redis.Setup(x => x.SetAsync(CacheKey, "正文", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new ContentDocumentService(
            db,
            redis.Object,
            memory.Object,
            NullLogger<ContentDocumentService>.Instance);

        await service.SaveTextAsync(
            "user-1",
            "project-1",
            "knowledge",
            "knowledge-1",
            "raw_upload",
            "素材.txt",
            "正文");

        memory.Verify(x => x.Set(CacheKey, "正文", It.IsAny<TimeSpan>()), Times.Once);
        redis.Verify(x => x.SetAsync(CacheKey, "正文", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveTextAsync_BumpsProjectContentMemoryVersion()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new NovelAgentDbContext(options);
        var versions = new Mock<IAgentMemoryVersionService>();
        versions.Setup(x => x.BumpAsync("user-1", "project-1", null, "content", It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var service = new ContentDocumentService(
            db,
            versions: versions.Object);

        await service.SaveTextAsync(
            "user-1",
            "project-1",
            "knowledge",
            "knowledge-1",
            "raw_upload",
            "素材.txt",
            "正文");

        versions.Verify(x => x.BumpAsync("user-1", "project-1", null, "content", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeferredSave_PublishesNoCacheUntilCommittedChangesAreAnnounced()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new NovelAgentDbContext(options);
        var redis = new Mock<IDistributedCacheService>(MockBehavior.Strict);
        var memory = new Mock<IMemoryCacheService>(MockBehavior.Strict);
        memory.Setup(x => x.RemoveByPrefix("content:text:user-1:project-1:"));
        redis.Setup(x => x.RemoveByPrefixAsync(
                "content:text:user-1:project-1:",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var service = new ContentDocumentService(
            db,
            redis.Object,
            memory.Object,
            NullLogger<ContentDocumentService>.Instance);

        await service.SaveTextDeferredAsync(
            "user-1",
            "project-1",
            "chapter",
            "chapter-1",
            "chapter_body",
            "第一章",
            "事务内正文");

        memory.Verify(x => x.Set(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);
        redis.Verify(x => x.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);

        await service.PublishCommittedProjectChangesAsync("user-1", "project-1");

        memory.Verify(x => x.RemoveByPrefix("content:text:user-1:project-1:"), Times.Once);
        redis.Verify(x => x.RemoveByPrefixAsync("content:text:user-1:project-1:", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteBySourceAsync_RemovesHotTextCaches()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new NovelAgentDbContext(options);
        var redis = new Mock<IDistributedCacheService>(MockBehavior.Strict);
        var memory = new Mock<IMemoryCacheService>(MockBehavior.Strict);
        memory.Setup(x => x.Set(CacheKey, "正文", It.IsAny<TimeSpan>()));
        memory.Setup(x => x.Remove(CacheKey));
        redis.Setup(x => x.SetAsync(CacheKey, "正文", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        redis.Setup(x => x.RemoveAsync(CacheKey, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new ContentDocumentService(
            db,
            redis.Object,
            memory.Object,
            NullLogger<ContentDocumentService>.Instance);
        await service.SaveTextAsync(
            "user-1",
            "project-1",
            "knowledge",
            "knowledge-1",
            "raw_upload",
            "素材.txt",
            "正文");

        await service.DeleteBySourceAsync("user-1", "knowledge", "knowledge-1", "raw_upload");

        memory.Verify(x => x.Remove(CacheKey), Times.Once);
        redis.Verify(x => x.RemoveAsync(CacheKey, It.IsAny<CancellationToken>()), Times.Once);
    }
}
