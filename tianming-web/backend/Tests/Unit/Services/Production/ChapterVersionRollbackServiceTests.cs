using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class ChapterVersionRollbackServiceTests
{
    [Fact]
    public async Task RollbackAsync_SwitchesCurrentDocumentInvalidatesDownstreamPackagesAndQueuesPostWork()
    {
        await using var db = CreateDb();
        var contentDocuments = new ContentDocumentService(db);
        var truthStore = new ProductionTruthStore(db);
        var eventWriter = new ProductionEventWriter(truthStore);
        await SeedAsync(db, truthStore);
        IChapterVersionRollbackService service = new ChapterVersionRollbackService(
            db,
            truthStore,
            eventWriter,
            new OutputArtifactRecorder(eventWriter));

        var result = await service.RollbackAsync(new RollbackChapterVersionRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "chapter-001",
            TargetVersionId: "version-1",
            RuntimeRunId: "run-rollback-1",
            Reason: "用户确认回滚到第一版。"));

        Assert.True(result.Success, result.Message);
        Assert.Equal("version-1", result.CurrentVersionId);
        Assert.Equal(new[] { "pkg-chapter-001-v2", "pkg-chapter-002-v1" }, result.InvalidatedPackageIds);

        var chapter = await db.Chapters.SingleAsync(chapter => chapter.Id == "chapter-001");
        Assert.Equal("doc-v1", chapter.CurrentDocumentId);
        Assert.Equal("第一章：旧邮徽", chapter.Title);
        Assert.Equal(3200, chapter.WordCount);

        var activeContent = await contentDocuments.GetTextAsync(
            "user-1",
            "project-1",
            "chapter",
            "chapter-001",
            "chapter_body");
        Assert.Contains("第一版正文", activeContent);
        Assert.DoesNotContain("第二版正文", activeContent);

        Assert.Equal("active", (await db.ContentDocuments.SingleAsync(doc => doc.Id == "doc-v1")).Status);
        Assert.Equal("archived", (await db.ContentDocuments.SingleAsync(doc => doc.Id == "doc-v2")).Status);
        Assert.Equal("completed", (await db.TianmingPackages.SingleAsync(package => package.Id == "pkg-chapter-001-v1")).Status);
        Assert.Equal("stale", (await db.TianmingPackages.SingleAsync(package => package.Id == "pkg-chapter-001-v2")).Status);
        Assert.Equal("stale", (await db.TianmingPackages.SingleAsync(package => package.Id == "pkg-chapter-002-v1")).Status);

        var productionEvent = await db.ProductionEvents.SingleAsync(evt => evt.EventType == "chapter_version_rolled_back");
        Assert.Equal("version_rollback", productionEvent.Stage);
        Assert.Equal("completed", productionEvent.Status);
        Assert.Equal("version-1", productionEvent.ArtifactId);
        Assert.Contains("pkg-chapter-002-v1", productionEvent.DataJson);

        var outputArtifact = await db.ProductionEvents.SingleAsync(evt =>
            evt.EventType == OutputArtifactRecorder.EventType &&
            evt.ArtifactType == "chapter_version_rollback" &&
            evt.ArtifactId == "version-1");
        Assert.Equal("version_rollback", outputArtifact.Stage);
        Assert.Equal("completed", outputArtifact.Status);
        Assert.Contains("已回滚到 v1", outputArtifact.Message);
        Assert.Contains("chapter_version_rolled_back", outputArtifact.DataJson);
        Assert.Contains("小说书城", outputArtifact.DataJson);

        var outboxEvents = await db.OutboxEvents.OrderBy(evt => evt.EventType).ToListAsync();
        var factOutbox = outboxEvents.Single(evt =>
            evt.EventType == "extract_chapter_continuity_facts" &&
            evt.AggregateId == "chapter-001" &&
            evt.RuntimeRunId == "run-rollback-1");
        using (var payload = JsonDocument.Parse(factOutbox.PayloadJson))
        {
            Assert.Contains("第一版正文", payload.RootElement.GetProperty("committedContent").GetString());
        }
        Assert.Contains(outboxEvents, evt =>
            evt.EventType == "index_chapter_content" &&
            evt.AggregateType == "chapter_version" &&
            evt.AggregateId == "version-1");
    }

    [Fact]
    public async Task RollbackAsync_ReusesResultForSameIdempotencyKeyWithoutDuplicatingSideEffects()
    {
        await using var db = CreateDb();
        var truthStore = new ProductionTruthStore(db);
        var eventWriter = new ProductionEventWriter(truthStore);
        await SeedAsync(db, truthStore);
        IChapterVersionRollbackService service = new ChapterVersionRollbackService(
            db,
            truthStore,
            eventWriter,
            new OutputArtifactRecorder(eventWriter));

        var request = new RollbackChapterVersionRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "chapter-001",
            TargetVersionId: "version-1",
            RuntimeRunId: "run-rollback-idempotent",
            Reason: "用户确认回滚到第一版。",
            IdempotencyKey: "rollback-key-001");

        var first = await service.RollbackAsync(request);
        var second = await service.RollbackAsync(request);

        Assert.True(first.Success, first.Message);
        Assert.True(second.Success, second.Message);
        Assert.Equal(first.CurrentVersionId, second.CurrentVersionId);
        Assert.Equal(first.CurrentDocumentId, second.CurrentDocumentId);
        Assert.Equal(first.InvalidatedPackageIds, second.InvalidatedPackageIds);

        Assert.Equal(1, await db.ProductionEvents.CountAsync(evt =>
            evt.EventType == "chapter_version_rolled_back" &&
            evt.ArtifactId == "version-1" &&
            evt.DataJson != null &&
            evt.DataJson.Contains("rollback-key-001")));
        Assert.Equal(1, await db.ProductionEvents.CountAsync(evt =>
            evt.EventType == OutputArtifactRecorder.EventType &&
            evt.ArtifactType == "chapter_version_rollback" &&
            evt.ArtifactId == "version-1"));
        Assert.Equal(1, await db.OutboxEvents.CountAsync(evt =>
            evt.EventType == "index_chapter_content" &&
            evt.AggregateType == "chapter_version" &&
            evt.AggregateId == "version-1"));
        Assert.Equal(1, await db.OutboxEvents.CountAsync(evt =>
            evt.EventType == "extract_chapter_continuity_facts" &&
            evt.AggregateType == "chapter" &&
            evt.AggregateId == "chapter-001"));
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static async Task SeedAsync(
        NovelAgentDbContext db,
        IProductionTruthStore truthStore)
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
            Title = "回滚测试书",
            Status = "Writing",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.Chapters.AddRange(
            new Chapter
            {
                Id = "chapter-001",
                ProjectId = "project-1",
                Title = "第一章：旧邮徽",
                ChapterNumber = 1,
                Status = "committed",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Chapter
            {
                Id = "chapter-002",
                ProjectId = "project-1",
                Title = "第二章：邮车亮灯",
                ChapterNumber = 2,
                Status = "committed",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        AddChapterDocument(
            db,
            id: "doc-v1",
            status: "archived",
            content: "第一章：旧邮徽\n第一版正文：沈砚捡起银蓝邮徽。");
        db.ChapterVersions.Add(new ChapterVersion
        {
            Id = "version-1",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-001",
            ContentDocumentId = "doc-v1",
            VersionNumber = 1,
            Title = "第一章：旧邮徽",
            WordCount = 3200,
            Status = "committed",
            RuntimeRunId = "run-chapter-001-v1",
            PackageId = "pkg-chapter-001-v1",
            GateReportJson = "{\"status\":\"passed\"}",
            AgentReviewJson = "{\"decision\":\"accept\"}",
            CreatedAt = DateTime.UtcNow.AddMinutes(-10)
        });

        AddChapterDocument(
            db,
            id: "doc-v2",
            status: "active",
            content: "第一章：旧邮徽\n第二版正文：沈砚直接开走旧邮车。");
        db.ChapterVersions.Add(new ChapterVersion
        {
            Id = "version-2",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-001",
            ContentDocumentId = "doc-v2",
            VersionNumber = 2,
            Title = "第一章：旧邮徽重写版",
            WordCount = 3600,
            Status = "committed",
            RuntimeRunId = "run-chapter-001-v2",
            PackageId = "pkg-chapter-001-v2",
            GateReportJson = "{\"status\":\"passed\"}",
            AgentReviewJson = "{\"decision\":\"accept\"}",
            CreatedAt = DateTime.UtcNow
        });
        var chapter = await db.Chapters.SingleAsync(item => item.Id == "chapter-001");
        chapter.CurrentDocumentId = "doc-v2";
        chapter.Title = "第一章：旧邮徽重写版";
        chapter.WordCount = 3600;
        chapter.Status = "committed";
        chapter.UpdatedAt = DateTime.UtcNow;

        db.TianmingPackages.AddRange(
            Package("pkg-chapter-001-v1", "chapter-001", "run-chapter-001-v1"),
            Package("pkg-chapter-001-v2", "chapter-001", "run-chapter-001-v2"),
            Package("pkg-chapter-002-v1", "chapter-002", "run-chapter-002-v1"));
        await db.SaveChangesAsync();
    }

    private static void AddChapterDocument(
        NovelAgentDbContext db,
        string id,
        string status,
        string content)
    {
        db.ContentDocuments.Add(new ContentDocument
        {
            Id = id,
            UserId = "user-1",
            ProjectId = "project-1",
            SourceType = "chapter",
            SourceId = "chapter-001",
            DocumentRole = "chapter_body",
            Title = "第一章：旧邮徽",
            MimeType = "text/plain",
            ContentHash = id,
            Version = id.EndsWith("v1", StringComparison.OrdinalIgnoreCase) ? 1 : 2,
            Status = status,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.ContentChunks.Add(new ContentChunk
        {
            Id = $"chunk-{id}",
            DocumentId = id,
            ChunkIndex = 0,
            ChunkText = content,
            TokenCount = content.Length,
            CharStart = 0,
            CharEnd = content.Length,
            ContentHash = $"chunk-{id}"
        });
    }

    private static TianmingPackage Package(string id, string chapterId, string runtimeRunId) => new()
    {
        Id = id,
        UserId = "user-1",
        ProjectId = "project-1",
        ChapterId = chapterId,
        RuntimeRunId = runtimeRunId,
        PackageKind = "chapter_generation",
        Status = "completed",
        InputJson = "{}",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
