using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Workflow;
using TM.Web.NovelAgentWeb.Support;
using System.Reflection;
using Xunit;

namespace Tests.Unit.Support;

public class ProjectWorkflowTests
{
    [Fact]
    public void BuildArtifactTimeline_DoesNotTreatWorkflowDraftAsLibraryChapter()
    {
        var library = BuildLibrary(Chapter(visibleInWorkflow: true, visibleInLibrary: false, hasGeneratedContent: true));

        var timeline = ProjectWorkflow.BuildArtifactTimeline(
            library,
            new StoryBibleDocument(),
            Array.Empty<WorkflowChapterArtifactSummary>(),
            Array.Empty<AgentScheduledTask>());

        Assert.DoesNotContain(timeline, item => item.Kind == "library_chapter");
        Assert.Contains(timeline, item => item.Kind == "volume_plan");
    }

    [Fact]
    public void BuildProductionStages_UsesCommittedChapterCountForLibraryStage()
    {
        var library = BuildLibrary(Chapter(visibleInWorkflow: true, visibleInLibrary: false, hasGeneratedContent: true));
        var timeline = ProjectWorkflow.BuildArtifactTimeline(
            library,
            new StoryBibleDocument(),
            Array.Empty<WorkflowChapterArtifactSummary>(),
            Array.Empty<AgentScheduledTask>());

        var stages = ProjectWorkflow.BuildProductionStages(library, timeline, Array.Empty<AgentScheduledTask>());
        var libraryStage = Assert.Single(stages, stage => stage.Key == "library");

        Assert.Equal("empty", libraryStage.Status);
        Assert.Equal(0, libraryStage.CurrentCount);
        Assert.Equal(6, libraryStage.TotalCount);
    }

    [Fact]
    public void BuildArtifactTimeline_ProjectsRecordedOutputArtifacts()
    {
        var library = BuildLibrary(Chapter(visibleInWorkflow: true, visibleInLibrary: false, hasGeneratedContent: true));
        var events = new[]
        {
            new WorkflowProductionEventSummary(
                "evt-output-artifact",
                "run-001",
                "chapter-001",
                "package-001",
                "tool_output_artifact_recorded",
                NovelAgentProductionStages.DraftGeneration,
                "completed",
                "第二章草稿已生成。",
                "chapter_draft",
                "draft-chapter-002-v1",
                """
                {
                  "toolName":"ProduceChapter",
                  "outputKind":"ProcessArtifact",
                  "visibleInWorkflow":true,
                  "visibleInLibrary":false,
                  "userVisibleWhere":["创作工作流"],
                  "sourceEventType":"chapter_draft_generated",
                  "sourceEventId":"evt-draft-002",
                  "summary":"第二章草稿已生成，等待门禁校验。"
                }
                """,
                DateTime.UtcNow.ToString("O"))
        };

        var timeline = ProjectWorkflow.BuildArtifactTimeline(
            library,
            new StoryBibleDocument(),
            Array.Empty<WorkflowChapterArtifactSummary>(),
            Array.Empty<AgentScheduledTask>(),
            events);

        var artifact = Assert.Single(timeline, item => item.Id == "output-artifact:evt-output-artifact:draft-chapter-002-v1");
        Assert.Equal("chapter_draft", artifact.Kind);
        Assert.Equal("过程产物", artifact.Label);
        Assert.Equal("创作工作流", artifact.Surface);
        Assert.Equal("completed", artifact.Status);
        Assert.Equal("第二章草稿已生成，等待门禁校验。", artifact.Summary);
        Assert.Equal("chapter-001", artifact.ChapterId);
        Assert.Equal("run-001", artifact.RunId);
        Assert.False(artifact.IsFinal);
        Assert.True(artifact.IsUserVisible);
        Assert.Equal("OutputArtifactRecorder:chapter_draft_generated", artifact.Source);
    }

