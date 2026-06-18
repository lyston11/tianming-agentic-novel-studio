using System.Reflection;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Support;

public class AgentRuntimeTests
{
    [Fact]
    public void FormatChatPromptWindow_UsesSqlitePromptWindowAndNotSessionSnapshot()
    {
        var promptWindow = new ChatPromptWindowDto(
            "总体摘要",
            new[]
            {
                new ChatHistorySummaryDto(1, 10, "summary", "前十轮摘要", new[] { "决定一" })
            },
            new[]
            {
                new ChatHistoryTurnDto("user", "SQLite 第一条", DateTime.UtcNow),
                new ChatHistoryTurnDto("assistant", "SQLite 回复", DateTime.UtcNow)
            });

        var history = AgentRuntime.FormatChatPromptWindow(promptWindow);

        Assert.Contains("总体摘要", history);
        Assert.Contains("前十轮摘要", history);
        Assert.Contains("决定一", history);
        Assert.Contains("U: SQLite 第一条", history);
        Assert.Contains("A: SQLite 回复", history);
        Assert.DoesNotContain("session snapshot only", history);
    }

    [Fact]
    public void FormatSessionHistorySnapshot_BoundsFallbackHistoryWhenPromptWindowFails()
    {
        var session = new AgentSession();
        for (var i = 0; i < 12; i++)
        {
            session.ChatHistory.Add(new AgentConversationTurn
            {
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = $"session fallback {i}"
            });
        }

        var history = AgentRuntime.FormatSessionHistorySnapshot(session);
        var lines = history.Split('\n');

        Assert.DoesNotContain(lines, line => line == "U: session fallback 0");
        Assert.DoesNotContain(lines, line => line == "A: session fallback 1");
        Assert.Contains("U: session fallback 2", lines);
        Assert.Contains("A: session fallback 11", lines);
    }

