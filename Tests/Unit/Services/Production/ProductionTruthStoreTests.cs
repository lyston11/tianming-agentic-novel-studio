using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Production;

public class ProductionTruthStoreTests
{
    [Fact]
    public async Task CreateChapterVersionAsync_AppendsVersionAndUpdatesChapterCurrentDocument()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        var store = new ProductionTruthStore(db);

        var first = await store.CreateChapterVersionAsync(new CreateChapterVersionRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "chapter-001",
            ContentDocumentId: "doc-1",
            Title: "第一章 旧邮徽",
            WordCount: 3200,
            Status: "committed",
            RuntimeRunId: "run-1",
            PackageId: "pkg-1",
            GateReportJson: "{\"passed\":true}",
            AgentReviewJson: "{\"decision\":\"commit\"}"));

        var second = await store.CreateChapterVersionAsync(new CreateChapterVersionRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "chapter-001",
            ContentDocumentId: "doc-2",
            Title: "第一章 旧邮徽重写版",
            WordCount: 3500,
            Status: "committed",
            RuntimeRunId: "run-2",
            PackageId: "pkg-2",
            GateReportJson: "{\"passed\":true}",
            AgentReviewJson: "{\"decision\":\"commit\"}"));

        var chapter = await db.Chapters.SingleAsync(c => c.Id == "chapter-001");

        Assert.Equal(1, first.VersionNumber);
        Assert.Equal(2, second.VersionNumber);
        Assert.Equal("doc-2", chapter.CurrentDocumentId);
        Assert.Equal("第一章 旧邮徽重写版", chapter.Title);
        Assert.Equal(3500, chapter.WordCount);
    }

    [Fact]
    public async Task CreatePackageAsync_PersistsReplayableProductionInputs()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        var store = new ProductionTruthStore(db);

        var package = await store.CreatePackageAsync(new CreateTianmingPackageRequest(
            Id: "pkg-1",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "chapter-001",
            RuntimeRunId: "run-1",
            PackageKind: "chapter_generation",
            InputJson: "{\"goal\":\"写第一章\"}",
            DependencyVersionsJson: "{\"storyBible\":3,\"blueprint\":2}",
            KnowledgeSnapshotJson: "{\"bindings\":[\"kb-1\"]}",
            FactSnapshotJson: "{\"protagonist\":\"林岑\"}",
            PromptVersion: "chapter-v1",
            KernelVersion: "tianming-kernel-v1"));

        Assert.Equal("pkg-1", package.Id);
        Assert.Equal("pending", package.Status);
        Assert.Contains("\"storyBible\":3", package.DependencyVersionsJson);
        Assert.Equal("tianming-kernel-v1", package.KernelVersion);
    }

    [Fact]
    public async Task AppendEventAsync_AllowsRunAndChapterTimelineQueries()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        var store = new ProductionTruthStore(db);

        await store.AppendEventAsync(new CreateProductionEventRequest(
            RuntimeRunId: "run-1",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "chapter-001",
            PackageId: "pkg-1",
            EventType: "kernel_gate",
            Stage: "KernelGate",
            Status: "completed",
            Message: "门禁通过",
            ArtifactType: "GateReport",
            ArtifactId: "gate-1",
            DataJson: "{\"score\":92}"));

        var runEvents = await store.GetEventsForRunAsync("run-1");
        var chapterEvents = await store.GetEventsForChapterAsync("project-1", "chapter-001");

        Assert.Single(runEvents);
        Assert.Single(chapterEvents);
        Assert.Equal("KernelGate", runEvents[0].Stage);
        Assert.Equal("GateReport", chapterEvents[0].ArtifactType);
    }

    [Fact]
    public async Task AppendEventAsync_SyncsPackageStatusWhenEventReferencesPackage()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        var store = new ProductionTruthStore(db);
        var package = await store.CreatePackageAsync(new CreateTianmingPackageRequest(
            Id: "pkg-status-1",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "chapter-001",
            RuntimeRunId: "run-1",
            PackageKind: "chapter_generation",
            InputJson: "{}",
            DependencyVersionsJson: null,
            KnowledgeSnapshotJson: null,
            FactSnapshotJson: null,
            PromptVersion: "chapter-v1",
            KernelVersion: "tianming-kernel-v1"));

        await store.AppendEventAsync(new CreateProductionEventRequest(
            RuntimeRunId: "run-1",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "chapter-001",
            PackageId: package.Id,
            EventType: "kernel_gate",
            Stage: "KernelGate",
            Status: "completed",
            Message: "门禁通过",
            ArtifactType: "GateReport",
            ArtifactId: "gate-1",
            DataJson: "{\"score\":92}"));

        var savedPackage = await db.TianmingPackages.AsNoTracking().SingleAsync(p => p.Id == package.Id);
        Assert.Equal("completed", savedPackage.Status);
        Assert.True(savedPackage.UpdatedAt >= savedPackage.CreatedAt);
    }

    [Fact]
    public async Task AppendEventAsync_WithSameStageArtifactAndPayloadReusesExistingEvent()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        var store = new ProductionTruthStore(db);
        var request = new CreateProductionEventRequest(
            RuntimeRunId: "run-duplicate-stage",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "chapter-001",
            PackageId: "pkg-duplicate-stage",
            EventType: "chapter_gate_validated",
            Stage: "GateValidated",
            Status: "failed",
            Message: "章节草稿未通过硬门禁：缺少 CHANGES。",
            ArtifactType: "generation_gate_report",
            ArtifactId: "gate_failed",
            DataJson: "{\"issues\":[\"missing_changes\"]}");

        var first = await store.AppendEventAsync(request);
        var second = await store.AppendEventAsync(request);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await db.ProductionEvents
            .Where(evt =>
                evt.RuntimeRunId == "run-duplicate-stage" &&
                evt.ChapterId == "chapter-001" &&
                evt.PackageId == "pkg-duplicate-stage" &&
                evt.EventType == "chapter_gate_validated")
            .ToListAsync());
    }

    [Fact]
    public async Task CreateChapterDraftAsync_WithSameRunAndArtifactIdReusesExistingDraft()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        var store = new ProductionTruthStore(db);
        var request = new CreateChapterDraftRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "run-draft",
            ChapterId: "chapter-001",
            PackageId: "pkg-draft",
            ArtifactId: "draft-artifact-001",
            Status: "draft_generated",
            DraftContent: "第一章 正文",
            ChangesJson: "{\"facts\":[\"邮徽不能攻击\"]}",
            RepairAttemptCount: 0,
            HasChanges: true);

        var first = await store.CreateChapterDraftAsync(request);
        var second = await store.CreateChapterDraftAsync(request);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await db.ChapterDrafts
            .Where(draft =>
                draft.RuntimeRunId == "run-draft" &&
                draft.ChapterId == "chapter-001" &&
                draft.ArtifactId == "draft-artifact-001")
            .ToListAsync());
    }

    [Fact]
    public async Task CreateChapterDraftAsync_WithSameRunAndArtifactIdUpdatesRepairedDraft()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        var store = new ProductionTruthStore(db);

        var first = await store.CreateChapterDraftAsync(new CreateChapterDraftRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "run-draft-repair",
            ChapterId: "chapter-001",
            PackageId: "pkg-draft",
            ArtifactId: "draft-artifact-001",
            Status: "gate_failed",
            DraftContent: "第一章 初稿",
            ChangesJson: null,
            RepairAttemptCount: 0,
            HasChanges: false));

        var second = await store.CreateChapterDraftAsync(new CreateChapterDraftRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "run-draft-repair",
            ChapterId: "chapter-001",
            PackageId: "pkg-draft",
            ArtifactId: "draft-artifact-001",
            Status: "validated",
            DraftContent: "第一章 修复后正文",
            ChangesJson: "{\"CharacterStateChanges\":[]}",
            RepairAttemptCount: 2,
            HasChanges: true));

        var saved = await db.ChapterDrafts.SingleAsync(draft =>
            draft.RuntimeRunId == "run-draft-repair" &&
            draft.ChapterId == "chapter-001" &&
            draft.ArtifactId == "draft-artifact-001");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("validated", saved.Status);
        Assert.Equal("第一章 修复后正文", saved.DraftContent);
        Assert.Equal("{\"CharacterStateChanges\":[]}", saved.ChangesJson);
        Assert.Equal(2, saved.RepairAttemptCount);
        Assert.True(saved.HasChanges);
        Assert.True(saved.UpdatedAt >= first.UpdatedAt);
    }

    [Fact]
    public async Task CreateGenerationGateReportAsync_WithSameRunArtifactAndReportReusesExistingReport()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        var store = new ProductionTruthStore(db);
        var request = new CreateGenerationGateReportRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "run-gate",
            ChapterId: "chapter-001",
            PackageId: "pkg-gate",
            ArtifactId: "gate-artifact-001",
            Status: "validated",
            ReportJson: "{\"status\":\"validated\",\"issues\":[]}",
            ProtocolPassed: true,
            ChangesDetected: true,
            FactSnapshotPassed: true,
            BlueprintPassed: true,
            RagPassed: true,
            IssueCount: 0,
            RepairHintCount: 0,
            ValidatedAt: new DateTime(2026, 6, 23, 8, 0, 0, DateTimeKind.Utc));

        var first = await store.CreateGenerationGateReportAsync(request);
        var second = await store.CreateGenerationGateReportAsync(request);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await db.GenerationGateReports
            .Where(report =>
                report.RuntimeRunId == "run-gate" &&
                report.ChapterId == "chapter-001" &&
                report.PackageId == "pkg-gate" &&
                report.ArtifactId == "gate-artifact-001")
            .ToListAsync());
    }

    [Fact]
    public async Task SaveFactSnapshotAsync_VersionsAndReturnsLatestSnapshot()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        var store = new ProductionTruthStore(db);

        var first = await store.SaveFactSnapshotAsync(new SaveProjectFactSnapshotRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "chapter-001",
            ChapterVersionId: "version-1",
            SnapshotJson: "{\"protagonist\":\"林岑\",\"state\":\"获得银蓝邮徽\"}",
            Source: "chapter_commit"));

        var second = await store.SaveFactSnapshotAsync(new SaveProjectFactSnapshotRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "chapter-001",
            ChapterVersionId: "version-2",
            SnapshotJson: "{\"protagonist\":\"林岑\",\"state\":\"逃入旧邮路\"}",
            Source: "chapter_commit"));

        var latest = await store.GetLatestFactSnapshotAsync("project-1", "chapter-001");

        Assert.Equal(1, first.VersionNumber);
        Assert.Equal(2, second.VersionNumber);
        Assert.NotNull(latest);
        Assert.Contains("逃入旧邮路", latest!.SnapshotJson);
    }

    [Fact]
    public async Task GetLatestFactSnapshotAsync_WithoutChapterId_ReturnsLatestSnapshotAcrossProject()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        db.Chapters.Add(new Chapter
        {
            Id = "chapter-002",
            ProjectId = "project-1",
            Title = "第二章 蓝磷骨光",
            ChapterNumber = 2,
            Status = "draft",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var store = new ProductionTruthStore(db);

        var firstChapterSnapshot = await store.SaveFactSnapshotAsync(new SaveProjectFactSnapshotRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "chapter-001",
            ChapterVersionId: "version-1",
            SnapshotJson: "{\"chapter\":1,\"ending\":\"林岑握住银蓝邮徽\"}",
            Source: "chapter_commit"));

        var secondChapterSnapshot = await store.SaveFactSnapshotAsync(new SaveProjectFactSnapshotRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "chapter-002",
            ChapterVersionId: "version-2",
            SnapshotJson: "{\"chapter\":2,\"ending\":\"林岑看见蓝磷骨光\"}",
            Source: "chapter_commit"));

        firstChapterSnapshot.CreatedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        secondChapterSnapshot.CreatedAt = new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc);
        await db.SaveChangesAsync();

        var projectLatest = await store.GetLatestFactSnapshotAsync("project-1");
        var chapterOneLatest = await store.GetLatestFactSnapshotAsync("project-1", "chapter-001");

        Assert.NotNull(projectLatest);
        Assert.Equal("chapter-002", projectLatest!.ChapterId);
        Assert.Contains("蓝磷骨光", projectLatest.SnapshotJson);
        Assert.NotNull(chapterOneLatest);
        Assert.Equal("chapter-001", chapterOneLatest!.ChapterId);
        Assert.Contains("银蓝邮徽", chapterOneLatest.SnapshotJson);
    }

    [Fact]
    public async Task GetLatestFactSnapshotAsync_DoesNotCrossProjectBoundary()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-2",
            UserId = "user-1",
            Title = "另一本书",
            Genre = "都市",
            Status = "draft",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.Chapters.Add(new Chapter
        {
            Id = "project-2-chapter-001",
            ProjectId = "project-2",
            Title = "第一章 霓虹雨",
            ChapterNumber = 1,
            Status = "draft",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var store = new ProductionTruthStore(db);

        var projectOneSnapshot = await store.SaveFactSnapshotAsync(new SaveProjectFactSnapshotRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "chapter-001",
            ChapterVersionId: "version-1",
            SnapshotJson: "{\"protagonist\":\"林岑\",\"ending\":\"旧邮路开启\"}",
            Source: "chapter_commit"));

        var projectTwoSnapshot = await store.SaveFactSnapshotAsync(new SaveProjectFactSnapshotRequest(
            UserId: "user-1",
            ProjectId: "project-2",
            ChapterId: "project-2-chapter-001",
            ChapterVersionId: "version-other",
            SnapshotJson: "{\"protagonist\":\"沈烁\",\"ending\":\"霓虹雨落下\"}",
            Source: "chapter_commit"));

        projectOneSnapshot.CreatedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        projectTwoSnapshot.CreatedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        await db.SaveChangesAsync();

        var projectOneLatest = await store.GetLatestFactSnapshotAsync("project-1");

        Assert.NotNull(projectOneLatest);
        Assert.Equal("project-1", projectOneLatest!.ProjectId);
        Assert.Contains("林岑", projectOneLatest.SnapshotJson);
        Assert.DoesNotContain("沈烁", projectOneLatest.SnapshotJson);
    }

    [Fact]
    public async Task OutboxEvent_CanBeEnqueuedAndMarkedCompletedOrRetryableFailed()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        var store = new ProductionTruthStore(db);

        var queued = await store.EnqueueOutboxAsync(new EnqueueOutboxEventRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "run-1",
            EventType: "index_chapter",
            AggregateType: "chapter_version",
            AggregateId: "version-1",
            PayloadJson: "{\"documentId\":\"doc-1\"}"));

        await store.MarkOutboxFailedAsync(queued.Id, "Qdrant timeout", retryable: true, nextAttemptAt: DateTime.UtcNow.AddMinutes(5));
        var failed = await db.OutboxEvents.AsNoTracking().SingleAsync(e => e.Id == queued.Id);

        Assert.Equal("retryable_failed", failed.Status);
        Assert.Equal(1, failed.Attempts);

        await store.MarkOutboxCompletedAsync(queued.Id);
        var completed = await db.OutboxEvents.AsNoTracking().SingleAsync(e => e.Id == queued.Id);

        Assert.Equal("completed", completed.Status);
        Assert.NotNull(completed.CompletedAt);
    }

    [Fact]
    public async Task EnqueueOutboxAsync_ReusesEventWithSameIdempotencyKey()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        var store = new ProductionTruthStore(db);
        var request = new EnqueueOutboxEventRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "run-1",
            EventType: "finalize_chapter_commit_metadata",
            AggregateType: "chapter",
            AggregateId: "chapter-001",
            PayloadJson: "{\"version\":1}",
            IdempotencyKey: "chapter-001:run-1:finalize");

        var first = await store.EnqueueOutboxAsync(request);
        var second = await store.EnqueueOutboxAsync(request with { PayloadJson = "{\"version\":2}" });

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await db.OutboxEvents.ToListAsync());
    }

    [Fact]
    public async Task AttachLatestChapterVersionToRunAsync_LinksLatestVersionAndOutboxToRuntimeRun()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        var store = new ProductionTruthStore(db);

        var first = await store.CreateChapterVersionAsync(new CreateChapterVersionRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "chapter-001",
            ContentDocumentId: "doc-1",
            Title: "第一章 旧邮徽",
            WordCount: 3200,
            Status: "committed",
            RuntimeRunId: null,
            PackageId: null,
            GateReportJson: null,
            AgentReviewJson: null));
        var second = await store.CreateChapterVersionAsync(new CreateChapterVersionRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "chapter-001",
            ContentDocumentId: "doc-2",
            Title: "第一章 旧邮徽重写版",
            WordCount: 3500,
            Status: "committed",
            RuntimeRunId: null,
            PackageId: null,
            GateReportJson: null,
            AgentReviewJson: null));
        await store.EnqueueOutboxAsync(new EnqueueOutboxEventRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: null,
            EventType: "index_chapter_content",
            AggregateType: "chapter_version",
            AggregateId: second.Id,
            PayloadJson: "{}"));

        var linked = await store.AttachLatestChapterVersionToRunAsync(
            "project-1",
            "chapter-001",
            "run-2",
            PackageId: "pkg-2",
            GateReportJson: "{\"status\":\"validated\"}",
            AgentReviewJson: "{\"decision\":\"commit\"}");

        var firstReloaded = await db.ChapterVersions.SingleAsync(v => v.Id == first.Id);
        var secondReloaded = await db.ChapterVersions.SingleAsync(v => v.Id == second.Id);
        var outbox = await db.OutboxEvents.SingleAsync(e => e.AggregateId == second.Id);

        Assert.Equal(second.Id, linked.Id);
        Assert.Null(firstReloaded.RuntimeRunId);
        Assert.Equal("run-2", secondReloaded.RuntimeRunId);
        Assert.Equal("pkg-2", secondReloaded.PackageId);
        Assert.Contains("validated", secondReloaded.GateReportJson);
        Assert.Contains("commit", secondReloaded.AgentReviewJson);
        Assert.Equal("run-2", outbox.RuntimeRunId);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new NovelAgentDbContext(options);
    }

    private static void SeedProjectChapterAndDocuments(NovelAgentDbContext db)
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
            Title = "chapter-001",
            ChapterNumber = 1,
            Status = "draft",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        db.ContentDocuments.AddRange(
            new ContentDocument
            {
                Id = "doc-1",
                UserId = "user-1",
                ProjectId = "project-1",
                SourceType = "chapter",
                SourceId = "chapter-001",
                DocumentRole = "final",
                Title = "第一章 旧邮徽",
                ContentHash = "hash-1",
                Version = 1
            },
            new ContentDocument
            {
                Id = "doc-2",
                UserId = "user-1",
                ProjectId = "project-1",
                SourceType = "chapter",
                SourceId = "chapter-001",
                DocumentRole = "final",
                Title = "第一章 旧邮徽重写版",
                ContentHash = "hash-2",
                Version = 2
            });

        db.SaveChanges();
    }
}
