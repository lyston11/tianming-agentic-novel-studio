using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Models.Tracking;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.AgentRuntime;
using TM.Web.NovelAgentWeb.Services.AgentTools;
using TM.Web.NovelAgentWeb.Models.Chapters;
using TM.Web.NovelAgentWeb.Services.Chapters;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Creative;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using TM.Web.NovelAgentWeb.Services.Materials;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Services.Workspace;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class AgentProductionStageProgressOptions
{
    public int HeartbeatSeconds { get; set; } = 15;
    public int StageTimeoutSeconds { get; set; } = 180;
    public int DraftGenerationTimeoutSeconds { get; set; } = 360;
}

public static class AgentProductionStageTimeoutPolicy
{
    public static TimeSpan ResolveTimeout(
        string stage,
        AgentProductionStageProgressOptions? options)
    {
        var canonicalStage = NovelAgentProductionStages.ToCanonicalStage(stage);
        var isDraftGeneration = string.Equals(canonicalStage, NovelAgentProductionStages.DraftGenerated, StringComparison.Ordinal);
        var defaultSeconds = isDraftGeneration
            ? 360
            : 180;
        var configuredSeconds = isDraftGeneration
            ? options?.DraftGenerationTimeoutSeconds ?? defaultSeconds
            : options?.StageTimeoutSeconds ?? defaultSeconds;
        var seconds = Math.Clamp(configuredSeconds, 0, 900);
        return seconds <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(seconds);
    }
}

public sealed partial class AgentToolRegistry
{
    private static readonly AsyncLocal<NovelAgentWorkspace?> _currentWorkspace = new();
    private static readonly AsyncLocal<NovelProjectCatalog?> _currentCatalog = new();
    private static readonly RuntimeInterruptDrainResult NoRuntimeInterrupts = new(
        Array.Empty<AgentRuntimeInterruptObservation>(),
        null);

    private NovelAgentWorkspace? _workspaceInstance;
    private NovelProjectCatalog? _catalogInstance;

    private NovelAgentWorkspace _workspace => _workspaceInstance ?? _currentWorkspace.Value ?? throw new InvalidOperationException("Workspace not set for current request");
    private NovelProjectCatalog _catalog => _catalogInstance ?? _currentCatalog.Value ?? throw new InvalidOperationException("Catalog not set for current request");

    private readonly UserSettingsManager _settingsManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, AgentToolEntry> _entries;
    private readonly ILogger<AgentToolRegistry> _logger;

    private sealed record RuntimeInterruptDrainResult(
        IReadOnlyList<AgentRuntimeInterruptObservation> Consumed,
        AgentRuntimeInterruptObservation? DirectionChange);

    internal static void SetWorkspace(NovelAgentWorkspace workspace, NovelProjectCatalog catalog)
    {
        _currentWorkspace.Value = workspace;
        _currentCatalog.Value = catalog;
    }

    internal static void ClearWorkspace()
    {
        _currentWorkspace.Value = null;
        _currentCatalog.Value = null;
    }

    internal void SetWorkspaceContext(NovelAgentWorkspace workspace, NovelProjectCatalog catalog)
    {
        _workspaceInstance = workspace;
        _catalogInstance = catalog;
        SetWorkspace(workspace, catalog);
    }

    internal void ClearWorkspaceContext()
    {
        _workspaceInstance = null;
        _catalogInstance = null;
        ClearWorkspace();
    }

    public AgentToolRegistry(UserSettingsManager settingsManager, IServiceProvider serviceProvider, ILogger<AgentToolRegistry> logger)
    {
        _settingsManager = settingsManager;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _entries = BuildEntries();
    }

    public IReadOnlyList<AgentToolDefinition> ListTools() => _entries.Values
        .Select(e => e.Definition)
        .ToList();

    public IReadOnlyList<ToolSchema> ListToolSchemas() => _entries.Values
        .Select(e => ToSchema(e.Definition))
        .ToList();

    public IReadOnlyList<string> GetToolNamesRankedByPhaseHint(ConversationPhase phase)
    {
        return _entries.Values
            .Where(entry => !string.Equals(entry.Definition.Name, "tool_search", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => CategoryHintRank(entry.Category, phase))
            .ThenBy(entry => entry.Definition.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry => entry.Definition.Name)
            .ToArray();
    }

    internal string CurrentWorkspaceProjectIdForTests() => _workspace.ProjectId;

    public IReadOnlyList<ToolSchema> ListToolSchemasRankedByPhaseHint(ConversationPhase phase)
    {
        return GetToolNamesRankedByPhaseHint(phase)
            .Select(name => _entries.TryGetValue(name, out var entry) ? entry.Definition : null)
            .Where(def => def != null)
            .Select(def => ToSchema(def!))
            .ToList();
    }

    public AgentToolDefinition? Find(string name) =>
        _entries.TryGetValue(name.Trim(), out var entry) ? entry.Definition : null;

    private static ToolSchema ToSchema(AgentToolDefinition definition) => new()
    {
        Name = definition.Name,
        Description = definition.Description,
        Risk = definition.Risk,
        RequiresConfirmation = definition.RequiresConfirmation,
        Parameters = definition.Arguments.ToDictionary(arg => arg, _ => "string", StringComparer.OrdinalIgnoreCase),
        SideEffects = definition.SideEffects,
        Semantic = definition.Semantic,
    };

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolCall call,
        AgentSession session,
        StoryBibleDocument bible,
        bool confirmed,
        CancellationToken ct)
    {
        var name = call.Name.Trim();
        if (!_entries.TryGetValue(name, out var entry))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = $"未知工具：{name}",
                Phase = session.Phase,
            };
        }

        using var ledgerScope = _serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var ledger = ledgerScope.ServiceProvider.GetRequiredService<IAgentToolExecutionLedger>();
        var execution = await ledger.StartAsync(new AgentToolExecutionStart(
                session.UserId,
                string.IsNullOrWhiteSpace(session.ActiveProjectId) ? null : session.ActiveProjectId,
                session.SessionId,
                ResolveRunId(call, session),
                session.Phase,
                entry.Definition.Risk,
                call,
                entry.Definition.SideEffects,
                entry.Definition.Semantic), ct)
            .ConfigureAwait(false);

        try
        {
            var preconditionFailure = await ValidateToolSemanticPreconditionsAsync(
                    entry.Definition,
                    call,
                    session,
                    bible,
                    ct)
                .ConfigureAwait(false);
            if (preconditionFailure != null)
            {
                await ledger.CompleteAsync(execution.Id, preconditionFailure, ct).ConfigureAwait(false);
                return preconditionFailure;
            }

            var result = await entry.Handler(call, session, bible, confirmed || IsAgentAutoProceedAuthorizedTool(name), ct).ConfigureAwait(false);
            result.RequiresConfirmation = false;
            if (!string.IsNullOrWhiteSpace(session.ActiveProjectId) &&
                !string.Equals(execution.ProjectId, session.ActiveProjectId, StringComparison.Ordinal))
            {
                await ledger.RebindProjectAsync(execution.Id, session.ActiveProjectId, ct).ConfigureAwait(false);
            }

            await ledger.CompleteAsync(execution.Id, result, ct).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var failureResult = BuildUnhandledToolExceptionResult(call, session, ex);
            await ledger.CompleteAsync(
                    execution.Id,
                    failureResult,
                    ct)
                .ConfigureAwait(false);

            if (IsClosedLoopLongTool(call.Name))
                return failureResult;

            throw;
        }
    }

    private async Task<AgentToolExecutionResult?> ValidateToolSemanticPreconditionsAsync(
        AgentToolDefinition definition,
        AgentToolCall call,
        AgentSession session,
        StoryBibleDocument bible,
        CancellationToken ct)
    {
        if (!definition.Semantic.InputArtifacts.Contains("chapter_plan_run", StringComparer.OrdinalIgnoreCase))
            return null;

        var resolver = _serviceProvider.GetRequiredService<IToolInputArtifactResolver>();
        var resolution = await resolver.ResolveAsync(
                new ToolInputArtifactResolutionRequest(
                    definition,
                    call,
                    session,
                    bible,
                    TryGetWorkspaceProjectId(),
                    LoadLatestStoryBibleIfWorkspaceAvailableAsync),
                ct)
            .ConfigureAwait(false);
        if (!resolution.BlocksExecution)
            return null;

        if (!string.Equals(resolution.FailureCode, "TOOL_INPUT_ARTIFACT_MISSING", StringComparison.OrdinalIgnoreCase))
        {
            return BuildBlockedInputArtifactResult(
                definition,
                session,
                resolution.RunId,
                resolution.MissingPrerequisite,
                resolution.FailureCode,
                resolution.Reason,
                resolution.InputArtifacts);
        }

        return BuildMissingInputArtifactResult(
            definition,
            session,
            resolution.MissingPrerequisite,
            resolution.Reason);
    }

    private string TryGetWorkspaceProjectId()
    {
        try
        {
            return _workspace.ProjectId;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private async Task<StoryBibleDocument> LoadLatestStoryBibleIfWorkspaceAvailableAsync(CancellationToken ct)
    {
        return await _workspace.Orchestrator.GetStoryBibleAsync(ct).ConfigureAwait(false);
    }

    private static AgentToolExecutionResult BuildMissingInputArtifactResult(
        AgentToolDefinition definition,
        AgentSession session,
        string missingArtifact,
        string reason)
    {
        var artifact = BuildArtifact(
            "tool_input_artifact_missing",
            missingArtifact,
            session.ActiveProjectId ?? string.Empty,
            session.ActiveRunId ?? string.Empty,
            reason,
            new[] { "PlanChapter", "QueryProjectStatus", "QueryNovelProductionState" });

        return new AgentToolExecutionResult
        {
            Success = false,
            RequiresConfirmation = false,
            Risk = definition.Risk,
            Message = $"工具 {definition.Name} 缺少输入产物 {missingArtifact}：{reason}",
            RunId = session.ActiveRunId ?? string.Empty,
            Phase = session.Phase,
            IsRepairable = true,
            RecommendedToolName = definition.Name,
            RecommendedArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            MissingPrerequisite = missingArtifact,
            Failure = new AgentToolFailure
            {
                Code = "TOOL_INPUT_ARTIFACT_MISSING",
                FailedStage = "semantic_precondition",
                Reason = reason,
                Recoverable = true,
                RecommendedAction = definition.Name,
                ArtifactIds = new[] { missingArtifact },
                ProducedArtifacts = new[] { ToProducedArtifact(artifact) },
                RecoverableActions = new[] { "PlanChapter", "QueryProjectStatus", "QueryNovelProductionState" },
                RequiresUserDecision = false
            },
            Artifact = artifact,
            Suggestions = new[] { "先规划章节", "查询项目状态", "查询生产状态" }
        };
    }

    private static AgentToolExecutionResult BuildBlockedInputArtifactResult(
        AgentToolDefinition definition,
        AgentSession session,
        string runId,
        string missingPrerequisite,
        string code,
        string reason,
        IReadOnlyList<ToolInputArtifactState> states)
    {
        var blockingState = states.FirstOrDefault(state => state.BlocksExecution);
        var artifact = BuildArtifact(
            "tool_input_artifact_blocked",
            blockingState?.ArtifactId ?? missingPrerequisite,
            session.ActiveProjectId ?? string.Empty,
            runId,
            reason,
            new[] { "QueryNovelProductionState", "QueryRevisionPlans", "CreateRevisionPlan" });

        return new AgentToolExecutionResult
        {
            Success = false,
            RequiresConfirmation = false,
            Risk = definition.Risk,
            Message = $"工具 {definition.Name} 输入产物不可用：{reason}",
            RunId = runId,
            Phase = session.Phase,
            IsRepairable = true,
            RecommendedToolName = definition.Name,
            RecommendedArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["runId"] = runId
            },
            MissingPrerequisite = missingPrerequisite,
            Failure = new AgentToolFailure
            {
                Code = code,
                FailedStage = "semantic_precondition",
                Reason = reason,
                Recoverable = true,
                RecommendedAction = definition.Name,
                ArtifactIds = states
                    .Select(state => state.ArtifactId)
                    .Where(static id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ProducedArtifacts = new[] { ToProducedArtifact(artifact) },
                RecoverableActions = states
                    .SelectMany(state => state.RecommendedActions)
                    .DefaultIfEmpty("QueryNovelProductionState")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                RequiresUserDecision = false
            },
            Data = new ToolInputArtifactResolution
            {
                BlocksExecution = true,
                MissingPrerequisite = missingPrerequisite,
                FailureCode = code,
                Reason = reason,
                RunId = runId,
                InputArtifacts = states
            },
            Artifact = artifact,
            Suggestions = new[] { "查询生产状态", "查询修订计划", "创建修订计划" }
        };
    }

    private static bool IsClosedLoopLongTool(string toolName) =>
        string.Equals(toolName, "ProduceChapter", StringComparison.OrdinalIgnoreCase);

    private static AgentToolExecutionResult BuildUnhandledToolExceptionResult(
        AgentToolCall call,
        AgentSession session,
        Exception ex)
    {
        var runId = ResolveRunId(call, session) ?? string.Empty;
        var toolName = call.Name?.Trim() ?? string.Empty;
        var knowledgeConflict = FindKnowledgeConflictBlockedException(ex);
        if (knowledgeConflict != null && IsClosedLoopLongTool(toolName))
            return BuildKnowledgeConflictBlockedToolResult(toolName, session, runId, knowledgeConflict);

        var isTransient = IsTransientToolException(ex);
        var stage = string.IsNullOrWhiteSpace(session.Phase) ? toolName : session.Phase;
        var reason = $"{ex.GetType().Name}: {ex.Message}";
        var nextActions = isTransient
            ? new[] { "稍后重试当前工具", "继续 ProduceChapter", "检查模型服务连接" }
            : new[] { "查看后台异常", "重新执行当前工具", "检查工具输入和模型配置" };
        var recommendedArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (IsClosedLoopLongTool(toolName) && !string.IsNullOrWhiteSpace(runId))
        {
            recommendedArguments["runId"] = runId;
            var revisionPlanId = FirstNonEmpty(
                Arg(call, "revisionPlanId"),
                Arg(call, "sourceRevisionPlanId"),
                Arg(call, "planId"));
            if (!string.IsNullOrWhiteSpace(revisionPlanId))
                recommendedArguments["revisionPlanId"] = revisionPlanId;
            recommendedArguments["commitPolicy"] = "auto_commit";
        }

        var failure = new AgentToolFailure
        {
            Code = isTransient ? "TRANSIENT_TOOL_EXCEPTION" : "UNHANDLED_TOOL_EXCEPTION",
            FailedStage = stage,
            Reason = reason,
            Recoverable = isTransient || IsClosedLoopLongTool(toolName),
            RecommendedAction = IsClosedLoopLongTool(toolName) ? toolName : string.Empty,
            RecoverableActions = nextActions,
            RequiresUserDecision = false
        };

        return new AgentToolExecutionResult
        {
            Success = false,
            RequiresConfirmation = false,
            Risk = IsClosedLoopLongTool(toolName) ? "High" : "Medium",
            Message = IsClosedLoopLongTool(toolName)
                ? $"工具 {toolName} 在阶段「{stage}」遇到异常：{reason}。已保留当前产物和执行记录，可以继续重试。"
                : ex.Message,
            RunId = runId,
            Phase = stage,
            IsRepairable = failure.Recoverable,
            RecommendedToolName = failure.RecommendedAction,
            RecommendedArguments = recommendedArguments,
            Failure = failure,
            Suggestions = nextActions
        };
    }

    private static KnowledgeConflictBlockedException? FindKnowledgeConflictBlockedException(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is KnowledgeConflictBlockedException blocked)
                return blocked;
        }

        return null;
    }

    private static AgentToolExecutionResult BuildKnowledgeConflictBlockedToolResult(
        string toolName,
        AgentSession session,
        string runId,
        KnowledgeConflictBlockedException ex)
    {
        var actions = new[] { "查看知识冲突报告", "查询项目知识绑定", "向用户确认" };
        var conflictIds = ex.ConflictIds.Count == 0
            ? ex.FirstConflictId
            : string.Join("、", ex.ConflictIds);
        var message = $"章节生产在「{NovelAgentProductionStages.Label(NovelAgentProductionStages.ContextPackage)}」阶段暂停：存在未解决的硬知识冲突（{conflictIds}）。{ex.Explanation} 需要先让用户决定保留、修订或废弃哪条知识设定，不能继续自动生成正文。";
        var failure = new AgentToolFailure
        {
            Code = "KNOWLEDGE_CONFLICT_BLOCKED",
            FailedStage = NovelAgentProductionStages.ContextPackage,
            Reason = ex.Explanation,
            Recoverable = true,
            RecommendedAction = "QueryProjectKnowledgeBindings",
            ArtifactIds = ex.ConflictIds.Count == 0 ? new[] { ex.FirstConflictId } : ex.ConflictIds,
            ProducedArtifacts = new[]
            {
                new AgentToolProducedArtifact
                {
                    ArtifactType = "knowledge_conflict_report",
                    ArtifactId = ex.FirstConflictId,
                    OutputKind = "blocking_report",
                    UserVisibleWhere = new[] { "工作流", "知识库冲突报告" },
                    Summary = ex.Explanation
                }
            },
            RecoverableActions = actions,
            RequiresUserDecision = true
        };

        return new AgentToolExecutionResult
        {
            Success = false,
            RequiresConfirmation = false,
            Risk = "High",
            Message = message,
            RunId = runId,
            Phase = NovelAgentProductionStages.ContextPackage,
            IsRepairable = false,
            RecommendedToolName = "QueryProjectKnowledgeBindings",
            RecommendedArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Failure = failure,
            Artifact = BuildArtifact(
                "knowledge_conflict_report",
                runId,
                session.ActiveProjectId,
                ex.FirstConflictId,
                message,
                actions),
            Suggestions = actions
        };
    }

    private static bool IsTransientToolException(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is System.Net.Http.HttpRequestException or TimeoutException or TaskCanceledException)
                return true;
        }

        return false;
    }

    private static string? ResolveRunId(AgentToolCall call, AgentSession session) =>
        call.Arguments.TryGetValue("runId", out var runId) && !string.IsNullOrWhiteSpace(runId)
            ? runId.Trim()
            : session.ActiveRunId;

    private async Task ThrowIfRuntimeRunCancelledAsync(
        AgentSession session,
        string stage,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.RuntimeRunId))
            return;

        using var scope = _serviceProvider.CreateScope();
        var runs = scope.ServiceProvider.GetService<IAgentRuntimeRunService>();
        if (runs == null)
            return;

        var run = await runs.TryGetAsync(session.RuntimeRunId, ct).ConfigureAwait(false);
        if (run?.CancelRequested == true)
            throw new AgentRuntimeRunCancelledException(session.RuntimeRunId, stage);
    }

    private async Task<RuntimeInterruptDrainResult> DrainPendingRuntimeInterruptsAtToolBoundaryAsync(
        AgentSession session,
        string runId,
        string stage,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.RuntimeRunId))
            return NoRuntimeInterrupts;

        using var scope = _serviceProvider.CreateScope();
        var interrupts = scope.ServiceProvider.GetService<IAgentInterruptService>();
        if (interrupts == null)
            return NoRuntimeInterrupts;

        var pending = await interrupts.GetPendingAsync(session.RuntimeRunId, ct).ConfigureAwait(false);
        if (pending.Count == 0)
            return NoRuntimeInterrupts;

        var consumed = new List<AgentRuntimeInterruptObservation>();
        foreach (var interrupt in pending)
        {
            if (string.IsNullOrWhiteSpace(interrupt.Message))
            {
                await interrupts.MarkRejectedAsync(interrupt.Id, "empty_message_at_tool_boundary", ct)
                    .ConfigureAwait(false);
                continue;
            }

            if (string.Equals(interrupt.Kind, "cancel", StringComparison.OrdinalIgnoreCase))
                continue;

            var consumedAt = DateTime.UtcNow;
            await interrupts.MarkConsumedAsync(interrupt.Id, new
                {
                    consumedBy = "agent_tool_stage_boundary",
                    productionRunId = runId,
                    stage
                }, ct)
                .ConfigureAwait(false);

            consumed.Add(new AgentRuntimeInterruptObservation
            {
                InterruptId = interrupt.Id,
                RuntimeRunId = interrupt.RuntimeRunId,
                Kind = interrupt.Kind,
                Message = interrupt.Message.Trim(),
                Priority = interrupt.Priority,
                ReceivedAt = interrupt.CreatedAt,
                ConsumedAt = consumedAt
            });
        }

        if (consumed.Count == 0)
            return NoRuntimeInterrupts;

        session.WorkingMemory.RuntimeInterrupts.AddRange(consumed);
        if (session.WorkingMemory.RuntimeInterrupts.Count > 16)
        {
            session.WorkingMemory.RuntimeInterrupts = session.WorkingMemory.RuntimeInterrupts
                .TakeLast(16)
                .ToList();
        }

        var directionChange = consumed
            .Where(item => string.Equals(item.Kind, "direction_change", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Priority)
            .ThenBy(item => item.ReceivedAt)
            .FirstOrDefault();
        var softCount = consumed.Count(item => string.Equals(item.Kind, "soft_requirement", StringComparison.OrdinalIgnoreCase));
        var message = directionChange != null
            ? $"在{FormatProductionStageLabel(stage)}前收到改方向要求，当前工具会停在安全边界。"
            : $"在{FormatProductionStageLabel(stage)}前读取到 {softCount} 条运行中补充要求。";

        await AppendChapterRuntimeProgressEventAsync(
                session,
                runId,
                stage,
                directionChange != null ? "interrupted" : "running",
                message,
                "runtime_interrupt",
                directionChange?.InterruptId ?? consumed[0].InterruptId,
                new
                {
                    runtimeRunId = session.RuntimeRunId,
                    productionRunId = runId,
                    stage,
                    interruptCount = consumed.Count,
                    softRequirementCount = softCount,
                    directionChangeInterruptId = directionChange?.InterruptId,
                    interruptKinds = consumed.Select(item => item.Kind).ToArray()
                },
                ct)
            .ConfigureAwait(false);

        return new RuntimeInterruptDrainResult(consumed, directionChange);
    }

    private AgentToolExecutionResult? BuildDirectionChangeBoundaryResult(
        AgentSession session,
        string runId,
        string stage,
        RuntimeInterruptDrainResult drain)
    {
        if (drain.DirectionChange == null)
            return null;

        var nextHints = new[] { "根据新方向重建修订计划", "重新规划本章", "查看当前生产状态" };
        var message = $"已在{FormatProductionStageLabel(stage)}前收到你的改方向要求，当前章节生产停在安全边界：{TrimBody(drain.DirectionChange.Message, 120)}";
        var artifact = BuildArtifact(
            "runtime_direction_change_interrupt",
            runId,
            session.ActiveProjectId,
            drain.DirectionChange.InterruptId,
            message,
            nextHints);

        return new AgentToolExecutionResult
        {
            Success = false,
            RequiresConfirmation = false,
            Risk = "High",
            Message = message,
            RunId = runId,
            Phase = "runtime_direction_change",
            IsRepairable = true,
            RecommendedToolName = "QueryNovelProductionState",
            RecommendedArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["runId"] = runId,
                ["includeEvents"] = "true"
            },
            Failure = new AgentToolFailure
            {
                Code = "RUNTIME_DIRECTION_CHANGE",
                FailedStage = stage,
                Reason = message,
                Recoverable = true,
                RecommendedAction = "QueryNovelProductionState",
                ArtifactIds = new[] { artifact.ArtifactId },
                ProducedArtifacts = new[] { ToProducedArtifact(artifact) },
                RecoverableActions = nextHints,
                RequiresUserDecision = false
            },
            Data = new
            {
                interrupt = drain.DirectionChange,
                consumedInterrupts = drain.Consumed
            },
            Artifact = artifact,
            Suggestions = nextHints
        };
    }

    private Dictionary<string, AgentToolEntry> BuildEntries()
    {
        var entries = new[]
        {
            Entry("tool_search", "meta", "Low", false, new[] { "query", "intent", "context", "phase", "includeAll", "limit" }, "全局工具目录与语义检索入口。query/intent/context 用于检索和排序工具语义；phase 只是可选 hint，不会限制可用工具集合；includeAll=true 可返回完整目录。", Effects(toolCache: true, sqliteSnapshot: true, sqliteReads: new[] { "agent_tool_search_snapshots" }, sqliteWrites: new[] { "agent_tool_search_snapshots" }), (call, session, _, _, ct) => ToolSearchAsync(call, session, ct)),
            Entry("QueryWorkspaceState", "workspace", "Low", false, Array.Empty<string>(), "只读查询当前用户可见的工作台真实状态：小说书城项目、知识库条目、创作工作流 Run、当前会话绑定情况、记忆读取审计和记忆提升审计。不会创建、绑定或切换项目。", Effects(readOnly: true, sqliteReads: new[] { "novel_projects", "knowledge_base", "agent_runs", "volumes", "chapters", "agent_sessions", "agent_memory_reads", "agent_memory_promotions" }), (call, session, _, _, ct) => QueryWorkspaceStateAsync(session, ct)),
            Entry("ResolveNovelProject", "project", "Low", false, new[] { "mode", "projectId", "projectTitle", "title", "genre", "seed", "foundationBriefReady" }, "由 Agent 决策绑定已有小说或创建新小说。mode 可选 bind_existing/create_new/auto；绑定不创建新书，创建会切换当前会话上下文。foundationBriefReady 由模型显式判断：true 表示当前结构化创作简报足够进入故事地基候选，未传或 false 则保持待补地基。", Effects(memory: new[] { "session", "project" }, sqliteReads: new[] { "novel_projects", "agent_sessions" }, sqliteWrites: new[] { "novel_projects", "agent_sessions" }), (call, session, _, _, ct) => ResolveNovelProjectAsync(call, session, ct)),
            Entry("ProcessKnowledgeFile", "knowledge", "Medium", false, new[] { "taskId" }, "处理已上传的知识文件，自动提取创意写作知识条目。支持结构化文档和创意素材。该操作只处理用户已上传的待处理任务，并把提取结果绑定到当前项目，不覆盖正文、不删除数据。", Effects(memory: new[] { "project" }, sqliteReads: new[] { "knowledge_processing_tasks", "content_documents", "knowledge_base", "project_knowledge_usages" }, sqliteWrites: new[] { "knowledge_base", "knowledge_processing_tasks", "content_documents", "project_knowledge_usages" }, vector: new[] { "knowledge" }), (call, session, _, _, ct) => ProcessKnowledgeFileAsync(call, session, ct)),
            Entry("QueryProjectStatus", "blackboard", "Low", false, Array.Empty<string>(), "读取 MissionBlackboard、Story Bible、素材、账本、当前可操作 Run 状态。", Effects(readOnly: true), (call, session, bible, _, ct) => QueryProjectStatusAsync(session, bible, ct)),
            Entry("QueryProjectContent", "content", "Low", false, new[] { "chapterId", "chapterNumber", "volumeNumber", "versionId", "versionNumber", "includeBody", "includeFacts" }, "只读读取当前项目的卷、章、正文、摘要、关键连续性事实，以及指定章节版本的历史正文、门禁、AgentReview 和 FactSnapshot。用于回答“第几章写了什么、某版本正文是什么、所属卷、正文开头、关键事实、历史评审”等内容问题，不改变工作流或书城。", Effects(readOnly: true, sqliteReads: new[] { "novel_projects", "volumes", "volume_arcs", "chapters", "chapter_versions", "content_documents", "content_chunks", "project_fact_snapshots", "generation_gate_reports", "agent_reviews" }), (call, session, bible, _, ct) => QueryProjectContentAsync(call, session, bible, ct)),
            Entry("QueryChapterVersions", "content", "Low", false, new[] { "chapterId", "chapterNumber" }, "只读读取当前项目某章的历史版本、当前版本标记、Run、生产包、内核版本、门禁报告、AgentReview 和 rebuilt package 替代旧包关系。用于回答“这章有哪些版本、哪个是当前版本、能否回滚、重写来自哪里”等问题；不切换版本、不回滚、不修改书城。", Effects(readOnly: true, sqliteReads: new[] { "chapters", "chapter_versions", "content_documents", "tianming_packages", "production_events" }), (call, session, _, _, ct) => QueryChapterVersionsAsync(call, session, ct)),
            Entry("CompareChapterVersions", "content", "Low", false, new[] { "chapterId", "chapterNumber", "leftVersionId", "rightVersionId" }, "只读对比同一章节两个 ChapterVersion 的正文段落差异、字数变化、当前版本标记、生产包 lineage，并返回目标版本采用的 CreativeIntent、RevisionPlan、AgentReview 对照证据。用于回答“两个版本差在哪里、回滚会丢哪些内容、修订是否落实、这次改写是否符合用户创意”等问题；不切换版本、不回滚、不修改书城。", Effects(readOnly: true, sqliteReads: new[] { "chapters", "chapter_versions", "content_documents", "content_chunks", "tianming_packages", "creative_intents", "revision_plans" }), (call, session, _, _, ct) => CompareChapterVersionsAsync(call, session, ct)),
            Entry("QueryNovelProductionState", "production", "Low", false, new[] { "runId", "chapterId", "chapterNumber", "projectId", "includeEvents" }, "只读查询小说生产状态：Agent Runtime run、工具执行、天命生产包、生产事件、runtime 事件、outbox、LLM 事实沉淀快照、记忆读取审计和记忆提升审计。用于回答“现在是否在执行、卡在哪、哪个包/门禁/索引还没完成、上一章沉淀了哪些事实、下一章必须承接什么、刚才读了哪些记忆、哪些会话偏好被沉淀成项目约束”。", Effects(readOnly: true, sqliteReads: new[] { "agent_runtime_runs", "agent_tool_executions", "tianming_packages", "production_events", "agent_runtime_events", "outbox_events", "project_fact_snapshots", "agent_memory_reads", "agent_memory_promotions" }), (call, session, _, _, ct) => QueryNovelProductionStateAsync(call, session, ct)),
            Entry("QueryProductionOutbox", "production", "Low", false, new[] { "projectId", "status", "limit" }, "只读查询当前项目后台 outbox：索引、事实沉淀、提交后 metadata finalizer 等后台任务。用于判断后台任务是否 pending、retryable_failed、failed 或 completed，不修改任何任务。", Effects(readOnly: true, sqliteReads: new[] { "outbox_events" }), (call, session, _, _, ct) => QueryProductionOutboxAsync(call, session, ct)),
            Entry("RetryProductionOutbox", "production", "Medium", false, new[] { "eventId", "projectId" }, "重试当前项目内指定后台 outbox。用于恢复 QueryNovelProductionState 或 QueryProductionOutbox 暴露的 retryable_failed/failed/pending 后台任务；只重置并触发 outbox dispatcher，不直接改正文、不绕过门禁。", Effects(sqliteReads: new[] { "outbox_events", "novel_projects" }, sqliteWrites: new[] { "outbox_events", "production_events" }, vector: new[] { "chapter", "knowledge", "memory" }), (call, session, _, _, ct) => RetryProductionOutboxAsync(call, session, ct)),
            Entry("QueryProjectKnowledgeBindings", "knowledge", "Low", false, Array.Empty<string>(), "只读查询当前项目已绑定/引用的知识条目、约束类型、标签、来源会话、硬事实摘要、Story Bible Canon/CanonLedger 状态和知识冲突报告。用于判断项目生产包、RAG 和门禁会使用哪些知识与硬规则；不会修改知识库、Story Bible 或项目。", Effects(readOnly: true, sqliteReads: new[] { "knowledge_base", "project_knowledge_usages", "knowledge_conflict_reports", "knowledge_classifications" }), (call, session, _, _, ct) => QueryProjectKnowledgeBindingsAsync(session, ct)),
            Entry("AttachKnowledgeToProject", "knowledge", "Medium", false, new[] { "knowledgeId", "status" }, "把 Agent 已决定采用的知识库条目绑定到当前小说项目。status 可为 imported 或 referenced：imported 表示纳入项目知识清单，referenced 表示后续生产包、RAG、门禁和事实快照应把它作为项目引用知识考虑。该工具不写正文、不替用户解决冲突。", Effects(memory: new[] { "project" }, sqliteReads: new[] { "knowledge_base", "project_knowledge_usages" }, sqliteWrites: new[] { "project_knowledge_usages", "agent_memory_events" }), (call, session, _, _, ct) => AttachKnowledgeToProjectAsync(call, session, ct), dedupPolicy: "per_turn"),
            Entry("ClassifyProjectKnowledge", "knowledge", "Medium", false, new[] { "knowledgeId" }, "让大模型理解某条知识在当前项目生产结构中的用途，并写入 knowledge_classifications，同时覆盖当前项目的 project_knowledge_usages 语义字段。用于把知识库条目转成天命生产包、门禁和事实快照可使用的结构化约束；不会写小说正文，不会影响其他项目。", Effects(sqliteReads: new[] { "knowledge_base", "project_knowledge_usages", "knowledge_classifications" }, sqliteWrites: new[] { "knowledge_classifications", "project_knowledge_usages" }), (call, session, _, _, ct) => ClassifyProjectKnowledgeAsync(call, session, ct)),
            Entry("DetectKnowledgeConflicts", "knowledge", "Medium", false, new[] { "knowledgeId" }, "让大模型审阅某条项目知识与当前项目已绑定知识之间的冲突，并写入 knowledge_conflict_reports。用于发现硬约束冲突、设定矛盾和生产入口冲突；只生成 ConflictReport，不自动修改知识、Story Bible 或正文。", Effects(sqliteReads: new[] { "knowledge_base", "project_knowledge_usages", "knowledge_classifications", "knowledge_conflict_reports" }, sqliteWrites: new[] { "knowledge_conflict_reports" }), (call, session, _, _, ct) => DetectKnowledgeConflictsAsync(call, session, ct)),
            Entry("ResolveKnowledgeConflict", "knowledge", "Medium", false, new[] { "conflictId", "decision", "note" }, "把 Agent 和用户已经确认的知识冲突处理决定写回 knowledge_conflict_reports。decision 支持 resolved/rejected/superseded；该工具会发出生产恢复信号，提示章节生产包可重建，但不自动改写知识条目、Story Bible 或正文。", Effects(sqliteReads: new[] { "knowledge_conflict_reports", "project_knowledge_usages" }, sqliteWrites: new[] { "knowledge_conflict_reports", "production_events", "agent_runtime_events" }), (call, session, _, _, ct) => ResolveKnowledgeConflictAsync(call, session, ct)),
            Entry("CreateCreativeIntent", "creative", "Medium", false, new[] { "rawContent", "normalizedIntent", "source", "targetScope", "targetChapterId", "targetVolumeId", "targetCharacterName", "impactLevel", "requiresConfirmation", "conflictStatus", "metadataJson" }, "创建创意收件箱条目。用于把用户补充想法、Agent 建议、工作流评论或知识库灵感结构化沉淀，不直接改正文。", Effects(memory: new[] { "session", "project" }, sqliteReads: new[] { "creative_intents", "chapters", "volume_arcs" }, sqliteWrites: new[] { "creative_intents" }), (call, session, _, _, ct) => CreateCreativeIntentAsync(call, session, ct)),
            Entry("DecideCreativeIntent", "creative", "Medium", false, new[] { "intentId", "status", "decisionReason", "conflictStatus", "markExecuted" }, "更新创意意图决策状态：accepted/rejected/replaced/executed。用于 Agent 自主采纳、拒绝或标记已执行创意，后续生产包只读取 accepted/executed 创意。", Effects(memory: new[] { "project" }, sqliteReads: new[] { "creative_intents" }, sqliteWrites: new[] { "creative_intents" }), (call, session, _, _, ct) => DecideCreativeIntentAsync(call, session, ct)),
            Entry("QueryCreativeIntents", "creative", "Low", false, new[] { "status", "targetChapterId", "limit" }, "只读查询当前项目创意收件箱，展示候选、已采纳、已拒绝、已执行创意及其目标范围。", Effects(readOnly: true, sqliteReads: new[] { "creative_intents" }), (call, session, _, _, ct) => QueryCreativeIntentsAsync(call, session, ct)),
            Entry("CreateRevisionPlan", "creative", "Medium", false, new[] { "source", "planType", "targetScope", "targetChapterId", "targetVolumeId", "creativeIntentId", "knowledgeConflictReportId", "status", "requirementsJson", "continuityRequirementsJson", "impactAnalysisJson", "affectedChapterIdsJson", "invalidatedPackageIdsJson", "riskLevel", "recommendation" }, "创建结构化修订计划，把已采纳创意、知识冲突处理决定或章节审阅意见转成天命生产内核可执行的 RevisionPlan。该工具只写修订计划和影响分析，不直接改正文或 Story Bible。", Effects(memory: new[] { "project" }, sqliteReads: new[] { "revision_plans", "creative_intents", "knowledge_conflict_reports", "chapters", "tianming_packages" }, sqliteWrites: new[] { "revision_plans" }), (call, session, _, _, ct) => CreateRevisionPlanAsync(call, session, ct)),
            Entry("QueryRevisionPlans", "creative", "Low", false, new[] { "status", "targetChapterId", "limit" }, "只读查询当前项目修订计划，展示计划状态、目标章节、影响分析、失效包和推荐下一步。", Effects(readOnly: true, sqliteReads: new[] { "revision_plans", "creative_intents", "knowledge_conflict_reports", "tianming_packages" }), (call, session, _, _, ct) => QueryRevisionPlansAsync(call, session, ct)),
            Entry("InvalidateAffectedPackages", "creative", "Medium", false, new[] { "revisionPlanId" }, "执行修订计划的影响失效：读取 RevisionPlan 中的 affectedChapterIds 和 invalidatedPackageIds，把当前项目内受影响的旧 TianmingPackage 标记为 stale，并写入生产事件和工作流进度事件。该工具不生成正文、不提交书城，只为后续重建生产包做准备。", Effects(memory: new[] { "project", "execution" }, sqliteReads: new[] { "revision_plans", "tianming_packages" }, sqliteWrites: new[] { "revision_plans", "tianming_packages", "production_events", "agent_runtime_events" }), (call, session, _, _, ct) => InvalidateAffectedPackagesAsync(call, session, ct)),
            Entry("RunBookValidation", "review", "Medium", false, new[] { "startChapterNumber", "endChapterNumber", "includeBodyPreview" }, "只读执行整书或章节群生产验收：读取书城章节、正文、ChapterVersion、FactSnapshot 和相邻章连续性事实，报告缺章、未提交、正文缺失、事实快照缺失、主角/承接断裂等问题。该工具不修改正文、不自动修订。", Effects(readOnly: true, sqliteReads: new[] { "novel_projects", "chapters", "chapter_versions", "content_documents", "content_chunks", "project_fact_snapshots" }), (call, session, _, _, ct) => RunBookValidationAsync(call, session, ct)),
            Entry("AuditCommittedChapter", "review", "Medium", false, new[] { "chapterId", "chapterNumber", "focus" }, "只读审查已提交章节正文，复用连续性包和知识库硬事实门禁，返回问题报告但不覆盖书城正文。", Effects(readOnly: true, sqliteReads: new[] { "chapters", "content_documents", "content_chunks", "story_bible", "project_fact_snapshots", "knowledge_base" }), (call, session, bible, _, ct) => AuditCommittedChapterAsync(call, session, bible, ct)),
            Entry("ReviseCommittedChapter", "commit", "High", true, new[] { "chapterId", "chapterNumber", "revisionGoal", "revisionPlanId", "auditRunId" }, "修订已提交章节正文：必须绑定当前项目的 RevisionPlan，读取书城正文和连续性包，让模型重写完整章节，通过硬门禁后才覆盖书城正文。", Effects(memory: new[] { "project", "execution" }, sqliteReads: new[] { "chapters", "content_documents", "content_chunks", "agent_runs", "revision_plans", "story_bible", "project_fact_snapshots", "knowledge_base" }, sqliteWrites: new[] { "chapters", "content_documents", "agent_runs", "revision_plans" }, vector: new[] { "chapter" }), (call, session, bible, confirmed, ct) => ReviseCommittedChapterAsync(call, session, bible, confirmed, ct)),
            Entry("RollbackChapterVersion", "commit", "High", true, new[] { "chapterId", "chapterNumber", "targetVersionId", "reason", "runId" }, "回滚已提交章节到指定 ChapterVersion：切换书城当前正文，恢复目标 ContentDocument，失效当前章及下游章节旧生产包，写入生产事件并排队重建索引和事实快照。必须由用户确认后执行。", Effects(memory: new[] { "project", "execution" }, sqliteReads: new[] { "chapters", "content_documents", "chapter_versions", "tianming_packages", "project_fact_snapshots" }, sqliteWrites: new[] { "chapters", "content_documents", "chapter_versions", "tianming_packages", "production_events", "outbox_events" }, vector: new[] { "chapter" }), (call, session, _, confirmed, ct) => RollbackChapterVersionAsync(call, session, confirmed, ct)),
            Entry("SearchCreativeKnowledge", "rag", "Low", false, new[] { "query" }, "检索创意知识库、类型原则、反套路策略和项目记忆。", Effects(memory: new[] { "execution" }, sqliteReads: new[] { "knowledge_base", "project_knowledge_usages", "content_chunks", "content_vector_points" }, vector: new[] { "knowledge" }), (call, session, _, _, ct) => SearchCreativeKnowledgeAsync(call, session, ct), dedupPolicy: "per_turn"),
            Entry("PlanStoryFoundation", "planning", "Low", false, new[] { "userSeed", "genre", "subGenre", "candidateDirections", "forbiddenDirections" }, "生成故事地基和大框架候选，不直接固化。candidateDirections 必须由大模型根据用户意图给出；工具只按这些结构化方向展开，不从用户原话关键词推断候选。", Effects(memory: new[] { "session", "execution" }, sqliteReads: new[] { "novel_projects", "agent_sessions", "knowledge_base", "project_knowledge_usages" }, sqliteWrites: new[] { "agent_runs", "content_documents" }), (call, session, _, _, ct) => PlanStoryFoundationAsync(call, session, ct)),
            Entry("CommitStoryFoundation", "commit", "High", true, new[] { "runId", "selectedMacroCandidateIndex", "selectedMacroCandidateId" }, "把候选故事地基固化到 Story Bible。", Effects(memory: new[] { "project", "execution" }, sqliteReads: new[] { "agent_runs", "content_documents", "story_constitutions" }, sqliteWrites: new[] { "story_constitutions", "agent_runs", "content_documents" }, vector: new[] { "story_bible" }), (call, session, _, confirmed, ct) => CommitStoryFoundationAsync(call, session, confirmed, ct)),
            Entry("PlanVolumeArc", "planning", "Low", false, new[] { "creativeBrief", "volumeId", "volumeTitle", "sourceTurnId", "expectedChapterCount", "startChapterId", "endChapterId", "candidateDirections", "forbiddenDirections" }, "规划卷级弧线，不直接固化。expectedChapterCount 表示每卷目标章节数；creativeBrief 和 candidateDirections 必须由大模型根据上下文整理；工具只按这些结构化方向生成卷节拍，不从用户原话或 Story Bible 关键词推断。", Effects(memory: new[] { "session", "execution" }, sqliteReads: new[] { "story_constitutions", "volume_arcs", "agent_runs", "content_documents", "knowledge_base", "project_knowledge_usages" }, sqliteWrites: new[] { "agent_runs", "content_documents" }), (call, session, bible, _, ct) => PlanVolumeArcAsync(call, session, bible, ct)),
            Entry("CommitVolumeArc", "commit", "High", true, new[] { "runId", "overwrite" }, "把卷规划提交到 Story Bible。若是在修正已有卷规划，模型可明确传 overwrite=true 覆盖同一卷的旧规划。", Effects(memory: new[] { "project", "execution" }, sqliteReads: new[] { "agent_runs", "content_documents", "volume_arcs" }, sqliteWrites: new[] { "volume_arcs", "agent_runs", "content_documents" }, vector: new[] { "story_bible" }), (call, session, _, confirmed, ct) => CommitVolumeArcAsync(call, session, confirmed, ct)),
            Entry("PlanChapter", "planning", "Medium", false, new[] { "creativeBrief", "chapterId", "sourceTurnId", "candidateDirections", "forbiddenDirections" }, "检索项目状态和知识库，生成章节候选。creativeBrief 和 candidateDirections 必须由大模型根据上下文整理；工具只按这些结构化方向生成候选，不从用户原话或 creativeBrief 关键词推断桥段。", Effects(memory: new[] { "session", "execution" }, sqliteReads: new[] { "story_constitutions", "volume_arcs", "chapters", "project_fact_snapshots", "creative_intents", "revision_plans", "knowledge_base", "project_knowledge_usages", "agent_runs", "content_documents" }, sqliteWrites: new[] { "agent_runs", "content_documents" }, vector: new[] { "knowledge", "chapter_context" }), (call, session, bible, _, ct) => PlanChapterAsync(call, session, bible, ct)),
            Entry("SelectChapterCandidate", "planning", "Medium", false, new[] { "runId", "candidateTitles", "candidateIndex", "selectedCandidateIndex" }, "选择章节候选，决定后续正文生成方向。可传 candidateIndex/selectedCandidateIndex 选择第几个候选，也可在 candidateTitles 中传完整标题或“【2】...”这类序号文本。", Effects(memory: new[] { "session", "execution" }, sqliteReads: new[] { "agent_runs", "content_documents" }, sqliteWrites: new[] { "agent_runs" }), (call, session, _, confirmed, ct) => SelectChapterCandidateAsync(call, session, confirmed, ct)),
            Entry("ProduceChapter", "writing", "High", true, new[] { "runId", "revisionPlanId", "maxRepairAttempts", "commitPolicy" }, "章节生产闭环工具。基于已规划章节 Run 自动串起上下文包、正文生成、硬门禁、必要修复、Agent 质量评审，并按 commitPolicy 决定是否提交书城：auto_commit 自动提交，draft_only 只生成并校验草稿，require_user_review 等待用户审阅后再提交；revisionPlanId 用于承接 QueryNovelProductionState 返回的过期生产包修订计划；过程写入 production events，失败时返回失败阶段、已产物和可继续动作。", Effects(memory: new[] { "project", "author", "execution" }, sqliteReads: new[] { "agent_runs", "content_documents", "story_bible", "volume_arcs", "chapters", "project_fact_snapshots", "creative_intents", "revision_plans", "knowledge_base", "project_knowledge_usages", "outbox_events" }, sqliteWrites: new[] { "tianming_packages", "production_events", "chapter_versions", "project_fact_snapshots", "chapters", "content_documents", "agent_runs" }, vector: new[] { "chapter", "chapter_context" }), (call, session, _, confirmed, ct) => ProduceChapterAsync(call, session, confirmed, ct)),
            Entry("RefreshProjectIndexes", "maintenance", "Medium", false, new[] { "runId" }, "刷新章节摘要、事实快照、长距离 RAG 和索引标记。", Effects(memory: new[] { "project", "execution" }, sqliteReads: new[] { "agent_runs", "chapters", "content_documents", "content_chunks", "content_vector_points", "project_fact_snapshots" }, sqliteWrites: new[] { "agent_runs", "content_chunks", "content_vector_points" }, vector: new[] { "chapter", "story_bible" }), (call, session, _, _, ct) => RefreshProjectIndexesAsync(call, session, ct)),
            Entry("AnalyzeDependencyImpact", "maintenance", "Low", false, new[] { "runId" }, "分析结构化设定改动对卷、章节、蓝图和校验摘要的影响。", Effects(memory: new[] { "execution" }, sqliteReads: new[] { "agent_runs", "story_constitutions", "volume_arcs", "chapters", "tianming_packages", "revision_plans" }, sqliteWrites: new[] { "agent_runs" }), (call, session, _, _, ct) => AnalyzeDependencyImpactAsync(call, session, ct)),
            Entry("ReviewChapter", "review", "Medium", false, new[] { "runId" }, "复盘已生成章节并提出账本沉淀。", Effects(memory: new[] { "project", "author", "execution" }, sqliteReads: new[] { "agent_runs", "content_documents", "chapters", "project_fact_snapshots" }, sqliteWrites: new[] { "agent_runs", "agent_memory_events" }), (call, session, _, _, ct) => ReviewChapterAsync(call, session, ct)),
            Entry("AggregateDesignRules", "knowledge", "Medium", false, Array.Empty<string>(), "把当前项目所有已分类知识聚合为结构化设计规则（WorldCoreRule/CharacterPsycheRule/ConflictEngine/WritingTech/ReaderPromise/StyleGuide），按 ConstraintLevel 分组并写入 project_design_rules。生产包构建时会自动加载这些规则进入 ChapterContextPackage。不修改知识库本体或 Story Bible。", Effects(sqliteReads: new[] { "knowledge_classifications", "knowledge_base", "project_design_rules" }, sqliteWrites: new[] { "project_design_rules" }), (call, session, _, _, ct) => AggregateDesignRulesAsync(call, session, ct)),
            Entry("CreateChapterBlueprint", "planning", "Medium", false, new[] { "chapterId", "chapterIndex", "title", "intent", "keyEvents", "characters", "conflictNote", "endingNote", "requiredKnowledgeIds", "appliedDesignRuleIds", "dependencyChapterIds", "targetWordCount" }, "为指定章节创建或更新结构化蓝图（持久化到 chapter_blueprints 表）。蓝图包含章节意图、关键事件、人物、冲突点、结尾状态、依赖章节、所需知识和适用设计规则；同一章节已有 active 蓝图会被 supersede 形成版本链。该工具只写蓝图，不生成正文。", Effects(memory: new[] { "project" }, sqliteReads: new[] { "chapter_blueprints", "chapters" }, sqliteWrites: new[] { "chapter_blueprints" }), (call, session, _, _, ct) => CreateChapterBlueprintAsync(call, session, ct)),
            Entry("QueryChapterBlueprints", "planning", "Low", false, new[] { "chapterId", "includeHistory" }, "只读查询当前项目的章节蓝图：默认返回每个章节的 active 蓝图，传 chapterId+includeHistory=true 可查看某章节蓝图版本历史。用于了解\"哪些章节已有蓝图、当前版本是什么、谁需要重做蓝图\"。", Effects(readOnly: true, sqliteReads: new[] { "chapter_blueprints" }), (call, session, _, _, ct) => QueryChapterBlueprintsAsync(call, session, ct)),
        };
        return entries.ToDictionary(e => e.Definition.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsAgentAutoProceedAuthorizedTool(string name) =>
        name is "CommitStoryFoundation" or "CommitVolumeArc" or
            "ProduceChapter";

    private async Task<AgentToolExecutionResult> QueryWorkspaceStateAsync(AgentSession session, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var queryService = scope.ServiceProvider.GetRequiredService<IWorkspaceStateQueryService>();
        var state = await queryService.QueryAsync(
                new WorkspaceStateQueryRequest(
                    UserId: session.UserId,
                    SessionId: session.SessionId,
                    ActiveProjectId: session.ActiveProjectId ?? string.Empty,
                    Phase: session.Phase,
                    AuthorDisplayName: session.WorkingMemory.AuthorMemory?.DisplayName ?? string.Empty,
                    StyleLikeCount: session.WorkingMemory.AuthorMemory?.StyleLikes.Count ?? 0,
                    StyleDislikeCount: session.WorkingMemory.AuthorMemory?.StyleDislikes.Count ?? 0,
                    GenreHabitCount: session.WorkingMemory.AuthorMemory?.GenreHabits.Count ?? 0),
                ct)
            .ConfigureAwait(false);

        return new AgentToolExecutionResult
        {
            Success = true,
            Message = BuildWorkspaceStateMessage(state),
            Phase = session.Phase,
            Data = state,
            Artifact = BuildArtifact(
                "workspace_state",
                "workspace",
                session.ActiveProjectId ?? string.Empty,
                string.Empty,
                $"已读取小说书城、知识库和创作工作流状态；小说书城当前可见项目共 {state.ProjectTotalCount} 本。",
                state.VisibleProjects.Select(p => $"{p.Title} ({p.Id})").ToArray()),
            Suggestions = new[]
            {
                "根据书城状态决定是否绑定已有项目",
                "查看知识库条目",
                "继续当前创作工作流"
            }
        };
    }

    private static string BuildWorkspaceStateMessage(AgentWorkspaceState state)
    {
        var projectCountLine = $"小说书城当前可见项目共 {state.ProjectTotalCount} 本；本次快照列出最近 {state.ProjectPreviewCount} 本。";
        var projectLines = state.VisibleProjects.Count == 0
            ? projectCountLine
            : projectCountLine + "\n" + string.Join("\n", state.VisibleProjects.Select(p =>
                $"• {p.Title}（{p.Status}，owner={p.OwnerUsername}/{p.OwnerUserId}，chapters={p.ChapterCount}，committed={p.CommittedChapterCount}，owned_by_current_user={p.IsOwnedByCurrentUser}）"));
        var knowledgeTypes = state.KnowledgeBase.CountsByType.Count == 0
            ? "无分类"
            : string.Join("，", state.KnowledgeBase.CountsByType.Select(x => $"{x.EntryType}:{x.Count}"));
        var knowledgeUsageLine = state.KnowledgeBase.RecentlyUsedBindings.Count == 0
            ? "最近实际用于章节的知识：暂无。"
            : "最近实际用于章节的知识：\n" + string.Join("\n", state.KnowledgeBase.RecentlyUsedBindings.Take(6).Select(binding =>
                $"• {binding.Title}（{binding.EntryType}/{binding.ConstraintLevel}，project={binding.ProjectTitle}/{binding.ProjectId}，used_chapters={binding.UsedByChapters.Count}）"));
        var knowledgeEvidenceLine = state.KnowledgeBase.RecentConstraintEvidence.Count == 0
            ? "最近知识约束证据：暂无。"
            : "最近知识约束证据：\n" + string.Join("\n", state.KnowledgeBase.RecentConstraintEvidence.Take(6).Select(evidence =>
                $"• {evidence.Title}（{evidence.EntryType}/{evidence.ConstraintLevel}/{evidence.PackagePolicy}，project={evidence.ProjectTitle}/{evidence.ProjectId}，chapter={evidence.ChapterId}，evidence={evidence.EvidenceStatus}，gate={evidence.GateStatus}）"));
        var knowledgeConflictLine = state.KnowledgeBase.RecentConflictReports.Count == 0
            ? "知识冲突报告：暂无。"
            : "知识冲突报告：\n" + string.Join("\n", state.KnowledgeBase.RecentConflictReports.Take(6).Select(report =>
            {
                var decision = report.RequiresUserDecision ? "requires_user_decision=true" : "requires_user_decision=false";
                var resolution = string.IsNullOrWhiteSpace(report.ResolutionNote) ? string.Empty : $"，resolution={TrimBody(report.ResolutionNote, 60)}";
                return $"• {report.ConflictId}（{report.Status}/{report.Severity}，project={report.ProjectTitle}/{report.ProjectId}，{decision}{resolution}）：{TrimBody(report.Explanation, 100)}";
            }));
        var workflowLines = state.Workflow.RecentRuns.Count == 0
            ? "创作工作流：最近无 Run。"
            : "创作工作流最近 Run：\n" + string.Join("\n", state.Workflow.RecentRuns.Select(r =>
                $"• {r.RunType}/{r.Status} project={r.ProjectId} run={r.Id}"));
        var memoryReadLine = state.Memory.RecentReads.Count == 0
            ? "记忆读取审计：暂无。"
            : "记忆读取审计：\n" + string.Join("\n", state.Memory.RecentReads.Take(6).Select(read =>
            {
                var keys = read.MemoryKeys.Count == 0 ? "无key" : string.Join("、", read.MemoryKeys.Take(4));
                var run = string.IsNullOrWhiteSpace(read.RunId) ? string.Empty : $"，run={read.RunId}";
                return $"• {read.MemoryScope}/{read.Consumer}（{read.Id}{run}，keys={keys}）";
            }));
        var memoryPromotionLine = state.Memory.RecentPromotions.Count == 0
            ? "记忆提升审计：暂无。"
            : "记忆提升审计：\n" + string.Join("\n", state.Memory.RecentPromotions.Take(6).Select(promotion =>
            {
                var run = string.IsNullOrWhiteSpace(promotion.RunId) ? string.Empty : $"，run={promotion.RunId}";
                return $"• {promotion.SourceMemoryKey} -> {promotion.TargetMemoryKey}（{promotion.PromotionReason}{run}，{promotion.Id}）";
            }));
        var authorLine = string.IsNullOrWhiteSpace(state.AuthorProfile.DisplayName)
            ? "作者记忆：未记录用户称呼。"
            : $"作者记忆：作者称呼：{state.AuthorProfile.DisplayName}；风格喜好 {state.AuthorProfile.StyleLikeCount} 条，反感风格 {state.AuthorProfile.StyleDislikeCount} 条，题材习惯 {state.AuthorProfile.GenreHabitCount} 条。";

        return string.Join("\n\n", new[]
        {
            projectLines,
            $"知识库：共 {state.KnowledgeBase.TotalCount} 条；分类：{knowledgeTypes}。",
            knowledgeUsageLine,
            knowledgeEvidenceLine,
            knowledgeConflictLine,
            workflowLines,
            memoryReadLine,
            memoryPromotionLine,
            authorLine,
            $"当前会话：activeProjectId={state.CurrentSession.ActiveProjectId}，phase={state.CurrentSession.Phase}，hasActiveProject={state.CurrentSession.HasActiveProject}",
            string.Join("\n", state.Notes)
        });
    }

    private static AgentToolEntry Entry(
        string name,
        string category,
        string risk,
        bool requiresConfirmation,
        IReadOnlyList<string> args,
        string description,
        AgentToolSideEffectSpec sideEffects,
        Func<AgentToolCall, AgentSession, StoryBibleDocument, bool, CancellationToken, Task<AgentToolExecutionResult>> handler,
        string? dedupPolicy = null) =>
        new()
        {
            Category = category,
            Definition = new AgentToolDefinition
            {
                Name = name,
                Description = description,
                Risk = risk,
                RequiresConfirmation = requiresConfirmation,
                Arguments = args.ToList(),
                SideEffects = sideEffects,
                Semantic = BuildDefaultSemantic(name, category, sideEffects, dedupPolicy),
            },
            Handler = handler,
        };

    private static AgentToolSemanticSpec BuildDefaultSemantic(string name, string category, AgentToolSideEffectSpec sideEffects, string? dedupPolicy = null)
    {
        var readsFrom = new List<string>();
        var writesTo = new List<string>();
        var domainSurface = category switch
        {
            "meta" => "Agent Runtime",
            "workspace" => "小说书城 / 创作工作流 / 知识库 / 记忆系统",
            "project" => "小说书城",
            "content" => "小说书城 / 正文内容 / Story Bible",
            "knowledge" or "rag" => "知识库",
            "creative" => "创意收件箱 / 创作工作流 / 天命生产包",
            "blackboard" => "创作工作流",
            "planning" or "writing" or "gate" => "创作工作流",
            "commit" => "小说书城 / Story Bible",
            "review" => "记忆系统 / 创作工作流",
            "maintenance" => "知识库 / 索引维护",
            _ => "Agent Runtime"
        };
        var outputKind = category switch
        {
            "meta" => "capability_catalog",
            "workspace" => "read_only_workspace_snapshot",
            "project" => "project_binding_or_creation",
            "content" => "read_only_project_content",
            "knowledge" => "knowledge_ingestion_process_result",
            "rag" => "retrieval_context",
            "creative" => "creative_intent_state",
            "blackboard" => "project_workflow_state",
            "planning" => "workflow_process_artifact",
            "writing" => "workflow_process_artifact",
            "gate" => "workflow_gate_report",
            "commit" => "canonical_project_or_chapter_state",
            "review" => "reflection_memory_update",
            "maintenance" => "index_maintenance_result",
            _ => "runtime_result"
        };

        switch (category)
        {
            case "meta":
                readsFrom.AddRange(new[] { "agent_tool_registry", "tool_search_cache" });
                break;
            case "workspace":
                readsFrom.AddRange(new[] { "novel_projects", "knowledge_base", "agent_runs", "volumes", "chapters", "agent_sessions" });
                break;
            case "project":
                readsFrom.AddRange(new[] { "novel_projects", "agent_sessions", "session_memory" });
                break;
            case "content":
                readsFrom.AddRange(new[] { "novel_projects", "volumes", "volume_arcs", "chapters", "chapter_versions", "content_documents", "content_chunks", "project_fact_snapshots", "production_events", "tianming_packages", "story_bible", "continuity_facts" });
                break;
            case "knowledge":
            case "rag":
                readsFrom.AddRange(new[] { "knowledge_base", "project_knowledge_usages", "content_chunks", "content_vector_points", "project_memory" });
                break;
            case "creative":
                readsFrom.AddRange(new[] { "creative_intents", "revision_plans", "chapters", "story_bible", "project_fact_snapshots", "tianming_packages" });
                break;
            case "blackboard":
                readsFrom.AddRange(new[] { "mission_blackboard", "story_bible", "agent_runs", "tool_execution_ledger" });
                break;
            case "production":
                readsFrom.AddRange(new[] { "agent_runtime_runs", "agent_runtime_events", "agent_tool_executions", "tianming_packages", "production_events", "outbox_events", "project_fact_snapshots", "chapters", "novel_projects" });
                break;
            case "planning":
            case "writing":
            case "gate":
                readsFrom.AddRange(new[] { "story_bible", "project_memory", "author_memory", "execution_memory", "knowledge_base", "agent_runs" });
                break;
            case "commit":
                readsFrom.AddRange(new[] { "agent_runs", "content_documents", "story_bible" });
                break;
            case "review":
                readsFrom.AddRange(new[] { "agent_runs", "content_documents", "execution_memory", "author_memory" });
                break;
            case "maintenance":
                readsFrom.AddRange(new[] { "chapters", "story_bible", "content_chunks", "content_vector_points" });
                break;
        }

        if (string.Equals(name, "AuditCommittedChapter", StringComparison.OrdinalIgnoreCase))
        {
            readsFrom.AddRange(new[] { "chapters", "content_documents", "content_chunks", "story_bible", "continuity_facts", "knowledge_base" });
        }
        else if (string.Equals(name, "ReviseCommittedChapter", StringComparison.OrdinalIgnoreCase))
        {
            readsFrom.AddRange(new[] { "chapters", "content_documents", "content_chunks", "story_bible", "continuity_facts", "knowledge_base" });
        }
        else if (string.Equals(name, "RollbackChapterVersion", StringComparison.OrdinalIgnoreCase))
        {
            readsFrom.AddRange(new[] { "chapters", "chapter_versions", "content_documents", "content_chunks", "tianming_packages", "project_fact_snapshots" });
        }
        else if (string.Equals(name, "QueryProjectKnowledgeBindings", StringComparison.OrdinalIgnoreCase))
        {
            readsFrom.AddRange(new[] { "story_bible", "canon_ledger", "knowledge_conflict_reports", "knowledge_classifications" });
        }
        else if (string.Equals(name, "AttachKnowledgeToProject", StringComparison.OrdinalIgnoreCase))
        {
            readsFrom.AddRange(new[] { "knowledge_base", "current_project" });
        }

        readsFrom.AddRange(sideEffects.ReadsSqliteEntities);
        writesTo.AddRange(sideEffects.WritesMemoryScopes.Select(scope => $"{scope}_memory"));
        writesTo.AddRange(sideEffects.WritesSqliteEntities);
        writesTo.AddRange(sideEffects.WritesVectorIndexes.Select(index => $"vector:{index}"));
        if (sideEffects.WritesLedger) writesTo.Add("agent_tool_execution_ledger");
        if (sideEffects.WritesToolSearchCache) writesTo.Add("tool_search_cache");
        if (sideEffects.WritesRedisRecentCache) writesTo.Add("recent_runtime_cache");

        if (sideEffects.BusinessReadOnly || writesTo.Count == 0)
        {
            writesTo.Clear();
            writesTo.Add("none_read_only");
        }

        return new AgentToolSemanticSpec
        {
            DisplayName = BuildDisplayName(name),
            DomainSurface = domainSurface,
            OutputKind = outputKind,
            SideEffectLevel = BuildSideEffectLevel(name, category),
            ImpactScope = BuildImpactScope(name, category),
            FailureContract = BuildFailureContract(name, category),
            RequiresProject = RequiresProjectContext(name, category),
            SupportsNoProjectSession = SupportsNoProjectSession(name, category),
            AverageDuration = BuildAverageDuration(name, category),
            ProgressEventContract = BuildProgressEventContract(name, category),
            NextPossibleTools = BuildNextPossibleTools(name, category),
            ReadsFrom = readsFrom.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            WritesTo = writesTo.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            InputArtifacts = BuildInputArtifacts(name, category),
            OutputArtifacts = BuildOutputArtifacts(name, category),
            IdempotencyPolicy = BuildIdempotencyPolicy(name, category),
            RollbackPolicy = BuildRollbackPolicy(name, category),
            UserVisibleWhere = category switch
            {
                "workspace" => "Agent 对话中的工作台快照；不会改变书城或会话绑定",
                "project" => "当前 Agent 会话和小说书城项目列表",
                "content" => "Agent 对话中的项目内容引用；不会改变书城、正文或工作流",
                "knowledge" or "rag" => "知识库、项目知识引用和 Agent 对话",
                "creative" => "创意收件箱、创作工作流详情和后续章节生产包",
                "planning" or "writing" or "gate" => "创作工作流 Run 和 Agent 对话",
                "commit" => "小说书城、Story Bible、章节列表和 Agent 对话",
                "review" => "记忆系统、复盘记录和 Agent 对话",
                "maintenance" => "索引状态、知识库检索效果和 Agent 对话",
                _ => "Agent 对话"
            },
            ResultSemantics = category switch
            {
                "workspace" => "返回当前可见真实状态，帮助模型理解书城、知识库、工作流和会话绑定；结果不是项目绑定决策。",
                "content" => "返回当前项目已落库的卷、章、正文片段、连续性事实和本章生产包知识绑定，供模型基于真实内容回答、解释知识影响或续写。",
                _ when string.Equals(name, "QueryProjectKnowledgeBindings", StringComparison.OrdinalIgnoreCase) => "返回项目知识绑定状态摘要、绑定条目、Story Bible CanonLedger 和冲突快照，用于判断生产包、RAG、门禁和事实快照会使用哪些知识；只读，不替模型决定是否采纳知识。",
                "planning" => "返回候选或计划，属于过程产物，需后续 commit 才会成为最终项目状态。",
                "writing" => "返回草稿或上下文包，属于过程产物，需门禁和 commit 后才进入书城。",
                "gate" => "返回校验报告，决定是否修复或提交，报告本身不是最终章节。",
                "commit" => "把已选择/已通过门禁的产物固化为最终项目状态。",
                "creative" => "记录、查询或决策创意意图；已采纳创意会进入后续章节生产包，但不会直接覆盖正文。",
                "rag" => "返回检索上下文，供推理使用，不直接改变最终作品。",
                _ => "返回工具执行结果，模型需要结合当前任务判断下一步。"
            },
            DeduplicationPolicy = dedupPolicy ?? (sideEffects.BusinessReadOnly ? "per_turn" : "strict")
        };
    }

    private static string BuildDisplayName(string name) => name switch
    {
        "tool_search" => "工具语义检索",
        "QueryWorkspaceState" => "查询工作台状态",
        "ResolveNovelProject" => "解析或创建小说项目",
        "ProcessKnowledgeFile" => "处理知识文件",
        "QueryProjectStatus" => "查询项目工作流状态",
        "QueryProjectContent" => "查询项目卷章正文",
        "QueryChapterVersions" => "查询章节版本历史",
        "CompareChapterVersions" => "对比章节版本差异",
            "QueryNovelProductionState" => "查询生产运行状态",
            "QueryProjectKnowledgeBindings" => "查询项目知识绑定",
            "AttachKnowledgeToProject" => "绑定知识到项目",
            "ClassifyProjectKnowledge" => "分类项目知识约束",
            "DetectKnowledgeConflicts" => "检测知识冲突",
            "ResolveKnowledgeConflict" => "解决知识冲突",
            "CreateCreativeIntent" => "创建创意意图",
        "DecideCreativeIntent" => "决策创意意图",
        "QueryCreativeIntents" => "查询创意收件箱",
        "CreateRevisionPlan" => "创建修订计划",
        "QueryRevisionPlans" => "查询修订计划",
        "InvalidateAffectedPackages" => "失效受影响生产包",
        "RunBookValidation" => "整书章节群校验",
        "AuditCommittedChapter" => "审查已提交章节",
        "ReviseCommittedChapter" => "修订已提交章节",
        "RollbackChapterVersion" => "回滚章节版本",
        "QueryProductionOutbox" => "查询后台 outbox",
        "RetryProductionOutbox" => "重试后台 outbox",
        "SearchCreativeKnowledge" => "检索创意知识库",
        "PlanStoryFoundation" => "规划故事地基",
        "CommitStoryFoundation" => "提交故事地基",
        "PlanVolumeArc" => "规划卷级弧线",
        "CommitVolumeArc" => "提交卷级弧线",
        "PlanChapter" => "规划章节候选",
        "SelectChapterCandidate" => "选择章节候选",
        "ProduceChapter" => "生产章节闭环",
        "RefreshProjectIndexes" => "刷新项目索引",
        "AnalyzeDependencyImpact" => "分析依赖影响",
        "ReviewChapter" => "复盘章节产物",
        _ => name
    };

    private static List<string> BuildInputArtifacts(string name, string category)
    {
        if (string.Equals(name, "ProduceChapter", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string>
            {
                "chapter_plan_run",
                "continuity_pack",
                "knowledge_binding_snapshot",
                "creative_intent_snapshot",
                "revision_plan_optional",
                "commitPolicy"
            };
        }

        if (string.Equals(name, "QueryProjectContent", StringComparison.OrdinalIgnoreCase))
            return new List<string> { "project_content_query", "chapter_selector", "include_body_flag", "include_facts_flag" };

        if (string.Equals(name, "CreateCreativeIntent", StringComparison.OrdinalIgnoreCase))
            return new List<string> { "creative_raw_intent", "target_scope", "impact_level", "confirmation_requirement" };

        if (string.Equals(name, "SearchCreativeKnowledge", StringComparison.OrdinalIgnoreCase))
            return new List<string> { "knowledge_query", "project_binding_context", "memory_recall_context" };

        if (string.Equals(name, "RunBookValidation", StringComparison.OrdinalIgnoreCase))
            return new List<string> { "chapter_range_selector", "canonical_chapters", "fact_snapshots", "chapter_versions" };

        return category switch
        {
            "meta" => new List<string> { "tool_search_query", "phase_hint_optional", "tool_catalog_signature" },
            "workspace" => new List<string> { "current_user", "current_session" },
            "project" => new List<string> { "project_resolution_request", "session_context", "creation_brief_optional" },
            "content" => new List<string> { "project_content_selector", "chapter_or_version_selector" },
            "knowledge" => new List<string> { "knowledge_entry_or_task", "project_binding_context" },
            "rag" => new List<string> { "retrieval_query", "project_context" },
            "creative" => new List<string> { "creative_intent_or_revision_request", "project_context", "chapter_scope_optional" },
            "blackboard" => new List<string> { "current_project", "mission_blackboard" },
            "production" => new List<string> { "production_state_selector", "project_context", "runtime_run_optional" },
            "planning" => new List<string> { "creative_brief", "story_bible_context", "knowledge_context", "candidate_direction_hints" },
            "writing" => new List<string> { "chapter_plan_run", "story_bible_context", "continuity_context" },
            "gate" => new List<string> { "draft_artifact", "continuity_pack", "quality_rubric" },
            "commit" => new List<string> { "validated_run_or_version", "user_confirmation", "project_context" },
            "review" => new List<string> { "chapter_artifact", "review_focus", "project_context" },
            "maintenance" => new List<string> { "maintenance_run", "project_content_snapshot", "index_state" },
            _ => new List<string> { "tool_call_arguments", "agent_session_context" }
        };
    }

    private static List<string> BuildOutputArtifacts(string name, string category)
    {
        if (string.Equals(name, "ProduceChapter", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string>
            {
                "tianming_package",
                "chapter_draft",
                "kernel_gate_report",
                "agent_editorial_review",
                "chapter_commit",
                "chapter_version",
                "production_events",
                "post_commit_outbox"
            };
        }

        if (string.Equals(name, "QueryProjectContent", StringComparison.OrdinalIgnoreCase))
            return new List<string> { "project_content_snapshot", "chapter_body_snapshot", "fact_snapshot" };

        if (string.Equals(name, "CreateCreativeIntent", StringComparison.OrdinalIgnoreCase))
            return new List<string> { "creative_intent", "creative_inbox_event" };

        if (string.Equals(name, "SearchCreativeKnowledge", StringComparison.OrdinalIgnoreCase))
            return new List<string> { "retrieval_context", "knowledge_hits", "memory_hits" };

        if (string.Equals(name, "QueryProjectKnowledgeBindings", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string>
            {
                "project_knowledge_binding_status_summary",
                "project_knowledge_binding_snapshot",
                "story_bible_canon_ledger_snapshot",
                "knowledge_conflict_snapshot"
            };
        }

        if (string.Equals(name, "AttachKnowledgeToProject", StringComparison.OrdinalIgnoreCase))
            return new List<string> { "project_knowledge_binding", "knowledge_binding_output_artifact" };

        if (string.Equals(name, "RunBookValidation", StringComparison.OrdinalIgnoreCase))
            return new List<string> { "book_validation_report", "cross_chapter_continuity_report", "revision_recommendation_inputs" };

        return category switch
        {
            "meta" => new List<string> { "tool_catalog_page", "tool_semantic_cards", "tool_search_snapshot" },
            "workspace" => new List<string> { "workspace_state_snapshot" },
            "project" => new List<string> { "project_binding_result", "session_project_context" },
            "content" => new List<string> { "content_snapshot", "version_snapshot" },
            "knowledge" => new List<string> { "knowledge_processing_result", "knowledge_binding_update", "knowledge_conflict_report_optional" },
            "rag" => new List<string> { "retrieval_context" },
            "creative" => new List<string> { "creative_intent_state", "revision_plan_state", "production_invalidation_event_optional" },
            "blackboard" => new List<string> { "project_status_snapshot" },
            "production" => new List<string> { "production_state_snapshot", "outbox_state_snapshot_or_retry_signal" },
            "planning" => new List<string> { "planning_run", "candidate_artifact", "workflow_process_artifact" },
            "writing" => new List<string> { "draft_artifact", "gate_report", "workflow_process_artifact" },
            "gate" => new List<string> { "gate_report", "repair_recommendation" },
            "commit" => new List<string> { "canonical_project_state", "chapter_or_story_bible_commit", "version_or_outbox_event_optional" },
            "review" => new List<string> { "review_report", "memory_update_proposal" },
            "maintenance" => new List<string> { "index_refresh_report", "dependency_impact_report" },
            _ => new List<string> { "tool_execution_result" }
        };
    }

    private static string BuildIdempotencyPolicy(string name, string category)
    {
        if (string.Equals(name, "ProduceChapter", StringComparison.OrdinalIgnoreCase))
            return "Uses runId + targetChapterId + commitPolicy; auto_commit must reuse or supersede ChapterVersion/outbox instead of duplicating committed chapters.";

        if (string.Equals(name, "CreateCreativeIntent", StringComparison.OrdinalIgnoreCase))
            return "Prefer client/model supplied intentId when available; otherwise dedupe by projectId + source + normalizedIntent + target scope.";

        if (string.Equals(name, "RetryProductionOutbox", StringComparison.OrdinalIgnoreCase))
            return "Uses eventId; completed outbox is not retried, pending/failed items are reset in place.";

        if (category is "workspace" or "content" or "blackboard" ||
            name.StartsWith("Query", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Compare", StringComparison.OrdinalIgnoreCase))
            return "Read-only; repeat calls return the latest persisted snapshot without creating business artifacts.";

        return category switch
        {
            "meta" => "Cache key is catalog signature + query/intent/context; repeated calls may refresh tool_search_snapshot only.",
            "project" => "Bind uses projectId/projectTitle; create should dedupe by userId + title/idempotency key where available.",
            "knowledge" => "Use taskId/knowledgeId/conflictId where available; repeated processing updates the same task/report instead of creating unrelated artifacts.",
            "rag" => "Retrieval is repeatable for the same query and project context; only runtime cache/memory observation may change.",
            "creative" => "Use intentId/revisionPlanId when available; otherwise dedupe by target scope and normalized requirements.",
            "planning" => "Use runId/sourceTurnId/target chapter or volume when available; repeated planning should supersede or version the process artifact.",
            "writing" => "Use runId and target chapter; repeated writing should version drafts and package lineage.",
            "commit" => "Requires explicit confirmation; commits must use runId/versionId and avoid duplicate canonical writes.",
            "review" => "Use runId/chapterId; repeated review should attach a new review event or update the same run report.",
            "maintenance" => "Use runId/projectId; repeated refreshes update indexes and reports in place.",
            _ => "Use tool call arguments and current session as the idempotency boundary."
        };
    }

    private static string BuildRollbackPolicy(string name, string category)
    {
        if (string.Equals(name, "ProduceChapter", StringComparison.OrdinalIgnoreCase))
            return "Committed output is recoverable through ChapterVersion rollback; draft/package failures stay in workflow and can be retried from runId.";

        if (string.Equals(name, "RollbackChapterVersion", StringComparison.OrdinalIgnoreCase))
            return "Rollback itself creates production events and invalidates downstream packages; reversing requires another confirmed version switch.";

        if (string.Equals(name, "RetryProductionOutbox", StringComparison.OrdinalIgnoreCase))
            return "Retry resets the selected outbox item in place; failures remain visible through QueryProductionOutbox.";

        if (category is "workspace" or "content" or "blackboard" ||
            name.StartsWith("Query", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Compare", StringComparison.OrdinalIgnoreCase))
            return "No rollback required for read-only tools.";

        return category switch
        {
            "meta" => "No business rollback; tool_search snapshots can be regenerated from the registry.",
            "project" => "Project creation/binding rollback is manual: switch session binding or archive/delete project with explicit user action.",
            "knowledge" => "Knowledge changes should be corrected by conflict resolution, reclassification, archive/delete, or reprocessing the task.",
            "rag" => "No business rollback; retrieval observations can be superseded by later context.",
            "creative" => "CreativeIntent/RevisionPlan can be rejected, replaced, marked stale, or superseded before production.",
            "planning" => "Planning artifacts are process outputs; rerun planning or commit a newer version before canonicalization.",
            "writing" => "Draft/package outputs can be regenerated; committed chapters require ChapterVersion rollback.",
            "commit" => "Canonical writes require explicit follow-up rollback/revision tools and user confirmation.",
            "review" => "Review conclusions are append-only observations; supersede with a newer review if wrong.",
            "maintenance" => "Index and dependency reports can be rebuilt from SQLite production truth.",
            _ => "Rollback depends on the concrete business artifact; inspect tool result and production events."
        };
    }

    private static bool RequiresProjectContext(string name, string category)
    {
        if (name is "tool_search" or "QueryWorkspaceState" or "ResolveNovelProject" or "SearchCreativeKnowledge")
            return false;

        return category is not "meta" and not "workspace" and not "project";
    }

    private static bool SupportsNoProjectSession(string name, string category)
    {
        if (name is "tool_search" or "QueryWorkspaceState" or "ResolveNovelProject" or "SearchCreativeKnowledge")
            return true;

        return category is "meta" or "workspace" or "project";
    }

    private static string BuildAverageDuration(string name, string category)
    {
        if (name is "ProduceChapter" or "ReviseCommittedChapter")
            return "long:60-180s";
        if (name is "ProcessKnowledgeFile" or "PlanStoryFoundation" or "PlanVolumeArc" or "PlanChapter" or "CommitStoryFoundation" or "CommitVolumeArc" or "RefreshProjectIndexes")
            return "medium:10-60s";
        if (category is "planning" or "writing" or "commit" or "maintenance")
            return "medium:10-60s";

        return "short:0-10s";
    }

    private static List<string> BuildProgressEventContract(string name, string category)
    {
        if (string.Equals(name, "ProduceChapter", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string>
            {
                "chapter_context_package:读取 Story Bible、知识绑定、FactSnapshot、创意意图并构建生产包",
                "draft_generation:生成章节正文和 CHANGES",
                "kernel_gate:执行连续性、知识硬事实和格式门禁",
                "agent_review:总编验收用户意图、作品承诺和章节质量",
                "chapter_commit:提交书城、写章节版本和内容文档",
                "fact_snapshot:沉淀章节事实、角色状态和下一章承接项",
                "index_outbox:排队或刷新章节索引与长距离召回"
            };
        }

        if (string.Equals(name, "ReviseCommittedChapter", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string>
            {
                "revision_plan:读取已提交正文和修订目标",
                "draft_generation:生成完整修订章节",
                "kernel_gate:重新执行连续性和知识硬事实门禁",
                "chapter_commit:写入新章节版本并更新书城",
                "fact_snapshot:重算事实快照和后续影响"
            };
        }

        if (string.Equals(name, "ProcessKnowledgeFile", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string>
            {
                "knowledge_parse:解析上传文件",
                "knowledge_extract:提取结构化知识条目",
                "knowledge_persist:写入知识库和项目引用",
                "knowledge_index:排队或写入向量索引"
            };
        }

        if (category is "planning")
        {
            return new List<string>
            {
                "context_read:读取项目、知识库和记忆上下文",
                "candidate_generation:生成候选计划",
                "process_artifact:写入工作流过程产物"
            };
        }

        if (category is "commit")
        {
            return new List<string>
            {
                "artifact_read:读取已选择过程产物",
                "canonical_write:写入 Story Bible、卷规划或书城成品",
                "memory_index:沉淀记忆和索引任务"
            };
        }

        if (category is "maintenance")
        {
            return new List<string>
            {
                "state_scan:扫描当前项目状态",
                "maintenance_write:刷新摘要、事实、索引或影响分析",
                "status_report:返回可恢复维护结果"
            };
        }

        return new List<string>
        {
            IsReadOnlyTool(name) || category is "meta" or "rag"
                ? "read_only_snapshot:读取真实状态或检索上下文并返回给模型"
                : "tool_result:执行工具并返回结构化结果"
        };
    }

    private static List<string> BuildNextPossibleTools(string name, string category)
    {
        var next = name switch
        {
            "tool_search" => new[] { "QueryWorkspaceState", "QueryProjectStatus", "SearchCreativeKnowledge", "ResolveNovelProject", "ProduceChapter" },
            "QueryWorkspaceState" => new[] { "ResolveNovelProject", "QueryProjectStatus", "SearchCreativeKnowledge", "QueryNovelProductionState" },
            "ResolveNovelProject" => new[] { "QueryProjectStatus", "PlanStoryFoundation", "SearchCreativeKnowledge", "QueryProjectKnowledgeBindings" },
            "ProcessKnowledgeFile" => new[] { "ClassifyProjectKnowledge", "DetectKnowledgeConflicts", "QueryProjectKnowledgeBindings", "SearchCreativeKnowledge", "PlanChapter", "ProduceChapter" },
            "QueryProjectStatus" => new[] { "QueryProjectContent", "QueryChapterVersions", "QueryNovelProductionState", "PlanChapter", "ProduceChapter" },
            "QueryProjectContent" => new[] { "QueryChapterVersions", "CompareChapterVersions", "RunBookValidation", "PlanChapter", "ProduceChapter", "AuditCommittedChapter", "ReviseCommittedChapter" },
            "QueryChapterVersions" => new[] { "CompareChapterVersions", "QueryProjectContent", "RunBookValidation", "AuditCommittedChapter", "ReviseCommittedChapter", "RollbackChapterVersion", "QueryNovelProductionState" },
            "CompareChapterVersions" => new[] { "QueryChapterVersions", "RunBookValidation", "AuditCommittedChapter", "CreateRevisionPlan", "ReviseCommittedChapter", "RollbackChapterVersion" },
            "QueryNovelProductionState" => new[] { "QueryProductionOutbox", "RetryProductionOutbox", "QueryProjectContent", "QueryChapterVersions", "CompareChapterVersions", "ProduceChapter", "ReviseCommittedChapter", "RefreshProjectIndexes" },
            "QueryProductionOutbox" => new[] { "RetryProductionOutbox", "QueryNovelProductionState", "ProduceChapter", "RefreshProjectIndexes" },
            "RetryProductionOutbox" => new[] { "QueryNovelProductionState", "QueryProductionOutbox", "ProduceChapter" },
            "QueryProjectKnowledgeBindings" => new[] { "AttachKnowledgeToProject", "ClassifyProjectKnowledge", "DetectKnowledgeConflicts", "ResolveKnowledgeConflict", "SearchCreativeKnowledge", "PlanChapter", "ProduceChapter" },
            "AttachKnowledgeToProject" => new[] { "QueryProjectKnowledgeBindings", "ClassifyProjectKnowledge", "DetectKnowledgeConflicts", "PlanChapter", "ProduceChapter" },
            "ClassifyProjectKnowledge" => new[] { "DetectKnowledgeConflicts", "QueryProjectKnowledgeBindings", "PlanChapter", "ProduceChapter" },
            "DetectKnowledgeConflicts" => new[] { "ResolveKnowledgeConflict", "QueryProjectKnowledgeBindings", "CreateCreativeIntent", "PlanChapter" },
            "ResolveKnowledgeConflict" => new[] { "QueryProjectKnowledgeBindings", "CreateCreativeIntent", "CreateRevisionPlan", "PlanChapter", "ProduceChapter" },
            "CreateCreativeIntent" => new[] { "DecideCreativeIntent", "CreateRevisionPlan", "QueryCreativeIntents", "PlanChapter", "ProduceChapter" },
            "DecideCreativeIntent" => new[] { "CreateRevisionPlan", "QueryCreativeIntents", "PlanChapter", "ProduceChapter" },
            "QueryCreativeIntents" => new[] { "CreateCreativeIntent", "DecideCreativeIntent", "CreateRevisionPlan", "PlanChapter", "ProduceChapter" },
            "CreateRevisionPlan" => new[] { "InvalidateAffectedPackages", "QueryRevisionPlans", "QueryNovelProductionState", "ProduceChapter", "ReviseCommittedChapter" },
            "QueryRevisionPlans" => new[] { "InvalidateAffectedPackages", "CreateRevisionPlan", "QueryNovelProductionState", "ProduceChapter", "ReviseCommittedChapter" },
            "InvalidateAffectedPackages" => new[] { "QueryNovelProductionState", "ProduceChapter", "PlanChapter", "ReviseCommittedChapter" },
            "RunBookValidation" => new[] { "QueryProjectContent", "CreateRevisionPlan", "ReviseCommittedChapter", "PlanChapter", "ProduceChapter", "QueryNovelProductionState" },
            "AuditCommittedChapter" => new[] { "RunBookValidation", "ReviseCommittedChapter", "RollbackChapterVersion", "QueryProjectContent", "QueryNovelProductionState" },
            "ReviseCommittedChapter" => new[] { "QueryProjectContent", "QueryChapterVersions", "CompareChapterVersions", "RollbackChapterVersion", "QueryNovelProductionState", "RefreshProjectIndexes" },
            "RollbackChapterVersion" => new[] { "QueryChapterVersions", "CompareChapterVersions", "QueryProjectContent", "QueryNovelProductionState", "ProduceChapter" },
            "SearchCreativeKnowledge" => new[] { "AttachKnowledgeToProject", "ClassifyProjectKnowledge", "DetectKnowledgeConflicts", "ResolveKnowledgeConflict", "QueryProjectKnowledgeBindings", "CreateCreativeIntent", "PlanStoryFoundation", "PlanChapter", "ProduceChapter" },
            "PlanStoryFoundation" => new[] { "CommitStoryFoundation", "SearchCreativeKnowledge", "QueryProjectStatus" },
            "CommitStoryFoundation" => new[] { "PlanVolumeArc", "QueryProjectStatus", "SearchCreativeKnowledge" },
            "PlanVolumeArc" => new[] { "CommitVolumeArc", "PlanChapter", "QueryProjectStatus" },
            "CommitVolumeArc" => new[] { "PlanChapter", "QueryProjectStatus", "QueryProjectContent" },
            "PlanChapter" => new[] { "SelectChapterCandidate", "QueryProjectContent" },
            "SelectChapterCandidate" => new[] { "ProduceChapter", "QueryNovelProductionState", "QueryProjectContent" },
            "ProduceChapter" => new[] { "QueryProjectContent", "QueryChapterVersions", "CompareChapterVersions", "QueryNovelProductionState", "RunBookValidation", "AuditCommittedChapter", "ReviseCommittedChapter", "RefreshProjectIndexes" },
            "RefreshProjectIndexes" => new[] { "QueryProjectContent", "QueryNovelProductionState", "SearchCreativeKnowledge" },
            "AnalyzeDependencyImpact" => new[] { "QueryProjectContent", "PlanChapter", "ReviseCommittedChapter" },
            "ReviewChapter" => new[] { "QueryProjectContent", "ProduceChapter", "RefreshProjectIndexes" },
            _ => Array.Empty<string>()
        };

        if (next.Length > 0)
            return next.ToList();

        return category switch
        {
            "planning" => new List<string> { "SelectChapterCandidate", "ProduceChapter", "QueryProjectStatus" },
            "review" => new List<string> { "QueryProjectContent", "ReviseCommittedChapter", "RefreshProjectIndexes" },
            "maintenance" => new List<string> { "QueryNovelProductionState", "QueryProjectContent" },
            _ => new List<string> { "tool_search" }
        };
    }

    private static string BuildSideEffectLevel(string name, string category)
    {
        if (IsReadOnlyTool(name))
            return "read_only";

        return category switch
        {
            "meta" => "runtime_metadata_write",
            "project" => "workspace_context_write",
            "knowledge" => "knowledge_state_write",
            "rag" => "retrieval_process_write",
            "creative" => "creative_state_write",
            "planning" => "workflow_process_write",
            "writing" => string.Equals(name, "ProduceChapter", StringComparison.OrdinalIgnoreCase)
                ? "production_final_write"
                : "production_process_write",
            "gate" => "production_gate_write",
            "commit" => "production_final_write",
            "review" => "review_memory_write",
            "maintenance" => "maintenance_write",
            _ => "runtime_process_write"
        };
    }

    private static string BuildImpactScope(string name, string category)
    {
        if (string.Equals(name, "tool_search", StringComparison.OrdinalIgnoreCase))
            return "current_session/tool_catalog";
        if (string.Equals(name, "QueryWorkspaceState", StringComparison.OrdinalIgnoreCase))
            return "current_user/workspace";
        if (string.Equals(name, "QueryProjectContent", StringComparison.OrdinalIgnoreCase))
            return "current_project/content";
        if (string.Equals(name, "QueryChapterVersions", StringComparison.OrdinalIgnoreCase))
            return "current_project/chapter_versions";
        if (string.Equals(name, "CompareChapterVersions", StringComparison.OrdinalIgnoreCase))
            return "current_project/chapter_version_diff";
        if (string.Equals(name, "QueryNovelProductionState", StringComparison.OrdinalIgnoreCase))
            return "current_project/production_run";
        if (string.Equals(name, "QueryProjectKnowledgeBindings", StringComparison.OrdinalIgnoreCase))
            return "current_project/knowledge_bindings";
        if (string.Equals(name, "ProduceChapter", StringComparison.OrdinalIgnoreCase))
            return "current_project/chapter/run/library";

        return category switch
        {
            "workspace" => "current_user/workspace",
            "project" => "current_session/project",
            "content" => "current_project/content",
            "knowledge" or "rag" => "current_user/knowledge/optional_project_binding",
            "creative" => "current_project/creative_inbox",
            "blackboard" => "current_project/workflow",
            "planning" => "current_project/planning_run",
            "writing" or "gate" => "current_project/chapter/run",
            "commit" => "current_project/library/story_bible",
            "review" => "current_project/review_memory",
            "maintenance" => "current_project/indexes",
            _ => "current_session/runtime"
        };
    }

    private static string BuildFailureContract(string name, string category)
    {
        if (IsReadOnlyTool(name))
            return "read-only failure without writes; returns failed_stage, error_message, recoverable_actions";

        if (category is "writing" or "gate" or "commit")
            return "returns failed_stage, produced_artifacts, recoverable_actions, requires_user_decision";

        return category switch
        {
            "meta" => "returns failed_stage and cache_status; no business state is changed",
            "project" => "returns failed_stage, project_resolution_status, recoverable_actions",
            "knowledge" => "returns failed_stage, processed_artifacts, recoverable_actions, requires_user_decision",
            "rag" => "returns failed_stage and retrieval_status; no final story state is changed",
            "creative" => "returns failed_stage, creative_intent_status, recoverable_actions",
            "planning" => "returns missing_structured_arguments, failed_stage, process_artifact_status, recoverable_actions",
            "review" => "returns failed_stage, review_report_status, recoverable_actions",
            "maintenance" => "returns failed_stage, maintenance_status, recoverable_actions",
            _ => "returns failed_stage, error_message, recoverable_actions"
        };
    }

    private static bool IsReadOnlyTool(string name) =>
        name is "QueryWorkspaceState" or
            "QueryProjectStatus" or
            "QueryProjectContent" or
            "QueryChapterVersions" or
            "CompareChapterVersions" or
            "QueryNovelProductionState" or
            "QueryProductionOutbox" or
            "QueryProjectKnowledgeBindings" or
            "QueryCreativeIntents" or
            "QueryRevisionPlans" or
            "AuditCommittedChapter";

    private static AgentToolSideEffectSpec Effects(
        bool toolCache = false,
        bool sqliteSnapshot = false,
        bool readOnly = false,
        IReadOnlyList<string>? memory = null,
        IReadOnlyList<string>? sqliteReads = null,
        IReadOnlyList<string>? sqliteWrites = null,
        IReadOnlyList<string>? vector = null) => new()
        {
            WritesLedger = true,
            WritesRedisRecentCache = true,
            WritesToolSearchCache = toolCache,
            WritesSqliteSnapshot = sqliteSnapshot,
            BusinessReadOnly = readOnly,
            WritesMemoryScopes = memory?.ToList() ?? new List<string>(),
            ReadsSqliteEntities = sqliteReads?.ToList() ?? new List<string>(),
            WritesSqliteEntities = sqliteWrites?.ToList() ?? new List<string>(),
            WritesVectorIndexes = vector?.ToList() ?? new List<string>()
        };

    private async Task<AgentToolExecutionResult> ResolveNovelProjectAsync(AgentToolCall call, AgentSession session, CancellationToken ct)
    {
        var mode = Arg(call, "mode", "auto").Trim().ToLowerInvariant();
        var projectId = Arg(call, "projectId");
        var projectTitle = Arg(call, "projectTitle");
        var wantsCreation = mode is "create_new" or "create" or "new";
        var wantsBinding = !wantsCreation &&
                           (mode is "bind_existing" or "bind" or "existing" or "continue_existing" or "continue" ||
                            !string.IsNullOrWhiteSpace(projectId) ||
                            !string.IsNullOrWhiteSpace(projectTitle));

        if (wantsBinding)
        {
            var project = await FindProjectForBindingAsync(projectId, projectTitle, ct).ConfigureAwait(false);
            if (project == null)
            {
                return new AgentToolExecutionResult
                {
                    Success = false,
                    Message = string.IsNullOrWhiteSpace(projectTitle) && string.IsNullOrWhiteSpace(projectId)
                        ? "没有找到可绑定的已有小说项目。"
                        : $"没有找到匹配的已有小说项目：{FirstNonEmpty(projectTitle, projectId)}。",
                    Phase = session.Phase,
                    Suggestions = new[] { "列出已有项目", "创建新小说", "换一个项目名" },
                };
            }

            await _catalog.ActivateAsync(project.Id, ct).ConfigureAwait(false);
            session.ActiveProjectId = project.Id;
            session.ActiveRunId = null;
            session.Phase = "project_bound";
            session.WorkingMemory.ProjectMemory.ProjectId = project.Id;
            session.WorkingMemory.MissionPlan.ProjectId = project.Id;
            session.WorkingMemory.MissionPlan.ProjectTitle = project.Title;
            session.WorkingMemory.Mission.CurrentGoal = FirstNonEmpty(session.WorkingMemory.CurrentGoal, project.CoreHook);

            return new AgentToolExecutionResult
            {
                Success = true,
                Message = $"已切换到已有小说「{project.Title}」。后续记忆、工具和任务都会在这个项目上下文里继续。",
                Phase = session.Phase,
                Data = project,
                Artifact = BuildArtifact("project_bound", project.Id, project.Id, string.Empty, $"已绑定已有小说「{project.Title}」。", new[] { "查看当前状态", "继续规划" }),
                Suggestions = new[] { "查看当前状态", "继续规划", "处理知识文件" },
            };
        }

        return await CreateNovelProjectAsync(call, session, ct).ConfigureAwait(false);
    }

    private async Task<NovelProjectInfo?> FindProjectForBindingAsync(string projectId, string projectTitle, CancellationToken ct)
    {
        var document = await _catalog.GetAsync(ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(projectId))
        {
            var byId = document.Projects.FirstOrDefault(p =>
                string.Equals(p.Id, projectId, StringComparison.OrdinalIgnoreCase));
            if (byId != null)
            {
                return byId;
            }
        }

        if (!string.IsNullOrWhiteSpace(projectTitle))
        {
            var byTitle = document.Projects.FirstOrDefault(p =>
                string.Equals(p.Title, projectTitle, StringComparison.OrdinalIgnoreCase));
            if (byTitle != null)
            {
                return byTitle;
            }

            var normalizedTitle = NormalizeProjectFingerprint(projectTitle);
            if (!string.IsNullOrWhiteSpace(normalizedTitle))
            {
                byTitle = document.Projects.FirstOrDefault(p =>
                {
                    var candidate = NormalizeProjectFingerprint(p.Title);
                    return !string.IsNullOrWhiteSpace(candidate) &&
                           (candidate.Contains(normalizedTitle, StringComparison.OrdinalIgnoreCase) ||
                            normalizedTitle.Contains(candidate, StringComparison.OrdinalIgnoreCase));
                });
                if (byTitle != null)
                {
                    return byTitle;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(document.ActiveProjectId))
        {
            return document.Projects.FirstOrDefault(p =>
                string.Equals(p.Id, document.ActiveProjectId, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private async Task<AgentToolExecutionResult> CreateNovelProjectAsync(AgentToolCall call, AgentSession session, CancellationToken ct)
    {
        var seed = Arg(call, "seed", session.WorkingMemory.CurrentGoal);
        var foundationBriefReady = ResolveFoundationBriefReady(call);
        if (IsAwaitingFoundationForExistingProject(session))
        {
            var existing = !string.IsNullOrWhiteSpace(session.ActiveProjectId)
                ? await _catalog.FindAsync(session.ActiveProjectId, ct).ConfigureAwait(false)
                : null;
            existing ??= await _catalog.GetActiveAsync(ct).ConfigureAwait(false);
            BindSessionToFoundationProject(session, existing, seed, foundationBriefReady);

            return new AgentToolExecutionResult
            {
                Success = true,
                Message = foundationBriefReady
                    ? $"当前会话已经有新小说工程「{existing.Title}」。模型已显式标记创作简报足够，下一步可以生成故事地基候选。"
                    : $"当前会话已经有待补地基的新小说工程「{existing.Title}」。工具未重复创建项目，下一步应读取作者补充的地基信息。",
                Phase = session.Phase,
                Data = existing,
                Artifact = BuildArtifact("existing_novel_project", existing.Id, existing.Id, string.Empty, $"新小说工程「{existing.Title}」。", foundationBriefReady ? new[] { "生成故事地基候选" } : new[] { "补齐故事地基", "生成故事地基候选" }),
                Suggestions = foundationBriefReady ? new[] { "生成故事地基候选", "查看当前状态" } : new[] { "补齐类型/核心钩子/主角引擎", "查看当前状态", "继续地基规划" },
            };
        }

        var settings = await _settingsManager.LoadAsync(ct).ConfigureAwait(false);
        var requestedTitle = FirstNonEmpty(Arg(call, "title"), Arg(call, "projectTitle"));
        var requestedGenre = FirstNonEmpty(Arg(call, "genre"), settings.DefaultGenre);
        var reusableProject = await FindReusableDraftProjectAsync(seed, requestedTitle, ct).ConfigureAwait(false);
        if (reusableProject != null)
        {
            BindSessionToFoundationProject(session, reusableProject, seed, foundationBriefReady);
            var message = foundationBriefReady
                ? $"已复用待补地基的新小说「{reusableProject.Title}」，没有重复创建同名项目。\n\n模型已显式标记创作简报足够，下一步可以生成故事地基候选。"
                : $"已复用待补地基的新小说「{reusableProject.Title}」，没有重复创建同名项目。\n\n现在继续把地基问清楚：这本书的类型、核心钩子、主角引擎、主要阅读快感，以及明确不要的方向分别是什么？";
            return new AgentToolExecutionResult
            {
                Success = true,
                Message = message,
                Phase = session.Phase,
                Data = reusableProject,
                Artifact = BuildArtifact("existing_novel_project", reusableProject.Id, reusableProject.Id, string.Empty, $"复用新小说「{reusableProject.Title}」。", foundationBriefReady ? new[] { "生成故事地基候选" } : new[] { "补齐故事地基", "生成故事地基候选" }),
                Suggestions = foundationBriefReady ? new[] { "生成故事地基候选", "查看当前状态" } : new[] { "补齐类型/核心钩子/主角引擎", "查看当前状态", "继续地基规划" },
            };
        }

        var project = await _catalog.CreateAsync(new NovelProjectCreateRequest(
            requestedTitle,
            requestedGenre,
            seed), ct).ConfigureAwait(false);

        BindSessionToFoundationProject(session, project, seed, foundationBriefReady);
        var resultMessage = foundationBriefReady
            ? $"已创建新小说「{project.Title}」，它会作为独立作品进入书城，不会覆盖旧书。\n\n模型已显式标记创作简报足够，下一步可以生成故事地基候选。"
            : $"已创建新小说「{project.Title}」，它会作为独立作品进入书城，不会覆盖旧书。\n\n现在先把地基问清楚：这本书的类型、核心钩子、主角引擎、主要阅读快感，以及明确不要的方向分别是什么？";

        return new AgentToolExecutionResult
        {
            Success = true,
            Message = resultMessage,
            Phase = session.Phase,
            Data = project,
            Artifact = BuildArtifact("novel_project", project.Id, project.Id, string.Empty, $"新小说「{project.Title}」已创建。", foundationBriefReady ? new[] { "生成故事地基候选" } : new[] { "补齐故事地基", "生成故事地基候选" }),
            Suggestions = foundationBriefReady ? new[] { "生成故事地基候选", "查看当前状态" } : new[] { "补齐创作简报", "查看当前状态" },
        };
    }

    private async Task<NovelProjectInfo?> FindReusableDraftProjectAsync(string seed, string title, CancellationToken ct)
    {
        var normalizedSeed = NormalizeProjectFingerprint(seed);
        var normalizedTitle = NormalizeProjectFingerprint(title);
        if (string.IsNullOrWhiteSpace(normalizedSeed) && string.IsNullOrWhiteSpace(normalizedTitle))
            return null;

        var document = await _catalog.GetAsync(ct).ConfigureAwait(false);
        foreach (var project in document.Projects.OrderByDescending(p => p.UpdatedAt))
        {
            if (!IsPotentialDraftDuplicate(project, normalizedSeed, normalizedTitle))
                continue;

            var bible = await _catalog.WithProjectAsync(
                project,
                () => _workspace.Orchestrator.GetStoryBibleAsync(ct),
                ct).ConfigureAwait(false);
            if (HasCommittedChapter(bible))
                continue;

            if (bible.Constitution == null || project.Status is "Drafting" or "Foundation" or "Planning")
                return project;
        }

        return null;
    }

    private static bool IsPotentialDraftDuplicate(NovelProjectInfo project, string normalizedSeed, string normalizedTitle)
    {
        var projectSeed = NormalizeProjectFingerprint(project.CoreHook);
        var projectTitle = NormalizeProjectFingerprint(project.Title);
        return (!string.IsNullOrWhiteSpace(normalizedSeed) && normalizedSeed == projectSeed) ||
               (!string.IsNullOrWhiteSpace(normalizedTitle) && normalizedTitle == projectTitle && IsGenericDraftTitle(project.Title));
    }

    private static bool HasCommittedChapter(StoryBibleDocument bible) =>
        bible.AgentRuns.Any(run =>
            string.Equals(run.DraftArtifact?.Status, "committed", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(run.DraftArtifact?.CommittedContent));

    private static void BindSessionToFoundationProject(AgentSession session, NovelProjectInfo project, string seed, bool foundationBriefReady = false)
    {
        session.ActiveProjectId = project.Id;
        session.ActiveRunId = null;
        session.RunHistory.Clear();
        session.Phase = foundationBriefReady ? "foundation_ready" : "awaiting_user_foundation";
        session.WorkingMemory.PendingToolCall = null;
        session.WorkingMemory.CurrentGoal = seed;
        session.WorkingMemory.OpenQuestions.Clear();
        session.WorkingMemory.Mission = new AgentMissionState
        {
            CurrentGoal = seed,
            CreativePhase = foundationBriefReady ? "ready_for_foundation_planning" : "foundation_intake",
            Readiness = foundationBriefReady ? "ready" : "needs_author_input",
            NextIntent = foundationBriefReady ? "plan_story_foundation" : "ask_foundation_question",
            PendingUserDecision = foundationBriefReady ? string.Empty : "补齐故事地基设定",
            PendingQuestion = foundationBriefReady ? string.Empty : "这本新小说的类型、核心钩子、主角引擎、阅读快感和禁区分别是什么？",
        };
        if (!string.IsNullOrWhiteSpace(seed))
            session.WorkingMemory.Mission.FoundationBrief["rawSeed"] = seed;
        if (!foundationBriefReady)
            session.WorkingMemory.OpenQuestions.Add(session.WorkingMemory.Mission.PendingQuestion);
    }

    private static bool ResolveFoundationBriefReady(AgentToolCall call) =>
        ArgBool(call, "foundationBriefReady") ||
        string.Equals(Arg(call, "readiness"), "ready", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAny(string text, params string[] tokens) =>
        !string.IsNullOrWhiteSpace(text) &&
        tokens.Any(token => !string.IsNullOrWhiteSpace(token) &&
                            text.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeProjectFingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    private static bool IsGenericDraftTitle(string value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Contains("未命名", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("新书", StringComparison.OrdinalIgnoreCase);

    private static bool IsAwaitingFoundationForExistingProject(AgentSession session) =>
        !string.IsNullOrWhiteSpace(session.ActiveProjectId) &&
        (string.Equals(session.Phase, "awaiting_user_foundation", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(session.WorkingMemory.Mission.CreativePhase, "foundation_intake", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(session.WorkingMemory.MissionPlan.Stage, "foundation", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(session.WorkingMemory.MissionPlan.Status, "blocked", StringComparison.OrdinalIgnoreCase));

    private async Task<AgentToolExecutionResult> QueryProjectStatusAsync(AgentSession session, StoryBibleDocument bible, CancellationToken ct)
    {
        var materialCount = await CountProjectMaterialsAsync(session, ct).ConfigureAwait(false);
        var currentRun = AgentRunSelector.SelectCurrentRun(bible);
        var plan = session.WorkingMemory.MissionPlan;
        var lines = new List<string>();
        lines.Add($"任务黑板：{plan.ProjectTitle}/{plan.Stage}/{plan.Status}");
        if (!string.IsNullOrWhiteSpace(plan.LastVerifiedState))
            lines.Add($"最近核验状态：{plan.LastVerifiedState}");
        if (plan.AllowedNextActions.Count > 0)
            lines.Add($"允许下一步：{string.Join("、", plan.AllowedNextActions.Take(8))}");
        if (plan.SchedulerState.Tasks.Count > 0)
        {
            var taskLines = plan.SchedulerState.Tasks.Take(5)
                .Select(t => $"{t.ChapterId}:{t.NextAction}/{t.Status}" + (string.IsNullOrWhiteSpace(t.BlockedReason) ? "" : $"({t.BlockedReason})"));
            lines.Add($"任务队列：{string.Join("；", taskLines)}");
        }
        if (bible.Constitution == null)
            lines.Add("Story Bible 尚未固化。");
        else
            lines.Add($"Story Bible：{bible.Constitution.Genre}/{bible.Constitution.SubGenre}；核心钩子：{bible.Constitution.CoreHook}");
        lines.Add($"素材：{materialCount} 份；卷规划：{bible.VolumeArcs.Count}；设定/伏笔/角色账本：{bible.CanonLedger.Count}/{bible.ForeshadowLedger.Count}/{bible.CharacterLedger.Count}");
        if (currentRun != null)
            lines.Add($"当前 Run：{currentRun.Intent}/{currentRun.Status}");
        var recommendedTool = currentRun?.Intent == NovelAgentIntent.PlanVolumeArc &&
                              currentRun.VolumeArcPlan != null &&
                              currentRun.Status is not (NovelAgentRunStatus.Completed or NovelAgentRunStatus.Failed or NovelAgentRunStatus.Cancelled)
            ? "CommitVolumeArc"
            : string.Empty;
        var recommendedArguments = string.IsNullOrWhiteSpace(recommendedTool)
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["runId"] = currentRun!.RunId };
        var nextHints = string.IsNullOrWhiteSpace(recommendedTool)
            ? bible.Constitution == null ? new[] { "开始规划故事地基" } : new[] { "规划下一章", "查看书城" }
            : new[] { "提交当前卷规划", "调整卷规划" };

        return new AgentToolExecutionResult
        {
            Success = true,
            Message = string.Join("\n", lines),
            Phase = "query_project",
            RecommendedToolName = recommendedTool,
            RecommendedArguments = recommendedArguments,
            Artifact = BuildArtifact("project_status", "story_bible", string.Empty, currentRun?.RunId ?? string.Empty, string.Join("；", lines), nextHints),
            Suggestions = nextHints,
        };
    }

    private async Task<AgentToolExecutionResult> QueryProjectContentAsync(
        AgentToolCall call,
        AgentSession session,
        StoryBibleDocument bible,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "当前会话尚未绑定小说项目，无法读取项目卷章正文。",
                Phase = "query_project_content",
                Suggestions = new[] { "先绑定小说项目", "查看工作台项目列表" }
            };
        }

        var chapterId = Arg(call, "chapterId");
        var chapterNumber = ArgInt(call, "chapterNumber");
        var volumeNumber = ArgInt(call, "volumeNumber");
        var versionId = Arg(call, "versionId");
        var versionNumber = ArgInt(call, "versionNumber");
        var includeBody = ArgBool(call, "includeBody", fallback: false);
        var includeFacts = ArgBool(call, "includeFacts", fallback: true);

        using var scope = _serviceProvider.CreateScope();
        var queryService = scope.ServiceProvider.GetRequiredService<IProjectContentQueryService>();
        var query = await queryService.QueryAsync(
                new ProjectContentQueryRequest(
                    UserId: session.UserId,
                    ProjectId: session.ActiveProjectId,
                    StoryBible: bible,
                    ChapterId: chapterId,
                    ChapterNumber: chapterNumber,
                    VolumeNumber: volumeNumber,
                    IncludeBody: includeBody,
                    IncludeFacts: includeFacts,
                    VersionId: versionId,
                    VersionNumber: versionNumber),
                ct)
            .ConfigureAwait(false);
        if (query == null)
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "没有找到当前会话可读取的小说项目。",
                Phase = "query_project_content",
                Suggestions = new[] { "重新绑定项目", "查看工作台状态" }
            };
        }

        if (query.Items.Count == 0)
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "没有找到匹配的章节内容。",
                Phase = "query_project_content",
                Suggestions = new[] { "换一个章节号", "先查询项目状态", "查看书城章节列表" }
            };
        }

        var message = FormatProjectContentQuery(query.ProjectTitle, query.Items, includeBody);
        var nextHints = BuildProjectContentNextHints(query.Items);
        var sourceRunId = query.Items.Select(i => i.SourceRunId).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id)) ?? string.Empty;
        return new AgentToolExecutionResult
        {
            Success = true,
            Message = message,
            Phase = "query_project_content",
            RunId = sourceRunId,
            Data = query,
            Artifact = BuildArtifact(
                "project_content_query",
                query.Items.First().ChapterId,
                query.ProjectId,
                sourceRunId,
                $"已读取 {query.Items.Count} 个章节内容。",
                nextHints),
            Suggestions = nextHints
        };
    }

    private async Task<AgentToolExecutionResult> QueryChapterVersionsAsync(
        AgentToolCall call,
        AgentSession session,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "当前会话尚未绑定小说项目，无法读取章节版本。",
                Phase = "query_chapter_versions",
                Suggestions = new[] { "先绑定小说项目", "查看工作台项目列表" }
            };
        }

        using var scope = _serviceProvider.CreateScope();
        var contentQuery = scope.ServiceProvider.GetRequiredService<IProjectContentQueryService>();
        var chapter = await contentQuery.ResolveChapterAsync(
                new ProjectChapterIdentityRequest(
                    session.UserId,
                    session.ActiveProjectId,
                    Arg(call, "chapterId"),
                    ArgInt(call, "chapterNumber")),
                ct)
            .ConfigureAwait(false);
        if (chapter == null)
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "没有找到匹配的章节，无法读取版本历史。",
                Phase = "query_chapter_versions",
                Suggestions = new[] { "换一个章节号", "先查询项目内容", "查看书城章节列表" }
            };
        }

        var isAdmin = await contentQuery.IsAdminAsync(session.UserId, ct).ConfigureAwait(false);
        var chapters = scope.ServiceProvider.GetRequiredService<IChapterService>();
        var versions = await chapters.GetChapterVersionsAsync(
                chapter.ChapterId,
                session.UserId,
                isAdmin,
                ct)
            .ConfigureAwait(false);
        if (versions.Count == 0)
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "该章节还没有可读取的版本记录。",
                Phase = "query_chapter_versions",
                Suggestions = new[] { "查询章节正文", "检查章节是否已提交", "继续生产章节" }
            };
        }

        var message = FormatChapterVersions(chapter.ChapterId, versions);
        var current = versions.FirstOrDefault(version => version.IsCurrent) ?? versions.First();
        var suggestions = new[] { "对比章节版本", "审查当前版本", "基于当前版本创建修订计划" };
        return new AgentToolExecutionResult
        {
            Success = true,
            Message = message,
            Phase = "query_chapter_versions",
            RunId = current.RuntimeRunId ?? string.Empty,
            Data = versions,
            Artifact = BuildArtifact(
                "chapter_versions",
                chapter.ChapterId,
                session.ActiveProjectId,
                current.RuntimeRunId ?? string.Empty,
                $"已读取 {versions.Count} 个章节版本。",
                suggestions),
            Suggestions = suggestions
        };
    }

    private async Task<AgentToolExecutionResult> CompareChapterVersionsAsync(
        AgentToolCall call,
        AgentSession session,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "当前会话尚未绑定小说项目，无法对比章节版本。",
                Phase = "compare_chapter_versions",
                Suggestions = new[] { "先绑定小说项目", "查看工作台项目列表" }
            };
        }

        var leftVersionId = Arg(call, "leftVersionId", Arg(call, "baselineVersionId"));
        var rightVersionId = Arg(call, "rightVersionId", Arg(call, "targetVersionId"));
        if (string.IsNullOrWhiteSpace(leftVersionId) || string.IsNullOrWhiteSpace(rightVersionId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "对比章节版本需要提供 leftVersionId 和 rightVersionId。",
                Phase = "compare_chapter_versions",
                Suggestions = new[] { "先调用 QueryChapterVersions", "选择两个版本 ID" }
            };
        }

        using var scope = _serviceProvider.CreateScope();
        var contentQuery = scope.ServiceProvider.GetRequiredService<IProjectContentQueryService>();
        var chapter = await contentQuery.ResolveChapterAsync(
                new ProjectChapterIdentityRequest(
                    session.UserId,
                    session.ActiveProjectId,
                    Arg(call, "chapterId"),
                    ArgInt(call, "chapterNumber")),
                ct)
            .ConfigureAwait(false);
        if (chapter == null)
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "没有找到匹配的章节，无法对比版本。",
                Phase = "compare_chapter_versions",
                Suggestions = new[] { "换一个章节号", "先查询项目内容", "查看章节版本列表" }
            };
        }

        var isAdmin = await contentQuery.IsAdminAsync(session.UserId, ct).ConfigureAwait(false);
        var chapters = scope.ServiceProvider.GetRequiredService<IChapterService>();
        ChapterVersionCompareResponse comparison;
        try
        {
            comparison = await chapters.CompareChapterVersionsAsync(
                    chapter.ChapterId,
                    leftVersionId,
                    rightVersionId,
                    session.UserId,
                    isAdmin,
                    ct)
                .ConfigureAwait(false);
        }
        catch (KeyNotFoundException ex)
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = ex.Message,
                Phase = "compare_chapter_versions",
                Suggestions = new[] { "先调用 QueryChapterVersions", "重新选择版本 ID" }
            };
        }
        catch (ArgumentException ex)
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = ex.Message,
                Phase = "compare_chapter_versions",
                Suggestions = new[] { "补充 leftVersionId", "补充 rightVersionId" }
            };
        }

        var suggestions = new[] { "审查右侧版本", "基于差异创建修订计划", "确认后可回滚到某个版本" };
        return new AgentToolExecutionResult
        {
            Success = true,
            Message = FormatChapterVersionComparison(comparison),
            Phase = "compare_chapter_versions",
            RunId = comparison.Right.RuntimeRunId ?? comparison.Left.RuntimeRunId ?? string.Empty,
            Data = comparison,
            Artifact = BuildArtifact(
                "chapter_version_diff",
                chapter.ChapterId,
                session.ActiveProjectId,
                comparison.Right.RuntimeRunId ?? comparison.Left.RuntimeRunId ?? string.Empty,
                $"已对比章节 v{comparison.Left.VersionNumber} 与 v{comparison.Right.VersionNumber}。",
                suggestions),
            Suggestions = suggestions
        };
    }

    private async Task<AgentToolExecutionResult> RollbackChapterVersionAsync(
        AgentToolCall call,
        AgentSession session,
        bool confirmed,
        CancellationToken ct)
    {
        if (!confirmed)
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Risk = NovelToolRiskLevel.High.ToString(),
                Message = "回滚章节版本会切换书城当前正文，并使当前章及下游生产包过期，需要用户确认后执行。",
                Phase = "rollback_chapter_version",
                Suggestions = new[] { "确认回滚", "先查询章节版本", "取消回滚" }
            };
        }

        if (string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Risk = NovelToolRiskLevel.High.ToString(),
                Message = "当前会话尚未绑定小说项目，无法回滚章节版本。",
                Phase = "rollback_chapter_version",
                Suggestions = new[] { "先绑定小说项目", "查看工作台项目列表" }
            };
        }

        var targetVersionId = Arg(call, "targetVersionId");
        if (string.IsNullOrWhiteSpace(targetVersionId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Risk = NovelToolRiskLevel.High.ToString(),
                Message = "缺少 targetVersionId，无法判断要回滚到哪个章节版本。",
                Phase = "rollback_chapter_version",
                Suggestions = new[] { "先调用 QueryChapterVersions", "选择目标版本" }
            };
        }

        using var scope = _serviceProvider.CreateScope();
        var contentQuery = scope.ServiceProvider.GetRequiredService<IProjectContentQueryService>();
        var chapter = await contentQuery.ResolveChapterAsync(
                new ProjectChapterIdentityRequest(
                    session.UserId,
                    session.ActiveProjectId,
                    Arg(call, "chapterId"),
                    ArgInt(call, "chapterNumber")),
                ct)
            .ConfigureAwait(false);
        if (chapter == null)
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Risk = NovelToolRiskLevel.High.ToString(),
                Message = "没有找到匹配的章节，无法回滚版本。",
                Phase = "rollback_chapter_version",
                Suggestions = new[] { "换一个章节号", "先查询章节版本", "查看书城章节列表" }
            };
        }

        var rollbackService = scope.ServiceProvider.GetRequiredService<IChapterVersionRollbackService>();
        var result = await rollbackService.RollbackAsync(
                new RollbackChapterVersionRequest(
                    UserId: session.UserId,
                    ProjectId: session.ActiveProjectId,
                    ChapterId: chapter.ChapterId,
                    TargetVersionId: targetVersionId,
                    RuntimeRunId: FirstNonEmpty(Arg(call, "runId"), session.ActiveRunId, $"rollback:{targetVersionId}"),
                    Reason: Arg(call, "reason")),
                ct)
            .ConfigureAwait(false);

        var message = result.Success
            ? $"已回滚章节 {chapter.ChapterId} 到 v{result.CurrentVersionNumber}（{result.CurrentVersionId}）。失效生产包：{(result.InvalidatedPackageIds.Count == 0 ? "无" : string.Join("、", result.InvalidatedPackageIds))}。后台已排队刷新索引和事实快照。"
            : result.Message;
        return new AgentToolExecutionResult
        {
            Success = result.Success,
            Risk = NovelToolRiskLevel.High.ToString(),
            Message = message,
            Phase = "rollback_chapter_version",
            RunId = result.RuntimeRunId,
            Data = result,
            Artifact = BuildArtifact(
                "chapter_version_rollback",
                result.CurrentVersionId,
                session.ActiveProjectId,
                result.RuntimeRunId,
                message,
                new[] { "查询章节版本", "查询生产状态", "检查书城正文" }),
            Suggestions = result.Success
                ? new[] { "查询章节版本", "查询生产状态", "检查书城正文" }
                : new[] { "先查询章节版本", "确认目标版本", "取消回滚" }
        };
    }

    private async Task<AgentToolExecutionResult> QueryNovelProductionStateAsync(
        AgentToolCall call,
        AgentSession session,
        CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var queryService = scope.ServiceProvider.GetRequiredService<INovelProductionStateQueryService>();

        var projectId = FirstNonEmpty(Arg(call, "projectId"), session.ActiveProjectId);
        var runId = Arg(call, "runId");
        var chapterId = Arg(call, "chapterId");
        var chapterNumber = ArgInt(call, "chapterNumber");
        var includeEvents = ArgBool(call, "includeEvents", fallback: true);

        var state = await queryService.QueryAsync(
                new NovelProductionStateQueryRequest(
                    UserId: session.UserId,
                    SessionId: session.SessionId,
                    ProjectId: projectId,
                    RunId: runId,
                    ChapterId: chapterId,
                    ChapterNumber: chapterNumber,
                    IncludeEvents: includeEvents),
                ct)
            .ConfigureAwait(false);
        if (state == null)
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "没有找到匹配的小说生产状态。",
                Phase = "query_novel_production_state",
                Suggestions = new[] { "确认 runId", "查询项目内容", "查看工作台状态" }
            };
        }

        var message = FormatNovelProductionState(state);
        return new AgentToolExecutionResult
        {
            Success = true,
            Message = message,
            Phase = "query_novel_production_state",
            RunId = state.RuntimeRun?.Id ?? runId,
            Data = state,
            Artifact = BuildArtifact(
                "novel_production_state",
                state.RuntimeRun?.Id ?? runId,
                state.ProjectId,
                state.RuntimeRun?.Id ?? runId,
                message,
                BuildNovelProductionStateHints(state)),
            Suggestions = BuildNovelProductionStateHints(state)
        };
    }

    private async Task<AgentToolExecutionResult> AuditCommittedChapterAsync(
        AgentToolCall call,
        AgentSession session,
        StoryBibleDocument bible,
        CancellationToken ct) =>
        await AuditCommittedChapterCoreAsync(call, session, bible, ct).ConfigureAwait(false);

    private async Task<AgentToolExecutionResult> RunBookValidationAsync(
        AgentToolCall call,
        AgentSession session,
        CancellationToken ct)
    {
        var projectId = FirstNonEmpty(Arg(call, "projectId"), session.ActiveProjectId);
        if (string.IsNullOrWhiteSpace(session.UserId) || string.IsNullOrWhiteSpace(projectId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Risk = "Medium",
                Message = "当前会话尚未绑定项目，不能执行整书章节群校验。",
                Phase = "book_validation",
                Suggestions = new[] { "先绑定项目", "查询工作台状态" }
            };
        }

        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBookValidationService>();
        var report = await service.ValidateAsync(
                new BookValidationRequest(
                    session.UserId,
                    projectId,
                    ArgInt(call, "startChapterNumber"),
                    ArgInt(call, "endChapterNumber"),
                    ArgBool(call, "includeBodyPreview", fallback: false)),
                ct)
            .ConfigureAwait(false);
        var message = FormatBookValidationReport(report);
        var suggestions = BuildBookValidationSuggestions(report);

        return new AgentToolExecutionResult
        {
            Success = true,
            Risk = NovelToolRiskLevel.Medium.ToString(),
            Message = message,
            Phase = "book_validation",
            RunId = session.ActiveRunId ?? string.Empty,
            Data = report,
            Artifact = BuildArtifact(
                "book_validation_report",
                projectId,
                projectId,
                session.ActiveRunId ?? string.Empty,
                message,
                suggestions),
            Suggestions = suggestions
        };
    }

    private async Task<AgentToolExecutionResult> QueryProductionOutboxAsync(
        AgentToolCall call,
        AgentSession session,
        CancellationToken ct)
    {
        var projectId = FirstNonEmpty(Arg(call, "projectId"), session.ActiveProjectId);
        if (string.IsNullOrWhiteSpace(session.UserId) || string.IsNullOrWhiteSpace(projectId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Risk = "Low",
                Message = "当前会话尚未绑定项目，不能查询项目后台 outbox。",
                Phase = "query_production_outbox",
                Suggestions = new[] { "先绑定项目", "查询工作台状态" }
            };
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        if (!await CanAccessProjectAsync(db, session.UserId, projectId, ct).ConfigureAwait(false))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Risk = "Low",
                Message = "没有权限查询该项目的后台 outbox。",
                Phase = "query_production_outbox",
                Suggestions = new[] { "确认项目", "查询工作台状态" }
            };
        }

        var admin = scope.ServiceProvider.GetRequiredService<IProductionOutboxAdminService>();
        var status = Arg(call, "status");
        var limit = ArgInt(call, "limit");
        var result = await admin.ListAsync(
                projectId,
                string.IsNullOrWhiteSpace(status) ? null : status,
                limit <= 0 ? 20 : limit,
                ct)
            .ConfigureAwait(false);
        var message = FormatProductionOutboxList(result);

        return new AgentToolExecutionResult
        {
            Success = true,
            Risk = "Low",
            Message = message,
            Phase = "query_production_outbox",
            RunId = session.ActiveRunId ?? string.Empty,
            Data = result,
            Artifact = BuildArtifact(
                "production_outbox",
                projectId,
                projectId,
                session.ActiveRunId ?? string.Empty,
                message,
                BuildProductionOutboxHints(result)),
            Suggestions = BuildProductionOutboxHints(result)
        };
    }

    private async Task<AgentToolExecutionResult> RetryProductionOutboxAsync(
        AgentToolCall call,
        AgentSession session,
        CancellationToken ct)
    {
        var eventId = Arg(call, "eventId");
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Risk = "Medium",
                Message = "缺少 eventId，不能重试后台 outbox。",
                Phase = "retry_production_outbox",
                Suggestions = new[] { "先查询生产状态", "先查询后台 outbox" }
            };
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var evt = await db.OutboxEvents
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == eventId, ct)
            .ConfigureAwait(false);
        if (evt == null)
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Risk = "Medium",
                Message = $"没有找到 outbox 事件 {eventId}。",
                Phase = "retry_production_outbox",
                Suggestions = new[] { "重新查询后台 outbox", "查询生产状态" }
            };
        }

        var projectId = FirstNonEmpty(Arg(call, "projectId"), session.ActiveProjectId, evt.ProjectId);
        if (string.IsNullOrWhiteSpace(projectId) ||
            !string.Equals(evt.ProjectId ?? string.Empty, projectId, StringComparison.OrdinalIgnoreCase) ||
            !await CanAccessProjectAsync(db, session.UserId, projectId, ct).ConfigureAwait(false))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Risk = "Medium",
                Message = "没有权限重试该项目的后台 outbox，或 eventId 不属于当前项目。",
                Phase = "retry_production_outbox",
                Suggestions = new[] { "确认项目", "重新查询后台 outbox" }
            };
        }

        if (string.Equals(evt.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return new AgentToolExecutionResult
            {
                Success = true,
                Risk = "Medium",
                Message = $"outbox {eventId} 已经完成，无需重试。",
                Phase = "retry_production_outbox",
                Data = evt,
                Suggestions = new[] { "查询生产状态", "继续章节生产" }
            };
        }

        var admin = scope.ServiceProvider.GetRequiredService<IProductionOutboxAdminService>();
        var result = await admin.RetryAsync(
                eventId,
                idempotencyKey: $"agent:{FirstNonEmpty(session.ActiveRunId, session.SessionId)}:{eventId}",
                cancellationToken: ct)
            .ConfigureAwait(false);
        var message = $"已重试后台 outbox {eventId}：当前状态 {result.Status}，尝试次数 {result.Attempts}，本次派发 {result.DispatchAttempted}，剩余待处理 {result.PendingCount}。";
        if (!string.IsNullOrWhiteSpace(result.LastError))
            message += $" 最近错误：{result.LastError}";

        var runId = evt.RuntimeRunId ?? session.ActiveRunId ?? string.Empty;
        var outputRecorder = scope.ServiceProvider.GetService<IOutputArtifactRecorder>();
        if (outputRecorder != null)
        {
            await outputRecorder.RecordAsync(
                    new OutputArtifactRecordRequest(
                        RuntimeRunId: runId,
                        UserId: session.UserId,
                        ProjectId: projectId,
                        ChapterId: string.Equals(evt.AggregateType, "chapter", StringComparison.OrdinalIgnoreCase)
                            ? evt.AggregateId
                            : null,
                        PackageId: null,
                        ToolName: "RetryProductionOutbox",
                        Stage: "retry_production_outbox",
                        Status: "completed",
                        ArtifactType: "production_outbox_retry",
                        ArtifactId: eventId,
                        OutputKind: "ProcessArtifact",
                        Summary: message,
                        UserVisibleWhere: new[] { "创作工作流" },
                        VisibleInWorkflow: true,
                        VisibleInLibrary: false,
                        SourceEventType: "outbox_retry_requested",
                        SourceEventId: eventId,
                        Data: result),
                    ct)
                .ConfigureAwait(false);
        }

        return new AgentToolExecutionResult
        {
            Success = true,
            Risk = "Medium",
            Message = message,
            Phase = "retry_production_outbox",
            RunId = runId,
            Data = result,
            Artifact = BuildArtifact(
                "production_outbox_retry",
                eventId,
                projectId,
                runId,
                message,
                new[] { "查询生产状态", "查询后台 outbox", "继续章节生产" }),
            Suggestions = new[] { "查询生产状态", "查询后台 outbox", "继续章节生产" }
        };
    }

    private async Task<AgentToolExecutionResult> AuditCommittedChapterCoreAsync(
        AgentToolCall call,
        AgentSession session,
        StoryBibleDocument bible,
        CancellationToken ct)
    {
        var contentResult = await QueryProjectContentAsync(
                ForceProjectContentBodyRead(call),
                session,
                bible,
                ct)
            .ConfigureAwait(false);
        if (!contentResult.Success)
            return contentResult;

        var content = AssertProjectContentItem(contentResult);
        if (content == null)
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "已读取项目内容，但没有可审查的章节正文。",
                Phase = "committed_chapter_audit",
                Suggestions = new[] { "重新读取章节正文", "查看项目内容" }
            };
        }

        var contextPackage = await BuildCommittedChapterAuditContextAsync(
                session,
                bible,
                content,
                Arg(call, "focus"),
                ct)
            .ConfigureAwait(false);
        var result = await _workspace.Orchestrator.AuditCommittedChapterAsync(
                content.ChapterId,
                content.Body,
                contextPackage,
                ct)
            .ConfigureAwait(false);

        session.ActiveRunId = result.Run?.RunId ?? session.ActiveRunId;
        session.Phase = "committed_chapter_audit";

        var message = FormatCommittedChapterAudit(content, result);
        var nextHints = result.GateReport?.Status == "validated"
            ? new[] { "继续下一章", "查看章节内容" }
            : new[] { "修订已提交章节", "查看失败项", "继续审查相邻章节" };

        return new AgentToolExecutionResult
        {
            Success = true,
            Risk = NovelToolRiskLevel.Medium.ToString(),
            Message = message,
            RunId = result.Run?.RunId ?? content.SourceRunId,
            Phase = session.Phase,
            Data = result,
            Artifact = BuildArtifact(
                "committed_chapter_audit",
                content.ChapterId,
                session.ActiveProjectId,
                result.Run?.RunId ?? content.SourceRunId,
                message,
                nextHints),
            Suggestions = nextHints
        };
    }

    private async Task<AgentToolExecutionResult> ReviseCommittedChapterAsync(
        AgentToolCall call,
        AgentSession session,
        StoryBibleDocument bible,
        bool confirmed,
        CancellationToken ct)
    {
        if (!confirmed)
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Risk = NovelToolRiskLevel.High.ToString(),
                Message = "修订已提交章节会覆盖书城正文，需要用户确认后执行。",
                Phase = session.Phase,
                Suggestions = new[] { "确认修订", "先审查章节" }
            };
        }

        var revisionPlanId = FirstNonEmpty(
            Arg(call, "revisionPlanId"),
            Arg(call, "sourceRevisionPlanId"),
            Arg(call, "revision_plan_id"));
        if (string.IsNullOrWhiteSpace(revisionPlanId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Risk = NovelToolRiskLevel.High.ToString(),
                Message = "修订已提交章节必须绑定 revisionPlanId，不能只凭自由文本 revisionGoal 直接覆盖书城正文。",
                Phase = "committed_chapter_revision_blocked",
                RecommendedToolName = "QueryRevisionPlans",
                Suggestions = new[] { "QueryRevisionPlans", "CreateRevisionPlan", "InvalidateAffectedPackages" }
            };
        }

        var contentResult = await QueryProjectContentAsync(
                ForceProjectContentBodyRead(call),
                session,
                bible,
                ct)
            .ConfigureAwait(false);
        if (!contentResult.Success)
            return contentResult;

        var content = AssertProjectContentItem(contentResult);
        if (content == null)
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Risk = NovelToolRiskLevel.High.ToString(),
                Message = "已读取项目内容，但没有可修订的章节正文。",
                Phase = "committed_chapter_revision",
                Suggestions = new[] { "重新读取章节正文", "查看项目内容" }
            };
        }

        var revisionPlan = await ResolveRevisionPlanForCommittedRevisionAsync(
                session,
                content,
                revisionPlanId,
                ct)
            .ConfigureAwait(false);
        if (revisionPlan == null)
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Risk = NovelToolRiskLevel.High.ToString(),
                Message = $"未找到当前项目可用于修订已提交章节的 RevisionPlan：{revisionPlanId}。",
                Phase = "committed_chapter_revision_blocked",
                RecommendedToolName = "QueryRevisionPlans",
                Suggestions = new[] { "QueryRevisionPlans", "CreateRevisionPlan" }
            };
        }

        var revisionGoal = FirstNonEmpty(
            Arg(call, "revisionGoal"),
            Arg(call, "focus"),
            revisionPlan.Recommendation,
            "修复回溯审查发现的连续性和知识库硬事实问题。");
        var contextPackage = await BuildCommittedChapterAuditContextAsync(
                session,
                bible,
                content,
                revisionGoal,
                ct)
            .ConfigureAwait(false);
        InjectRevisionPlanSnapshot(contextPackage, revisionPlan);
        NormalizeContextLists(contextPackage);
        var result = await _workspace.Orchestrator.ReviseCommittedChapterAsync(
                content.ChapterId,
                content.Body,
                contextPackage,
                revisionGoal,
                ct)
            .ConfigureAwait(false);

        if (result.Success)
            await _catalog.UpdateFromCurrentStoryBibleAsync(session.ActiveProjectId, ct).ConfigureAwait(false);

        session.ActiveRunId = result.Run?.RunId ?? session.ActiveRunId;
        session.Phase = result.Success ? "committed_chapter_revised" : "committed_chapter_revision_blocked";
        var message = result.Success
            ? $"{content.ChapterTitle} 已完成已提交正文修订并通过硬门禁。"
            : $"{content.ChapterTitle} 修订未完成：{result.Message}";
        var nextHints = result.Success
            ? new[] { "查看书城正文", "继续下一章", "审查下一章" }
            : new[] { "查看失败项", "继续修订", "请用户补充设定" };

        return new AgentToolExecutionResult
        {
            Success = result.Success,
            Risk = NovelToolRiskLevel.High.ToString(),
            Message = message,
            RunId = result.Run?.RunId ?? content.SourceRunId,
            Phase = session.Phase,
            Data = result,
            Artifact = BuildArtifact(
                result.Success ? "committed_chapter_revision" : "committed_chapter_revision_blocked",
                content.ChapterId,
                session.ActiveProjectId,
                result.Run?.RunId ?? content.SourceRunId,
                message,
                nextHints),
            Suggestions = nextHints
        };
    }

    private async Task<RevisionPlanItem?> ResolveRevisionPlanForCommittedRevisionAsync(
        AgentSession session,
        ProjectContentQueryItem content,
        string revisionPlanId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) ||
            string.IsNullOrWhiteSpace(session.ActiveProjectId) ||
            string.IsNullOrWhiteSpace(revisionPlanId))
        {
            return null;
        }

        using var scope = _serviceProvider.CreateScope();
        var revisionService = scope.ServiceProvider.GetService<IRevisionPlanService>();
        if (revisionService == null)
            return null;

        var query = await revisionService.QueryAsync(
                new QueryRevisionPlansRequest(
                    UserId: session.UserId,
                    ProjectId: session.ActiveProjectId,
                    Status: string.Empty,
                    TargetChapterId: content.ChapterId,
                    Limit: 80),
                ct)
            .ConfigureAwait(false);

        return query.Items.FirstOrDefault(plan =>
            string.Equals(plan.Id, revisionPlanId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static AgentToolCall ForceProjectContentBodyRead(AgentToolCall call)
    {
        var args = new Dictionary<string, string>(call.Arguments, StringComparer.OrdinalIgnoreCase)
        {
            ["includeBody"] = "true",
            ["includeFacts"] = "true"
        };
        return new AgentToolCall { Name = "QueryProjectContent", Arguments = args };
    }

    private static ProjectContentQueryItem? AssertProjectContentItem(AgentToolExecutionResult contentResult)
    {
        if (contentResult.Data is not ProjectContentQueryResult query)
            return null;
        return query.Items.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Body));
    }

    private async Task<ChapterContextPackageSummary> BuildCommittedChapterAuditContextAsync(
        AgentSession session,
        StoryBibleDocument bible,
        ProjectContentQueryItem content,
        string focus,
        CancellationToken ct)
    {
        var sourceRun = bible.AgentRuns
            .Where(r => IsSameChapterIdentity(
                FirstNonEmpty(r.TargetChapterId, r.ChapterBrief?.ChapterId),
                content.ChapterId,
                content.ChapterNumber))
            .OrderByDescending(r => r.UpdatedAt)
            .FirstOrDefault();
        var package = CloneContextPackage(sourceRun?.ContextPackage);
        package.ChapterId = content.ChapterId;
        package.Status = "committed_audit";

        if (bible.Constitution != null)
        {
            AddIfPresent(package.WorldRules, bible.Constitution.WorldCoreRule);
            AddIfPresent(package.WorldRules, bible.Constitution.MainConflictEngine);
            AddIfPresent(package.WorldRules, bible.Constitution.ProtagonistEngine);
            AddIfPresent(package.HardContinuityFacts, bible.Constitution.WorldCoreRule);
            foreach (var forbidden in bible.Constitution.ForbiddenDirections)
                AddIfPresent(package.HardContinuityFacts, forbidden);
        }

        foreach (var canon in bible.CanonLedger
                     .Where(c => c.Status is CanonLedgerEntryStatus.Canon or CanonLedgerEntryStatus.Proposed)
                     .OrderByDescending(c => c.UpdatedAt)
                     .Take(12))
        {
            AddIfPresent(package.HardContinuityFacts, $"{canon.Title}：{canon.Content}");
        }

        foreach (var facts in SelectContinuityFactsForChapter(bible, content))
        {
            foreach (var line in FormatContinuityFactLines(facts))
                AddIfPresent(package.HardContinuityFacts, line);
            if (!string.IsNullOrWhiteSpace(facts.EndingState))
                AddIfPresent(package.PreviousSummaries, $"{facts.ChapterId}: {facts.EndingState}");
        }

        foreach (var fact in content.ContinuityFacts)
        {
            foreach (var line in FormatContinuityFactLines(fact))
                AddIfPresent(package.HardContinuityFacts, line);
        }

        foreach (var hardFact in await QueryProjectKnowledgeHardFactsAsync(session, ct).ConfigureAwait(false))
        {
            AddIfPresent(package.HardContinuityFacts, hardFact);
            AddIfPresent(package.WorldRules, hardFact);
        }

        AddIfPresent(package.ChapterBlueprints, $"回溯审查对象：第 {content.ChapterNumber} 章《{content.ChapterTitle}》。");
        AddIfPresent(package.PreviousSummaries, $"当前提交正文开头：{content.BodyPreview}");
        AddIfPresent(package.Warnings, focus);
        NormalizeContextLists(package);
        return package;
    }

    private async Task<IReadOnlyList<string>> QueryProjectKnowledgeHardFactsAsync(
        AgentSession session,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) || string.IsNullOrWhiteSpace(session.ActiveProjectId))
            return Array.Empty<string>();

        using var scope = _serviceProvider.CreateScope();
        var queryService = scope.ServiceProvider.GetRequiredService<IProjectKnowledgeBindingQueryService>();

        return await queryService.GetProjectHardFactLinesAsync(session.UserId, session.ActiveProjectId, ct)
            .ConfigureAwait(false);
    }

    private static ChapterContextPackageSummary CloneContextPackage(ChapterContextPackageSummary? source)
    {
        if (source == null)
            return new ChapterContextPackageSummary();

        return new ChapterContextPackageSummary
        {
            ChapterId = source.ChapterId,
            Status = source.Status,
            WorldRules = source.WorldRules.ToList(),
            CharacterStates = source.CharacterStates.ToList(),
            ActiveConflicts = source.ActiveConflicts.ToList(),
            ActiveForeshadowing = source.ActiveForeshadowing.ToList(),
            ChapterBlueprints = source.ChapterBlueprints.ToList(),
            PreviousSummaries = source.PreviousSummaries.ToList(),
            LongDistanceRecall = source.LongDistanceRecall.ToList(),
            RagQueries = source.RagQueries.ToList(),
            HardContinuityFacts = source.HardContinuityFacts.ToList(),
            KnowledgeBindings = source.KnowledgeBindings
                .Select(CloneBoundKnowledgeSnapshot)
                .ToList(),
            AcceptedCreativeIntents = source.AcceptedCreativeIntents
                .Select(CloneAcceptedCreativeIntentSnapshot)
                .ToList(),
            SourceRevisionPlans = source.SourceRevisionPlans
                .Select(CloneRevisionPlanSnapshot)
                .ToList(),
            Warnings = source.Warnings.ToList(),
            BuiltAt = source.BuiltAt
        };
    }

    private static IEnumerable<ChapterContinuityFacts> SelectContinuityFactsForChapter(
        StoryBibleDocument bible,
        ProjectContentQueryItem content)
    {
        return bible.ContinuityFacts
            .Select(f => new { Facts = f, Number = ExtractTrailingNumber(f.ChapterId) })
            .Where(x => x.Number <= 0 || x.Number <= content.ChapterNumber)
            .OrderByDescending(x => x.Number)
            .Take(4)
            .OrderBy(x => x.Number)
            .Select(x => x.Facts);
    }

    private static IEnumerable<string> FormatContinuityFactLines(ChapterContinuityFacts facts)
    {
        if (!string.IsNullOrWhiteSpace(facts.ProtagonistName)) yield return $"主角姓名：{facts.ProtagonistName}";
        if (!string.IsNullOrWhiteSpace(facts.ProtagonistIdentity)) yield return $"主角身份：{facts.ProtagonistIdentity}";
        if (!string.IsNullOrWhiteSpace(facts.ProtagonistStatus)) yield return $"主角当前状态：{facts.ProtagonistStatus}";
        if (!string.IsNullOrWhiteSpace(facts.CurrentLocation)) yield return $"当前位置：{facts.CurrentLocation}";
        if (!string.IsNullOrWhiteSpace(facts.SystemState)) yield return $"系统状态：{facts.SystemState}";
        if (!string.IsNullOrWhiteSpace(facts.EquipmentState)) yield return $"装备状态：{facts.EquipmentState}";
        foreach (var keyEvent in facts.KeyEvents.Where(x => !string.IsNullOrWhiteSpace(x)).Take(8))
            yield return $"已发生事件：{keyEvent}";
        if (!string.IsNullOrWhiteSpace(facts.EndingState)) yield return $"上一章结尾状态：{facts.EndingState}";
        foreach (var carry in facts.NextChapterMustCarry.Where(x => !string.IsNullOrWhiteSpace(x)).Take(8))
            yield return $"下一章必须承接：{carry}";
    }

    private static void NormalizeContextLists(ChapterContextPackageSummary package)
    {
        package.WorldRules = NormalizeDistinct(package.WorldRules, 24);
        package.CharacterStates = NormalizeDistinct(package.CharacterStates, 24);
        package.ActiveConflicts = NormalizeDistinct(package.ActiveConflicts, 24);
        package.ActiveForeshadowing = NormalizeDistinct(package.ActiveForeshadowing, 24);
        package.ChapterBlueprints = NormalizeDistinct(package.ChapterBlueprints, 24);
        package.PreviousSummaries = NormalizeDistinct(package.PreviousSummaries, 24);
        package.LongDistanceRecall = NormalizeDistinct(package.LongDistanceRecall, 24);
        package.RagQueries = NormalizeDistinct(package.RagQueries, 24);
        package.HardContinuityFacts = NormalizeDistinct(package.HardContinuityFacts, 48);
        package.KnowledgeBindings = package.KnowledgeBindings
            .Where(binding => !string.IsNullOrWhiteSpace(binding.KnowledgeId))
            .GroupBy(binding => binding.KnowledgeId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(24)
            .Select(CloneBoundKnowledgeSnapshot)
            .ToList();
        package.AcceptedCreativeIntents = package.AcceptedCreativeIntents
            .Where(intent => !string.IsNullOrWhiteSpace(intent.IntentId))
            .GroupBy(intent => intent.IntentId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(24)
            .Select(CloneAcceptedCreativeIntentSnapshot)
            .ToList();
        package.SourceRevisionPlans = package.SourceRevisionPlans
            .Where(plan => !string.IsNullOrWhiteSpace(plan.RevisionPlanId))
            .GroupBy(plan => plan.RevisionPlanId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(16)
            .Select(CloneRevisionPlanSnapshot)
            .ToList();
        package.Warnings = NormalizeDistinct(package.Warnings, 24);
    }

    private static BoundKnowledgeSnapshot CloneBoundKnowledgeSnapshot(BoundKnowledgeSnapshot source) => new()
    {
        KnowledgeId = source.KnowledgeId,
        Title = source.Title,
        EntryType = source.EntryType,
        Content = source.Content,
        Tags = source.Tags.ToList(),
        Weight = source.Weight,
        SourceProjectId = source.SourceProjectId,
        ProjectUsageStatus = source.ProjectUsageStatus,
        ProjectUsageCount = source.ProjectUsageCount,
        SourceSessionId = source.SourceSessionId,
        SourceRunId = source.SourceRunId,
        Note = source.Note,
        Role = source.Role,
        Scope = source.Scope,
        Priority = source.Priority,
        ConstraintLevel = source.ConstraintLevel,
        PackagePolicy = source.PackagePolicy,
        BoundVersion = source.BoundVersion,
        UsedByChapters = source.UsedByChapters.ToList(),
        ClassificationId = source.ClassificationId,
        ClassificationModel = source.ClassificationModel,
        ClassificationRule = source.ClassificationRule,
        TargetEntities = source.TargetEntities.ToList(),
        ShouldEnterGate = source.ShouldEnterGate,
        ShouldEnterBlueprint = source.ShouldEnterBlueprint,
        ShouldEnterFactSnapshot = source.ShouldEnterFactSnapshot,
        ClassificationConfidence = source.ClassificationConfidence
    };

    private static AcceptedCreativeIntentSnapshot CloneAcceptedCreativeIntentSnapshot(AcceptedCreativeIntentSnapshot source) => new()
    {
        IntentId = source.IntentId,
        NormalizedIntent = source.NormalizedIntent,
        TargetScope = source.TargetScope,
        TargetChapterId = source.TargetChapterId,
        TargetVolumeId = source.TargetVolumeId,
        TargetCharacterName = source.TargetCharacterName,
        ImpactLevel = source.ImpactLevel,
        Source = source.Source,
        DecisionReason = source.DecisionReason,
        CreatedAt = source.CreatedAt
    };

    private static RevisionPlanSnapshot CloneRevisionPlanSnapshot(RevisionPlanSnapshot source) => new()
    {
        RevisionPlanId = source.RevisionPlanId,
        PlanType = source.PlanType,
        TargetScope = source.TargetScope,
        TargetChapterId = source.TargetChapterId,
        TargetChapterLogicalId = source.TargetChapterLogicalId,
        TargetChapterDisplayName = source.TargetChapterDisplayName,
        Status = source.Status,
        RequirementsJson = source.RequirementsJson,
        ContinuityRequirementsJson = source.ContinuityRequirementsJson,
        AffectedChapterIdsJson = source.AffectedChapterIdsJson,
        InvalidatedPackageIdsJson = source.InvalidatedPackageIdsJson,
        RiskLevel = source.RiskLevel,
        Recommendation = source.Recommendation
    };

    private static bool InjectRevisionPlanSnapshot(ChapterContextPackageSummary package, RevisionPlanItem plan)
    {
        if (package.SourceRevisionPlans.Any(existing => string.Equals(existing.RevisionPlanId, plan.Id, StringComparison.OrdinalIgnoreCase)))
            return false;

        package.SourceRevisionPlans.Add(new RevisionPlanSnapshot
        {
            RevisionPlanId = plan.Id,
            PlanType = plan.PlanType,
            TargetScope = plan.TargetScope,
            TargetChapterId = plan.TargetChapterId,
            TargetChapterLogicalId = plan.TargetChapterLogicalId,
            TargetChapterDisplayName = plan.TargetChapterDisplayName,
            Status = plan.Status,
            RequirementsJson = plan.RequirementsJson,
            ContinuityRequirementsJson = plan.ContinuityRequirementsJson,
            AffectedChapterIdsJson = plan.AffectedChapterIdsJson,
            InvalidatedPackageIdsJson = plan.InvalidatedPackageIdsJson,
            RiskLevel = plan.RiskLevel,
            Recommendation = plan.Recommendation
        });

        foreach (var requirement in ParseJsonStringArray(plan.RequirementsJson).Take(8))
            AddIfPresent(package.HardContinuityFacts, $"修订计划要求：{requirement}");
        foreach (var requirement in ParseJsonStringArray(plan.ContinuityRequirementsJson).Take(8))
            AddIfPresent(package.HardContinuityFacts, $"修订连续性要求：{requirement}");
        AddIfPresent(package.Warnings, $"来源修订计划：{plan.Id} / {plan.PlanType} / {plan.RiskLevel}");
        if (!string.IsNullOrWhiteSpace(plan.Recommendation))
            AddIfPresent(package.ChapterBlueprints, $"修订计划建议：{plan.Recommendation}");
        return true;
    }

    private static List<string> NormalizeDistinct(IEnumerable<string> values, int take) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToList();

    private static void AddIfPresent(ICollection<string> values, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            values.Add(value.Trim());
    }

    private static string FormatCommittedChapterAudit(
        ProjectContentQueryItem content,
        NovelAgentExecutionResult result)
    {
        var report = result.GateReport;
        if (report == null)
            return $"{content.ChapterTitle} 已审查，但没有返回门禁报告。";

        if (report.Status == "validated")
            return $"{content.ChapterTitle} 已通过已提交章节回溯审查。";

        var issues = report.Issues.Count == 0
            ? "未返回具体问题。"
            : string.Join("；", report.Issues.Take(6));
        var userVisibleHints = report.RepairHints
            .Where(hint => !IsInternalAuditDiagnostic(hint))
            .Take(4)
            .ToList();
        var hints = userVisibleHints.Count == 0
            ? string.Empty
            : "\n修订提示：" + string.Join("；", userVisibleHints);
        return $"{content.ChapterTitle} 回溯审查未通过：{issues}{hints}";
    }

    private static string FormatBookValidationReport(BookValidationReport report)
    {
        var title = string.IsNullOrWhiteSpace(report.ProjectTitle)
            ? report.ProjectId
            : report.ProjectTitle;
        var range = report.EndChapterNumber > 0
            ? $"第 {report.StartChapterNumber}-{report.EndChapterNumber} 章"
            : "当前章节";
        var lines = new List<string>
        {
            $"{title} 整书校验：{report.OverallStatus}，范围 {range}，已读取 {report.Chapters.Count} 章，发现 {report.Issues.Count} 个问题。"
        };

        foreach (var issue in report.Issues.Take(8))
        {
            var chapter = issue.ChapterNumber > 0 ? $"第 {issue.ChapterNumber} 章" : "项目";
            lines.Add($"- [{issue.Severity}/{issue.Code}] {chapter}：{issue.Message}");
        }

        var healthy = report.Chapters
            .Where(chapter => report.Issues.All(issue => issue.ChapterNumber != chapter.ChapterNumber))
            .OrderBy(chapter => chapter.ChapterNumber)
            .Take(4)
            .Select(chapter => $"第 {chapter.ChapterNumber} 章《{chapter.Title}》")
            .ToList();
        if (healthy.Count > 0)
            lines.Add($"无明显结构问题章节：{string.Join("、", healthy)}。");

        return string.Join("\n", lines);
    }

    private static string[] BuildBookValidationSuggestions(BookValidationReport report)
    {
        if (report.Issues.Count == 0)
            return new[] { "继续下一章生产", "查询项目内容", "刷新项目索引" };

        var suggestions = new List<string> { "QueryProjectContent", "QueryNovelProductionState" };
        if (report.Issues.Any(issue => issue.Code is "missing_chapter" or "chapter_not_committed" or "missing_body"))
        {
            suggestions.Add("PlanChapter");
            suggestions.Add("ProduceChapter");
        }

        if (report.Issues.Any(issue => issue.Code is "protagonist_continuity_mismatch" or "carry_not_reflected" or "missing_fact_snapshot"))
        {
            suggestions.Add("CreateRevisionPlan");
            suggestions.Add("ReviseCommittedChapter");
        }

        return suggestions
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
    }

    private static bool IsInternalAuditDiagnostic(string hint) =>
        ContainsAny(hint,
            "Project business data",
            "filesystem paths are disabled");

    private async Task<AgentToolExecutionResult> SearchCreativeKnowledgeAsync(AgentToolCall call, AgentSession session, CancellationToken ct)
    {
        var query = Arg(call, "query");
        var result = await _workspace.Orchestrator.RetrieveCreativeKnowledgeAsync(query, ct).ConfigureAwait(false);
        var dbHits = await SearchDatabaseKnowledgeAsync(query, session, ct).ConfigureAwait(false);

        if (dbHits.Count > 0)
        {
            var seen = result.Hits.Select(h => h.Entry.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var hit in dbHits)
            {
                if (seen.Add(hit.Entry.Id))
                    result.Hits.Insert(0, hit);
            }

            RebuildKnowledgeBuckets(result);
            result.Message = $"创意知识库命中 {result.Hits.Count} 条（SQLite/Qdrant/Redis 知识通路）。";
        }

        var lines = result.Hits.Count == 0
            ? new List<string> { "创意知识库里没有检索到强相关条目。" }
            : result.Hits.Take(8).Select(h => $"【{h.Entry.Category}】{h.Entry.Title}\n{h.Entry.Content}").ToList();

        return new AgentToolExecutionResult
        {
            Success = true,
            Message = string.Join("\n\n", lines),
            Phase = "knowledge_retrieved",
            Data = result,
            Artifact = BuildArtifact("knowledge_hits", query, string.Empty, string.Empty, $"检索到 {result.Hits.Count} 条创意知识。", new[] { "基于知识继续构思", "规划下一章" }),
            Suggestions = new[] { "基于这些知识继续构思", "规划下一章" },
        };
    }

    private async Task<List<CreativeKnowledgeHit>> SearchDatabaseKnowledgeAsync(string query, AgentSession session, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            _logger.LogDebug("Skipping DB knowledge search because session {SessionId} has no active project", session.SessionId);
            return new List<CreativeKnowledgeHit>();
        }

        using var scope = _serviceProvider.CreateScope();
        var knowledgeService = scope.ServiceProvider.GetRequiredService<IKnowledgeService>();
        var results = await knowledgeService.SearchKnowledgeAsync(new SearchKnowledgeRequest
        {
            ProjectId = session.ActiveProjectId,
            Query = query,
            TopK = 8
        }, ct).ConfigureAwait(false);

        foreach (var result in results)
        {
            await knowledgeService.IncrementUsageAsync(
                result.Id,
                session.ActiveProjectId,
                session.SessionId,
                session.ActiveRunId,
                $"agent-knowledge-search:{FirstNonEmpty(session.ActiveRunId, session.SessionId)}:{ComputeStableHash(query)}:{result.Id}",
                ct).ConfigureAwait(false);
        }

        return results
            .Select(MapKnowledgeSearchResult)
            .ToList();
    }

    private async Task<int> CountProjectMaterialsAsync(AgentSession session, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.ActiveProjectId))
            return 0;

        using var scope = _serviceProvider.CreateScope();
        var materialService = scope.ServiceProvider.GetRequiredService<IMaterialService>();
        return await materialService
            .CountProjectMaterialsAsync(session.UserId, session.ActiveProjectId, ct)
            .ConfigureAwait(false);
    }

    private static CreativeKnowledgeHit MapKnowledgeSearchResult(KnowledgeSearchResult result)
    {
        return new CreativeKnowledgeHit
        {
            Entry = new CreativeKnowledgeEntry
            {
                Id = result.Id,
                Category = ParseKnowledgeCategory(result.EntryType),
                Title = result.Title,
                Content = result.Content,
                Source = "DBKnowledge"
            },
            Score = result.Score,
            Reason = "DB knowledge search"
        };
    }

    private static CreativeKnowledgeCategory ParseKnowledgeCategory(string value) =>
        Enum.TryParse<CreativeKnowledgeCategory>(value, ignoreCase: true, out var category)
            ? category
            : CreativeKnowledgeCategory.ProjectUsedPattern;

    private static void RebuildKnowledgeBuckets(CreativeKnowledgeRetrievalResult result)
    {
        result.GenrePrinciples = KnowledgeContents(result.Hits, CreativeKnowledgeCategory.GenrePrinciple, 4);
        result.TropeWarnings = KnowledgeContents(result.Hits, CreativeKnowledgeCategory.TropePattern, 4);
        result.AntiTropeStrategies = KnowledgeContents(result.Hits, CreativeKnowledgeCategory.AntiTropeStrategy, 5);
        result.EmotionRelationshipGuides = KnowledgeContents(result.Hits, CreativeKnowledgeCategory.EmotionArc, 4)
            .Concat(KnowledgeContents(result.Hits, CreativeKnowledgeCategory.RelationshipDynamic, 4))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
        result.ProjectMemory = KnowledgeContents(result.Hits, CreativeKnowledgeCategory.ProjectUsedPattern, 5);
        result.HardFacts = KnowledgeContents(result.Hits, CreativeKnowledgeCategory.HardFact, 8);
    }

    private static List<string> KnowledgeContents(
        IEnumerable<CreativeKnowledgeHit> hits,
        CreativeKnowledgeCategory category,
        int take) =>
        hits
            .Where(hit => hit.Entry.Category == category)
            .Select(hit => hit.Entry.Content)
            .Where(content => !string.IsNullOrWhiteSpace(content))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToList();

    private async Task<AgentToolExecutionResult> PlanStoryFoundationAsync(AgentToolCall call, AgentSession session, CancellationToken ct)
    {
        _logger.LogInformation(
            "PlanStoryFoundation starting: sessionProject={SessionProjectId}, workspaceProject={WorkspaceProjectId}, user={UserId}",
            session.ActiveProjectId,
            _workspace.ProjectId,
            _workspace.UserId);

        var settings = await _settingsManager.LoadAsync(ct).ConfigureAwait(false);
        var userSeed = Arg(call, "userSeed", Arg(call, "creativeBrief", session.WorkingMemory.CurrentGoal));
        var genre = FirstNonEmpty(Arg(call, "genre"), settings.DefaultGenre);
        var subGenre = Arg(call, "subGenre", settings.DefaultSubGenre);
        var candidateDirections = ArgList(call, "candidateDirections");
        if (candidateDirections.Count == 0)
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "PlanStoryFoundation 需要 candidateDirections。Agent 必须先给出结构化故事方向，工具不会从用户原话里关键词推断候选。",
                Phase = session.Phase,
                Suggestions = new[] { "补充候选方向", "先创建创意意图" },
            };
        }

        var run = await _workspace.Orchestrator.PlanStoryFoundationAsync(new StoryFoundationRequest
        {
            UserSeed = userSeed,
            Genre = genre,
            SubGenre = subGenre,
            CandidateDirections = candidateDirections,
            ForbiddenDirections = ArgList(call, "forbiddenDirections"),
        }, ct).ConfigureAwait(false);

        session.ActiveRunId = run.RunId;
        session.RunHistory.Add(run.RunId);
        session.Phase = "foundation_candidates";
        var hasCandidates = run.MacroCandidates.Count > 0;

        return new AgentToolExecutionResult
        {
            Success = hasCandidates,
            Message = FormatFoundationCandidates(run),
            RunId = run.RunId,
            Phase = session.Phase,
            Data = run,
            Artifact = hasCandidates
                ? BuildArtifact("story_foundation_candidates", run.RunId, session.ActiveProjectId, run.RunId, $"生成 {run.MacroCandidates.Count} 个故事地基候选。", run.MacroCandidates.Select(c => c.Title).Take(3).ToArray())
                : null,
            Suggestions = hasCandidates
                ? run.MacroCandidates.Select((c, i) => $"选第{i + 1}个: {c.Title}").ToArray()
                : new[] { "补充正向创作方向", "重新生成故事地基" },
        };
    }

    private async Task<AgentToolExecutionResult> CommitStoryFoundationAsync(AgentToolCall call, AgentSession session, bool confirmed, CancellationToken ct)
    {
        var runId = Arg(call, "runId", session.ActiveRunId ?? string.Empty);
        var selectedId = Arg(call, "selectedMacroCandidateId");
        var selectedIndex = ArgInt(call, "selectedMacroCandidateIndex");
        var result = await _workspace.Orchestrator.CommitStoryFoundationAsync(runId, false, confirmed, selectedId, selectedIndex, ct).ConfigureAwait(false);
        session.Phase = result.Success ? "foundation_committed" : session.Phase;
        if (result.Success)
            await _catalog.UpdateFromCurrentStoryBibleAsync(session.ActiveProjectId, ct).ConfigureAwait(false);
        return new AgentToolExecutionResult
        {
            Success = result.Success,
            RequiresConfirmation = false,
            Risk = result.RiskLevel.ToString(),
            Message = result.Message,
            RunId = runId,
            Phase = session.Phase,
            Data = result,
            Artifact = BuildArtifact("story_foundation_commit", runId, session.ActiveProjectId, runId, result.Message, result.Success ? new[] { "规划第一卷", "查看当前状态" } : new[] { "重新选择候选", "修改候选" }),
            Suggestions = result.Success ? new[] { "规划第一卷", "查看当前状态" } : new[] { "重新选择候选", "修改候选" },
        };
    }

    private async Task<AgentToolExecutionResult> PlanVolumeArcAsync(
        AgentToolCall call,
        AgentSession session,
        StoryBibleDocument bible,
        CancellationToken ct)
    {
        var creativeBrief = Arg(call, "creativeBrief");
        if (string.IsNullOrWhiteSpace(creativeBrief))
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "PlanVolumeArc 需要 creativeBrief。Agent 必须先根据上下文自主整理卷级创作简报，工具不会代写。",
                Phase = session.Phase,
            };

        var candidateDirections = ArgList(call, "candidateDirections");
        if (candidateDirections.Count == 0)
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "PlanVolumeArc 需要 candidateDirections。Agent 必须先给出结构化卷级候选方向，工具不会从 creativeBrief、用户原话或 Story Bible 里关键词推断。",
                Phase = session.Phase,
            };

        var settings = await _settingsManager.LoadAsync(ct).ConfigureAwait(false);
        var chapterCount = Math.Clamp(
            ArgInt(call, "expectedChapterCount", settings.DefaultVolumeChapterCount),
            1,
            200);
        var nextVolumeNumber = NextVolumeNumber(bible);
        var startChapterNumber = NextStartChapterNumber(bible);
        var volumeId = Arg(call, "volumeId", $"volume-{nextVolumeNumber:000}");
        var volumeTitle = Arg(call, "volumeTitle");
        var startChapterId = Arg(call, "startChapterId", $"chapter-{startChapterNumber:000}");
        var endChapterId = Arg(call, "endChapterId", $"chapter-{(startChapterNumber + chapterCount - 1):000}");
        var run = await _workspace.Orchestrator.PlanVolumeArcAsync(new VolumeArcPlanningRequest
        {
            UserGoal = creativeBrief,
            VolumeId = volumeId,
            VolumeTitle = volumeTitle,
            StartChapterId = startChapterId,
            EndChapterId = endChapterId,
            ExpectedChapterCount = chapterCount,
            CandidateDirections = candidateDirections,
            ForbiddenDirections = ArgList(call, "forbiddenDirections"),
        }, ct).ConfigureAwait(false);

        session.ActiveRunId = run.RunId;
        session.RunHistory.Add(run.RunId);
        session.Phase = "volume_plan";

        return new AgentToolExecutionResult
        {
            Success = run.VolumeArcPlan != null,
            Message = FormatVolumePlan(run),
            RunId = run.RunId,
            Phase = session.Phase,
            Data = run,
            Artifact = BuildArtifact("volume_arc_candidates", run.RunId, session.ActiveProjectId, run.RunId, run.VolumeArcPlan == null ? "卷规划生成失败。" : $"卷规划「{run.VolumeArcPlan.Title}」已生成。", new[] { "提交入库", "调整卷规划" }),
            Suggestions = new[] { "提交入库", "调整卷规划" },
        };
    }

    private async Task<AgentToolExecutionResult> CommitVolumeArcAsync(AgentToolCall call, AgentSession session, bool confirmed, CancellationToken ct)
    {
        var runId = await ResolveVolumeArcRunIdAsync(
            Arg(call, "runId", session.ActiveRunId ?? string.Empty),
            session,
            ct).ConfigureAwait(false);
        var overwrite = ArgBool(call, "overwrite");
        var result = await _workspace.Orchestrator.CommitVolumeArcAsync(runId, overwrite, confirmed, ct).ConfigureAwait(false);
        session.Phase = result.Success ? "volume_committed" : session.Phase;
        if (result.Success)
        {
            session.ActiveRunId = runId;
            await _catalog.UpdateFromCurrentStoryBibleAsync(session.ActiveProjectId, ct).ConfigureAwait(false);
        }
        return new AgentToolExecutionResult
        {
            Success = result.Success,
            RequiresConfirmation = false,
            Risk = result.RiskLevel.ToString(),
            Message = result.Message,
            RunId = runId,
            Phase = session.Phase,
            Data = result,
            Artifact = BuildArtifact("volume_arc_commit", runId, session.ActiveProjectId, runId, result.Message, result.Success ? new[] { "规划第一章", "查看当前状态" } : new[] { "继续修改", "查看当前状态" }),
            Suggestions = result.Success ? new[] { "规划第一章", "查看当前状态" } : new[] { "继续修改", "查看当前状态" },
        };
    }

    private async Task<string> ResolveVolumeArcRunIdAsync(string requestedRunId, AgentSession session, CancellationToken ct)
    {
        var document = await _workspace.Orchestrator.GetStoryBibleAsync(ct).ConfigureAwait(false);
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(requestedRunId))
            candidates.Add(requestedRunId.Trim());
        if (!string.IsNullOrWhiteSpace(session.ActiveRunId))
            candidates.Add(session.ActiveRunId.Trim());
        candidates.AddRange(session.RunHistory
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Reverse()
            .Select(x => x.Trim()));

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var run = document.AgentRuns.FirstOrDefault(r =>
                string.Equals(r.RunId, candidate, StringComparison.OrdinalIgnoreCase) &&
                r.VolumeArcPlan != null);
            if (run != null)
                return run.RunId;
        }

        var latestDraft = document.AgentRuns
            .Where(r => r.Intent == NovelAgentIntent.PlanVolumeArc &&
                        r.VolumeArcPlan != null &&
                        r.Status is not (NovelAgentRunStatus.Completed or NovelAgentRunStatus.Failed or NovelAgentRunStatus.Cancelled))
            .OrderByDescending(r => r.UpdatedAt)
            .FirstOrDefault();

        return latestDraft?.RunId ?? requestedRunId;
    }

    private async Task<AgentToolExecutionResult> PlanChapterAsync(AgentToolCall call, AgentSession session, StoryBibleDocument bible, CancellationToken ct)
    {
        if (call.Arguments.ContainsKey("userGoal"))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "PlanChapter 已废弃 userGoal 参数。请使用 creativeBrief 和 sourceTurnId。",
                Phase = session.Phase,
                Suggestions = new[] { "查看当前状态", "补充章节创作简报" },
            };
        }

        var creativeBrief = Arg(call, "creativeBrief");
        if (string.IsNullOrWhiteSpace(creativeBrief))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "PlanChapter 需要 creativeBrief。Agent 不能把用户原话直接塞进章节工具。",
                Phase = session.Phase,
                Suggestions = new[] { "给出章节创作简报", "查看当前任务" },
            };
        }
        var candidateDirections = ArgList(call, "candidateDirections");
        if (candidateDirections.Count == 0)
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "PlanChapter 需要 candidateDirections。Agent 必须先给出结构化章节候选方向，工具不会从 creativeBrief 或用户原话里关键词推断候选。",
                Phase = session.Phase,
                Suggestions = new[] { "补充章节候选方向", "查看当前任务" },
            };
        }

        var chapterId = Arg(call, "chapterId");
        if (string.IsNullOrWhiteSpace(chapterId))
        {
            var existing = bible.AgentRuns.Where(r => !string.IsNullOrWhiteSpace(r.TargetChapterId)).Select(r => r.TargetChapterId).Distinct().Count();
            chapterId = $"chapter-{(existing + 1):000}";
        }

        var run = await _workspace.Orchestrator.PlanChapterAsync(new ChapterCreativeRequest
        {
            UserGoal = creativeBrief,
            ChapterId = chapterId,
            CandidateDirections = candidateDirections,
            ForbiddenDirections = ArgList(call, "forbiddenDirections"),
        }, ct).ConfigureAwait(false);

        session.ActiveRunId = run.RunId;
        session.RunHistory.Add(run.RunId);
        session.Phase = "chapter_candidates";

        return new AgentToolExecutionResult
        {
            Success = run.ChapterBrief?.Candidates.Count > 0,
            Message = FormatChapterCandidates(run),
            RunId = run.RunId,
            Phase = session.Phase,
            Data = run,
            Artifact = BuildArtifact("chapter_candidates", run.TargetChapterId, session.ActiveProjectId, run.RunId, $"为 {run.TargetChapterId} 生成章节候选。", new[] { "选择推荐，开始生成", "选其他候选" }),
            Suggestions = new[] { "选择推荐，开始生成", "选其他候选" },
        };
    }

    private async Task<AgentToolExecutionResult> SelectChapterCandidateAsync(AgentToolCall call, AgentSession session, bool confirmed, CancellationToken ct)
    {
        var runId = Arg(call, "runId", session.ActiveRunId ?? string.Empty);
        var result = await _workspace.Orchestrator.SelectChapterCandidateAsync(
            runId,
            Arg(call, "candidateTitles"),
            Arg(call, "selectionMode", "Recommended"),
            Arg(call, "selectionRationale"),
            true,
            ct,
            candidateIndex: ArgInt(call, "candidateIndex", ArgInt(call, "selectedCandidateIndex"))).ConfigureAwait(false);

        session.Phase = result.Success ? "candidate_selected" : session.Phase;
        return new AgentToolExecutionResult
        {
            Success = result.Success,
            RequiresConfirmation = false,
            Risk = result.RiskLevel.ToString(),
            Message = result.Success ? "章节方向已选定。接下来可以开始生成正文。" : result.Message,
            RunId = runId,
            Phase = session.Phase,
            Data = result,
            Artifact = BuildArtifact("chapter_candidate_selection", runId, session.ActiveProjectId, runId, result.Success ? "章节方向已选定。" : result.Message, result.Success ? new[] { "开始生成正文", "先看简报" } : new[] { "换一个候选", "重新规划这一章" }),
            Suggestions = result.Success ? new[] { "开始生成正文", "先看简报" } : new[] { "换一个候选", "重新规划这一章" },
        };
    }

    private static string BuildConfirmationMessage(string toolName) =>
        toolName switch
        {
            "CommitStoryFoundation" => "这个操作会把故事地基写入 Story Bible，并继续推进后续规划。",
            "CommitVolumeArc" => "这个操作会把卷规划写入 Story Bible，并继续推进章节生产线。",
            "ProduceChapter" => "这个操作会自动完成章节生产闭环：构建上下文包、生成正文、硬门禁、必要修复、质量评审、提交书城和事实回写。",
            _ => "这个操作会改变小说工程状态，并由 Agent 继续推进。",
        };

    private async Task<AgentToolExecutionResult> ProduceChapterAsync(
        AgentToolCall call,
        AgentSession session,
        bool confirmed,
        CancellationToken ct)
    {
        var runId = Arg(call, "runId", session.ActiveRunId ?? string.Empty);
        var revisionPlanId = FirstNonEmpty(
            Arg(call, "revisionPlanId"),
            Arg(call, "sourceRevisionPlanId"),
            Arg(call, "planId"));
        var maxRepairAttempts = Math.Clamp(ArgInt(call, "maxRepairAttempts", 2), 0, 5);
        var maxAgentReviewRewriteAttempts = Math.Min(maxRepairAttempts, 2);
        var commitPolicy = NormalizeProduceChapterCommitPolicy(Arg(call, "commitPolicy", "auto_commit"));
        var executedStages = new List<string>();
        await ThrowIfRuntimeRunCancelledAsync(session, "ProduceChapter", ct).ConfigureAwait(false);
        var targetChapterId = await ResolveProduceChapterTargetChapterIdAsync(runId, ct).ConfigureAwait(false);
        var productionLeaseResult = await TryAcquireChapterProductionLeaseAsync(
                session,
                runId,
                targetChapterId,
                ct)
            .ConfigureAwait(false);
        if (productionLeaseResult.Required && productionLeaseResult.Lease == null)
            return BuildChapterProductionBusyResult(session, runId, targetChapterId);
        await using var productionLease = productionLeaseResult.Lease;

        Dictionary<string, string> BuildClosedLoopRecommendedArguments(string nextCommitPolicy = "")
        {
            var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["runId"] = runId,
                ["maxRepairAttempts"] = maxRepairAttempts.ToString()
            };
            if (!string.IsNullOrWhiteSpace(revisionPlanId))
                args["revisionPlanId"] = revisionPlanId;
            args["commitPolicy"] = string.IsNullOrWhiteSpace(nextCommitPolicy) ? commitPolicy : nextCommitPolicy;
            return args;
        }

        AgentToolExecutionResult? StopIfFailed(
            string stage,
            AgentToolExecutionResult result,
            IReadOnlyList<string> nextHints)
        {
            executedStages.Add(stage);
            if (result.Success)
                return null;

            var message = BuildProduceChapterFailureMessage(stage, result, executedStages, nextHints);
            var artifact = BuildArtifact(
                "chapter_production_blocked",
                runId,
                session.ActiveProjectId,
                runId,
                message,
                nextHints);
            return new AgentToolExecutionResult
            {
                Success = false,
                RequiresConfirmation = false,
                Risk = "High",
                Message = message,
                RunId = runId,
                Phase = session.Phase,
                IsRepairable = result.IsRepairable,
                RecommendedToolName = result.IsRepairable ? "ProduceChapter" : string.Empty,
                RecommendedArguments = result.IsRepairable ? BuildClosedLoopRecommendedArguments() : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                MissingPrerequisite = result.MissingPrerequisite,
                Failure = BuildProduceChapterFailure(stage, result, artifact, nextHints, requiresUserDecision: !result.IsRepairable),
                Data = result.Data,
                Artifact = artifact,
                Suggestions = nextHints
            };
        }

        async Task<AgentToolExecutionResult?> StopForInterruptBoundaryAsync(string stage)
        {
            await ThrowIfRuntimeRunCancelledAsync(session, stage, ct).ConfigureAwait(false);
            var drain = await DrainPendingRuntimeInterruptsAtToolBoundaryAsync(session, runId, stage, ct)
                .ConfigureAwait(false);
            var directionBlock = BuildDirectionChangeBoundaryResult(session, runId, stage, drain);
            if (directionBlock != null)
                executedStages.Add($"{stage}:DirectionChange");
            return directionBlock;
        }

        async Task<(AgentToolExecutionResult ValidateResult, AgentToolExecutionResult? Blocked, int RepairAttempts)> GenerateValidateAndRepairAsync(
            int reviewRewriteAttempt)
        {
            var stageSuffix = reviewRewriteAttempt <= 0 ? string.Empty : $"#review{reviewRewriteAttempt}";
            var interruptBlocked = await StopForInterruptBoundaryAsync(NovelAgentProductionStages.DraftGeneration + stageSuffix)
                .ConfigureAwait(false);
            if (interruptBlocked != null)
                return (interruptBlocked, interruptBlocked, 0);
            var generateResult = await RunDraftGenerationStageAsync(runId, session, confirmed: true, ct).ConfigureAwait(false);
            var blockedResult = StopIfFailed(
                NovelAgentProductionStages.DraftGeneration + stageSuffix,
                generateResult,
                new[] { "检查模型配置", "查看已生成草稿", "重新生成章节" });
            if (blockedResult != null)
                return (generateResult, blockedResult, 0);

            interruptBlocked = await StopForInterruptBoundaryAsync(NovelAgentProductionStages.GateValidation + stageSuffix)
                .ConfigureAwait(false);
            if (interruptBlocked != null)
                return (generateResult, interruptBlocked, 0);
            var validateResult = await RunDraftGateStageAsync(runId, session, ct).ConfigureAwait(false);
            executedStages.Add(NovelAgentProductionStages.GateValidation + stageSuffix);

            var gateRepairAttempts = 0;
            var consecutiveSameIssues = 0; // 连续相同 Issues 计数（智能终止）
            string? lastIssuesFingerprint = null;

            while (!validateResult.Success && gateRepairAttempts < maxRepairAttempts)
            {
                ct.ThrowIfCancellationRequested();

                // 智能终止：检测 RepairHints 是否无效（连续 2 次相同 Issues）
                var currentFingerprint = BuildIssuesFingerprint(validateResult);
                if (currentFingerprint == lastIssuesFingerprint)
                {
                    consecutiveSameIssues++;
                    if (consecutiveSameIssues >= 2)
                    {
                        _logger?.LogWarning(
                            "Rewrite Loop early terminated: same issues repeated {Count} times, RepairHints may be ineffective",
                            consecutiveSameIssues);
                        validateResult.IsRepairable = false; // 标记为不可修复，强制终止
                        break;
                    }
                }
                else
                {
                    consecutiveSameIssues = 0;
                    lastIssuesFingerprint = currentFingerprint;
                }

                interruptBlocked = await StopForInterruptBoundaryAsync($"{NovelAgentProductionStages.DraftRepair}{stageSuffix}#{gateRepairAttempts + 1}")
                    .ConfigureAwait(false);
                if (interruptBlocked != null)
                    return (validateResult, interruptBlocked, gateRepairAttempts);
                gateRepairAttempts++;
                var repairResult = await RunDraftRepairStageAsync(runId, session, confirmed: true, ct).ConfigureAwait(false);
                executedStages.Add($"{NovelAgentProductionStages.DraftRepair}{stageSuffix}#{gateRepairAttempts}");
                validateResult = repairResult;
                if (repairResult.Success || !repairResult.IsRepairable)
                    break;
            }

            if (!validateResult.Success)
            {
                var nextHints = validateResult.IsRepairable
                    ? new[] { "继续修复章节草稿", "查看门禁失败项", "补充创作设定" }
                    : new[] { "查看门禁失败项", "重建章节蓝图", "请求用户确认取舍" };
                var message = BuildProduceChapterFailureMessage(
                    NovelAgentProductionStages.GateValidationOrRepair + stageSuffix,
                    validateResult,
                    executedStages,
                    nextHints);
                var artifact = BuildArtifact("chapter_production_blocked", runId, session.ActiveProjectId, runId, message, nextHints);
                blockedResult = new AgentToolExecutionResult
                {
                    Success = false,
                    RequiresConfirmation = false,
                    Risk = "High",
                    Message = message,
                    RunId = runId,
                    Phase = session.Phase,
                    IsRepairable = validateResult.IsRepairable,
                    RecommendedToolName = validateResult.IsRepairable ? "ProduceChapter" : string.Empty,
                    RecommendedArguments = validateResult.IsRepairable ? BuildClosedLoopRecommendedArguments() : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    MissingPrerequisite = validateResult.MissingPrerequisite,
                    Failure = BuildProduceChapterFailure(
                        NovelAgentProductionStages.GateValidationOrRepair,
                        validateResult,
                        artifact,
                        nextHints,
                        requiresUserDecision: !validateResult.IsRepairable),
                    Data = validateResult.Data,
                    Artifact = artifact,
                    Suggestions = nextHints
                };
                return (validateResult, blockedResult, gateRepairAttempts);
            }

            return (validateResult, null, gateRepairAttempts);
        }

        var boundaryBlocked = await StopForInterruptBoundaryAsync(NovelAgentProductionStages.ContextPackage)
            .ConfigureAwait(false);
        if (boundaryBlocked != null) return boundaryBlocked;
        GuideContextService.RaiseCacheInvalidated();
        var contextResult = await RunBuildContextPackageStageAsync(runId, session, revisionPlanId, ct).ConfigureAwait(false);
        var blocked = StopIfFailed(NovelAgentProductionStages.ContextPackage, contextResult, new[] { "检查章节 Run", "补齐项目上下文", "查询生产状态" });
        if (blocked != null) return blocked;

        var production = await GenerateValidateAndRepairAsync(reviewRewriteAttempt: 0).ConfigureAwait(false);
        blocked = production.Blocked;
        if (blocked != null) return blocked;
        var validateResult = production.ValidateResult;
        var repairAttempts = production.RepairAttempts;

        if (commitPolicy == "draft_only")
        {
            var message = BuildProduceChapterDraftOnlyMessage(validateResult, executedStages, repairAttempts);
            return new AgentToolExecutionResult
            {
                Success = true,
                RequiresConfirmation = false,
                Risk = "Medium",
                Message = message,
                RunId = runId,
                Phase = session.Phase,
                Data = validateResult.Data,
                Artifact = BuildArtifact("chapter_draft_ready", runId, session.ActiveProjectId, runId, message, new[] { "查看草稿", "执行质量评审", "提交前让用户审阅" }),
                Suggestions = new[] { "查看草稿", "执行质量评审", "提交前让用户审阅" },
                RecommendedToolName = "ReviewChapter",
                RecommendedArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["runId"] = runId }
            };
        }

        boundaryBlocked = await StopForInterruptBoundaryAsync(NovelAgentProductionStages.QualityReview)
            .ConfigureAwait(false);
        if (boundaryBlocked != null) return boundaryBlocked;
        var reviewResult = await ReviewChapterAsync(
                new AgentToolCall
                {
                    Name = "ReviewChapter",
                    Arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["runId"] = runId
                    }
                },
                session,
                ct)
            .ConfigureAwait(false);
        executedStages.Add("ReviewChapter");

        var reviewRewriteAttempts = 0;
        if (IsWarningOnlyAgentReview(reviewResult))
        {
            await AcceptWarningOnlyAgentReviewAsync(session, runId, reviewResult, reviewRewriteAttempts, ct)
                .ConfigureAwait(false);
            executedStages.Add("AgentReviewWarningsAccepted");
            reviewResult.Success = true;
        }

        while (!reviewResult.Success &&
               reviewResult.IsRepairable &&
               reviewRewriteAttempts < maxAgentReviewRewriteAttempts)
        {
            ct.ThrowIfCancellationRequested();
            reviewRewriteAttempts++;
            boundaryBlocked = await StopForInterruptBoundaryAsync($"{NovelAgentProductionStages.QualityReview}Feedback#{reviewRewriteAttempts}")
                .ConfigureAwait(false);
            if (boundaryBlocked != null) return boundaryBlocked;
            await ApplyAgentReviewFeedbackToContextPackageAsync(session, runId, reviewResult, reviewRewriteAttempts, ct)
                .ConfigureAwait(false);
            executedStages.Add($"{NovelAgentProductionStages.QualityReview}Feedback#{reviewRewriteAttempts}");

            production = await GenerateValidateAndRepairAsync(reviewRewriteAttempts).ConfigureAwait(false);
            blocked = production.Blocked;
            if (blocked != null) return blocked;
            validateResult = production.ValidateResult;
            repairAttempts += production.RepairAttempts;

            boundaryBlocked = await StopForInterruptBoundaryAsync($"{NovelAgentProductionStages.QualityReview}#{reviewRewriteAttempts}")
                .ConfigureAwait(false);
            if (boundaryBlocked != null) return boundaryBlocked;
            reviewResult = await ReviewChapterAsync(
                    new AgentToolCall
                    {
                        Name = "ReviewChapter",
                        Arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["runId"] = runId
                        }
                    },
                    session,
                    ct)
                .ConfigureAwait(false);
            executedStages.Add($"ReviewChapter#{reviewRewriteAttempts}");
        }

        if (!reviewResult.Success)
        {
            if (IsWarningOnlyAgentReview(reviewResult))
            {
                await AcceptWarningOnlyAgentReviewAsync(session, runId, reviewResult, reviewRewriteAttempts, ct)
                    .ConfigureAwait(false);
                executedStages.Add("AgentReviewWarningsAccepted");
            }
            else
            {
            var canContinueReviewRewrite = reviewResult.IsRepairable &&
                                           reviewRewriteAttempts < maxAgentReviewRewriteAttempts;
            var nextHints = canContinueReviewRewrite
                ? new[] { "查看质量评审", "按评审修订", "重新生成章节" }
                : new[] { "查看质量评审", "按评审修订", "调整章节蓝图", "请求用户确认取舍" };
            var message = BuildProduceChapterFailureMessage("ReviewChapter", reviewResult, executedStages, nextHints);
            var artifact = BuildArtifact("chapter_production_blocked", runId, session.ActiveProjectId, runId, message, nextHints);
            return new AgentToolExecutionResult
            {
                Success = false,
                RequiresConfirmation = false,
                Risk = "High",
                Message = message,
                RunId = runId,
                Phase = session.Phase,
                IsRepairable = canContinueReviewRewrite,
                RecommendedToolName = canContinueReviewRewrite ? "ProduceChapter" : string.Empty,
                RecommendedArguments = canContinueReviewRewrite ? BuildClosedLoopRecommendedArguments() : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                MissingPrerequisite = reviewResult.MissingPrerequisite,
                Failure = BuildProduceChapterFailure(
                    "ReviewChapter",
                    reviewResult,
                    artifact,
                    nextHints,
                    requiresUserDecision: !canContinueReviewRewrite),
                Data = reviewResult.Data,
                Artifact = artifact,
                Suggestions = nextHints
            };
            }
        }

        if (commitPolicy == "require_user_review")
        {
            executedStages.Add("AwaitUserReview");
            var message = BuildProduceChapterUserReviewMessage(reviewResult, executedStages, repairAttempts);
            return new AgentToolExecutionResult
            {
                Success = true,
                RequiresConfirmation = false,
                Risk = "Medium",
                Message = message,
                RunId = runId,
                Phase = session.Phase,
                Data = reviewResult.Data,
                Artifact = BuildArtifact("chapter_ready_for_user_review", runId, session.ActiveProjectId, runId, message, new[] { "审阅草稿", "确认提交书城", "提出修改意见" }),
                Suggestions = new[] { "审阅草稿", "确认提交书城", "提出修改意见" },
                RecommendedToolName = "ProduceChapter",
                RecommendedArguments = BuildClosedLoopRecommendedArguments("auto_commit")
            };
        }

        boundaryBlocked = await StopForInterruptBoundaryAsync(NovelAgentProductionStages.ChapterCommit)
            .ConfigureAwait(false);
        if (boundaryBlocked != null) return boundaryBlocked;
        var commitResult = await RunChapterCommitStageAsync(runId, session, confirmed: true, ct).ConfigureAwait(false);
        executedStages.Add(NovelAgentProductionStages.ChapterCommit);
        if (!commitResult.Success)
        {
            var nextHints = new[] { "查看质量评审", "重新执行提交", "查询生产状态" };
            var message = BuildProduceChapterFailureMessage(NovelAgentProductionStages.ChapterCommit, commitResult, executedStages, nextHints);
            var artifact = BuildArtifact("chapter_production_blocked", runId, session.ActiveProjectId, runId, message, nextHints);
            return new AgentToolExecutionResult
            {
                Success = false,
                RequiresConfirmation = false,
                Risk = "High",
                Message = message,
                RunId = runId,
                Phase = session.Phase,
                Data = commitResult.Data,
                RecommendedToolName = "ProduceChapter",
                RecommendedArguments = BuildClosedLoopRecommendedArguments("auto_commit"),
                Failure = BuildProduceChapterFailure(
                    NovelAgentProductionStages.ChapterCommit,
                    commitResult,
                    artifact,
                    nextHints,
                    requiresUserDecision: false),
                Artifact = artifact,
                Suggestions = nextHints
            };
        }

        var successMessage = BuildProduceChapterSuccessMessage(commitResult, executedStages, repairAttempts);
        return new AgentToolExecutionResult
        {
            Success = true,
            RequiresConfirmation = false,
            Risk = "High",
            Message = successMessage,
            RunId = runId,
            Phase = session.Phase,
            Data = commitResult.Data,
            Artifact = BuildArtifact("chapter_produced", runId, session.ActiveProjectId, runId, successMessage, new[] { "查看书城", "继续下一章", "查看工作流" }),
            Suggestions = new[] { "查看书城", "继续下一章", "查看工作流" }
        };
    }

    private async Task ApplyAgentReviewFeedbackToContextPackageAsync(
        AgentSession session,
        string runId,
        AgentToolExecutionResult reviewResult,
        int reviewRewriteAttempt,
        CancellationToken ct)
    {
        if (reviewResult.Data is not NovelAgentExecutionResult executionResult ||
            executionResult.Run == null ||
            executionResult.Run.ContextPackage == null)
        {
            return;
        }

        var run = executionResult.Run;
        var package = run.ContextPackage;
        var review = run.PostGenerationReview;
        var feedback = BuildAgentReviewRewriteFeedback(review);
        if (feedback.Count == 0)
            feedback.Add("AgentReview修订要求：质量评审未通过，重写时必须补足章节核心创意、冲突推进、代价后果和项目知识落地。");

        var added = 0;
        foreach (var line in feedback)
        {
            if (!package.Warnings.Contains(line, StringComparer.OrdinalIgnoreCase))
            {
                package.Warnings.Add(line);
                added++;
            }

            var hardFactLine = $"AgentReview修订要求：{line.Replace("AgentReview修订要求：", string.Empty, StringComparison.Ordinal)}";
            if (!package.HardContinuityFacts.Contains(hardFactLine, StringComparer.OrdinalIgnoreCase))
            {
                package.HardContinuityFacts.Add(hardFactLine);
                added++;
            }
        }

        package.Warnings = package.Warnings
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToList();
        package.HardContinuityFacts = package.HardContinuityFacts
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToList();

        run.ContextPackage = package;
        run.PostGenerationReview = null;
        run.GateReport = null;
        run.Notes.Add($"AgentReview 第 {reviewRewriteAttempt} 次未通过，已将 {feedback.Count} 条评审意见写入下一轮章节生产包。");
        await _workspace.StoryBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);
        await PersistAgentReviewFeedbackContextPackageAsync(session, runId, run, package, ct).ConfigureAwait(false);

        var message = $"已把 AgentReview 失败项写入章节生产包，准备第 {reviewRewriteAttempt} 次按评审改写。";
        await AppendChapterProductionEventAsync(
                session,
                runId,
                executionResult,
                eventType: "chapter_agent_review_feedback_applied",
                stage: NovelAgentProductionStages.QualityReview,
                status: "completed",
                message: message,
                artifactType: "chapter_review_feedback",
                artifactId: review?.ReviewId ?? runId,
                data: new
                {
                    reviewRewriteAttempt,
                    added,
                    feedback
                },
                ct)
            .ConfigureAwait(false);
    }

    private async Task PersistAgentReviewFeedbackContextPackageAsync(
        AgentSession session,
        string runId,
        NovelAgentRun run,
        ChapterContextPackageSummary package,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.ActiveProjectId) ||
            string.IsNullOrWhiteSpace(package.PackageId))
        {
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var storedPackage = await db.TianmingPackages
            .FirstOrDefaultAsync(item =>
                    item.ProjectId == session.ActiveProjectId &&
                    item.RuntimeRunId == runId &&
                    item.Id == package.PackageId,
                ct)
            .ConfigureAwait(false);
        if (storedPackage == null)
            return;

        storedPackage.InputJson = BuildContextPackageInputJson(runId, run, package);
        storedPackage.KnowledgeSnapshotJson = BuildContextPackageKnowledgeSnapshotJson(package);
        storedPackage.FactSnapshotJson = BuildContextPackageFactSnapshotJson(package);
        storedPackage.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static string BuildContextPackageInputJson(
        string runId,
        NovelAgentRun run,
        ChapterContextPackageSummary package)
    {
        return JsonSerializer.Serialize(new
        {
            runId,
            run.UserGoal,
            chapterId = package.ChapterId,
            status = package.Status,
            builtAt = package.BuiltAt,
            worldRules = package.WorldRules,
            characterStates = package.CharacterStates,
            activeConflicts = package.ActiveConflicts,
            activeForeshadowing = package.ActiveForeshadowing,
            hardContinuityFacts = package.HardContinuityFacts,
            chapterBlueprints = package.ChapterBlueprints,
            previousSummaries = package.PreviousSummaries,
            longDistanceRecall = package.LongDistanceRecall,
            ragQueries = package.RagQueries,
            knowledgeBindings = package.KnowledgeBindings,
            acceptedCreativeIntents = package.AcceptedCreativeIntents,
            sourceRevisionPlans = package.SourceRevisionPlans,
            rebuiltFromPackageIds = ResolveContextPackageRebuiltFromPackageIds(package),
            warnings = package.Warnings
        });
    }

    private static string BuildContextPackageKnowledgeSnapshotJson(ChapterContextPackageSummary package)
    {
        var bindings = package.KnowledgeBindings;
        return JsonSerializer.Serialize(new
        {
            hardContinuityFacts = package.HardContinuityFacts,
            ragQueries = package.RagQueries,
            knowledgeBindingSummary = new
            {
                bindingCount = bindings.Count,
                shouldEnterGateCount = bindings.Count(binding => binding.ShouldEnterGate),
                shouldEnterBlueprintCount = bindings.Count(binding => binding.ShouldEnterBlueprint),
                shouldEnterFactSnapshotCount = bindings.Count(binding => binding.ShouldEnterFactSnapshot),
                hardConstraintCount = bindings.Count(binding =>
                    string.Equals(binding.ConstraintLevel, "HardConstraint", StringComparison.OrdinalIgnoreCase)),
                referenceCount = bindings.Count(binding =>
                    string.Equals(binding.ConstraintLevel, "Reference", StringComparison.OrdinalIgnoreCase)),
                classifiedCount = bindings.Count(binding => !string.IsNullOrWhiteSpace(binding.ClassificationId)),
                pendingClassificationCount = bindings.Count(binding => string.IsNullOrWhiteSpace(binding.ClassificationId)),
                importedCount = bindings.Count(binding =>
                    string.Equals(binding.ProjectUsageStatus, "imported", StringComparison.OrdinalIgnoreCase)),
                referencedCount = bindings.Count(binding =>
                    string.Equals(binding.ProjectUsageStatus, "referenced", StringComparison.OrdinalIgnoreCase))
            },
            knowledgeBindings = package.KnowledgeBindings,
            acceptedCreativeIntents = package.AcceptedCreativeIntents,
            sourceRevisionPlans = package.SourceRevisionPlans,
            rebuiltFromPackageIds = ResolveContextPackageRebuiltFromPackageIds(package)
        });
    }

    private static string BuildContextPackageFactSnapshotJson(ChapterContextPackageSummary package)
    {
        return JsonSerializer.Serialize(new
        {
            package.WorldRules,
            package.CharacterStates,
            package.ActiveConflicts,
            package.ActiveForeshadowing,
            package.PreviousSummaries,
            package.HardContinuityFacts,
            package.Warnings
        });
    }

    private static IReadOnlyList<string> ResolveContextPackageRebuiltFromPackageIds(ChapterContextPackageSummary package)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in package.SourceRevisionPlans)
        {
            foreach (var id in ParseJsonStringArray(plan.InvalidatedPackageIdsJson ?? string.Empty))
                ids.Add(id);
        }

        return ids.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task AcceptWarningOnlyAgentReviewAsync(
        AgentSession session,
        string runId,
        AgentToolExecutionResult reviewResult,
        int reviewRewriteAttempts,
        CancellationToken ct)
    {
        if (reviewResult.Data is not NovelAgentExecutionResult executionResult ||
            executionResult.Run == null ||
            executionResult.Run.PostGenerationReview == null)
        {
            return;
        }

        var run = executionResult.Run;
        var review = run.PostGenerationReview;
        review.RequiresRewrite = false;
        review.RecommendedAction = "commit";
        var warningCount = review.Checks.Count(c => c.Status == NovelAgentReviewCheckStatus.Warning);
        review.Summary = reviewRewriteAttempts > 0
            ? $"AgentReview 仍有 {warningCount} 个警告，但自动改写预算已用完且没有失败项，允许先提交并把警告留给后续修订。"
            : $"AgentReview 有 {warningCount} 个警告但没有失败项，允许先提交并把警告留给后续修订。";
        if (!review.NextChapterSuggestions.Any(s => s.Contains("质量警告", StringComparison.OrdinalIgnoreCase)))
            review.NextChapterSuggestions.Add("后续章节或修订时继续处理本章遗留质量警告。");
        run.Notes.Add(reviewRewriteAttempts > 0
            ? $"AgentReview 自动改写 {reviewRewriteAttempts} 次后仅剩警告，已允许提交书城。"
            : "AgentReview 仅有警告无失败项，未触发自动改写，已允许提交书城。");
        await _workspace.StoryBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);
        await RecordAgentReviewAsync(session, runId, executionResult, review, ct).ConfigureAwait(false);

        await AppendChapterProductionEventAsync(
                session,
                runId,
                executionResult,
                eventType: "chapter_quality_review_warnings_accepted",
                stage: NovelAgentProductionStages.QualityReview,
                status: "completed",
                message: review.Summary,
                artifactType: "chapter_review_warning_acceptance",
                artifactId: review.ReviewId ?? runId,
                data: new
                {
                    reviewRewriteAttempts,
                    warningCount,
                    warnings = review.Checks
                        .Where(c => c.Status == NovelAgentReviewCheckStatus.Warning)
                        .Select(c => new { c.Key, c.Name, c.Message })
                        .Take(12)
                        .ToList()
                },
                ct)
            .ConfigureAwait(false);
    }

    private static bool IsWarningOnlyAgentReview(AgentToolExecutionResult reviewResult)
    {
        if (reviewResult.Data is not NovelAgentExecutionResult executionResult ||
            executionResult.Run?.PostGenerationReview == null)
        {
            return false;
        }

        var review = executionResult.Run.PostGenerationReview;
        return IsWarningOnlyAgentReview(review);
    }

    private static bool IsWarningOnlyAgentReview(NovelAgentPostGenerationReview review)
    {
        return review.Checks.Any(c => c.Status == NovelAgentReviewCheckStatus.Warning) &&
               !AgentReviewHasFailures(review);
    }

    private static bool AgentReviewAllowsCommit(NovelAgentPostGenerationReview? review)
    {
        return review != null && !AgentReviewHasFailures(review);
    }

    private static bool AgentReviewHasFailures(NovelAgentPostGenerationReview review)
    {
        return review.Checks.Any(c => c.Status == NovelAgentReviewCheckStatus.Fail) ||
               review.OverallResult is "Fail" or "Failed";
    }

    private static List<string> BuildAgentReviewRewriteFeedback(NovelAgentPostGenerationReview? review)
    {
        if (review == null)
            return new List<string>();

        return review.Checks
            .Where(check => check.Status is NovelAgentReviewCheckStatus.Fail or NovelAgentReviewCheckStatus.Warning)
            .Take(8)
            .Select(check =>
            {
                var suggestions = check.Suggestions.Count == 0
                    ? string.Empty
                    : $"；修订建议：{string.Join("、", check.Suggestions.Take(3))}";
                var evidence = check.Evidence.Count == 0
                    ? string.Empty
                    : $"；证据：{string.Join(" / ", check.Evidence.Take(2))}";
                return $"AgentReview修订要求：{check.Name}={check.Status}：{check.Message}{suggestions}{evidence}";
            })
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeProduceChapterCommitPolicy(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "draft_only" or "draft" or "草稿" or "只生成草稿" => "draft_only",
            "require_user_review" or "user_review" or "review" or "manual_review" or "人工审阅" or "用户审阅" => "require_user_review",
            _ => "auto_commit"
        };
    }

    private async Task<string> ResolveProduceChapterTargetChapterIdAsync(
        string runId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return string.Empty;

        var bible = await _workspace.Orchestrator.GetStoryBibleAsync(ct).ConfigureAwait(false);
        var run = bible.AgentRuns.FirstOrDefault(item =>
            string.Equals(item.RunId, runId, StringComparison.OrdinalIgnoreCase));
        return FirstNonEmpty(run?.TargetChapterId, run?.ChapterBrief?.ChapterId);
    }

    private async Task<(bool Required, ChapterProductionLease? Lease)> TryAcquireChapterProductionLeaseAsync(
        AgentSession session,
        string runId,
        string targetChapterId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) ||
            string.IsNullOrWhiteSpace(session.ActiveProjectId) ||
            string.IsNullOrWhiteSpace(targetChapterId))
        {
            return (false, null);
        }

        using var scope = _serviceProvider.CreateScope();
        var leaseService = scope.ServiceProvider.GetService<IChapterProductionLeaseService>();
        if (leaseService == null)
            return (false, null);

        var lease = await leaseService
            .TryAcquireAsync(session.UserId, session.ActiveProjectId, targetChapterId, runId, ct)
            .ConfigureAwait(false);
        return (true, lease);
    }

    private AgentToolExecutionResult BuildChapterProductionBusyResult(
        AgentSession session,
        string runId,
        string targetChapterId)
    {
        var message = $"同一章节正在生产中，暂不能并发执行 ProduceChapter。项目：{session.ActiveProjectId}，章节：{targetChapterId}，run：{runId}。";
        var hints = new[] { "QueryNovelProductionState", "稍后重试 ProduceChapter", "查看当前任务进度" };
        var artifact = BuildArtifact(
            "chapter_production_lock_busy",
            targetChapterId,
            session.ActiveProjectId,
            runId,
            message,
            hints);

        return new AgentToolExecutionResult
        {
            Success = false,
            RequiresConfirmation = false,
            Risk = "High",
            Message = message,
            RunId = runId,
            Phase = session.Phase,
            IsRepairable = true,
            RecommendedToolName = "QueryNovelProductionState",
            RecommendedArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["runId"] = runId,
                ["chapterId"] = targetChapterId
            },
            MissingPrerequisite = "chapter_production_lock",
            Failure = new AgentToolFailure
            {
                Code = "CHAPTER_PRODUCTION_LOCK_BUSY",
                FailedStage = "produce_chapter_lock",
                Reason = message,
                Recoverable = true,
                RecommendedAction = "QueryNovelProductionState",
                ArtifactIds = new[] { artifact.ArtifactId },
                ProducedArtifacts = new[] { ToProducedArtifact(artifact) },
                RecoverableActions = hints,
                RequiresUserDecision = false
            },
            Artifact = artifact,
            Suggestions = hints
        };
    }

    private static string BuildProduceChapterDraftOnlyMessage(
        AgentToolExecutionResult validateResult,
        IReadOnlyList<string> executedStages,
        int repairAttempts)
    {
        var lines = new List<string>
        {
            "章节草稿已生成并通过硬门禁，按 commitPolicy=draft_only 停止，未提交书城。",
            $"执行阶段：{FormatExecutedStages(executedStages)}。"
        };
        if (repairAttempts > 0)
            lines.Add($"自动修复次数：{repairAttempts}。");
        if (!string.IsNullOrWhiteSpace(validateResult.Message))
            lines.Add($"门禁结果：{validateResult.Message}");
        return string.Join("\n", lines);
    }

    private static string BuildProduceChapterUserReviewMessage(
        AgentToolExecutionResult reviewResult,
        IReadOnlyList<string> executedStages,
        int repairAttempts)
    {
        var lines = new List<string>
        {
            "章节草稿已生成、通过硬门禁和质量评审，按 commitPolicy=require_user_review 等待用户审阅，暂未提交书城。",
            $"执行阶段：{FormatExecutedStages(executedStages)}。"
        };
        if (repairAttempts > 0)
            lines.Add($"自动修复次数：{repairAttempts}。");
        if (!string.IsNullOrWhiteSpace(reviewResult.Message))
            lines.Add($"评审结果：{reviewResult.Message}");
        return string.Join("\n", lines);
    }

    private static string BuildProduceChapterSuccessMessage(
        AgentToolExecutionResult commitResult,
        IReadOnlyList<string> executedStages,
        int repairAttempts)
    {
        var lines = new List<string>
        {
            "章节生产闭环已完成，并已提交到书城。",
            $"执行阶段：{FormatExecutedStages(executedStages)}。"
        };
        if (repairAttempts > 0)
            lines.Add($"自动修复次数：{repairAttempts}。");
        if (!string.IsNullOrWhiteSpace(commitResult.Message))
            lines.Add($"提交结果：{commitResult.Message}");
        lines.Add("正文已进入小说书城；向量索引、长距离召回刷新等后台任务通过 outbox 异步继续，可用 QueryNovelProductionState 查看状态。");
        return string.Join("\n", lines);
    }

    private static string BuildProduceChapterFailureMessage(
        string failedStage,
        AgentToolExecutionResult result,
        IReadOnlyList<string> executedStages,
        IReadOnlyList<string> nextHints)
    {
        var lines = new List<string>
        {
            $"章节生产闭环在 {FormatProductionStageLabel(failedStage)} 阶段停止。",
            $"已执行阶段：{FormatExecutedStages(executedStages)}。",
            $"失败原因：{FirstNonEmpty(result.Message, result.Failure?.Reason, result.Failure?.Code, "未知原因")}"
        };
        if (result.Artifact != null)
            lines.Add($"已产物：{result.Artifact.UserVisibleStatus} / {result.Artifact.ArtifactId}");
        if (nextHints.Count > 0)
            lines.Add($"可继续动作：{string.Join("、", nextHints)}。");
        return string.Join("\n", lines);
    }

    private static AgentToolFailure BuildProduceChapterFailure(
        string failedStage,
        AgentToolExecutionResult result,
        AgentToolArtifact artifact,
        IReadOnlyList<string> nextHints,
        bool requiresUserDecision)
    {
        var source = result.Failure;
        return new AgentToolFailure
        {
            Code = FirstNonEmpty(source?.Code, result.MissingPrerequisite, result.IsRepairable ? "PRODUCE_CHAPTER_RECOVERABLE_FAILURE" : "PRODUCE_CHAPTER_STAGE_FAILED"),
            FailedStage = FirstNonEmpty(source?.FailedStage, failedStage, result.Phase, "produce_chapter"),
            Reason = FirstNonEmpty(source?.Reason, result.Message, "章节生产闭环停止。"),
            Recoverable = source?.Recoverable == true || result.IsRepairable || nextHints.Count > 0,
            RecommendedAction = FirstNonEmpty(source?.RecommendedAction, result.IsRepairable ? "ProduceChapter" : string.Empty),
            ArtifactIds = source?.ArtifactIds.Count > 0 ? source.ArtifactIds : new[] { artifact.ArtifactId },
            ProducedArtifacts = source?.ProducedArtifacts.Count > 0
                ? source.ProducedArtifacts
                : new[] { ToProducedArtifact(artifact) },
            RecoverableActions = source?.RecoverableActions.Count > 0 ? source.RecoverableActions : nextHints,
            RequiresUserDecision = source?.RequiresUserDecision == true || requiresUserDecision
        };
    }

    private static AgentToolProducedArtifact ToProducedArtifact(AgentToolArtifact artifact) => new()
    {
        ArtifactType = artifact.ArtifactType,
        ArtifactId = artifact.ArtifactId,
        OutputKind = artifact.OutputKind,
        UserVisibleWhere = artifact.UserVisibleWhere,
        Summary = artifact.Summary
    };

    private static string FormatExecutedStages(IReadOnlyList<string> stages) =>
        string.Join(" -> ", stages.Select(FormatProductionStageLabel));

    private static string AppendStatusToMessage(string message, params string?[] statuses)
    {
        var normalizedStatuses = statuses
            .Where(status => !string.IsNullOrWhiteSpace(status))
            .Select(status => status!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var baseMessage = string.IsNullOrWhiteSpace(message) ? "阶段状态已更新。" : message.Trim();
        if (normalizedStatuses.Length == 0)
            return baseMessage;
        return $"{baseMessage} 状态：{string.Join(" / ", normalizedStatuses)}。";
    }

    private static string FormatProductionStageLabel(string stage)
    {
        if (string.IsNullOrWhiteSpace(stage))
            return string.Empty;

        var attemptSuffix = string.Empty;
        var baseStage = stage;
        var hashIndex = stage.IndexOf('#');
        if (hashIndex >= 0)
        {
            baseStage = stage[..hashIndex];
            attemptSuffix = stage[hashIndex..];
        }

        var label = baseStage == "ReviewChapter"
            ? "Agent 质量评审"
            : NovelAgentProductionStages.Label(baseStage);

        return label + attemptSuffix;
    }

    private async Task<AgentToolExecutionResult> RunDraftGenerationStageAsync(string runId, AgentSession session, bool confirmed, CancellationToken ct)
    {
        await AppendChapterRuntimeProgressEventAsync(
                session,
                runId,
                NovelAgentProductionStages.DraftGeneration,
                "running",
                "正在生成章节正文与修订记录。",
                "chapter_draft_artifact",
                null,
                new { runId, stage = NovelAgentProductionStages.DraftGeneration, status = "running" },
                ct)
            .ConfigureAwait(false);

        var contextResult = await GetOrBuildContextPackageStageAsync(runId, ct).ConfigureAwait(false);
        if (contextResult?.Success == true)
        {
            await InjectDatabaseHardFactsAsync(session, contextResult, ct).ConfigureAwait(false);
            await EnsureChapterContextPackagePersistedAsync(session, runId, contextResult, ct).ConfigureAwait(false);
        }

        NovelAgentExecutionResult result;
        try
        {
            result = await RunWithProductionStageHeartbeatAsync(
                    session,
                    runId,
                    NovelAgentProductionStages.DraftGeneration,
                    "chapter_draft_artifact",
                    null,
                    token => _workspace.Orchestrator.GenerateChapterDraftStageAsync(runId, confirmed, token),
                    ct)
                .ConfigureAwait(false);
        }
        catch (AgentProductionStageTimeoutException ex)
        {
            await AppendChapterRuntimeProgressEventAsync(
                    session,
                    runId,
                    NovelAgentProductionStages.DraftGeneration,
                    "failed",
                    ex.Message,
                    "chapter_draft_artifact",
                    null,
                    new
                    {
                        runId,
                        stage = NovelAgentProductionStages.DraftGeneration,
                        status = "failed",
                        code = "STAGE_TIMEOUT",
                        elapsedSeconds = (int)Math.Round(ex.Elapsed.TotalSeconds),
                        timeoutSeconds = (int)Math.Round(ex.Timeout.TotalSeconds)
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
            return BuildStageTimeoutToolResult(
                session,
                runId,
                NovelAgentProductionStages.DraftGeneration,
                ex,
                new[] { "继续 ProduceChapter", "检查模型服务连接", "缩短章节生产包或降低输出长度" });
        }
        session.Phase = result.Success ? "draft_generated" : result.GateReport?.Status ?? session.Phase;
        var generationStatus = string.Equals(result.DraftArtifact?.Status, "blocked_missing_llm_settings", StringComparison.OrdinalIgnoreCase)
            ? "blocked"
            : result.Success ? "completed" : "failed";
        var generationMessage = AppendStatusToMessage(result.Message, result.DraftArtifact?.Status);
        await AppendChapterProductionEventAsync(
                session,
                runId,
                result,
                eventType: "chapter_draft_generated",
                stage: NovelAgentProductionStages.DraftGeneration,
                status: generationStatus,
                message: generationMessage,
                artifactType: "chapter_draft_artifact",
                artifactId: result.DraftArtifact?.ArtifactId ?? "chapter_draft",
                data: new
                {
                    draftStatus = result.DraftArtifact?.Status ?? string.Empty,
                    result.DraftArtifact?.HasChanges,
                    result.DraftArtifact?.RepairAttemptCount,
                    contentLength = result.DraftArtifact?.DraftContent.Length ?? 0,
                    changesLength = result.DraftArtifact?.ChangesJson.Length ?? 0
                },
                ct)
            .ConfigureAwait(false);
        if (result.Success)
            await RecordChapterDraftAsync(session, runId, result, ct).ConfigureAwait(false);
        if (result.Success)
            await RecordDraftChapterChangesAsync(session, runId, result, ct).ConfigureAwait(false);
        return new AgentToolExecutionResult
        {
            Success = result.Success,
            RequiresConfirmation = false,
            Risk = result.RiskLevel.ToString(),
            Message = result.Success
                ? result.Message
                : BuildWritingFailureMessage("生成章节正文", session.Phase, result.Message, hasDraft: result.DraftArtifact != null, hasGateReport: result.GateReport != null),
            RunId = runId,
            Phase = session.Phase,
            Data = result,
            Artifact = BuildArtifact(
                result.Success ? "chapter_draft_with_changes" : "chapter_generation_blocked",
                result.Run?.TargetChapterId ?? runId,
                session.ActiveProjectId,
                runId,
                result.Message,
                result.Success ? new[] { "执行硬门禁校验", "查看草稿" } : new[] { "选择章节候选", "补齐上下文" }),
            Suggestions = result.Success ? new[] { "执行硬门禁校验", "查看草稿" } : new[] { "选择章节候选", "补齐上下文" },
        };
    }

    private async Task RecordChapterDraftAsync(
        AgentSession session,
        string runId,
        NovelAgentExecutionResult result,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) ||
            string.IsNullOrWhiteSpace(session.ActiveProjectId) ||
            result.DraftArtifact == null)
        {
            return;
        }

        var chapterId = FirstNonEmpty(
            result.Run?.TargetChapterId,
            result.DraftArtifact.ChapterId,
            runId);
        var packageId = FirstNonEmpty(
            result.ContextPackage?.PackageId,
            result.Run?.ContextPackage?.PackageId);

        using var scope = _serviceProvider.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IProductionEventWriter>();
        await writer.AppendChapterDraftAsync(
                new AppendChapterDraftRequest(
                    session.UserId,
                    session.ActiveProjectId,
                    runId,
                    chapterId,
                    packageId,
                    result.DraftArtifact.ArtifactId,
                    result.DraftArtifact.Status,
                    result.DraftArtifact.DraftContent,
                    FirstNonEmpty(
                        result.DraftArtifact.ChangesJson,
                        ChapterChangesText.ExtractChangesJson(result.DraftArtifact.DraftContent)),
                    result.DraftArtifact.RepairAttemptCount,
                    result.DraftArtifact.HasChanges),
                ct)
            .ConfigureAwait(false);
    }

    private async Task RecordGenerationGateReportAsync(
        AgentSession session,
        string runId,
        NovelAgentExecutionResult result,
        GenerationGateReport report,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) ||
            string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            return;
        }

        var chapterId = FirstNonEmpty(
            result.Run?.TargetChapterId,
            result.DraftArtifact?.ChapterId,
            result.ContextPackage?.ChapterId,
            runId);
        var packageId = FirstNonEmpty(
            result.ContextPackage?.PackageId,
            result.Run?.ContextPackage?.PackageId);

        using var scope = _serviceProvider.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IProductionEventWriter>();
        await writer.AppendGenerationGateReportAsync(
                new AppendGenerationGateReportRequest(
                    session.UserId,
                    session.ActiveProjectId,
                    runId,
                    chapterId,
                    packageId,
                    FirstNonEmpty(report.Status, "generation_gate_report"),
                    report.Status,
                    JsonSerializer.Serialize(report),
                    report.ProtocolPassed,
                    report.ChangesDetected,
                    report.FactSnapshotPassed,
                    report.BlueprintPassed,
                    report.RagPassed,
                    report.Issues.Count,
                    report.RepairHints.Count,
                    report.ValidatedAt),
                ct)
            .ConfigureAwait(false);
    }

    private async Task RecordAgentReviewAsync(
        AgentSession session,
        string runId,
        NovelAgentExecutionResult result,
        NovelAgentPostGenerationReview review,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) ||
            string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            return;
        }

        var chapterId = FirstNonEmpty(
            result.Run?.TargetChapterId,
            review.ChapterId,
            result.DraftArtifact?.ChapterId,
            result.ContextPackage?.ChapterId,
            runId);
        var packageId = FirstNonEmpty(
            result.ContextPackage?.PackageId,
            result.Run?.ContextPackage?.PackageId);

        using var scope = _serviceProvider.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IProductionEventWriter>();
        await writer.AppendAgentReviewAsync(
                new AppendAgentReviewRequest(
                    session.UserId,
                    session.ActiveProjectId,
                    runId,
                    chapterId,
                    packageId,
                    review.ReviewId,
                    review.OverallResult,
                    review.ValidationOverallResult,
                    review.RequiresRewrite,
                    review.QualityScore,
                    review.ContentLength,
                    review.Checks.Count,
                    review.Summary,
                    JsonSerializer.Serialize(review),
                    review.CreatedAt,
                    review.MeetsAcceptedCreativeIntents,
                    review.ContinuityRisk,
                    review.ChapterPacing,
                    review.RecommendedAction),
                ct)
            .ConfigureAwait(false);
    }

    private async Task RecordDraftChapterChangesAsync(
        AgentSession session,
        string runId,
        NovelAgentExecutionResult result,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) ||
            string.IsNullOrWhiteSpace(session.ActiveProjectId) ||
            result.DraftArtifact == null)
        {
            return;
        }

        var changesJson = FirstNonEmpty(
            result.DraftArtifact.ChangesJson,
            ChapterChangesText.ExtractChangesJson(result.DraftArtifact.DraftContent));
        if (string.IsNullOrWhiteSpace(changesJson) &&
            !result.DraftArtifact.HasChanges)
        {
            return;
        }

        if (!ChapterChangesText.TryDeserializeChanges(changesJson, out var changes))
            changes = new ChapterChanges();

        var chapterId = FirstNonEmpty(
            result.Run?.TargetChapterId,
            result.DraftArtifact.ChapterId,
            runId);
        var run = result.Run ?? new NovelAgentRun
        {
            RunId = runId,
            TargetChapterId = chapterId,
            ContextPackage = result.ContextPackage
        };
        if (run.ContextPackage == null && result.ContextPackage != null)
            run.ContextPackage = result.ContextPackage;

        using var scope = _serviceProvider.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IProductionEventWriter>();
        var recorder = new ProductionChapterChangesRecorder(
            writer,
            session.UserId,
            session.ActiveProjectId);
        await recorder.RecordAsync(run, chapterId, changes, changesJson, ct).ConfigureAwait(false);
    }

    private async Task<NovelAgentExecutionResult?> GetOrBuildContextPackageStageAsync(
        string runId,
        CancellationToken ct)
    {
        var document = await _workspace.Orchestrator.GetStoryBibleAsync(ct).ConfigureAwait(false);
        var run = document.AgentRuns.FirstOrDefault(r => string.Equals(r.RunId, runId, StringComparison.OrdinalIgnoreCase));
        if (run?.ContextPackage != null)
        {
            return new NovelAgentExecutionResult
            {
                Success = true,
                Message = "复用已构建的章节上下文包。",
                ContextPackage = run.ContextPackage,
                Run = run
            };
        }

        return await _workspace.Orchestrator.BuildContextPackageStageAsync(runId, ct).ConfigureAwait(false);
    }

    private async Task<AgentToolExecutionResult> RunBuildContextPackageStageAsync(
        string runId,
        AgentSession session,
        string revisionPlanId,
        CancellationToken ct)
    {
        await AppendChapterRuntimeProgressEventAsync(
                session,
                runId,
                NovelAgentProductionStages.ContextPackage,
                "running",
                "正在构建章节上下文包。",
                "tianming_package",
                null,
                new { runId, stage = NovelAgentProductionStages.ContextPackage, status = "running" },
                ct)
            .ConfigureAwait(false);

        var dependencyBlock = await FindPreviousChapterPostCommitDependencyBlockAsync(session, runId, ct)
            .ConfigureAwait(false);
        if (dependencyBlock != null)
        {
            var blockMessage = $"上一章提交后后台沉淀尚未完成，暂不能构建下一章生产包：{dependencyBlock.Summary}";
            await AppendChapterRuntimeProgressEventAsync(
                    session,
                    runId,
                    NovelAgentProductionStages.ContextPackage,
                    "blocked",
                    blockMessage,
                    "post_commit_outbox",
                    dependencyBlock.OutboxEventIds.FirstOrDefault(),
                    new
                    {
                        runId,
                        stage = NovelAgentProductionStages.ContextPackage,
                        status = "blocked",
                        reason = dependencyBlock.Code,
                        dependencyBlock.PreviousChapterId,
                        dependencyBlock.PreviousChapterNumber,
                        dependencyBlock.TargetChapterNumber,
                        dependencyBlock.OutboxEventIds,
                        dependencyBlock.OutboxEventTypes,
                        dependencyBlock.Statuses
                    },
                    ct)
                .ConfigureAwait(false);
            using (var scope = _serviceProvider.CreateScope())
            {
                var writer = scope.ServiceProvider.GetService<IProductionEventWriter>();
                if (writer != null)
                {
                    await writer.AppendChapterStageAsync(
                            new AppendChapterProductionEventRequest(
                                RuntimeRunId: runId,
                                UserId: session.UserId,
                                ProjectId: session.ActiveProjectId,
                                ChapterId: dependencyBlock.TargetChapterId,
                                PackageId: null,
                                EventType: "production_dependency_blocked",
                                Stage: NovelAgentProductionStages.ContextPackage,
                                Status: "blocked",
                                Message: blockMessage,
                                ArtifactType: "post_commit_outbox",
                                ArtifactId: dependencyBlock.OutboxEventIds.FirstOrDefault(),
                                Data: new
                                {
                                    runId,
                                    stage = NovelAgentProductionStages.ContextPackage,
                                    status = "blocked",
                                    reason = dependencyBlock.Code,
                                    dependencyBlock.TargetChapterId,
                                    dependencyBlock.TargetChapterNumber,
                                    dependencyBlock.PreviousChapterId,
                                    dependencyBlock.PreviousChapterNumber,
                                    dependencyBlock.OutboxEventIds,
                                    dependencyBlock.OutboxEventTypes,
                                    dependencyBlock.Statuses
                                }),
                            ct)
                        .ConfigureAwait(false);
                }
            }

            return new AgentToolExecutionResult
            {
                Success = false,
                RequiresConfirmation = false,
                Risk = "Medium",
                Message = blockMessage,
                RunId = runId,
                Phase = session.Phase,
                IsRepairable = true,
                RecommendedToolName = "QueryNovelProductionState",
                RecommendedArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["runId"] = runId
                },
                MissingPrerequisite = "previous_chapter_post_commit_outbox",
                Data = dependencyBlock,
                Artifact = BuildArtifact(
                    "chapter_context_dependency_blocked",
                    dependencyBlock.PreviousChapterId,
                    session.ActiveProjectId,
                    runId,
                    blockMessage,
                    new[] { "等待后台沉淀完成", "查看生产状态", "重试 outbox" }),
                Suggestions = new[] { "等待后台沉淀完成", "查看生产状态", "重试 outbox" },
            };
        }

        var result = await RunWithProductionStageHeartbeatAsync(
                session,
                runId,
                NovelAgentProductionStages.ContextPackage,
                "tianming_package",
                null,
                token => _workspace.Orchestrator.BuildContextPackageStageAsync(runId, token),
                ct)
            .ConfigureAwait(false);
        session.Phase = result.Success ? "context_ready" : session.Phase;
        var injectedHardFactCount = result.Success
            ? await InjectDatabaseHardFactsAsync(session, result, ct).ConfigureAwait(false)
            : 0;
        var injectedCreativeIntentCount = result.Success
            ? await InjectAcceptedCreativeIntentsAsync(session, result, ct).ConfigureAwait(false)
            : 0;
        var injectedRevisionPlanCount = result.Success
            ? await InjectReadyRevisionPlansAsync(session, result, revisionPlanId, ct).ConfigureAwait(false)
            : 0;
        var injectedRuntimeRequirementCount = result.Success
            ? InjectRuntimeSoftRequirements(session, result)
            : 0;
        var package = result.ContextPackage;
        if (result.Success && package != null)
            await EnsureChapterContextPackagePersistedAsync(session, runId, result, ct).ConfigureAwait(false);
        var message = result.Success && package != null
            ? $"上下文包：世界规则 {package.WorldRules.Count}，角色状态 {package.CharacterStates.Count}，知识库硬事实 {injectedHardFactCount}，已采纳创意 {injectedCreativeIntentCount}，修订计划 {injectedRevisionPlanCount}，运行中补充 {injectedRuntimeRequirementCount}，长距离召回 {package.LongDistanceRecall.Count}，警告 {package.Warnings.Count}。"
            : result.Message;
        return new AgentToolExecutionResult
        {
            Success = result.Success,
            Message = message,
            RunId = runId,
            Phase = session.Phase,
            Data = result,
            Artifact = BuildArtifact("chapter_context_package", package?.ChapterId ?? runId, session.ActiveProjectId, runId, message, new[] { "生成正文和 CHANGES", "查看上下文摘要" }),
            Suggestions = new[] { "生成正文和 CHANGES", "查看上下文摘要" },
        };
    }

    private static int InjectRuntimeSoftRequirements(
        AgentSession session,
        NovelAgentExecutionResult result)
    {
        var package = result.ContextPackage ?? result.Run?.ContextPackage;
        if (package == null || session.WorkingMemory.RuntimeInterrupts.Count == 0)
            return 0;

        var runtimeRunId = session.RuntimeRunId?.Trim() ?? string.Empty;
        var added = 0;
        foreach (var interrupt in session.WorkingMemory.RuntimeInterrupts
                     .Where(item => string.Equals(item.Kind, "soft_requirement", StringComparison.OrdinalIgnoreCase))
                     .Where(item => !string.IsNullOrWhiteSpace(item.Message))
                     .Where(item => string.IsNullOrWhiteSpace(runtimeRunId) ||
                                    string.IsNullOrWhiteSpace(item.RuntimeRunId) ||
                                    string.Equals(item.RuntimeRunId, runtimeRunId, StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(item => item.Priority)
                     .ThenBy(item => item.ReceivedAt)
                     .Take(8))
        {
            var requirementLine = $"执行中补充要求：{interrupt.Message.Trim()}";
            if (!package.HardContinuityFacts.Contains(requirementLine, StringComparer.OrdinalIgnoreCase))
            {
                package.HardContinuityFacts.Add(requirementLine);
                added++;
            }

            var sourceLine = $"执行中补充来源：{FirstNonEmpty(interrupt.InterruptId, "runtime_interrupt")} / priority={interrupt.Priority}";
            if (!package.Warnings.Contains(sourceLine, StringComparer.OrdinalIgnoreCase))
                package.Warnings.Add(sourceLine);
        }

        if (added > 0)
            NormalizeContextLists(package);
        return added;
    }

    private async Task<ProductionDependencyBlock?> FindPreviousChapterPostCommitDependencyBlockAsync(
        AgentSession session,
        string runId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) ||
            string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            return null;
        }

        var bible = await _workspace.Orchestrator.GetStoryBibleAsync(ct).ConfigureAwait(false);
        var run = bible.AgentRuns.FirstOrDefault(item =>
            string.Equals(item.RunId, runId, StringComparison.OrdinalIgnoreCase));
        var targetChapterNumber = ExtractTrailingNumber(run?.TargetChapterId ?? string.Empty);
        if (targetChapterNumber <= 1)
            return null;

        using var scope = _serviceProvider.CreateScope();
        var guard = scope.ServiceProvider.GetService<IProductionDependencyGuard>();
        if (guard == null)
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            guard = new ProductionDependencyGuard(db);
        }

        var blocks = await guard.FindBlocksAsync(
                new ProductionDependencyGuardRequest(
                    session.UserId,
                    session.ActiveProjectId,
                    targetChapterNumber),
                ct)
            .ConfigureAwait(false);
        return blocks.FirstOrDefault();
    }

    private async Task<AgentToolExecutionResult> QueryProjectKnowledgeBindingsAsync(AgentSession session, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) || string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "当前会话尚未绑定小说项目，不能查询项目知识绑定。",
                Phase = session.Phase,
                Suggestions = new[] { "先绑定或创建小说项目", "查询工作台状态" }
            };
        }

        using var scope = _serviceProvider.CreateScope();
        var queryService = scope.ServiceProvider.GetRequiredService<IProjectKnowledgeBindingQueryService>();
        var result = await queryService.QueryBindingsAsync(session.UserId, session.ActiveProjectId, ct)
            .ConfigureAwait(false)
            ?? new ProjectKnowledgeBindingsQueryResult { ProjectId = session.ActiveProjectId };
        var message = FormatProjectKnowledgeBindings(result);
        return new AgentToolExecutionResult
        {
            Success = true,
            Message = message,
            RunId = session.ActiveRunId ?? string.Empty,
            Phase = session.Phase,
            Data = result,
            Artifact = BuildArtifact("project_knowledge_bindings", session.ActiveProjectId, session.ActiveProjectId, session.ActiveRunId ?? string.Empty, message, new[] { "构建章节生产包", "检查知识冲突", "继续写章节" }),
            Suggestions = new[] { "构建章节生产包", "检查知识冲突", "继续写章节" }
        };
    }

    private async Task<AgentToolExecutionResult> AttachKnowledgeToProjectAsync(
        AgentToolCall call,
        AgentSession session,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) || string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "当前会话尚未绑定小说项目，不能绑定知识到项目。",
                Phase = session.Phase,
                Suggestions = new[] { "先绑定或创建小说项目", "查询工作台状态" }
            };
        }

        var knowledgeId = Arg(call, "knowledgeId");
        if (string.IsNullOrWhiteSpace(knowledgeId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "缺少 knowledgeId，无法绑定知识到项目。",
                Phase = session.Phase,
                Suggestions = new[] { "先搜索或选择知识条目", "查询项目知识绑定" }
            };
        }

        var requestedStatus = Arg(call, "status", "imported").Trim().ToLowerInvariant();
        if (requestedStatus is not "imported" and not "referenced")
            requestedStatus = "imported";

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var knowledge = await db.KnowledgeBases
            .AsNoTracking()
            .Where(item => item.UserId == session.UserId &&
                           item.Id == knowledgeId.Trim() &&
                           !item.IsArchived)
            .Select(item => new
            {
                item.Id,
                item.Title,
                item.EntryType,
                item.Content
            })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (knowledge == null)
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = $"未找到可绑定的知识条目 {knowledgeId}，或该条目已归档/不属于当前用户。",
                Phase = session.Phase,
                Suggestions = new[] { "重新搜索知识库", "查询工作台知识库状态" }
            };
        }

        var usageService = scope.ServiceProvider.GetRequiredService<IProjectKnowledgeUsageService>();
        if (requestedStatus == "referenced")
        {
            await usageService.MarkReferencedAsync(
                    session.UserId,
                    session.ActiveProjectId,
                    knowledge.Id,
                    session.SessionId,
                    session.ActiveRunId,
                    $"agent-knowledge-binding:{FirstNonEmpty(session.ActiveRunId, session.SessionId)}:{knowledge.Id}",
                    ct)
                .ConfigureAwait(false);
        }
        else
        {
            await usageService.MarkImportedAsync(
                    session.UserId,
                    session.ActiveProjectId,
                    knowledge.Id,
                    session.SessionId,
                    "agent_attach",
                    ct)
                .ConfigureAwait(false);
        }

        var usage = await db.ProjectKnowledgeUsages
            .AsNoTracking()
            .Where(item => item.UserId == session.UserId &&
                           item.ProjectId == session.ActiveProjectId &&
                           item.KnowledgeId == knowledge.Id)
            .Select(item => new
            {
                item.Id,
                item.Status,
                item.Role,
                item.Scope,
                item.Priority,
                item.ConstraintLevel,
                item.PackagePolicy,
                item.BoundVersion,
                item.UsageCount,
                item.LastUsedAt
            })
            .FirstAsync(ct)
            .ConfigureAwait(false);

        var nextTools = new[] { "QueryProjectKnowledgeBindings", "ClassifyProjectKnowledge", "DetectKnowledgeConflicts", "ProduceChapter" };
        var data = new
        {
            UsageId = usage.Id,
            ProjectId = session.ActiveProjectId,
            KnowledgeId = knowledge.Id,
            knowledge.Title,
            knowledge.EntryType,
            usage.Status,
            usage.Role,
            usage.Scope,
            usage.Priority,
            usage.ConstraintLevel,
            usage.PackagePolicy,
            usage.BoundVersion,
            usage.UsageCount,
            usage.LastUsedAt,
            NextRecommendedTools = nextTools
        };
        var message =
            $"已绑定知识 {knowledge.Id}「{knowledge.Title}」到当前项目，状态：{usage.Status}，约束级别：{usage.ConstraintLevel}，生产包策略：{usage.PackagePolicy}。下一步可调用 QueryProjectKnowledgeBindings、ClassifyProjectKnowledge、DetectKnowledgeConflicts，再由 Agent 决定是否 ProduceChapter。";
        var artifact = BuildArtifact(
            "project_knowledge_binding",
            knowledge.Id,
            session.ActiveProjectId,
            session.ActiveRunId ?? string.Empty,
            message,
            nextTools);
        artifact.UserVisibleWhere = new[] { "知识库", "创作工作流", "Agent 对话" };

        return new AgentToolExecutionResult
        {
            Success = true,
            Message = message,
            RunId = session.ActiveRunId ?? string.Empty,
            Phase = session.Phase,
            Data = data,
            Artifact = artifact,
            Suggestions = nextTools
        };
    }

    private async Task<AgentToolExecutionResult> ClassifyProjectKnowledgeAsync(
        AgentToolCall call,
        AgentSession session,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) || string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "当前会话尚未绑定小说项目，不能分类项目知识。",
                Phase = session.Phase,
                Suggestions = new[] { "先绑定或创建小说项目", "查询工作台状态" }
            };
        }

        var knowledgeId = Arg(call, "knowledgeId");
        if (string.IsNullOrWhiteSpace(knowledgeId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "缺少 knowledgeId，无法分类项目知识。",
                Phase = session.Phase,
                Suggestions = new[] { "先查询项目知识绑定", "选择要分类的知识条目" }
            };
        }

        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IKnowledgeClassificationService>();
        var result = await service.ClassifyAndApplyAsync(
                new KnowledgeClassificationRequest(
                    session.UserId,
                    session.ActiveProjectId,
                    knowledgeId.Trim(),
                    session.SessionId,
                    session.ActiveRunId),
                ct)
            .ConfigureAwait(false);

        var nextTools = new[] { "QueryProjectKnowledgeBindings", "DetectKnowledgeConflicts", "ProduceChapter" };

        // 智能聚合提示：检查已分类知识数量
        var aggregationHint = await ShouldSuggestAggregationAsync(session.UserId, session.ActiveProjectId, ct)
            .ConfigureAwait(false);

        var data = new KnowledgeClassificationToolResult
        {
            ClassificationId = result.Id,
            KnowledgeId = result.KnowledgeId,
            ProjectId = result.ProjectId,
            Role = result.Role,
            Scope = result.Scope,
            Priority = result.Priority,
            ConstraintLevel = result.ConstraintLevel,
            PackagePolicy = result.PackagePolicy,
            TargetEntities = result.TargetEntities,
            Rule = result.Rule,
            ShouldEnterGate = result.ShouldEnterGate,
            ShouldEnterBlueprint = result.ShouldEnterBlueprint,
            ShouldEnterFactSnapshot = result.ShouldEnterFactSnapshot,
            Confidence = result.Confidence,
            NextRecommendedTools = nextTools
        };
        var message =
            $"已将知识 {result.KnowledgeId} 分类为 {result.Role} / {result.ConstraintLevel}，生产包策略：{result.PackagePolicy}，优先级：{result.Priority}。{aggregationHint}下一步可查询绑定、检测冲突，或由 Agent 决定是否 ProduceChapter。";
        var artifact = BuildArtifact(
            "project_knowledge_classification",
            result.KnowledgeId,
            session.ActiveProjectId,
            session.ActiveRunId ?? string.Empty,
            message,
            new[] { "查询项目知识绑定", "检查知识冲突", "继续写章节" });
        artifact.UserVisibleWhere = new[] { "知识库", "创作工作流" };
        return new AgentToolExecutionResult
        {
            Success = true,
            Message = message,
            RunId = session.ActiveRunId ?? string.Empty,
            Phase = session.Phase,
            Data = data,
            Artifact = artifact,
            Suggestions = new[] { "查询项目知识绑定", "检查知识冲突", "继续写章节" }
        };
    }

    private async Task<AgentToolExecutionResult> DetectKnowledgeConflictsAsync(
        AgentToolCall call,
        AgentSession session,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) || string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "当前会话尚未绑定小说项目，不能检测项目知识冲突。",
                Phase = session.Phase,
                Suggestions = new[] { "先绑定或创建小说项目", "查询工作台状态" }
            };
        }

        var knowledgeId = Arg(call, "knowledgeId");
        if (string.IsNullOrWhiteSpace(knowledgeId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "缺少 knowledgeId，无法检测知识冲突。",
                Phase = session.Phase,
                Suggestions = new[] { "先查询项目知识绑定", "选择要检测的知识条目" }
            };
        }

        using var scope = _serviceProvider.CreateScope();
        var detector = scope.ServiceProvider.GetRequiredService<IKnowledgeConflictDetector>();
        var result = await detector.DetectAsync(
                new KnowledgeConflictDetectionRequest(
                    session.UserId,
                    session.ActiveProjectId,
                    knowledgeId.Trim(),
                    session.SessionId,
                    session.ActiveRunId),
                ct)
            .ConfigureAwait(false);

        var blocksProduceChapter = result.HasConflict
            && (result.RequiresUserDecision || string.Equals(result.Severity, "Hard", StringComparison.OrdinalIgnoreCase));
        var nextTools = blocksProduceChapter
            ? new[] { "ResolveKnowledgeConflict", "QueryProjectKnowledgeBindings", "CreateCreativeIntent" }
            : new[] { "QueryProjectKnowledgeBindings", "ProduceChapter" };
        var data = new KnowledgeConflictDetectionToolResult
        {
            ReportId = result.ReportId,
            KnowledgeId = result.KnowledgeId,
            ProjectId = result.ProjectId,
            HasConflict = result.HasConflict,
            ConflictType = result.ConflictType,
            Severity = result.Severity,
            ImpactScope = result.ImpactScope,
            ConflictingKnowledgeIds = result.ConflictingKnowledgeIds,
            Explanation = result.Explanation,
            RecommendedAction = result.RecommendedAction,
            RequiresUserDecision = result.RequiresUserDecision,
            BlocksProduceChapter = blocksProduceChapter,
            NextRecommendedTools = nextTools
        };
        var message = result.HasConflict
            ? $"已发现知识{FormatConflictSeverity(result.Severity)}：{result.Explanation} 建议：{result.RecommendedAction}"
            : $"未发现知识 {result.KnowledgeId} 与当前项目绑定知识存在明显冲突。";
        var artifact = BuildArtifact(
            "knowledge_conflict_report",
            string.IsNullOrWhiteSpace(result.ReportId) ? result.KnowledgeId : result.ReportId,
            session.ActiveProjectId,
            session.ActiveRunId ?? string.Empty,
            message,
            result.HasConflict
                ? new[] { "向用户解释冲突并请求选择", "查询项目知识绑定", "创建创意意图或修订计划" }
                : new[] { "构建章节生产包", "继续写章节" });
        artifact.UserVisibleWhere = new[] { "知识库", "创作工作流" };

        return new AgentToolExecutionResult
        {
            Success = true,
            Message = message,
            RunId = session.ActiveRunId ?? string.Empty,
            Phase = session.Phase,
            Data = data,
            RecommendedToolName = blocksProduceChapter ? "ResolveKnowledgeConflict" : string.Empty,
            Artifact = artifact,
            Suggestions = result.HasConflict
                ? new[] { "向用户解释冲突并请求选择", "查询项目知识绑定", "创建创意意图或修订计划" }
                : new[] { "构建章节生产包", "继续写章节" }
        };
    }

    private static string FormatConflictSeverity(string severity) =>
        severity.Trim().ToLowerInvariant() switch
        {
            "hard" => "硬冲突",
            "medium" => "中等冲突",
            "soft" => "软冲突",
            _ => "冲突"
        };

    private async Task<AgentToolExecutionResult> ResolveKnowledgeConflictAsync(
        AgentToolCall call,
        AgentSession session,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) || string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "当前会话尚未绑定小说项目，不能解决项目知识冲突。",
                Phase = session.Phase,
                Suggestions = new[] { "先绑定或创建小说项目", "查询工作台状态" }
            };
        }

        var conflictId = Arg(call, "conflictId", Arg(call, "reportId"));
        var decision = Arg(call, "decision", Arg(call, "status"));
        var note = FirstNonEmpty(Arg(call, "note"), Arg(call, "reason"), Arg(call, "resolutionNote"));
        if (string.IsNullOrWhiteSpace(conflictId) ||
            string.IsNullOrWhiteSpace(decision) ||
            string.IsNullOrWhiteSpace(note))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "缺少 conflictId、decision 或 note，无法解决知识冲突。",
                Phase = session.Phase,
                Suggestions = new[] { "查询项目知识绑定", "向用户确认保留哪条设定", "补充处理理由" }
            };
        }

        using var scope = _serviceProvider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IKnowledgeConflictResolver>();
        var result = await resolver.ResolveAsync(
                new KnowledgeConflictResolutionRequest(
                    session.UserId,
                    session.ActiveProjectId,
                    conflictId.Trim(),
                    decision.Trim(),
                    note.Trim(),
                    session.SessionId,
                    session.ActiveRunId),
                ct)
            .ConfigureAwait(false);

        var message = $"知识冲突 {result.ConflictId} 已更新为 {result.Status}。处理说明：{result.Note}";
        var suggestions = new[] { "查询生产状态", "继续章节生产", "查询项目知识绑定" };
        return new AgentToolExecutionResult
        {
            Success = true,
            Message = message,
            RunId = session.ActiveRunId ?? string.Empty,
            Phase = session.Phase,
            Data = result,
            Artifact = BuildArtifact(
                "knowledge_conflict_resolution",
                result.ConflictId,
                session.ActiveProjectId,
                session.ActiveRunId ?? string.Empty,
                message,
                suggestions),
            Suggestions = suggestions,
            RecommendedToolName = "ProduceChapter"
        };
    }

    private async Task<AgentToolExecutionResult> CreateCreativeIntentAsync(
        AgentToolCall call,
        AgentSession session,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) || string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "当前会话尚未绑定小说项目，不能创建创意意图。",
                Phase = session.Phase,
                Suggestions = new[] { "先绑定或创建小说项目", "查询工作台状态" }
            };
        }

        var rawContent = FirstNonEmpty(Arg(call, "rawContent"), Arg(call, "content"), Arg(call, "normalizedIntent"));
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "缺少创意原文 rawContent。",
                Phase = session.Phase,
                Suggestions = new[] { "补充创意内容", "继续对话澄清" }
            };
        }

        using var scope = _serviceProvider.CreateScope();
        var creativeService = scope.ServiceProvider.GetRequiredService<ICreativeIntentService>();
        var item = await creativeService.CreateAsync(new CreateCreativeIntentRequest(
                UserId: session.UserId,
                ProjectId: session.ActiveProjectId,
                SessionId: session.SessionId,
                RunId: session.ActiveRunId ?? string.Empty,
                IdempotencyKey: $"creative-intent:{session.ActiveRunId ?? session.SessionId}:{Arg(call, "targetChapterId")}:{ComputeStableHash(rawContent)}",
                RawContent: rawContent,
                NormalizedIntent: FirstNonEmpty(Arg(call, "normalizedIntent"), rawContent),
                Source: Arg(call, "source", "chat"),
                TargetScope: Arg(call, "targetScope", "project"),
                TargetVolumeId: Arg(call, "targetVolumeId"),
                TargetChapterId: Arg(call, "targetChapterId"),
                TargetCharacterName: Arg(call, "targetCharacterName"),
                ImpactLevel: Arg(call, "impactLevel", "future_carry"),
                RequiresConfirmation: ArgBool(call, "requiresConfirmation", false),
                ConflictStatus: Arg(call, "conflictStatus", "unknown"),
                MetadataJson: Arg(call, "metadataJson", "{}")),
            ct)
            .ConfigureAwait(false);
        if (item == null)
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "当前项目不存在或无权访问，不能创建创意意图。",
                Phase = session.Phase,
                Suggestions = new[] { "重新绑定项目", "查询工作台状态" }
            };
        }

        var message = $"已记录创意意图：{item.NormalizedIntent}（范围：{item.TargetScope}，状态：{item.Status}）。";
        return new AgentToolExecutionResult
        {
            Success = true,
            Message = message,
            RunId = session.ActiveRunId ?? string.Empty,
            Phase = session.Phase,
            Data = item,
            Artifact = BuildArtifact("creative_intent_created", item.Id, session.ActiveProjectId, session.ActiveRunId ?? string.Empty, message, new[] { "采纳创意", "查看创意收件箱", "构建章节生产包" }),
            Suggestions = new[] { "采纳创意", "查看创意收件箱", "构建章节生产包" }
        };
    }

    private async Task<AgentToolExecutionResult> DecideCreativeIntentAsync(
        AgentToolCall call,
        AgentSession session,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) || string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "当前会话尚未绑定小说项目，不能决策创意意图。",
                Phase = session.Phase,
                Suggestions = new[] { "先绑定或创建小说项目", "查询工作台状态" }
            };
        }

        var intentId = Arg(call, "intentId");
        if (string.IsNullOrWhiteSpace(intentId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "缺少 intentId。",
                Phase = session.Phase,
                Suggestions = new[] { "查询创意收件箱", "重新选择创意" }
            };
        }

        using var scope = _serviceProvider.CreateScope();
        var creativeService = scope.ServiceProvider.GetRequiredService<ICreativeIntentService>();
        var item = await creativeService.DecideAsync(new DecideCreativeIntentRequest(
                UserId: session.UserId,
                ProjectId: session.ActiveProjectId,
                IntentId: intentId,
                Status: Arg(call, "status", "accepted"),
                DecisionReason: Arg(call, "decisionReason"),
                ConflictStatus: Arg(call, "conflictStatus"),
                MarkExecuted: ArgBool(call, "markExecuted", false)),
            ct)
            .ConfigureAwait(false);
        if (item == null)
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = $"未找到创意意图 {intentId}。",
                Phase = session.Phase,
                Suggestions = new[] { "查询创意收件箱", "重新创建创意" }
            };
        }

        var message = $"创意意图已更新为 {item.Status}：{item.NormalizedIntent}";
        return new AgentToolExecutionResult
        {
            Success = true,
            Message = message,
            RunId = session.ActiveRunId ?? string.Empty,
            Phase = session.Phase,
            Data = item,
            Artifact = BuildArtifact("creative_intent_decision", item.Id, session.ActiveProjectId, session.ActiveRunId ?? string.Empty, message, new[] { "构建章节生产包", "查看创意收件箱", "继续写章节" }),
            Suggestions = new[] { "构建章节生产包", "查看创意收件箱", "继续写章节" }
        };
    }

    private async Task<AgentToolExecutionResult> QueryCreativeIntentsAsync(
        AgentToolCall call,
        AgentSession session,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) || string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "当前会话尚未绑定小说项目，不能查询创意收件箱。",
                Phase = session.Phase,
                Suggestions = new[] { "先绑定或创建小说项目", "查询工作台状态" }
            };
        }

        var status = Arg(call, "status");
        var targetChapterId = Arg(call, "targetChapterId");
        var limit = Math.Clamp(ArgInt(call, "limit", 20), 1, 80);

        using var scope = _serviceProvider.CreateScope();
        var creativeService = scope.ServiceProvider.GetRequiredService<ICreativeIntentService>();
        var result = await creativeService.QueryAsync(new QueryCreativeIntentsRequest(
                UserId: session.UserId,
                ProjectId: session.ActiveProjectId,
                Status: status,
                TargetChapterId: targetChapterId,
                Limit: limit),
            ct)
            .ConfigureAwait(false);
        var message = FormatCreativeIntents(result);
        return new AgentToolExecutionResult
        {
            Success = true,
            Message = message,
            RunId = session.ActiveRunId ?? string.Empty,
            Phase = session.Phase,
            Data = result,
            Artifact = BuildArtifact("creative_intents_query", session.ActiveProjectId, session.ActiveProjectId, session.ActiveRunId ?? string.Empty, message, new[] { "采纳创意", "构建章节生产包", "继续写章节" }),
            Suggestions = new[] { "采纳创意", "构建章节生产包", "继续写章节" }
        };
    }

    private async Task<AgentToolExecutionResult> CreateRevisionPlanAsync(
        AgentToolCall call,
        AgentSession session,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) || string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "当前会话尚未绑定小说项目，不能创建修订计划。",
                Phase = session.Phase,
                Suggestions = new[] { "先绑定或创建小说项目", "查询工作台状态" }
            };
        }

        var recommendation = Arg(call, "recommendation");
        if (string.IsNullOrWhiteSpace(recommendation))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "缺少 recommendation，无法创建修订计划。",
                Phase = session.Phase,
                Suggestions = new[] { "补充修订建议", "查询创意收件箱", "查询生产状态" }
            };
        }

        var targetChapterId = Arg(call, "targetChapterId");
        using var scope = _serviceProvider.CreateScope();
        var revisionService = scope.ServiceProvider.GetRequiredService<IRevisionPlanService>();
        var item = await revisionService.CreateAsync(
                new CreateRevisionPlanRequest(
                    UserId: session.UserId,
                    ProjectId: session.ActiveProjectId,
                    SessionId: session.SessionId,
                    RunId: session.ActiveRunId ?? string.Empty,
                    IdempotencyKey: $"revision-plan:{session.ActiveRunId ?? session.SessionId}:{targetChapterId}:{ComputeStableHash(recommendation)}",
                    Source: Arg(call, "source", "user_request"),
                    PlanType: Arg(call, "planType", Arg(call, "impactLevel", "future_carry")),
                    TargetScope: Arg(call, "targetScope", string.IsNullOrWhiteSpace(targetChapterId) ? "project" : "chapter"),
                    TargetVolumeId: Arg(call, "targetVolumeId"),
                    TargetChapterId: targetChapterId,
                    CreativeIntentId: Arg(call, "creativeIntentId", Arg(call, "intentId")),
                    KnowledgeConflictReportId: Arg(call, "knowledgeConflictReportId", Arg(call, "conflictId")),
                    Status: Arg(call, "status", "draft"),
                    RequirementsJson: Arg(call, "requirementsJson", "[]"),
                    ContinuityRequirementsJson: Arg(call, "continuityRequirementsJson", "[]"),
                    ImpactAnalysisJson: Arg(call, "impactAnalysisJson", "{}"),
                    AffectedChapterIdsJson: Arg(call, "affectedChapterIdsJson", "[]"),
                    InvalidatedPackageIdsJson: Arg(call, "invalidatedPackageIdsJson", "[]"),
                    RiskLevel: Arg(call, "riskLevel", "medium"),
                    Recommendation: recommendation),
                ct)
            .ConfigureAwait(false);
        if (item == null)
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "当前项目不存在或无权访问，不能创建修订计划。",
                Phase = session.Phase,
                Suggestions = new[] { "重新绑定项目", "查询工作台状态" }
            };
        }

        RevisionPlanPackageInvalidationResult? invalidation = null;
        var invalidationService = scope.ServiceProvider.GetService<IRevisionPlanPackageInvalidationService>();
        if (invalidationService != null && ShouldAutoInvalidateCreatedRevisionPlan(item))
        {
            invalidation = await invalidationService.InvalidateAsync(
                    new InvalidateRevisionPlanPackagesRequest(
                        UserId: session.UserId,
                        ProjectId: session.ActiveProjectId,
                        SessionId: session.SessionId,
                        RuntimeRunId: session.ActiveRunId ?? item.RunId,
                        RevisionPlanId: item.Id),
                    ct)
                .ConfigureAwait(false);
            if (invalidation.Success)
            {
                item.Status = invalidation.Status;
                item.AffectedChapterIdsJson = JsonSerializer.Serialize(invalidation.AffectedChapterIds);
                item.InvalidatedPackageIdsJson = JsonSerializer.Serialize(invalidation.InvalidatedPackageIds);
            }
        }

        var message = invalidation?.Success == true
            ? $"已创建修订计划：{item.Recommendation}（范围：{item.TargetScope}，状态：{item.Status}），并已使 {invalidation.InvalidatedPackageIds.Count} 个旧生产包失效。"
            : $"已创建修订计划：{item.Recommendation}（范围：{item.TargetScope}，状态：{item.Status}）。";
        return new AgentToolExecutionResult
        {
            Success = true,
            Message = message,
            RunId = session.ActiveRunId ?? string.Empty,
            Phase = session.Phase,
            Data = item,
            Artifact = BuildArtifact("revision_plan_created", item.Id, session.ActiveProjectId, session.ActiveRunId ?? string.Empty, message, new[] { "查询修订计划", "查询生产状态", "重建章节生产包" }),
            Suggestions = new[] { "查询修订计划", "查询生产状态", "重建章节生产包" },
            RecommendedToolName = invalidation?.Success == true ? "QueryNovelProductionState" : "QueryRevisionPlans"
        };
    }

    private static bool ShouldAutoInvalidateCreatedRevisionPlan(RevisionPlanItem item)
    {
        if (!string.Equals(item.Status, "accepted", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(item.Status, "ready_for_rebuild", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(item.Status, "executing", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return HasJsonArrayItems(item.AffectedChapterIdsJson) ||
               HasJsonArrayItems(item.InvalidatedPackageIdsJson);
    }

    private static bool HasJsonArrayItems(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array &&
                   document.RootElement.EnumerateArray().Any();
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<AgentToolExecutionResult> QueryRevisionPlansAsync(
        AgentToolCall call,
        AgentSession session,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) || string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "当前会话尚未绑定小说项目，不能查询修订计划。",
                Phase = session.Phase,
                Suggestions = new[] { "先绑定或创建小说项目", "查询工作台状态" }
            };
        }

        var status = Arg(call, "status");
        var targetChapterId = Arg(call, "targetChapterId");
        var limit = Math.Clamp(ArgInt(call, "limit", 20), 1, 80);

        using var scope = _serviceProvider.CreateScope();
        var revisionService = scope.ServiceProvider.GetRequiredService<IRevisionPlanService>();
        var result = await revisionService.QueryAsync(
                new QueryRevisionPlansRequest(
                    UserId: session.UserId,
                    ProjectId: session.ActiveProjectId,
                    Status: status,
                    TargetChapterId: targetChapterId,
                    Limit: limit),
                ct)
            .ConfigureAwait(false);
        var message = FormatRevisionPlans(result);
        return new AgentToolExecutionResult
        {
            Success = true,
            Message = message,
            RunId = session.ActiveRunId ?? string.Empty,
            Phase = session.Phase,
            Data = result,
            Artifact = BuildArtifact("revision_plans_query", session.ActiveProjectId, session.ActiveProjectId, session.ActiveRunId ?? string.Empty, message, new[] { "创建修订计划", "查询生产状态", "继续章节生产" }),
            Suggestions = new[] { "创建修订计划", "查询生产状态", "继续章节生产" }
        };
    }

    private async Task<AgentToolExecutionResult> InvalidateAffectedPackagesAsync(
        AgentToolCall call,
        AgentSession session,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) || string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "当前会话尚未绑定小说项目，不能执行修订计划包失效。",
                Phase = session.Phase,
                Suggestions = new[] { "先绑定或创建小说项目", "查询工作台状态" }
            };
        }

        var revisionPlanId = Arg(call, "revisionPlanId", Arg(call, "planId"));
        if (string.IsNullOrWhiteSpace(revisionPlanId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "缺少 revisionPlanId，无法执行受影响生产包失效。",
                Phase = session.Phase,
                Suggestions = new[] { "查询修订计划", "查询生产状态" }
            };
        }

        using var scope = _serviceProvider.CreateScope();
        var invalidationService = scope.ServiceProvider.GetRequiredService<IRevisionPlanPackageInvalidationService>();
        var result = await invalidationService.InvalidateAsync(
                new InvalidateRevisionPlanPackagesRequest(
                    UserId: session.UserId,
                    ProjectId: session.ActiveProjectId,
                    SessionId: session.SessionId,
                    RuntimeRunId: session.ActiveRunId ?? string.Empty,
                    RevisionPlanId: revisionPlanId),
                ct)
            .ConfigureAwait(false);

        var nextHints = result.Success
            ? new[] { "查询生产状态", "重建章节生产包", "继续生产章节" }
            : new[] { "查询修订计划", "查询生产状态", "补充影响分析" };
        var message = result.Message;
        return new AgentToolExecutionResult
        {
            Success = result.Success,
            Message = message,
            RunId = session.ActiveRunId ?? string.Empty,
            Phase = session.Phase,
            Data = result,
            Artifact = BuildArtifact("revision_plan_packages_invalidated", revisionPlanId, session.ActiveProjectId, session.ActiveRunId ?? string.Empty, message, nextHints),
            Suggestions = nextHints,
            IsRepairable = false,
            RecommendedToolName = result.Success ? "QueryNovelProductionState" : "QueryRevisionPlans",
            RecommendedArguments = result.Success
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["projectId"] = session.ActiveProjectId,
                    ["includeEvents"] = "true"
                }
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
    }

    private async Task<int> InjectAcceptedCreativeIntentsAsync(
        AgentSession session,
        NovelAgentExecutionResult result,
        CancellationToken ct)
    {
        var package = result.ContextPackage ?? result.Run?.ContextPackage;
        if (package == null ||
            string.IsNullOrWhiteSpace(session.UserId) ||
            string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            return 0;
        }

        using var scope = _serviceProvider.CreateScope();
        var creativeService = scope.ServiceProvider.GetRequiredService<ICreativeIntentService>();
        var accepted = await creativeService.GetAcceptedSnapshotsForPackageAsync(
                session.UserId,
                session.ActiveProjectId,
                package,
                limit: 48,
                ct)
            .ConfigureAwait(false);

        var added = 0;
        foreach (var snapshot in accepted)
        {
            if (package.AcceptedCreativeIntents.Any(existing => string.Equals(existing.IntentId, snapshot.IntentId, StringComparison.OrdinalIgnoreCase)))
                continue;

            package.AcceptedCreativeIntents.Add(snapshot);
            AddIfPresent(package.HardContinuityFacts, $"已采纳创意：{snapshot.NormalizedIntent}");
            added++;
        }

        if (added <= 0 || result.Run == null)
            return added;

        result.Run.ContextPackage = package;
        result.Run.Notes.Add($"已注入已采纳创意 {added} 条到章节上下文包。");
        await _workspace.StoryBibleService.SaveRunAsync(result.Run, ct).ConfigureAwait(false);
        return added;
    }

    private async Task<int> InjectReadyRevisionPlansAsync(
        AgentSession session,
        NovelAgentExecutionResult result,
        string revisionPlanId,
        CancellationToken ct)
    {
        var package = result.ContextPackage ?? result.Run?.ContextPackage;
        if (package == null ||
            string.IsNullOrWhiteSpace(session.UserId) ||
            string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            return 0;
        }

        using var scope = _serviceProvider.CreateScope();
        var revisionService = scope.ServiceProvider.GetService<IRevisionPlanService>();
        if (revisionService == null)
            return 0;

        var plans = await revisionService.QueryAsync(
                new QueryRevisionPlansRequest(
                    UserId: session.UserId,
                    ProjectId: session.ActiveProjectId,
                    Status: "ready_for_rebuild",
                    TargetChapterId: package.ChapterId,
                    Limit: 16),
                ct)
            .ConfigureAwait(false);

        var selectedPlans = string.IsNullOrWhiteSpace(revisionPlanId)
            ? plans.Items
            : plans.Items
                .Where(plan => string.Equals(plan.Id, revisionPlanId.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();

        var added = 0;
        foreach (var plan in selectedPlans)
        {
            if (InjectRevisionPlanSnapshot(package, plan))
                added++;
        }

        if (added <= 0 || result.Run == null)
            return added;

        NormalizeContextLists(package);
        result.Run.ContextPackage = package;
        result.Run.Notes.Add($"已注入待重建修订计划 {added} 条到章节上下文包。");
        await _workspace.StoryBibleService.SaveRunAsync(result.Run, ct).ConfigureAwait(false);
        return added;
    }

    private async Task EnsureChapterContextPackagePersistedAsync(
        AgentSession session,
        string runId,
        NovelAgentExecutionResult result,
        CancellationToken ct)
    {
        var package = result.ContextPackage ?? result.Run?.ContextPackage;
        if (package == null)
            return;

        if (!string.IsNullOrWhiteSpace(package.PackageId) &&
            await IsChapterContextPackagePersistedAsync(session, runId, package.PackageId, ct).ConfigureAwait(false))
        {
            return;
        }

        await PersistChapterContextPackageAsync(session, runId, result, package, ct).ConfigureAwait(false);
    }

    private async Task<bool> IsChapterContextPackagePersistedAsync(
        AgentSession session,
        string runId,
        string packageId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.ActiveProjectId))
            return false;

        using var scope = _serviceProvider.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<IChapterContextPackageRecorder>();
        return await recorder.ExistsAsync(session.ActiveProjectId, runId, packageId, ct)
            .ConfigureAwait(false);
    }

    private async Task PersistChapterContextPackageAsync(
        AgentSession session,
        string runId,
        NovelAgentExecutionResult result,
        ChapterContextPackageSummary package,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) || string.IsNullOrWhiteSpace(session.ActiveProjectId))
            throw new InvalidOperationException("Chapter context package persistence requires userId and activeProjectId.");

        using var scope = _serviceProvider.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<IChapterContextPackageRecorder>();

        var createdPackage = await recorder.RecordAsync(
                new RecordChapterContextPackageRequest(
                    RuntimeRunId: runId,
                    UserId: session.UserId,
                    SessionId: session.SessionId,
                    ProjectId: session.ActiveProjectId,
                    UserGoal: result.Run?.UserGoal,
                    RunUpdatedAt: result.Run?.UpdatedAt,
                    Package: package,
                    AgentRuntimeRunId: string.IsNullOrWhiteSpace(session.RuntimeRunId) ? null : session.RuntimeRunId),
                ct)
            .ConfigureAwait(false);

        package.PackageId = createdPackage.Id;
        if (result.Run != null)
        {
            result.Run.ContextPackage = package;
            result.Run.Notes.Add($"章节生产包已持久化：{createdPackage.Id}");
            await _workspace.StoryBibleService.SaveRunAsync(result.Run, ct).ConfigureAwait(false);
        }
    }

    private async Task<int> InjectDatabaseHardFactsAsync(
        AgentSession session,
        NovelAgentExecutionResult result,
        CancellationToken ct)
    {
        var package = result.ContextPackage ?? result.Run?.ContextPackage;
        if (package == null)
            return 0;

        var added = 0;
        var query = BuildContextKnowledgeQuery(result.Run, package);
        using var scope = _serviceProvider.CreateScope();
        var enrichmentService = scope.ServiceProvider.GetRequiredService<IChapterContextEnrichmentService>();
        var enrichment = await enrichmentService.BuildAsync(new ChapterContextEnrichmentRequest(
                UserId: session.UserId,
                ProjectId: session.ActiveProjectId ?? string.Empty,
                SessionId: session.SessionId,
                RunId: session.ActiveRunId ?? string.Empty,
                Query: query,
                Package: package),
            ct)
            .ConfigureAwait(false);

        foreach (var binding in enrichment.KnowledgeBindings)
        {
            if (package.KnowledgeBindings.Any(existing => string.Equals(existing.KnowledgeId, binding.KnowledgeId, StringComparison.OrdinalIgnoreCase)))
                continue;

            package.KnowledgeBindings.Add(binding);
            added++;
        }

        foreach (var fact in enrichment.HardFacts)
        {
            if (package.HardContinuityFacts.Any(existing => string.Equals(existing, fact, StringComparison.OrdinalIgnoreCase)))
                continue;

            package.HardContinuityFacts.Add(fact);
            added++;
        }

        foreach (var summary in enrichment.PreviousSummaries)
        {
            if (package.PreviousSummaries.Any(existing => string.Equals(existing, summary, StringComparison.OrdinalIgnoreCase)))
                continue;
            package.PreviousSummaries.Insert(0, summary);
            added++;
        }

        foreach (var state in enrichment.CharacterStates)
        {
            if (package.CharacterStates.Any(existing => string.Equals(existing, state, StringComparison.OrdinalIgnoreCase)))
                continue;
            package.CharacterStates.Add(state);
            added++;
        }

        foreach (var conflict in enrichment.ActiveConflicts)
        {
            if (package.ActiveConflicts.Any(existing => string.Equals(existing, conflict, StringComparison.OrdinalIgnoreCase)))
                continue;
            package.ActiveConflicts.Add(conflict);
            added++;
        }

        foreach (var source in enrichment.SourceWarnings)
        {
            if (package.Warnings.Any(existing => string.Equals(existing, source, StringComparison.OrdinalIgnoreCase)))
                continue;
            package.Warnings.Add(source);
            added++;
        }

        // Inject persisted DesignRules and ChapterBlueprint (P0 fix - design doc Section 4)
        try
        {
            var packageEnrichment = scope.ServiceProvider
                .GetService<TM.Web.NovelAgentWeb.Services.Production.IChapterPackageEnrichmentService>();
            if (packageEnrichment != null)
            {
                await packageEnrichment.EnrichAsync(
                        package,
                        session.UserId,
                        session.ActiveProjectId ?? string.Empty,
                        result.Run?.TargetChapterId ?? string.Empty,
                        ct)
                    .ConfigureAwait(false);

                if (package.DesignRules.Count > 0)
                {
                    result.Run?.Notes.Add($"已注入设计规则 {package.DesignRules.Count} 条到章节上下文包。");
                    added += package.DesignRules.Count;
                }
                if (package.PersistedBlueprint != null)
                {
                    result.Run?.Notes.Add($"已注入持久化章节蓝图 v{package.PersistedBlueprint.Version}（{package.PersistedBlueprint.Title}）。");
                    added++;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to enrich package with DesignRules/Blueprint");
        }

        if (added <= 0 || result.Run == null)
            return added;

        result.Run.ContextPackage = package;
        result.Run.Notes.Add($"已注入知识库硬事实 {added} 条到章节上下文包。");
        await _workspace.StoryBibleService.SaveRunAsync(result.Run, ct).ConfigureAwait(false);
        return added;
    }

    private static string FormatProjectKnowledgeBindings(ProjectKnowledgeBindingsQueryResult result)
    {
        if (result.Bindings.Count == 0 &&
            result.CanonLedger.Count == 0 &&
            result.ConflictReports.Count == 0)
        {
            return "当前项目还没有绑定知识条目。";
        }

        var lines = new List<string>
        {
            $"当前项目已绑定知识 {result.BindingCount} 条，其中硬事实 {result.HardFactCount} 条；Story Bible Canon {result.CanonCount} 条。",
            $"知识状态：已导入 {result.StatusSummary.ImportedCount} 条，已引用 {result.StatusSummary.ReferencedCount} 条，已进入 CanonLedger {result.StatusSummary.CanonLedgerCount} 条，待分类 {result.StatusSummary.PendingClassificationCount} 条，开放冲突 {result.StatusSummary.OpenConflictCount} 条。",
            $"知识入口：Gate {result.StatusSummary.ShouldEnterGateCount}，蓝图 {result.StatusSummary.ShouldEnterBlueprintCount}，FactSnapshot {result.StatusSummary.ShouldEnterFactSnapshotCount}。"
        };
        foreach (var binding in result.Bindings.Take(12))
        {
            var tags = binding.Tags.Count == 0 ? string.Empty : $"；标签：{string.Join("、", binding.Tags.Take(4))}";
            var source = string.IsNullOrWhiteSpace(binding.SourceSessionId) ? string.Empty : $"；来源会话：{binding.SourceSessionId}";
            var entrance = BuildKnowledgeEntranceLabel(binding);
            lines.Add($"- 【{binding.EntryType}】{binding.Title}（ID：{binding.KnowledgeId}；{binding.ProjectUsageStatus}；{entrance}；使用 {binding.ProjectUsageCount} 次{tags}{source}）：{TrimBody(binding.Content, 120)}");
        }

        if (result.HardFacts.Count > 0)
            lines.Add("硬事实摘要：" + string.Join("；", result.HardFacts.Take(6)));

        if (result.CanonLedger.Count > 0)
        {
            lines.Add($"Story Bible Canon 摘要 {result.CanonLedger.Count} 条：");
            foreach (var canon in result.CanonLedger.Take(8))
            {
                var conflict = string.IsNullOrWhiteSpace(canon.ConflictCheck)
                    ? string.Empty
                    : $"；冲突状态：{TrimBody(canon.ConflictCheck, 80)}";
                lines.Add($"- [{canon.Status}/{canon.Type}] {canon.Id}：{canon.Title}：{TrimBody(canon.Content, 120)}{conflict}");
            }
        }

        if (result.ConflictReports.Count > 0)
        {
            lines.Add($"知识冲突报告 {result.ConflictReports.Count} 条：");
            foreach (var report in result.ConflictReports.Take(8))
            {
                var decision = report.RequiresUserDecision ? "需要用户决定" : "不需要用户决定";
                var resolution = string.IsNullOrWhiteSpace(report.ResolutionNote)
                    ? string.Empty
                    : $"；处理说明：{TrimBody(report.ResolutionNote, 80)}";
                lines.Add($"- [{report.Status}/{report.Severity}] {report.ConflictId}：{TrimBody(report.Explanation, 120)}（{decision}{resolution}）");
            }
        }

        return string.Join("\n", lines);
    }

    private static string BuildKnowledgeEntranceLabel(BoundKnowledgeSnapshot binding)
    {
        var entrances = new List<string>();
        if (binding.ShouldEnterGate)
            entrances.Add("Gate");
        if (binding.ShouldEnterBlueprint)
            entrances.Add("蓝图");
        if (binding.ShouldEnterFactSnapshot)
            entrances.Add("FactSnapshot");

        var status = entrances.Count == 0 ? "未进入生产入口" : $"进入 {string.Join("/", entrances)}";
        var classification = string.IsNullOrWhiteSpace(binding.ClassificationId)
            ? "未分类"
            : $"已分类 {binding.ClassificationId}";
        return $"{classification}；{binding.ConstraintLevel}/{binding.PackagePolicy}；{status}";
    }

    private static string FormatCreativeIntents(CreativeIntentQueryResult result)
    {
        if (result.Items.Count == 0)
            return "当前项目没有匹配的创意意图。";

        var lines = new List<string>
        {
            $"当前项目创意意图 {result.Items.Count} 条（状态：{result.Status}）。"
        };
        foreach (var item in result.Items.Take(12))
        {
            var target = string.IsNullOrWhiteSpace(item.TargetChapterId)
                ? item.TargetScope
                : $"{item.TargetScope}:{item.TargetChapterId}";
            lines.Add($"- [{item.Status}] {item.NormalizedIntent}（{target}，影响：{item.ImpactLevel}，来源：{item.Source}，ID：{item.Id}）");
        }

        return string.Join("\n", lines);
    }

    private static string FormatRevisionPlans(RevisionPlanQueryResult result)
    {
        if (result.Items.Count == 0)
            return "当前项目没有匹配的修订计划。";

        var lines = new List<string>
        {
            $"当前项目修订计划 {result.Items.Count} 条（状态：{result.Status}）。"
        };
        foreach (var item in result.Items.Take(12))
        {
            var target = string.IsNullOrWhiteSpace(item.TargetChapterId)
                ? item.TargetScope
                : $"{item.TargetScope}:{item.TargetChapterId}";
            var invalidated = TryFormatJsonArraySummary(item.InvalidatedPackageIdsJson, "失效包");
            var requirements = TryFormatJsonArraySummary(item.RequirementsJson, "要求");
            var suffix = string.IsNullOrWhiteSpace(invalidated) ? string.Empty : $"，{invalidated}";
            var requirementPrefix = string.IsNullOrWhiteSpace(requirements) ? string.Empty : $"{requirements}；";
            lines.Add($"- [{item.Status}] {requirementPrefix}{item.Recommendation}（{target}，类型：{item.PlanType}，风险：{item.RiskLevel}{suffix}，ID：{item.Id}）");
        }

        return string.Join("\n", lines);
    }

    private static string TryFormatJsonArraySummary(string json, string label)
    {
        if (string.IsNullOrWhiteSpace(json))
            return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return string.Empty;

            var values = doc.RootElement
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .Take(4)
                .ToArray();
            return values.Length == 0 ? string.Empty : $"{label}：{string.Join("/", values)}";
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static IReadOnlyList<string> ParseJsonStringArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();

            return doc.RootElement
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static string BuildContextKnowledgeQuery(NovelAgentRun? run, ChapterContextPackageSummary package)
    {
        var parts = new List<string>
        {
            run?.UserGoal ?? string.Empty,
            run?.TargetChapterId ?? package.ChapterId,
            run?.ChapterBrief?.CoreIdea ?? string.Empty,
            run?.ChapterBrief?.ConflictMove ?? string.Empty,
            run?.ChapterBrief?.CharacterChoice ?? string.Empty
        };
        parts.AddRange(package.WorldRules.Take(4));
        parts.AddRange(package.CharacterStates.Take(4));
        parts.AddRange(package.ChapterBlueprints.Take(4));
        var query = string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));
        return string.IsNullOrWhiteSpace(query)
            ? "硬事实 连续性 角色 道具 世界规则 卷章结构"
            : query;
    }

    private static string FormatDatabaseHardFact(CreativeKnowledgeEntry entry)
    {
        var content = entry.Content.Trim();
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        var title = entry.Title.Trim();
        return string.IsNullOrWhiteSpace(title)
            ? $"知识库硬事实：{content}"
            : $"知识库硬事实：{title}：{content}";
    }

    private async Task<AgentToolExecutionResult> RunDraftGateStageAsync(string runId, AgentSession session, CancellationToken ct)
    {
        await AppendChapterRuntimeProgressEventAsync(
                session,
                runId,
                NovelAgentProductionStages.GateValidation,
                "running",
                "正在校验章节草稿。",
                "generation_gate_report",
                null,
                new { runId, stage = NovelAgentProductionStages.GateValidation, status = "running" },
                ct)
            .ConfigureAwait(false);

        var result = await RunWithProductionStageHeartbeatAsync(
                session,
                runId,
                NovelAgentProductionStages.GateValidation,
                "generation_gate_report",
                null,
                token => _workspace.Orchestrator.ValidateDraftGateStageAsync(runId, token),
                ct)
            .ConfigureAwait(false);
        var report = result.GateReport;
        session.Phase = report?.Status ?? session.Phase;
        var message = AppendStatusToMessage(result.Message, report?.Status);
        await AppendChapterProductionEventAsync(
                session,
                runId,
                result,
                eventType: "chapter_gate_validated",
                stage: NovelAgentProductionStages.GateValidation,
                status: result.Success ? "completed" : "failed",
                message,
                artifactType: "generation_gate_report",
                artifactId: report?.Status ?? "gate_report",
                data: new
                {
                    gateStatus = report?.Status ?? string.Empty,
                    report?.ProtocolPassed,
                    report?.ChangesDetected,
                    report?.FactSnapshotPassed,
                    report?.BlueprintPassed,
                    report?.RagPassed,
                    issues = report?.Issues ?? new List<string>(),
                    repairHints = report?.RepairHints ?? new List<string>()
                },
                ct)
            .ConfigureAwait(false);
        if (report != null)
            await RecordGenerationGateReportAsync(session, runId, result, report, ct).ConfigureAwait(false);
        return new AgentToolExecutionResult
        {
            Success = result.Success,
            Message = message,
            RunId = runId,
            Phase = session.Phase,
            Data = result,
            Artifact = BuildArtifact("generation_gate_report", result.Run?.TargetChapterId ?? runId, session.ActiveProjectId, runId, message, report?.Status == "validated" ? new[] { "提交成稿", "继续下一章" } : new[] { "修复章节草稿", "查看失败项" }),
            Suggestions = report?.Status == "validated" ? new[] { "提交成稿", "继续下一章" } : new[] { "修复章节草稿", "查看失败项" },
        };
    }

    private async Task AppendChapterProductionEventAsync(
        AgentSession session,
        string runId,
        NovelAgentExecutionResult result,
        string eventType,
        string stage,
        string status,
        string message,
        string? artifactType,
        string? artifactId,
        object? data,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) || string.IsNullOrWhiteSpace(session.ActiveProjectId))
            throw new InvalidOperationException("Chapter production event requires userId and activeProjectId.");

        var package = result.ContextPackage ?? result.Run?.ContextPackage;
        var packageId = package?.PackageId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(packageId))
            throw new InvalidOperationException($"Chapter production event requires a persisted package for run {runId}.");

        using var scope = _serviceProvider.CreateScope();
        var eventWriter = scope.ServiceProvider.GetRequiredService<IProductionEventWriter>();

        var productionEvent = await eventWriter.AppendChapterStageAsync(
                new AppendChapterProductionEventRequest(
                    RuntimeRunId: runId,
                    UserId: session.UserId,
                    ProjectId: session.ActiveProjectId,
                    ChapterId: package?.ChapterId ?? result.Run?.TargetChapterId,
                    PackageId: packageId,
                    EventType: eventType,
                    Stage: stage,
                    Status: status,
                    Message: message,
                    ArtifactType: artifactType,
                    ArtifactId: artifactId,
                    Data: data),
                ct)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(artifactType) && !string.IsNullOrWhiteSpace(artifactId))
        {
            var outputRecorder = scope.ServiceProvider.GetService<IOutputArtifactRecorder>();
            if (outputRecorder != null)
            {
                await outputRecorder.RecordAsync(
                        new OutputArtifactRecordRequest(
                            RuntimeRunId: runId,
                            UserId: session.UserId,
                            ProjectId: session.ActiveProjectId,
                            ChapterId: package?.ChapterId ?? result.Run?.TargetChapterId,
                            PackageId: packageId,
                            ToolName: "ProduceChapter",
                            Stage: stage,
                            Status: status,
                            ArtifactType: artifactType,
                            ArtifactId: artifactId,
                            OutputKind: MapProductionOutputKind(artifactType, eventType),
                            Summary: message,
                            UserVisibleWhere: MapProductionOutputVisibleWhere(artifactType, eventType),
                            VisibleInWorkflow: true,
                            VisibleInLibrary: IsLibraryOutputArtifact(artifactType, eventType),
                            SourceEventType: productionEvent.EventType,
                            SourceEventId: productionEvent.Id,
                            Data: data),
                        ct)
                    .ConfigureAwait(false);
            }
        }

        await AppendChapterRuntimeProgressEventAsync(
                session,
                runId,
                stage,
                status,
                message,
                artifactType,
                artifactId,
                data,
                ct)
            .ConfigureAwait(false);
    }

    private static string MapProductionOutputKind(string artifactType, string eventType)
    {
        if (IsLibraryOutputArtifact(artifactType, eventType))
            return AgentToolOutputKind.FinalArtifact;
        if (artifactType.Contains("knowledge", StringComparison.OrdinalIgnoreCase))
            return AgentToolOutputKind.KnowledgeEntry;
        return AgentToolOutputKind.ProcessArtifact;
    }

    private static IReadOnlyList<string> MapProductionOutputVisibleWhere(string artifactType, string eventType)
    {
        if (IsLibraryOutputArtifact(artifactType, eventType))
            return new[] { "小说书城", "创作工作流" };
        return new[] { "创作工作流" };
    }

    private static bool IsLibraryOutputArtifact(string artifactType, string eventType) =>
        string.Equals(artifactType, "chapter_version", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(eventType, "chapter_committed", StringComparison.OrdinalIgnoreCase);

    private async Task AppendChapterRuntimeProgressEventAsync(
        AgentSession session,
        string runId,
        string stage,
        string status,
        string message,
        string? artifactType,
        string? artifactId,
        object? data,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) || string.IsNullOrWhiteSpace(session.ActiveProjectId))
            throw new InvalidOperationException("Chapter runtime progress event requires userId and activeProjectId.");

        var canonicalStage = NovelAgentProductionStages.ToCanonicalStage(stage);
        using var scope = _serviceProvider.CreateScope();
        var runtimeEvents = scope.ServiceProvider.GetRequiredService<IAgentRuntimeEventService>();
        await runtimeEvents.AppendAsync(
                new CreateAgentRuntimeEventRequest(
                    RuntimeRunId: ResolveRuntimeProgressRunId(session, runId),
                    UserId: session.UserId,
                    SessionId: session.SessionId,
                    ProjectId: session.ActiveProjectId,
                    Type: "production_progress",
                    Message: message,
                    Data: MergeRuntimeProgressData(data, runId, canonicalStage),
                    Stage: canonicalStage,
                    Status: status,
                    ArtifactType: artifactType,
                    ArtifactId: artifactId,
                    DisplaySurface: AgentRuntimeEventSurface.Workflow,
                    DisplayPolicy: AgentRuntimeEventDisplayPolicy.Timeline,
                    PublishToSse: true),
                ct)
            .ConfigureAwait(false);
    }

    private static string ResolveRuntimeProgressRunId(AgentSession session, string productionRunId) =>
        string.IsNullOrWhiteSpace(session.RuntimeRunId) ? productionRunId : session.RuntimeRunId.Trim();

    private static object MergeRuntimeProgressData(object? data, string productionRunId, string stage)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["productionRunId"] = productionRunId,
            ["stage"] = stage
        };

        if (data == null)
            return result;

        try
        {
            var element = JsonSerializer.SerializeToElement(data);
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                    result[property.Name] = string.Equals(property.Name, "stage", StringComparison.OrdinalIgnoreCase)
                        ? stage
                        : property.Value.Clone();
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

        return result;
    }

    private AgentToolExecutionResult BuildStageTimeoutToolResult(
        AgentSession session,
        string runId,
        string stage,
        AgentProductionStageTimeoutException ex,
        IReadOnlyList<string> nextHints)
    {
        var message = $"{FormatProductionStageLabel(stage)}阶段超时：已等待 {(int)Math.Round(ex.Elapsed.TotalSeconds)} 秒，超过预算 {(int)Math.Round(ex.Timeout.TotalSeconds)} 秒。系统已停止等待本阶段，保留现有生产包与执行记录，可继续重试。";
        var artifact = BuildArtifact(
            "chapter_production_blocked",
            runId,
            session.ActiveProjectId,
            runId,
            message,
            nextHints);

        return new AgentToolExecutionResult
        {
            Success = false,
            RequiresConfirmation = false,
            Risk = "High",
            Message = message,
            RunId = runId,
            Phase = stage,
            IsRepairable = true,
            RecommendedToolName = "ProduceChapter",
            RecommendedArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["runId"] = runId,
                ["commitPolicy"] = "auto_commit"
            },
            Failure = new AgentToolFailure
            {
                Code = "STAGE_TIMEOUT",
                FailedStage = stage,
                Reason = message,
                Recoverable = true,
                RecommendedAction = "ProduceChapter",
                ArtifactIds = new[] { artifact.ArtifactId },
                ProducedArtifacts = new[] { ToProducedArtifact(artifact) },
                RecoverableActions = nextHints,
                RequiresUserDecision = false
            },
            Artifact = artifact,
            Suggestions = nextHints
        };
    }

    private async Task<T> RunWithProductionStageHeartbeatAsync<T>(
        AgentSession session,
        string runId,
        string stage,
        string? artifactType,
        string? artifactId,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct)
    {
        var interval = GetProductionStageHeartbeatInterval();
        var timeout = GetProductionStageTimeout(stage);
        using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout > TimeSpan.Zero)
            operationCts.CancelAfter(timeout);
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(operationCts.Token);
        var startedAt = DateTime.UtcNow;
        var heartbeatTask = EmitProductionStageHeartbeatAsync(
            session,
            runId,
            stage,
            artifactType,
            artifactId,
            startedAt,
            interval,
            heartbeatCts.Token);
        var operationTask = operation(operationCts.Token);
        var cancellationMonitorTask = MonitorRuntimeRunCancellationAsync(
            session,
            stage,
            interval <= TimeSpan.FromSeconds(1) ? interval : TimeSpan.FromSeconds(1),
            heartbeatCts.Token);

        try
        {
            var completed = await Task.WhenAny(operationTask, heartbeatTask, cancellationMonitorTask).ConfigureAwait(false);
            if (completed == heartbeatTask && heartbeatTask.IsFaulted)
            {
                await operationCts.CancelAsync().ConfigureAwait(false);
                await heartbeatTask.ConfigureAwait(false);
            }
            if (completed == cancellationMonitorTask && cancellationMonitorTask.IsFaulted)
            {
                await operationCts.CancelAsync().ConfigureAwait(false);
                await cancellationMonitorTask.ConfigureAwait(false);
            }

            var result = await operationTask.ConfigureAwait(false);
            await ThrowIfRuntimeRunCancelledAsync(session, stage, ct).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested &&
                                                timeout > TimeSpan.Zero &&
                                                operationCts.IsCancellationRequested)
        {
            throw new AgentProductionStageTimeoutException(stage, DateTime.UtcNow - startedAt, timeout);
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
                // Expected when the production stage finishes before the next heartbeat tick.
            }
            try
            {
                await cancellationMonitorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when the production stage finishes before the next runtime cancellation poll.
            }
        }
    }

    private async Task MonitorRuntimeRunCancellationAsync(
        AgentSession session,
        string stage,
        TimeSpan interval,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.RuntimeRunId))
            return;

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            await ThrowIfRuntimeRunCancelledAsync(session, stage, ct).ConfigureAwait(false);
    }

    private async Task EmitProductionStageHeartbeatAsync(
        AgentSession session,
        string runId,
        string stage,
        string? artifactType,
        string? artifactId,
        DateTime startedAt,
        TimeSpan interval,
        CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            var elapsed = DateTime.UtcNow - startedAt;
            var progress = AgentToolProgressPresenter.DescribeHeartbeat(stage, elapsed, session.Phase, runId);
            await AppendChapterRuntimeProgressEventAsync(
                    session,
                    runId,
                    stage,
                    "running",
                    progress.Detail,
                    artifactType,
                    artifactId,
                    new
                    {
                        runId,
                        stage,
                        status = "running",
                        heartbeat = true,
                        elapsedSeconds = (int)Math.Round(elapsed.TotalSeconds),
                        progress.Title,
                        progress.Detail
                    },
                    ct)
                .ConfigureAwait(false);
        }
    }

    private TimeSpan GetProductionStageHeartbeatInterval()
    {
        using var scope = _serviceProvider.CreateScope();
        var options = scope.ServiceProvider.GetService<IOptions<AgentProductionStageProgressOptions>>()?.Value;
        var seconds = Math.Clamp(options?.HeartbeatSeconds ?? 15, 1, 300);
        return TimeSpan.FromSeconds(seconds);
    }

    private TimeSpan GetProductionStageTimeout(string stage)
    {
        using var scope = _serviceProvider.CreateScope();
        var options = scope.ServiceProvider.GetService<IOptions<AgentProductionStageProgressOptions>>()?.Value;
        return AgentProductionStageTimeoutPolicy.ResolveTimeout(stage, options);
    }

    private async Task<AgentToolExecutionResult> RunDraftRepairStageAsync(string runId, AgentSession session, bool confirmed, CancellationToken ct)
    {
        await AppendChapterRuntimeProgressEventAsync(
                session,
                runId,
                NovelAgentProductionStages.DraftRepair,
                "running",
                "正在修复章节草稿。",
                "chapter_repair_report",
                null,
                new { runId, stage = NovelAgentProductionStages.DraftRepair, status = "running" },
                ct)
            .ConfigureAwait(false);

        var result = await RunWithProductionStageHeartbeatAsync(
                session,
                runId,
                NovelAgentProductionStages.DraftRepair,
                "chapter_repair_report",
                null,
                token => _workspace.Orchestrator.RepairDraftStageAsync(runId, confirmed, token),
                ct)
            .ConfigureAwait(false);
        session.Phase = result.Success ? "validated" : result.GateReport?.Status ?? session.Phase;
        var repairAttempt = result.DraftArtifact?.RepairAttemptCount ?? 0;
        var canAutoRepair = !result.Success && result.GateReport != null && repairAttempt < 3;
        var repairMessage = AppendStatusToMessage(result.Message, result.DraftArtifact?.Status, result.GateReport?.Status);
        await AppendChapterProductionEventAsync(
                session,
                runId,
                result,
                eventType: "chapter_draft_repaired",
                stage: NovelAgentProductionStages.DraftRepair,
                status: result.Success ? "completed" : "failed",
                message: repairMessage,
                artifactType: "chapter_repair_report",
                artifactId: result.DraftArtifact?.ArtifactId ?? "chapter_repair",
                data: new
                {
                    repairAttempt,
                    draftStatus = result.DraftArtifact?.Status ?? string.Empty,
                    gateStatus = result.GateReport?.Status ?? string.Empty,
                    issues = result.GateReport?.Issues ?? new List<string>(),
                    repairHints = result.GateReport?.RepairHints ?? new List<string>(),
                    canAutoRepair
                },
                ct)
            .ConfigureAwait(false);
        if (result.Success)
            await RecordChapterDraftAsync(session, runId, result, ct).ConfigureAwait(false);
        return new AgentToolExecutionResult
        {
            Success = result.Success,
            RequiresConfirmation = false,
            Risk = result.RiskLevel.ToString(),
            Message = result.Success
                ? result.Message
                : BuildWritingFailureMessage("修复章节草稿", session.Phase, result.Message, hasDraft: result.DraftArtifact != null, hasGateReport: result.GateReport != null),
            RunId = runId,
            Phase = session.Phase,
            IsRepairable = canAutoRepair,
            RecommendedToolName = canAutoRepair ? "ProduceChapter" : string.Empty,
            RecommendedArguments = canAutoRepair
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["runId"] = runId,
                    ["commitPolicy"] = "auto_commit",
                    ["maxRepairAttempts"] = "2"
                }
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            MissingPrerequisite = canAutoRepair ? "chapter_draft_repair" : string.Empty,
            Data = result,
            Artifact = BuildArtifact(result.Success ? "chapter_draft_repaired" : "chapter_repair_report", result.Run?.TargetChapterId ?? runId, session.ActiveProjectId, runId, result.Message, result.Success ? new[] { "提交成稿", "查看门禁报告" } : new[] { "继续修复", "请用户补设定" }),
            Suggestions = result.Success ? new[] { "提交成稿", "查看门禁报告" } : new[] { "继续修复", "请用户补设定" },
        };
    }

    private async Task<AgentToolExecutionResult> RunChapterCommitStageAsync(string runId, AgentSession session, bool confirmed, CancellationToken ct)
    {
        await AppendChapterRuntimeProgressEventAsync(
                session,
                runId,
                NovelAgentProductionStages.ChapterCommit,
                "running",
                "正在提交章节到书城。",
                "chapter_committed",
                null,
                new { runId, stage = NovelAgentProductionStages.ChapterCommit, status = "running" },
                ct)
            .ConfigureAwait(false);

        var result = await RunWithProductionStageHeartbeatAsync(
                session,
                runId,
                NovelAgentProductionStages.ChapterCommit,
                "chapter_committed",
                null,
                token => _workspace.Orchestrator.CommitChapterStageAsync(runId, confirmed, token),
                ct)
            .ConfigureAwait(false);
        session.Phase = result.Success ? "committed" : session.Phase;
        var commitMessage = AppendStatusToMessage(result.Message, result.DraftArtifact?.Status, result.GateReport?.Status);
        var commitProgressData = new
        {
            draftStatus = result.DraftArtifact?.Status ?? string.Empty,
            gateStatus = result.GateReport?.Status ?? string.Empty,
            result.Run?.TargetChapterId
        };
        if (result.Success)
        {
            await AppendChapterRuntimeProgressEventAsync(
                    session,
                    runId,
                    NovelAgentProductionStages.ChapterCommit,
                    "completed",
                    commitMessage,
                    "chapter_committed",
                    result.Run?.TargetChapterId ?? runId,
                    commitProgressData,
                    ct)
                .ConfigureAwait(false);

            await RunChapterCommitPostCommitTasksAsync(session, runId, result, ct).ConfigureAwait(false);
        }
        else
        {
            await AppendChapterProductionEventAsync(
                    session,
                    runId,
                    result,
                    eventType: "chapter_commit_blocked",
                    stage: NovelAgentProductionStages.ChapterCommit,
                    status: "failed",
                    message: commitMessage,
                    artifactType: "chapter_commit_blocked",
                    artifactId: result.Run?.TargetChapterId ?? runId,
                    data: commitProgressData,
                    ct)
                .ConfigureAwait(false);
        }
        return new AgentToolExecutionResult
        {
            Success = result.Success,
            RequiresConfirmation = false,
            Risk = result.RiskLevel.ToString(),
            Message = result.Success
                ? result.Message
                : BuildWritingFailureMessage("提交章节到书城", session.Phase, result.Message, hasDraft: result.DraftArtifact != null, hasGateReport: result.GateReport != null),
            RunId = runId,
            Phase = session.Phase,
            Data = result,
            Artifact = BuildArtifact(result.Success ? "chapter_committed" : "chapter_commit_blocked", result.Run?.TargetChapterId ?? runId, session.ActiveProjectId, runId, result.Message, result.Success ? new[] { "继续下一章", "查看书城" } : new[] { "校验草稿", "查看失败项" }),
            Suggestions = result.Success ? new[] { "继续下一章", "查看书城" } : new[] { "校验草稿", "查看失败项" },
        };
    }

    private async Task RunChapterCommitPostCommitTasksAsync(
        AgentSession session,
        string runId,
        NovelAgentExecutionResult result,
        CancellationToken ct)
    {
        try
        {
            await _catalog.UpdateFromCurrentStoryBibleAsync(session.ActiveProjectId, ct).ConfigureAwait(false);
            await EnqueueChapterCommitPostCommitOutboxAsync(session, runId, result, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await AppendChapterPostCommitFailureEventAsync(session, runId, result, ex).ConfigureAwait(false);
        }
    }

    private async Task EnqueueChapterCommitPostCommitOutboxAsync(
        AgentSession session,
        string runId,
        NovelAgentExecutionResult result,
        CancellationToken ct)
    {
        if (result.Run == null || string.IsNullOrWhiteSpace(session.ActiveProjectId))
            throw new InvalidOperationException("Chapter commit post-commit outbox requires a run and activeProjectId.");

        using var scope = _serviceProvider.CreateScope();
        var truthStore = scope.ServiceProvider.GetRequiredService<IProductionTruthStore>();
        var payload = new ChapterCommitPostCommitPayload
        {
            RuntimeRunId = runId,
            UserId = session.UserId,
            ProjectId = session.ActiveProjectId,
            TargetChapterId = result.Run.TargetChapterId,
            Message = result.Message,
            ContextPackage = result.ContextPackage ?? result.Run.ContextPackage,
            DraftArtifact = result.DraftArtifact ?? result.Run.DraftArtifact,
            GateReport = result.GateReport ?? result.Run.GateReport,
            PostGenerationReview = result.Run.PostGenerationReview,
            ContinuityFacts = result.Run.ContinuityFacts
        };

        await truthStore.EnqueueOutboxAsync(
                new EnqueueOutboxEventRequest(
                    UserId: session.UserId,
                    ProjectId: session.ActiveProjectId,
                    RuntimeRunId: runId,
                    EventType: "finalize_chapter_commit_metadata",
                    AggregateType: "chapter",
                    AggregateId: result.Run.TargetChapterId,
                    PayloadJson: JsonSerializer.Serialize(
                        payload,
                        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })),
                ct)
            .ConfigureAwait(false);
    }

    private async Task AppendChapterPostCommitFailureEventAsync(
        AgentSession session,
        string runId,
        NovelAgentExecutionResult result,
        Exception ex)
    {
        try
        {
            await AppendChapterRuntimeProgressEventAsync(
                    session,
                    runId,
                    NovelAgentProductionStages.ChapterCommit,
                    "post_commit_failed",
                    $"章节已入书城，但提交后元数据沉淀失败，后台可重试：{ex.Message}",
                    "chapter_post_commit",
                    result.Run?.TargetChapterId ?? runId,
                    new
                    {
                        runId,
                        stage = NovelAgentProductionStages.ChapterCommit,
                        status = "post_commit_failed",
                        error = ex.Message,
                        result.Run?.TargetChapterId
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            // The chapter is already committed. A secondary failure event must not overturn the commit result.
        }
    }

    private async Task PersistChapterCommitProductionTruthAsync(
        AgentSession session,
        string runId,
        NovelAgentExecutionResult result,
        CancellationToken ct)
    {
        if (result.Run == null || string.IsNullOrWhiteSpace(session.ActiveProjectId))
            throw new InvalidOperationException("Chapter commit truth persistence requires a run and activeProjectId.");

        using var scope = _serviceProvider.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<IChapterCommitTruthRecorder>();

        await recorder.RecordAsync(
                new RecordChapterCommitTruthRequest(
                    RuntimeRunId: runId,
                    UserId: session.UserId,
                    ProjectId: session.ActiveProjectId,
                    Message: result.Message,
                    TargetChapterId: result.Run.TargetChapterId,
                    ContextPackage: result.ContextPackage ?? result.Run.ContextPackage,
                    DraftArtifact: result.DraftArtifact ?? result.Run.DraftArtifact,
                    GateReport: result.GateReport ?? result.Run.GateReport,
                    PostGenerationReview: result.Run.PostGenerationReview,
                    ContinuityFacts: result.Run.ContinuityFacts),
                ct)
            .ConfigureAwait(false);
    }

    private async Task MarkCommittedPackageCreativeIntentsExecutedAsync(
        AgentSession session,
        string runId,
        NovelAgentExecutionResult result,
        CancellationToken ct)
    {
        var package = result.ContextPackage ?? result.Run?.ContextPackage;
        if (package == null ||
            package.AcceptedCreativeIntents.Count == 0 ||
            string.IsNullOrWhiteSpace(session.UserId) ||
            string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var creativeService = scope.ServiceProvider.GetRequiredService<ICreativeIntentService>();
        var execution = await creativeService.MarkPackageIntentsExecutedAsync(
                session.UserId,
                session.ActiveProjectId,
                package,
                $"章节 {package.ChapterId} 已提交书城，生产包内创意已执行。",
                ct)
            .ConfigureAwait(false);
        if (execution.ExecutedCount <= 0)
            return;

        var message = $"已将 {execution.ExecutedCount} 条生产包创意标记为已执行。";
        await AppendChapterProductionEventAsync(
                session,
                runId,
                result,
                eventType: "creative_intents_executed",
                stage: NovelAgentProductionStages.ChapterCommit,
                status: "completed",
                message,
                artifactType: "creative_intents",
                artifactId: package.ChapterId,
                data: new
                {
                    packageId = package.PackageId,
                    package.ChapterId,
                    execution.ExecutedCount,
                    intentIds = execution.IntentIds
                },
                ct)
            .ConfigureAwait(false);
    }

    private async Task MarkCommittedPackageKnowledgeBindingsUsedAsync(
        AgentSession session,
        string runId,
        NovelAgentExecutionResult result,
        CancellationToken ct)
    {
        var package = result.ContextPackage ?? result.Run?.ContextPackage;
        if (package == null ||
            package.KnowledgeBindings.Count == 0 ||
            string.IsNullOrWhiteSpace(session.UserId) ||
            string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            return;
        }

        var bindings = package.KnowledgeBindings
            .Where(binding => !string.IsNullOrWhiteSpace(binding.KnowledgeId) ||
                              !string.IsNullOrWhiteSpace(binding.Title))
            .Select(binding => new
            {
                knowledgeId = binding.KnowledgeId,
                title = binding.Title,
                entryType = binding.EntryType,
                projectUsageStatus = "used",
                weight = binding.Weight
            })
            .Take(24)
            .ToList();
        if (bindings.Count == 0)
            return;

        var message = $"本章生产包实际使用 {bindings.Count} 条项目知识绑定。";
        await AppendChapterProductionEventAsync(
                session,
                runId,
                result,
                eventType: "knowledge_bindings_used",
                stage: NovelAgentProductionStages.ChapterCommit,
                status: "completed",
                message,
                artifactType: "knowledge_bindings",
                artifactId: package.ChapterId,
                data: new
                {
                    packageId = package.PackageId,
                    package.ChapterId,
                    bindingCount = bindings.Count,
                    knowledgeIds = bindings.Select(binding => binding.knowledgeId).Where(id => !string.IsNullOrWhiteSpace(id)).ToList(),
                    bindings
                },
                ct)
            .ConfigureAwait(false);
    }

    private async Task<AgentToolExecutionResult> RefreshProjectIndexesAsync(AgentToolCall call, AgentSession session, CancellationToken ct)
    {
        var runId = Arg(call, "runId", session.ActiveRunId ?? string.Empty);
        var result = await _workspace.Orchestrator.RefreshProjectIndexesAsync(runId, ct).ConfigureAwait(false);
        var impact = result.DependencyImpact;
        var message = result.Message;
        return new AgentToolExecutionResult
        {
            Success = result.Success,
            Message = message,
            RunId = runId,
            Phase = impact?.Status ?? session.Phase,
            Data = result,
            Artifact = BuildArtifact("dependency_impact", result.Run?.TargetChapterId ?? runId, session.ActiveProjectId, runId, message, new[] { "查看工作流", "继续下一章" }),
            Suggestions = new[] { "查看工作流", "继续下一章" },
        };
    }

    private Task<AgentToolExecutionResult> AnalyzeDependencyImpactAsync(AgentToolCall call, AgentSession session, CancellationToken ct)
    {
        return RefreshProjectIndexesAsync(call, session, ct);
    }

    private async Task<AgentToolExecutionResult> ReviewChapterAsync(AgentToolCall call, AgentSession session, CancellationToken ct)
    {
        var runId = Arg(call, "runId", session.ActiveRunId ?? string.Empty);
        await AppendChapterRuntimeProgressEventAsync(
                session,
                runId,
                NovelAgentProductionStages.QualityReview,
                "running",
                "正在执行 Agent 质量评审。",
                "chapter_review",
                null,
                new { runId, stage = NovelAgentProductionStages.QualityReview, status = "running" },
                ct)
            .ConfigureAwait(false);

        var result = await RunWithProductionStageHeartbeatAsync(
                session,
                runId,
                NovelAgentProductionStages.QualityReview,
                "chapter_review",
                null,
                token => _workspace.Orchestrator.ReviewGeneratedChapterAsync(runId, token),
                ct)
            .ConfigureAwait(false);
        var review = result.Run?.PostGenerationReview;
        var reviewAllowsCommit = result.Success && AgentReviewAllowsCommit(review);
        var eventStatus = result.Success
            ? reviewAllowsCommit ? "completed" : "blocked"
            : "failed";
        session.Phase = reviewAllowsCommit ? "chapter_reviewed" : session.Phase;
        var message = reviewAllowsCommit
            ? result.Message
            : result.Success
                ? $"章节质量评审未通过，生产闭环需要停在 AgentReview 阶段：{review?.Summary ?? result.Message}{FormatReviewBlockers(review)}"
                : result.Message;
        await AppendChapterProductionEventAsync(
                session,
                runId,
                result,
                eventType: "chapter_quality_reviewed",
                stage: NovelAgentProductionStages.QualityReview,
                status: eventStatus,
                message: message,
                artifactType: "chapter_review",
                artifactId: review?.ReviewId ?? "chapter_review",
                data: new
                {
                    reviewId = review?.ReviewId ?? string.Empty,
                    overallResult = review?.OverallResult ?? string.Empty,
                    validationOverallResult = review?.ValidationOverallResult ?? string.Empty,
                    validationIssueCount = review?.ValidationIssueCount ?? 0,
                    requiresRewrite = review?.RequiresRewrite ?? false,
                    qualityScore = review?.QualityScore ?? 0,
                    contentLength = review?.ContentLength ?? 0,
                    checkCount = review?.Checks.Count ?? 0,
                    summary = review?.Summary ?? string.Empty
                },
                ct)
            .ConfigureAwait(false);
        if (review != null)
            await RecordAgentReviewAsync(session, runId, result, review, ct).ConfigureAwait(false);
        return new AgentToolExecutionResult
        {
            Success = reviewAllowsCommit,
            Message = message,
            RunId = runId,
            Phase = session.Phase,
            Risk = reviewAllowsCommit ? "Medium" : "High",
            IsRepairable = result.Success,
            RecommendedToolName = result.Success && !reviewAllowsCommit ? "ProduceChapter" : string.Empty,
            RecommendedArguments = result.Success && !reviewAllowsCommit
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["runId"] = runId,
                    ["commitPolicy"] = "auto_commit",
                    ["maxRepairAttempts"] = "2"
                }
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Data = result,
            Artifact = BuildArtifact(
                reviewAllowsCommit ? "chapter_review" : "chapter_review_blocked",
                runId,
                session.ActiveProjectId,
                runId,
                message,
                reviewAllowsCommit ? new[] { "提交成稿", "查看评审报告" } : new[] { "按评审修订", "重新生成章节", "查看评审报告" }),
            Suggestions = reviewAllowsCommit ? new[] { "提交成稿", "查看评审报告" } : new[] { "按评审修订", "重新生成章节", "查看评审报告" },
        };
    }

    private static string FormatReviewBlockers(NovelAgentPostGenerationReview? review)
    {
        if (review == null)
            return string.Empty;

        var blockers = review.Checks
            .Where(check => check.Status is NovelAgentReviewCheckStatus.Fail or NovelAgentReviewCheckStatus.Warning)
            .Take(6)
            .Select(check => $"{check.Name}={check.Status}: {check.Message}")
            .ToList();
        return blockers.Count == 0 ? string.Empty : "\n评审阻塞项：" + string.Join("；", blockers);
    }

    private static string BuildWritingFailureMessage(
        string stage,
        string phase,
        string originalMessage,
        bool hasDraft,
        bool hasGateReport)
    {
        var artifacts = new List<string>();
        if (hasDraft) artifacts.Add("章节草稿");
        if (hasGateReport) artifacts.Add("门禁报告");
        if (artifacts.Count == 0) artifacts.Add("暂无可用产物");

        return string.Join("\n", new[]
        {
            $"失败阶段：{stage}（当前状态：{(string.IsNullOrWhiteSpace(phase) ? "未知" : phase)}）",
            $"已有产物：{string.Join("、", artifacts)}",
            $"失败原因：{originalMessage}",
            hasGateReport ? "可继续动作：查看失败项后修复章节草稿，或补充设定后重试。" : "可继续动作：补齐上下文或重新生成章节草稿。",
            hasGateReport ? "是否需要用户决定：若失败项涉及设定取舍，需要用户确认；格式/连续性问题可继续自动修复。" : "是否需要用户决定：通常不需要，除非缺少关键创作设定。"
        });
    }

    private async Task<NovelAgentRun?> FindRunAsync(string runId, CancellationToken ct)
    {
        var bible = await _workspace.Orchestrator.GetStoryBibleAsync(ct).ConfigureAwait(false);
        return bible.AgentRuns.FirstOrDefault(r => string.Equals(r.RunId, runId, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatProjectContentQuery(
        string projectTitle,
        IReadOnlyList<ProjectContentQueryItem> items,
        bool includeBody)
    {
        var lines = new List<string> { $"项目「{projectTitle}」内容查询结果：" };
        foreach (var item in items)
        {
            lines.Add(
                $"第 {item.ChapterNumber} 章：{item.ChapterTitle}\n" +
                $"所属卷：{(item.VolumeNumber > 0 ? $"第 {item.VolumeNumber} 卷" : "未绑定卷")} {item.VolumeTitle}\n" +
                (string.IsNullOrWhiteSpace(item.SourceRunId) ? string.Empty : $"工作流 Run：{item.SourceRunId}\n") +
                (item.CurrentVersionNumber <= 0 ? string.Empty : $"当前版本：v{item.CurrentVersionNumber}\n") +
                (string.IsNullOrWhiteSpace(item.PackageId) ? string.Empty : $"生产包：{item.PackageId}" + (string.IsNullOrWhiteSpace(item.KernelVersion) ? "\n" : $"；内核：{item.KernelVersion}\n")) +
                $"状态：{item.Status}；字数：{item.WordCount}\n" +
                $"正文{(includeBody ? "" : "开头")}：{item.BodyPreview}");
            if (!string.IsNullOrWhiteSpace(item.FactSnapshotJson))
                lines.Add($"事实快照 v{item.FactSnapshotVersion}：{TrimBody(item.FactSnapshotJson, 360)}");
            if (item.KnowledgeBindings.Count > 0)
            {
                var knowledgeLines = item.KnowledgeBindings
                    .Take(6)
                    .Select(FormatProjectContentKnowledgeBinding);
                lines.Add("本章使用知识：" + string.Join("；", knowledgeLines));
            }
            if (item.ChapterChanges.Count > 0)
            {
                var changeLines = item.ChapterChanges
                    .TakeLast(4)
                    .Select(FormatProjectContentChapterChange);
                lines.Add("CHANGES记录：" + string.Join("；", changeLines));
            }
            if (item.GenerationGateReports.Count > 0)
            {
                var gateLines = item.GenerationGateReports
                    .TakeLast(3)
                    .Select(FormatProjectContentGenerationGateReport);
                lines.Add("门禁报告：" + string.Join("；", gateLines));
            }
            if (item.AgentReviews.Count > 0)
            {
                var reviewLines = item.AgentReviews
                    .TakeLast(3)
                    .Select(FormatProjectContentAgentReview);
                lines.Add("总编验收：" + string.Join("；", reviewLines));
            }
            if (item.SourceRevisionPlans.Count > 0)
            {
                var revisionPlanLines = item.SourceRevisionPlans
                    .Take(6)
                    .Select(FormatProjectContentRevisionPlan);
                lines.Add("来源修订计划：" + string.Join("；", revisionPlanLines));
            }
            if (item.RebuiltFromPackageIds.Count > 0)
            {
                lines.Add("替代旧包：" + string.Join("；", item.RebuiltFromPackageIds.Take(6)));
            }
            if (item.ProductionEvents.Count > 0)
            {
                var eventLines = item.ProductionEvents
                    .TakeLast(4)
                    .Select(evt => $"{evt.Stage}/{evt.Status}：{evt.Message}");
                lines.Add("最近生产事件：" + string.Join("；", eventLines));
            }
            foreach (var facts in item.ContinuityFacts)
            {
                var factLines = new[]
                {
                    string.IsNullOrWhiteSpace(facts.ProtagonistName) ? string.Empty : $"主角：{facts.ProtagonistName}",
                    string.IsNullOrWhiteSpace(facts.ProtagonistIdentity) ? string.Empty : $"身份：{facts.ProtagonistIdentity}",
                    string.IsNullOrWhiteSpace(facts.ProtagonistStatus) ? string.Empty : $"状态：{facts.ProtagonistStatus}",
                    string.IsNullOrWhiteSpace(facts.EndingState) ? string.Empty : $"结尾：{facts.EndingState}",
                    facts.NextChapterMustCarry.Count == 0 ? string.Empty : $"下一章必须承接：{string.Join("；", facts.NextChapterMustCarry)}"
                }.Where(s => !string.IsNullOrWhiteSpace(s));
                lines.Add("关键事实：" + string.Join("；", factLines));
            }
        }

        return string.Join("\n\n", lines);
    }

    private static string FormatProjectContentKnowledgeBinding(BoundKnowledgeSnapshot binding)
    {
        var title = FirstNonEmpty(binding.Title, binding.KnowledgeId, "未命名知识");
        var constraint = FirstNonEmpty(binding.ConstraintLevel, binding.PackagePolicy, binding.EntryType, "Reference");
        var routes = new List<string>();
        if (binding.ShouldEnterGate)
            routes.Add("Gate");
        if (binding.ShouldEnterBlueprint)
            routes.Add("蓝图");
        if (binding.ShouldEnterFactSnapshot)
            routes.Add("FactSnapshot");
        var routeText = routes.Count == 0
            ? string.Empty
            : $"，进入 {string.Join("/", routes)}";
        var classification = string.IsNullOrWhiteSpace(binding.ClassificationId)
            ? "，待分类"
            : $"，分类 {binding.ClassificationId}";
        var chapters = binding.UsedByChapters.Count == 0
            ? string.Empty
            : $"，已用于 {binding.UsedByChapters.Count} 章";
        return $"{title}（{binding.EntryType}/{constraint}{routeText}{classification}{chapters}）";
    }

    private static string FormatProjectContentChapterChange(ProjectContentChapterChange change)
    {
        var applied = change.AppliedToFactSnapshot ? "已进入事实快照" : "未进入事实快照";
        var error = string.IsNullOrWhiteSpace(change.ParseError) ? string.Empty : $"，错误：{TrimBody(change.ParseError, 80)}";
        var package = string.IsNullOrWhiteSpace(change.PackageId) ? string.Empty : $"，生产包 {change.PackageId}";
        return $"{change.Id}/{change.ParseStatus}/{applied}{package}{error}";
    }

    private static string FormatProjectContentGenerationGateReport(ProjectContentGenerationGateReport report)
    {
        var checks = new List<string>();
        if (report.ProtocolPassed) checks.Add("协议");
        if (report.ChangesDetected) checks.Add("CHANGES");
        if (report.FactSnapshotPassed) checks.Add("事实");
        if (report.BlueprintPassed) checks.Add("蓝图");
        if (report.RagPassed) checks.Add("知识");
        var checkText = checks.Count == 0 ? "无通过项" : string.Join("/", checks);
        return $"{report.Id}/{report.Status}（通过={checkText}，问题={report.IssueCount}，修复提示={report.RepairHintCount}）";
    }

    private static string FormatProjectContentAgentReview(ProjectContentAgentReview review)
    {
        var rewrite = review.RequiresRewrite ? "需重写" : "可继续";
        var editorial = FormatAgentReviewEditorialFields(
            review.MeetsAcceptedCreativeIntents,
            review.ContinuityRisk,
            review.ChapterPacing,
            review.RecommendedAction);
        var summary = string.IsNullOrWhiteSpace(review.Summary) ? string.Empty : $"：{TrimBody(review.Summary, 100)}";
        return $"{review.Id}/{review.OverallResult}/{rewrite}/score={review.QualityScore}{editorial}{summary}";
    }

    private static string FormatAgentReviewEditorialFields(
        bool meetsAcceptedCreativeIntents,
        string continuityRisk,
        string chapterPacing,
        string recommendedAction)
    {
        var parts = new List<string>();
        parts.Add(meetsAcceptedCreativeIntents ? "创意已落实" : "创意未完全落实");
        if (!string.IsNullOrWhiteSpace(continuityRisk))
            parts.Add($"连续性风险={continuityRisk}");
        if (!string.IsNullOrWhiteSpace(chapterPacing))
            parts.Add($"节奏={chapterPacing}");
        if (!string.IsNullOrWhiteSpace(recommendedAction))
            parts.Add($"建议={recommendedAction}");
        return parts.Count == 0 ? string.Empty : $"，{string.Join("，", parts)}";
    }

    private static string FormatPackageKnowledgeSummary(NovelProductionKnowledgeBindingSummaryState summary)
    {
        if (summary.BindingCount <= 0)
            return string.Empty;

        return $"，知识入口：绑定 {summary.BindingCount}，Gate {summary.ShouldEnterGateCount}，蓝图 {summary.ShouldEnterBlueprintCount}，FactSnapshot {summary.ShouldEnterFactSnapshotCount}，待分类 {summary.PendingClassificationCount}";
    }

    private static string FormatProjectContentRevisionPlan(RevisionPlanSnapshot plan)
    {
        var id = FirstNonEmpty(plan.RevisionPlanId, "未命名修订计划");
        var target = FirstNonEmpty(plan.TargetChapterDisplayName, plan.TargetChapterLogicalId, plan.TargetChapterId);
        var status = FirstNonEmpty(plan.Status, "unknown");
        var type = FirstNonEmpty(plan.PlanType, plan.TargetScope, "revision");
        var recommendation = TrimBody(plan.Recommendation, 80);
        var header = string.IsNullOrWhiteSpace(target)
            ? id
            : $"{target} / {id}";
        return string.IsNullOrWhiteSpace(recommendation)
            ? $"{header}（{status}/{type}）"
            : $"{header}（{status}/{type}）：{recommendation}";
    }

    private static string FormatChapterVersions(
        string chapterId,
        IReadOnlyList<ChapterVersionResponse> versions)
    {
        var lines = new List<string>
        {
            $"章节版本查询结果：{chapterId}",
            "这是只读版本历史，不会切换当前版本，也不会执行回滚。"
        };

        foreach (var version in versions.OrderByDescending(version => version.VersionNumber))
        {
            var markers = new List<string>();
            if (version.IsCurrent)
                markers.Add("当前");
            if (!string.IsNullOrWhiteSpace(version.Status))
                markers.Add(version.Status);

            var header = $"v{version.VersionNumber}" +
                         (markers.Count == 0 ? string.Empty : $"（{string.Join(" / ", markers)}）") +
                         $"：{version.Title}，字数 {version.WordCount}";
            var metadata = new List<string>();
            if (!string.IsNullOrWhiteSpace(version.RuntimeRunId))
                metadata.Add($"Run：{version.RuntimeRunId}");
            if (!string.IsNullOrWhiteSpace(version.PackageId))
                metadata.Add($"生产包：{version.PackageId}");
            if (!string.IsNullOrWhiteSpace(version.KernelVersion))
                metadata.Add($"内核：{version.KernelVersion}");
            if (!string.IsNullOrWhiteSpace(version.PromptVersion))
                metadata.Add($"Prompt：{version.PromptVersion}");
            if (version.RebuiltFromPackageIds.Count > 0)
                metadata.Add($"替代旧包：{string.Join("；", version.RebuiltFromPackageIds.Take(6))}");
            if (!string.IsNullOrWhiteSpace(version.GateReportJson))
                metadata.Add($"门禁：{TrimBody(version.GateReportJson, 120)}");
            if (!string.IsNullOrWhiteSpace(version.AgentReviewJson))
                metadata.Add($"AgentReview：{TrimBody(version.AgentReviewJson, 120)}");

            lines.Add(
                header + "\n" +
                (metadata.Count == 0 ? string.Empty : string.Join("\n", metadata) + "\n") +
                $"正文预览：{TrimBody(version.ContentPreview, 240)}");
        }

        return string.Join("\n\n", lines);
    }

    private static string FormatChapterVersionComparison(ChapterVersionCompareResponse comparison)
    {
        var lines = new List<string>
        {
            $"章节版本差异：{comparison.ChapterId}",
            comparison.Summary,
            $"左侧：v{comparison.Left.VersionNumber}（{comparison.Left.Title}，字数 {comparison.Left.WordCount}）",
            $"右侧：v{comparison.Right.VersionNumber}（{comparison.Right.Title}，字数 {comparison.Right.WordCount}）"
        };
        if (!string.IsNullOrWhiteSpace(comparison.Right.PackageId))
            lines.Add($"右侧生产包：{comparison.Right.PackageId}");
        if (comparison.Right.RebuiltFromPackageIds.Count > 0)
            lines.Add($"右侧替代旧包：{string.Join("；", comparison.Right.RebuiltFromPackageIds.Take(6))}");
        var alignment = comparison.ProductionAlignment;
        if (alignment.AcceptedCreativeIntents.Count > 0)
        {
            lines.Add("采用创意：");
            foreach (var intent in alignment.AcceptedCreativeIntents.Take(4))
            {
                lines.Add($"- {FirstNonEmpty(intent.NormalizedIntent, intent.IntentId)}（{FirstNonEmpty(intent.ImpactLevel, intent.TargetScope, intent.Status)}）");
            }
        }

        if (alignment.SourceRevisionPlans.Count > 0)
        {
            lines.Add("来源修订计划：");
            foreach (var plan in alignment.SourceRevisionPlans.Take(4))
            {
                lines.Add($"- {FirstNonEmpty(plan.Recommendation, plan.RevisionPlanId)}（{FirstNonEmpty(plan.Status, plan.PlanType)}）");
            }
        }

        if (!string.IsNullOrWhiteSpace(alignment.AgentReviewDecision) || alignment.AgentReviewChecks.Count > 0)
        {
            lines.Add($"Agent 审稿：{FirstNonEmpty(alignment.AgentReviewDecision, $"{alignment.AgentReviewChecks.Count} 项检查")}");
            foreach (var check in alignment.AgentReviewChecks.Take(4))
            {
                lines.Add($"- {FirstNonEmpty(check.Name, check.Key)}：{FirstNonEmpty(check.Status, check.Message)}");
            }
        }

        var changedBlocks = comparison.DiffBlocks
            .Where(block => !string.Equals(block.Kind, "unchanged", StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .ToList();
        if (changedBlocks.Count == 0)
        {
            lines.Add("正文段落没有可见差异。");
        }
        else
        {
            lines.Add("主要差异：");
            foreach (var block in changedBlocks)
            {
                var label = block.Kind switch
                {
                    "added" => "新增",
                    "removed" => "删除",
                    "changed" => "修改",
                    _ => block.Kind
                };
                lines.Add(
                    $"- {label}：" +
                    (string.IsNullOrWhiteSpace(block.LeftText) ? string.Empty : $"旧：{TrimBody(block.LeftText, 120)}；") +
                    (string.IsNullOrWhiteSpace(block.RightText) ? string.Empty : $"新：{TrimBody(block.RightText, 120)}"));
            }
        }

        return string.Join("\n", lines);
    }

    private static string FormatNovelProductionState(NovelProductionStateQueryResult state)
    {
        var lines = new List<string>();
        if (state.RuntimeRun != null)
        {
            var run = state.RuntimeRun;
            lines.Add(
                $"生产 Run：{run.Id}\n" +
                $"当前状态：{RuntimeRunStatusLabel(run.Status)}；模式：{run.Mode}；阶段：{run.CurrentPhase}；步骤：{run.CurrentStep}\n" +
                (string.IsNullOrWhiteSpace(run.ActiveTool) ? string.Empty : $"当前工具：{run.ActiveTool}\n") +
                (string.IsNullOrWhiteSpace(run.LastMessage) ? string.Empty : $"最近消息：{run.LastMessage}\n") +
                (string.IsNullOrWhiteSpace(run.ErrorMessage) ? string.Empty : $"错误：{run.ErrorMessage}\n") +
                $"取消请求：{(run.CancelRequested ? "是" : "否")}");
            if (run.Failure != null)
            {
                var failure = run.Failure;
                var failureLines = new List<string>
                {
                    $"Run 失败契约：{FirstNonEmpty(failure.Code, "UNKNOWN_RUNTIME_FAILURE")}/{FirstNonEmpty(failure.Stage, run.CurrentPhase)}",
                    $"可恢复：{(failure.Recoverable ? "是" : "否")}"
                };
                if (!string.IsNullOrWhiteSpace(failure.Message))
                    failureLines.Add($"原因：{failure.Message}");
                if (!string.IsNullOrWhiteSpace(failure.RecommendedAction))
                    failureLines.Add($"推荐动作：{failure.RecommendedAction}");
                failureLines.Add($"是否需要用户决定：{(failure.RequiresUserDecision ? "是" : "否")}");
                if (failure.ArtifactIds.Count > 0)
                    failureLines.Add($"相关产物：{string.Join("、", failure.ArtifactIds.Take(5))}");
                lines.Add(string.Join("\n", failureLines));
            }
        }
        else
        {
            lines.Add("未找到匹配的 Agent Runtime Run，但找到了相关生产记录。");
        }

        if (state.Packages.Count > 0)
        {
            var packageLines = state.Packages
                .Select(p =>
                {
                    var kernel = string.IsNullOrWhiteSpace(p.KernelVersion) ? string.Empty : $"（{p.KernelVersion}）";
                    var rebuilt = p.RebuiltFromPackageIds.Count == 0
                        ? string.Empty
                        : $"，替代旧包 {string.Join("、", p.RebuiltFromPackageIds.Take(4))}";
                    var knowledge = FormatPackageKnowledgeSummary(p.KnowledgeBindingSummary);
                    return $"{p.Id}/{p.PackageKind}/{p.Status}{kernel}{rebuilt}{knowledge}";
                });
            lines.Add("生产包：" + string.Join("；", packageLines));
        }

        if (state.RebuildLinks.Count > 0)
        {
            var linkLines = state.RebuildLinks
                .Take(6)
                .Select(link =>
                {
                    var oldStatus = string.IsNullOrWhiteSpace(link.OldPackageStatus) ? string.Empty : $"（{link.OldPackageStatus}）";
                    var newStatus = string.IsNullOrWhiteSpace(link.NewPackageStatus) ? string.Empty : $"（{link.NewPackageStatus}）";
                    return $"{link.OldPackageId}{oldStatus} -> {link.NewPackageId}{newStatus}";
                });
            lines.Add("重建链路：" + string.Join("；", linkLines));
        }

        if (state.ProductionChains.Count > 0)
        {
            var chainLines = state.ProductionChains
                .Take(5)
                .Select(chain =>
                {
                    var chapterLabel = FirstNonEmpty(
                        chain.ChapterDisplayName,
                        chain.ChapterLogicalId,
                        chain.ChapterId);
                    var steps = chain.Steps.Count == 0
                        ? string.Empty
                        : $"，步骤：{string.Join(" -> ", chain.Steps.Select(step => $"{step.Label}/{step.Status}").Take(6))}";
                    var version = string.IsNullOrWhiteSpace(chain.ChapterVersionId)
                        ? string.Empty
                        : $"，章节版本 {chain.ChapterVersionId}";
                    var fact = string.IsNullOrWhiteSpace(chain.FactSnapshotId)
                        ? string.Empty
                        : $"，事实快照 {chain.FactSnapshotId}/v{chain.FactSnapshotVersion}";
                    return $"{chapterLabel}/{chain.RuntimeRunId}/{chain.Status}：{chain.Summary}{version}{fact}{steps}";
                });
            lines.Add("生产链路：" + string.Join("；", chainLines));
        }

        if (state.FactSnapshots.Count > 0)
        {
            var factLines = state.FactSnapshots
                .TakeLast(3)
                .Select(snapshot =>
                {
                    if (!snapshot.IsParseable)
                        return $"{snapshot.ChapterId}/v{snapshot.VersionNumber}：事实快照不可解析";

                    var title = FirstNonEmpty(snapshot.ChapterTitle, snapshot.ChapterId);
                    var protagonist = string.IsNullOrWhiteSpace(snapshot.ProtagonistName)
                        ? string.Empty
                        : $"主角 {snapshot.ProtagonistName}";
                    var status = FirstNonEmpty(snapshot.ProtagonistStatus, snapshot.ProtagonistIdentity);
                    var statusPart = string.IsNullOrWhiteSpace(status) ? string.Empty : $"，状态 {status}";
                    var ending = string.IsNullOrWhiteSpace(snapshot.EndingState) ? string.Empty : $"，结尾 {snapshot.EndingState}";
                    var carry = snapshot.NextChapterMustCarry.Count == 0
                        ? string.Empty
                        : $"，下一章必须承接：{string.Join("、", snapshot.NextChapterMustCarry.Take(3))}";
                    return $"{title}/v{snapshot.VersionNumber}/{snapshot.Source}：{protagonist}{statusPart}{ending}{carry}";
                });
            lines.Add("事实沉淀：" + string.Join("；", factLines));
        }

        if (state.ChapterDrafts.Count > 0)
        {
            var draftLines = state.ChapterDrafts
                .TakeLast(6)
                .Select(draft =>
                {
                    var changes = draft.HasChanges ? "含CHANGES" : "无CHANGES";
                    var repair = draft.RepairAttemptCount > 0 ? $"，修复 {draft.RepairAttemptCount} 次" : string.Empty;
                    var preview = string.IsNullOrWhiteSpace(draft.Preview) ? string.Empty : $"：{TrimBody(draft.Preview, 90)}";
                    return $"{draft.ChapterId}/{draft.Status}/{draft.ContentLength}字/{changes}（{draft.ArtifactId}）{repair}{preview}";
                });
            lines.Add("章节草稿：" + string.Join("；", draftLines));
        }

        if (state.GenerationGateReports.Count > 0)
        {
            var gateLines = state.GenerationGateReports
                .TakeLast(6)
                .Select(report =>
                {
                    var passBits = new[]
                    {
                        report.ProtocolPassed ? "协议" : string.Empty,
                        report.ChangesDetected ? "CHANGES" : string.Empty,
                        report.FactSnapshotPassed ? "事实" : string.Empty,
                        report.BlueprintPassed ? "蓝图" : string.Empty,
                        report.RagPassed ? "知识" : string.Empty
                    }.Where(item => !string.IsNullOrWhiteSpace(item));
                    return $"{report.ChapterId}/{report.Status}（{report.Id}，通过={string.Join("/", passBits)}，问题={report.IssueCount}，修复提示={report.RepairHintCount}）";
                });
            lines.Add("门禁报告：" + string.Join("；", gateLines));
        }

        if (state.AgentReviews.Count > 0)
        {
            var reviewLines = state.AgentReviews
                .TakeLast(6)
                .Select(review =>
                {
                    var rewrite = review.RequiresRewrite ? "需重写" : "可继续";
                    var editorial = FormatAgentReviewEditorialFields(
                        review.MeetsAcceptedCreativeIntents,
                        review.ContinuityRisk,
                        review.ChapterPacing,
                        review.RecommendedAction);
                    var summary = string.IsNullOrWhiteSpace(review.Summary) ? string.Empty : $"：{TrimBody(review.Summary, 80)}";
                    return $"{review.ChapterId}/{review.OverallResult}/{rewrite}/score={review.QualityScore}{editorial}（{review.ReviewId}，检查={review.CheckCount}）{summary}";
                });
            lines.Add("AgentReview：" + string.Join("；", reviewLines));
        }

        if (state.MemoryReads.Count > 0)
        {
            var readLines = state.MemoryReads
                .TakeLast(6)
                .Select(read =>
                {
                    var keys = read.MemoryKeys.Count == 0
                        ? "无key"
                        : string.Join("、", read.MemoryKeys.Take(4));
                    var run = string.IsNullOrWhiteSpace(read.RunId) ? string.Empty : $"，run={read.RunId}";
                    return $"{read.MemoryScope}/{read.Consumer}（{read.Id}{run}，keys={keys}）";
                });
            lines.Add("记忆读取：" + string.Join("；", readLines));
        }

        if (state.MemoryPromotions.Count > 0)
        {
            var promotionLines = state.MemoryPromotions
                .TakeLast(6)
                .Select(promotion =>
                {
                    var payload = string.IsNullOrWhiteSpace(promotion.PayloadJson)
                        ? string.Empty
                        : $"：{TrimBody(promotion.PayloadJson, 80)}";
                    var run = string.IsNullOrWhiteSpace(promotion.RunId) ? string.Empty : $"，run={promotion.RunId}";
                    return $"{promotion.SourceMemoryKey} -> {promotion.TargetMemoryKey}（{promotion.PromotionReason}{run}，{promotion.Id}）{payload}";
                });
            lines.Add("记忆提升：" + string.Join("；", promotionLines));
        }

        if (state.ChapterChanges.Count > 0)
        {
            var changeLines = state.ChapterChanges
                .TakeLast(6)
                .Select(change =>
                {
                    var applied = change.AppliedToFactSnapshot ? "已进入事实快照" : "未进入事实快照";
                    var error = string.IsNullOrWhiteSpace(change.ParseError) ? string.Empty : $"，错误：{TrimBody(change.ParseError, 80)}";
                    return $"{change.ChapterId}/{change.ParseStatus}/{applied}（{change.Id}）{error}";
                });
            lines.Add("CHANGES审计：" + string.Join("；", changeLines));
        }

        if (state.StalePackages.Count > 0)
        {
            var staleLines = state.StalePackages
                .Take(6)
                .Select(p =>
                {
                    var plan = string.IsNullOrWhiteSpace(p.RevisionPlanId) ? "无关联修订计划" : $"修订计划 {p.RevisionPlanId}/{p.RevisionPlanStatus}";
                    var action = string.IsNullOrWhiteSpace(p.RecommendedToolName) ? "等待 Agent 决定下一步" : $"建议 {p.RecommendedToolName}";
                    return $"{p.PackageId}（章节 {p.ChapterId}，{plan}，{action}：{p.Reason}）";
                });
            lines.Add("过期生产包：" + string.Join("；", staleLines));
        }

        if (state.DependencyBlocks.Count > 0)
        {
            var blockLines = state.DependencyBlocks
                .Take(6)
                .Select(block =>
                {
                    var outboxes = block.OutboxEventTypes.Count == 0
                        ? "未列出 outbox 类型"
                        : string.Join("、", block.OutboxEventTypes.Take(4));
                    var statuses = block.Statuses.Count == 0
                        ? "unknown"
                        : string.Join("、", block.Statuses.Take(4));
                    return $"第 {block.TargetChapterNumber} 章被第 {block.PreviousChapterNumber} 章后台沉淀阻塞：{outboxes}/{statuses}。{block.Reason}";
                });
            lines.Add("生产依赖阻塞：" + string.Join("；", blockLines));
        }

        if (state.RevisionPlans.Count > 0)
        {
            var revisionLines = state.RevisionPlans
                .TakeLast(5)
                .Select(p =>
                {
                    var requirements = TryFormatJsonArraySummary(p.RequirementsJson, "要求");
                    var invalidated = TryFormatJsonArraySummary(p.InvalidatedPackageIdsJson, "失效包");
                    var details = string.Join("，", new[] { requirements, invalidated }.Where(x => !string.IsNullOrWhiteSpace(x)));
                    var prefix = string.IsNullOrWhiteSpace(details) ? string.Empty : $"{details}，";
                    return $"{p.Id}/{p.Status}/{p.PlanType}：{prefix}{p.Recommendation}";
                });
            lines.Add("修订计划：" + string.Join("；", revisionLines));
        }

        if (state.ToolExecutions.Count > 0)
        {
            var toolLines = state.ToolExecutions
                .TakeLast(5)
                .Select(t =>
                {
                    var contract = t.SemanticContract;
                    var displayName = string.IsNullOrWhiteSpace(contract.DisplayName)
                        ? t.ToolName
                        : contract.DisplayName;
                    var inputs = contract.InputArtifacts.Count == 0
                        ? string.Empty
                        : $"，输入={string.Join("/", contract.InputArtifacts.Take(3))}";
                    var outputs = contract.OutputArtifacts.Count == 0
                        ? string.Empty
                        : $"，输出={string.Join("/", contract.OutputArtifacts.Take(3))}";
                    var error = string.IsNullOrWhiteSpace(t.ErrorMessage) ? string.Empty : $"（{t.ErrorMessage}）";
                    return $"{displayName}/{t.ToolName}/{t.Status}{inputs}{outputs}{error}";
                });
            lines.Add("工具执行：" + string.Join("；", toolLines));
        }

        if (state.OutputArtifacts.Count > 0)
        {
            var artifactLines = state.OutputArtifacts
                .TakeLast(6)
                .Select(a =>
                {
                    var artifact = $"{a.ArtifactType}/{a.ArtifactId}";
                    var kind = string.IsNullOrWhiteSpace(a.OutputKind) ? string.Empty : $"/{a.OutputKind}";
                    var status = string.IsNullOrWhiteSpace(a.Status) ? string.Empty : $"/{a.Status}";
                    var summary = string.IsNullOrWhiteSpace(a.Summary) ? string.Empty : $"：{TrimBody(a.Summary, 80)}";
                    var visibleWhere = a.UserVisibleWhere.ToList();
                    if (a.VisibleInWorkflow && !visibleWhere.Contains("创作工作流", StringComparer.Ordinal))
                        visibleWhere.Add("创作工作流");
                    if (a.VisibleInLibrary && !visibleWhere.Contains("小说书城", StringComparer.Ordinal))
                        visibleWhere.Add("小说书城");
                    var visible = visibleWhere.Count == 0
                        ? string.Empty
                        : $"，可见={string.Join("、", visibleWhere.Take(4))}";
                    var source = string.IsNullOrWhiteSpace(a.SourceEventType)
                        ? string.Empty
                        : $"，来源={a.SourceEventType}" +
                          (string.IsNullOrWhiteSpace(a.SourceEventId) ? string.Empty : $"/{a.SourceEventId}");
                    return $"{artifact}{kind}{status}{summary}{visible}{source}";
                });
            lines.Add("已产出：" + string.Join("；", artifactLines));
        }

        var latestFailedTool = state.ToolExecutions
            .Where(t => string.Equals(t.Status, "failed", StringComparison.OrdinalIgnoreCase))
            .LastOrDefault();
        if (latestFailedTool?.Failure != null)
        {
            var failure = latestFailedTool.Failure;
            var failedStage = FormatProductionStageLabel(FirstNonEmpty(failure.FailedStage, latestFailedTool.ResultMessage, latestFailedTool.Phase));
            var reason = FirstNonEmpty(failure.Reason, latestFailedTool.ErrorMessage, latestFailedTool.ResultMessage, "未知原因");
            lines.Add($"最近失败：{latestFailedTool.ToolName} 在{failedStage}停止：{reason}");
            if (failure.RecoverableActions.Count > 0)
                lines.Add($"可恢复动作：{string.Join("、", failure.RecoverableActions)}");
            if (!string.IsNullOrWhiteSpace(failure.RecommendedAction))
                lines.Add($"推荐动作：{failure.RecommendedAction}");
            lines.Add($"是否需要用户决定：{(failure.RequiresUserDecision ? "是" : "否")}");
            if (failure.ProducedArtifacts.Count > 0)
            {
                var artifactLines = failure.ProducedArtifacts
                    .Take(3)
                    .Select(a => $"{a.ArtifactType}/{a.ArtifactId}" + (string.IsNullOrWhiteSpace(a.Summary) ? string.Empty : $"（{a.Summary}）"));
                lines.Add("失败保留产物：" + string.Join("；", artifactLines));
            }
        }

        var currentProductionProgress = state.RuntimeEvents
            .Where(e => string.Equals(e.Type, "production_progress", StringComparison.OrdinalIgnoreCase))
            .LastOrDefault();
        if (currentProductionProgress != null)
        {
            lines.Add($"当前生产进度：{FormatProductionStageLabel(currentProductionProgress.Stage)}/{currentProductionProgress.Status}：{currentProductionProgress.Message}");
        }

        if (state.ProductionEvents.Count > 0)
        {
            var eventLines = state.ProductionEvents
                .TakeLast(6)
                .Select(FormatNovelProductionEventLine);
            lines.Add("生产事件：" + string.Join("；", eventLines));
        }

        if (state.RuntimeEvents.Count > 0)
        {
            var runtimeLines = state.RuntimeEvents
                .TakeLast(4)
                .Select(e => $"{e.Stage}/{e.Status}：{e.Message}");
            lines.Add("Runtime 事件：" + string.Join("；", runtimeLines));
        }

        var pendingOutboxCount = state.OutboxEvents.Count(e => e.Status is "pending" or "retryable_failed");
        lines.Add($"待处理 outbox：{pendingOutboxCount}");
        if (state.OutboxEvents.Count > 0)
        {
            var outboxLines = state.OutboxEvents
                .TakeLast(4)
                .Select(e => $"{e.EventType}/{e.Status}/attempts={e.Attempts}" + (string.IsNullOrWhiteSpace(e.LastError) ? string.Empty : $"（{e.LastError}）"));
            lines.Add("Outbox：" + string.Join("；", outboxLines));
        }

        return string.Join("\n\n", lines);
    }

    private static async Task<bool> CanAccessProjectAsync(
        NovelAgentDbContext db,
        string userId,
        string projectId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(projectId))
            return false;

        var userRole = await db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.Role)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        var isAdmin = string.Equals(userRole, "admin", StringComparison.OrdinalIgnoreCase);
        return await db.NovelProjects
            .AsNoTracking()
            .AnyAsync(project =>
                    project.Id == projectId &&
                    (isAdmin || project.UserId == userId),
                ct)
            .ConfigureAwait(false);
    }

    private static string FormatProductionOutboxList(OutboxAdminListResponse result)
    {
        if (result.Items.Count == 0)
            return "当前项目没有匹配的后台 outbox。";

        var lines = new List<string>
        {
            $"后台 outbox：{result.Count} 条"
        };
        lines.AddRange(result.Items
            .Take(12)
            .Select(item =>
            {
                var error = string.IsNullOrWhiteSpace(item.LastError) ? string.Empty : $"，错误：{item.LastError}";
                var next = item.NextAttemptAt == null ? string.Empty : $"，下次重试：{item.NextAttemptAt:O}";
                return $"{item.Id}/{item.EventType}/{item.AggregateType}/{item.Status}/attempts={item.Attempts}{error}{next}";
            }));
        return string.Join("\n", lines);
    }

    private static IReadOnlyList<string> BuildProductionOutboxHints(OutboxAdminListResponse result)
    {
        if (result.Items.Any(item => item.Status is "retryable_failed" or "failed"))
            return new[] { "重试失败 outbox", "查询生产状态", "查看工作流" };
        if (result.Items.Any(item => item.Status is "pending" or "processing"))
            return new[] { "等待后台处理", "查询生产状态", "稍后再查 outbox" };
        return new[] { "查询生产状态", "继续章节生产", "查看工作流" };
    }

    private static string FormatNovelProductionEventLine(NovelProductionEventState evt)
    {
        var failureSuffix = FormatNovelProductionEventFailure(evt.Failure);
        if (evt.EventType.StartsWith("outbox_", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(evt.Stage, "index_outbox", StringComparison.OrdinalIgnoreCase))
        {
            var error = ExtractJsonString(evt.DataJson, "error");
            var suffix = string.IsNullOrWhiteSpace(error) ? string.Empty : $"（{error}）";
            return $"后台索引：{evt.Status}：{evt.Message}{suffix}{failureSuffix}";
        }

        return $"{evt.Stage}/{evt.Status}：{evt.Message}{failureSuffix}";
    }

    private static string FormatNovelProductionEventFailure(NovelProductionEventFailureState? failure)
    {
        if (failure == null)
            return string.Empty;

        var parts = new List<string>
        {
            $"失败契约：{FirstNonEmpty(failure.Code, "PRODUCTION_FAILED")}/{FirstNonEmpty(failure.Stage, "unknown")}",
            $"可恢复：{(failure.Recoverable ? "是" : "否")}"
        };
        if (!string.IsNullOrWhiteSpace(failure.RecommendedAction))
            parts.Add($"推荐动作：{failure.RecommendedAction}");
        parts.Add($"是否需要用户决定：{(failure.RequiresUserDecision ? "是" : "否")}");

        return "；" + string.Join("；", parts);
    }

    private static string ExtractJsonString(string json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(propertyName))
            return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty(propertyName, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
        }

        return string.Empty;
    }

    private static IReadOnlyList<string> BuildNovelProductionStateHints(NovelProductionStateQueryResult state)
    {
        if (state.RuntimeRun?.Status is "failed")
            return new[] { "查看失败阶段", "根据失败原因恢复", "请求用户确认下一步" };
        if (state.DependencyBlocks.Count > 0)
            return new[] { "等待上一章事实沉淀完成", "查看阻塞 outbox", "重试后台 outbox" };
        if (state.OutboxEvents.Any(e => e.Status is "pending" or "retryable_failed"))
            return new[] { "等待后台索引完成", "查看 outbox 失败原因", "继续查询生产状态" };
        if (state.StalePackages.Count > 0)
            return new[] { "重建过期章节生产包", "查看修订计划影响范围", "继续章节生产" };
        if (state.RuntimeRun?.Status is "running" or "queued")
            return new[] { "继续等待当前工具", "补充创作要求", "查询章节内容" };
        return new[] { "查询章节内容", "继续下一步生产", "查看工作流" };
    }

    private static string RuntimeRunStatusLabel(string status) =>
        status switch
        {
            "queued" => "排队等待",
            "running" => "后台执行中",
            "completed" => "已完成",
            "failed" => "执行失败",
            "cancelled" => "已取消",
            _ => status
        };

    private static IReadOnlyList<string> BuildProjectContentNextHints(IReadOnlyList<ProjectContentQueryItem> items)
    {
        if (items.Any(item => !string.IsNullOrWhiteSpace(item.SourceRunId)))
        {
            return new[]
            {
                "基于该章节工作流 Run 重新生成修订稿",
                "执行硬门禁校验",
                "执行质量评审",
                "重新提交书城"
            };
        }

        return new[] { "基于真实章节回答", "继续下一章" };
    }

    private static string TrimBody(string body, int maxLength)
    {
        var text = string.IsNullOrWhiteSpace(body)
            ? "未找到正文内容。"
            : body.Trim();
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }

    private static AgentToolArtifact BuildArtifact(
        string type,
        string artifactId,
        string projectId,
        string runId,
        string summary,
        IReadOnlyList<string> nextHints) => new()
        {
            ArtifactType = type,
            ArtifactId = artifactId,
            ProjectId = projectId,
            RunId = runId,
            Summary = summary,
            NextHints = nextHints,
            VisibleInWorkflow = IsWorkflowArtifact(type),
            VisibleInLibrary = string.Equals(type, "chapter_committed", StringComparison.OrdinalIgnoreCase),
            UserVisibleStatus = MapArtifactStatus(type),
        };

    private static bool IsWorkflowArtifact(string type) =>
        type is not "knowledge_hits";

    private static string MapArtifactStatus(string type) =>
        type switch
        {
            "novel_project" or "existing_novel_project" => "新书任务已创建",
            "project_bound" => "已有项目已绑定",
            "story_foundation_candidates" => "故事地基候选已生成",
            "story_foundation_commit" => "故事地基已固化",
            "volume_arc_candidates" => "卷规划候选已生成",
            "volume_arc_plan" => "卷规划已生成",
            "volume_arc_commit" => "卷规划已提交",
            "chapter_candidates" => "章节候选已生成",
            "chapter_candidate_selection" => "章节候选已选定",
            "chapter_context_package" => "章节上下文包已就绪",
            "chapter_draft_with_changes" => "章节草稿已生成",
            "chapter_generation_blocked" => "章节生成被阻塞",
            "generation_gate_report" => "章节门禁已校验",
            "chapter_draft_repaired" => "章节草稿已修复",
            "chapter_repair_report" => "章节修复需要处理",
            "chapter_committed" => "章节已提交入库",
            "chapter_commit_blocked" => "章节提交被阻塞",
            "dependency_impact" => "依赖影响已分析",
            "chapter_review" => "章节复盘已生成",
            "project_content_query" => "项目内容已读取",
            "committed_chapter_audit" => "已提交章节已审查",
            "committed_chapter_revision" => "已提交章节已修订",
            "committed_chapter_revision_blocked" => "已提交章节修订被阻塞",
            _ => type,
        };

    private static string FormatFoundationCandidates(NovelAgentRun run)
    {
        if (run.MacroCandidates.Count == 0) return "未能生成故事地基候选。";
        var lines = new List<string> { $"生成了 {run.MacroCandidates.Count} 个故事地基候选：" };
        for (var i = 0; i < run.MacroCandidates.Count; i++)
        {
            var c = run.MacroCandidates[i];
            lines.Add(
                $"【{i + 1}】{c.Title}\n" +
                $"核心钩子：{c.CoreHook}\n" +
                $"世界观：{c.WorldbuildingBlueprint}\n" +
                $"升级/能力体系：{c.ProgressionSystem}\n" +
                $"主角：{c.ProtagonistProfile}\n" +
                $"爽点循环：{c.PleasureLoop}\n" +
                $"前三卷：{string.Join(" / ", c.FirstThreeVolumes)}\n" +
                $"首批角色：{string.Join("；", c.KeyCharacters)}\n" +
                $"新颖度/可持续/类型匹配：{c.NoveltyScore}/{c.SustainabilityScore}/{c.TypeMatchScore}");
        }
        return string.Join("\n\n", lines);
    }

    private static string FormatVolumePlan(NovelAgentRun run)
    {
        var plan = run.VolumeArcPlan;
        if (plan == null) return "卷规划生成失败。";
        return $"第一卷「{plan.Title}」规划完成：\n卷承诺：{plan.VolumePromise}\n核心问题：{plan.CoreQuestion}\n中点反转：{plan.MidpointReversal}\n高潮：{plan.Climax}\n章节节拍：{plan.ChapterBeats.Count} 个";
    }

    private static string FormatChapterCandidates(NovelAgentRun run)
    {
        var brief = run.ChapterBrief;
        if (brief == null || brief.Candidates.Count == 0) return "章节候选生成失败。";
        var lines = new List<string> { $"为 {brief.ChapterId} 生成了 {brief.Candidates.Count} 个剧情候选：" };
        for (var i = 0; i < brief.Candidates.Count; i++)
        {
            var c = brief.Candidates[i];
            lines.Add($"【{i + 1}】{c.Title}\n转折：{c.CoreTwist}\n角色选择：{c.CharacterChoice}\n代价：{c.CostOrConsequence}\n总分：{c.TotalScore}");
        }
        if (!string.IsNullOrWhiteSpace(brief.RecommendedCandidateTitle))
            lines.Add($"推荐：「{brief.RecommendedCandidateTitle}」。");
        return string.Join("\n\n", lines);
    }

    private static string Arg(AgentToolCall call, string name, string fallback = "") =>
        call.Arguments.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback;

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static int ArgInt(AgentToolCall call, string name, int fallback = 0) =>
        call.Arguments.TryGetValue(name, out var value) && int.TryParse(value?.Trim(), out var parsed) ? parsed : fallback;

    private static List<string> ArgList(AgentToolCall call, string name)
    {
        if (!call.Arguments.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
            return new List<string>();

        var text = value.Trim();
        if (TryParseStructuredArgList(text, out var structured) && structured.Count > 0)
            return structured;
        if (TryParseNaturalDirectionBlocks(text, out var naturalBlocks) && naturalBlocks.Count > 0)
            return naturalBlocks;

        return NormalizeArgListItems(text
            .Split(new[] { '\n', ',', '，', '、', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()));
    }

    private static bool TryParseNaturalDirectionBlocks(string text, out List<string> items)
    {
        items = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var markerIndexes = FindDirectionMarkerIndexes(normalized);
        if (markerIndexes.Count >= 2)
        {
            var blocks = new List<string>();
            for (var i = 0; i < markerIndexes.Count; i++)
            {
                var start = markerIndexes[i];
                var end = i + 1 < markerIndexes.Count ? markerIndexes[i + 1] : normalized.Length;
                blocks.Add(normalized[start..end]);
            }

            items = NormalizeArgListItems(blocks);
            return items.Count > 0;
        }

        var paragraphs = normalized
            .Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        if (paragraphs.Count >= 2 && paragraphs.Count <= 8)
        {
            items = NormalizeArgListItems(paragraphs);
            return items.Count > 0;
        }

        return false;
    }

    private static List<int> FindDirectionMarkerIndexes(string text)
    {
        var indexes = new List<int>();
        var offset = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            var leadingWhitespace = rawLine.Length - rawLine.TrimStart().Length;
            var line = rawLine.TrimStart();
            if (LooksLikeDirectionMarker(line))
                indexes.Add(offset + leadingWhitespace);
            offset += rawLine.Length + 1;
        }

        if (indexes.Count >= 2)
            return indexes.Distinct().OrderBy(x => x).ToList();

        var search = 0;
        while (search >= 0 && search < text.Length)
        {
            var index = text.IndexOf("【方向", search, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                break;
            indexes.Add(index);
            search = index + 2;
        }

        return indexes.Distinct().OrderBy(x => x).ToList();
    }

    private static bool LooksLikeDirectionMarker(string line)
    {
        if (line.StartsWith("【方向", StringComparison.OrdinalIgnoreCase))
            return true;

        var clean = line
            .TrimStart('-', '*', '+', ' ', '\t')
            .TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.', '、', ')', '）', ' ');
        return clean.StartsWith("方向", StringComparison.OrdinalIgnoreCase) &&
               clean.Length >= 3 &&
               (clean.Contains('：') || clean.Contains(':') || clean.Contains('【'));
    }

    private static bool TryParseStructuredArgList(string text, out List<string> items)
    {
        items = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var cleaned = StripJsonCodeFence(text);
        if (!cleaned.StartsWith("[", StringComparison.Ordinal) &&
            !cleaned.StartsWith("{", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            items = NormalizeArgListItems(ExtractArgListItems(doc.RootElement));
            return true;
        }
        catch (JsonException)
        {
            items = new List<string>();
            return false;
        }
    }

    private static IEnumerable<string> ExtractArgListItems(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var value in ExtractArgListItems(item))
                    yield return value;
            }

            yield break;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString() ?? string.Empty;
            if (TryParseStructuredArgList(value, out var nested) && nested.Count > 0)
            {
                foreach (var item in nested)
                    yield return item;
            }
            else
            {
                yield return value;
            }

            yield break;
        }

        if (element.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var nestedKey in new[] { "candidateDirections", "directions", "items", "candidates", "options" })
        {
            if (element.TryGetProperty(nestedKey, out var nested) && nested.ValueKind == JsonValueKind.Array)
            {
                foreach (var value in ExtractArgListItems(nested))
                    yield return value;
            }
        }

        foreach (var key in new[] { "direction", "title", "name", "label", "brief", "summary", "description", "content", "value" })
        {
            if (element.TryGetProperty(key, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString()))
            {
                yield return value.GetString()!;
                yield break;
            }
        }
    }

    private static List<string> NormalizeArgListItems(IEnumerable<string> values) =>
        values
            .Select(CleanArgListItem)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string CleanArgListItem(string value)
    {
        var text = StripJsonCodeFence(value).Trim();
        text = text.Trim(' ', '"', '\'', '`', '[', ']', '{', '}', '，', ',', '；', ';');
        return text;
    }

    private static string StripJsonCodeFence(string value)
    {
        var cleaned = value.Trim();
        if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[7..];
        else if (cleaned.StartsWith("```", StringComparison.Ordinal))
            cleaned = cleaned[3..];
        if (cleaned.EndsWith("```", StringComparison.Ordinal))
            cleaned = cleaned[..^3];
        return cleaned.Trim();
    }

    private static int NextVolumeNumber(StoryBibleDocument bible) =>
        Math.Max(1, bible.VolumeArcs.Count + 1);

    private static int NextStartChapterNumber(StoryBibleDocument bible)
    {
        var maxChapter = 0;
        foreach (var volume in bible.VolumeArcs)
        {
            maxChapter = Math.Max(maxChapter, ExtractTrailingNumber(volume.EndChapterId));
            maxChapter = Math.Max(maxChapter, ExtractTrailingNumber(volume.StartChapterId) + Math.Max(0, volume.ExpectedChapterCount - 1));
        }

        foreach (var run in bible.AgentRuns.Where(r => !string.IsNullOrWhiteSpace(r.TargetChapterId)))
            maxChapter = Math.Max(maxChapter, ExtractTrailingNumber(run.TargetChapterId));

        return maxChapter + 1;
    }

    private static int ExtractTrailingNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        var index = value.Length - 1;
        while (index >= 0 && char.IsDigit(value[index])) index--;
        return index == value.Length - 1 || !int.TryParse(value[(index + 1)..], out var number) ? 0 : number;
    }

    private static bool IsSameChapterIdentity(string? candidate, string chapterId, int chapterNumber)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(chapterId))
            return false;

        var normalized = candidate.Trim();
        if (string.Equals(normalized, chapterId, StringComparison.OrdinalIgnoreCase))
            return true;
        if (chapterId.EndsWith("-" + normalized, StringComparison.OrdinalIgnoreCase))
            return true;

        var candidateNumber = ExtractTrailingNumber(normalized);
        return candidateNumber > 0 && chapterNumber > 0 && candidateNumber == chapterNumber;
    }

    private async Task<AgentToolExecutionResult> ProcessKnowledgeFileAsync(
        AgentToolCall call,
        AgentSession session,
        CancellationToken ct)
    {
        var taskId = call.Arguments.GetValueOrDefault("taskId")?.ToString();
        if (string.IsNullOrEmpty(taskId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "错误：缺少 taskId 参数",
                Phase = "error",
            };
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var processingService = scope.ServiceProvider.GetRequiredService<IKnowledgeProcessingService>();
            var progress = BuildKnowledgeProcessingProgressContext(scope, session, taskId);
            var result = await processingService.ProcessPendingFileAsync(taskId, _workspace.UserId, ct, progress).ConfigureAwait(false);
            var data = await BuildKnowledgeProcessingToolResultAsync(scope, taskId, _workspace.UserId, session.ActiveProjectId, ct)
                .ConfigureAwait(false);
            var nextHints = data.NextRecommendedTools.Count > 0
                ? new[] { "分类项目知识", "查询项目知识绑定", "检查知识冲突", "继续写章节" }
                : new[] { "查看知识库", "查询项目知识绑定" };
            var message = data.ExtractedEntriesCount > 0
                ? $"{result} 已提取 {data.ExtractedEntriesCount} 条知识：{string.Join("、", data.KnowledgeIds.Take(6))}。下一步可调用 ClassifyProjectKnowledge、QueryProjectKnowledgeBindings、DetectKnowledgeConflicts，再由 Agent 决定是否 ProduceChapter。"
                : result;
            var artifact = BuildArtifact(
                "knowledge_file_processed",
                taskId,
                data.ProjectId,
                session.ActiveRunId ?? string.Empty,
                message,
                nextHints);
            artifact.UserVisibleWhere = new[] { "知识库", "创作工作流" };

            return new AgentToolExecutionResult
            {
                Success = true,
                Message = message,
                Phase = "knowledge_processed",
                Data = data,
                Artifact = artifact,
                Suggestions = nextHints,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process knowledge file {TaskId}", taskId);
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = $"处理失败：{ex.Message}",
                Phase = "error",
            };
        }
    }

    private static async Task<KnowledgeProcessingToolResult> BuildKnowledgeProcessingToolResultAsync(
        IServiceScope scope,
        string taskId,
        string userId,
        string? activeProjectId,
        CancellationToken ct)
    {
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var task = await db.KnowledgeProcessingTasks
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == taskId && item.UserId == userId, ct)
            .ConfigureAwait(false);
        var projectId = FirstNonEmpty(activeProjectId, task?.ProjectId);
        var knowledgeIds = await db.KnowledgeBases
            .AsNoTracking()
            .Where(item => item.UserId == userId && item.SourceUploadTaskId == taskId && !item.IsArchived)
            .OrderBy(item => item.ChunkIndex ?? int.MaxValue)
            .ThenBy(item => item.CreatedAt)
            .Select(item => item.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var usages = string.IsNullOrWhiteSpace(projectId) || knowledgeIds.Count == 0
            ? new List<ProjectKnowledgeUsageSnapshot>()
            : await db.ProjectKnowledgeUsages
                .AsNoTracking()
                .Where(usage =>
                    usage.UserId == userId &&
                    usage.ProjectId == projectId &&
                    knowledgeIds.Contains(usage.KnowledgeId))
                .Select(usage => new ProjectKnowledgeUsageSnapshot(usage.KnowledgeId, usage.Status))
                .ToListAsync(ct)
                .ConfigureAwait(false);

        var usageStatusById = usages
            .GroupBy(usage => usage.KnowledgeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Status, StringComparer.OrdinalIgnoreCase);

        return new KnowledgeProcessingToolResult
        {
            TaskId = taskId,
            FileName = task?.FileName ?? string.Empty,
            ProjectId = projectId,
            Status = task?.Status ?? string.Empty,
            Strategy = task?.Strategy ?? string.Empty,
            Progress = task?.Progress ?? 0,
            ExtractedEntriesCount = task?.ExtractedEntriesCount ?? knowledgeIds.Count,
            KnowledgeIds = knowledgeIds,
            ImportedKnowledgeIds = knowledgeIds
                .Where(id => usageStatusById.TryGetValue(id, out var status) &&
                             string.Equals(status, "imported", StringComparison.OrdinalIgnoreCase))
                .ToArray(),
            ReferencedKnowledgeIds = knowledgeIds
                .Where(id => usageStatusById.TryGetValue(id, out var status) &&
                             string.Equals(status, "referenced", StringComparison.OrdinalIgnoreCase))
                .ToArray(),
            NextRecommendedTools = knowledgeIds.Count == 0
                ? new[] { "QueryProjectKnowledgeBindings" }
                : new[]
                {
                    "ClassifyProjectKnowledge",
                    "QueryProjectKnowledgeBindings",
                    "DetectKnowledgeConflicts",
                    "SearchCreativeKnowledge",
                    "ProduceChapter"
                }
        };
    }

    private sealed record ProjectKnowledgeUsageSnapshot(string KnowledgeId, string Status);

    private KnowledgeProcessingProgressContext? BuildKnowledgeProcessingProgressContext(IServiceScope scope, AgentSession session, string taskId)
    {
        if (string.IsNullOrWhiteSpace(session.ActiveRunId))
            return null;

        var runtimeEvents = scope.ServiceProvider.GetRequiredService<IAgentRuntimeEventService>();
        var sessions = scope.ServiceProvider.GetRequiredService<AgentSessionManager>();
        return new KnowledgeProcessingProgressContext(
            session.ActiveRunId,
            _workspace.UserId,
            session.SessionId,
            string.IsNullOrWhiteSpace(session.ActiveProjectId) ? null : session.ActiveProjectId,
            async (evt, ct) =>
            {
                var data = new
                {
                    taskId,
                    evt.Stage,
                    evt.Progress,
                    evt.Data
                };
                await runtimeEvents.AppendAsync(
                        new CreateAgentRuntimeEventRequest(
                            session.ActiveRunId,
                            _workspace.UserId,
                            session.SessionId,
                            string.IsNullOrWhiteSpace(session.ActiveProjectId) ? null : session.ActiveProjectId,
                            AgentSseEventType.AgentActing,
                            evt.Message,
                            data,
                            Stage: evt.Stage,
                            Status: "running",
                            ArtifactType: "knowledge_processing",
                            ArtifactId: taskId,
                            DisplaySurface: AgentRuntimeEventSurface.Knowledge,
                            DisplayPolicy: AgentRuntimeEventDisplayPolicy.Timeline,
                            PublishToSse: true),
                        ct)
                    .ConfigureAwait(false);
                await sessions.SendEventAsync(session.SessionId, new AgentSseEvent
                {
                    Type = AgentSseEventType.AgentActing,
                    RunId = session.ActiveRunId,
                    Stage = evt.Stage,
                    Status = "running",
                    ArtifactType = "knowledge_processing",
                    ArtifactId = taskId,
                    DisplaySurface = AgentRuntimeEventSurface.Knowledge,
                    DisplayPolicy = AgentRuntimeEventDisplayPolicy.Timeline,
                    Message = evt.Message,
                    Data = data
                }, ct).ConfigureAwait(false);
            });
    }

    private async Task<AgentToolExecutionResult> ToolSearchAsync(AgentToolCall call, AgentSession session, CancellationToken ct)
    {
        var phaseArg = NormalizeToolSearchPhase(Arg(call, "phase", session.Phase));
        var query = FirstNonEmpty(Arg(call, "query"), Arg(call, "intent"), Arg(call, "context"), session.WorkingMemory.CurrentGoal);
        var intent = Arg(call, "intent");
        var context = Arg(call, "context");
        var includeAll = ArgBool(call, "includeAll");
        var requestedLimit = ArgInt(call, "limit", 12);
        var limit = includeAll
            ? int.MaxValue
            : Math.Clamp(requestedLimit <= 0 ? 12 : requestedLimit, 1, 50);

        var rankedTools = SearchToolDefinitions(query, intent, context, phaseArg, includeAll, limit);
        var toolNames = rankedTools.Select(def => def.Name).ToArray();
        var matches = rankedTools
            .Select(def => new ToolSearchMatch(
                def.Name,
                def.Semantic.DisplayName,
                BuildToolRelevanceReason(def, query, intent, context)))
            .ToArray();

        var toolList = toolNames
            .Select(name => _entries.TryGetValue(name, out var entry) ? entry.Definition : null)
            .Where(def => def != null)
            .Select(def => $"• {def!.Name} / {def.Semantic.DisplayName}（{def.Description}；空间={def.Semantic.DomainSurface}；产物={def.Semantic.OutputKind}；需要项目={def.Semantic.RequiresProject}；无项目可用={def.Semantic.SupportsNoProjectSession}；耗时={def.Semantic.AverageDuration}；可见位置={def.Semantic.UserVisibleWhere}；可搭配={string.Join("/", def.Semantic.NextPossibleTools.Take(4))}；非限制，模型仍应按当前目标自主选择工具）")
            .ToList();

        var message = toolList.Count == 0
            ? "已读取当前创作上下文，正在重新判断下一步。"
            : $"已准备 {toolList.Count} 项可用创作能力，正在选择最适合当前目标的下一步。";

        var discoveredTools = rankedTools
            .Select(def => new ToolSchema
            {
                Name = def.Name,
                Description = def.Description,
                Risk = def.Risk,
                RequiresConfirmation = def.RequiresConfirmation,
                Parameters = def.Arguments?.ToDictionary(p => p, _ => "string") ?? new Dictionary<string, string>(),
                SideEffects = def.SideEffects,
                Semantic = def.Semantic
            })
            .ToList();

        using var scope = _serviceProvider.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IToolSearchCacheService>();
        var searchScope = BuildToolSearchScopeKey(query, intent, context, phaseArg, includeAll);
        await cache.SaveAsync(
                session,
                searchScope,
                discoveredTools,
                ToolCatalogSignature.Compute(ListToolSchemas()),
                ct)
            .ConfigureAwait(false);

        return new AgentToolExecutionResult
        {
            Success = true,
            Message = message,
            Phase = session.Phase,
            Data = new { Query = query, PhaseHint = phaseArg, ScopeKey = searchScope, Tools = toolNames, Matches = matches, ToolDetails = toolList },
            Artifact = BuildArtifact("tool_search_result", searchScope, session.ActiveProjectId ?? string.Empty, string.Empty, $"已准备 {toolList.Count} 项可用创作能力。", Array.Empty<string>()),
            Suggestions = Array.Empty<string>(),
        };
    }

    private static string BuildToolRelevanceReason(AgentToolDefinition tool, string query, string intent, string context)
    {
        var related = new List<string>();
        var searchText = string.Join(' ', new[] { query, intent, context }.Where(x => !string.IsNullOrWhiteSpace(x)));

        AddIfMatches(related, searchText, tool.Semantic.DomainSurface, $"相关空间：{tool.Semantic.DomainSurface}");
        AddIfMatches(related, searchText, tool.Semantic.OutputKind, $"相关产物：{tool.Semantic.OutputKind}");
        AddIfMatches(related, searchText, tool.Description, "工具描述与当前请求相关");
        AddIfMatches(related, searchText, string.Join(' ', tool.Semantic.ReadsFrom), $"读取：{string.Join("/", tool.Semantic.ReadsFrom.Take(3))}");
        AddIfMatches(related, searchText, string.Join(' ', tool.Semantic.ProgressEventContract), "进度事件可解释当前阶段");
        AddIfMatches(related, searchText, string.Join(' ', tool.Semantic.NextPossibleTools), $"可搭配能力：{string.Join("/", tool.Semantic.NextPossibleTools.Take(3))}");

        if (related.Count == 0)
        {
            related.Add(tool.Semantic.RequiresProject
                ? "该工具属于项目上下文能力，模型需确认当前任务是否需要项目状态。"
                : "该工具支持无项目会话，适合先读取或发现上下文。");
        }

        return string.Join("；", related.Distinct(StringComparer.OrdinalIgnoreCase).Take(3));
    }

    private static void AddIfMatches(List<string> reasons, string searchText, string haystack, string reason)
    {
        if (string.IsNullOrWhiteSpace(searchText) || string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(reason))
            return;

        var terms = TokenizeToolSearchText(searchText);
        if (terms.Count == 0 ||
            terms.Any(term => haystack.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
            haystack.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            searchText.Contains(haystack, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add(reason);
        }
    }

    private sealed record ToolSearchMatch(string Tool, string DisplayName, string RelevanceReason);

    private IReadOnlyList<AgentToolDefinition> SearchToolDefinitions(
        string query,
        string intent,
        string context,
        string phaseHint,
        bool includeAll,
        int limit)
    {
        var phase = phaseHint switch
        {
            "Planning" => ConversationPhase.Planning,
            "Creation" => ConversationPhase.Creation,
            "Review" => ConversationPhase.Review,
            _ => ConversationPhase.Conversation,
        };
        var searchText = string.Join(' ', new[] { query, intent, context }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var terms = TokenizeToolSearchText(searchText);

        return _entries.Values
            .Where(entry => !string.Equals(entry.Definition.Name, "tool_search", StringComparison.OrdinalIgnoreCase))
            .Select((entry, index) => new
            {
                Definition = entry.Definition,
                Index = index,
                Score = includeAll ? 0 : ScoreTool(entry.Definition, searchText, terms),
                HintRank = CategoryHintRank(entry.Category, phase)
            })
            .Where(x => includeAll || terms.Count == 0 || x.Score > 0 || x.HintRank >= 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.HintRank < 0 ? int.MaxValue : x.HintRank)
            .ThenBy(x => x.Index)
            .Take(limit)
            .Select(x => x.Definition)
            .ToList();
    }

    private static int ScoreTool(AgentToolDefinition tool, string searchText, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
            return 0;

        var haystack = string.Join(' ', new[]
        {
            tool.Name,
            tool.Semantic.DisplayName,
            tool.Description,
            tool.Risk,
            tool.Semantic.DomainSurface,
            tool.Semantic.OutputKind,
            tool.Semantic.UserVisibleWhere,
            tool.Semantic.ResultSemantics,
            tool.Semantic.AverageDuration,
            tool.Semantic.ImpactScope,
            tool.Semantic.FailureContract,
            string.Join(' ', tool.Arguments),
            string.Join(' ', tool.Semantic.ProgressEventContract),
            string.Join(' ', tool.Semantic.NextPossibleTools),
            string.Join(' ', tool.Semantic.ReadsFrom),
            string.Join(' ', tool.Semantic.WritesTo),
        }).ToLowerInvariant();

        var score = 0;
        foreach (var term in terms)
        {
            if (haystack.Contains(term, StringComparison.OrdinalIgnoreCase))
                score += tool.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ? 6 : 2;
        }
        foreach (var alias in BuildToolSearchAliases(tool.Name))
        {
            if (searchText.Contains(alias, StringComparison.OrdinalIgnoreCase))
                score += 6;
        }
        return score;
    }

    private static IReadOnlyList<string> BuildToolSearchAliases(string toolName) =>
        toolName switch
        {
            "ProduceChapter" => new[] { "继续写", "写第", "写章", "写正文", "生成正文", "生产章节", "提交书城" },
            "SearchCreativeKnowledge" => new[] { "知识库", "知识", "素材", "设定" },
            "QueryProjectContent" => new[] { "已有正文", "查看正文", "读取正文", "第几章", "所属卷" },
            "QueryNovelProductionState" => new[] { "执行到哪", "卡在哪", "生产阶段", "后台进度" },
            "CreateCreativeIntent" => new[] { "新想法", "创意", "补充要求", "改方向" },
            "CreateRevisionPlan" => new[] { "重写", "修订计划", "改写", "返工" },
            _ => Array.Empty<string>()
        };

    private static IReadOnlyList<string> TokenizeToolSearchText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        return text
            .Split(new[] { ' ', '\t', '\r', '\n', ',', '.', ';', ':', '，', '。', '；', '：', '、', '(', ')', '（', '）', '[', ']', '【', '】', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .Where(t => t.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToList();
    }

    private static int CategoryHintRank(string category, ConversationPhase phase)
    {
        return phase switch
        {
            ConversationPhase.Planning => category switch
            {
                "planning" => 0,
                "project" => 1,
                "content" or "knowledge" or "rag" => 2,
                "blackboard" or "workspace" => 3,
                "commit" => 4,
                _ => 10,
            },
            ConversationPhase.Creation => category switch
            {
                "writing" => 0,
                "gate" => 1,
                "planning" => 2,
                "content" or "blackboard" or "workspace" => 3,
                _ => 10,
            },
            ConversationPhase.Review => category switch
            {
                "review" or "gate" => 0,
                "commit" => 1,
                "maintenance" => 2,
                "content" or "blackboard" or "workspace" => 3,
                _ => 10,
            },
            _ => category switch
            {
                "workspace" or "blackboard" => 0,
                "project" or "content" => 1,
                "knowledge" or "rag" => 2,
                _ => 10,
            },
        };
    }

    private static bool ArgBool(AgentToolCall call, string name, bool fallback = false) =>
        call.Arguments.TryGetValue(name, out var value) && bool.TryParse(value?.Trim(), out var parsed) ? parsed : fallback;

    private static string BuildToolSearchScopeKey(string query, string intent, string context, string phaseHint, bool includeAll)
    {
        var normalized = string.Join("|", new[]
        {
            $"phaseHint={phaseHint}",
            $"includeAll={includeAll}",
            $"query={query}",
            $"intent={intent}",
            $"context={context}",
        }).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return "global";
        var hash = ComputeStableHash(normalized);
        return $"global:{phaseHint}:{hash}";
    }

    private static string ComputeStableHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    private static string NormalizeToolSearchPhase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Conversation";

        return value.Trim().ToLowerInvariant() switch
        {
            "planning" => "Planning",
            "creation" => "Creation",
            "review" => "Review",
            "all" => "All",
            "conversation" => "Conversation",
            _ => "Conversation"
        };
    }

    /// <summary>
    /// 构建 Issues 指纹，用于检测 Rewrite Loop 是否陷入重复。
    /// 连续 2 次相同指纹 → RepairHints 无效 → 智能终止。
    /// </summary>
    private static string BuildIssuesFingerprint(AgentToolExecutionResult result)
    {
        if (result.Data == null) return string.Empty;

        var gateReport = result.Data as GenerationGateReport;
        if (gateReport?.Issues == null || gateReport.Issues.Count == 0)
            return string.Empty;

        // 指纹 = Issues 排序后的 hash
        var sortedIssues = string.Join("|", gateReport.Issues.OrderBy(x => x));
        return sortedIssues.GetHashCode().ToString();
    }

    /// <summary>
    /// 检查是否应该提示 Agent 聚合 DesignRules。
    /// 已分类 ≥5 个知识 且 最近一次聚合后又分类了 ≥3 个 → 建议聚合。
    /// </summary>
    private async Task<string> ShouldSuggestAggregationAsync(string userId, string projectId, CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();

            // 统计已分类知识数量
            var classifiedCount = await db.KnowledgeClassifications
                .Where(x => x.UserId == userId && x.ProjectId == projectId)
                .CountAsync(ct)
                .ConfigureAwait(false);

            if (classifiedCount < 5)
                return string.Empty;

            // 检查最近一次聚合时间
            var lastAggregation = await db.ProjectDesignRules
                .Where(x => x.UserId == userId && x.ProjectId == projectId)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => x.CreatedAt)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            // 统计最近一次聚合后新增的分类数量
            var newClassifiedCount = lastAggregation != default
                ? await db.KnowledgeClassifications
                    .Where(x => x.UserId == userId && x.ProjectId == projectId && x.CreatedAt > lastAggregation)
                    .CountAsync(ct)
                    .ConfigureAwait(false)
                : classifiedCount;

            if (newClassifiedCount >= 3)
            {
                return $"建议：已分类 {classifiedCount} 个知识（最近新增 {newClassifiedCount} 个），建议调用 AggregateDesignRules 聚合为结构化设计规则。";
            }

            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private sealed class AgentProductionStageTimeoutException : Exception
    {
        public AgentProductionStageTimeoutException(string stage, TimeSpan elapsed, TimeSpan timeout)
            : base($"{FormatProductionStageLabel(stage)}阶段超时，已等待 {(int)Math.Round(elapsed.TotalSeconds)} 秒，超过预算 {(int)Math.Round(timeout.TotalSeconds)} 秒。")
        {
            Stage = stage;
            Elapsed = elapsed;
            Timeout = timeout;
        }

        public string Stage { get; }

        public TimeSpan Elapsed { get; }

        public TimeSpan Timeout { get; }
    }
}
