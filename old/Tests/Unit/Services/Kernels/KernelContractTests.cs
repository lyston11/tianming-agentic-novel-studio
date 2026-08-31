using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moq;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Canon;
using TM.Web.NovelAgentWeb.Services.Context;
using TM.Web.NovelAgentWeb.Services.DomainEvents;
using TM.Web.NovelAgentWeb.Services.Goals;
using TM.Web.NovelAgentWeb.Services.Execution;
using TM.Web.NovelAgentWeb.Services.Kernels;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Services.Quality;
using TM.Web.NovelAgentWeb.Services.Rag;
using Xunit;

namespace Tests.Unit.Services.Kernels;

public sealed class KernelContractTests
{
    [Fact]
    public async Task ExecutionRouter_ExecutesFreezeBaselinesWithoutRegisteredKernel()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new NovelAgentDbContext(options);
        var claim = Context("FreezeBaselines", []).Claim with
        {
            TaskId = "graph-1:freeze-baselines",
            KernelName = "context_compiler",
            BranchId = null
        };
        db.GoalContextSnapshots.Add(new GoalContextSnapshot
        {
            Id = "snapshot-1",
            UserId = claim.UserId,
            ProjectId = claim.ProjectId,
            GoalId = claim.GoalId,
            CanonVersion = "canon-7",
            KnowledgeVersion = "knowledge-9"
        });
        db.CreativeGoals.Add(new CreativeGoal
        {
            Id = claim.GoalId,
            UserId = claim.UserId,
            ProjectId = claim.ProjectId,
            GoalType = "write_batch",
            CollaborationMode = "coauthor",
            HumanReadableObjective = "冻结第一章生产基线",
            TargetChapterRangeJson = "{\"start\":1,\"end\":1}",
            IdempotencyKey = "goal-freeze"
        });
        db.TaskGraphVersions.Add(new TaskGraphVersion
        {
            Id = claim.TaskGraphVersionId,
            UserId = claim.UserId,
            ProjectId = claim.ProjectId,
            GoalId = claim.GoalId,
            Version = 1,
            Status = "active",
            ContentHash = "graph-freeze-hash"
        });
        db.KernelTasks.Add(new KernelTask
        {
            Id = claim.TaskId,
            UserId = claim.UserId,
            ProjectId = claim.ProjectId,
            GoalId = claim.GoalId,
            TaskGraphVersionId = claim.TaskGraphVersionId,
            KernelName = claim.KernelName,
            TaskType = claim.TaskType,
            Status = "running",
            LeaseOwner = claim.LeaseOwner,
            LeaseExpiresAt = claim.LeaseExpiresAt,
            IdempotencyKey = claim.TaskId
        });
        await db.SaveChangesAsync();
        var currentUser = new StubCurrentUserService(claim.UserId);
        var reducer = new DomainReducer(
            db,
            currentUser,
            new DomainContractValidator(db, currentUser),
            new KernelArtifactStore(db, new Tests.Unit.Support.LegacyControlPlaneCommandTestDouble(db)));
        var control = new Mock<IGoalControlService>();
        control.Setup(service => service.ReachSafePointAsync(
                claim,
                It.IsAny<IReadOnlyList<KernelArtifactProposal>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoalSafePointResult(GoalSafePointDisposition.Continue, []));
        var contexts = new AgentContextAssembler(
            db,
            Mock.Of<IMemoryStore>(),
            Mock.Of<IKnowledgeQueryTool>());
        var router = new KernelTaskExecutionRouter(contexts, new KernelRegistry([]), reducer, control.Object);

        var result = await router.ExecuteAsync(claim);

        var artifact = await db.KernelArtifacts.SingleAsync(item => result.ArtifactIds.Contains(item.Id));
        Assert.Equal("FrozenBaselines", artifact.ArtifactType);
        Assert.Contains("canon-7", artifact.ContentJson);
        Assert.Contains("knowledge-9", artifact.ContentJson);
    }

