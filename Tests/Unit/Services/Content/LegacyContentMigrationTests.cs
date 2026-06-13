using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Scripts;
using TM.Web.NovelAgentWeb.Services.Content;
using Xunit;

namespace Tests.Unit.Services.Content;

public class LegacyContentMigrationTests
{
    [Fact]
    public async Task RunAsync_MigratesChapterContentPathIntoContentDocument()
    {
        var root = Path.Combine(Path.GetTempPath(), "legacy-content-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var chapterPath = Path.Combine(root, "chapter_001.md");
        await File.WriteAllTextAsync(chapterPath, "第一章正文");

        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new NovelAgentDbContext(options);

        db.Users.Add(new User
        {
            Id = "user-1",
            Username = "u",
            Email = "u@example.com",
            PasswordHash = "h",
            Role = "author"
        });
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-1",
            UserId = "user-1",
            Title = "书"
        });
        db.Chapters.Add(new Chapter
        {
            Id = "chapter-1",
            ProjectId = "project-1",
            Title = "第一章",
            ContentPath = chapterPath,
            ChapterNumber = 1
        });
        await db.SaveChangesAsync();

        var content = new ContentDocumentService(db);
        var migrated = await MigrateLegacyContentToSqlite.RunAsync(
            db,
            content,
            root,
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Equal(1, migrated.ChaptersMigrated);
        var document = await db.ContentDocuments.SingleAsync(x =>
            x.SourceType == "chapter" && x.SourceId == "chapter-1");
        Assert.Equal("chapter_body", document.DocumentRole);
        Assert.Equal(1, await db.ContentChunks.CountAsync(x => x.DocumentId == document.Id));
    }
}
