using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.AgentTools;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Services.Workspace;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class AgentRuntime : IAgentForegroundTurnRunner, IAgentInterruptDecisionService
{
    private const int MaxRecentObservations = 16;

    // Thread-local workspace context for current request
    private static readonly AsyncLocal<WorkspaceEntry?> _currentWorkspaceEntry = new();
    private static readonly AsyncLocal<NovelAgentWorkspace?> _currentWorkspace = new();
    private static readonly AsyncLocal<NovelProjectCatalog?> _currentCatalog = new();

    private WorkspaceEntry? _workspaceEntry;
    private NovelAgentWorkspace? _workspaceInstance;
    private NovelProjectCatalog? _catalogInstance;

    private NovelAgentWorkspace _workspace => _workspaceInstance ?? throw new InvalidOperationException("Workspace not set for current request");
    private NovelProjectCatalog _catalog => _catalogInstance ?? throw new InvalidOperationException("Catalog not set for current request");

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
    private readonly IChatHistoryRepository _chatHistory;
    private readonly ChatHistoryCompressor _chatHistoryCompressor;
    private readonly AgentMissionTaskTreeService _taskTreeService;
    private readonly AgentTaskScheduler _taskScheduler;
    private readonly MissionBlackboardRecoveryService _blackboardRecovery;
    private readonly AgentToolGuardrails _guardrails;
    private readonly ConversationKernel _conversationKernel;
    private readonly AgentRecoveryEngine _recoveryEngine;
    private readonly PhaseContextBuilder _contextBuilder;
    private readonly IAgentRuntimeRunService _runtimeRuns;
    private readonly IAgentRuntimeEventService _runtimeEvents;
    private readonly IAgentInterruptService _interrupts;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentRuntime> _logger;
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
        IChatHistoryRepository chatHistory,
        ChatHistoryCompressor chatHistoryCompressor,
        AgentMissionTaskTreeService taskTreeService,
        AgentTaskScheduler taskScheduler,
        MissionBlackboardRecoveryService blackboardRecovery,
        AgentToolGuardrails guardrails,
        ConversationKernel conversationKernel,
        PhaseContextBuilder contextBuilder,
        IAgentRuntimeRunService runtimeRuns,
        IAgentRuntimeEventService runtimeEvents,
        IAgentInterruptService interrupts,
        IServiceScopeFactory scopeFactory,
        ILogger<AgentRuntime> logger)
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
        _chatHistory = chatHistory;
        _chatHistoryCompressor = chatHistoryCompressor;
        _taskTreeService = taskTreeService;
        _taskScheduler = taskScheduler;
        _blackboardRecovery = blackboardRecovery;
        _guardrails = guardrails;
        _conversationKernel = conversationKernel;
        _recoveryEngine = new AgentRecoveryEngine(toolRegistry, guardrails);
        _contextBuilder = contextBuilder;
        _runtimeRuns = runtimeRuns;
        _runtimeEvents = runtimeEvents;
        _interrupts = interrupts;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Main Agent Loop — Observe / Plan / Act / Reflect
    //  Inspired by GenericAgent's agent_runner_loop and Hermes Agent's conversation_loop
    // ═══════════════════════════════════════════════════════════════

    public async Task<AgentChatResponse> RunAsync(
        string sessionId,
        string userMessage,
        CancellationToken ct,
        string? runtimeRunId = null)
    {
        // ── Acquire user-specific workspace ──
        var userId = _currentUserService.GetUserId();
        var session = await _sessionManager.GetOrCreateSessionAsync(sessionId, ct);

        // For first turn, use temp projectId; will be replaced after project resolution
        var projectId = string.IsNullOrWhiteSpace(session.ActiveProjectId)
            ? "temp-" + userId
            : session.ActiveProjectId;

        var workspaceEntry = await _workspaceFactory.AcquireAsync(userId, projectId, ct).ConfigureAwait(false);
        SetWorkspaceContext(workspaceEntry);
        try
        {
            return await RunWithWorkspaceAsync(session, userMessage, ct, runtimeRunId).ConfigureAwait(false);
        }
        finally
        {
            ClearWorkspaceContext(releaseLease: true);
        }
    }

    public async Task<AgentForegroundTurnResult> TryHandleForegroundAsync(string sessionId, string userMessage, CancellationToken ct)
    {
        var userId = _currentUserService.GetUserId();
        var session = await _sessionManager.GetOrCreateSessionAsync(sessionId, ct).ConfigureAwait(false);
        var projectId = string.IsNullOrWhiteSpace(session.ActiveProjectId)
            ? "temp-" + userId
            : session.ActiveProjectId;

        var workspaceEntry = await _workspaceFactory.AcquireAsync(userId, projectId, ct).ConfigureAwait(false);
        SetWorkspaceContext(workspaceEntry);
        try
        {
            return await TryHandleForegroundWithWorkspaceAsync(session, userMessage, ct).ConfigureAwait(false);
        }
        finally
        {
            ClearWorkspaceContext(releaseLease: true);
        }
    }

    public Task<AgentForegroundTurnResult> TryHandleAsync(string sessionId, string userMessage, CancellationToken ct) =>
        TryHandleForegroundAsync(sessionId, userMessage, ct);

    public async Task<AgentInterruptDecision> DecideAsync(
        AgentSession session,
        Data.Entities.AgentRuntimeRun activeRun,
        string userMessage,
        CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();
        var projectId = string.IsNullOrWhiteSpace(session.ActiveProjectId)
            ? "temp-" + userId
            : session.ActiveProjectId;

        var workspaceEntry = await _workspaceFactory.AcquireAsync(userId, projectId, ct).ConfigureAwait(false);
        SetWorkspaceContext(workspaceEntry);
        try
        {
            var context = new AgentInterruptDecisionContext
            {
                UserMessage = userMessage,
                SessionId = session.SessionId,
                ProjectId = activeRun.ProjectId ?? session.ActiveProjectId,
                RuntimeRunId = activeRun.Id,
                RunStatus = activeRun.Status,
                ActiveTool = activeRun.ActiveTool,
                CurrentPhase = activeRun.CurrentPhase,
                LastMessage = activeRun.LastMessage,
                CurrentUserGoal = FirstNonEmpty(
                    session.WorkingMemory.MissionPlan.CurrentObjective,
                    session.WorkingMemory.MissionPlan.CurrentNovelGoal,
                    session.WorkingMemory.CurrentGoal,
                    activeRun.UserMessage),
                RecentInterrupts = session.WorkingMemory.RuntimeInterrupts.TakeLast(8).ToList()
            };

            return await _planner.PlanInterruptDecisionAsync(context, ct).ConfigureAwait(false);
        }
        finally
        {
            ClearWorkspaceContext(releaseLease: true);
        }
    }

    private void SetWorkspaceContext(WorkspaceEntry workspaceEntry)
    {
        var workspace = workspaceEntry.Workspace;
        var catalog = new NovelProjectCatalog(workspace, _scopeFactory);

        _logger.LogInformation(
            "Agent workspace context set: entryProject={EntryProjectId}, workspaceProject={WorkspaceProjectId}, user={UserId}",
            workspaceEntry.ProjectId,
            workspace.ProjectId,
            workspace.UserId);

        _currentWorkspaceEntry.Value = workspaceEntry;
        _currentWorkspace.Value = workspace;
        _currentCatalog.Value = catalog;
        _workspaceEntry = workspaceEntry;
        _workspaceInstance = workspace;
        _catalogInstance = catalog;
        workspace.SetRequestContext();
        _toolRegistry.SetWorkspaceContext(workspace, catalog);
        AgentObservationBuilder.SetWorkspace(workspace);
        ProjectScopedExecutor.SetCatalog(catalog);
        PhaseContextBuilder.SetWorkspace(workspace);
    }

    private void ClearWorkspaceContext(bool releaseLease)
    {
        var workspaceEntry = _workspaceEntry ?? _currentWorkspaceEntry.Value;
        var workspace = _workspaceInstance ?? _currentWorkspace.Value;

        workspace?.ClearRequestContext();
        _currentWorkspaceEntry.Value = null;
        _currentWorkspace.Value = null;
        _currentCatalog.Value = null;
        _workspaceEntry = null;
        _workspaceInstance = null;
        _catalogInstance = null;
        _toolRegistry.ClearWorkspaceContext();
        AgentObservationBuilder.ClearWorkspace();
        ProjectScopedExecutor.ClearCatalog();
        PhaseContextBuilder.ClearWorkspace();

        if (releaseLease)
            workspaceEntry?.ReleaseLease();
    }

    private async Task EnsureWorkspaceForSessionProjectAsync(AgentSession session, CancellationToken ct)
    {
        var userId = _currentUserService.GetUserId();
        var desiredProjectId = string.IsNullOrWhiteSpace(session.ActiveProjectId)
            ? "temp-" + userId
            : session.ActiveProjectId;

        var currentEntry = _workspaceEntry ?? _currentWorkspaceEntry.Value;
        if (currentEntry != null && string.Equals(currentEntry.ProjectId, desiredProjectId, StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Agent workspace context already matches session: desiredProject={DesiredProjectId}, entryProject={EntryProjectId}, workspaceProject={WorkspaceProjectId}, sessionProject={SessionProjectId}",
                desiredProjectId,
                currentEntry.ProjectId,
                currentEntry.Workspace.ProjectId,
                session.ActiveProjectId);
            return;
        }

        WorkspaceEntry nextEntry;
        if (!string.IsNullOrWhiteSpace(session.ActiveProjectId) &&
            !session.ActiveProjectId.StartsWith("temp-", StringComparison.OrdinalIgnoreCase))
        {
            nextEntry = await _workspaceFactory.AcquireAsync(userId, session.ActiveProjectId, ct).ConfigureAwait(false);
        }
        else
        {
            nextEntry = await _workspaceFactory.AcquireAsync(userId, desiredProjectId, ct).ConfigureAwait(false);
        }

        ClearWorkspaceContext(releaseLease: true);
        try
        {
            SetWorkspaceContext(nextEntry);
            _logger.LogInformation(
                "Agent workspace context switched: desiredProject={DesiredProjectId}, entryProject={EntryProjectId}, workspaceProject={WorkspaceProjectId}, sessionProject={SessionProjectId}",
                desiredProjectId,
                nextEntry.ProjectId,
                nextEntry.Workspace.ProjectId,
                session.ActiveProjectId);
        }
        catch
        {
            nextEntry.ReleaseLease();
            throw;
        }
    }

    private async Task<AgentChatResponse> RunWithWorkspaceAsync(
        AgentSession session,
        string userMessage,
        CancellationToken ct,
        string? runtimeRunId = null)
    {
        // ── Session setup ──
        session.UpdatedAt = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(session.Title) || session.Title == "新会话")
            session.Title = BuildSessionTitle(userMessage);
        var activeRuntimeRun = await _runtimeRuns
            .TryGetActiveAsync(session.UserId, session.SessionId, ct)
            .ConfigureAwait(false);
        if (activeRuntimeRun != null)
            session.RuntimeRunId = activeRuntimeRun.Id;
        if (!string.IsNullOrWhiteSpace(runtimeRunId))
            session.RuntimeRunId = runtimeRunId.Trim();

        // Add user message to chat history immediately so Agent can see it in context
        await AddChatTurnAsync(session, "user", userMessage, ct).ConfigureAwait(false);

        // ── Load project if session has one ──
        NovelProjectInfo? project = null;
        if (!string.IsNullOrWhiteSpace(session.ActiveProjectId) && !session.ActiveProjectId.StartsWith("temp-"))
        {
            project = await _catalog.FindAsync(session.ActiveProjectId, ct).ConfigureAwait(false);
        }

        var settings = await _settingsManager.LoadAsync(ct).ConfigureAwait(false);
        var maxSteps = settings.AgentLoopAutoProceed ? Math.Clamp(settings.AgentLoopMaxSteps, 5, 20) : 3;
        var trace = new List<AgentRuntimeStep>();
        var executedCalls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AgentObservationContext? lastContext = null;
        AgentReflection? lastReflection = null;
        AgentToolExecutionResult? lastResult = null;
        var userTurn = _conversationKernel.BuildEnvelope(session, userMessage);

        StoryBibleDocument bible = new();
        if (project != null)
        {
            bible = await _catalog.WithProjectAsync(project,
                () => _workspace.Orchestrator.GetStoryBibleAsync(ct), ct).ConfigureAwait(false) ?? new StoryBibleDocument();
            await _memoryService.HydrateAsync(session, project, bible, ct).ConfigureAwait(false);
        }
        else
        {
            await _memoryService.HydrateProjectlessAsync(session, ct).ConfigureAwait(false);
        }
        trace.Add(new AgentRuntimeStep
        {
            StepIndex = 0,
            Stage = "conversation_intent",
            StopReason = $"{userTurn.Intent.Label}:{userTurn.DialogueAct}",
        });

        // Pending confirmations resume as explicit follow-up work.
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
            userMessage = await DrainInterruptsAsync(session, userMessage, ct).ConfigureAwait(false);

            // Observe - only if we have a project
            if (project != null && bible != null)
            {
                _blackboardRecovery.Recover(session, project, bible);
                lastContext = await _catalog.WithProjectAsync(project,
                    () => _observationBuilder.BuildAsync(session, project, bible, userMessage, userTurn.Intent, ct), ct).ConfigureAwait(false);
                lastContext.AnchorPrompt = await BuildAnchorPromptAsync(session, project, bible, step, maxSteps, userMessage, ct).ConfigureAwait(false);
            }
            else
            {
                lastContext = BuildProjectlessReflectContext(session, userMessage, new AgentObservationContext
                {
                    TurnIntent = userTurn.Intent,
                    UserTurn = userTurn,
                });
                lastContext.AnchorPrompt = $"Step {step}/{maxSteps}. No active project. User can ask to create a new novel or switch to existing project.";
            }

            trace.Add(new AgentRuntimeStep
            {
                StepIndex = step,
                Stage = "observe",
                Action = new AgentAction
                {
                    Type = AgentActionType.FinalReply,
                    Intent = "observe",
                    Brief = lastContext.MissionPlan?.LastVerifiedState ?? "casual_chat",
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
                if (lastResult is { Success: true })
                {
                    return await FinishTextResponse(session, userMessage, new AgentAction
                    {
                        Type = AgentActionType.FinalReply,
                        Intent = "tool_result_summary",
                        Reply = BuildCompletedToolResultReply(lastResult, lastReflection),
                        Suggestions = lastResult.Suggestions ?? new[] { "查看当前状态", "继续下一步" },
                        Source = "runtime_tool_result_summary",
                    }, lastContext, trace, lastReflection, ct);
                }

                return await FinishTextResponse(session, userMessage, new AgentAction
                {
                    Type = AgentActionType.FinalReply,
                    Intent = "planner_no_action",
                    Reply = BuildStatusSummary(session, bible),
                    Suggestions = new[] { "查看当前状态", "补充要求" },
                    Source = "runtime_no_action",
                }, lastContext, trace, lastReflection, ct);
            }

            // Retrieve is a planner intent, not a runtime route to a privileged tool.
            if (action.Type == AgentActionType.Retrieve)
            {
                var query = action.RagQueries.FirstOrDefault() ?? userMessage;
                session.WorkingMemory.RecentObservations.Add(new AgentRuntimeObservation
                {
                    ObservationType = "runtime_observation",
                    ToolName = "runtime",
                    Success = false,
                    Phase = "retrieve_requires_tool_selection",
                    Message = $"Planner requested retrieval for '{query}', but runtime will not route it to a specific business tool. Choose a concrete registered tool by its semantics."
                });
                action = new AgentAction
                {
                    Type = AgentActionType.ChatReply,
                    Intent = action.Intent,
                    Reply = "我需要先根据当前工具语义选择合适的检索或状态能力，再继续处理。",
                    IsNoTool = true,
                    Source = "runtime_retrieve_requires_tool_selection"
                };
                continue;
            }

            // Agent loop auto-proceed mode: planner confirm_request is treated as an executable tool call.
            if (action.Type == AgentActionType.ConfirmRequest)
            {
                if (action.ToolCall == null)
                    return await FinishTextResponse(session, userMessage, BuildNoActionReplyAction(action), lastContext, trace, lastReflection, ct);
                action.Type = AgentActionType.ToolCall;
                action.RequiresConfirmation = false;
                action.Source = FirstNonEmpty(action.Source, "agent_auto_proceed_confirm_request");
            }

            // ── Tool execution pipeline ──
            if (action.ToolCall == null)
                return await FinishTextResponse(session, userMessage, BuildNoActionReplyAction(action), lastContext, trace, lastReflection, ct);

            var currentBible = bible ?? new StoryBibleDocument();
            NormalizeStoryFoundationCandidateSelection(action, lastContext, currentBible, session);

            // Policy check
            var repairRedirects = 0;
            var confirmed = IsPendingConfirmationAction(action, session);
            var policy = _toolPolicyEngine.BeforeCall(action.ToolCall, session, currentBible, lastContext, confirmed);
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
                    return await FinishTextResponse(session, userMessage, BuildNoActionReplyAction(action), lastContext, trace, lastReflection, ct);

                NormalizeStoryFoundationCandidateSelection(action, lastContext, currentBible, session);
                confirmed = IsPendingConfirmationAction(action, session);
                policy = _toolPolicyEngine.BeforeCall(action.ToolCall, session, currentBible, lastContext, confirmed);
                trace.Add(new AgentRuntimeStep { StepIndex = step, Stage = "policy", Action = action, StopReason = policy.Message });
                repairRedirects++;
            }

            if (!policy.IsRepairable && policy.ReplacementAction != null)
            {
                action = policy.ReplacementAction;
                lastAction = action;
                if (action.ToolCall == null)
                    return await FinishTextResponse(session, userMessage, BuildNoActionReplyAction(action), lastContext, trace, lastReflection, ct);
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

            if (policy.RequiresConfirmation)
            {
                RememberPendingConfirmation(session, action, policy.Message);
                return await FinishConfirmationResponse(session, userMessage, action, lastContext, trace, policy.Message, ct).ConfigureAwait(false);
            }

            action.RequiresConfirmation = false;
            action.Risk = policy.Risk;

            // Dedup check based on tool's deduplication policy
            var toolDefinition = _toolRegistry.Find(action.ToolCall.Name);
            var dedupPolicy = toolDefinition?.Semantic.DeduplicationPolicy ?? "strict";

            if (dedupPolicy != "none")
            {
                var fingerprint = dedupPolicy == "strict"
                    ? BuildToolCallFingerprint(action.ToolCall)  // name::arg1=val1&arg2=val2
                    : action.ToolCall.Name;  // per_turn: only check name

                if (!executedCalls.Add(fingerprint))
                {
                    var message = dedupPolicy == "strict"
                        ? "本轮已有同名同参数工具结果，请基于已有观察继续。"
                        : "本轮已调用过该工具，请尝试其他工具或反思当前状态。";
                    var govResult = BuildGovernanceResult(action.ToolCall.Name, "repeated_call", message, session, "runtime_observation");
                    return await FinishGovernanceResponseAsync(session, userMessage, action, lastContext, trace, step, action.ToolCall.Name, govResult, "runtime_observation", project, ct).ConfigureAwait(false);
                }
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
            var progress = TM.Web.NovelAgentWeb.Services.AgentTools.AgentToolProgressPresenter.DescribeRunning(
                action.ToolCall.Name,
                "running",
                session.Phase,
                session.ActiveRunId);
            AddProgressObservation(session, step, action.ToolCall.Name, progress);
            await EmitAsync(session, AgentSseEventType.AgentActing, progress.Title, ct, progress, session.ActiveRunId);
            confirmed = IsAgentAutoProceedAuthorizedAction(action, session);
            AgentToolExecutionResult result;
            if (CanExecuteWithoutProject(action.ToolCall.Name))
            {
                result = await ExecuteToolWithHeartbeatAsync(session, action.ToolCall,
                    () => _toolRegistry.ExecuteAsync(action.ToolCall, session, currentBible, confirmed, ct), ct).ConfigureAwait(false);
            }
            else
            {
                await EnsureWorkspaceForSessionProjectAsync(session, ct).ConfigureAwait(false);
                _logger.LogInformation(
                    "Executing project tool with workspace: tool={ToolName}, sessionProject={SessionProjectId}, runtimeWorkspace={RuntimeWorkspaceProjectId}",
                    action.ToolCall.Name,
                    session.ActiveProjectId,
                    _workspace.ProjectId);
                project = await _catalog.FindAsync(session.ActiveProjectId, ct).ConfigureAwait(false);
                if (project == null)
                    throw new InvalidOperationException($"Project {session.ActiveProjectId} not found");
                bible = await _catalog.WithProjectAsync(project,
                    () => _workspace.Orchestrator.GetStoryBibleAsync(ct), ct).ConfigureAwait(false) ?? new StoryBibleDocument();
                currentBible = bible;
                result = await WithSessionProjectAsync(session,
                    () => ExecuteToolWithHeartbeatAsync(session, action.ToolCall,
                        () => _toolRegistry.ExecuteAsync(action.ToolCall, session, currentBible, confirmed, ct), ct), ct).ConfigureAwait(false);
            }

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
                    currentBible,
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
                        Message = result.Success
                            ? $"工具 {action.ToolCall.Name} 初次失败后自动恢复成功"
                            : $"工具 {action.ToolCall.Name} 初次失败后已进入可继续修复状态：{result.Message}",
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

                    if (ShouldFinishFailureWithoutReflection(result))
                    {
                        var terminalReflection = BuildTerminalFailureReflection(result);
                        ApplyReflection(session, terminalReflection);
                        SyncMissionPlan(session, project, result, action);
                        trace.Add(new AgentRuntimeStep
                        {
                            StepIndex = step,
                            Stage = "terminal_failure",
                            Action = action,
                            Observation = failureObservation,
                            Reflection = terminalReflection,
                            StopReason = result.Message
                        });
                        return await FinishReflectionResponse(session, userMessage, action, lastContext, trace, terminalReflection, result, ct);
                    }

                    // Break the loop as before
                    await EmitAsync(session, AgentSseEventType.AgentReflecting, "正在根据失败观察重新判断...", ct).ConfigureAwait(false);
                    var failReflectContext = project != null && !string.IsNullOrWhiteSpace(session.ActiveProjectId)
                        ? await WithSessionProjectAsync(session,
                            async () =>
                            {
                                var refreshedBible = await _workspace.Orchestrator.GetStoryBibleAsync(ct).ConfigureAwait(false);
                                return await _observationBuilder.BuildAsync(session, project, refreshedBible, userMessage, userTurn.Intent, ct).ConfigureAwait(false);
                            }, ct).ConfigureAwait(false)
                        : BuildProjectlessReflectContext(session, userMessage, lastContext);
                    var failReflection = await _reflectionEngine.ReflectAsync(failReflectContext, failureObservation, ct).ConfigureAwait(false);
                    ApplyReflection(session, failReflection);
                    await SyncMissionPlanAsync(session, project, result, action, failReflection, ct).ConfigureAwait(false);
                    trace.Add(new AgentRuntimeStep { StepIndex = step, Stage = "reflect", Action = action, Observation = failureObservation, Reflection = failReflection });
                    return await FinishReflectionResponse(session, userMessage, action, failReflectContext, trace, failReflection, result, ct);
                }
            }

            lastResult = result;
            if (result.Success)
                _guardrails.RecordSuccess(action.ToolCall.Name);
            if (!string.IsNullOrWhiteSpace(result.Phase)) session.Phase = result.Phase;

            // Refresh project if tool execution set ActiveProjectId
            if (!string.IsNullOrWhiteSpace(session.ActiveProjectId) && !session.ActiveProjectId.StartsWith("temp-"))
            {
                await EnsureWorkspaceForSessionProjectAsync(session, ct).ConfigureAwait(false);
                project = await _catalog.FindAsync(session.ActiveProjectId, ct).ConfigureAwait(false);
                if (project != null)
                {
                    bible = await _catalog.WithProjectAsync(project,
                        () => _workspace.Orchestrator.GetStoryBibleAsync(ct), ct).ConfigureAwait(false) ?? new StoryBibleDocument();
                }
            }

            if (project != null)
                await SyncMissionPlanAsync(session, project, result, action, null, ct).ConfigureAwait(false);

            await PublishToolResultAsync(session, action.ToolCall.Name, result, ct).ConfigureAwait(false);

            var observation = AddRuntimeObservation(session, step, action.ToolCall.Name, result);
            trace.Add(new AgentRuntimeStep { StepIndex = step, Stage = "act", Action = action, Observation = observation });

            // Reflect
            await EmitAsync(session, AgentSseEventType.AgentReflecting, "正在反思...", ct, observation);
            var reflectContext = project != null && !string.IsNullOrWhiteSpace(session.ActiveProjectId)
                ? await WithSessionProjectAsync(session,
                    async () =>
                    {
                        var refreshedBible = await _workspace.Orchestrator.GetStoryBibleAsync(ct).ConfigureAwait(false);
                        return await _observationBuilder.BuildAsync(session, project, refreshedBible, userMessage, userTurn.Intent, ct).ConfigureAwait(false);
                    }, ct).ConfigureAwait(false)
                : BuildProjectlessReflectContext(session, userMessage, lastContext);
            var reflection = await _reflectionEngine.ReflectAsync(reflectContext, observation, ct).ConfigureAwait(false);
            lastReflection = reflection;
            ApplyReflection(session, reflection);
            await SyncMissionPlanAsync(session, project, result, action, reflection, ct).ConfigureAwait(false);
            trace.Add(new AgentRuntimeStep { StepIndex = step, Stage = "reflect", Action = action, Observation = observation, Reflection = reflection });
            await EmitAsync(session, AgentSseEventType.MissionUpdated, "任务记忆已更新。", ct, session.WorkingMemory.MissionPlan);

            // Loop termination checks
            if (ShouldStopAfterToolResult(result, reflection, session.WorkingMemory.MissionPlan))
                return await FinishReflectionResponse(session, userMessage, action, lastContext, trace, reflection, result, ct);

            // Safety escalation at configured turn limit.
            if (ShouldEscalateSafety(step, maxSteps))
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

            // Refresh project/bible for next iteration
            if (!string.IsNullOrWhiteSpace(session.ActiveProjectId) && !session.ActiveProjectId.StartsWith("temp-"))
            {
                await EnsureWorkspaceForSessionProjectAsync(session, ct).ConfigureAwait(false);
                project = await _catalog.FindAsync(session.ActiveProjectId, ct).ConfigureAwait(false);
                if (project != null)
                {
                    bible = await _catalog.WithProjectAsync(project,
                        () => _workspace.Orchestrator.GetStoryBibleAsync(ct), ct).ConfigureAwait(false) ?? new StoryBibleDocument();
                }
            }
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

    private async Task<AgentForegroundTurnResult> TryHandleForegroundWithWorkspaceAsync(
        AgentSession session,
        string userMessage,
        CancellationToken ct)
    {
        session.UpdatedAt = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(session.Title) || session.Title == "新会话")
            session.Title = BuildSessionTitle(userMessage);

        NovelProjectInfo? project = null;
        if (!string.IsNullOrWhiteSpace(session.ActiveProjectId) && !session.ActiveProjectId.StartsWith("temp-"))
            project = await _catalog.FindAsync(session.ActiveProjectId, ct).ConfigureAwait(false);

        var trace = new List<AgentRuntimeStep>();
        var userTurn = _conversationKernel.BuildEnvelope(session, userMessage);
        StoryBibleDocument bible = new();
        if (project != null)
        {
            bible = await _catalog.WithProjectAsync(project,
                () => _workspace.Orchestrator.GetStoryBibleAsync(ct), ct).ConfigureAwait(false) ?? new StoryBibleDocument();
            await _memoryService.HydrateAsync(session, project, bible, ct).ConfigureAwait(false);
        }
        else
        {
            await _memoryService.HydrateProjectlessAsync(session, ct).ConfigureAwait(false);
        }

        trace.Add(new AgentRuntimeStep
        {
            StepIndex = 0,
            Stage = "conversation_intent",
            StopReason = $"{userTurn.Intent.Label}:{userTurn.DialogueAct}",
        });

        var pendingAction = ResolvePendingConfirmationAction(session, userMessage);
        if (pendingAction != null)
        {
            if (pendingAction.Type == AgentActionType.FinalReply)
            {
                await AddChatTurnAsync(session, "user", userMessage, ct).ConfigureAwait(false);
                var cancelResponse = await FinishTextResponse(session, userMessage, pendingAction, null, trace, null, ct).ConfigureAwait(false);
                return AgentForegroundTurnResult.Reply(cancelResponse);
            }

            var settings = await _settingsManager.LoadAsync(ct).ConfigureAwait(false);
            var confirmResponse = await ExecuteSingleActionAsync(session, userMessage, pendingAction, project, settings, trace, ct)
                .ConfigureAwait(false);
            return AgentForegroundTurnResult.Reply(confirmResponse);
        }

        AgentObservationContext context;
        if (project != null && bible != null)
        {
            _blackboardRecovery.Recover(session, project, bible);
            context = await _catalog.WithProjectAsync(project,
                () => _observationBuilder.BuildAsync(session, project, bible, userMessage, userTurn.Intent, ct), ct).ConfigureAwait(false);
            context.AnchorPrompt = await BuildAnchorPromptAsync(session, project, bible, 1, 1, userMessage, ct).ConfigureAwait(false);
        }
        else
        {
            context = BuildProjectlessReflectContext(session, userMessage, new AgentObservationContext
            {
                TurnIntent = userTurn.Intent,
                UserTurn = userTurn,
            });
            context.AnchorPrompt = "Foreground turn. No active project. Decide whether to answer now or defer action to the background workflow.";
        }

        trace.Add(new AgentRuntimeStep
        {
            StepIndex = 1,
            Stage = "observe",
            Action = new AgentAction
            {
                Type = AgentActionType.FinalReply,
                Intent = "observe",
                Brief = context.MissionPlan?.LastVerifiedState ?? "foreground_turn",
                Source = "foreground_observation",
            },
        });

        if (project != null && IsForegroundStatusIntent(userTurn.Intent.Label))
        {
            var statusAction = BuildForegroundProductionStatusAction(userTurn.Intent, session, project);
            session.WorkingMemory.LastDecision = statusAction.ToDecision();
            RememberUserMessage(session, userMessage, statusAction);
            trace.Add(new AgentRuntimeStep { StepIndex = 1, Stage = "plan", Action = statusAction });
            return await ExecuteForegroundReadableToolAsync(session, userMessage, statusAction, context, project, bible, trace, ct)
                .ConfigureAwait(false);
        }

        var action = await _planner.PlanActionAsync(context, ct).ConfigureAwait(false);
        session.WorkingMemory.LastDecision = action.ToDecision();
        RememberUserMessage(session, userMessage, action);
        trace.Add(new AgentRuntimeStep { StepIndex = 1, Stage = "plan", Action = action });

        if (!ShouldReturnUserFacingReply(action))
        {
            if (IsForegroundReadableToolAction(action))
            {
                if (ShouldDeferForegroundReadableToolToBackground(action, context))
                    return AgentForegroundTurnResult.Background();

                return await ExecuteForegroundReadableToolAsync(session, userMessage, action, context, project, bible, trace, ct)
                    .ConfigureAwait(false);
            }

            if (ShouldStartBackground(action))
                return AgentForegroundTurnResult.Background();

            await AddChatTurnAsync(session, "user", userMessage, ct).ConfigureAwait(false);
            var replyAction = BuildForegroundNoBackgroundReply(action);
            var response = await FinishTextResponse(session, userMessage, replyAction, context, trace, null, ct).ConfigureAwait(false);
            return AgentForegroundTurnResult.NoBackground(response);
        }

        await AddChatTurnAsync(session, "user", userMessage, ct).ConfigureAwait(false);
        var reply = await FinishTextResponse(session, userMessage, action, context, trace, null, ct).ConfigureAwait(false);
        return AgentForegroundTurnResult.Reply(reply);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Anchor Prompt — Inject compressed memory each turn
    //  Inspired by GenericAgent's _get_anchor_prompt
    // ═══════════════════════════════════════════════════════════════

    private async Task<string> BuildAnchorPromptAsync(AgentSession session, NovelProjectInfo? project, StoryBibleDocument? bible, int step, int maxSteps, string userMessage, CancellationToken ct)
    {
        if (project == null || bible == null)
            return $"Step {step}/{maxSteps}. 无项目。用户可以要求创建新小说或切换到现有项目。";

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

        var history = await BuildHistoryBlockAsync(session, project.Id, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(history))
            parts.Add($"<history>\n{history}\n</history>");

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

    public static string FormatChatPromptWindow(ChatPromptWindowDto promptWindow)
    {
        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(promptWindow.MetaSummary))
        {
            lines.Add($"Meta: {TruncateLine(promptWindow.MetaSummary, 300)}");
        }

        foreach (var summary in promptWindow.Summaries)
        {
            lines.Add($"Summary {summary.StartTurn}-{summary.EndTurn}: {TruncateLine(summary.Content, 240)}");
            if (summary.KeyDecisions.Count > 0)
                lines.Add($"Decisions: {string.Join("; ", summary.KeyDecisions.Select(d => TruncateLine(d, 120)))}");
        }

        foreach (var message in promptWindow.RecentMessages)
        {
            var role = string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase) ? "U" : "A";
            lines.Add($"{role}: {TruncateLine(message.Content, 120)}");
        }

        return string.Join("\n", lines);
    }

    public static string FormatSessionHistorySnapshot(AgentSession session)
    {
        var recentHistory = session.ChatHistory.TakeLast(10).Select(t =>
        {
            var role = string.Equals(t.Role, "user", StringComparison.OrdinalIgnoreCase) ? "U" : "A";
            return $"{role}: {TruncateLine(t.Content, 120)}";
        });

        return string.Join("\n", recentHistory);
    }

    private async Task<string> BuildHistoryBlockAsync(AgentSession session, string projectId, CancellationToken ct)
    {
        try
        {
            var promptWindow = await _chatHistory
                .GetPromptWindowAsync(session.UserId, projectId, session.SessionId, ct)
                .ConfigureAwait(false);
            return FormatChatPromptWindow(promptWindow);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load chat prompt window for session {SessionId}; falling back to session snapshot", session.SessionId);
            return FormatSessionHistorySnapshot(session);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Pending confirmation command handling
    // ═══════════════════════════════════════════════════════════════

    private static AgentAction? ResolvePendingConfirmationAction(AgentSession session, string userMessage)
    {
        var pending = session.WorkingMemory.PendingConfirmation;
        if (pending?.ToolCall == null) return null;

        var msg = userMessage.Trim();

        if (IsCancelCommand(msg))
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

        if (IsConfirmCommand(msg, pending))
        {
            return new AgentAction
            {
                Type = AgentActionType.ToolCall,
                Intent = "agent_loop_pending_confirmation",
                ToolCall = pending.ToolCall,
                Risk = pending.Risk,
                Brief = "Agent 继续执行原待确认动作。",
                Source = "pending_confirmation",
            };
        }

        return null;
    }

    private static bool IsConfirmCommand(string msg, AgentPendingConfirmation pending)
    {
        var normalized = NormalizePendingCommand(msg);
        if (normalized.Length == 0)
            return false;

        if (!string.IsNullOrWhiteSpace(pending.ConfirmationId) &&
            string.Equals(normalized, pending.ConfirmationId.Trim().ToLowerInvariant(), StringComparison.Ordinal))
            return true;

        return normalized is "确认" or "确定" or "ok" or "yes" or "y";
    }

    private static bool IsCancelCommand(string msg)
    {
        var normalized = NormalizePendingCommand(msg);
        return normalized is "取消" or "no" or "n";
    }

    private static string NormalizePendingCommand(string value) =>
        value.Trim().ToLowerInvariant();

    private static bool IsPendingConfirmationAction(AgentAction action, AgentSession session) =>
        string.Equals(action.Source, "pending_confirmation", StringComparison.OrdinalIgnoreCase) &&
        session.WorkingMemory.PendingConfirmation?.ToolCall != null;

    private static void RememberPendingConfirmation(AgentSession session, AgentAction action, string? impactSummary)
    {
        if (action.ToolCall == null)
            return;

        session.WorkingMemory.PendingToolCall = action.ToolCall;
        session.WorkingMemory.PendingConfirmation = new AgentPendingConfirmation
        {
            ToolCall = action.ToolCall,
            Risk = FirstNonEmpty(action.Risk, "High"),
            ImpactSummary = FirstNonEmpty(impactSummary, action.Brief, action.Reply, BuildConfirmationMessage(action.ToolCall.Name)),
            RequiresUserInput = "回复“确认”继续执行，或回复“取消”放弃该操作。",
            ProjectId = session.ActiveProjectId,
            RunId = FirstNonEmpty(
                action.ToolCall.Arguments.TryGetValue("runId", out var runId) ? runId : null,
                action.ToolCall.Arguments.TryGetValue("auditRunId", out var auditRunId) ? auditRunId : null,
                session.ActiveRunId),
            CandidateId = action.ToolCall.Arguments.TryGetValue("selectedMacroCandidateId", out var candidateId) ? candidateId : string.Empty,
            CandidateTitle = action.ToolCall.Arguments.TryGetValue("selectedCandidateTitle", out var selectedCandidateTitle)
                ? selectedCandidateTitle
                : string.Empty,
            CandidateIndex = TryParseInt(
                FirstNonEmpty(
                    action.ToolCall.Arguments.TryGetValue("selectedMacroCandidateIndex", out var macroIndex) ? macroIndex : null,
                    action.ToolCall.Arguments.TryGetValue("selectedCandidateIndex", out var selectedIndex) ? selectedIndex : null,
                    action.ToolCall.Arguments.TryGetValue("candidateIndex", out var candidateIndex) ? candidateIndex : null))
        };
    }

    private static int TryParseInt(string value) =>
        int.TryParse(value, out var parsed) ? parsed : 0;

    private static List<AgentToolDefinition> BuildToolSearchOnlyContext() => new()
    {
        new AgentToolDefinition
        {
            Name = "tool_search",
            Description = "全局工具目录与语义检索入口。query/intent/context 用于检索工具语义；phase 只是排序提示，不是阶段白名单。",
            Risk = "Low",
            RequiresConfirmation = false,
            Arguments = new List<string> { "query", "intent", "context", "phase", "includeAll", "limit" },
            Semantic = new AgentToolSemanticSpec
            {
                DisplayName = "工具语义检索",
                DomainSurface = "Agent Runtime",
                OutputKind = "capability_catalog",
                SideEffectLevel = "runtime_metadata_write",
                ImpactScope = "current_session/tool_catalog",
                FailureContract = "returns failed_stage and cache_status; no business state is changed",
                RequiresProject = false,
                SupportsNoProjectSession = true,
                AverageDuration = "short:0-10s",
                ProgressEventContract = new List<string> { "read_only_snapshot:读取工具语义目录、缓存并返回模型可用能力地图" },
                NextPossibleTools = new List<string> { "QueryWorkspaceState", "QueryProjectStatus", "SearchCreativeKnowledge", "ResolveNovelProject", "ProduceChapter" },
                ReadsFrom = new List<string> { "agent_tool_registry", "tool_search_cache" },
                WritesTo = new List<string> { "agent_tool_search_snapshots", "tool_search_cache" },
                UserVisibleWhere = "Agent 对话中的工具发现结果",
                ResultSemantics = "返回全局工具目录的语义检索结果，帮助模型自主选择下一步；不是阶段白名单，也不是业务工具前置门禁。"
            }
        }
    };

    private static bool IsAgentAutoProceedAuthorizedAction(AgentAction action, AgentSession session) =>
        IsPendingConfirmationAction(action, session) ||
        action.ToolCall?.Name is "CommitStoryFoundation" or "CommitVolumeArc" or
            "ProduceChapter";

    private bool CanExecuteWithoutProject(string toolName)
    {
        var definition = _toolRegistry.Find(toolName);
        if (definition == null)
            return false;

        var impactScope = definition.Semantic.ImpactScope ?? string.Empty;
        return !impactScope.Contains("current_project", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLongRunningWritingTool(string toolName) =>
        toolName is "ProduceChapter";

    private static bool IsForegroundReadableToolAction(AgentAction action)
    {
        if (action.Type != AgentActionType.ToolCall || action.ToolCall == null)
            return false;

        return action.ToolCall.Name is "QueryWorkspaceState" or "QueryProjectStatus" or "QueryProjectContent" or "QueryNovelProductionState" or "SearchCreativeKnowledge";
    }

    private static bool ShouldDeferForegroundReadableToolToBackground(AgentAction action, AgentObservationContext context)
    {
        if (action.ToolCall == null)
            return false;

        var intent = action.Intent?.Trim() ?? string.Empty;
        if (IsForegroundStatusIntent(intent))
            return false;
        if (IsForegroundReadIntent(intent))
            return false;

        if (IsCreativeContinuationIntent(intent))
            return true;

        var turnLabel = FirstNonEmpty(context.TurnIntent?.Label, context.UserTurn?.Intent?.Label);
        if (IsForegroundStatusIntent(turnLabel))
            return false;
        if (IsForegroundReadIntent(turnLabel))
            return false;

        return IsCreativeContinuationIntent(turnLabel) ||
               HasMissionStructuredNextStep(context.MissionPlan);
    }

    private static bool IsForegroundStatusIntent(string intent)
    {
        if (string.IsNullOrWhiteSpace(intent))
            return false;

        var normalized = intent.Trim().ToLowerInvariant();
        return normalized is "status_query" or "query_status" or "inspect_status" or "read_status";
    }

    private static AgentAction BuildForegroundProductionStatusAction(
        TurnIntent intent,
        AgentSession session,
        NovelProjectInfo project)
    {
        var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["projectId"] = project.Id,
            ["includeEvents"] = "true"
        };
        if (!string.IsNullOrWhiteSpace(intent.ReferencedChapterId))
            arguments["chapterId"] = intent.ReferencedChapterId;

        return new AgentAction
        {
            Type = AgentActionType.ToolCall,
            Intent = "status_query",
            Brief = "读取当前小说生产运行状态、最近 runtime 事件和章节生产证据。",
            Source = "foreground_status_truth_read",
            ToolCall = new AgentToolCall
            {
                Name = "QueryNovelProductionState",
                Arguments = arguments
            },
            RequiresConfirmation = false,
            Risk = "Low"
        };
    }

    private static bool IsForegroundReadIntent(string intent)
    {
        if (string.IsNullOrWhiteSpace(intent))
            return false;

        var normalized = intent.Trim().ToLowerInvariant();
        return normalized is
            "query_project_content" or
            "read_project_content" or
            "query_content" or
            "read_content" or
            "inspect_content" or
            "query_workspace_state" or
            "inspect_workspace" or
            "search_knowledge" or
            "query_knowledge";
    }

    private static bool IsCreativeContinuationIntent(string intent)
    {
        if (string.IsNullOrWhiteSpace(intent))
            return false;

        var normalized = intent.Trim().ToLowerInvariant();
        return normalized is
            "creative_production" or
            "continue_mission" or
            "start_new_novel_project" or
            "create_foundation" or
            "plan_chapter" or
            "write_chapter" or
            "produce_chapter" or
            "book_production";
    }

    private static AgentObservationContext BuildProjectlessReflectContext(
        AgentSession session,
        string userMessage,
        AgentObservationContext? currentContext)
    {
        var turnIntent = currentContext?.TurnIntent ?? GetCurrentTurnIntent(session, userMessage);
        var userTurn = currentContext?.UserTurn ?? session.WorkingMemory.MissionPlan.InteractionState ?? new UserTurnEnvelope
        {
            Intent = turnIntent,
            CreativeBrief = turnIntent.CreativeBrief,
        };

        var recentMessages = currentContext?.RecentMessages?.Count > 0
            ? currentContext.RecentMessages.ToList()
            : session.ChatHistory
                .TakeLast(8)
                .Select(t => $"{t.Role}: {t.Content}")
                .ToList();

        var recentObservations = currentContext?.RecentObservations?.Count > 0
            ? currentContext.RecentObservations.ToList()
            : session.WorkingMemory.RecentObservations.TakeLast(8).ToList();

        var runtimeInterrupts = session.WorkingMemory.RuntimeInterrupts.Count > 0
            ? session.WorkingMemory.RuntimeInterrupts.TakeLast(8).ToList()
            : currentContext?.RuntimeInterrupts?.ToList() ?? new List<AgentRuntimeInterruptObservation>();

        var availableTools = session.DiscoveredTools.Count > 0
            ? session.DiscoveredTools.Select(ToAgentToolDefinition).ToList()
            : currentContext?.AvailableTools?.Count > 0
            ? currentContext.AvailableTools.ToList()
            : BuildToolSearchOnlyContext();

        return new AgentObservationContext
        {
            UserMessage = userMessage,
            TurnIntent = turnIntent,
            UserTurn = userTurn,
            Phase = session.Phase,
            ActiveRunId = session.ActiveRunId ?? string.Empty,
            RecentMessages = recentMessages,
            RecentObservations = recentObservations,
            RuntimeInterrupts = runtimeInterrupts,
            Rag = currentContext?.Rag ?? new AgentRagContext(),
            MissionPlan = session.WorkingMemory.MissionPlan ?? currentContext?.MissionPlan ?? new AgentMissionPlan(),
            MissionState = session.WorkingMemory.Mission ?? currentContext?.MissionState ?? new AgentMissionState(),
            SessionMemory = session.WorkingMemory.SessionMemory ?? currentContext?.SessionMemory ?? new AgentSessionMemory(),
            ProjectMemory = session.WorkingMemory.ProjectMemory ?? currentContext?.ProjectMemory ?? new AgentProjectMemory(),
            AuthorMemory = session.WorkingMemory.AuthorMemory ?? currentContext?.AuthorMemory ?? new AgentAuthorMemory(),
            ExecutionMemory = session.WorkingMemory.ExecutionMemory ?? currentContext?.ExecutionMemory ?? new AgentExecutionMemory(),
            PendingConfirmation = session.WorkingMemory.PendingConfirmation,
            AvailableTools = availableTools,
            ProductSpace = currentContext?.ProductSpace ?? AgentProductSpaceCatalog.Create(),
            WorkspaceState = currentContext?.WorkspaceState ?? AgentWorkspaceState.Hint(session),
            AnchorPrompt = FirstNonEmpty(
                currentContext?.AnchorPrompt,
                "No active project. User can chat freely, ask for capabilities, or ask the Agent to create or bind a novel project."),
        };
    }

    private static AgentToolDefinition ToAgentToolDefinition(ToolSchema schema) => new()
    {
        Name = schema.Name,
        Description = schema.Description,
        Risk = schema.Risk,
        RequiresConfirmation = schema.RequiresConfirmation,
        Arguments = schema.Parameters.Keys.ToList(),
        SideEffects = schema.SideEffects,
        Semantic = schema.Semantic,
    };

    // ═══════════════════════════════════════════════════════════════
    //  Helper methods
    // ═══════════════════════════════════════════════════════════════

    private async Task<AgentChatResponse> ExecuteSingleActionAsync(
        AgentSession session, string userMessage, AgentAction action,
        NovelProjectInfo? project, UserSettings settings, List<AgentRuntimeStep> trace, CancellationToken ct)
    {
        var confirmed = IsAgentAutoProceedAuthorizedAction(action, session);
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

        var toolProgress = TM.Web.NovelAgentWeb.Services.AgentTools.AgentToolProgressPresenter.DescribeRunning(
            action.ToolCall!.Name,
            "running",
            session.Phase,
            session.ActiveRunId);
        AddProgressObservation(session, 1, action.ToolCall!.Name, toolProgress);
        await EmitAsync(session, AgentSseEventType.AgentActing, toolProgress.Title, ct, toolProgress, session.ActiveRunId);

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

                return await ExecuteToolWithHeartbeatAsync(session, action.ToolCall!,
                    () => _toolRegistry.ExecuteAsync(action.ToolCall!, session, scopedBible, confirmed, ct), ct).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        result.RequiresConfirmation = false;
        if (result.Success)
        {
            session.WorkingMemory.PendingToolCall = null;
            session.WorkingMemory.PendingConfirmation = null;
        }
        project = scopedProject;
        await SyncMissionPlanAsync(session, project, result, action, null, ct).ConfigureAwait(false);
        await PublishToolResultAsync(session, action.ToolCall!.Name, result, ct).ConfigureAwait(false);
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

    private async Task<AgentForegroundTurnResult> ExecuteForegroundReadableToolAsync(
        AgentSession session,
        string userMessage,
        AgentAction action,
        AgentObservationContext context,
        NovelProjectInfo? project,
        StoryBibleDocument? bible,
        List<AgentRuntimeStep> trace,
        CancellationToken ct)
    {
        await AddChatTurnAsync(session, "user", userMessage, ct).ConfigureAwait(false);

        if (action.ToolCall == null)
        {
            var replyAction = BuildForegroundNoBackgroundReply(action);
            var replyResponse = await FinishTextResponse(session, userMessage, replyAction, context, trace, null, ct).ConfigureAwait(false);
            return AgentForegroundTurnResult.NoBackground(replyResponse);
        }
        var toolBible = bible ?? new StoryBibleDocument();

        if (string.Equals(action.ToolCall.Name, "QueryProjectStatus", StringComparison.Ordinal) &&
            (project == null || bible == null))
        {
            var noProject = new AgentAction
            {
                Type = AgentActionType.ChatReply,
                Intent = action.Intent,
                Reply = "当前会话还没有绑定小说项目，所以没有项目工作流状态可读。你可以让我查看整个工作台状态，或者先选择/创建一本小说。",
                Suggestions = new[] { "查看工作台状态", "选择已有项目", "创建新小说" },
                Source = "foreground_readable_no_project",
                IsNoTool = true,
            };
            var response = await FinishTextResponse(session, userMessage, noProject, context, trace, null, ct).ConfigureAwait(false);
            return AgentForegroundTurnResult.NoBackground(response);
        }

        var result = CanExecuteWithoutProject(action.ToolCall.Name)
            ? await ExecuteToolWithHeartbeatAsync(session, action.ToolCall,
                () => _toolRegistry.ExecuteAsync(action.ToolCall, session, toolBible, confirmed: false, ct), ct).ConfigureAwait(false)
            : await WithSessionProjectAsync(session,
                () => ExecuteToolWithHeartbeatAsync(session, action.ToolCall,
                    () => _toolRegistry.ExecuteAsync(action.ToolCall, session, toolBible, confirmed: false, ct), ct), ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(result.Phase))
            session.Phase = result.Phase;

        if (project != null)
            await SyncMissionPlanAsync(session, project, result, action, null, ct).ConfigureAwait(false);

        await PublishToolResultAsync(session, action.ToolCall.Name, result, ct).ConfigureAwait(false);
        var observation = AddRuntimeObservation(session, 1, action.ToolCall.Name, result);
        trace.Add(new AgentRuntimeStep { StepIndex = 1, Stage = "act", Action = action, Observation = observation });

        var reflectContext = project != null && !string.IsNullOrWhiteSpace(session.ActiveProjectId)
            ? await WithSessionProjectAsync(session,
                async () =>
                {
                    var refreshedBible = await _workspace.Orchestrator.GetStoryBibleAsync(ct).ConfigureAwait(false);
                    return await _observationBuilder.BuildAsync(session, project, refreshedBible, userMessage, GetCurrentTurnIntent(session, userMessage), ct).ConfigureAwait(false);
                }, ct).ConfigureAwait(false)
            : BuildProjectlessReflectContext(session, userMessage, context);

        var reflection = await _reflectionEngine.ReflectAsync(reflectContext, observation, ct).ConfigureAwait(false);
        ApplyReflection(session, reflection);
        if (project != null)
            await SyncMissionPlanAsync(session, project, result, action, reflection, ct).ConfigureAwait(false);

        var finalResponse = await FinishReflectionResponse(session, userMessage, action, reflectContext, trace, reflection, result, ct).ConfigureAwait(false);
        return AgentForegroundTurnResult.Reply(finalResponse);
    }

    private async Task<string> DrainInterruptsAsync(AgentSession session, string currentUserMessage, CancellationToken ct)
    {
        var userId = _currentUserService.GetUserId();
        var activeRun = await _runtimeRuns.TryGetActiveAsync(userId, session.SessionId, ct).ConfigureAwait(false);
        if (activeRun == null)
            return currentUserMessage;

        var pending = await _interrupts.GetPendingAsync(activeRun.Id, ct).ConfigureAwait(false);
        if (pending.Count == 0)
            return currentUserMessage;

        var interruptLines = new List<string>();
        var consumedInterrupts = new List<AgentRuntimeInterruptObservation>();
        foreach (var interrupt in pending)
        {
            if (string.IsNullOrWhiteSpace(interrupt.Message))
            {
                await _interrupts.MarkRejectedAsync(interrupt.Id, "empty_message", ct).ConfigureAwait(false);
                continue;
            }

            var content = interrupt.Message.Trim();
            interruptLines.Add(FormatRuntimeInterruptForModel(interrupt.Kind, content, interrupt.Priority));
            await AddChatTurnAsync(session, "user", content, ct).ConfigureAwait(false);
            var consumedAt = DateTime.UtcNow;
            await _interrupts.MarkConsumedAsync(interrupt.Id, new
            {
                consumedBy = "agent_runtime_safe_point",
                activeRunId = activeRun.Id
            }, ct).ConfigureAwait(false);
            consumedInterrupts.Add(new AgentRuntimeInterruptObservation
            {
                InterruptId = interrupt.Id,
                RuntimeRunId = interrupt.RuntimeRunId,
                Kind = interrupt.Kind,
                Message = content,
                Priority = interrupt.Priority,
                ReceivedAt = interrupt.CreatedAt,
                ConsumedAt = consumedAt
            });
        }

        if (interruptLines.Count == 0)
            return currentUserMessage;

        session.WorkingMemory.RuntimeInterrupts.AddRange(consumedInterrupts);
        if (session.WorkingMemory.RuntimeInterrupts.Count > 16)
        {
            session.WorkingMemory.RuntimeInterrupts = session.WorkingMemory.RuntimeInterrupts
                .TakeLast(16)
                .ToList();
        }

        var merged = string.Join("\n", interruptLines);
        AddRuntimeObservation(session, 0, "interrupt_queue", new AgentToolExecutionResult
        {
            Success = true,
            Phase = "interrupt_received",
            RunId = activeRun.Id,
            Message = $"执行中收到用户补充：{TrimForReply(merged, 300)}"
        }, "user_interrupt");

        await _runtimeRuns.UpdateProgressAsync(
            activeRun.Id,
            "interrupt_received",
            "已读取执行中的用户补充，正在重新判断下一步。",
            currentStep: 0,
            ct: ct).ConfigureAwait(false);

        return $"{currentUserMessage}\n\n[执行中收到用户补充]\n{merged}";
    }

    private static string FormatRuntimeInterruptForModel(string kind, string message, int priority)
    {
        var normalizedKind = string.IsNullOrWhiteSpace(kind) ? "freeform" : kind.Trim().ToLowerInvariant();
        var normalizedMessage = string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim();
        return $"- kind={normalizedKind}; priority={Math.Clamp(priority, 0, 100)}; message={normalizedMessage}";
    }

    private async Task<NovelProjectInfo> ResolveSessionProjectAsync(AgentSession session, CancellationToken ct)
    {
        NovelProjectInfo? project = null;
        if (!string.IsNullOrWhiteSpace(session.ActiveProjectId))
            project = await _catalog.FindAsync(session.ActiveProjectId, ct).ConfigureAwait(false);

        // Don't auto-bind to last active project - let LLM decide via tool calls
        if (project == null)
            throw new InvalidOperationException("No active project. LLM should use CommitStoryFoundation to create one or switch to existing project.");

        return project;
    }

    private async Task<NovelProjectInfo?> ResolvePendingConfirmationProjectAsync(AgentSession session, CancellationToken ct)
    {
        var pendingProjectId = session.WorkingMemory.PendingConfirmation?.ProjectId;
        if (!string.IsNullOrWhiteSpace(pendingProjectId))
            return await _catalog.FindAsync(pendingProjectId, ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(session.ActiveProjectId))
            return await _catalog.FindAsync(session.ActiveProjectId, ct).ConfigureAwait(false);

        // No automatic project selection - return null if no project
        return null;
    }

    private async Task<T> WithSessionProjectAsync<T>(AgentSession session, Func<Task<T>> operation, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.ActiveProjectId))
            throw new InvalidOperationException("No active project in session");

        await EnsureWorkspaceForSessionProjectAsync(session, ct).ConfigureAwait(false);
        var project = await _catalog.FindAsync(session.ActiveProjectId, ct).ConfigureAwait(false);
        if (project == null)
            throw new InvalidOperationException($"Project {session.ActiveProjectId} not found");

        return await _catalog.WithProjectAsync(project, operation, ct).ConfigureAwait(false);
    }

    private async Task<T> WithProjectScopeAsync<T>(AgentSession session, NovelProjectInfo project, Func<Task<T>> operation, CancellationToken ct)
    {
        var previousProjectId = session.ActiveProjectId;
        session.ActiveProjectId = project.Id;
        try
        {
            await EnsureWorkspaceForSessionProjectAsync(session, ct).ConfigureAwait(false);
            return await _catalog.WithProjectAsync(project, operation, ct).ConfigureAwait(false);
        }
        finally
        {
            session.ActiveProjectId = previousProjectId;
            await EnsureWorkspaceForSessionProjectAsync(session, ct).ConfigureAwait(false);
        }
    }

    private async Task<AgentChatResponse> FinishTextResponse(
        AgentSession session, string userMessage, AgentAction action,
        AgentObservationContext? context, IReadOnlyList<AgentRuntimeStep> trace, AgentReflection? reflection,
        CancellationToken ct)
    {
        var reply = PrepareUserFacingReply(FirstNonEmpty(action.Reply, reflection?.ReplyDraft, action.Brief, "已完成本轮分析。"));
        await AddChatTurnAsync(session, "assistant", reply, ct).ConfigureAwait(false);
        session.Phase = action.Type == AgentActionType.Clarify ? "awaiting_user_foundation" : session.Phase;
        await PersistTextSessionMemoryAsync(session, action, context, reflection, ct).ConfigureAwait(false);
        await _sessionManager.SaveSessionAsync(session, ct);
        return await BuildResponseAsync(session, reply, action.Suggestions, action, context, trace, ct).ConfigureAwait(false);
    }

    private async Task PersistTextSessionMemoryAsync(
        AgentSession session,
        AgentAction action,
        AgentObservationContext? context,
        AgentReflection? reflection,
        CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(session.ActiveProjectId) ||
                session.ActiveProjectId.StartsWith("temp-", StringComparison.OrdinalIgnoreCase))
            {
                await _memoryService.PersistProjectlessAsync(session, reflection, ct).ConfigureAwait(false);
                return;
            }

            var project = await _catalog.FindAsync(session.ActiveProjectId, ct).ConfigureAwait(false);
            if (project == null)
                return;

            SyncMissionPlan(session, project, null, action);
            if (context?.MissionPlan != null)
            {
                session.WorkingMemory.MissionPlan.ActiveTurnId = FirstNonEmpty(
                    session.WorkingMemory.MissionPlan.ActiveTurnId,
                    context.MissionPlan.ActiveTurnId);
                session.WorkingMemory.MissionPlan.ActiveToolTransactionId = FirstNonEmpty(
                    session.WorkingMemory.MissionPlan.ActiveToolTransactionId,
                    context.MissionPlan.ActiveToolTransactionId);
                session.WorkingMemory.MissionPlan.ArtifactCursor = FirstNonEmpty(
                    session.WorkingMemory.MissionPlan.ArtifactCursor,
                    context.MissionPlan.ArtifactCursor);
            }

            var bible = await _catalog.WithProjectAsync(project,
                () => _workspace.Orchestrator.GetStoryBibleAsync(ct), ct).ConfigureAwait(false) ?? new StoryBibleDocument();
            await _memoryService.PersistAsync(session, project, bible, reflection, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist pure text session memory for session {SessionId}", session.SessionId);
        }
    }

    private MemoryUpdateTrigger DetermineUpdateTrigger(AgentSession session)
    {
        var turnCount = session.ChatHistory.Count / 2;
        var lastDecision = session.WorkingMemory.LastDecision;
        var hasToolCall = lastDecision?.Mode == "tool_calling";
        var toolName = session.WorkingMemory.PendingToolCall?.Name;

        if (hasToolCall && toolName == "WriteChapter") return MemoryUpdateTrigger.ChapterWrite;
        if (hasToolCall) return MemoryUpdateTrigger.ToolCall;
        if (turnCount % 5 == 0 && turnCount > 0) return MemoryUpdateTrigger.Standard;
        return MemoryUpdateTrigger.Lightweight;
    }

    private async Task<AgentChatResponse> FinishReflectionResponse(
        AgentSession session, string userMessage, AgentAction action,
        AgentObservationContext? context, IReadOnlyList<AgentRuntimeStep> trace,
        AgentReflection reflection, AgentToolExecutionResult result, CancellationToken ct)
    {
        var trigger = DetermineUpdateTrigger(session);
        _logger.LogInformation("Memory update trigger: {Trigger} for session {SessionId}", trigger, session.SessionId);

        var reply = PrepareUserFacingReply(FirstNonEmpty(
            SelectReadOnlySnapshotReply(session, reflection, result),
            BuildToolFailureUserFacingReply(result, reflection),
            reflection.ReplyDraft,
            reflection.Summary,
            result.Message));
        await AddChatTurnAsync(session, "assistant", reply, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(session.ActiveProjectId) ||
            session.ActiveProjectId.StartsWith("temp-", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await _memoryService.PersistProjectlessAsync(session, reflection, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist projectless reflection memory for session {SessionId}", session.SessionId);
            }
        }
        await _sessionManager.SaveSessionAsync(session, ct);
        return await BuildResponseAsync(session, reply, result.Suggestions, action, context, trace, ct).ConfigureAwait(false);
    }

    private async Task<AgentChatResponse> FinishGovernanceResponseAsync(
        AgentSession session, string userMessage, AgentAction action,
        AgentObservationContext? context, List<AgentRuntimeStep> trace,
        int step, string toolName, AgentToolExecutionResult result,
        string observationType, NovelProjectInfo? project, CancellationToken ct)
    {
        var observation = AddRuntimeObservation(session, step, toolName, result, observationType);
        trace.Add(new AgentRuntimeStep { StepIndex = step, Stage = observationType, Action = action, Observation = observation });

        await EmitAsync(session, AgentSseEventType.AgentReflecting, "正在根据观察重新判断...", ct).ConfigureAwait(false);

        AgentObservationContext reflectContext;
        if (context != null)
        {
            reflectContext = context;
        }
        else if (project != null)
        {
            reflectContext = await _catalog.WithProjectAsync(project, async () =>
            {
                var bible = await _workspace.Orchestrator.GetStoryBibleAsync(ct).ConfigureAwait(false);
                return await _observationBuilder.BuildAsync(session, project, bible, userMessage, GetCurrentTurnIntent(session, userMessage), ct).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        }
        else
        {
            reflectContext = BuildProjectlessReflectContext(session, userMessage, context);
        }

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
        PrepareConfirmationResponseState(session);
        var reply = PrepareUserFacingReply(confirmationMessage);
        await AddChatTurnAsync(session, "assistant", reply, ct).ConfigureAwait(false);
        await _sessionManager.SaveSessionAsync(session, ct);
        return await BuildResponseAsync(session, reply, new[] { "继续执行", "调整方案" }, action, context, trace, ct).ConfigureAwait(false);
    }

    private static void PrepareConfirmationResponseState(AgentSession session)
    {
        var plan = session.WorkingMemory.MissionPlan ??= new AgentMissionPlan();
        plan.Status = FirstNonEmpty(plan.Status, plan.Stage, "active");
        plan.ProjectId = session.ActiveProjectId;
        plan.CurrentRunId = session.ActiveRunId ?? plan.CurrentRunId;
        plan.UpdatedAt = DateTime.UtcNow;
    }

    private async Task<AgentChatResponse> BuildResponseAsync(
        AgentSession session, string reply, IReadOnlyList<string> suggestions,
        AgentAction action, AgentObservationContext? context, IReadOnlyList<AgentRuntimeStep> trace,
        CancellationToken ct)
    {
        var memoryAudit = await BuildMemoryAuditSummaryAsync(session, ct).ConfigureAwait(false);
        return new AgentChatResponse(
            PrepareUserFacingReply(reply),
            suggestions.Count > 0 ? suggestions : new[] { "查看当前状态", "继续下一步" },
            session.SessionId,
            string.IsNullOrWhiteSpace(session.RuntimeRunId) ? null : session.RuntimeRunId,
            session.Phase,
            action.ToDecision() is { } decision ? AgentDecisionTrace.From(decision) : null,
            context?.Rag,
            AgentWorkingMemorySnapshot.From(session.WorkingMemory),
            trace.ToArray(),
            session.WorkingMemory.MissionPlan,
            null,
            memoryAudit,
            session.ActiveProjectId ?? string.Empty);
    }

    private async Task<AgentMemoryAuditSummary?> BuildMemoryAuditSummaryAsync(AgentSession session, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var projectId = string.IsNullOrWhiteSpace(session.ActiveProjectId) ||
                session.ActiveProjectId.StartsWith("temp-", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : session.ActiveProjectId;
            var summary = await AgentMemoryAuditSummaryBuilder
                .BuildAsync(db, session.UserId, session.SessionId, projectId, session.RuntimeRunId, ct)
                .ConfigureAwait(false);

            return summary.Reads.Count == 0 && summary.Promotions.Count == 0
                ? null
                : summary;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to build memory audit summary for session {SessionId}", session.SessionId);
            return null;
        }
    }

    private static string PrepareUserFacingReply(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
            return reply;

        var cleaned = reply.Trim();
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Commit 进书城"] = "提交到书城",
            ["Commit 到书城"] = "提交到书城",
            ["`PlanStoryFoundation`"] = "搭建故事地基",
            ["PlanStoryFoundation"] = "搭建故事地基",
            ["`QueryWorkspaceState`"] = "读取工作台状态",
            ["QueryWorkspaceState"] = "读取工作台状态",
            ["`QueryProjectStatus`"] = "读取项目状态",
            ["QueryProjectStatus"] = "读取项目状态",
            ["pending_quality_review"] = "等待质量评审",
            ["（pending）"] = "（等待提交）",
            ["**planned**"] = "**已规划，未启动**",
            ["planned"] = "已规划",
            ["CHANGES"] = "修订记录",
            ["Story Bible"] = "故事设定库",
            ["fallback 模式"] = "基础上下文模式",
            ["fallback"] = "基础上下文",
        };

        foreach (var (internalText, userText) in replacements)
            cleaned = cleaned.Replace(internalText, userText, StringComparison.Ordinal);

        return cleaned;
    }

    private static string BuildToolFailureUserFacingReply(AgentToolExecutionResult result, AgentReflection? reflection)
    {
        if (result.Success)
            return string.Empty;

        var isRecoverable = result.IsRepairable ||
                            result.Failure?.Recoverable == true ||
                            !string.IsNullOrWhiteSpace(result.RecommendedToolName) ||
                            result.Suggestions.Count > 0;
        var hasRawException = ContainsRawExceptionText(
            result.Message,
            result.Failure?.Reason,
            reflection?.ReplyDraft,
            reflection?.Summary);

        if (!isRecoverable && !hasRawException)
            return string.Empty;

        var toolName = FirstNonEmpty(
            result.RecommendedToolName,
            result.Failure?.RecommendedAction,
            "当前工具");
        var stage = FirstNonEmpty(result.Failure?.FailedStage, result.Phase, "当前阶段");
        var stageText = string.Equals(stage, "当前阶段", StringComparison.OrdinalIgnoreCase)
            ? "当前阶段"
            : $"「{stage}」阶段";
        var retryText = string.Equals(toolName, "当前工具", StringComparison.OrdinalIgnoreCase)
            ? "你可以让我重试，或先去工作流查看失败阶段。"
            : $"你可以让我重试 {toolName}，或先去工作流查看失败阶段。";

        return $"这次 {toolName} 在{stageText}遇到{(isRecoverable ? "可重试" : "执行")}问题，当前产物和执行记录已保留。{retryText}";
    }

    private static bool ContainsRawExceptionText(params string?[] values)
    {
        var text = string.Join("\n", values.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var markers = new[]
        {
            "Exception:",
            "HttpRequestException",
            "TimeoutException",
            "TaskCanceledException",
            "InvalidOperationException",
            "NullReferenceException",
            "An error occurred while sending the request",
            "System.",
            " at "
        };
        return markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

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

    private static void AddProgressObservation(
        AgentSession session,
        int step,
        string toolName,
        TM.Web.NovelAgentWeb.Services.AgentTools.AgentToolProgressView progress)
    {
        var observation = new AgentRuntimeObservation
        {
            StepIndex = step,
            ObservationType = "tool_progress",
            ToolName = toolName,
            Success = true,
            RequiresConfirmation = false,
            Risk = "Low",
            Message = $"{progress.Title}。{progress.Detail}",
            RunId = progress.RunId ?? session.ActiveRunId ?? string.Empty,
            Phase = session.Phase,
        };
        session.WorkingMemory.RecentObservations.Add(observation);
        if (session.WorkingMemory.RecentObservations.Count > MaxRecentObservations)
            session.WorkingMemory.RecentObservations.RemoveRange(0, session.WorkingMemory.RecentObservations.Count - MaxRecentObservations);
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

    private static void SyncMissionPlan(AgentSession session, NovelProjectInfo? project, AgentToolExecutionResult? result, AgentAction? action)
    {
        if (project == null) return;

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

    private async Task SyncMissionPlanAsync(AgentSession session, NovelProjectInfo? project, AgentToolExecutionResult? result, AgentAction? action, AgentReflection? reflection, CancellationToken ct)
    {
        if (project == null) return;

        SyncMissionPlan(session, project, result, action);
        var bible = await _catalog.WithProjectAsync(project, () => _workspace.Orchestrator.GetStoryBibleAsync(ct), ct).ConfigureAwait(false) ?? new StoryBibleDocument();
        _taskTreeService.Sync(session, project, bible, result, action, reflection);
        await _memoryService.PersistAsync(session, project, bible, reflection, ct).ConfigureAwait(false);
    }

    private async Task PublishToolResultAsync(AgentSession session, string toolName, AgentToolExecutionResult result, CancellationToken ct)
    {
        var type = result.Success ? AgentSseEventType.StepComplete : AgentSseEventType.StepFail;
        var progress = string.IsNullOrWhiteSpace(toolName)
            ? null
            : TM.Web.NovelAgentWeb.Services.AgentTools.AgentToolProgressPresenter.Describe(new TM.Web.NovelAgentWeb.Services.AgentTools.AgentToolExecutionSnapshot
            {
                ToolName = toolName,
                Status = result.Success ? "succeeded" : "failed",
                Phase = session.Phase,
                ResultPhase = result.Phase,
                ResultMessage = result.Message,
                RunId = result.RunId,
                StartedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
        });
        await EmitAsync(session, type, progress?.Title ?? result.Message, ct, progress ?? result.Data, result.RunId);
        var preview = TM.Web.NovelAgentWeb.Services.AgentTools.AgentArtifactPreviewPresenter.Build(toolName, result);
        if (preview != null)
            await EmitAsync(session, AgentSseEventType.ArtifactPreview, preview.Title, ct, preview, preview.RunId ?? result.RunId);
        if (result.Data is NovelAgentRun run)
            await EmitAsync(session, AgentSseEventType.RunCreated, result.Message, ct, run, run.RunId);
    }

    private async Task<AgentToolExecutionResult> ExecuteToolWithHeartbeatAsync(
        AgentSession session,
        AgentToolCall toolCall,
        Func<Task<AgentToolExecutionResult>> execute,
        CancellationToken ct)
    {
        if (!IsLongRunningWritingTool(toolCall.Name))
            return await execute().ConfigureAwait(false);

        var startedAt = DateTime.UtcNow;
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeatTask = EmitToolHeartbeatAsync(session, toolCall, startedAt, heartbeatCts.Token);
        try
        {
            return await execute().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var report = BuildToolFailureProgress(toolCall.Name, session.Phase, ExtractRunId(toolCall, session), ex.Message);
            await EmitAsync(session, AgentSseEventType.StepFail, report.Title, ct, report, report.RunId).ConfigureAwait(false);
            throw;
        }
        finally
        {
            await heartbeatCts.CancelAsync().ConfigureAwait(false);
            try
            {
                await heartbeatTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown once the tool finishes.
            }
        }
    }

    private async Task EmitToolHeartbeatAsync(
        AgentSession session,
        AgentToolCall toolCall,
        DateTime startedAt,
        CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var elapsed = DateTime.UtcNow - startedAt;
                var runId = ExtractRunId(toolCall, session);
                var progress = AgentToolProgressPresenter.DescribeHeartbeat(toolCall.Name, elapsed, session.Phase, runId);
                AddProgressObservation(session, 0, toolCall.Name, progress);
                await EmitAsync(session, AgentSseEventType.AgentActing, progress.Title, ct, progress, runId).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // The owning tool completed or the request was canceled.
        }
    }

    private static AgentToolProgressView BuildToolFailureProgress(string toolName, string phase, string? runId, string error)
    {
        var progress = AgentToolProgressPresenter.DescribeRunning(toolName, "failed", phase, runId);
        progress.Detail = string.Join("\n", new[]
        {
            $"失败阶段：{FirstNonEmpty(phase, "工具执行")}",
            "已有产物：请查看工作流中的最近草稿、门禁报告或评审记录。",
            $"可继续动作：查看失败项，必要时重新修订后继续。原始错误：{error}",
            "是否需要用户决定：如果失败项涉及设定取舍，需要用户确认；纯格式或索引问题可继续自动修复。"
        });
        return progress;
    }

    private static string? ExtractRunId(AgentToolCall toolCall, AgentSession session)
    {
        if (toolCall.Arguments.TryGetValue("runId", out var value) && !string.IsNullOrWhiteSpace(value))
            return value;
        return session.ActiveRunId;
    }

    private async Task EmitAsync(AgentSession session, string type, string message, CancellationToken ct, object? data = null, string? runId = null)
    {
        var persistence = await PersistRuntimeProgressAsync(session, type, message, data, ct).ConfigureAwait(false);
        var saved = persistence?.Event;
        var runtimeRunId = FirstNonEmpty(
            persistence?.RuntimeRunId,
            saved?.RuntimeRunId,
            session.RuntimeRunId,
            runId);
        var sourceMessageId = FirstNonEmpty(persistence?.SourceMessageId, ExtractStringProperty(data, "sourceMessageId"));
        await _sessionManager.SendEventAsync(session.SessionId, new AgentSseEvent
        {
            EventId = saved?.Id ?? string.Empty,
            Type = type,
            RunId = runtimeRunId,
            SourceMessageId = sourceMessageId,
            Stage = saved?.Stage ?? string.Empty,
            Status = saved?.Status ?? string.Empty,
            ArtifactType = saved?.ArtifactType ?? string.Empty,
            ArtifactId = saved?.ArtifactId ?? string.Empty,
            DisplaySurface = saved?.DisplaySurface ?? string.Empty,
            DisplayPolicy = saved?.DisplayPolicy ?? string.Empty,
            Message = message,
            Data = MergeRuntimeSseData(data, runtimeRunId, sourceMessageId),
            Timestamp = saved?.CreatedAt ?? DateTime.UtcNow
        }, ct).ConfigureAwait(false);
    }

    private sealed record RuntimeProgressPersistence(
        Data.Entities.AgentRuntimeEvent? Event,
        string RuntimeRunId,
        string SourceMessageId);

    private async Task<RuntimeProgressPersistence?> PersistRuntimeProgressAsync(AgentSession session, string type, string message, object? data, CancellationToken ct)
    {
        try
        {
            var userId = _currentUserService.GetUserId();
            var activeRun = await _runtimeRuns.TryGetActiveAsync(userId, session.SessionId, ct).ConfigureAwait(false);
            if (activeRun == null)
                return null;

            var phase = type switch
            {
                AgentSseEventType.AgentObserving => "observing",
                AgentSseEventType.AgentPlanning => "planning",
                AgentSseEventType.AgentActing => "acting",
                AgentSseEventType.AgentReflecting => "reflecting",
                AgentSseEventType.StepComplete => "step_complete",
                AgentSseEventType.StepFail => "step_failed",
                AgentSseEventType.ArtifactPreview => "artifact_preview",
                AgentSseEventType.MissionUpdated => "mission_updated",
                AgentSseEventType.ConfirmationRequired => "waiting_confirmation",
                _ => activeRun.CurrentPhase
            };
            var toolName = data is TM.Web.NovelAgentWeb.Services.AgentTools.AgentToolProgressView progress
                ? progress.ToolName
                : activeRun.ActiveTool;
            await _runtimeRuns.UpdateProgressAsync(activeRun.Id, phase, message, toolName, ExtractStepNumber(message), ct).ConfigureAwait(false);
            if (type == AgentSseEventType.ArtifactPreview)
            {
                var saved = await _runtimeEvents.AppendAsync(new CreateAgentRuntimeEventRequest(
                    activeRun.Id,
                    userId,
                    session.SessionId,
                    activeRun.ProjectId ?? session.ActiveProjectId,
                    type,
                    message,
                    data), ct).ConfigureAwait(false);
                return new RuntimeProgressPersistence(saved, activeRun.Id, activeRun.SourceMessageId);
            }
            return new RuntimeProgressPersistence(null, activeRun.Id, activeRun.SourceMessageId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to persist runtime progress for session {SessionId}", session.SessionId);
            return null;
        }
    }

    private static object? MergeRuntimeSseData(object? data, string runtimeRunId, string sourceMessageId)
    {
        if (string.IsNullOrWhiteSpace(runtimeRunId) && string.IsNullOrWhiteSpace(sourceMessageId))
            return data;

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(runtimeRunId))
            result["runtimeRunId"] = runtimeRunId.Trim();
        if (!string.IsNullOrWhiteSpace(sourceMessageId))
            result["sourceMessageId"] = sourceMessageId.Trim();

        if (data == null)
            return result;

        try
        {
            var element = JsonSerializer.SerializeToElement(data);
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                    result[property.Name] = property.Value.Clone();
            }
            else
            {
                result["payload"] = element.Clone();
            }
        }
        catch
        {
            result["payload"] = data;
        }

        if (!string.IsNullOrWhiteSpace(runtimeRunId))
            result["runtimeRunId"] = runtimeRunId.Trim();
        if (!string.IsNullOrWhiteSpace(sourceMessageId))
            result["sourceMessageId"] = sourceMessageId.Trim();

        return result;
    }

    private static string ExtractStringProperty(object? data, string propertyName)
    {
        if (data == null)
            return string.Empty;

        try
        {
            var element = JsonSerializer.SerializeToElement(data);
            if (element.ValueKind != JsonValueKind.Object)
                return string.Empty;
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                    return property.Value.GetString() ?? string.Empty;
            }
        }
        catch
        {
            return string.Empty;
        }

        return string.Empty;
    }

    private static int? ExtractStepNumber(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        var marker = message.IndexOf("第 ", StringComparison.Ordinal);
        if (marker < 0)
            return null;

        var start = marker + 2;
        var slash = message.IndexOf('/', start);
        if (slash <= start)
            return null;

        return int.TryParse(message[start..slash], out var step) ? step : null;
    }

    private static void RememberUserMessage(AgentSession session, string userMessage, AgentAction action)
    {
        if (action.Intent is "clarify_story" or "create_foundation" or "plan_chapter" or "start_new_novel_project")
            session.WorkingMemory.CurrentGoal = userMessage.Trim();
        if (session.WorkingMemory.UserPreferences.Count > MaxRecentObservations)
            session.WorkingMemory.UserPreferences.RemoveRange(0, session.WorkingMemory.UserPreferences.Count - MaxRecentObservations);
    }

    private async Task AddChatTurnAsync(AgentSession session, string role, string content, CancellationToken ct)
    {
        var trimmed = content.Trim();
        session.ChatHistory.Add(new AgentConversationTurn { Role = role, Content = trimmed, CreatedAt = DateTime.UtcNow });
        await _chatHistory.AppendAsync(
            session.UserId,
            string.IsNullOrWhiteSpace(session.ActiveProjectId) ? null : session.ActiveProjectId,
            session.SessionId,
            role,
            trimmed,
            ct).ConfigureAwait(false);
        await _chatHistoryCompressor.CompressAndPersistAsync(
            session.UserId,
            string.IsNullOrWhiteSpace(session.ActiveProjectId) ? null : session.ActiveProjectId,
            session.SessionId,
            session.ChatHistory,
            ct).ConfigureAwait(false);
    }

    private static AgentAction BuildNoActionReplyAction(AgentAction action) => new()
    {
        Type = AgentActionType.FinalReply,
        Intent = action.Intent,
        Reply = FirstNonEmpty(action.Reply, action.Brief, "没有可执行动作，先停在这里。"),
        Suggestions = action.Suggestions,
        Source = "runtime_guard",
    };

    private static string BuildCompletedToolResultReply(
        AgentToolExecutionResult result,
        AgentReflection? reflection)
    {
        var message = FirstNonEmpty(result.Message, result.Artifact?.UserVisibleStatus, "本轮工具执行已完成。");
        var next = FirstNonEmpty(
            reflection?.ReplyDraft,
            reflection?.Summary,
            result.Suggestions is { Count: > 0 }
                ? $"下一步可以：{string.Join("、", result.Suggestions.Take(3))}。"
                : string.Empty);

        if (string.IsNullOrWhiteSpace(next))
            return message;

        return $"{message}\n\n{next}";
    }

    public static bool ShouldStopAfterToolResult(
        AgentToolExecutionResult result,
        AgentReflection reflection,
        AgentMissionPlan? plan = null)
    {
        if (IsCapabilityDiscoveryArtifact(result.Artifact))
            return false;

        if (!result.Success)
            return !IsRecoverableToolFailure(result, plan);

        if (IsCommittedChapterAuditResult(result))
            return true;

        var hasStructuredNextStep = HasStructuredNextStep(result, plan);
        var hasMissionStructuredNextStep = HasMissionStructuredNextStep(plan);

        if (IsTerminalChapterProductionResult(result))
            return !HasRemainingBookProductionWork(plan);

        if (IsReadOnlyStateSnapshotArtifact(result.Artifact) && string.IsNullOrWhiteSpace(result.RecommendedToolName))
        {
            if (string.Equals(result.Artifact?.ArtifactType, "workspace_state", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(result.Artifact?.ArtifactType, "project_content_query", StringComparison.OrdinalIgnoreCase) &&
                reflection.ShouldContinue &&
                !reflection.GoalSatisfied &&
                !reflection.RequiresUserInput)
            {
                return false;
            }

            if (reflection.ShouldContinue &&
                !reflection.GoalSatisfied &&
                !reflection.RequiresUserInput &&
                hasMissionStructuredNextStep)
            {
                return false;
            }

            return true;
        }

        if (IsUserReviewArtifact(result.Artifact) || IsUserReviewPhase(result.Phase))
            return ShouldPauseForUserReviewArtifact(reflection, plan);

        if (reflection.RequiresUserInput && !hasStructuredNextStep)
            return true;

        if (reflection.GoalSatisfied && !hasStructuredNextStep)
            return true;

        if (!reflection.ShouldContinue && !hasStructuredNextStep)
            return true;

        return false;
    }

    public static bool ShouldFinishFailureWithoutReflection(AgentToolExecutionResult result)
    {
        if (result.Success)
            return false;

        if (result.Artifact?.ArtifactType is
            "chapter_production_blocked" or
            "chapter_generation_blocked" or
            "chapter_commit_blocked" or
            "chapter_review_blocked" or
            "committed_chapter_revision_blocked")
        {
            if (result.Failure?.RequiresUserDecision == true)
                return true;
            if (result.IsRepairable && !string.IsNullOrWhiteSpace(result.RecommendedToolName))
                return false;
            return true;
        }

        return result.Failure?.RequiresUserDecision == true &&
               string.Equals(result.Failure.FailedStage, "ReviewChapter", StringComparison.OrdinalIgnoreCase);
    }

    private static AgentReflection BuildTerminalFailureReflection(AgentToolExecutionResult result)
    {
        var message = FirstNonEmpty(result.Message, result.Failure?.Reason, result.Artifact?.Summary, "工具执行已停止。");
        var next = result.Suggestions.Count > 0
            ? $"可继续动作：{string.Join("、", result.Suggestions.Take(3))}。"
            : string.Empty;
        return new AgentReflection
        {
            Summary = message,
            ReplyDraft = string.IsNullOrWhiteSpace(next) ? message : $"{message}\n\n{next}",
            ShouldContinue = false,
            RequiresUserInput = result.Failure?.RequiresUserDecision == true || !result.IsRepairable,
            GoalSatisfied = false,
            NextIntent = "blocked",
            Blockers = { message }
        };
    }

    public static bool ShouldEscalateSafety(int step, int maxSteps) =>
        step >= Math.Max(1, maxSteps);

    private static bool HasStructuredNextStep(AgentToolExecutionResult result, AgentMissionPlan? plan) =>
        result.Artifact?.NextHints.Count > 0 == true ||
        result.Suggestions.Count > 0 ||
        HasMissionStructuredNextStep(plan);

    private static bool IsRecoverableToolFailure(AgentToolExecutionResult result, AgentMissionPlan? plan) =>
        result.IsRepairable &&
        !string.IsNullOrWhiteSpace(result.RecommendedToolName) &&
        HasMissionStructuredNextStep(plan);

    private static bool HasMissionStructuredNextStep(AgentMissionPlan? plan) =>
        plan?.AllowedNextActions.Count > 0 ||
        plan?.TodoQueue.Count > 0 ||
        HasSchedulerPointerNextStep(plan) ||
        plan?.SchedulerState.Tasks.Any(t => t.Status is "running" or "queued") == true;

    private static bool HasRemainingBookProductionWork(AgentMissionPlan? plan)
    {
        if (plan == null)
            return false;

        if (plan.SchedulerState.Tasks.Any(task =>
                IsOpenTaskStatus(task.Status) &&
                !string.IsNullOrWhiteSpace(task.NextAction)))
        {
            return true;
        }

        if (plan.BookTaskTree.Volumes
            .SelectMany(volume => volume.Chapters)
            .Any(chapter =>
                IsOpenChapterStatus(chapter.Status) &&
                (HasChapterProductionPointer(chapter) || !string.IsNullOrWhiteSpace(chapter.NextAction))))
        {
            return true;
        }

        return plan.TodoQueue.Count > 0 || plan.AllowedNextActions.Count > 0;
    }

    private static bool IsOpenTaskStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return true;

        return status.Trim().ToLowerInvariant() is not
            "completed" and not
            "done" and not
            "cancelled" and not
            "canceled" and not
            "failed" and not
            "blocked";
    }

    private static bool IsOpenChapterStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return true;

        return status.Trim().ToLowerInvariant() is not
            "completed" and not
            "done" and not
            "committed" and not
            "published" and not
            "failed" and not
            "cancelled" and not
            "canceled";
    }

    private static bool HasChapterProductionPointer(AgentChapterTask chapter) =>
        !string.IsNullOrWhiteSpace(chapter.RunId) ||
        !string.IsNullOrWhiteSpace(chapter.DraftArtifactId) ||
        !string.IsNullOrWhiteSpace(chapter.GateReportId) ||
        !string.IsNullOrWhiteSpace(chapter.QualityReportId) ||
        !string.IsNullOrWhiteSpace(chapter.CommitConfirmationId) ||
        chapter.CandidateStatus is not "unstarted" ||
        chapter.ContextStatus is not "pending" ||
        chapter.DraftStatus is not "none";

    private static bool ShouldPauseForUserReviewArtifact(AgentReflection reflection, AgentMissionPlan? plan)
    {
        if (reflection.RequiresUserInput)
            return true;

        if (reflection.GoalSatisfied)
            return true;

        if (!reflection.ShouldContinue)
            return true;

        return !HasMissionStructuredNextStep(plan);
    }

    private static bool HasSchedulerPointerNextStep(AgentMissionPlan? plan)
    {
        if (plan == null)
            return false;

        var scheduler = plan.SchedulerState;
        return !string.IsNullOrWhiteSpace(scheduler.ActiveTaskId) ||
               !string.IsNullOrWhiteSpace(scheduler.ActiveRunId) ||
               !string.IsNullOrWhiteSpace(scheduler.ActiveChapterId);
    }

    private static bool IsCapabilityDiscoveryArtifact(AgentToolArtifact? artifact) =>
        string.Equals(artifact?.ArtifactType, "tool_search_result", StringComparison.OrdinalIgnoreCase);

    private static bool IsReadOnlyStateSnapshotArtifact(AgentToolArtifact? artifact) =>
        artifact?.ArtifactType is "workspace_state" or "project_status" or "workflow_state" or "project_content_query";

    private static bool IsCommittedChapterAuditResult(AgentToolExecutionResult result) =>
        string.Equals(result.Phase, "committed_chapter_audit", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(result.Artifact?.ArtifactType, "committed_chapter_audit", StringComparison.OrdinalIgnoreCase);

    private static bool IsTerminalChapterProductionResult(AgentToolExecutionResult result)
    {
        var artifactType = result.Artifact?.ArtifactType ?? string.Empty;
        return string.Equals(result.Phase, "committed", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(result.Phase, "chapter_committed", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(artifactType, "chapter_produced", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(artifactType, "chapter_committed", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildReadOnlySnapshotReply(AgentSession session, AgentToolExecutionResult result)
    {
        if (!result.Success || !IsReadOnlyStateSnapshotArtifact(result.Artifact))
            return string.Empty;

        if (result.Data is AgentWorkspaceState workspaceState)
            return BuildWorkspaceSnapshotReply(session, workspaceState);

        var artifact = result.Artifact;
        if (artifact == null)
            return string.Empty;

        var summary = string.Equals(artifact.ArtifactType, "project_content_query", StringComparison.OrdinalIgnoreCase)
            ? FirstNonEmpty(result.Message, artifact.Summary)
            : FirstNonEmpty(artifact.Summary, result.Message);
        if (string.IsNullOrWhiteSpace(summary))
            return string.Empty;

        var lines = summary
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(4)
            .ToList();
        return lines.Count == 0 ? summary : string.Join("\n", lines);
    }

    private static string SelectReadOnlySnapshotReply(
        AgentSession session,
        AgentReflection reflection,
        AgentToolExecutionResult result)
    {
        if (!result.Success || !IsReadOnlyStateSnapshotArtifact(result.Artifact))
            return string.Empty;

        if (IsUsableModelSnapshotReply(reflection.ReplyDraft, result))
            return reflection.ReplyDraft;

        return BuildReadOnlySnapshotReply(session, result);
    }

    private static bool IsUsableModelSnapshotReply(string? reply, AgentToolExecutionResult result)
    {
        if (string.IsNullOrWhiteSpace(reply))
            return false;

        var normalizedReply = reply.Trim();
        if (string.Equals(normalizedReply, result.Message?.Trim(), StringComparison.Ordinal))
            return false;

        if (IsNoToolFallbackReply(normalizedReply))
            return false;

        return !normalizedReply.Contains("activeProjectId=", StringComparison.OrdinalIgnoreCase) &&
               !normalizedReply.Contains("owned_by_current_user=", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNoToolFallbackReply(string reply) =>
        reply.Contains("没有新的工具执行结果", StringComparison.OrdinalIgnoreCase) ||
        reply.Contains("没有继续执行新的写入动作", StringComparison.OrdinalIgnoreCase);

    private static string BuildWorkspaceSnapshotReply(AgentSession session, AgentWorkspaceState state)
    {
        var displayName = FirstNonEmpty(state.AuthorProfile.DisplayName, session.WorkingMemory.AuthorMemory?.DisplayName);
        var prefix = string.IsNullOrWhiteSpace(displayName) ? string.Empty : $"{displayName}，";

        var projectLine = state.ProjectTotalCount <= 0
            ? "小说书城当前没有可见项目。"
            : $"小说书城当前可见项目共 {state.ProjectTotalCount} 本；本次快照列出最近 {state.ProjectPreviewCount} 本。";

        var previewLine = string.Empty;
        if (state.VisibleProjects.Count > 0)
        {
            var statusCounts = state.VisibleProjects
                .GroupBy(p => string.IsNullOrWhiteSpace(p.Status) ? "未知状态" : p.Status)
                .Select(g => $"{g.Key} {g.Count()} 本");
            var committed = state.VisibleProjects.Sum(p => p.CommittedChapterCount);
            previewLine = $"快照状态：{string.Join("，", statusCounts)}；已提交章节 {committed} 章。";
        }

        var knowledgeLine = $"知识库当前可见条目 {state.KnowledgeBase.TotalCount} 条。";
        var workflowLine = state.Workflow.ActiveRunCount > 0
            ? $"创作工作流当前有 {state.Workflow.ActiveRunCount} 个活跃 Run。"
            : "创作工作流当前没有活跃 Run。";
        var sessionLine = state.CurrentSession.HasActiveProject
            ? "当前会话已绑定一个小说项目。"
            : "当前会话没有绑定项目。";

        return string.Join("\n", new[]
        {
            prefix + projectLine,
            previewLine,
            knowledgeLine,
            workflowLine,
            sessionLine,
            "这次只是读取状态，没有创建或修改项目。"
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static bool IsUserReviewArtifact(AgentToolArtifact? artifact)
    {
        if (artifact == null)
            return false;

        return artifact.ArtifactType is "story_foundation_candidates" or "volume_arc_candidates" or "chapter_candidates" ||
               artifact.ArtifactType.Contains("candidate", StringComparison.OrdinalIgnoreCase) ||
               artifact.ArtifactType.Contains("confirmation", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUserReviewPhase(string? phase)
    {
        if (string.IsNullOrWhiteSpace(phase))
            return false;

        return phase.Contains("candidate", StringComparison.OrdinalIgnoreCase) ||
               phase.Contains("awaiting_confirmation", StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldReturnUserFacingReply(AgentAction action)
    {
        if (string.IsNullOrWhiteSpace(action.Reply)) return false;
        if (action.Type is not (AgentActionType.ChatReply or AgentActionType.FinalReply or AgentActionType.Clarify))
            return false;

        var source = action.Source.Trim().ToLowerInvariant();
        if (source.Contains("planner_error", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("parse_error", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("provider_raw_error", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("error_timeout", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static bool ShouldStartBackground(AgentAction action) =>
        action.Type is AgentActionType.ToolCall or AgentActionType.Retrieve or AgentActionType.ConfirmRequest ||
        action.ToolCall != null;

    private static AgentAction BuildForegroundNoBackgroundReply(AgentAction action)
    {
        if (!string.IsNullOrWhiteSpace(action.Reply))
        {
            return new AgentAction
            {
                Type = action.Type is AgentActionType.Clarify ? AgentActionType.Clarify : AgentActionType.ChatReply,
                Intent = string.IsNullOrWhiteSpace(action.Intent) ? "foreground_reply" : action.Intent,
                Reply = action.Reply,
                Suggestions = action.Suggestions,
                Source = action.Source,
                Risk = action.Risk,
                Confidence = action.Confidence,
                IsNoTool = true,
            };
        }

        return new AgentAction
        {
            Type = AgentActionType.ChatReply,
            Intent = string.IsNullOrWhiteSpace(action.Intent) ? "foreground_no_background" : action.Intent,
            Reply = "这一轮我没有拿到可靠的可执行动作，所以不会启动后台任务。你可以继续问我当前状态，或直接告诉我要创作、修改、查询哪一部分。",
            Suggestions = new[] { "查看当前状态", "开始创作任务", "补充要求" },
            Source = string.IsNullOrWhiteSpace(action.Source) ? "foreground_no_background" : action.Source,
            IsNoTool = true,
            Confidence = action.Confidence,
        };
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
    }

    private static string BuildConfirmationMessage(string toolName) => toolName switch
    {
        "CommitStoryFoundation" => "会把故事地基写入 Story Bible，并继续推进后续规划。",
        "CommitVolumeArc" => "会把卷规划写入 Story Bible，并继续推进章节生产线。",
        "ProduceChapter" => "会执行章节生产闭环：构建上下文、生成正文、门禁校验、必要修复、质量评审和提交书城。",
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

            return $"我理解你选择了第 {index} 个{kind}。{actionMessage} 如果确认这样推进，请回复“确认”；如果要放弃这次选择，请回复“取消”。";
        }

        var resolved = FirstNonEmpty(message, action.Reply, baseMessage);
        if (resolved.Contains("确认", StringComparison.Ordinal) &&
            resolved.Contains("取消", StringComparison.Ordinal))
        {
            return resolved;
        }

        return $"{resolved} 如果确认这样推进，请回复“确认”；如果要放弃这次操作，请回复“取消”。";
    }

    private static string MapMissionStage(string phase) => phase switch
    {
        "foundation_candidates" or "foundation_committed" or "awaiting_user_foundation" => "foundation",
        "volume_plan" or "volume_committed" => "volume_planning",
        "chapter_candidates" or "candidate_selected" or "chapter_generated" or "chapter_reviewed" => "chapter_work",
        "awaiting_confirmation" => "active",
        _ => phase,
    };

    private static string BuildStatusSummary(AgentSession session, StoryBibleDocument? bible)
    {
        var parts = new List<string>();
        if (bible == null)
        {
            parts.Add("尚未绑定项目。可以先创建新小说，或切换到已有项目。");
            return string.Join("\n", parts);
        }

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
        return title.Length <= 80 ? title : title[..80].TrimEnd();
    }

    private static string TrimForReply(string value, int maxLength)
    {
        var trimmed = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "...";
    }

    private static string TruncateLine(string value, int maxLength)
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

}