    [Fact]
    public void BuildStatusSummary_HandlesMissingStoryBibleWhenSessionHasNoProject()
    {
        var method = typeof(AgentRuntime).GetMethod("BuildStatusSummary", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var session = new AgentSession();
        string? summary = null;

        var exception = Record.Exception(() =>
        {
            summary = Assert.IsType<string>(method!.Invoke(null, new object?[] { session, null }));
        });

        Assert.Null(exception);
        Assert.Contains("尚未绑定项目", summary);
    }

    [Fact]
    public void BuildToolSearchOnlyContext_DescribesGlobalSemanticSearch()
    {
        var method = typeof(AgentRuntime).GetMethod("BuildToolSearchOnlyContext", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var tools = Assert.IsType<List<AgentToolDefinition>>(method!.Invoke(null, Array.Empty<object>()));
        var toolSearch = Assert.Single(tools);

        Assert.Equal("tool_search", toolSearch.Name);
        Assert.Contains("全局工具目录", toolSearch.Description);
        Assert.Contains("query", toolSearch.Arguments);
        Assert.Contains("includeAll", toolSearch.Arguments);
        Assert.DoesNotContain("搜索指定阶段的可用工具", toolSearch.Description);
        Assert.Contains("不是阶段白名单", toolSearch.Semantic.ResultSemantics);
    }

    [Fact]
    public void PrepareUserFacingReply_RewritesInternalToolAndStatusTerms()
    {
        var method = typeof(AgentRuntime).GetMethod("PrepareUserFacingReply", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var reply = Assert.IsType<string>(method!.Invoke(null, new object?[]
        {
            "当前调度任务 `CommitValidatedChapter` 显示 running，但实际上还没有执行提交。质量评审是 pending_quality_review，提交状态是（pending），其他章节是 **planned**，CHANGES 已修复，Story Bible 已更新，尚未 Commit 进书城，上下文包使用 fallback 模式。"
        }));

        Assert.Contains("提交动作记录", reply);
        Assert.Contains("等待质量评审", reply);
        Assert.Contains("等待提交", reply);
        Assert.Contains("已规划，未启动", reply);
        Assert.Contains("修订记录", reply);
        Assert.Contains("故事设定库", reply);
        Assert.Contains("提交到书城", reply);
        Assert.Contains("基础上下文模式", reply);
        Assert.DoesNotContain("CommitValidatedChapter", reply);
        Assert.DoesNotContain("Commit 进书城", reply);
        Assert.DoesNotContain("pending_quality_review", reply);
        Assert.DoesNotContain("planned", reply);
        Assert.DoesNotContain("CHANGES", reply);
        Assert.DoesNotContain("Story Bible", reply);
        Assert.DoesNotContain("fallback", reply);
    }

    [Fact]
    public void ShouldStopAfterToolResult_StopsAfterReadOnlyWorkspaceSnapshotEvenWhenPreviewHasItems()
    {
        var result = new AgentToolExecutionResult
        {
            Success = true,
            Message = "小说书城：当前可见项目共 3 本；本次快照列出 3 本。",
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "workspace_state",
                Summary = "已读取小说书城、知识库和创作工作流状态。",
                NextHints = new[]
                {
                    "项目一 (project-1)",
                    "项目二 (project-2)",
                    "项目三 (project-3)"
                }
            }
        };
        var reflection = new AgentReflection
        {
            GoalSatisfied = false,
            ShouldContinue = true,
            RequiresUserInput = false,
            ReplyDraft = result.Message
        };
        var plan = new AgentMissionPlan
        {
            TodoQueue = { "根据工具结果继续推进下一步" }
        };

        Assert.True(AgentRuntime.ShouldStopAfterToolResult(result, reflection, plan));
    }

    [Fact]
    public void ShouldStopAfterToolResult_DoesNotStopAfterKnowledgeHitsWhenModelCanSynthesize()
    {
        var result = new AgentToolExecutionResult
        {
            Success = true,
            Phase = "knowledge_retrieved",
            Message = "【GenrePrinciple】规则驱动型叙事\n潮汐倒计时与盐雾钟声应成为剧情规则。",
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "knowledge_hits",
                Summary = "检索到 4 条创意知识。",
                NextHints = new[] { "基于知识继续构思" }
            },
            Suggestions = new[] { "基于这些知识继续构思" }
        };
        var reflection = new AgentReflection
        {
            GoalSatisfied = false,
            ShouldContinue = true,
            RequiresUserInput = false,
            ReplyDraft = "我已检索到知识，接下来应基于这些知识完成用户要求的写作。"
        };

        Assert.False(AgentRuntime.ShouldStopAfterToolResult(result, reflection, new AgentMissionPlan()));
    }

    [Fact]
    public void ShouldStopAfterToolResult_DoesNotStopAfterCandidateArtifactWhenMissionCanContinue()
    {
        var result = new AgentToolExecutionResult
        {
            Success = true,
            Phase = "chapter_candidates",
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "chapter_candidates",
                NextHints = new[] { "选择推荐，开始生成" }
            },
            Suggestions = new[] { "选择推荐，开始生成" }
        };
        var reflection = new AgentReflection
        {
            GoalSatisfied = false,
            ShouldContinue = true,
            RequiresUserInput = false
        };
        var plan = new AgentMissionPlan
        {
            AllowedNextActions = { "SelectChapterCandidate" },
            SchedulerState = new AgentTaskSchedulerState
            {
                Tasks =
                {
                    new AgentScheduledTask
                    {
                        Status = "queued",
                        NextAction = "SelectChapterCandidate",
                        RunId = "run-003",
                        ChapterId = "chapter-003"
                    }
                }
            }
        };

        Assert.False(AgentRuntime.ShouldStopAfterToolResult(result, reflection, plan));
    }

    [Fact]
    public void ShouldStopAfterToolResult_DoesNotStopAfterCandidateArtifactWhenMissionPointerHasNextTask()
    {
        var result = new AgentToolExecutionResult
        {
            Success = true,
            Phase = "chapter_candidates",
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "chapter_candidates"
            }
        };
        var reflection = new AgentReflection
        {
            GoalSatisfied = false,
            ShouldContinue = true,
            RequiresUserInput = false
        };
        var plan = new AgentMissionPlan
        {
            SchedulerState = new AgentTaskSchedulerState
            {
                ActiveTaskId = "run-004:BuildChapterContextPackage",
                ActiveRunId = "run-004",
                ActiveChapterId = "chapter-004"
            }
        };

