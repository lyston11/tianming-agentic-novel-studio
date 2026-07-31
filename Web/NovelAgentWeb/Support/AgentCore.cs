using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Services.Workspace;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class AgentDecision
{
    public string Mode { get; set; } = "chat";
    public string Intent { get; set; } = "free_chat";
    public bool NeedsRag { get; set; }
    public List<string> RagQueries { get; set; } = new();
    public AgentToolCall? ToolCall { get; set; }
    public string Risk { get; set; } = "Low";
    public bool RequiresConfirmation { get; set; }
    public string AssistantBrief { get; set; } = string.Empty;
    public string DirectReply { get; set; } = string.Empty;
    public IReadOnlyList<string> Suggestions { get; set; } = Array.Empty<string>();
    public double Confidence { get; set; } = 0.5;
    public string Source { get; set; } = "planner";
}

public enum ConversationPhase
{
    Conversation = 0,  // 状态查询、确认、闲聊
    Planning = 1,      // 规划章节、选择候选、构建上下文
    Creation = 2,      // 生成正文、修复草稿
    Review = 3,        // 质量评审、门禁检查
}

public sealed class AgentToolCall
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string> Arguments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public enum AgentActionType
{
    ChatReply,
    Clarify,
    Retrieve,
    ToolCall,
    ConfirmRequest,
    Reflect,
    FinalReply,
}

public sealed class AgentAction
{
    public AgentActionType Type { get; set; } = AgentActionType.ChatReply;
    public string Intent { get; set; } = "free_chat";
    public AgentToolCall? ToolCall { get; set; }
    public string Reply { get; set; } = string.Empty;
    public string Brief { get; set; } = string.Empty;
    public string Risk { get; set; } = "Low";
    public bool RequiresConfirmation { get; set; }
    public List<string> RagQueries { get; set; } = new();
    public IReadOnlyList<string> Suggestions { get; set; } = Array.Empty<string>();
    public double Confidence { get; set; } = 0.5;
    public string Source { get; set; } = "planner";
    // Planner did not produce a tool action; runtime may treat this as degraded/no-action state.
    public bool IsNoTool { get; set; }

    public AgentDecision ToDecision() => new()
    {
        Mode = Type switch
        {
            AgentActionType.Clarify => "clarify",
            AgentActionType.Retrieve => "retrieve",
            AgentActionType.ToolCall => "execute_tool",
            AgentActionType.ConfirmRequest => "execute_tool",
            AgentActionType.FinalReply => "chat",
            _ => "chat",
        },
        Intent = Intent,
        NeedsRag = RagQueries.Count > 0,
        RagQueries = RagQueries.ToList(),
        ToolCall = ToolCall,
        Risk = Risk,
        RequiresConfirmation = RequiresConfirmation,
        AssistantBrief = Brief,
        DirectReply = Reply,
        Suggestions = Suggestions,
        Confidence = Confidence,
        Source = Source,
    };
}

