using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Modules.ProjectData.Models.Tracking;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class ProductionPlotPointRecallServiceTests
{
    [Fact]
    public void Constructor_FailsFastWhenProjectHasNoDatabaseScope()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ProductionPlotPointRecallService((IServiceScopeFactory)null!, "project-1"));

        Assert.Equal("scopeFactory", exception.ParamName);
    }

    [Fact]
    public async Task SearchRecentAsync_ReadsRelatedPlotPointsFromProductionEvents()
    {
        await using var db = CreateDb();
        SeedProjectsAndChapters(db);
        IProductionTruthStore truthStore = new ProductionTruthStore(db);
        IProductionEventWriter writer = new ProductionEventWriter(truthStore);

        await RecordChangesAsync(writer, "user-1", "project-1", "run-1", "vol1_ch001", new ChapterChanges
        {
            NewPlotPoints =
            {
                new PlotPointChange
                {
                    Context = "林澈第一次发现旧邮路会在雨后亮起",
                    Keywords = new List<string> { "旧邮路", "邮徽" },
                    InvolvedCharacters = new List<string> { "林澈" },
                    Importance = "critical",
                    Storyline = "main"
                }
            }
        });
        await RecordChangesAsync(writer, "user-1", "project-1", "run-2", "vol1_ch004", new ChapterChanges
        {
            NewPlotPoints =
            {
                new PlotPointChange
                {
                    Context = "未来章节不能泄露给第三章",
                    Keywords = new List<string> { "邮徽" },
                    InvolvedCharacters = new List<string> { "林澈" },
                    Importance = "critical",
                    Storyline = "main"
                }
            }
        });
        await RecordChangesAsync(writer, "user-2", "project-2", "run-other", "vol1_ch001", new ChapterChanges
        {
            NewPlotPoints =
            {
                new PlotPointChange
                {
                    Context = "其他项目的邮徽线索不能混进来",
                    Keywords = new List<string> { "邮徽" },
                    InvolvedCharacters = new List<string> { "林澈" },
                    Importance = "critical",
                    Storyline = "main"
                }
            }
        });

        var recall = new ProductionPlotPointRecallService(db, "project-1");

        var result = await recall.SearchRecentAsync(
            "vol1_ch003",
            new HashSet<string> { "林澈" },
            new HashSet<string> { "邮徽" },
            lookbackVolumes: 0);

        var item = Assert.Single(result);
        Assert.Equal("vol1_ch001", item.Chapter);
        Assert.Equal("林澈第一次发现旧邮路会在雨后亮起", item.Context);
        Assert.Equal("critical", item.Importance);
        Assert.Contains("邮徽", item.Keywords);
        Assert.Contains("林澈", item.InvolvedCharacters);
    }

    [Fact]
    public async Task RecordAsync_PersistsChapterChangeEntityWithParseStatus()
    {
        await using var db = CreateDb();
        SeedProjectsAndChapters(db);
        IProductionTruthStore truthStore = new ProductionTruthStore(db);
        IProductionEventWriter writer = new ProductionEventWriter(truthStore);
        var changesJson = """
        {"CharacterStateChanges":[],"ConflictProgress":[],"NewPlotPoints":[{"Context":"林澈发现邮徽会照亮旧邮路","Keywords":["邮徽"],"InvolvedCharacters":["林澈"],"Importance":"critical","Storyline":"main"}],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":null,"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}
        """;

        await RecordChangesAsync(
            writer,
            "user-1",
            "project-1",
            "run-1",
            "vol1_ch001",
            new ChapterChanges
            {
                NewPlotPoints =
                {
                    new PlotPointChange
                    {
                        Context = "林澈发现邮徽会照亮旧邮路",
                        Keywords = new List<string> { "邮徽" },
                        InvolvedCharacters = new List<string> { "林澈" },
                        Importance = "critical",
                        Storyline = "main"
                    }
                }
            },
            changesJson);

        var entity = await db.ChapterChanges.SingleAsync();
        Assert.Equal("user-1", entity.UserId);
        Assert.Equal("project-1", entity.ProjectId);
        Assert.Equal("run-1", entity.RuntimeRunId);
        Assert.Equal("vol1_ch001", entity.ChapterId);
        Assert.Equal("parsed", entity.ParseStatus);
        Assert.False(entity.AppliedToFactSnapshot);
        Assert.Contains("林澈发现邮徽", entity.ChangesJson);
        Assert.Contains("NewPlotPoints", entity.CanonicalChangesJson);
    }

    private static async Task RecordChangesAsync(
        IProductionEventWriter writer,
        string userId,
        string projectId,
        string runId,
        string chapterId,
        ChapterChanges changes,
        string? changesJson = null)
    {
        var recorder = new ProductionChapterChangesRecorder(writer, userId, projectId);
        await recorder.RecordAsync(
            new NovelAgentRun
            {
                RunId = runId,
                TargetChapterId = chapterId
            },
            chapterId,
            changes,
            changesJson,
            CancellationToken.None);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static void SeedProjectsAndChapters(NovelAgentDbContext db)
    {
        db.Users.AddRange(
            new User
            {
                Id = "user-1",
                Username = "user1",
                Email = "user1@example.com",
                PasswordHash = "hash",
                Role = "User",
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            },
            new User
            {
                Id = "user-2",
                Username = "user2",
                Email = "user2@example.com",
                PasswordHash = "hash",
                Role = "User",
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
        db.NovelProjects.AddRange(
            new NovelProject
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "旧邮路",
                Genre = "末世",
                Status = "draft",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new NovelProject
            {
                Id = "project-2",
                UserId = "user-2",
                Title = "别的书",
                Genre = "末世",
                Status = "draft",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        db.Chapters.AddRange(
            NewChapter("vol1_ch001", "project-1", 1),
            NewChapter("vol1_ch004", "project-1", 4),
            NewChapter("vol1_ch001-other", "project-2", 1));
        db.SaveChanges();
    }

    private static Chapter NewChapter(string id, string projectId, int number) => new()
    {
        Id = id,
        ProjectId = projectId,
        Title = $"第{number}章",
        ChapterNumber = number,
        Status = "committed",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
