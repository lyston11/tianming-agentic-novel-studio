using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class ChapterCommitTruthRecorderTests
{
    [Fact]
    public async Task RecordAsync_LinksLatestCommittedVersionSavesFactSnapshotAndProductionEvent()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        IProductionTruthStore truthStore = new ProductionTruthStore(db);
        IProductionEventWriter eventWriter = new ProductionEventWriter(truthStore);
        IOutputArtifactRecorder outputArtifacts = new OutputArtifactRecorder(eventWriter);
        IChapterCommitTruthRecorder recorder = new ChapterCommitTruthRecorder(db, truthStore, eventWriter, outputArtifacts);
        var first = await truthStore.CreateChapterVersionAsync(new CreateChapterVersionRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "chapter-001",
            ContentDocumentId: "doc-1",
            Title: "第一章 旧邮徽",
            WordCount: 3000,
            Status: "committed",
            RuntimeRunId: null,
            PackageId: null,
            GateReportJson: null,
            AgentReviewJson: null));
        var latest = await truthStore.CreateChapterVersionAsync(new CreateChapterVersionRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "chapter-001",
            ContentDocumentId: "doc-2",
            Title: "第一章 银蓝邮徽",
            WordCount: 3500,
            Status: "committed",
            RuntimeRunId: null,
            PackageId: null,
            GateReportJson: null,
            AgentReviewJson: null));
        await truthStore.EnqueueOutboxAsync(new EnqueueOutboxEventRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            RuntimeRunId: null,
            EventType: "index_chapter_content",
            AggregateType: "chapter_version",
            AggregateId: latest.Id,
            PayloadJson: "{}"));
        var package = new ChapterContextPackageSummary
        {
            PackageId = "pkg-1",
            ChapterId = "chapter-001",
            Status = "context_ready",
            WorldRules = { "旧邮徽只能辨认被篡改邮路" },
            CharacterStates = { "陈默：维修站青年" },
            ActiveConflicts = { "黑雨逼近维修站" },
            ActiveForeshadowing = { "银蓝邮徽仍在发光" },
            HardContinuityFacts = { "邮徽不能攻击" },
            KnowledgeBindings =
            {
                new BoundKnowledgeSnapshot
                {
                    KnowledgeId = "knowledge-badge-boundary",
                    Title = "邮徽能力边界",
                    EntryType = "HardFact",
                    Content = "旧邮徽只能辨认被篡改邮路，不能攻击。",
                    ProjectUsageStatus = "referenced",
                    Role = "ItemRule",
                    Scope = "ProjectWide",
                    Priority = 90,
                    ConstraintLevel = "HardConstraint",
                    PackagePolicy = "DefaultEveryChapter",
                    BoundVersion = "knowledge-badge-boundary-v1",
                    UsedByChapters = { "chapter-001" },
                    ClassificationId = "classification-badge-boundary",
                    ClassificationModel = "fake-llm",
                    ClassificationRule = "旧邮徽只能辨认被篡改邮路，不能攻击。",
                    TargetEntities = { "旧邮徽" },
                    ShouldEnterGate = true,
                    ShouldEnterBlueprint = true,
                    ShouldEnterFactSnapshot = true,
                    ClassificationConfidence = 0.92
                },
                new BoundKnowledgeSnapshot
                {
                    KnowledgeId = "knowledge-style-reference",
                    Title = "废土风格参考",
                    EntryType = "StyleExample",
                    Content = "描写保持冷硬废土质感。",
                    Role = "StyleGuide",
                    Scope = "Chapter",
                    Priority = 40,
                    ConstraintLevel = "Reference",
                    PackagePolicy = "RelevantOnly",
                    ClassificationId = "classification-style-reference",
                    ClassificationModel = "fake-llm",
                    ClassificationRule = "只作为当前章节风格参考，不成为后续事实承接项。",
                    ShouldEnterGate = false,
                    ShouldEnterBlueprint = true,
                    ShouldEnterFactSnapshot = false,
                    ClassificationConfidence = 0.81
                }
            }
        };

        var record = await recorder.RecordAsync(new RecordChapterCommitTruthRequest(
            RuntimeRunId: "run-1",
            UserId: "user-1",
            ProjectId: "project-1",
            TargetChapterId: "chapter-001",
            Message: "章节已提交书城。",
            ContextPackage: package,
            DraftArtifact: new ChapterDraftArtifact
            {
                ChapterId = "chapter-001",
                ChangesJson = "{\"characters\":[\"陈默\"]}",
                HasChanges = true
            },
            GateReport: new GenerationGateReport
            {
                Status = "validated",
                Issues = { "关注黑雨代价" }
            },
            PostGenerationReview: new NovelAgentPostGenerationReview
            {
                OverallResult = "Pass",
                Summary = "评审通过"
            }));

        Assert.NotNull(record);
        Assert.Equal(latest.Id, record!.Version.Id);
        Assert.Equal("run-1", record.Version.RuntimeRunId);
        Assert.Equal("pkg-1", record.Version.PackageId);
        Assert.Contains("validated", record.Version.GateReportJson);
        Assert.Contains("Pass", record.Version.AgentReviewJson);

        var firstReloaded = await db.ChapterVersions.SingleAsync(v => v.Id == first.Id);
        Assert.Null(firstReloaded.RuntimeRunId);

        var outbox = await db.OutboxEvents.SingleAsync(e => e.AggregateId == latest.Id);
        Assert.Equal("run-1", outbox.RuntimeRunId);
        Assert.Equal("chapter_commit", record.FactSnapshot.Source);
        using var snapshotJson = JsonDocument.Parse(record.FactSnapshot.SnapshotJson);
        Assert.Equal("chapter-001", snapshotJson.RootElement.GetProperty("chapterId").GetString());
        Assert.Contains(
            snapshotJson.RootElement.GetProperty("worldRules").EnumerateArray(),
            item => item.GetString() == "旧邮徽只能辨认被篡改邮路");
        Assert.Contains(
            snapshotJson.RootElement.GetProperty("nextChapterMustCarry").EnumerateArray(),
            item => item.GetString() == "邮徽不能攻击");
        Assert.DoesNotContain(
            snapshotJson.RootElement.GetProperty("nextChapterMustCarry").EnumerateArray(),
            item => item.GetString()?.Contains("上一章已提交", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(
            snapshotJson.RootElement.GetProperty("nextChapterMustCarry").EnumerateArray(),
            item => item.GetString()?.Contains("上一章章节ID", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(
            snapshotJson.RootElement.GetProperty("nextChapterMustCarry").EnumerateArray(),
            item => item.GetString()?.Contains("上一章评审结论", StringComparison.Ordinal) == true);
        var knowledgeEvidence = Assert.Single(snapshotJson.RootElement.GetProperty("knowledgeConstraintEvidence").EnumerateArray());
        Assert.Equal("knowledge-badge-boundary", knowledgeEvidence.GetProperty("knowledgeId").GetString());
        Assert.Equal("邮徽能力边界", knowledgeEvidence.GetProperty("title").GetString());
        Assert.Equal("HardConstraint", knowledgeEvidence.GetProperty("constraintLevel").GetString());
        Assert.Equal("DefaultEveryChapter", knowledgeEvidence.GetProperty("packagePolicy").GetString());
        Assert.Equal("validated", knowledgeEvidence.GetProperty("gateStatus").GetString());
        Assert.Equal("satisfied", knowledgeEvidence.GetProperty("evidenceStatus").GetString());
        Assert.Equal("classification-badge-boundary", knowledgeEvidence.GetProperty("classificationId").GetString());
        Assert.Equal("fake-llm", knowledgeEvidence.GetProperty("classificationModel").GetString());
        Assert.Equal("旧邮徽只能辨认被篡改邮路，不能攻击。", knowledgeEvidence.GetProperty("classificationRule").GetString());
        Assert.Contains(
            knowledgeEvidence.GetProperty("targetEntities").EnumerateArray(),
            item => item.GetString() == "旧邮徽");
        Assert.True(knowledgeEvidence.GetProperty("shouldEnterGate").GetBoolean());
        Assert.True(knowledgeEvidence.GetProperty("shouldEnterBlueprint").GetBoolean());
        Assert.True(knowledgeEvidence.GetProperty("shouldEnterFactSnapshot").GetBoolean());
        Assert.Equal(0.92, knowledgeEvidence.GetProperty("classificationConfidence").GetDouble(), 3);
        Assert.DoesNotContain("knowledge-style-reference", record.FactSnapshot.SnapshotJson);
        Assert.DoesNotContain("废土风格参考", record.FactSnapshot.SnapshotJson);

        Assert.Equal("chapter_committed", record.Event.EventType);
        Assert.Equal(NovelAgentProductionStages.ChapterCommitted, record.Event.Stage);
        Assert.Equal(latest.Id, record.Event.ArtifactId);
        Assert.Contains("\"factSnapshotVersion\":1", record.Event.DataJson);

        var knowledgeUsedEvent = await db.ProductionEvents.AsNoTracking()
            .SingleAsync(evt => evt.EventType == "knowledge_bindings_used");
        Assert.Equal("chapter-001", knowledgeUsedEvent.ChapterId);
        Assert.Equal("pkg-1", knowledgeUsedEvent.PackageId);
        Assert.Equal("knowledge_bindings", knowledgeUsedEvent.ArtifactType);
        Assert.Contains("\"knowledge-badge-boundary\"", knowledgeUsedEvent.DataJson);
        Assert.Contains("\"邮徽能力边界\"", knowledgeUsedEvent.DataJson);
        Assert.Contains("\"chapterId\":\"chapter-001\"", knowledgeUsedEvent.DataJson);
        Assert.Contains("\"projectUsageStatus\":\"used\"", knowledgeUsedEvent.DataJson);

        var outputArtifactEvent = await db.ProductionEvents.AsNoTracking()
            .SingleAsync(evt => evt.EventType == OutputArtifactRecorder.EventType);
        Assert.Equal("chapter_version", outputArtifactEvent.ArtifactType);
        Assert.Equal(latest.Id, outputArtifactEvent.ArtifactId);
        Assert.Equal(NovelAgentProductionStages.ChapterCommitted, outputArtifactEvent.Stage);
        Assert.Contains("\"toolName\":\"ProduceChapter\"", outputArtifactEvent.DataJson);
        Assert.Contains("\"visibleInLibrary\":true", outputArtifactEvent.DataJson);
        Assert.Contains("\"userVisibleWhere\":[\"小说书城\",\"创作工作流\"]", outputArtifactEvent.DataJson);
    }

    [Fact]
    public async Task RecordAsync_WithSameRunAndVersionReusesCommitWithoutDuplicatingSideEffects()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        IProductionTruthStore truthStore = new ProductionTruthStore(db);
        IProductionEventWriter eventWriter = new ProductionEventWriter(truthStore);
        IOutputArtifactRecorder outputArtifacts = new OutputArtifactRecorder(eventWriter);
        IChapterCommitTruthRecorder recorder = new ChapterCommitTruthRecorder(db, truthStore, eventWriter, outputArtifacts);
        var latest = await truthStore.CreateChapterVersionAsync(new CreateChapterVersionRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "chapter-001",
            ContentDocumentId: "doc-2",
            Title: "第一章 银蓝邮徽",
            WordCount: 3500,
            Status: "committed",
            RuntimeRunId: null,
            PackageId: null,
            GateReportJson: null,
            AgentReviewJson: null));
        var request = new RecordChapterCommitTruthRequest(
            RuntimeRunId: "run-idempotent-commit",
            UserId: "user-1",
            ProjectId: "project-1",
            TargetChapterId: "chapter-001",
            Message: "章节已提交书城。",
            ContextPackage: new ChapterContextPackageSummary
            {
                PackageId = "pkg-commit",
                ChapterId = "chapter-001",
                HardContinuityFacts = { "邮徽不能攻击" }
            },
            DraftArtifact: new ChapterDraftArtifact
            {
                ChapterId = "chapter-001",
                HasChanges = true,
                ChangesJson = "{\"facts\":[\"邮徽不能攻击\"]}"
            },
            GateReport: new GenerationGateReport { Status = "validated" },
            PostGenerationReview: new NovelAgentPostGenerationReview { OverallResult = "Pass" });

        var first = await recorder.RecordAsync(request);
        var second = await recorder.RecordAsync(request);

        Assert.Equal(latest.Id, first.Version.Id);
        Assert.Equal(first.Version.Id, second.Version.Id);
        Assert.Equal(first.FactSnapshot.Id, second.FactSnapshot.Id);
        Assert.Equal(first.Event.Id, second.Event.Id);
        Assert.Single(await db.ProjectFactSnapshots
            .Where(snapshot => snapshot.ChapterId == "chapter-001" && snapshot.ChapterVersionId == latest.Id)
            .ToListAsync());
        Assert.Single(await db.ProductionEvents
            .Where(evt => evt.EventType == "chapter_committed" && evt.ArtifactId == latest.Id)
            .ToListAsync());
        Assert.Single(await db.ProductionEvents
            .Where(evt => evt.EventType == OutputArtifactRecorder.EventType &&
                          evt.ArtifactType == "chapter_version" &&
                          evt.ArtifactId == latest.Id)
            .ToListAsync());
    }

    [Fact]
    public async Task RecordAsync_MergesModelExtractedContinuityFactsIntoProjectFactSnapshot()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        IProductionTruthStore truthStore = new ProductionTruthStore(db);
        IProductionEventWriter eventWriter = new ProductionEventWriter(truthStore);
        IChapterCommitTruthRecorder recorder = new ChapterCommitTruthRecorder(db, truthStore, eventWriter);
        await truthStore.CreateChapterVersionAsync(new CreateChapterVersionRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "chapter-001",
            ContentDocumentId: "doc-2",
            Title: "第一章 银蓝邮徽",
            WordCount: 3500,
            Status: "committed",
            RuntimeRunId: null,
            PackageId: null,
            GateReportJson: null,
            AgentReviewJson: null));

        var record = await recorder.RecordAsync(new RecordChapterCommitTruthRequest(
            RuntimeRunId: "run-1",
            UserId: "user-1",
            ProjectId: "project-1",
            TargetChapterId: "chapter-001",
            Message: "章节已提交书城。",
            ContextPackage: new ChapterContextPackageSummary
            {
                PackageId = "pkg-1",
                ChapterId = "chapter-001",
                HardContinuityFacts =
                {
                    "上一章硬事实：上一章硬事实：邮徽不能攻击",
                    "上一章已提交：chapter-000",
                    "AgentReview修订要求：核心创意落地=Warning：正文没有明显体现章节核心创意。"
                },
                ActiveConflicts =
                {
                    "上一章冲突：上一章冲突：主冲突引擎：邮路防卫机制仍在追击。",
                    "当前位置：上一章废弃设备平台",
                    "Story Bible：这是生产包输入上下文，不应该回灌为下一章硬承接。"
                }
            },
            DraftArtifact: null,
            GateReport: null,
            PostGenerationReview: null,
            ContinuityFacts: new ChapterContinuityFacts
            {
                ChapterId = "chapter-001",
                ChapterTitle = "第一章 银蓝邮徽",
                ProtagonistName = "沈砚",
                ProtagonistIdentity = "雾潮城第七码头的低阶星渊邮差",
                ProtagonistStatus = "负伤但清醒，已获得银蓝邮徽",
                CurrentLocation = "废弃邮局地下室",
                SystemState = "邮徽只能辨认旧邮路和读取残响",
                EquipmentState = "银蓝邮徽完整但不能攻击",
                KeyEvents = { "沈砚觉醒银蓝邮徽", "邮徽指向旧邮路网络入口" },
                EndingState = "沈砚站在旧邮路入口前，准备进入更深层网络",
                NextChapterMustCarry = { "银蓝邮徽不能攻击", "承接旧邮路入口选择" }
            }));

        using var snapshotJson = JsonDocument.Parse(record.FactSnapshot.SnapshotJson);
        var root = snapshotJson.RootElement;
        Assert.Equal("沈砚", root.GetProperty("protagonistName").GetString());
        Assert.Equal("雾潮城第七码头的低阶星渊邮差", root.GetProperty("protagonistIdentity").GetString());
        Assert.Equal("负伤但清醒，已获得银蓝邮徽", root.GetProperty("protagonistStatus").GetString());
        Assert.Equal("废弃邮局地下室", root.GetProperty("currentLocation").GetString());
        Assert.Equal("邮徽只能辨认旧邮路和读取残响", root.GetProperty("systemState").GetString());
        Assert.Equal("银蓝邮徽完整但不能攻击", root.GetProperty("equipmentState").GetString());
        Assert.Contains(root.GetProperty("keyEvents").EnumerateArray(), item => item.GetString() == "沈砚觉醒银蓝邮徽");
        Assert.Equal("沈砚站在旧邮路入口前，准备进入更深层网络", root.GetProperty("endingState").GetString());
        Assert.Equal("沈砚站在旧邮路入口前，准备进入更深层网络", root.GetProperty("chapterEndingState").GetString());
        Assert.Contains(root.GetProperty("nextChapterMustCarry").EnumerateArray(), item => item.GetString() == "承接旧邮路入口选择");
        Assert.DoesNotContain(
            root.GetProperty("nextChapterMustCarry").EnumerateArray(),
            item => item.GetString()?.Contains("上一章硬事实：上一章硬事实", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(
            root.GetProperty("nextChapterMustCarry").EnumerateArray(),
            item => item.GetString()?.Contains("上一章已提交", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(
            root.GetProperty("nextChapterMustCarry").EnumerateArray(),
            item => item.GetString()?.Contains("AgentReview修订要求", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(
            root.GetProperty("nextChapterMustCarry").EnumerateArray(),
            item => item.GetString()?.Contains("上一章冲突：上一章冲突", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(
            root.GetProperty("nextChapterMustCarry").EnumerateArray(),
            item => item.GetString() == "主冲突引擎：邮路防卫机制仍在追击。");
        Assert.DoesNotContain(
            root.GetProperty("nextChapterMustCarry").EnumerateArray(),
            item => item.GetString()?.Contains("上一章废弃设备平台", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(
            root.GetProperty("nextChapterMustCarry").EnumerateArray(),
            item => item.GetString()?.Contains("Story Bible", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task RecordAsync_MarksSourceRevisionPlansExecutedAndEmitsProductionEvents()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        db.RevisionPlans.AddRange(
            new RevisionPlan
            {
                Id = "revision-plan-001",
                UserId = "user-1",
                ProjectId = "project-1",
                SessionId = "session-1",
                RuntimeRunId = "run-revision",
                Source = "user_request",
                PlanType = "chapter_rewrite",
                TargetScope = "chapter",
                TargetChapterId = "chapter-001",
                Status = "ready_for_rebuild",
                RequirementsJson = "[\"第一章增加怪物围攻\"]",
                ContinuityRequirementsJson = "[\"承接银蓝邮徽不能攻击\"]",
                ImpactAnalysisJson = "{}",
                AffectedChapterIdsJson = "[\"chapter-001\"]",
                InvalidatedPackageIdsJson = "[\"pkg-old\"]",
                RiskLevel = "high",
                Recommendation = "按新方向重写第一章。",
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-4)
            },
            new RevisionPlan
            {
                Id = "revision-plan-other",
                UserId = "user-1",
                ProjectId = "project-1",
                Source = "user_request",
                PlanType = "chapter_rewrite",
                TargetScope = "chapter",
                TargetChapterId = "chapter-002",
                Status = "ready_for_rebuild",
                RequirementsJson = "[\"第二章另行处理\"]",
                ContinuityRequirementsJson = "[]",
                ImpactAnalysisJson = "{}",
                AffectedChapterIdsJson = "[\"chapter-002\"]",
                InvalidatedPackageIdsJson = "[]",
                RiskLevel = "medium",
                Recommendation = "第二章待重建。",
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-4)
            });
        await db.SaveChangesAsync();

        IProductionTruthStore truthStore = new ProductionTruthStore(db);
        IProductionEventWriter eventWriter = new ProductionEventWriter(truthStore);
        IChapterCommitTruthRecorder recorder = new ChapterCommitTruthRecorder(db, truthStore, eventWriter);
        var latest = await truthStore.CreateChapterVersionAsync(new CreateChapterVersionRequest(
            UserId: "user-1",
            ProjectId: "project-1",
            ChapterId: "chapter-001",
            ContentDocumentId: "doc-2",
            Title: "第一章 银蓝邮徽",
            WordCount: 3500,
            Status: "committed",
            RuntimeRunId: null,
            PackageId: null,
            GateReportJson: null,
            AgentReviewJson: null));
        var package = new ChapterContextPackageSummary
        {
            PackageId = "pkg-new",
            ChapterId = "chapter-001",
            SourceRevisionPlans =
            {
                new RevisionPlanSnapshot
                {
                    RevisionPlanId = "revision-plan-001",
                    PlanType = "chapter_rewrite",
                    TargetScope = "chapter",
                    TargetChapterId = "chapter-001",
                    Status = "ready_for_rebuild",
                    RequirementsJson = "[\"第一章增加怪物围攻\"]",
                    ContinuityRequirementsJson = "[\"承接银蓝邮徽不能攻击\"]",
                    AffectedChapterIdsJson = "[\"chapter-001\"]",
                    InvalidatedPackageIdsJson = "[\"pkg-old\"]",
                    RiskLevel = "high",
                    Recommendation = "按新方向重写第一章。"
                }
            }
        };

        var record = await recorder.RecordAsync(new RecordChapterCommitTruthRequest(
            RuntimeRunId: "run-commit",
            UserId: "user-1",
            ProjectId: "project-1",
            TargetChapterId: "chapter-001",
            Message: "章节已提交书城。",
            ContextPackage: package,
            DraftArtifact: null,
            GateReport: new GenerationGateReport { Status = "validated" },
            PostGenerationReview: new NovelAgentPostGenerationReview { OverallResult = "Pass" }));

        var executedPlan = await db.RevisionPlans.SingleAsync(plan => plan.Id == "revision-plan-001");
        var untouchedPlan = await db.RevisionPlans.SingleAsync(plan => plan.Id == "revision-plan-other");
        Assert.Equal("executed", executedPlan.Status);
        Assert.Equal("ready_for_rebuild", untouchedPlan.Status);
        Assert.True(executedPlan.UpdatedAt > executedPlan.CreatedAt);

        using var snapshotJson = JsonDocument.Parse(record.FactSnapshot.SnapshotJson);
        var sourcePlans = snapshotJson.RootElement.GetProperty("sourceRevisionPlans").EnumerateArray().ToList();
        Assert.Contains(sourcePlans, item => item.GetProperty("revisionPlanId").GetString() == "revision-plan-001");

        var executionEvent = await db.ProductionEvents.SingleAsync(evt =>
            evt.EventType == "revision_plan_executed" &&
            evt.ArtifactType == "RevisionPlan" &&
            evt.ArtifactId == "revision-plan-001");
        Assert.Equal(NovelAgentProductionStages.ChapterCommitted, executionEvent.Stage);
        Assert.Equal("executed", executionEvent.Status);
        Assert.Equal("chapter-001", executionEvent.ChapterId);
        Assert.Null(executionEvent.PackageId);
        Assert.Contains(latest.Id, executionEvent.DataJson);
    }

    [Fact]
    public async Task RecordAsync_ThrowsWhenChapterCannotBeResolved()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        IProductionTruthStore truthStore = new ProductionTruthStore(db);
        IProductionEventWriter eventWriter = new ProductionEventWriter(truthStore);
        IChapterCommitTruthRecorder recorder = new ChapterCommitTruthRecorder(db, truthStore, eventWriter);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            recorder.RecordAsync(new RecordChapterCommitTruthRequest(
                RuntimeRunId: "run-1",
                UserId: "user-1",
                ProjectId: "project-1",
                TargetChapterId: "chapter-999",
                Message: "章节已提交书城。",
                ContextPackage: null,
                DraftArtifact: null,
                GateReport: null,
                PostGenerationReview: null)));

        Assert.Contains("Committed chapter cannot be resolved", ex.Message);
        Assert.Empty(await db.ProductionEvents.ToListAsync());
        Assert.Empty(await db.ProjectFactSnapshots.ToListAsync());
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
            Title = "第一章 银蓝邮徽",
            ChapterNumber = 1,
            Status = "committed",
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
                Title = "第一章 银蓝邮徽",
                ContentHash = "hash-2",
                Version = 2
            });
        db.SaveChanges();
    }
}
