using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Content;

public sealed class ProjectContentQueryServiceTests
{
    [Fact]
    public async Task QueryAsync_ReturnsVolumeChapterBodyVersionFactsAndProductionEvents()
    {
        await using var db = CreateDb();
        var contentDocuments = new ContentDocumentService(db);
        await SeedProjectContentAsync(db, contentDocuments);
        IProjectContentQueryService service = new ProjectContentQueryService(db, contentDocuments);

        var result = await service.QueryAsync(new ProjectContentQueryRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            StoryBible: BuildBible(),
            ChapterNumber: 4,
            IncludeBody: true));

        Assert.NotNull(result);
        Assert.Equal("project-1", result!.ProjectId);
        Assert.Equal("连续性测试书", result.ProjectTitle);
        var item = Assert.Single(result.Items);
        Assert.Equal("project-1-chapter-004", item.ChapterId);
        Assert.Equal("run-chapter-004", item.SourceRunId);
        Assert.Equal("第四章：地下室反击", item.ChapterTitle);
        Assert.Equal("第一卷：黑雨觉醒", item.VolumeTitle);
        Assert.Equal(1, item.CurrentVersionNumber);
        Assert.Equal("pkg-chapter-004", item.PackageId);
        Assert.Equal(new[] { "pkg-chapter-004-old", "pkg-chapter-004-v1" }, item.RebuiltFromPackageIds);
        Assert.Equal("tianming-kernel-v1", item.KernelVersion);
        Assert.Contains("陈默握着旧扳手", item.Body);
        Assert.Contains("陈默守住地下室入口", item.FactSnapshotJson);
        var binding = Assert.Single(item.KnowledgeBindings);
        Assert.Equal("kb-black-rain", binding.KnowledgeId);
        Assert.Equal("HardConstraint", binding.ConstraintLevel);
        Assert.Equal("DefaultEveryChapter", binding.PackagePolicy);
        Assert.Contains("project-1-chapter-004", binding.UsedByChapters);
        var sourceRevisionPlan = Assert.Single(item.SourceRevisionPlans);
        Assert.Equal("revision-plan-004", sourceRevisionPlan.RevisionPlanId);
        Assert.Equal("chapter_rewrite", sourceRevisionPlan.PlanType);
        Assert.Equal("chapter", sourceRevisionPlan.TargetScope);
        Assert.Equal("project-1-chapter-004", sourceRevisionPlan.TargetChapterId);
        Assert.Equal("chapter-004", sourceRevisionPlan.TargetChapterLogicalId);
        Assert.Equal("第四章：地下室反击", sourceRevisionPlan.TargetChapterDisplayName);
        Assert.Equal("executed", sourceRevisionPlan.Status);
        Assert.Equal("medium", sourceRevisionPlan.RiskLevel);
        Assert.Equal("把第四章改成地下室反击并保留黑雨硬约束。", sourceRevisionPlan.Recommendation);
        Assert.Contains(item.ProductionEvents, evt =>
            evt.Stage == "KernelGate" &&
            evt.Status == "completed" &&
            evt.Message == "门禁通过");
        var gateReport = Assert.Single(item.GenerationGateReports);
        Assert.False(string.IsNullOrWhiteSpace(gateReport.Id));
        Assert.Equal("gate-artifact-004", gateReport.ArtifactId);
        Assert.Equal("validated", gateReport.Status);
        Assert.True(gateReport.ProtocolPassed);
        Assert.True(gateReport.FactSnapshotPassed);
        Assert.Equal(0, gateReport.IssueCount);
        var agentReview = Assert.Single(item.AgentReviews);
        Assert.False(string.IsNullOrWhiteSpace(agentReview.Id));
        Assert.Equal("review-004", agentReview.ReviewId);
        Assert.Equal("Pass", agentReview.OverallResult);
        Assert.False(agentReview.RequiresRewrite);
        Assert.Equal(91, agentReview.QualityScore);
        Assert.Equal("总编验收通过：地下室反击承接稳定。", agentReview.Summary);
        Assert.Contains(item.ContinuityFacts, fact =>
            fact.ProtagonistName == "陈默" &&
            fact.NextChapterMustCarry.Contains("继续承接地下室入口攻防"));
    }

    [Fact]
    public async Task QueryAsync_WhenVersionNumberSpecified_ReturnsHistoricalBodyAndProductionEvidence()
    {
        await using var db = CreateDb();
        var contentDocuments = new ContentDocumentService(db);
        await SeedProjectContentAsync(db, contentDocuments);
        await SeedSecondVersionAsync(db, contentDocuments);
        IProjectContentQueryService service = new ProjectContentQueryService(db, contentDocuments);

        var result = await service.QueryAsync(new ProjectContentQueryRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            StoryBible: BuildBible(),
            ChapterNumber: 4,
            IncludeBody: true,
            VersionNumber: 1));

        Assert.NotNull(result);
        var item = Assert.Single(result!.Items);
        Assert.Equal(1, item.CurrentVersionNumber);
        Assert.Equal("pkg-chapter-004", item.PackageId);
        Assert.Equal("run-chapter-004", item.SourceRunId);
        Assert.Contains("陈默握着旧扳手", item.Body);
        Assert.DoesNotContain("陈默改用信标长钉", item.Body);
        Assert.Contains("陈默守住地下室入口", item.FactSnapshotJson);
        Assert.DoesNotContain("陈默夺回维修站地面层", item.FactSnapshotJson);
        Assert.Single(item.GenerationGateReports);
        Assert.Equal("gate-artifact-004", item.GenerationGateReports[0].ArtifactId);
        Assert.Single(item.AgentReviews);
        Assert.Equal("review-004", item.AgentReviews[0].ReviewId);
        Assert.Contains(item.ProductionEvents, evt =>
            evt.EventType == "chapter_changes_recorded" &&
            evt.Message == "历史短章号 CHANGES 已记录");
        var change = Assert.Single(item.ChapterChanges);
        Assert.Equal("run-chapter-004", change.RuntimeRunId);
        Assert.Contains("地下室入口攻防", change.CanonicalChangesJson);
        Assert.DoesNotContain("地面层", change.CanonicalChangesJson);
    }

    [Fact]
    public async Task QueryAsync_WhenVersionNumberSpecified_DoesNotMixEvidenceFromOtherRuntimeEvenIfPackageIdMatches()
    {
        await using var db = CreateDb();
        var contentDocuments = new ContentDocumentService(db);
        await SeedProjectContentAsync(db, contentDocuments);
        await SeedSecondVersionAsync(db, contentDocuments);
        await SeedCrossVersionContaminatedEvidenceAsync(db);
        IProjectContentQueryService service = new ProjectContentQueryService(db, contentDocuments);

        var result = await service.QueryAsync(new ProjectContentQueryRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            StoryBible: BuildBible(),
            ChapterNumber: 4,
            IncludeBody: true,
            VersionNumber: 1));

        Assert.NotNull(result);
        var item = Assert.Single(result!.Items);
        Assert.Equal(1, item.CurrentVersionNumber);
        Assert.DoesNotContain(item.ProductionEvents, evt => evt.Message.Contains("跨版本污染", StringComparison.Ordinal));
        Assert.DoesNotContain(item.ChapterChanges, change => change.CanonicalChangesJson.Contains("跨版本污染", StringComparison.Ordinal));
        Assert.DoesNotContain(item.GenerationGateReports, report => report.ArtifactId == "gate-contaminated-v2-on-v1-package");
        Assert.DoesNotContain(item.AgentReviews, review => review.ReviewId == "review-contaminated-v2-on-v1-package");
    }

    [Fact]
    public async Task QueryAsync_WhenVersionIdSpecified_ReturnsShortChapterFactSnapshotAndIndependentRevisionPlan()
    {
        await using var db = CreateDb();
        var contentDocuments = new ContentDocumentService(db);
        await SeedProjectContentAsync(db, contentDocuments);
        await SeedSecondVersionAsync(db, contentDocuments);
        var version = await db.ChapterVersions
            .AsNoTracking()
            .SingleAsync(item => item.ProjectId == "project-1" && item.VersionNumber == 1);
        await SeedShortChapterFactSnapshotAndDirectRevisionPlanAsync(db, version.Id);
        IProjectContentQueryService service = new ProjectContentQueryService(db, contentDocuments);

        var result = await service.QueryAsync(new ProjectContentQueryRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            StoryBible: BuildBible(),
            ChapterNumber: 4,
            IncludeBody: true,
            VersionId: version.Id));

        Assert.NotNull(result);
        var item = Assert.Single(result!.Items);
        Assert.Equal(version.Id, item.CurrentVersionId);
        Assert.Equal(1, item.CurrentVersionNumber);
        Assert.Contains("短章号事实快照", item.FactSnapshotJson);
        Assert.DoesNotContain("陈默夺回维修站地面层", item.FactSnapshotJson);
        Assert.Contains(item.SourceRevisionPlans, plan =>
            plan.RevisionPlanId == "revision-plan-direct-004" &&
            plan.TargetChapterLogicalId == "chapter-004" &&
            plan.TargetChapterDisplayName == "第四章：地下室反击" &&
            plan.Status == "executed");
        Assert.DoesNotContain(item.SourceRevisionPlans, plan => plan.RevisionPlanId == "revision-plan-direct-v2");
    }

    [Fact]
    public async Task QueryAsync_NonAdminCannotReadOtherUsersProjectContent()
    {
        await using var db = CreateDb();
        var contentDocuments = new ContentDocumentService(db);
        await SeedProjectContentAsync(db, contentDocuments);
        IProjectContentQueryService service = new ProjectContentQueryService(db, contentDocuments);

        var result = await service.QueryAsync(new ProjectContentQueryRequest(
            UserId: "user-2",
            ProjectId: "project-1",
            StoryBible: new StoryBibleDocument(),
            ChapterNumber: 4));

        Assert.Null(result);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static StoryBibleDocument BuildBible() => new()
    {
        AgentRuns =
        {
            new NovelAgentRun
            {
                RunId = "run-chapter-004",
                TargetChapterId = "chapter-004",
                UpdatedAt = DateTime.UtcNow
            }
        },
        ContinuityFacts =
        {
            new ChapterContinuityFacts
            {
                ChapterId = "chapter-004",
                ProtagonistName = "陈默",
                EndingState = "陈默守住地下室入口",
                NextChapterMustCarry = { "继续承接地下室入口攻防" }
            }
        }
    };

    private static async Task SeedProjectContentAsync(
        NovelAgentDbContext db,
        IContentDocumentService contentDocuments)
    {
        db.Users.AddRange(
            new User
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            },
            new User
            {
                Id = "user-2",
                Username = "other",
                Email = "other@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-1",
            UserId = "user-1",
            Title = "连续性测试书",
            Status = "Writing",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.Volumes.Add(new Volume
        {
            Id = "volume-1",
            ProjectId = "project-1",
            Title = "第一卷：黑雨觉醒",
            VolumeNumber = 1
        });
        db.Chapters.Add(new Chapter
        {
            Id = "project-1-chapter-004",
            ProjectId = "project-1",
            VolumeId = "volume-1",
            Title = "第四章：地下室反击",
            ChapterNumber = 4,
            Status = "committed",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var document = await contentDocuments.SaveOrReplaceTextAsync(
            "user-1",
            "project-1",
            "chapter",
            "project-1-chapter-004",
            "chapter_body",
            "第四章：地下室反击",
            "第四章：地下室反击\n陈默握着旧扳手，在维修站地下室迎向黑潮异兽。",
            CancellationToken.None);
        var chapter = await db.Chapters.SingleAsync(c => c.Id == "project-1-chapter-004");
        chapter.CurrentDocumentId = document.Id;
        await db.SaveChangesAsync();

        var truthStore = new ProductionTruthStore(db);
        var package = await truthStore.CreatePackageAsync(new CreateTianmingPackageRequest(
            Id: "pkg-chapter-004",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "project-1-chapter-004",
            RuntimeRunId: "run-chapter-004",
            PackageKind: "chapter_generation",
            InputJson: "{\"goal\":\"地下室反击\"}",
            DependencyVersionsJson: "{\"storyBible\":2,\"blueprint\":1}",
            KnowledgeSnapshotJson: """
                {
                  "knowledgeBindings": [
                    {
                      "knowledgeId": "kb-black-rain",
                      "title": "黑雨规则",
                      "entryType": "HardFact",
                      "content": "黑雨会腐蚀暴露在外的记忆标签。",
                      "tags": ["黑雨", "硬事实"],
                      "weight": 10,
                      "projectUsageStatus": "referenced",
                      "projectUsageCount": 3,
                      "role": "WorldRule",
                      "scope": "ProjectWide",
                      "priority": 80,
                      "constraintLevel": "HardConstraint",
                      "packagePolicy": "DefaultEveryChapter",
                      "boundVersion": "kb-black-rain-v1",
                      "usedByChapters": ["project-1-chapter-004"]
                    }
                  ],
                  "sourceRevisionPlans": [
                    {
                      "revisionPlanId": "revision-plan-004",
                      "planType": "chapter_rewrite",
                      "targetScope": "chapter",
                      "targetChapterId": "project-1-chapter-004",
                      "targetChapterLogicalId": "chapter-004",
                      "targetChapterDisplayName": "第四章：地下室反击",
                      "status": "ready_for_rebuild",
                      "riskLevel": "medium",
                      "recommendation": "把第四章改成地下室反击并保留黑雨硬约束。"
                    }
                  ],
                  "rebuiltFromPackageIds": ["pkg-chapter-004-old", "pkg-chapter-004-v1"]
                }
                """,
            FactSnapshotJson: "{\"state\":\"地下室攻防\"}",
            PromptVersion: "chapter-v1",
            KernelVersion: "tianming-kernel-v1"));
        var version = await truthStore.CreateChapterVersionAsync(new CreateChapterVersionRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "project-1-chapter-004",
            ContentDocumentId: document.Id,
            Title: "第四章：地下室反击",
            WordCount: 28,
            Status: "committed",
            RuntimeRunId: "run-chapter-004",
            PackageId: package.Id,
            GateReportJson: "{\"status\":\"validated\"}",
            AgentReviewJson: "{\"decision\":\"commit\"}"));
        await truthStore.SaveFactSnapshotAsync(new SaveProjectFactSnapshotRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "project-1-chapter-004",
            ChapterVersionId: version.Id,
            SnapshotJson: """
                {
                  "protagonist": "陈默",
                  "endingState": "陈默守住地下室入口",
                  "next": "继续承接地下室入口攻防",
                  "sourceRevisionPlans": [
                    {
                      "revisionPlanId": "revision-plan-004",
                      "planType": "chapter_rewrite",
                      "targetScope": "chapter",
                      "targetChapterId": "project-1-chapter-004",
                      "status": "executed",
                      "riskLevel": "medium",
                      "recommendation": "把第四章改成地下室反击并保留黑雨硬约束。"
                    }
                  ]
                }
                """,
            Source: "chapter_commit"));
        await truthStore.AppendEventAsync(new CreateProductionEventRequest(
            RuntimeRunId: "run-chapter-004",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "project-1-chapter-004",
            PackageId: package.Id,
            EventType: "kernel_gate",
            Stage: "KernelGate",
            Status: "completed",
            Message: "门禁通过",
            ArtifactType: "GateReport",
            ArtifactId: "gate-chapter-004",
            DataJson: "{\"score\":92}"));
        await truthStore.AppendEventAsync(new CreateProductionEventRequest(
            RuntimeRunId: "run-chapter-004",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "chapter-004",
            PackageId: package.Id,
            EventType: "chapter_changes_recorded",
            Stage: "ChangesExtracted",
            Status: "completed",
            Message: "历史短章号 CHANGES 已记录",
            ArtifactType: "ChapterChange",
            ArtifactId: "changes-chapter-004",
            DataJson: "{\"changes\":[\"地下室入口攻防\"]}"));
        await truthStore.CreateChapterChangeAsync(new CreateChapterChangeRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "run-chapter-004",
            ChapterId: "chapter-004",
            PackageId: package.Id,
            ChangesJson: "{\"changes\":[\"地下室入口攻防\"]}",
            CanonicalChangesJson: "{\"changes\":[\"地下室入口攻防\"]}",
            ParseStatus: "parsed",
            ParseError: null,
            AppliedToFactSnapshot: true));
        await truthStore.CreateGenerationGateReportAsync(new CreateGenerationGateReportRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "run-chapter-004",
            ChapterId: "project-1-chapter-004",
            PackageId: package.Id,
            ArtifactId: "gate-artifact-004",
            Status: "validated",
            ReportJson: "{\"status\":\"validated\",\"checks\":[\"protocol\",\"facts\"]}",
            ProtocolPassed: true,
            ChangesDetected: true,
            FactSnapshotPassed: true,
            BlueprintPassed: true,
            RagPassed: true,
            IssueCount: 0,
            RepairHintCount: 0,
            ValidatedAt: DateTime.UtcNow));
        await truthStore.CreateAgentReviewAsync(new CreateAgentReviewRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "run-chapter-004",
            ChapterId: "project-1-chapter-004",
            PackageId: package.Id,
            ReviewId: "review-004",
            OverallResult: "Pass",
            ValidationOverallResult: "validated",
            RequiresRewrite: false,
            QualityScore: 91,
            ContentLength: 1200,
            CheckCount: 6,
            Summary: "总编验收通过：地下室反击承接稳定。",
            ReviewJson: "{\"decision\":\"pass\",\"evidence\":[\"承接上一章\"]}",
            ReviewedAt: DateTime.UtcNow));
    }

    private static async Task SeedSecondVersionAsync(
        NovelAgentDbContext db,
        IContentDocumentService contentDocuments)
    {
        var document = await contentDocuments.SaveOrReplaceTextAsync(
            "user-1",
            "project-1",
            "chapter",
            "project-1-chapter-004-v2",
            "chapter_body",
            "第四章：信标长钉",
            "第四章：信标长钉\n陈默改用信标长钉，在维修站地面层截断黑潮异兽。",
            CancellationToken.None);

        var truthStore = new ProductionTruthStore(db);
        var package = await truthStore.CreatePackageAsync(new CreateTianmingPackageRequest(
            Id: "pkg-chapter-004-v2",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "project-1-chapter-004",
            RuntimeRunId: "run-chapter-004-v2",
            PackageKind: "chapter_generation",
            InputJson: "{\"goal\":\"信标长钉\"}",
            DependencyVersionsJson: "{\"storyBible\":3,\"blueprint\":2}",
            KnowledgeSnapshotJson: "{\"rebuiltFromPackageIds\":[\"pkg-chapter-004\"]}",
            FactSnapshotJson: "{\"state\":\"地面层反击\"}",
            PromptVersion: "chapter-v2",
            KernelVersion: "tianming-kernel-v2"));
        var version = await truthStore.CreateChapterVersionAsync(new CreateChapterVersionRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "project-1-chapter-004",
            ContentDocumentId: document.Id,
            Title: "第四章：信标长钉",
            WordCount: 31,
            Status: "committed",
            RuntimeRunId: "run-chapter-004-v2",
            PackageId: package.Id,
            GateReportJson: "{\"status\":\"validated-v2\"}",
            AgentReviewJson: "{\"decision\":\"commit-v2\"}"));
        await truthStore.SaveFactSnapshotAsync(new SaveProjectFactSnapshotRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "project-1-chapter-004",
            ChapterVersionId: version.Id,
            SnapshotJson: "{\"endingState\":\"陈默夺回维修站地面层\"}",
            Source: "chapter_commit"));
        await truthStore.CreateGenerationGateReportAsync(new CreateGenerationGateReportRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "run-chapter-004-v2",
            ChapterId: "project-1-chapter-004",
            PackageId: package.Id,
            ArtifactId: "gate-artifact-004-v2",
            Status: "validated",
            ReportJson: "{\"status\":\"validated-v2\"}",
            ProtocolPassed: true,
            ChangesDetected: true,
            FactSnapshotPassed: true,
            BlueprintPassed: true,
            RagPassed: true,
            IssueCount: 0,
            RepairHintCount: 0,
            ValidatedAt: DateTime.UtcNow));
        await truthStore.CreateAgentReviewAsync(new CreateAgentReviewRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "run-chapter-004-v2",
            ChapterId: "project-1-chapter-004",
            PackageId: package.Id,
            ReviewId: "review-004-v2",
            OverallResult: "Pass",
            ValidationOverallResult: "validated",
            RequiresRewrite: false,
            QualityScore: 93,
            ContentLength: 1300,
            CheckCount: 6,
            Summary: "总编验收通过：信标长钉版本承接稳定。",
            ReviewJson: "{\"decision\":\"pass-v2\"}",
            ReviewedAt: DateTime.UtcNow));
        await truthStore.CreateChapterChangeAsync(new CreateChapterChangeRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "run-chapter-004-v2",
            ChapterId: "project-1-chapter-004",
            PackageId: package.Id,
            ChangesJson: "{\"changes\":[\"地面层反击\"]}",
            CanonicalChangesJson: "{\"changes\":[\"地面层反击\"]}",
            ParseStatus: "parsed",
            ParseError: null,
            AppliedToFactSnapshot: true));
    }

    private static async Task SeedShortChapterFactSnapshotAndDirectRevisionPlanAsync(
        NovelAgentDbContext db,
        string versionId)
    {
        var truthStore = new ProductionTruthStore(db);
        await truthStore.SaveFactSnapshotAsync(new SaveProjectFactSnapshotRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "chapter-004",
            ChapterVersionId: versionId,
            SnapshotJson: """
                {
                  "endingState": "短章号事实快照：陈默仍在地下室入口",
                  "sourceRevisionPlans": []
                }
                """,
            Source: "chapter_commit"));
        db.RevisionPlans.AddRange(
            new RevisionPlan
            {
                Id = "revision-plan-direct-004",
                UserId = "user-1",
                ProjectId = "project-1",
                RuntimeRunId = "run-chapter-004",
                Source = "agent_review",
                PlanType = "chapter_rewrite",
                TargetScope = "chapter",
                TargetChapterId = "chapter-004",
                TargetChapterLogicalId = "chapter-004",
                TargetChapterDisplayName = "第四章：地下室反击",
                Status = "executed",
                RequirementsJson = "[\"保留地下室入口攻防\"]",
                ContinuityRequirementsJson = "[\"下一章继续承接地下室入口\"]",
                AffectedChapterIdsJson = "[\"chapter-004\"]",
                InvalidatedPackageIdsJson = "[\"pkg-chapter-004-old\"]",
                RiskLevel = "medium",
                Recommendation = "独立表修订计划：保留地下室入口攻防。",
                CreatedAt = DateTime.UtcNow.AddSeconds(1),
                UpdatedAt = DateTime.UtcNow.AddSeconds(1)
            },
            new RevisionPlan
            {
                Id = "revision-plan-direct-v2",
                UserId = "user-1",
                ProjectId = "project-1",
                RuntimeRunId = "run-chapter-004-v2",
                Source = "agent_review",
                PlanType = "chapter_rewrite",
                TargetScope = "chapter",
                TargetChapterId = "project-1-chapter-004",
                TargetChapterLogicalId = "chapter-004",
                TargetChapterDisplayName = "第四章：信标长钉",
                Status = "executed",
                AffectedChapterIdsJson = "[\"project-1-chapter-004\"]",
                InvalidatedPackageIdsJson = "[\"pkg-chapter-004\"]",
                RiskLevel = "medium",
                Recommendation = "第二版修订计划，不应出现在 v1 查询。",
                CreatedAt = DateTime.UtcNow.AddSeconds(2),
                UpdatedAt = DateTime.UtcNow.AddSeconds(2)
            });
        await db.SaveChangesAsync();
    }

    private static async Task SeedCrossVersionContaminatedEvidenceAsync(NovelAgentDbContext db)
    {
        var truthStore = new ProductionTruthStore(db);
        await truthStore.AppendEventAsync(new CreateProductionEventRequest(
            RuntimeRunId: "run-chapter-004-v2",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "project-1-chapter-004",
            PackageId: "pkg-chapter-004",
            EventType: "chapter_changes_recorded",
            Stage: "ChangesExtracted",
            Status: "completed",
            Message: "跨版本污染事件，不应出现在 v1 查询。",
            ArtifactType: "ChapterChange",
            ArtifactId: "changes-contaminated-v2-on-v1-package",
            DataJson: "{\"changes\":[\"跨版本污染\"]}"));
        await truthStore.CreateChapterChangeAsync(new CreateChapterChangeRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "run-chapter-004-v2",
            ChapterId: "project-1-chapter-004",
            PackageId: "pkg-chapter-004",
            ChangesJson: "{\"changes\":[\"跨版本污染\"]}",
            CanonicalChangesJson: "{\"changes\":[\"跨版本污染\"]}",
            ParseStatus: "parsed",
            ParseError: null,
            AppliedToFactSnapshot: true));
        await truthStore.CreateGenerationGateReportAsync(new CreateGenerationGateReportRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "run-chapter-004-v2",
            ChapterId: "project-1-chapter-004",
            PackageId: "pkg-chapter-004",
            ArtifactId: "gate-contaminated-v2-on-v1-package",
            Status: "validated",
            ReportJson: "{\"status\":\"contaminated\"}",
            ProtocolPassed: true,
            ChangesDetected: true,
            FactSnapshotPassed: true,
            BlueprintPassed: true,
            RagPassed: true,
            IssueCount: 0,
            RepairHintCount: 0,
            ValidatedAt: DateTime.UtcNow));
        await truthStore.CreateAgentReviewAsync(new CreateAgentReviewRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "run-chapter-004-v2",
            ChapterId: "project-1-chapter-004",
            PackageId: "pkg-chapter-004",
            ReviewId: "review-contaminated-v2-on-v1-package",
            OverallResult: "Pass",
            ValidationOverallResult: "validated",
            RequiresRewrite: false,
            QualityScore: 99,
            ContentLength: 1300,
            CheckCount: 6,
            Summary: "跨版本污染验收，不应出现在 v1 查询。",
            ReviewJson: "{\"decision\":\"contaminated\"}",
            ReviewedAt: DateTime.UtcNow));
    }
}
