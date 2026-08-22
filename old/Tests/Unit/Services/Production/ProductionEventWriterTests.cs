using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Modules.ProjectData.Models.Tracking;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class ProductionEventWriterTests
{
    [Fact]
    public async Task AppendChapterStageAsync_PersistsEventAndSerializesDataThroughTruthStore()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndPackage(db);
        IProductionTruthStore truthStore = new ProductionTruthStore(db);
        IProductionEventWriter writer = new ProductionEventWriter(truthStore);

        var evt = await writer.AppendChapterStageAsync(new AppendChapterProductionEventRequest(
            RuntimeRunId: "run-1",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "chapter-001",
            PackageId: "pkg-1",
            EventType: "chapter_gate_validated",
            Stage: NovelAgentProductionStages.GateValidation,
            Status: "completed",
            Message: "门禁通过。",
            ArtifactType: "generation_gate_report",
            ArtifactId: "validated",
            Data: new { gateStatus = "validated", issueCount = 0 }));

        Assert.NotNull(evt);
        Assert.Equal("run-1", evt!.RuntimeRunId);
        Assert.Equal("project-1", evt.ProjectId);
        Assert.Equal("chapter-001", evt.ChapterId);
        Assert.Equal("pkg-1", evt.PackageId);
        Assert.Equal(NovelAgentProductionStages.GateValidated, evt.Stage);
        Assert.Contains("\"gateStatus\":\"validated\"", evt.DataJson);
        Assert.Contains("\"issueCount\":0", evt.DataJson);

        var package = await db.TianmingPackages.AsNoTracking().SingleAsync(p => p.Id == "pkg-1");
        Assert.Equal("completed", package.Status);
    }

    [Fact]
    public async Task AppendAgentReviewAsync_UpdatesSameReviewWhenWarningAcceptanceChangesFinalDecision()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndPackage(db);
        IProductionTruthStore truthStore = new ProductionTruthStore(db);
        IProductionEventWriter writer = new ProductionEventWriter(truthStore);

        await writer.AppendAgentReviewAsync(new AppendAgentReviewRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "run-review",
            ChapterId: "chapter-001",
            PackageId: "pkg-1",
            ReviewId: "review-1",
            OverallResult: "Warning",
            ValidationOverallResult: "通过",
            RequiresRewrite: true,
            QualityScore: 68,
            ContentLength: 3000,
            CheckCount: 5,
            Summary: "警告过多，建议改写。",
            ReviewJson: "{\"requiresRewrite\":true}",
            ReviewedAt: DateTime.UtcNow,
            MeetsAcceptedCreativeIntents: true,
            ContinuityRisk: "low",
            ChapterPacing: "slow",
            RecommendedAction: "revise_before_commit"));

        var accepted = await writer.AppendAgentReviewAsync(new AppendAgentReviewRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "run-review",
            ChapterId: "chapter-001",
            PackageId: "pkg-1",
            ReviewId: "review-1",
            OverallResult: "Warning",
            ValidationOverallResult: "通过",
            RequiresRewrite: false,
            QualityScore: 68,
            ContentLength: 3000,
            CheckCount: 5,
            Summary: "警告已由 Agent 接受，允许先提交。",
            ReviewJson: "{\"requiresRewrite\":false}",
            ReviewedAt: DateTime.UtcNow.AddSeconds(1),
            MeetsAcceptedCreativeIntents: true,
            ContinuityRisk: "low",
            ChapterPacing: "slow",
            RecommendedAction: "commit"));

        var review = await db.AgentReviews.AsNoTracking().SingleAsync();
        Assert.Equal(review.Id, accepted.Id);
        Assert.False(review.RequiresRewrite);
        Assert.Equal("警告已由 Agent 接受，允许先提交。", review.Summary);
        Assert.Equal("commit", review.RecommendedAction);
        Assert.Contains("\"requiresRewrite\":false", review.ReviewJson);
    }

    [Fact]
    public async Task AppendChapterStageAsync_ThrowsWhenRequiredScopeIsMissing()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndPackage(db);
        IProductionTruthStore truthStore = new ProductionTruthStore(db);
        IProductionEventWriter writer = new ProductionEventWriter(truthStore);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            writer.AppendChapterStageAsync(new AppendChapterProductionEventRequest(
                RuntimeRunId: "run-1",
                UserId: "",
                ProjectId: "project-1",
                ChapterId: "chapter-001",
                PackageId: "pkg-1",
                EventType: "chapter_gate_validated",
                Stage: NovelAgentProductionStages.GateValidation,
                Status: "completed",
                Message: "门禁通过。",
                ArtifactType: "generation_gate_report",
                ArtifactId: "validated",
                Data: null)));

        Assert.Contains("Production event requires runtimeRunId, userId and projectId", ex.Message);
        Assert.Empty(await db.ProductionEvents.ToListAsync());
    }

    [Fact]
    public async Task AppendChapterStageAsync_AllowsCommittedChapterEventWithoutPackage()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndPackage(db);
        IProductionTruthStore truthStore = new ProductionTruthStore(db);
        IProductionEventWriter writer = new ProductionEventWriter(truthStore);

        var evt = await writer.AppendChapterStageAsync(new AppendChapterProductionEventRequest(
            RuntimeRunId: "run-commit",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "chapter-001",
            PackageId: null,
            EventType: "chapter_committed",
            Stage: NovelAgentProductionStages.ChapterCommit,
            Status: "completed",
            Message: "章节已提交书城。",
            ArtifactType: "chapter_version",
            ArtifactId: "version-1",
            Data: new { versionNumber = 1, wordCount = 3200 }));

        Assert.NotNull(evt);
        Assert.Equal("chapter_committed", evt!.EventType);
        Assert.Null(evt.PackageId);
        Assert.Contains("\"versionNumber\":1", evt.DataJson);
    }

    [Fact]
    public async Task ProductionChapterChangesRecorder_PersistsChangesAsProductionEvent()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndPackage(db);
        IProductionTruthStore truthStore = new ProductionTruthStore(db);
        IProductionEventWriter writer = new ProductionEventWriter(truthStore);
        var recorder = new ProductionChapterChangesRecorder(
            writer,
            userId: "user-1",
            projectId: "project-1");

        await recorder.RecordAsync(
            new NovelAgentRun
            {
                RunId = "run-1",
                TargetChapterId = "chapter-001"
            },
            "chapter-001",
            new ChapterChanges
            {
                NewPlotPoints = new List<PlotPointChange>
                {
                    new()
                    {
                        Context = "旧邮路第一次亮起银蓝光",
                        Keywords = new List<string> { "旧邮路", "银蓝光" }
                    }
                },
                CharacterStateChanges = new List<CharacterStateChange>
                {
                    new()
                    {
                        CharacterId = "林澈",
                        KeyEvent = "林澈确认邮徽不是攻击型道具"
                    }
                }
            },
            """{"NewPlotPoints":["旧邮路第一次亮起银蓝光"],"CharacterStateChanges":["林澈确认邮徽不是攻击型道具"]}""",
            CancellationToken.None);

        var evt = await db.ProductionEvents.AsNoTracking().SingleAsync(e => e.EventType == "chapter_changes_recorded");
        Assert.Equal("run-1", evt.RuntimeRunId);
        Assert.Equal("project-1", evt.ProjectId);
        Assert.Equal("chapter-001", evt.ChapterId);
        Assert.Equal(NovelAgentProductionStages.ChangesExtracted, evt.Stage);
        Assert.Equal("chapter_changes", evt.ArtifactType);
        Assert.Equal("chapter-001:changes", evt.ArtifactId);
        Assert.Contains("\"newPlotPointCount\":1", evt.DataJson);
        Assert.Contains("旧邮路第一次亮起银蓝光", evt.DataJson);
        Assert.Contains("林澈确认邮徽不是攻击型道具", evt.DataJson);
    }

    [Fact]
    public async Task ProductionChapterChangesRecorder_ReusesSameChangesWithoutDuplicatingEventOrChange()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndPackage(db);
        IProductionTruthStore truthStore = new ProductionTruthStore(db);
        IProductionEventWriter writer = new ProductionEventWriter(truthStore);
        var recorder = new ProductionChapterChangesRecorder(
            writer,
            userId: "user-1",
            projectId: "project-1");
        var run = new NovelAgentRun
        {
            RunId = "run-1",
            TargetChapterId = "chapter-001",
            ContextPackage = new ChapterContextPackageSummary
            {
                PackageId = "pkg-1",
                ChapterId = "chapter-001"
            }
        };
        var changes = new ChapterChanges
        {
            NewPlotPoints = new List<PlotPointChange>
            {
                new()
                {
                    Context = "旧邮路第一次亮起银蓝光",
                    Keywords = new List<string> { "旧邮路", "银蓝光" }
                }
            }
        };
        const string changesJson = """{"NewPlotPoints":["旧邮路第一次亮起银蓝光"]}""";

        await recorder.RecordAsync(run, "chapter-001", changes, changesJson, CancellationToken.None);
        await recorder.RecordAsync(run, "chapter-001", changes, changesJson, CancellationToken.None);

        Assert.Single(await db.ProductionEvents
            .Where(e => e.EventType == "chapter_changes_recorded" &&
                        e.RuntimeRunId == "run-1" &&
                        e.ChapterId == "chapter-001" &&
                        e.PackageId == "pkg-1")
            .ToListAsync());
        Assert.Single(await db.ChapterChanges
            .Where(change =>
                change.RuntimeRunId == "run-1" &&
                change.ChapterId == "chapter-001" &&
                change.PackageId == "pkg-1")
            .ToListAsync());
    }

    [Fact]
    public async Task AppendPreCommitEvidence_UsesCanonicalChapterIdWhenChapterAlreadyExists()
    {
        await using var db = CreateDb();
        SeedProjectWithCanonicalChapterAndPackage(db);
        IProductionTruthStore truthStore = new ProductionTruthStore(db);
        IProductionEventWriter writer = new ProductionEventWriter(truthStore, db);

        await writer.AppendChapterStageAsync(new AppendChapterProductionEventRequest(
            RuntimeRunId: "run-canonical",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "chapter-001",
            PackageId: "pkg-canonical",
            EventType: "chapter_draft_generated",
            Stage: NovelAgentProductionStages.DraftGeneration,
            Status: "completed",
            Message: "草稿已生成。",
            ArtifactType: "chapter_draft_artifact",
            ArtifactId: "draft-artifact",
            Data: new { logicalChapterId = "chapter-001" }));

        await writer.AppendChapterDraftAsync(new AppendChapterDraftRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "run-canonical",
            ChapterId: "chapter-001",
            PackageId: "pkg-canonical",
            ArtifactId: "draft-artifact",
            Status: "draft_generated",
            DraftContent: "第一章 正文",
            ChangesJson: "{}",
            RepairAttemptCount: 0,
            HasChanges: true));

        await writer.AppendChapterChangeAsync(new AppendChapterChangeRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "run-canonical",
            ChapterId: "chapter-001",
            PackageId: "pkg-canonical",
            ChangesJson: "{}",
            CanonicalChangesJson: "{}",
            ParseStatus: "parsed",
            ParseError: null,
            AppliedToFactSnapshot: false));

        await writer.AppendGenerationGateReportAsync(new AppendGenerationGateReportRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "run-canonical",
            ChapterId: "chapter-001",
            PackageId: "pkg-canonical",
            ArtifactId: "gate-artifact",
            Status: "validated",
            ReportJson: "{}",
            ProtocolPassed: true,
            ChangesDetected: true,
            FactSnapshotPassed: true,
            BlueprintPassed: true,
            RagPassed: true,
            IssueCount: 0,
            RepairHintCount: 0,
            ValidatedAt: DateTime.UtcNow));

        await writer.AppendAgentReviewAsync(new AppendAgentReviewRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "run-canonical",
            ChapterId: "chapter-001",
            PackageId: "pkg-canonical",
            ReviewId: "review-1",
            OverallResult: "Pass",
            ValidationOverallResult: "通过",
            RequiresRewrite: false,
            QualityScore: 90,
            ContentLength: 128,
            CheckCount: 3,
            Summary: "通过总编验收。",
            ReviewJson: "{}",
            ReviewedAt: DateTime.UtcNow,
            MeetsAcceptedCreativeIntents: false,
            ContinuityRisk: "medium",
            ChapterPacing: "needs_revision",
            RecommendedAction: "revise_before_commit"));

        Assert.All(await db.ProductionEvents.AsNoTracking().ToListAsync(),
            evt => Assert.Equal("project-1-chapter-001", evt.ChapterId));
        Assert.All(await db.ChapterDrafts.AsNoTracking().ToListAsync(),
            draft => Assert.Equal("project-1-chapter-001", draft.ChapterId));
        Assert.All(await db.ChapterChanges.AsNoTracking().ToListAsync(),
            change => Assert.Equal("project-1-chapter-001", change.ChapterId));
        Assert.All(await db.GenerationGateReports.AsNoTracking().ToListAsync(),
            gate => Assert.Equal("project-1-chapter-001", gate.ChapterId));
        Assert.All(await db.AgentReviews.AsNoTracking().ToListAsync(),
            review => Assert.Equal("project-1-chapter-001", review.ChapterId));
        var agentReview = await db.AgentReviews.AsNoTracking().SingleAsync();
        Assert.False(agentReview.MeetsAcceptedCreativeIntents);
        Assert.Equal("medium", agentReview.ContinuityRisk);
        Assert.Equal("needs_revision", agentReview.ChapterPacing);
        Assert.Equal("revise_before_commit", agentReview.RecommendedAction);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static void SeedProjectChapterAndPackage(NovelAgentDbContext db)
    {
        db.Users.Add(new User
        {
            Id = "user-1",
            Username = "user",
            Email = "user@example.com",
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
        db.Chapters.Add(new Chapter
        {
            Id = "chapter-001",
            ProjectId = "project-1",
            Title = "第一章 银蓝邮徽",
            ChapterNumber = 1,
            Status = "draft",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.TianmingPackages.Add(new TianmingPackage
        {
            Id = "pkg-1",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-001",
            RuntimeRunId = "run-1",
            PackageKind = "chapter_context_package",
            Status = "pending",
            InputJson = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    private static void SeedProjectWithCanonicalChapterAndPackage(NovelAgentDbContext db)
    {
        db.Users.Add(new User
        {
            Id = "user-1",
            Username = "user",
            Email = "user@example.com",
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
        db.Chapters.Add(new Chapter
        {
            Id = "project-1-chapter-001",
            ProjectId = "project-1",
            Title = "第一章 银蓝邮徽",
            ChapterNumber = 1,
            Status = "draft",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.TianmingPackages.Add(new TianmingPackage
        {
            Id = "pkg-canonical",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "project-1-chapter-001",
            RuntimeRunId = "run-canonical",
            PackageKind = "chapter_context_package",
            Status = "pending",
            InputJson = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }
}
