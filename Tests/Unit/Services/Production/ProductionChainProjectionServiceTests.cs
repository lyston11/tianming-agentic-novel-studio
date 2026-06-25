using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class ProductionChainProjectionServiceTests
{
    [Fact]
    public void BuildWorkflowChainsCore_AggregatesGateReviewChangesAndFactEvidenceOnChain()
    {
        var gateEvidence = EmptyEvidence() with
        {
            Gate = new WorkflowGateEvidence(
                "validated",
                ProtocolPassed: true,
                FactSnapshotPassed: true,
                BlueprintPassed: true,
                RagPassed: true,
                ChangesDetected: true,
                Issues: Array.Empty<string>(),
                RepairHints: Array.Empty<string>())
        };
        var reviewEvidence = EmptyEvidence() with
        {
            AgentReview = new WorkflowAgentReviewSummaryEvidence(
                "commit",
                "Pass",
                Array.Empty<string>(),
                new[] { "可以提交书城" },
                MeetsAcceptedCreativeIntents: true,
                "low",
                "紧凑",
                "commit")
        };
        var factEvidence = EmptyEvidence() with
        {
            FactSnapshot = new WorkflowFactSnapshotEvidence(
                "沈砚",
                "邮路幸存者",
                "右手被邮徽灼伤",
                "旧邮路入口",
                "银蓝邮徽待激活",
                "扳手仍在",
                new[] { "完成地下室入口攻防" },
                "沈砚冲进旧邮路入口",
                new[] { "下一章必须承接邮徽代价" })
        };
        var events = new[]
        {
            Event(
                id: "event-changes",
                runId: "runtime-run-1",
                packageId: "pkg-chapter-002",
                eventType: "chapter_changes_recorded",
                stage: "ChangesExtracted",
                message: "CHANGES 已解析。",
                artifactType: "ChapterChange",
                artifactId: "changes-chapter-002",
                dataJson: "{\"changes\":[\"入口攻防\"]}",
                createdAt: "2026-06-24T03:00:00Z"),
            Event(
                id: "event-gate",
                runId: "runtime-run-1",
                packageId: "pkg-chapter-002",
                eventType: "chapter_gate_validated",
                stage: "GateValidated",
                message: "门禁通过。",
                artifactType: "GateReport",
                artifactId: "gate-chapter-002",
                dataJson: "{}",
                createdAt: "2026-06-24T03:01:00Z",
                evidence: gateEvidence),
            Event(
                id: "event-review",
                runId: "runtime-run-1",
                packageId: "pkg-chapter-002",
                eventType: "chapter_quality_reviewed",
                stage: "ReviewCompleted",
                message: "总编验收通过。",
                artifactType: "AgentReview",
                artifactId: "review-chapter-002",
                dataJson: "{}",
                createdAt: "2026-06-24T03:02:00Z",
                evidence: reviewEvidence),
            Event(
                id: "event-fact",
                runId: "runtime-run-1",
                packageId: "pkg-chapter-002",
                eventType: "chapter_continuity_facts_extracted",
                stage: "FactsPersisted",
                message: "事实快照已沉淀。",
                artifactType: "ProjectFactSnapshot",
                artifactId: "fact-chapter-002",
                dataJson: "{}",
                createdAt: "2026-06-24T03:03:00Z",
                evidence: factEvidence)
        };

        var chain = Assert.Single(ProductionChainProjectionService.BuildWorkflowChainsCore(events));
        var evidence = chain.GetType().GetProperty("Evidence")?.GetValue(chain);

        Assert.NotNull(evidence);
        Assert.Equal("validated", evidence!.GetType().GetProperty("Gate")?.GetValue(evidence)?.GetType().GetProperty("Status")?.GetValue(evidence.GetType().GetProperty("Gate")?.GetValue(evidence)));
        Assert.Equal("Pass", evidence.GetType().GetProperty("AgentReview")?.GetValue(evidence)?.GetType().GetProperty("OverallResult")?.GetValue(evidence.GetType().GetProperty("AgentReview")?.GetValue(evidence)));
        Assert.Equal("沈砚冲进旧邮路入口", evidence.GetType().GetProperty("FactSnapshot")?.GetValue(evidence)?.GetType().GetProperty("EndingState")?.GetValue(evidence.GetType().GetProperty("FactSnapshot")?.GetValue(evidence)));
        Assert.Equal(1, evidence.GetType().GetProperty("ChapterChangeCount")?.GetValue(evidence));
    }

    [Fact]
    public void BuildWorkflowChainsCore_CarriesReadableChapterIdentityFromRevisionPlanEvidence()
    {
        var revisionPlan = new WorkflowRevisionPlanEvidence(
            "revision-plan-1",
            "chapter_rewrite",
            "chapter",
            "project-1-chapter-002",
            "chapter-002",
            "第二章 邮路围城",
            "ready_for_rebuild",
            new[] { "project-1-chapter-002" },
            new[] { "pkg-chapter-002-v1" },
            "medium",
            "第二章改为怪物围攻，并让邮徽成为逃生关键。");
        var evidence = EmptyEvidence() with
        {
            SourceRevisionPlans = new[] { revisionPlan }
        };
        var events = new[]
        {
            Event(
                id: "event-plan",
                runId: "runtime-run-1",
                packageId: "pkg-chapter-002-v1",
                eventType: "revision_plan_created",
                stage: "revision_plan",
                message: "第二章修订计划已创建。",
                artifactType: "RevisionPlan",
                artifactId: "revision-plan-1",
                dataJson: """
                {
                  "revisionPlanId": "revision-plan-1",
                  "invalidatedPackageIds": ["pkg-chapter-002-v1"]
                }
                """,
                createdAt: "2026-06-24T03:00:00Z",
                evidence: evidence),
            Event(
                id: "event-commit",
                runId: "runtime-run-2",
                packageId: "pkg-chapter-002-v2",
                eventType: "chapter_committed",
                stage: "ChapterCommitted",
                message: "第二章 v2 已提交书城。",
                artifactType: "chapter_version",
                artifactId: "version-chapter-002-v2",
                dataJson: """
                {
                  "sourceRevisionPlanIds": ["revision-plan-1"],
                  "versionNumber": 2,
                  "factSnapshotId": "fact-chapter-002-v2",
                  "factSnapshotVersion": 4
                }
                """,
                createdAt: "2026-06-24T03:02:00Z")
        };

        var chain = Assert.Single(ProductionChainProjectionService.BuildWorkflowChainsCore(events));

        Assert.Equal("chapter-002", chain.GetType().GetProperty("ChapterLogicalId")?.GetValue(chain));
        Assert.Equal("第二章 邮路围城", chain.GetType().GetProperty("ChapterDisplayName")?.GetValue(chain));
    }

    [Fact]
    public void BuildWorkflowChainsCore_KeepsRevisionPlanChainIdStableAcrossRuntimeRuns()
    {
        var events = new[]
        {
            Event(
                id: "event-plan",
                runId: "runtime-run-1",
                packageId: "pkg-chapter-002-v1",
                eventType: "revision_plan_created",
                stage: "revision_plan",
                message: "第二章修订计划已创建。",
                artifactType: "RevisionPlan",
                artifactId: "revision-plan-1",
                dataJson: """
                {
                  "revisionPlanId": "revision-plan-1",
                  "invalidatedPackageIds": ["pkg-chapter-002-v1"]
                }
                """,
                createdAt: "2026-06-24T03:00:00Z"),
            Event(
                id: "event-package",
                runId: "runtime-run-2",
                packageId: "pkg-chapter-002-v2",
                eventType: "build_package",
                stage: "PackageBuilt",
                message: "第二章生产包已按修订计划重建。",
                artifactType: "TianmingPackage",
                artifactId: "pkg-chapter-002-v2",
                dataJson: """
                {
                  "sourceRevisionPlanIds": ["revision-plan-1"],
                  "rebuiltFromPackageIds": ["pkg-chapter-002-v1"]
                }
                """,
                createdAt: "2026-06-24T03:01:00Z"),
            Event(
                id: "event-commit",
                runId: "runtime-run-2",
                packageId: "pkg-chapter-002-v2",
                eventType: "chapter_committed",
                stage: "ChapterCommitted",
                message: "第二章 v2 已提交书城。",
                artifactType: "chapter_version",
                artifactId: "version-chapter-002-v2",
                dataJson: """
                {
                  "sourceRevisionPlanIds": ["revision-plan-1"],
                  "versionNumber": 2,
                  "factSnapshotId": "fact-chapter-002-v2",
                  "factSnapshotVersion": 4
                }
                """,
                createdAt: "2026-06-24T03:02:00Z")
        };

        var chain = Assert.Single(ProductionChainProjectionService.BuildWorkflowChainsCore(events));

        Assert.Equal("revision_plan:revision-plan-1:project-1-chapter-002", chain.Id);
        Assert.Equal("project-1-chapter-002", chain.ChapterId);
        Assert.Equal("runtime-run-2", chain.RuntimeRunId);
        Assert.Equal("pkg-chapter-002-v2", chain.PackageId);
        Assert.Contains("revision-plan-1", chain.RevisionPlanIds);
        Assert.Contains(chain.RebuildLinks, link =>
            link.OldPackageId == "pkg-chapter-002-v1" &&
            link.NewPackageId == "pkg-chapter-002-v2" &&
            link.ChapterId == "project-1-chapter-002" &&
            link.RuntimeRunId == "runtime-run-2");
        Assert.Contains(chain.Steps, step => step.Key == "revision_plan" && step.EventId == "event-plan");
        Assert.Contains(chain.Steps, step => step.Key == "context" && step.EventId == "event-package");
        Assert.Contains(chain.Steps, step => step.Key == "commit" && step.EventId == "event-commit");
    }

    [Fact]
    public void BuildWorkflowChainsCore_MergesRevisionPlanChainWhenPlanUsesLogicalChapterAndCommitUsesCanonicalChapter()
    {
        var events = new[]
        {
            Event(
                id: "event-plan",
                runId: "runtime-run-plan",
                packageId: "pkg-chapter-002-v1",
                eventType: "revision_plan_created",
                stage: "revision_plan",
                message: "第二章修订计划已创建。",
                artifactType: "RevisionPlan",
                artifactId: "revision-plan-logic-canonical",
                dataJson: """
                {
                  "revisionPlanId": "revision-plan-logic-canonical",
                  "targetChapterId": "chapter-002",
                  "targetChapterLogicalId": "chapter-002",
                  "invalidatedPackageIds": ["pkg-chapter-002-v1"]
                }
                """,
                createdAt: "2026-06-24T03:00:00Z",
                chapterId: "chapter-002"),
            Event(
                id: "event-package",
                runId: "runtime-run-produce",
                packageId: "pkg-chapter-002-v2",
                eventType: "build_package",
                stage: "PackageBuilt",
                message: "第二章生产包已按修订计划重建。",
                artifactType: "TianmingPackage",
                artifactId: "pkg-chapter-002-v2",
                dataJson: """
                {
                  "sourceRevisionPlanIds": ["revision-plan-logic-canonical"],
                  "rebuiltFromPackageIds": ["pkg-chapter-002-v1"]
                }
                """,
                createdAt: "2026-06-24T03:01:00Z",
                chapterId: "project-1-chapter-002"),
            Event(
                id: "event-commit",
                runId: "runtime-run-produce",
                packageId: "pkg-chapter-002-v2",
                eventType: "chapter_committed",
                stage: "ChapterCommitted",
                message: "第二章 v2 已提交书城。",
                artifactType: "chapter_version",
                artifactId: "version-chapter-002-v2",
                dataJson: """
                {
                  "sourceRevisionPlanIds": ["revision-plan-logic-canonical"],
                  "versionNumber": 2
                }
                """,
                createdAt: "2026-06-24T03:02:00Z",
                chapterId: "project-1-chapter-002")
        };

        var chain = Assert.Single(ProductionChainProjectionService.BuildWorkflowChainsCore(events));

        Assert.Equal("revision_plan:revision-plan-logic-canonical:project-1-chapter-002", chain.Id);
        Assert.Equal("project-1-chapter-002", chain.ChapterId);
        Assert.Equal("chapter-002", chain.ChapterLogicalId);
        Assert.Contains(chain.Steps, step => step.Key == "revision_plan" && step.EventId == "event-plan");
        Assert.Contains(chain.Steps, step => step.Key == "context" && step.EventId == "event-package");
        Assert.Contains(chain.Steps, step => step.Key == "commit" && step.EventId == "event-commit");
    }

    [Fact]
    public void BuildWorkflowChainsCore_AttachesChapterlessRevisionPlanEventThroughRebuiltPackage()
    {
        var events = new[]
        {
            Event(
                id: "event-plan",
                runId: "runtime-run-1",
                packageId: "pkg-chapter-002-v1",
                eventType: "revision_plan_created",
                stage: "revision_plan",
                message: "第二章修订计划已创建。",
                artifactType: "RevisionPlan",
                artifactId: "revision-plan-1",
                dataJson: """
                {
                  "revisionPlanId": "revision-plan-1",
                  "invalidatedPackageIds": ["pkg-chapter-002-v1"]
                }
                """,
                createdAt: "2026-06-24T03:00:00Z",
                chapterId: ""),
            Event(
                id: "event-package",
                runId: "runtime-run-2",
                packageId: "pkg-chapter-002-v2",
                eventType: "build_package",
                stage: "PackageBuilt",
                message: "第二章生产包已按修订计划重建。",
                artifactType: "TianmingPackage",
                artifactId: "pkg-chapter-002-v2",
                dataJson: """
                {
                  "rebuiltFromPackageIds": ["pkg-chapter-002-v1"]
                }
                """,
                createdAt: "2026-06-24T03:01:00Z"),
            Event(
                id: "event-commit",
                runId: "runtime-run-2",
                packageId: "pkg-chapter-002-v2",
                eventType: "chapter_committed",
                stage: "ChapterCommitted",
                message: "第二章 v2 已提交书城。",
                artifactType: "chapter_version",
                artifactId: "version-chapter-002-v2",
                dataJson: """
                {
                  "versionNumber": 2,
                  "factSnapshotId": "fact-chapter-002-v2",
                  "factSnapshotVersion": 4
                }
                """,
                createdAt: "2026-06-24T03:02:00Z")
        };

        var chain = Assert.Single(ProductionChainProjectionService.BuildWorkflowChainsCore(events));

        Assert.Equal("revision_plan:revision-plan-1:project-1-chapter-002", chain.Id);
        Assert.Contains("revision-plan-1", chain.RevisionPlanIds);
        Assert.Contains(chain.Steps, step => step.Key == "revision_plan" && step.EventId == "event-plan");
        Assert.Contains(chain.Steps, step => step.Key == "context" && step.EventId == "event-package");
        Assert.Contains(chain.Steps, step => step.Key == "commit" && step.EventId == "event-commit");
    }

    [Fact]
    public void BuildWorkflowChainsCore_MergesRevisionPlanEvidenceOnlyTargetWithLaterChapterRun()
    {
        var revisionPlan = new WorkflowRevisionPlanEvidence(
            "revision-plan-evidence-only",
            "chapter_rewrite",
            "chapter",
            "",
            "chapter-002",
            "第二章 邮路围城",
            "ready_for_rebuild",
            Array.Empty<string>(),
            Array.Empty<string>(),
            "medium",
            "第二章按用户新创意重写。");
        var evidence = EmptyEvidence() with
        {
            SourceRevisionPlans = new[] { revisionPlan }
        };
        var events = new[]
        {
            Event(
                id: "event-plan",
                runId: "runtime-run-plan",
                packageId: "",
                eventType: "revision_plan_created",
                stage: "revision_plan",
                message: "第二章修订计划已创建。",
                artifactType: "RevisionPlan",
                artifactId: "revision-plan-evidence-only",
                dataJson: "{}",
                createdAt: "2026-06-24T03:00:00Z",
                chapterId: "",
                evidence: evidence),
            Event(
                id: "event-package",
                runId: "runtime-run-produce",
                packageId: "pkg-chapter-002-v2",
                eventType: "build_package",
                stage: "PackageBuilt",
                message: "第二章生产包已重建。",
                artifactType: "TianmingPackage",
                artifactId: "pkg-chapter-002-v2",
                dataJson: "{}",
                createdAt: "2026-06-24T03:01:00Z",
                chapterId: "project-1-chapter-002"),
            Event(
                id: "event-commit",
                runId: "runtime-run-produce",
                packageId: "pkg-chapter-002-v2",
                eventType: "chapter_committed",
                stage: "ChapterCommitted",
                message: "第二章 v2 已提交书城。",
                artifactType: "chapter_version",
                artifactId: "version-chapter-002-v2",
                dataJson: "{\"versionNumber\":2}",
                createdAt: "2026-06-24T03:02:00Z",
                chapterId: "project-1-chapter-002")
        };

        var chain = Assert.Single(ProductionChainProjectionService.BuildWorkflowChainsCore(events));

        Assert.Equal("revision_plan:revision-plan-evidence-only:project-1-chapter-002", chain.Id);
        Assert.Equal("project-1-chapter-002", chain.ChapterId);
        Assert.Equal("chapter-002", chain.ChapterLogicalId);
        Assert.Equal("第二章 邮路围城", chain.ChapterDisplayName);
        Assert.Contains("revision-plan-evidence-only", chain.RevisionPlanIds);
        Assert.Contains(chain.Steps, step => step.Key == "revision_plan" && step.EventId == "event-plan");
        Assert.Contains(chain.Steps, step => step.Key == "context" && step.EventId == "event-package");
        Assert.Contains(chain.Steps, step => step.Key == "commit" && step.EventId == "event-commit");
    }

    [Fact]
    public void BuildWorkflowChainsCore_MergesRevisionPlanAcrossRunsWhenLaterEventsOnlyShareChapter()
    {
        var events = new[]
        {
            Event(
                id: "event-plan",
                runId: "runtime-run-plan",
                packageId: "",
                eventType: "revision_plan_created",
                stage: "revision_plan",
                message: "第二章修订计划已创建。",
                artifactType: "RevisionPlan",
                artifactId: "revision-plan-cross-run",
                dataJson: """
                {
                  "revisionPlanId": "revision-plan-cross-run",
                  "targetChapterId": "chapter-002",
                  "targetChapterDisplayName": "第二章 邮路围城"
                }
                """,
                createdAt: "2026-06-24T03:00:00Z",
                chapterId: ""),
            Event(
                id: "event-package",
                runId: "runtime-run-produce",
                packageId: "pkg-chapter-002-v2",
                eventType: "build_package",
                stage: "PackageBuilt",
                message: "第二章生产包已重建。",
                artifactType: "TianmingPackage",
                artifactId: "pkg-chapter-002-v2",
                dataJson: "{}",
                createdAt: "2026-06-24T03:01:00Z",
                chapterId: "project-1-chapter-002"),
            Event(
                id: "event-commit",
                runId: "runtime-run-produce",
                packageId: "pkg-chapter-002-v2",
                eventType: "chapter_committed",
                stage: "ChapterCommitted",
                message: "第二章 v2 已提交书城。",
                artifactType: "chapter_version",
                artifactId: "version-chapter-002-v2",
                dataJson: "{\"versionNumber\":2}",
                createdAt: "2026-06-24T03:02:00Z",
                chapterId: "project-1-chapter-002")
        };

        var chain = Assert.Single(ProductionChainProjectionService.BuildWorkflowChainsCore(events));

        Assert.Equal("revision_plan:revision-plan-cross-run:project-1-chapter-002", chain.Id);
        Assert.Equal("project-1-chapter-002", chain.ChapterId);
        Assert.Equal("runtime-run-produce", chain.RuntimeRunId);
        Assert.Contains("revision-plan-cross-run", chain.RevisionPlanIds);
        Assert.Contains(chain.Steps, step => step.Key == "revision_plan" && step.EventId == "event-plan");
        Assert.Contains(chain.Steps, step => step.Key == "context" && step.EventId == "event-package");
        Assert.Contains(chain.Steps, step => step.Key == "commit" && step.EventId == "event-commit");
    }

    [Fact]
    public void BuildNovelChains_WithOnlyRevisionPlanProjectsPendingRevisionChain()
    {
        var service = new ProductionChainProjectionService();
        var revisionPlan = new NovelProductionRevisionPlanState
        {
            Id = "revision-plan-pending",
            ProjectId = "project-1",
            RuntimeRunId = "runtime-run-plan",
            Source = "chat",
            PlanType = "chapter_rewrite",
            TargetScope = "chapter",
            TargetChapterId = "project-1-chapter-002",
            TargetChapterLogicalId = "chapter-002",
            TargetChapterDisplayName = "第二章 邮路围城",
            Status = "accepted",
            InvalidatedPackageIdsJson = """["pkg-chapter-002-v1"]""",
            Recommendation = "第二章按用户新创意重写，后续生产包需重新构建。",
            CreatedAt = DateTime.Parse("2026-06-24T03:00:00Z").ToUniversalTime()
        };

        var chains = service.BuildNovelChains(
            Array.Empty<NovelProductionEventState>(),
            Array.Empty<NovelProductionPackageState>(),
            new[] { revisionPlan },
            Array.Empty<NovelProductionOutboxState>(),
            Array.Empty<NovelProductionRebuildLinkState>());

        var chain = Assert.Single(chains);
        Assert.Equal("revision_plan:revision-plan-pending:project-1-chapter-002", chain.Id);
        Assert.Equal("project-1-chapter-002", chain.ChapterId);
        Assert.Equal("chapter-002", chain.ChapterLogicalId);
        Assert.Equal("第二章 邮路围城", chain.ChapterDisplayName);
        Assert.Equal("runtime-run-plan", chain.RuntimeRunId);
        Assert.Equal("pkg-chapter-002-v1", chain.PackageId);
        Assert.Contains("revision-plan-pending", chain.RevisionPlanIds);
        Assert.Contains(chain.Steps, step => step.Key == "revision_plan" && step.EventId == "revision-plan-pending");
    }

    [Fact]
    public void BuildNovelChains_MergesDisplayNamedRevisionPlanWithLaterCanonicalChapterRun()
    {
        var service = new ProductionChainProjectionService();
        var revisionPlan = new NovelProductionRevisionPlanState
        {
            Id = "revision-plan-display-only",
            ProjectId = "project-1",
            RuntimeRunId = "runtime-run-plan",
            Source = "chat",
            PlanType = "chapter_rewrite",
            TargetScope = "chapter",
            TargetChapterDisplayName = "第二章 邮路围城",
            Status = "accepted",
            Recommendation = "第二章按用户新创意重写，后续生产包需重新构建。",
            CreatedAt = DateTime.Parse("2026-06-24T03:00:00Z").ToUniversalTime()
        };
        var events = new[]
        {
            new NovelProductionEventState
            {
                Id = "event-package",
                RuntimeRunId = "runtime-run-produce",
                ProjectId = "project-1",
                ChapterId = "project-1-chapter-002",
                PackageId = "pkg-chapter-002-v2",
                EventType = "chapter_context_package_built",
                Stage = "PackageBuilt",
                Status = "completed",
                Message = "第二章生产包已重建。",
                ArtifactType = "tianming_package",
                ArtifactId = "pkg-chapter-002-v2",
                DataJson = "{}",
                CreatedAt = DateTime.Parse("2026-06-24T03:01:00Z").ToUniversalTime()
            },
            new NovelProductionEventState
            {
                Id = "event-commit",
                RuntimeRunId = "runtime-run-produce",
                ProjectId = "project-1",
                ChapterId = "project-1-chapter-002",
                PackageId = "pkg-chapter-002-v2",
                EventType = "chapter_committed",
                Stage = "ChapterCommitted",
                Status = "completed",
                Message = "第二章 v2 已提交书城。",
                ArtifactType = "chapter_version",
                ArtifactId = "version-chapter-002-v2",
                DataJson = "{\"versionNumber\":2}",
                CreatedAt = DateTime.Parse("2026-06-24T03:02:00Z").ToUniversalTime()
            }
        };

        var chains = service.BuildNovelChains(
            events,
            Array.Empty<NovelProductionPackageState>(),
            new[] { revisionPlan },
            Array.Empty<NovelProductionOutboxState>(),
            Array.Empty<NovelProductionRebuildLinkState>());

        var chain = Assert.Single(chains);
        Assert.Equal("revision_plan:revision-plan-display-only:project-1-chapter-002", chain.Id);
        Assert.Equal("project-1-chapter-002", chain.ChapterId);
        Assert.Equal("chapter-002", chain.ChapterLogicalId);
        Assert.Equal("第二章 邮路围城", chain.ChapterDisplayName);
        Assert.Equal("runtime-run-produce", chain.RuntimeRunId);
        Assert.Contains("revision-plan-display-only", chain.RevisionPlanIds);
        Assert.Contains(chain.Steps, step => step.Key == "revision_plan" && step.EventId == "revision-plan-display-only");
        Assert.Contains(chain.Steps, step => step.Key == "context" && step.EventId == "event-package");
        Assert.Contains(chain.Steps, step => step.Key == "commit" && step.EventId == "event-commit");
    }

    private static WorkflowProductionEventSummary Event(
        string id,
        string runId,
        string packageId,
        string eventType,
        string stage,
        string message,
        string artifactType,
        string artifactId,
        string dataJson,
        string createdAt,
        string chapterId = "project-1-chapter-002",
        WorkflowProductionEvidenceSummary? evidence = null) =>
        new(
            id,
            runId,
            chapterId,
            packageId,
            eventType,
            stage,
            "completed",
            message,
            artifactType,
            artifactId,
            dataJson,
            createdAt,
            evidence);

    private static WorkflowProductionEvidenceSummary EmptyEvidence() =>
        new(
            "",
            "",
            "",
            "",
            0,
            0,
            0,
            "",
            "",
            0,
            "",
            "",
            0,
            0,
            Array.Empty<WorkflowKnowledgeBindingEvidence>(),
            Array.Empty<WorkflowCreativeIntentEvidence>(),
            Array.Empty<WorkflowAgentReviewCheckEvidence>(),
            Array.Empty<WorkflowKnowledgeConstraintEvidence>());
}
