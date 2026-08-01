using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Moq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TM.Web.NovelAgentWeb.Controllers;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Canon;
using TM.Web.NovelAgentWeb.Services.Execution;
using TM.Web.NovelAgentWeb.Services.Goals;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Services.Rework;
using Xunit;

namespace Tests.Unit.Controllers;

public sealed class GoalWorkflowControllerTests
{
    [Fact]
    public async Task Pause_ReturnsPersistedPauseState()
    {
        await using var db = CreateDb();
        SeedGoal(db);
        await db.SaveChangesAsync();
        var control = new Mock<IGoalControlService>();
        control.Setup(service => service.RequestPauseAsync("user-1", "goal-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("paused");
        var controller = CreateController(db, control: control.Object);

        var result = Assert.IsType<OkObjectResult>(await controller.Pause("goal-1", CancellationToken.None));

        var response = Assert.IsType<GoalWorkflowControlResponse>(result.Value);
        Assert.Equal("paused", response.Status);
    }

    [Fact]
    public async Task MergePrefix_AfterSuccessfulExplicitMerge_CompletesManualWorkflowTasks()
    {
        await using var db = CreateDb();
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-1",
            UserId = "user-1",
            Title = "测试小说",
            Status = "draft"
        });
        SeedGoal(db);
        db.BookProductions.Add(new BookProduction
        {
            Id = "production-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            TargetStartChapterNumber = 1,
            TargetEndChapterNumber = 3,
            NextChapterNumber = 1,
            BatchSize = 3,
            CurrentBatchNumber = 1
        });
        db.ProductionBatches.Add(new ProductionBatch
        {
            Id = "batch-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            BookProductionId = "production-1",
            BatchNumber = 1,
            StartChapterNumber = 1,
            EndChapterNumber = 3,
            CanonBranchId = "branch-1",
            TaskGraphVersionId = "graph-1",
            Status = "running"
        });
        db.TaskGraphVersions.Add(new TaskGraphVersion
        {
            Id = "graph-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            Version = 1,
            GraphJson = "{}",
            ContentHash = "graph-hash"
        });
        db.CanonBranches.Add(new CanonBranch
        {
            Id = "branch-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            StartChapterNumber = 1,
            EndChapterNumber = 3
        });
        db.KernelTasks.AddRange(
            ManualTask("graph-1:user-acceptance", "UserAcceptance", "ready"),
            ManualTask("graph-1:prefix-merge", "PrefixMerge", "blocked"));
        db.KernelArtifacts.AddRange(
            new KernelArtifact
            {
                Id = "acceptance-artifact-1",
                UserId = "user-1",
                ProjectId = "project-1",
                GoalId = "goal-1",
                TaskId = "graph-1:user-acceptance",
                BranchId = "branch-1",
                ArtifactType = "AcceptanceDecision",
                ContentJson = "{}",
                ContentHash = "acceptance-hash",
                Status = "adopted",
                Authorship = "human"
            },
            new KernelArtifact
            {
                Id = "merge-artifact-1",
                UserId = "user-1",
                ProjectId = "project-1",
                GoalId = "goal-1",
                TaskId = "graph-1:prefix-merge",
                BranchId = "branch-1",
                ArtifactType = "MergeRecord",
                ContentJson = "{}",
                ContentHash = "merge-hash",
                Status = "adopted",
                Authorship = "human"
            });
        await db.SaveChangesAsync();
        var merger = new Mock<IPrefixMergeService>(MockBehavior.Strict);
        merger.Setup(service => service.MergeAcceptedPrefixAsync("branch-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BranchMergeRecord
            {
                Id = "merge-1",
                UserId = "user-1",
                ProjectId = "project-1",
                GoalId = "goal-1",
                BranchId = "branch-1",
                StartChapterNumber = 1,
                EndChapterNumber = 3
            });
        var controller = CreateController(db, prefixMerge: merger.Object);

        await controller.MergePrefix("goal-1", new GoalPrefixMergeRequest("branch-1"), CancellationToken.None);

        var tasks = await db.KernelTasks.OrderBy(task => task.Priority).ToListAsync();
        Assert.All(tasks, task => Assert.Equal("completed", task.Status));
        Assert.All(tasks, task => Assert.NotNull(task.CompletedAt));
        Assert.Equal(
            new[] { "acceptance-artifact-1" },
            JsonSerializer.Deserialize<string[]>(tasks.Single(task => task.TaskType == "UserAcceptance").OutputArtifactIdsJson));
        Assert.Equal(
            new[] { "merge-artifact-1" },
            JsonSerializer.Deserialize<string[]>(tasks.Single(task => task.TaskType == "PrefixMerge").OutputArtifactIdsJson));
        merger.VerifyAll();
    }

    [Fact]
    public async Task Confirm_WhenGoalAlreadyExists_ReturnsPersistedGraphWithoutRecompiling()
    {
        await using var db = CreateDb();
        SeedGoal(db);
        var graph = new TaskGraphDefinition("goal-1", 1, []);
        db.TaskGraphVersions.Add(new TaskGraphVersion
        {
            Id = "graph-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            Version = 1,
            GraphJson = JsonSerializer.Serialize(graph),
            ContentHash = "graph-hash"
        });
        await db.SaveChangesAsync();

        var assessment = new CommitmentAssessment(
            DialogueCommitmentState.Committed,
            GoalAuthorizationKind.ExplicitAction,
            1,
            false,
            "用户已确认。",
            Contract());
        var commitments = new Mock<ICommitmentAssessmentService>();
        commitments.Setup(service => service.AssessAsync(
                It.IsAny<CommitmentAssessmentRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(assessment);
        var goals = new Mock<ICreativeGoalService>();
        goals.Setup(service => service.SubmitAsync(
                It.IsAny<CreateCreativeGoalCommand>(),
                assessment,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoalSubmissionResult(GoalSubmissionStatus.Existing, "goal-1"));
        var compiler = new Mock<IGoalCompiler>(MockBehavior.Strict);
        var controller = CreateController(
            db,
            commitments: commitments.Object,
            goals: goals.Object,
            compiler: compiler.Object);
        var request = new CommitCreativeGoalRequest(
            new CommitmentAssessmentRequest("project-1", "coauthor", [], "[]", "{}", true, Contract()),
            new CreateCreativeGoalCommand("project-1", "session-1", "confirm-key", 10, Contract()));

        var result = Assert.IsType<OkObjectResult>(await controller.Confirm(request, CancellationToken.None));

        var response = Assert.IsType<GoalWorkflowConfirmationResponse>(result.Value);
        Assert.Equal(GoalSubmissionStatus.Existing, response.Submission.Status);
        Assert.Equal(graph.GoalId, response.Graph.GoalId);
        Assert.Equal(graph.Version, response.Graph.Version);
        Assert.Empty(response.Graph.Nodes);
        compiler.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Confirm_WhenCompilationFails_RollsBackCreatedGoal()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>().UseSqlite(connection).Options;
        await using var db = new NovelAgentDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var assessment = new CommitmentAssessment(
            DialogueCommitmentState.Committed,
            GoalAuthorizationKind.ExplicitAction,
            1,
            false,
            "用户已确认。",
            Contract());
        var commitments = new Mock<ICommitmentAssessmentService>();
        commitments.Setup(service => service.AssessAsync(It.IsAny<CommitmentAssessmentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(assessment);
        var goals = new Mock<ICreativeGoalService>();
        goals.Setup(service => service.SubmitAsync(It.IsAny<CreateCreativeGoalCommand>(), assessment, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                db.CreativeGoals.Add(new CreativeGoal
                {
                    Id = "goal-created",
                    UserId = "user-1",
                    ProjectId = "project-1",
                    IdempotencyKey = "confirm-key"
                });
                await db.SaveChangesAsync();
                return new GoalSubmissionResult(GoalSubmissionStatus.Created, "goal-created");
            });
        var compiler = new Mock<IGoalCompiler>();
        compiler.Setup(service => service.CompileAsync("goal-created", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("章节范围无效"));
        var controller = CreateController(db, commitments: commitments.Object, goals: goals.Object, compiler: compiler.Object);
        var request = new CommitCreativeGoalRequest(
            new CommitmentAssessmentRequest("project-1", "coauthor", [], "[]", "{}", true, Contract()),
            new CreateCreativeGoalCommand("project-1", "session-1", "confirm-key", 10, Contract()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.Confirm(request, CancellationToken.None));

        db.ChangeTracker.Clear();
        Assert.Empty(await db.CreativeGoals.ToListAsync());
    }

    [Fact]
    public async Task Revise_WhenRecompilationFails_RollsBackRevision()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>().UseSqlite(connection).Options;
        await using var db = new NovelAgentDbContext(options);
        await db.Database.EnsureCreatedAsync();
        SeedGoal(db);
        await db.SaveChangesAsync();
        var goals = new Mock<ICreativeGoalService>();
        goals.Setup(service => service.ReviseAsync(It.IsAny<ReviseCreativeGoalCommand>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                var revision = new GoalRevision
                {
                    Id = "revision-1",
                    UserId = "user-1",
                    ProjectId = "project-1",
                    GoalId = "goal-1",
                    RevisionNumber = 1,
                    Reason = "调整章节",
                    AffectedNodeIdsJson = "[\"chapter-1-plan\"]"
                };
                db.GoalRevisions.Add(revision);
                await db.SaveChangesAsync();
                return revision;
            });
        var compiler = new Mock<IGoalCompiler>();
        compiler.Setup(service => service.RecompileForRevisionAsync(
                "goal-1", "revision-1", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("未知任务节点"));
        var controller = CreateController(db, goals: goals.Object, compiler: compiler.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.Revise(
            "goal-1",
            new ReviseCreativeGoalApiRequest("调整章节", "{}", [], [], ["chapter-1-plan"]),
            CancellationToken.None));

        db.ChangeTracker.Clear();
        Assert.Empty(await db.GoalRevisions.ToListAsync());
    }

    [Fact]
    public async Task GetStatusAndChapterDetail_ReturnOnlyOwnedGoalProductionEvidence()
    {
        await using var db = CreateDb();
        SeedGoal(db);
        db.TaskGraphVersions.Add(new TaskGraphVersion
        {
            Id = "graph-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            Version = 1,
            ContentHash = "graph-hash"
        });
        db.KernelTasks.Add(new KernelTask
        {
            Id = "graph-1:write",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            TaskGraphVersionId = "graph-1",
            KernelName = "tianming_writing",
            TaskType = "WriteCandidate",
            Status = "completed",
            IdempotencyKey = "write-1"
        });
        db.CanonBranches.Add(new CanonBranch
        {
            Id = "branch-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            StartChapterNumber = 1,
            EndChapterNumber = 2
        });
        db.KernelArtifacts.Add(new KernelArtifact
        {
            Id = "artifact-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            TaskId = "graph-1:write",
            BranchId = "branch-1",
            ArtifactType = "CandidateChapterDraft",
            ContentJson = "{\"draftContent\":\"数据库候选正文\"}",
            ContentHash = "artifact-hash",
            Status = "adopted"
        });
        db.CandidateChapters.Add(new CandidateChapter
        {
            Id = "candidate-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            BranchId = "branch-1",
            ChapterId = "chapter-1",
            ChapterNumber = 1,
            CurrentArtifactId = "artifact-1"
        });
        await db.SaveChangesAsync();
        var controller = CreateController(db);

        var statusResult = Assert.IsType<OkObjectResult>(await controller.GetStatus("goal-1", CancellationToken.None));
        var status = Assert.IsType<GoalWorkflowStatusResponse>(statusResult.Value);
        var chapterResult = Assert.IsType<OkObjectResult>(await controller.GetChapter("goal-1", 1, CancellationToken.None));
        var chapter = Assert.IsType<GoalChapterDetailResponse>(chapterResult.Value);

        Assert.Equal("goal-1", status.Goal.Id);
        Assert.Single(status.Tasks);
        Assert.Equal(1, status.CandidateChapterCount);
        Assert.Equal("candidate-1", chapter.Candidate.Id);
        Assert.Equal("{\"draftContent\":\"数据库候选正文\"}", chapter.DraftArtifact.ContentJson);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => controller.GetStatus("other-user-goal", CancellationToken.None));
    }

    [Fact]
    public async Task Rework_IsIdempotentAndSchedulesDirectedTaskWithOriginalAndIntentArtifacts()
    {
        await using var db = CreateDb();
        SeedGoal(db);
        db.TaskGraphVersions.Add(new TaskGraphVersion
        {
            Id = "graph-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            Version = 1,
            ContentHash = "graph-hash"
        });
        db.KernelArtifacts.Add(new KernelArtifact
        {
            Id = "artifact-original",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            TaskId = "write-original",
            BranchId = "branch-1",
            ArtifactType = "CandidateChapterDraft",
            ContentJson = "{\"draftContent\":\"原正文\"}",
            ContentHash = "original-hash"
        });
        db.KernelArtifacts.Add(new KernelArtifact
        {
            Id = "context-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            TaskId = "graph-1:chapter-1-context",
            BranchId = "branch-1",
            ArtifactType = "ChapterContextContract",
            ContentJson = "{}",
            ContentHash = "context-hash"
        });
        db.CandidateChapters.Add(new CandidateChapter
        {
            Id = "candidate-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            BranchId = "branch-1",
            ChapterId = "chapter-1",
            ChapterNumber = 1,
            CurrentArtifactId = "artifact-original"
        });
        await db.SaveChangesAsync();
        var rework = new Mock<IReworkIntentService>();
        rework.Setup(service => service.CompileAsync(
                It.IsAny<CompileReworkIntentRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReworkIntent
            {
                Id = "intent-1",
                UserId = "user-1",
                ProjectId = "project-1",
                GoalId = "goal-1",
                BranchId = "branch-1",
                CandidateChapterId = "candidate-1",
                CandidateVersion = 1,
                TargetScope = "chapter",
                Problem = "动机不清",
                DesiredEffect = "强化因果",
                PreserveJson = "[\"人物身份\"]",
                MayChangeJson = "[\"行动过程\"]",
                MustNotChangeJson = "[\"章节结局\"]",
                AcceptanceCriteriaJson = "[\"动机有证据\"]",
                Status = "proposed"
            });
        rework.Setup(service => service.StartAutomaticAttemptAsync("intent-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReworkIntent { Id = "intent-1", Status = "executing", AttemptCount = 1 });
        var controller = CreateController(db, rework.Object);
        var request = new GoalChapterReworkRequest(
            "candidate-1", 1, "session-1", "主角行动动机不清", null, null, "", "rework-key-1");

        var first = Assert.IsType<OkObjectResult>(await controller.Rework("goal-1", 1, request, CancellationToken.None));
        var second = Assert.IsType<OkObjectResult>(await controller.Rework("goal-1", 1, request, CancellationToken.None));

        var firstResponse = Assert.IsType<GoalChapterReworkResponse>(first.Value);
        var secondResponse = Assert.IsType<GoalChapterReworkResponse>(second.Value);
        Assert.Equal(firstResponse.TaskId, secondResponse.TaskId);
        var tasks = await db.KernelTasks.OrderBy(task => task.Priority).ToListAsync();
        Assert.Equal(5, tasks.Count);
        Assert.Equal(
            ["DirectedReworkDraft", "ReviewContinuity", "ReviewLiteraryQuality", "DirectedRework", "ExtractContinuitySummary"],
            tasks.Select(task => task.TaskType).ToArray());
        Assert.Contains("artifact-original", tasks[0].InputArtifactIdsJson);
        Assert.Contains("context-1", tasks[0].InputArtifactIdsJson);
        Assert.Equal("ready", tasks[0].Status);
        Assert.All(tasks.Skip(1), task => Assert.Equal("blocked", task.Status));
        Assert.Equal("ReworkIntent", (await db.KernelArtifacts.SingleAsync(item => item.ArtifactType == "ReworkIntent")).ArtifactType);
        rework.Verify(service => service.CompileAsync(It.IsAny<CompileReworkIntentRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rework_WhenTaskInsertConflicts_RollsBackIntentAndAttempt()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>().UseSqlite(connection).Options;
        await using var db = new NovelAgentDbContext(options);
        await db.Database.EnsureCreatedAsync();
        SeedGoal(db);
        db.TaskGraphVersions.Add(new TaskGraphVersion
        {
            Id = "graph-1", UserId = "user-1", ProjectId = "project-1", GoalId = "goal-1",
            Version = 1, Status = "active", ContentHash = "graph-hash"
        });
        db.KernelArtifacts.AddRange(
            new KernelArtifact
            {
                Id = "artifact-original", UserId = "user-1", ProjectId = "project-1", GoalId = "goal-1",
                TaskId = "write-original", BranchId = "branch-1", ArtifactType = "CandidateChapterDraft",
                ContentJson = "{\"draftContent\":\"原正文\"}", ContentHash = "original-hash"
            },
            new KernelArtifact
            {
                Id = "context-1", UserId = "user-1", ProjectId = "project-1", GoalId = "goal-1",
                TaskId = "graph-1:chapter-1-context", BranchId = "branch-1", ArtifactType = "ChapterContextContract",
                ContentJson = "{}", ContentHash = "context-hash"
            });
        db.CandidateChapters.Add(new CandidateChapter
        {
            Id = "candidate-1", UserId = "user-1", ProjectId = "project-1", GoalId = "goal-1",
            BranchId = "branch-1", ChapterId = "chapter-1", ChapterNumber = 1, Version = 1,
            CurrentArtifactId = "artifact-original"
        });
        const string requestKey = "conflicting-rework";
        var taskKey = $"goal-rework:goal-1:{requestKey}";
        db.KernelTasks.Add(new KernelTask
        {
            Id = "existing-conflict", UserId = "user-1", ProjectId = "project-1", GoalId = "goal-1",
            TaskGraphVersionId = "graph-1", KernelName = "continuity_review", TaskType = "ReviewContinuity",
            Status = "blocked", IdempotencyKey = $"{taskKey}:continuity"
        });
        await db.SaveChangesAsync();
        var rework = new Mock<IReworkIntentService>();
        rework.Setup(service => service.CompileAsync(It.IsAny<CompileReworkIntentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                var intent = new ReworkIntent
                {
                    Id = "intent-rollback", UserId = "user-1", ProjectId = "project-1", GoalId = "goal-1",
                    BranchId = "branch-1", CandidateChapterId = "candidate-1", CandidateVersion = 1,
                    TargetScope = "chapter", Problem = "问题", DesiredEffect = "效果",
                    AcceptanceCriteriaJson = "[\"通过\"]", Status = "proposed"
                };
                db.ReworkIntents.Add(intent);
                await db.SaveChangesAsync();
                return intent;
            });
        rework.Setup(service => service.StartAutomaticAttemptAsync("intent-rollback", It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                var intent = await db.ReworkIntents.SingleAsync();
                intent.Status = "executing";
                intent.AttemptCount = 1;
                await db.SaveChangesAsync();
                return intent;
            });
        var controller = CreateController(db, rework.Object);

        await Assert.ThrowsAsync<DbUpdateException>(() => controller.Rework(
            "goal-1",
            1,
            new GoalChapterReworkRequest("candidate-1", 1, "session-1", "问题", null, null, "", requestKey),
            CancellationToken.None));

        db.ChangeTracker.Clear();
        Assert.Empty(await db.ReworkIntents.ToListAsync());
        Assert.Single(await db.KernelTasks.ToListAsync());
        Assert.Empty(await db.KernelArtifacts.Where(item => item.ArtifactType == "ReworkIntent").ToListAsync());
    }

    [Fact]
    public async Task SaveManualEdit_CreatesProtectedHumanCandidateVersion()
    {
        await using var db = CreateDb();
        SeedGoal(db);
        db.CanonBranches.Add(new CanonBranch
        {
            Id = "branch-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            Status = "active",
            StartChapterNumber = 1,
            EndChapterNumber = 1
        });
        db.KernelArtifacts.Add(new KernelArtifact
        {
            Id = "artifact-original",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            TaskId = "graph-1:write",
            BranchId = "branch-1",
            ArtifactType = "CandidateChapterDraft",
            ContentJson = "{\"chapterId\":\"chapter-1\",\"draftContent\":\"原正文\"}",
            ContentHash = "original-hash"
        });
        db.CandidateChapters.Add(new CandidateChapter
        {
            Id = "candidate-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            BranchId = "branch-1",
            ChapterId = "chapter-1",
            ChapterNumber = 1,
            Version = 1,
            CurrentArtifactId = "artifact-original"
        });
        await db.SaveChangesAsync();
        var current = new Mock<ICurrentUserService>();
        current.Setup(service => service.GetUserId()).Returns("user-1");
        var controller = CreateController(db, branches: new CanonBranchService(db, current.Object));

        var result = Assert.IsType<OkObjectResult>(await controller.SaveManualEdit(
            "goal-1",
            1,
            new GoalChapterManualEditRequest("candidate-1", 1, "人工修改后的正文"),
            CancellationToken.None));

        var response = Assert.IsType<GoalChapterManualEditResponse>(result.Value);
        Assert.Equal(2, response.CandidateVersion);
        Assert.Equal("human", response.Authorship);
        Assert.True(response.IsProtected);
        var candidate = await db.CandidateChapters.SingleAsync(item => item.Id == response.CandidateChapterId);
        var artifact = await db.KernelArtifacts.SingleAsync(item => item.Id == candidate.CurrentArtifactId);
        Assert.Equal("human", candidate.Authorship);
        Assert.True(candidate.IsProtected);
        Assert.Equal("human", artifact.Authorship);
        Assert.True(artifact.IsProtected);
        using var content = JsonDocument.Parse(artifact.ContentJson);
        Assert.Equal("人工修改后的正文", content.RootElement.GetProperty("draftContent").GetString());
    }

    private static GoalWorkflowController CreateController(
        NovelAgentDbContext db,
        IReworkIntentService? rework = null,
        ICommitmentAssessmentService? commitments = null,
        ICreativeGoalService? goals = null,
        IGoalCompiler? compiler = null,
        IGoalControlService? control = null,
        IPrefixMergeService? prefixMerge = null,
        ICanonBranchService? branches = null)
    {
        var current = new Mock<ICurrentUserService>();
        current.Setup(service => service.GetUserId()).Returns("user-1");
        return new GoalWorkflowController(
            db,
            current.Object,
            commitments ?? Mock.Of<ICommitmentAssessmentService>(),
            goals ?? Mock.Of<ICreativeGoalService>(),
            compiler ?? Mock.Of<IGoalCompiler>(),
            control ?? Mock.Of<IGoalControlService>(),
            branches ?? Mock.Of<ICanonBranchService>(),
            prefixMerge ?? Mock.Of<IPrefixMergeService>(),
            rework ?? Mock.Of<IReworkIntentService>(),
            Mock.Of<IGoalProgressEventPublisher>(),
            new BookProductionService(db, current.Object, new PassingBookValidationService()));
    }

    private sealed class PassingBookValidationService : IBookValidationService
    {
        public Task<BookValidationReport> ValidateAsync(BookValidationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BookValidationReport { OverallStatus = "validated" });
    }

    private static KernelTask ManualTask(string id, string taskType, string status) => new()
    {
        Id = id,
        UserId = "user-1",
        ProjectId = "project-1",
        GoalId = "goal-1",
        TaskGraphVersionId = "graph-1",
        BranchId = "branch-1",
        KernelName = taskType == "UserAcceptance" ? "workflow" : "domain_reducer",
        TaskType = taskType,
        Status = status,
        IdempotencyKey = id,
        Priority = taskType == "UserAcceptance" ? 1 : 2
    };

    private static CreativeGoalContract Contract() => new(
        "chapter_batch",
        "coauthor",
        "推进三章",
        "{\"start\":1,\"end\":3}",
        ["保持连续"],
        ["人物动机"],
        ["推进线索"],
        ["人物关系"],
        "{}",
        "{}");

    private static void SeedGoal(NovelAgentDbContext db)
    {
        db.CreativeGoals.AddRange(
            new CreativeGoal
            {
                Id = "goal-1",
                UserId = "user-1",
                ProjectId = "project-1",
                HumanReadableObjective = "写两章",
                TotalCostLimit = 10,
                Status = "running",
                IdempotencyKey = "goal-1"
            },
            new CreativeGoal
            {
                Id = "other-user-goal",
                UserId = "user-2",
                ProjectId = "project-2",
                HumanReadableObjective = "秘密目标",
                TotalCostLimit = 10,
                Status = "running",
                IdempotencyKey = "other-user-goal"
            });
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }
}
