using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Tests.AgentKernelRegression;

internal static class Program
{
    private static readonly List<(string Name, Func<Task> Test)> Tests = new()
    {
        ("ConversationKernel routes status queries away from creation tools", ConversationKernelRoutesStatusQuery),
        ("ToolPolicy blocks raw userGoal PlanChapter", ToolPolicyBlocksRawUserGoalPlanChapter),
        ("AgentSession clears removed GenerateChapter pending calls", AgentSessionClearsRemovedLegacyPending),
        ("ToolPolicy preflights confirmed story foundation commit", ToolPolicyPreflightsConfirmedStoryFoundationCommit),
        ("ToolPolicy blocks commit when quality gate still has issues", ToolPolicyBlocksCommitWithQualityIssues),
        ("Scheduler continue uses active blackboard task", SchedulerContinueUsesActiveBlackboardTask),
        ("Scheduler continue uses repairable policy observations", SchedulerContinueUsesRepairablePolicyObservation),
        ("ToolPolicy repairs missing context package before draft generation", ToolPolicyRepairsMissingContextPackageBeforeDraft),
        ("ToolPolicy repairs missing draft before validation", ToolPolicyRepairsMissingDraftBeforeValidation),
        ("ToolPolicy keeps hard boundaries terminal", ToolPolicyKeepsHardBoundariesTerminal),
        ("ToolPolicy blocks draft generation when context rebuild is required", ToolPolicyBlocksDraftWhenContextRebuildRequired),
        ("ToolPolicy blocks commit when revalidation is required", ToolPolicyBlocksCommitWhenRevalidationRequired),
        ("Mission blackboard recovery rebuilds scheduler state", MissionBlackboardRecoveryRebuildsSchedulerState),
        ("Mission blackboard exposes drafts before library commit", MissionBlackboardExposesDraftsBeforeLibraryCommit),
        ("New project reflection stops for foundation input", NewProjectReflectionStopsForFoundationInput),
        ("Runtime governance observations do not leak guard text", RuntimeGovernanceObservationDoesNotLeakGuardText),
        ("Runtime returns user-facing chat replies before no-action fallback", RuntimeReturnsUserFacingChatRepliesBeforeNoActionFallback),
        ("Planner missing LLM settings returns local identity for free chat", PlannerMissingLlmSettingsReturnsLocalIdentityForFreeChat),
        ("Planner missing LLM settings leaves status query to no-action fallback", PlannerMissingLlmSettingsLeavesStatusQueryToNoActionFallback),
        ("ConversationKernel builds stable user turn envelopes", ConversationKernelBuildsUserTurnEnvelope),
        ("ConversationKernel models numeric candidate selection", ConversationKernelModelsCandidateSelection),
        ("Candidate selection confirmation reply is user-visible", CandidateSelectionConfirmationReplyIsUserVisible),
        ("Runtime normalizes story foundation candidate selection", RuntimeNormalizesStoryFoundationCandidateSelection),
        ("Native tool calls still pass through ToolPolicy", NativeToolCallsPassThroughToolPolicy),
        ("Provider tool calling diagnostics parse mock responses", ProviderToolCallingDiagnosticsParseMockResponses),
        ("Quality review suite blocks weak chapter quality", QualityReviewSuiteBlocksWeakChapterQuality),
        ("Tool registry exposes provider tool schemas", ToolRegistryExposesToolSchemas),
        ("StartNewNovelProject is idempotent while awaiting foundation", StartNewNovelProjectIsIdempotentWhileAwaitingFoundation),
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

    private static Task ConversationKernelRoutesStatusQuery()
    {
        var session = new AgentSession();
        var kernel = new ConversationKernel();
        var intent = kernel.Classify(session, "你好，我叫你生成的小说章节准备好了吗");

        Check.Equal(TurnIntentType.StatusQuery, intent.Type,
            "Status wording about generated chapters must be classified as status_query.");
        Check.Equal("status_query", intent.Label,
            "Status query label should be stable for runtime trace and blackboard routing.");
        Check.True(string.IsNullOrWhiteSpace(intent.CreativeBrief),
            "Status queries must not become creative briefs.");

        return Task.CompletedTask;
    }

    private static Task ToolPolicyBlocksRawUserGoalPlanChapter()
    {
        var policy = new ToolPolicyEngine();
        var session = new AgentSession();
        var bible = new StoryBibleDocument();
        var context = new AgentObservationContext
        {
            TurnIntent = new TurnIntent
            {
                Type = TurnIntentType.StatusQuery,
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

    private static Task AgentSessionClearsRemovedLegacyPending()
    {
        var session = new AgentSession
        {
            WorkingMemory = new AgentWorkingMemory
            {
                PendingToolCall = new AgentToolCall { Name = "GenerateChapter" },
                PendingConfirmation = new AgentPendingConfirmation
                {
                    ToolCall = new AgentToolCall { Name = "GenerateChapter" },
                    ImpactSummary = "legacy one-step chapter generation"
                }
            }
        };

        session.NormalizeLegacyState();

        Check.True(session.WorkingMemory.PendingToolCall == null,
            "Removed GenerateChapter pending tool calls must be cleared, not migrated silently.");
        Check.True(session.WorkingMemory.PendingConfirmation == null,
            "Removed GenerateChapter pending confirmations must be cleared, not consumed.");

        return Task.CompletedTask;
    }

    private static Task ToolPolicyPreflightsConfirmedStoryFoundationCommit()
    {
        const string runId = "run-foundation-001";
        var policy = new ToolPolicyEngine();
        var session = new AgentSession { ActiveRunId = runId };
        var context = new AgentObservationContext
        {
            TurnIntent = new TurnIntent { Type = TurnIntentType.Confirmation, Label = "confirmation" },
            MissionPlan = new AgentMissionPlan()
        };
        var call = new AgentToolCall
        {
            Name = "CommitStoryFoundation",
            Arguments =
            {
                ["runId"] = runId,
                ["selectedMacroCandidateTitle"] = "规则反噬型"
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

        var wrongTitleWithIndex = new AgentToolCall
        {
            Name = "CommitStoryFoundation",
            Arguments =
            {
                ["runId"] = runId,
                ["selectedMacroCandidateIndex"] = "1",
                ["selectedMacroCandidateTitle"] = "规则反哺型"
            }
        };
        var indexAllowed = policy.BeforeCall(
            wrongTitleWithIndex,
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
            "Valid candidateIndex must win over a misspelled LLM-provided title.");
        Check.Equal("规则反噬型", wrongTitleWithIndex.Arguments["selectedMacroCandidateTitle"],
            "Policy should normalize misspelled title to the canonical candidate title when index is valid.");

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
                                        NextAction = "RepairChapterDraft"
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
            TurnIntent = new TurnIntent { Type = TurnIntentType.Confirmation, Label = "confirmation" },
        };
        var call = new AgentToolCall
        {
            Name = "CommitValidatedChapter",
            Arguments = { ["runId"] = runId }
        };

        var result = policy.BeforeCall(call, session, bible, context, confirmed: true);

        Check.True(!result.AllowsExecution,
            "GenerationGate pass alone must not allow commit when Reflect quality gate still has issues.");
        Check.Contains("质量门禁", result.Message,
            "Commit block should name the quality gate, not pretend this is a tool error.");

        return Task.CompletedTask;
    }

    private static Task SchedulerContinueUsesActiveBlackboardTask()
    {
        const string runId = "run-chapter-002";
        var scheduler = new AgentTaskScheduler();
        var context = new AgentObservationContext
        {
            TurnIntent = new TurnIntent { Type = TurnIntentType.ContinueMission, Label = "continue_mission" },
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
                            NextAction = "BuildChapterContextPackage",
                            TaskType = "BuildChapterContextPackage",
                            Status = "queued",
                            Risk = "Medium"
                        }
                    }
                }
            }
        };

        var action = scheduler.BuildContinueAction(context);

        Check.True(action?.ToolCall?.Name == "BuildChapterContextPackage",
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
            TurnIntent = new TurnIntent { Type = TurnIntentType.ContinueMission, Label = "continue_mission" },
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
                            NextAction = "GenerateChapterWithChanges",
                            TaskType = "GenerateChapterWithChanges",
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
                    ToolName = "GenerateChapterWithChanges",
                    Success = false,
                    IsRepairable = true,
                    RecommendedToolName = "BuildChapterContextPackage",
                    RecommendedArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["runId"] = runId
                    },
                    MissingPrerequisite = "chapter_context_package",
                    RunId = runId,
                    CreatedAt = DateTime.UtcNow
                }
            }
        };

        var action = scheduler.BuildContinueAction(context);

        Check.Equal("BuildChapterContextPackage", action?.ToolCall?.Name ?? string.Empty,
            "Continue should prefer the latest repairable policy observation over a stale queued task.");
        Check.Equal(runId, action!.ToolCall!.Arguments["runId"],
            "Repairable scheduler action should carry recommended runId.");
        Check.True(!action.RequiresConfirmation,
            "Medium-risk repair prerequisite should not become a confirmation request.");
        return Task.CompletedTask;
    }

    private static Task ToolPolicyRepairsMissingContextPackageBeforeDraft()
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
                    chapter.NextAction = "BuildChapterContextPackage";
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
            MissionPlan = session.WorkingMemory.MissionPlan,
            TurnIntent = new TurnIntent { Type = TurnIntentType.ContinueMission, Label = "continue_mission" },
        };

        var result = policy.BeforeCall(
            new AgentToolCall { Name = "GenerateChapterWithChanges", Arguments = { ["runId"] = runId } },
            session,
            bible,
            context,
            confirmed: true);

        Check.True(!result.AllowsExecution && result.IsRepairable,
            "Missing context package should be a repairable policy block, not a terminal stop.");
        Check.Equal("BuildChapterContextPackage", result.RecommendedToolName,
            "Missing context package should recommend building chapter context.");
        Check.Equal("BuildChapterContextPackage", result.ReplacementAction?.ToolCall?.Name ?? string.Empty,
            "Repairable policy should provide a replacement prerequisite action.");
        Check.Equal(runId, result.RecommendedArguments["runId"],
            "Repair recommendation should preserve runId.");
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
                    chapter.NextAction = "GenerateChapterWithChanges";
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
            MissionPlan = session.WorkingMemory.MissionPlan,
            TurnIntent = new TurnIntent { Type = TurnIntentType.ContinueMission, Label = "continue_mission" },
        };

        var result = policy.BeforeCall(
            new AgentToolCall { Name = "ValidateChapterDraft", Arguments = { ["runId"] = runId } },
            session,
            bible,
            context,
            confirmed: false);

        Check.True(!result.AllowsExecution && result.IsRepairable,
            "Missing draft should be a repairable policy block before validation.");
        Check.Equal("GenerateChapterWithChanges", result.RecommendedToolName,
            "Missing draft should recommend generating the draft.");
        Check.Equal("GenerateChapterWithChanges", result.ReplacementAction?.ToolCall?.Name ?? string.Empty,
            "Repairable policy should provide the draft generation prerequisite action.");
        return Task.CompletedTask;
    }

    private static Task ToolPolicyKeepsHardBoundariesTerminal()
    {
        var policy = new ToolPolicyEngine();
        var session = new AgentSession();
        var context = new AgentObservationContext
        {
            TurnIntent = new TurnIntent { Type = TurnIntentType.ContinueMission, Label = "continue_mission" },
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
                MissionPlan = BuildPlanWithChapter("run-hard-boundary", chapter =>
                {
                    chapter.GateStatus = "validated";
                    chapter.QualityStatus = "quality_passed";
                }),
                TurnIntent = new TurnIntent { Type = TurnIntentType.ContinueMission, Label = "continue_mission" }
            },
            confirmed: false);
        Check.True(unconfirmedCommit.AllowsExecution && unconfirmedCommit.RequiresConfirmation && !unconfirmedCommit.IsRepairable,
            "Confirmation gates must remain confirmation gates, not repairable auto-execution.");
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
                    chapter.NextAction = "BuildChapterContextPackage";
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
            TurnIntent = new TurnIntent { Type = TurnIntentType.ContinueMission, Label = "continue_mission" },
        };

        var result = policy.BeforeCall(
            new AgentToolCall { Name = "GenerateChapterWithChanges", Arguments = { ["runId"] = runId } },
            session,
            bible,
            context,
            confirmed: true);

        Check.True(!result.AllowsExecution,
            "Draft generation must be blocked when MissionBlackboard requires context rebuild.");
        Check.True(result.IsRepairable,
            "Context rebuild requirement should be repairable so runtime can rebuild context first.");
        Check.Equal("BuildChapterContextPackage", result.RecommendedToolName,
            "Context rebuild requirement should recommend BuildChapterContextPackage.");
        Check.Equal("BuildChapterContextPackage", result.ReplacementAction?.ToolCall?.Name ?? string.Empty,
            "Repairable context rebuild should provide a replacement action.");
        Check.Contains("重新构建上下文包", result.Message,
            "Policy message should point to context rebuild.");
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
                    chapter.NextAction = "ValidateChapterDraft";
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
            TurnIntent = new TurnIntent { Type = TurnIntentType.Confirmation, Label = "confirmation" },
        };

        var result = policy.BeforeCall(
            new AgentToolCall { Name = "CommitValidatedChapter", Arguments = { ["runId"] = runId } },
            session,
            bible,
            context,
            confirmed: true);

        Check.True(!result.AllowsExecution,
            "Commit must be blocked when MissionBlackboard requires revalidation.");
        Check.True(result.IsRepairable,
            "Revalidation requirement should be repairable so runtime can validate first.");
        Check.Equal("ValidateChapterDraft", result.RecommendedToolName,
            "Revalidation requirement should recommend ValidateChapterDraft.");
        Check.Equal("ValidateChapterDraft", result.ReplacementAction?.ToolCall?.Name ?? string.Empty,
            "Repairable revalidation should provide a replacement action.");
        Check.Contains("重新校验", result.Message,
            "Policy message should point to revalidation.");
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
        Check.Equal("BuildChapterContextPackage", session.WorkingMemory.MissionPlan.SchedulerState.Tasks[0].NextAction,
            "Recovered selected chapter candidate should continue with context package construction.");
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
                DraftContent = "草稿正文\n\n---CHANGES---\n{}",
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

    private static async Task NewProjectReflectionStopsForFoundationInput()
    {
        var planner = new AgentPlanner(new UserSettingsManager(
            Path.Combine(Path.GetTempPath(), "agent-kernel-regression-settings"),
            "AgentKernelRegression"),
            new HttpClient());
        var reflection = await planner.ReflectAsync(
            new AgentObservationContext
            {
                UserMessage = "你好，我想写一本像斗罗大陆一样风格的小说"
            },
            new AgentRuntimeObservation
            {
                ToolName = "StartNewNovelProject",
                Success = true,
                Phase = "awaiting_user_foundation",
                Message = "已创建新小说，现在先把地基问清楚。"
            },
            CancellationToken.None);

        Check.True(reflection.RequiresUserInput,
            "New project creation must stop and ask for foundation input.");
        Check.True(!reflection.ShouldContinue,
            "New project creation must not auto-continue into another tool call.");
    }

    private static async Task RuntimeGovernanceObservationDoesNotLeakGuardText()
    {
        var planner = new AgentPlanner(new UserSettingsManager(
            Path.Combine(Path.GetTempPath(), "agent-kernel-regression-governance-reflect"),
            "AgentKernelRegression"),
            new HttpClient());
        var reflection = await planner.ReflectAsync(
            new AgentObservationContext
            {
                UserMessage = "你好，我想写一本像斗罗大陆一样风格的小说",
                TurnIntent = new TurnIntent { Type = TurnIntentType.NewProjectSeed, Label = "new_project_seed" },
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
                ToolName = "StartNewNovelProject",
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

    private static Task RuntimeReturnsUserFacingChatRepliesBeforeNoActionFallback()
    {
        var normalChat = new AgentAction
        {
            Type = AgentActionType.ChatReply,
            Reply = "我是天命小说 Agent。",
            Source = "openai_tool_calling"
        };
        var legacyChat = new AgentAction
        {
            Type = AgentActionType.ChatReply,
            Reply = "我是天命小说 Agent。",
            Source = "chat_reply",
            IsNoTool = true
        };
        var noAction = new AgentAction
        {
            Type = AgentActionType.ChatReply,
            Source = "planner_error_fallback",
            IsNoTool = true
        };
        var internalErrorText = new AgentAction
        {
            Type = AgentActionType.ChatReply,
            Reply = "raw provider exception",
            Source = "provider_raw_error",
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
        Check.True(AgentRuntime.ShouldReturnUserFacingReply(legacyChat),
            "Legacy ChatReply text marked IsNoTool should still return directly.");
        Check.True(!AgentRuntime.ShouldReturnUserFacingReply(noAction),
            "No-action fallback without reply should not be treated as chat.");
        Check.True(!AgentRuntime.ShouldReturnUserFacingReply(internalErrorText),
            "Raw provider/planner internals should not return directly.");
        Check.True(AgentRuntime.ShouldReturnUserFacingReply(authError),
            "User-fixable auth errors may be returned directly.");
        return Task.CompletedTask;
    }

    private static async Task PlannerMissingLlmSettingsReturnsLocalIdentityForFreeChat()
    {
        var root = Path.Combine(Path.GetTempPath(), "agent-kernel-regression-missing-llm-" + Guid.NewGuid().ToString("N"));
        var planner = new AgentPlanner(new UserSettingsManager(root, "AgentKernelRegression"), new HttpClient());
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
            "Missing LLM settings free chat should produce a local ChatReply.");
        Check.True(!action.IsNoTool,
            "Local identity fallback is a valid chat reply, not planner_failed_or_no_action.");
        Check.Contains("天命小说 Agent", action.Reply,
            "Missing LLM settings identity fallback should introduce the agent instead of returning Story Bible status.");
    }

    private static async Task PlannerMissingLlmSettingsLeavesStatusQueryToNoActionFallback()
    {
        var root = Path.Combine(Path.GetTempPath(), "agent-kernel-regression-missing-llm-status-" + Guid.NewGuid().ToString("N"));
        var planner = new AgentPlanner(new UserSettingsManager(root, "AgentKernelRegression"), new HttpClient());
        var action = await planner.PlanActionAsync(new AgentObservationContext
        {
            UserMessage = "刚才那章呢",
            TurnIntent = new TurnIntent
            {
                Type = TurnIntentType.StatusQuery,
                Label = "status_query",
                RawMessage = "刚才那章呢"
            },
            MissionPlan = new AgentMissionPlan()
        }, CancellationToken.None);

        Check.True(action.IsNoTool,
            "Missing LLM settings status query should remain planner_failed_or_no_action for Runtime status fallback.");
        Check.True(string.IsNullOrWhiteSpace(action.Reply),
            "Missing LLM settings status query must not be swallowed by local identity chat.");
        Check.True(!AgentRuntime.ShouldReturnUserFacingReply(action),
            "Status no-action should not be returned as direct chat.");
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
                                NextAction = "ValidateChapterDraft"
                            }
                        }
                    }
                }
            }
        };
        var envelope = new ConversationKernel().BuildEnvelope(session, "刚才那章呢，准备好了吗");

        Check.Equal(DialogueAct.AskStatus, envelope.DialogueAct,
            "Status query should become an ask-status dialogue act.");
        Check.Equal("chapter", envelope.StatusQueryScope,
            "Chapter wording should scope the status query to chapter artifacts.");
        Check.Equal("draft-run-active", envelope.TargetArtifact,
            "Recent chapter wording should point at the blackboard artifact cursor.");
        Check.Equal("task-active", envelope.ReferencedTask,
            "Envelope should preserve the active scheduler task reference.");
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
                    ["runId"] = runId,
                    ["selectedMacroCandidateTitle"] = "规则反哺型"
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
        Check.Equal("规则反噬型", action.ToolCall.Arguments["selectedMacroCandidateTitle"],
            "Runtime should overwrite misspelled LLM titles with the canonical candidate title.");
        return Task.CompletedTask;
    }

    private static Task NativeToolCallsPassThroughToolPolicy()
    {
        var policy = new ToolPolicyEngine();
        var session = new AgentSession();
        var context = new AgentObservationContext
        {
            TurnIntent = new TurnIntent
            {
                Type = TurnIntentType.StatusQuery,
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
        var settings = new UserSettingsManager(
            Path.Combine(Path.GetTempPath(), "agent-kernel-regression-tool-schema"),
            "AgentKernelRegression");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:ProjectName"] = "AgentKernelRegression",
                ["NovelAgent:StorageRoot"] = Path.Combine(Path.GetTempPath(), "agent-kernel-regression-tool-schema"),
            })
            .Build();
        var workspace = new NovelAgentWorkspace(new TestWebHostEnvironment(), config, settings);
        var catalog = new NovelProjectCatalog(workspace);
        AgentToolRegistry.SetWorkspace(workspace, catalog);
        try
        {
            var registry = new AgentToolRegistry(settings);
            var schemas = registry.ListToolSchemas();

            Check.True(schemas.Any(s => s.Name == "BuildChapterContextPackage"),
                "Registry should expose tool schemas for provider adapters.");
            Check.True(schemas.Any(s => s.Name == "GenerateChapterWithChanges" && s.RequiresConfirmation),
                "High-risk writing tools should preserve confirmation metadata in schemas.");
        }
        finally
        {
            AgentToolRegistry.ClearWorkspace();
        }
        return Task.CompletedTask;
    }

    private static async Task StartNewNovelProjectIsIdempotentWhileAwaitingFoundation()
    {
        var root = Path.Combine(Path.GetTempPath(), "agent-kernel-regression-start-project-" + Guid.NewGuid().ToString("N"));
        var settings = new UserSettingsManager(root, "AgentKernelRegression");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NovelAgent:ProjectName"] = "AgentKernelRegression",
                ["NovelAgent:StorageRoot"] = root,
            })
            .Build();
        var workspace = new NovelAgentWorkspace(new TestWebHostEnvironment { ContentRootPath = root, WebRootPath = root }, config, settings);
        var catalog = new NovelProjectCatalog(workspace);
        var session = new AgentSession { SessionId = "session-idempotent" };
        var call = new AgentToolCall
        {
            Name = "StartNewNovelProject",
            Arguments =
            {
                ["seed"] = "写一本斗罗大陆风格的玄幻学院流小说",
                ["genre"] = "玄幻"
            }
        };

        AgentToolRegistry.SetWorkspace(workspace, catalog);
        try
        {
            var registry = new AgentToolRegistry(settings);
            var first = await registry.ExecuteAsync(call, session, new StoryBibleDocument(), confirmed: false, CancellationToken.None);
            var countAfterFirst = (await catalog.GetAsync()).Projects.Count;
            var second = await registry.ExecuteAsync(call, session, new StoryBibleDocument(), confirmed: false, CancellationToken.None);
            var countAfterSecond = (await catalog.GetAsync()).Projects.Count;

            Check.True(first.Success, "First StartNewNovelProject call should create the project.");
            Check.True(second.Success, "Idempotent StartNewNovelProject call should return existing project state.");
            Check.Equal(countAfterFirst, countAfterSecond,
                "Second StartNewNovelProject call while awaiting foundation must not create another project.");
            Check.Equal("existing_novel_project", second.Artifact?.ArtifactType ?? string.Empty,
                "Second call should return an existing-project artifact.");
        }
        finally
        {
            AgentToolRegistry.ClearWorkspace();
        }
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
            NextAction = "CommitValidatedChapter"
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
