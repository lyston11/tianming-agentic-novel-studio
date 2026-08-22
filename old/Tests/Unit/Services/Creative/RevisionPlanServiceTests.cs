using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Creative;
using Xunit;

namespace Tests.Unit.Services.Creative;

public sealed class RevisionPlanServiceTests
{
    [Fact]
    public async Task CreateAsync_StoresCanonicalChapterIdsWhenChapterAlreadyExists()
    {
        await using var db = CreateDb();
        SeedProjectWithChapters(db);
        IRevisionPlanService service = new RevisionPlanService(db);

        var item = await service.CreateAsync(new CreateRevisionPlanRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            SessionId: "session-1",
            RunId: "run-1",
            IdempotencyKey: "revision-plan-canonical-create",
            Source: "user_request",
            PlanType: "chapter_rewrite",
            TargetScope: "chapter",
            TargetVolumeId: string.Empty,
            TargetChapterId: "chapter-002",
            CreativeIntentId: string.Empty,
            KnowledgeConflictReportId: string.Empty,
            Status: "accepted",
            RequirementsJson: "[\"第二章改为怪物围攻\"]",
            ContinuityRequirementsJson: "[\"承接第一章银蓝邮徽\"]",
            ImpactAnalysisJson: "{\"reason\":\"用户要求重写\"}",
            AffectedChapterIdsJson: "[\"chapter-002\",\"chapter-003\"]",
            InvalidatedPackageIdsJson: "[]",
            RiskLevel: "high",
            Recommendation: "重写第二章并检查第三章承接。"));

        Assert.NotNull(item);
        Assert.Equal("project-1-chapter-002", item!.TargetChapterId);
        Assert.Equal("chapter-002", item.TargetChapterLogicalId);
        Assert.Equal("第二章", item.TargetChapterDisplayName);
        Assert.Equal("[\"project-1-chapter-002\",\"project-1-chapter-003\"]", item.AffectedChapterIdsJson);

        var saved = await db.RevisionPlans.SingleAsync();
        Assert.Equal("project-1-chapter-002", saved.TargetChapterId);
        Assert.Equal("chapter-002", saved.TargetChapterLogicalId);
        Assert.Equal("第二章", saved.TargetChapterDisplayName);
        Assert.Equal("[\"project-1-chapter-002\",\"project-1-chapter-003\"]", saved.AffectedChapterIdsJson);
        Assert.Contains("\"logicalTargetChapterId\":\"chapter-002\"", saved.ImpactAnalysisJson);
        Assert.Contains("\"logicalAffectedChapterIds\":[\"chapter-002\",\"chapter-003\"]", saved.ImpactAnalysisJson);
    }

    [Fact]
    public async Task QueryAsync_FindsCanonicalRevisionPlanWhenCallerUsesLogicalChapterId()
    {
        await using var db = CreateDb();
        SeedProjectWithChapters(db);
        db.RevisionPlans.Add(new RevisionPlan
        {
            Id = "revision-plan-2",
            UserId = "user-1",
            ProjectId = "project-1",
            Source = "user_request",
            PlanType = "chapter_rewrite",
            TargetScope = "chapter",
            TargetChapterId = "project-1-chapter-002",
            TargetChapterLogicalId = "chapter-002",
            TargetChapterDisplayName = "第二章",
            Status = "ready_for_rebuild",
            RequirementsJson = "[\"重建第二章\"]",
            ContinuityRequirementsJson = "[]",
            ImpactAnalysisJson = "{}",
            AffectedChapterIdsJson = "[\"project-1-chapter-002\"]",
            InvalidatedPackageIdsJson = "[\"pkg-stale-2\"]",
            RiskLevel = "medium",
            Recommendation = "重建第二章。",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        IRevisionPlanService service = new RevisionPlanService(db);

        var result = await service.QueryAsync(new QueryRevisionPlansRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            Status: "ready_for_rebuild",
            TargetChapterId: "chapter-002",
            Limit: 20));

        Assert.Equal("project-1-chapter-002", result.TargetChapterId);
        var item = Assert.Single(result.Items);
        Assert.Equal("revision-plan-2", item.Id);
        Assert.Equal("project-1-chapter-002", item.TargetChapterId);
        Assert.Equal("chapter-002", item.TargetChapterLogicalId);
        Assert.Equal("第二章", item.TargetChapterDisplayName);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static void SeedProjectWithChapters(NovelAgentDbContext db)
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
            Title = "修订计划身份测试项目",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.Chapters.AddRange(
            new Chapter
            {
                Id = "project-1-chapter-001",
                ProjectId = "project-1",
                Title = "第一章",
                ChapterNumber = 1,
                Status = "committed",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Chapter
            {
                Id = "project-1-chapter-002",
                ProjectId = "project-1",
                Title = "第二章",
                ChapterNumber = 2,
                Status = "committed",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Chapter
            {
                Id = "project-1-chapter-003",
                ProjectId = "project-1",
                Title = "第三章",
                ChapterNumber = 3,
                Status = "planned",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        db.SaveChanges();
    }
}