        Assert.False(AgentRuntime.ShouldStopAfterToolResult(result, reflection, plan));
    }

    [Fact]
    public void ShouldStopAfterToolResult_DoesNotStopAfterCandidateArtifactWithNextTaskEvenWhenReflectionRequestsInput()
    {
        var result = new AgentToolExecutionResult
        {
            Success = true,
            Phase = "chapter_candidates",
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "chapter_candidates"
            }
        };
        var reflection = new AgentReflection
        {
            GoalSatisfied = false,
            ShouldContinue = false,
            RequiresUserInput = true
        };
        var plan = new AgentMissionPlan
        {
            SchedulerState = new AgentTaskSchedulerState
            {
                ActiveTaskId = "run-005:BuildChapterContextPackage",
                ActiveRunId = "run-005",
                ActiveChapterId = "chapter-005"
            }
        };

        Assert.False(AgentRuntime.ShouldStopAfterToolResult(result, reflection, plan));
    }

    [Fact]
    public void ShouldStopAfterToolResult_DoesNotStopAfterChapterReviewWhenCommitIsNext()
    {
        var result = new AgentToolExecutionResult
        {
            Success = true,
            Phase = "chapter_reviewed",
            Message = "质量评审完成，可以提交成稿。",
            RunId = "run-003",
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "chapter_review",
                RunId = "run-003",
                NextHints = new[] { "提交成稿" }
            },
            Suggestions = new[] { "提交成稿" }
        };
        var reflection = new AgentReflection
        {
            GoalSatisfied = false,
            ShouldContinue = false,
            RequiresUserInput = false,
            ReplyDraft = "质量评审通过。"
        };
        var plan = new AgentMissionPlan
        {
            AllowedNextActions = { "CommitValidatedChapter" },
            SchedulerState = new AgentTaskSchedulerState
            {
                ActiveTaskId = "run-003:CommitValidatedChapter",
                ActiveRunId = "run-003",
                ActiveChapterId = "chapter-003",
                Tasks =
                {
                    new AgentScheduledTask
                    {
                        Status = "queued",
                        NextAction = "CommitValidatedChapter",
                        RunId = "run-003",
                        ChapterId = "chapter-003"
                    }
                }
            }
        };

        Assert.False(AgentRuntime.ShouldStopAfterToolResult(result, reflection, plan));
    }

    [Fact]
    public void ShouldStopAfterToolResult_DoesNotStopAfterRepairableGateFailureWhenRepairIsNext()
    {
        var result = new AgentToolExecutionResult
        {
            Success = false,
            Phase = "gate_failed",
            Message = "章节草稿修复后仍未通过硬门禁。",
            RunId = "run-003",
            IsRepairable = true,
            RecommendedToolName = "RepairChapterDraft",
            RecommendedArguments =
            {
                ["runId"] = "run-003",
                ["repairStrategy"] = "rewrite_continuity_scene"
            },
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "chapter_repair_report",
                RunId = "run-003"
            }
        };
        var reflection = new AgentReflection
        {
            GoalSatisfied = false,
            ShouldContinue = true,
            RequiresUserInput = false,
            ReplyDraft = "这次失败仍可自动修复。"
        };
        var plan = new AgentMissionPlan
        {
            AllowedNextActions = { "RepairChapterDraft" },
            SchedulerState = new AgentTaskSchedulerState
            {
                ActiveTaskId = "run-003:RepairChapterDraft",
                ActiveRunId = "run-003",
                ActiveChapterId = "chapter-003",
                Tasks =
                {
                    new AgentScheduledTask
                    {
                        Status = "queued",
                        NextAction = "RepairChapterDraft",
                        RunId = "run-003",
                        ChapterId = "chapter-003"
                    }
                }
            }
        };

        Assert.False(AgentRuntime.ShouldStopAfterToolResult(result, reflection, plan));
    }

    [Fact]
    public void ShouldDeferUserFacingReplyToScheduler_WhenContinueTurnHasQueuedMissionTask()
    {
        var action = new AgentAction
        {
            Type = AgentActionType.ChatReply,
            Reply = "我会继续推进。"
        };
        var context = new AgentObservationContext
        {
            TurnIntent = new TurnIntent { Type = TurnIntentType.ContinueMission },
            UserTurn = new UserTurnEnvelope { DialogueAct = DialogueAct.ContinueTask },
            MissionPlan = new AgentMissionPlan
            {
                AllowedNextActions = { "ValidateChapterDraft" },
                SchedulerState = new AgentTaskSchedulerState
                {
                    Tasks =
                    {
                        new AgentScheduledTask
                        {
                            Status = "queued",
                            NextAction = "ValidateChapterDraft",
                            RunId = "run-001",
                            ChapterId = "chapter-002"
                        }
                    }
                }
            }
        };

        Assert.True(AgentRuntime.ShouldDeferUserFacingReplyToScheduler(action, context));
    }

    [Fact]
    public void ShouldDeferUserFacingReplyToScheduler_DoesNotDeferStatusQuestion()
    {
        var action = new AgentAction
        {
            Type = AgentActionType.ChatReply,
            Reply = "当前第 2 章等待校验。"
        };
        var context = new AgentObservationContext
        {
            TurnIntent = new TurnIntent { Type = TurnIntentType.StatusQuery },
            UserTurn = new UserTurnEnvelope { DialogueAct = DialogueAct.AskStatus },
            MissionPlan = new AgentMissionPlan
            {
                AllowedNextActions = { "ValidateChapterDraft" },
                SchedulerState = new AgentTaskSchedulerState
                {
                    Tasks =
                    {
                        new AgentScheduledTask
                        {
                            Status = "queued",
                            NextAction = "ValidateChapterDraft",
                            RunId = "run-001",
                            ChapterId = "chapter-002"
                        }
                    }
                }
            }
        };

        Assert.False(AgentRuntime.ShouldDeferUserFacingReplyToScheduler(action, context));
    }

    [Fact]
    public void ShouldEscalateSafety_UsesConfiguredMaxStepsInsteadOfHardCodedSeven()
    {
        Assert.False(AgentRuntime.ShouldEscalateSafety(7, 12));
        Assert.True(AgentRuntime.ShouldEscalateSafety(12, 12));
    }

    [Fact]
    public void BuildReadOnlySnapshotReply_FormatsWorkspaceStateForUser()
    {
        var method = typeof(AgentRuntime).GetMethod("BuildReadOnlySnapshotReply", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var session = new AgentSession();
        session.WorkingMemory.AuthorMemory.DisplayName = "lyston";
        var result = new AgentToolExecutionResult
        {
            Success = true,
            Message = "当前会话：activeProjectId=，phase=idle，hasActiveProject=False\n小说书城：当前可见项目共 25 本；本次快照列出 20 本。\n• 内部长列表",
            Data = new AgentWorkspaceState
            {
                ProjectTotalCount = 25,
                ProjectPreviewCount = 20,
                VisibleProjects =
                {
                    new AgentWorkspaceProjectState { Title = "项目一", Status = "Drafting", CommittedChapterCount = 0 },
                    new AgentWorkspaceProjectState { Title = "项目二", Status = "Drafting", CommittedChapterCount = 0 }
                },
                KnowledgeBase = new AgentWorkspaceKnowledgeState { TotalCount = 7 },
                Workflow = new AgentWorkspaceWorkflowState { ActiveRunCount = 0 },
                CurrentSession = new AgentCurrentSessionState { HasActiveProject = false }
            },
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "workspace_state",
                Summary = "已读取小说书城、知识库和创作工作流状态。"
            }
        };

        var reply = Assert.IsType<string>(method!.Invoke(null, new object?[] { session, result }));

        Assert.Contains("lyston，小说书城当前可见项目共 25 本", reply);
        Assert.Contains("知识库当前可见条目 7 条", reply);
        Assert.Contains("这次只是读取状态", reply);
        Assert.DoesNotContain("activeProjectId", reply);
        Assert.DoesNotContain("内部长列表", reply);
    }

    [Fact]
    public void SelectReadOnlySnapshotReply_PrefersCleanModelReply()
    {
        var method = typeof(AgentRuntime).GetMethod("SelectReadOnlySnapshotReply", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var session = new AgentSession();
        var reflection = new AgentReflection
        {
            ReplyDraft = "你的书城目前共有 25 本书。"
        };
        var result = new AgentToolExecutionResult
        {
            Success = true,
            Message = "当前会话：activeProjectId=，phase=idle\n小说书城当前可见项目共 25 本。",
            Artifact = new AgentToolArtifact { ArtifactType = "workspace_state" }
        };

        var reply = Assert.IsType<string>(method!.Invoke(null, new object?[] { session, reflection, result }));

        Assert.Equal(reflection.ReplyDraft, reply);
    }
}