public sealed class AgentPendingConfirmation
{
    public string ConfirmationId { get; set; } = Guid.NewGuid().ToString("N");
    public AgentToolCall? ToolCall { get; set; }
    public string Risk { get; set; } = "High";
    public string ImpactSummary { get; set; } = string.Empty;
    public string RequiresUserInput { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string CandidateId { get; set; } = string.Empty;
    public int CandidateIndex { get; set; }
    public string CandidateTitle { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AgentRuntimeObservation
{
    public int StepIndex { get; set; }
    public string ObservationType { get; set; } = "tool_result";
    public string ToolName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public bool RequiresConfirmation { get; set; }
    public string Risk { get; set; } = "Low";
    public string Message { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public bool IsRepairable { get; set; }
    public string RecommendedToolName { get; set; } = string.Empty;
    public Dictionary<string, string> RecommendedArguments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string MissingPrerequisite { get; set; } = string.Empty;
    public AgentToolArtifact? Artifact { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AgentRuntimeInterruptObservation
{
    public string InterruptId { get; set; } = string.Empty;
    public string RuntimeRunId { get; set; } = string.Empty;
    public string Kind { get; set; } = "freeform";
    public string Message { get; set; } = string.Empty;
    public int Priority { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ConsumedAt { get; set; }
}

public sealed class AgentInterruptDecisionContext
{
    public string UserMessage { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string RuntimeRunId { get; set; } = string.Empty;
    public string RunStatus { get; set; } = string.Empty;
    public string ActiveTool { get; set; } = string.Empty;
    public string CurrentPhase { get; set; } = string.Empty;
    public string LastMessage { get; set; } = string.Empty;
    public string CurrentUserGoal { get; set; } = string.Empty;
    public List<AgentRuntimeInterruptObservation> RecentInterrupts { get; set; } = new();
}

public sealed class ToolTransaction
{
    public string TransactionId { get; set; } = Guid.NewGuid().ToString("N");
    public string TurnId { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public Dictionary<string, string> Arguments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string PolicyResult { get; set; } = string.Empty;
    public string ConfirmationState { get; set; } = "none";
    public string ExecutionState { get; set; } = "planned";
    public AgentToolArtifact? ObservationArtifact { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AgentToolArtifact
{
    public string ArtifactType { get; set; } = string.Empty;
    public string ArtifactId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string OutputKind { get; set; } = AgentToolOutputKind.ProcessArtifact;
    public IReadOnlyList<string> UserVisibleWhere { get; set; } = Array.Empty<string>();
    public string Summary { get; set; } = string.Empty;
    public IReadOnlyList<string> NextHints { get; set; } = Array.Empty<string>();
    public bool VisibleInWorkflow { get; set; } = true;
    public bool VisibleInLibrary { get; set; }
    public string UserVisibleStatus { get; set; } = string.Empty;
}

public static class AgentToolOutputKind
{
    public const string ProcessArtifact = "ProcessArtifact";
    public const string FinalArtifact = "FinalArtifact";
    public const string StateSnapshot = "StateSnapshot";
    public const string KnowledgeEntry = "KnowledgeEntry";
    public const string RuntimeEvent = "RuntimeEvent";
}

public sealed class AgentRuntimeStep
{
    public int StepIndex { get; set; }
    public string Stage { get; set; } = string.Empty;
    public AgentAction? Action { get; set; }
    public AgentRuntimeObservation? Observation { get; set; }
    public AgentReflection? Reflection { get; set; }
    public string StopReason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AgentReflection
{
    public string Summary { get; set; } = string.Empty;
    public bool GoalSatisfied { get; set; }
    public bool ShouldContinue { get; set; }
    public bool RequiresUserInput { get; set; }
    public string NextIntent { get; set; } = string.Empty;
    public string ReplyDraft { get; set; } = string.Empty;
    public List<string> CompletedItems { get; set; } = new();
    public List<string> NewTodoItems { get; set; } = new();
    public List<string> Blockers { get; set; } = new();
    public AgentQualityGateReport QualityGate { get; set; } = new();
    public AgentMissionPatch MissionPatch { get; set; } = new();
}

public sealed class AgentQualityGateReport
{
    public string Status { get; set; } = "not_applicable";
    public AgentQualityScores Scores { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public List<string> Evidence { get; set; } = new();
    public string RewriteDecision { get; set; } = string.Empty;
    public bool RequiresUserInput { get; set; }
    public List<AgentReviewerReport> ReviewReports { get; set; } = new();
    public QualityArbiterDecision ArbiterDecision { get; set; } = new();
}

public sealed class AgentReviewerReport
{
    public string Reviewer { get; set; } = string.Empty;
    public string Status { get; set; } = "not_applicable";
    public int Score { get; set; }
    public List<string> Issues { get; set; } = new();
    public List<string> Evidence { get; set; } = new();
    public string RewriteAdvice { get; set; } = string.Empty;
    public bool Blocking { get; set; }
}

public sealed class QualityArbiterDecision
{
    public string Status { get; set; } = "not_applicable";
    public string Reason { get; set; } = string.Empty;
    public bool RequiresUserInput { get; set; }
    public List<string> BlockingReviewers { get; set; } = new();
}

public sealed class AgentQualityScores
{
    public int Pacing { get; set; }
    public int CharacterMotivation { get; set; }
    public int Conflict { get; set; }
    public int Continuity { get; set; }
    public int Prose { get; set; }
    public int ReaderPromise { get; set; }
}

public sealed class AgentMissionPatch
{
    public string Status { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string CurrentFocus { get; set; } = string.Empty;
    public List<AgentChapterTaskPatch> ChapterPatches { get; set; } = new();
    public AgentMemoryUpdate? MemoryUpdate { get; set; }
}

public sealed class AgentMissionPlan
{
    public string MissionId { get; set; } = Guid.NewGuid().ToString("N");
    public int BlackboardVersion { get; set; } = 2;
    public int DependencyVersion { get; set; }
    public DateTime? LastRecoveredAt { get; set; }
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectTitle { get; set; } = string.Empty;
    public string Status { get; set; } = "idle";
    public string CurrentObjective { get; set; } = string.Empty;
    public string ActiveChapterId { get; set; } = string.Empty;
    public string CurrentRunId { get; set; } = string.Empty;
    public string OverallGoal { get; set; } = string.Empty;
    public string CurrentNovelGoal { get; set; } = string.Empty;
    public string Stage { get; set; } = "idle";
    public List<string> Milestones { get; set; } = new();
    public List<string> TodoQueue { get; set; } = new();
    public List<string> CompletedItems { get; set; } = new();
    public List<string> Blockers { get; set; } = new();
    public List<string> AuthorPreferences { get; set; } = new();
    public List<string> ConfirmedDecisions { get; set; } = new();
    public AgentBookTaskTree BookTaskTree { get; set; } = new();
    public AgentTaskSchedulerState SchedulerState { get; set; } = new();
    public List<AgentDependencyImpactState> DependencyImpacts { get; set; } = new();
    public List<string> RecoveryWarnings { get; set; } = new();
    public string ActiveTurnId { get; set; } = string.Empty;
    public string ActiveToolTransactionId { get; set; } = string.Empty;
    public string ArtifactCursor { get; set; } = string.Empty;
    public string ActiveArtifactCursor { get; set; } = string.Empty;
    public string LastUserVisibleState { get; set; } = string.Empty;
    public string LifecycleStatus { get; set; } = "idle";
    public string LastRecoveredFrom { get; set; } = string.Empty;
    public double RecoveryConfidence { get; set; }
    public string SchedulerLease { get; set; } = string.Empty;
    public List<AgentReviewerReport> ReviewReports { get; set; } = new();
    public QualityArbiterDecision QualityArbiterDecision { get; set; } = new();
    public List<ToolTransaction> ToolTransactions { get; set; } = new();
    public List<string> AllowedNextActions { get; set; } = new();
    public List<AgentToolArtifact> ActiveArtifacts { get; set; } = new();
    public string BlockedReason { get; set; } = string.Empty;
    public string LastVerifiedState { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AgentBookTaskTree
{
    public string BookId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = "planning";
    public string CurrentFocus { get; set; } = string.Empty;
    public AgentFoundationTask Foundation { get; set; } = new();
    public List<AgentVolumeTask> Volumes { get; set; } = new();
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AgentFoundationTask
{
    public string Status { get; set; } = "unstarted";
    public DateTime? ConfirmedAt { get; set; }
    public List<string> Blockers { get; set; } = new();
    public List<string> Decisions { get; set; } = new();
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AgentVolumeTask
{
    public string VolumeId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string Status { get; set; } = "planned";
    public string StartChapterId { get; set; } = string.Empty;
    public string EndChapterId { get; set; } = string.Empty;
    public List<AgentChapterTask> Chapters { get; set; } = new();
    public List<string> Blockers { get; set; } = new();
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AgentChapterTask
{
    public string ChapterId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = "unstarted";
    public string CandidateStatus { get; set; } = "unstarted";
    public string ContextStatus { get; set; } = "pending";
    public string DraftStatus { get; set; } = "none";
    public string GateStatus { get; set; } = "pending";
    public string QualityStatus { get; set; } = "not_reviewed";
    public AgentQualityScores QualityScores { get; set; } = new();
    public int RepairAttemptCount { get; set; }
    public string CommitStatus { get; set; } = "pending";
    public string DependencyStatus { get; set; } = "clean";
    public bool RequiresContextRebuild { get; set; }
    public bool RequiresRevalidation { get; set; }
    public string RunId { get; set; } = string.Empty;
    public string GateIssueSummary { get; set; } = string.Empty;
    public string QualityIssueSummary { get; set; } = string.Empty;
    public string ArtifactStatus { get; set; } = "none";
    public string DraftArtifactId { get; set; } = string.Empty;
    public string GateReportId { get; set; } = string.Empty;
    public string QualityReportId { get; set; } = string.Empty;
    public string CommitConfirmationId { get; set; } = string.Empty;
    public string UserVisibleStatus { get; set; } = "待规划";
    public string NextAction { get; set; } = string.Empty;
    public List<string> AllowedNextActions { get; set; } = new();
    public List<string> LastArtifactIds { get; set; } = new();
    public string LastTransitionReason { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AgentChapterTaskPatch
{
    public string ChapterId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string GateStatus { get; set; } = string.Empty;
    public string QualityIssueSummary { get; set; } = string.Empty;
    public string NextAction { get; set; } = string.Empty;
}

public sealed class AgentTaskSchedulerState
{
    public List<AgentScheduledTask> Tasks { get; set; } = new();
    public string ActiveTaskId { get; set; } = string.Empty;
    public string ActiveRunId { get; set; } = string.Empty;
    public string ActiveChapterId { get; set; } = string.Empty;
    public string LastDecisionReason { get; set; } = string.Empty;
    public string LeaseOwner { get; set; } = string.Empty;
    public DateTime? LeaseExpiresAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AgentScheduledTask
{
    public string TaskId { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectTitle { get; set; } = string.Empty;
    public string ChapterId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public string Status { get; set; } = "queued";
    public string NextAction { get; set; } = string.Empty;
    public string BlockedReason { get; set; } = string.Empty;
    public string Risk { get; set; } = "Low";
    public bool RequiresConfirmation { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AgentDependencyImpactState
{
    public string ImpactId { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceRunId { get; set; } = string.Empty;
    public string Status { get; set; } = "clean";
    public List<string> ChangedModules { get; set; } = new();
    public List<string> ImpactedModules { get; set; } = new();
    public List<string> ImpactedChapters { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AgentDecisionTrace
{
    public string Mode { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public bool NeedsRag { get; set; }
    public IReadOnlyList<string> RagQueries { get; set; } = Array.Empty<string>();
    public string ToolName { get; set; } = string.Empty;
    public string Risk { get; set; } = "Low";
    public bool RequiresConfirmation { get; set; }
    public string AssistantBrief { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;

    public static AgentDecisionTrace From(AgentDecision decision) => new()
    {
        Mode = decision.Mode,
        Intent = decision.Intent,
        NeedsRag = decision.NeedsRag,
        RagQueries = decision.RagQueries,
        ToolName = decision.ToolCall?.Name ?? string.Empty,
        Risk = decision.Risk,
        RequiresConfirmation = decision.RequiresConfirmation,
        AssistantBrief = decision.AssistantBrief,
        Source = decision.Source,
    };
}

public sealed class UserProfile
{
    [System.Text.Json.Serialization.JsonPropertyName("userId")]
    public string UserId { get; set; } = "default";

    [System.Text.Json.Serialization.JsonPropertyName("stylePreferences")]
    public Dictionary<string, string> StylePreferences { get; set; } = new();

    [System.Text.Json.Serialization.JsonPropertyName("genreHabits")]
    public Dictionary<string, int> GenreHabits { get; set; } = new();

    [System.Text.Json.Serialization.JsonPropertyName("confirmationTolerance")]
    public string ConfirmationTolerance { get; set; } = "medium";

    [System.Text.Json.Serialization.JsonPropertyName("globalConstraints")]
    public List<string> GlobalConstraints { get; set; } = new();
}

public sealed class SessionContext
{
    [System.Text.Json.Serialization.JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");

    [System.Text.Json.Serialization.JsonPropertyName("activeProjectId")]
    public string? ActiveProjectId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("chatHistory")]
    public List<AgentConversationTurn> ChatHistory { get; set; } = new();

    [System.Text.Json.Serialization.JsonPropertyName("currentGoal")]
    public string CurrentGoal { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("openQuestions")]
    public List<string> OpenQuestions { get; set; } = new();

    [System.Text.Json.Serialization.JsonPropertyName("recentObservations")]
    public List<AgentRuntimeObservation> RecentObservations { get; set; } = new();

    [System.Text.Json.Serialization.JsonPropertyName("pendingToolCall")]
    public AgentToolCall? PendingToolCall { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("pendingConfirmation")]
    public AgentPendingConfirmation? PendingConfirmation { get; set; }
}

public sealed class AgentRuntimeContext
{
    [System.Text.Json.Serialization.JsonPropertyName("user")]
    public UserProfile User { get; set; } = new();

    // NovelProjectInfo will be added in Task 6
    [System.Text.Json.Serialization.JsonPropertyName("activeProject")]
    public object? ActiveProject { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("session")]
    public SessionContext Session { get; set; } = new();

    [System.Text.Json.Serialization.JsonPropertyName("mission")]
    public AgentMissionState Mission { get; set; } = new();

    [System.Text.Json.Serialization.JsonPropertyName("missionPlan")]
    public AgentMissionPlan MissionPlan { get; set; } = new();
}

public sealed class AgentWorkingMemory
{
    public string CurrentGoal { get; set; } = string.Empty;
    public List<string> OpenQuestions { get; set; } = new();
    public List<string> UserPreferences { get; set; } = new();
    public List<AgentRuntimeObservation> RecentObservations { get; set; } = new();
    public List<AgentRuntimeInterruptObservation> RuntimeInterrupts { get; set; } = new();
    public AgentToolCall? PendingToolCall { get; set; }
    public AgentPendingConfirmation? PendingConfirmation { get; set; }
    public AgentDecision? LastDecision { get; set; }
    public AgentMissionState Mission { get; set; } = new();
    public AgentMissionPlan MissionPlan { get; set; } = new();
    public AgentSessionMemory SessionMemory { get; set; } = new();
    public AgentProjectMemory ProjectMemory { get; set; } = new();
    public AgentAuthorMemory AuthorMemory { get; set; } = new();
    public AgentExecutionMemory ExecutionMemory { get; set; } = new();

    /// <summary>
    /// 永不压缩的工具执行历史摘要。
    /// 用于在 ChatHistory 被压缩后，LLM 仍能看到所有工具调用的上下文。
    /// </summary>
    public List<ToolExecutionSnapshot> ToolExecutionHistory { get; set; } = new();
}

public sealed class AgentSessionMemory
{
    public string CurrentGoal { get; set; } = string.Empty;
    public List<string> OpenQuestions { get; set; } = new();
    public string ChatSummary { get; set; } = string.Empty;
    public List<string> ShortTermPreferences { get; set; } = new();
    public List<string> RecentObservations { get; set; } = new();
    public List<string> LastObservations { get; set; } = new();
    public string? PendingToolName { get; set; }
    public string? LastIntent { get; set; }
}

public sealed class AgentProjectMemory
{
    public string ProjectId { get; set; } = string.Empty;
    public string LongTermGoal { get; set; } = string.Empty;
    public string ReaderPromise { get; set; } = string.Empty;
    public string Tone { get; set; } = string.Empty;
    public List<string> Constraints { get; set; } = new();
    // UnresolvedThreads removed - Agent should query StoryBible.ForeshadowLedger directly
    public List<string> ReferencedKnowledgeIds { get; set; } = new();
    public List<string> ImportedKnowledgeIds { get; set; } = new();
    public List<KnowledgeInventoryItem> KnowledgeInventory { get; set; } = new();
    public List<string> UsedTropePatterns { get; set; } = new();
}

public sealed class AgentAuthorMemory
{
    public string DisplayName { get; set; } = string.Empty;
    public List<string> StyleLikes { get; set; } = new();
    public List<string> StyleDislikes { get; set; } = new();
    public string ConfirmationTolerance { get; set; } = "key_checkpoints";
    public List<string> GenreHabits { get; set; } = new();
    public List<string> FavoriteKnowledgeIds { get; set; } = new();
}

public sealed class AgentExecutionMemory
{
    public List<string> ToolFailurePatterns { get; set; } = new();
    public List<string> RepeatedBlockers { get; set; } = new();
    public List<string> SuccessfulRepairNotes { get; set; } = new();
    public List<string> KnowledgeProcessingFailures { get; set; } = new();
}

/// <summary>
/// 工具执行历史快照 - 永久保留，不被 ChatHistory 压缩影响。
/// 用于让 LLM 始终能看到本次会话中所有工具调用的关键信息。
/// </summary>
public sealed class ToolExecutionSnapshot
{
    public int StepIndex { get; set; }
    public string RuntimeRunId { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public Dictionary<string, string> Arguments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Success { get; set; }
    public string ResultSummary { get; set; } = string.Empty;
    public string ArtifactType { get; set; } = string.Empty;
    public string ArtifactId { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AgentMissionState
{
    public string CurrentGoal { get; set; } = string.Empty;
    public string CreativePhase { get; set; } = "idle";
    public string Readiness { get; set; } = "unknown";
    public string NextIntent { get; set; } = string.Empty;
    public string PendingUserDecision { get; set; } = string.Empty;
    public Dictionary<string, string> FoundationBrief { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> MissingFoundationFields { get; set; } = new();
    public string PendingQuestion { get; set; } = string.Empty;
    public AgentMissionPlan? MissionPlan { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AgentWorkingMemorySnapshot
{
    public string CurrentGoal { get; set; } = string.Empty;
    public IReadOnlyList<string> OpenQuestions { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> UserPreferences { get; set; } = Array.Empty<string>();
    public IReadOnlyList<AgentRuntimeObservation> RecentObservations { get; set; } = Array.Empty<AgentRuntimeObservation>();
    public IReadOnlyList<AgentRuntimeInterruptObservation> RuntimeInterrupts { get; set; } = Array.Empty<AgentRuntimeInterruptObservation>();
    public string PendingToolName { get; set; } = string.Empty;
    public string LastIntent { get; set; } = string.Empty;
    public string LastMode { get; set; } = string.Empty;
    public AgentMissionStateSnapshot Mission { get; set; } = new();
    public AgentMissionPlan MissionPlan { get; set; } = new();
    public AgentPendingConfirmation? PendingConfirmation { get; set; }
    public AgentSessionMemory SessionMemory { get; set; } = new();
    public AgentProjectMemory ProjectMemory { get; set; } = new();
    public AgentAuthorMemory AuthorMemory { get; set; } = new();
    public AgentExecutionMemory ExecutionMemory { get; set; } = new();

    public static AgentWorkingMemorySnapshot From(AgentWorkingMemory memory) => new()
    {
        CurrentGoal = memory.CurrentGoal,
        OpenQuestions = memory.OpenQuestions.TakeLast(6).ToArray(),
        UserPreferences = memory.UserPreferences.TakeLast(8).ToArray(),
        RecentObservations = memory.RecentObservations.TakeLast(6).ToArray(),
        RuntimeInterrupts = memory.RuntimeInterrupts.TakeLast(6).ToArray(),
        PendingToolName = memory.PendingToolCall?.Name ?? string.Empty,
        LastIntent = memory.LastDecision?.Intent ?? string.Empty,
        LastMode = memory.LastDecision?.Mode ?? string.Empty,
        Mission = AgentMissionStateSnapshot.From(memory.Mission ?? new AgentMissionState()),
        MissionPlan = memory.MissionPlan ?? new AgentMissionPlan(),
        PendingConfirmation = null,
        SessionMemory = memory.SessionMemory ?? new AgentSessionMemory(),
        ProjectMemory = memory.ProjectMemory ?? new AgentProjectMemory(),
        AuthorMemory = memory.AuthorMemory ?? new AgentAuthorMemory(),
        ExecutionMemory = memory.ExecutionMemory ?? new AgentExecutionMemory(),
    };
}

public sealed class AgentMissionStateSnapshot
{
    public string CurrentGoal { get; set; } = string.Empty;
    public string CreativePhase { get; set; } = "idle";
    public string Readiness { get; set; } = "unknown";
    public string NextIntent { get; set; } = string.Empty;
    public string PendingUserDecision { get; set; } = string.Empty;
    public IReadOnlyDictionary<string, string> FoundationBrief { get; set; } = new Dictionary<string, string>();
    public IReadOnlyList<string> MissingFoundationFields { get; set; } = Array.Empty<string>();
    public string PendingQuestion { get; set; } = string.Empty;

    public static AgentMissionStateSnapshot From(AgentMissionState mission) => new()
    {
        CurrentGoal = mission.CurrentGoal,
        CreativePhase = mission.CreativePhase,
        Readiness = mission.Readiness,
        NextIntent = mission.NextIntent,
        PendingUserDecision = mission.PendingUserDecision,
        FoundationBrief = new Dictionary<string, string>(mission.FoundationBrief ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase),
        MissingFoundationFields = (mission.MissingFoundationFields ?? new List<string>()).ToArray(),
        PendingQuestion = mission.PendingQuestion,
    };
}

public sealed class AgentRagContext
{
    public bool Used { get; set; }
    public AgentRagStrategy Strategy { get; set; } = new();
    public List<string> Queries { get; set; } = new();
    public List<string> KnowledgeNotes { get; set; } = new();
    public List<string> ProjectNotes { get; set; } = new();
    public Dictionary<string, List<string>> ResultsByBucket { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AgentRagStrategy
{
    public string Stage { get; set; } = "free_chat";
    public List<AgentRagQueryPlan> QueryPlan { get; set; } = new();
}

public sealed class AgentRagQueryPlan
{
    public string Bucket { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class AgentToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Risk { get; set; } = "Low";
    public bool RequiresConfirmation { get; set; }
    public List<string> Arguments { get; set; } = new();
    public Dictionary<string, AgentToolParameterSpec> ParameterSpecs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public AgentToolSideEffectSpec SideEffects { get; set; } = new();
    public AgentToolSemanticSpec Semantic { get; set; } = new();
}

public sealed class AgentToolParameterSpec
{
    public string Type { get; set; } = "string";
    public string Description { get; set; } = "Structured tool argument.";
    public bool Required { get; set; }
    public List<string> Enum { get; set; } = new();
    public int? Minimum { get; set; }
    public int? Maximum { get; set; }
}

public sealed class AgentToolSemanticSpec
{
    public string DisplayName { get; set; } = string.Empty;
    public string DomainSurface { get; set; } = string.Empty;
    public string OutputKind { get; set; } = string.Empty;
    public string SideEffectLevel { get; set; } = string.Empty;
    public string ImpactScope { get; set; } = string.Empty;
    public string FailureContract { get; set; } = string.Empty;
    public bool RequiresProject { get; set; }
    public bool SupportsNoProjectSession { get; set; }
    public string AverageDuration { get; set; } = string.Empty;
    public List<string> ProgressEventContract { get; set; } = new();
    public List<string> NextPossibleTools { get; set; } = new();
    public List<string> ReadsFrom { get; set; } = new();
    public List<string> WritesTo { get; set; } = new();
    public List<string> InputArtifacts { get; set; } = new();
    public List<string> OutputArtifacts { get; set; } = new();
    public string IdempotencyPolicy { get; set; } = string.Empty;
    public string RollbackPolicy { get; set; } = string.Empty;
    public string UserVisibleWhere { get; set; } = string.Empty;
    public string ResultSemantics { get; set; } = string.Empty;

    /// <summary>
    /// 工具去重策略:
    /// - "strict": 同名同参数禁止重复（默认）
    /// - "per_turn": 允许同名不同参数（适用于 SearchKnowledge 等检索类工具）
    /// - "none": 完全不去重（谨慎使用，仅用于幂等只读工具）
    /// </summary>
    public string DeduplicationPolicy { get; set; } = "strict";
}

public sealed class AgentToolSideEffectSpec
{
    public bool WritesLedger { get; set; } = true;
    public bool WritesRedisRecentCache { get; set; } = true;
    public bool WritesToolSearchCache { get; set; }
    public bool WritesSqliteSnapshot { get; set; }
    public bool BusinessReadOnly { get; set; }
    public List<string> WritesMemoryScopes { get; set; } = new();
    public List<string> ReadsSqliteEntities { get; set; } = new();
    public List<string> WritesSqliteEntities { get; set; } = new();
    public List<string> WritesVectorIndexes { get; set; } = new();
}

public sealed class ToolSchema
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Risk { get; set; } = "Low";
    public bool RequiresConfirmation { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, AgentToolParameterSpec> ParameterSpecs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public AgentToolSideEffectSpec SideEffects { get; set; } = new();
    public AgentToolSemanticSpec Semantic { get; set; } = new();
}

public sealed class AgentToolEntry
{
    public AgentToolDefinition Definition { get; set; } = new();
    public string Category { get; set; } = string.Empty;
    public IReadOnlyList<string> Preconditions { get; set; } = Array.Empty<string>();
    public Func<AgentToolCall, AgentSession, StoryBibleDocument, bool, CancellationToken, Task<AgentToolExecutionResult>> Handler { get; set; } =
        (_, _, _, _, _) => Task.FromResult(new AgentToolExecutionResult
        {
            Success = false,
            Message = "工具未绑定执行器。",
        });
}

public sealed class AgentToolExecutionResult
{
    public bool Success { get; set; }
    public bool RequiresConfirmation { get; set; }
    public string Risk { get; set; } = "Low";
    public string Message { get; set; } = string.Empty;
    public string? RunId { get; set; }
    public string Phase { get; set; } = string.Empty;
    public bool IsRepairable { get; set; }
    public string RecommendedToolName { get; set; } = string.Empty;
    public Dictionary<string, string> RecommendedArguments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string MissingPrerequisite { get; set; } = string.Empty;
    public AgentToolFailure? Failure { get; set; }
    public object? Data { get; set; }
    public AgentToolArtifact? Artifact { get; set; }
    public IReadOnlyList<string> Suggestions { get; set; } = Array.Empty<string>();
}

public sealed class KnowledgeProcessingToolResult
{
    public string TaskId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
    public int Progress { get; set; }
    public int ExtractedEntriesCount { get; set; }
    public IReadOnlyList<string> KnowledgeIds { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> ImportedKnowledgeIds { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> ReferencedKnowledgeIds { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> NextRecommendedTools { get; set; } = Array.Empty<string>();
}

public sealed class KnowledgeClassificationToolResult
{
    public string ClassificationId { get; set; } = string.Empty;
    public string KnowledgeId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public int Priority { get; set; }
    public string ConstraintLevel { get; set; } = string.Empty;
    public string PackagePolicy { get; set; } = string.Empty;
    public IReadOnlyList<string> TargetEntities { get; set; } = Array.Empty<string>();
    public string Rule { get; set; } = string.Empty;
    public bool ShouldEnterGate { get; set; }
    public bool ShouldEnterBlueprint { get; set; }
    public bool ShouldEnterFactSnapshot { get; set; }
    public double Confidence { get; set; }
    public IReadOnlyList<string> NextRecommendedTools { get; set; } = Array.Empty<string>();
}

public sealed class KnowledgeConflictDetectionToolResult
{
    public string ReportId { get; set; } = string.Empty;
    public string KnowledgeId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public bool HasConflict { get; set; }
    public string ConflictType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string ImpactScope { get; set; } = string.Empty;
    public IReadOnlyList<string> ConflictingKnowledgeIds { get; set; } = Array.Empty<string>();
    public string Explanation { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public bool RequiresUserDecision { get; set; }
    public bool BlocksProduceChapter { get; set; }
    public IReadOnlyList<string> NextRecommendedTools { get; set; } = Array.Empty<string>();
}

public sealed class AgentToolFailure
{
    public string Code { get; set; } = string.Empty;
    public string FailedStage { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public bool Recoverable { get; set; }
    public string RecommendedAction { get; set; } = string.Empty;
    public IReadOnlyList<string> ArtifactIds { get; set; } = Array.Empty<string>();
    public IReadOnlyList<AgentToolProducedArtifact> ProducedArtifacts { get; set; } = Array.Empty<AgentToolProducedArtifact>();
    public IReadOnlyList<string> RecoverableActions { get; set; } = Array.Empty<string>();
    public bool RequiresUserDecision { get; set; }
}

public sealed class AgentToolProducedArtifact
{
    public string ArtifactType { get; set; } = string.Empty;
    public string ArtifactId { get; set; } = string.Empty;
    public string OutputKind { get; set; } = AgentToolOutputKind.ProcessArtifact;
    public IReadOnlyList<string> UserVisibleWhere { get; set; } = Array.Empty<string>();
    public string Summary { get; set; } = string.Empty;
}

public static class AgentRunSelector
{
    public static NovelAgentRun? SelectCurrentRun(StoryBibleDocument bible)
    {
        var actionable = bible.AgentRuns
            .Where(run => IsActionableAwaitingRun(bible, run))
            .OrderByDescending(run => run.UpdatedAt)
            .FirstOrDefault();
        if (actionable != null)
            return actionable;

        // Only return non-terminal runs (exclude Completed, Failed, Cancelled)
        return bible.AgentRuns
            .Where(run => run.Status is not (NovelAgentRunStatus.Completed or NovelAgentRunStatus.Failed or NovelAgentRunStatus.Cancelled))
            .Where(run => !IsStaleFoundationAwaitingRun(bible, run))
            .OrderByDescending(run => run.UpdatedAt)
            .FirstOrDefault();
    }

    public static bool IsActionableAwaitingRun(StoryBibleDocument bible, NovelAgentRun run)
    {
        if (run.Status != NovelAgentRunStatus.AwaitingConfirmation)
            return false;

        return run.Intent switch
        {
            NovelAgentIntent.CreateStoryFoundation => bible.Constitution == null && run.MacroCandidates.Count > 0,
            NovelAgentIntent.PlanVolumeArc => run.VolumeArcPlan != null,
            NovelAgentIntent.PlanChapter => run.ChapterBrief?.Candidates.Count > 0,
            _ => false,
        };
    }

    private static bool IsStaleFoundationAwaitingRun(StoryBibleDocument bible, NovelAgentRun run) =>
        bible.Constitution != null &&
        run.Intent == NovelAgentIntent.CreateStoryFoundation &&
        run.Status == NovelAgentRunStatus.AwaitingConfirmation;
}

public static class AgentProjectSummaryBuilder
{
    public static string Build(StoryBibleDocument bible)
    {
        var constitution = bible.Constitution == null
            ? "Story Bible 尚未固化"
            : $"Story Bible 已固化：{bible.Constitution.Genre}/{bible.Constitution.SubGenre}；核心钩子={bible.Constitution.CoreHook}；主冲突={bible.Constitution.MainConflictEngine}";

        var currentRun = AgentRunSelector.SelectCurrentRun(bible);
        var run = currentRun == null ? "无 Agent Run" : $"当前 Run={currentRun.Intent}/{currentRun.Status}/{currentRun.RunId}";

        return $"{constitution}；卷={bible.VolumeArcs.Count}；角色账本={bible.CharacterLedger.Count}；伏笔={bible.ForeshadowLedger.Count}；设定={bible.CanonLedger.Count}；{run}";
    }
}