    [Fact]
    public void BuildArtifactTimeline_UsesEventScopedIdsForRepeatedOutputArtifacts()
    {
        var library = BuildLibrary(Chapter(visibleInWorkflow: true, visibleInLibrary: false, hasGeneratedContent: true));
        var events = new[]
        {
            new WorkflowProductionEventSummary(
                "evt-gate-failed-1",
                "run-001",
                "chapter-001",
                "package-001",
                "tool_output_artifact_recorded",
                NovelAgentProductionStages.GateValidation,
                "failed",
                "第一次门禁未通过。",
                "generation_gate_report",
                "gate_failed",
                """
                {
                  "outputKind":"ProcessArtifact",
                  "visibleInWorkflow":true,
                  "sourceEventType":"chapter_gate_validated",
                  "summary":"第一次门禁未通过。"
                }
                """,
                DateTime.UtcNow.AddSeconds(-2).ToString("O")),
            new WorkflowProductionEventSummary(
                "evt-gate-failed-2",
                "run-001",
                "chapter-001",
                "package-001",
                "tool_output_artifact_recorded",
                NovelAgentProductionStages.GateValidation,
                "failed",
                "第二次门禁未通过。",
                "generation_gate_report",
                "gate_failed",
                """
                {
                  "outputKind":"ProcessArtifact",
                  "visibleInWorkflow":true,
                  "sourceEventType":"chapter_gate_validated",
                  "summary":"第二次门禁未通过。"
                }
                """,
                DateTime.UtcNow.AddSeconds(-1).ToString("O"))
        };

        var timeline = ProjectWorkflow.BuildArtifactTimeline(
            library,
            new StoryBibleDocument(),
            Array.Empty<WorkflowChapterArtifactSummary>(),
            Array.Empty<AgentScheduledTask>(),
            events);

        var gateArtifacts = timeline
            .Where(item => item.Kind == "generation_gate_report")
            .ToList();

        Assert.Equal(2, gateArtifacts.Count);
        Assert.Equal(gateArtifacts.Count, gateArtifacts.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(gateArtifacts, item => item.Id == "output-artifact:evt-gate-failed-1:gate_failed");
        Assert.Contains(gateArtifacts, item => item.Id == "output-artifact:evt-gate-failed-2:gate_failed");
    }

    [Fact]
    public void BuildProductionStages_ProjectsProductionEventsIntoTianmingStages()
    {
        var library = BuildLibrary(Chapter(visibleInWorkflow: true, visibleInLibrary: false, hasGeneratedContent: true));
        var events = new[]
        {
            new WorkflowProductionEventSummary(
                "evt-package",
                "run-001",
                "chapter-001",
                "package-001",
                "chapter_context_package_built",
                NovelAgentProductionStages.ContextPackage,
                "completed",
                "章节生产包已构建并持久化。",
                "tianming_package",
                "package-001",
                "{\"hardFactCount\":2,\"knowledgeBindingCount\":2,\"acceptedCreativeIntentCount\":1}",
                DateTime.UtcNow.AddMinutes(-4).ToString("O"),
                new WorkflowProductionEvidenceSummary(
                    "chapter_context_package",
                    "pending",
                    "chapter-context-v1",
                    "agentic-tianming-v1",
                    2,
                    1,
                    0,
                    string.Empty,
                    string.Empty,
                    0,
                    string.Empty,
                    string.Empty,
                    0,
                    1,
                    new[]
                    {
                        new WorkflowKnowledgeBindingEvidence(
                            "knowledge-hardfact-1",
                            "银蓝邮徽能力边界",
                            "HardFact",
                            "referenced",
                            10),
                        new WorkflowKnowledgeBindingEvidence(
                            "knowledge-style-1",
                            "废土邮路风格",
                            "Style",
                            "imported",
                            5)
                    },
                    Array.Empty<WorkflowCreativeIntentEvidence>(),
                    Array.Empty<WorkflowAgentReviewCheckEvidence>(),
                    Array.Empty<WorkflowKnowledgeConstraintEvidence>())),
            new WorkflowProductionEventSummary(
                "evt-gate",
                "run-001",
                "chapter-001",
                "package-001",
                "chapter_gate_validated",
                NovelAgentProductionStages.GateValidation,
                "failed",
                "章节草稿未通过硬门禁。",
                "generation_gate_report",
                "gate_report",
                "{\"gateStatus\":\"gate_failed\",\"issues\":[\"缺少承接\"]}",
                DateTime.UtcNow.ToString("O"),
                new WorkflowProductionEvidenceSummary(
                    "chapter_context_package",
                    "pending",
                    "chapter-context-v1",
                    "agentic-tianming-v1",
                    2,
                    1,
                    3,
                    "chapter_commit",
                    "fact-003",
                    2,
                    "version-002",
                    "committed",
                    3200,
                    0,
                    Array.Empty<WorkflowKnowledgeBindingEvidence>(),
                    Array.Empty<WorkflowCreativeIntentEvidence>(),
                    Array.Empty<WorkflowAgentReviewCheckEvidence>(),
                    Array.Empty<WorkflowKnowledgeConstraintEvidence>()))
        };

        var stages = ProjectWorkflow.BuildProductionStages(
            library,
            Array.Empty<WorkflowArtifactTimelineItem>(),
            Array.Empty<AgentScheduledTask>(),
            events);

        var packageStage = Assert.Single(stages, stage => stage.Key == "context");
        var gateStage = Assert.Single(stages, stage => stage.Key == "gate");

        Assert.Equal("ready", packageStage.Status);
        Assert.Equal("package-001", packageStage.PrimaryArtifactId);
        Assert.Equal("run-001", packageStage.PrimaryRunId);
        Assert.Equal("blocked", gateStage.Status);
        Assert.Contains("章节草稿未通过硬门禁", gateStage.Summary);
        Assert.NotEmpty(gateStage.ProductionEvents);
        Assert.Equal(NovelAgentProductionStages.GateValidation, gateStage.ProductionEvents[0].Stage);
        Assert.Equal(3, gateStage.ProductionEvents[0].Evidence?.FactSnapshotVersion);
        Assert.Equal(2, gateStage.ProductionEvents[0].Evidence?.ChapterVersionNumber);
        Assert.Equal(2, packageStage.ProductionEvents[0].Evidence?.KnowledgeFactCount);
        Assert.Equal(2, packageStage.ProductionEvents[0].Evidence?.KnowledgeBindingCount);
        Assert.Equal(1, packageStage.ProductionEvents[0].Evidence?.AcceptedCreativeIntentCount);
        Assert.Contains(packageStage.ProductionEvents[0].Evidence!.KnowledgeBindings,
            binding => binding.Title == "银蓝邮徽能力边界" &&
                       binding.EntryType == "HardFact" &&
                       binding.ProjectUsageStatus == "referenced");
    }

    [Fact]
    public void BuildProductionStages_ProjectsKnowledgeUsedEventsIntoLibraryStage()
    {
        var library = BuildLibrary(Chapter(visibleInWorkflow: true, visibleInLibrary: true, hasGeneratedContent: true));
        var events = new[]
        {
            new WorkflowProductionEventSummary(
                "evt-knowledge-used",
                "run-001",
                "chapter-001",
                "package-001",
                "knowledge_bindings_used",
                NovelAgentProductionStages.FactsPersisted,
                "completed",
                "本章生产包实际使用 1 条项目知识绑定。",
                "knowledge_bindings",
                "chapter-001",
                "{\"bindingCount\":1,\"knowledgeIds\":[\"kb-1\"]}",
                DateTime.UtcNow.ToString("O"),
                new WorkflowProductionEvidenceSummary(
                    "chapter_context_package",
                    "completed",
                    "chapter-context-v1",
                    "agentic-tianming-v1",
                    0,
                    0,
                    1,
                    "chapter_commit",
                    "fact-001",
                    1,
                    "version-001",
                    "committed",
                    3200,
                    0,
                    new[]
                    {
                        new WorkflowKnowledgeBindingEvidence(
                            "kb-1",
                            "盐鸦驿站",
                            "HardFact",
                            "used",
                            90)
                    },
                    Array.Empty<WorkflowCreativeIntentEvidence>(),
                    Array.Empty<WorkflowAgentReviewCheckEvidence>(),
                    Array.Empty<WorkflowKnowledgeConstraintEvidence>()))
        };

        var stages = ProjectWorkflow.BuildProductionStages(
            library,
            Array.Empty<WorkflowArtifactTimelineItem>(),
            Array.Empty<AgentScheduledTask>(),
            events);

        var libraryStage = Assert.Single(stages, stage => stage.Key == "library");
        var evt = Assert.Single(libraryStage.ProductionEvents, item => item.Id == "evt-knowledge-used");
        var binding = Assert.Single(evt.Evidence!.KnowledgeBindings);
        Assert.Equal("used", binding.ProjectUsageStatus);
    }

    [Fact]
    public void BuildProductionStages_DoesNotKeepStageBlockedAfterLaterSuccessfulEvent()
    {
        var now = DateTime.UtcNow;
        var library = BuildLibrary(Chapter(visibleInWorkflow: true, visibleInLibrary: true, hasGeneratedContent: true));
        var events = new[]
        {
            new WorkflowProductionEventSummary(
                "evt-gate-failed",
                "run-001",
                "chapter-001",
                "package-001",
                "chapter_gate_validated",
                NovelAgentProductionStages.GateValidation,
                "failed",
                "第一次门禁未通过。",
                "generation_gate_report",
                "gate-001-a",
                "{}",
                now.AddMinutes(-3).ToString("O")),
            new WorkflowProductionEventSummary(
                "evt-gate-passed",
                "run-001",
                "chapter-001",
                "package-001",
                "chapter_gate_validated",
                NovelAgentProductionStages.GateValidation,
                "completed",
                "修订后门禁通过。",
                "generation_gate_report",
                "gate-001-b",
                "{}",
                now.AddMinutes(-2).ToString("O")),
            new WorkflowProductionEventSummary(
                "evt-commit",
                "run-001",
                "chapter-001",
                "package-001",
                "chapter_committed",
                NovelAgentProductionStages.ChapterCommit,
                "completed",
                "章节已提交书城。",
                "chapter_version",
                "version-001",
                "{}",
                now.AddMinutes(-1).ToString("O"))
        };

        var stages = ProjectWorkflow.BuildProductionStages(
            library,
            Array.Empty<WorkflowArtifactTimelineItem>(),
            Array.Empty<AgentScheduledTask>(),
            events);

        var gateStage = Assert.Single(stages, stage => stage.Key == "gate");

        Assert.Equal("ready", gateStage.Status);
        Assert.Equal("修订后门禁通过。", gateStage.Summary);
        Assert.Equal("gate-001-b", gateStage.PrimaryArtifactId);
        Assert.Equal(2, gateStage.ProductionEvents.Count);
    }

    [Fact]
    public void BuildProductionStages_DoesNotKeepGateBlockedAfterRepairAndCommit()
    {
        var now = DateTime.UtcNow;
        var library = BuildLibrary(Chapter(visibleInWorkflow: true, visibleInLibrary: true, hasGeneratedContent: true));
        var events = new[]
        {
            new WorkflowProductionEventSummary(
                "evt-gate-failed",
                "run-001",
                "chapter-001",
                "package-001",
                "chapter_gate_validated",
                NovelAgentProductionStages.GateValidation,
                "failed",
                "门禁未通过。",
                "generation_gate_report",
                "gate-001",
                "{}",
                now.AddMinutes(-4).ToString("O")),
            new WorkflowProductionEventSummary(
                "evt-repair-completed",
                "run-001",
                "chapter-001",
                "package-001",
                "chapter_draft_repaired",
                NovelAgentProductionStages.DraftRepair,
                "completed",
                "草稿已修复并通过门禁。",
                "chapter_draft",
                "draft-001-v2",
                "{}",
                now.AddMinutes(-3).ToString("O")),
            new WorkflowProductionEventSummary(
                "evt-review",
                "run-001",
                "chapter-001",
                "package-001",
                "chapter_quality_reviewed",
                NovelAgentProductionStages.QualityReview,
                "completed",
                "质量评审通过。",
                "agent_review",
                "review-001",
                "{}",
                now.AddMinutes(-2).ToString("O")),
            new WorkflowProductionEventSummary(
                "evt-commit",
                "run-001",
                "chapter-001",
                "package-001",
                "chapter_committed",
                NovelAgentProductionStages.ChapterCommit,
                "completed",
                "章节已提交书城。",
                "chapter_version",
                "version-001",
                "{}",
                now.AddMinutes(-1).ToString("O"))
        };

        var stages = ProjectWorkflow.BuildProductionStages(
            library,
            Array.Empty<WorkflowArtifactTimelineItem>(),
            Array.Empty<AgentScheduledTask>(),
            events);

        var gateStage = Assert.Single(stages, stage => stage.Key == "gate");

        Assert.Equal("ready", gateStage.Status);
        Assert.Contains("已恢复", gateStage.Summary);
        Assert.Equal("version-001", gateStage.PrimaryArtifactId);
    }

    [Fact]
    public void BuildProductionStages_DoesNotKeepDraftBlockedAfterLaterSuccessfulToolExecution()
    {
        var now = DateTime.UtcNow;
        var library = BuildLibrary(Chapter(visibleInWorkflow: true, visibleInLibrary: true, hasGeneratedContent: true));
        var events = new[]
        {
            new WorkflowProductionEventSummary(
                "evt-draft",
                "run-002",
                "chapter-002",
                "package-002",
                "chapter_draft_generated",
                NovelAgentProductionStages.DraftGeneration,
                "completed",
                "章节草稿已生成。",
                "chapter_draft",
                "draft-002",
                "{}",
                now.AddMinutes(-1).ToString("O"))
        };
        var tools = new[]
        {
            new WorkflowToolExecutionSummary
            {
                Id = "tool-produce-failed",
                RunId = "run-001",
                ToolName = "ProduceChapter",
                Phase = "candidate_selected",
                Status = "failed",
                StartedAt = now.AddMinutes(-4).ToString("O"),
                CompletedAt = now.AddMinutes(-3).ToString("O")
            },
            new WorkflowToolExecutionSummary
            {
                Id = "tool-produce-succeeded",
                RunId = "run-002",
                ToolName = "ProduceChapter",
                Phase = "committed",
                Status = "succeeded",
                StartedAt = now.AddMinutes(-2).ToString("O"),
                CompletedAt = now.AddMinutes(-1).ToString("O")
            }
        };

        var stages = ProjectWorkflow.BuildProductionStages(
            library,
            Array.Empty<WorkflowArtifactTimelineItem>(),
            Array.Empty<AgentScheduledTask>(),
            events,
            tools);

        var draftStage = Assert.Single(stages, stage => stage.Key == "draft");
        var tool = Assert.Single(draftStage.ToolExecutions);

        Assert.Equal("ready", draftStage.Status);
        Assert.Equal("tool-produce-succeeded", tool.Id);
        Assert.Equal("succeeded", tool.Status);
    }

    [Fact]
    public void BuildProductionStages_KeepsDraftBlockedWhenLatestToolFailureIsAfterReadyEvidence()
    {
        var now = DateTime.UtcNow;
        var library = BuildLibrary(Chapter(visibleInWorkflow: true, visibleInLibrary: false, hasGeneratedContent: true));
        var events = new[]
        {
            new WorkflowProductionEventSummary(
                "evt-draft",
                "run-001",
                "chapter-002",
                "package-002",
                "chapter_draft_generated",
                NovelAgentProductionStages.DraftGeneration,
                "completed",
                "章节草稿已生成。",
                "chapter_draft",
                "draft-002",
                "{}",
                now.AddMinutes(-3).ToString("O"))
        };
        var tools = new[]
        {
            new WorkflowToolExecutionSummary
            {
                Id = "tool-produce-succeeded",
                RunId = "run-001",
                ToolName = "ProduceChapter",
                Phase = "committed",
                Status = "succeeded",
                StartedAt = now.AddMinutes(-4).ToString("O"),
                CompletedAt = now.AddMinutes(-3).ToString("O")
            },
            new WorkflowToolExecutionSummary
            {
                Id = "tool-produce-failed",
                RunId = "run-002",
                ToolName = "ProduceChapter",
                Phase = "candidate_selected",
                Status = "failed",
                StartedAt = now.AddMinutes(-2).ToString("O"),
                CompletedAt = now.AddMinutes(-1).ToString("O")
            }
        };

        var stages = ProjectWorkflow.BuildProductionStages(
            library,
            Array.Empty<WorkflowArtifactTimelineItem>(),
            Array.Empty<AgentScheduledTask>(),
            events,
            tools);

        var draftStage = Assert.Single(stages, stage => stage.Key == "draft");
        var tool = Assert.Single(draftStage.ToolExecutions);

        Assert.Equal("blocked", draftStage.Status);
        Assert.Equal("tool-produce-failed", tool.Id);
    }

    [Fact]
    public void BuildProductionStages_ProjectsToolSemanticContractsIntoMatchingStages()
    {
        var library = BuildLibrary(Chapter(visibleInWorkflow: true, visibleInLibrary: false, hasGeneratedContent: true));
        var tools = new[]
        {
            new WorkflowToolExecutionSummary
            {
                Id = "tool-draft-1",
                RunId = "run-001",
                ToolName = NovelAgentProductionStages.DraftGeneration,
                Phase = "writing",
                Status = "running",
                Risk = "High",
                SemanticContract = new WorkflowToolSemanticContractSummary
                {
                    DisplayName = "章节正文生成",
                    InputArtifacts = { "chapter_plan_run", "continuity_pack", "knowledge_binding_snapshot" },
                    OutputArtifacts = { "chapter_draft", "kernel_gate_report", "chapter_version" },
                    IdempotencyPolicy = "Uses runId + targetChapterId + commitPolicy.",
                    RollbackPolicy = "Recover through ChapterVersion rollback."
                }
            }
        };

        var stages = ProjectWorkflow.BuildProductionStages(
            library,
            Array.Empty<WorkflowArtifactTimelineItem>(),
            Array.Empty<AgentScheduledTask>(),
            Array.Empty<WorkflowProductionEventSummary>(),
            tools);

        var draftStage = Assert.Single(stages, stage => stage.Key == "draft");
        var tool = Assert.Single(draftStage.ToolExecutions);
        Assert.Equal("章节正文生成", tool.SemanticContract.DisplayName);
        Assert.Contains("continuity_pack", tool.SemanticContract.InputArtifacts);
        Assert.Contains("chapter_draft", tool.SemanticContract.OutputArtifacts);
        Assert.Equal("running", draftStage.Status);
        Assert.Contains("章节正文生成", draftStage.Summary);
    }

    [Fact]
    public void BuildProductionStages_ShowsBlockedToolInputArtifactsInStageDetail()
    {
        var library = BuildLibrary(Chapter(visibleInWorkflow: true, visibleInLibrary: false, hasGeneratedContent: true));
        var tools = new[]
        {
            new WorkflowToolExecutionSummary
            {
                Id = "tool-context-blocked",
                RunId = "run-002",
                ToolName = "ProduceChapter",
                Phase = NovelAgentProductionStages.ContextPackage,
                Status = "failed",
                Risk = "High",
                Failure = new WorkflowToolFailureSummary
                {
                    Code = "TOOL_INPUT_ARTIFACT_BLOCKED",
                    FailedStage = "semantic_precondition",
                    Reason = "上一章提交后后台沉淀尚未完成。",
                    Recoverable = true,
                    RecommendedAction = "QueryProductionOutbox",
                    InputArtifacts = new[]
                    {
                        new WorkflowToolInputArtifactSummary(
                            "post_commit_outbox",
                            "retryable_failed",
                            "outbox-finalize-001",
                            "事实沉淀失败，可重试。",
                            true,
                            new[] { "QueryProductionOutbox", "RetryProductionOutbox" })
                    }
                }
            }
        };

        var stages = ProjectWorkflow.BuildProductionStages(
            library,
            Array.Empty<WorkflowArtifactTimelineItem>(),
            Array.Empty<AgentScheduledTask>(),
            Array.Empty<WorkflowProductionEventSummary>(),
            tools);

        var contextStage = Assert.Single(stages, stage => stage.Key == "context");
        Assert.Equal("blocked", contextStage.Status);
        Assert.Contains("post_commit_outbox", contextStage.Detail);
        Assert.Contains("retryable_failed", contextStage.Detail);
        Assert.Contains("RetryProductionOutbox", contextStage.Detail);
    }

    [Fact]
    public void BuildProductionStages_ProjectsDependencyBlockIntoContextStage()
    {
        var library = BuildLibrary(Chapter(visibleInWorkflow: true, visibleInLibrary: false, hasGeneratedContent: false));
        var events = new[]
        {
            new WorkflowProductionEventSummary(
                "evt-dependency-blocked",
                "run-002",
                "chapter-002",
                string.Empty,
                "production_dependency_blocked",
                NovelAgentProductionStages.ContextPackage,
                "blocked",
                "上一章提交后后台沉淀尚未完成，暂不能构建下一章生产包。",
                "post_commit_outbox",
                "outbox-finalize-001",
                "{\"reason\":\"previous_chapter_post_commit_outbox_pending\"}",
                DateTime.UtcNow.ToString("O"))
        };

        var stages = ProjectWorkflow.BuildProductionStages(
            library,
            Array.Empty<WorkflowArtifactTimelineItem>(),
            Array.Empty<AgentScheduledTask>(),
            events);

        var contextStage = Assert.Single(stages, stage => stage.Key == "context");
        var evt = Assert.Single(contextStage.ProductionEvents);
        Assert.Equal("production_dependency_blocked", evt.EventType);
        Assert.Equal("blocked", evt.Status);
        Assert.Equal("post_commit_outbox", evt.ArtifactType);
    }

    [Fact]
    public void BuildProductionStages_ProjectsIndexFailureIntoSeparateStageWithoutBlockingLibrary()
    {
        var library = BuildLibrary(Chapter(visibleInWorkflow: true, visibleInLibrary: true, hasGeneratedContent: true));
        var events = new[]
        {
            new WorkflowProductionEventSummary(
                "evt-commit",
                "run-001",
                "chapter-001",
                "package-001",
                "chapter_committed",
                NovelAgentProductionStages.ChapterCommit,
                "completed",
                "章节已提交书城。",
                "chapter_version",
                "version-001",
                "{}",
                DateTime.UtcNow.AddMinutes(-1).ToString("O")),
            new WorkflowProductionEventSummary(
                "evt-index-failed",
                "run-001",
                "chapter-001",
                "package-001",
                "outbox_failed",
                "index_outbox",
                "retryable_failed",
                "后台索引失败，已等待重试。",
                "outbox_event",
                "outbox-001",
                "{\"error\":\"Qdrant timeout\"}",
                DateTime.UtcNow.ToString("O"),
                Failure: new WorkflowProductionFailureSummary(
                    "INDEX_FAILED",
                    "index_outbox",
                    "Qdrant timeout",
                    true,
                    "RetryOutboxEvent(outbox-001)",
                    new[] { "outbox-001", "chapter-001" },
                    false))
        };

        var stages = ProjectWorkflow.BuildProductionStages(
            library,
            Array.Empty<WorkflowArtifactTimelineItem>(),
            Array.Empty<AgentScheduledTask>(),
            events);

        var libraryStage = Assert.Single(stages, stage => stage.Key == "library");
        var indexStage = Assert.Single(stages, stage => stage.Key == "index");

        Assert.Equal("ready", libraryStage.Status);
        Assert.Equal("blocked", indexStage.Status);
        Assert.Contains("后台索引失败", indexStage.Summary);
        var failure = Assert.Single(indexStage.ProductionEvents).Failure;
        Assert.NotNull(failure);
        Assert.Equal("INDEX_FAILED", failure!.Code);
        Assert.Equal("RetryOutboxEvent(outbox-001)", failure.RecommendedAction);
    }

    [Fact]
    public void BuildProductionStages_ProjectsRevisionPlanEventsIntoSeparateStage()
    {
        var library = BuildLibrary(Chapter(visibleInWorkflow: true, visibleInLibrary: true, hasGeneratedContent: true));
        var events = new[]
        {
            new WorkflowProductionEventSummary(
                "evt-revision-invalidated",
                "run-revision",
                "chapter-002",
                string.Empty,
                "revision_plan_packages_invalidated",
                "packages_invalidated",
                "stale",
                "修订计划已使 2 个旧生产包失效，等待重建。",
                "RevisionPlan",
                "revision-plan-001",
                "{\"invalidatedPackageIds\":[\"package-002-v1\",\"package-003-v1\"]}",
                DateTime.UtcNow.AddMinutes(-2).ToString("O")),
            new WorkflowProductionEventSummary(
                "evt-revision-executed",
                "run-revision",
                "chapter-002",
                string.Empty,
                "revision_plan_executed",
                NovelAgentProductionStages.ChapterCommit,
                "executed",
                "修订计划已随章节提交执行。",
                "RevisionPlan",
                "revision-plan-001",
                "{\"chapterVersionId\":\"chapter-version-002-v2\"}",
                DateTime.UtcNow.ToString("O"),
                new WorkflowProductionEvidenceSummary(
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    0,
                    0,
                    4,
                    "chapter_commit",
                    "fact-004",
                    2,
                    "chapter-version-002-v2",
                    "committed",
                    3600,
                    0,
                    Array.Empty<WorkflowKnowledgeBindingEvidence>(),
                    Array.Empty<WorkflowCreativeIntentEvidence>(),
                    Array.Empty<WorkflowAgentReviewCheckEvidence>(),
                    Array.Empty<WorkflowKnowledgeConstraintEvidence>()))
        };

        var stages = ProjectWorkflow.BuildProductionStages(
            library,
            Array.Empty<WorkflowArtifactTimelineItem>(),
            Array.Empty<AgentScheduledTask>(),
            events);

        var revisionStage = Assert.Single(stages, stage => stage.Key == "revision_plan");

        Assert.Equal("修订计划", revisionStage.Label);
        Assert.Equal("ready", revisionStage.Status);
        Assert.Equal("revision-plan-001", revisionStage.PrimaryArtifactId);
        Assert.Equal("run-revision", revisionStage.PrimaryRunId);
        Assert.Equal(2, revisionStage.ProductionEvents.Count);
        Assert.Contains(revisionStage.ProductionEvents, evt => evt.EventType == "revision_plan_packages_invalidated" && evt.Status == "stale");
        Assert.Contains(revisionStage.ProductionEvents, evt => evt.EventType == "revision_plan_executed" && evt.Status == "executed");
        Assert.DoesNotContain(stages.Single(stage => stage.Key == "library").ProductionEvents,
            evt => evt.EventType == "revision_plan_executed");
    }

    [Fact]
    public void BuildProductionChains_GroupsRevisionRebuildCommitFactAndOutboxIntoReadableChain()
    {
        var now = DateTime.UtcNow;
        var events = new[]
        {
            new WorkflowProductionEventSummary(
                "evt-revision-ready",
                "run-002",
                "chapter-002",
                string.Empty,
                "revision_plan_ready",
                "revision_plan",
                "ready_for_rebuild",
                "修订计划已准备重建第二章。",
                "RevisionPlan",
                "revision-plan-002",
                "{}",
                now.AddMinutes(-5).ToString("O")),
            new WorkflowProductionEventSummary(
                "evt-context-rebuilt",
                "run-002",
                "chapter-002",
                "pkg-chapter-002-v2",
                "chapter_context_package_built",
                NovelAgentProductionStages.ContextPackage,
                "completed",
                "第二章生产包已重建。",
                "tianming_package",
                "pkg-chapter-002-v2",
                "{}",
                now.AddMinutes(-4).ToString("O"),
                new WorkflowProductionEvidenceSummary(
                    "chapter_context_package",
                    "completed",
                    "chapter-context-v1",
                    "agentic-tianming-v1",
                    2,
                    1,
                    0,
                    string.Empty,
                    string.Empty,
                    0,
                    string.Empty,
                    string.Empty,
                    0,
                    0,
                    Array.Empty<WorkflowKnowledgeBindingEvidence>(),
                    Array.Empty<WorkflowCreativeIntentEvidence>(),
                    Array.Empty<WorkflowAgentReviewCheckEvidence>(),
                    Array.Empty<WorkflowKnowledgeConstraintEvidence>())
                {
                    SourceRevisionPlans = new[]
                    {
                        new WorkflowRevisionPlanEvidence(
                            "revision-plan-002",
                            "chapter_rewrite",
                            "chapter",
                            "chapter-002",
                            "chapter-002",
                            "第二章",
                            "ready_for_rebuild",
                            new[] { "chapter-002" },
                            new[] { "pkg-chapter-002-v1" },
                            "medium",
                            "重写第二章承接第一章结尾。")
                    },
                    RebuildLinks = new[]
                    {
                        new WorkflowPackageRebuildLinkEvidence(
                            "pkg-chapter-002-v1",
                            "stale",
                            "pkg-chapter-002-v2",
                            "completed",
                            "chapter_context_package",
                            "chapter-002",
                            "run-002")
                    }
                }),
            new WorkflowProductionEventSummary(
                "evt-commit",
                "run-002",
                "chapter-002",
                "pkg-chapter-002-v2",
                "chapter_committed",
                NovelAgentProductionStages.ChapterCommit,
                "completed",
                "第二章已提交书城。",
                "chapter_version",
                "version-chapter-002-v2",
                "{}",
                now.AddMinutes(-2).ToString("O"),
                new WorkflowProductionEvidenceSummary(
                    "chapter_context_package",
                    "completed",
                    "chapter-context-v1",
                    "agentic-tianming-v1",
                    2,
                    1,
                    8,
                    "chapter_commit",
                    "fact-chapter-002-v2",
                    2,
                    "version-chapter-002-v2",
                    "committed",
                    3600,
                    0,
                    Array.Empty<WorkflowKnowledgeBindingEvidence>(),
                    Array.Empty<WorkflowCreativeIntentEvidence>(),
                    Array.Empty<WorkflowAgentReviewCheckEvidence>(),
                    Array.Empty<WorkflowKnowledgeConstraintEvidence>())),
            new WorkflowProductionEventSummary(
                "evt-outbox",
                "run-002",
                "chapter-002",
                "pkg-chapter-002-v2",
                "outbox_completed",
                "index_outbox",
                "completed",
                "后台 outbox 处理完成。 index_chapter_content/chapter_version",
                "outbox_event",
                "outbox-chapter-002",
                "{}",
                now.AddMinutes(-1).ToString("O"),
                new WorkflowProductionEvidenceSummary(
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    0,
                    0,
                    0,
                    string.Empty,
                    string.Empty,
                    0,
                    string.Empty,
                    string.Empty,
                    0,
                    0,
                    Array.Empty<WorkflowKnowledgeBindingEvidence>(),
                    Array.Empty<WorkflowCreativeIntentEvidence>(),
                    Array.Empty<WorkflowAgentReviewCheckEvidence>(),
                    Array.Empty<WorkflowKnowledgeConstraintEvidence>())
                {
                    Outbox = new WorkflowOutboxEvidence(
                        "outbox-chapter-002",
                        "index_chapter_content",
                        "chapter_version",
                        "version-chapter-002-v2",
                        "completed",
                        1,
                        string.Empty)
                })
        };

        var chains = ProjectWorkflow.BuildProductionChains(events);

        var chain = Assert.Single(chains);
        Assert.Equal("chapter-002", chain.ChapterId);
        Assert.Equal("run-002", chain.RuntimeRunId);
        Assert.Equal("pkg-chapter-002-v2", chain.PackageId);
        Assert.Equal("completed", chain.Status);
        Assert.Equal("第二章已提交书城。", chain.Summary);
        Assert.Equal("version-chapter-002-v2", chain.ChapterVersionId);
        Assert.Equal("fact-chapter-002-v2", chain.FactSnapshotId);
        Assert.Equal(8, chain.FactSnapshotVersion);
        Assert.Contains(chain.RevisionPlanIds, id => id == "revision-plan-002");
        Assert.Contains(chain.RebuildLinks, link => link.OldPackageId == "pkg-chapter-002-v1" && link.NewPackageId == "pkg-chapter-002-v2");
        Assert.Contains(chain.Steps, step => step.Key == "revision_plan" && step.Status == "ready_for_rebuild");
        Assert.Contains(chain.Steps, step => step.Key == "context" && step.ArtifactId == "pkg-chapter-002-v2");
        Assert.Contains(chain.Steps, step => step.Key == "commit" && step.ArtifactId == "version-chapter-002-v2");
        Assert.Contains(chain.Steps, step => step.Key == "outbox" && step.OutboxEventId == "outbox-chapter-002");
    }

    [Fact]
    public void BuildProductionChains_GroupsRevisionPlanAndRebuildAcrossRuntimeRuns()
    {
        var now = DateTime.UtcNow;
        var events = new[]
        {
            new WorkflowProductionEventSummary(
                "evt-revision-ready",
                "run-plan",
                "chapter-002",
                string.Empty,
                "revision_plan_ready",
                "revision_plan",
                "ready_for_rebuild",
                "修订计划已准备重建第二章。",
                "RevisionPlan",
                "revision-plan-002",
                "{}",
                now.AddMinutes(-5).ToString("O")),
            new WorkflowProductionEventSummary(
                "evt-context-rebuilt",
                "run-produce",
                "chapter-002",
                "pkg-chapter-002-v2",
                "chapter_context_package_built",
                NovelAgentProductionStages.ContextPackage,
                "completed",
                "第二章生产包已重建。",
                "tianming_package",
                "pkg-chapter-002-v2",
                "{}",
                now.AddMinutes(-4).ToString("O"),
                new WorkflowProductionEvidenceSummary(
                    "chapter_context_package",
                    "completed",
                    "chapter-context-v1",
                    "agentic-tianming-v1",
                    0,
                    0,
                    0,
                    string.Empty,
                    string.Empty,
                    0,
                    string.Empty,
                    string.Empty,
                    0,
                    0,
                    Array.Empty<WorkflowKnowledgeBindingEvidence>(),
                    Array.Empty<WorkflowCreativeIntentEvidence>(),
                    Array.Empty<WorkflowAgentReviewCheckEvidence>(),
                    Array.Empty<WorkflowKnowledgeConstraintEvidence>())
                {
                    SourceRevisionPlans = new[]
                    {
                        new WorkflowRevisionPlanEvidence(
                            "revision-plan-002",
                            "chapter_rewrite",
                            "chapter",
                            "chapter-002",
                            "chapter-002",
                            "第二章",
                            "ready_for_rebuild",
                            new[] { "chapter-002" },
                            new[] { "pkg-chapter-002-v1" },
                            "medium",
                            "按用户要求重写第二章。")
                    },
                    RebuildLinks = new[]
                    {
                        new WorkflowPackageRebuildLinkEvidence(
                            "pkg-chapter-002-v1",
                            "stale",
                            "pkg-chapter-002-v2",
                            "completed",
                            "chapter_context_package",
                            "chapter-002",
                            "run-produce")
                    }
                }),
            new WorkflowProductionEventSummary(
                "evt-commit",
                "run-produce",
                "chapter-002",
                "pkg-chapter-002-v2",
                "chapter_committed",
                NovelAgentProductionStages.ChapterCommit,
                "completed",
                "第二章已提交书城。",
                "chapter_version",
                "version-chapter-002-v2",
                "{\"revisionPlanId\":\"revision-plan-002\"}",
                now.AddMinutes(-2).ToString("O"),
                new WorkflowProductionEvidenceSummary(
                    "chapter_context_package",
                    "completed",
                    "chapter-context-v1",
                    "agentic-tianming-v1",
                    0,
                    0,
                    3,
                    "chapter_commit",
                    "fact-chapter-002-v2",
                    2,
                    "version-chapter-002-v2",
                    "committed",
                    3600,
                    0,
                    Array.Empty<WorkflowKnowledgeBindingEvidence>(),
                    Array.Empty<WorkflowCreativeIntentEvidence>(),
                    Array.Empty<WorkflowAgentReviewCheckEvidence>(),
                    Array.Empty<WorkflowKnowledgeConstraintEvidence>()))
        };

        var chain = Assert.Single(ProjectWorkflow.BuildProductionChains(events));

        Assert.Equal("chapter-002", chain.ChapterId);
        Assert.Equal("run-produce", chain.RuntimeRunId);
        Assert.Equal("pkg-chapter-002-v2", chain.PackageId);
        Assert.Equal("completed", chain.Status);
        Assert.Contains(chain.RevisionPlanIds, id => id == "revision-plan-002");
        Assert.Contains(chain.Steps, step => step.Key == "revision_plan" && step.EventId == "evt-revision-ready");
        Assert.Contains(chain.Steps, step => step.Key == "context" && step.EventId == "evt-context-rebuilt");
        Assert.Contains(chain.Steps, step => step.Key == "commit" && step.EventId == "evt-commit");
    }

    [Fact]
    public void BuildProductionChains_GroupsRevisionPlanByInvalidatedPackageRebuildLinkAcrossRuntimeRuns()
    {
        var now = DateTime.UtcNow;
        var events = new[]
        {
            new WorkflowProductionEventSummary(
                "evt-revision-ready",
                "run-plan",
                "chapter-002",
                string.Empty,
                "revision_plan_ready",
                "revision_plan",
                "ready_for_rebuild",
                "修订计划已准备重建第二章。",
                "RevisionPlan",
                "revision-plan-002",
                "{\"invalidatedPackageIds\":[\"pkg-chapter-002-v1\"]}",
                now.AddMinutes(-5).ToString("O")),
            new WorkflowProductionEventSummary(
                "evt-context-rebuilt",
                "run-produce",
                "chapter-002",
                "pkg-chapter-002-v2",
                "chapter_context_package_built",
                NovelAgentProductionStages.ContextPackage,
                "completed",
                "第二章生产包已重建。",
                "tianming_package",
                "pkg-chapter-002-v2",
                "{}",
                now.AddMinutes(-4).ToString("O"),
                new WorkflowProductionEvidenceSummary(
                    "chapter_context_package",
                    "completed",
                    "chapter-context-v1",
                    "agentic-tianming-v1",
                    0,
                    0,
                    0,
                    string.Empty,
                    string.Empty,
                    0,
                    string.Empty,
                    string.Empty,
                    0,
                    0,
                    Array.Empty<WorkflowKnowledgeBindingEvidence>(),
                    Array.Empty<WorkflowCreativeIntentEvidence>(),
                    Array.Empty<WorkflowAgentReviewCheckEvidence>(),
                    Array.Empty<WorkflowKnowledgeConstraintEvidence>())
                {
                    RebuildLinks = new[]
                    {
                        new WorkflowPackageRebuildLinkEvidence(
                            "pkg-chapter-002-v1",
                            "stale",
                            "pkg-chapter-002-v2",
                            "completed",
                            "chapter_context_package",
                            "chapter-002",
                            "run-produce")
                    }
                }),
            new WorkflowProductionEventSummary(
                "evt-commit",
                "run-produce",
                "chapter-002",
                "pkg-chapter-002-v2",
                "chapter_committed",
                NovelAgentProductionStages.ChapterCommit,
                "completed",
                "第二章已提交书城。",
                "chapter_version",
                "version-chapter-002-v2",
                "{}",
                now.AddMinutes(-2).ToString("O"))
        };

        var chain = Assert.Single(ProjectWorkflow.BuildProductionChains(events));

        Assert.Equal("chapter-002", chain.ChapterId);
        Assert.Equal("run-produce", chain.RuntimeRunId);
        Assert.Equal("pkg-chapter-002-v2", chain.PackageId);
        Assert.Contains(chain.RevisionPlanIds, id => id == "revision-plan-002");
        Assert.Contains(chain.Steps, step => step.Key == "revision_plan" && step.EventId == "evt-revision-ready");
        Assert.Contains(chain.Steps, step => step.Key == "context" && step.EventId == "evt-context-rebuilt");
        Assert.Contains(chain.Steps, step => step.Key == "commit" && step.EventId == "evt-commit");
    }

    [Fact]
    public void BuildProductionChains_ReadsSourceRevisionPlansFromEventDataWhenEvidenceIsMissing()
    {
        var now = DateTime.UtcNow;
        var events = new[]
        {
            new WorkflowProductionEventSummary(
                "evt-revision-ready",
                "run-plan",
                "chapter-002",
                string.Empty,
                "revision_plan_ready",
                "revision_plan",
                "ready_for_rebuild",
                "修订计划已准备重建第二章。",
                "RevisionPlan",
                "revision-plan-002",
                "{\"targetChapterId\":\"chapter-002\"}",
                now.AddMinutes(-5).ToString("O")),
            new WorkflowProductionEventSummary(
                "evt-context-rebuilt",
                "run-produce",
                "chapter-002",
                "pkg-chapter-002-v2",
                "chapter_context_package_built",
                NovelAgentProductionStages.ContextPackage,
                "completed",
                "第二章生产包已重建。",
                "tianming_package",
                "pkg-chapter-002-v2",
                """
                {
                  "sourceRevisionPlans": [
                    {
                      "revisionPlanId": "revision-plan-002",
                      "targetChapterId": "chapter-002",
                      "invalidatedPackageIdsJson": "[\"pkg-chapter-002-v1\"]"
                    }
                  ]
                }
                """,
                now.AddMinutes(-4).ToString("O")),
            new WorkflowProductionEventSummary(
                "evt-commit",
                "run-produce",
                "chapter-002",
                "pkg-chapter-002-v2",
                "chapter_committed",
                NovelAgentProductionStages.ChapterCommit,
                "completed",
                "第二章已提交书城。",
                "chapter_version",
                "version-chapter-002-v2",
                "{}",
                now.AddMinutes(-2).ToString("O"))
        };

        var chain = Assert.Single(ProjectWorkflow.BuildProductionChains(events));

        Assert.Equal("chapter-002", chain.ChapterId);
        Assert.Equal("run-produce", chain.RuntimeRunId);
        Assert.Equal("pkg-chapter-002-v2", chain.PackageId);
        Assert.Contains(chain.RevisionPlanIds, id => id == "revision-plan-002");
        Assert.Contains(chain.Steps, step => step.Key == "revision_plan" && step.EventId == "evt-revision-ready");
        Assert.Contains(chain.Steps, step => step.Key == "context" && step.EventId == "evt-context-rebuilt");
        Assert.Contains(chain.Steps, step => step.Key == "commit" && step.EventId == "evt-commit");
    }

    [Fact]
    public void BuildProductionChains_UsesRevisionPlanChainWhenPlanEventHasNoChapterId()
    {
        var now = DateTime.UtcNow;
        var events = new[]
        {
            new WorkflowProductionEventSummary(
                "evt-revision-ready",
                "run-plan",
                string.Empty,
                string.Empty,
                "revision_plan_ready",
                "revision_plan",
                "ready_for_rebuild",
                "修订计划已准备重建第二章。",
                "RevisionPlan",
                "revision-plan-002",
                "{\"targetChapterId\":\"chapter-002\"}",
                now.AddMinutes(-5).ToString("O")),
            new WorkflowProductionEventSummary(
                "evt-context-rebuilt",
                "run-produce",
                "chapter-002",
                "pkg-chapter-002-v2",
                "chapter_context_package_built",
                NovelAgentProductionStages.ContextPackage,
                "completed",
                "第二章生产包已重建。",
                "tianming_package",
                "pkg-chapter-002-v2",
                "{}",
                now.AddMinutes(-4).ToString("O"),
                new WorkflowProductionEvidenceSummary(
                    "chapter_context_package",
                    "completed",
                    "chapter-context-v1",
                    "agentic-tianming-v1",
                    0,
                    0,
                    0,
                    string.Empty,
                    string.Empty,
                    0,
                    string.Empty,
                    string.Empty,
                    0,
                    0,
                    Array.Empty<WorkflowKnowledgeBindingEvidence>(),
                    Array.Empty<WorkflowCreativeIntentEvidence>(),
                    Array.Empty<WorkflowAgentReviewCheckEvidence>(),
                    Array.Empty<WorkflowKnowledgeConstraintEvidence>())
                {
                    SourceRevisionPlans = new[]
                    {
                        new WorkflowRevisionPlanEvidence(
                            "revision-plan-002",
                            "chapter_rewrite",
                            "chapter",
                            "chapter-002",
                            "chapter-002",
                            "第二章",
                            "ready_for_rebuild",
                            new[] { "chapter-002" },
                            new[] { "pkg-chapter-002-v1" },
                            "medium",
                            "按用户要求重写第二章。")
                    }
                }),
            new WorkflowProductionEventSummary(
                "evt-commit",
                "run-produce",
                "chapter-002",
                "pkg-chapter-002-v2",
                "chapter_committed",
                NovelAgentProductionStages.ChapterCommit,
                "completed",
                "第二章已提交书城。",
                "chapter_version",
                "version-chapter-002-v2",
                "{}",
                now.AddMinutes(-2).ToString("O"))
        };

        var chain = Assert.Single(ProjectWorkflow.BuildProductionChains(events));

        Assert.Equal("chapter-002", chain.ChapterId);
        Assert.Contains(chain.Steps, step => step.Key == "revision_plan" && step.EventId == "evt-revision-ready");
        Assert.Contains(chain.Steps, step => step.Key == "context" && step.EventId == "evt-context-rebuilt");
        Assert.Contains(chain.Steps, step => step.Key == "commit" && step.EventId == "evt-commit");
    }

    [Fact]
    public void BuildProductionChains_GroupsRevisionPlanByTargetChapterWhenProductionEventLacksPlanEvidence()
    {
        var now = DateTime.UtcNow;
        var events = new[]
        {
            new WorkflowProductionEventSummary(
                "evt-revision-ready",
                "run-plan",
                string.Empty,
                string.Empty,
                "revision_plan_ready",
                "revision_plan",
                "ready_for_rebuild",
                "修订计划已准备重建第二章。",
                "RevisionPlan",
                "revision-plan-002",
                "{\"targetChapterId\":\"chapter-002\",\"affectedChapterIds\":[\"chapter-002\"]}",
                now.AddMinutes(-5).ToString("O")),
            new WorkflowProductionEventSummary(
                "evt-context-rebuilt",
                "run-produce",
                "chapter-002",
                "pkg-chapter-002-v2",
                "chapter_context_package_built",
                NovelAgentProductionStages.ContextPackage,
                "completed",
                "第二章生产包已重建。",
                "tianming_package",
                "pkg-chapter-002-v2",
                "{}",
                now.AddMinutes(-4).ToString("O")),
            new WorkflowProductionEventSummary(
                "evt-commit",
                "run-produce",
                "chapter-002",
                "pkg-chapter-002-v2",
                "chapter_committed",
                NovelAgentProductionStages.ChapterCommit,
                "completed",
                "第二章已提交书城。",
                "chapter_version",
                "version-chapter-002-v2",
                "{}",
                now.AddMinutes(-2).ToString("O"))
        };

        var chain = Assert.Single(ProjectWorkflow.BuildProductionChains(events));

        Assert.Equal("chapter-002", chain.ChapterId);
        Assert.Equal("run-produce", chain.RuntimeRunId);
        Assert.Equal("pkg-chapter-002-v2", chain.PackageId);
        Assert.Contains(chain.RevisionPlanIds, id => id == "revision-plan-002");
        Assert.Contains(chain.Steps, step => step.Key == "revision_plan" && step.EventId == "evt-revision-ready");
        Assert.Contains(chain.Steps, step => step.Key == "context" && step.EventId == "evt-context-rebuilt");
        Assert.Contains(chain.Steps, step => step.Key == "commit" && step.EventId == "evt-commit");
    }

    [Fact]
    public void BuildProductionChains_GroupsRevisionPlanByTargetChapterDisplayNameAcrossRuntimeRuns()
    {
        var now = DateTime.UtcNow;
        var events = new[]
        {
            new WorkflowProductionEventSummary(
                "evt-revision-ready",
                "run-plan",
                string.Empty,
                string.Empty,
                "revision_plan_ready",
                "revision_plan",
                "ready_for_rebuild",
                "修订计划已准备重建第二章。",
                "RevisionPlan",
                "revision-plan-002",
                "{\"targetChapterDisplayName\":\"第二章\"}",
                now.AddMinutes(-5).ToString("O")),
            new WorkflowProductionEventSummary(
                "evt-context-rebuilt",
                "run-produce",
                "project-1-chapter-002",
                "pkg-project-1-chapter-002-v2",
                "chapter_context_package_built",
                NovelAgentProductionStages.ContextPackage,
                "completed",
                "第二章生产包已重建。",
                "tianming_package",
                "pkg-project-1-chapter-002-v2",
                "{}",
                now.AddMinutes(-4).ToString("O")),
            new WorkflowProductionEventSummary(
                "evt-commit",
                "run-produce",
                "project-1-chapter-002",
                "pkg-project-1-chapter-002-v2",
                "chapter_committed",
                NovelAgentProductionStages.ChapterCommit,
                "completed",
                "第二章已提交书城。",
                "chapter_version",
                "version-project-1-chapter-002-v2",
                "{}",
                now.AddMinutes(-2).ToString("O"))
        };

        var chain = Assert.Single(ProjectWorkflow.BuildProductionChains(events));

        Assert.Equal("project-1-chapter-002", chain.ChapterId);
        Assert.Equal("run-produce", chain.RuntimeRunId);
        Assert.Equal("pkg-project-1-chapter-002-v2", chain.PackageId);
        Assert.Contains(chain.RevisionPlanIds, id => id == "revision-plan-002");
        Assert.Contains(chain.Steps, step => step.Key == "revision_plan" && step.EventId == "evt-revision-ready");
        Assert.Contains(chain.Steps, step => step.Key == "context" && step.EventId == "evt-context-rebuilt");
        Assert.Contains(chain.Steps, step => step.Key == "commit" && step.EventId == "evt-commit");
    }

    [Fact]
    public void BuildProductionChains_MarksRecoveredCommittedChainAsCompleted()
    {
        var now = DateTime.UtcNow;
        var events = new[]
        {
            new WorkflowProductionEventSummary(
                "evt-context",
                "run-008",
                "project-1-chapter-008",
                "pkg-008",
                "chapter_context_package_built",
                NovelAgentProductionStages.ContextPackage,
                "completed",
                "生产包已构建。",
                "tianming_package",
                "pkg-008",
                "{}",
                now.AddMinutes(-5).ToString("O")),
            new WorkflowProductionEventSummary(
                "evt-gate-failed",
                "run-008",
                "project-1-chapter-008",
                "pkg-008",
                "chapter_gate_validated",
                NovelAgentProductionStages.GateValidation,
                "failed",
                "第一次门禁未通过。",
                "gate_report",
                "gate-008",
                "{}",
                now.AddMinutes(-4).ToString("O")),
            new WorkflowProductionEventSummary(
                "evt-commit",
                "run-008",
                "project-1-chapter-008",
                "pkg-008",
                "chapter_committed",
                NovelAgentProductionStages.ChapterCommit,
                "completed",
                "修订后已提交书城。",
                "chapter_version",
                "version-008",
                "{}",
                now.AddMinutes(-2).ToString("O"),
                new WorkflowProductionEvidenceSummary(
                    "chapter_context_package",
                    "completed",
                    "chapter-context-v1",
                    "agentic-tianming-v1",
                    0,
                    0,
                    1,
                    "chapter_commit",
                    "fact-008",
                    1,
                    "version-008",
                    "committed",
                    6148,
                    0,
                    Array.Empty<WorkflowKnowledgeBindingEvidence>(),
                    Array.Empty<WorkflowCreativeIntentEvidence>(),
                    Array.Empty<WorkflowAgentReviewCheckEvidence>(),
                    Array.Empty<WorkflowKnowledgeConstraintEvidence>())),
            new WorkflowProductionEventSummary(
                "evt-outbox-failed",
                "run-008",
                "project-1-chapter-008",
                "pkg-008",
                "outbox_failed",
                "index_outbox",
                "retryable_failed",
                "后台索引第一次失败。",
                "outbox_event",
                "outbox-008",
                "{}",
                now.AddMinutes(-1).ToString("O")),
            new WorkflowProductionEventSummary(
                "evt-outbox-completed",
                "run-008",
                "project-1-chapter-008",
                "pkg-008",
                "outbox_completed",
                "index_outbox",
                "completed",
                "后台索引重试完成。",
                "outbox_event",
                "outbox-008",
                "{}",
                now.ToString("O"))
        };

        var chain = Assert.Single(ProjectWorkflow.BuildProductionChains(events));

        Assert.Equal("completed", chain.Status);
        Assert.Contains(chain.Steps, step => step.Status == "failed");
        Assert.Contains(chain.Steps, step => step.Status == "retryable_failed");
        Assert.Contains(chain.Steps, step => step.Status == "completed");
    }

    [Fact]
    public void NovelLibrary_DoesNotShowCommittedDraftWithoutQualityReview()
    {
        var method = typeof(NovelLibrary).GetMethod("IsLibraryVisible", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var run = new NovelAgentRun
        {
            GateReport = new GenerationGateReport { Status = "validated" },
            DraftArtifact = new ChapterDraftArtifact
            {
                Status = "committed",
                CommittedContent = "正文已提交但缺少质量评审"
            }
        };

        var visible = Assert.IsType<bool>(method!.Invoke(null, new object?[] { run }));

        Assert.False(visible);
    }

    [Fact]
    public void WebReviewer_UsesDraftContentWhenChapterDocumentIsNotCommittedYet()
    {
        var reviewerType = typeof(NovelAgentRun).Assembly.GetType(
            "TM.Services.Framework.AI.NovelAgent.Services.ChapterPostGenerationReviewer");
        Assert.NotNull(reviewerType);

        var method = reviewerType!.GetMethod("ResolveReviewContent", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);

        var draft = "第一章正文\n主角启动深海机甲。\n<chapter_changes>{\"facts\":[]}</chapter_changes>";
        var content = Assert.IsType<string>(method!.Invoke(null, new object?[] { "", "", draft }));

        Assert.Contains("主角启动深海机甲", content);
        Assert.DoesNotContain("CHANGES", content);
    }

    [Fact]
    public void BuildDatabaseVolumes_DeduplicatesLegacyArtifactWhenCommittedChapterUsesProjectScopedId()
    {
        var method = typeof(WorkflowService).GetMethod(
            "BuildDatabaseVolumes",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var volumeArc = new VolumeArc
        {
            Id = "arc-001",
            UserId = "user-001",
            ProjectId = "project-001",
            VolumeNumber = 1,
            VolumeTitle = "第一卷",
            TargetChapters = 6,
            CurrentChapters = 0,
            Status = "planned",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var committedChapter = new Chapter
        {
            Id = "project-001-chapter-001",
            ProjectId = "project-001",
            VolumeId = "volume-project-001",
            Title = "第一章：银蓝邮徽",
            ChapterNumber = 1,
            WordCount = 3200,
            Status = "committed",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var legacyArtifact = new WorkflowChapterArtifactSummary(
            "chapter-001",
            "run-001",
            "PlanChapter",
            "Completed",
            DateTime.UtcNow.ToString("O"),
            "旧工作流草稿",
            "committed",
            "# 第一章 旧草稿",
            true,
            "validated",
            Array.Empty<string>(),
            Array.Empty<string>(),
            "Pass",
            88,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            0,
            Array.Empty<string>(),
            1,
            true);

        var volumes = Assert.IsAssignableFrom<IEnumerable<NovelVolumeView>>(
            method!.Invoke(null, new object?[]
            {
                Array.Empty<Volume>(),
                new[] { volumeArc },
                new[] { committedChapter },
                new[] { legacyArtifact },
                new Dictionary<string, string>(),
                Array.Empty<WorkflowProductionChain>(),
                Array.Empty<WorkflowCreativeIntentEvidence>(),
                Array.Empty<WorkflowChapterRevisionPlanSummary>()
            })!);
        var result = volumes.ToList();

        var volume = Assert.Single(result);
        Assert.Equal(volumeArc.Id, volume.VolumeId);
        var chapter = Assert.Single(volume.Chapters);
        Assert.Equal(committedChapter.Id, chapter.ChapterId);
        Assert.Equal(volumeArc.Id, chapter.VolumeId);
        Assert.Equal(3200, chapter.WordCount);
        Assert.DoesNotContain(result.SelectMany(v => v.Chapters), c => c.ChapterId == legacyArtifact.ChapterId);
    }

    private static NovelLibraryDocument BuildLibrary(NovelChapterView chapter)
    {
        var volume = new NovelVolumeView(
            "volume-001",
            "第一卷",
            "Draft",
            chapter.ChapterId,
            chapter.ChapterId,
            6,
            new[] { chapter });
        var book = new NovelBookView(
            "project-001",
            "测试小说",
            "科幻",
            "机甲",
            "过程草稿不等于书城入库",
            string.Empty,
            "Drafting",
            true,
            1,
            1,
            6,
            0,
            DateTime.UtcNow.ToString("O"),
            chapter);

        return new NovelLibraryDocument(
            new[] { book },
            book,
            new[] { volume },
            chapter,
            1,
            6,
            0);
    }

    private static NovelChapterView Chapter(bool visibleInWorkflow, bool visibleInLibrary, bool hasGeneratedContent) =>
        new(
            "chapter-001",
            "目标推进",
            "volume-001",
            "第一卷",
            1,
            "开局",
            "建立困境",
            "发现机会",
            "付出代价",
            "validated",
            "run-001",
            "GenerateChapterDraft",
            DateTime.UtcNow.ToString("O"),
            hasGeneratedContent,
            false,
            0,
            "草稿已生成。",
            "草稿预览",
            "目标推进",
            0,
            0,
            Array.Empty<string>(),
            Array.Empty<string>(),
            "validated",
            string.Empty,
            "draft_generated",
            "validated",
            true,
            true,
            true,
            false,
            0,
            0,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            visibleInWorkflow,
            visibleInLibrary,
            "硬门禁已通过",
            "validated",
            "draft:run-001",
            "gate:run-001",
            string.Empty);
}
