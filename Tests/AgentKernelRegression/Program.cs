using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Diagnostics;
using TM.Services.Framework.AI.Embedding;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Services.AgentTools;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using TM.Web.NovelAgentWeb.Support;
using DbKnowledgeBase = TM.Web.NovelAgentWeb.Data.Entities.KnowledgeBase;
using DbNovelProject = TM.Web.NovelAgentWeb.Data.Entities.NovelProject;
using DbUser = TM.Web.NovelAgentWeb.Data.Entities.User;

namespace TM.Tests.AgentKernelRegression;

internal static class Program
{
    private static readonly List<(string Name, Func<Task> Test)> Tests = new()
    {
        ("ConversationKernel leaves natural status wording to model planner", ConversationKernelLeavesNaturalStatusWordingToModelPlanner),
        ("ToolPolicy blocks raw userGoal PlanChapter", ToolPolicyBlocksRawUserGoalPlanChapter),
        ("ToolPolicy blocks PlanChapter missing creative directions", ToolPolicyBlocksPlanChapterMissingCreativeDirections),
        ("ToolPolicy blocks PlanVolumeArc missing creative directions", ToolPolicyBlocksPlanVolumeArcMissingCreativeDirections),
        ("ToolPolicy preflights confirmed story foundation commit", ToolPolicyPreflightsConfirmedStoryFoundationCommit),
        ("ToolPolicy delegates chapter quality handling to ProduceChapter", ToolPolicyBlocksCommitWithQualityIssues),
        ("Scheduler continue uses active blackboard task", SchedulerContinueUsesActiveBlackboardTask),
        ("Scheduler continue uses registered repairable production observations", SchedulerContinueUsesRepairablePolicyObservation),
        ("Recovery keeps ProduceChapter gate failures recoverable", RecoveryKeepsRepairDraftGateFailuresRecoverable),
        ("ToolPolicy allows ProduceChapter to own missing context recovery", ToolPolicyAllowsDraftGenerationToBuildMissingContext),
        ("ToolPolicy rejects obsolete validate stage", ToolPolicyRepairsMissingDraftBeforeValidation),
        ("ToolPolicy keeps hard boundaries terminal", ToolPolicyKeepsHardBoundariesTerminal),
        ("ToolPolicy allows registered tools without discovery gate", ToolPolicyAllowsRegisteredToolsWithoutDiscoveryGate),
        ("ToolPolicy lets ProduceChapter handle context rebuild requirements", ToolPolicyBlocksDraftWhenContextRebuildRequired),
        ("ToolPolicy rejects obsolete commit stage", ToolPolicyBlocksCommitWhenRevalidationRequired),
        ("ToolPolicy rejects obsolete repair stage", ToolPolicyRepairsPrematureDraftRepairToValidation),
        ("Mission blackboard recovery rebuilds scheduler state", MissionBlackboardRecoveryRebuildsSchedulerState),
        ("Mission blackboard exposes drafts before library commit", MissionBlackboardExposesDraftsBeforeLibraryCommit),
        ("New project reflection does not locally continue foundation planning", NewProjectReflectionDoesNotLocallyContinueFoundationPlanning),
        ("Planner reflection timeout returns bounded non-decisive fallback", PlannerReflectionTimeoutReturnsBoundedNonDecisiveFallback),
        ("Story foundation candidates continue with structured commit action", StoryFoundationCandidatesContinueWithStructuredCommitAction),
        ("Runtime continues process tools with structured next hints", RuntimeContinuesProcessToolsWithStructuredNextHints),
        ("Runtime continues after committed chapter when book tasks remain", RuntimeContinuesAfterCommittedChapterWhenBookTasksRemain),
        ("Runtime governance observations do not leak guard text", RuntimeGovernanceObservationDoesNotLeakGuardText),
        ("Runtime governance observations do not leak tool names", RuntimeGovernanceObservationDoesNotLeakToolNames),
        ("Tool execution snapshots expose user-visible progress", ToolExecutionSnapshotsExposeUserVisibleProgress),
        ("Runtime returns user-facing chat replies before no-action fallback", RuntimeReturnsUserFacingChatRepliesBeforeNoActionFallback),
        ("Runtime does not expose no-tool retry loop", RuntimeDoesNotExposeNoToolRetryLoop),
        ("Foreground does not finish creative work after tool search", ForegroundDoesNotFinishCreativeWorkAfterToolSearch),
        ("Foreground defers creative readable tools to background", ForegroundDefersCreativeReadableToolsToBackground),
        ("Planner missing LLM settings returns explicit degraded no-tool reply", PlannerMissingLlmSettingsReturnsExplicitDegradedNoToolReply),
        ("Planner missing LLM settings does not invent status results", PlannerMissingLlmSettingsDoesNotInventStatusResults),
        ("ConversationKernel builds stable user turn envelopes", ConversationKernelBuildsUserTurnEnvelope),
        ("ConversationKernel leaves new novel requests as raw planner input", ConversationKernelLeavesNewNovelRequestsAsRawPlannerInput),
        ("ConversationKernel leaves production requests as raw planner input", ConversationKernelLeavesProductionRequestsAsRawPlannerInput),
        ("ConversationKernel does not keyword-route continuation", ConversationKernelDoesNotKeywordRouteContinuation),
        ("ConversationKernel models numeric candidate selection", ConversationKernelModelsCandidateSelection),
        ("Candidate selection confirmation reply is user-visible", CandidateSelectionConfirmationReplyIsUserVisible),
        ("Runtime normalizes story foundation candidate selection", RuntimeNormalizesStoryFoundationCandidateSelection),
        ("Native tool calls still pass through ToolPolicy", NativeToolCallsPassThroughToolPolicy),
        ("Provider tool calling diagnostics parse mock responses", ProviderToolCallingDiagnosticsParseMockResponses),
        ("Hardcore writing engine builds Anthropic messages URL like tool client", HardcoreWritingEngineBuildsAnthropicMessagesUrlLikeToolClient),
        ("Hardcore writing engine normalizes provider model suffixes like tool client", HardcoreWritingEngineNormalizesProviderModelSuffixesLikeToolClient),
        ("Hardcore writing engine reserves enough output tokens for CHANGES", HardcoreWritingEngineReservesEnoughOutputTokensForChanges),
        ("Quality review suite blocks weak chapter quality", QualityReviewSuiteBlocksWeakChapterQuality),
        ("Tool registry exposes provider tool schemas", ToolRegistryExposesToolSchemas),
        ("Tool registry semantic search treats phase as hint", ToolRegistrySemanticSearchTreatsPhaseAsHint),
        ("Tool registry declares side effects for every tool", ToolRegistryDeclaresSideEffectsForEveryTool),
        ("Tool registry scoped workspace overrides stale ambient workspace", ToolRegistryScopedWorkspaceOverridesStaleAmbientWorkspace),
        ("SearchCreativeKnowledge returns DB-created knowledge", SearchCreativeKnowledgeReturnsDbKnowledge),
        ("SearchCreativeKnowledge returns project material chunks", SearchCreativeKnowledgeReturnsProjectMaterialChunks),
        ("Knowledge usage remains project-scoped", KnowledgeUsageRemainsProjectScoped),
        ("ResolveNovelProject is idempotent while awaiting foundation", ResolveNovelProjectIsIdempotentWhileAwaitingFoundation),
        ("ResolveNovelProject create_new does not bind active old project", ResolveNovelProjectCreateNewDoesNotBindActiveOldProject),
        ("ResolveNovelProject create_new honors projectTitle as new title", ResolveNovelProjectCreateNewHonorsProjectTitle),
        ("ResolveNovelProject foundation readiness requires explicit model flag", ResolveNovelProjectFoundationReadinessRequiresExplicitModelFlag),
    };

