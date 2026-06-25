using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.NovelAgent.Models;
using Microsoft.Extensions.Logging.Abstractions;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.Creative;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Knowledge;

public sealed class KnowledgeConflictDetectorTests
{
    [Fact]
    public async Task DetectAsync_PersistsLlmConflictReportForCurrentProjectOnly()
    {
        await using var db = CreateDb();
        Seed(db);
        var model = new FakeKnowledgeConflictModelClient(new KnowledgeConflictDecision
        {
            Model = "fake-conflict-llm",
            HasConflict = true,
            ConflictType = "HardConstraintContradiction",
            Severity = "Hard",
            ImpactScope = "ProjectWide",
            ConflictingKnowledgeIds = new List<string> { "knowledge-boundary" },
            Explanation = "银蓝邮徽不能攻击与银蓝邮徽释放蓝焰攻击互相冲突。",
            RecommendedAction = "询问用户保留能力边界，还是修改新知识为非攻击表现。",
            RequiresUserDecision = true,
            RawJson = """
            {
              "hasConflict": true,
              "conflictType": "HardConstraintContradiction",
              "severity": "Hard",
              "impactScope": "ProjectWide",
              "conflictingKnowledgeIds": ["knowledge-boundary"],
              "explanation": "银蓝邮徽不能攻击与银蓝邮徽释放蓝焰攻击互相冲突。",
              "recommendedAction": "询问用户保留能力边界，还是修改新知识为非攻击表现。",
              "requiresUserDecision": true
            }
            """
        });
        var canonStatus = new RecordingKnowledgeCanonConflictStatusService();
        IKnowledgeConflictDetector detector = new KnowledgeConflictDetector(
            db,
            model,
            NullLogger<KnowledgeConflictDetector>.Instance,
            canonStatus);

        var result = await detector.DetectAsync(new KnowledgeConflictDetectionRequest(
            UserId: "user-1",
            ProjectId: "project-a",
            KnowledgeId: "knowledge-blue-flame",
            SessionId: "session-1",
            RunId: "run-1"));

        Assert.True(result.HasConflict);
        Assert.Equal("HardConstraintContradiction", result.ConflictType);
        Assert.Equal("Hard", result.Severity);
        Assert.Contains("knowledge-boundary", result.ConflictingKnowledgeIds);
        Assert.True(result.RequiresUserDecision);

        var saved = await db.KnowledgeConflictReports.SingleAsync();
        Assert.Equal("user-1", saved.UserId);
        Assert.Equal("project-a", saved.ProjectId);
        Assert.Equal("knowledge-blue-flame", saved.KnowledgeId);
        Assert.Contains("knowledge-boundary", saved.ConflictingKnowledgeIdsJson);
        Assert.Contains("hasConflict", saved.DetectionJson);
        Assert.Equal("open", saved.Status);
        Assert.Equal("session-1", saved.SourceSessionId);
        Assert.Equal("run-1", saved.SourceRunId);

        Assert.DoesNotContain(model.LastPrompt!.ExistingKnowledge, item => item.KnowledgeId == "knowledge-other-project");
        var statusRequest = Assert.Single(canonStatus.Requests);
        Assert.Equal("knowledge-blue-flame", statusRequest.KnowledgeId);
        Assert.True(statusRequest.HasConflict);
        Assert.Equal(saved.Id, statusRequest.ReportId);
        Assert.Equal("Hard", statusRequest.Severity);
        Assert.Contains("knowledge-boundary", statusRequest.ConflictingKnowledgeIds);
    }