    [Fact]
    public async Task KnowledgeRetrievalKernel_CompilesEvidenceAndTianmingContext()
    {
        var retriever = new Mock<IHybridRetriever>(MockBehavior.Strict);
        retriever.Setup(service => service.RetrieveAsync(
                It.Is<RagRetrievalRequest>(request =>
                    request.UserId == "user-1" &&
                    request.ProjectId == "project-1" &&
                    request.UserMessage.Contains("旧城区") &&
                    request.Snapshot != null &&
                    request.Snapshot.CanonVersion == "canon-7" &&
                    request.Snapshot.KnowledgeVersion == 9),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EvidenceBundle(
                new RagQueryPlan([RagRoute.Continuity, RagRoute.Knowledge], ["旧城区封锁"], [], [], true),
                [new EvidenceItem("user-1", "project-1", "knowledge_entry", "knowledge-1", "旧城区曾发生封锁。", 1, ["dense"]) ]));
        var targets = new Mock<IChapterTargetResolver>(MockBehavior.Strict);
        targets.Setup(service => service.ResolveAsync(
                "user-1",
                "project-1",
                2,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("chapter-2");
        var kernel = new KnowledgeRetrievalKernel(retriever.Object, targets.Object);
        var context = Context("CompileChapterContext",
        [
            new KernelInputArtifact("plan-1", "ChapterPlan", 1, "{\"objective\":\"调查旧城区\"}", "plan-hash")
        ]) with
        {
            GoalSnapshot = new GoalContextSnapshot
            {
                Id = "snapshot-1",
                UserId = "user-1",
                ProjectId = "project-1",
                GoalId = "goal-1",
                CanonVersion = "canon-7",
                KnowledgeVersion = "knowledge:user-1:v9",
                CreatedAt = new DateTime(2026, 7, 19, 0, 0, 0, DateTimeKind.Utc)
            },
            Claim = Context("CompileChapterContext", []).Claim with
            {
                TaskId = "graph-1:chapter-2-context",
                KernelName = "knowledge_retrieval",
                BranchId = "branch-1"
            }
        };

        var output = await kernel.ExecuteAsync(context);

        Assert.Contains(output.Artifacts, artifact => artifact.ArtifactType == "ChapterContextContract");
        Assert.Contains(output.Artifacts, artifact => artifact.ArtifactType == "EvidenceBundle");
        var contract = output.Artifacts.Single(artifact => artifact.ArtifactType == "ChapterContextContract");
        var writingContext = JsonSerializer.Deserialize<TianmingChapterContextInput>(contract.ContentJson);
        Assert.Equal("chapter-2", writingContext?.Run.TargetChapterId);
        Assert.Contains("旧城区曾发生封锁。", writingContext?.Package.LongDistanceRecall ?? []);
        retriever.VerifyAll();
        targets.VerifyAll();
    }

    [Fact]
    public void ProfessionalKernels_DoNotDependOnAuthoritativeDbContext()
    {
        var kernelTypes = new[]
        {
            typeof(SettingKernel),
            typeof(NarrativePlanningKernel),
            typeof(TianmingWritingKernel)
        };

        foreach (var kernelType in kernelTypes)
        {
            Assert.DoesNotContain(kernelType.GetConstructors().SelectMany(constructor => constructor.GetParameters()),
                parameter => parameter.ParameterType == typeof(NovelAgentDbContext));
        }
    }

    [Fact]
    public void KernelRegistry_ResolvesEachProfessionalKernelByStableName()
    {
        var model = new StubKernelModelClient("{}");
        var tianming = new Mock<ITianmingProductionKernel>(MockBehavior.Strict);
        var registry = new KernelRegistry(
        [
            new SettingKernel(model),
            new NarrativePlanningKernel(model),
            new TianmingWritingKernel(tianming.Object)
        ]);

        Assert.IsType<SettingKernel>(registry.GetRequired("setting"));
        Assert.IsType<NarrativePlanningKernel>(registry.GetRequired("narrative_planning"));
        Assert.IsType<TianmingWritingKernel>(registry.GetRequired("tianming_writing"));
    }

    [Fact]
    public async Task TianmingWritingKernel_OnlyGeneratesCandidateFromCompiledContext()
    {
        var run = new NovelAgentRun
        {
            RunId = "kernel-run-1",
            UserGoal = "完成第一章候选正文",
            TargetChapterId = "chapter-1"
        };
        var package = new ChapterContextPackageSummary
        {
            PackageId = "package-1",
            ChapterId = "chapter-1",
            Status = "compiled"
        };
        var draft = new ChapterDraftArtifact
        {
            ArtifactId = "draft-1",
            ChapterId = "chapter-1",
            DraftContent = "第一章候选正文",
            ChangesJson = "{}",
            HasChanges = false
        };
        var tianming = new Mock<ITianmingProductionKernel>(MockBehavior.Strict);
        tianming.Setup(kernel => kernel.GenerateDraftWithChangesAsync(
                It.Is<NovelAgentRun>(value => value.RunId == run.RunId),
                It.Is<ChapterContextPackageSummary>(value => value.PackageId == package.PackageId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);
        var kernel = new TianmingWritingKernel(tianming.Object);
        var contextJson = JsonSerializer.Serialize(new TianmingChapterContextInput(run, package));

        var output = await kernel.ExecuteAsync(Context(
            "WriteCandidate",
            [new KernelInputArtifact("context-1", "ChapterContextContract", 1, contextJson, "hash-context")]));

        var artifact = Assert.Single(output.Artifacts);
        Assert.Equal("CandidateChapterDraft", artifact.ArtifactType);
        Assert.Equal(
            "第一章候选正文",
            JsonSerializer.Deserialize<ChapterDraftArtifact>(artifact.ContentJson)?.DraftContent);
        Assert.Single(output.Events);
        tianming.VerifyAll();
    }

    [Fact]
    public async Task TianmingWritingKernel_DirectedReworkUsesExplicitContractAndOriginalDraft()
    {
        var run = new NovelAgentRun
        {
            RunId = "kernel-run-2",
            UserGoal = "定向返工第一章",
            TargetChapterId = "chapter-1"
        };
        var package = new ChapterContextPackageSummary
        {
            PackageId = "package-1",
            ChapterId = "chapter-1",
            Status = "compiled"
        };
        var original = new ChapterDraftArtifact
        {
            ArtifactId = "draft-original",
            ChapterId = "chapter-1",
            DraftContent = "原始候选正文"
        };
        var reworked = new ChapterDraftArtifact
        {
            ArtifactId = "draft-reworked",
            ChapterId = "chapter-1",
            DraftContent = "定向返工后的候选正文"
        };
        var contract = new ReworkIntentArtifactContract(
            "rework-1",
            "selection",
            2,
            8,
            "对白太直白",
            "增加潜台词",
            ["交易结果"],
            ["选区对白"],
            ["选区外正文"],
            ["威胁不直说"]);
        var tianming = new Mock<ITianmingProductionKernel>(MockBehavior.Strict);
        tianming.Setup(kernel => kernel.GenerateDraftWithChangesAsync(
                It.Is<NovelAgentRun>(value =>
                    value.Intent == NovelAgentIntent.RewriteChapter &&
                    value.DraftArtifact != null &&
                    value.DraftArtifact.DraftContent == original.DraftContent),
                It.Is<ChapterContextPackageSummary>(value =>
                    value.DirectedRework != null &&
                    value.DirectedRework.IntentId == "rework-1" &&
                    value.DirectedRework.MustNotChange.Contains("选区外正文")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(reworked);
        var kernel = new TianmingWritingKernel(tianming.Object);
        var contextJson = JsonSerializer.Serialize(new TianmingChapterContextInput(run, package));

        var output = await kernel.ExecuteAsync(Context(
            "DirectedRework",
            [
                new KernelInputArtifact("context-1", "ChapterContextContract", 1, contextJson, "hash-context"),
                new KernelInputArtifact("draft-1", "CandidateChapterDraft", 1, JsonSerializer.Serialize(original), "hash-draft"),
                new KernelInputArtifact("rework-1", "ReworkIntent", 1, JsonSerializer.Serialize(contract), "hash-rework")
            ]));

        Assert.Equal("ReviewedCandidateChapter", Assert.Single(output.Artifacts).ArtifactType);
        Assert.Equal("CandidateChapterReworked", Assert.Single(output.Events).EventType);
        tianming.VerifyAll();
    }

    [Fact]
    public async Task TianmingWritingKernel_DirectedReworkDraftReturnsDraftForIndependentReviews()
    {
        var webJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var run = new NovelAgentRun { RunId = "kernel-rework-draft", TargetChapterId = "chapter-1" };
        var package = new ChapterContextPackageSummary { PackageId = "package-1", ChapterId = "chapter-1" };
        var original = new ChapterDraftArtifact { ChapterId = "chapter-1", DraftContent = "原正文" };
        var reworked = new ChapterDraftArtifact { ChapterId = "chapter-1", DraftContent = "返工正文" };
        var intent = new ReworkIntentArtifactContract(
            "intent-1", "chapter", null, null, "动机不清", "强化因果", ["人物身份"], ["行动过程"], ["结局"], ["动机有证据"]);
        var tianming = new Mock<ITianmingProductionKernel>(MockBehavior.Strict);
        tianming.Setup(kernel => kernel.GenerateDraftWithChangesAsync(
                It.Is<NovelAgentRun>(value => value.Intent == NovelAgentIntent.RewriteChapter),
                It.Is<ChapterContextPackageSummary>(value =>
                    value.DirectedRework != null && value.DirectedRework.IntentId == "intent-1"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(reworked);
        var kernel = new TianmingWritingKernel(tianming.Object);

        var output = await kernel.ExecuteAsync(Context(
            "DirectedReworkDraft",
            [
                new KernelInputArtifact("context-1", "ChapterContextContract", 1,
                    JsonSerializer.Serialize(new TianmingChapterContextInput(run, package), webJson), "context-hash"),
                new KernelInputArtifact("draft-1", "ReviewedCandidateChapter", 1, JsonSerializer.Serialize(original, webJson), "draft-hash"),
                new KernelInputArtifact("intent-1", "ReworkIntent", 1, JsonSerializer.Serialize(intent, webJson), "intent-hash", "human")
            ]));

        Assert.Equal("CandidateChapterDraft", Assert.Single(output.Artifacts).ArtifactType);
        Assert.Equal("CandidateChapterReworkDraftProduced", Assert.Single(output.Events).EventType);
        tianming.VerifyAll();
    }

    [Fact]
    public async Task TianmingWritingKernel_PassedReviewsPromoteOriginalWithoutAnotherModelCall()
    {
        var run = new NovelAgentRun { RunId = "kernel-run-3", TargetChapterId = "chapter-1" };
        var package = new ChapterContextPackageSummary { PackageId = "package-1", ChapterId = "chapter-1" };
        var original = new ChapterDraftArtifact
        {
            ArtifactId = "draft-original",
            ChapterId = "chapter-1",
            DraftContent = "双审稿已经通过的正文"
        };
        var continuity = new ContinuityReviewArtifact(
            ReviewVerdict.Pass,
            [],
            [],
            []);
        var literary = new LiteraryReviewDecision(
            ReviewVerdict.PassWithSuggestions,
            new Dictionary<string, LiteraryDimensionReview>(),
            ["可选建议，不阻止通过"]);
        var tianming = new Mock<ITianmingProductionKernel>(MockBehavior.Strict);
        var kernel = new TianmingWritingKernel(tianming.Object);
        var contextJson = JsonSerializer.Serialize(new TianmingChapterContextInput(run, package));

        var output = await kernel.ExecuteAsync(Context(
            "DirectedRework",
            [
                new KernelInputArtifact("context-1", "ChapterContextContract", 1, contextJson, "hash-context"),
                new KernelInputArtifact("draft-1", "CandidateChapterDraft", 1, JsonSerializer.Serialize(original), "hash-draft"),
                new KernelInputArtifact("review-1", "ContinuityReview", 1, JsonSerializer.Serialize(continuity), "hash-review-1"),
                new KernelInputArtifact("review-2", "LiteraryReview", 1, JsonSerializer.Serialize(literary), "hash-review-2")
            ]));

        Assert.Equal("ReviewedCandidateChapter", Assert.Single(output.Artifacts).ArtifactType);
        Assert.Equal("CandidateChapterReviewPassed", Assert.Single(output.Events).EventType);
        Assert.Equal(
            original.DraftContent,
            JsonSerializer.Deserialize<ChapterDraftArtifact>(output.Artifacts[0].ContentJson)?.DraftContent);
        tianming.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TianmingWritingKernel_NeverRewritesProtectedHumanCandidate()
    {
        var run = new NovelAgentRun { RunId = "kernel-run-4", TargetChapterId = "chapter-1" };
        var package = new ChapterContextPackageSummary { PackageId = "package-1", ChapterId = "chapter-1" };
        var original = new ChapterDraftArtifact
        {
            ArtifactId = "draft-human",
            ChapterId = "chapter-1",
            DraftContent = "用户亲自编辑并保护的正文"
        };
        var continuity = new ContinuityReviewArtifact(ReviewVerdict.ReworkRequired, [], [], ["存在连续性问题"]);
        var literary = new LiteraryReviewDecision(
            ReviewVerdict.ReworkRequired,
            new Dictionary<string, LiteraryDimensionReview>(),
            ["需要调整节奏"]);
        var tianming = new Mock<ITianmingProductionKernel>(MockBehavior.Strict);
        var kernel = new TianmingWritingKernel(tianming.Object);
        var contextJson = JsonSerializer.Serialize(new TianmingChapterContextInput(run, package));

        var output = await kernel.ExecuteAsync(Context(
            "DirectedRework",
            [
                new KernelInputArtifact("context-1", "ChapterContextContract", 1, contextJson, "hash-context"),
                new KernelInputArtifact("draft-1", "CandidateChapterDraft", 1, JsonSerializer.Serialize(original), "hash-draft", "human", true),
                new KernelInputArtifact("review-1", "ContinuityReview", 1, JsonSerializer.Serialize(continuity), "hash-review-1"),
                new KernelInputArtifact("review-2", "LiteraryReview", 1, JsonSerializer.Serialize(literary), "hash-review-2")
            ]));

        var artifact = Assert.Single(output.Artifacts);
        Assert.Equal("human", artifact.Authorship);
        Assert.True(artifact.IsProtected);
        Assert.Equal("CandidateChapterNeedsDecision", Assert.Single(output.Events).EventType);
        tianming.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ContinuityKernel_ExtractsLightweightCanonFromReviewedCandidateWithoutDbWrites()
    {
        const string body = "林岚公开自己是失踪王女。";
        const string quote = "公开自己是失踪王女";
        var start = body.IndexOf(quote, StringComparison.Ordinal);
        var extraction = new Mock<ILightweightCanonExtractionModelClient>(MockBehavior.Strict);
        extraction.Setup(model => model.ExtractAsync(
                It.Is<LightweightCanonExtractionRequest>(request => request.ChapterBody == body),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LightweightCanonDraft(
                [new LightweightSummaryItemDraft("identity", "key_change", "林岚公开王女身份", new CanonEvidenceSpan(start, start + quote.Length, quote))],
                [new CanonChangeDraft("change", "identity_reveal", "林岚", "公开王女身份", new CanonEvidenceSpan(start, start + quote.Length, quote))]));
        var semantic = new Mock<ILightweightCanonSemanticReviewModelClient>(MockBehavior.Strict);
        semantic.Setup(model => model.ReviewAsync(
                It.IsAny<LightweightCanonSemanticReviewRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LightweightCanonSemanticReview(["identity"], ["change"], new Dictionary<string, string>()));
        var continuityModel = new Mock<IContinuityReviewModelClient>(MockBehavior.Strict);
        var detector = new Mock<IPotentialViolationDetector>(MockBehavior.Strict);
        var kernel = new ContinuityReviewKernel(
            detector.Object,
            continuityModel.Object,
            extraction.Object,
            semantic.Object);
        var reviewed = new ChapterDraftArtifact { ChapterId = "chapter-1", DraftContent = body };

        var output = await kernel.ExecuteAsync(Context(
            "ExtractContinuitySummary",
            [new KernelInputArtifact("reviewed-1", "ReviewedCandidateChapter", 1, JsonSerializer.Serialize(reviewed), "hash-reviewed")]));

        var artifact = Assert.Single(output.Artifacts);
        Assert.Equal("ContinuitySummary", artifact.ArtifactType);
        Assert.Contains("chapter_body", artifact.ContentJson);
        Assert.Contains("identity_reveal", artifact.ContentJson);
        continuityModel.VerifyNoOtherCalls();
        detector.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("setting", "SettingProposal")]
    [InlineData("narrative_planning", "ChapterPlan")]
    public async Task ModelBackedKernels_ReturnArtifactsWithoutWritingState(string kernelName, string artifactType)
    {
        var model = new StubKernelModelClient("{\"result\":\"ok\"}");
        IKernel kernel = kernelName == "setting"
            ? new SettingKernel(model)
            : new NarrativePlanningKernel(model);

        var output = await kernel.ExecuteAsync(Context("PlanChapter", []));

        Assert.Equal(artifactType, Assert.Single(output.Artifacts).ArtifactType);
        Assert.Equal(1, model.CallCount);
    }

    [Fact]
    public async Task ExecutionRouter_LoadsDependencyArtifactsAndAdoptsKernelOutputThroughReducer()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new NovelAgentDbContext(options);
        var claim = Context("TestTask", []).Claim with
        {
            TaskId = "graph-1:current",
            KernelName = "test_kernel",
            BranchId = null
        };
        db.GoalContextSnapshots.Add(new GoalContextSnapshot
        {
            Id = "snapshot-1",
            UserId = claim.UserId,
            ProjectId = claim.ProjectId,
            GoalId = claim.GoalId
        });
        db.CreativeGoals.Add(new CreativeGoal
        {
            Id = claim.GoalId,
            UserId = claim.UserId,
            ProjectId = claim.ProjectId,
            GoalType = "write_batch",
            CollaborationMode = "coauthor",
            HumanReadableObjective = "验证执行路由",
            TargetChapterRangeJson = "{\"start\":1,\"end\":1}",
            IdempotencyKey = "goal-router"
        });
        db.TaskGraphVersions.Add(new TaskGraphVersion
        {
            Id = claim.TaskGraphVersionId,
            UserId = claim.UserId,
            ProjectId = claim.ProjectId,
            GoalId = claim.GoalId,
            Version = 1,
            Status = "active",
            ContentHash = "graph-hash"
        });
        db.KernelTasks.AddRange(
            new KernelTask
            {
                Id = "graph-1:dependency",
                UserId = claim.UserId,
                ProjectId = claim.ProjectId,
                GoalId = claim.GoalId,
                TaskGraphVersionId = claim.TaskGraphVersionId,
                KernelName = "test_kernel",
                TaskType = "Dependency",
                Status = "completed",
                OutputArtifactIdsJson = "[\"input-artifact\"]",
                IdempotencyKey = "dependency"
            },
            new KernelTask
            {
                Id = claim.TaskId,
                UserId = claim.UserId,
                ProjectId = claim.ProjectId,
                GoalId = claim.GoalId,
                TaskGraphVersionId = claim.TaskGraphVersionId,
                KernelName = claim.KernelName,
                TaskType = claim.TaskType,
                Status = "running",
                LeaseOwner = claim.LeaseOwner,
                LeaseExpiresAt = claim.LeaseExpiresAt,
                DependencyTaskIdsJson = "[\"dependency\"]",
                InputArtifactIdsJson = "[\"direct-input-artifact\"]",
                IdempotencyKey = "current"
            });
        db.KernelArtifacts.AddRange(
            new KernelArtifact
            {
                Id = "input-artifact",
                UserId = claim.UserId,
                ProjectId = claim.ProjectId,
                GoalId = claim.GoalId,
                TaskId = "graph-1:dependency",
                ArtifactType = "ChapterPlan",
                SchemaVersion = 1,
                ContentJson = "{}",
                ContentHash = "input-hash"
            },
            new KernelArtifact
            {
                Id = "direct-input-artifact",
                UserId = claim.UserId,
                ProjectId = claim.ProjectId,
                GoalId = claim.GoalId,
                TaskId = claim.TaskId,
                ArtifactType = "ReworkIntent",
                SchemaVersion = 1,
                ContentJson = "{}",
                ContentHash = "direct-input-hash"
            });
        await db.SaveChangesAsync();
        var currentUser = new StubCurrentUserService(claim.UserId);
        var reducer = new DomainReducer(
            db,
            currentUser,
            new DomainContractValidator(db, currentUser),
            new KernelArtifactStore(db, new Tests.Unit.Support.LegacyControlPlaneCommandTestDouble(db)));
        var modelExecutionScopes = new KernelModelExecutionScopeAccessor();
        var registry = new KernelRegistry([new StubKernel(modelExecutionScopes)]);
        var control = new Mock<IGoalControlService>();
        control.Setup(service => service.ReachSafePointAsync(
                claim,
                It.IsAny<IReadOnlyList<KernelArtifactProposal>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoalSafePointResult(GoalSafePointDisposition.Continue, []));
        var contexts = new AgentContextAssembler(
            db,
            Mock.Of<IMemoryStore>(),
            Mock.Of<IKnowledgeQueryTool>());
        var router = new KernelTaskExecutionRouter(
            contexts,
            registry,
            reducer,
            control.Object,
            modelExecutionScopes);

        var result = await router.ExecuteAsync(claim);

        Assert.Single(result.ArtifactIds);
        Assert.Contains(await db.KernelArtifacts.ToListAsync(), artifact => artifact.ArtifactType == "OutputArtifact");
        Assert.Single(await db.DomainEvents.ToListAsync());
        Assert.Single(await db.OutboxEvents.ToListAsync());
        Assert.Null(modelExecutionScopes.Current);
    }

    private static KernelExecutionContext Context(
        string taskType,
        IReadOnlyList<KernelInputArtifact> artifacts) => new(
            new KernelTaskClaim(
                "task-1",
                "user-1",
                "project-1",
                "goal-1",
                "graph-1",
                "branch-1",
                taskType is "WriteCandidate" or "DirectedRework" ? "tianming_writing" : "narrative_planning",
                taskType,
                1,
                "worker-1",
                DateTime.UtcNow.AddMinutes(2)),
            new GoalContextSnapshot
            {
                Id = "snapshot-1",
                UserId = "user-1",
                ProjectId = "project-1",
                GoalId = "goal-1"
            },
            GoalContract(),
            artifacts);

    private static KernelGoalContract GoalContract() => new(
        "write_batch",
        "coauthor",
        "写第一章",
        "{\"start\":1,\"end\":1}",
        "[]",
        "[]",
        "[]",
        "[]",
        "{}",
        "{}");

    private sealed class StubKernelModelClient(string result) : IKernelStructuredModelClient
    {
        public int CallCount { get; private set; }

        public Task<string> GenerateAsync(
            string kernelName,
            KernelExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class StubKernel(IKernelModelExecutionScopeAccessor? modelExecutionScopes = null) : IKernel
    {
        public string Name => "test_kernel";

        public Task<KernelExecutionOutput> ExecuteAsync(
            KernelExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            if (modelExecutionScopes != null)
            {
                Assert.Equal(context.Claim.UserId, modelExecutionScopes.Current?.UserId);
                Assert.Equal(context.Claim.ProjectId, modelExecutionScopes.Current?.ProjectId);
                Assert.Equal(context.Claim.GoalId, modelExecutionScopes.Current?.GoalId);
                Assert.Equal(context.Claim.TaskId, modelExecutionScopes.Current?.TaskId);
                Assert.Equal(context.Claim.KernelName, modelExecutionScopes.Current?.KernelName);
                Assert.Equal(context.Claim.Attempt, modelExecutionScopes.Current?.Attempt);
            }
            Assert.Contains(context.Inputs, input => input.Id == "input-artifact");
            Assert.Contains(context.Inputs, input => input.Id == "direct-input-artifact");
            return Task.FromResult(new KernelExecutionOutput(
            [
                new KernelArtifactProposal("OutputArtifact", 1, "{}", "output-hash", "agent", false)
            ],
            [
                new DomainEventProposal(
                    "test_aggregate",
                    "aggregate-1",
                    1,
                    "TestProduced",
                    ["@artifact:0"],
                    ["input-artifact"],
                    "{}",
                    "test-event")
            ]));
        }
    }

    private sealed class StubCurrentUserService(string userId) : ICurrentUserService
    {
        public string GetUserId() => userId;
        public string GetUsername() => "author";
        public string GetEmail() => "author@example.com";
        public string GetRole() => "author";
        public bool IsAdmin() => false;
        public bool IsAuthenticated() => true;
        public string? TryGetUserId() => userId;
    }
}
