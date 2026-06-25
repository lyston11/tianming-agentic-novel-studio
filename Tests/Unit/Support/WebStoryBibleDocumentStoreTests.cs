using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services;
using TM.Services.Modules.ProjectData.Models.Tracking;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Support;

public class WebStoryBibleDocumentStoreTests
{
    [Fact]
    public async Task SaveAsync_StoresFullAgentRunOutputAsContentDocumentAndKeepsRunRowLightweight()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddSingleton(Mock.Of<IDistributedCacheService>());
        services.AddSingleton(CreateMemoryCache());
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

        var store = new WebStoryBibleDocumentStore(
            provider.GetRequiredService<IServiceScopeFactory>(),
            "user-1",
            "project-1");
        var run = new NovelAgentRun
        {
            RunId = "run-1",
            Intent = NovelAgentIntent.PlanChapter,
            Status = NovelAgentRunStatus.Completed,
            TargetChapterId = "chapter-001",
            UserGoal = "写第一章",
            DraftArtifact = new ChapterDraftArtifact
            {
                ArtifactId = "draft-1",
                DraftContent = new string('甲', 200)
            },
            GateReport = new GenerationGateReport { Status = "validated" },
            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTime.UtcNow
        };

        await store.SaveAsync(new StoryBibleDocument { AgentRuns = { run } });

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var savedRun = await verifyDb.AgentRuns.SingleAsync(r => r.Id == "run-1");
        Assert.False(string.IsNullOrWhiteSpace(savedRun.OutputDocumentId));
        Assert.DoesNotContain("DraftContent", savedRun.OutputData ?? string.Empty, StringComparison.Ordinal);

        var content = await new ContentDocumentService(verifyDb).GetTextAsync(
            "user-1",
            "project-1",
            "agent_run",
            "run-1",
            "run_output");
        Assert.Contains("\"draftArtifact\"", content, StringComparison.Ordinal);
        Assert.Contains("draft-1", content, StringComparison.Ordinal);
    }

    private static IMemoryCacheService CreateMemoryCache()
    {
        var memory = new Mock<IMemoryCacheService>();
        memory.Setup(x => x.GetOrSetAsync(
                It.IsAny<string>(),
                It.IsAny<Func<Task<StoryBibleDocument?>>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, Func<Task<StoryBibleDocument?>> factory, TimeSpan _, CancellationToken _) => factory());
        return memory.Object;
    }
}
