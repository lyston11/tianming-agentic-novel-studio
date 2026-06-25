using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Services.AgentTools;
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

public enum DialogueAct
{
    Chat,
    SelectCandidate,
}

public sealed class UserTurnEnvelope
{
    public string TurnId { get; set; } = Guid.NewGuid().ToString("N");
    public TurnIntent Intent { get; set; } = new();
    public DialogueAct DialogueAct { get; set; } = DialogueAct.Chat;
    public string TargetArtifact { get; set; } = string.Empty;
    public string ReferencedTask { get; set; } = string.Empty;
    public string ConfirmationDecision { get; set; } = string.Empty;
    public string CreativeBrief { get; set; } = string.Empty;
    public string FeedbackPatch { get; set; } = string.Empty;
    public string StatusQueryScope { get; set; } = string.Empty;
    public string SelectedOption { get; set; } = string.Empty;
    public int? SelectedOptionIndex { get; set; }
    public string SelectionKind { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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
    public TurnIntent? TurnIntent { get; set; }
    public UserTurnEnvelope? InteractionState { get; set; }
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

public sealed class AgentObservationContext
{
    public string UserMessage { get; set; } = string.Empty;
    public AgentProductSpaceMap ProductSpace { get; set; } = AgentProductSpaceCatalog.Create();
    public AgentWorkspaceState? WorkspaceState { get; set; }
    public TurnIntent TurnIntent { get; set; } = new();
    public UserTurnEnvelope UserTurn { get; set; } = new();
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectTitle { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public string ActiveRunId { get; set; } = string.Empty;
    public string ProjectSummary { get; set; } = string.Empty;
    public List<string> RecentMessages { get; set; } = new();
    public List<AgentRuntimeObservation> RecentObservations { get; set; } = new();
    public List<AgentRuntimeInterruptObservation> RuntimeInterrupts { get; set; } = new();
    public AgentRagContext Rag { get; set; } = new();
    public AgentMissionPlan MissionPlan { get; set; } = new();
    public AgentMissionState MissionState { get; set; } = new();
    public AgentSessionMemory SessionMemory { get; set; } = new();
    public AgentProjectMemory ProjectMemory { get; set; } = new();
    public AgentAuthorMemory AuthorMemory { get; set; } = new();
    public AgentExecutionMemory ExecutionMemory { get; set; } = new();
    public AgentPendingConfirmation? PendingConfirmation { get; set; }
    public List<AgentToolDefinition> AvailableTools { get; set; } = new();
    public string AnchorPrompt { get; set; } = string.Empty;
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
    public AgentToolSideEffectSpec SideEffects { get; set; } = new();
    public AgentToolSemanticSpec Semantic { get; set; } = new();
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
    public IReadOnlyList<ToolInputArtifactState> InputArtifacts { get; set; } = Array.Empty<ToolInputArtifactState>();
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

public sealed class AgentObservationBuilder
{
    private static readonly AsyncLocal<NovelAgentWorkspace?> _currentWorkspace = new();
    private NovelAgentWorkspace _workspace => _currentWorkspace.Value ?? throw new InvalidOperationException("Workspace not set for current request");

    private readonly AgentToolRegistry _toolRegistry;
    private readonly IAgentMemoryContextService _memoryContextService;
    private readonly IToolSearchCacheService _toolSearchCache;
    private readonly AgentMissionTaskTreeService _taskTreeService;

    internal static void SetWorkspace(NovelAgentWorkspace workspace) => _currentWorkspace.Value = workspace;
    internal static void ClearWorkspace() => _currentWorkspace.Value = null;

    public AgentObservationBuilder(
        AgentToolRegistry toolRegistry,
        IAgentMemoryContextService memoryContextService,
        IToolSearchCacheService toolSearchCache,
        AgentMissionTaskTreeService taskTreeService)
    {
        _toolRegistry = toolRegistry;
        _memoryContextService = memoryContextService;
        _toolSearchCache = toolSearchCache;
        _taskTreeService = taskTreeService;
    }

    public async Task<AgentObservationContext> BuildAsync(
        AgentSession session,
        NovelProjectInfo project,
        StoryBibleDocument bible,
        string userMessage,
        TurnIntent? turnIntent,
        CancellationToken ct)
    {
        turnIntent ??= new TurnIntent { RawMessage = userMessage };
        var envelope = session.WorkingMemory.MissionPlan.InteractionState;
        if (envelope?.Intent == null ||
            !string.Equals(envelope.Intent.RawMessage, turnIntent.RawMessage, StringComparison.Ordinal))
        {
            envelope = new UserTurnEnvelope { Intent = turnIntent, CreativeBrief = turnIntent.CreativeBrief };
            session.WorkingMemory.MissionPlan.InteractionState = envelope;
        }
        EnsureMissionPlan(session, project, bible, userMessage, turnIntent);
        var memoryContext = await _memoryContextService
            .BuildAsync(session.UserId, project.Id, session.SessionId, ct, runId: session.RuntimeRunId)
            .ConfigureAwait(false);
        ApplyMemoryContext(session, project.Id, memoryContext);
        _taskTreeService.Sync(session, project, bible);
        var rag = await BuildRagAsync(session, bible, userMessage, ct).ConfigureAwait(false);

        var allToolSchemas = _toolRegistry.ListToolSchemas();
        var toolCatalogSignature = ToolCatalogSignature.Compute(allToolSchemas);
        var toolCacheScope = string.IsNullOrWhiteSpace(session.DiscoveredPhase)
            ? "global"
            : session.DiscoveredPhase;
        var availableToolLookup = await _toolSearchCache.GetAsync(session, toolCacheScope, toolCatalogSignature, ct).ConfigureAwait(false);
        var availableTools = availableToolLookup.Tools;

        if (availableTools == null)
        {
            availableTools = allToolSchemas;
        }

        var workspaceState = await BuildWorkspaceStateAsync(session, ct).ConfigureAwait(false);

        return new AgentObservationContext
        {
            UserMessage = userMessage,
            TurnIntent = turnIntent,
            UserTurn = envelope,
            ProjectId = project.Id,
            ProjectTitle = project.Title,
            Phase = session.Phase,
            ActiveRunId = session.ActiveRunId ?? string.Empty,
            ProjectSummary = AgentProjectSummaryBuilder.Build(bible),
            RecentMessages = memoryContext.Chat.RecentMessages
                .Select(t => $"{t.Role}: {t.Content}")
                .ToList(),
            RecentObservations = session.WorkingMemory.RecentObservations.TakeLast(8).ToList(),
            RuntimeInterrupts = session.WorkingMemory.RuntimeInterrupts.TakeLast(8).ToList(),
            Rag = rag,
            MissionPlan = session.WorkingMemory.MissionPlan,
            MissionState = session.WorkingMemory.Mission,
            SessionMemory = session.WorkingMemory.SessionMemory,
            ProjectMemory = session.WorkingMemory.ProjectMemory,
            AuthorMemory = session.WorkingMemory.AuthorMemory,
            ExecutionMemory = session.WorkingMemory.ExecutionMemory,
            PendingConfirmation = null,
            AvailableTools = availableTools.Select(t => new AgentToolDefinition
            {
                Name = t.Name,
                Description = t.Description,
                Risk = t.Risk,
                RequiresConfirmation = t.RequiresConfirmation,
                Arguments = t.Parameters.Keys.ToList(),
                SideEffects = t.SideEffects,
                Semantic = t.Semantic,
            }).ToList(),
            ProductSpace = AgentProductSpaceCatalog.Create(),
            WorkspaceState = workspaceState,
        };
    }

    private async Task<AgentWorkspaceState> BuildWorkspaceStateAsync(AgentSession session, CancellationToken ct)
    {
        try
        {
            using var scope = _workspace.ScopeFactory.CreateScope();
            var queryService = scope.ServiceProvider.GetService<IWorkspaceStateQueryService>();
            if (queryService == null)
                return AgentWorkspaceState.Hint(session);

            return await queryService.QueryAsync(
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
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            return AgentWorkspaceState.Hint(session);
        }
    }

    private static void ApplyMemoryContext(AgentSession session, string projectId, AgentMemoryContextDto memoryContext)
    {
        session.WorkingMemory.SessionMemory = MapSession(memoryContext.Session, memoryContext.Chat);
        session.WorkingMemory.ProjectMemory = MapProject(memoryContext.Project, projectId);
        session.WorkingMemory.AuthorMemory = MapAuthor(memoryContext.Author);
        session.WorkingMemory.ExecutionMemory = MapExecution(memoryContext.Execution);
    }

    private static AgentSessionMemory MapSession(SessionMemory source, ChatMemoryContext chat) => new()
    {
        CurrentGoal = source.CurrentGoal,
        OpenQuestions = new List<string>(source.OpenQuestions),
        ChatSummary = chat.MetaSummary ?? string.Empty,
        ShortTermPreferences = new List<string>(source.ShortTermPreferences),
        RecentObservations = new List<string>(source.RecentObservations),
        LastObservations = new List<string>(source.RecentObservations),
        PendingToolName = source.PendingToolName,
        LastIntent = source.LastIntent
    };

    private static AgentProjectMemory MapProject(ProjectMemory source, string projectId) => new()
    {
        ProjectId = projectId,
        LongTermGoal = source.LongTermGoal ?? string.Empty,
        ReaderPromise = source.ReaderPromise ?? string.Empty,
        Constraints = new List<string>(source.Constraints),
        // UnresolvedThreads removed
        ReferencedKnowledgeIds = new List<string>(source.ReferencedKnowledgeIds),
        ImportedKnowledgeIds = new List<string>(source.ImportedKnowledgeIds),
        KnowledgeInventory = new List<KnowledgeInventoryItem>(source.KnowledgeInventory),
        UsedTropePatterns = new List<string>(source.UsedTropePatterns)
    };

    private static AgentAuthorMemory MapAuthor(AuthorMemory source) => new()
    {
        DisplayName = source.DisplayName ?? string.Empty,
        StyleLikes = new List<string>(source.StyleLikes),
        StyleDislikes = new List<string>(source.StyleDislikes),
        ConfirmationTolerance = source.ConfirmationTolerance ?? "key_checkpoints",
        GenreHabits = new List<string>(source.GenreHabits),
        FavoriteKnowledgeIds = new List<string>(source.FavoriteKnowledgeIds)
    };

    private static AgentExecutionMemory MapExecution(ExecutionMemory source) => new()
    {
        ToolFailurePatterns = new List<string>(source.ToolFailurePatterns),
        RepeatedBlockers = new List<string>(source.RepeatedBlockers),
        SuccessfulRepairNotes = new List<string>(source.SuccessfulRepairNotes),
        KnowledgeProcessingFailures = new List<string>(source.KnowledgeProcessingFailures)
    };

    private async Task<AgentRagContext> BuildRagAsync(
        AgentSession session,
        StoryBibleDocument bible,
        string userMessage,
        CancellationToken ct)
    {
        var strategy = BuildRagStrategy(session, bible, userMessage);
        var queries = strategy.QueryPlan.Select(q => q.Query).Where(q => !string.IsNullOrWhiteSpace(q)).Distinct().Take(8).ToList();
        var context = new AgentRagContext
        {
            Used = queries.Count > 0,
            Strategy = strategy,
            Queries = queries,
        };
        AddBucket(context, "storyBible", BuildProjectNote(bible, session));
        AddBucket(context, "authorPreferences", string.Join("；", session.WorkingMemory.AuthorMemory.StyleLikes.Concat(session.WorkingMemory.AuthorMemory.StyleDislikes).Take(8)));
        AddBucket(context, "projectMemory", FirstNonEmpty(session.WorkingMemory.ProjectMemory.LongTermGoal, session.WorkingMemory.ProjectMemory.ReaderPromise));
        foreach (var item in strategy.QueryPlan)
        {
            var result = await _workspace.Orchestrator.RetrieveCreativeKnowledgeAsync(item.Query, ct).ConfigureAwait(false);
            var notes = result.Hits
                .Take(4)
                .Select(h => $"{h.Entry.Category}/{h.Entry.Title}: {h.Entry.Content}")
                .ToList();
            foreach (var note in notes)
                AddBucket(context, item.Bucket, note);
            context.KnowledgeNotes.AddRange(notes);
        }
        context.KnowledgeNotes = context.KnowledgeNotes.Distinct().Take(12).ToList();
        context.ProjectNotes = context.ResultsByBucket
            .Where(p => p.Key is "storyBible" or "projectMemory" or "authorPreferences")
            .SelectMany(p => p.Value)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct()
            .Take(12)
            .ToList();
        return context;
    }

    private static void EnsureMissionPlan(AgentSession session, NovelProjectInfo project, StoryBibleDocument bible, string userMessage)
    {
        EnsureMissionPlan(session, project, bible, userMessage, new TurnIntent { RawMessage = userMessage });
    }

    private static void EnsureMissionPlan(AgentSession session, NovelProjectInfo project, StoryBibleDocument bible, string userMessage, TurnIntent turnIntent)
    {
        var plan = session.WorkingMemory.MissionPlan ??= new AgentMissionPlan();
        if (string.IsNullOrWhiteSpace(plan.MissionId))
            plan.MissionId = Guid.NewGuid().ToString("N");
        plan.ProjectId = project.Id;
        plan.ProjectTitle = project.Title;
        plan.CurrentRunId = session.ActiveRunId ?? AgentRunSelector.SelectCurrentRun(bible)?.RunId ?? string.Empty;
        var currentRun = AgentRunSelector.SelectCurrentRun(bible);
        if (string.IsNullOrWhiteSpace(plan.OverallGoal))
            plan.OverallGoal = FirstNonEmpty(session.WorkingMemory.CurrentGoal, currentRun?.UserGoal);
        if (string.IsNullOrWhiteSpace(plan.CurrentNovelGoal))
            plan.CurrentNovelGoal = session.WorkingMemory.CurrentGoal;
        if (string.IsNullOrWhiteSpace(plan.CurrentNovelGoal))
            plan.CurrentNovelGoal = FirstNonEmpty(plan.OverallGoal, currentRun?.UserGoal);
        plan.Stage = bible.Constitution == null
            ? "foundation"
            : bible.VolumeArcs.Count == 0 ? "volume_planning" : "chapter_work";
        plan.Status = plan.Blockers.Count > 0 ? "blocked" : plan.Stage;
        if (plan.Milestones.Count == 0)
            plan.Milestones.AddRange(new[] { "故事地基", "卷规划", "章节规划", "正文生成", "复盘沉淀" });
        plan.TurnIntent = turnIntent;
        if (plan.InteractionState == null ||
            plan.InteractionState.Intent == null ||
            !string.Equals(plan.InteractionState.Intent.RawMessage, turnIntent.RawMessage, StringComparison.Ordinal))
        {
            plan.InteractionState = new UserTurnEnvelope { Intent = turnIntent, CreativeBrief = turnIntent.CreativeBrief };
        }
        plan.ActiveTurnId = plan.InteractionState.TurnId;
        plan.UpdatedAt = DateTime.UtcNow;
    }

    private static AgentRagStrategy BuildRagStrategy(AgentSession session, StoryBibleDocument bible, string userMessage)
    {
        var stage = DetermineRagStage(session, bible);
        var plan = new AgentRagStrategy { Stage = stage };
        var missionGoal = FirstNonEmpty(session.WorkingMemory.MissionPlan.CurrentNovelGoal, session.WorkingMemory.CurrentGoal, userMessage);
        var constitution = bible.Constitution;
        var currentRun = AgentRunSelector.SelectCurrentRun(bible);
        var chapterId = FirstNonEmpty(currentRun?.TargetChapterId, currentRun?.ChapterBrief?.ChapterId, session.ActiveRunId ?? string.Empty);

        void Add(string bucket, string query, string reason)
        {
            if (!string.IsNullOrWhiteSpace(query) &&
                plan.QueryPlan.All(q => !string.Equals(q.Bucket, bucket, StringComparison.OrdinalIgnoreCase) ||
                                        !string.Equals(q.Query, query, StringComparison.OrdinalIgnoreCase)))
            {
                plan.QueryPlan.Add(new AgentRagQueryPlan { Bucket = bucket, Query = query, Reason = reason });
            }
        }

        Add("currentTurn", userMessage, "用户本轮原始输入");

        switch (stage)
        {
            case "foundation":
                Add("authorPreferences", string.Join(" ", session.WorkingMemory.AuthorMemory.GenreHabits.Concat(session.WorkingMemory.UserPreferences)), "作者长期偏好");
                Add("creativeKnowledge", missionGoal, "新书地基灵感");
                Add("projectMemory", session.WorkingMemory.ProjectMemory.LongTermGoal, "项目长期创作意图");
                break;
            case "volume_planning":
                Add("storyBible", $"{constitution?.Genre} {constitution?.SubGenre} {constitution?.ReaderPromise}", "Story Bible 类型承诺");
                Add("foreshadowLedger", string.Join(" ", bible.ForeshadowLedger.Select(f => f.Name).Take(8)), "卷目标需要承接伏笔");
                Add("creativeKnowledge", $"{constitution?.CoreHook} 卷目标 节奏 反转", "卷级类型节奏");
                break;
            case "chapter_planning":
                Add("chapterContinuity", $"{chapterId} 上一章 摘要 代价 转折", "章节连续性");
                Add("characterState", string.Join(" ", bible.CharacterLedger.Select(c => c.CharacterName + c.Summary).Take(8)), "角色当前状态");
                Add("foreshadowLedger", string.Join(" ", bible.ForeshadowLedger.Select(f => f.Name).Take(8)), "伏笔推进");
                break;
            case "draft_generation":
            case "context_building":
                Add("chapterContinuity", $"{chapterId} 章节上下文包 上章摘要 历史里程碑", "正文生成上下文");
                Add("styleSamples", string.Join(" ", session.WorkingMemory.AuthorMemory.StyleLikes.Concat(session.WorkingMemory.UserPreferences).Take(8)), "文风样本与作者偏好");
                Add("storyBible", $"{constitution?.CoreHook} {constitution?.MainConflictEngine} {constitution?.ProtagonistEngine}", "事实快照与主线约束");
                break;
            case "gate_validation":
            case "repair":
                Add("chapterContinuity", $"{chapterId} 门禁失败 CHANGES 事实快照 蓝图 修复", "修复草稿");
                Add("characterState", string.Join(" ", bible.CharacterLedger.Select(c => c.CharacterName + c.Status).Take(8)), "未知实体与角色状态");
                Add("storyBible", $"{constitution?.WorldCoreRule} {constitution?.MainConflictEngine}", "一致性修复");
                break;
            case "review":
            case "commit":
                Add("projectMemory", $"{chapterId} 复盘 用户反馈 质量报告", "提交后复盘沉淀");
                Add("foreshadowLedger", string.Join(" ", bible.ForeshadowLedger.Select(f => f.Name).Take(8)), "复盘伏笔账本");
                break;
            default:
                if (constitution != null)
                    Add("storyBible", $"{constitution.Genre} {constitution.SubGenre} {constitution.ReaderPromise}", "自由对话项目上下文");
                break;
        }

        return plan;
    }

    private static string DetermineRagStage(AgentSession session, StoryBibleDocument bible)
    {
        var phase = session.Phase;
        if (bible.Constitution == null) return "foundation";
        if (bible.VolumeArcs.Count == 0 || phase.Contains("volume", StringComparison.OrdinalIgnoreCase)) return "volume_planning";
        if (phase.Contains("context", StringComparison.OrdinalIgnoreCase)) return "context_building";
        if (phase.Contains("draft", StringComparison.OrdinalIgnoreCase)) return "draft_generation";
        if (phase.Contains("validated", StringComparison.OrdinalIgnoreCase) || phase.Contains("gate", StringComparison.OrdinalIgnoreCase)) return "gate_validation";
        if (phase.Contains("repair", StringComparison.OrdinalIgnoreCase)) return "repair";
        if (phase.Contains("commit", StringComparison.OrdinalIgnoreCase)) return "commit";
        if (phase.Contains("review", StringComparison.OrdinalIgnoreCase)) return "review";

        var run = AgentRunSelector.SelectCurrentRun(bible);
        if (run?.Intent is NovelAgentIntent.GenerateChapter or NovelAgentIntent.ValidateContinuity)
            return "chapter_planning";

        return "project_context";
    }

    private static void AddBucket(AgentRagContext context, string bucket, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!context.ResultsByBucket.TryGetValue(bucket, out var items))
        {
            items = new List<string>();
            context.ResultsByBucket[bucket] = items;
        }
        if (!items.Contains(value))
            items.Add(value);
    }

    private static List<string> BuildDefaultQueries(AgentSession session, StoryBibleDocument bible, string userMessage)
    {
        var queries = new List<string>();
        if (!string.IsNullOrWhiteSpace(userMessage))
            queries.Add(userMessage);
        if (bible.Constitution != null)
        {
            queries.Add($"{bible.Constitution.Genre} {bible.Constitution.SubGenre} {bible.Constitution.ReaderPromise}");
            queries.Add($"{bible.Constitution.CoreHook} {bible.Constitution.MainConflictEngine}");
        }
        if (!string.IsNullOrWhiteSpace(session.WorkingMemory.MissionPlan.CurrentNovelGoal))
            queries.Add(session.WorkingMemory.MissionPlan.CurrentNovelGoal);
        return queries.Where(q => !string.IsNullOrWhiteSpace(q)).Distinct().Take(5).ToList();
    }

    private static string BuildProjectNote(StoryBibleDocument bible, AgentSession session)
    {
        var currentRun = AgentRunSelector.SelectCurrentRun(bible);
        var constitution = bible.Constitution == null
            ? "Story Bible 尚未固化。"
            : $"Story Bible: {bible.Constitution.Genre}/{bible.Constitution.SubGenre}; {bible.Constitution.CoreHook}; {bible.Constitution.MainConflictEngine}.";
        var run = currentRun == null ? "无当前 Run。" : $"当前 Run: {currentRun.Intent}/{currentRun.Status}/{currentRun.RunId}.";
        return $"{constitution} 卷={bible.VolumeArcs.Count}; 章节Run={bible.AgentRuns.Count(r => !string.IsNullOrWhiteSpace(r.TargetChapterId))}; 会话阶段={session.Phase}; {run}";
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
}

public sealed class AgentPlanner
{
    private static readonly TimeSpan ReflectionLlmBudget = TimeSpan.FromSeconds(2);
    private readonly UserSettingsManager _settingsManager;
    private readonly ILlmToolCallingClient _toolCallingClient;
    private readonly HttpClient _http;

    public AgentPlanner(UserSettingsManager settingsManager, HttpClient http)
        : this(settingsManager, new ProviderToolCallingClient(http), http)
    {
    }

    public AgentPlanner(UserSettingsManager settingsManager, ILlmToolCallingClient toolCallingClient, HttpClient http)
    {
        _settingsManager = settingsManager;
        _toolCallingClient = toolCallingClient;
        _http = http;
    }

    public async Task<AgentAction> PlanActionAsync(AgentObservationContext context, CancellationToken ct)
    {
        var settings = await _settingsManager.LoadAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(settings.LlmBaseUrl) ||
            string.IsNullOrWhiteSpace(settings.LlmModel) ||
            string.IsNullOrWhiteSpace(settings.LlmApiKey))
            return BuildMissingLlmSettingsAction();

        try
        {
            var nativeAction = await _toolCallingClient.PlanToolActionAsync(settings, context, BuildActionSystemPrompt(context.AvailableTools), ct)
                .ConfigureAwait(false);
            if (nativeAction != null)
            {
                NormalizeAction(nativeAction);
                return nativeAction;
            }

            var json = await CompleteJsonAsync(settings, BuildActionSystemPrompt(context.AvailableTools), BuildActionUserPrompt(context), ct)
                .ConfigureAwait(false);
            var action = ParseAction(json);
            NormalizeAction(action);
            return action;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("401") || ex.Message.Contains("403"))
        {
            // Auth error — let runtime handle as no_tool observation
            return new AgentAction
            {
                Type = AgentActionType.ChatReply,
                Intent = "error",
                Reply = "API 认证失败，请在用户设置中检查 API Key。",
                IsNoTool = true,
                Source = "error_auth",
            };
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("429"))
        {
            // Rate limit — retry once after brief delay
            await Task.Delay(2000, ct).ConfigureAwait(false);
            try
            {
                var retryJson = await CompleteJsonAsync(settings, BuildActionSystemPrompt(context.AvailableTools), BuildActionUserPrompt(context), ct).ConfigureAwait(false);
                var retryAction = ParseAction(retryJson);
                NormalizeAction(retryAction);
                return retryAction;
            }
            catch
            {
                return BuildPlannerUnavailableAction(
                    "planner_rate_limited",
                    "模型服务暂时限流，我暂时无法完成本轮决策。请稍后再试，或检查模型额度与配置。");
            }
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new AgentAction
            {
                Type = AgentActionType.ChatReply,
                Intent = "error",
                Reply = "模型响应超时，请稍后重试。",
                IsNoTool = true,
                Source = "error_timeout",
            };
        }
        catch (Exception)
        {
            return BuildPlannerUnavailableAction(
                "planner_unavailable_no_action",
                "模型决策暂时不可用，我暂时无法可靠判断下一步。请稍后重试，或检查模型配置。");
        }
    }

    public async Task<AgentInterruptDecision> PlanInterruptDecisionAsync(
        AgentInterruptDecisionContext context,
        CancellationToken ct)
    {
        var settings = await _settingsManager.LoadAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(settings.LlmBaseUrl) ||
            string.IsNullOrWhiteSpace(settings.LlmModel) ||
            string.IsNullOrWhiteSpace(settings.LlmApiKey))
        {
            return AgentInterruptDecision.Freeform("missing_llm_settings");
        }

        try
        {
            var json = await CompleteJsonAsync(
                    settings,
                    BuildInterruptDecisionSystemPrompt(),
                    BuildInterruptDecisionUserPrompt(context),
                    ct)
                .ConfigureAwait(false);
            return ParseInterruptDecision(json);
        }
        catch
        {
            return AgentInterruptDecision.Freeform("interrupt_decision_model_unavailable");
        }
    }

    public async Task<AgentReflection> ReflectAsync(
        AgentObservationContext context,
        AgentRuntimeObservation observation,
        CancellationToken ct)
    {
        var settings = await _settingsManager.LoadAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(settings.LlmBaseUrl) ||
            string.IsNullOrWhiteSpace(settings.LlmModel) ||
            string.IsNullOrWhiteSpace(settings.LlmApiKey))
            return BuildRuleReflection(context, observation);

        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(ReflectionLlmBudget);
            var json = await CompleteJsonAsync(settings, BuildReflectSystemPrompt(), BuildReflectUserPrompt(context, observation), budget.Token)
                .ConfigureAwait(false);
            return ParseReflection(json);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return BuildRuleReflection(context, observation);
        }
        catch
        {
            return BuildRuleReflection(context, observation);
        }
    }

    private static string BuildActionSystemPrompt(IReadOnlyList<AgentToolDefinition> tools)
    {
        var toolLines = string.Join("\n", tools.Select(t =>
            $"- {t.Name}: {t.Description} [Risk={t.Risk}; Surface={t.Semantic.DomainSurface}; Output={t.Semantic.OutputKind}; Visible={t.Semantic.UserVisibleWhere}]"));

        return "# Stable Layer - Identity & Rules\n\n" +
            "你是天命小说助手，一个专门帮助用户创作长篇小说的 AI 助手。\n" +
            "当用户问起你的身份、名字或你是谁时，回答你是天命小说助手，不要提及 Claude、Anthropic 或其他底层模型名称；当用户问“我是谁/我叫什么/你知道我是谁吗”时，应优先查看 authorMemory.displayName 等用户记忆再回答。\n" +
            "你的职责是帮助用户构思故事、规划章节、生成内容、管理创作进度。用自然、温暖的方式与用户对话。\n\n" +
            $"## Available Tools ({tools.Count})\n{toolLines}\n\n" +
            "## Decision Principles\n" +
            "1. 你拥有 product_space、memory_layers、workspace_state_hint 和工具语义地图；请基于这些信息自己决策，不要依赖关键词路由。\n" +
            "1a. 用户说“继续”“下一步”“开始写”“这是什么意思”等自然表达时，不代表固定工具名或固定阶段；你必须结合 raw_message、chat history、mission blackboard、recent_observations、tool semantics 和真实执行进度判断。\n" +
            "2. 自然问候、开放闲聊可直接 chat_reply；涉及真实系统状态、小说书城、知识库、工作流进度、工具产物位置时，不要凭空猜测；从工具语义中的 Surface/ReadsFrom/Output 自主选择合适工具。\n" +
            "3. 区分过程产物和最终产物：规划、草稿、校验、修订产物属于创作工作流；只有提交后才成为书城或 Story Bible 的最终可见结果。\n" +
            "3a. 面向用户回复时必须使用作者能理解的产品语言，不要展示内部工具名、调度状态码、JSON 字段、fallback/Runtime/guardrail 等工程词；例如说“等待质量评审”“提交章节到书城”，不要说内部状态码或内部提交工具名。\n" +
            "4. Project management: when user wants to bind an existing novel or create a new novel, choose the registered tool whose semantics match project binding/creation. Casual chat must not auto-bind a project.\n" +
            "4a. If turn_intent.type is NewProjectSeed or dialogue_act is StartProject, treat this as a request for an independent new work. Do not bind or continue an old active project unless the user explicitly says to open/bind/continue that existing project. Prefer project creation/resolution arguments that preserve the requested new title and seed.\n" +
            "4b. If the user provides a new book title, that title is the target work identity. An existing active project is background context only, not permission to reuse it.\n" +
            "5. Use clarify when creative info is missing for an explicit action request.\n" +
            "6. Agent loop auto-proceed mode: when executing a writing workflow, proceed through steps without asking for confirmation.\n" +
            "7. Chapter generation workflow: for full chapter writing, prefer the closed-loop ProduceChapter capability so context, draft, validation, repair, review and library commit can run as one production task. Choose concrete tools from the registered tool semantics.\n" +
            "8. PlanChapter and PlanVolumeArc must NOT use userGoal parameter.\n" +
            "8a. PlanStoryFoundation, PlanVolumeArc and PlanChapter require candidateDirections. You must synthesize candidateDirections yourself from raw_message, context, knowledge, memory and project state; runtime/tools will not infer them from keywords.\n" +
            "9. Do not repeat the same tool call. If result satisfies the need, use final_reply.\n" +
            "10. When more knowledge or real state is needed, choose from the complete registered tool list by reading each tool's description, Surface, ReadsFrom, Output and Visible semantics; no business tool is privileged.\n" +
            "11. Read anchor_context for working_memory, task_state, history context.\n" +
            "12. If recent_observations contains a repairable policy/guardrail observation, treat it as an environment fact: choose its recommended prerequisite tool or ask the user; do not repeat the blocked tool.\n\n" +
            "## 工具发现机制\n\n" +
            "tool_search 是全局工具目录与语义检索入口，不是阶段白名单，也不是所有业务工具的前置门禁。\n\n" +
            "**基本原则**：\n" +
            "1. 你自己根据用户意图、产品空间、记忆、工作流状态和工具语义决定要不要调用工具、调用哪个工具。\n" +
            "2. Available Tools 是当前候选工具池，不是唯一可用范围；如果你已明确知道需要哪个已注册业务工具，可以直接调用。\n" +
            "3. 只有工具能力不清、候选缓存明显不足、用户询问系统能力、或需要跨产品空间找工具时，才调用 tool_search。\n" +
            "4. 调用 tool_search 时传 query/intent/context；phase 只能作为排序 hint，不能当作可用工具边界。\n" +
            "5. 如果 tool_search 或其他工具重复调用被运行时拦截，不要把拦截机制告诉用户；把它当作内部观察，改选更合适的业务工具、只读状态工具或给出真实状态回答。\n\n" +
            "**示例**：\n" +
            "- 用户说\"你好\" → 如果只是在问候，可用 chat_reply\n" +
            "- 用户问\"书城里有哪些项目\"、\"知识库有什么\"、\"工作流跑到哪\" → 需要真实状态，应从完整工具目录中选择具备对应读取能力的工具\n" +
            "- 用户明确要求创建新小说、打开/绑定某本已有小说 → 应从完整工具目录中选择具备项目绑定或创建能力的工具\n" +
            "- 用户要求搭建故事地基且已有项目上下文 → 应选择能产出故事地基过程候选的工具；这是工作流过程产物，不是最终书城成稿\n" +
            "- 用户表达“开始写/继续/下一步” → 先理解当前项目状态和最近执行结果，再自主判断是追问、查询状态、规划、生成还是提交；不要把这些词当作硬路由\n\n" +
            "## 任务执行原则\n" +
            "采用'先执行后修正'模式，不要频繁请求用户确认：\n" +
            "1. 理解用户意图后，如已有足够上下文和工具语义，直接选择合适业务工具执行；不需要每次先 tool_search\n" +
            "2. 执行后告知用户结果和下一步计划\n" +
            "3. 如果用户不满意，会主动告诉你如何调整\n" +
            "4. 工作流自带校验和修复机制，发现问题时优先让闭环生产任务继续修复\n\n" +
            "**不要问**：\"我现在要调用XX工具，可以吗？\"\n" +
            "**应该做**：调用工具 → 展示结果 → \"已完成XX，现在进行YY...\"\n\n" +
            "## Output Format\n" +
            "You can respond in two ways:\n" +
            "1. Natural reply (for conversations): Set action_type='chat_reply' or 'final_reply', put your conversational response in 'reply' field. Be warm and helpful.\n" +
            "2. Tool call (for actions): Set action_type='tool_call', specify tool_call.name and arguments.\n\n" +
            "## JSON Schema\n" +
            "{\"action_type\":\"chat_reply|clarify|retrieve|tool_call|final_reply\",\"intent\":\"\",\"reply\":\"\",\"brief\":\"\",\"tool_call\":{\"name\":\"\",\"arguments\":{}},\"rag_queries\":[],\"risk\":\"Low\",\"requires_confirmation\":false,\"suggestions\":[],\"confidence\":0.8}";
    }

    private static string BuildActionUserPrompt(AgentObservationContext context)
    {
        // If we have an anchor prompt, prepend it for richer context
        var anchorSection = string.IsNullOrWhiteSpace(context.AnchorPrompt)
            ? ""
            : context.AnchorPrompt + "\n\n";

        var payload = new
        {
            user_message = context.UserMessage,
            anchor_context = string.IsNullOrWhiteSpace(context.AnchorPrompt) ? null : context.AnchorPrompt,
            product_space = context.ProductSpace,
            memory_layers = context.ProductSpace.MemoryLayers,
            memory_context = new
            {
                session_memory = context.SessionMemory,
                project_memory = context.ProjectMemory,
                author_memory = new
                {
                    display_name = context.AuthorMemory.DisplayName,
                    style_likes = context.AuthorMemory.StyleLikes,
                    style_dislikes = context.AuthorMemory.StyleDislikes,
                    confirmation_tolerance = context.AuthorMemory.ConfirmationTolerance,
                    genre_habits = context.AuthorMemory.GenreHabits,
                    favorite_knowledge_ids = context.AuthorMemory.FavoriteKnowledgeIds,
                },
                execution_memory = context.ExecutionMemory,
            },
            workspace_state = context.WorkspaceState,
            project = new
            {
                id = context.ProjectId,
                title = context.ProjectTitle,
                summary = context.ProjectSummary,
                phase = context.Phase,
                active_run_id = context.ActiveRunId,
            },
            mission_plan = context.MissionPlan,
            mission_state = context.MissionState,
            pending_confirmation = context.PendingConfirmation,
            runtime_interrupts = context.RuntimeInterrupts,
            turn_intent = context.TurnIntent,
            recent_messages = context.RecentMessages,
            rag = context.Rag,
            recent_observations = context.RecentObservations,
            tools = context.AvailableTools,
        };
        return anchorSection + JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string BuildReflectSystemPrompt() =>
        """
        你是小说 Agent 的 Reflect 阶段。你只能输出 JSON。
        你要阅读刚执行的工具观察和完整上下文，判断目标是否满足、是否继续、是否需要用户输入，并给出任务记忆更新。
        如果 latest_observation.ObservationType / observation_type 是 policy_observation 或 runtime_observation：
        - 这不是给用户看的文案，而是运行时治理层的观察。
        - 不要复述“策略拦截、重复工具、Runtime 拒绝”等内部机制。
        - 你要把它当作新的环境事实，重新判断当前会话状态、任务黑板和下一步。
        - 如果工具已经创建过项目或已有等待中的地基输入，应继续沿当前项目推进，用自然语言向作者说明当前需要的创作输入。
        - 如果策略阻止了错误工具调用，应解释可见状态和下一步，而不是责备用户或暴露 guard。
        - 面向作者的 reply_draft 必须使用产品语言，不要输出内部工具名、调度状态码、JSON 字段、fallback/Runtime/guardrail 等工程词。
        对章节草稿、门禁报告、修复结果和提交前状态，你必须额外进行文学质量门禁：
        - 节奏是否拖沓或跳跃。
        - 角色动机是否成立。
        - 冲突是否推进。
        - 是否违背事实快照、伏笔、设定或 RAG 上下文。
        - 文风是否符合作者偏好。
        - 是否应该修复、重写、追问用户，还是继续提交。
        GenerationGate 通过不等于可以提交；结构化门禁通过后，你仍要用 quality_gate 决定文学质量。
        不要重复工具结果原文，要用作者能理解的方式总结。

        输出 JSON schema：
        {
          "summary": "",
          "goal_satisfied": false,
          "should_continue": false,
          "requires_user_input": false,
          "next_intent": "",
          "reply_draft": "",
          "completed_items": [],
          "new_todo_items": [],
          "blockers": [],
          "quality_gate": {
            "status": "pass|warn|fail|needs_rewrite|needs_user_input|not_applicable",
            "scores": {
              "pacing": 0,
              "character_motivation": 0,
              "conflict": 0,
              "continuity": 0,
              "prose": 0,
              "reader_promise": 0
            },
            "issues": [],
            "evidence": [],
            "rewrite_decision": "",
            "requires_user_input": false
          },
          "mission_patch": {
            "status": "",
            "stage": "",
            "current_focus": "",
            "chapter_patches": [
              {
                "chapter_id": "",
                "status": "",
                "gate_status": "",
                "quality_issue_summary": "",
                "next_action": ""
              }
            ]
          }
        }

        # 记忆提取指令

        在每次 Reflection 时，从对话历史中提取关键信息更新四层记忆：

        ## memoryUpdate.sessionMemory
        - chatSummary: 本轮对话核心内容（50-100字）
        - extractedPreferences: 用户偏好（如"避免..."、"更喜欢..."）

        ## memoryUpdate.projectMemory
        - newConstraints: 写作约束（如"不要出现XXX"）
        - unresolvedThreads: 伏笔线索（格式："线索名（计划揭示章节）"）

        ## memoryUpdate.authorMemory
        - displayName: 用户明确告诉你的称呼或名字。只在用户明确自称或要求你这样称呼时写入；不确定时返回 null。
        - styleLikes: 喜欢的写作风格
        - styleDislikes: 反感的风格
        - confirmationTolerance: 用户对自动执行/确认的偏好（如 auto_low_risk、key_checkpoints）
        - genreHabits: 常写或偏好的题材习惯
        - favoriteKnowledgeIds: 用户反复认可或偏好的知识条目ID

        ## memoryUpdate.executionMemory
        - toolSuccess: 工具成功经验
        - toolFailure: 工具失败原因
        - toolFailurePatterns: 可复用的工具失败模式
        - knowledgeProcessingFailures: 知识文件处理失败经验

        注意：无更新时返回空数组或null
        """;

    private static string BuildReflectUserPrompt(AgentObservationContext context, AgentRuntimeObservation observation)
    {
        var payload = new
        {
            user_message = context.UserMessage,
            project = context.ProjectSummary,
            mission_plan = context.MissionPlan,
            memory_layers = new
            {
                session = context.SessionMemory,
                project = context.ProjectMemory,
                author = context.AuthorMemory,
                execution = context.ExecutionMemory,
            },
            rag = context.Rag,
            runtime_interrupts = context.RuntimeInterrupts,
            recent_messages = context.RecentMessages,
            latest_observation = observation,
            recent_observations = context.RecentObservations,
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    private static AgentAction ParseAction(string raw)
    {
        using var doc = JsonDocument.Parse(ExtractJsonObject(raw));
        var root = doc.RootElement;
        var action = new AgentAction
        {
            Type = ParseActionType(ReadString(root, "action_type", "chat_reply")),
            Intent = ReadString(root, "intent", "free_chat"),
            Reply = ReadString(root, "reply", string.Empty),
            Brief = ReadString(root, "brief", string.Empty),
            Risk = ReadString(root, "risk", "Low"),
            RequiresConfirmation = ReadBool(root, "requires_confirmation"),
            Confidence = ReadDouble(root, "confidence", 0.5),
            Source = "llm_runtime",
        };

        if (root.TryGetProperty("rag_queries", out var queries) && queries.ValueKind == JsonValueKind.Array)
            action.RagQueries = queries.EnumerateArray()
                .Select(q => q.GetString() ?? string.Empty)
                .Where(q => !string.IsNullOrWhiteSpace(q))
                .ToList();

        if (root.TryGetProperty("suggestions", out var suggestions) && suggestions.ValueKind == JsonValueKind.Array)
            action.Suggestions = suggestions.EnumerateArray()
                .Select(q => q.GetString() ?? string.Empty)
                .Where(q => !string.IsNullOrWhiteSpace(q))
                .ToArray();

        if (root.TryGetProperty("tool_call", out var tool) && tool.ValueKind == JsonValueKind.Object)
        {
            var call = new AgentToolCall { Name = ReadString(tool, "name", string.Empty) };
            if (tool.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in args.EnumerateObject())
                    call.Arguments[prop.Name] = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? string.Empty : prop.Value.ToString();
            }
            if (!string.IsNullOrWhiteSpace(call.Name))
                action.ToolCall = call;
        }

        return action;
    }

    private static AgentReflection ParseReflection(string raw)
    {
        using var doc = JsonDocument.Parse(ExtractJsonObject(raw));
        var root = doc.RootElement;
        var reflection = new AgentReflection
        {
            Summary = ReadString(root, "summary", string.Empty),
            GoalSatisfied = ReadBool(root, "goal_satisfied"),
            ShouldContinue = ReadBool(root, "should_continue"),
            RequiresUserInput = ReadBool(root, "requires_user_input"),
            NextIntent = ReadString(root, "next_intent", string.Empty),
            ReplyDraft = ReadString(root, "reply_draft", string.Empty),
            CompletedItems = ReadStringArray(root, "completed_items"),
            NewTodoItems = ReadStringArray(root, "new_todo_items"),
            Blockers = ReadStringArray(root, "blockers"),
        };
        if (root.TryGetProperty("quality_gate", out var quality) && quality.ValueKind == JsonValueKind.Object)
            reflection.QualityGate = ParseQualityGate(quality);
        if (TryGetObject(root, out var patch, "mission_patch", "missionPatch"))
            reflection.MissionPatch = ParseMissionPatch(patch);
        if (TryGetObject(root, out var memoryUpdate, "memory_update", "memoryUpdate"))
            reflection.MissionPatch.MemoryUpdate = ParseMemoryUpdate(memoryUpdate);
        return reflection;
    }

    private static AgentQualityGateReport ParseQualityGate(JsonElement root)
    {
        var report = new AgentQualityGateReport
        {
            Status = ReadString(root, "status", "not_applicable"),
            Issues = ReadStringArray(root, "issues"),
            Evidence = ReadStringArray(root, "evidence"),
            RewriteDecision = ReadString(root, "rewrite_decision", string.Empty),
            RequiresUserInput = ReadBool(root, "requires_user_input"),
        };
        if (root.TryGetProperty("scores", out var scores) && scores.ValueKind == JsonValueKind.Object)
        {
            report.Scores = new AgentQualityScores
            {
                Pacing = ReadInt(scores, "pacing"),
                CharacterMotivation = ReadInt(scores, "character_motivation"),
                Conflict = ReadInt(scores, "conflict"),
                Continuity = ReadInt(scores, "continuity"),
                Prose = ReadInt(scores, "prose"),
                ReaderPromise = ReadInt(scores, "reader_promise"),
            };
        }
        return report;
    }

    private static AgentMissionPatch ParseMissionPatch(JsonElement root)
    {
        var patch = new AgentMissionPatch
        {
            Status = ReadString(root, "status", string.Empty),
            Stage = ReadString(root, "stage", string.Empty),
            CurrentFocus = ReadString(root, "current_focus", string.Empty),
        };
        if (TryGetProperty(root, out var chapters, "chapter_patches", "chapterPatches") && chapters.ValueKind == JsonValueKind.Array)
        {
            patch.ChapterPatches = chapters.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => new AgentChapterTaskPatch
                {
                    ChapterId = ReadString(item, "chapter_id", string.Empty),
                    Status = ReadString(item, "status", string.Empty),
                    GateStatus = ReadString(item, "gate_status", string.Empty),
                    QualityIssueSummary = ReadString(item, "quality_issue_summary", string.Empty),
                    NextAction = ReadString(item, "next_action", string.Empty),
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.ChapterId))
                .ToList();
        }
        if (TryGetObject(root, out var memoryUpdate, "memory_update", "memoryUpdate"))
            patch.MemoryUpdate = ParseMemoryUpdate(memoryUpdate);
        return patch;
    }

    private static AgentMemoryUpdate ParseMemoryUpdate(JsonElement root)
    {
        var update = new AgentMemoryUpdate
        {
            UsedKnowledgeIds = ReadStringArray(root, "used_knowledge_ids", "usedKnowledgeIds"),
            UsedTropePatterns = ReadStringArray(root, "used_trope_patterns", "usedTropePatterns"),
        };

        if (TryGetObject(root, out var session, "session_memory", "sessionMemory"))
        {
            update.SessionMemory = new SessionMemoryUpdate
            {
                ChatSummary = ReadOptionalString(session, string.Empty, "chat_summary", "chatSummary"),
                ExtractedPreferences = ReadStringArray(session, "extracted_preferences", "extractedPreferences")
            };
        }

        if (TryGetObject(root, out var project, "project_memory", "projectMemory"))
        {
            update.ProjectMemory = new ProjectMemoryUpdate
            {
                NewConstraints = ReadStringArray(project, "new_constraints", "newConstraints")
                // UnresolvedThreads removed
            };
        }

        if (TryGetObject(root, out var author, "author_memory", "authorMemory"))
        {
            update.AuthorMemory = new AuthorMemoryUpdate
            {
                DisplayName = ReadOptionalString(author, null, "display_name", "displayName"),
                StyleLikes = ReadStringArray(author, "style_likes", "styleLikes"),
                StyleDislikes = ReadStringArray(author, "style_dislikes", "styleDislikes"),
                ConfirmationTolerance = ReadOptionalString(author, null, "confirmation_tolerance", "confirmationTolerance"),
                GenreHabits = ReadStringArray(author, "genre_habits", "genreHabits"),
                FavoriteKnowledgeIds = ReadStringArray(author, "favorite_knowledge_ids", "favoriteKnowledgeIds")
            };
        }

        if (TryGetObject(root, out var execution, "execution_memory", "executionMemory"))
        {
            update.ExecutionMemory = new ExecutionMemoryUpdate
            {
                ToolSuccess = ReadOptionalString(execution, null, "tool_success", "toolSuccess"),
                ToolFailure = ReadOptionalString(execution, null, "tool_failure", "toolFailure"),
                ToolFailurePatterns = ReadStringArray(execution, "tool_failure_patterns", "toolFailurePatterns"),
                KnowledgeProcessingFailures = ReadStringArray(execution, "knowledge_processing_failures", "knowledgeProcessingFailures")
            };
        }

        return update;
    }

    private static AgentToolCall? ParseToolCall(JsonElement tool)
    {
        var call = new AgentToolCall { Name = ReadString(tool, "name", string.Empty) };
        if (string.IsNullOrWhiteSpace(call.Name))
            return null;
        if (tool.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in args.EnumerateObject())
                call.Arguments[prop.Name] = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? string.Empty : prop.Value.ToString();
        }
        return call;
    }

    private static AgentAction BuildMissingLlmSettingsAction() => new()
    {
        Type = AgentActionType.ChatReply,
        Intent = "degraded_missing_llm",
        Reply = "当前没有配置可用的模型服务，我只能进入降级模式：不会冒充完整 Agent 决策，也不会自动选择写作工具。请先配置模型服务，或只让我查看已有状态。",
        Suggestions = new[] { "查看当前状态", "配置模型服务" },
        IsNoTool = true,
        Source = "missing_llm_settings",
    };

    private static AgentAction BuildPlannerUnavailableAction(string source, string reply) => new()
    {
        Type = AgentActionType.ChatReply,
        Intent = "planner_unavailable",
        Reply = reply,
        IsNoTool = true,
        Source = source,
    };

    private static AgentReflection BuildRuleReflection(AgentObservationContext context, AgentRuntimeObservation observation)
    {
        if (observation.ObservationType is "policy_observation" or "runtime_observation")
            return BuildGovernanceRuleReflection(context, observation);

        var isReviewableProcessArtifact =
            observation.Artifact?.ArtifactType is "story_foundation_candidates" or "volume_arc_candidates" or "chapter_candidates" ||
            observation.Phase.Contains("candidates", StringComparison.OrdinalIgnoreCase);
        var reviewableArtifactCanContinue =
            isReviewableProcessArtifact &&
            HasStructuredContinuationForReviewableArtifact(context, observation);
        var isNewProjectFoundationIntake =
            string.Equals(observation.ToolName, "ResolveNovelProject", StringComparison.OrdinalIgnoreCase) &&
            (observation.Phase.Contains("awaiting_user_foundation", StringComparison.OrdinalIgnoreCase) ||
             observation.Phase.Contains("foundation_intake", StringComparison.OrdinalIgnoreCase) ||
             observation.Artifact?.ArtifactType is "novel_project" or "existing_novel_project");
        var isProjectLifecycleArtifact =
            observation.Artifact?.ArtifactType is "novel_project" or "existing_novel_project" or "project_bound";
        var requiresInput = !observation.Success ||
                            (isReviewableProcessArtifact && !reviewableArtifactCanContinue) ||
                            isNewProjectFoundationIntake ||
                            observation.Phase.Contains("awaiting_user", StringComparison.OrdinalIgnoreCase) ||
                            observation.Phase.Contains("foundation_intake", StringComparison.OrdinalIgnoreCase) ||
                            isProjectLifecycleArtifact;
        var qualityGate = BuildRuleQualityGate(observation);
        if (qualityGate.Status is "fail" or "needs_rewrite" or "needs_user_input")
            requiresInput = qualityGate.RequiresUserInput;
        return new AgentReflection
        {
            Summary = observation.Message,
            GoalSatisfied = observation.Success && requiresInput,
            ShouldContinue = observation.Success && !requiresInput && qualityGate.Status != "needs_rewrite",
            RequiresUserInput = requiresInput,
            NextIntent = DetermineNextIntent(observation, qualityGate),
            ReplyDraft = observation.Message,
            CompletedItems = observation.Success ? new List<string> { $"{observation.ToolName} 完成" } : new List<string>(),
            NewTodoItems = observation.Success && !requiresInput ? new List<string> { "根据工具结果继续推进下一步" } : new List<string>(),
            Blockers = observation.Success ? new List<string>() : new List<string> { observation.Message },
            QualityGate = qualityGate,
            MissionPatch = BuildRuleMissionPatch(observation, qualityGate),
            // Don't recommend next tool — let LLM decide
        };
    }

    private static bool HasStructuredContinuationForReviewableArtifact(AgentObservationContext context, AgentRuntimeObservation observation)
    {
        var artifactType = observation.Artifact?.ArtifactType ?? string.Empty;
        var allowed = context.MissionPlan.AllowedNextActions;
        if (allowed.Count == 0 && context.MissionPlan.SchedulerState.Tasks.Count == 0)
            return false;

        var continuationActions = artifactType switch
        {
            "story_foundation_candidates" => new[] { "CommitStoryFoundation" },
            "volume_arc_candidates" => new[] { "CommitVolumeArc" },
            "chapter_candidates" => new[] { "SelectChapterCandidate", "ProduceChapter" },
            _ when observation.Phase.Contains("foundation_candidates", StringComparison.OrdinalIgnoreCase) => new[] { "CommitStoryFoundation" },
            _ when observation.Phase.Contains("volume", StringComparison.OrdinalIgnoreCase) => new[] { "CommitVolumeArc" },
            _ when observation.Phase.Contains("chapter", StringComparison.OrdinalIgnoreCase) => new[] { "SelectChapterCandidate", "ProduceChapter" },
            _ => Array.Empty<string>(),
        };

        if (continuationActions.Length == 0)
            return allowed.Count > 0 || context.MissionPlan.SchedulerState.Tasks.Any(task => !string.IsNullOrWhiteSpace(task.NextAction));

        return allowed.Any(action => continuationActions.Contains(action, StringComparer.OrdinalIgnoreCase)) ||
               context.MissionPlan.SchedulerState.Tasks.Any(task =>
                   !string.Equals(task.Status, "completed", StringComparison.OrdinalIgnoreCase) &&
                   continuationActions.Contains(task.NextAction, StringComparer.OrdinalIgnoreCase));
    }

    private static AgentReflection BuildGovernanceRuleReflection(AgentObservationContext context, AgentRuntimeObservation observation)
    {
        var needsFoundationIntake = observation.Phase.Contains("foundation", StringComparison.OrdinalIgnoreCase);
        var reply = needsFoundationIntake
            ? "这本新小说的工程已经在当前会话里准备好了。下一步我需要你补齐故事地基：类型、核心钩子、主角引擎、主要阅读快感，以及明确不要的方向。"
            : BuildNaturalGovernanceReply(context, observation);

        return new AgentReflection
        {
            Summary = "治理层观察已进入任务反思。",
            GoalSatisfied = true,
            ShouldContinue = false,
            RequiresUserInput = true,
            NextIntent = needsFoundationIntake ? "foundation_intake" : "await_user",
            ReplyDraft = reply,
            NewTodoItems = new List<string> { "等待作者补充下一步输入" },
            Blockers = new List<string>(),
            QualityGate = new AgentQualityGateReport { Status = "not_applicable" },
            MissionPatch = new AgentMissionPatch
            {
                Status = "blocked",
                Stage = needsFoundationIntake ? "foundation" : "await_user",
                CurrentFocus = context.ActiveRunId,
            },
        };
    }

    private static string BuildNaturalGovernanceReply(AgentObservationContext context, AgentRuntimeObservation observation)
    {
        if (context.MissionPlan.AllowedNextActions.Count > 0)
        {
            var actions = context.MissionPlan.AllowedNextActions
                .Select(TM.Web.NovelAgentWeb.Services.AgentTools.AgentToolProgressPresenter.DescribeAction)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();
            if (actions.Count > 0)
                return $"当前还没有新的工具执行结果。按工作流，下一步可以推进：{string.Join("、", actions)}。你可以继续补充要求，我会基于真实执行进度再判断下一步。";
        }
        return "我没有继续执行新的写入动作。你可以告诉我现在要查看状态、继续任务，还是补充新的创作简报。";
    }

    private static AgentQualityGateReport BuildRuleQualityGate(AgentRuntimeObservation observation)
    {
        var isWritingStep = string.Equals(observation.ToolName, "ProduceChapter", StringComparison.OrdinalIgnoreCase) ||
                            observation.Phase.Contains("draft", StringComparison.OrdinalIgnoreCase) ||
                            observation.Phase.Contains("validated", StringComparison.OrdinalIgnoreCase) ||
                            observation.Phase.Contains("failed", StringComparison.OrdinalIgnoreCase);
        if (!isWritingStep)
            return new AgentQualityGateReport { Status = "not_applicable" };

        if (!observation.Success)
        {
            return new AgentQualityGateReport
            {
                Status = "fail",
                RequiresUserInput = false,
                Issues = new List<string> { observation.Message },
                Evidence = new List<string> { observation.ToolName, observation.Phase },
                RewriteDecision = "工具失败，不能继续提交章节。",
                Scores = new AgentQualityScores { Continuity = 2, ReaderPromise = 2 },
            };
        }

        if (observation.Phase.Contains("validated", StringComparison.OrdinalIgnoreCase))
        {
            return new AgentQualityGateReport
            {
                Status = "pass",
                Evidence = new List<string> { "GenerationGate validated", observation.Message },
                RewriteDecision = "结构化门禁通过，结构化检查未发现质量阻塞；可以继续自动提交。",
                Scores = new AgentQualityScores { Pacing = 7, CharacterMotivation = 7, Conflict = 7, Continuity = 8, Prose = 7, ReaderPromise = 7 },
            };
        }

        if (observation.Phase.Contains("failed", StringComparison.OrdinalIgnoreCase) || observation.Phase.Contains("gate_failed", StringComparison.OrdinalIgnoreCase))
        {
            return new AgentQualityGateReport
            {
                Status = "needs_rewrite",
                Issues = new List<string> { observation.Message },
                Evidence = new List<string> { observation.ToolName, observation.Phase },
                RewriteDecision = "GenerationGate 未通过，必须修复草稿与 CHANGES 后再考虑提交。",
                Scores = new AgentQualityScores { Continuity = 3, ReaderPromise = 4 },
            };
        }

        return new AgentQualityGateReport
        {
            Status = "warn",
            Evidence = new List<string> { observation.ToolName, observation.Phase },
            RewriteDecision = "已生成写作中间产物，下一步需要结构化门禁和质量复核。",
            Scores = new AgentQualityScores { Pacing = 6, CharacterMotivation = 6, Conflict = 6, Continuity = 6, Prose = 6, ReaderPromise = 6 },
        };
    }

    private static AgentMissionPatch BuildRuleMissionPatch(
        AgentRuntimeObservation observation,
        AgentQualityGateReport qualityGate)
    {
        var patch = new AgentMissionPatch
        {
            Stage = DetermineNextIntent(observation, qualityGate),
            Status = qualityGate.Status is "fail" or "needs_user_input" ? "blocked" : string.Empty,
            CurrentFocus = observation.RunId,
        };
        if (!string.IsNullOrWhiteSpace(observation.Artifact?.ArtifactId))
        {
            patch.ChapterPatches.Add(new AgentChapterTaskPatch
            {
                ChapterId = observation.Artifact.ArtifactId,
                Status = MapObservationToChapterStatus(observation, qualityGate),
                GateStatus = observation.Phase is "validated" or "gate_failed" or "failed" ? observation.Phase : string.Empty,
                QualityIssueSummary = qualityGate.Issues.Count == 0 ? string.Empty : string.Join("；", qualityGate.Issues.Take(3)),
                NextAction = string.Empty,
            });
        }
        return patch;
    }

    private static string DetermineNextIntent(AgentRuntimeObservation observation, AgentQualityGateReport qualityGate)
    {
        if (qualityGate.Status == "needs_rewrite") return "repair";
        if (qualityGate.Status == "needs_user_input") return "await_user";
        if (qualityGate.Status == "pass" && observation.Phase == "validated") return "commit";
        return observation.Phase switch
        {
            "context_ready" => "draft_generation",
            "draft_generated" => "gate_validation",
            "validated" => "commit",
            "committed" => "review",
            _ => "continue",
        };
    }

    private static string MapObservationToChapterStatus(AgentRuntimeObservation observation, AgentQualityGateReport qualityGate)
    {
        if (qualityGate.Status == "needs_rewrite") return "gate_failed";
        if (qualityGate.Status is "pass" or "warn" && observation.Phase == "validated") return "quality_passed";
        return observation.Phase switch
        {
            "context_ready" => "context_ready",
            "draft_generated" => "draft_generated",
            "validated" => "validated",
            "committed" => "committed",
            _ => string.Empty,
        };
    }

    private static void NormalizeAction(AgentAction action)
    {
        if (string.IsNullOrWhiteSpace(action.Intent))
            action.Intent = "free_chat";
        if (action.ToolCall != null && string.IsNullOrWhiteSpace(action.ToolCall.Name))
            action.ToolCall = null;
        if (action.Type == AgentActionType.ToolCall && action.ToolCall == null)
            action.Type = AgentActionType.ChatReply;
        if (action.Type == AgentActionType.ConfirmRequest && action.ToolCall != null)
            action.Type = AgentActionType.ToolCall;
        action.RequiresConfirmation = false;
    }

    private static AgentActionType ParseActionType(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "clarify" => AgentActionType.Clarify,
            "retrieve" => AgentActionType.Retrieve,
            "tool_call" => AgentActionType.ToolCall,
            "confirm_request" => AgentActionType.ConfirmRequest,
            "reflect" => AgentActionType.Reflect,
            "final_reply" => AgentActionType.FinalReply,
            _ => AgentActionType.ChatReply,
        };

    private static string BuildInterruptDecisionSystemPrompt() =>
        """
        你是 Agent Runtime 的中断意图判定器。
        你只负责理解用户在后台任务执行中的插话语义，不调用业务工具，不推进小说生产，不写正文。

        必须只返回一个 JSON object：
        {
          "kind": "status | soft_requirement | cancel | direction_change | freeform",
          "priority": 0-100,
          "reason": "一句话说明判定依据",
          "userVisibleAcknowledgement": "给用户看的简短确认"
        }

        判断准则：
        - status：用户在问当前执行进度、是否开始、做到哪一步、为什么没反馈。不中断工具。
        - soft_requirement：用户补充可并入当前任务的要求、偏好、限制。能在下个安全边界注入。
        - cancel：用户明确要求停止、暂停、不要继续当前后台执行。
        - direction_change：用户想改变当前任务方向，可能需要暂停当前产物、重新决策或重建生产包。
        - freeform：无法归类，或只是普通说明。

        不要用关键词路由。必须结合 active run 状态、当前工具、用户话语的真实意图判断。
        """;

    private static string BuildInterruptDecisionUserPrompt(AgentInterruptDecisionContext context) =>
        JsonSerializer.Serialize(new
        {
            userMessage = context.UserMessage,
            activeRun = new
            {
                context.RuntimeRunId,
                context.RunStatus,
                context.ActiveTool,
                context.CurrentPhase,
                context.LastMessage,
                context.ProjectId,
                context.CurrentUserGoal
            },
            recentInterrupts = context.RecentInterrupts.TakeLast(6).Select(item => new
            {
                item.Kind,
                item.Message,
                item.Priority,
                item.ReceivedAt
            })
        }, JsonHelper.CnDefault);

    private static AgentInterruptDecision ParseInterruptDecision(string raw)
    {
        var json = ExtractJsonObject(raw);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var kind = ReadString(root, "kind");
        var reason = ReadString(root, "reason");
        var acknowledgement = FirstNonEmpty(
            ReadString(root, "userVisibleAcknowledgement"),
            ReadString(root, "acknowledgement"),
            BuildInterruptAcknowledgement(kind));
        var priority = ReadInt(root, "priority");

        return new AgentInterruptDecision(
            NormalizeInterruptKind(kind),
            Math.Clamp(priority, 0, 100),
            reason,
            acknowledgement);
    }

    private static string NormalizeInterruptKind(string kind) =>
        kind.Trim().ToLowerInvariant() switch
        {
            "status" => "status",
            "soft_requirement" => "soft_requirement",
            "cancel" => "cancel",
            "direction_change" => "direction_change",
            "freeform" => "freeform",
            _ => "freeform"
        };

    private static string BuildInterruptAcknowledgement(string kind) =>
        NormalizeInterruptKind(kind) switch
        {
            "status" => "我收到你的进度追问了。",
            "soft_requirement" => "我收到你的补充要求了，会交给当前执行在安全边界处理。",
            "cancel" => "我收到你的暂停/取消请求了。",
            "direction_change" => "我收到你的改方向要求了，会让当前执行在安全边界重新决策。",
            _ => "我收到你的消息了。"
        };

    private static string ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int ReadInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed)
            ? parsed
            : 0;

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string ExtractJsonObject(string raw)
    {
        return ModelJsonObjectExtractor.ExtractFirstObject(
            raw,
            "Agent Planner 没有返回 JSON 内容。",
            "Agent Planner 没有返回 JSON object。");
    }

