using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class ProductionTruthChapterIdentityMigrationServiceTests
{
    [Fact]
    public async Task NormalizeAsync_RebindsHistoricalLogicalChapterIdsToCanonicalChapterIds()
    {
        await using var db = CreateDb();
        SeedHistoricalLogicalChapterTruth(db);
        var service = new ProductionTruthChapterIdentityMigrationService(db);

        var result = await service.NormalizeAsync("project-1");

        Assert.True(result.UpdatedRows >= 8);
        Assert.All(await db.TianmingPackages.ToListAsync(), item => Assert.Equal("project-1-chapter-002", item.ChapterId));
        Assert.All(await db.ProductionEvents.ToListAsync(), item => Assert.Equal("project-1-chapter-002", item.ChapterId));
        Assert.All(await db.ChapterDrafts.ToListAsync(), item => Assert.Equal("project-1-chapter-002", item.ChapterId));
        Assert.All(await db.ChapterChanges.ToListAsync(), item => Assert.Equal("project-1-chapter-002", item.ChapterId));
        Assert.All(await db.GenerationGateReports.ToListAsync(), item => Assert.Equal("project-1-chapter-002", item.ChapterId));
        Assert.All(await db.AgentReviews.ToListAsync(), item => Assert.Equal("project-1-chapter-002", item.ChapterId));
        Assert.All(await db.ProjectFactSnapshots.ToListAsync(), item => Assert.Equal("project-1-chapter-002", item.ChapterId));

        var plan = await db.RevisionPlans.SingleAsync();
        Assert.Equal("project-1-chapter-002", plan.TargetChapterId);
        Assert.Equal("[\"project-1-chapter-002\",\"project-1-chapter-003\"]", plan.AffectedChapterIdsJson);
        Assert.Equal("chapter-002", plan.TargetChapterLogicalId);
        Assert.Equal("第二章", plan.TargetChapterDisplayName);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static void SeedHistoricalLogicalChapterTruth(NovelAgentDbContext db)
    {
        var now = DateTime.UtcNow;
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
            Title = "历史章号迁移测试",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Chapters.AddRange(
            new Chapter
            {
                Id = "project-1-chapter-002",
                ProjectId = "project-1",
                Title = "第二章",
                ChapterNumber = 2,
                Status = "committed",
                CreatedAt = now,
                UpdatedAt = now
            },
            new Chapter
            {
                Id = "project-1-chapter-003",
                ProjectId = "project-1",
                Title = "第三章",
                ChapterNumber = 3,
                Status = "planned",
                CreatedAt = now,
                UpdatedAt = now
            });
        db.TianmingPackages.Add(new TianmingPackage
        {
            Id = "pkg-002",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-002",
            RuntimeRunId = "run-002",
            InputJson = "{}"
        });
        db.ProductionEvents.Add(new ProductionEvent
        {
            Id = "evt-002",
            RuntimeRunId = "run-002",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-002",
            PackageId = "pkg-002",
            EventType = "chapter_context_package_built",
            Stage = "BuildChapterPackage",
            Status = "completed",
            Message = "旧事件",
            ArtifactType = "tianming_package",
            ArtifactId = "pkg-002",
            DataJson = "{}",
            CreatedAt = now
        });
        db.ChapterDrafts.Add(new ChapterDraft
        {
            Id = "draft-002",
            UserId = "user-1",
            ProjectId = "project-1",
            RuntimeRunId = "run-002",
            ChapterId = "chapter-002",
            PackageId = "pkg-002",
            ArtifactId = "draft-artifact-002",
            DraftContent = "第二章正文",
            ContentLength = 4,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.ChapterChanges.Add(new ChapterChange
        {
            Id = "changes-002",
            UserId = "user-1",
            ProjectId = "project-1",
            RuntimeRunId = "run-002",
            ChapterId = "chapter-002",
            PackageId = "pkg-002",
            ChangesJson = "{}",
            CanonicalChangesJson = "{}",
            CreatedAt = now
        });
        db.GenerationGateReports.Add(new GenerationGateReportRecord
        {
            Id = "gate-002",
            UserId = "user-1",
            ProjectId = "project-1",
            RuntimeRunId = "run-002",
            ChapterId = "chapter-002",
            PackageId = "pkg-002",
            ArtifactId = "gate-artifact-002",
            Status = "validated",
            ReportJson = "{}",
            ValidatedAt = now,
            CreatedAt = now
        });
        db.AgentReviews.Add(new AgentReviewRecord
        {
            Id = "review-002",
            UserId = "user-1",
            ProjectId = "project-1",
            RuntimeRunId = "run-002",
            ChapterId = "chapter-002",
            PackageId = "pkg-002",
            ReviewId = "review-artifact-002",
            OverallResult = "Pass",
            ValidationOverallResult = "Pass",
            Summary = "通过",
            ReviewJson = "{}",
            ReviewedAt = now,
            CreatedAt = now
        });
        db.ProjectFactSnapshots.Add(new ProjectFactSnapshot
        {
            Id = "fact-002",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-002",
            VersionNumber = 1,
            SnapshotJson = "{}",
            Source = "chapter_commit",
            CreatedAt = now
        });
        db.RevisionPlans.Add(new RevisionPlan
        {
            Id = "revision-002",
            UserId = "user-1",
            ProjectId = "project-1",
            Source = "user_request",
            PlanType = "chapter_rewrite",
            TargetScope = "chapter",
            TargetChapterId = "chapter-002",
            Status = "ready_for_rebuild",
            RequirementsJson = "[]",
            ContinuityRequirementsJson = "[]",
            ImpactAnalysisJson = "{}",
            AffectedChapterIdsJson = "[\"chapter-002\",\"chapter-003\"]",
            InvalidatedPackageIdsJson = "[]",
            RiskLevel = "medium",
            Recommendation = "重写第二章",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.SaveChanges();
    }
}
