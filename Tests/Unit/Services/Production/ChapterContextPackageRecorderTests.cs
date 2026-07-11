using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class ChapterContextPackageRecorderTests
{
    [Fact]
    public async Task RecordAsync_PersistsPackageAndBuildEventThroughProductionWriters()
    {
        await using var db = CreateDb();
        SeedProject(db);
        IProductionTruthStore truthStore = new ProductionTruthStore(db);
        IProductionEventWriter eventWriter = new ProductionEventWriter(truthStore);
        IAgentRuntimeEventService runtimeEvents = new AgentRuntimeEventService(db);
        IChapterContextPackageRecorder recorder = new ChapterContextPackageRecorder(truthStore, eventWriter, runtimeEvents, db);
        var package = new ChapterContextPackageSummary
        {
            ChapterId = "chapter-001",
            Status = "context_ready",
            WorldRules = { "黑雨会腐蚀记忆标签" },
            CharacterStates = { "陈默持有银蓝邮徽" },
            HardContinuityFacts = { "邮徽只能识别邮路，不能攻击" },
            ChapterBlueprints = { "第一幕：陈默在废弃邮局发现银蓝邮徽。", "结尾：黑雨逼近，必须逃入旧邮路。" },
            DesignRules =
            {
                new DesignRuleSnapshot
                {
                    RuleId = "rule-blue-badge-boundary",
                    RuleType = "ItemBoundary",
                    RuleContent = "银蓝邮徽不能攻击、治愈或升级。",
                    ConstraintLevel = "HardConstraint",
                    Scope = "ProjectWide",
                    Priority = 90,
                    SourceKnowledgeIds = { "hardfact-1" }
                }
            },
            PersistedBlueprint = new PersistedChapterBlueprintSnapshot
            {
                BlueprintId = "blueprint-chapter-001",
                ChapterId = "chapter-001",
                ChapterIndex = 1,
                Title = "第一章：银蓝邮徽",
                Intent = "建立银蓝邮徽能力边界和黑雨危机。",
                KeyEvents = { "陈默发现银蓝邮徽", "黑雨逼近" },
                Characters = { "陈默" },
                EndingNote = "陈默带着银蓝邮徽逃入旧邮路。",
                RequiredKnowledgeIds = { "hardfact-1" },
                AppliedDesignRuleIds = { "rule-blue-badge-boundary" },
                Version = 2,
                Status = "Accepted",
                TargetWordCount = 3200
            },
            KnowledgeBindings =
            {
                new BoundKnowledgeSnapshot
                {
                    KnowledgeId = "hardfact-1",
                    Title = "银蓝邮徽能力边界",
                    EntryType = "HardFact",
                    Content = "银蓝邮徽只能辨认被篡改邮路，不能攻击、治愈或升级。",
                    Role = "ItemRule",
                    Scope = "ProjectWide",
                    Priority = 80,
                    ConstraintLevel = "HardConstraint",
                    PackagePolicy = "DefaultEveryChapter",
                    BoundVersion = "knowledge-v3",
                    UsedByChapters = { "chapter-001" },
                    ClassificationId = "classification-hardfact-latest",
                    ClassificationModel = "fake-llm",
                    ClassificationRule = "银蓝邮徽只能辨认旧邮路，不能攻击、治愈或升级。",
                    TargetEntities = { "银蓝邮徽" },
                    ShouldEnterGate = true,
                    ShouldEnterBlueprint = true,
                    ShouldEnterFactSnapshot = true,
                    ClassificationConfidence = 0.91
                },
                new BoundKnowledgeSnapshot
                {
                    KnowledgeId = "style-1",
                    Title = "废土风格参考",
                    EntryType = "StyleExample",
                    Content = "描写保持冷硬废土质感。",
                    Role = "StyleGuide",
                    Scope = "Chapter",
                    Priority = 40,
                    ConstraintLevel = "Reference",
                    PackagePolicy = "RelevantOnly",
                    ProjectUsageStatus = "imported",
                    ShouldEnterGate = false,
                    ShouldEnterBlueprint = true,
                    ShouldEnterFactSnapshot = false
                }
            },
            AcceptedCreativeIntents =
            {
                new AcceptedCreativeIntentSnapshot
                {
                    IntentId = "intent-001",
                    NormalizedIntent = "第二章主冲突改为怪物围攻。",
                    TargetScope = "chapter",
                    TargetChapterId = "chapter-002",
                    ImpactLevel = "chapter_rewrite",
                    Source = "chat"
                }
            },
            SourceRevisionPlans =
            {
                new RevisionPlanSnapshot
                {
                    RevisionPlanId = "revision-plan-001",
                    PlanType = "chapter_rewrite",
                    TargetScope = "chapter",
                    TargetChapterId = "chapter-001",
                    Status = "ready_for_rebuild",
                    AffectedChapterIdsJson = "[\"chapter-001\",\"chapter-002\"]",
                    InvalidatedPackageIdsJson = "[\"pkg-old-1\",\"pkg-old-2\"]",
                    RiskLevel = "medium",
                    Recommendation = "按用户新要求重写第一章。"
                }
            },
            LongDistanceRecall = { "旧邮路每次开启都有代价" },
            Warnings = { "不得把邮徽写成武器" }
        };

        var createdPackage = await recorder.RecordAsync(new RecordChapterContextPackageRequest(
            RuntimeRunId: "run-1",
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserGoal: "构建第一章连续性包",
            RunUpdatedAt: new DateTime(2026, 6, 20, 8, 0, 0, DateTimeKind.Utc),
            Package: package));

        Assert.NotNull(createdPackage);
        Assert.Equal(createdPackage!.Id, package.PackageId);
        Assert.Equal("chapter_context_package", createdPackage.PackageKind);
        using var inputJson = JsonDocument.Parse(createdPackage.InputJson);
        Assert.Contains(
            inputJson.RootElement.GetProperty("characterStates").EnumerateArray(),
            item => item.GetString() == "陈默持有银蓝邮徽");
        Assert.Equal(new[] { "pkg-old-1", "pkg-old-2" },
            inputJson.RootElement.GetProperty("rebuiltFromPackageIds")
                .EnumerateArray()
                .Select(item => item.GetString())
                .ToArray());
        using var factJson = JsonDocument.Parse(createdPackage.FactSnapshotJson!);
        Assert.Contains(
            factJson.RootElement.GetProperty("Warnings").EnumerateArray(),
            item => item.GetString() == "不得把邮徽写成武器");
        using var knowledgeJson = JsonDocument.Parse(createdPackage.KnowledgeSnapshotJson!);
        var summary = knowledgeJson.RootElement.GetProperty("knowledgeBindingSummary");
        Assert.Equal(2, summary.GetProperty("bindingCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("shouldEnterGateCount").GetInt32());
        Assert.Equal(2, summary.GetProperty("shouldEnterBlueprintCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("shouldEnterFactSnapshotCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("hardConstraintCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("referenceCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("classifiedCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("pendingClassificationCount").GetInt32());
        var binding = knowledgeJson.RootElement.GetProperty("knowledgeBindings").EnumerateArray()
            .Single(item => item.GetProperty("knowledgeId").GetString() == "hardfact-1");
        Assert.Equal("ItemRule", binding.GetProperty("role").GetString());
        Assert.Equal("ProjectWide", binding.GetProperty("scope").GetString());
        Assert.Equal("HardConstraint", binding.GetProperty("constraintLevel").GetString());
        Assert.Equal("DefaultEveryChapter", binding.GetProperty("packagePolicy").GetString());
        Assert.Equal("knowledge-v3", binding.GetProperty("boundVersion").GetString());
        Assert.Contains(binding.GetProperty("usedByChapters").EnumerateArray(),
            item => item.GetString() == "chapter-001");
        Assert.Equal("classification-hardfact-latest", binding.GetProperty("classificationId").GetString());
        Assert.Equal("fake-llm", binding.GetProperty("classificationModel").GetString());
        Assert.Equal("银蓝邮徽只能辨认旧邮路，不能攻击、治愈或升级。", binding.GetProperty("classificationRule").GetString());
        Assert.Contains(binding.GetProperty("targetEntities").EnumerateArray(),
            item => item.GetString() == "银蓝邮徽");
        Assert.True(binding.GetProperty("shouldEnterGate").GetBoolean());
        Assert.True(binding.GetProperty("shouldEnterBlueprint").GetBoolean());
        Assert.True(binding.GetProperty("shouldEnterFactSnapshot").GetBoolean());
        Assert.Equal(0.91, binding.GetProperty("classificationConfidence").GetDouble(), precision: 2);
        Assert.Equal(new[] { "pkg-old-1", "pkg-old-2" },
            knowledgeJson.RootElement.GetProperty("rebuiltFromPackageIds")
                .EnumerateArray()
                .Select(item => item.GetString())
                .ToArray());

        var events = await db.ProductionEvents.OrderBy(e => e.CreatedAt).ToListAsync();
        Assert.Contains(events, e =>
            e.EventType == "chapter_knowledge_resolved" &&
            e.Stage == NovelAgentProductionStages.KnowledgeResolved &&
            e.PackageId == createdPackage.Id &&
            e.DataJson!.Contains("\"knowledgeBindingCount\":2", StringComparison.Ordinal));
        Assert.Contains(events, e =>
            e.EventType == "chapter_knowledge_classified" &&
            e.Stage == NovelAgentProductionStages.KnowledgeClassified &&
            e.PackageId == createdPackage.Id &&
            e.DataJson!.Contains("\"classifiedCount\":1", StringComparison.Ordinal));
        Assert.Contains(events, e =>
            e.EventType == "chapter_story_design_built" &&
            e.Stage == NovelAgentProductionStages.StoryDesignBuilt &&
            e.PackageId == createdPackage.Id &&
            e.DataJson!.Contains("\"designRuleCount\":1", StringComparison.Ordinal));
        Assert.Contains(events, e =>
            e.EventType == "chapter_blueprint_built" &&
            e.Stage == NovelAgentProductionStages.ChapterBlueprintBuilt &&
            e.PackageId == createdPackage.Id &&
            e.DataJson!.Contains("\"blueprintId\":\"blueprint-chapter-001\"", StringComparison.Ordinal));

        var evt = await db.ProductionEvents.SingleAsync(e => e.EventType == "chapter_context_package_built");
        Assert.Equal(createdPackage.Id, evt.PackageId);
        Assert.Equal(NovelAgentProductionStages.PackageBuilt, evt.Stage);
        Assert.Contains("\"hardFactCount\":1", evt.DataJson);
        Assert.Contains("\"acceptedCreativeIntentCount\":1", evt.DataJson);
        Assert.Contains("\"rebuiltFromPackageIds\":[\"pkg-old-1\",\"pkg-old-2\"]", evt.DataJson);

        var runtimeEventRows = await db.AgentRuntimeEvents
            .Where(e => e.Type == "production_progress")
            .OrderBy(e => e.CreatedAt)
            .ToListAsync();
        Assert.Contains(runtimeEventRows, e => e.Stage == NovelAgentProductionStages.KnowledgeResolved);
        Assert.Contains(runtimeEventRows, e => e.Stage == NovelAgentProductionStages.KnowledgeClassified);
        Assert.Contains(runtimeEventRows, e => e.Stage == NovelAgentProductionStages.StoryDesignBuilt);
        Assert.Contains(runtimeEventRows, e => e.Stage == NovelAgentProductionStages.ChapterBlueprintBuilt);
        var runtimeEvt = await db.AgentRuntimeEvents.SingleAsync(e =>
            e.Type == "production_progress" &&
            e.Stage == NovelAgentProductionStages.PackageBuilt);
        Assert.Equal("session-1", runtimeEvt.SessionId);
        Assert.Equal("completed", runtimeEvt.Status);
        Assert.Equal(AgentRuntimeEventSurface.Workflow, runtimeEvt.DisplaySurface);
        Assert.Equal(AgentRuntimeEventDisplayPolicy.Timeline, runtimeEvt.DisplayPolicy);
    }

    [Fact]
    public async Task RecordAsync_UsesCanonicalChapterIdWhenDatabaseChapterAlreadyExists()
    {
        await using var db = CreateDb();
        SeedProject(db);
        db.Chapters.Add(new Chapter
        {
            Id = "project-1-chapter-001",
            ProjectId = "project-1",
            Title = "第一章 旧邮路",
            ChapterNumber = 1,
            Status = "committed",
            WordCount = 1200,
            CurrentDocumentId = "doc-chapter-001",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.KnowledgeBases.Add(new KnowledgeBase
        {
            Id = "hardfact-1",
            UserId = "user-1",
            SourceProjectId = null,
            EntryType = "HardFact",
            Title = "银蓝邮徽边界",
            Content = "银蓝邮徽只能识别旧邮路，不能攻击。",
            Weight = 10,
            CreatedAt = DateTime.UtcNow
        });
        db.ProjectKnowledgeUsages.Add(new ProjectKnowledgeUsage
        {
            Id = "usage-hardfact-1",
            UserId = "user-1",
            ProjectId = "project-1",
            KnowledgeId = "hardfact-1",
            Status = "imported",
            UsageCount = 0,
            UsedByChaptersJson = "[]",
            FirstSeenAt = DateTime.UtcNow.AddHours(-1)
        });
        await db.SaveChangesAsync();

        IProductionTruthStore truthStore = new ProductionTruthStore(db);
        IProductionEventWriter eventWriter = new ProductionEventWriter(truthStore);
        IAgentRuntimeEventService runtimeEvents = new AgentRuntimeEventService(db);
        IChapterContextPackageRecorder recorder = new ChapterContextPackageRecorder(truthStore, eventWriter, runtimeEvents, db);
        var package = new ChapterContextPackageSummary
        {
            PackageId = "pkg-canonical",
            ChapterId = "chapter-001",
            Status = "context_ready",
            KnowledgeBindings =
            {
                new BoundKnowledgeSnapshot
                {
                    KnowledgeId = "hardfact-1",
                    Title = "银蓝邮徽边界",
                    EntryType = "HardFact",
                    Content = "银蓝邮徽只能识别旧邮路，不能攻击。",
                    ConstraintLevel = "HardConstraint",
                    PackagePolicy = "DefaultEveryChapter"
                }
            }
        };

        var createdPackage = await recorder.RecordAsync(new RecordChapterContextPackageRequest(
            RuntimeRunId: "run-canonical",
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserGoal: "重建第一章生产包",
            RunUpdatedAt: DateTime.UtcNow,
            Package: package));

        Assert.Equal("project-1-chapter-001", createdPackage.ChapterId);
        using var inputJson = JsonDocument.Parse(createdPackage.InputJson);
        Assert.Equal("project-1-chapter-001", inputJson.RootElement.GetProperty("chapterId").GetString());
        Assert.Equal("chapter-001", inputJson.RootElement.GetProperty("logicalChapterId").GetString());

        var evt = await db.ProductionEvents.SingleAsync(e => e.EventType == "chapter_context_package_built");
        Assert.Equal("project-1-chapter-001", evt.ChapterId);
        Assert.Contains("\"chapterId\":\"project-1-chapter-001\"", evt.DataJson);
        Assert.Contains("\"logicalChapterId\":\"chapter-001\"", evt.DataJson);

        var usage = await db.ProjectKnowledgeUsages.SingleAsync(x => x.Id == "usage-hardfact-1");
        using var chaptersJson = JsonDocument.Parse(usage.UsedByChaptersJson!);
        var usedChapters = chaptersJson.RootElement.EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Equal(new[] { "project-1-chapter-001" }, usedChapters);
        Assert.DoesNotContain(usedChapters, chapterId => string.Equals(chapterId, "chapter-001", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RecordAsync_MarksKnowledgeBindingsUsedByChapterIdempotently()
    {
        await using var db = CreateDb();
        SeedProject(db);
        db.KnowledgeBases.Add(new KnowledgeBase
        {
            Id = "hardfact-1",
            UserId = "user-1",
            SourceProjectId = null,
            EntryType = "HardFact",
            Title = "银蓝邮徽边界",
            Content = "银蓝邮徽只能识别旧邮路，不能攻击。",
            Weight = 10,
            CreatedAt = DateTime.UtcNow
        });
        db.ProjectKnowledgeUsages.Add(new ProjectKnowledgeUsage
        {
            Id = "usage-hardfact-1",
            UserId = "user-1",
            ProjectId = "project-1",
            KnowledgeId = "hardfact-1",
            Status = "referenced",
            UsageCount = 2,
            UsedByChaptersJson = "[\"chapter-001\"]",
            FirstSeenAt = DateTime.UtcNow.AddHours(-1)
        });
        await db.SaveChangesAsync();

        IProductionTruthStore truthStore = new ProductionTruthStore(db);
        IProductionEventWriter eventWriter = new ProductionEventWriter(truthStore);
        IAgentRuntimeEventService runtimeEvents = new AgentRuntimeEventService(db);
        IChapterContextPackageRecorder recorder = new ChapterContextPackageRecorder(truthStore, eventWriter, runtimeEvents, db);
        var package = new ChapterContextPackageSummary
        {
            PackageId = "pkg-first",
            ChapterId = "chapter-002",
            Status = "context_ready",
            KnowledgeBindings =
            {
                new BoundKnowledgeSnapshot
                {
                    KnowledgeId = "hardfact-1",
                    Title = "银蓝邮徽边界",
                    EntryType = "HardFact",
                    Content = "银蓝邮徽只能识别旧邮路，不能攻击。",
                    ConstraintLevel = "HardConstraint",
                    PackagePolicy = "DefaultEveryChapter"
                }
            }
        };

        await recorder.RecordAsync(new RecordChapterContextPackageRequest(
            RuntimeRunId: "run-1",
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserGoal: "构建第二章连续性包",
            RunUpdatedAt: DateTime.UtcNow,
            Package: package));
        package.PackageId = "pkg-second";
        await recorder.RecordAsync(new RecordChapterContextPackageRequest(
            RuntimeRunId: "run-2",
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserGoal: "重建第二章连续性包",
            RunUpdatedAt: DateTime.UtcNow,
            Package: package));

        var usage = await db.ProjectKnowledgeUsages.SingleAsync(x => x.Id == "usage-hardfact-1");
        Assert.Equal("referenced", usage.Status);
        Assert.Equal(3, usage.UsageCount);
        Assert.NotNull(usage.LastUsedAt);
        using var chaptersJson = JsonDocument.Parse(usage.UsedByChaptersJson!);
        var chapters = chaptersJson.RootElement.EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Equal(new[] { "chapter-001", "chapter-002" }, chapters);
    }

    [Fact]
    public async Task RecordAsync_WithSameRunAndPackageIdReusesPackageWithoutDuplicatingSideEffects()
    {
        await using var db = CreateDb();
        SeedProject(db);
        IProductionTruthStore truthStore = new ProductionTruthStore(db);
        IProductionEventWriter eventWriter = new ProductionEventWriter(truthStore);
        IAgentRuntimeEventService runtimeEvents = new AgentRuntimeEventService(db);
        IChapterContextPackageRecorder recorder = new ChapterContextPackageRecorder(truthStore, eventWriter, runtimeEvents, db);
        var package = new ChapterContextPackageSummary
        {
            PackageId = "pkg-idempotent",
            ChapterId = "chapter-002",
            Status = "context_ready",
            HardContinuityFacts = { "第二章必须承接第一章结尾。" }
        };
        var request = new RecordChapterContextPackageRequest(
            RuntimeRunId: "run-idempotent",
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserGoal: "构建第二章连续性包",
            RunUpdatedAt: DateTime.UtcNow,
            Package: package);

        var first = await recorder.RecordAsync(request);
        var second = await recorder.RecordAsync(request);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("pkg-idempotent", second.Id);
        Assert.Single(await db.TianmingPackages.ToListAsync());
        Assert.Single(await db.ProductionEvents
            .Where(e => e.EventType == "chapter_context_package_built" && e.ArtifactId == "pkg-idempotent")
            .ToListAsync());
        Assert.Single(await db.AgentRuntimeEvents
            .Where(e => e.Type == "production_progress" &&
                        e.Stage == NovelAgentProductionStages.PackageBuilt &&
                        e.ArtifactId == "pkg-idempotent")
            .ToListAsync());
    }

    [Fact]
    public async Task RecordAsync_BlocksPackageWhenOpenHardKnowledgeConflictExists()
    {
        await using var db = CreateDb();
        SeedProject(db);
        db.KnowledgeBases.Add(new KnowledgeBase
        {
            Id = "knowledge-blue-flame",
            UserId = "user-1",
            SourceProjectId = null,
            EntryType = "ItemRule",
            Title = "邮徽蓝焰攻击",
            Content = "银蓝邮徽可以释放蓝焰攻击怪物。",
            Weight = 9,
            CreatedAt = DateTime.UtcNow
        });
        db.KnowledgeConflictReports.Add(new KnowledgeConflictReport
        {
            Id = "conflict-1",
            UserId = "user-1",
            ProjectId = "project-1",
            KnowledgeId = "knowledge-blue-flame",
            ConflictingKnowledgeIdsJson = "[\"knowledge-boundary\"]",
            ConflictType = "HardConstraintContradiction",
            Severity = "Hard",
            ImpactScope = "ProjectWide",
            Explanation = "银蓝邮徽不能攻击与蓝焰攻击冲突。",
            RecommendedAction = "询问用户选择保留哪条设定。",
            RequiresUserDecision = true,
            Status = "open",
            DetectionJson = "{}",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        IProductionTruthStore truthStore = new ProductionTruthStore(db);
        IProductionEventWriter eventWriter = new ProductionEventWriter(truthStore);
        IAgentRuntimeEventService runtimeEvents = new AgentRuntimeEventService(db);
        IChapterContextPackageRecorder recorder = new ChapterContextPackageRecorder(truthStore, eventWriter, runtimeEvents, db);
        var package = new ChapterContextPackageSummary
        {
            PackageId = "pkg-conflict",
            ChapterId = "chapter-003",
            Status = "context_ready",
            KnowledgeBindings =
            {
                new BoundKnowledgeSnapshot
                {
                    KnowledgeId = "knowledge-blue-flame",
                    Title = "邮徽蓝焰攻击",
                    EntryType = "ItemRule",
                    Content = "银蓝邮徽可以释放蓝焰攻击怪物。",
                    ConstraintLevel = "HardConstraint",
                    PackagePolicy = "DefaultEveryChapter"
                }
            }
        };

        var ex = await Assert.ThrowsAsync<KnowledgeConflictBlockedException>(() =>
            recorder.RecordAsync(new RecordChapterContextPackageRequest(
                RuntimeRunId: "run-conflict",
                UserId: "user-1",
                SessionId: "session-1",
                ProjectId: "project-1",
                UserGoal: "构建第三章连续性包",
                RunUpdatedAt: DateTime.UtcNow,
                Package: package)));

        Assert.Contains("open hard knowledge conflict", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("conflict-1", ex.FirstConflictId);
        Assert.Contains("conflict-1", ex.ConflictIds);
        Assert.Contains("蓝焰攻击", ex.Explanation);
        Assert.Empty(await db.TianmingPackages.ToListAsync());
        var evt = await db.ProductionEvents.SingleAsync();
        Assert.Equal("knowledge_conflict_blocked", evt.EventType);
        Assert.Equal(NovelAgentProductionStages.PackageBuilt, evt.Stage);
        Assert.Equal("blocked", evt.Status);
        Assert.Contains("conflict-1", evt.DataJson);
        Assert.Contains("\"requiresUserDecision\":true", evt.DataJson);
    }

    [Fact]
    public async Task RecordAsync_ThrowsWhenRequiredScopeIsMissing()
    {
        await using var db = CreateDb();
        SeedProject(db);
        IProductionTruthStore truthStore = new ProductionTruthStore(db);
        IProductionEventWriter eventWriter = new ProductionEventWriter(truthStore);
        IAgentRuntimeEventService runtimeEvents = new AgentRuntimeEventService(db);
        IChapterContextPackageRecorder recorder = new ChapterContextPackageRecorder(truthStore, eventWriter, runtimeEvents);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            recorder.RecordAsync(new RecordChapterContextPackageRequest(
                RuntimeRunId: "run-1",
                UserId: "",
                SessionId: "session-1",
                ProjectId: "project-1",
                UserGoal: "构建第一章连续性包",
                RunUpdatedAt: null,
                Package: new ChapterContextPackageSummary { ChapterId = "chapter-001" })));

        Assert.Contains("Chapter context package requires runtimeRunId, userId and projectId", ex.Message);
        Assert.Empty(await db.TianmingPackages.ToListAsync());
        Assert.Empty(await db.ProductionEvents.ToListAsync());
    }

    [Fact]
    public async Task ExistsAsync_ReturnsWhetherPackageWasPersistedForRunAndProject()
    {
        await using var db = CreateDb();
        SeedProject(db);
        IProductionTruthStore truthStore = new ProductionTruthStore(db);
        IProductionEventWriter eventWriter = new ProductionEventWriter(truthStore);
        IAgentRuntimeEventService runtimeEvents = new AgentRuntimeEventService(db);
        IChapterContextPackageRecorder recorder = new ChapterContextPackageRecorder(truthStore, eventWriter, runtimeEvents);
        var package = new ChapterContextPackageSummary
        {
            PackageId = "pkg-existing",
            ChapterId = "chapter-001",
            Status = "context_ready"
        };
        await recorder.RecordAsync(new RecordChapterContextPackageRequest(
            RuntimeRunId: "run-1",
            UserId: "user-1",
            SessionId: "session-1",
            ProjectId: "project-1",
            UserGoal: "构建第一章连续性包",
            RunUpdatedAt: DateTime.UtcNow,
            Package: package));

        Assert.True(await recorder.ExistsAsync("project-1", "run-1", package.PackageId));
        Assert.False(await recorder.ExistsAsync("project-1", "run-other", package.PackageId));
        Assert.False(await recorder.ExistsAsync("project-1", "run-1", "pkg-missing"));
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static void SeedProject(NovelAgentDbContext db)
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
        db.SaveChanges();
    }
}
