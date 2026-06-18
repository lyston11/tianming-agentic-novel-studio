using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Support;

public class WebGeneratedContentServiceTests
{
    [Fact]
    public async Task SaveChapterAsync_StoresCurrentDocumentPointerOnChapter()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddSingleton<ICurrentUserService>(new FixedCurrentUserService("user-1"));
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
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
            await db.SaveChangesAsync();
        }

        var service = new WebGeneratedContentService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ICurrentUserService>(),
            "project-1",
            vectorStore: null,
            embeddingService: null);

        await service.SaveChapterAsync("chapter-001", "章节正文");

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var chapter = await verifyDb.Chapters.SingleAsync(c => c.Id == "chapter-001");
        Assert.False(string.IsNullOrWhiteSpace(chapter.CurrentDocumentId));
        Assert.True(await verifyDb.ContentDocuments.AnyAsync(d =>
            d.Id == chapter.CurrentDocumentId &&
            d.SourceType == "chapter" &&
            d.SourceId == chapter.Id &&
            d.DocumentRole == "chapter_body" &&
            d.Status == "active"));
    }

    [Fact]
    public async Task SaveChapterAsync_MarksChapterContentVectorPointsCompleted()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddSingleton<ICurrentUserService>(new FixedCurrentUserService("user-1"));
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
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
            await db.SaveChangesAsync();
        }

        var vectorStore = new RecordingVectorStore();
        var service = new WebGeneratedContentService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ICurrentUserService>(),
            "project-1",
            vectorStore,
            new FixedEmbeddingService());

        await service.SaveChapterAsync("chapter-001", "章节正文 用于 向量化");

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var chapter = await verifyDb.Chapters.SingleAsync(c => c.Id == "chapter-001");
        var point = await verifyDb.ContentVectorPoints.SingleAsync(p => p.DocumentId == chapter.CurrentDocumentId);
        var vector = Assert.Single(vectorStore.Upserted);
        Assert.Equal("completed", point.IndexStatus);
        Assert.Equal(vector.Id, point.QdrantPointId);
        Assert.True(Guid.TryParse(point.QdrantPointId, out _));
        Assert.Equal(nameof(FixedEmbeddingService), point.VectorModel);
        Assert.NotNull(point.IndexedAt);
    }

    [Fact]
    public async Task SaveChapterAsync_RecomputesProjectWordCountFromChapters()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddSingleton<ICurrentUserService>(new FixedCurrentUserService("user-1"));
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
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
                WordCount = 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.Chapters.Add(new Chapter
            {
                Id = "chapter-000",
                ProjectId = "project-1",
                Title = "已有章节",
                ChapterNumber = 0,
                Status = "committed",
                WordCount = 12,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var service = new WebGeneratedContentService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ICurrentUserService>(),
            "project-1",
            vectorStore: null,
            embeddingService: null);

        await service.SaveChapterAsync("chapter-001", "章节正文");

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var project = await verifyDb.NovelProjects.SingleAsync(p => p.Id == "project-1");
        var chapterWordSum = await verifyDb.Chapters
            .Where(c => c.ProjectId == "project-1")
            .SumAsync(c => c.WordCount);
        Assert.Equal(chapterWordSum, project.WordCount);
    }

    [Fact]
    public async Task SaveChapterAsync_RecomputesProjectWordCountWithSqlitePersistence()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddSingleton<ICurrentUserService>(new FixedCurrentUserService("user-1"));
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
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
                WordCount = 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.Chapters.Add(new Chapter
            {
                Id = "chapter-000",
                ProjectId = "project-1",
                Title = "已有章节",
                ChapterNumber = 0,
                Status = "committed",
                WordCount = 12,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var service = new WebGeneratedContentService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ICurrentUserService>(),
            "project-1",
            vectorStore: null,
            embeddingService: null);

        await service.SaveChapterAsync("chapter-001", "章节正文");

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var project = await verifyDb.NovelProjects.SingleAsync(p => p.Id == "project-1");
        var chapterWordSum = await verifyDb.Chapters
            .Where(c => c.ProjectId == "project-1")
            .SumAsync(c => c.WordCount);
        Assert.Equal(chapterWordSum, project.WordCount);
    }

    [Fact]
    public async Task SaveChapterAsync_MarksGeneratedChapterCommittedAndProjectWriting()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddSingleton<ICurrentUserService>(new FixedCurrentUserService("user-1"));
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
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
                Status = "Planning",
                WordCount = 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var service = new WebGeneratedContentService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ICurrentUserService>(),
            "project-1",
            vectorStore: null,
            embeddingService: null);

        await service.SaveChapterAsync("chapter-001", "章节正文");

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var chapter = await verifyDb.Chapters.SingleAsync(c => c.Id == "chapter-001");
        var project = await verifyDb.NovelProjects.SingleAsync(p => p.Id == "project-1");
        Assert.Equal("committed", chapter.Status);
        Assert.Equal("Writing", project.Status);
        Assert.True(project.WordCount > 0);
    }

    [Fact]
    public async Task SaveChapterAsync_UsesProjectScopedChapterIdWhenTechnicalIdExistsInAnotherProject()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddSingleton<ICurrentUserService>(new FixedCurrentUserService("user-1"));
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new User
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.NovelProjects.AddRange(
                new NovelProject
                {
                    Id = "project-old",
                    UserId = "user-1",
                    Title = "旧项目",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new NovelProject
                {
                    Id = "project-new",
                    UserId = "user-1",
                    Title = "新项目",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            db.Chapters.Add(new Chapter
            {
                Id = "chapter-001",
                ProjectId = "project-old",
                Title = "旧项目第一章",
                ChapterNumber = 1,
                Status = "committed",
                WordCount = 8,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var service = new WebGeneratedContentService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ICurrentUserService>(),
            "project-new",
            vectorStore: null,
            embeddingService: null);

        await service.SaveChapterAsync("chapter-001", "第一章：潮汐骨塔\n林澈拧紧雾灯铜环。");

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var newChapter = await verifyDb.Chapters.SingleAsync(c => c.ProjectId == "project-new");
        Assert.NotEqual("chapter-001", newChapter.Id);
        Assert.Contains("project-new", newChapter.Id);
        Assert.Equal(1, newChapter.ChapterNumber);
        Assert.Equal("第一章：潮汐骨塔", newChapter.Title);
        Assert.Equal("committed", newChapter.Status);
    }

    [Fact]
    public async Task SaveChapterAsync_BindsChapterToCanonicalVolumeAndReadableTitle()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddSingleton<ICurrentUserService>(new FixedCurrentUserService("user-1"));
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
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
            db.VolumeArcs.Add(new VolumeArc
            {
                Id = "arc-1",
                UserId = "user-1",
                ProjectId = "project-1",
                VolumeNumber = 1,
                VolumeTitle = "第一卷：黑雨觉醒",
                TargetChapters = 6,
                CurrentChapters = 0,
                Status = "planned",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var service = new WebGeneratedContentService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ICurrentUserService>(),
            "project-1",
            vectorStore: null,
            embeddingService: null);

        await service.SaveChapterAsync("chapter-001", "第一章：维修站的黑雨\n陈默拧紧旧扳手。");

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var chapter = await verifyDb.Chapters.Include(c => c.Volume).SingleAsync(c => c.Id == "chapter-001");
        var volume = await verifyDb.Volumes.SingleAsync(v => v.ProjectId == "project-1" && v.VolumeNumber == 1);
        var arc = await verifyDb.VolumeArcs.SingleAsync(a => a.Id == "arc-1");

        Assert.Equal("第一章：维修站的黑雨", chapter.Title);
        Assert.Equal(volume.Id, chapter.VolumeId);
        Assert.Equal("第一卷：黑雨觉醒", volume.Title);
        Assert.Equal(1, arc.CurrentChapters);
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
            Task.FromResult(new[] { 0.1f, 0.2f, 0.3f });

        public Task<float[][]> EncodeBatchAsync(IReadOnlyList<string> texts, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default) =>
            Task.FromResult(texts.Select(_ => new[] { 0.1f, 0.2f, 0.3f }).ToArray());

        public bool IsModelReady() => true;
        public void ReleaseSession() { }
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
