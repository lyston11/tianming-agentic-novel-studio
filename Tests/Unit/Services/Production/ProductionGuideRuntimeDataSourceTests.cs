using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class ProductionGuideRuntimeDataSourceTests
{
    [Fact]
    public async Task BuildsContentGuideFromPlannedChapterAgentRunWhenChapterRowDoesNotExistYet()
    {
        await using var db = CreateDb();
        SeedProjectGraphWithoutChapters(db);
        SeedPlanChapterRun(db);

        var source = new ProductionGuideRuntimeDataSource(db, "user-1", "project-1");

        var guide = await source.LoadGuideAsync<ContentGuide>(GuideRuntimeDataKeys.ContentGuide);
        Assert.True(guide.Chapters.TryGetValue("chapter-001", out var chapter));
        Assert.NotNull(chapter);

        Assert.Equal("chapter-001", chapter!.ChapterId);
        Assert.Equal(1, chapter.ChapterNumber);
        Assert.Equal("银蓝邮徽开局", chapter.Title);
        Assert.Equal("旧邮路", chapter.Volume);
        Assert.Equal("第一卷目标", chapter.ChapterTheme);
        Assert.Equal("chapter-001:plan", chapter.ContextIds.ChapterPlanId);
        Assert.Equal("chapter-001:blueprint", chapter.ContextIds.ChapterBlueprint);
        Assert.Empty(chapter.ContextIds.PreviousChapter);
    }

    [Fact]
    public async Task LoadsContentGuideAndDesignItemsFromCurrentProjectDatabase()
    {
        await using var db = CreateDb();
        SeedProjectGraph(db);

        var source = new ProductionGuideRuntimeDataSource(db, "user-1", "project-1");

        var guide = await source.LoadGuideAsync<ContentGuide>(GuideRuntimeDataKeys.ContentGuide);
        var characters = await source.LoadItemsAsync<CharacterRulesData>(GuideRuntimeDataKeys.Characters);
        var volumeDesigns = await source.LoadItemsAsync<VolumeDesignData>(GuideRuntimeDataKeys.VolumeDesigns);

        Assert.True(guide.Chapters.ContainsKey("vol1_ch002"));
        Assert.Equal("vol1_ch001", guide.Chapters["vol1_ch002"].ContextIds.PreviousChapter);
        Assert.Equal("旧邮路", guide.Chapters["vol1_ch002"].Volume);
        Assert.Contains("char-lin", guide.Chapters["vol1_ch002"].ContextIds.Characters);
        Assert.DoesNotContain("char-other", guide.Chapters["vol1_ch002"].ContextIds.Characters);

        var character = Assert.Single(characters);
        Assert.Equal("char-lin", character.Id);
        Assert.Equal("林澈", character.Name);
        Assert.Equal("主角", character.CharacterType);

        var volume = Assert.Single(volumeDesigns);
        Assert.Equal("volume-1", volume.Id);
        Assert.Equal(1, volume.VolumeNumber);
        Assert.Equal("旧邮路", volume.VolumeTitle);
        Assert.Equal("第一卷目标", volume.StageGoal);
    }

    [Fact]
    public async Task ContentGuide_ExposesLogicalChapterAliasForDatabaseChapterIds()
    {
        await using var db = CreateDb();
        SeedProjectGraph(db);
        var source = new ProductionGuideRuntimeDataSource(db, "user-1", "project-1");

        var guide = await source.LoadGuideAsync<ContentGuide>(GuideRuntimeDataKeys.ContentGuide);

        Assert.True(guide.Chapters.TryGetValue("chapter-001", out var alias));
        Assert.NotNull(alias);
        Assert.Equal("vol1_ch001", alias!.ChapterId);
        Assert.Equal(1, alias.ChapterNumber);
        Assert.Equal("银蓝邮徽", alias.Title);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static void SeedProjectGraph(NovelAgentDbContext db)
    {
        db.Users.AddRange(
            NewUser("user-1", "user1"),
            NewUser("user-2", "user2"));
        db.NovelProjects.AddRange(
            NewProject("project-1", "user-1", "旧邮路"),
            NewProject("project-2", "user-2", "别的书"));
        db.VolumeArcs.Add(new VolumeArc
        {
            Id = "volume-1",
            UserId = "user-1",
            ProjectId = "project-1",
            VolumeNumber = 1,
            VolumeTitle = "旧邮路",
            VolumeTheme = "记忆代价",
            TargetChapters = 6,
            Act1Setup = "第一卷目标",
            KeyEvents = "灰塔封站",
            MajorConflict = "邮差工会追捕",
            Status = "in_progress",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.Chapters.AddRange(
            NewChapter("vol1_ch001", "project-1", 1, "银蓝邮徽"),
            NewChapter("vol1_ch002", "project-1", 2, "旧邮路开启"),
            NewChapter("vol1_ch001-other", "project-2", 1, "别的主角"));
        db.Characters.AddRange(
            NewCharacter("char-lin", "user-1", "project-1", "林澈", "protagonist"),
            NewCharacter("char-other", "user-2", "project-2", "沈烁", "protagonist"));
        db.SaveChanges();
    }

    private static void SeedProjectGraphWithoutChapters(NovelAgentDbContext db)
    {
        db.Users.Add(NewUser("user-1", "user1"));
        db.NovelProjects.Add(NewProject("project-1", "user-1", "旧邮路"));
        db.VolumeArcs.Add(new VolumeArc
        {
            Id = "volume-1",
            UserId = "user-1",
            ProjectId = "project-1",
            VolumeNumber = 1,
            VolumeTitle = "旧邮路",
            VolumeTheme = "第一卷目标",
            TargetChapters = 6,
            Act1Setup = "第一卷目标",
            KeyEvents = "灰塔封站",
            MajorConflict = "邮差工会追捕",
            Status = "in_progress",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    private static void SeedPlanChapterRun(NovelAgentDbContext db)
    {
        var run = new NovelAgentRun
        {
            RunId = "run-plan-chapter-001",
            UserGoal = "第一章写沈砚获得银蓝邮徽并进入旧邮路。",
            Intent = NovelAgentIntent.PlanChapter,
            Status = NovelAgentRunStatus.Planning,
            TargetChapterId = "chapter-001",
            ChapterBrief = new ChapterCreativeBrief
            {
                ChapterId = "chapter-001",
                SelectedCandidateTitle = "银蓝邮徽开局",
                RecommendedCandidateTitle = "银蓝邮徽开局",
                CoreIdea = "沈砚在第七码头接到被篡改旧邮路的第一封信。",
                ConflictMove = "无址会制造断信区，逼迫沈砚进入邮路废墟。",
                CharacterChoice = "沈砚选择用邮徽辨认路线而不是把它当攻击能力。",
                CostOrConsequence = "邮路入口暴露，下一章必须承接断信区追击。",
                VolumeBeatRole = "第一章开卷钩子"
            },
            CreatedAt = DateTime.UtcNow.AddMinutes(-2),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-1)
        };
        var json = JsonSerializer.Serialize(run);
        db.ContentDocuments.Add(new ContentDocument
        {
            Id = "doc-run-plan-chapter-001",
            UserId = "user-1",
            ProjectId = "project-1",
            SourceType = "agent_run",
            SourceId = run.RunId,
            DocumentRole = "run_output",
            Title = "Agent Run run-plan-chapter-001",
            ContentHash = "hash",
            Status = "active",
            CreatedAt = DateTime.UtcNow.AddMinutes(-2),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-1)
        });
        db.ContentChunks.Add(new ContentChunk
        {
            Id = "chunk-run-plan-chapter-001",
            DocumentId = "doc-run-plan-chapter-001",
            ChunkIndex = 0,
            ChunkText = json,
            TokenCount = json.Length,
            CharStart = 0,
            CharEnd = json.Length,
            ContentHash = "hash"
        });
        db.AgentRuns.Add(new AgentRun
        {
            Id = run.RunId,
            UserId = "user-1",
            ProjectId = "project-1",
            RunType = NovelAgentIntent.PlanChapter.ToString(),
            TargetChapterId = "chapter-001",
            Status = NovelAgentRunStatus.Planning.ToString(),
            OutputDocumentId = "doc-run-plan-chapter-001",
            CreatedAt = DateTime.UtcNow.AddMinutes(-2),
            StartedAt = DateTime.UtcNow.AddMinutes(-2),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-1)
        });
        db.SaveChanges();
    }

    private static User NewUser(string id, string username) => new()
    {
        Id = id,
        Username = username,
        Email = $"{username}@example.com",
        PasswordHash = "hash",
        Role = "User",
        CreatedAt = DateTime.UtcNow,
        IsActive = true
    };

    private static NovelProject NewProject(string id, string userId, string title) => new()
    {
        Id = id,
        UserId = userId,
        Title = title,
        Genre = "末世",
        Status = "draft",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static Chapter NewChapter(string id, string projectId, int number, string title) => new()
    {
        Id = id,
        ProjectId = projectId,
        Title = title,
        ChapterNumber = number,
        Status = "draft",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static Character NewCharacter(string id, string userId, string projectId, string name, string role) => new()
    {
        Id = id,
        UserId = userId,
        ProjectId = projectId,
        Name = name,
        Role = role,
        Status = "active",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
