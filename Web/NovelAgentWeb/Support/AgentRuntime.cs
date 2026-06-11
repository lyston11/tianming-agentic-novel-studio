using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Workspace;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class AgentRuntime
{
    private const int MaxRecentObservations = 16;

    // Thread-local workspace context for current request
    private static readonly AsyncLocal<NovelAgentWorkspace?> _currentWorkspace = new();
    private static readonly AsyncLocal<NovelProjectCatalog?> _currentCatalog = new();

    private NovelAgentWorkspace _workspace => _currentWorkspace.Value ?? throw new InvalidOperationException("Workspace not set for current request");
    private NovelProjectCatalog _catalog => _currentCatalog.Value ?? throw new InvalidOperationException("Catalog not set for current request");

    private readonly IWorkspaceFactory _workspaceFactory;
    private readonly ICurrentUserService _currentUserService;
    private readonly AgentSessionManager _sessionManager;
    private readonly UserSettingsManager _settingsManager;
    private readonly AgentObservationBuilder _observationBuilder;
    private readonly AgentPlanner _planner;
    private readonly ToolPolicyEngine _toolPolicyEngine;
    private readonly ReflectionEngine _reflectionEngine;
    private readonly AgentToolRegistry _toolRegistry;
    private readonly AgentMemoryService _memoryService;
    private readonly AgentMissionTaskTreeService _taskTreeService;
    private readonly AgentTaskScheduler _taskScheduler;
    private readonly MissionBlackboardRecoveryService _blackboardRecovery;
    private readonly AgentToolGuardrails _guardrails;
    private readonly ConversationKernel _conversationKernel;
    private readonly AgentRecoveryEngine _recoveryEngine;
    private readonly PhaseContextBuilder _contextBuilder;
    private AgentAction? lastAction;

    public AgentRuntime(
        IWorkspaceFactory workspaceFactory,
        ICurrentUserService currentUserService,
        AgentSessionManager sessionManager,
        UserSettingsManager settingsManager,
        AgentObservationBuilder observationBuilder,
        AgentPlanner planner,
        ToolPolicyEngine toolPolicyEngine,
        ReflectionEngine reflectionEngine,
        AgentToolRegistry toolRegistry,
        AgentMemoryService memoryService,
        AgentMissionTaskTreeService taskTreeService,
        AgentTaskScheduler taskScheduler,
        MissionBlackboardRecoveryService blackboardRecovery,
        AgentToolGuardrails guardrails,
        ConversationKernel conversationKernel,
        PhaseContextBuilder contextBuilder)
    {
        _workspaceFactory = workspaceFactory;
        _currentUserService = currentUserService;
        _sessionManager = sessionManager;
        _settingsManager = settingsManager;
        _observationBuilder = observationBuilder;
        _planner = planner;
        _toolPolicyEngine = toolPolicyEngine;
        _reflectionEngine = reflectionEngine;
        _toolRegistry = toolRegistry;
        _memoryService = memoryService;
        _taskTreeService = taskTreeService;
        _taskScheduler = taskScheduler;
        _blackboardRecovery = blackboardRecovery;
        _guardrails = guardrails;
        _conversationKernel = conversationKernel;
        _recoveryEngine = new AgentRecoveryEngine(toolRegistry, guardrails);
        _contextBuilder = contextBuilder;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Main Agent Loop — Observe / Plan / Act / Reflect
    //  Inspired by GenericAgent's agent_runner_loop and Hermes Agent's conversation_loop
    // ═══════════════════════════════════════════════════════════════

    public async Task<AgentChatResponse> RunAsync(string sessionId, string userMessage, CancellationToken ct)
    {
        // ── Acquire user-specific workspace ──
        var userId = _currentUserService.GetUserId();
        var session = await _sessionManager.GetOrCreateSessionAsync(sessionId, ct);

        // For first turn, use temp projectId; will be replaced after project resolution
        var projectId = session.ActiveProjectId ?? "temp-" + userId;

        var workspaceEntry = await _workspaceFactory.AcquireAsync(userId, projectId, ct).ConfigureAwait(false);
        try
        {
            // Set workspace context for this request across all services
            var workspace = workspaceEntry.Workspace;
            var catalog = new NovelProjectCatalog(workspace);

            _currentWorkspace.Value = workspace;
            _currentCatalog.Value = catalog;
            AgentMemoryService.SetWorkspace(workspace);
            AgentToolRegistry.SetWorkspace(workspace, catalog);
            PhaseContextBuilder.SetWorkspace(workspace);

            return await RunWithWorkspaceAsync(session, userMessage, ct).ConfigureAwait(false);
        }
        finally
        {
            _currentWorkspace.Value = null;
            _currentCatalog.Value = null;
            AgentMemoryService.ClearWorkspace();
            AgentToolRegistry.ClearWorkspace();
            PhaseContextBuilder.ClearWorkspace();
            workspaceEntry.ReleaseLease();
        }
    }

    private async Task<AgentChatResponse> RunWithWorkspaceAsync(
        AgentSession session,
        string userMessage,
        CancellationToken ct)
    {
        // ── Session setup ──
        session.UpdatedAt = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(session.Title) || session.Title == "新会话")
            session.Title = BuildSessionTitle(userMessage);

        // ── Ensure session has a project (use temp if none) ──
        if (string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            var userId = _currentUserService.GetUserId();
            session.ActiveProjectId = "temp-" + userId;
        }

        var project = await ResolveSessionProjectAsync(session, ct).ConfigureAwait(false);
        var settings = await _settingsManager.LoadAsync(ct).ConfigureAwait(false);
        var maxSteps = settings.AgentAutoContinue ? Math.Clamp(settings.AgentMaxAutoSteps, 5, 20) : 3;
        var trace = new List<AgentRuntimeStep>();
        var executedCalls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AgentObservationContext? lastContext = null;
        AgentReflection? lastReflection = null;
        AgentToolExecutionResult? lastResult = null;
        var userTurn = _conversationKernel.BuildEnvelope(session, userMessage);
        trace.Add(new AgentRuntimeStep
        {
            StepIndex = 0,
            Stage = "conversation_intent",
            StopReason = $"{userTurn.Intent.Label}:{userTurn.DialogueAct}",
        });

        // ── Legacy quick-path: old pending confirmations resume as autopilot work. ──
        var pendingAction = ResolvePendingConfirmationAction(session, userMessage);
        if (pendingAction != null)
        {
            if (pendingAction.Type == AgentActionType.FinalReply)
                return await FinishTextResponse(session, userMessage, pendingAction, null, trace, null, ct);
            // Pending confirmation resolved -> execute the tool
            return await ExecuteSingleActionAsync(session, userMessage, pendingAction, project, settings, trace, ct).ConfigureAwait(false);
        }

        await EmitAsync(session, AgentSseEventType.AgentObserving, "正在观察项目上下文...", ct);

        // ── Main loop: LLM-driven, up to maxSteps ──
        for (var step = 1; step <= maxSteps; step++)
        {
            ct.ThrowIfCancellationRequested();

            // Observe
            var bible = await WithSessionProjectAsync(session, () => _workspace.Orchestrator.GetStoryBibleAsync(ct), ct).ConfigureAwait(false);
            _blackboardRecovery.Recover(session, project, bible);
            lastContext = await WithSessionProjectAsync(session,
                () => _observationBuilder.BuildAsync(session, project, bible, userMessage, userTurn.Intent, ct), ct).ConfigureAwait(false);

            // Inject anchor prompt into context for the LLM
            lastContext.AnchorPrompt = BuildAnchorPrompt(session, project, bible, step, maxSteps, userMessage);

            trace.Add(new AgentRuntimeStep
            {
                StepIndex = step,
                Stage = "observe",
                Action = new AgentAction
                {
                    Type = AgentActionType.FinalReply,
                    Intent = "observe",
                    Brief = lastContext.MissionPlan.LastVerifiedState,
                    Source = "observation",
                },
            });

            // Plan — LLM decides what to do
            await EmitAsync(session, AgentSseEventType.AgentPlanning, $"第 {step}/{maxSteps} 步：正在决策...", ct);
            var action = await _planner.PlanActionAsync(lastContext, ct).ConfigureAwait(false);
            lastAction = action;
            session.WorkingMemory.LastDecision = action.ToDecision();
            RememberUserMessage(session, userMessage, action);
            trace.Add(new AgentRuntimeStep { StepIndex = step, Stage = "plan", Action = action });

            // ── Handle action types ──

            // First respect a valid user-facing text reply. IsNoTool means planner_failed_or_no_action,
            // not "this normal chat response did not need a tool".
            if (ShouldReturnUserFacingReply(action))
                return await FinishTextResponse(session, userMessage, action, lastContext, trace, lastReflection, ct);

            // no_action sentinel — planner/provider failed to produce a usable action.
            if (action.IsNoTool || action.ToolCall == null)
            {
                // If we have a task in progress, try to continue via scheduler
                var scheduledAction = _taskScheduler.BuildContinueAction(lastContext);
                if (scheduledAction != null)
                {
                    action = scheduledAction;
                }
                else
                {
                    // Ask user what to do next
                    return await FinishTextResponse(session, userMessage, new AgentAction
                    {
                        Type = AgentActionType.FinalReply,
                        Intent = "no_tool_fallback",
                        Reply = BuildStatusSummary(session, bible),
                        Suggestions = new[] { "继续下一步", "查看当前状态" },
                        Source = "runtime_no_tool",
                    }, lastContext, trace, lastReflection, ct);
                }
            }

            // Retrieve → convert to SearchCreativeKnowledge tool call
            if (action.Type == AgentActionType.Retrieve)
            {
                action.Type = AgentActionType.ToolCall;
                action.ToolCall ??= new AgentToolCall
                {
                    Name = "SearchCreativeKnowledge",
                    Arguments = { ["query"] = action.RagQueries.FirstOrDefault() ?? userMessage },
                };
            }

            // Autopilot mode: planner confirm_request is treated as an executable tool call.
            if (action.Type == AgentActionType.ConfirmRequest)
            {
                if (action.ToolCall == null)
                    return await FinishTextResponse(session, userMessage, BuildFallbackReplyAction(action), lastContext, trace, lastReflection, ct);
                action.Type = AgentActionType.ToolCall;
                action.RequiresConfirmation = false;
                action.Source = FirstNonEmpty(action.Source, "autopilot_confirm_request");
            }

            // ── Tool execution pipeline ──
            if (action.ToolCall == null)
                return await FinishTextResponse(session, userMessage, BuildFallbackReplyAction(action), lastContext, trace, lastReflection, ct);

            NormalizeStoryFoundationCandidateSelection(action, lastContext, bible, session);

            // Policy check
            var repairRedirects = 0;
            var confirmed = IsPendingConfirmationAction(action, session);
            var policy = _toolPolicyEngine.BeforeCall(action.ToolCall, session, bible, lastContext, confirmed);
            trace.Add(new AgentRuntimeStep { StepIndex = step, Stage = "policy", Action = action, StopReason = policy.Message });

            while (policy.IsRepairable &&
                   policy.ReplacementAction?.ToolCall != null &&
                   repairRedirects < 2)
            {
                var repairResult = BuildGovernanceResult(
                    action.ToolCall.Name,
                    "policy_repairable",
                    FirstNonEmpty(policy.UserFacingMessage, policy.Message),
                    session,
                    "policy_observation",
                    policy);
                var repairObservation = AddRuntimeObservation(session, step, action.ToolCall.Name, repairResult, "policy_observation");
                trace.Add(new AgentRuntimeStep
                {
                    StepIndex = step,
                    Stage = "policy_repair",
                    Action = action,
                    Observation = repairObservation,
                    StopReason = repairResult.Message
                });

                action = policy.ReplacementAction;
                lastAction = action;
                if (action.ToolCall == null)
                    return await FinishTextResponse(session, userMessage, BuildFallbackReplyAction(action), lastContext, trace, lastReflection, ct);

                NormalizeStoryFoundationCandidateSelection(action, lastContext, bible, session);
                confirmed = IsPendingConfirmationAction(action, session);
                policy = _toolPolicyEngine.BeforeCall(action.ToolCall, session, bible, lastContext, confirmed);
                trace.Add(new AgentRuntimeStep { StepIndex = step, Stage = "policy", Action = action, StopReason = policy.Message });
                repairRedirects++;
            }

            if (!policy.IsRepairable && policy.ReplacementAction != null)
            {
                action = policy.ReplacementAction;
                lastAction = action;
                if (action.ToolCall == null)
                    return await FinishTextResponse(session, userMessage, BuildFallbackReplyAction(action), lastContext, trace, lastReflection, ct);
            }
            else if (!policy.AllowsExecution)
            {
                var govResult = BuildGovernanceResult(
                    action.ToolCall.Name,
                    policy.IsRepairable ? "policy_repair_limit" : "policy_blocked",
                    FirstNonEmpty(policy.UserFacingMessage, policy.Message),
                    session,
                    "policy_observation",
                    policy);
                return await FinishGovernanceResponseAsync(session, userMessage, action, lastContext, trace, step, action.ToolCall.Name, govResult, "policy_observation", project, ct).ConfigureAwait(false);
            }

            action.RequiresConfirmation = false;
            action.Risk = policy.Risk;

            // Dedup check
            var fingerprint = BuildToolCallFingerprint(action.ToolCall);
            if (!executedCalls.Add(fingerprint))
            {
                var govResult = BuildGovernanceResult(action.ToolCall.Name, "repeated_call", "本轮已有同名同参数工具结果，请基于已有观察继续。", session, "runtime_observation");
                return await FinishGovernanceResponseAsync(session, userMessage, action, lastContext, trace, step, action.ToolCall.Name, govResult, "runtime_observation", project, ct).ConfigureAwait(false);
            }

            // Guardrail check
            var guardrail = _guardrails.Check(action.ToolCall.Name, action.ToolCall.Arguments, lastResult?.Success ?? true);
            if (guardrail.IsBlocked)
            {
                var govResult = BuildGovernanceResult(action.ToolCall.Name, "guardrail_blocked", guardrail.Message, session, "guardrail_observation");
                return await FinishGovernanceResponseAsync(session, userMessage, action, lastContext, trace, step, action.ToolCall.Name, govResult, "guardrail_observation", project, ct).ConfigureAwait(false);
            }
            if (guardrail.IsWarning)
            {
                trace.Add(new AgentRuntimeStep { StepIndex = step, Stage = "guardrail_warn", Action = action, StopReason = guardrail.Message });
            }

            // Execute tool
            await EmitAsync(session, AgentSseEventType.AgentActing, $"正在执行：{action.ToolCall.Name}", ct, action.ToolCall);
            confirmed = IsAutopilotAuthorizedAction(action, session);
            var result = string.Equals(action.ToolCall.Name, "StartNewNovelProject", StringComparison.OrdinalIgnoreCase)
                ? await _toolRegistry.ExecuteAsync(action.ToolCall, session, bible, confirmed, ct).ConfigureAwait(false)
                : await WithSessionProjectAsync(session,
                    () => _toolRegistry.ExecuteAsync(action.ToolCall, session, bible, confirmed, ct), ct).ConfigureAwait(false);

            if (result.Success)
            {
                session.WorkingMemory.PendingToolCall = null;
                session.WorkingMemory.PendingConfirmation = null;
            }

            // Handle tool execution failure with automatic recovery
            if (!result.Success)
            {
                // Attempt automatic recovery
                var recoveryResult = await _recoveryEngine.RecoverFromFailureAsync(
                    action.ToolCall,
                    result,
                    session,
                    bible,
                    ct).ConfigureAwait(false);

                if (recoveryResult.Recovered && recoveryResult.RetryResult != null)
                {
                    // Recovery succeeded, use retry result
                    result = recoveryResult.RetryResult;
                    var recoveryObservation = new AgentRuntimeObservation
                    {
                        StepIndex = step,
                        ObservationType = "tool_recovery",
                        ToolName = action.ToolCall.Name,
                        Success = result.Success,
                        Message = $"工具 {action.ToolCall.Name} 初次失败后自动恢复成功",
                        RunId = result.RunId ?? session.ActiveRunId ?? string.Empty,
                        Phase = result.Phase,
                    };
                    session.WorkingMemory.RecentObservations.Add(recoveryObservation);
                    trace.Add(new AgentRuntimeStep { StepIndex = step, Stage = "recovery", Action = action, Observation = recoveryObservation });
                }
                else
                {
                    // Recovery failed or not recoverable
                    var failureObservation = new AgentRuntimeObservation
                    {
                        StepIndex = step,
                        ObservationType = "tool_failure",
                        ToolName = action.ToolCall.Name,
                        Success = false,
                        Message = $"工具 {action.ToolCall.Name} 失败：{result.Message}。{recoveryResult.Message}",
                        RunId = result.RunId ?? session.ActiveRunId ?? string.Empty,
                        Phase = result.Phase,
                    };
                    session.WorkingMemory.RecentObservations.Add(failureObservation);
                    trace.Add(new AgentRuntimeStep { StepIndex = step, Stage = "act", Action = action, Observation = failureObservation });

                    // Break the loop as before
                    await EmitAsync(session, AgentSseEventType.AgentReflecting, "正在根据失败观察重新判断...", ct).ConfigureAwait(false);
                    var failReflectContext = await WithSessionProjectAsync(session,
                        async () =>
                        {
                            var refreshedBible = await _workspace.Orchestrator.GetStoryBibleAsync(ct).ConfigureAwait(false);
                            return await _observationBuilder.BuildAsync(session, project, refreshedBible, userMessage, userTurn.Intent, ct).ConfigureAwait(false);
                        }, ct).ConfigureAwait(false);
                    var failReflection = await _reflectionEngine.ReflectAsync(failReflectContext, failureObservation, ct).ConfigureAwait(false);
                    ApplyReflection(session, failReflection);
                    await SyncMissionPlanAsync(session, project, result, action, failReflection, ct).ConfigureAwait(false);
                    trace.Add(new AgentRuntimeStep { StepIndex = step, Stage = "reflect", Action = action, Observation = failureObservation, Reflection = failReflection });
                    return await FinishReflectionResponse(session, userMessage, action, failReflectContext, trace, failReflection, result, ct);
                }
            }

            lastResult = result;
            _guardrails.RecordSuccess(action.ToolCall.Name);
            if (!string.IsNullOrWhiteSpace(result.Phase)) session.Phase = result.Phase;
            project = await ResolveSessionProjectAsync(session, ct).ConfigureAwait(false);
            await SyncMissionPlanAsync(session, project, result, action, null, ct).ConfigureAwait(false);
            await PublishToolResultAsync(session, result, ct).ConfigureAwait(false);

            var observation = AddRuntimeObservation(session, step, action.ToolCall.Name, result);
            trace.Add(new AgentRuntimeStep { StepIndex = step, Stage = "act", Action = action, Observation = observation });

            // Reflect
            await EmitAsync(session, AgentSseEventType.AgentReflecting, "正在反思...", ct, observation);
            var reflectContext = await WithSessionProjectAsync(session,
                async () =>
                {
                    var refreshedBible = await _workspace.Orchestrator.GetStoryBibleAsync(ct).ConfigureAwait(false);
                    return await _observationBuilder.BuildAsync(session, project, refreshedBible, userMessage, userTurn.Intent, ct).ConfigureAwait(false);
                }, ct).ConfigureAwait(false);
            var reflection = await _reflectionEngine.ReflectAsync(reflectContext, observation, ct).ConfigureAwait(false);
            lastReflection = reflection;
            ApplyReflection(session, reflection);
            await SyncMissionPlanAsync(session, project, result, action, reflection, ct).ConfigureAwait(false);
            trace.Add(new AgentRuntimeStep { StepIndex = step, Stage = "reflect", Action = action, Observation = observation, Reflection = reflection });
            await EmitAsync(session, AgentSseEventType.MissionUpdated, "任务记忆已更新。", ct, session.WorkingMemory.MissionPlan);

            // Loop termination checks
            if (!result.Success || reflection.RequiresUserInput || reflection.GoalSatisfied || !reflection.ShouldContinue)
                return await FinishReflectionResponse(session, userMessage, action, lastContext, trace, reflection, result, ct);

            // Safety escalation at turn 7
            if (step >= 7)
            {
                var safetyAction = new AgentAction
                {
                    Type = AgentActionType.FinalReply,
                    Intent = "safety_escalation",
                    Reply = $"已在 {step} 步内完成多轮操作。我先暂停，避免越权推进。" + (reflection.ReplyDraft ?? ""),
                    Suggestions = result.Suggestions ?? new[] { "继续推进", "查看当前状态" },
                    Source = "runtime_safety",
                };
                return await FinishTextResponse(session, userMessage, safetyAction, lastContext, trace, reflection, ct);
            }

            project = await ResolveSessionProjectAsync(session, ct).ConfigureAwait(false);
        }

        // Max steps reached
        return await FinishTextResponse(session, userMessage, new AgentAction
        {
            Type = AgentActionType.FinalReply,
            Intent = "max_steps",
            Reply = "已达到本轮自动执行步数上限。你可以告诉我下一步要推进什么。",
            Suggestions = lastResult?.Suggestions ?? new[] { "查看当前状态", "继续下一步" },
            Source = "runtime_guard",
        }, lastContext, trace, lastReflection, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Anchor Prompt — Inject compressed memory each turn
    //  Inspired by GenericAgent's _get_anchor_prompt
    // ═══════════════════════════════════════════════════════════════

    private string BuildAnchorPrompt(AgentSession session, NovelProjectInfo project, StoryBibleDocument bible, int step, int maxSteps, string userMessage)
    {
        var parts = new List<string>();
        var mission = session.WorkingMemory.Mission;
        var plan = session.WorkingMemory.MissionPlan;

        // Working memory block
        var wm = new List<string>();
        if (!string.IsNullOrWhiteSpace(mission.CurrentGoal)) wm.Add($"当前目标: {mission.CurrentGoal}");
        if (!string.IsNullOrWhiteSpace(mission.PendingQuestion)) wm.Add($"开放问题: {mission.PendingQuestion}");
        if (!string.IsNullOrWhiteSpace(mission.PendingUserDecision)) wm.Add($"等待决策: {mission.PendingUserDecision}");
        if (session.WorkingMemory.UserPreferences.Count > 0)
            wm.Add($"用户偏好: {string.Join("; ", session.WorkingMemory.UserPreferences.TakeLast(5))}");
        if (wm.Count > 0) parts.Add($"<working_memory>\n{string.Join("\n", wm)}\n</working_memory>");

        // Task state block
        var ts = new List<string>();
        ts.Add($"项目: {project.Title} | 阶段: {session.Phase}");
        if (!string.IsNullOrWhiteSpace(plan.Stage)) ts.Add($"任务阶段: {plan.Stage}");
        var activeTask = plan.SchedulerState.Tasks.FirstOrDefault(t =>
            string.Equals(t.TaskId, plan.SchedulerState.ActiveTaskId, StringComparison.OrdinalIgnoreCase));
        if (activeTask != null) ts.Add($"活跃任务: {activeTask.ChapterId} ({activeTask.TaskType}) → {activeTask.NextAction}");
        if (plan.AllowedNextActions.Count > 0) ts.Add($"允许操作: {string.Join(", ", plan.AllowedNextActions.Take(5))}");
        ts.Add($"Story Bible: {(bible.Constitution != null ? $"{bible.Constitution.Genre}/{bible.Constitution.SubGenre}" : "未固化")}");
        ts.Add($"卷: {bible.VolumeArcs.Count} | 账本: 设定{bible.CanonLedger.Count}/伏笔{bible.ForeshadowLedger.Count}/角色{bible.CharacterLedger.Count}");
        parts.Add($"<task_state>\n{string.Join("\n", ts)}\n</task_state>");

        // Compressed history
        var recentHistory = session.ChatHistory.TakeLast(10).Select(t =>
            $"{(t.Role == "user" ? "U" : "A")}: {t.Content.Replace("\n", " ").Trim().Take(120)}");
        if (recentHistory.Any())
            parts.Add($"<history>\n{string.Join("\n", recentHistory)}\n</history>");

        // Recent observations summary
        var recentObs = session.WorkingMemory.RecentObservations.TakeLast(5).Select(o =>
        {
            var repair = o.IsRepairable && !string.IsNullOrWhiteSpace(o.RecommendedToolName)
                ? $" | repair={o.RecommendedToolName} missing={o.MissingPrerequisite}"
                : string.Empty;
            return $"{o.ToolName}: {(o.Success ? "OK" : "FAIL")} - {o.Message?.Take(80)}{repair}";
        });
        if (recentObs.Any())
            parts.Add($"<recent_observations>\n{string.Join("\n", recentObs)}\n</recent_observations>");

        // Turn counter with safety escalations
        var turnInfo = $"Turn {step}/{maxSteps}";
        if (step >= 5) turnInfo += "\n⚠ 接近步数上限，优先完成当前目标或询问用户。";
        if (step >= 7) turnInfo += "\n⚠ 不要无效重试。换策略或直接问用户。";
        parts.Add($"<turn_counter>\n{turnInfo}\n</turn_counter>");

        return string.Join("\n\n", parts);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Confirmation detection — enhanced with colloquial Chinese
    // ═══════════════════════════════════════════════════════════════

    private static AgentAction? ResolvePendingConfirmationAction(AgentSession session, string userMessage)
    {
        var pending = session.WorkingMemory.PendingConfirmation;
        if (pending?.ToolCall == null) return null;

        var msg = userMessage.Trim();

        if (IsCancel(msg))
        {
            session.WorkingMemory.PendingConfirmation = null;
            session.WorkingMemory.PendingToolCall = null;
            return new AgentAction
            {
                Type = AgentActionType.FinalReply,
                Intent = "cancel_pending",
                Reply = "好，已取消这个操作。",
                Suggestions = new[] { "查看当前状态", "继续调整" },
                Source = "pending_cancel",
            };
        }

        if (IsExplicitConfirmation(msg) || msg.Length > 0)
        {
            return new AgentAction
            {
                Type = AgentActionType.ToolCall,
                Intent = "autopilot_pending",
                ToolCall = pending.ToolCall,
                Risk = pending.Risk,
                Brief = "Autopilot 自动执行原待确认动作。",
                Source = "pending_confirmation",
            };
        }

        return null;
    }

    private static bool IsExplicitConfirmation(string msg)
    {
        // Direct confirmation words
        if (msg.Contains("确认") || msg.Contains("提交") || msg.Contains("就选") || msg.Contains("选这个"))
            return true;
        if (msg.Contains("按推荐") || msg.Contains("推荐的") || msg.Contains("用推荐"))
            return true;
        // Colloquial confirmations
        if (msg is "行" or "好" or "可以" or "ok" or "yes" or "嗯" or "对" or "是" or "搞" or "来" or "干" or "上" or "冲")
            return true;
        if (msg.StartsWith("行") || msg.StartsWith("好的") || msg.StartsWith("可以的") || msg.StartsWith("没问题"))
            return true;
        return false;
    }

    private static bool IsCancel(string msg)
    {
        if (msg.Contains("取消") || msg.Contains("不要") || msg.Contains("先不") || msg.Contains("算了"))
            return true;
        if (msg is "不" or "停" or "别" or "no" or "取消")
            return true;
        return false;
    }

    private static bool NeedsConfirmation(AgentAction action, AgentSession session)
    {
        return false;
    }

    private static bool IsPendingConfirmationAction(AgentAction action, AgentSession session) =>
        string.Equals(action.Source, "pending_confirmation", StringComparison.OrdinalIgnoreCase) &&
        session.WorkingMemory.PendingConfirmation?.ToolCall != null;

    private static bool IsAutopilotAuthorizedAction(AgentAction action, AgentSession session) =>
        IsPendingConfirmationAction(action, session) ||
        action.ToolCall?.Name is "CommitStoryFoundation" or "CommitVolumeArc" or
            "GenerateChapterWithChanges" or "RepairChapterDraft" or "CommitValidatedChapter";

    // ═══════════════════════════════════════════════════════════════
    //  Helper methods
    // ═══════════════════════════════════════════════════════════════

    private async Task<AgentChatResponse> ExecuteSingleActionAsync(
        AgentSession session, string userMessage, AgentAction action,
        NovelProjectInfo project, UserSettings settings, List<AgentRuntimeStep> trace, CancellationToken ct)
    {
        var confirmed = IsAutopilotAuthorizedAction(action, session);
        var scopedProject = confirmed
            ? await ResolvePendingConfirmationProjectAsync(session, ct).ConfigureAwait(false)
            : project;
        if (scopedProject == null)
        {
            var blocked = BuildGovernanceResult(
                action.ToolCall?.Name ?? "PendingConfirmation",
                "pending_project_missing",
                "这个操作绑定的小说项目已经不存在，我不会执行。请重新生成候选或回到正确项目继续。",
                session,
                "policy_observation");
            trace.Add(new AgentRuntimeStep { StepIndex = 1, Stage = "policy", Action = action, StopReason = blocked.Message });
            return await FinishGovernanceResponseAsync(session, userMessage, action, null, trace, 1, action.ToolCall?.Name ?? "PendingConfirmation", blocked, "policy_observation", project, ct).ConfigureAwait(false);
        }

        var result = await WithProjectScopeAsync(session, scopedProject,
            async () =>
            {
                var scopedBible = await _workspace.Orchestrator.GetStoryBibleAsync(ct).ConfigureAwait(false);
                var scopedContext = await _observationBuilder.BuildAsync(session, scopedProject, scopedBible, userMessage, GetCurrentTurnIntent(session, userMessage), ct).ConfigureAwait(false);
                NormalizeStoryFoundationCandidateSelection(action, scopedContext, scopedBible, session);
                var policy = _toolPolicyEngine.BeforeCall(action.ToolCall!, session, scopedBible, scopedContext, confirmed);
                trace.Add(new AgentRuntimeStep { StepIndex = 1, Stage = "policy", Action = action, StopReason = policy.Message });
                if (!policy.AllowsExecution)
                {
                    return BuildGovernanceResult(
                        action.ToolCall!.Name,
                        "policy_blocked",
                        policy.Message,
                        session,
                        "policy_observation");
                }

                return await _toolRegistry.ExecuteAsync(action.ToolCall!, session, scopedBible, confirmed, ct).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        result.RequiresConfirmation = false;
        if (result.Success)
        {
            session.WorkingMemory.PendingToolCall = null;
            session.WorkingMemory.PendingConfirmation = null;
        }
        project = scopedProject;
        await SyncMissionPlanAsync(session, project, result, action, null, ct).ConfigureAwait(false);
        await PublishToolResultAsync(session, result, ct).ConfigureAwait(false);
        var observation = AddRuntimeObservation(session, 1, action.ToolCall!.Name, result);
        trace.Add(new AgentRuntimeStep { StepIndex = 1, Stage = "act", Action = action, Observation = observation });

        var reflectContext = await WithSessionProjectAsync(session,
            async () =>
            {
                var refreshedBible = await _workspace.Orchestrator.GetStoryBibleAsync(ct).ConfigureAwait(false);
                return await _observationBuilder.BuildAsync(session, project, refreshedBible, userMessage, GetCurrentTurnIntent(session, userMessage), ct).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        var reflection = await _reflectionEngine.ReflectAsync(reflectContext, observation, ct).ConfigureAwait(false);
        ApplyReflection(session, reflection);
        await SyncMissionPlanAsync(session, project, result, action, reflection, ct).ConfigureAwait(false);
        return await FinishReflectionResponse(session, userMessage, action, reflectContext, trace, reflection, result, ct);
    }

    private async Task<NovelProjectInfo> ResolveSessionProjectAsync(AgentSession session, CancellationToken ct)
    {
        NovelProjectInfo? project = null;
        if (!string.IsNullOrWhiteSpace(session.ActiveProjectId))
            project = await _catalog.FindAsync(session.ActiveProjectId, ct).ConfigureAwait(false);
        project ??= await _catalog.GetActiveAsync(ct).ConfigureAwait(false);
        session.ActiveProjectId = project.Id;
        return project;
    }

    private async Task<NovelProjectInfo?> ResolvePendingConfirmationProjectAsync(AgentSession session, CancellationToken ct)
    {
        var pendingProjectId = session.WorkingMemory.PendingConfirmation?.ProjectId;
        if (!string.IsNullOrWhiteSpace(pendingProjectId))
            return await _catalog.FindAsync(pendingProjectId, ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            var sessionProject = await _catalog.FindAsync(session.ActiveProjectId, ct).ConfigureAwait(false);
            if (sessionProject != null) return sessionProject;
        }

        return await _catalog.GetActiveAsync(ct).ConfigureAwait(false);
    }

    private async Task<T> WithSessionProjectAsync<T>(AgentSession session, Func<Task<T>> operation, CancellationToken ct)
    {
        var project = !string.IsNullOrWhiteSpace(session.ActiveProjectId)
            ? await _catalog.FindAsync(session.ActiveProjectId, ct).ConfigureAwait(false)
            : null;
        project ??= await _catalog.GetActiveAsync(ct).ConfigureAwait(false);
        return await _catalog.WithProjectAsync(project, operation, ct).ConfigureAwait(false);
    }

    private async Task<T> WithProjectScopeAsync<T>(AgentSession session, NovelProjectInfo project, Func<Task<T>> operation, CancellationToken ct)
    {
        var previousProjectId = session.ActiveProjectId;
        session.ActiveProjectId = project.Id;
        try
        {
            return await _catalog.WithProjectAsync(project, operation, ct).ConfigureAwait(false);
        }
        finally
        {
            session.ActiveProjectId = previousProjectId;
        }
    }

    private async Task<AgentChatResponse> FinishTextResponse(
        AgentSession session, string userMessage, AgentAction action,
        AgentObservationContext? context, IReadOnlyList<AgentRuntimeStep> trace, AgentReflection? reflection,
        CancellationToken ct)
    {
        var reply = FirstNonEmpty(action.Reply, reflection?.ReplyDraft, action.Brief, "已完成本轮分析。");
        AddChatTurn(session, "user", userMessage);
        AddChatTurn(session, "assistant", reply);
        session.Phase = action.Type == AgentActionType.Clarify ? "awaiting_user_foundation" : session.Phase;
        await _sessionManager.SaveSessionAsync(session, ct);
        return BuildResponse(session, reply, action.Suggestions, action, context, trace);
    }

    private async Task<AgentChatResponse> FinishReflectionResponse(
        AgentSession session, string userMessage, AgentAction action,
        AgentObservationContext? context, IReadOnlyList<AgentRuntimeStep> trace,
        AgentReflection reflection, AgentToolExecutionResult result, CancellationToken ct)
    {
        var reply = FirstNonEmpty(reflection.ReplyDraft, reflection.Summary, result.Message);
        AddChatTurn(session, "user", userMessage);
        AddChatTurn(session, "assistant", reply);
        await _sessionManager.SaveSessionAsync(session, ct);
        return BuildResponse(session, reply, result.Suggestions, action, context, trace);
    }

    private async Task<AgentChatResponse> FinishGovernanceResponseAsync(
        AgentSession session, string userMessage, AgentAction action,
        AgentObservationContext? context, List<AgentRuntimeStep> trace,
        int step, string toolName, AgentToolExecutionResult result,
        string observationType, NovelProjectInfo project, CancellationToken ct)
    {
        var observation = AddRuntimeObservation(session, step, toolName, result, observationType);
        trace.Add(new AgentRuntimeStep { StepIndex = step, Stage = observationType, Action = action, Observation = observation });

        await EmitAsync(session, AgentSseEventType.AgentReflecting, "正在根据观察重新判断...", ct).ConfigureAwait(false);
        var reflectContext = context ?? await WithSessionProjectAsync(session,
            async () =>
            {
                var bible = await _workspace.Orchestrator.GetStoryBibleAsync(ct).ConfigureAwait(false);
                return await _observationBuilder.BuildAsync(session, project, bible, userMessage, GetCurrentTurnIntent(session, userMessage), ct).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        var reflection = await _reflectionEngine.ReflectAsync(reflectContext, observation, ct).ConfigureAwait(false);
        ApplyReflection(session, reflection);
        await SyncMissionPlanAsync(session, project, result, action, reflection, ct).ConfigureAwait(false);
        trace.Add(new AgentRuntimeStep { StepIndex = step, Stage = "reflect", Action = action, Observation = observation, Reflection = reflection });
        return await FinishReflectionResponse(session, userMessage, action, reflectContext, trace, reflection, result, ct);
    }

    private async Task<AgentChatResponse> FinishConfirmationResponse(
        AgentSession session, string userMessage, AgentAction action,
        AgentObservationContext? context, IReadOnlyList<AgentRuntimeStep> trace, string? message, CancellationToken ct)
    {
        var confirmationMessage = BuildUserVisibleConfirmationMessage(action, context, message);
        session.WorkingMemory.PendingToolCall = null;
        session.WorkingMemory.PendingConfirmation = null;
        var plan = session.WorkingMemory.MissionPlan ??= new AgentMissionPlan();
        plan.Status = FirstNonEmpty(plan.Status, plan.Stage, "active");
        plan.ProjectId = session.ActiveProjectId;
        plan.CurrentRunId = session.ActiveRunId ?? plan.CurrentRunId;
        plan.UpdatedAt = DateTime.UtcNow;
        var reply = confirmationMessage;
        AddChatTurn(session, "user", userMessage);
        AddChatTurn(session, "assistant", reply);
        await _sessionManager.SaveSessionAsync(session, ct);
        return BuildResponse(session, reply, new[] { "继续执行", "调整方案" }, action, context, trace);
    }

    private AgentChatResponse BuildResponse(
        AgentSession session, string reply, IReadOnlyList<string> suggestions,
        AgentAction action, AgentObservationContext? context, IReadOnlyList<AgentRuntimeStep> trace) =>
        new(
            reply,
            suggestions.Count > 0 ? suggestions : new[] { "查看当前状态", "继续下一步" },
            session.SessionId,
            session.ActiveRunId,
            session.Phase,
            action.ToDecision() is { } decision ? AgentDecisionTrace.From(decision) : null,
            context?.Rag,
            AgentWorkingMemorySnapshot.From(session.WorkingMemory),
            trace.ToArray(),
            session.WorkingMemory.MissionPlan,
            null);

    private static AgentRuntimeObservation AddRuntimeObservation(
        AgentSession session, int step, string toolName,
        AgentToolExecutionResult result, string observationType = "tool_result")
    {
        var observation = new AgentRuntimeObservation
        {
            StepIndex = step,
            ObservationType = observationType,
            ToolName = toolName,
            Success = result.Success,
            RequiresConfirmation = false,
            Risk = result.Risk,
            Message = TrimForReply(result.Message, 500),
            RunId = result.RunId ?? session.ActiveRunId ?? string.Empty,
            Phase = string.IsNullOrWhiteSpace(result.Phase) ? session.Phase : result.Phase,
            IsRepairable = result.IsRepairable,
            RecommendedToolName = result.RecommendedToolName,
            RecommendedArguments = new Dictionary<string, string>(result.RecommendedArguments, StringComparer.OrdinalIgnoreCase),
            MissingPrerequisite = result.MissingPrerequisite,
            Artifact = result.Artifact,
        };
        session.WorkingMemory.RecentObservations.Add(observation);
        if (session.WorkingMemory.RecentObservations.Count > MaxRecentObservations)
            session.WorkingMemory.RecentObservations.RemoveRange(0, session.WorkingMemory.RecentObservations.Count - MaxRecentObservations);
        return observation;
    }

    private static AgentToolExecutionResult BuildGovernanceResult(
        string toolName,
        string phase,
        string message,
        AgentSession session,
        string artifactType,
        ToolPolicyResult? policy = null) =>
        new()
        {
            Success = false,
            Message = message,
            Phase = phase,
            RunId = session.ActiveRunId,
            IsRepairable = policy?.IsRepairable ?? false,
            RecommendedToolName = policy?.RecommendedToolName ?? string.Empty,
            RecommendedArguments = policy == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(policy.RecommendedArguments, StringComparer.OrdinalIgnoreCase),
            MissingPrerequisite = policy?.MissingPrerequisite ?? string.Empty,
            Artifact = new AgentToolArtifact
            {
                ArtifactType = artifactType,
                ArtifactId = $"{artifactType}-{Guid.NewGuid():N}",
                ProjectId = session.ActiveProjectId,
                RunId = session.ActiveRunId ?? string.Empty,
                Summary = $"{toolName}:{phase}",
                NextHints = policy?.IsRepairable == true && !string.IsNullOrWhiteSpace(policy.RecommendedToolName)
                    ? new[] { policy.RecommendedToolName, "reflect", "query_blackboard" }
                    : new[] { "reflect", "query_blackboard" },
                VisibleInWorkflow = true,
                VisibleInLibrary = false,
                UserVisibleStatus = policy?.IsRepairable == true ? "Agent 已识别可补前置步骤" : "Agent 已保留当前任务状态",
            },
            Suggestions = policy?.IsRepairable == true && !string.IsNullOrWhiteSpace(policy.RecommendedToolName)
                ? new[] { $"执行 {policy.RecommendedToolName}", "查看当前状态", "继续任务" }
                : new[] { "查看当前状态", "继续任务", "补充创作简报" },
        };

    private static void ApplyReflection(AgentSession session, AgentReflection reflection)
    {
        var plan = session.WorkingMemory.MissionPlan ??= new AgentMissionPlan();
        plan.ReviewReports = reflection.QualityGate.ReviewReports.ToList();
        plan.QualityArbiterDecision = reflection.QualityGate.ArbiterDecision;
        foreach (var item in reflection.CompletedItems.Where(i => !string.IsNullOrWhiteSpace(i)))
            if (!plan.CompletedItems.Contains(item)) plan.CompletedItems.Add(item);
        foreach (var item in reflection.NewTodoItems.Where(i => !string.IsNullOrWhiteSpace(i)))
            if (!plan.TodoQueue.Contains(item)) plan.TodoQueue.Add(item);
        foreach (var item in reflection.Blockers.Where(i => !string.IsNullOrWhiteSpace(i)))
            if (!plan.Blockers.Contains(item)) plan.Blockers.Add(item);
        if (!string.IsNullOrWhiteSpace(reflection.NextIntent)) plan.Stage = reflection.NextIntent;
        plan.Status = reflection.Blockers.Count > 0
            ? "blocked" : reflection.GoalSatisfied ? "completed" : FirstNonEmpty(reflection.NextIntent, plan.Stage, "active");
        plan.UpdatedAt = DateTime.UtcNow;
    }

    private static void SyncMissionPlan(AgentSession session, NovelProjectInfo project, AgentToolExecutionResult? result, AgentAction? action)
    {
        var plan = session.WorkingMemory.MissionPlan ??= new AgentMissionPlan();
        if (string.IsNullOrWhiteSpace(plan.MissionId)) plan.MissionId = Guid.NewGuid().ToString("N");
        plan.ProjectId = project.Id;
        plan.ProjectTitle = project.Title;
        plan.CurrentRunId = result?.RunId ?? session.ActiveRunId ?? plan.CurrentRunId;
        if (!string.IsNullOrWhiteSpace(result?.Phase)) plan.Stage = MapMissionStage(result.Phase);
        else if (!string.IsNullOrWhiteSpace(action?.Intent)) plan.Stage = action.Intent;
        plan.Status = result is { Success: false } ? "blocked" : FirstNonEmpty(plan.Status, plan.Stage, "active");
        if (result?.Artifact != null && !string.IsNullOrWhiteSpace(result.Artifact.Summary) && !plan.CompletedItems.Contains(result.Artifact.Summary))
            plan.CompletedItems.Add(result.Artifact.Summary);
        plan.UpdatedAt = DateTime.UtcNow;
    }

    private async Task SyncMissionPlanAsync(AgentSession session, NovelProjectInfo project, AgentToolExecutionResult? result, AgentAction? action, AgentReflection? reflection, CancellationToken ct)
    {
        SyncMissionPlan(session, project, result, action);
        var bible = await WithSessionProjectAsync(session, () => _workspace.Orchestrator.GetStoryBibleAsync(ct), ct).ConfigureAwait(false);
        _taskTreeService.Sync(session, project, bible, result, action, reflection);
        await _memoryService.PersistAsync(session, project, bible, reflection, ct).ConfigureAwait(false);
    }

    private async Task PublishToolResultAsync(AgentSession session, AgentToolExecutionResult result, CancellationToken ct)
    {
        var type = result.Success ? AgentSseEventType.StepComplete : AgentSseEventType.StepFail;
        await EmitAsync(session, type, result.Message, ct, result.Data, result.RunId);
        if (result.Data is NovelAgentRun run)
            await EmitAsync(session, AgentSseEventType.RunCreated, result.Message, ct, run, run.RunId);
    }

    private Task EmitAsync(AgentSession session, string type, string message, CancellationToken ct, object? data = null, string? runId = null) =>
        _sessionManager.SendEventAsync(session.SessionId, new AgentSseEvent { Type = type, RunId = runId, Message = message, Data = data }, ct);

    private static void RememberUserMessage(AgentSession session, string userMessage, AgentAction action)
    {
        if (action.Intent is "clarify_story" or "create_foundation" or "plan_chapter" or "start_new_novel_project")
            session.WorkingMemory.CurrentGoal = userMessage.Trim();
        if (userMessage.Contains("不要") || userMessage.Contains("不想") || userMessage.Contains("避免"))
        {
            var preference = userMessage.Trim();
            if (!session.WorkingMemory.UserPreferences.Contains(preference))
                session.WorkingMemory.UserPreferences.Add(preference);
        }
        if (session.WorkingMemory.UserPreferences.Count > MaxRecentObservations)
            session.WorkingMemory.UserPreferences.RemoveRange(0, session.WorkingMemory.UserPreferences.Count - MaxRecentObservations);
    }

    private static void AddChatTurn(AgentSession session, string role, string content)
    {
        session.ChatHistory.Add(new AgentConversationTurn { Role = role, Content = content.Trim(), CreatedAt = DateTime.UtcNow });
        if (session.ChatHistory.Count > 40) session.ChatHistory.RemoveRange(0, session.ChatHistory.Count - 40);
    }

    private static AgentAction BuildFallbackReplyAction(AgentAction action) => new()
    {
        Type = AgentActionType.FinalReply,
        Intent = action.Intent,
        Reply = FirstNonEmpty(action.Reply, action.Brief, "没有可执行动作，先停在这里。"),
        Suggestions = action.Suggestions,
        Source = "runtime_guard",
    };

    public static bool ShouldReturnUserFacingReply(AgentAction action)
    {
        if (string.IsNullOrWhiteSpace(action.Reply)) return false;
        if (action.Type is not (AgentActionType.ChatReply or AgentActionType.FinalReply or AgentActionType.Clarify))
            return false;

        var source = action.Source.Trim().ToLowerInvariant();
        if (source.Contains("planner_error", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("parse_error", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("provider_raw_error", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static string BuildToolCallFingerprint(AgentToolCall call)
    {
        var args = call.Arguments.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => $"{p.Key}={p.Value}");
        return $"{call.Name.Trim()}::{string.Join("&", args)}";
    }

    public static void NormalizeStoryFoundationCandidateSelection(
        AgentAction action,
        AgentObservationContext? context,
        StoryBibleDocument bible,
        AgentSession session)
    {
        var call = action.ToolCall;
        if (call == null || !string.Equals(call.Name, "CommitStoryFoundation", StringComparison.OrdinalIgnoreCase))
            return;

        var intent = context?.TurnIntent ?? session.WorkingMemory.MissionPlan.InteractionState?.Intent;
        if (intent?.Type != TurnIntentType.CandidateSelection ||
            !string.Equals(intent.SelectionKind, "story_foundation_candidate", StringComparison.OrdinalIgnoreCase) ||
            intent.SelectedOptionIndex is not { } index)
            return;

        var runId = FirstNonEmpty(
            Arg(call, "runId"),
            intent.ReferencedRunId,
            context?.ActiveRunId,
            session.ActiveRunId);
        var run = string.IsNullOrWhiteSpace(runId)
            ? AgentRunSelector.SelectCurrentRun(bible)
            : bible.AgentRuns.FirstOrDefault(r => string.Equals(r.RunId, runId, StringComparison.OrdinalIgnoreCase));
        if (run == null)
            return;

        call.Arguments["runId"] = run.RunId;
        call.Arguments["selectedMacroCandidateIndex"] = index.ToString();
        if (index <= 0 || index > run.MacroCandidates.Count)
            return;

        var candidate = run.MacroCandidates[index - 1];
        if (!string.IsNullOrWhiteSpace(candidate.CandidateId))
            call.Arguments["selectedMacroCandidateId"] = candidate.CandidateId;
        call.Arguments["selectedMacroCandidateTitle"] = candidate.Title;
    }

    private static string BuildConfirmationMessage(string toolName) => toolName switch
    {
        "CommitStoryFoundation" => "会把故事地基写入 Story Bible，并继续推进后续规划。",
        "CommitVolumeArc" => "会把卷规划写入 Story Bible，并继续推进章节生产线。",
        "GenerateChapterWithChanges" => "会生成章节草稿和 CHANGES，随后进入硬门禁校验。",
        "RepairChapterDraft" => "会按门禁失败项修复草稿，并继续校验。",
        "CommitValidatedChapter" => "会把已通过门禁的章节提交进书城，并刷新事实快照和索引。",
        _ => "会改变小说工程状态，并由 Agent 继续推进。",
    };

    public static string BuildUserVisibleConfirmationMessage(AgentAction action, AgentObservationContext? context, string? message)
    {
        if (action.ToolCall == null)
            return FirstNonEmpty(message, action.Reply, "我会继续推进。");

        var baseMessage = BuildConfirmationMessage(action.ToolCall.Name);
        var intent = context?.TurnIntent;
        if (intent?.Type == TurnIntentType.CandidateSelection && intent.SelectedOptionIndex is { } index)
        {
            var kind = intent.SelectionKind switch
            {
                "story_foundation_candidate" => "故事地基候选",
                "chapter_candidate" => "章节候选",
                "feedback_option" => "反馈选项",
                _ => "候选",
            };

            var actionMessage = action.ToolCall.Name switch
            {
                "CommitStoryFoundation" => "这个操作会写入 Story Bible，并继续后续规划。",
                "SelectChapterCandidate" => "会选定章节方向，并进入后续生成链路。",
                _ => baseMessage,
            };

            return $"我理解你选择了第 {index} 个{kind}。{actionMessage}";
        }

        return FirstNonEmpty(message, action.Reply, baseMessage);
    }

    private static string MapMissionStage(string phase) => phase switch
    {
        "foundation_candidates" or "foundation_committed" or "awaiting_user_foundation" => "foundation",
        "volume_plan" or "volume_committed" => "volume_planning",
        "chapter_candidates" or "candidate_selected" or "chapter_generated" or "chapter_reviewed" => "chapter_work",
        "awaiting_confirmation" => "active",
        _ => phase,
    };

    private static string BuildStatusSummary(AgentSession session, StoryBibleDocument bible)
    {
        var parts = new List<string>();
        if (bible.Constitution != null)
            parts.Add($"Story Bible 已固化：{bible.Constitution.Genre}/{bible.Constitution.SubGenre}，核心钩子「{bible.Constitution.CoreHook}」");
        else
            parts.Add("Story Bible 尚未固化。");
        parts.Add($"卷: {bible.VolumeArcs.Count} | 设定: {bible.CanonLedger.Count} | 伏笔: {bible.ForeshadowLedger.Count} | 角色: {bible.CharacterLedger.Count}");
        if (session.WorkingMemory.MissionPlan.SchedulerState.Tasks.Count > 0)
        {
            var active = session.WorkingMemory.MissionPlan.SchedulerState.Tasks.FirstOrDefault(t =>
                string.Equals(t.TaskId, session.WorkingMemory.MissionPlan.SchedulerState.ActiveTaskId, StringComparison.OrdinalIgnoreCase));
            if (active != null) parts.Add($"当前任务: {active.ChapterId} → {active.NextAction}");
        }
        return string.Join("\n", parts);
    }

    private static string BuildSessionTitle(string userMessage)
    {
        var title = userMessage.Trim().Replace("\r", " ").Replace("\n", " ");
        if (string.IsNullOrWhiteSpace(title)) return "新会话";
        return title.Length <= 22 ? title : title[..22] + "...";
    }

    private static string TrimForReply(string value, int maxLength)
    {
        var trimmed = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "...";
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static string Arg(AgentToolCall call, string name, string fallback = "") =>
        call.Arguments.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback;

    private static TurnIntent GetCurrentTurnIntent(AgentSession session, string userMessage) =>
        session.WorkingMemory.MissionPlan.InteractionState?.Intent ??
        session.WorkingMemory.MissionPlan.TurnIntent ??
        new TurnIntent { RawMessage = userMessage };

    private static SessionContext ToSessionContext(AgentSession session) => new()
    {
        SessionId = session.SessionId,
        ActiveProjectId = session.ActiveProjectId,
        ChatHistory = session.ChatHistory,
        CurrentGoal = session.WorkingMemory.CurrentGoal,
        OpenQuestions = session.WorkingMemory.OpenQuestions,
        RecentObservations = session.WorkingMemory.RecentObservations,
        PendingToolCall = session.WorkingMemory.PendingToolCall,
        PendingConfirmation = session.WorkingMemory.PendingConfirmation,
    };

    private static bool IsProjectSwitchIntent(string message)
    {
        var lower = message.ToLowerInvariant();
        return lower.Contains("切换") || lower.Contains("换个") || lower.Contains("换一本");
    }
}
