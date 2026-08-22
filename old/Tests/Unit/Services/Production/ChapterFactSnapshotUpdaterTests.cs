using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class ChapterFactSnapshotUpdaterTests
{
    [Fact]
    public async Task UpdateAsync_AppendsFactExtractionSnapshotAndProductionEvent()
    {
        await using var db = CreateDb();
        await SeedProjectChapterAndVersionAsync(db);
        var truthStore = new ProductionTruthStore(db);
        var updater = new ChapterFactSnapshotUpdater(
            db,
            truthStore,
            new ProductionEventWriter(truthStore));

        await updater.UpdateAsync(
            userId: "user-1",
            projectId: "project-1",
            chapterId: "chapter-001",
            runtimeRunId: "run-1",
            packageId: "pkg-1",
            facts: new ChapterContinuityFacts
            {
                ChapterId = "chapter-001",
                ChapterTitle = "第一章 银蓝邮徽",
                ProtagonistName = "沈砚",
                ProtagonistIdentity = "低阶星渊邮差",
                ProtagonistStatus = "负伤但清醒",
                CurrentLocation = "废弃邮局地下室",
                SystemState = "银蓝邮徽只能辨认旧邮路",
                EquipmentState = "银蓝邮徽完整但不能攻击",
                KeyEvents = { "沈砚觉醒银蓝邮徽" },
                EndingState = "银蓝邮徽指向旧邮路入口",
                NextChapterMustCarry = { "承接旧邮路入口", "银蓝邮徽不能攻击" }
            });

        var snapshot = await db.ProjectFactSnapshots.SingleAsync();
        Assert.Equal("chapter_fact_extraction", snapshot.Source);
        Assert.Equal("chapter-version-1", snapshot.ChapterVersionId);
        using var json = JsonDocument.Parse(snapshot.SnapshotJson);
        var root = json.RootElement;
        Assert.Equal("沈砚", root.GetProperty("protagonistName").GetString());
        Assert.Equal("银蓝邮徽指向旧邮路入口", root.GetProperty("endingState").GetString());
        Assert.Contains(root.GetProperty("nextChapterMustCarry").EnumerateArray(),
            item => item.GetString() == "承接旧邮路入口");

        var evt = await db.ProductionEvents.SingleAsync();
        Assert.Equal("chapter_continuity_facts_extracted", evt.EventType);
        Assert.Equal(NovelAgentProductionStages.FactsPersisted, evt.Stage);
        Assert.Equal("completed", evt.Status);
        Assert.Equal(snapshot.Id, evt.ArtifactId);
        Assert.Contains("沈砚", evt.DataJson);
    }

    [Fact]
    public async Task UpdateAsync_WhenGivenShortChapterId_ResolvesProjectScopedChapterVersion()
    {
        await using var db = CreateDb();
        await SeedProjectChapterAndVersionAsync(
            db,
            chapterId: "project-1-chapter-008",
            chapterNumber: 8,
            versionId: "chapter-version-8");
        var truthStore = new ProductionTruthStore(db);
        var updater = new ChapterFactSnapshotUpdater(
            db,
            truthStore,
            new ProductionEventWriter(truthStore));

        await updater.UpdateAsync(
            userId: "user-1",
            projectId: "project-1",
            chapterId: "chapter-008",
            runtimeRunId: "run-8",
            packageId: "pkg-8",
            facts: new ChapterContinuityFacts
            {
                ChapterId = "chapter-008",
                ChapterTitle = "第八章 深渊猎场",
                ProtagonistName = "沈砚",
                EndingState = "机械运转声从阴影深处传来",
                NextChapterMustCarry = { "承接机械运转声" }
            });

        var snapshot = await db.ProjectFactSnapshots.SingleAsync();
        var evt = await db.ProductionEvents.SingleAsync();
        Assert.Equal("project-1-chapter-008", snapshot.ChapterId);
        Assert.Equal("chapter-version-8", snapshot.ChapterVersionId);
        Assert.Equal("project-1-chapter-008", evt.ChapterId);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static async Task SeedProjectChapterAndVersionAsync(
        NovelAgentDbContext db,
        string chapterId = "chapter-001",
        int chapterNumber = 1,
        string versionId = "chapter-version-1")
    {
        db.Users.Add(new User
        {
            Id = "user-1",
            Username = "author",
            Email = "author@example.com",
            PasswordHash = "hash",
            Role = "author",
            IsActive = true
        });
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-1",
            UserId = "user-1",
            Title = "旧邮路",
            Genre = "末世",
            Status = "draft",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.Chapters.Add(new Chapter
        {
            Id = chapterId,
            ProjectId = "project-1",
            Title = "第一章 银蓝邮徽",
            ChapterNumber = chapterNumber,
            Status = "committed",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.ChapterVersions.Add(new ChapterVersion
        {
            Id = versionId,
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = chapterId,
            ContentDocumentId = "doc-1",
            VersionNumber = 1,
            Title = "第一章 银蓝邮徽",
            WordCount = 3000,
            Status = "committed",
            RuntimeRunId = "run-1",
            PackageId = "pkg-1",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }
}
