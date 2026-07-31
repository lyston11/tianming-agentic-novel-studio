using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Support;
using TM.Services.Modules.ProjectData.Interfaces;
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
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
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
            "project-1");

        await service.SaveChapterAsync("chapter-001", "章节正文");

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var chapter = await verifyDb.Chapters.SingleAsync(c => c.Id == "project-1-chapter-001");
        Assert.False(string.IsNullOrWhiteSpace(chapter.CurrentDocumentId));
        Assert.True(await verifyDb.ContentDocuments.AnyAsync(d =>
            d.Id == chapter.CurrentDocumentId &&
            d.SourceType == "chapter" &&
            d.SourceId == chapter.Id &&
            d.DocumentRole == "chapter_body" &&
            d.Status == "active"));
    }

    [Fact]
    public async Task SaveChapterAsync_LeavesChapterContentVectorPointsPendingForOutboxIndexing()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
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
            "project-1");

        await service.SaveChapterAsync("chapter-001", "章节正文 用于 向量化");

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var chapter = await verifyDb.Chapters.SingleAsync(c => c.Id == "project-1-chapter-001");
        var point = await verifyDb.ContentVectorPoints.SingleAsync(p => p.DocumentId == chapter.CurrentDocumentId);
        Assert.Equal("pending", point.IndexStatus);
        Assert.Equal("pending", point.VectorModel);
        Assert.Null(point.IndexedAt);
        Assert.StartsWith("content_", point.QdrantPointId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteChapterAsync_EnqueuesChapterContentVectorCleanup()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
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
            "project-1");

        await service.SaveChapterAsync("chapter-001", "章节正文");
        var deleted = await service.DeleteChapterAsync("chapter-001");

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var deleteEvent = await verifyDb.OutboxEvents.SingleAsync(e => e.EventType == "delete_chapter_content");
        Assert.True(deleted);
        Assert.Equal("chapter", deleteEvent.AggregateType);
        Assert.Equal("project-1-chapter-001", deleteEvent.AggregateId);
        Assert.Equal("project-1", deleteEvent.ProjectId);
        Assert.Equal("pending", deleteEvent.Status);
    }

    [Fact]
    public async Task SaveChapterAsync_RecomputesProjectWordCountFromChapters()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
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
            "project-1");

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
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
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
            "project-1");

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
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
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
            "project-1");

        await service.SaveChapterAsync("chapter-001", "章节正文");

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var chapter = await verifyDb.Chapters.SingleAsync(c => c.Id == "project-1-chapter-001");
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
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
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
            "project-new");

        await service.SaveChapterAsync("chapter-001", "第一章：潮汐骨塔\n林澈拧紧雾灯铜环。");

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var newChapter = await verifyDb.Chapters.SingleAsync(c => c.ProjectId == "project-new");
        Assert.Equal("project-new-chapter-001", newChapter.Id);
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
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
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
            "project-1");

        await service.SaveChapterAsync("chapter-001", "第一章：维修站的黑雨\n陈默拧紧旧扳手。");

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var chapter = await verifyDb.Chapters.Include(c => c.Volume).SingleAsync(c => c.Id == "project-1-chapter-001");
        var volume = await verifyDb.Volumes.SingleAsync(v => v.ProjectId == "project-1" && v.VolumeNumber == 1);
        var arc = await verifyDb.VolumeArcs.SingleAsync(a => a.Id == "arc-1");

        Assert.Equal("第一章：维修站的黑雨", chapter.Title);
        Assert.Equal(volume.Id, chapter.VolumeId);
        Assert.Equal("第一卷：黑雨觉醒", volume.Title);
        Assert.Equal(1, arc.CurrentChapters);
    }

    [Fact]
    public async Task SaveChapterAsync_StripsMarkdownDecorationFromReadableTitle()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
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
            "project-1");

        await service.SaveChapterAsync("chapter-001", "# **第一章：邮徽觉醒**\n沈砚握住银蓝邮徽。");

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var chapter = await verifyDb.Chapters.SingleAsync(c => c.Id == "project-1-chapter-001");

        Assert.Equal("第一章：邮徽觉醒", chapter.Title);
    }

    [Fact]
    public async Task SaveChapterAsync_PreservesExistingVolumeTitleWhenNoVolumeArcExists()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
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
            db.Volumes.Add(new Volume
            {
                Id = "volume-1",
                ProjectId = "project-1",
                VolumeNumber = 1,
                Title = "第一卷：黑雨旧邮路"
            });
            await db.SaveChangesAsync();
        }

        var service = new WebGeneratedContentService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ICurrentUserService>(),
            "project-1");

        await service.SaveChapterAsync("chapter-001", "第一章：维修站的黑雨\n沈砚拧紧旧扳手。");

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var chapter = await verifyDb.Chapters.SingleAsync(c => c.Id == "project-1-chapter-001");
        var volume = await verifyDb.Volumes.SingleAsync(v => v.Id == "volume-1");

        Assert.Equal("volume-1", chapter.VolumeId);
        Assert.Equal("第一卷：黑雨旧邮路", volume.Title);
    }

    [Fact]
    public async Task SaveChapterAsync_ReusesExistingProjectChapterWhenCalledWithLogicalChapterId()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
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
            db.Chapters.Add(new Chapter
            {
                Id = "project-1-chapter-007",
                ProjectId = "project-1",
                Title = "第七章：旧标题",
                ChapterNumber = 7,
                Status = "draft",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var service = new WebGeneratedContentService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ICurrentUserService>(),
            "project-1");

        await service.SaveChapterAsync("chapter-007", "第七章：旧货场的“缝”与追猎的阴影\n沈砚进入旧货场。");

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var chapter = await verifyDb.Chapters.SingleAsync(c => c.ProjectId == "project-1");
        Assert.Equal("project-1-chapter-007", chapter.Id);
        Assert.Equal(7, chapter.ChapterNumber);
        Assert.Equal("第七章：旧货场的“缝”与追猎的阴影", chapter.Title);
        Assert.Equal("committed", chapter.Status);
    }

    [Fact]
    public async Task SaveChapterAsync_PreservesFullReadableHeadingSubtitle()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
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
            "project-1");

        await service.SaveChapterAsync("chapter-007", "第七章：旧货场的“缝”与追猎的阴影\n沈砚进入旧货场。");

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var chapter = await verifyDb.Chapters.SingleAsync(c => c.Id == "project-1-chapter-007");
        var document = await verifyDb.ContentDocuments.SingleAsync(d => d.Id == chapter.CurrentDocumentId);
        Assert.Equal("第七章：旧货场的“缝”与追猎的阴影", chapter.Title);
        Assert.Equal(chapter.Title, document.Title);
        Assert.Equal(chapter.Id, document.SourceId);
    }

    [Fact]
    public async Task SaveChapterAsync_UsesExplicitTitleWhenCommittedBodyHasNoHeading()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
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
            "project-1");

        await service.SaveChapterAsync(
            "chapter-006",
            "沈砚沿着锈蚀管汇向主管道深处走去。",
            "第六章：主管道深处的残响");

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var chapter = await verifyDb.Chapters.SingleAsync(c => c.Id == "project-1-chapter-006");
        var document = await verifyDb.ContentDocuments.SingleAsync(d => d.Id == chapter.CurrentDocumentId);
        var version = await verifyDb.ChapterVersions.SingleAsync(v => v.ChapterId == chapter.Id);

        Assert.Equal("第六章：主管道深处的残响", chapter.Title);
        Assert.Equal(chapter.Title, document.Title);
        Assert.Equal(chapter.Title, version.Title);
    }

    [Fact]
    public async Task SaveChapterAsync_CreatesChapterVersionsAndOutboxEvents()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
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
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var service = new WebGeneratedContentService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ICurrentUserService>(),
            "project-1");

        await service.SaveChapterAsync("chapter-001", "第一章：黑雨来临\n陈默第一次看见蓝磷骨光。");
        await service.SaveChapterAsync("chapter-001", "第一章：黑雨来临\n陈默重新确认蓝磷骨光来自旧邮徽。");

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var chapter = await verifyDb.Chapters.SingleAsync(c => c.Id == "project-1-chapter-001");
        var versions = await verifyDb.ChapterVersions
            .Where(v => v.ChapterId == chapter.Id)
            .OrderBy(v => v.VersionNumber)
            .ToListAsync();
        var outboxEvents = await verifyDb.OutboxEvents
            .Where(e => e.ProjectId == "project-1" && e.AggregateType == "chapter_version")
            .OrderBy(e => e.CreatedAt)
            .ToListAsync();

        Assert.Equal(2, versions.Count);
        Assert.Equal(new[] { 1, 2 }, versions.Select(v => v.VersionNumber).ToArray());
        Assert.Equal("committed", versions[0].Status);
        Assert.Equal("第一章：黑雨来临", versions[1].Title);
        Assert.Equal(chapter.CurrentDocumentId, versions[1].ContentDocumentId);
        Assert.Equal(2, outboxEvents.Count);
        Assert.All(outboxEvents, evt =>
        {
            Assert.Equal("index_chapter_content", evt.EventType);
            Assert.Equal("pending", evt.Status);
        });
        Assert.Equal(versions.Select(v => v.Id).ToArray(), outboxEvents.Select(e => e.AggregateId).ToArray());
    }

    [Fact]
    public async Task SaveChapterAsync_RollsBackChapterDocumentAndVersionWhenOutboxInsertFails()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options => options.UseSqlite(connection));
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddSingleton<ICurrentUserService>(new FixedCurrentUserService("user-1"));
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new User
            {
                Id = "user-1", Username = "author", Email = "author@example.com", PasswordHash = "hash", Role = "author"
            });
            db.NovelProjects.Add(new NovelProject
            {
                Id = "project-1", UserId = "user-1", Title = "测试项目", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER fail_outbox BEFORE INSERT ON outbox_events BEGIN SELECT RAISE(ABORT, 'forced outbox failure'); END;");
        }

        var service = new WebGeneratedContentService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ICurrentUserService>(),
            "project-1");

        await Assert.ThrowsAnyAsync<Exception>(() =>
            service.SaveChapterAsync("chapter-001", "第一章：事务测试\n正文不应部分提交。"));

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        Assert.Empty(await verifyDb.Chapters.ToListAsync());
        Assert.Empty(await verifyDb.ContentDocuments.ToListAsync());
        Assert.Empty(await verifyDb.ChapterVersions.ToListAsync());
    }

    [Fact]
    public async Task AtomicCommit_WritesChapterVersionAndPostCommitOutboxesTogether()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options => options.UseSqlite(connection));
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IProductionTruthStore, ProductionTruthStore>();
        services.AddSingleton<ICurrentUserService>(new FixedCurrentUserService("user-1"));
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new User { Id = "user-1", Username = "author", Email = "author@example.com", PasswordHash = "hash", Role = "author" });
            db.NovelProjects.Add(new NovelProject { Id = "project-1", UserId = "user-1", Title = "测试项目" });
            await db.SaveChangesAsync();
        }

        IAtomicGeneratedChapterCommitService service = new WebGeneratedContentService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ICurrentUserService>(),
            "project-1");
        await service.SaveChapterAtomicallyAsync(
            "chapter-001",
            "第一章：黑雨\n正文。",
            "第一章：黑雨",
            new[]
            {
                new GeneratedChapterOutboxWrite("run-1", "extract_chapter_continuity_facts", "chapter", "chapter-001", "{}"),
                new GeneratedChapterOutboxWrite("run-1", "finalize_chapter_commit_metadata", "chapter", "chapter-001", "{}"),
            });

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        Assert.Single(await verifyDb.ChapterVersions.ToListAsync());
        var outboxes = await verifyDb.OutboxEvents.OrderBy(evt => evt.EventType).ToListAsync();
        Assert.Equal(3, outboxes.Count);
        Assert.Contains(outboxes, evt => evt.EventType == "index_chapter_content");
        Assert.Contains(outboxes, evt => evt.EventType == "extract_chapter_continuity_facts");
        Assert.Contains(outboxes, evt => evt.EventType == "finalize_chapter_commit_metadata");
        var metadataOutbox = outboxes.Single(evt => evt.EventType == "finalize_chapter_commit_metadata");
        using var payload = JsonDocument.Parse(metadataOutbox.PayloadJson);
        Assert.Equal("user-1", payload.RootElement.GetProperty("userId").GetString());
        Assert.Equal("project-1", payload.RootElement.GetProperty("projectId").GetString());
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

}