    public static async Task<int> Main()
    {
        var failed = 0;
        Console.WriteLine("Agent kernel regression suite");
        Console.WriteLine("=============================");

        foreach (var (name, test) in Tests)
        {
            try
            {
                await test();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"FAIL {name}");
                Console.WriteLine($"     {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? $"All {Tests.Count} Agent kernel checks passed."
            : $"{failed} of {Tests.Count} Agent kernel checks failed.");

        return failed == 0 ? 0 : 1;
    }

    private static Task ConversationKernelLeavesNaturalStatusWordingToModelPlanner()
    {
        var session = new AgentSession();
        var kernel = new ConversationKernel();
        var intent = kernel.Classify(session, "你好，我叫你生成的小说章节准备好了吗");

        Check.Equal(TurnIntentType.FreeChat, intent.Type,
            "ConversationKernel must not keyword-route natural status wording; the LLM planner decides whether to query state.");
        Check.Equal("free_chat", intent.Label,
            "Natural language should remain a model-visible chat turn unless it is a structural candidate selection.");
        Check.True(string.IsNullOrWhiteSpace(intent.CreativeBrief),
            "ConversationKernel should not manufacture creative briefs from natural status wording.");

        return Task.CompletedTask;
    }

    private static Task ToolPolicyBlocksRawUserGoalPlanChapter()
    {
        var policy = new ToolPolicyEngine();
        var session = new AgentSession();
        var bible = new StoryBibleDocument();
        var context = new AgentObservationContext
        {
            AvailableTools =
            {
                new AgentToolDefinition { Name = "PlanChapter" },
                new AgentToolDefinition { Name = "QueryProjectStatus" },
            },
            TurnIntent = new TurnIntent
            {
                Type = TurnIntentType.FreeChat,
                Label = "status_query",
                RawMessage = "我叫你生成的章节准备好了吗"
            },
            MissionPlan = new AgentMissionPlan(),
        };
        var call = new AgentToolCall
        {
            Name = "PlanChapter",
            Arguments =
            {
                ["userGoal"] = "我叫你生成的章节准备好了吗"
            }
        };

        var result = policy.BeforeCall(call, session, bible, context, confirmed: false);

        Check.True(!result.AllowsExecution,
            "PlanChapter with deprecated raw userGoal must not execute.");
        Check.True(result.ReplacementAction?.ToolCall?.Name == "QueryProjectStatus" ||
                   result.Message.Contains("状态查询", StringComparison.OrdinalIgnoreCase) ||
                   result.Message.Contains("userGoal", StringComparison.OrdinalIgnoreCase),
            "Blocked raw status/userGoal input should be routed to status or explain the hard block.");

        return Task.CompletedTask;
    }

    private static Task ToolPolicyBlocksPlanChapterMissingCreativeDirections()
    {
        var policy = new ToolPolicyEngine();
        var session = new AgentSession
        {
            WorkingMemory = new AgentWorkingMemory
            {
                MissionPlan = new AgentMissionPlan
                {
                    SchedulerState = new AgentTaskSchedulerState
                    {
                        ActiveChapterId = "chapter-001"
                    }
                }
            }
        };
        var bible = new StoryBibleDocument
        {
            Constitution = new StoryCreativeConstitution
            {
                Genre = "深海废土",
                SubGenre = "机甲打怪升级爽文",
                CoreHook = "沉船修理工修复旧式潜航机甲，猎杀深海异变体升级，探索失落海城。",
                ReaderPromise = "一路打怪、改装机甲、解锁海域并碾压更强敌人。",
                MainPleasure = "战斗升级和装备改造爽感",
                WorldCoreRule = "深海异变体核心可驱动机甲进化。"
            },
            VolumeArcs =
            {
                new VolumeArcPlan
                {
                    VolumeId = "volume-001",
                    Title = "沉船区崛起",
                    StartChapterId = "chapter-001",
                    EndChapterId = "chapter-012",
                    VolumePromise = "主角从贫民区修理工成长为沉船区最强机甲猎手。",
                    CoreQuestion = "旧式潜航机甲能否帮助他夺回生存权？"
                }
            }
        };
        var context = new AgentObservationContext
        {
            UserMessage = "把第一个方向作为正式故事地基，然后推进第一卷、前三章和第一章草稿。",
            MissionPlan = session.WorkingMemory.MissionPlan,
        };
        var call = new AgentToolCall { Name = "PlanChapter" };

        var result = policy.BeforeCall(call, session, bible, context, confirmed: false);

        Check.True(!result.AllowsExecution && !result.IsRepairable,
            "PlanChapter without creativeBrief should be blocked, not repaired by Runtime.");
        Check.True(result.ReplacementAction == null,
            "Runtime policy must not generate PlanChapter business arguments.");
        Check.Contains("creativeBrief", result.Message,
            "Block message should tell the model which structured argument is missing.");

        var missingDirections = new AgentToolCall
        {
            Name = "PlanChapter",
            Arguments =
            {
                ["creativeBrief"] = "chapter-001：沉船修理工修复旧式潜航机甲，猎杀深海异变体升级。"
            }
        };
        var directionResult = policy.BeforeCall(missingDirections, session, bible, context, confirmed: false);
        Check.True(!directionResult.AllowsExecution && !directionResult.IsRepairable,
            "PlanChapter without candidateDirections should be blocked, not inferred from creativeBrief.");
        Check.True(directionResult.ReplacementAction == null,
            "Runtime policy must not infer candidateDirections.");
        Check.Contains("candidateDirections", directionResult.Message,
            "Block message should tell the model that structured candidate directions are required.");

        return Task.CompletedTask;
    }

    private static Task ToolPolicyBlocksPlanVolumeArcMissingCreativeDirections()
    {
        var policy = new ToolPolicyEngine();
        var session = new AgentSession();
        var bible = new StoryBibleDocument
        {
            Constitution = new StoryCreativeConstitution
            {
                Genre = "废土邮差冒险",
                ReaderPromise = "邮路探索和硬事实连续性"
            }
        };
        var context = new AgentObservationContext
        {
            UserMessage = "继续规划第一卷",
            MissionPlan = session.WorkingMemory.MissionPlan,
        };

        var rawUserGoal = new AgentToolCall
        {
            Name = "PlanVolumeArc",
            Arguments = { ["userGoal"] = "规划第一卷" }
        };
        var rawResult = policy.BeforeCall(rawUserGoal, session, bible, context, confirmed: false);
        Check.True(!rawResult.AllowsExecution && !rawResult.IsRepairable,
            "PlanVolumeArc with deprecated raw userGoal must not execute.");
        Check.True(rawResult.ReplacementAction == null,
            "Runtime policy must not rewrite raw userGoal into creativeBrief.");

        var missingBrief = new AgentToolCall { Name = "PlanVolumeArc" };
        var briefResult = policy.BeforeCall(missingBrief, session, bible, context, confirmed: false);
        Check.True(!briefResult.AllowsExecution && !briefResult.IsRepairable,
            "PlanVolumeArc without creativeBrief should be blocked, not repaired by Runtime.");
        Check.True(briefResult.ReplacementAction == null,
            "Runtime policy must not generate PlanVolumeArc business arguments.");
        Check.Contains("creativeBrief", briefResult.Message,
            "Block message should tell the model which structured volume argument is missing.");

        var missingDirections = new AgentToolCall
        {
            Name = "PlanVolumeArc",
            Arguments = { ["creativeBrief"] = "第一卷建立废墟邮线，并制造旧邮路危机升级。" }
        };
        var directionResult = policy.BeforeCall(missingDirections, session, bible, context, confirmed: false);
        Check.True(!directionResult.AllowsExecution && !directionResult.IsRepairable,
            "PlanVolumeArc without candidateDirections should be blocked, not inferred from creativeBrief.");
        Check.True(directionResult.ReplacementAction == null,
            "Runtime policy must not infer volume candidateDirections.");
        Check.Contains("candidateDirections", directionResult.Message,
            "Block message should tell the model that structured volume directions are required.");

        return Task.CompletedTask;
    }

    private static Task ToolPolicyPreflightsConfirmedStoryFoundationCommit()
    {
        const string runId = "run-foundation-001";
        var policy = new ToolPolicyEngine();
        var session = new AgentSession { ActiveRunId = runId };
        var context = new AgentObservationContext
        {
            TurnIntent = new TurnIntent { Type = TurnIntentType.FreeChat, Label = "confirmation" },
            MissionPlan = new AgentMissionPlan()
        };
        var call = new AgentToolCall
        {
            Name = "CommitStoryFoundation",
            Arguments =
            {
                ["runId"] = runId,
                ["selectedMacroCandidateIndex"] = "1"
            }
        };

        var missingRun = policy.BeforeCall(call, session, new StoryBibleDocument(), context, confirmed: true);
        Check.True(!missingRun.AllowsExecution,
            "Confirmed CommitStoryFoundation must not execute when the run is absent from the scoped Story Bible.");
        Check.Contains("当前项目 Story Bible", missingRun.Message,
            "Missing run block should explain this is a project/run scope mismatch.");

        var missingConstitution = policy.BeforeCall(
            call,
            session,
            new StoryBibleDocument
            {
                AgentRuns =
                {
                    new NovelAgentRun
                    {
                        RunId = runId,
                        Intent = NovelAgentIntent.CreateStoryFoundation
                    }
                }
            },
            context,
            confirmed: true);
        Check.True(!missingConstitution.AllowsExecution,
            "Confirmed CommitStoryFoundation must not execute before the run has a StoryConstitution.");
        Check.Contains("创意宪法", missingConstitution.Message,
            "Missing constitution block should name the commit prerequisite.");

        var allowed = policy.BeforeCall(
            call,
            session,
            new StoryBibleDocument
            {
                AgentRuns =
                {
                    new NovelAgentRun
                    {
                        RunId = runId,
                        Intent = NovelAgentIntent.CreateStoryFoundation,
                        StoryConstitution = new StoryCreativeConstitution
                        {
                            Genre = "玄幻",
                            CoreHook = "规则会反噬使用者"
                        },
                        MacroCandidates =
                        {
                            new MacroStoryConceptCandidate { CandidateId = "macro-001-rule-backlash", Title = "规则反噬型" }
                        }
                    }
                }
            },
            context,
            confirmed: true);
        Check.True(allowed.AllowsExecution && !allowed.RequiresConfirmation,
            "Confirmed CommitStoryFoundation should execute when the scoped run is commit-ready.");

        var indexSelection = new AgentToolCall
        {
            Name = "CommitStoryFoundation",
            Arguments =
            {
                ["runId"] = runId,
                ["selectedMacroCandidateIndex"] = "1"
            }
        };
        var indexAllowed = policy.BeforeCall(
            indexSelection,
            session,
            new StoryBibleDocument
            {
                AgentRuns =
                {
                    new NovelAgentRun
                    {
                        RunId = runId,
                        Intent = NovelAgentIntent.CreateStoryFoundation,
                        StoryConstitution = new StoryCreativeConstitution { Genre = "玄幻", CoreHook = "规则会反噬使用者" },
                        MacroCandidates =
                        {
                            new MacroStoryConceptCandidate { CandidateId = "macro-001-rule-backlash", Title = "规则反噬型" }
                        }
                    }
                }
            },
            context,
            confirmed: true);
        Check.True(indexAllowed.AllowsExecution,
            "Valid candidateIndex should authorize the selected story foundation candidate.");
        Check.Equal("macro-001-rule-backlash", indexSelection.Arguments["selectedMacroCandidateId"],
            "Policy should persist the canonical candidateId when index is valid.");

        var zeroBasedIndex = new AgentToolCall
        {
            Name = "CommitStoryFoundation",
            Arguments =
            {
                ["runId"] = runId,
                ["selectedMacroCandidateIndex"] = "0"
            }
        };
        var zeroBasedAllowed = policy.BeforeCall(
            zeroBasedIndex,
            session,
            new StoryBibleDocument
            {
                AgentRuns =
                {
                    new NovelAgentRun
                    {
                        RunId = runId,
                        Intent = NovelAgentIntent.CreateStoryFoundation,
                        StoryConstitution = new StoryCreativeConstitution { Genre = "玄幻", CoreHook = "规则会反噬使用者" },
                        MacroCandidates =
                        {
                            new MacroStoryConceptCandidate { CandidateId = "macro-001-rule-backlash", Title = "规则反噬型" }
                        }
                    }
                }
            },
            context,
            confirmed: true);
        Check.True(zeroBasedAllowed.AllowsExecution,
            "Zero-based candidate index should be normalized to the first story foundation candidate.");
        Check.Equal("macro-001-rule-backlash", zeroBasedIndex.Arguments["selectedMacroCandidateId"],
            "Policy should persist the canonical candidateId when zero-based index targets the first candidate.");

        var invalidIndex = new AgentToolCall
        {
            Name = "CommitStoryFoundation",
            Arguments =
            {
                ["runId"] = runId,
                ["selectedMacroCandidateIndex"] = "9"
            }
        };
        var invalidIndexResult = policy.BeforeCall(
            invalidIndex,
            session,
            new StoryBibleDocument
            {
                AgentRuns =
                {
                    new NovelAgentRun
                    {
                        RunId = runId,
                        Intent = NovelAgentIntent.CreateStoryFoundation,
                        StoryConstitution = new StoryCreativeConstitution { Genre = "玄幻", CoreHook = "规则会反噬使用者" },
                        MacroCandidates =
                        {
                            new MacroStoryConceptCandidate { CandidateId = "macro-001-rule-backlash", Title = "规则反噬型" }
                        }
                    }
                }
            },
            context,
            confirmed: true);
        Check.True(!invalidIndexResult.AllowsExecution,
            "Out-of-range candidateIndex must be blocked before commit.");
        Check.Contains("超出", invalidIndexResult.Message,
            "Out-of-range candidateIndex block should explain that the selection is invalid.");

        return Task.CompletedTask;
    }

    private static Task ToolPolicyBlocksCommitWithQualityIssues()
    {
        const string runId = "run-chapter-001";
        var policy = new ToolPolicyEngine();
        var session = new AgentSession
        {
            ActiveRunId = runId,
            WorkingMemory = new AgentWorkingMemory
            {
                MissionPlan = new AgentMissionPlan
                {
                    BookTaskTree = new AgentBookTaskTree
                    {
                        Volumes =
                        {
                            new AgentVolumeTask
                            {
                                Chapters =
                                {
                                    new AgentChapterTask
                                    {
                                        ChapterId = "chapter-001",
                                        RunId = runId,
                                        GateStatus = "validated",
                                        QualityIssueSummary = "角色动机不足，需要重写关键选择。",
                                        NextAction = "ProduceChapter"
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
        var bible = new StoryBibleDocument
        {
            AgentRuns =
            {
                new NovelAgentRun
                {
                    RunId = runId,
                    GateReport = new GenerationGateReport
                    {
                        Status = "validated"
                    }
                }
            }
        };
        var context = new AgentObservationContext
        {
            MissionPlan = session.WorkingMemory.MissionPlan,
            TurnIntent = new TurnIntent { Type = TurnIntentType.FreeChat, Label = "confirmation" },
        };
        var call = new AgentToolCall
        {
            Name = "ProduceChapter",
            Arguments = { ["runId"] = runId }
        };

        var result = policy.BeforeCall(call, session, bible, context, confirmed: true);

        Check.True(result.AllowsExecution,
            "ProduceChapter owns validation, repair, review and commit decisions inside the closed-loop production tool.");
        Check.True(!result.IsRepairable,
            "Policy must not synthesize old stage prerequisites for chapter quality issues.");

        return Task.CompletedTask;
    }

    private static Task SchedulerContinueUsesActiveBlackboardTask()
    {
        const string runId = "run-chapter-002";
        var scheduler = new AgentTaskScheduler();
        var context = new AgentObservationContext
        {
            TurnIntent = new TurnIntent { Type = TurnIntentType.FreeChat, Label = "continue_mission" },
            MissionPlan = new AgentMissionPlan
            {
                SchedulerState = new AgentTaskSchedulerState
                {
                    Tasks =
                    {
                        new AgentScheduledTask
                        {
                            TaskId = "task-context",
                            RunId = runId,
                            ChapterId = "chapter-002",
                            NextAction = "ProduceChapter",
                            TaskType = "ProduceChapter",
                            Status = "queued",
                            Risk = "Medium"
                        }
                    }
                }
            }
        };

        var action = scheduler.BuildContinueAction(context);

        Check.True(action?.ToolCall?.Name == "ProduceChapter",
            "Continue must use scheduler active task instead of keyword guessing.");
        Check.Equal(runId, action!.ToolCall!.Arguments["runId"],
            "Scheduler should carry the active task runId into tool arguments.");
        return Task.CompletedTask;
    }

    private static Task SchedulerContinueUsesRepairablePolicyObservation()
    {
        const string runId = "run-chapter-repair";
        var scheduler = new AgentTaskScheduler();
        var context = new AgentObservationContext
        {
            TurnIntent = new TurnIntent { Type = TurnIntentType.FreeChat, Label = "continue_mission" },
            MissionPlan = new AgentMissionPlan
            {
                SchedulerState = new AgentTaskSchedulerState
                {
                    Tasks =
                    {
                        new AgentScheduledTask
                        {
                            TaskId = "task-stale",
                            RunId = runId,
                            ChapterId = "chapter-repair",
                            NextAction = "ProduceChapter",
                            TaskType = "ProduceChapter",
                            Status = "queued",
                            Risk = "High",
                            RequiresConfirmation = true
                        }
                    }
                }
            },
            RecentObservations =
            {
                new AgentRuntimeObservation
                {
                    ObservationType = "policy_observation",
                    ToolName = "ProduceChapter",
                    Success = false,
                    IsRepairable = true,
                    RecommendedToolName = "ProduceChapter",
                    RecommendedArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["runId"] = runId
                    },
                    MissingPrerequisite = "chapter_closed_loop_retry",
                    RunId = runId,
                    CreatedAt = DateTime.UtcNow
                }
            }
        };

        var action = scheduler.BuildContinueAction(context);

        Check.Equal("ProduceChapter", action?.ToolCall?.Name ?? string.Empty,
            "Continue should use registered repairable production observations without mapping obsolete stage tools.");
        Check.Equal(runId, action!.ToolCall!.Arguments["runId"],
            "Repairable scheduler action should carry recommended runId.");
        Check.True(!action.RequiresConfirmation,
            "Medium-risk repair prerequisite should not become a confirmation request.");
        return Task.CompletedTask;
    }

    private static Task RecoveryKeepsRepairDraftGateFailuresRecoverable()
    {
        const string runId = "run-chapter-003";
        var recovery = new AgentRecoveryEngine(new object(), new object());

        var analysis = recovery.AnalyzeFailure(
            new AgentToolCall
            {
                Name = "ProduceChapter",
                Arguments =
                {
                    ["runId"] = runId,
                    ["commitPolicy"] = "auto_commit"
                }
            },
            new AgentToolExecutionResult
            {
                Success = false,
                Phase = "gate_failed",
                Message = "章节草稿修复后仍未通过硬门禁：核心连续性失败：未承接「逆潮夜临近，威胁增加」。"
            },
            new AgentSession { ActiveRunId = runId },
            new StoryBibleDocument());

        Check.True(analysis.IsRecoverable,
            "ProduceChapter gate failures should stay recoverable until the guardrail budget is exhausted.");
        Check.True(analysis.RecommendedChains.Count > 0,
            "Repairable closed-loop failures must expose a follow-up ProduceChapter chain.");
        var next = analysis.RecommendedChains[0].Steps[0].ToolCall;
        Check.Equal("ProduceChapter", next.Name,
            "The follow-up chain should retry the closed-loop production tool instead of exposing internal stages.");
        Check.Equal(runId, next.Arguments["runId"],
            "The follow-up production call should preserve the runId.");
        Check.Equal("auto_commit", next.Arguments["commitPolicy"],
            "The follow-up production call should preserve the commit policy.");

        return Task.CompletedTask;
    }

    private static Task ToolPolicyAllowsDraftGenerationToBuildMissingContext()
    {
        const string runId = "run-missing-context";
        var policy = new ToolPolicyEngine();
        var session = new AgentSession
        {
            ActiveRunId = runId,
            WorkingMemory = new AgentWorkingMemory
            {
                MissionPlan = BuildPlanWithChapter(runId, chapter =>
                {
                    chapter.Status = "candidate_selected";
                    chapter.ContextStatus = "pending";
                    chapter.NextAction = "ProduceChapter";
                })
            }
        };
        var bible = new StoryBibleDocument
        {
            AgentRuns =
            {
                new NovelAgentRun
                {
                    RunId = runId,
                    TargetChapterId = "chapter-missing-context",
                    ChapterBrief = new ChapterCreativeBrief
                    {
                        ChapterId = "chapter-missing-context",
                        SelectedCandidateTitle = "主角第一次付出规则代价"
                    }
                }
            }
        };
        var context = new AgentObservationContext
        {
            AvailableTools =
            {
                new AgentToolDefinition { Name = "ProduceChapter" },
            },
            MissionPlan = session.WorkingMemory.MissionPlan,
            TurnIntent = new TurnIntent { Type = TurnIntentType.FreeChat, Label = "continue_mission" },
        };

        var result = policy.BeforeCall(
            new AgentToolCall { Name = "ProduceChapter", Arguments = { ["runId"] = runId } },
            session,
            bible,
            context,
            confirmed: true);

        Check.True(result.AllowsExecution,
            "ProduceChapter owns context construction and should not be bounced to obsolete stage tools.");
        Check.True(!result.IsRepairable,
            "Missing context alone should not create a repair loop.");
        return Task.CompletedTask;
    }

    private static Task ToolPolicyRepairsMissingDraftBeforeValidation()
    {
        const string runId = "run-missing-draft";
        var policy = new ToolPolicyEngine();
        var session = new AgentSession
        {
            ActiveRunId = runId,
            WorkingMemory = new AgentWorkingMemory
            {
                MissionPlan = BuildPlanWithChapter(runId, chapter =>
                {
                    chapter.Status = "context_ready";
                    chapter.DraftStatus = "none";
                    chapter.NextAction = "ProduceChapter";
                })
            }
        };
        var bible = new StoryBibleDocument
        {
            AgentRuns =
            {
                new NovelAgentRun
                {
                    RunId = runId,
                    TargetChapterId = "chapter-missing-draft",
                    ContextPackage = new ChapterContextPackageSummary
                    {
                        ChapterId = "chapter-missing-draft",
                        Status = "context_ready"
                    }
                }
            }
        };
        var context = new AgentObservationContext
        {
            AvailableTools =
            {
                new AgentToolDefinition { Name = "ProduceChapter" },
            },
            MissionPlan = session.WorkingMemory.MissionPlan,
            TurnIntent = new TurnIntent { Type = TurnIntentType.FreeChat, Label = "continue_mission" },
        };

        var result = policy.BeforeCall(
            new AgentToolCall { Name = "ValidateChapterDraft", Arguments = { ["runId"] = runId } },
            session,
            bible,
            context,
            confirmed: false);

        Check.True(!result.AllowsExecution && !result.IsRepairable,
            "Obsolete ValidateChapterDraft must be terminal at policy level instead of synthesizing old prerequisites.");
        Check.Contains("未知工具", result.Message,
            "Policy should reject obsolete stage tools as unregistered tools.");
        return Task.CompletedTask;
    }

    private static Task ToolPolicyKeepsHardBoundariesTerminal()
    {
        var policy = new ToolPolicyEngine();
        var session = new AgentSession();
        var context = new AgentObservationContext
        {
            TurnIntent = new TurnIntent { Type = TurnIntentType.FreeChat, Label = "continue_mission" },
            MissionPlan = new AgentMissionPlan()
        };

        var unknown = policy.BeforeCall(
            new AgentToolCall { Name = "DefinitelyNotARegisteredTool" },
            session,
            new StoryBibleDocument(),
            context,
            confirmed: false);
        Check.True(!unknown.AllowsExecution && !unknown.IsRepairable,
            "Unknown tools must remain terminal and must not be auto-repaired.");

        var unconfirmedCommit = policy.BeforeCall(
            new AgentToolCall { Name = "CommitValidatedChapter", Arguments = { ["runId"] = "run-hard-boundary" } },
            session,
            new StoryBibleDocument
            {
                AgentRuns =
                {
                    new NovelAgentRun
                    {
                        RunId = "run-hard-boundary",
                        GateReport = new GenerationGateReport { Status = "validated" }
                    }
                }
            },
            new AgentObservationContext
            {
                AvailableTools =
                {
                    new AgentToolDefinition { Name = "ProduceChapter" },
                },
                MissionPlan = BuildPlanWithChapter("run-hard-boundary", chapter =>
                {
                    chapter.GateStatus = "validated";
                    chapter.QualityStatus = "quality_passed";
                }),
                TurnIntent = new TurnIntent { Type = TurnIntentType.FreeChat, Label = "continue_mission" }
            },
            confirmed: false);
        Check.True(!unconfirmedCommit.AllowsExecution && !unconfirmedCommit.IsRepairable,
            "Obsolete CommitValidatedChapter must be rejected as an unregistered tool.");
        Check.Contains("未知工具", unconfirmedCommit.Message,
            "Obsolete stage tools should not behave as confirmation gates.");
        return Task.CompletedTask;
    }

    private static Task ToolPolicyRepairsPrematureDraftRepairToValidation()
    {
        const string runId = "run-premature-repair";
        var policy = new ToolPolicyEngine();
        var session = new AgentSession
        {
            ActiveRunId = runId,
            WorkingMemory = new AgentWorkingMemory
            {
                MissionPlan = BuildPlanWithChapter(runId, chapter =>
                {
                    chapter.Status = "draft_generated";
                    chapter.DraftStatus = "draft_generated";
                    chapter.GateStatus = "none";
                    chapter.NextAction = "ProduceChapter";
                })
            }
        };
        var bible = new StoryBibleDocument
        {
            AgentRuns =
            {
                new NovelAgentRun
                {
                    RunId = runId,
                    TargetChapterId = "chapter-premature-repair",
                    ContextPackage = new ChapterContextPackageSummary
                    {
                        ChapterId = "chapter-premature-repair",
                        Status = "context_ready"
                    },
                    DraftArtifact = new ChapterDraftArtifact
                    {
                        ChapterId = "chapter-premature-repair",
                        DraftContent = "正文\n<chapter_changes>{}</chapter_changes>",
                        ChangesJson = "{}",
                        HasChanges = true
                    }
                }
            }
        };
        var context = new AgentObservationContext
        {
            AvailableTools =
            {
                new AgentToolDefinition { Name = "ProduceChapter" },
            },
            MissionPlan = session.WorkingMemory.MissionPlan,
            TurnIntent = new TurnIntent { Type = TurnIntentType.FreeChat, Label = "continue_mission" },
        };

        var result = policy.BeforeCall(
            new AgentToolCall { Name = "RepairChapterDraft", Arguments = { ["runId"] = runId } },
            session,
            bible,
            context,
            confirmed: true);

        Check.True(!result.AllowsExecution && !result.IsRepairable,
            "Obsolete RepairChapterDraft must be terminal at policy level.");
        Check.Contains("未知工具", result.Message,
            "Policy should reject obsolete repair stages as unregistered tools.");
        return Task.CompletedTask;
    }

    private static Task ToolPolicyAllowsRegisteredToolsWithoutDiscoveryGate()
    {
        var policy = new ToolPolicyEngine();
        var session = new AgentSession();
        var bible = new StoryBibleDocument();
        var context = new AgentObservationContext
        {
            AvailableTools =
            {
                new AgentToolDefinition
                {
                    Name = "tool_search",
                    Description = "discover tools",
                    Risk = "Low"
                },
                new AgentToolDefinition
                {
                    Name = "ResolveNovelProject",
                    Description = "resolve or create project",
                    Risk = "Low"
                }
            },
            TurnIntent = new TurnIntent { Type = TurnIntentType.FreeChat, Label = "new_project_seed" },
            MissionPlan = new AgentMissionPlan()
        };

        var skippedDiscovery = policy.BeforeCall(
            new AgentToolCall { Name = "ResolveNovelProject", Arguments = { ["mode"] = "create_new", ["seed"] = "玄幻学院流" } },
            session,
            bible,
            context,
            confirmed: false);

        Check.True(skippedDiscovery.AllowsExecution,
            "Registered low-risk tools must not be blocked just because the model did not call tool_search first.");
        Check.True(!skippedDiscovery.Message.Contains("tool_search", StringComparison.OrdinalIgnoreCase),
            "Policy must not leak or enforce tool_search as a discovery gate.");

        var discovery = policy.BeforeCall(
            new AgentToolCall { Name = "tool_search", Arguments = { ["phase"] = "Planning" } },
            session,
            bible,
            context,
            confirmed: false);
        Check.True(discovery.AllowsExecution,
            "tool_search itself must always pass the discovery boundary.");

        var discovered = policy.BeforeCall(
            new AgentToolCall { Name = "ResolveNovelProject", Arguments = { ["mode"] = "create_new", ["seed"] = "玄幻学院流" } },
            session,
            bible,
            context,
            confirmed: false);
        Check.True(discovered.AllowsExecution,
            "Adding a tool to AvailableTools should remain compatible, but it is no longer required for low-risk registered tools.");
        return Task.CompletedTask;
    }

    private static Task ToolPolicyBlocksDraftWhenContextRebuildRequired()
    {
        const string runId = "run-chapter-003";
        var policy = new ToolPolicyEngine();
        var session = new AgentSession
        {
            ActiveRunId = runId,
            WorkingMemory = new AgentWorkingMemory
            {
                MissionPlan = BuildPlanWithChapter(runId, chapter =>
                {
                    chapter.Status = "needs_context_rebuild";
                    chapter.RequiresContextRebuild = true;
                    chapter.NextAction = "ProduceChapter";
                })
            }
        };
        var bible = new StoryBibleDocument
        {
            AgentRuns =
            {
                new NovelAgentRun
                {
                    RunId = runId,
                    TargetChapterId = "chapter-003",
                    ContextPackage = new ChapterContextPackageSummary
                    {
                        ChapterId = "chapter-003",
                        Status = "context_ready"
                    }
                }
            }
        };
        var context = new AgentObservationContext
        {
            MissionPlan = session.WorkingMemory.MissionPlan,
            TurnIntent = new TurnIntent { Type = TurnIntentType.FreeChat, Label = "continue_mission" },
        };

        var result = policy.BeforeCall(
            new AgentToolCall { Name = "ProduceChapter", Arguments = { ["runId"] = runId } },
            session,
            bible,
            context,
            confirmed: true);

        Check.True(result.AllowsExecution,
            "ProduceChapter should own context rebuild requirements inside the closed-loop tool.");
        Check.True(!result.IsRepairable,
            "Policy must not synthesize obsolete context-package repair actions.");
        return Task.CompletedTask;
    }

    private static Task ToolPolicyBlocksCommitWhenRevalidationRequired()
    {
        const string runId = "run-chapter-004";
        var policy = new ToolPolicyEngine();
        var session = new AgentSession
        {
            ActiveRunId = runId,
            WorkingMemory = new AgentWorkingMemory
            {
                MissionPlan = BuildPlanWithChapter(runId, chapter =>
                {
                    chapter.Status = "needs_revalidation";
                    chapter.RequiresRevalidation = true;
                    chapter.GateStatus = "validated";
                    chapter.QualityStatus = "quality_passed";
                    chapter.NextAction = "ProduceChapter";
                })
            }
        };
        var bible = new StoryBibleDocument
        {
            AgentRuns =
            {
                new NovelAgentRun
                {
                    RunId = runId,
                    TargetChapterId = "chapter-004",
                    GateReport = new GenerationGateReport { Status = "validated" }
                }
            }
        };
        var context = new AgentObservationContext
        {
            MissionPlan = session.WorkingMemory.MissionPlan,
            TurnIntent = new TurnIntent { Type = TurnIntentType.FreeChat, Label = "confirmation" },
        };

        var result = policy.BeforeCall(
            new AgentToolCall { Name = "CommitValidatedChapter", Arguments = { ["runId"] = runId } },
            session,
            bible,
            context,
            confirmed: true);

        Check.True(!result.AllowsExecution && !result.IsRepairable,
            "Obsolete CommitValidatedChapter must be rejected instead of creating revalidation repair chains.");
        Check.Contains("未知工具", result.Message,
            "Policy should reject obsolete commit stages as unregistered tools.");
        return Task.CompletedTask;
    }

    private static Task MissionBlackboardRecoveryRebuildsSchedulerState()
    {
        const string runId = "run-chapter-005";
        var session = new AgentSession { SessionId = "session-recovery", ActiveRunId = runId };
        var project = new NovelProjectInfo
        {
            Id = "project-recovery",
            Title = "恢复测试",
            Genre = "玄幻",
            CoreHook = "规则裂纹",
            Status = "Drafting",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var bible = new StoryBibleDocument
        {
            AgentRuns =
            {
                new NovelAgentRun
                {
                    RunId = runId,
                    Intent = NovelAgentIntent.PlanChapter,
                    Status = NovelAgentRunStatus.Planning,
                    TargetChapterId = "chapter-005",
                    ChapterBrief = new ChapterCreativeBrief
                    {
                        ChapterId = "chapter-005",
                        SelectedCandidateTitle = "主动利用规则裂纹"
                    }
                }
            }
        };
        var recovery = new MissionBlackboardRecoveryService(new AgentMissionTaskTreeService());

        recovery.Recover(session, project, bible);

        Check.True(session.WorkingMemory.MissionPlan.SchedulerState.Tasks.Count > 0,
            "Recovery should rebuild scheduler tasks from StoryBible runs.");
        Check.True(session.WorkingMemory.MissionPlan.LastRecoveredAt != null,
            "Recovery should record LastRecoveredAt.");
        Check.Equal("ProduceChapter", session.WorkingMemory.MissionPlan.SchedulerState.Tasks[0].NextAction,
            "Recovered selected chapter candidate should continue with the closed-loop production tool.");
        return Task.CompletedTask;
    }

    private static Task MissionBlackboardExposesDraftsBeforeLibraryCommit()
    {
        var session = new AgentSession
        {
            SessionId = "draft-visibility-session",
            ActiveProjectId = "project-draft-visibility",
            ActiveRunId = "run-draft-visibility"
        };
        var project = new NovelProjectInfo
        {
            Id = "project-draft-visibility",
            Title = "草稿可见性测试",
            Status = "Drafting",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var run = new NovelAgentRun
        {
            RunId = "run-draft-visibility",
            Intent = NovelAgentIntent.PlanChapter,
            Status = NovelAgentRunStatus.Executing,
            TargetChapterId = "chapter-visibility",
            ChapterBrief = new ChapterCreativeBrief
            {
                ChapterId = "chapter-visibility",
                SelectedCandidateTitle = "第一次主动利用规则裂纹"
            },
            ContextPackage = new ChapterContextPackageSummary
            {
                ChapterId = "chapter-visibility",
                Status = "context_ready"
            },
            DraftArtifact = new ChapterDraftArtifact
            {
                ArtifactId = "draft-visibility",
                ChapterId = "chapter-visibility",
                Status = "draft_generated",
                DraftContent = "草稿正文\n<chapter_changes>{}</chapter_changes>",
                HasChanges = true
            }
        };
        var bible = new StoryBibleDocument { AgentRuns = { run } };
        var sync = new AgentMissionTaskTreeService();

        sync.Sync(session, project, bible);
        var chapter = session.WorkingMemory.MissionPlan.BookTaskTree.Volumes
            .SelectMany(v => v.Chapters)
            .Single(c => c.ChapterId == "chapter-visibility");

        Check.Equal("draft_generated", chapter.Status,
            "Draft artifact should be visible in MissionBlackboard before library commit.");
        Check.Equal("draft-visibility", chapter.DraftArtifactId,
            "MissionBlackboard should keep the draft artifact cursor.");
        Check.Contains("草稿", chapter.UserVisibleStatus,
            "Workflow user-visible status should explain that a draft exists.");

        run.GateReport = new GenerationGateReport
        {
            Status = "validated",
            ProtocolPassed = true,
            ChangesDetected = true,
            FactSnapshotPassed = true,
            BlueprintPassed = true,
            RagPassed = true
        };
        run.PostGenerationReview = new NovelAgentPostGenerationReview
        {
            ReviewId = "review-visibility",
            ChapterId = "chapter-visibility",
            OverallResult = "Pass",
            RequiresRewrite = false,
            QualityScore = 90
        };
        run.DraftArtifact.Status = "committed";
        run.DraftArtifact.CommittedContent = "成稿正文";
        run.Status = NovelAgentRunStatus.Completed;

        sync.Sync(session, project, bible);
        chapter = session.WorkingMemory.MissionPlan.BookTaskTree.Volumes
            .SelectMany(v => v.Chapters)
            .Single(c => c.ChapterId == "chapter-visibility");

        Check.Equal("committed", chapter.Status,
            "Committed draft with validated gate and passing review should become committed in the blackboard.");
        Check.Contains("书城", chapter.UserVisibleStatus,
            "Committed chapter user-visible status should point to library visibility.");
        return Task.CompletedTask;
    }

    private static async Task NewProjectReflectionDoesNotLocallyContinueFoundationPlanning()
    {
        var planner = new AgentPlanner(CreateDbBackedSettingsManager(), new HttpClient());
        var insufficient = await planner.ReflectAsync(
            new AgentObservationContext
            {
                UserMessage = "你好，我想写一本像斗罗大陆一样风格的小说"
            },
            new AgentRuntimeObservation
            {
                ToolName = "ResolveNovelProject",
                Success = true,
                Phase = "awaiting_user_foundation",
                Message = "已创建新小说，现在先把地基问清楚。"
            },
            CancellationToken.None);

        Check.True(insufficient.RequiresUserInput,
            "New project creation may ask for foundation input when the user has not supplied a usable brief.");
        Check.True(!insufficient.ShouldContinue,
            "Insufficient new project brief should not auto-continue into another tool call.");

        var sufficient = await planner.ReflectAsync(
            new AgentObservationContext
            {
                UserMessage = "写一本新小说《废土神国：我靠吞噬怪物升级》。末世玄幻爽文，男主从底层幸存者开始，系统吞噬怪物晶核升级，一路打怪升级、建基地、收伙伴。不要绑定旧项目，不要规则反噬/真相递进/关系代价，直接创建新书并推进故事地基。"
            },
            new AgentRuntimeObservation
            {
                ToolName = "ResolveNovelProject",
                Success = true,
                Phase = "awaiting_user_foundation",
                Message = "已创建新小说「废土神国：我靠吞噬怪物升级」，它会作为独立作品进入书城，不会覆盖旧书。"
            },
            CancellationToken.None);

        Check.True(sufficient.RequiresUserInput,
            "Without an LLM reflection result, runtime must not locally infer that a long creative brief is sufficient.");
        Check.True(!sufficient.ShouldContinue,
            "Rule reflection must not continue into foundation planning by parsing natural-language completeness.");
        Check.Contains("已创建新小说", sufficient.ReplyDraft,
            "Fallback reflection should preserve the real tool result instead of inventing a next tool.");
    }

    private static async Task PlannerReflectionTimeoutReturnsBoundedNonDecisiveFallback()
    {
        var settings = CreateDbBackedSettingsManager(settings: new UserSettings
        {
            LlmProvider = "openai",
            LlmBaseUrl = "https://example.test/v1",
            LlmApiKey = "test-key",
            LlmModel = "test-model",
            LlmMaxTokens = 512
        });
        using var http = new HttpClient(new SlowHandler(TimeSpan.FromSeconds(5)));
        var planner = new AgentPlanner(settings, http);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var elapsed = Stopwatch.StartNew();

        var reflection = await planner.ReflectAsync(
            new AgentObservationContext
            {
                UserMessage = "写一本新小说《黑潮领主：我吞噬异兽晶核升级》。末世玄幻爽文，男主从海港贫民窟幸存者开始，系统吞噬异兽晶核升级，打怪升级、建基地、收伙伴。不要绑定旧项目，直接创建新书并推进故事地基。"
            },
            new AgentRuntimeObservation
            {
                ToolName = "ResolveNovelProject",
                Success = true,
                Phase = "awaiting_user_foundation",
                Message = "已创建新小说「黑潮领主：我吞噬异兽晶核升级」。"
            },
            timeout.Token);
        elapsed.Stop();

        Check.True(elapsed.Elapsed < TimeSpan.FromSeconds(3),
            "Reflection should have its own short fallback budget instead of waiting for the full model HTTP timeout.");
        Check.True(!reflection.ShouldContinue,
            "Slow model reflection must not use a local natural-language completeness guess to continue production.");
        Check.True(reflection.RequiresUserInput,
            "Bounded fallback should return control instead of pretending the model approved the next tool.");
    }

    private static async Task StoryFoundationCandidatesContinueWithStructuredCommitAction()
    {
        var planner = new AgentPlanner(CreateDbBackedSettingsManager(), new HttpClient());
        var reflection = await planner.ReflectAsync(
            new AgentObservationContext
            {
                UserMessage = "这些都不要，就要一路打怪升级的",
                TurnIntent = new TurnIntent { Type = TurnIntentType.FreeChat, Label = "free_chat" },
                MissionPlan = new AgentMissionPlan
                {
                    Stage = "foundation",
                    Status = "active",
                    AllowedNextActions = { "CommitStoryFoundation" }
                }
            },
            new AgentRuntimeObservation
            {
                ObservationType = "tool_result",
                ToolName = "PlanStoryFoundation",
                Success = true,
                Phase = "foundation_candidates",
                RunId = "run-foundation-candidates",
                Message = "生成了 3 个故事地基候选：规则反噬型、真相递进型、关系代价型。",
                Artifact = new AgentToolArtifact
                {
                    ArtifactType = "story_foundation_candidates",
                    ArtifactId = "run-foundation-candidates",
                    RunId = "run-foundation-candidates",
                    Summary = "生成 3 个故事地基候选。",
                    VisibleInWorkflow = true,
                    UserVisibleStatus = "故事地基候选已生成"
                }
            },
            CancellationToken.None);

        Check.True(!reflection.RequiresUserInput,
            "Story foundation candidates should not force user input when the mission blackboard has a structured commit action.");
        Check.True(reflection.ShouldContinue,
            "Runtime should allow the model to continue from generated foundation candidates into the structured next action.");
        Check.Contains("故事地基候选", reflection.ReplyDraft,
            "Final reply should preserve the successful tool artifact instead of falling through to no-result governance text.");
        Check.DoesNotContain("没有新的工具执行结果", reflection.ReplyDraft,
            "A successful tool result must not be reported as no new execution result.");
    }

    private static Task RuntimeContinuesProcessToolsWithStructuredNextHints()
    {
        var result = new AgentToolExecutionResult
        {
            Success = true,
            Phase = "foundation_committed",
            Message = "故事地基已固化。",
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "story_foundation_commit",
                ArtifactId = "run-foundation-commit",
                RunId = "run-foundation-commit",
                Summary = "故事地基已固化。",
                NextHints = new[] { "规划第一卷" },
                VisibleInWorkflow = true,
                UserVisibleStatus = "故事地基已固化"
            },
            Suggestions = new[] { "规划第一卷", "查看当前状态" }
        };
        var conservativeReflection = new AgentReflection
        {
            Summary = "故事地基已固化。",
            GoalSatisfied = true,
            ShouldContinue = false,
            RequiresUserInput = false
        };

        Check.True(!AgentRuntime.ShouldStopAfterToolResult(result, conservativeReflection, new AgentMissionPlan
        {
            TodoQueue = { "规划第一卷" }
        }), "Successful process tools with structured next hints should continue into the next LLM planning turn.");

        var completeBriefProjectResult = new AgentToolExecutionResult
        {
            Success = true,
            Phase = "awaiting_user_foundation",
            Message = "已创建新小说，当前 brief 已包含类型、主角引擎、爽点和禁区。",
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "novel_project",
                ArtifactId = "project-new",
                ProjectId = "project-new",
                Summary = "新小说已创建。",
                NextHints = new[] { "PlanStoryFoundation" },
                VisibleInWorkflow = true,
                UserVisibleStatus = "新小说已创建"
            }
        };
        var continueReflection = new AgentReflection
        {
            Summary = "新小说已创建，信息足够继续生成地基候选。",
            GoalSatisfied = false,
            ShouldContinue = true,
            RequiresUserInput = false
        };

        Check.True(!AgentRuntime.ShouldStopAfterToolResult(completeBriefProjectResult, continueReflection, new AgentMissionPlan
        {
            TodoQueue = { "生成故事地基候选" }
        }), "awaiting_user_foundation should not force-stop when reflection says the brief is sufficient and next work is structured.");

        var candidateResult = new AgentToolExecutionResult
        {
            Success = true,
            Phase = "foundation_candidates",
            Message = "故事地基候选已生成。",
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "story_foundation_candidates",
                ArtifactId = "run-foundation-candidates",
                RunId = "run-foundation-candidates",
                Summary = "故事地基候选已生成。",
                NextHints = new[] { "选第1个" },
                VisibleInWorkflow = true,
                UserVisibleStatus = "故事地基候选已生成"
            },
            Suggestions = new[] { "选第1个" }
        };

        Check.True(AgentRuntime.ShouldStopAfterToolResult(candidateResult, conservativeReflection),
            "Candidate artifacts still stop for user review even when they expose next hints.");

        var toolSearchResult = new AgentToolExecutionResult
        {
            Success = true,
            Phase = "idle",
            Message = "检索到 12 个工具候选，已更新工具语义缓存。",
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "tool_search_result",
                ArtifactId = "tool-search-scope",
                Summary = "检索到 12 个工具候选。",
                VisibleInWorkflow = true
            }
        };

        Check.True(!AgentRuntime.ShouldStopAfterToolResult(toolSearchResult, conservativeReflection),
            "tool_search is internal capability discovery and must continue into another LLM planning turn.");
        return Task.CompletedTask;
    }

    private static Task RuntimeContinuesAfterCommittedChapterWhenBookTasksRemain()
    {
        var result = new AgentToolExecutionResult
        {
            Success = true,
            Phase = "chapter_committed",
            Message = "第 1 章已提交书城。",
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "chapter_committed",
                ArtifactId = "chapter-001",
                ProjectId = "project-book",
                RunId = "run-chapter-001",
                Summary = "第 1 章已提交书城。",
                UserVisibleStatus = "第 1 章已入书城",
                VisibleInWorkflow = true,
                VisibleInLibrary = true
            }
        };
        var reflection = new AgentReflection
        {
            Summary = "第 1 章已完成。",
            GoalSatisfied = true,
            ShouldContinue = false,
            RequiresUserInput = false
        };
        var plan = new AgentMissionPlan
        {
            CurrentObjective = "写完三章短篇并提交书城。",
            BookTaskTree = new AgentBookTaskTree
            {
                Volumes =
                {
                    new AgentVolumeTask
                    {
                        VolumeId = "volume-001",
                        Chapters =
                        {
                            new AgentChapterTask
                            {
                                ChapterId = "chapter-001",
                                RunId = "run-chapter-001",
                                Status = "completed",
                                CommitStatus = "committed"
                            },
                            new AgentChapterTask
                            {
                                ChapterId = "chapter-002",
                                Status = "queued",
                                NextAction = "PlanChapter"
                            },
                            new AgentChapterTask
                            {
                                ChapterId = "chapter-003",
                                Status = "queued",
                                NextAction = "PlanChapter"
                            }
                        }
                    }
                }
            },
            SchedulerState = new AgentTaskSchedulerState
            {
                ActiveTaskId = "task-chapter-002",
                ActiveChapterId = "chapter-002",
                Tasks =
                {
                    new AgentScheduledTask
                    {
                        TaskId = "task-chapter-002",
                        ProjectId = "project-book",
                        ChapterId = "chapter-002",
                        Status = "queued",
                        TaskType = "PlanChapter",
                        NextAction = "PlanChapter"
                    },
                    new AgentScheduledTask
                    {
                        TaskId = "task-chapter-003",
                        ProjectId = "project-book",
                        ChapterId = "chapter-003",
                        Status = "queued",
                        TaskType = "PlanChapter",
                        NextAction = "PlanChapter"
                    }
                }
            }
        };

        Check.True(!AgentRuntime.ShouldStopAfterToolResult(result, reflection, plan),
            "A committed chapter is terminal only for that chapter; a whole-book mission must continue while later chapter tasks are queued.");
        return Task.CompletedTask;
    }

    private static async Task RuntimeGovernanceObservationDoesNotLeakGuardText()
    {
        var planner = new AgentPlanner(CreateDbBackedSettingsManager(), new HttpClient());
        var reflection = await planner.ReflectAsync(
            new AgentObservationContext
            {
                UserMessage = "你好，我想写一本像斗罗大陆一样风格的小说",
                TurnIntent = new TurnIntent { Type = TurnIntentType.FreeChat, Label = "new_project_seed" },
                MissionPlan = new AgentMissionPlan
                {
                    Stage = "foundation",
                    Status = "blocked",
                    AllowedNextActions = { "PlanStoryFoundation" }
                }
            },
            new AgentRuntimeObservation
            {
                ObservationType = "runtime_observation",
                ToolName = "ResolveNovelProject",
                Success = false,
                Phase = "runtime_repeated_tool_observation",
                Message = "本轮已有同名同参数工具结果。运行时没有重复执行写入动作；请基于已有 observation 继续反思或回复。"
            },
            CancellationToken.None);

        Check.True(reflection.RequiresUserInput,
            "Governance observations should return control to the user-facing agent response.");
        Check.DoesNotContain("重复工具", reflection.ReplyDraft,
            "Reflect fallback must not expose repeated-tool guard wording.");
        Check.DoesNotContain("Runtime", reflection.ReplyDraft,
            "Reflect fallback must not expose Runtime internals.");
        Check.DoesNotContain("运行时", reflection.ReplyDraft,
            "Reflect fallback must not expose runtime internals.");
    }

    private static async Task RuntimeGovernanceObservationDoesNotLeakToolNames()
    {
        var planner = new AgentPlanner(CreateDbBackedSettingsManager(), new HttpClient());
        var reflection = await planner.ReflectAsync(
            new AgentObservationContext
            {
                UserMessage = "这是什么意思？不是开始写小说了吗",
                TurnIntent = new TurnIntent { Type = TurnIntentType.FreeChat, Label = "free_chat" },
                MissionPlan = new AgentMissionPlan
                {
                    Stage = "foundation",
                    Status = "blocked",
                    AllowedNextActions = { "PlanStoryFoundation", "PlanVolumeArc" }
                }
            },
            new AgentRuntimeObservation
            {
                ObservationType = "runtime_observation",
                ToolName = "runtime",
                Success = false,
                Phase = "runtime_repeated_tool_observation",
                Message = "本轮没有执行新的工具。"
            },
            CancellationToken.None);

        Check.DoesNotContain("PlanStoryFoundation", reflection.ReplyDraft,
            "Governance reply must not expose internal planning tool names.");
        Check.DoesNotContain("PlanVolumeArc", reflection.ReplyDraft,
            "Governance reply must not expose internal planning tool names.");
        Check.Contains("故事地基", reflection.ReplyDraft,
            "Governance reply should translate internal tools into product language.");
    }

    private static Task ToolExecutionSnapshotsExposeUserVisibleProgress()
    {
        var running = AgentToolProgressPresenter.Describe(new AgentToolExecutionSnapshot
        {
            ToolName = "PlanStoryFoundation",
            Status = "running",
            Phase = "planning",
            StartedAt = DateTime.UtcNow
        });
        var completed = AgentToolProgressPresenter.Describe(new AgentToolExecutionSnapshot
        {
            ToolName = NovelAgentProductionStages.ChapterCommit,
            Status = "succeeded",
            Phase = "review",
            ResultPhase = "chapter_committed",
            ResultMessage = "章节已提交。",
            StartedAt = DateTime.UtcNow.AddSeconds(-3),
            CompletedAt = DateTime.UtcNow
        });

        Check.Equal("正在生成故事地基候选", running.Title,
            "Running tool progress should use product language.");
        Check.True(running.IsRunning,
            "Running snapshot should be marked as in progress.");
        Check.DoesNotContain("PlanStoryFoundation", running.Title,
            "Tool progress title must not leak internal tool names.");
        Check.Contains("书城", completed.Detail,
            "Committed chapter progress should tell the user where the final artifact appears.");
        return Task.CompletedTask;
    }

    private static Task RuntimeReturnsUserFacingChatRepliesBeforeNoActionFallback()
    {
        var normalChat = new AgentAction
        {
            Type = AgentActionType.ChatReply,
            Reply = "我是天命小说 Agent。",
            Source = "openai_tool_calling"
        };
        var noToolChatReply = new AgentAction
        {
            Type = AgentActionType.ChatReply,
            Reply = "我是天命小说 Agent。",
            Source = "chat_reply",
            IsNoTool = true
        };
        var noAction = new AgentAction
        {
            Type = AgentActionType.ChatReply,
            Source = "planner_no_action",
            IsNoTool = true
        };
        var internalErrorText = new AgentAction
        {
            Type = AgentActionType.ChatReply,
            Reply = "raw provider exception",
            Source = "provider_raw_error",
            IsNoTool = true
        };
        var timeoutError = new AgentAction
        {
            Type = AgentActionType.ChatReply,
            Reply = "模型响应超时，请稍后重试。",
            Source = "error_timeout",
            IsNoTool = true
        };
        var authError = new AgentAction
        {
            Type = AgentActionType.ChatReply,
            Reply = "API 认证失败，请在用户设置中检查 API Key。",
            Source = "error_auth",
            IsNoTool = true
        };

        Check.True(AgentRuntime.ShouldReturnUserFacingReply(normalChat),
            "Normal ChatReply text from a provider should return directly.");
        Check.True(AgentRuntime.ShouldReturnUserFacingReply(noToolChatReply),
            "ChatReply text marked IsNoTool should still return directly when it is user-facing.");
        Check.True(!AgentRuntime.ShouldReturnUserFacingReply(noAction),
            "No-action fallback without reply should not be treated as chat.");
        Check.True(!AgentRuntime.ShouldReturnUserFacingReply(internalErrorText),
            "Raw provider/planner internals should not return directly.");
        Check.True(!AgentRuntime.ShouldReturnUserFacingReply(timeoutError),
            "Planner timeouts should fall through to runtime status/scheduler fallback instead of becoming normal chat.");
        Check.True(AgentRuntime.ShouldReturnUserFacingReply(authError),
            "User-fixable auth errors may be returned directly.");
        return Task.CompletedTask;
    }

    private static Task RuntimeDoesNotExposeNoToolRetryLoop()
    {
        var method = typeof(AgentRuntime).GetMethod(
            "ShouldRetryNoToolPlanning",
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Instance);

        Check.True(method == null,
            "Runtime must not keep the old no-tool retry loop; production continuation should be decided by model-visible task state and scheduler context.");
        return Task.CompletedTask;
    }

    private static async Task PlannerMissingLlmSettingsReturnsExplicitDegradedNoToolReply()
    {
        var planner = new AgentPlanner(CreateDbBackedSettingsManager(), new HttpClient());
        var action = await planner.PlanActionAsync(new AgentObservationContext
        {
            UserMessage = "你是谁",
            TurnIntent = new TurnIntent
            {
                Type = TurnIntentType.FreeChat,
                Label = "free_chat",
                RawMessage = "你是谁"
            },
            MissionPlan = new AgentMissionPlan()
        }, CancellationToken.None);

        Check.Equal(AgentActionType.ChatReply, action.Type,
            "Missing LLM settings should still produce an explicit user-facing degraded reply.");
        Check.True(action.IsNoTool,
            "Missing LLM settings is a degraded no-tool state, not a normal local identity decision.");
        Check.Contains("降级模式", action.Reply,
            "The reply should clearly say the agent is degraded instead of pretending to be a complete planner.");
        Check.DoesNotContain("天命小说 Agent", action.Reply,
            "Missing LLM fallback must not masquerade as a normal identity chat reply.");
        Check.True(AgentRuntime.ShouldReturnUserFacingReply(action),
            "Configuration problems should be visible to the user.");
    }

    private static async Task PlannerMissingLlmSettingsDoesNotInventStatusResults()
    {
        var planner = new AgentPlanner(CreateDbBackedSettingsManager(), new HttpClient());
        var action = await planner.PlanActionAsync(new AgentObservationContext
        {
            UserMessage = "刚才那章呢",
            TurnIntent = new TurnIntent
            {
                Type = TurnIntentType.FreeChat,
                Label = "status_query",
                RawMessage = "刚才那章呢"
            },
            MissionPlan = new AgentMissionPlan()
        }, CancellationToken.None);

        Check.True(action.IsNoTool,
            "Missing LLM settings status query should remain a no-tool degraded response.");
        Check.Contains("没有配置可用的模型服务", action.Reply,
            "Missing LLM fallback must report configuration state, not fabricate project progress.");
        Check.DoesNotContain("刚才那章", action.Reply,
            "Missing LLM fallback must not pretend it inspected chapter state.");
        Check.True(AgentRuntime.ShouldReturnUserFacingReply(action),
            "Configuration problems should be returned directly so the user can fix settings.");
    }

    private static Task ConversationKernelBuildsUserTurnEnvelope()
    {
        var session = new AgentSession
        {
            ActiveRunId = "run-active",
            WorkingMemory = new AgentWorkingMemory
            {
                MissionPlan = new AgentMissionPlan
                {
                    ArtifactCursor = "draft-run-active",
                    SchedulerState = new AgentTaskSchedulerState
                    {
                        ActiveTaskId = "task-active",
                        Tasks =
                        {
                            new AgentScheduledTask
                            {
                                TaskId = "task-active",
                                RunId = "run-active",
                                Status = "queued",
                                NextAction = "ProduceChapter"
                            }
                        }
                    }
                }
            }
        };
        var envelope = new ConversationKernel().BuildEnvelope(session, "刚才那章呢，准备好了吗");

        Check.Equal(DialogueAct.Chat, envelope.DialogueAct,
            "ConversationKernel should not keyword-route natural status wording into an ask-status act.");
        Check.Equal(string.Empty, envelope.StatusQueryScope,
            "Status scope should only be produced by model/tool decisions, not local keyword parsing.");
        Check.Equal("run-active", envelope.TargetArtifact,
            "Envelope should preserve scheduler context for the model without forcing artifact-cursor status routing.");
        Check.Equal("task-active", envelope.ReferencedTask,
            "Envelope should preserve the active scheduler task reference.");
        return Task.CompletedTask;
    }

    private static Task ForegroundDoesNotFinishCreativeWorkAfterToolSearch()
    {
        var method = typeof(AgentRuntime).GetMethod(
            "IsForegroundReadableToolAction",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (method == null)
            throw new RegressionAssertException("AgentRuntime.IsForegroundReadableToolAction was not found.");

        bool IsForegroundReadable(string toolName)
        {
            var action = new AgentAction
            {
                Type = AgentActionType.ToolCall,
                ToolCall = new AgentToolCall { Name = toolName }
            };
            return (bool)(method.Invoke(null, new object[] { action }) ?? false);
        }

        Check.True(!IsForegroundReadable("tool_search"),
            "tool_search is internal capability discovery; foreground must hand it to the background loop instead of returning a final chat reply.");
        Check.True(IsForegroundReadable("QueryWorkspaceState"),
            "Workspace state snapshots should still be readable in the foreground.");
        Check.True(IsForegroundReadable("QueryProjectStatus"),
            "Project status snapshots should still be readable in the foreground.");
        Check.True(IsForegroundReadable("SearchCreativeKnowledge"),
            "Knowledge searches should still be readable in the foreground.");

        return Task.CompletedTask;
    }

    private static Task ForegroundDefersCreativeReadableToolsToBackground()
    {
        var method = typeof(AgentRuntime).GetMethod(
            "ShouldDeferForegroundReadableToolToBackground",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (method == null)
            throw new RegressionAssertException("AgentRuntime.ShouldDeferForegroundReadableToolToBackground was not found.");

        bool ShouldDefer(string toolName, string actionIntent)
        {
            var action = new AgentAction
            {
                Type = AgentActionType.ToolCall,
                Intent = actionIntent,
                ToolCall = new AgentToolCall { Name = toolName }
            };
            var context = new AgentObservationContext
            {
                TurnIntent = new TurnIntent { Type = TurnIntentType.FreeChat, Label = "free_chat" },
                UserTurn = new UserTurnEnvelope { DialogueAct = DialogueAct.Chat }
            };
            return (bool)(method.Invoke(null, new object[] { action, context }) ?? false);
        }

        Check.True(ShouldDefer("SearchCreativeKnowledge", "creative_production"),
            "A creative production turn may start by reading knowledge, but foreground must defer to the background loop so the LLM can continue planning/writing.");
        Check.True(ShouldDefer("QueryProjectStatus", "continue_mission"),
            "A continuation turn may read project status, then should continue in the background instead of ending as a status dump.");
        Check.True(!ShouldDefer("QueryWorkspaceState", "status_query"),
            "A direct status query should remain a foreground-readable reply.");
        Check.True(!ShouldDefer("SearchCreativeKnowledge", "free_chat"),
            "Casual chat should not be converted into background work just because a readable tool was selected.");

        return Task.CompletedTask;
    }

    private static Task ConversationKernelLeavesNewNovelRequestsAsRawPlannerInput()
    {
        var envelope = new ConversationKernel().BuildEnvelope(
            new AgentSession { Phase = "idle" },
            "我想写一本新的末世玄幻爽文，书名《废土神国：我靠吞噬怪物升级》，不要绑定旧项目，直接创建新小说。");

        Check.Equal(TurnIntentType.FreeChat, envelope.Intent.Type,
            "ConversationKernel must not keyword-route new-novel wording into project creation.");
        Check.Equal(DialogueAct.Chat, envelope.DialogueAct,
            "Project creation should be chosen by the LLM planner from tool semantics.");
        Check.Equal(string.Empty, envelope.CreativeBrief,
            "ConversationKernel must not manufacture creative briefs from raw natural language.");
        Check.Contains("废土神国", envelope.Intent.RawMessage,
            "The raw user request must remain available for the model planner.");
        return Task.CompletedTask;
    }

    private static Task ConversationKernelModelsCandidateSelection()
    {
        var foundationSession = new AgentSession
        {
            Phase = "foundation_candidates",
            ActiveRunId = "run-foundation-candidates",
            WorkingMemory = new AgentWorkingMemory
            {
                MissionPlan = new AgentMissionPlan
                {
                    CurrentRunId = "run-foundation-candidates",
                    Stage = "foundation_candidates"
                }
            }
        };
        var foundation = new ConversationKernel().BuildEnvelope(foundationSession, "1");
        Check.Equal(TurnIntentType.CandidateSelection, foundation.Intent.Type,
            "Numeric input in foundation candidate context should become candidate selection.");
        Check.Equal(DialogueAct.SelectCandidate, foundation.DialogueAct,
            "Candidate selection should become a select-candidate dialogue act.");
        Check.Equal(1, foundation.Intent.SelectedOptionIndex ?? 0,
            "Kernel should preserve selected option index.");
        Check.Equal("story_foundation_candidate", foundation.Intent.SelectionKind,
            "Foundation candidate context should be explicit.");
        Check.Equal("run-foundation-candidates", foundation.TargetArtifact,
            "Candidate selection should target the active candidate run.");

        var chapterSession = new AgentSession
        {
            Phase = "chapter_candidates",
            ActiveRunId = "run-chapter-candidates",
            WorkingMemory = new AgentWorkingMemory
            {
                MissionPlan = new AgentMissionPlan
                {
                    CurrentRunId = "run-chapter-candidates",
                    ActiveChapterId = "chapter-002"
                }
            }
        };
        var chapter = new ConversationKernel().BuildEnvelope(chapterSession, "B");
        Check.Equal(TurnIntentType.CandidateSelection, chapter.Intent.Type,
            "Letter input in chapter candidate context should become candidate selection.");
        Check.Equal(2, chapter.Intent.SelectedOptionIndex ?? 0,
            "Letter B should map to option 2.");
        Check.Equal("chapter_candidate", chapter.Intent.SelectionKind,
            "Chapter candidate context should be explicit.");

        var casual = new ConversationKernel().BuildEnvelope(new AgentSession { Phase = "idle" }, "1");
        Check.Equal(TurnIntentType.FreeChat, casual.Intent.Type,
            "Numeric input without candidate context must not become candidate selection.");
        return Task.CompletedTask;
    }

    private static Task ConversationKernelLeavesProductionRequestsAsRawPlannerInput()
    {
        var envelope = new ConversationKernel().BuildEnvelope(
            new AgentSession { Phase = "idle", ActiveProjectId = "project-1" },
            "我们正式开始《星渊邮差》的完整生产。请先理解当前项目、真实知识库硬事实和工作台状态，再建立完整创作工作流：6卷60章，每章正文不少于3000字，并开始第一章生成、校验和提交书城。");

        Check.Equal(TurnIntentType.FreeChat, envelope.Intent.Type,
            "ConversationKernel must not locally decide that a production request is a creative brief.");
        Check.Equal(DialogueAct.Chat, envelope.DialogueAct,
            "The LLM planner should decide whether this starts background production.");
        Check.Equal(string.Empty, envelope.CreativeBrief,
            "ConversationKernel must not prefill creative briefs from natural language.");
        Check.Contains("星渊邮差", envelope.Intent.RawMessage,
            "The raw production request must remain available for the model planner.");
        return Task.CompletedTask;
    }

    private static Task ConversationKernelDoesNotKeywordRouteContinuation()
    {
        var kernel = new ConversationKernel();
        var session = new AgentSession
        {
            Phase = "foundation",
            WorkingMemory = new AgentWorkingMemory
            {
                MissionPlan = new AgentMissionPlan
                {
                    Stage = "foundation",
                    AllowedNextActions = { "PlanStoryFoundation" }
                }
            }
        };

        foreach (var message in new[] { "继续", "下一步", "开始写", "这是什么意思？不是开始写小说了吗" })
        {
            var envelope = kernel.BuildEnvelope(session, message);
            Check.Equal(TurnIntentType.FreeChat, envelope.Intent.Type,
                $"Message '{message}' should remain natural language for the planner, not a keyword-routed ContinueMission.");
            Check.Equal(DialogueAct.Chat, envelope.DialogueAct,
                $"Message '{message}' must remain a neutral chat act for the LLM planner.");
        }

        return Task.CompletedTask;
    }

    private static Task CandidateSelectionConfirmationReplyIsUserVisible()
    {
        var action = new AgentAction
        {
            Type = AgentActionType.ToolCall,
            Intent = "commit_story_foundation",
            Risk = "High",
            ToolCall = new AgentToolCall
            {
                Name = "CommitStoryFoundation",
                Arguments = { ["runId"] = "run-foundation-candidates" }
            }
        };
        var context = new AgentObservationContext
        {
            AvailableTools =
            {
                new AgentToolDefinition { Name = "PlanChapter" },
            },
            TurnIntent = new TurnIntent
            {
                Type = TurnIntentType.CandidateSelection,
                Label = "candidate_selection",
                SelectedOption = "1",
                SelectedOptionIndex = 1,
                SelectionKind = "story_foundation_candidate",
                ReferencedRunId = "run-foundation-candidates"
            }
        };

        var reply = AgentRuntime.BuildUserVisibleConfirmationMessage(action, context, "固化 Story Bible 前必须确认。");

        Check.Contains("选择了第 1 个故事地基候选", reply,
            "Confirmation reply should expose the structured candidate interpretation.");
        Check.Contains("确认", reply,
            "Confirmation reply should tell the user how to proceed.");
        Check.Contains("取消", reply,
            "Confirmation reply should offer cancellation.");
        Check.DoesNotContain("固化 Story Bible 前必须确认。", reply,
            "Confirmation reply should not expose raw policy text when candidate selection is known.");
        return Task.CompletedTask;
    }

    private static Task RuntimeNormalizesStoryFoundationCandidateSelection()
    {
        var runId = "run-foundation-candidates";
        var action = new AgentAction
        {
            Type = AgentActionType.ToolCall,
            Intent = "commit_story_foundation",
            ToolCall = new AgentToolCall
            {
                Name = "CommitStoryFoundation",
                Arguments =
                {
                    ["runId"] = runId
                }
            }
        };
        var context = new AgentObservationContext
        {
            ActiveRunId = runId,
            TurnIntent = new TurnIntent
            {
                Type = TurnIntentType.CandidateSelection,
                SelectionKind = "story_foundation_candidate",
                SelectedOption = "1",
                SelectedOptionIndex = 1,
                ReferencedRunId = runId
            }
        };
        var bible = new StoryBibleDocument
        {
            AgentRuns =
            {
                new NovelAgentRun
                {
                    RunId = runId,
                    Intent = NovelAgentIntent.CreateStoryFoundation,
                    MacroCandidates =
                    {
                        new MacroStoryConceptCandidate
                        {
                            CandidateId = "macro-001-rule-backlash",
                            Title = "规则反噬型"
                        }
                    }
                }
            }
        };

        AgentRuntime.NormalizeStoryFoundationCandidateSelection(action, context, bible, new AgentSession { ActiveRunId = runId });

        Check.Equal("1", action.ToolCall!.Arguments["selectedMacroCandidateIndex"],
            "Runtime should persist the structured 1-based selected option into tool args.");
        Check.Equal("macro-001-rule-backlash", action.ToolCall.Arguments["selectedMacroCandidateId"],
            "Runtime should persist candidateId when the selected candidate has one.");
        Check.True(!action.ToolCall.Arguments.ContainsKey("selectedMacroCandidateTitle"),
            "Runtime should not write title-based story foundation selection args.");
        return Task.CompletedTask;
    }

    private static Task NativeToolCallsPassThroughToolPolicy()
    {
        var policy = new ToolPolicyEngine();
        var session = new AgentSession();
        var context = new AgentObservationContext
        {
            AvailableTools =
            {
                new AgentToolDefinition { Name = "PlanChapter" },
            },
            TurnIntent = new TurnIntent
            {
                Type = TurnIntentType.FreeChat,
                Label = "status_query",
                RawMessage = "刚才生成的章节在哪里"
            },
            MissionPlan = new AgentMissionPlan()
        };
        var nativeCall = new AgentToolCall
        {
            Name = "PlanChapter",
            Arguments =
            {
                ["creativeBrief"] = "刚才生成的章节在哪里",
                ["candidateDirections"] = "读取当前章节产物位置并整理状态",
                ["sourceTurnId"] = "native-tool"
            }
        };

        var result = policy.BeforeCall(nativeCall, session, new StoryBibleDocument(), context, confirmed: false);

        Check.True(result.AllowsExecution && result.ReplacementAction == null,
            "Native provider tool calls must still pass through ToolPolicy, but policy should not become intent routing.");
        return Task.CompletedTask;
    }

    private static Task ProviderToolCallingDiagnosticsParseMockResponses()
    {
        const string openAiJson = """
        {
          "choices": [
            {
              "message": {
                "tool_calls": [
                  {
                    "type": "function",
                    "function": {
                      "name": "QueryProjectStatus",
                      "arguments": "{\"scope\":\"chapter\"}"
                    }
                  }
                ]
              }
            }
          ]
        }
        """;
        const string anthropicJson = """
        {
          "content": [
            {
              "type": "tool_use",
              "name": "QueryProjectStatus",
              "input": {"scope":"chapter"}
            }
          ]
        }
        """;
        const string anthropicJsonTextEnvelope = """
        {
          "content": [
            {
              "type": "text",
              "text": "{\"action_type\":\"chat_reply\",\"intent\":\"free_chat\",\"reply\":\"你好！我是天命小说智能体，可以帮你管理设定、草稿和成稿。\"}"
            }
          ]
        }
        """;
        const string openAiJsonTextToolCall = """
        {
          "choices": [
            {
              "message": {
                "content": "{\"action_type\":\"tool_call\",\"intent\":\"status_query\",\"tool_call\":{\"name\":\"QueryProjectStatus\",\"arguments\":{\"scope\":\"chapter\"}},\"reply\":\"\"}"
              }
            }
          ]
        }
        """;
        const string anthropicPlainText = """
        {
          "content": [
            {
              "type": "text",
              "text": "我是天命小说 Agent，可以帮你推进长篇小说创作。"
            }
          ]
        }
        """;
        const string anthropicBrokenJsonText = """
        {
          "content": [
            {
              "type": "text",
              "text": "{\"action_type\":\"chat_reply\",\"reply\":\"少了右括号\""
            }
          ]
        }
        """;
        const string anthropicToolCallWithText = """
        {
          "content": [
            {
              "type": "text",
              "text": "{\"action_type\":\"chat_reply\",\"reply\":\"这段 text 不应覆盖 native tool call\"}"
            },
            {
              "type": "tool_use",
              "name": "QueryProjectStatus",
              "input": {"scope":"mission"}
            }
          ]
        }
        """;

        var openAi = ProviderToolCallingClient.ParseOpenAiActionForDiagnostics(openAiJson);
        var anthropic = ProviderToolCallingClient.ParseAnthropicActionForDiagnostics(anthropicJson);
        var mimo = ProviderToolCallingClient.ParseAnthropicActionForDiagnostics(anthropicJson, mimoCompatible: true);
        var anthropicEnvelope = ProviderToolCallingClient.ParseAnthropicActionForDiagnostics(anthropicJsonTextEnvelope);
        var mimoEnvelope = ProviderToolCallingClient.ParseAnthropicActionForDiagnostics(anthropicJsonTextEnvelope, mimoCompatible: true);
        var openAiTextTool = ProviderToolCallingClient.ParseOpenAiActionForDiagnostics(openAiJsonTextToolCall);
        var plainText = ProviderToolCallingClient.ParseAnthropicActionForDiagnostics(anthropicPlainText);
        var brokenText = ProviderToolCallingClient.ParseAnthropicActionForDiagnostics(anthropicBrokenJsonText);
        var toolCallWithText = ProviderToolCallingClient.ParseAnthropicActionForDiagnostics(anthropicToolCallWithText);

        Check.Equal("QueryProjectStatus", openAi?.ToolCall?.Name ?? string.Empty,
            "OpenAI mock tool call should parse into an AgentToolCall.");
        Check.Equal("chapter", openAi?.ToolCall?.Arguments["scope"] ?? string.Empty,
            "OpenAI mock arguments should be parsed.");
        Check.Equal("QueryProjectStatus", anthropic?.ToolCall?.Name ?? string.Empty,
            "Anthropic mock tool call should parse into an AgentToolCall.");
        Check.Equal("mimo_anthropic_tool_calling", mimo?.Source ?? string.Empty,
            "Mimo-compatible mock should preserve provider source.");
        Check.Equal(AgentActionType.ChatReply, anthropicEnvelope?.Type ?? AgentActionType.ToolCall,
            "Anthropic JSON text envelope should unpack into an AgentAction.");
        Check.Equal("free_chat", anthropicEnvelope?.Intent ?? string.Empty,
            "Anthropic JSON text envelope should preserve inner intent.");
        Check.Equal("你好！我是天命小说智能体，可以帮你管理设定、草稿和成稿。", anthropicEnvelope?.Reply ?? string.Empty,
            "Anthropic JSON text envelope should expose inner reply, not raw JSON.");
        Check.DoesNotContain("action_type", anthropicEnvelope?.Reply ?? string.Empty,
            "Anthropic JSON text envelope reply must not contain the JSON wrapper.");
        Check.Equal("mimo_anthropic_tool_calling", mimoEnvelope?.Source ?? string.Empty,
            "Mimo JSON text envelope should preserve Mimo provider source.");
        Check.Equal("QueryProjectStatus", openAiTextTool?.ToolCall?.Name ?? string.Empty,
            "OpenAI action JSON text with tool_call should unpack into AgentToolCall.");
        Check.Equal("chapter", openAiTextTool?.ToolCall?.Arguments["scope"] ?? string.Empty,
            "OpenAI action JSON text tool arguments should be preserved.");
        Check.Equal("我是天命小说 Agent，可以帮你推进长篇小说创作。", plainText?.Reply ?? string.Empty,
            "Plain provider text should remain a normal ChatReply.");
        Check.True(plainText?.IsNoTool == false,
            "Plain provider text should not be marked as planner_failed_or_no_action.");
        Check.Equal("{\"action_type\":\"chat_reply\",\"reply\":\"少了右括号\"", brokenText?.Reply ?? string.Empty,
            "Broken JSON text should degrade to normal text, not force Runtime status fallback.");
        Check.Equal("QueryProjectStatus", toolCallWithText?.ToolCall?.Name ?? string.Empty,
            "Native tool_use should win over JSON text when both are present.");
        Check.Equal("mission", toolCallWithText?.ToolCall?.Arguments["scope"] ?? string.Empty,
            "Native tool_use arguments should be preserved when text is also present.");
        return Task.CompletedTask;
    }

    private static Task HardcoreWritingEngineBuildsAnthropicMessagesUrlLikeToolClient()
    {
        var method = typeof(HardcoreWritingEngine).GetMethod(
            "BuildAnthropicMessagesUrl",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Check.True(method != null,
            "HardcoreWritingEngine should share the same Anthropic messages URL rules as planner/tool clients.");

        var customProxy = (string)method!.Invoke(null, new object[] { "https://token-plan-cn.xiaomimimo.com/anthropic" })!;
        Check.Equal("https://token-plan-cn.xiaomimimo.com/anthropic/v1/messages", customProxy,
            "Custom Anthropic-compatible proxies without /v1 should be normalized before /messages.");

        var anthropicRoot = (string)method.Invoke(null, new object[] { "https://api.anthropic.com" })!;
        Check.Equal("https://api.anthropic.com/v1/messages", anthropicRoot,
            "Anthropic root URL should target /v1/messages.");

        var anthropicV1 = (string)method.Invoke(null, new object[] { "https://api.anthropic.com/v1" })!;
        Check.Equal("https://api.anthropic.com/v1/messages", anthropicV1,
            "Anthropic v1 URL should append /messages only once.");

        var fullEndpoint = (string)method.Invoke(null, new object[] { "https://api.anthropic.com/v1/messages" })!;
        Check.Equal("https://api.anthropic.com/v1/messages", fullEndpoint,
            "Full Anthropic messages endpoint should be preserved.");

        return Task.CompletedTask;
    }

    private static Task HardcoreWritingEngineNormalizesProviderModelSuffixesLikeToolClient()
    {
        var method = typeof(HardcoreWritingEngine).GetMethod(
            "NormalizeProviderModelId",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Check.True(method != null,
            "HardcoreWritingEngine should normalize provider model ids before calling writing models.");

        var mimoLongContext = (string)method!.Invoke(null, new object[] { "mimo-v2.5-pro[1m]" })!;
        Check.Equal("mimo-v2.5-pro", mimoLongContext,
            "Long-context display suffix should not be sent to Anthropic-compatible providers.");

        var prefixedLongContext = (string)method.Invoke(null, new object[] { "anthropic/mimo-v2.5-pro[1m]" })!;
        Check.Equal("mimo-v2.5-pro", prefixedLongContext,
            "Provider prefix and display suffix should both be stripped.");

        var extendedSuffix = (string)method.Invoke(null, new object[] { "mimo-v2.5-pro:extended" })!;
        Check.Equal("mimo-v2.5-pro", extendedSuffix,
            "Extended display suffix should not be sent to model providers.");

        return Task.CompletedTask;
    }

    private static Task HardcoreWritingEngineReservesEnoughOutputTokensForChanges()
    {
        var method = typeof(HardcoreWritingEngine).GetMethod(
            "NormalizeWritingMaxTokens",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Check.True(method != null,
            "HardcoreWritingEngine should normalize writing output budget separately from short planner calls.");

        var defaultBudget = (int)method!.Invoke(null, new object[] { 4096 })!;
        Check.True(defaultBudget >= 8192,
            "Chapter writing must reserve enough output tokens for the full draft plus CHANGES.");

        var explicitLargerBudget = (int)method.Invoke(null, new object[] { 20000 })!;
        Check.Equal(20000, explicitLargerBudget,
            "Explicitly larger user output budgets should be preserved.");

        return Task.CompletedTask;
    }

    private static Task QualityReviewSuiteBlocksWeakChapterQuality()
    {
        var suite = new AgentQualityReviewSuite();
        var report = suite.Review(
            new AgentObservationContext
            {
                UserMessage = "请检查这一章",
                ProjectSummary = "读者承诺：规则悬疑与代价升级。",
                MissionPlan = new AgentMissionPlan { CurrentObjective = "推进主线冲突" },
                Rag = new AgentRagContext()
            },
            new AgentRuntimeObservation
            {
                ToolName = "ValidateChapterDraft",
                Success = true,
                Phase = "validated",
                Message = "GenerationGate validated，但正文拖沓、动机不足、冲突未推进、风格偏离。"
            },
            new AgentQualityGateReport
            {
                Status = "pass",
                Scores = new AgentQualityScores { Pacing = 8, CharacterMotivation = 8, Conflict = 8, Continuity = 8, Prose = 8, ReaderPromise = 8 }
            });

        Check.Equal("needs_rewrite", report.Status,
            "Five-reviewer arbiter must downgrade a structurally valid but weak chapter.");
        Check.True(report.ReviewReports.Count == 5,
            "Quality review should include all five reviewer reports.");
        Check.True(report.ArbiterDecision.BlockingReviewers.Count > 0,
            "Arbiter should name blocking reviewer dimensions.");
        return Task.CompletedTask;
    }

    private static Task ToolRegistryExposesToolSchemas()
    {
        var settings = CreateDbBackedSettingsManager();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:ProjectName"] = "AgentKernelRegression",
                ["NovelAgent:StorageRoot"] = Path.Combine(Path.GetTempPath(), "agent-kernel-regression-tool-schema"),
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(),
            config,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            CreateWorkspaceScopeFactory(),
            "regression-user",
            ""
            ), new UnsupportedProductionKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var registry = CreateToolRegistry(settings);
            var schemas = registry.ListToolSchemas();

            Check.True(schemas.Any(s => s.Name == "ProduceChapter" && s.RequiresConfirmation),
                "Registry should expose the closed-loop chapter production tool for provider adapters.");
            Check.True(!schemas.Any(s => s.Name == "BuildChapterContextPackage" || s.Name == "GenerateChapterWithChanges"),
                "Low-level chapter production stages must stay internal to ProduceChapter.");
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
        }
        return Task.CompletedTask;
    }

    private static Task ToolRegistrySemanticSearchTreatsPhaseAsHint()
    {
        var settings = CreateDbBackedSettingsManager();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:ProjectName"] = "AgentKernelRegression",
                ["NovelAgent:StorageRoot"] = Path.Combine(Path.GetTempPath(), "agent-kernel-regression-tool-phase"),
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(),
            config,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            CreateWorkspaceScopeFactory(),
            "regression-user",
            ""
            ), new UnsupportedProductionKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var registry = CreateToolRegistry(settings);
            var planning = registry.ListToolSchemasRankedByPhaseHint(ConversationPhase.Planning).Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var review = registry.ListToolSchemasRankedByPhaseHint(ConversationPhase.Review).Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            Check.True(planning.Contains("CommitStoryFoundation"),
                "Phase hint Planning should still include foundation commit tools.");
            Check.True(planning.Contains("ProduceChapter"),
                "Phase hint Planning must not hide the closed-loop writing tool; the model may need to choose across the global catalog.");
            Check.True(planning.Contains("AnalyzeDependencyImpact"),
                "Phase hint Planning must not hide maintenance tools; phase is ordering context, not a whitelist.");
            Check.True(review.Contains("AnalyzeDependencyImpact"),
                "Phase hint Review should still include maintenance/review flows.");
            Check.True(review.Contains("PlanStoryFoundation"),
                "Phase hint Review must not hide planning tools; the model keeps final tool choice.");
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
        }
        return Task.CompletedTask;
    }

    private static Task ToolRegistryDeclaresSideEffectsForEveryTool()
    {
        var settings = CreateDbBackedSettingsManager();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:ProjectName"] = "AgentKernelRegression",
                ["NovelAgent:StorageRoot"] = Path.Combine(Path.GetTempPath(), "agent-kernel-regression-tool-effects"),
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment(),
            config,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            CreateWorkspaceScopeFactory(),
            "regression-user",
            ""
            ), new UnsupportedProductionKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var registry = CreateToolRegistry(settings);
            var tools = registry.ListTools();

            Check.True(tools.Count >= 19, "Registry should expose the full tool set.");
            Check.True(tools.All(t => t.SideEffects.WritesLedger && t.SideEffects.WritesRedisRecentCache),
                "Every tool must declare SQLite ledger and Redis recent hot-cache writes.");

            var toolSearch = tools.Single(t => t.Name == "tool_search");
            Check.True(toolSearch.SideEffects.WritesToolSearchCache && toolSearch.SideEffects.WritesSqliteSnapshot,
                "tool_search must declare Memory/Redis cache and SQLite snapshot writes.");

            var knowledge = tools.Single(t => t.Name == "ProcessKnowledgeFile");
            Check.True(knowledge.SideEffects.WritesSqliteEntities.Contains("knowledge_base"),
                "ProcessKnowledgeFile must declare knowledge_base writes.");
            Check.True(knowledge.SideEffects.WritesVectorIndexes.Contains("knowledge"),
                "ProcessKnowledgeFile must declare Qdrant knowledge index writes.");

            var produceChapter = tools.Single(t => t.Name == "ProduceChapter");
            Check.True(produceChapter.SideEffects.WritesSqliteEntities.Contains("chapters"),
                "ProduceChapter must declare chapter truth writes.");
            Check.True(produceChapter.SideEffects.WritesVectorIndexes.Contains("chapter"),
                "ProduceChapter must declare chapter vector index writes.");
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
        }
        return Task.CompletedTask;
    }

    private static Task ToolRegistryScopedWorkspaceOverridesStaleAmbientWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "agent-kernel-regression-tool-scope-" + Guid.NewGuid().ToString("N"));
        var settings = CreateDbBackedSettingsManager();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:ProjectName"] = "AgentKernelRegression",
                ["NovelAgent:StorageRoot"] = root,
            })
            .Build();
        var staleWorkspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment { ContentRootPath = root, WebRootPath = root },
            config,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            CreateWorkspaceScopeFactory("user-scope", "temp-user-scope"),
            "user-scope",
            "temp-user-scope"
            ), new UnsupportedProductionKernel());
        var staleCatalog = CreateDbBackedCatalog(staleWorkspace);
        AgentToolRegistry.SetWorkspace(staleWorkspace, staleCatalog);

        var realWorkspace = WithTestProductionKernel(new NovelAgentWorkspace(
            new TestWebHostEnvironment { ContentRootPath = root, WebRootPath = root },
            config,
            settings,
            new WorkspaceProductionRuntimeBuilder(),
            CreateWorkspaceScopeFactory("user-scope", "project-real-scope"),
            "user-scope",
            "project-real-scope"
            ), new UnsupportedProductionKernel());
        var realCatalog = CreateDbBackedCatalog(realWorkspace);
        var registry = CreateToolRegistry(settings);
        registry.SetWorkspaceContext(realWorkspace, realCatalog);
        realWorkspace.SetRequestContext();
        try
        {
            Check.Equal("project-real-scope", registry.CurrentWorkspaceProjectIdForTests(),
                "Instance-scoped workspace should override stale static AsyncLocal workspace when executing tools.");
        }
        finally
        {
            realWorkspace.ClearRequestContext();
            registry.ClearWorkspaceContext();
            AgentToolRegistry.ClearWorkspace();
        }
        return Task.CompletedTask;
    }

    private static async Task SearchCreativeKnowledgeReturnsDbKnowledge()
    {
        var root = Path.Combine(Path.GetTempPath(), "agent-kernel-regression-db-knowledge-" + Guid.NewGuid().ToString("N"));
        var settings = CreateDbBackedSettingsManager();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:ProjectName"] = "AgentKernelRegression",
                ["NovelAgent:StorageRoot"] = root,
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(new TestWebHostEnvironment { ContentRootPath = root, WebRootPath = root }, config, settings, new WorkspaceProductionRuntimeBuilder(),
            CreateWorkspaceScopeFactory(),
            "regression-user",
            ""
            ), new UnsupportedProductionKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var knowledgeService = new FixedKnowledgeService(new KnowledgeSearchResult
        {
            Id = "db-knowledge-001",
            EntryType = nameof(CreativeKnowledgeCategory.ReaderPromise),
            Title = "数据库知识条目",
            Content = "上传知识要求主角每次胜利都付出清晰代价。",
            Score = 3.5f
        });
        var registry = new AgentToolRegistry(
            settings,
            CreateToolServiceProvider(knowledgeService),
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            SessionId = "session-db-knowledge",
            ActiveProjectId = "project-db-knowledge"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "SearchCreativeKnowledge",
                    Arguments = { ["query"] = "主角胜利代价" }
                },
                session,
                new StoryBibleDocument(),
                confirmed: false,
                CancellationToken.None);

            Check.True(result.Success, "SearchCreativeKnowledge should succeed when DB knowledge service is available.");
            Check.Contains("数据库知识条目", result.Message, "Agent tool message should include DB-created knowledge title.");
            Check.Contains("清晰代价", result.Message, "Agent tool message should include DB-created knowledge content.");

            var data = result.Data as CreativeKnowledgeRetrievalResult;
            Check.True(data?.Hits.Any(h => h.Entry.Id == "db-knowledge-001" && h.Entry.Source == "DBKnowledge") == true,
                "Agent tool data should merge DB knowledge into creative knowledge hits.");
            Check.Equal("project-db-knowledge", knowledgeService.LastRequest?.ProjectId ?? string.Empty,
                "DB knowledge search should use the active session project id.");
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
        }
    }

    private static async Task SearchCreativeKnowledgeReturnsProjectMaterialChunks()
    {
        var root = Path.Combine(Path.GetTempPath(), "agent-kernel-regression-material-knowledge-" + Guid.NewGuid().ToString("N"));
        var settings = CreateDbBackedSettingsManager();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:ProjectName"] = "AgentKernelRegression",
                ["NovelAgent:StorageRoot"] = root,
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(new TestWebHostEnvironment { ContentRootPath = root, WebRootPath = root }, config, settings, new WorkspaceProductionRuntimeBuilder(),
            CreateWorkspaceScopeFactory(),
            "regression-user",
            ""
            ), new UnsupportedProductionKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var vectorStore = new RecordingProjectVectorStore(new SearchResult
        {
            Id = "vector-material-001",
            ProjectId = "project-material-knowledge",
            SourceType = "material",
            SourceId = "material-001",
            Content = "潮汐墨印只能标记潮湿墙面的隐藏门，不能攻击、治疗或升级。",
            Score = 0.96f
        });
        var semanticSearch = new SemanticSearchService(
            vectorStore,
            new AgentKernelFixedEmbeddingService(),
            NullLogger<SemanticSearchService>.Instance);
        var registry = new AgentToolRegistry(
            settings,
            CreateToolServiceProvider(new FixedKnowledgeService(), semanticSearch),
            NullLogger<AgentToolRegistry>.Instance);
        var session = new AgentSession
        {
            UserId = "regression-user",
            SessionId = "session-material-knowledge",
            ActiveProjectId = "project-material-knowledge"
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        workspace.SetRequestContext();
        try
        {
            var result = await registry.ExecuteAsync(
                new AgentToolCall
                {
                    Name = "SearchCreativeKnowledge",
                    Arguments = { ["query"] = "潮汐墨印隐藏门" }
                },
                session,
                new StoryBibleDocument(),
                confirmed: false,
                CancellationToken.None);

            Check.True(result.Success, "SearchCreativeKnowledge should succeed when project material vectors are available.");
            Check.Contains("项目素材", result.Message, "Agent tool message should label project material hits.");
            Check.Contains("潮汐墨印", result.Message, "Agent tool message should include material chunk content.");
            Check.Equal("project-material-knowledge", vectorStore.LastFilters?["project_id"]?.ToString() ?? string.Empty,
                "Project material search must stay scoped to the active project id.");

            var data = result.Data as CreativeKnowledgeRetrievalResult;
            Check.True(data?.Hits.Any(h => h.Entry.Id == "material-001" && h.Entry.Source == "ProjectMaterial") == true,
                "Agent tool data should merge project material chunks into creative knowledge hits.");
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
        }
    }

    private static async Task ResolveNovelProjectIsIdempotentWhileAwaitingFoundation()
    {
        var root = Path.Combine(Path.GetTempPath(), "agent-kernel-regression-start-project-" + Guid.NewGuid().ToString("N"));
        var settings = CreateDbBackedSettingsManager();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:ProjectName"] = "AgentKernelRegression",
                ["NovelAgent:StorageRoot"] = root,
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(new TestWebHostEnvironment { ContentRootPath = root, WebRootPath = root }, config, settings, new WorkspaceProductionRuntimeBuilder(),
            CreateWorkspaceScopeFactory(),
            "regression-user",
            ""
            ), new UnsupportedProductionKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var session = new AgentSession { SessionId = "session-idempotent" };
        var call = new AgentToolCall
        {
            Name = "ResolveNovelProject",
            Arguments =
            {
                ["mode"] = "create_new",
                ["seed"] = "写一本斗罗大陆风格的玄幻学院流小说",
                ["genre"] = "玄幻"
            }
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        try
        {
            workspace.SetRequestContext();
            var registry = CreateToolRegistry(settings);
            var first = await registry.ExecuteAsync(call, session, new StoryBibleDocument(), confirmed: false, CancellationToken.None);
            var countAfterFirst = (await catalog.GetAsync()).Projects.Count;
            var second = await registry.ExecuteAsync(call, session, new StoryBibleDocument(), confirmed: false, CancellationToken.None);
            var countAfterSecond = (await catalog.GetAsync()).Projects.Count;

            Check.True(first.Success, "First ResolveNovelProject call should create the project.");
            Check.True(second.Success, "Idempotent ResolveNovelProject call should return existing project state.");
            Check.Equal(countAfterFirst, countAfterSecond,
                "Second ResolveNovelProject call while awaiting foundation must not create another project.");
            Check.Equal("existing_novel_project", second.Artifact?.ArtifactType ?? string.Empty,
                "Second call should return an existing-project artifact.");
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
        }
    }

    private static async Task ResolveNovelProjectCreateNewDoesNotBindActiveOldProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "agent-kernel-regression-create-new-project-" + Guid.NewGuid().ToString("N"));
        var settings = CreateDbBackedSettingsManager();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:ProjectName"] = "AgentKernelRegression",
                ["NovelAgent:StorageRoot"] = root,
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(new TestWebHostEnvironment { ContentRootPath = root, WebRootPath = root }, config, settings, new WorkspaceProductionRuntimeBuilder(),
            CreateWorkspaceScopeFactory(),
            "regression-user",
            ""
            ), new UnsupportedProductionKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var oldProject = await catalog.CreateAsync(new NovelProjectCreateRequest(
            "末世觉醒：系统在手，美女我有",
            "末世",
            "旧项目"), CancellationToken.None);
        await catalog.ActivateAsync(oldProject.Id, CancellationToken.None);
        var session = new AgentSession
        {
            SessionId = "session-create-new",
            ActiveProjectId = oldProject.Id,
            Phase = "idle"
        };
        var call = new AgentToolCall
        {
            Name = "ResolveNovelProject",
            Arguments =
            {
                ["mode"] = "create_new",
                ["title"] = "废土神国：我靠吞噬怪物升级",
                ["seed"] = "新的末世玄幻爽文，系统吞噬怪物晶核升级，建基地，打怪升级。",
                ["genre"] = "末世玄幻"
            }
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        try
        {
            workspace.SetRequestContext();
            var registry = CreateToolRegistry(settings);
            var result = await registry.ExecuteAsync(call, session, new StoryBibleDocument(), confirmed: false, CancellationToken.None);
            var projects = await catalog.GetAsync(CancellationToken.None);
            var created = projects.Projects.SingleOrDefault(p => p.Title == "废土神国：我靠吞噬怪物升级");

            Check.True(result.Success, "Explicit create_new should succeed.");
            Check.True(created != null, "Explicit create_new with a new title should create the requested new project.");
            Check.Equal(created!.Id, session.ActiveProjectId,
                "The current session should bind to the newly created project, not the previously active old project.");
            Check.True(!string.Equals(oldProject.Id, session.ActiveProjectId, StringComparison.OrdinalIgnoreCase),
                "Explicit new novel requests must not silently reuse the old active project.");
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
        }
    }

    private static async Task ResolveNovelProjectCreateNewHonorsProjectTitle()
    {
        var root = Path.Combine(Path.GetTempPath(), "agent-kernel-regression-create-new-project-title-" + Guid.NewGuid().ToString("N"));
        var settings = CreateDbBackedSettingsManager();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:ProjectName"] = "AgentKernelRegression",
                ["NovelAgent:StorageRoot"] = root,
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(new TestWebHostEnvironment { ContentRootPath = root, WebRootPath = root }, config, settings, new WorkspaceProductionRuntimeBuilder(),
            CreateWorkspaceScopeFactory(),
            "regression-user",
            ""
            ), new UnsupportedProductionKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var oldProject = await catalog.CreateAsync(new NovelProjectCreateRequest(
            "星骸武神：我吞噬星兽进化",
            "末世星际",
            "旧项目"), CancellationToken.None);
        await catalog.ActivateAsync(oldProject.Id, CancellationToken.None);
        var session = new AgentSession
        {
            SessionId = "session-create-new-project-title",
            ActiveProjectId = oldProject.Id,
            Phase = "idle"
        };
        var call = new AgentToolCall
        {
            Name = "ResolveNovelProject",
            Arguments =
            {
                ["mode"] = "create_new",
                ["projectTitle"] = "黑潮领主：我吞噬异兽晶核升级",
                ["seed"] = "末世玄幻爽文，男主从海港贫民窟幸存者开始，系统吞噬异兽晶核升级。",
                ["genre"] = "末世玄幻"
            }
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        try
        {
            workspace.SetRequestContext();
            var registry = CreateToolRegistry(settings);
            var result = await registry.ExecuteAsync(call, session, new StoryBibleDocument(), confirmed: false, CancellationToken.None);
            var projects = await catalog.GetAsync(CancellationToken.None);
            var created = projects.Projects.SingleOrDefault(p => p.Title == "黑潮领主：我吞噬异兽晶核升级");

            Check.True(result.Success, "Explicit create_new with projectTitle should succeed.");
            Check.True(created != null, "projectTitle should be treated as the requested new title when mode=create_new.");
            Check.Equal(created!.Id, session.ActiveProjectId,
                "The session should bind to the requested new projectTitle project.");
            Check.True(!string.Equals(oldProject.Id, session.ActiveProjectId, StringComparison.OrdinalIgnoreCase),
                "Explicit create_new must not bind the old active project just because projectTitle is present.");
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
        }
    }

    private static async Task ResolveNovelProjectFoundationReadinessRequiresExplicitModelFlag()
    {
        var root = Path.Combine(Path.GetTempPath(), "agent-kernel-regression-complete-brief-" + Guid.NewGuid().ToString("N"));
        var settings = CreateDbBackedSettingsManager();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:ProjectName"] = "AgentKernelRegression",
                ["NovelAgent:StorageRoot"] = root,
            })
            .Build();
        var workspace = WithTestProductionKernel(new NovelAgentWorkspace(new TestWebHostEnvironment { ContentRootPath = root, WebRootPath = root }, config, settings, new WorkspaceProductionRuntimeBuilder(),
            CreateWorkspaceScopeFactory(),
            "regression-user",
            ""
            ), new UnsupportedProductionKernel());
        var catalog = CreateDbBackedCatalog(workspace);
        var session = new AgentSession
        {
            SessionId = "session-complete-brief",
            Phase = "idle"
        };
        var call = new AgentToolCall
        {
            Name = "ResolveNovelProject",
            Arguments =
            {
                ["mode"] = "create_new",
                ["projectTitle"] = "霜铁荒原：从流放矿工到星甲战神",
                ["seed"] = "打怪升级爽文。靠爆材料、改装战甲、升级和扩大地图变强。需要世界观、升级体系、核心爽点循环、前三卷方向和首批角色。",
                ["genre"] = "废土机甲"
            }
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        try
        {
            workspace.SetRequestContext();
            var registry = CreateToolRegistry(settings);
            var result = await registry.ExecuteAsync(call, session, new StoryBibleDocument(), confirmed: false, CancellationToken.None);

            Check.True(result.Success, "Complete new-novel brief should create the project.");
            Check.Equal("awaiting_user_foundation", result.Phase,
                "ResolveNovelProject must not infer foundation readiness by parsing natural-language seed completeness.");
            Check.Equal("foundation_intake", session.WorkingMemory.Mission.CreativePhase,
                "Without an explicit model flag, the session remains in foundation intake.");
            Check.Equal("ask_foundation_question", session.WorkingMemory.Mission.NextIntent,
                "Without an explicit model flag, the tool should not locally choose foundation planning.");
            Check.True(session.WorkingMemory.OpenQuestions.Count > 0,
                "Without an explicit model flag, the tool may keep a foundation question instead of guessing readiness.");

            call.Arguments["foundationBriefReady"] = "true";
            var ready = await registry.ExecuteAsync(call, session, new StoryBibleDocument(), confirmed: false, CancellationToken.None);

            Check.True(ready.Success, "Explicit foundationBriefReady should succeed.");
            Check.Equal("foundation_ready", ready.Phase,
                "Only the explicit model-provided foundationBriefReady flag should mark the project ready.");
            Check.Equal("ready_for_foundation_planning", session.WorkingMemory.Mission.CreativePhase,
                "Explicit readiness should update the session mission for foundation planning.");
            Check.Equal("plan_story_foundation", session.WorkingMemory.Mission.NextIntent,
                "Explicit readiness should guide the LLM toward foundation planning without hard-routing a tool call.");
            Check.Equal(0, session.WorkingMemory.OpenQuestions.Count,
                "Explicit readiness should clear stale intake questions.");
            Check.Contains("故事地基候选", ready.Message,
                "User-visible project confirmation should say the agent can proceed to foundation candidates after explicit readiness.");
            Check.Contains("生成故事地基候选", string.Join(" ", ready.Suggestions),
                "Suggestions should point to the product action after explicit readiness.");
        }
        finally
        {
            workspace.ClearRequestContext();
            AgentToolRegistry.ClearWorkspace();
        }
    }

    private static async Task KnowledgeUsageRemainsProjectScoped()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new NovelAgentDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Users.Add(new DbUser { Id = "user-scope", Username = "scope", Email = "scope@example.com", PasswordHash = "hash", Role = "author" });
        db.NovelProjects.Add(new DbNovelProject { Id = "project-a", UserId = "user-scope", Title = "A" });
        db.NovelProjects.Add(new DbNovelProject { Id = "project-b", UserId = "user-scope", Title = "B" });
        db.KnowledgeBases.Add(new DbKnowledgeBase
        {
            Id = "knowledge-shared",
            UserId = "user-scope",
            SourceProjectId = "project-a",
            EntryType = "ReaderPromise",
            Title = "胜利代价",
            Content = "胜利必须付出代价。",
            SourceType = "upload",
            Weight = 8
        });
        await db.SaveChangesAsync();

        var memoryRepository = new AgentMemoryRepository(
            db,
            new NoopDistributedCacheService(),
            new DirectMemoryCacheService(),
            NullLogger<AgentMemoryRepository>.Instance,
            new ProductionTruthStore(db));
        var usageService = new ProjectKnowledgeUsageService(
            db,
            new NoopMemoryEventService(),
            memoryRepository,
            NullLogger<ProjectKnowledgeUsageService>.Instance);
        var contextService = new AgentMemoryContextService(new EmptyChatHistoryRepository(), memoryRepository);

        await usageService.MarkReferencedAsync("user-scope", "project-a", "knowledge-shared", "session-a", "run-a");
        await usageService.MarkImportedAsync("user-scope", "project-b", "knowledge-shared", "session-b", "upload");

        var projectA = await contextService.BuildAsync("user-scope", "project-a", "session-a");
        var projectB = await contextService.BuildAsync("user-scope", "project-b", "session-b");

        Check.True(projectA.Project.ReferencedKnowledgeIds.Contains("knowledge-shared"),
            "Project A should remember that the knowledge was referenced.");
        Check.True(projectB.Project.ImportedKnowledgeIds.Contains("knowledge-shared"),
            "Project B should remember imported knowledge for its own project.");
        Check.True(!projectB.Project.ReferencedKnowledgeIds.Contains("knowledge-shared"),
            "Project B must not inherit Project A referenced knowledge state.");
        Check.Equal("referenced", projectA.Project.KnowledgeInventory.Single(x => x.KnowledgeId == "knowledge-shared").ProjectUsageStatus,
            "Project A inventory should carry referenced usage status.");
        Check.Equal("imported", projectB.Project.KnowledgeInventory.Single(x => x.KnowledgeId == "knowledge-shared").ProjectUsageStatus,
            "Project B inventory should carry imported usage status.");
    }

    private static AgentMissionPlan BuildPlanWithChapter(string runId, Action<AgentChapterTask> configure)
    {
        var chapter = new AgentChapterTask
        {
            ChapterId = runId.Replace("run-", string.Empty),
            RunId = runId,
            Status = "validated",
            GateStatus = "validated",
            QualityStatus = "quality_passed",
            NextAction = "ProduceChapter"
        };
        configure(chapter);
        return new AgentMissionPlan
        {
            BookTaskTree = new AgentBookTaskTree
            {
                Volumes =
                {
                    new AgentVolumeTask
                    {
                        VolumeId = "volume-001",
                        Chapters = { chapter }
                    }
                }
            }
        };
    }

    private static UserSettingsManager CreateDbBackedSettingsManager(
        string userId = "regression-user",
        UserSettings? settings = null)
    {
        var services = new ServiceCollection();
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        services.AddSingleton(connection);
        services.AddSingleton<ILlmApiKeyProtector>(
            new DataProtectionLlmApiKeyProtector(new EphemeralDataProtectionProvider()));
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            db.Database.EnsureCreated();
            db.Users.Add(new DbUser
            {
                Id = userId,
                Username = $"author-{userId}",
                Email = $"{userId}@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
            db.SaveChanges();
        }

        var manager = new UserSettingsManager(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new HttpContextAccessor(),
            new FixedBackgroundUserContext(userId),
            provider.GetRequiredService<ILlmApiKeyProtector>());

        manager.SaveAsync(settings ?? new UserSettings
        {
            LlmProvider = "openai",
            LlmBaseUrl = string.Empty,
            LlmModel = string.Empty,
            LlmApiKey = string.Empty
        }).GetAwaiter().GetResult();
        return manager;
    }

    private static AgentToolRegistry CreateToolRegistry(UserSettingsManager settings)
    {
        return new AgentToolRegistry(settings, CreateToolServiceProvider(), NullLogger<AgentToolRegistry>.Instance);
    }

    private static NovelProjectCatalog CreateDbBackedCatalog(NovelAgentWorkspace workspace)
    {
        return new NovelProjectCatalog(workspace, workspace.ScopeFactory);
    }

    private static IServiceScopeFactory CreateWorkspaceScopeFactory(
        string userId = "regression-user",
        string projectId = "")
    {
        var services = new ServiceCollection();
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        services.AddSingleton(connection);
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseSqlite(connection));
        services.AddSingleton<IDistributedCacheService, NoopDistributedCacheService>();
        services.AddSingleton<IMemoryCacheService, DirectMemoryCacheService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        db.Database.EnsureCreated();
        db.Users.Add(new DbUser
        {
            Id = userId,
            Username = $"author-{userId}",
            Email = $"{userId}@example.com",
            PasswordHash = "hash",
            Role = "author"
        });
        if (!string.IsNullOrWhiteSpace(projectId))
        {
            db.NovelProjects.Add(new DbNovelProject
            {
                Id = projectId,
                UserId = userId,
                Title = projectId,
                Status = "draft",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        db.SaveChanges();

        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    private static NovelAgentWorkspace WithTestProductionKernel(
        NovelAgentWorkspace workspace,
        ITianmingProductionKernel productionKernel)
    {
        var field = typeof(NovelAgentOrchestrator)
            .GetField("_productionKernel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Check.True(field != null, "NovelAgentOrchestrator must keep a production kernel field for regression inspection.");
        field!.SetValue(workspace.Orchestrator, productionKernel);
        return workspace;
    }

    private static ServiceProvider CreateToolServiceProvider(
        IKnowledgeService? knowledgeService = null,
        SemanticSearchService? semanticSearchService = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAgentToolExecutionLedger, RecordingAgentToolExecutionLedger>();
        if (knowledgeService != null)
            services.AddSingleton(knowledgeService);
        if (semanticSearchService != null)
            services.AddSingleton(semanticSearchService);

        return services.BuildServiceProvider();
    }
}

internal sealed class RecordingAgentToolExecutionLedger : IAgentToolExecutionLedger
{
    private readonly Dictionary<string, TM.Web.NovelAgentWeb.Data.Entities.AgentToolExecution> _executions = new(StringComparer.OrdinalIgnoreCase);

    public Task<TM.Web.NovelAgentWeb.Data.Entities.AgentToolExecution> StartAsync(
        AgentToolExecutionStart start,
        CancellationToken ct = default)
    {
        var execution = new TM.Web.NovelAgentWeb.Data.Entities.AgentToolExecution
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = start.UserId,
            ProjectId = start.ProjectId,
            SessionId = start.SessionId,
            RunId = start.RunId,
            ToolName = start.Call.Name,
            Phase = start.Phase,
            Risk = start.Risk,
            ArgumentsHash = Guid.NewGuid().ToString("N"),
            Status = "running",
            StartedAt = DateTime.UtcNow
        };
        _executions[execution.Id] = execution;
        return Task.FromResult(execution);
    }

    public Task RebindProjectAsync(string executionId, string projectId, CancellationToken ct = default)
    {
        if (_executions.TryGetValue(executionId, out var execution))
            execution.ProjectId = projectId;
        return Task.CompletedTask;
    }

    public Task CompleteAsync(string executionId, AgentToolExecutionResult result, CancellationToken ct = default)
    {
        if (_executions.TryGetValue(executionId, out var execution))
        {
            execution.Status = result.Success ? "completed" : "failed";
            execution.ResultPhase = result.Phase;
            execution.ResultMessage = result.Message;
            execution.CompletedAt = DateTime.UtcNow;
        }

        return Task.CompletedTask;
    }

    public Task<int> FailRunningForSessionAsync(
        string userId,
        string sessionId,
        string? projectId,
        string reason,
        CancellationToken ct = default) =>
        Task.FromResult(0);

    public Task<int> FailAllRunningAsync(string reason, CancellationToken ct = default) =>
        Task.FromResult(0);

    public Task<IReadOnlyList<AgentToolExecutionSnapshot>> GetRecentAsync(
        string userId,
        string sessionId,
        string? projectId,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AgentToolExecutionSnapshot>>(Array.Empty<AgentToolExecutionSnapshot>());
}

internal sealed class FixedBackgroundUserContext : IBackgroundUserContext
{
    public FixedBackgroundUserContext(string userId)
    {
        Current = new BackgroundUserSnapshot(userId, "author", "author@example.com", "author");
    }

    public BackgroundUserSnapshot? Current { get; }

    public IDisposable Push(string userId, string username = "background-agent", string email = "", string role = "author") =>
        new NoopDisposable();

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

internal sealed class FixedKnowledgeService : IKnowledgeService
{
    private readonly IReadOnlyList<KnowledgeSearchResult> _results;

    public FixedKnowledgeService(params KnowledgeSearchResult[] results)
    {
        _results = results;
    }

    public SearchKnowledgeRequest? LastRequest { get; private set; }

    public Task<KnowledgeResponse> CreateKnowledgeAsync(CreateKnowledgeRequest request, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<KnowledgeResponse> CreateExtractedKnowledgeAsync(CreateExtractedKnowledgeRequest request, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<List<KnowledgeResponse>> ListKnowledgeAsync(string projectId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<List<KnowledgeDirectoryResponse>> ListKnowledgeDirectoriesAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<KnowledgeDirectoryResponse>());

    public Task<KnowledgeDirectoryResponse> CreateKnowledgeDirectoryAsync(CreateKnowledgeDirectoryRequest request, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<KnowledgeDirectoryResponse> UpdateKnowledgeDirectoryAsync(string key, UpdateKnowledgeDirectoryRequest request, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task DeleteKnowledgeDirectoryAsync(string key, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<KnowledgeResponse> GetKnowledgeAsync(string knowledgeId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<KnowledgeResponse> UpdateKnowledgeAsync(string knowledgeId, UpdateKnowledgeRequest request, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task DeleteKnowledgeAsync(string knowledgeId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<List<KnowledgeSearchResult>> SearchKnowledgeAsync(SearchKnowledgeRequest request, CancellationToken ct = default)
    {
        LastRequest = request;
        return Task.FromResult(_results.ToList());
    }

    public Task IncrementUsageAsync(
        string knowledgeId,
        string projectId,
        string? sessionId = null,
        string? runId = null,
        string? idempotencyKey = null,
        CancellationToken ct = default) =>
        Task.CompletedTask;
}

internal sealed class AgentKernelFixedEmbeddingService : IMicroEmbeddingService
{
    public int Dimension => 3;

    public Task<float[]> EncodeAsync(string text, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default) =>
        Task.FromResult(new[] { 0.1f, 0.2f, 0.3f });

    public Task<float[][]> EncodeBatchAsync(IReadOnlyList<string> texts, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default) =>
        Task.FromResult(texts.Select(_ => new[] { 0.1f, 0.2f, 0.3f }).ToArray());

    public void ReleaseSession()
    {
    }

    public bool IsModelReady() => true;
}

internal sealed class RecordingProjectVectorStore : IVectorStore
{
    private readonly SearchResult _result;

    public RecordingProjectVectorStore(SearchResult result)
    {
        _result = result;
    }

    public Dictionary<string, object>? LastFilters { get; private set; }

    public Task InitializeUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;

    public Task UpsertVectorsAsync(string userId, List<VectorData> vectors, CancellationToken ct = default) => Task.CompletedTask;

    public Task<List<SearchResult>> SearchSimilarAsync(
        string userId,
        float[] queryVector,
        int topK = 10,
        Dictionary<string, object>? filters = null,
        CancellationToken ct = default)
    {
        LastFilters = filters == null
            ? null
            : new Dictionary<string, object>(filters, StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(new List<SearchResult> { _result });
    }

    public Task DeleteUserCollectionAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> CollectionExistsAsync(string userId, CancellationToken ct = default) => Task.FromResult(true);

    public Task<CollectionInfo?> GetCollectionInfoAsync(string userId, CancellationToken ct = default) =>
        Task.FromResult<CollectionInfo?>(null);

    public Task DeleteVectorsByFilterAsync(
        string userId,
        Dictionary<string, object> filters,
        CancellationToken ct = default) =>
        Task.CompletedTask;
}

internal sealed class NoopDistributedCacheService : IDistributedCacheService
{
    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class =>
        Task.FromResult<T?>(null);

    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken ct = default) where T : class =>
        Task.CompletedTask;

    public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
    public Task RemoveByPrefixAsync(string keyPrefix, CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(false);
}

internal sealed class DirectMemoryCacheService : IMemoryCacheService
{
    public async Task<T?> GetOrSetAsync<T>(
        string key,
        Func<Task<T>> factory,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
    {
        return await factory();
    }

    public T? Get<T>(string key) => default;
    public void Set<T>(string key, T value, TimeSpan expiration) { }
    public void Remove(string key) { }
    public void RemoveByPrefix(string keyPrefix) { }
}

internal sealed class NoopMemoryEventService : IAgentMemoryEventService
{
    public Task AppendAsync(
        string userId,
        string? projectId,
        string? sessionId,
        string? runId,
        string sourceType,
        string triggerType,
        string memoryScope,
        string memoryKey,
        object payload,
        CancellationToken ct = default) =>
        Task.CompletedTask;
}

internal sealed class EmptyChatHistoryRepository : IChatHistoryRepository
{
    public Task AppendAsync(string userId, string? projectId, string sessionId, string role, string content, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<bool> ReplaceLastAssistantTurnAsync(
        string userId,
        string? projectId,
        string sessionId,
        string expectedContent,
        string replacementContent,
        CancellationToken ct = default) =>
        Task.FromResult(false);

    public Task SaveSummaryAsync(
        string userId,
        string? projectId,
        string sessionId,
        int startTurn,
        int endTurn,
        string summaryType,
        string content,
        IReadOnlyList<string> keyDecisions,
        CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<ChatPromptWindowDto> GetPromptWindowAsync(string userId, string? projectId, string sessionId, CancellationToken ct = default) =>
        Task.FromResult(new ChatPromptWindowDto(null, Array.Empty<ChatHistorySummaryDto>(), Array.Empty<ChatHistoryTurnDto>()));

    public Task<IReadOnlyList<ChatHistoryTurnDto>> GetHotWindowAsync(string userId, string? projectId, string sessionId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ChatHistoryTurnDto>>(Array.Empty<ChatHistoryTurnDto>());
}

internal sealed class SlowHandler : HttpMessageHandler
{
    private readonly TimeSpan _delay;

    public SlowHandler(TimeSpan delay)
    {
        _delay = delay;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(_delay, cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"{}\"}}]}")
        };
    }
}

internal sealed class UnsupportedProductionKernel : ITianmingProductionKernel
{
    public Task<ChapterContextPackageSummary> BuildContextPackageAsync(
        NovelAgentRun run,
        StoryBibleDocument document,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Agent kernel regression workspace tests do not execute the production kernel.");

    public Task<ChapterDraftArtifact> GenerateDraftWithChangesAsync(
        NovelAgentRun run,
        ChapterContextPackageSummary package,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Agent kernel regression workspace tests do not execute the production kernel.");

    public Task<GenerationGateReport> ValidateDraftAsync(
        NovelAgentRun run,
        ChapterContextPackageSummary package,
        ChapterDraftArtifact draft,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Agent kernel regression workspace tests do not execute the production kernel.");

    public Task<ChapterDraftArtifact> RepairDraftAsync(
        NovelAgentRun run,
        ChapterContextPackageSummary package,
        ChapterDraftArtifact draft,
        GenerationGateReport gate,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Agent kernel regression workspace tests do not execute the production kernel.");

    public Task<DependencyImpactReport> CommitChapterAsync(
        NovelAgentRun run,
        ChapterContextPackageSummary package,
        ChapterDraftArtifact draft,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Agent kernel regression workspace tests do not execute the production kernel.");

    public Task<GenerationGateReport> AuditCommittedChapterAsync(
        string chapterId,
        string committedContent,
        ChapterContextPackageSummary contextPackage,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Agent kernel regression workspace tests do not execute the production kernel.");

    public Task<NovelAgentExecutionResult> ReviseCommittedChapterAsync(
        string chapterId,
        string committedContent,
        ChapterContextPackageSummary contextPackage,
        string revisionGoal,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Agent kernel regression workspace tests do not execute the production kernel.");

    public DependencyImpactReport RefreshIndexesAndAnalyzeImpact(
        NovelAgentRun run,
        ChapterDraftArtifact draft) =>
        throw new NotSupportedException("Agent kernel regression workspace tests do not execute the production kernel.");

    public string StripChanges(string content) => content;
}

internal sealed class TestWebHostEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = "Regression";
    public string ApplicationName { get; set; } = "AgentKernelRegression";
    public string WebRootPath { get; set; } = Path.GetTempPath();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = Path.GetTempPath();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

internal sealed class RegressionAssertException : Exception
{
    public RegressionAssertException(string message) : base(message)
    {
    }
}

internal static class Check
{
    public static void True(bool condition, string message)
    {
        if (!condition)
            throw new RegressionAssertException(message);
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new RegressionAssertException($"{message} Expected: {expected}; Actual: {actual}");
    }

    public static void Contains(string expectedFragment, string actual, string message)
    {
        if (actual?.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase) != true)
            throw new RegressionAssertException($"{message} Missing fragment: {expectedFragment}; Actual: {actual}");
    }

    public static void DoesNotContain(string unexpectedFragment, string actual, string message)
    {
        if (actual?.Contains(unexpectedFragment, StringComparison.OrdinalIgnoreCase) == true)
            throw new RegressionAssertException($"{message} Unexpected fragment: {unexpectedFragment}; Actual: {actual}");
    }
}
