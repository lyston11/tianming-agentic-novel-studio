using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class ProductionWorkflowBridgeTests
{
    [Fact]
    public async Task LoadProjectEventsAsync_MapsEventsWithPackageChapterVersionAndFactEvidence()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        db.TianmingPackages.AddRange(
            new TianmingPackage
            {
                Id = "pkg-old-1",
                UserId = "user-1",
                ProjectId = "project-1",
                ChapterId = "chapter-001",
                RuntimeRunId = "run-old-1",
                PackageKind = "chapter_context_package",
                Status = "stale",
                InputJson = "{\"goal\":\"旧版第一章\"}",
                PromptVersion = "chapter-context-v1",
                KernelVersion = "agentic-tianming-v1",
                CreatedAt = DateTime.UtcNow.AddMinutes(-20),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-19)
            },
            new TianmingPackage
            {
                Id = "pkg-old-2",
                UserId = "user-1",
                ProjectId = "project-1",
                ChapterId = "chapter-001",
                RuntimeRunId = "run-old-2",
                PackageKind = "chapter_context_package",
                Status = "stale",
                InputJson = "{\"goal\":\"旧版第一章修订\"}",
                PromptVersion = "chapter-context-v1",
                KernelVersion = "agentic-tianming-v1",
                CreatedAt = DateTime.UtcNow.AddMinutes(-15),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-14)
            },
            new TianmingPackage
        {
            Id = "pkg-1",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-001",
            RuntimeRunId = "run-1",
            PackageKind = "chapter_context_package",
            Status = "completed",
            InputJson = "{\"goal\":\"写第一章\"}",
            KnowledgeSnapshotJson = """
            {
              "hardContinuityFacts": ["主角姓名：沈砚", "系统状态：银蓝邮徽只能识路"],
              "ragQueries": ["旧邮路 蓝磷"],
              "knowledgeBindingSummary": {
                "bindingCount": 1,
                "shouldEnterGateCount": 1,
                "shouldEnterBlueprintCount": 1,
                "shouldEnterFactSnapshotCount": 1,
                "hardConstraintCount": 1,
                "referenceCount": 0,
                "classifiedCount": 1,
                "pendingClassificationCount": 0,
                "importedCount": 0,
                "referencedCount": 1
              },
              "acceptedCreativeIntents": [
                {
                  "intentId": "intent-1",
                  "normalizedIntent": "第一章结尾必须落在旧站台逃生。",
                  "targetScope": "chapter",
                  "targetChapterId": "chapter-001"
                }
              ],
              "sourceRevisionPlans": [
                {
                  "revisionPlanId": "revision-plan-001",
                  "planType": "chapter_rewrite",
                  "targetScope": "chapter",
                  "targetChapterId": "chapter-001",
                  "targetChapterLogicalId": "chapter-001",
                  "targetChapterDisplayName": "第一章 银蓝邮徽",
                  "status": "ready_for_rebuild",
                  "affectedChapterIdsJson": "[\"chapter-001\",\"chapter-002\"]",
                  "invalidatedPackageIdsJson": "[\"pkg-old-1\",\"pkg-old-2\"]",
                  "riskLevel": "medium",
                  "recommendation": "把第一章改为旧站台逃生。"
                }
              ],
              "rebuiltFromPackageIds": ["pkg-old-1", "pkg-old-2"],
              "knowledgeBindings": [
                {
                  "knowledgeId": "kb-1",
                  "title": "银蓝邮徽能力边界",
                  "entryType": "HardFact",
                  "projectUsageStatus": "referenced",
                  "weight": 10,
                  "role": "HardConstraint",
                  "constraintLevel": "HardConstraint",
                  "packagePolicy": "AlwaysInclude",
                  "classificationId": "classification-kb-1",
                  "classificationRule": "银蓝邮徽只能识路，不能攻击、治疗或升级。",
                  "shouldEnterGate": true,
                  "shouldEnterBlueprint": true,
                  "shouldEnterFactSnapshot": true,
                  "classificationConfidence": 0.94
                }
              ]
            }
            """,
            PromptVersion = "chapter-context-v1",
            KernelVersion = "agentic-tianming-v1",
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-4)
        });
        db.ChapterVersions.Add(new ChapterVersion
        {
            Id = "version-1",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-001",
            ContentDocumentId = "doc-1",
            VersionNumber = 2,
            Title = "第一章 银蓝邮徽",
            WordCount = 3280,
            Status = "committed",
            RuntimeRunId = "run-1",
            PackageId = "pkg-1",
            GateReportJson = """
            {
              "status": "gate_failed",
              "protocolPassed": true,
              "factSnapshotPassed": false,
              "blueprintPassed": true,
              "ragPassed": true,
              "changesDetected": true,
              "issues": ["没有承接第一章结尾地下室出口被堵"],
              "repairHints": ["重写开头，先处理地下室出口被堵。"]
            }
            """,
            AgentReviewJson = """
            {
              "overallResult": "Warning",
              "decision": "revise_before_commit",
              "problems": ["打怪升级未落实，仍偏情绪拉扯。"],
              "suggestions": ["重写战斗段落，让邮徽只负责识路。"],
              "checks": [
                {
                  "key": "project_knowledge_binding_alignment",
                  "name": "项目知识绑定落地",
                  "status": "Warning",
                  "message": "正文未明显响应 1 条项目知识绑定。",
                  "evidence": ["银蓝邮徽能力边界（HardFact/referenced）"]
                }
              ]
            }
            """,
            CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        });
        db.ProjectFactSnapshots.Add(new ProjectFactSnapshot
        {
            Id = "fact-1",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-001",
            ChapterVersionId = "version-1",
            VersionNumber = 3,
            SnapshotJson = """
            {
              "protagonistName": "沈砚",
              "protagonistIdentity": "维修站青年，刚被旧邮路选中",
              "protagonistStatus": "左臂受伤但仍能行动",
              "currentLocation": "维修站地下室出口",
              "systemState": "银蓝邮徽只能识路，不能攻击",
              "equipmentState": "银蓝邮徽发烫预警",
              "keyEvents": ["沈砚打开旧邮路入口"],
              "endingState": "地下室出口被堵，黑雨逼近",
              "nextChapterMustCarry": ["沈砚必须先从堵死的地下室出口脱身"],
              "knowledgeConstraintEvidence": [
                {
                  "knowledgeId": "kb-1",
                  "title": "银蓝邮徽能力边界",
                  "entryType": "HardFact",
                  "subject": "银蓝邮徽",
                  "constraintLevel": "HardConstraint",
                  "packagePolicy": "DefaultEveryChapter",
                  "classificationId": "classification-kb-1",
                  "classificationRule": "银蓝邮徽只能识路，不能攻击、治疗或升级。",
                  "shouldEnterGate": true,
                  "shouldEnterBlueprint": true,
                  "shouldEnterFactSnapshot": true,
                  "status": "satisfied",
                  "gateStatus": "validated",
                  "allowedTerms": ["识路", "照亮旧邮路"],
                  "forbiddenTerms": ["攻击", "升级"],
                  "violations": []
                }
              ]
            }
            """,
            Source = "chapter_commit",
            CreatedAt = DateTime.UtcNow.AddMinutes(-1)
        });
        db.ProductionEvents.Add(new ProductionEvent
        {
            Id = "evt-1",
            RuntimeRunId = "run-1",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-001",
            PackageId = "pkg-1",
            EventType = "chapter_context_package_built",
            Stage = NovelAgentProductionStages.ContextPackage,
            Status = "completed",
            Message = "章节生产包已构建。",
            ArtifactType = "tianming_package",
            ArtifactId = "pkg-1",
            DataJson = "{\"hardFactCount\":2}",
            CreatedAt = DateTime.UtcNow
        });
        db.ProductionEvents.Add(new ProductionEvent
        {
            Id = "evt-creative-executed",
            RuntimeRunId = "run-1",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-001",
            PackageId = "pkg-1",
            EventType = "creative_intents_executed",
            Stage = NovelAgentProductionStages.ChapterCommit,
            Status = "completed",
            Message = "已将 1 条生产包创意标记为已执行。",
            ArtifactType = "creative_intents",
            ArtifactId = "chapter-001",
            DataJson = "{\"executedCount\":1,\"intentIds\":[\"intent-1\"]}",
            CreatedAt = DateTime.UtcNow.AddSeconds(1)
        });
        db.ProductionEvents.Add(new ProductionEvent
        {
            Id = "evt-knowledge-used",
            RuntimeRunId = "run-1",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-001",
            PackageId = "pkg-1",
            EventType = "knowledge_bindings_used",
            Stage = NovelAgentProductionStages.ChapterCommit,
            Status = "completed",
            Message = "本章生产包实际使用 1 条项目知识绑定。",
            ArtifactType = "knowledge_bindings",
            ArtifactId = "chapter-001",
            DataJson = "{\"bindingCount\":1,\"knowledgeIds\":[\"kb-1\"]}",
            CreatedAt = DateTime.UtcNow.AddSeconds(2)
        });
        db.ProductionEvents.Add(new ProductionEvent
        {
            Id = "evt-other-project",
            RuntimeRunId = "run-other",
            UserId = "user-1",
            ProjectId = "project-2",
            ChapterId = "chapter-001",
            PackageId = "pkg-other",
            EventType = "chapter_context_package_built",
            Stage = NovelAgentProductionStages.ContextPackage,
            Status = "completed",
            Message = "不应串到 project-1。",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        IProductionWorkflowBridge bridge = new ProductionWorkflowBridge(db);

        var events = await bridge.LoadProjectEventsAsync("project-1");

        var evt = Assert.Single(events, item => item.Id == "evt-1");
        var executedEvt = Assert.Single(events, item => item.Id == "evt-creative-executed");
        var knowledgeUsedEvt = Assert.Single(events, item => item.Id == "evt-knowledge-used");
        Assert.Equal("evt-1", evt.Id);
        Assert.Equal("run-1", evt.RuntimeRunId);
        Assert.Equal("chapter-001", evt.ChapterId);
        Assert.Equal("pkg-1", evt.PackageId);
        Assert.Equal(NovelAgentProductionStages.ContextPackage, evt.Stage);
        Assert.Equal("tianming_package", evt.ArtifactType);
        Assert.NotNull(evt.Evidence);
        Assert.Equal("chapter_context_package", evt.Evidence!.PackageKind);
        Assert.Equal("completed", evt.Evidence.PackageStatus);
        Assert.Equal("chapter-context-v1", evt.Evidence.PromptVersion);
        Assert.Equal("agentic-tianming-v1", evt.Evidence.KernelVersion);
        Assert.Equal(2, evt.Evidence.KnowledgeFactCount);
        Assert.Equal(1, evt.Evidence.RagQueryCount);
        Assert.NotNull(evt.Evidence.KnowledgeBindingSummary);
        Assert.Equal(1, evt.Evidence.KnowledgeBindingSummary!.BindingCount);
        Assert.Equal(1, evt.Evidence.KnowledgeBindingSummary.ShouldEnterGateCount);
        Assert.Equal(1, evt.Evidence.KnowledgeBindingSummary.ShouldEnterBlueprintCount);
        Assert.Equal(1, evt.Evidence.KnowledgeBindingSummary.ShouldEnterFactSnapshotCount);
        Assert.Equal(1, evt.Evidence.KnowledgeBindingSummary.HardConstraintCount);
        Assert.Equal(1, evt.Evidence.KnowledgeBindingSummary.ClassifiedCount);
        Assert.Equal(0, evt.Evidence.KnowledgeBindingSummary.PendingClassificationCount);
        Assert.Equal(1, evt.Evidence.AcceptedCreativeIntentCount);
        var creativeIntent = Assert.Single(evt.Evidence.CreativeIntents);
        Assert.Equal("intent-1", creativeIntent.IntentId);
        Assert.Equal("第一章结尾必须落在旧站台逃生。", creativeIntent.NormalizedIntent);
        Assert.Equal("chapter", creativeIntent.TargetScope);
        Assert.Equal("chapter-001", creativeIntent.TargetChapterId);
        Assert.Equal("accepted", creativeIntent.Status);
        var sourceRevisionPlan = Assert.Single(evt.Evidence.SourceRevisionPlans);
        Assert.Equal("revision-plan-001", sourceRevisionPlan.RevisionPlanId);
        Assert.Equal("chapter_rewrite", sourceRevisionPlan.PlanType);
        Assert.Equal("chapter", sourceRevisionPlan.TargetScope);
        Assert.Equal("chapter-001", sourceRevisionPlan.TargetChapterId);
        Assert.Equal("chapter-001", sourceRevisionPlan.TargetChapterLogicalId);
        Assert.Equal("第一章 银蓝邮徽", sourceRevisionPlan.TargetChapterDisplayName);
        Assert.Equal("ready_for_rebuild", sourceRevisionPlan.Status);
        Assert.Equal(new[] { "chapter-001", "chapter-002" }, sourceRevisionPlan.AffectedChapterIds);
        Assert.Equal(new[] { "pkg-old-1", "pkg-old-2" }, sourceRevisionPlan.InvalidatedPackageIds);
        Assert.Equal("medium", sourceRevisionPlan.RiskLevel);
        Assert.Equal("把第一章改为旧站台逃生。", sourceRevisionPlan.Recommendation);
        Assert.Equal(new[] { "pkg-old-1", "pkg-old-2" }, evt.Evidence.RebuiltFromPackageIds);
        Assert.Equal(2, evt.Evidence.RebuildLinks.Count);
        var firstRebuildLink = Assert.Single(evt.Evidence.RebuildLinks, link => link.OldPackageId == "pkg-old-1");
        Assert.Equal("stale", firstRebuildLink.OldPackageStatus);
        Assert.Equal("pkg-1", firstRebuildLink.NewPackageId);
        Assert.Equal("completed", firstRebuildLink.NewPackageStatus);
        Assert.Equal("chapter_context_package", firstRebuildLink.NewPackageKind);
        Assert.Equal("chapter-001", firstRebuildLink.ChapterId);
        Assert.Equal("run-1", firstRebuildLink.RuntimeRunId);
        var executedCreativeIntent = Assert.Single(executedEvt.Evidence!.CreativeIntents);
        Assert.Equal("intent-1", executedCreativeIntent.IntentId);
        Assert.Equal("executed", executedCreativeIntent.Status);
        Assert.Equal(3, evt.Evidence.FactSnapshotVersion);
        Assert.Equal("chapter_commit", evt.Evidence.FactSnapshotSource);
        Assert.Equal("fact-1", evt.Evidence.FactSnapshotId);
        Assert.NotNull(evt.Evidence.FactSnapshot);
        Assert.Equal("沈砚", evt.Evidence.FactSnapshot!.ProtagonistName);
        Assert.Equal("维修站青年，刚被旧邮路选中", evt.Evidence.FactSnapshot.ProtagonistIdentity);
        Assert.Equal("左臂受伤但仍能行动", evt.Evidence.FactSnapshot.ProtagonistStatus);
        Assert.Equal("维修站地下室出口", evt.Evidence.FactSnapshot.CurrentLocation);
        Assert.Equal("银蓝邮徽只能识路，不能攻击", evt.Evidence.FactSnapshot.SystemState);
        Assert.Equal("银蓝邮徽发烫预警", evt.Evidence.FactSnapshot.EquipmentState);
        Assert.Contains("沈砚打开旧邮路入口", evt.Evidence.FactSnapshot.KeyEvents);
        Assert.Equal("地下室出口被堵，黑雨逼近", evt.Evidence.FactSnapshot.EndingState);
        Assert.Contains("沈砚必须先从堵死的地下室出口脱身", evt.Evidence.FactSnapshot.NextChapterMustCarry);
        Assert.NotNull(evt.Evidence.Gate);
        Assert.Equal("gate_failed", evt.Evidence.Gate!.Status);
        Assert.True(evt.Evidence.Gate.ProtocolPassed);
        Assert.False(evt.Evidence.Gate.FactSnapshotPassed);
        Assert.True(evt.Evidence.Gate.BlueprintPassed);
        Assert.True(evt.Evidence.Gate.RagPassed);
        Assert.True(evt.Evidence.Gate.ChangesDetected);
        Assert.Contains("没有承接第一章结尾地下室出口被堵", evt.Evidence.Gate.Issues);
        Assert.Contains("重写开头，先处理地下室出口被堵。", evt.Evidence.Gate.RepairHints);
        var constraintEvidence = Assert.Single(evt.Evidence.KnowledgeConstraintEvidence);
        Assert.Equal("kb-1", constraintEvidence.KnowledgeId);
        Assert.Equal("银蓝邮徽能力边界", constraintEvidence.Title);
        Assert.Equal("HardFact", constraintEvidence.EntryType);
        Assert.Equal("银蓝邮徽", constraintEvidence.Subject);
        Assert.Equal("HardConstraint", constraintEvidence.ConstraintLevel);
        Assert.Equal("DefaultEveryChapter", constraintEvidence.PackagePolicy);
        Assert.Equal("classification-kb-1", constraintEvidence.ClassificationId);
        Assert.Equal("银蓝邮徽只能识路，不能攻击、治疗或升级。", constraintEvidence.ClassificationRule);
        Assert.True(constraintEvidence.ShouldEnterGate);
        Assert.True(constraintEvidence.ShouldEnterBlueprint);
        Assert.True(constraintEvidence.ShouldEnterFactSnapshot);
        Assert.Equal("satisfied", constraintEvidence.EvidenceStatus);
        Assert.Equal("validated", constraintEvidence.GateStatus);
        Assert.Equal("chapter-001", constraintEvidence.ChapterId);
        Assert.Equal("fact-1", constraintEvidence.FactSnapshotId);
        Assert.Equal(3, constraintEvidence.FactSnapshotVersion);
        Assert.Contains("识路", constraintEvidence.AllowedTerms);
        Assert.Contains("攻击", constraintEvidence.ForbiddenTerms);
        Assert.Equal(2, evt.Evidence.ChapterVersionNumber);
        Assert.Equal("version-1", evt.Evidence.ChapterVersionId);
        Assert.Equal("committed", evt.Evidence.ChapterVersionStatus);
        Assert.Equal(3280, evt.Evidence.ChapterVersionWordCount);
        var reviewCheck = Assert.Single(evt.Evidence.AgentReviewChecks);
        Assert.Equal("project_knowledge_binding_alignment", reviewCheck.Key);
        Assert.Equal("项目知识绑定落地", reviewCheck.Name);
        Assert.Equal("Warning", reviewCheck.Status);
        Assert.Contains("银蓝邮徽能力边界", reviewCheck.Evidence[0]);
        Assert.NotNull(evt.Evidence.AgentReview);
        Assert.Equal("revise_before_commit", evt.Evidence.AgentReview!.Decision);
        Assert.Equal("Warning", evt.Evidence.AgentReview.OverallResult);
        Assert.Contains("打怪升级未落实，仍偏情绪拉扯。", evt.Evidence.AgentReview.Problems);
        Assert.Contains("重写战斗段落，让邮徽只负责识路。", evt.Evidence.AgentReview.Suggestions);
        var binding = Assert.Single(evt.Evidence.KnowledgeBindings);
        Assert.Equal("kb-1", binding.KnowledgeId);
        Assert.Equal("银蓝邮徽能力边界", binding.Title);
        Assert.Equal("HardFact", binding.EntryType);
        Assert.Equal("referenced", binding.ProjectUsageStatus);
        Assert.Equal(10, binding.Weight);
        Assert.Equal("HardConstraint", binding.Role);
        Assert.Equal("HardConstraint", binding.ConstraintLevel);
        Assert.Equal("AlwaysInclude", binding.PackagePolicy);
        Assert.Equal("classification-kb-1", binding.ClassificationId);
        Assert.Equal("银蓝邮徽只能识路，不能攻击、治疗或升级。", binding.ClassificationRule);
        Assert.True(binding.ShouldEnterGate);
        Assert.True(binding.ShouldEnterBlueprint);
        Assert.True(binding.ShouldEnterFactSnapshot);
        Assert.Equal(0.94, binding.ClassificationConfidence, precision: 2);
        var usedBinding = Assert.Single(knowledgeUsedEvt.Evidence!.KnowledgeBindings);
        Assert.Equal("kb-1", usedBinding.KnowledgeId);
        Assert.Equal("used", usedBinding.ProjectUsageStatus);
    }

    [Fact]
    public async Task LoadProjectEventsAsync_MapsKnowledgeUsedEventBindingsWithoutPackageSnapshot()
    {
        await using var db = CreateDb();
        db.ProductionEvents.Add(new ProductionEvent
        {
            Id = "evt-knowledge-used-only",
            RuntimeRunId = "run-knowledge-only",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "project-1-chapter-001",
            PackageId = "pkg-missing",
            EventType = "knowledge_bindings_used",
            Stage = NovelAgentProductionStages.FactsPersisted,
            Status = "completed",
            Message = "本章生产包实际使用 1 条项目知识绑定。",
            ArtifactType = "knowledge_bindings",
            ArtifactId = "project-1-chapter-001",
            DataJson = """
            {
              "bindingCount": 1,
              "knowledgeIds": ["kb-used-1"],
              "bindings": [
                {
                  "knowledgeId": "kb-used-1",
                  "title": "盐鸦驿站",
                  "entryType": "HardFact",
                  "projectUsageStatus": "referenced",
                  "weight": 90,
                  "role": "HardFact",
                  "constraintLevel": "HardConstraint",
                  "packagePolicy": "DefaultEveryChapter"
                }
              ]
            }
            """,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        IProductionWorkflowBridge bridge = new ProductionWorkflowBridge(db);

        var events = await bridge.LoadProjectEventsAsync("project-1");

        var evt = Assert.Single(events);
        Assert.NotNull(evt.Evidence);
        var binding = Assert.Single(evt.Evidence!.KnowledgeBindings);
        Assert.Equal("kb-used-1", binding.KnowledgeId);
        Assert.Equal("盐鸦驿站", binding.Title);
        Assert.Equal("used", binding.ProjectUsageStatus);
        Assert.Equal("HardConstraint", binding.ConstraintLevel);
        Assert.Equal("DefaultEveryChapter", binding.PackagePolicy);
    }

    [Fact]
    public async Task LoadProjectEventsAsync_NormalizesShortChapterIdsToProjectScopedChapterIds()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        db.Chapters.Add(new Chapter
        {
            Id = "project-1-chapter-002",
            ProjectId = "project-1",
            Title = "第二章 旧邮路",
            ChapterNumber = 2,
            Status = "committed",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.TianmingPackages.Add(new TianmingPackage
        {
            Id = "pkg-2",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-002",
            RuntimeRunId = "run-2",
            PackageKind = "chapter_context_package",
            Status = "completed",
            InputJson = "{}",
            PromptVersion = "chapter-context-v1",
            KernelVersion = "agentic-tianming-v1",
            CreatedAt = DateTime.UtcNow.AddMinutes(-3),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-2)
        });
        db.ChapterVersions.Add(new ChapterVersion
        {
            Id = "version-2",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "project-1-chapter-002",
            ContentDocumentId = "doc-2",
            VersionNumber = 1,
            Title = "第二章 旧邮路",
            WordCount = 3600,
            Status = "committed",
            RuntimeRunId = "run-2",
            PackageId = "pkg-2",
            CreatedAt = DateTime.UtcNow.AddMinutes(-1)
        });
        db.ProjectFactSnapshots.Add(new ProjectFactSnapshot
        {
            Id = "fact-2",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "project-1-chapter-002",
            ChapterVersionId = "version-2",
            VersionNumber = 1,
            SnapshotJson = "{\"ending\":\"沈砚进入旧邮路\"}",
            Source = "chapter_commit",
            CreatedAt = DateTime.UtcNow
        });
        db.ProductionEvents.Add(new ProductionEvent
        {
            Id = "evt-short-chapter",
            RuntimeRunId = "run-2",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-002",
            PackageId = "pkg-2",
            EventType = "chapter_committed",
            Stage = NovelAgentProductionStages.ChapterCommit,
            Status = "completed",
            Message = "章节已提交到书城。",
            ArtifactType = "chapter_version",
            ArtifactId = "version-2",
            DataJson = "{}",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        IProductionWorkflowBridge bridge = new ProductionWorkflowBridge(db);

        var events = await bridge.LoadProjectEventsAsync("project-1");

        var evt = Assert.Single(events, item => item.Id == "evt-short-chapter");
        Assert.Equal("project-1-chapter-002", evt.ChapterId);
        Assert.NotNull(evt.Evidence);
        Assert.Equal("version-2", evt.Evidence!.ChapterVersionId);
        Assert.Equal("fact-2", evt.Evidence.FactSnapshotId);
    }

    [Fact]
    public async Task LoadProjectEventsAsync_UsesIndependentGateReportsAndAgentReviewsBeforeVersionJson()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        db.TianmingPackages.Add(new TianmingPackage
        {
            Id = "pkg-independent",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-001",
            RuntimeRunId = "run-independent",
            PackageKind = "chapter_context_package",
            Status = "completed",
            InputJson = "{}",
            PromptVersion = "chapter-context-v1",
            KernelVersion = "agentic-tianming-v1",
            CreatedAt = DateTime.UtcNow.AddMinutes(-3),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-2)
        });
        db.ChapterVersions.Add(new ChapterVersion
        {
            Id = "version-independent",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-001",
            ContentDocumentId = "doc-1",
            VersionNumber = 1,
            Title = "第一章 银蓝邮徽",
            WordCount = 3200,
            Status = "committed",
            RuntimeRunId = "run-independent",
            PackageId = "pkg-independent",
            GateReportJson = null,
            AgentReviewJson = null,
            CreatedAt = DateTime.UtcNow.AddMinutes(-1)
        });
        db.GenerationGateReports.Add(new GenerationGateReportRecord
        {
            Id = "gate-independent",
            UserId = "user-1",
            ProjectId = "project-1",
            RuntimeRunId = "run-independent",
            ChapterId = "chapter-001",
            PackageId = "pkg-independent",
            ArtifactId = "validated",
            Status = "gate_failed",
            ReportJson = """
            {
              "status": "gate_failed",
              "protocolPassed": true,
              "changesDetected": true,
              "factSnapshotPassed": false,
              "blueprintPassed": true,
              "ragPassed": true,
              "issues": ["独立门禁发现未承接上一章结尾"],
              "repairHints": ["先补上一章结尾承接。"]
            }
            """,
            ProtocolPassed = true,
            ChangesDetected = true,
            FactSnapshotPassed = false,
            BlueprintPassed = true,
            RagPassed = true,
            IssueCount = 1,
            RepairHintCount = 1,
            ValidatedAt = DateTime.UtcNow.AddSeconds(-30),
            CreatedAt = DateTime.UtcNow.AddSeconds(-30)
        });
        db.AgentReviews.Add(new AgentReviewRecord
        {
            Id = "review-independent",
            UserId = "user-1",
            ProjectId = "project-1",
            RuntimeRunId = "run-independent",
            ChapterId = "chapter-001",
            PackageId = "pkg-independent",
            ReviewId = "review-independent-model",
            OverallResult = "Warning",
            ValidationOverallResult = "Pass",
            RequiresRewrite = false,
            QualityScore = 82,
            ContentLength = 3200,
            CheckCount = 1,
            Summary = "独立 AgentReview 认为可继续但战斗反馈偏弱。",
            MeetsAcceptedCreativeIntents = false,
            ContinuityRisk = "medium",
            ChapterPacing = "battle_feedback_weak",
            RecommendedAction = "revise_before_commit",
            ReviewJson = """
            {
              "reviewId": "review-independent-model",
              "overallResult": "Warning",
              "summary": "独立 AgentReview 认为可继续但战斗反馈偏弱。",
              "checks": [
                {
                  "key": "battle_feedback",
                  "name": "战斗反馈",
                  "status": "Warning",
                  "message": "战斗反馈偏弱。",
                  "evidence": ["怪物被击退但收益反馈不足。"]
                }
              ]
            }
            """,
            ReviewedAt = DateTime.UtcNow.AddSeconds(-20),
            CreatedAt = DateTime.UtcNow.AddSeconds(-20)
        });
        db.ProductionEvents.Add(new ProductionEvent
        {
            Id = "evt-independent",
            RuntimeRunId = "run-independent",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-001",
            PackageId = "pkg-independent",
            EventType = "chapter_quality_reviewed",
            Stage = NovelAgentProductionStages.QualityReview,
            Status = "completed",
            Message = "章节质量评审完成。",
            ArtifactType = "chapter_review",
            ArtifactId = "review-independent-model",
            DataJson = "{}",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        IProductionWorkflowBridge bridge = new ProductionWorkflowBridge(db);

        var events = await bridge.LoadProjectEventsAsync("project-1");

        var evt = Assert.Single(events, item => item.Id == "evt-independent");
        Assert.NotNull(evt.Evidence);
        Assert.NotNull(evt.Evidence!.Gate);
        Assert.Equal("gate_failed", evt.Evidence.Gate!.Status);
        Assert.True(evt.Evidence.Gate.ProtocolPassed);
        Assert.False(evt.Evidence.Gate.FactSnapshotPassed);
        Assert.Contains("独立门禁发现未承接上一章结尾", evt.Evidence.Gate.Issues);
        Assert.NotNull(evt.Evidence.AgentReview);
        Assert.Equal("Warning", evt.Evidence.AgentReview!.OverallResult);
        Assert.False(evt.Evidence.AgentReview.MeetsAcceptedCreativeIntents);
        Assert.Equal("medium", evt.Evidence.AgentReview.ContinuityRisk);
        Assert.Equal("battle_feedback_weak", evt.Evidence.AgentReview.ChapterPacing);
        Assert.Equal("revise_before_commit", evt.Evidence.AgentReview.RecommendedAction);
        var check = Assert.Single(evt.Evidence.AgentReviewChecks);
        Assert.Equal("battle_feedback", check.Key);
        Assert.Equal("Warning", check.Status);
        Assert.Contains("怪物被击退但收益反馈不足。", check.Evidence);
    }

    [Fact]
    public async Task LoadProjectEventsAsync_MapsRollbackEvidenceFromEventDataAndArtifactVersion()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        db.TianmingPackages.AddRange(
            new TianmingPackage
            {
                Id = "pkg-v1",
                UserId = "user-1",
                ProjectId = "project-1",
                ChapterId = "chapter-001",
                RuntimeRunId = "run-v1",
                PackageKind = "chapter_generation",
                Status = "completed",
                InputJson = "{}",
                PromptVersion = "chapter-v1",
                KernelVersion = "tianming-kernel-v1",
                CreatedAt = DateTime.UtcNow.AddMinutes(-10),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-10)
            },
            new TianmingPackage
            {
                Id = "pkg-v2",
                UserId = "user-1",
                ProjectId = "project-1",
                ChapterId = "chapter-001",
                RuntimeRunId = "run-v2",
                PackageKind = "chapter_generation",
                Status = "stale",
                InputJson = "{}",
                PromptVersion = "chapter-v1",
                KernelVersion = "tianming-kernel-v1",
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                UpdatedAt = DateTime.UtcNow
            });
        db.ChapterVersions.AddRange(
            new ChapterVersion
            {
                Id = "version-v1",
                UserId = "user-1",
                ProjectId = "project-1",
                ChapterId = "chapter-001",
                ContentDocumentId = "doc-1",
                VersionNumber = 1,
                Title = "第一章 银蓝邮徽",
                WordCount = 3200,
                Status = "committed",
                RuntimeRunId = "run-v1",
                PackageId = "pkg-v1",
                CreatedAt = DateTime.UtcNow.AddMinutes(-9)
            },
            new ChapterVersion
            {
                Id = "version-v2",
                UserId = "user-1",
                ProjectId = "project-1",
                ChapterId = "chapter-001",
                ContentDocumentId = "doc-2",
                VersionNumber = 2,
                Title = "第一章 银蓝邮徽重写版",
                WordCount = 3600,
                Status = "committed",
                RuntimeRunId = "run-v2",
                PackageId = "pkg-v2",
                CreatedAt = DateTime.UtcNow.AddMinutes(-4)
            });
        db.ProductionEvents.Add(new ProductionEvent
        {
            Id = "evt-rollback",
            RuntimeRunId = "run-rollback-1",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-001",
            PackageId = "pkg-v1",
            EventType = "chapter_version_rolled_back",
            Stage = "version_rollback",
            Status = "completed",
            Message = "章节已回滚到 v1，后续生产包已过期。",
            ArtifactType = "chapter_version",
            ArtifactId = "version-v1",
            DataJson = """
            {
              "reason": "用户确认回滚到第一版。",
              "targetVersionId": "version-v1",
              "targetVersionNumber": 1,
              "currentDocumentId": "doc-1",
              "invalidatedPackageIds": ["pkg-v2", "pkg-chapter-002-v1"]
            }
            """,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        IProductionWorkflowBridge bridge = new ProductionWorkflowBridge(db);

        var events = await bridge.LoadProjectEventsAsync("project-1");

        var evt = Assert.Single(events, item => item.Id == "evt-rollback");
        Assert.NotNull(evt.Evidence);
        Assert.Equal(1, evt.Evidence!.ChapterVersionNumber);
        Assert.Equal("version-v1", evt.Evidence.ChapterVersionId);
        Assert.Equal("pkg-v1", evt.PackageId);
        Assert.NotNull(evt.Evidence.Rollback);
        Assert.Equal("version-v1", evt.Evidence.Rollback!.TargetVersionId);
        Assert.Equal(1, evt.Evidence.Rollback.TargetVersionNumber);
        Assert.Equal("doc-1", evt.Evidence.Rollback.CurrentDocumentId);
        Assert.Equal("用户确认回滚到第一版。", evt.Evidence.Rollback.Reason);
        Assert.Equal(new[] { "pkg-v2", "pkg-chapter-002-v1" }, evt.Evidence.Rollback.InvalidatedPackageIds);
    }

    [Fact]
    public async Task LoadProjectEventsAsync_MapsFailureContractFromProductionEventData()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        db.ProductionEvents.Add(new ProductionEvent
        {
            Id = "evt-index-failed",
            RuntimeRunId = "run-1",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-001",
            PackageId = "pkg-1",
            EventType = "outbox_failed",
            Stage = "index_outbox",
            Status = "retryable_failed",
            Message = "后台 outbox 处理失败，已排队重试。",
            ArtifactType = "outbox_event",
            ArtifactId = "outbox-1",
            DataJson = """
            {
              "error": "Qdrant timeout",
              "recommendedAction": "RetryOutboxEvent(outbox-1)",
              "requiresUserDecision": false,
              "artifactIds": ["outbox-1", "chapter-001"]
            }
            """,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        IProductionWorkflowBridge bridge = new ProductionWorkflowBridge(db);

        var events = await bridge.LoadProjectEventsAsync("project-1");

        var evt = Assert.Single(events, item => item.Id == "evt-index-failed");
        Assert.NotNull(evt.Failure);
        Assert.Equal("INDEX_FAILED", evt.Failure!.Code);
        Assert.Equal("index_outbox", evt.Failure.Stage);
        Assert.True(evt.Failure.Recoverable);
        Assert.Equal("Qdrant timeout", evt.Failure.Message);
        Assert.Equal("RetryOutboxEvent(outbox-1)", evt.Failure.RecommendedAction);
        Assert.False(evt.Failure.RequiresUserDecision);
        Assert.Contains("outbox-1", evt.Failure.ArtifactIds);
        Assert.Contains("chapter-001", evt.Failure.ArtifactIds);
    }

    [Fact]
    public async Task LoadProjectEventsAsync_MapsChapterFactOutboxFailureContract()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        db.ProductionEvents.Add(new ProductionEvent
        {
            Id = "evt-facts-failed",
            RuntimeRunId = "run-1",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-001",
            PackageId = "pkg-1",
            EventType = "outbox_failed",
            Stage = "index_outbox",
            Status = "retryable_failed",
            Message = "后台 outbox 处理失败，已排队重试。 extract_chapter_continuity_facts/chapter",
            ArtifactType = "outbox_event",
            ArtifactId = "outbox-facts-1",
            DataJson = """
            {
              "eventType": "extract_chapter_continuity_facts",
              "aggregateType": "chapter",
              "aggregateId": "chapter-001",
              "error": "Chapter version not found"
            }
            """,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        IProductionWorkflowBridge bridge = new ProductionWorkflowBridge(db);

        var events = await bridge.LoadProjectEventsAsync("project-1");

        var evt = Assert.Single(events, item => item.Id == "evt-facts-failed");
        Assert.NotNull(evt.Failure);
        Assert.Equal("CHAPTER_FACT_EXTRACTION_FAILED", evt.Failure!.Code);
        Assert.Equal("post_commit_facts", evt.Failure.Stage);
        Assert.True(evt.Failure.Recoverable);
        Assert.Equal("Chapter version not found", evt.Failure.Message);
        Assert.Contains("后台事实沉淀重试", evt.Failure.RecommendedAction);
        Assert.False(evt.Failure.RequiresUserDecision);
        Assert.Contains("outbox-facts-1", evt.Failure.ArtifactIds);
        Assert.Contains("chapter-001", evt.Failure.ArtifactIds);
    }

    [Fact]
    public async Task LoadProjectEventsAsync_MapsOutboxEvidenceForWorkflowChain()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        db.OutboxEvents.Add(new OutboxEvent
        {
            Id = "outbox-1",
            UserId = "user-1",
            ProjectId = "project-1",
            RuntimeRunId = "run-1",
            EventType = "index_chapter_content",
            AggregateType = "chapter_version",
            AggregateId = "version-1",
            PayloadJson = "{}",
            Status = "completed",
            Attempts = 1,
            CompletedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow.AddMinutes(-2),
            UpdatedAt = DateTime.UtcNow
        });
        db.ProductionEvents.Add(new ProductionEvent
        {
            Id = "evt-outbox-completed",
            RuntimeRunId = "run-1",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-001",
            PackageId = "pkg-1",
            EventType = "outbox_completed",
            Stage = "index_outbox",
            Status = "completed",
            Message = "后台 outbox 处理完成。 index_chapter_content/chapter_version",
            ArtifactType = "outbox_event",
            ArtifactId = "outbox-1",
            DataJson = "{\"eventType\":\"index_chapter_content\",\"aggregateType\":\"chapter_version\"}",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        IProductionWorkflowBridge bridge = new ProductionWorkflowBridge(db);

        var events = await bridge.LoadProjectEventsAsync("project-1");

        var evt = Assert.Single(events, item => item.Id == "evt-outbox-completed");
        Assert.NotNull(evt.Evidence);
        Assert.NotNull(evt.Evidence!.Outbox);
        Assert.Equal("outbox-1", evt.Evidence.Outbox!.OutboxEventId);
        Assert.Equal("index_chapter_content", evt.Evidence.Outbox.EventType);
        Assert.Equal("chapter_version", evt.Evidence.Outbox.AggregateType);
        Assert.Equal("version-1", evt.Evidence.Outbox.AggregateId);
        Assert.Equal("completed", evt.Evidence.Outbox.Status);
        Assert.Equal(1, evt.Evidence.Outbox.Attempts);
    }

    [Fact]
    public async Task LoadProjectEventsAsync_MapsQueuedOutboxEvidenceBeforeProcessing()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        db.OutboxEvents.Add(new OutboxEvent
        {
            Id = "outbox-pending-1",
            UserId = "user-1",
            ProjectId = "project-1",
            RuntimeRunId = "run-1",
            EventType = "finalize_chapter_commit_metadata",
            AggregateType = "chapter",
            AggregateId = "chapter-001",
            PayloadJson = "{}",
            Status = "pending",
            Attempts = 0,
            CreatedAt = DateTime.UtcNow.AddMinutes(-2),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-2)
        });
        db.ProductionEvents.Add(new ProductionEvent
        {
            Id = "evt-outbox-queued",
            RuntimeRunId = "run-1",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-001",
            PackageId = string.Empty,
            EventType = "outbox_queued",
            Stage = NovelAgentProductionStages.FactsPersisted,
            Status = "pending",
            Message = "后台 outbox 已排队：finalize_chapter_commit_metadata/chapter。",
            ArtifactType = "outbox_event",
            ArtifactId = "outbox-pending-1",
            DataJson = "{\"outboxEventId\":\"outbox-pending-1\",\"eventType\":\"finalize_chapter_commit_metadata\",\"aggregateType\":\"chapter\",\"status\":\"pending\"}",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        IProductionWorkflowBridge bridge = new ProductionWorkflowBridge(db);

        var events = await bridge.LoadProjectEventsAsync("project-1");

        var evt = Assert.Single(events, item => item.Id == "evt-outbox-queued");
        Assert.NotNull(evt.Evidence);
        Assert.NotNull(evt.Evidence!.Outbox);
        Assert.Equal("outbox-pending-1", evt.Evidence.Outbox!.OutboxEventId);
        Assert.Equal("finalize_chapter_commit_metadata", evt.Evidence.Outbox.EventType);
        Assert.Equal("chapter", evt.Evidence.Outbox.AggregateType);
        Assert.Equal("chapter-001", evt.Evidence.Outbox.AggregateId);
        Assert.Equal("pending", evt.Evidence.Outbox.Status);
        Assert.Equal(0, evt.Evidence.Outbox.Attempts);
    }

    [Fact]
    public async Task LoadProjectEventsAsync_MapsMemoryReadAndPromotionEvidenceForRun()
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        db.ProductionEvents.Add(new ProductionEvent
        {
            Id = "evt-memory-audited",
            RuntimeRunId = "run-memory-1",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-001",
            PackageId = "pkg-memory-1",
            EventType = "chapter_context_package_built",
            Stage = NovelAgentProductionStages.ContextPackage,
            Status = "completed",
            Message = "生产包已读取记忆并提升创作承诺。",
            ArtifactType = "tianming_package",
            ArtifactId = "pkg-memory-1",
            CreatedAt = DateTime.UtcNow
        });
        db.AgentMemoryReads.Add(new AgentMemoryRead
        {
            Id = "memory-read-1",
            UserId = "user-1",
            ProjectId = "project-1",
            SessionId = "session-1",
            RunId = "run-memory-1",
            MemoryScope = "author",
            MemoryKeysJson = "[\"display_name\",\"style_likes\"]",
            SourceType = "memory_repository",
            Consumer = "AgentObservationBuilder",
            CreatedAt = DateTime.UtcNow.AddSeconds(1)
        });
        db.AgentMemoryPromotions.Add(new AgentMemoryPromotion
        {
            Id = "memory-promotion-1",
            UserId = "user-1",
            ProjectId = "project-1",
            SessionId = "session-1",
            RunId = "run-memory-1",
            SourceScope = "session",
            TargetScope = "project",
            SourceMemoryKey = "user_preferences",
            TargetMemoryKey = "project.constraints",
            PromotionReason = "用户明确要求后续章节保持打怪升级节奏。",
            PayloadJson = "{\"constraint\":\"后续章节保持打怪升级节奏\"}",
            CreatedAt = DateTime.UtcNow.AddSeconds(2)
        });
        await db.SaveChangesAsync();
        IProductionWorkflowBridge bridge = new ProductionWorkflowBridge(db);

        var events = await bridge.LoadProjectEventsAsync("project-1");

        var evt = Assert.Single(events, item => item.Id == "evt-memory-audited");
        Assert.NotNull(evt.Evidence);
        var read = Assert.Single(evt.Evidence!.MemoryReads);
        Assert.Equal("memory-read-1", read.Id);
        Assert.Equal("author", read.MemoryScope);
        Assert.Equal(new[] { "display_name", "style_likes" }, read.MemoryKeys);
        Assert.Equal("AgentObservationBuilder", read.Consumer);
        var promotion = Assert.Single(evt.Evidence.MemoryPromotions);
        Assert.Equal("memory-promotion-1", promotion.Id);
        Assert.Equal("session", promotion.SourceScope);
        Assert.Equal("project", promotion.TargetScope);
        Assert.Equal("project.constraints", promotion.TargetMemoryKey);
        Assert.Equal("用户明确要求后续章节保持打怪升级节奏。", promotion.PromotionReason);
    }

    [Theory]
    [InlineData("evt-gate-failed", "chapter_gate_validated", NovelAgentProductionStages.GateValidation, "failed", "GATE_FAILED", false)]
    [InlineData("evt-review-blocked", "chapter_quality_reviewed", NovelAgentProductionStages.QualityReview, "blocked", "AGENT_REVIEW_FAILED", true)]
    public async Task LoadProjectEventsAsync_InfersKernelFailureContractForGateAndAgentReview(
        string eventId,
        string eventType,
        string stage,
        string status,
        string expectedCode,
        bool expectedRequiresUserDecision)
    {
        await using var db = CreateDb();
        SeedProjectChapterAndDocuments(db);
        db.ProductionEvents.Add(new ProductionEvent
        {
            Id = eventId,
            RuntimeRunId = "run-1",
            UserId = "user-1",
            ProjectId = "project-1",
            ChapterId = "chapter-001",
            PackageId = "pkg-1",
            EventType = eventType,
            Stage = stage,
            Status = status,
            Message = "生产内核阶段未通过。",
            ArtifactType = stage,
            ArtifactId = "artifact-1",
            DataJson = "{\"reason\":\"没有满足用户创意要求\"}",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        IProductionWorkflowBridge bridge = new ProductionWorkflowBridge(db);

        var events = await bridge.LoadProjectEventsAsync("project-1");

        var evt = Assert.Single(events, item => item.Id == eventId);
        Assert.NotNull(evt.Failure);
        Assert.Equal(expectedCode, evt.Failure!.Code);
        Assert.Equal(stage, evt.Failure.Stage);
        Assert.False(evt.Failure.Recoverable);
        Assert.Equal("没有满足用户创意要求", evt.Failure.Message);
        Assert.Equal(expectedRequiresUserDecision, evt.Failure.RequiresUserDecision);
        Assert.Contains("artifact-1", evt.Failure.ArtifactIds);
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
            Id = "chapter-001",
            ProjectId = "project-1",
            Title = "第一章 银蓝邮徽",
            ChapterNumber = 1,
            Status = "draft",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.ContentDocuments.Add(new ContentDocument
        {
            Id = "doc-1",
            UserId = "user-1",
            ProjectId = "project-1",
            SourceType = "chapter",
            SourceId = "chapter-001",
            DocumentRole = "final",
            Title = "第一章 银蓝邮徽",
            ContentHash = "hash-1",
            Version = 1
        });
    }
}
