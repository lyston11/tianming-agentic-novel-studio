using System.Reflection;
using System.Text.Json;
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
    public void BuildToolFailureUserFacingReply_SummarizesRecoverableTransientFailureWithoutRawException()
    {
        var method = typeof(AgentRuntime).GetMethod("BuildToolFailureUserFacingReply", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = new AgentToolExecutionResult
        {
            Success = false,
            IsRepairable = true,
            RecommendedToolName = "ProduceChapter",
            Message = "工具 ProduceChapter 失败：工具 ProduceChapter 在阶段「context_ready」遇到异常：HttpRequestException: An error occurred while sending the request. 已保留当前产物。",
            Failure = new AgentToolFailure
            {
                Code = "PRODUCE_CHAPTER_RECOVERABLE_FAILURE",
                FailedStage = "context_ready",
                Reason = "HttpRequestException: An error occurred while sending the request.",
                Recoverable = true,
                RecommendedAction = "ProduceChapter"
            },
            Suggestions = new[] { "重试 ProduceChapter", "查看工作流" }
        };
        var reflection = new AgentReflection
        {
            ReplyDraft = result.Message,
            Summary = result.Message
        };

        var reply = Assert.IsType<string>(method!.Invoke(null, new object?[] { result, reflection }));

        Assert.Contains("context_ready", reply);
        Assert.Contains("ProduceChapter", reply);
        Assert.Contains("重试", reply);
        Assert.DoesNotContain("HttpRequestException", reply);
        Assert.DoesNotContain("An error occurred while sending the request", reply);
        Assert.DoesNotContain("工具 ProduceChapter 失败：工具 ProduceChapter", reply);
    }

    [Fact]
    public void PrepareUserFacingReply_NormalizesStatusTermsWithoutLegacyToolAliases()
    {
        var method = typeof(AgentRuntime).GetMethod("PrepareUserFacingReply", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var reply = Assert.IsType<string>(method!.Invoke(null, new object?[]
        {
            "质量评审是 pending_quality_review，提交状态是（pending），其他章节是 **planned**，CHANGES 已修复，Story Bible 已更新，尚未 Commit 进书城，上下文包使用 fallback 模式。"
        }));

        Assert.Contains("等待质量评审", reply);
        Assert.Contains("等待提交", reply);
        Assert.Contains("已规划，未启动", reply);
        Assert.Contains("修订记录", reply);
        Assert.Contains("故事设定库", reply);
        Assert.Contains("提交到书城", reply);
        Assert.Contains("基础上下文模式", reply);
        Assert.DoesNotContain("Commit 进书城", reply);
        Assert.DoesNotContain("pending_quality_review", reply);
        Assert.DoesNotContain("planned", reply);
        Assert.DoesNotContain("CHANGES", reply);
        Assert.DoesNotContain("Story Bible", reply);
        Assert.DoesNotContain("fallback", reply);
    }

    [Fact]
    public void BuildCompletedToolResultReply_PreservesSuccessfulArtifactResult()
    {
        var method = typeof(AgentRuntime).GetMethod("BuildCompletedToolResultReply", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var reply = Assert.IsType<string>(method!.Invoke(null, new object?[]
        {
            new AgentToolExecutionResult
            {
                Success = true,
                Message = "生成了 4 个故事地基候选。",
                Suggestions = new[] { "查看候选", "继续规划" }
            },
            new AgentReflection
            {
                Summary = "故事地基候选已生成，等待选择或继续规划。"
            }
        }));

        Assert.Contains("生成了 4 个故事地基候选", reply);
        Assert.Contains("故事地基候选已生成", reply);
        Assert.DoesNotContain("没有新的工具执行结果", reply);
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
    public void ShouldStopAfterToolResult_DoesNotStopAfterReadOnlySnapshotWithRecommendedTool()
    {
        var result = new AgentToolExecutionResult
        {
            Success = true,
            Phase = "query_project",
            RecommendedToolName = "CommitVolumeArc",
            RecommendedArguments = { ["runId"] = "volume-run-001" },
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "project_status",
                RunId = "volume-run-001",
                NextHints = new[] { "提交当前卷规划" }
            },
            Suggestions = new[] { "提交当前卷规划" }
        };
        var reflection = new AgentReflection
        {
            GoalSatisfied = false,
            ShouldContinue = true,
            RequiresUserInput = false
        };
        var plan = new AgentMissionPlan
        {
            AllowedNextActions = { "CommitVolumeArc", "PlanVolumeArc" }
        };

        Assert.False(AgentRuntime.ShouldStopAfterToolResult(result, reflection, plan));
    }

    [Fact]
    public void ShouldStopAfterToolResult_DoesNotStopAfterProjectStatusWhenReflectionRequiresContinuation()
    {
        var result = new AgentToolExecutionResult
        {
            Success = true,
            Phase = "query_project",
            Message = "任务黑板：末世邮徽录/volume_planning；允许下一步：PlanVolumeArc",
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "project_status",
                Summary = "项目状态已读取。"
            }
        };
        var reflection = new AgentReflection
        {
            GoalSatisfied = false,
            ShouldContinue = true,
            RequiresUserInput = false,
            NextIntent = "continue"
        };
        var plan = new AgentMissionPlan
        {
            AllowedNextActions = { "PlanVolumeArc" },
            TodoQueue = { "根据工具结果继续推进下一步" }
        };

        Assert.False(AgentRuntime.ShouldStopAfterToolResult(result, reflection, plan));
    }

    [Fact]
    public void ShouldStopAfterToolResult_DoesNotStopAfterProjectContentQueryWhenReflectionRequiresContinuation()
    {
        var result = new AgentToolExecutionResult
        {
            Success = true,
            Phase = "query_project_content",
            Message = "已读取第三章正文，下一步可承接生成第四章。",
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "project_content_query",
                Summary = "第三章内容已读取。"
            }
        };
        var reflection = new AgentReflection
        {
            GoalSatisfied = false,
            ShouldContinue = true,
            RequiresUserInput = false,
            NextIntent = "continue_chapter_production",
            ReplyDraft = "第三章已读取，用户目标是写第四章并提交，应继续进入章节生产。"
        };

        Assert.False(AgentRuntime.ShouldStopAfterToolResult(result, reflection, new AgentMissionPlan()));
    }

    [Fact]
    public void ShouldStopAfterToolResult_StopsAfterCommittedChapterAuditEvenWhenMissionCanContinue()
    {
        var result = new AgentToolExecutionResult
        {
            Success = true,
            Phase = "committed_chapter_audit",
            Message = "第六章回溯审查未通过：知识库硬事实失败。",
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "committed_chapter_audit",
                Summary = "已提交章节已审查，发现硬事实违例。",
                NextHints = new[] { "修订已提交章节", "查看失败项", "继续审查相邻章节" }
            },
            Suggestions = new[] { "修订已提交章节", "查看失败项" }
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
            AllowedNextActions = { "ReviseCommittedChapter" },
            SchedulerState = new AgentTaskSchedulerState
            {
                Tasks =
                {
                    new AgentScheduledTask
                    {
                        Status = "queued",
                        NextAction = "ReviseCommittedChapter",
                        RunId = "audit-run",
                        ChapterId = "chapter-006"
                    }
                }
            }
        };

        Assert.True(AgentRuntime.ShouldStopAfterToolResult(result, reflection, plan));
    }

    [Fact]
    public void ShouldStopAfterToolResult_DoesNotStopAfterProducedChapterCommittedWhenMissionHasNextChapter()
    {
        var result = new AgentToolExecutionResult
        {
            Success = true,
            Phase = "committed",
            Message = "第五章已通过硬门禁并提交成稿。",
            RunId = "run-005",
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "chapter_produced",
                RunId = "run-005",
                ArtifactId = "project-001-chapter-005",
                Summary = "第五章已入书城。",
                NextHints = new[] { "查看第五章", "继续第六章" }
            },
            Suggestions = new[] { "查看第五章", "继续第六章" }
        };
        var reflection = new AgentReflection
        {
            GoalSatisfied = false,
            ShouldContinue = true,
            RequiresUserInput = false,
            ReplyDraft = "第五章已经写完并提交，后续可以继续第六章。"
        };
        var plan = new AgentMissionPlan
        {
            AllowedNextActions = { "ProduceChapter" },
            SchedulerState = new AgentTaskSchedulerState
            {
                ActiveTaskId = "run-006:ProduceChapter",
                ActiveRunId = "run-006",
                ActiveChapterId = "project-001-chapter-006",
                Tasks =
                {
                    new AgentScheduledTask
                    {
                        Status = "queued",
                        NextAction = "ProduceChapter",
                        RunId = "run-006",
                        ChapterId = "project-001-chapter-006"
                    }
                }
            }
        };

        Assert.False(AgentRuntime.ShouldStopAfterToolResult(result, reflection, plan));
    }

    [Fact]
    public void ShouldFinishFailureWithoutReflection_DoesNotStopForRepairableProductionBlock()
    {
        var result = new AgentToolExecutionResult
        {
            Success = false,
            IsRepairable = true,
            RecommendedToolName = "ProduceChapter",
            Message = "章节质量评审未通过，生产闭环需要停在 AgentReview 阶段。",
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "chapter_production_blocked",
                Summary = "章节生产闭环在 Agent 质量评审阶段停止。",
                NextHints = new[] { "按评审修订", "重新生成章节", "查看评审报告" }
            },
            Failure = new AgentToolFailure
            {
                FailedStage = "ReviewChapter",
                Reason = "质量评审 blocked。",
                Recoverable = true,
                RecoverableActions = new[] { "按评审修订", "重新生成章节" }
            }
        };

        Assert.False(AgentRuntime.ShouldFinishFailureWithoutReflection(result));
    }

    [Fact]
    public void ShouldFinishFailureWithoutReflection_StopsForProductionBlockThatRequiresUserDecision()
    {
        var result = new AgentToolExecutionResult
        {
            Success = false,
            IsRepairable = false,
            Message = "章节质量评审仍未通过，需要用户决定是否调整蓝图。",
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "chapter_production_blocked",
                Summary = "章节生产闭环在 Agent 质量评审阶段停止。",
                NextHints = new[] { "查看评审报告", "调整章节蓝图" }
            },
            Failure = new AgentToolFailure
            {
                FailedStage = "ReviewChapter",
                Reason = "自动重写预算已用完。",
                Recoverable = true,
                RequiresUserDecision = true,
                RecoverableActions = new[] { "查看评审报告", "调整章节蓝图" }
            }
        };

        Assert.True(AgentRuntime.ShouldFinishFailureWithoutReflection(result));
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
    public void ShouldStopAfterToolResult_DoesNotStopAfterFoundationCandidatesWhenMissionCanCommitAndReflectionContinues()
    {
        var result = new AgentToolExecutionResult
        {
            Success = true,
            Phase = "foundation_candidates",
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "story_foundation_candidates",
                NextHints = new[] { "固化推荐故事地基", "继续卷规划" }
            },
            Suggestions = new[] { "固化推荐故事地基", "继续卷规划" }
        };
        var reflection = new AgentReflection
        {
            GoalSatisfied = false,
            ShouldContinue = true,
            RequiresUserInput = false,
            ReplyDraft = "故事地基候选已生成，用户目标是直接生产第一章，应继续固化并推进卷规划。"
        };
        var plan = new AgentMissionPlan
        {
            AllowedNextActions = { "CommitStoryFoundation", "PlanStoryFoundation" }
        };

        Assert.False(AgentRuntime.ShouldStopAfterToolResult(result, reflection, plan));
    }

    [Fact]
    public void BuildRuleReflection_DoesNotTreatFoundationCandidatesAsUserInputWhenMissionCanCommit()
    {
        var method = typeof(AgentPlanner).GetMethod("BuildRuleReflection", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var context = new AgentObservationContext
        {
            UserMessage = "新建小说，完成故事地基、卷规划，并直接生产第一章提交书城。",
            MissionPlan = new AgentMissionPlan
            {
                AllowedNextActions = { "CommitStoryFoundation", "PlanStoryFoundation" }
            }
        };
        var observation = new AgentRuntimeObservation
        {
            ToolName = "PlanStoryFoundation",
            Success = true,
            Phase = "foundation_candidates",
            Message = "生成了 3 个故事地基候选。",
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "story_foundation_candidates",
                NextHints = new[] { "固化推荐故事地基", "继续卷规划" }
            }
        };

        var reflection = Assert.IsType<AgentReflection>(method!.Invoke(null, new object[] { context, observation }));

        Assert.False(reflection.RequiresUserInput);
        Assert.False(reflection.GoalSatisfied);
        Assert.True(reflection.ShouldContinue);
        Assert.Contains("根据工具结果继续推进下一步", reflection.NewTodoItems);
    }

    [Fact]
    public void BuildRuleReflection_DoesNotTreatProjectContentQueryAsProjectLifecycleInput()
    {
        var method = typeof(AgentPlanner).GetMethod("BuildRuleReflection", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var context = new AgentObservationContext
        {
            UserMessage = "继续写第四章，并提交书城。",
            MissionPlan = new AgentMissionPlan()
        };
        var observation = new AgentRuntimeObservation
        {
            ToolName = "QueryProjectContent",
            Success = true,
            Phase = "query_project_content",
            Message = "已读取第三章正文，下一步可承接生成第四章。",
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "project_content_query",
                NextHints = new[] { "继续章节生产" }
            }
        };

        var reflection = Assert.IsType<AgentReflection>(method!.Invoke(null, new object[] { context, observation }));

        Assert.False(reflection.RequiresUserInput);
        Assert.False(reflection.GoalSatisfied);
        Assert.True(reflection.ShouldContinue);
        Assert.Contains("根据工具结果继续推进下一步", reflection.NewTodoItems);
    }

    [Fact]
    public void ShouldDeferForegroundReadableToolToBackground_DoesNotDeferProjectContentQueryBecauseMissionHasNextStep()
    {
        var method = typeof(AgentRuntime).GetMethod("ShouldDeferForegroundReadableToolToBackground", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var action = new AgentAction
        {
            Type = AgentActionType.ToolCall,
            Intent = "query_project_content",
            ToolCall = new AgentToolCall
            {
                Name = "QueryProjectContent",
                Arguments = new Dictionary<string, string>
                {
                    ["chapterNumber"] = "1",
                    ["includeBody"] = "true"
                }
            }
        };
        var context = new AgentObservationContext
        {
            UserMessage = "读取第一章标题、正文开头和结尾。",
            MissionPlan = new AgentMissionPlan
            {
                AllowedNextActions = { "ProduceChapter" }
            }
        };

        var shouldDefer = Assert.IsType<bool>(method!.Invoke(null, new object[] { action, context }));

        Assert.False(shouldDefer);
    }

    [Fact]
    public void PlannerPrompt_IncludesStructuredRuntimeInterrupts()
    {
        var method = typeof(AgentPlanner).GetMethod("BuildActionUserPrompt", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var context = new AgentObservationContext
        {
            UserMessage = "继续写第一章",
            RuntimeInterrupts =
            {
                new AgentRuntimeInterruptObservation
                {
                    InterruptId = "interrupt-1",
                    RuntimeRunId = "runtime-run-1",
                    Kind = "freeform",
                    Message = "第二章这里改成打怪升级，不要关系拉扯",
                    Priority = 3,
                    ReceivedAt = new DateTime(2026, 6, 23, 8, 0, 0, DateTimeKind.Utc),
                    ConsumedAt = new DateTime(2026, 6, 23, 8, 0, 5, DateTimeKind.Utc)
                }
            }
        };

        var prompt = Assert.IsType<string>(method!.Invoke(null, new object[] { context }));
        using var document = JsonDocument.Parse(prompt);
        var interrupt = document.RootElement.GetProperty("runtime_interrupts")[0];

        Assert.Contains("runtime_interrupts", prompt);
        Assert.Equal("第二章这里改成打怪升级，不要关系拉扯", interrupt.GetProperty("Message").GetString());
        Assert.Equal("interrupt-1", interrupt.GetProperty("InterruptId").GetString());
    }

    [Fact]
    public void InterruptDecisionPrompt_RequiresModelSemanticDecisionWithoutKeywordRouting()
    {
        var systemMethod = typeof(AgentPlanner).GetMethod("BuildInterruptDecisionSystemPrompt", BindingFlags.NonPublic | BindingFlags.Static);
        var userMethod = typeof(AgentPlanner).GetMethod("BuildInterruptDecisionUserPrompt", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(systemMethod);
        Assert.NotNull(userMethod);

        var context = new AgentInterruptDecisionContext
        {
            UserMessage = "不要让女主现在喜欢男主",
            RuntimeRunId = "runtime-run-1",
            RunStatus = "running",
            ActiveTool = "ProduceChapter",
            CurrentPhase = "draft_generation",
            LastMessage = "正在生成正文",
            CurrentUserGoal = "继续写第二章并提交书城"
        };

        var systemPrompt = Assert.IsType<string>(systemMethod!.Invoke(null, Array.Empty<object>()));
        var userPrompt = Assert.IsType<string>(userMethod!.Invoke(null, new object[] { context }));
        using var document = JsonDocument.Parse(userPrompt);

        Assert.Contains("status | soft_requirement | cancel | direction_change | freeform", systemPrompt);
        Assert.Contains("不要用关键词路由", systemPrompt);
        Assert.Equal("不要让女主现在喜欢男主", document.RootElement.GetProperty("userMessage").GetString());
        Assert.Equal("ProduceChapter", document.RootElement.GetProperty("activeRun").GetProperty("ActiveTool").GetString());
    }

    [Fact]
    public void FormatRuntimeInterruptForModel_PreservesInterruptKindAndPriority()
    {
        var method = typeof(AgentRuntime).GetMethod("FormatRuntimeInterruptForModel", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var line = Assert.IsType<string>(method!.Invoke(null, new object?[]
        {
            "soft_requirement",
            "不要让女主现在喜欢男主",
            5
        }));

        Assert.Contains("kind=soft_requirement", line);
        Assert.Contains("priority=5", line);
        Assert.Contains("不要让女主现在喜欢男主", line);
    }

    [Fact]
    public void ReflectPrompt_IncludesStructuredRuntimeInterrupts()
    {
        var method = typeof(AgentPlanner).GetMethod("BuildReflectUserPrompt", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var context = new AgentObservationContext
        {
            UserMessage = "继续写第一章",
            RuntimeInterrupts =
            {
                new AgentRuntimeInterruptObservation
                {
                    InterruptId = "interrupt-2",
                    RuntimeRunId = "runtime-run-1",
                    Kind = "freeform",
                    Message = "如果已经生成草稿，就按这个想法修订",
                    Priority = 1,
                    ReceivedAt = new DateTime(2026, 6, 23, 8, 1, 0, DateTimeKind.Utc),
                    ConsumedAt = new DateTime(2026, 6, 23, 8, 1, 2, DateTimeKind.Utc)
                }
            }
        };
        var observation = new AgentRuntimeObservation
        {
            ToolName = "ProduceChapter",
            Success = true,
            Message = "第一章草稿已生成。"
        };

        var prompt = Assert.IsType<string>(method!.Invoke(null, new object[] { context, observation }));
        using var document = JsonDocument.Parse(prompt);
        var interrupt = document.RootElement.GetProperty("runtime_interrupts")[0];

        Assert.Contains("runtime_interrupts", prompt);
        Assert.Equal("如果已经生成草稿，就按这个想法修订", interrupt.GetProperty("Message").GetString());
        Assert.Equal("interrupt-2", interrupt.GetProperty("InterruptId").GetString());
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
    public void ShouldStopAfterToolResult_DoesNotStopAfterVolumeArcCandidatesWhenMissionCanCommit()
    {
        var result = new AgentToolExecutionResult
        {
            Success = true,
            Phase = "volume_plan",
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "volume_arc_candidates",
                NextHints = new[] { "提交入库", "调整卷规划" }
            },
            Suggestions = new[] { "提交入库", "调整卷规划" }
        };
        var reflection = new AgentReflection
        {
            GoalSatisfied = false,
            ShouldContinue = true,
            RequiresUserInput = false
        };
        var plan = new AgentMissionPlan
        {
            AllowedNextActions = { "CommitVolumeArc" }
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
                ActiveTaskId = "run-004:ProduceChapter",
                ActiveRunId = "run-004",
                ActiveChapterId = "chapter-004"
            }
        };

        Assert.False(AgentRuntime.ShouldStopAfterToolResult(result, reflection, plan));
    }

    [Fact]
    public void ShouldStopAfterToolResult_StopsAfterCandidateArtifactWithNextTaskEvenWhenReflectionRequestsInput()
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
                ActiveTaskId = "run-005:ProduceChapter",
                ActiveRunId = "run-005",
                ActiveChapterId = "chapter-005"
            }
        };

        Assert.True(AgentRuntime.ShouldStopAfterToolResult(result, reflection, plan));
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
            AllowedNextActions = { "ProduceChapter" },
            SchedulerState = new AgentTaskSchedulerState
            {
                ActiveTaskId = "run-003:ProduceChapter",
                ActiveRunId = "run-003",
                ActiveChapterId = "chapter-003",
                Tasks =
                {
                    new AgentScheduledTask
                    {
                        Status = "queued",
                        NextAction = "ProduceChapter",
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
            RecommendedToolName = "ProduceChapter",
            RecommendedArguments =
            {
                ["runId"] = "run-003",
                ["commitPolicy"] = "auto_commit"
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
            AllowedNextActions = { "ProduceChapter" },
            SchedulerState = new AgentTaskSchedulerState
            {
                ActiveTaskId = "run-003:ProduceChapter",
                ActiveRunId = "run-003",
                ActiveChapterId = "chapter-003",
                Tasks =
                {
                    new AgentScheduledTask
                    {
                        Status = "queued",
                        NextAction = "ProduceChapter",
                        RunId = "run-003",
                        ChapterId = "chapter-003"
                    }
                }
            }
        };

        Assert.False(AgentRuntime.ShouldStopAfterToolResult(result, reflection, plan));
    }

    [Fact]
    public void ShouldEscalateSafety_UsesConfiguredMaxStepsInsteadOfHardCodedSeven()
    {
        Assert.False(AgentRuntime.ShouldEscalateSafety(7, 12));
        Assert.True(AgentRuntime.ShouldEscalateSafety(12, 12));
    }

    [Fact]
    public void RememberPendingConfirmation_StoresBlockedHighRiskToolForNextTurn()
    {
        var method = typeof(AgentRuntime).GetMethod("RememberPendingConfirmation", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var session = new AgentSession
        {
            ActiveProjectId = "project-1",
            ActiveRunId = "run-1"
        };
        var action = new AgentAction
        {
            Type = AgentActionType.ToolCall,
            Risk = "High",
            ToolCall = new AgentToolCall
            {
                Name = "ReviseCommittedChapter",
                Arguments =
                {
                    ["chapterNumber"] = "6",
                    ["revisionGoal"] = "修掉银蓝邮徽越权"
                }
            }
        };

        method!.Invoke(null, new object?[] { session, action, "修订已提交章节并覆盖书城正文。" });

        Assert.NotNull(session.WorkingMemory.PendingConfirmation);
        Assert.Equal("ReviseCommittedChapter", session.WorkingMemory.PendingConfirmation!.ToolCall!.Name);
        Assert.Equal("project-1", session.WorkingMemory.PendingConfirmation.ProjectId);
        Assert.Equal("run-1", session.WorkingMemory.PendingConfirmation.RunId);
        Assert.Equal("High", session.WorkingMemory.PendingConfirmation.Risk);
        Assert.Contains("覆盖书城正文", session.WorkingMemory.PendingConfirmation.ImpactSummary);
    }

    [Fact]
    public void ResolvePendingConfirmationAction_RestoresToolCallWhenUserConfirms()
    {
        var method = typeof(AgentRuntime).GetMethod("ResolvePendingConfirmationAction", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var session = new AgentSession();
        session.WorkingMemory.PendingConfirmation = new AgentPendingConfirmation
        {
            ToolCall = new AgentToolCall
            {
                Name = "ReviseCommittedChapter",
                Arguments = { ["chapterNumber"] = "6" }
            },
            Risk = "High",
            ProjectId = "project-1",
            RunId = "run-1"
        };

        var action = Assert.IsType<AgentAction>(method!.Invoke(null, new object[] { session, "确认" }));

        Assert.Equal(AgentActionType.ToolCall, action.Type);
        Assert.Equal("pending_confirmation", action.Source);
        Assert.Equal("ReviseCommittedChapter", action.ToolCall!.Name);
    }

    [Fact]
    public void ResolvePendingConfirmationAction_DoesNotConsumeExplanatoryConfirmationAsCommand()
    {
        var method = typeof(AgentRuntime).GetMethod("ResolvePendingConfirmationAction", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var session = BuildPendingConfirmationSession();

        var action = method!.Invoke(null, new object[] { session, "确认，允许覆盖正文" });

        Assert.Null(action);
        Assert.NotNull(session.WorkingMemory.PendingConfirmation);
    }

    [Fact]
    public void ResolvePendingConfirmationAction_DoesNotCancelExplanatoryNegativeFollowup()
    {
        var method = typeof(AgentRuntime).GetMethod("ResolvePendingConfirmationAction", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var session = BuildPendingConfirmationSession();

        var action = method!.Invoke(null, new object[] { session, "不要现在执行，先解释一下影响" });

        Assert.Null(action);
        Assert.NotNull(session.WorkingMemory.PendingConfirmation);
    }

    [Fact]
    public void ResolvePendingConfirmationAction_DoesNotTreatArbitraryFollowupAsConfirmation()
    {
        var method = typeof(AgentRuntime).GetMethod("ResolvePendingConfirmationAction", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var session = BuildPendingConfirmationSession();

        var action = method!.Invoke(null, new object[] { session, "这是什么意思？你先解释一下" });

        Assert.Null(action);
        Assert.NotNull(session.WorkingMemory.PendingConfirmation);
    }

    [Fact]
    public void PrepareConfirmationResponseState_PreservesPendingConfirmation()
    {
        var method = typeof(AgentRuntime).GetMethod("PrepareConfirmationResponseState", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var session = new AgentSession
        {
            ActiveProjectId = "project-1",
            ActiveRunId = "run-1"
        };
        session.WorkingMemory.PendingToolCall = new AgentToolCall
        {
            Name = "ReviseCommittedChapter",
            Arguments = { ["chapterNumber"] = "6" }
        };
        session.WorkingMemory.PendingConfirmation = new AgentPendingConfirmation
        {
            ToolCall = session.WorkingMemory.PendingToolCall,
            Risk = "High",
            ProjectId = "project-1",
            RunId = "run-1",
            ImpactSummary = "修订已提交章节并覆盖书城正文。"
        };

        method!.Invoke(null, new object[] { session });

        Assert.NotNull(session.WorkingMemory.PendingToolCall);
        Assert.NotNull(session.WorkingMemory.PendingConfirmation);
        Assert.Equal("ReviseCommittedChapter", session.WorkingMemory.PendingConfirmation!.ToolCall!.Name);
        Assert.Equal("project-1", session.WorkingMemory.MissionPlan.ProjectId);
        Assert.Equal("run-1", session.WorkingMemory.MissionPlan.CurrentRunId);
    }

    [Fact]
    public void BuildUserVisibleConfirmationMessage_AppendsConfirmAndCancelInstruction()
    {
        var action = new AgentAction
        {
            Type = AgentActionType.ToolCall,
            ToolCall = new AgentToolCall { Name = "ReviseCommittedChapter" }
        };

        var reply = AgentRuntime.BuildUserVisibleConfirmationMessage(
            action,
            null,
            "修订已提交章节并覆盖书城正文。");

        Assert.Contains("覆盖书城正文", reply);
        Assert.Contains("确认", reply);
        Assert.Contains("取消", reply);
    }

    [Fact]
    public void ToolPolicy_AllowsReviseCommittedChapterForAuditRunRevision()
    {
        var policy = new ToolPolicyEngine();
        var session = new AgentSession
        {
            ActiveProjectId = "project-1",
            ActiveRunId = "audit-run"
        };
        var bible = new StoryBibleDocument
        {
            AgentRuns =
            {
                new NovelAgentRun
                {
                    RunId = "audit-run",
                    TargetChapterId = "chapter-006",
                    Intent = NovelAgentIntent.ValidateContinuity,
                    Status = NovelAgentRunStatus.Validating,
                    UserGoal = "审查已提交章节正文的连续性和知识库硬事实。"
                }
            }
        };
        session.WorkingMemory.MissionPlan.SchedulerState.ActiveRunId = "audit-run";
        session.WorkingMemory.MissionPlan.SchedulerState.ActiveChapterId = "chapter-006";
        var context = new AgentObservationContext
        {
            UserMessage = "请修订并覆盖书城里的第6章，修掉银蓝邮徽越权问题。",
            TurnIntent = new TurnIntent
            {
                Type = TurnIntentType.FreeChat,
                ReferencedChapterId = "chapter-006",
                RawMessage = "请修订并覆盖书城里的第6章，修掉银蓝邮徽越权问题。"
            },
            UserTurn = new UserTurnEnvelope
            {
                DialogueAct = DialogueAct.Chat,
                TargetArtifact = "chapter-006"
            }
        };
        var call = new AgentToolCall
        {
            Name = "ReviseCommittedChapter",
            Arguments =
            {
                ["chapterId"] = "chapter-006",
                ["revisionGoal"] = "请修订并覆盖书城里的第6章，修掉银蓝邮徽越权问题。",
                ["auditRunId"] = "audit-run"
            }
        };

        var result = policy.BeforeCall(call, session, bible, context, confirmed: true);

        Assert.True(result.AllowsExecution);
        Assert.Equal("High", result.Risk);
        Assert.False(result.IsRepairable);
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

    [Fact]
    public void SelectReadOnlySnapshotReply_UsesProjectContentResultWhenReflectionIsNoToolFallback()
    {
        var method = typeof(AgentRuntime).GetMethod("SelectReadOnlySnapshotReply", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var session = new AgentSession();
        var reflection = new AgentReflection
        {
            ReplyDraft = "当前还没有新的工具执行结果。按工作流，下一步可以推进：评审章节质量、执行工具。"
        };
        var result = new AgentToolExecutionResult
        {
            Success = true,
            Message = "项目《末世邮路：银蓝邮徽》内容读取结果：\n第 2 章：第二章：邮路初探\n所属卷：第一卷：邮徽觉醒\n正文开头：黑雨停在邮局门外。",
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "project_content_query",
                Summary = "已读取 1 个章节内容。"
            }
        };

        var reply = Assert.IsType<string>(method!.Invoke(null, new object?[] { session, reflection, result }));

        Assert.Contains("第二章：邮路初探", reply);
        Assert.Contains("正文开头", reply);
        Assert.DoesNotContain("没有新的工具执行结果", reply);
    }

    [Fact]
    public void AgentPlannerFallbackMessages_DoNotClaimNoToolExecution()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "Web", "NovelAgentWeb", "Support", "AgentCore.cs"));

        Assert.DoesNotContain("本轮没有执行任何工具", source);
    }

    private static AgentSession BuildPendingConfirmationSession()
    {
        var session = new AgentSession();
        session.WorkingMemory.PendingConfirmation = new AgentPendingConfirmation
        {
            ToolCall = new AgentToolCall
            {
                Name = "ReviseCommittedChapter",
                Arguments = { ["chapterNumber"] = "6" }
            },
            Risk = "High",
            ProjectId = "project-1",
            RunId = "run-1"
        };
        return session;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "Web", "NovelAgentWeb", "Support", "AgentCore.cs");
            if (File.Exists(candidate))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
