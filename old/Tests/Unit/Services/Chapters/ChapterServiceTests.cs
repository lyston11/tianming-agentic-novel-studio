using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Models.Chapters;
using TM.Web.NovelAgentWeb.Services.Chapters;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Chapters;

public sealed class ChapterServiceTests
{
    [Fact]
    public async Task CreateChapterAsync_WithSameIdempotencyKey_ReturnsExistingChapter()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new NovelAgentDbContext(options);
        await db.Database.EnsureCreatedAsync();
        SeedUserProject(db);
        var service = CreateService(db);
        var request = new CreateChapterRequest
        {
            ProjectId = "project-1",
            Title = "第一章 旧邮路开站",
            ChapterNumber = 1,
            Content = "沈砚在雾城站台捡起银蓝邮徽。",
            Status = "published",
            IdempotencyKey = "chapter-key-001"
        };

        var first = await service.CreateChapterAsync(request, "user-1", isAdmin: false);
        var second = await service.CreateChapterAsync(request, "user-1", isAdmin: false);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("沈砚在雾城站台捡起银蓝邮徽。", second.Content);
        var chapter = await db.Chapters.SingleAsync();
        Assert.Equal("chapter-key-001", chapter.IdempotencyKey);
        Assert.Single(await db.ContentDocuments.ToListAsync());
        Assert.Single(await db.ChapterVersions.ToListAsync());
        Assert.Single(await db.OutboxEvents.ToListAsync());
    }

    [Fact]
    public async Task GetChapterVersionsAsync_ReturnsCurrentVersionAndPackageLineage()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new NovelAgentDbContext(options);
        await db.Database.EnsureCreatedAsync();
        SeedUserProject(db);
        var service = CreateService(db);

        var chapter = await service.CreateChapterAsync(new CreateChapterRequest
        {
            ProjectId = "project-1",
            Title = "第一章 旧邮路开站",
            ChapterNumber = 1,
            Content = "沈砚在雾城站台捡起银蓝邮徽。",
            Status = "published"
        }, "user-1", isAdmin: false);
        await service.UpdateChapterAsync(chapter.Id, new UpdateChapterRequest
        {
            Content = "沈砚在雾城站台捡起银蓝邮徽。\n他看见黑雨里的旧邮车重新亮灯。",
            Status = "published"
        }, "user-1", isAdmin: false);
        var latestVersion = await db.ChapterVersions
            .OrderByDescending(version => version.VersionNumber)
            .FirstAsync();
        db.TianmingPackages.Add(new TianmingPackage
        {
            Id = "pkg-chapter-001-v2",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = chapter.Id,
            RuntimeRunId = "run-chapter-001-v2",
            Status = "completed",
            KnowledgeSnapshotJson = """
                {
                  "rebuiltFromPackageIds": ["pkg-chapter-001-v1"]
                }
                """,
            KernelVersion = "tianming-kernel-v1",
            PromptVersion = "chapter-v1"
        });
        latestVersion.PackageId = "pkg-chapter-001-v2";
        latestVersion.RuntimeRunId = "run-chapter-001-v2";
        latestVersion.GateReportJson = "{\"status\":\"passed\"}";
        latestVersion.AgentReviewJson = "{\"decision\":\"accept\"}";
        await db.SaveChangesAsync();

        var versions = await service.GetChapterVersionsAsync(chapter.Id, "user-1", isAdmin: false);

        Assert.Equal(new[] { 2, 1 }, versions.Select(version => version.VersionNumber).ToArray());
        var current = versions[0];
        Assert.True(current.IsCurrent);
        Assert.Equal("pkg-chapter-001-v2", current.PackageId);
        Assert.Equal("run-chapter-001-v2", current.RuntimeRunId);
        Assert.Equal("tianming-kernel-v1", current.KernelVersion);
        Assert.Equal(new[] { "pkg-chapter-001-v1" }, current.RebuiltFromPackageIds);
        Assert.Contains("旧邮车重新亮灯", current.ContentPreview);
        Assert.False(versions[1].IsCurrent);
    }

    [Fact]
    public async Task CompareChapterVersionsAsync_ReturnsParagraphDiffAndLineage()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new NovelAgentDbContext(options);
        await db.Database.EnsureCreatedAsync();
        SeedUserProject(db);
        var service = CreateService(db);

        var chapter = await service.CreateChapterAsync(new CreateChapterRequest
        {
            ProjectId = "project-1",
            Title = "第一章 旧邮路开站",
            ChapterNumber = 1,
            Content = "沈砚在雾城站台捡起银蓝邮徽。\n黑雨刚刚落下。",
            Status = "published"
        }, "user-1", isAdmin: false);
        var leftVersion = await db.ChapterVersions.SingleAsync();
        await service.UpdateChapterAsync(chapter.Id, new UpdateChapterRequest
        {
            Content = "沈砚在雾城站台捡起银蓝邮徽。\n黑雨里，旧邮车重新亮灯。",
            Status = "published"
        }, "user-1", isAdmin: false);
        var rightVersion = await db.ChapterVersions
            .OrderByDescending(version => version.VersionNumber)
            .FirstAsync();
        db.TianmingPackages.Add(new TianmingPackage
        {
            Id = "pkg-chapter-001-v2",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = chapter.Id,
            RuntimeRunId = "run-chapter-001-v2",
            Status = "completed",
            KnowledgeSnapshotJson = """
                {
                  "rebuiltFromPackageIds": ["pkg-chapter-001-v1"]
                }
                """,
            KernelVersion = "tianming-kernel-v1",
            PromptVersion = "chapter-v1"
        });
        rightVersion.PackageId = "pkg-chapter-001-v2";
        rightVersion.RuntimeRunId = "run-chapter-001-v2";
        await db.SaveChangesAsync();

        var comparison = await service.CompareChapterVersionsAsync(
            chapter.Id,
            leftVersion.Id,
            rightVersion.Id,
            "user-1",
            isAdmin: false);

        Assert.Equal(chapter.Id, comparison.ChapterId);
        Assert.Equal(1, comparison.Left.VersionNumber);
        Assert.Equal(2, comparison.Right.VersionNumber);
        Assert.Equal("pkg-chapter-001-v2", comparison.Right.PackageId);
        Assert.Equal(new[] { "pkg-chapter-001-v1" }, comparison.Right.RebuiltFromPackageIds);
        Assert.Contains(comparison.DiffBlocks, block =>
            block.Kind == "unchanged" &&
            block.LeftText.Contains("银蓝邮徽") &&
            block.RightText.Contains("银蓝邮徽"));
        Assert.Contains(comparison.DiffBlocks, block =>
            block.Kind == "changed" &&
            block.LeftText.Contains("黑雨刚刚落下") &&
            block.RightText.Contains("旧邮车重新亮灯"));
        Assert.Contains("v1 -> v2", comparison.Summary);
    }

    [Fact]
    public async Task CompareChapterVersionsAsync_ReturnsProductionAlignmentEvidence()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new NovelAgentDbContext(options);
        await db.Database.EnsureCreatedAsync();
        SeedUserProject(db);
        var service = CreateService(db);

        var chapter = await service.CreateChapterAsync(new CreateChapterRequest
        {
            ProjectId = "project-1",
            Title = "第二章 黑雨围城",
            ChapterNumber = 2,
            Content = "沈砚沿着旧邮路逃出雾城。",
            Status = "published"
        }, "user-1", isAdmin: false);
        var leftVersion = await db.ChapterVersions.SingleAsync();
        await service.UpdateChapterAsync(chapter.Id, new UpdateChapterRequest
        {
            Content = "沈砚沿着旧邮路逃出雾城。\n黑雨怪群压向站台，他第一次用银蓝邮徽打开逃生路线。",
            Status = "published"
        }, "user-1", isAdmin: false);
        var rightVersion = await db.ChapterVersions
            .OrderByDescending(version => version.VersionNumber)
            .FirstAsync();
        db.TianmingPackages.Add(new TianmingPackage
        {
            Id = "pkg-chapter-002-v2",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = chapter.Id,
            RuntimeRunId = "run-chapter-002-v2",
            Status = "completed",
            KnowledgeSnapshotJson = """
                {
                  "acceptedCreativeIntents": [
                    {
                      "intentId": "intent-fight-upgrade",
                      "normalizedIntent": "第二章改成打怪升级，男主用邮徽逃生",
                      "targetScope": "chapter",
                      "targetChapterId": "chapter-002",
                      "impactLevel": "chapter_rewrite",
                      "source": "chat"
                    }
                  ],
                  "sourceRevisionPlans": [
                    {
                      "revisionPlanId": "revision-plan-002",
                      "planType": "chapter_rewrite",
                      "targetScope": "chapter",
                      "targetChapterId": "chapter-002",
                      "status": "executed",
                      "affectedChapterIdsJson": "[\"chapter-002\"]",
                      "invalidatedPackageIdsJson": "[\"pkg-chapter-002-v1\"]",
                      "riskLevel": "medium",
                      "recommendation": "重写第二章为怪物围攻和邮徽逃生"
                    }
                  ],
                  "rebuiltFromPackageIds": ["pkg-chapter-002-v1"]
                }
                """,
            KernelVersion = "tianming-kernel-v1",
            PromptVersion = "chapter-v2"
        });
        rightVersion.PackageId = "pkg-chapter-002-v2";
        rightVersion.RuntimeRunId = "run-chapter-002-v2";
        rightVersion.AgentReviewJson = """
            {
              "decision": "accept",
              "checks": [
                {
                  "key": "user_intent_fit",
                  "name": "用户意图",
                  "status": "pass",
                  "message": "已转为打怪升级主冲突",
                  "evidence": ["怪群压向站台", "银蓝邮徽打开逃生路线"]
                }
              ]
            }
            """;
        await db.SaveChangesAsync();

        var comparison = await service.CompareChapterVersionsAsync(
            chapter.Id,
            leftVersion.Id,
            rightVersion.Id,
            "user-1",
            isAdmin: false);

        Assert.Equal("pkg-chapter-002-v2", comparison.ProductionAlignment.RightPackageId);
        Assert.Equal(new[] { "pkg-chapter-002-v1" }, comparison.ProductionAlignment.RebuiltFromPackageIds);
        var intent = Assert.Single(comparison.ProductionAlignment.AcceptedCreativeIntents);
        Assert.Equal("intent-fight-upgrade", intent.IntentId);
        Assert.Contains("打怪升级", intent.NormalizedIntent);
        var plan = Assert.Single(comparison.ProductionAlignment.SourceRevisionPlans);
        Assert.Equal("revision-plan-002", plan.RevisionPlanId);
        Assert.Equal("executed", plan.Status);
        Assert.Equal(new[] { "pkg-chapter-002-v1" }, plan.InvalidatedPackageIds);
        Assert.Equal("accept", comparison.ProductionAlignment.AgentReviewDecision);
        var check = Assert.Single(comparison.ProductionAlignment.AgentReviewChecks);
        Assert.Equal("user_intent_fit", check.Key);
        Assert.Equal("pass", check.Status);
        Assert.Contains(check.Evidence, item => item.Contains("银蓝邮徽", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetChapterByIdAsync_ReturnsProductionChainsForLibraryArchive()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new NovelAgentDbContext(options);
        await db.Database.EnsureCreatedAsync();
        SeedUserProject(db);
        var service = CreateService(db);

        var chapter = await service.CreateChapterAsync(new CreateChapterRequest
        {
            ProjectId = "project-1",
            Title = "第八章 银蓝邮徽复燃",
            ChapterNumber = 8,
            Content = "沈砚在黑雨里重新点亮银蓝邮徽。",
            Status = "published"
        }, "user-1", isAdmin: false);
        var version = await db.ChapterVersions.SingleAsync();
        db.TianmingPackages.Add(new TianmingPackage
        {
            Id = "pkg-chapter-008",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = chapter.Id,
            RuntimeRunId = "run-chapter-008",
            Status = "completed",
            PackageKind = "chapter_generation",
            KnowledgeSnapshotJson = """
                {
                  "sourceRevisionPlans": [
                    {
                      "revisionPlanId": "revision-plan-008",
                      "status": "executed",
                      "recommendation": "承接银蓝邮徽复燃"
                    }
                  ]
                }
                """,
            KernelVersion = "tianming-kernel-v1",
            PromptVersion = "chapter-v3"
        });
        version.PackageId = "pkg-chapter-008";
        version.RuntimeRunId = "run-chapter-008";
        db.ProjectFactSnapshots.Add(new ProjectFactSnapshot
        {
            Id = "fact-chapter-008",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = chapter.Id,
            ChapterVersionId = version.Id,
            VersionNumber = 3,
            Source = "chapter_commit",
            SnapshotJson = "{\"nextChapterMustCarry\":[\"沈砚已经重新点亮银蓝邮徽\"]}"
        });
        db.RevisionPlans.Add(new RevisionPlan
        {
            Id = "revision-plan-008",
            UserId = "user-1",
            ProjectId = "project-1",
            RuntimeRunId = "run-chapter-008",
            Source = "user_request",
            PlanType = "chapter_rewrite",
            TargetScope = "chapter",
            TargetChapterId = chapter.Id,
            Status = "executed",
            RequirementsJson = "[\"承接银蓝邮徽复燃\"]",
            AffectedChapterIdsJson = $"[\"{chapter.Id}\"]",
            InvalidatedPackageIdsJson = "[\"pkg-chapter-008-v1\"]",
            RiskLevel = "medium",
            Recommendation = "承接银蓝邮徽复燃并提交第八章。"
        });
        db.OutboxEvents.Add(new OutboxEvent
        {
            Id = "outbox-chapter-008",
            UserId = "user-1",
            ProjectId = "project-1",
            RuntimeRunId = "run-chapter-008",
            EventType = "index_chapter_content",
            AggregateType = "chapter_version",
            AggregateId = version.Id,
            PayloadJson = "{}",
            Status = "completed",
            Attempts = 1,
            CompletedAt = DateTime.UtcNow
        });
        db.ProductionEvents.Add(new ProductionEvent
        {
            Id = "evt-chapter-008-commit",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-008",
            PackageId = "pkg-chapter-008",
            RuntimeRunId = "run-chapter-008",
            EventType = "chapter_committed",
            Stage = "commit",
            Status = "completed",
            Message = "第八章已入书城",
            ArtifactType = "chapter_version",
            ArtifactId = version.Id,
            DataJson = """
                {
                  "factSnapshotId": "fact-chapter-008",
                  "factSnapshotVersion": 3
                }
                """
        });
        db.ProductionEvents.Add(new ProductionEvent
        {
            Id = "evt-chapter-008-outbox",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-008",
            PackageId = "pkg-chapter-008",
            RuntimeRunId = "run-chapter-008",
            EventType = "outbox_completed",
            Stage = "index_outbox",
            Status = "completed",
            Message = "第八章后台索引完成。",
            ArtifactType = "outbox_event",
            ArtifactId = "outbox-chapter-008",
            DataJson = "{}"
        });
        await db.SaveChangesAsync();

        var response = await service.GetChapterByIdAsync(chapter.Id, "user-1", isAdmin: false);

        var chain = Assert.Single(response.ProductionChains);
        Assert.Equal("completed", chain.Status);
        Assert.Equal(version.Id, chain.ChapterVersionId);
        Assert.Equal("fact-chapter-008", chain.FactSnapshotId);
        Assert.NotNull(response.ProductionEvidence.LatestFactSnapshot);
        Assert.Equal("fact-chapter-008", response.ProductionEvidence.LatestFactSnapshot!.Id);
        Assert.Equal(3, response.ProductionEvidence.LatestFactSnapshot.VersionNumber);
        Assert.Contains("银蓝邮徽", response.ProductionEvidence.LatestFactSnapshot.SnapshotPreview);
        var plan = Assert.Single(response.ProductionEvidence.RevisionPlans);
        Assert.Equal("revision-plan-008", plan.Id);
        Assert.Equal("executed", plan.Status);
        Assert.Contains("银蓝邮徽复燃", plan.Recommendation);
        Assert.Equal(new[] { "pkg-chapter-008-v1" }, plan.InvalidatedPackageIds);
        var outbox = Assert.Single(response.ProductionEvidence.OutboxEvents, item => item.Id == "outbox-chapter-008");
        Assert.Equal("outbox-chapter-008", outbox.Id);
        Assert.Equal("completed", outbox.Status);
        Assert.Equal(1, outbox.Attempts);
    }

    private static ChapterService CreateService(NovelAgentDbContext db) =>
        new(
            db,
            NullLogger<ChapterService>.Instance,
            new ContentDocumentService(db),
            new ProductionTruthStore(db),
            new ProductionWorkflowBridge(db),
            new ProductionChainProjectionService());

    private static void SeedUserProject(NovelAgentDbContext db)
    {
        db.Users.Add(new User
        {
            Id = "user-1",
            Username = "user-1",
            Email = "user-1@example.test",
            PasswordHash = "hash",
            Role = "author",
            CreatedAt = DateTime.UtcNow
        });
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-1",
            UserId = "user-1",
            Title = "雾城邮路",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }
}