    private static string BuildSourceTurnId(string raw) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..12];

    private async Task<string> CompleteJsonAsync(UserSettings settings, string system, string user, CancellationToken ct)
    {
        var model = NormalizeProviderModelId(settings.LlmModel);

        if (string.Equals(settings.LlmProvider, "anthropic", StringComparison.OrdinalIgnoreCase))
        {
            var payload = new
            {
                model,
                system,
                max_tokens = settings.LlmMaxTokens,
                temperature = settings.LlmTemperature,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new[] { new { type = "text", text = user } }
                    }
                }
            };
            var body = await PostJsonAnthropicAsync(BuildAnthropicMessagesUrl(settings.LlmBaseUrl), settings.LlmApiKey, payload, ct).ConfigureAwait(false);
            return ParseAnthropicText(body);
        }

        var openAiPayload = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user },
            },
            max_tokens = settings.LlmMaxTokens,
            temperature = settings.LlmTemperature,
        };
        var openAiBody = await PostJsonOpenAiAsync(BuildChatCompletionsUrl(settings.LlmBaseUrl), settings.LlmApiKey, openAiPayload, ct).ConfigureAwait(false);
        return ParseOpenAiText(openAiBody);
    }

    private async Task<string> PostJsonOpenAiAsync(string url, string apiKey, object payload, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"模型接口返回 {(int)response.StatusCode}: {body}");
        return body;
    }

    private async Task<string> PostJsonAnthropicAsync(string url, string apiKey, object payload, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"模型接口返回 {(int)response.StatusCode}: {body}");
        return body;
    }

    private static async Task<string> PostJsonAsync(HttpClient http, string url, object payload, CancellationToken ct)
    {
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(url, content, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"模型接口返回 {(int)response.StatusCode}: {body}");
        return body;
    }

    private static string BuildChatCompletionsUrl(string baseUrl)
    {
        var url = baseUrl.Trim().TrimEnd('/');
        return url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) ? url : $"{url}/chat/completions";
    }

    private static string BuildAnthropicMessagesUrl(string baseUrl)
    {
        var url = baseUrl.Trim().TrimEnd('/');
        if (url.EndsWith("/v1/messages", StringComparison.OrdinalIgnoreCase) || url.EndsWith("/messages", StringComparison.OrdinalIgnoreCase))
            return url;
        if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return $"{url}/messages";
        return $"{url}/v1/messages";
    }

    private static string NormalizeProviderModelId(string model)
    {
        var value = model.Trim();
        if (value.EndsWith("[1m]", StringComparison.OrdinalIgnoreCase))
            value = value[..^4].Trim();
        if (value.EndsWith(":extended", StringComparison.OrdinalIgnoreCase))
            value = value[..^9].Trim();
        return value;
    }

    private static string ParseAnthropicText(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return string.Empty;
        return string.Join("\n", content.EnumerateArray()
            .Select(item => item.TryGetProperty("text", out var text) ? text.GetString() : string.Empty)
            .Where(text => !string.IsNullOrWhiteSpace(text))).Trim();
    }

    private static string ParseOpenAiText(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
            return string.Empty;
        var first = choices.EnumerateArray().FirstOrDefault();
        if (first.ValueKind == JsonValueKind.Undefined) return string.Empty;
        if (first.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
            return content.GetString()?.Trim() ?? string.Empty;
        return string.Empty;
    }

    private static string ReadString(JsonElement root, string name, string fallback) =>
        ReadOptionalString(root, fallback, name) ?? fallback;

    private static string? ReadOptionalString(JsonElement root, string? fallback, params string[] names) =>
        TryGetProperty(root, out var value, names) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;

    private static bool ReadBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();

    private static double ReadDouble(JsonElement root, string name, double fallback) =>
        root.TryGetProperty(name, out var value) && value.TryGetDouble(out var d) ? d : fallback;

    private static int ReadInt(JsonElement root, string name, int fallback = 0) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var i) ? i : fallback;

    private static List<string> ReadStringArray(JsonElement root, params string[] names)
    {
        if (!TryGetProperty(root, out var value, names) || value.ValueKind != JsonValueKind.Array)
            return new List<string>();
        return value.EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }

    private static bool TryGetObject(JsonElement root, out JsonElement value, params string[] names) =>
        TryGetProperty(root, out value, names) && value.ValueKind == JsonValueKind.Object;

    private static bool TryGetProperty(JsonElement root, out JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out value))
                return true;
        }

        value = default;
        return false;
    }

}