    [Fact]
    public async Task ResolveAsync_UpdatesCurrentProjectConflictWithoutTouchingOtherProjects()
    {
        await using var db = CreateDb();
        Seed(db);
        db.KnowledgeConflictReports.AddRange(
            new KnowledgeConflictReport
            {
                Id = "conflict-a",
                UserId = "user-1",
                ProjectId = "project-a",
                KnowledgeId = "knowledge-blue-flame",
                ConflictingKnowledgeIdsJson = "[\"knowledge-boundary\"]",
                ConflictType = "HardConstraintContradiction",
                Severity = "Hard",
                ImpactScope = "ProjectWide",
                Explanation = "银蓝邮徽不能攻击与蓝焰攻击冲突。",
                RecommendedAction = "询问用户选择。",
                RequiresUserDecision = true,
                Status = "open",
                DetectionJson = "{}",
                CreatedAt = DateTime.UtcNow
            },
            new KnowledgeConflictReport
            {
                Id = "conflict-b",
                UserId = "user-1",
                ProjectId = "project-b",
                KnowledgeId = "knowledge-other-project",
                ConflictingKnowledgeIdsJson = "[]",
                ConflictType = "HardConstraintContradiction",
                Severity = "Hard",
                ImpactScope = "ProjectWide",
                Explanation = "其他项目冲突。",
                RecommendedAction = "不要被项目A修改。",
                RequiresUserDecision = true,
                Status = "open",
                DetectionJson = "{}",
                CreatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();
        var canonStatus = new RecordingKnowledgeCanonConflictStatusService();
        IKnowledgeConflictResolver resolver = new KnowledgeConflictResolver(
            db,
            canonConflictStatus: canonStatus);

        var result = await resolver.ResolveAsync(new KnowledgeConflictResolutionRequest(
            UserId: "user-1",
            ProjectId: "project-a",
            ConflictId: "conflict-a",
            Decision: "resolved",
            Note: "用户确认保留能力边界，蓝焰攻击改为环境现象。",
            SessionId: "session-1",
            RunId: "run-1"));

        Assert.Equal("conflict-a", result.ConflictId);
        Assert.Equal("resolved", result.Status);
        Assert.Contains("能力边界", result.Note);

        var current = await db.KnowledgeConflictReports.SingleAsync(x => x.Id == "conflict-a");
        Assert.Equal("resolved", current.Status);
        Assert.NotNull(current.ResolvedAt);
        Assert.Equal("session-1", current.ResolvedBySessionId);
        Assert.Equal("run-1", current.ResolvedByRunId);
        Assert.Contains("能力边界", current.ResolutionNote);

        var otherProject = await db.KnowledgeConflictReports.SingleAsync(x => x.Id == "conflict-b");
        Assert.Equal("open", otherProject.Status);
        Assert.Null(otherProject.ResolvedAt);

        var resolutionStatus = Assert.Single(canonStatus.ResolutionRequests);
        Assert.Equal("knowledge-blue-flame", resolutionStatus.KnowledgeId);
        Assert.Equal("conflict-a", resolutionStatus.ReportId);
        Assert.Equal("resolved", resolutionStatus.ResolutionStatus);
        Assert.Contains("蓝焰攻击改为环境现象", resolutionStatus.ResolutionNote);
        Assert.Contains("knowledge-boundary", resolutionStatus.ConflictingKnowledgeIds);
    }

    [Fact]
    public async Task ResolveAsync_AppendsProductionRecoverySignalForResolvedHardConflict()
    {
        await using var db = CreateDb();
        Seed(db);
        db.AgentRuntimeRuns.Add(new AgentRuntimeRun
        {
            Id = "run-1",
            UserId = "user-1",
            SessionId = "session-1",
            ProjectId = "project-a",
            Status = "running",
            Mode = "production",
            CurrentPhase = NovelAgentProductionStages.ContextPackage,
            ActiveTool = "ProduceChapter",
            UserMessage = "继续写第二章",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.TianmingPackages.Add(new TianmingPackage
        {
            Id = "pkg-old-context",
            UserId = "user-1",
            ProjectId = "project-a",
            ChapterId = "chapter-002",
            RuntimeRunId = "run-1",
            PackageKind = "chapter_context_package",
            Status = "completed",
            InputJson = "{}",
            KnowledgeSnapshotJson = "{}",
            FactSnapshotJson = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.KnowledgeConflictReports.Add(new KnowledgeConflictReport
        {
            Id = "conflict-a",
            UserId = "user-1",
            ProjectId = "project-a",
            KnowledgeId = "knowledge-blue-flame",
            ConflictingKnowledgeIdsJson = "[\"knowledge-boundary\"]",
            ConflictType = "HardConstraintContradiction",
            Severity = "Hard",
            ImpactScope = "ProjectWide",
            Explanation = "银蓝邮徽不能攻击与蓝焰攻击冲突。",
            RecommendedAction = "询问用户选择。",
            RequiresUserDecision = true,
            Status = "open",
            DetectionJson = "{}",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        IProductionTruthStore truthStore = new ProductionTruthStore(db);
        IProductionEventWriter productionEvents = new ProductionEventWriter(truthStore);
        IOutputArtifactRecorder outputArtifacts = new OutputArtifactRecorder(productionEvents);
        IAgentRuntimeEventService runtimeEvents = new AgentRuntimeEventService(db);
        ICreativeIntentService creativeIntents = new CreativeIntentService(db);
        IRevisionPlanService revisionPlans = new RevisionPlanService(db);
        IKnowledgeConflictResolver resolver = new KnowledgeConflictResolver(
            db,
            productionEvents,
            runtimeEvents,
            creativeIntents,
            revisionPlans,
            outputArtifacts: outputArtifacts);

        await resolver.ResolveAsync(new KnowledgeConflictResolutionRequest(
            UserId: "user-1",
            ProjectId: "project-a",
            ConflictId: "conflict-a",
            Decision: "resolved",
            Note: "用户确认保留能力边界，蓝焰改为环境现象。",
            SessionId: "session-1",
            RunId: "run-1"));

        var productionEvent = await db.ProductionEvents.SingleAsync(evt => evt.EventType == "knowledge_conflict_resolved");
        Assert.Equal("knowledge_conflict_resolved", productionEvent.EventType);
        Assert.Equal(NovelAgentProductionStages.KnowledgeResolved, productionEvent.Stage);
        Assert.Equal("ready_for_rebuild", productionEvent.Status);
        Assert.Equal("knowledge_conflict_report", productionEvent.ArtifactType);
        Assert.Equal("conflict-a", productionEvent.ArtifactId);
        Assert.Contains("\"recommendedAction\":\"ProduceChapter\"", productionEvent.DataJson);
        Assert.Contains("pkg-old-context", productionEvent.DataJson);

        var outputArtifact = await db.ProductionEvents.SingleAsync(evt =>
            evt.EventType == OutputArtifactRecorder.EventType &&
            evt.ArtifactType == "knowledge_conflict_resolution" &&
            evt.ArtifactId == "conflict-a");
        Assert.Equal(NovelAgentProductionStages.KnowledgeResolved, outputArtifact.Stage);
        Assert.Equal("ready_for_rebuild", outputArtifact.Status);
        Assert.Contains("知识冲突已处理", outputArtifact.Message);
        Assert.Contains("ProduceChapter", outputArtifact.DataJson);

        var invalidatedPackage = await db.TianmingPackages.SingleAsync(p => p.Id == "pkg-old-context");
        Assert.Equal("stale", invalidatedPackage.Status);

        var creativeIntent = await db.CreativeIntents.SingleAsync();
        Assert.Equal("knowledge", creativeIntent.Source);
        Assert.Equal("accepted", creativeIntent.Status);
        Assert.Equal("project", creativeIntent.TargetScope);
        Assert.Equal("world_rule_change", creativeIntent.ImpactLevel);
        Assert.Equal("resolved", creativeIntent.ConflictStatus);
        Assert.Contains("conflict-a", creativeIntent.MetadataJson);
        Assert.Contains("蓝焰改为环境现象", creativeIntent.NormalizedIntent);

        var acceptedForPackage = await creativeIntents.GetAcceptedSnapshotsForPackageAsync(
            "user-1",
            "project-a",
            new ChapterContextPackageSummary { ChapterId = "chapter-002" });
        Assert.Contains(acceptedForPackage, intent =>
            intent.IntentId == creativeIntent.Id &&
            intent.NormalizedIntent.Contains("蓝焰改为环境现象", StringComparison.Ordinal));

        var revisionPlan = await db.RevisionPlans.SingleAsync();
        Assert.Equal("knowledge_conflict_resolution", revisionPlan.Source);
        Assert.Equal("accepted", revisionPlan.Status);
        Assert.Equal("project", revisionPlan.TargetScope);
        Assert.Equal("world_rule_change", revisionPlan.PlanType);
        Assert.Equal(creativeIntent.Id, revisionPlan.CreativeIntentId);
        Assert.Equal("conflict-a", revisionPlan.KnowledgeConflictReportId);
        Assert.Contains("pkg-old-context", revisionPlan.InvalidatedPackageIdsJson);
        Assert.Contains("knowledge-blue-flame", revisionPlan.ImpactAnalysisJson);
        Assert.Contains("ProduceChapter", revisionPlan.Recommendation);

        var runtimeEvent = await db.AgentRuntimeEvents.SingleAsync();
        Assert.Equal("production_progress", runtimeEvent.Type);
        Assert.Equal(NovelAgentProductionStages.KnowledgeResolved, runtimeEvent.Stage);
        Assert.Equal("ready_for_rebuild", runtimeEvent.Status);
        Assert.Equal("workflow", runtimeEvent.DisplaySurface);
        Assert.Equal("timeline", runtimeEvent.DisplayPolicy);
        Assert.Equal("knowledge_conflict_report", runtimeEvent.ArtifactType);
        Assert.Equal("conflict-a", runtimeEvent.ArtifactId);

        INovelProductionStateQueryService stateQuery = new NovelProductionStateQueryService(
            db,
            new ProductionChainProjectionService());
        var state = await stateQuery.QueryAsync(new NovelProductionStateQueryRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-a",
            RunId: "run-1",
            ChapterId: "",
            ChapterNumber: 0,
            IncludeEvents: true));
        Assert.NotNull(state);
        Assert.Contains(state!.ProductionEvents, e =>
            e.EventType == "knowledge_conflict_resolved" &&
            e.Status == "ready_for_rebuild" &&
            e.ArtifactId == "conflict-a");
        Assert.Contains(state.OutputArtifacts, artifact =>
            artifact.ArtifactType == "knowledge_conflict_resolution" &&
            artifact.ArtifactId == "conflict-a" &&
            artifact.SourceEventType == "knowledge_conflict_resolved");
        Assert.Contains(state.Packages, p => p.Id == "pkg-old-context" && p.Status == "stale");
        Assert.Contains(state.RuntimeEvents, e =>
            e.Type == "production_progress" &&
            e.Status == "ready_for_rebuild");
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static void Seed(NovelAgentDbContext db)
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
            new NovelProject { Id = "project-a", UserId = "user-1", Title = "项目A" },
            new NovelProject { Id = "project-b", UserId = "user-1", Title = "项目B" });
        db.KnowledgeBases.AddRange(
            new KnowledgeBase
            {
                Id = "knowledge-blue-flame",
                UserId = "user-1",
                EntryType = "ItemRule",
                Title = "邮徽蓝焰攻击",
                Content = "银蓝邮徽可以释放蓝焰攻击怪物。",
                Weight = 9,
                CreatedAt = DateTime.UtcNow
            },
            new KnowledgeBase
            {
                Id = "knowledge-boundary",
                UserId = "user-1",
                EntryType = "HardFact",
                Title = "邮徽能力边界",
                Content = "银蓝邮徽只能辨认旧邮路，不能攻击、不能修复、不能升级。",
                Weight = 10,
                CreatedAt = DateTime.UtcNow
            },
            new KnowledgeBase
            {
                Id = "knowledge-other-project",
                UserId = "user-1",
                EntryType = "HardFact",
                Title = "其他项目事实",
                Content = "其他项目才使用的设定，不能进入项目A冲突判断。",
                Weight = 10,
                CreatedAt = DateTime.UtcNow
            });
        db.ProjectKnowledgeUsages.AddRange(
            new ProjectKnowledgeUsage
            {
                Id = "usage-candidate",
                UserId = "user-1",
                ProjectId = "project-a",
                KnowledgeId = "knowledge-blue-flame",
                Status = "imported",
                Role = "ItemRule",
                Scope = "ProjectWide",
                Priority = 90,
                ConstraintLevel = "HardConstraint",
                PackagePolicy = "DefaultEveryChapter",
                FirstSeenAt = DateTime.UtcNow
            },
            new ProjectKnowledgeUsage
            {
                Id = "usage-boundary",
                UserId = "user-1",
                ProjectId = "project-a",
                KnowledgeId = "knowledge-boundary",
                Status = "referenced",
                Role = "ItemRule",
                Scope = "ProjectWide",
                Priority = 95,
                ConstraintLevel = "HardConstraint",
                PackagePolicy = "DefaultEveryChapter",
                FirstSeenAt = DateTime.UtcNow
            },
            new ProjectKnowledgeUsage
            {
                Id = "usage-other-project",
                UserId = "user-1",
                ProjectId = "project-b",
                KnowledgeId = "knowledge-other-project",
                Status = "referenced",
                Role = "WorldRule",
                Scope = "ProjectWide",
                Priority = 90,
                ConstraintLevel = "HardConstraint",
                PackagePolicy = "DefaultEveryChapter",
                FirstSeenAt = DateTime.UtcNow
            });
        db.SaveChanges();
    }

    private sealed class FakeKnowledgeConflictModelClient : IKnowledgeConflictModelClient
    {
        private readonly KnowledgeConflictDecision _decision;

        public FakeKnowledgeConflictModelClient(KnowledgeConflictDecision decision)
        {
            _decision = decision;
        }

        public KnowledgeConflictPrompt? LastPrompt { get; private set; }

        public Task<KnowledgeConflictDecision> DetectAsync(
            KnowledgeConflictPrompt prompt,
            CancellationToken ct = default)
        {
            LastPrompt = prompt;
            Assert.Equal("knowledge-blue-flame", prompt.Candidate.KnowledgeId);
            Assert.Contains(prompt.ExistingKnowledge, item => item.KnowledgeId == "knowledge-boundary");
            return Task.FromResult(_decision);
        }
    }

    private sealed class RecordingKnowledgeCanonConflictStatusService : IKnowledgeCanonConflictStatusService
    {
        public List<KnowledgeCanonConflictStatusRequest> Requests { get; } = new();
        public List<KnowledgeCanonConflictResolutionStatusRequest> ResolutionRequests { get; } = new();

        public Task ApplyAsync(KnowledgeCanonConflictStatusRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }

        public Task ApplyResolutionAsync(KnowledgeCanonConflictResolutionStatusRequest request, CancellationToken ct = default)
        {
            ResolutionRequests.Add(request);
            return Task.CompletedTask;
        }
    }
}
