using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.Creative;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Creative;

public sealed class RevisionPlanPackageInvalidationServiceTests
{
    [Fact]
    public async Task InvalidateAsync_MarksAffectedChapterAndDownstreamPackagesStale()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var productionEvents = new RecordingProductionEventWriter();
        var runtimeEvents = new RecordingRuntimeEventService();
        var outputArtifacts = new RecordingOutputArtifactRecorder();
        IRevisionPlanPackageInvalidationService service = new RevisionPlanPackageInvalidationService(
            db,
            productionEvents,
            runtimeEvents,
            outputArtifacts);

        var result = await service.InvalidateAsync(new InvalidateRevisionPlanPackagesRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            SessionId: "session-1",
            RuntimeRunId: "run-revision",
            RevisionPlanId: "revision-plan-1"));

        Assert.True(result.Success);
        Assert.Equal("ready_for_rebuild", result.Status);
        Assert.Contains(result.Packages, item => item.PackageId == "pkg-002" && item.CurrentStatus == "stale");
        Assert.Contains(result.Packages, item => item.PackageId == "pkg-003" && item.CurrentStatus == "stale");
        Assert.Contains(result.Packages, item => item.PackageId == "pkg-004" && item.CurrentStatus == "stale");

        var chapterOne = await db.TianmingPackages.SingleAsync(p => p.Id == "pkg-001");
        var otherProject = await db.TianmingPackages.SingleAsync(p => p.Id == "pkg-other-project");
        var plan = await db.RevisionPlans.SingleAsync(p => p.Id == "revision-plan-1");

        Assert.Equal("completed", chapterOne.Status);
        Assert.Equal("completed", otherProject.Status);
        Assert.Contains("pkg-002", plan.InvalidatedPackageIdsJson);
        Assert.Contains("pkg-003", plan.InvalidatedPackageIdsJson);
        Assert.Contains("pkg-004", plan.InvalidatedPackageIdsJson);
        Assert.Single(productionEvents.Events);
        Assert.Single(runtimeEvents.Events);
        var outputArtifact = Assert.Single(outputArtifacts.Requests);
        Assert.Equal("RevisionPlanPackageInvalidation", outputArtifact.ToolName);
        Assert.Equal("packages_invalidated", outputArtifact.Stage);
        Assert.Equal("revision_plan_packages_invalidated", outputArtifact.ArtifactType);
        Assert.Equal("revision-plan-1", outputArtifact.ArtifactId);
        Assert.Equal("ProcessArtifact", outputArtifact.OutputKind);
        Assert.Contains("创作工作流", outputArtifact.UserVisibleWhere);
        Assert.Contains("pkg-002", outputArtifact.Summary);
    }

    [Fact]
    public async Task InvalidateAsync_WithAlreadyInvalidatedRevisionPlanDoesNotDuplicateSideEffects()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var productionEvents = new RecordingProductionEventWriter();
        var runtimeEvents = new RecordingRuntimeEventService();
        var outputArtifacts = new RecordingOutputArtifactRecorder();
        IRevisionPlanPackageInvalidationService service = new RevisionPlanPackageInvalidationService(
            db,
            productionEvents,
            runtimeEvents,
            outputArtifacts);
        var request = new InvalidateRevisionPlanPackagesRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            SessionId: "session-1",
            RuntimeRunId: "run-revision",
            RevisionPlanId: "revision-plan-1");

        var first = await service.InvalidateAsync(request);
        var second = await service.InvalidateAsync(request);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(first.InvalidatedPackageIds.Order(StringComparer.OrdinalIgnoreCase), second.InvalidatedPackageIds.Order(StringComparer.OrdinalIgnoreCase));
        Assert.All(second.Packages, item => Assert.Equal("stale", item.CurrentStatus));
        Assert.Single(productionEvents.Events);
        Assert.Single(runtimeEvents.Events);
        Assert.Single(outputArtifacts.Requests);
    }

    [Fact]
    public async Task InvalidateAsync_NormalizesHistoricalLogicalRevisionPlanToCanonicalChapterIds()
    {
        await using var db = CreateDb();
        await SeedCanonicalAsync(db);
        var productionEvents = new RecordingProductionEventWriter();
        var runtimeEvents = new RecordingRuntimeEventService();
        IRevisionPlanPackageInvalidationService service = new RevisionPlanPackageInvalidationService(
            db,
            productionEvents,
            runtimeEvents);

        var result = await service.InvalidateAsync(new InvalidateRevisionPlanPackagesRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            SessionId: "session-1",
            RuntimeRunId: "run-revision",
            RevisionPlanId: "revision-plan-canonical"));

        Assert.True(result.Success);
        Assert.Equal(
            new[] { "project-1-chapter-002", "project-1-chapter-003", "project-1-chapter-004" },
            result.AffectedChapterIds);
        Assert.Contains(result.Packages, item => item.PackageId == "pkg-canonical-002" && item.ChapterId == "project-1-chapter-002");
        Assert.Contains(result.Packages, item => item.PackageId == "pkg-canonical-003" && item.ChapterId == "project-1-chapter-003");
        Assert.Contains(result.Packages, item => item.PackageId == "pkg-canonical-004" && item.ChapterId == "project-1-chapter-004");

        var plan = await db.RevisionPlans.SingleAsync(p => p.Id == "revision-plan-canonical");
        Assert.Equal("project-1-chapter-002", plan.TargetChapterId);
        Assert.Equal(
            new[] { "project-1-chapter-002", "project-1-chapter-003", "project-1-chapter-004" },
            ChapterIdentityResolver.ParseStringArray(plan.AffectedChapterIdsJson));
        Assert.Equal(
            new[] { "pkg-canonical-002", "pkg-canonical-003", "pkg-canonical-004" },
            ChapterIdentityResolver.ParseStringArray(plan.InvalidatedPackageIdsJson));

        var productionEvent = Assert.Single(productionEvents.Events);
        Assert.Equal("project-1-chapter-002", productionEvent.ChapterId);
        Assert.Contains("project-1-chapter-002", JsonSerializer.Serialize(productionEvent.Data));
        Assert.Contains("chapter-002", JsonSerializer.Serialize(productionEvent.Data));
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static async Task SeedAsync(NovelAgentDbContext db)
    {
        db.Users.Add(new User
        {
            Id = "user-1",
            Username = "author",
            Email = "author@example.com",
            PasswordHash = "hash",
            Role = "author"
        });
        db.NovelProjects.AddRange(
            new NovelProject
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "测试项目",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new NovelProject
            {
                Id = "project-2",
                UserId = "user-1",
                Title = "其他项目",
                Status = "Writing",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        db.TianmingPackages.AddRange(
            Package("pkg-001", "project-1", "chapter-001"),
            Package("pkg-002", "project-1", "chapter-002"),
            Package("pkg-003", "project-1", "chapter-003"),
            Package("pkg-004", "project-1", "chapter-004"),
            Package("pkg-other-project", "project-2", "chapter-003"));
        db.RevisionPlans.Add(new RevisionPlan
        {
            Id = "revision-plan-1",
            UserId = "user-1",
            ProjectId = "project-1",
            SessionId = "session-1",
            RuntimeRunId = "run-revision",
            Source = "user_request",
            PlanType = "chapter_rewrite",
            TargetScope = "chapter",
            TargetChapterId = "chapter-002",
            Status = "accepted",
            RequirementsJson = "[\"第二章改成怪物围攻\"]",
            ContinuityRequirementsJson = "[\"后续章节必须承接新版第二章\"]",
            AffectedChapterIdsJson = "[\"chapter-002\"]",
            InvalidatedPackageIdsJson = "[]",
            RiskLevel = "high",
            Recommendation = "重写第二章，并让后续章节生产包过期。",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedCanonicalAsync(NovelAgentDbContext db)
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
            Title = "Canonical 修订计划项目",
            Status = "Writing",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.Chapters.AddRange(
            Chapter("project-1-chapter-001", 1, "第一章"),
            Chapter("project-1-chapter-002", 2, "第二章"),
            Chapter("project-1-chapter-003", 3, "第三章"),
            Chapter("project-1-chapter-004", 4, "第四章"));
        db.TianmingPackages.AddRange(
            Package("pkg-canonical-001", "project-1", "project-1-chapter-001"),
            Package("pkg-canonical-002", "project-1", "project-1-chapter-002"),
            Package("pkg-canonical-003", "project-1", "project-1-chapter-003"),
            Package("pkg-canonical-004", "project-1", "project-1-chapter-004"));
        db.RevisionPlans.Add(new RevisionPlan
        {
            Id = "revision-plan-canonical",
            UserId = "user-1",
            ProjectId = "project-1",
            SessionId = "session-1",
            RuntimeRunId = "run-revision",
            Source = "user_request",
            PlanType = "chapter_rewrite",
            TargetScope = "chapter",
            TargetChapterId = "chapter-002",
            Status = "accepted",
            RequirementsJson = "[\"第二章改成怪物围攻\"]",
            ContinuityRequirementsJson = "[\"第三章以后必须承接新版第二章\"]",
            AffectedChapterIdsJson = "[\"chapter-002\"]",
            InvalidatedPackageIdsJson = "[]",
            RiskLevel = "high",
            Recommendation = "重写第二章，并让后续正式生产包过期。",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static Chapter Chapter(string id, int chapterNumber, string title) => new()
    {
        Id = id,
        ProjectId = "project-1",
        Title = title,
        ChapterNumber = chapterNumber,
        Status = "committed",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static TianmingPackage Package(string id, string projectId, string chapterId) => new()
    {
        Id = id,
        UserId = "user-1",
        ProjectId = projectId,
        ChapterId = chapterId,
        RuntimeRunId = $"run-{id}",
        PackageKind = "chapter_generation",
        Status = "completed",
        InputJson = "{}",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private sealed class RecordingProductionEventWriter : IProductionEventWriter
    {
        public List<AppendChapterProductionEventRequest> Events { get; } = new();

        public Task<ProductionEvent> AppendChapterStageAsync(
            AppendChapterProductionEventRequest request,
            CancellationToken cancellationToken = default)
        {
            Events.Add(request);
            return Task.FromResult(new ProductionEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                RuntimeRunId = request.RuntimeRunId,
                UserId = request.UserId,
                ProjectId = request.ProjectId,
                ChapterId = request.ChapterId,
                PackageId = request.PackageId,
                EventType = request.EventType,
                Stage = request.Stage,
                Status = request.Status,
                Message = request.Message,
                ArtifactType = request.ArtifactType,
                ArtifactId = request.ArtifactId,
                CreatedAt = DateTime.UtcNow
            });
        }

        public Task<ChapterDraft> AppendChapterDraftAsync(
            AppendChapterDraftRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChapterDraft
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = request.UserId,
                ProjectId = request.ProjectId,
                RuntimeRunId = request.RuntimeRunId,
                ChapterId = request.ChapterId,
                PackageId = request.PackageId,
                ArtifactId = request.ArtifactId,
                Status = request.Status,
                DraftContent = request.DraftContent,
                ChangesJson = request.ChangesJson,
                ContentLength = request.DraftContent.Length,
                RepairAttemptCount = request.RepairAttemptCount,
                HasChanges = request.HasChanges,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

        public Task<ChapterChange> AppendChapterChangeAsync(
            AppendChapterChangeRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChapterChange
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = request.UserId,
                ProjectId = request.ProjectId,
                RuntimeRunId = request.RuntimeRunId,
                ChapterId = request.ChapterId,
                PackageId = request.PackageId,
                ChangesJson = request.ChangesJson,
                CanonicalChangesJson = request.CanonicalChangesJson,
                ParseStatus = request.ParseStatus,
                ParseError = request.ParseError,
                AppliedToFactSnapshot = request.AppliedToFactSnapshot,
                CreatedAt = DateTime.UtcNow
            });

        public Task<GenerationGateReportRecord> AppendGenerationGateReportAsync(
            AppendGenerationGateReportRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GenerationGateReportRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = request.UserId,
                ProjectId = request.ProjectId,
                RuntimeRunId = request.RuntimeRunId,
                ChapterId = request.ChapterId,
                PackageId = request.PackageId,
                ArtifactId = request.ArtifactId,
                Status = request.Status,
                ReportJson = request.ReportJson,
                ProtocolPassed = request.ProtocolPassed,
                ChangesDetected = request.ChangesDetected,
                FactSnapshotPassed = request.FactSnapshotPassed,
                BlueprintPassed = request.BlueprintPassed,
                RagPassed = request.RagPassed,
                IssueCount = request.IssueCount,
                RepairHintCount = request.RepairHintCount,
                ValidatedAt = request.ValidatedAt,
                CreatedAt = DateTime.UtcNow
            });

        public Task<AgentReviewRecord> AppendAgentReviewAsync(
            AppendAgentReviewRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentReviewRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = request.UserId,
                ProjectId = request.ProjectId,
                RuntimeRunId = request.RuntimeRunId,
                ChapterId = request.ChapterId,
                PackageId = request.PackageId,
                ReviewId = request.ReviewId,
                OverallResult = request.OverallResult,
                ValidationOverallResult = request.ValidationOverallResult,
                RequiresRewrite = request.RequiresRewrite,
                QualityScore = request.QualityScore,
                ContentLength = request.ContentLength,
                CheckCount = request.CheckCount,
                Summary = request.Summary,
                ReviewJson = request.ReviewJson,
                ReviewedAt = request.ReviewedAt,
                CreatedAt = DateTime.UtcNow
            });
    }

    private sealed class RecordingRuntimeEventService : IAgentRuntimeEventService
    {
        public List<CreateAgentRuntimeEventRequest> Events { get; } = new();

        public Task<AgentRuntimeEvent> AppendAsync(
            CreateAgentRuntimeEventRequest request,
            CancellationToken ct = default)
        {
            Events.Add(request);
            return Task.FromResult(new AgentRuntimeEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                RuntimeRunId = request.RuntimeRunId,
                UserId = request.UserId,
                SessionId = request.SessionId,
                ProjectId = request.ProjectId,
                Type = request.Type,
                Stage = request.Stage,
                Status = request.Status,
                Message = request.Message,
                CreatedAt = DateTime.UtcNow
            });
        }

        public Task<IReadOnlyList<AgentRuntimeEvent>> GetRecentAsync(
            string userId,
            string sessionId,
            int limit = 50,
            CancellationToken ct = default,
            string? afterEventId = null) =>
            Task.FromResult<IReadOnlyList<AgentRuntimeEvent>>(Array.Empty<AgentRuntimeEvent>());

        public Task<IReadOnlyList<AgentRuntimeEvent>> GetForRunAsync(
            string runtimeRunId,
            int limit = 100,
            CancellationToken ct = default,
            string? afterEventId = null) =>
            Task.FromResult<IReadOnlyList<AgentRuntimeEvent>>(Array.Empty<AgentRuntimeEvent>());
    }

    private sealed class RecordingOutputArtifactRecorder : IOutputArtifactRecorder
    {
        public List<OutputArtifactRecordRequest> Requests { get; } = new();

        public Task<OutputArtifactRecordResult> RecordAsync(
            OutputArtifactRecordRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new OutputArtifactRecordResult(
                new ProductionEvent
                {
                    Id = Guid.NewGuid().ToString("N"),
                    RuntimeRunId = request.RuntimeRunId,
                    UserId = request.UserId,
                    ProjectId = request.ProjectId,
                    ChapterId = request.ChapterId,
                    PackageId = request.PackageId,
                    EventType = OutputArtifactRecorder.EventType,
                    Stage = request.Stage,
                    Status = request.Status,
                    Message = request.Summary,
                    ArtifactType = request.ArtifactType,
                    ArtifactId = request.ArtifactId,
                    CreatedAt = DateTime.UtcNow
                },
                new TM.Web.NovelAgentWeb.Support.AgentToolProducedArtifact
                {
                    ArtifactType = request.ArtifactType,
                    ArtifactId = request.ArtifactId,
                    OutputKind = request.OutputKind,
                    Summary = request.Summary
                }));
        }
    }
}
