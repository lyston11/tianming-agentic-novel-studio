using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Generated;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class ProductionUnifiedValidationServiceTests
{
    [Fact]
    public async Task ValidateChapterAsync_PassesOnlyWhenChapterMetadataAndCommittedContentExist()
    {
        var provider = BuildProvider(out var db);
        SeedProjectChapter(db);
        var content = new FakeGeneratedContentService();
        content.Save("chapter-001", new string('正', 180));
        var service = new ProductionUnifiedValidationService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            content,
            "user-1",
            "project-1");

        var result = await service.ValidateChapterAsync("chapter-001");

        Assert.Equal("通过", result.OverallResult);
        Assert.False(result.HasErrors);
        Assert.False(result.HasWarnings);
        Assert.Equal("第一章 雨站邮徽", result.ChapterTitle);
        Assert.Equal(1, result.VolumeNumber);
        Assert.Equal(1, result.ChapterNumber);
    }

    [Fact]
    public async Task ValidateChapterAsync_FailsWhenCommittedContentIsMissing()
    {
        var provider = BuildProvider(out var db);
        SeedProjectChapter(db);
        var service = new ProductionUnifiedValidationService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FakeGeneratedContentService(),
            "user-1",
            "project-1");

        var result = await service.ValidateChapterAsync("chapter-001");

        Assert.Equal("失败", result.OverallResult);
        Assert.True(result.HasErrors);
        Assert.Contains(result.IssuesByModule["chapter_content"], issue =>
            issue.Severity == "Error" &&
            issue.Message.Contains("正文未写入", StringComparison.Ordinal));
    }

    private static ServiceProvider BuildProvider(out NovelAgentDbContext db)
    {
        var services = new ServiceCollection();
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(databaseName, databaseRoot));
        var provider = services.BuildServiceProvider();
        db = provider.GetRequiredService<NovelAgentDbContext>();
        return provider;
    }

    private static void SeedProjectChapter(NovelAgentDbContext db)
    {
        db.Users.Add(new User
        {
            Id = "user-1",
            Username = "user1",
            Email = "user1@example.com",
            PasswordHash = "hash",
            Role = "User",
            CreatedAt = DateTime.UtcNow,
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
        db.Volumes.Add(new Volume
        {
            Id = "volume-1",
            ProjectId = "project-1",
            Title = "第一卷 灰塔雨站",
            VolumeNumber = 1
        });
        db.Chapters.Add(new Chapter
        {
            Id = "chapter-001",
            ProjectId = "project-1",
            VolumeId = "volume-1",
            Title = "第一章 雨站邮徽",
            ChapterNumber = 1,
            WordCount = 180,
            Status = "committed",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    private sealed class FakeGeneratedContentService : IGeneratedContentService
    {
        private readonly Dictionary<string, string> _chapters = new(StringComparer.OrdinalIgnoreCase);

        public void Save(string chapterId, string content) => _chapters[chapterId] = content;

        public Task SaveChapterAsync(string chapterId, string content)
        {
            Save(chapterId, content);
            return Task.CompletedTask;
        }

        public Task<string?> GetChapterAsync(string chapterId) =>
            Task.FromResult(_chapters.GetValueOrDefault(chapterId));

        public Task<bool> DeleteChapterAsync(string chapterId) =>
            Task.FromResult(_chapters.Remove(chapterId));

        public bool ChapterExists(string chapterId) => _chapters.ContainsKey(chapterId);

        public Task<List<ChapterInfo>> GetGeneratedChaptersAsync() =>
            Task.FromResult(_chapters.Keys
                .Select(id => new ChapterInfo { Id = id, Title = id })
                .ToList());

        public Task<bool> VolumeExistsAsync(int volumeNumber) => Task.FromResult(volumeNumber == 1);

        public Task<string> GenerateNextChapterIdFromSourceAsync(string sourceChapterId) =>
            Task.FromResult("chapter-002");
    }
}
