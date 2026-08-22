using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class BookValidationServiceTests
{
    [Fact]
    public async Task ValidateAsync_ReadsFactSnapshotStoredWithShortChapterId()
    {
        await using var db = CreateDb();
        var contentDocuments = new ContentDocumentService(db);
        await SeedCommittedChapterAsync(db, contentDocuments);
        db.ProjectFactSnapshots.Add(new ProjectFactSnapshot
        {
            Id = "fact-short-chapter-001",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-001",
            VersionNumber = 3,
            Source = "llm_fact_writer",
            SnapshotJson = JsonSerializer.Serialize(new
            {
                protagonistName = "沈砚",
                endingState = "沈砚带着银蓝邮徽冲进黑雨",
                nextChapterMustCarry = new[] { "下一章必须承接银蓝邮徽仍在沈砚手中" }
            }),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var service = new BookValidationService(db, contentDocuments);

        var report = await service.ValidateAsync(new BookValidationRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            StartChapterNumber: 1,
            EndChapterNumber: 1,
            IncludeBodyPreview: false));

        var chapter = Assert.Single(report.Chapters);
        Assert.Equal("fact-short-chapter-001", chapter.FactSnapshotId);
        Assert.Equal(3, chapter.FactSnapshotVersion);
        Assert.Equal("沈砚", chapter.ProtagonistName);
        Assert.DoesNotContain(report.Issues, issue => issue.Code == "missing_fact_snapshot");
    }

    [Fact]
    public async Task ValidateAsync_UsesFullBodyForCarryContinuityWhenPreviewIsDisabled()
    {
        await using var db = CreateDb();
        var contentDocuments = new ContentDocumentService(db);
        await SeedCommittedChaptersAsync(db, contentDocuments);
        db.ProjectFactSnapshots.AddRange(
            new ProjectFactSnapshot
            {
                Id = "fact-chapter-001",
                UserId = "user-1",
                ProjectId = "project-1",
                ChapterId = "project-1-chapter-001",
                VersionNumber = 1,
                Source = "llm_fact_writer",
                SnapshotJson = JsonSerializer.Serialize(new
                {
                    protagonistName = "沈砚",
                    endingState = "沈砚带着银蓝邮徽冲进黑雨",
                    nextChapterMustCarry = new[] { "银蓝邮徽仍在沈砚手中" }
                }),
                CreatedAt = DateTime.UtcNow.AddMinutes(-2)
            },
            new ProjectFactSnapshot
            {
                Id = "fact-chapter-002",
                UserId = "user-1",
                ProjectId = "project-1",
                ChapterId = "project-1-chapter-002",
                VersionNumber = 1,
                Source = "llm_fact_writer",
                SnapshotJson = JsonSerializer.Serialize(new
                {
                    protagonistName = "沈砚",
                    endingState = "沈砚抵达废弃分拣站，黑雨仍在身后逼近",
                    nextChapterMustCarry = Array.Empty<string>()
                }),
                CreatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();
        var service = new BookValidationService(db, contentDocuments);

        var report = await service.ValidateAsync(new BookValidationRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            StartChapterNumber: 1,
            EndChapterNumber: 2,
            IncludeBodyPreview: false));

        Assert.Equal(2, report.Chapters.Count);
        Assert.DoesNotContain(report.Issues, issue => issue.Code == "carry_not_reflected");
        Assert.Equal("validated", report.OverallStatus);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static async Task SeedCommittedChapterAsync(
        NovelAgentDbContext db,
        IContentDocumentService contentDocuments)
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
            Title = "短章号事实快照测试",
            Status = "Writing",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.Chapters.Add(new Chapter
        {
            Id = "project-1-chapter-001",
            ProjectId = "project-1",
            Title = "第一章：银蓝邮徽",
            ChapterNumber = 1,
            Status = "committed",
            WordCount = 1800,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var document = await contentDocuments.SaveOrReplaceTextAsync(
            "user-1",
            "project-1",
            "chapter",
            "project-1-chapter-001",
            "chapter_body",
            "第一章：银蓝邮徽",
            "第一章：银蓝邮徽\n沈砚在废弃邮局获得银蓝邮徽，结尾被迫带着银蓝邮徽冲进黑雨。",
            CancellationToken.None);
        var chapter = await db.Chapters.SingleAsync(item => item.Id == "project-1-chapter-001");
        chapter.CurrentDocumentId = document.Id;
        await db.SaveChangesAsync();
    }

    private static async Task SeedCommittedChaptersAsync(
        NovelAgentDbContext db,
        IContentDocumentService contentDocuments)
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
            Title = "整书连续性校验测试",
            Status = "Writing",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.Chapters.AddRange(
            new Chapter
            {
                Id = "project-1-chapter-001",
                ProjectId = "project-1",
                Title = "第一章：银蓝邮徽",
                ChapterNumber = 1,
                Status = "committed",
                WordCount = 1800,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Chapter
            {
                Id = "project-1-chapter-002",
                ProjectId = "project-1",
                Title = "第二章：黑雨分拣站",
                ChapterNumber = 2,
                Status = "committed",
                WordCount = 1900,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var chapterOneDocument = await contentDocuments.SaveOrReplaceTextAsync(
            "user-1",
            "project-1",
            "chapter",
            "project-1-chapter-001",
            "chapter_body",
            "第一章：银蓝邮徽",
            "第一章：银蓝邮徽\n沈砚在废弃邮局获得银蓝邮徽，结尾被迫带着银蓝邮徽冲进黑雨。",
            CancellationToken.None);
        var chapterTwoDocument = await contentDocuments.SaveOrReplaceTextAsync(
            "user-1",
            "project-1",
            "chapter",
            "project-1-chapter-002",
            "chapter_body",
            "第二章：黑雨分拣站",
            "第二章：黑雨分拣站\n沈砚攥紧掌心的银蓝邮徽，确认银蓝邮徽仍在沈砚手中，才沿着旧邮路标记冲进废弃分拣站。",
            CancellationToken.None);

        var chapterOne = await db.Chapters.SingleAsync(item => item.Id == "project-1-chapter-001");
        var chapterTwo = await db.Chapters.SingleAsync(item => item.Id == "project-1-chapter-002");
        chapterOne.CurrentDocumentId = chapterOneDocument.Id;
        chapterTwo.CurrentDocumentId = chapterTwoDocument.Id;
        await db.SaveChangesAsync();
    }
}
