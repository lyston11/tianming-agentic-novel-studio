using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Services.Workflow;
using TM.Web.NovelAgentWeb.Services.Workspace;
using TM.Web.NovelAgentWeb.Support;
using System.Reflection;
using Xunit;

namespace Tests.Unit.Services.Workflow;

public sealed class WorkflowServiceTests
{
    [Fact]
    public async Task GetProjectWorkflowAsync_WithGoal_UsesCanonicalProjectionWithoutLegacyWorkspace()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new NovelAgentDbContext(options);
        await db.Database.EnsureCreatedAsync();
        SeedUserProject(db);
        var now = DateTime.UtcNow;
        db.CreativeGoals.Add(new CreativeGoal
        {
            Id = "goal-1",
            UserId = "user-1",
            ProjectId = "project-1",
            SourceSessionId = "session-goal",
            HumanReadableObjective = "完成第一章",
            ExecutionStrategy = "interactive_batch",
            Status = "running",
            CreatedAt = now
        });
        db.TaskGraphVersions.Add(new TaskGraphVersion
        {
            Id = "graph-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            Version = 1,
            Status = "active",
            ContentHash = "graph-hash",
            CreatedAt = now
        });
        db.KernelTasks.Add(new KernelTask
        {
            Id = "task-write-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            TaskGraphVersionId = "graph-1",
            BranchId = "branch-1",
            KernelName = "writing",
            TaskType = "WriteCandidate",
            Status = "completed",
            IdempotencyKey = "goal-1:write:1",
            InputArtifactIdsJson = "[\"outline-1\"]",
            OutputArtifactIdsJson = "[\"artifact-draft-1\"]",
            Attempt = 1,
            MaxAttempts = 2,
            StartedAt = now.AddSeconds(-2),
            CompletedAt = now,
            CreatedAt = now.AddSeconds(-3),
            UpdatedAt = now
        });
        db.KernelTasks.Add(new KernelTask
        {
            Id = "task-acceptance-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            TaskGraphVersionId = "graph-1",
            BranchId = "branch-1",
            KernelName = "workflow",
            TaskType = "AcceptanceGate",
            Status = "awaiting_user",
            IdempotencyKey = "goal-1:acceptance:1",
            Attempt = 0,
            MaxAttempts = 1,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.KernelArtifacts.Add(new KernelArtifact
        {
            Id = "artifact-draft-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            TaskId = "task-write-1",
            BranchId = "branch-1",
            ArtifactType = "CandidateChapterDraft",
            ContentHash = "artifact-hash",
            Status = "proposed",
            CreatedAt = now
        });
        db.CandidateChapters.Add(new CandidateChapter
        {
            Id = "candidate-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            BranchId = "branch-1",
            ChapterId = "chapter-001",
            ChapterNumber = 1,
            Version = 1,
            CurrentArtifactId = "artifact-draft-1",
            Status = "candidate",
            CreatedAt = now
        });
        db.AgentToolExecutions.Add(new AgentToolExecution
        {
            Id = "legacy-tool-1",
            UserId = "user-1",
            ProjectId = "project-1",
            SessionId = "legacy-session",
            ToolName = "ProduceChapter",
            Phase = "writing",
            Risk = "High",
            ArgumentsHash = "legacy-hash",
            Status = "succeeded",
            StartedAt = now.AddMinutes(-5)
        });
        await db.SaveChangesAsync();

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(item => item.GetUserId()).Returns("user-1");
        var workspaceFactory = new Mock<IWorkspaceFactory>();
        var productionBridge = new Mock<IProductionWorkflowBridge>();
        productionBridge
            .Setup(item => item.LoadProjectEventsAsync("project-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WorkflowProductionEventSummary>());
        var service = new WorkflowService(
            db,
            currentUser.Object,
            workspaceFactory.Object,
            null!,
            null!,
            Mock.Of<IServiceScopeFactory>(),
            productionBridge.Object,
            new ProductionChainProjectionService(),
            NullLogger<WorkflowService>.Instance);

        var workflow = await service.GetProjectWorkflowAsync("project-1");

        Assert.Equal("goal", workflow.ProjectionKind);
        Assert.Equal("goal-1", workflow.LatestGoalId);
        Assert.Equal("session-goal", workflow.ActiveSessionId);
        Assert.Empty(workflow.Sessions);
        Assert.Empty(workflow.Runs);
        Assert.Empty(workflow.MissionPlans);
        Assert.Equal("not_loaded", workflow.LegacyAudit.Status);
        Assert.Null(workflow.LegacyAudit.MissionPlanCount);
        Assert.Null(workflow.LegacyAudit.AgentRunCount);
        Assert.Null(workflow.LegacyAudit.ToolExecutionCount);
        var task = Assert.Single(workflow.GoalState!.Tasks, item => item.TaskId == "task-write-1");
        Assert.Equal(new[] { "outline-1" }, task.InputArtifactIds);
        Assert.Equal(new[] { "artifact-draft-1" }, task.OutputArtifactIds);
        var writingStage = Assert.Single(workflow.ProductionStages, stage => stage.Key == "writing");
        Assert.Equal("task-write-1", Assert.Single(writingStage.TaskExecutions).TaskId);
        Assert.Empty(writingStage.ToolExecutions);
        var acceptanceStage = Assert.Single(workflow.ProductionStages, stage => stage.Key == "acceptance");
        Assert.Equal("awaiting_user", acceptanceStage.Status);
        Assert.Contains(workflow.ArtifactTimeline, item =>
            item.Id == "goal-artifact:artifact-draft-1" &&
            item.ChapterId == "chapter-001" &&
            item.Source == "KernelArtifact");
        workspaceFactory.Verify(
            item => item.AcquireAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateVolumeArcAsync_WithSameIdempotencyKey_ReturnsExistingVolumeArc()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new NovelAgentDbContext(options);
        await db.Database.EnsureCreatedAsync();
        SeedUserProject(db);
        var service = CreateService(db, "user-1");
        var request = new CreateVolumeArcRequest
        {
            ProjectId = "project-1",
            VolumeNumber = 1,
            VolumeTitle = "第一卷 雾城邮路",
            VolumeTheme = "旧邮路在怪物围城中重启。",
            TargetChapters = 6,
            Act1Setup = "沈砚获得银蓝邮徽。",
            Act2Confrontation = "邮路怪潮逼近雾城。",
            Act3Climax = "旧邮站开启第一条逃生线。",
            Act4Resolution = "幸存者承认邮路规则。",
            KeyEvents = "邮徽觉醒；怪潮围城；旧邮站开门",
            MajorConflict = "守住旧邮路还是放弃幸存者",
            ConflictEscalation = "每次投递都会暴露坐标",
            IdempotencyKey = "volume-arc-key-001"
        };

        var first = await service.CreateVolumeArcAsync(request);
        var second = await service.CreateVolumeArcAsync(request);

        Assert.Equal(first.Id, second.Id);
        var volumeArc = await db.VolumeArcs.SingleAsync();
        Assert.Equal("volume-arc-key-001", volumeArc.IdempotencyKey);
    }

    [Fact]
    public async Task LoadProjectToolExecutionsAsync_MapsSemanticContractForWorkflowStages()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new NovelAgentDbContext(options);
        await db.Database.EnsureCreatedAsync();
        SeedUserProject(db);
        db.AgentToolExecutions.Add(new AgentToolExecution
        {
            Id = "tool-exec-1",
            UserId = "user-1",
            ProjectId = "project-1",
            SessionId = "session-1",
            RunId = "run-1",
            ToolName = NovelAgentProductionStages.DraftGeneration,
            Phase = "writing",
            Risk = "High",
            ArgumentsHash = "hash",
            Status = "running",
            SemanticContractJson = """
                {
                  "displayName":"章节正文生成",
                  "domainSurface":"创作工作流 / 小说书城",
                  "outputKind":"workflow_process_artifact",
                  "inputArtifacts":["chapter_plan_run","continuity_pack"],
                  "outputArtifacts":["chapter_draft","chapter_version"],
                  "idempotencyPolicy":"Uses runId + targetChapterId + commitPolicy.",
                  "rollbackPolicy":"Recover through ChapterVersion rollback.",
                  "userVisibleWhere":"创作工作流",
                  "resultSemantics":"生成正文草稿"
                }
                """,
            StartedAt = DateTime.UtcNow.AddSeconds(-10)
        });
        await db.SaveChangesAsync();
        var service = CreateService(db, "user-1");
        var method = typeof(WorkflowService).GetMethod(
            "LoadProjectToolExecutionsAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var task = Assert.IsAssignableFrom<Task<IReadOnlyList<WorkflowToolExecutionSummary>>>(
            method!.Invoke(service, new object[] { "project-1", "user-1", CancellationToken.None })!);
        var tools = await task;

        var tool = Assert.Single(tools);
        Assert.Equal("章节正文生成", tool.SemanticContract.DisplayName);
        Assert.Contains("continuity_pack", tool.SemanticContract.InputArtifacts);
        Assert.Contains("chapter_version", tool.SemanticContract.OutputArtifacts);
    }

    [Fact]
    public async Task LoadProjectToolExecutionsAsync_MapsInputArtifactFailuresForWorkflowStages()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new NovelAgentDbContext(options);
        await db.Database.EnsureCreatedAsync();
        SeedUserProject(db);
        db.AgentToolExecutions.Add(new AgentToolExecution
        {
            Id = "tool-exec-blocked",
            UserId = "user-1",
            ProjectId = "project-1",
            SessionId = "session-1",
            RunId = "run-1",
            ToolName = "ProduceChapter",
            Phase = "semantic_precondition",
            Risk = "High",
            ArgumentsHash = "hash",
            Status = "failed",
            FailureJson = """
                {
                  "code":"TOOL_INPUT_ARTIFACT_BLOCKED",
                  "failedStage":"semantic_precondition",
                  "reason":"上一章提交后后台沉淀尚未完成。",
                  "recoverable":true,
                  "recommendedAction":"QueryProductionOutbox",
                  "inputArtifacts":[
                    {
                      "artifactName":"post_commit_outbox",
                      "status":"retryable_failed",
                      "artifactId":"outbox-finalize-001",
                      "message":"事实沉淀失败，可重试。",
                      "blocksExecution":true,
                      "recommendedActions":["QueryProductionOutbox","RetryProductionOutbox"]
                    }
                  ]
                }
                """,
            StartedAt = DateTime.UtcNow.AddSeconds(-10),
            CompletedAt = DateTime.UtcNow.AddSeconds(-8)
        });
        await db.SaveChangesAsync();
        var service = CreateService(db, "user-1");
        var method = typeof(WorkflowService).GetMethod(
            "LoadProjectToolExecutionsAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var task = Assert.IsAssignableFrom<Task<IReadOnlyList<WorkflowToolExecutionSummary>>>(
            method!.Invoke(service, new object[] { "project-1", "user-1", CancellationToken.None })!);
        var tools = await task;

        var failure = Assert.Single(tools).Failure;
        Assert.NotNull(failure);
        Assert.Equal("TOOL_INPUT_ARTIFACT_BLOCKED", failure!.Code);
        var inputArtifact = Assert.Single(failure.InputArtifacts);
        Assert.Equal("post_commit_outbox", inputArtifact.ArtifactName);
        Assert.Equal("retryable_failed", inputArtifact.Status);
        Assert.Equal("outbox-finalize-001", inputArtifact.ArtifactId);
        Assert.True(inputArtifact.BlocksExecution);
        Assert.Contains("RetryProductionOutbox", inputArtifact.RecommendedActions);
    }

    [Fact]
    public async Task BuildDatabaseLibraryAsync_UsesCanonicalVolumesWhenVolumeArcsAreAbsent()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new NovelAgentDbContext(options);
        await db.Database.EnsureCreatedAsync();
        SeedUserProject(db);
        db.Volumes.Add(new Volume
        {
            Id = "volume-1",
            ProjectId = "project-1",
            Title = "第一卷：黑雨旧邮路",
            VolumeNumber = 1
        });
        db.Chapters.AddRange(
            new Chapter
            {
                Id = "chapter-1",
                ProjectId = "project-1",
                VolumeId = "volume-1",
                Title = "第一章：邮徽醒来",
                ChapterNumber = 1,
                Status = "committed",
                WordCount = 3200
            },
            new Chapter
            {
                Id = "chapter-2",
                ProjectId = "project-1",
                VolumeId = "volume-1",
                Title = "第二章：旧站台追击",
                ChapterNumber = 2,
                Status = "committed",
                WordCount = 3500
            });
        await db.SaveChangesAsync();
        var service = CreateService(db, "user-1");
        var method = typeof(WorkflowService).GetMethod(
            "BuildDatabaseLibraryAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var project = await db.NovelProjects.SingleAsync(p => p.Id == "project-1");
        var currentLibrary = new NovelLibraryDocument(
            Array.Empty<NovelBookView>(),
            null,
            Array.Empty<NovelVolumeView>(),
            null,
            0,
            0,
            0);

        var task = Assert.IsAssignableFrom<Task<NovelLibraryDocument?>>(
            method!.Invoke(service, new object[]
            {
                project,
                currentLibrary,
                Array.Empty<WorkflowChapterArtifactSummary>(),
                Array.Empty<WorkflowProductionChain>(),
                Array.Empty<WorkflowCreativeIntentEvidence>(),
                CancellationToken.None
            })!);
        var library = await task;

        Assert.NotNull(library);
        Assert.Equal(1, library!.ActiveBook?.VolumeCount);
        Assert.Equal(2, library.GeneratedChapterCount);
        Assert.Equal(2, library.PlannedChapterCount);
        var volume = Assert.Single(library.Volumes);
        Assert.Equal("volume-1", volume.VolumeId);
        Assert.Equal("第一卷：黑雨旧邮路", volume.Title);
        Assert.Equal(2, volume.Chapters.Count);
        Assert.All(volume.Chapters, chapter => Assert.Equal("volume-1", chapter.VolumeId));
        Assert.DoesNotContain(library.Volumes, item => item.Title == "数据库章节");
    }

    [Fact]
    public async Task BuildDatabaseLibraryAsync_AttachesCanonicalProductionSummaryToDatabaseChapters()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new NovelAgentDbContext(options);
        await db.Database.EnsureCreatedAsync();
        SeedUserProject(db);
        db.Volumes.Add(new Volume
        {
            Id = "volume-1",
            ProjectId = "project-1",
            Title = "第一卷：黑雨旧邮路",
            VolumeNumber = 1
        });
        db.Chapters.Add(new Chapter
        {
            Id = "project-1-chapter-002",
            ProjectId = "project-1",
            VolumeId = "volume-1",
            Title = "第二章：旧邮路入口",
            ChapterNumber = 2,
            Status = "committed",
            WordCount = 4200,
            UpdatedAt = DateTime.UtcNow
        });
        db.RevisionPlans.Add(new RevisionPlan
        {
            Id = "revision-plan-002",
            UserId = "user-1",
            ProjectId = "project-1",
            RuntimeRunId = "runtime-run-1",
            Source = "creative_intent",
            PlanType = "chapter_rewrite",
            TargetScope = "chapter",
            TargetChapterId = "project-1-chapter-002",
            TargetChapterLogicalId = "chapter-002",
            TargetChapterDisplayName = "第二章：旧邮路入口",
            Status = "executed",
            RequirementsJson = """["怪物围攻", "男主用邮徽逃生"]""",
            ContinuityRequirementsJson = """["承接第一章银蓝邮徽刚激活"]""",
            ImpactAnalysisJson = """{"impact":"invalidates_old_package"}""",
            AffectedChapterIdsJson = """["project-1-chapter-002", "project-1-chapter-003"]""",
            InvalidatedPackageIdsJson = """["pkg-old-002"]""",
            RiskLevel = "medium",
            Recommendation = "重建第二章生产包并使第三章承接怪潮围站。",
            CreatedAt = DateTime.UtcNow.AddMinutes(-3),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-2)
        });
        await db.SaveChangesAsync();
        var productionChains = new[]
        {
            new WorkflowProductionChain(
                "chain-project-1-chapter-002",
                "project-1-chapter-002",
                "chapter-002",
                "第二章：旧邮路入口",
                "runtime-run-1",
                "pkg-chapter-002",
                "completed",
                "第二章生产链已完成。",
                DateTime.UtcNow.ToString("O"),
                "chapter-version-002-v2",
                2,
                "fact-snapshot-002-v2",
                2,
                new[] { "revision-plan-002" },
                new[]
                {
                    new WorkflowPackageRebuildLinkEvidence(
                        "pkg-old-002",
                        "stale",
                        "pkg-chapter-002",
                        "completed",
                        "chapter_generation",
                        "project-1-chapter-002",
                        "runtime-run-1")
                },
                new[]
                {
                    new WorkflowProductionChainStep(
                        "draft",
                        "正文草稿",
                        "completed",
                        "evt-draft-002",
                        "chapter_draft_generated",
                        NovelAgentProductionStages.DraftGenerated,
                        "chapter_draft",
                        "draft-artifact-002",
                        "正文草稿已生成，长度 4200 字。",
                        DateTime.UtcNow.AddSeconds(-8).ToString("O"),
                        string.Empty),
                    new WorkflowProductionChainStep(
                        "draft",
                        "正文草稿",
                        "failed",
                        "evt-draft-repair-002",
                        "chapter_repair_report",
                        NovelAgentProductionStages.DraftRewritten,
                        "chapter_repair_report",
                        "draft-artifact-002",
                        "章节草稿修复后仍未通过硬门禁。",
                        DateTime.UtcNow.AddSeconds(-7).ToString("O"),
                        string.Empty),
                    new WorkflowProductionChainStep(
                        "completed",
                        "生产完成",
                        "completed",
                        "evt-completed-002",
                        "chapter_production_completed",
                        NovelAgentProductionStages.RunCompleted,
                        "chapter_production_run",
                        "runtime-run-1",
                        "第二章生产闭环已完成。",
                        DateTime.UtcNow.AddSeconds(-1).ToString("O"),
                        string.Empty)
                },
                new WorkflowProductionChainEvidence(
                    new WorkflowGateEvidence(
                        "validated",
                        true,
                        true,
                        true,
                        true,
                        true,
                        Array.Empty<string>(),
                        Array.Empty<string>()),
                    new WorkflowFactSnapshotEvidence(
                        "沈砚",
                        "旧邮路守夜人",
                        "带着银蓝邮徽逃入旧站台",
                        "旧邮路入口",
                        "邮徽已激活但不能攻击",
                        "银蓝邮徽",
                        new[] { "旧邮路入口开启" },
                        "旧邮路入口开启，怪潮开始围站",
                        new[] { "第三章必须承接怪潮围站" }),
                    new WorkflowAgentReviewSummaryEvidence(
                        "commit",
                        "Pass",
                        Array.Empty<string>(),
                        new[] { "下一章扩大怪潮压力" },
                        true,
                        "low",
                        "fast",
                        "commit"),
                    1,
                    new[] { "change-record-002" }))
        };
        var creativeIntents = new[]
        {
            new WorkflowCreativeIntentEvidence(
                "intent-accepted-002",
                "第二章改成怪物围攻，男主用邮徽逃生。",
                "chapter",
                "project-1-chapter-002",
                "chapter_rewrite",
                "chat",
                "accepted"),
            new WorkflowCreativeIntentEvidence(
                "intent-global-001",
                "旧邮路每次开启都需要付出记忆代价。",
                "book",
                string.Empty,
                "mainline_change",
                "agent_suggestion",
                "executed")
        };
        var service = CreateService(db, "user-1");
        var method = typeof(WorkflowService).GetMethod(
            "BuildDatabaseLibraryAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var project = await db.NovelProjects.SingleAsync(p => p.Id == "project-1");
        var currentLibrary = new NovelLibraryDocument(
            Array.Empty<NovelBookView>(),
            null,
            Array.Empty<NovelVolumeView>(),
            null,
            0,
            0,
            0);

        var task = Assert.IsAssignableFrom<Task<NovelLibraryDocument?>>(
            method!.Invoke(service, new object[]
            {
                project,
                currentLibrary,
                Array.Empty<WorkflowChapterArtifactSummary>(),
                productionChains,
                creativeIntents,
                CancellationToken.None
            })!);
        var library = await task;

        var chapter = Assert.Single(Assert.Single(library!.Volumes).Chapters);
        Assert.NotNull(chapter.ProductionSummary);
        Assert.Equal("chain-project-1-chapter-002", chapter.ProductionSummary!.ChainId);
        Assert.Equal("completed", chapter.ProductionSummary.Status);
        Assert.Equal("chapter-version-002-v2", chapter.ProductionSummary.ChapterVersionId);
        Assert.Equal(2, chapter.ProductionSummary.ChapterVersionNumber);
        Assert.Equal("fact-snapshot-002-v2", chapter.ProductionSummary.FactSnapshotId);
        Assert.Equal("旧邮路入口开启，怪潮开始围站", chapter.ProductionSummary.EndingState);
        Assert.Equal("validated", chapter.ProductionSummary.GateStatus);
        Assert.Equal("Pass", chapter.ProductionSummary.AgentReviewResult);
        Assert.Equal(1, chapter.ProductionSummary.ChapterChangeCount);
        Assert.Equal(1, chapter.ProductionSummary.RevisionPlanCount);
        Assert.Equal(1, chapter.ProductionSummary.RebuildCount);
        Assert.Equal(new[] { "revision-plan-002" }, chapter.ProductionSummary.RevisionPlanIds);
        Assert.Equal(new[] { "pkg-old-002" }, chapter.ProductionSummary.RebuildPackageIds);
        var revisionPlan = Assert.Single(chapter.ProductionSummary.RevisionPlans);
        Assert.Equal("revision-plan-002", revisionPlan.RevisionPlanId);
        Assert.Equal("chapter_rewrite", revisionPlan.PlanType);
        Assert.Equal("executed", revisionPlan.Status);
        Assert.Equal("medium", revisionPlan.RiskLevel);
        Assert.Equal("重建第二章生产包并使第三章承接怪潮围站。", revisionPlan.Recommendation);
        Assert.Equal(new[] { "project-1-chapter-002", "project-1-chapter-003" }, revisionPlan.AffectedChapterIds);
        Assert.Equal(new[] { "pkg-old-002" }, revisionPlan.InvalidatedPackageIds);
        Assert.Contains(chapter.ProductionSummary.TraceItems, item =>
            item.Key == "revision-plan:revision-plan-002"
            && item.Label == "修订计划"
            && item.Status == "executed"
            && item.ArtifactId == "revision-plan-002"
            && item.RelatedArtifactIds.Contains("project-1-chapter-003")
            && item.RelatedArtifactIds.Contains("pkg-old-002"));
        Assert.Contains(chapter.ProductionSummary.TraceItems, item =>
            item.Key == "rebuild-package:pkg-old-002:pkg-chapter-002"
            && item.Label == "生产包重建"
            && item.Status == "completed"
            && item.ArtifactId == "pkg-chapter-002"
            && item.RelatedArtifactIds.Contains("pkg-old-002"));
        Assert.Contains(chapter.ProductionSummary.TraceItems, item =>
            item.Key == "draft:draft-artifact-002:evt-draft-002"
            && item.Label == "正文草稿"
            && item.Status == "completed"
            && item.ArtifactId == "draft-artifact-002"
            && item.Description.Contains("4200"));
        Assert.Contains(chapter.ProductionSummary.TraceItems, item =>
            item.Key == "draft:draft-artifact-002:evt-draft-repair-002"
            && item.Label == "正文草稿"
            && item.Status == "failed"
            && item.ArtifactId == "draft-artifact-002"
            && item.Description.Contains("修复后仍未通过"));
        Assert.Equal(
            chapter.ProductionSummary.TraceItems.Count,
            chapter.ProductionSummary.TraceItems.Select(item => item.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(chapter.ProductionSummary.TraceItems, item =>
            item.Key == "gate:chain-project-1-chapter-002"
            && item.Label == "门禁校验"
            && item.Status == "validated");
        Assert.Contains(chapter.ProductionSummary.TraceItems, item =>
            item.Key == "agent-review:chain-project-1-chapter-002"
            && item.Label == "Agent 总编验收"
            && item.Status == "Pass"
            && item.Description.Contains("下一章扩大怪潮压力", StringComparison.Ordinal));
        Assert.Contains(chapter.ProductionSummary.TraceItems, item =>
            item.Key == "changes:chain-project-1-chapter-002"
            && item.Label == "CHANGES 沉淀"
            && item.Status == "applied"
            && item.RelatedArtifactIds.Contains("change-record-002"));
        Assert.Contains(chapter.ProductionSummary.TraceItems, item =>
            item.Key == "chapter-version:chapter-version-002-v2"
            && item.Label == "章节版本"
            && item.Status == "v2");
        Assert.Contains(chapter.ProductionSummary.TraceItems, item =>
            item.Key == "fact-snapshot:fact-snapshot-002-v2"
            && item.Label == "事实快照"
            && item.Status == "v2"
            && item.Description.Contains("怪潮开始围站"));
        Assert.Contains(chapter.ProductionSummary.TraceItems, item =>
            item.Key == "completed:runtime-run-1"
            && item.Label == "生产完成"
            && item.Status == "completed"
            && item.Description.Contains("第二章生产闭环已完成。"));
        Assert.Equal(2, chapter.ProductionSummary.CreativeIntents.Count);
        Assert.Contains(chapter.ProductionSummary.CreativeIntents, intent =>
            intent.IntentId == "intent-accepted-002" && intent.Status == "accepted");
        Assert.Contains(chapter.ProductionSummary.CreativeIntents, intent =>
            intent.IntentId == "intent-global-001" && intent.Status == "executed");
        Assert.True(chapter.ProductionSummary.HasCanonicalEvidence);
    }

    [Fact]
    public async Task BuildDatabaseLibraryAsync_WhenCommittedChainHasFailedGateEvidence_ReportsCommittedSummaryAndKeepsGateTrace()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new NovelAgentDbContext(options);
        await db.Database.EnsureCreatedAsync();
        SeedUserProject(db);
        db.Volumes.Add(new Volume
        {
            Id = "volume-1",
            ProjectId = "project-1",
            Title = "第一卷：黑雨旧邮路",
            VolumeNumber = 1
        });
        db.Chapters.Add(new Chapter
        {
            Id = "project-1-chapter-002",
            ProjectId = "project-1",
            VolumeId = "volume-1",
            Title = "第二章：旧邮路入口",
            ChapterNumber = 2,
            Status = "committed",
            WordCount = 4200,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var productionChains = new[]
        {
            new WorkflowProductionChain(
                "chain-project-1-chapter-002",
                "project-1-chapter-002",
                "chapter-002",
                "第二章：旧邮路入口",
                "runtime-run-1",
                "pkg-chapter-002",
                "completed",
                "章节已通过硬门禁并提交成稿。",
                DateTime.UtcNow.ToString("O"),
                "chapter-version-002-v2",
                2,
                string.Empty,
                0,
                Array.Empty<string>(),
                Array.Empty<WorkflowPackageRebuildLinkEvidence>(),
                Array.Empty<WorkflowProductionChainStep>(),
                new WorkflowProductionChainEvidence(
                    new WorkflowGateEvidence(
                        "gate_failed",
                        false,
                        false,
                        true,
                        true,
                        false,
                        new[] { "CHANGES段的JSON格式错误" },
                        new[] { "重新提取 CHANGES 后复检" }),
                    null,
                    null,
                    0,
                    Array.Empty<string>()))
        };
        var service = CreateService(db, "user-1");
        var method = typeof(WorkflowService).GetMethod(
            "BuildDatabaseLibraryAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var project = await db.NovelProjects.SingleAsync(p => p.Id == "project-1");
        var currentLibrary = new NovelLibraryDocument(
            Array.Empty<NovelBookView>(),
            null,
            Array.Empty<NovelVolumeView>(),
            null,
            0,
            0,
            0);

        var task = Assert.IsAssignableFrom<Task<NovelLibraryDocument?>>(
            method!.Invoke(service, new object[]
            {
                project,
                currentLibrary,
                Array.Empty<WorkflowChapterArtifactSummary>(),
                productionChains,
                Array.Empty<WorkflowCreativeIntentEvidence>(),
                CancellationToken.None
            })!);
        var library = await task;

        var chapter = Assert.Single(Assert.Single(library!.Volumes).Chapters);
        Assert.NotNull(chapter.ProductionSummary);
        Assert.Equal("completed", chapter.ProductionSummary!.Status);
        Assert.Equal("validated", chapter.ProductionSummary.GateStatus);
        Assert.Contains("已通过硬门禁", chapter.ProductionSummary.Summary);
        var packageTrace = Assert.Single(
            chapter.ProductionSummary.TraceItems,
            item => item.Key == "package:pkg-chapter-002");
        Assert.Equal("completed", packageTrace.Status);
        Assert.Contains("已通过硬门禁", packageTrace.Description);
        var gateTrace = Assert.Single(
            chapter.ProductionSummary.TraceItems,
            item => item.Key == "gate:chain-project-1-chapter-002");
        Assert.Equal("gate_failed", gateTrace.Status);
        Assert.Contains("CHANGES段的JSON格式错误", gateTrace.Description);
    }

    [Fact]
    public async Task BuildWorkflowProductionChainsAsync_IncludesPersistedProductionArtifactsWhenEventsAreMissing()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new NovelAgentDbContext(options);
        await db.Database.EnsureCreatedAsync();
        SeedUserProject(db);
        db.Chapters.Add(new Chapter
        {
            Id = "project-1-chapter-002",
            ProjectId = "project-1",
            Title = "第二章：旧邮路入口",
            ChapterNumber = 2,
            Status = "draft",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.TianmingPackages.Add(new TianmingPackage
        {
            Id = "pkg-chapter-002",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "project-1-chapter-002",
            RuntimeRunId = "runtime-run-1",
            PackageKind = "chapter_generation",
            Status = "completed",
            InputJson = "{}",
            DependencyVersionsJson = "{}",
            KnowledgeSnapshotJson = "{}",
            FactSnapshotJson = "{}",
            CreatedAt = DateTime.UtcNow.AddSeconds(1),
            UpdatedAt = DateTime.UtcNow.AddSeconds(1)
        });
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
            DraftContent = "第二章正文草稿。",
            ContentLength = 4200,
            HasChanges = true,
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
            ReportJson = "{}",
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
            QualityScore = 88,
            ContentLength = 4200,
            CheckCount = 7,
            Summary = "Agent 总编验收通过。",
            ReviewedAt = DateTime.UtcNow.AddSeconds(6),
            CreatedAt = DateTime.UtcNow.AddSeconds(6)
        });
        db.ProjectFactSnapshots.Add(new ProjectFactSnapshot
        {
            Id = "fact-record-002",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "project-1-chapter-002",
            VersionNumber = 2,
            Source = "chapter_commit",
            SnapshotJson = "{\"endingState\":\"旧邮路入口开启\"}",
            CreatedAt = DateTime.UtcNow.AddSeconds(7)
        });
        await db.SaveChangesAsync();
        var service = CreateService(db, "user-1");
        var method = typeof(WorkflowService).GetMethod(
            "BuildWorkflowProductionChainsAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var productionEvents = new[]
        {
            new WorkflowProductionEventSummary(
                "event-package",
                "runtime-run-1",
                "project-1-chapter-002",
                "pkg-chapter-002",
                "build_package",
                NovelAgentProductionStages.PackageBuilt,
                "completed",
                "章节生产包已构建。",
                "TianmingPackage",
                "pkg-chapter-002",
                "{}",
                DateTime.UtcNow.AddSeconds(1).ToString("O"))
        };

        var task = Assert.IsAssignableFrom<Task<IReadOnlyList<WorkflowProductionChain>>>(
            method!.Invoke(service, new object[]
            {
                "project-1",
                "user-1",
                productionEvents,
                CancellationToken.None
            })!);
        var chains = await task;
        var chain = Assert.Single(chains);

        Assert.False(string.IsNullOrWhiteSpace(chain.UpdatedAt));
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
        Assert.NotNull(chain.Evidence.FactSnapshot);
        Assert.Equal("旧邮路入口开启", chain.Evidence.FactSnapshot!.EndingState);
        Assert.Equal(1, chain.Evidence.ChapterChangeCount);
    }

    private static WorkflowService CreateService(NovelAgentDbContext db, string userId)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(item => item.GetUserId()).Returns(userId);

        return new WorkflowService(
            db,
            currentUser.Object,
            Mock.Of<IWorkspaceFactory>(),
            null!,
            null!,
            Mock.Of<IServiceScopeFactory>(),
            Mock.Of<IProductionWorkflowBridge>(),
            new ProductionChainProjectionService(),
            NullLogger<WorkflowService>.Instance,
            new NovelProductionStateQueryService(
                db,
                new ProductionChainProjectionService()));
    }

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
