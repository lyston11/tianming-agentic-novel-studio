using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class NovelProductionStateQueryServiceTests
{
    [Fact]
    public async Task QueryAsync_ReturnsRuntimePackageEventsToolsAndOutbox()
    {
        await using var db = CreateDb();
        await SeedProductionStateAsync(db);
        INovelProductionStateQueryService service = CreateService(db);

        var state = await service.QueryAsync(new NovelProductionStateQueryRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            RunId: "runtime-run-1",
            ChapterId: "",
            ChapterNumber: 0,
            IncludeEvents: true));

        Assert.NotNull(state);
        Assert.Equal("runtime-run-1", state!.RuntimeRun?.Id);
        Assert.Equal("project-1-chapter-002", state.ChapterId);
        var package = Assert.Single(state.Packages);
        Assert.Equal("pkg-chapter-002", package.Id);
        Assert.Equal(2, package.KnowledgeBindingSummary.BindingCount);
        Assert.Equal(1, package.KnowledgeBindingSummary.ShouldEnterGateCount);
        Assert.Equal(2, package.KnowledgeBindingSummary.ShouldEnterBlueprintCount);
        Assert.Equal(1, package.KnowledgeBindingSummary.ShouldEnterFactSnapshotCount);
        Assert.Equal(1, package.KnowledgeBindingSummary.PendingClassificationCount);
        Assert.Contains(state.ProductionEvents, e =>
            e.EventType == "build_package" &&
            e.PackageId == "pkg-chapter-002");
        Assert.Contains(state.ProductionEvents, e =>
            e.EventType == "creative_intents_executed" &&
            e.Stage == NovelAgentProductionStages.ChapterCommitted &&
            e.Status == "completed" &&
            e.DataJson.Contains("intent-chapter-002", StringComparison.Ordinal));
        Assert.Contains(state.RuntimeEvents, e =>
            e.Type == "production_progress" &&
            e.Stage == NovelAgentProductionStages.PackageBuilt &&
            e.Status == "completed" &&
            e.DisplaySurface == "workflow" &&
            e.DisplayPolicy == "timeline");
        var revisionPlan = Assert.Single(state.RevisionPlans);
        Assert.Equal("revision-plan-1", revisionPlan.Id);
        Assert.Equal("accepted", revisionPlan.Status);
        Assert.Equal("chapter_rewrite", revisionPlan.PlanType);
        Assert.Contains("pkg-chapter-002", revisionPlan.InvalidatedPackageIdsJson);
        var toolExecution = Assert.Single(state.ToolExecutions);
        Assert.Equal(NovelAgentProductionStages.DraftGeneration, toolExecution.ToolName);
        Assert.Equal("章节正文生成", toolExecution.SemanticContract.DisplayName);
        Assert.Contains("chapter_plan_run", toolExecution.SemanticContract.InputArtifacts);
        Assert.Contains("chapter_draft", toolExecution.SemanticContract.OutputArtifacts);
        Assert.Contains("runId", toolExecution.SemanticContract.IdempotencyPolicy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ChapterVersion", toolExecution.SemanticContract.RollbackPolicy, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("index_chapter", Assert.Single(state.OutboxEvents).EventType);
    }

    [Fact]
    public async Task QueryAsync_WhenPackageUsesShortChapterId_IncludesCanonicalChapterChanges()
    {
        await using var db = CreateDb();
        await SeedProductionStateAsync(db);
        var package = await db.TianmingPackages.SingleAsync(p => p.Id == "pkg-chapter-002");
        package.ChapterId = "chapter-002";
        db.ChapterChanges.Add(new ChapterChange
        {
            Id = "change-canonical-chapter-002",
            UserId = "user-1",
            ProjectId = "project-1",
            RuntimeRunId = "runtime-run-1",
            ChapterId = "project-1-chapter-002",
            PackageId = "pkg-chapter-002",
            ParseStatus = "parsed",
            ChangesJson = """{"NewPlotPoints":["邮路怪物围攻"]}""",
            CanonicalChangesJson = """{"NewPlotPoints":["邮路怪物围攻"]}""",
            AppliedToFactSnapshot = true,
            AppliedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        INovelProductionStateQueryService service = CreateService(db);

        var state = await service.QueryAsync(new NovelProductionStateQueryRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            RunId: "runtime-run-1",
            ChapterId: "",
            ChapterNumber: 0,
            IncludeEvents: true));

        Assert.NotNull(state);
        var change = Assert.Single(state!.ChapterChanges);
        Assert.Equal("change-canonical-chapter-002", change.Id);
        Assert.Equal("parsed", change.ParseStatus);
        Assert.True(change.AppliedToFactSnapshot);
        Assert.Contains("邮路怪物围攻", change.CanonicalChangesJson);
    }

    [Fact]
    public async Task QueryAsync_ReturnsMemoryReadAndPromotionAuditsForAgentStatusAnswers()
    {
        await using var db = CreateDb();
        await SeedProductionStateAsync(db);
        db.AgentMemoryReads.Add(new AgentMemoryRead
        {
            Id = "memory-read-1",
            UserId = "user-1",
            ProjectId = "project-1",
            SessionId = "session-1",
            RunId = "runtime-run-1",
            MemoryScope = "project",
            MemoryKeysJson = """["project.constraints","project.reader_promise"]""",
            SourceType = "memory_repository",
            Consumer = "GetProjectMemoryAsync",
            CreatedAt = DateTime.UtcNow.AddSeconds(2)
        });
        db.AgentMemoryPromotions.Add(new AgentMemoryPromotion
        {
            Id = "memory-promotion-1",
            UserId = "user-1",
            ProjectId = "project-1",
            SessionId = "session-1",
            RunId = "runtime-run-1",
            SourceScope = "session",
            TargetScope = "project",
            SourceMemoryKey = "session.short_term_preferences",
            TargetMemoryKey = "project.constraints",
            PromotionReason = "preference_sedimentation_threshold",
            PayloadJson = """{"preference":"章节要打怪升级","threshold":3}""",
            CreatedAt = DateTime.UtcNow.AddSeconds(3)
        });
        await db.SaveChangesAsync();
        INovelProductionStateQueryService service = CreateService(db);

        var state = await service.QueryAsync(new NovelProductionStateQueryRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            RunId: "runtime-run-1",
            ChapterId: "",
            ChapterNumber: 0,
            IncludeEvents: true));

        Assert.NotNull(state);
        var read = Assert.Single(state!.MemoryReads);
        Assert.Equal("memory-read-1", read.Id);
        Assert.Equal("project", read.MemoryScope);
        Assert.Equal("GetProjectMemoryAsync", read.Consumer);
        Assert.Contains("project.constraints", read.MemoryKeys);

        var promotion = Assert.Single(state.MemoryPromotions);
        Assert.Equal("memory-promotion-1", promotion.Id);
        Assert.Equal("session", promotion.SourceScope);
        Assert.Equal("project", promotion.TargetScope);
        Assert.Equal("project.constraints", promotion.TargetMemoryKey);
        Assert.Contains("章节要打怪升级", promotion.PayloadJson);
    }

    [Fact]
    public async Task QueryAsync_ReturnsOutboxProductionEventsForAgentStatusAnswers()
    {
        await using var db = CreateDb();
        await SeedProductionStateAsync(db);
        var truthStore = new ProductionTruthStore(db);
        await truthStore.AppendEventAsync(new CreateProductionEventRequest(
            RuntimeRunId: "runtime-run-1",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "project-1-chapter-002",
            PackageId: "pkg-chapter-002",
            EventType: "outbox_completed",
            Stage: "index_outbox",
            Status: "completed",
            Message: "后台 outbox 处理完成。 index_chapter_content/chapter_version",
            ArtifactType: "outbox_event",
            ArtifactId: "outbox-1",
            DataJson: "{\"eventType\":\"index_chapter_content\"}"));
        INovelProductionStateQueryService service = CreateService(db);

        var state = await service.QueryAsync(new NovelProductionStateQueryRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            RunId: "runtime-run-1",
            ChapterId: "",
            ChapterNumber: 0,
            IncludeEvents: true));

        Assert.NotNull(state);
        Assert.Contains(state!.ProductionEvents, e =>
            e.EventType == "outbox_completed" &&
            e.Stage == "index_outbox" &&
            e.Status == "completed" &&
            e.ArtifactType == "outbox_event" &&
            e.ArtifactId == "outbox-1");
    }

    [Fact]
    public async Task QueryAsync_ReturnsToolInputArtifactFailureStates()
    {
        await using var db = CreateDb();
        await SeedProductionStateAsync(db);
        db.AgentToolExecutions.Add(new AgentToolExecution
        {
            Id = "tool-exec-blocked",
            UserId = "user-1",
            ProjectId = "project-1",
            SessionId = "session-1",
            RunId = "runtime-run-1",
            ToolName = "ProduceChapter",
            Phase = "semantic_precondition",
            Risk = "High",
            ArgumentsHash = "blocked-hash",
            Status = "failed",
            ResultMessage = "上一章提交后后台沉淀尚未完成，不能继续生产下一章。",
            ErrorMessage = "上一章提交后后台沉淀尚未完成，不能继续生产下一章。",
            FailureJson = """
                {
                  "code":"TOOL_INPUT_ARTIFACT_BLOCKED",
                  "failedStage":"semantic_precondition",
                  "reason":"上一章提交后后台沉淀尚未完成，不能继续生产下一章。",
                  "recoverable":true,
                  "recommendedAction":"QueryProductionOutbox",
                  "recoverableActions":["QueryNovelProductionState","QueryProductionOutbox","RetryProductionOutbox"],
                  "inputArtifacts":[
                    {
                      "artifactName":"post_commit_outbox",
                      "status":"pending",
                      "artifactId":"outbox-finalize-001",
                      "message":"上一章提交后事实沉淀仍在等待处理。",
                      "blocksExecution":true,
                      "recommendedActions":["QueryNovelProductionState","QueryProductionOutbox","RetryProductionOutbox"]
                    }
                  ],
                  "requiresUserDecision":false
                }
                """,
            StartedAt = DateTime.UtcNow.AddSeconds(-3),
            CompletedAt = DateTime.UtcNow.AddSeconds(-2)
        });
        await db.SaveChangesAsync();
        INovelProductionStateQueryService service = CreateService(db);

        var state = await service.QueryAsync(new NovelProductionStateQueryRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            RunId: "runtime-run-1",
            ChapterId: "project-1-chapter-002",
            ChapterNumber: 0,
            IncludeEvents: true));

        Assert.NotNull(state);
        var failure = state!.ToolExecutions.Single(tool => tool.Id == "tool-exec-blocked").Failure;
        Assert.NotNull(failure);
        var inputArtifact = Assert.Single(failure!.InputArtifacts);
        Assert.Equal("post_commit_outbox", inputArtifact.ArtifactName);
        Assert.Equal("pending", inputArtifact.Status);
        Assert.Equal("outbox-finalize-001", inputArtifact.ArtifactId);
        Assert.True(inputArtifact.BlocksExecution);
        Assert.Contains("RetryProductionOutbox", inputArtifact.RecommendedActions);
    }

    [Fact]
    public async Task QueryAsync_ReturnsRecordedOutputArtifactsForAgentAndWorkflow()
    {
        await using var db = CreateDb();
        await SeedProductionStateAsync(db);
        IProductionTruthStore truthStore = new ProductionTruthStore(db);
        IProductionEventWriter eventWriter = new ProductionEventWriter(truthStore);
        IOutputArtifactRecorder recorder = new OutputArtifactRecorder(eventWriter);
        await recorder.RecordAsync(new OutputArtifactRecordRequest(
            RuntimeRunId: "runtime-run-1",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "project-1-chapter-002",
            PackageId: "pkg-chapter-002",
            ToolName: "ProduceChapter",
            Stage: NovelAgentProductionStages.DraftGeneration,
            Status: "completed",
            ArtifactType: "chapter_draft",
            ArtifactId: "draft-chapter-002-v1",
            OutputKind: "ProcessArtifact",
            Summary: "第二章草稿已生成。",
            UserVisibleWhere: new[] { "创作工作流" },
            VisibleInWorkflow: true,
            VisibleInLibrary: false,
            SourceEventType: "chapter_draft_generated",
            SourceEventId: "evt-draft-002",
            Data: new { wordCount = 3100 }));
        INovelProductionStateQueryService service = CreateService(db);

        var state = await service.QueryAsync(new NovelProductionStateQueryRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            RunId: "runtime-run-1",
            ChapterId: "project-1-chapter-002",
            ChapterNumber: 0,
            IncludeEvents: true));

        Assert.NotNull(state);
        var artifact = Assert.Single(state!.OutputArtifacts);
        Assert.Equal("chapter_draft", artifact.ArtifactType);
        Assert.Equal("draft-chapter-002-v1", artifact.ArtifactId);
        Assert.Equal("ProcessArtifact", artifact.OutputKind);
        Assert.Equal("ProduceChapter", artifact.ToolName);
        Assert.True(artifact.VisibleInWorkflow);
        Assert.False(artifact.VisibleInLibrary);
        Assert.Contains("创作工作流", artifact.UserVisibleWhere);
        Assert.Equal("chapter_draft_generated", artifact.SourceEventType);
        Assert.Equal("evt-draft-002", artifact.SourceEventId);
    }

    [Fact]
    public async Task QueryAsync_ReturnsDependencyBlocksForPreviousChapterPostCommitOutbox()
    {
        await using var db = CreateDb();
        await SeedProductionStateAsync(db);
        db.Chapters.Add(new Chapter
        {
            Id = "project-1-chapter-001",
            ProjectId = "project-1",
            Title = "第一章：银蓝邮徽",
            ChapterNumber = 1,
            Status = "committed",
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5)
        });
        db.OutboxEvents.Add(new OutboxEvent
        {
            Id = "outbox-finalize-001",
            UserId = "user-1",
            ProjectId = "project-1",
            RuntimeRunId = "runtime-run-0",
            EventType = "finalize_chapter_commit_metadata",
            AggregateType = "chapter",
            AggregateId = "project-1-chapter-001",
            Status = "retryable_failed",
            PayloadJson = "{}",
            LastError = "LLM fact extraction timeout",
            CreatedAt = DateTime.UtcNow.AddMinutes(-4),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-4)
        });
        await db.SaveChangesAsync();
        INovelProductionStateQueryService service = new NovelProductionStateQueryService(
            db,
            new ProductionChainProjectionService(),
            new ProductionDependencyGuard(db));

        var state = await service.QueryAsync(new NovelProductionStateQueryRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            RunId: "runtime-run-1",
            ChapterId: "project-1-chapter-002",
            ChapterNumber: 0,
            IncludeEvents: true));

        Assert.NotNull(state);
        var block = Assert.Single(state!.DependencyBlocks);
        Assert.Equal("previous_chapter_post_commit_outbox_pending", block.Code);
        Assert.Equal(2, block.TargetChapterNumber);
        Assert.Equal("project-1-chapter-001", block.PreviousChapterId);
        Assert.Contains("outbox-finalize-001", block.OutboxEventIds);
        Assert.Contains("retryable_failed", block.Statuses);
        Assert.Equal("runtime-run-1", block.RecommendedArguments["runId"]);
        Assert.Equal("project-1-chapter-002", block.RecommendedArguments["chapterId"]);
    }

    [Fact]
    public async Task QueryAsync_ReturnsProductionChainsForAgentProgressAnswers()
    {
        await using var db = CreateDb();
        await SeedProductionStateAsync(db);
        var truthStore = new ProductionTruthStore(db);
        await truthStore.AppendEventAsync(new CreateProductionEventRequest(
            RuntimeRunId: "runtime-run-1",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "project-1-chapter-002",
            PackageId: "pkg-chapter-002",
            EventType: "chapter_committed",
            Stage: NovelAgentProductionStages.ChapterCommit,
            Status: "completed",
            Message: "第二章已提交书城。",
            ArtifactType: "chapter_version",
            ArtifactId: "version-chapter-002-v1",
            DataJson: "{\"factSnapshotId\":\"fact-chapter-002-v1\",\"factSnapshotVersion\":3,\"versionNumber\":1}"));
        await truthStore.AppendEventAsync(new CreateProductionEventRequest(
            RuntimeRunId: "runtime-run-1",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "project-1-chapter-002",
            PackageId: "pkg-chapter-002",
            EventType: "outbox_completed",
            Stage: "index_outbox",
            Status: "completed",
            Message: "后台 outbox 处理完成。 index_chapter_content/chapter_version",
            ArtifactType: "outbox_event",
            ArtifactId: "outbox-1",
            DataJson: "{\"eventType\":\"index_chapter_content\"}"));
        INovelProductionStateQueryService service = CreateService(db);

        var state = await service.QueryAsync(new NovelProductionStateQueryRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            RunId: "runtime-run-1",
            ChapterId: "",
            ChapterNumber: 0,
            IncludeEvents: true));

        Assert.NotNull(state);
        var chain = Assert.Single(state!.ProductionChains);
        Assert.Equal("project-1-chapter-002", chain.ChapterId);
        Assert.Equal("runtime-run-1", chain.RuntimeRunId);
        Assert.Equal("pkg-chapter-002", chain.PackageId);
        Assert.Equal("completed", chain.Status);
        Assert.Equal("第二章已提交书城。", chain.Summary);
        Assert.Equal("version-chapter-002-v1", chain.ChapterVersionId);
        Assert.Equal(1, chain.ChapterVersionNumber);
        Assert.Equal("fact-chapter-002-v1", chain.FactSnapshotId);
        Assert.Equal(3, chain.FactSnapshotVersion);
        Assert.Contains(chain.RevisionPlanIds, id => id == "revision-plan-1");
        Assert.Contains(chain.Steps, step => step.Key == "commit" && step.ArtifactId == "version-chapter-002-v1");
        Assert.Contains(chain.Steps, step => step.Key == "outbox" && step.OutboxEventId == "outbox-1");
    }

    [Fact]
    public async Task QueryAsync_GroupsRevisionPlanAndRebuiltPackageAcrossRuntimeRuns()
    {
        await using var db = CreateDb();
        await SeedProductionStateAsync(db);
        db.AgentRuntimeRuns.Add(new AgentRuntimeRun
        {
            Id = "runtime-run-2",
            UserId = "user-1",
            SessionId = "session-1",
            ProjectId = "project-1",
            Status = "completed",
            Mode = "production",
            CurrentPhase = "committed",
            CurrentStep = 8,
            ActiveTool = "ProduceChapter",
            UserMessage = "按修订计划重写第二章并提交。",
            LastMessage = "第二章 v2 已提交。",
            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTime.UtcNow
        });
        var truthStore = new ProductionTruthStore(db);
        await truthStore.CreatePackageAsync(new CreateTianmingPackageRequest(
            Id: "pkg-chapter-002-v2",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "project-1-chapter-002",
            RuntimeRunId: "runtime-run-2",
            PackageKind: "chapter_generation",
            InputJson: "{\"goal\":\"按修订计划重写第二章\"}",
            DependencyVersionsJson: "{\"storyBible\":3}",
            KnowledgeSnapshotJson: "{\"rebuiltFromPackageIds\":[\"pkg-chapter-002\"]}",
            FactSnapshotJson: "{\"previous\":\"第一章结尾\"}",
            PromptVersion: "chapter-v2",
            KernelVersion: "agentic-tianming-v1"));
        await truthStore.AppendEventAsync(new CreateProductionEventRequest(
            RuntimeRunId: "runtime-run-2",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "project-1-chapter-002",
            PackageId: "pkg-chapter-002-v2",
            EventType: "build_package",
            Stage: NovelAgentProductionStages.PackageBuilt,
            Status: "completed",
            Message: "第二章生产包已按修订计划重建。",
            ArtifactType: "TianmingPackage",
            ArtifactId: "pkg-chapter-002-v2",
            DataJson: "{}"));
        await truthStore.AppendEventAsync(new CreateProductionEventRequest(
            RuntimeRunId: "runtime-run-2",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "project-1-chapter-002",
            PackageId: "pkg-chapter-002-v2",
            EventType: "chapter_committed",
            Stage: NovelAgentProductionStages.ChapterCommit,
            Status: "completed",
            Message: "第二章 v2 已提交书城。",
            ArtifactType: "chapter_version",
            ArtifactId: "version-chapter-002-v2",
            DataJson: "{\"factSnapshotId\":\"fact-chapter-002-v2\",\"factSnapshotVersion\":4,\"versionNumber\":2}"));
        INovelProductionStateQueryService service = CreateService(db);

        var state = await service.QueryAsync(new NovelProductionStateQueryRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            RunId: "runtime-run-2",
            ChapterId: "",
            ChapterNumber: 0,
            IncludeEvents: true));

        Assert.NotNull(state);
        var chain = Assert.Single(state!.ProductionChains);
        Assert.Equal("project-1-chapter-002", chain.ChapterId);
        Assert.Equal("runtime-run-2", chain.RuntimeRunId);
        Assert.Equal("pkg-chapter-002-v2", chain.PackageId);
        Assert.Equal("completed", chain.Status);
        Assert.Contains(chain.RevisionPlanIds, id => id == "revision-plan-1");
        Assert.Contains(chain.RebuildLinks, link =>
            link.OldPackageId == "pkg-chapter-002" &&
            link.NewPackageId == "pkg-chapter-002-v2");
        Assert.Contains(chain.Steps, step => step.Key == "revision_plan" && step.EventId == "revision-plan-1");
        Assert.Contains(chain.Steps, step => step.Key == "context" && step.ArtifactId == "pkg-chapter-002-v2");
        Assert.Contains(chain.Steps, step => step.Key == "commit" && step.ArtifactId == "version-chapter-002-v2");
    }

    [Fact]
    public async Task QueryAsync_WithOnlyRevisionPlanProjectsPendingRevisionChain()
    {
        await using var db = CreateDb();
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
            Id = "project-plan-only",
            UserId = "user-1",
            Title = "只含修订计划的测试书",
            Status = "Writing",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.Chapters.Add(new Chapter
        {
            Id = "project-plan-only-chapter-002",
            ProjectId = "project-plan-only",
            Title = "第二章：邮路围城",
            ChapterNumber = 2,
            Status = "draft",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.RevisionPlans.Add(new RevisionPlan
        {
            Id = "revision-plan-pending",
            UserId = "user-1",
            ProjectId = "project-plan-only",
            SessionId = "session-plan",
            RuntimeRunId = "runtime-run-plan",
            Source = "chat",
            PlanType = "chapter_rewrite",
            TargetScope = "chapter",
            TargetChapterId = "project-plan-only-chapter-002",
            TargetChapterLogicalId = "chapter-002",
            TargetChapterDisplayName = "第二章 邮路围城",
            Status = "accepted",
            InvalidatedPackageIdsJson = """["pkg-chapter-002-v1"]""",
            Recommendation = "第二章按用户新创意重写，后续生产包需重新构建。",
            CreatedAt = DateTime.UtcNow.AddSeconds(-10),
            UpdatedAt = DateTime.UtcNow.AddSeconds(-9)
        });
        await db.SaveChangesAsync();
        INovelProductionStateQueryService service = CreateService(db);

        var state = await service.QueryAsync(new NovelProductionStateQueryRequest(
            UserId: "user-1",
            SessionId: "session-plan",
            ProjectId: "project-plan-only",
            RunId: "",
            ChapterId: "project-plan-only-chapter-002",
            ChapterNumber: 0,
            IncludeEvents: true));

        Assert.NotNull(state);
        var chain = Assert.Single(state!.ProductionChains);
        Assert.Equal("revision_plan:revision-plan-pending:project-plan-only-chapter-002", chain.Id);
        Assert.Equal("project-plan-only-chapter-002", chain.ChapterId);
        Assert.Equal("chapter-002", chain.ChapterLogicalId);
        Assert.Equal("第二章 邮路围城", chain.ChapterDisplayName);
        Assert.Equal("runtime-run-plan", chain.RuntimeRunId);
        Assert.Equal("pkg-chapter-002-v1", chain.PackageId);
        Assert.Contains("revision-plan-pending", chain.RevisionPlanIds);
        Assert.Contains(chain.Steps, step => step.Key == "revision_plan" && step.EventId == "revision-plan-pending");
    }

    [Fact]
    public async Task QueryAsync_ReturnsLatestFactSnapshotFactsForAgentStatusAnswers()
    {
        await using var db = CreateDb();
        await SeedProductionStateAsync(db);
        db.ProjectFactSnapshots.Add(new ProjectFactSnapshot
        {
            Id = "fact-chapter-002-v1",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "project-1-chapter-002",
            ChapterVersionId = "version-chapter-002-v1",
            VersionNumber = 3,
            Source = "chapter_fact_extraction",
            CreatedAt = DateTime.UtcNow.AddSeconds(5),
            SnapshotJson = """
            {
              "chapterId": "project-1-chapter-002",
              "chapterTitle": "第二章：黑雨邮路",
              "protagonistName": "沈砚",
              "protagonistIdentity": "旧邮局幸存投递员",
              "protagonistStatus": "右手被邮徽灼伤，但仍能行动",
              "currentLocation": "废弃维修站",
              "systemState": "银蓝邮徽只能识别旧邮路，不能主动攻击",
              "equipmentState": "银蓝邮徽贴在右掌，发烫警示",
              "keyEvents": ["沈砚用邮徽识别出旧邮路", "黑雨异兽开始围攻维修站"],
              "endingState": "沈砚带着银蓝邮徽冲进旧邮路入口",
              "nextChapterMustCarry": ["沈砚不能把银蓝邮徽当攻击武器", "旧邮路入口必须付出记忆代价"]
            }
            """
        });
        await db.SaveChangesAsync();
        INovelProductionStateQueryService service = CreateService(db);

        var state = await service.QueryAsync(new NovelProductionStateQueryRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            RunId: "runtime-run-1",
            ChapterId: "project-1-chapter-002",
            ChapterNumber: 0,
            IncludeEvents: true));

        Assert.NotNull(state);
        var snapshot = Assert.Single(state!.FactSnapshots);
        Assert.Equal("fact-chapter-002-v1", snapshot.Id);
        Assert.Equal("project-1-chapter-002", snapshot.ChapterId);
        Assert.Equal("第二章：黑雨邮路", snapshot.ChapterTitle);
        Assert.Equal("沈砚", snapshot.ProtagonistName);
        Assert.Equal("旧邮局幸存投递员", snapshot.ProtagonistIdentity);
        Assert.Equal("右手被邮徽灼伤，但仍能行动", snapshot.ProtagonistStatus);
        Assert.Equal("废弃维修站", snapshot.CurrentLocation);
        Assert.Equal("银蓝邮徽只能识别旧邮路，不能主动攻击", snapshot.SystemState);
        Assert.Equal("银蓝邮徽贴在右掌，发烫警示", snapshot.EquipmentState);
        Assert.Equal("沈砚带着银蓝邮徽冲进旧邮路入口", snapshot.EndingState);
        Assert.Contains("黑雨异兽开始围攻维修站", snapshot.KeyEvents);
        Assert.Contains("旧邮路入口必须付出记忆代价", snapshot.NextChapterMustCarry);
    }

    [Fact]
    public async Task QueryAsync_MarksRecoveredCommittedProductionChainAsCompleted()
    {
        await using var db = CreateDb();
        await SeedProductionStateAsync(db);
        var truthStore = new ProductionTruthStore(db);
        await truthStore.AppendEventAsync(new CreateProductionEventRequest(
            RuntimeRunId: "runtime-run-1",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "project-1-chapter-002",
            PackageId: "pkg-chapter-002",
            EventType: "chapter_gate_validated",
            Stage: NovelAgentProductionStages.GateValidation,
            Status: "failed",
            Message: "第一次门禁未通过。",
            ArtifactType: "gate_report",
            ArtifactId: "gate-chapter-002",
            DataJson: "{}"));
        await truthStore.AppendEventAsync(new CreateProductionEventRequest(
            RuntimeRunId: "runtime-run-1",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "project-1-chapter-002",
            PackageId: "pkg-chapter-002",
            EventType: "chapter_committed",
            Stage: NovelAgentProductionStages.ChapterCommit,
            Status: "completed",
            Message: "修订后第二章已提交书城。",
            ArtifactType: "chapter_version",
            ArtifactId: "version-chapter-002-v2",
            DataJson: "{\"factSnapshotId\":\"fact-chapter-002-v2\",\"factSnapshotVersion\":4,\"versionNumber\":2}"));
        await truthStore.AppendEventAsync(new CreateProductionEventRequest(
            RuntimeRunId: "runtime-run-1",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "project-1-chapter-002",
            PackageId: "pkg-chapter-002",
            EventType: "outbox_failed",
            Stage: "index_outbox",
            Status: "retryable_failed",
            Message: "后台索引第一次失败。",
            ArtifactType: "outbox_event",
            ArtifactId: "outbox-1",
            DataJson: "{\"error\":\"Qdrant timeout\"}"));
        await truthStore.AppendEventAsync(new CreateProductionEventRequest(
            RuntimeRunId: "runtime-run-1",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "project-1-chapter-002",
            PackageId: "pkg-chapter-002",
            EventType: "outbox_completed",
            Stage: "index_outbox",
            Status: "completed",
            Message: "后台索引重试完成。",
            ArtifactType: "outbox_event",
            ArtifactId: "outbox-1",
            DataJson: "{\"eventType\":\"index_chapter_content\"}"));
        INovelProductionStateQueryService service = CreateService(db);

        var state = await service.QueryAsync(new NovelProductionStateQueryRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            RunId: "runtime-run-1",
            ChapterId: "",
            ChapterNumber: 0,
            IncludeEvents: true));

        var chain = Assert.Single(state!.ProductionChains);
        Assert.Equal("completed", chain.Status);
        Assert.Contains(chain.Steps, step => step.Status == "failed");
        Assert.Contains(chain.Steps, step => step.Status == "retryable_failed");
        Assert.Contains(chain.Steps, step => step.Status == "completed");
    }

    [Fact]
    public async Task QueryAsync_ExplainsStalePackagesWithRevisionPlanAndRecommendedAction()
    {
        await using var db = CreateDb();
        await SeedProductionStateAsync(db);
        var package = await db.TianmingPackages.SingleAsync(p => p.Id == "pkg-chapter-002");
        package.Status = "stale";
        var plan = await db.RevisionPlans.SingleAsync(p => p.Id == "revision-plan-1");
        plan.Status = "ready_for_rebuild";
        plan.InvalidatedPackageIdsJson = "[\"pkg-chapter-002\"]";
        plan.AffectedChapterIdsJson = "[\"project-1-chapter-002\"]";
        await db.SaveChangesAsync();
        INovelProductionStateQueryService service = CreateService(db);

        var state = await service.QueryAsync(new NovelProductionStateQueryRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            RunId: "runtime-run-1",
            ChapterId: "",
            ChapterNumber: 0,
            IncludeEvents: true));

        Assert.NotNull(state);
        var stale = Assert.Single(state!.StalePackages);
        Assert.Equal("pkg-chapter-002", stale.PackageId);
        Assert.Equal("project-1-chapter-002", stale.ChapterId);
        Assert.Equal("revision-plan-1", stale.RevisionPlanId);
        Assert.Equal("ready_for_rebuild", stale.RevisionPlanStatus);
        Assert.Contains("重建第二章", stale.Reason);
        Assert.Equal("ProduceChapter", stale.RecommendedToolName);
        Assert.Contains("revision-plan-1", stale.RecommendedArguments["revisionPlanId"]);
    }

    [Fact]
    public async Task QueryAsync_ExplainsCanonicalStalePackageWithHistoricalLogicalRevisionPlan()
    {
        await using var db = CreateDb();
        await SeedProductionStateAsync(db);
        var package = await db.TianmingPackages.SingleAsync(p => p.Id == "pkg-chapter-002");
        package.Status = "stale";
        package.ChapterId = "project-1-chapter-002";
        var plan = await db.RevisionPlans.SingleAsync(p => p.Id == "revision-plan-1");
        plan.Status = "ready_for_rebuild";
        plan.TargetChapterId = "chapter-002";
        plan.InvalidatedPackageIdsJson = "[]";
        plan.AffectedChapterIdsJson = "[\"chapter-002\"]";
        await db.SaveChangesAsync();
        INovelProductionStateQueryService service = CreateService(db);

        var state = await service.QueryAsync(new NovelProductionStateQueryRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            RunId: "runtime-run-1",
            ChapterId: "project-1-chapter-002",
            ChapterNumber: 0,
            IncludeEvents: true));

        Assert.NotNull(state);
        var stale = Assert.Single(state!.StalePackages);
        Assert.Equal("pkg-chapter-002", stale.PackageId);
        Assert.Equal("project-1-chapter-002", stale.ChapterId);
        Assert.Equal("revision-plan-1", stale.RevisionPlanId);
        Assert.Equal("ready_for_rebuild", stale.RevisionPlanStatus);
        Assert.Contains("重建第二章", stale.Reason);
        Assert.Equal("revision-plan-1", stale.RecommendedArguments["revisionPlanId"]);
        var revisionPlan = Assert.Single(state.RevisionPlans);
        Assert.Equal("chapter-002", revisionPlan.TargetChapterId);
    }

    [Fact]
    public async Task QueryAsync_ReturnsPackageRebuildSourceIds()
    {
        await using var db = CreateDb();
        await SeedProductionStateAsync(db);
        var oldPackage = await db.TianmingPackages.SingleAsync(p => p.Id == "pkg-chapter-002");
        oldPackage.Status = "stale";
        db.TianmingPackages.Add(new TianmingPackage
        {
            Id = "pkg-chapter-002-v2",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "project-1-chapter-002",
            RuntimeRunId = "runtime-run-1",
            PackageKind = "chapter_context_package",
            Status = "pending",
            InputJson = "{\"rebuiltFromPackageIds\":[\"pkg-chapter-002\"]}",
            DependencyVersionsJson = "{}",
            KnowledgeSnapshotJson = "{\"rebuiltFromPackageIds\":[\"pkg-chapter-002\"]}",
            FactSnapshotJson = "{}",
            PromptVersion = "chapter-context-v1",
            KernelVersion = "agentic-tianming-v1",
            CreatedAt = DateTime.UtcNow.AddSeconds(5),
            UpdatedAt = DateTime.UtcNow.AddSeconds(5)
        });
        await db.SaveChangesAsync();
        INovelProductionStateQueryService service = CreateService(db);

        var state = await service.QueryAsync(new NovelProductionStateQueryRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            RunId: "runtime-run-1",
            ChapterId: "",
            ChapterNumber: 0,
            IncludeEvents: true));

        Assert.NotNull(state);
        var rebuiltPackage = Assert.Single(state!.Packages, p => p.Id == "pkg-chapter-002-v2");
        Assert.Equal(new[] { "pkg-chapter-002" }, rebuiltPackage.RebuiltFromPackageIds);
        var link = Assert.Single(state.RebuildLinks);
        Assert.Equal("pkg-chapter-002", link.OldPackageId);
        Assert.Equal("pkg-chapter-002-v2", link.NewPackageId);
        Assert.Equal("stale", link.OldPackageStatus);
        Assert.Equal("pending", link.NewPackageStatus);
        Assert.Equal("project-1-chapter-002", link.ChapterId);
    }

    [Fact]
    public async Task QueryAsync_IncludesRevisionPlansExecutedByCurrentRunEvents()
    {
        await using var db = CreateDb();
        await SeedProductionStateAsync(db);
        var plan = await db.RevisionPlans.SingleAsync(p => p.Id == "revision-plan-1");
        plan.RuntimeRunId = "revision-authoring-run";
        plan.Status = "executed";
        await db.SaveChangesAsync();
        var truthStore = new ProductionTruthStore(db);
        await truthStore.AppendEventAsync(new CreateProductionEventRequest(
            RuntimeRunId: "runtime-run-1",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "project-1-chapter-002",
            PackageId: "pkg-chapter-002",
            EventType: "revision_plan_executed",
            Stage: NovelAgentProductionStages.ChapterCommit,
            Status: "executed",
            Message: "修订计划已随章节提交完成执行：重建第二章生产包后重新生成正文。",
            ArtifactType: "RevisionPlan",
            ArtifactId: "revision-plan-1",
            DataJson: "{\"revisionPlanId\":\"revision-plan-1\",\"packageId\":\"pkg-chapter-002\"}"));
        INovelProductionStateQueryService service = CreateService(db);

        var state = await service.QueryAsync(new NovelProductionStateQueryRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            RunId: "runtime-run-1",
            ChapterId: "",
            ChapterNumber: 0,
            IncludeEvents: true));

        Assert.NotNull(state);
        var executedPlan = Assert.Single(state!.RevisionPlans, item => item.Id == "revision-plan-1");
        Assert.Equal("executed", executedPlan.Status);
        Assert.Contains(state.ProductionEvents, evt =>
            evt.EventType == "revision_plan_executed" &&
            evt.ArtifactId == "revision-plan-1" &&
            evt.Status == "executed");
    }

    [Fact]
    public async Task QueryAsync_ReturnsStructuredProductionEventFailureForIndexFailures()
    {
        await using var db = CreateDb();
        await SeedProductionStateAsync(db);
        var truthStore = new ProductionTruthStore(db);
        await truthStore.AppendEventAsync(new CreateProductionEventRequest(
            RuntimeRunId: "runtime-run-1",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "project-1-chapter-002",
            PackageId: "pkg-chapter-002",
            EventType: "outbox_failed",
            Stage: "index_outbox",
            Status: "retryable_failed",
            Message: "后台 outbox 处理失败，已排队重试。 index_chapter_content/chapter_version",
            ArtifactType: "outbox_event",
            ArtifactId: "outbox-1",
            DataJson: "{\"error\":\"Qdrant timeout\",\"recommendedAction\":\"RetryOutboxEvent(outbox-1)\"}"));
        INovelProductionStateQueryService service = CreateService(db);

        var state = await service.QueryAsync(new NovelProductionStateQueryRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            RunId: "runtime-run-1",
            ChapterId: "",
            ChapterNumber: 0,
            IncludeEvents: true));

        var evt = Assert.Single(state!.ProductionEvents, e => e.EventType == "outbox_failed");
        Assert.NotNull(evt.Failure);
        Assert.Equal("INDEX_FAILED", evt.Failure!.Code);
        Assert.Equal("index_outbox", evt.Failure.Stage);
        Assert.True(evt.Failure.Recoverable);
        Assert.Equal("Qdrant timeout", evt.Failure.Message);
        Assert.Equal("RetryOutboxEvent(outbox-1)", evt.Failure.RecommendedAction);
        Assert.False(evt.Failure.RequiresUserDecision);
        Assert.Contains("outbox-1", evt.Failure.ArtifactIds);
    }

    [Fact]
    public async Task QueryAsync_ReturnsStructuredRuntimeFailureForHeartbeatLostRuns()
    {
        await using var db = CreateDb();
        await SeedProductionStateAsync(db);
        var run = await db.AgentRuntimeRuns.SingleAsync(r => r.Id == "runtime-run-1");
        run.Status = "failed";
        run.CurrentPhase = "heartbeat_lost";
        run.ErrorMessage = "Redis heartbeat missing; recovering from database truth.";
        run.FailureJson = """
            {
              "code": "RUNTIME_HEARTBEAT_LOST",
              "stage": "heartbeat_lost",
              "message": "Redis heartbeat missing; recovering from database truth.",
              "recoverable": true,
              "recommendedAction": "QueryRuntimeRun 后按当前章节、工作流和工具结果决定恢复、重试或询问用户。",
              "artifactIds": ["runtime-run-1"]
            }
            """;
        await db.SaveChangesAsync();
        INovelProductionStateQueryService service = CreateService(db);

        var state = await service.QueryAsync(new NovelProductionStateQueryRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            RunId: "runtime-run-1",
            ChapterId: "",
            ChapterNumber: 0,
            IncludeEvents: true));

        Assert.NotNull(state);
        Assert.NotNull(state!.RuntimeRun?.Failure);
        Assert.Equal("RUNTIME_HEARTBEAT_LOST", state.RuntimeRun.Failure!.Code);
        Assert.Equal("heartbeat_lost", state.RuntimeRun.Failure.Stage);
        Assert.True(state.RuntimeRun.Failure.Recoverable);
        Assert.Contains("QueryRuntimeRun", state.RuntimeRun.Failure.RecommendedAction);
        Assert.Contains("runtime-run-1", state.RuntimeRun.Failure.ArtifactIds);
    }

    [Fact]
    public async Task QueryAsync_ProjectsPersistedArtifactsIntoProductionChainWhenEventsAreMissing()
    {
        await using var db = CreateDb();
        await SeedProductionStateAsync(db);
        db.ChapterDrafts.Add(new ChapterDraft
        {
            Id = "draft-record-002",
            UserId = "user-1",
            ProjectId = "project-1",
            RuntimeRunId = "runtime-run-1",
            ChapterId = "project-1-chapter-002",
            PackageId = "pkg-chapter-002",
            ArtifactId = "draft-artifact-002",
            Status = "draft_generated",
            DraftContent = "第二章正文草稿：沈砚冲进旧邮路。",
            ContentLength = 4800,
            HasChanges = true,
            ChangesJson = """{"NewPlotPoints":["旧邮路入口开启"]}""",
            GeneratedAt = DateTime.UtcNow.AddSeconds(2),
            CreatedAt = DateTime.UtcNow.AddSeconds(2)
        });
        db.ChapterChanges.Add(new ChapterChange
        {
            Id = "change-record-002",
            UserId = "user-1",
            ProjectId = "project-1",
            RuntimeRunId = "runtime-run-1",
            ChapterId = "project-1-chapter-002",
            PackageId = "pkg-chapter-002",
            ParseStatus = "parsed",
            ChangesJson = """{"NewPlotPoints":["旧邮路入口开启"]}""",
            CanonicalChangesJson = """{"NewPlotPoints":["旧邮路入口开启"]}""",
            AppliedToFactSnapshot = true,
            AppliedAt = DateTime.UtcNow.AddSeconds(5),
            CreatedAt = DateTime.UtcNow.AddSeconds(3)
        });
        db.GenerationGateReports.Add(new GenerationGateReportRecord
        {
            Id = "gate-record-002",
            UserId = "user-1",
            ProjectId = "project-1",
            RuntimeRunId = "runtime-run-1",
            ChapterId = "project-1-chapter-002",
            PackageId = "pkg-chapter-002",
            ArtifactId = "gate-artifact-002",
            Status = "validated",
            ProtocolPassed = true,
            ChangesDetected = true,
            FactSnapshotPassed = true,
            BlueprintPassed = true,
            RagPassed = true,
            ReportJson = "{\"status\":\"validated\"}",
            ValidatedAt = DateTime.UtcNow.AddSeconds(4),
            CreatedAt = DateTime.UtcNow.AddSeconds(4)
        });
        db.AgentReviews.Add(new AgentReviewRecord
        {
            Id = "review-record-002",
            UserId = "user-1",
            ProjectId = "project-1",
            RuntimeRunId = "runtime-run-1",
            ChapterId = "project-1-chapter-002",
            PackageId = "pkg-chapter-002",
            ReviewId = "review-artifact-002",
            OverallResult = "Pass",
            ValidationOverallResult = "validated",
            QualityScore = 90,
            ContentLength = 4800,
            CheckCount = 8,
            Summary = "Agent 总编验收通过。",
            MeetsAcceptedCreativeIntents = false,
            ContinuityRisk = "medium",
            ChapterPacing = "battle_feedback_weak",
            RecommendedAction = "revise_before_commit",
            ReviewJson = """
                {
                  "decision": "revise_before_commit",
                  "overallResult": "Warning",
                  "problems": ["打怪升级未落实，仍偏情绪拉扯。"],
                  "suggestions": ["重写战斗段落，让邮徽只负责识路。"],
                  "meetsAcceptedCreativeIntents": false,
                  "continuityRisk": "medium",
                  "chapterPacing": "battle_feedback_weak",
                  "recommendedAction": "revise_before_commit"
                }
                """,
            ReviewedAt = DateTime.UtcNow.AddSeconds(6),
            CreatedAt = DateTime.UtcNow.AddSeconds(6)
        });
        db.ProjectFactSnapshots.Add(new ProjectFactSnapshot
        {
            Id = "fact-record-002",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "project-1-chapter-002",
            ChapterVersionId = "version-chapter-002-v1",
            VersionNumber = 2,
            Source = "chapter_commit",
            SnapshotJson = "{\"endingState\":\"沈砚冲进旧邮路入口\"}",
            CreatedAt = DateTime.UtcNow.AddSeconds(7)
        });
        await db.SaveChangesAsync();
        INovelProductionStateQueryService service = CreateService(db);

        var state = await service.QueryAsync(new NovelProductionStateQueryRequest(
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            RunId: "runtime-run-1",
            ChapterId: "project-1-chapter-002",
            ChapterNumber: 0,
            IncludeEvents: true));

        Assert.NotNull(state);
        var chain = Assert.Single(state!.ProductionChains);
        Assert.Contains(chain.Steps, step => step.Key == "draft" && step.ArtifactId == "draft-artifact-002");
        Assert.Contains(chain.Steps, step => step.Key == "changes" && step.ArtifactId == "change-record-002");
        Assert.Contains(chain.Steps, step => step.Key == "gate" && step.ArtifactId == "gate-artifact-002");
        Assert.Contains(chain.Steps, step => step.Key == "quality" && step.ArtifactId == "review-artifact-002");
        Assert.Contains(chain.Steps, step => step.Key == "facts" && step.ArtifactId == "fact-record-002");
        Assert.Equal("fact-record-002", chain.FactSnapshotId);
        Assert.Equal(2, chain.FactSnapshotVersion);
        Assert.NotNull(chain.Evidence.Gate);
        Assert.Equal("validated", chain.Evidence.Gate!.Status);
        Assert.NotNull(chain.Evidence.AgentReview);
        Assert.Equal("Pass", chain.Evidence.AgentReview!.OverallResult);
        Assert.False(chain.Evidence.AgentReview.MeetsAcceptedCreativeIntents);
        Assert.Equal("medium", chain.Evidence.AgentReview.ContinuityRisk);
        Assert.Equal("battle_feedback_weak", chain.Evidence.AgentReview.ChapterPacing);
        Assert.Equal("revise_before_commit", chain.Evidence.AgentReview.RecommendedAction);
        Assert.Contains("打怪升级未落实，仍偏情绪拉扯。", chain.Evidence.AgentReview.Problems);
        Assert.Contains("重写战斗段落，让邮徽只负责识路。", chain.Evidence.AgentReview.Suggestions);
        Assert.NotNull(chain.Evidence.FactSnapshot);
        Assert.Equal("沈砚冲进旧邮路入口", chain.Evidence.FactSnapshot!.EndingState);
        Assert.Equal(1, chain.Evidence.ChapterChangeCount);
    }

    [Fact]
    public async Task QueryAsync_NonAdminCannotReadOtherUserProductionState()
    {
        await using var db = CreateDb();
        await SeedProductionStateAsync(db);
        INovelProductionStateQueryService service = CreateService(db);

        var state = await service.QueryAsync(new NovelProductionStateQueryRequest(
            UserId: "user-2",
            SessionId: "session-2",
            ProjectId: "project-1",
            RunId: "runtime-run-1",
            ChapterId: "",
            ChapterNumber: 0,
            IncludeEvents: true));

        Assert.Null(state);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static INovelProductionStateQueryService CreateService(NovelAgentDbContext db) =>
        new NovelProductionStateQueryService(
            db,
            new ProductionChainProjectionService());

    private static async Task SeedProductionStateAsync(NovelAgentDbContext db)
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
            Title = "生产状态测试书",
            Status = "Writing",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.Chapters.Add(new Chapter
        {
            Id = "project-1-chapter-002",
            ProjectId = "project-1",
            Title = "第二章：黑雨邮路",
            ChapterNumber = 2,
            Status = "draft",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.AgentRuntimeRuns.Add(new AgentRuntimeRun
        {
            Id = "runtime-run-1",
            UserId = "user-1",
            SessionId = "session-1",
            ProjectId = "project-1",
            Status = "running",
            Mode = "production",
            CurrentPhase = "writing",
            CurrentStep = 3,
            ActiveTool = NovelAgentProductionStages.DraftGeneration,
            UserMessage = "继续写第二章",
            LastMessage = "正在生成正文",
            CreatedAt = DateTime.UtcNow.AddMinutes(-2),
            UpdatedAt = DateTime.UtcNow
        });
        db.AgentRuntimeEvents.Add(new AgentRuntimeEvent
        {
            Id = "runtime-event-1",
            RuntimeRunId = "runtime-run-1",
            UserId = "user-1",
            SessionId = "session-1",
            ProjectId = "project-1",
            Type = "progress",
            Stage = NovelAgentProductionStages.DraftGeneration,
            Status = "running",
            Message = "已开始生成正文",
            DisplaySurface = "workflow",
            DisplayPolicy = "timeline",
            CreatedAt = DateTime.UtcNow
        });
        db.AgentRuntimeEvents.Add(new AgentRuntimeEvent
        {
            Id = "runtime-event-2",
            RuntimeRunId = "runtime-run-1",
            UserId = "user-1",
            SessionId = "session-1",
            ProjectId = "project-1",
            Type = "production_progress",
            Stage = NovelAgentProductionStages.PackageBuilt,
            Status = "completed",
            Message = "章节生产包已构建并持久化。",
            DisplaySurface = "workflow",
            DisplayPolicy = "timeline",
            CreatedAt = DateTime.UtcNow.AddSeconds(1)
        });
        db.AgentToolExecutions.Add(new AgentToolExecution
        {
            Id = "tool-exec-1",
            UserId = "user-1",
            ProjectId = "project-1",
            SessionId = "session-1",
            RunId = "runtime-run-1",
            ToolName = NovelAgentProductionStages.DraftGeneration,
            Phase = "writing",
            Risk = "High",
            ArgumentsHash = "hash",
            SemanticContractJson = """
                {
                  "displayName":"章节正文生成",
                  "domainSurface":"创作工作流 / 小说书城",
                  "outputKind":"workflow_process_artifact",
                  "inputArtifacts":["chapter_plan_run","continuity_pack","knowledge_binding_snapshot"],
                  "outputArtifacts":["chapter_draft","chapter_version"],
                  "idempotencyPolicy":"Uses runId + targetChapterId + commitPolicy.",
                  "rollbackPolicy":"Recover through ChapterVersion rollback.",
                  "userVisibleWhere":"创作工作流",
                  "resultSemantics":"生成章节正文并进入后续门禁"
                }
                """,
            Status = "running",
            StartedAt = DateTime.UtcNow.AddSeconds(-30)
        });
        db.RevisionPlans.Add(new RevisionPlan
        {
            Id = "revision-plan-1",
            UserId = "user-1",
            ProjectId = "project-1",
            SessionId = "session-1",
            RuntimeRunId = "runtime-run-1",
            Source = "user_request",
            PlanType = "chapter_rewrite",
            TargetScope = "chapter",
            TargetChapterId = "project-1-chapter-002",
            Status = "accepted",
            RequirementsJson = "[\"第二章加入怪物围攻\"]",
            ContinuityRequirementsJson = "[\"承接第一章结尾\"]",
            ImpactAnalysisJson = "{\"affectedChapterIds\":[\"project-1-chapter-002\"]}",
            AffectedChapterIdsJson = "[\"project-1-chapter-002\"]",
            InvalidatedPackageIdsJson = "[\"pkg-chapter-002\"]",
            RiskLevel = "high",
            Recommendation = "重建第二章生产包后重新生成正文。",
            CreatedAt = DateTime.UtcNow.AddSeconds(-20),
            UpdatedAt = DateTime.UtcNow.AddSeconds(-19)
        });
        var truthStore = new ProductionTruthStore(db);
        await truthStore.CreatePackageAsync(new CreateTianmingPackageRequest(
            Id: "pkg-chapter-002",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "project-1-chapter-002",
            RuntimeRunId: "runtime-run-1",
            PackageKind: "chapter_generation",
            InputJson: "{\"goal\":\"第二章\"}",
            DependencyVersionsJson: "{\"storyBible\":2}",
            KnowledgeSnapshotJson: "{\"bindings\":[\"kb-1\"],\"knowledgeBindingSummary\":{\"bindingCount\":2,\"shouldEnterGateCount\":1,\"shouldEnterBlueprintCount\":2,\"shouldEnterFactSnapshotCount\":1,\"hardConstraintCount\":1,\"referenceCount\":1,\"classifiedCount\":1,\"pendingClassificationCount\":1,\"importedCount\":1,\"referencedCount\":1}}",
            FactSnapshotJson: "{\"previous\":\"第一章结尾\"}",
            PromptVersion: "chapter-v1",
            KernelVersion: "tianming-kernel-v1"));
        await truthStore.AppendEventAsync(new CreateProductionEventRequest(
            RuntimeRunId: "runtime-run-1",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "project-1-chapter-002",
            PackageId: "pkg-chapter-002",
            EventType: "build_package",
            Stage: "BuildChapterPackage",
            Status: "completed",
            Message: "连续性包已构建",
            ArtifactType: "TianmingPackage",
            ArtifactId: "pkg-chapter-002",
            DataJson: "{}"));
        await truthStore.AppendEventAsync(new CreateProductionEventRequest(
            RuntimeRunId: "runtime-run-1",
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "project-1-chapter-002",
            PackageId: "pkg-chapter-002",
            EventType: "creative_intents_executed",
            Stage: NovelAgentProductionStages.ChapterCommit,
            Status: "completed",
            Message: "已将 1 条生产包创意标记为已执行。",
            ArtifactType: "creative_intents",
            ArtifactId: "project-1-chapter-002",
            DataJson: "{\"executedCount\":1,\"intentIds\":[\"intent-chapter-002\"]}"));
        await truthStore.EnqueueOutboxAsync(new EnqueueOutboxEventRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: "runtime-run-1",
            EventType: "index_chapter",
            AggregateType: "chapter",
            AggregateId: "project-1-chapter-002",
            PayloadJson: "{}"));
    }
}
