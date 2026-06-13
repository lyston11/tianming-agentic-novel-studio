using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Services.Memory;

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
    AskStatus,
    Confirm,
    Cancel,
    StartProject,
    ProvideBrief,
    ContinueTask,
    ReviseArtifact,
    GiveFeedback,
    SwitchProject,
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
    // Compatibility field: means planner_failed_or_no_action, not "chat reply does not need a tool".
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
    public string Summary { get; set; } = string.Empty;
    public IReadOnlyList<string> NextHints { get; set; } = Array.Empty<string>();
    public bool VisibleInWorkflow { get; set; } = true;
    public bool VisibleInLibrary { get; set; }
    public string UserVisibleStatus { get; set; } = string.Empty;
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
    public List<string> RecentUploadedKnowledgeIds { get; set; } = new();
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
    public List<string> UnresolvedThreads { get; set; } = new();
    public List<string> ReferencedKnowledgeIds { get; set; } = new();
    public List<string> ImportedKnowledgeIds { get; set; } = new();
    public List<KnowledgeInventoryItem> KnowledgeInventory { get; set; } = new();
    public List<string> UsedTropePatterns { get; set; } = new();
}

public sealed class AgentAuthorMemory
{
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
    public TurnIntent TurnIntent { get; set; } = new();
    public UserTurnEnvelope UserTurn { get; set; } = new();
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectTitle { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public string ActiveRunId { get; set; } = string.Empty;
    public string ProjectSummary { get; set; } = string.Empty;
    public List<string> RecentMessages { get; set; } = new();
    public List<AgentRuntimeObservation> RecentObservations { get; set; } = new();
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
}

public sealed class ToolSchema
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Risk { get; set; } = "Low";
    public bool RequiresConfirmation { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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
    public object? Data { get; set; }
    public AgentToolArtifact? Artifact { get; set; }
    public IReadOnlyList<string> Suggestions { get; set; } = Array.Empty<string>();
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
    private readonly AgentMissionTaskTreeService _taskTreeService;

    internal static void SetWorkspace(NovelAgentWorkspace workspace) => _currentWorkspace.Value = workspace;
    internal static void ClearWorkspace() => _currentWorkspace.Value = null;

    public AgentObservationBuilder(
        AgentToolRegistry toolRegistry,
        IAgentMemoryContextService memoryContextService,
        AgentMissionTaskTreeService taskTreeService)
    {
        _toolRegistry = toolRegistry;
        _memoryContextService = memoryContextService;
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
        var memoryContext = await _memoryContextService.BuildAsync(session.UserId, project.Id, session.SessionId, ct).ConfigureAwait(false);
        ApplyMemoryContext(session, project.Id, memoryContext);
        _taskTreeService.Sync(session, project, bible);
        var rag = await BuildRagAsync(session, bible, userMessage, ct).ConfigureAwait(false);

        // Check session cache for tools, otherwise expose only tool_search
        IReadOnlyList<ToolSchema> availableTools;

        if (!string.IsNullOrWhiteSpace(session.DiscoveredPhase) &&
            session.DiscoveredTools.Count > 0)
        {
            // Has cache, expose previously discovered tools
            availableTools = session.DiscoveredTools;
        }
        else
        {
            // No cache, expose only tool_search
            var toolSearchEntry = _toolRegistry.Find("tool_search");
            if (toolSearchEntry == null)
            {
                throw new InvalidOperationException("tool_search not found in registry");
            }

            availableTools = new List<ToolSchema>
            {
                new ToolSchema
                {
                    Name = "tool_search",
                    Description = toolSearchEntry.Description,
                    Risk = "Low",
                    RequiresConfirmation = false,
                    Parameters = new Dictionary<string, string> { { "phase", "string" } }
                }
            };
        }

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
            }).ToList(),
        };
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
        RecentUploadedKnowledgeIds = new List<string>(source.RecentUploadedKnowledgeIds),
        PendingToolName = source.PendingToolName,
        LastIntent = source.LastIntent
    };

    private static AgentProjectMemory MapProject(ProjectMemory source, string projectId) => new()
    {
        ProjectId = projectId,
        LongTermGoal = source.LongTermGoal ?? string.Empty,
        ReaderPromise = source.ReaderPromise ?? string.Empty,
        Constraints = new List<string>(source.Constraints),
        UnresolvedThreads = new List<string>(source.UnresolvedThreads),
        ReferencedKnowledgeIds = new List<string>(source.ReferencedKnowledgeIds),
        ImportedKnowledgeIds = new List<string>(source.ImportedKnowledgeIds),
        KnowledgeInventory = new List<KnowledgeInventoryItem>(source.KnowledgeInventory),
        UsedTropePatterns = new List<string>(source.UsedTropePatterns)
    };

    private static AgentAuthorMemory MapAuthor(AuthorMemory source) => new()
    {
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
        if (string.IsNullOrWhiteSpace(plan.OverallGoal) && LooksCreative(userMessage))
            plan.OverallGoal = userMessage.Trim();
        if (string.IsNullOrWhiteSpace(plan.CurrentNovelGoal))
            plan.CurrentNovelGoal = session.WorkingMemory.CurrentGoal;
        if (string.IsNullOrWhiteSpace(plan.CurrentNovelGoal) && LooksCreative(userMessage))
            plan.CurrentNovelGoal = userMessage.Trim();
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
        var stage = DetermineRagStage(session, bible, userMessage);
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

        if (LooksCreative(userMessage))
            Add("creativeKnowledge", userMessage, "用户本轮创作意图");

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

    private static string DetermineRagStage(AgentSession session, StoryBibleDocument bible, string userMessage)
    {
        var phase = session.Phase;
        if (bible.Constitution == null) return "foundation";
        if (bible.VolumeArcs.Count == 0 || phase.Contains("volume", StringComparison.OrdinalIgnoreCase)) return "volume_planning";
        if (phase.Contains("context", StringComparison.OrdinalIgnoreCase)) return "context_building";
        if (phase.Contains("draft", StringComparison.OrdinalIgnoreCase) || userMessage.Contains("正文")) return "draft_generation";
        if (phase.Contains("validated", StringComparison.OrdinalIgnoreCase) || phase.Contains("gate", StringComparison.OrdinalIgnoreCase)) return "gate_validation";
        if (phase.Contains("repair", StringComparison.OrdinalIgnoreCase) || userMessage.Contains("修复")) return "repair";
        if (phase.Contains("commit", StringComparison.OrdinalIgnoreCase) || userMessage.Contains("提交")) return "commit";
        if (phase.Contains("review", StringComparison.OrdinalIgnoreCase) || userMessage.Contains("复盘")) return "review";
        if (userMessage.Contains("章节") || userMessage.Contains("下一章")) return "chapter_planning";
        return LooksCreative(userMessage) ? "chapter_planning" : "free_chat";
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
        if (LooksCreative(userMessage))
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

    private static bool LooksCreative(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Contains("小说") || text.Contains("故事") || text.Contains("角色") || text.Contains("章节") ||
               text.Contains("卷") || text.Contains("设定") || text.Contains("写") || text.Contains("生成") ||
               text.Contains("规划") || text.Contains("知识库") || text.Contains("素材");
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
}

public sealed class AgentPlanner
{
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
            return BuildActionRuleFallback(context, "missing_llm_settings");

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
            catch { return BuildActionRuleFallback(context, "rate_limit_fallback"); }
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
            // Generic error — fall back to rule engine, don't expose error details to user
            return BuildActionRuleFallback(context, "planner_error_fallback");
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
            var json = await CompleteJsonAsync(settings, BuildReflectSystemPrompt(), BuildReflectUserPrompt(context, observation), ct)
                .ConfigureAwait(false);
            return ParseReflection(json);
        }
        catch
        {
            return BuildRuleReflection(context, observation);
        }
    }

    private static string BuildActionSystemPrompt(IReadOnlyList<AgentToolDefinition> tools)
    {
        var toolLines = string.Join("\n", tools.Select(t =>
            $"- {t.Name}: {t.Description} [Risk={t.Risk}]"));

        return "# Stable Layer - Identity & Rules\n\n" +
            "你是天命小说助手，一个专门帮助用户创作长篇小说的 AI 助手。\n" +
            "当用户问起你的身份、名字或你是谁时，回答你是天命小说助手，不要提及 Claude、Anthropic 或其他底层模型名称。\n" +
            "你的职责是帮助用户构思故事、规划章节、生成内容、管理创作进度。用自然、温暖的方式与用户对话。\n\n" +
            $"## Available Tools ({tools.Count})\n{toolLines}\n\n" +
            "## Decision Principles\n" +
            "1. Prioritize natural conversation. Use chat_reply for greetings, questions, status queries, and casual chat.\n" +
            "2. Use tool calls only when user explicitly requests an action (e.g., '开始写章节', '生成草稿', '提交章节').\n" +
            "3. For status queries like '进度如何' or '现在到哪了', use chat_reply with project context, NOT QueryProjectStatus tool.\n" +
            "4. Project management: Call StartNewNovelProject when user wants to create a new novel. For casual greetings or questions, use chat_reply to explain you can help create novels.\n" +
            "5. Use clarify when creative info is missing for an explicit action request.\n" +
            "6. Autopilot mode: when executing a writing workflow, proceed through steps without asking for confirmation.\n" +
            "7. Chapter generation workflow: BuildChapterContextPackage -> GenerateChapterWithChanges -> ValidateChapterDraft -> RepairChapterDraft or CommitValidatedChapter.\n" +
            "8. PlanChapter and PlanVolumeArc must NOT use userGoal parameter.\n" +
            "9. Do not repeat the same tool call. If result satisfies the need, use final_reply.\n" +
            "10. Use SearchCreativeKnowledge when more knowledge is needed.\n" +
            "11. Read anchor_context for working_memory, task_state, history context.\n" +
            "12. If recent_observations contains a repairable policy/guardrail observation, treat it as an environment fact: choose its recommended prerequisite tool or ask the user; do not repeat the blocked tool.\n\n" +
            "## 工具发现机制\n\n" +
            "你通过 tool_search 工具来动态发现可用工具。\n\n" +
            "**基本原则**：\n" +
            "1. 根据用户意图和当前任务，判断需要什么工具\n" +
            "2. 检查已缓存的工具是否满足需求\n" +
            "3. 如果缓存不满足，调用 tool_search(phase=\"阶段名\") 获取该阶段的工具\n" +
            "4. 工具缓存是上次 tool_search 的结果。如果缓存中的工具能满足需求，直接使用；如果缓存中没有你需要的工具，先调用 tool_search 发现新工具。\n\n" +
            "**阶段说明**（供参考，不是硬性规则）：\n" +
            "- Conversation: 闲聊、问候、状态查询\n" +
            "- Planning: 规划故事地基、卷、章节\n" +
            "- Creation: 生成章节正文、修复草稿\n" +
            "- Review: 提交章节、复盘\n" +
            "- All: 查看所有可用工具\n\n" +
            "**示例**：\n" +
            "- 用户说\"你好\" → 缓存里已有 tool_search，不需要其他工具 → 用 chat_reply\n" +
            "- 用户说\"创建新小说\" → 需要 StartNewNovelProject → 如果缓存里没有，调用 tool_search(phase=\"Planning\")\n" +
            "- 正在规划阶段，用户说\"开始写\" → 需要生成工具 → 调用 tool_search(phase=\"Creation\")\n\n" +
            "## 任务执行原则\n" +
            "采用'先执行后修正'模式，不要频繁请求用户确认：\n" +
            "1. 理解用户意图后，使用tool_search找到工具，直接执行\n" +
            "2. 执行后告知用户结果和下一步计划\n" +
            "3. 如果用户不满意，会主动告诉你如何调整\n" +
            "4. 工作流自带校验机制（如ValidateChapterDraft），发现问题自动修复\n\n" +
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
        - styleLikes: 喜欢的写作风格
        - styleDislikes: 反感的风格

        ## memoryUpdate.executionMemory
        - toolSuccess: 工具成功经验
        - toolFailure: 工具失败原因

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
                NewConstraints = ReadStringArray(project, "new_constraints", "newConstraints"),
                UnresolvedThreads = ReadStringArray(project, "unresolved_threads", "unresolvedThreads")
            };
        }

        if (TryGetObject(root, out var author, "author_memory", "authorMemory"))
        {
            update.AuthorMemory = new AuthorMemoryUpdate
            {
                StyleLikes = ReadStringArray(author, "style_likes", "styleLikes"),
                StyleDislikes = ReadStringArray(author, "style_dislikes", "styleDislikes")
            };
        }

        if (TryGetObject(root, out var execution, "execution_memory", "executionMemory"))
        {
            update.ExecutionMemory = new ExecutionMemoryUpdate
            {
                ToolSuccess = ReadOptionalString(execution, null, "tool_success", "toolSuccess"),
                ToolFailure = ReadOptionalString(execution, null, "tool_failure", "toolFailure")
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

    private static AgentAction BuildActionRuleFallback(AgentObservationContext context, string source)
    {
        var msg = context.UserMessage.Trim().ToLowerInvariant();

        // Legacy pending confirmations are treated as resumable autopilot work.
        if (context.PendingConfirmation?.ToolCall != null && !msg.Contains("取消") && !msg.Contains("不要") && !msg.Contains("先不"))
        {
            return new AgentAction
            {
                Type = AgentActionType.ToolCall,
                Intent = "resume_pending_tool",
                ToolCall = context.PendingConfirmation.ToolCall,
                Risk = context.PendingConfirmation.Risk,
                Source = "pending_confirmation",
            };
        }

        if (context.PendingConfirmation != null && (msg.Contains("取消") || msg.Contains("不要") || msg.Contains("先不")))
        {
            return new AgentAction
            {
                Type = AgentActionType.FinalReply,
                Intent = "cancel_pending_confirmation",
                Reply = "好，已取消这个待确认动作。",
                Suggestions = new[] { "查看当前状态", "继续调整" },
                Source = "pending_cancel",
            };
        }

        if (source == "missing_llm_settings" && context.TurnIntent.Type == TurnIntentType.FreeChat)
        {
            return new AgentAction
            {
                Type = AgentActionType.ChatReply,
                Intent = "free_chat",
                Reply = "我是天命小说 Agent，负责和你一起管理长篇小说的设定、章节草稿、门禁校验、质量反思和提交入库。当前还没有配置可用的模型服务，所以我会先用本地能力回答基础问题；配置模型后，我可以进行更完整的创作决策和工具调用。",
                Suggestions = new[] { "查看当前状态", "配置模型服务", "写一本新小说" },
                Source = source,
            };
        }

        if (source == "missing_llm_settings")
        {
            return new AgentAction
            {
                Type = AgentActionType.ChatReply,
                Intent = context.TurnIntent.Label,
                IsNoTool = true,
                Source = source,
            };
        }

        // Minimal fallback — try scheduler, then status query
        var scheduled = context.MissionPlan.SchedulerState.Tasks
            .FirstOrDefault(t => t.Status is "running" or "queued");
        if (scheduled != null)
        {
            return new AgentAction
            {
                Type = AgentActionType.ToolCall,
                Intent = "continue_mission",
                ToolCall = new AgentToolCall { Name = "QueryProjectStatus" },
                Source = source,
            };
        }

        return new AgentAction
        {
            Type = AgentActionType.ChatReply,
            Intent = "free_chat",
            Reply = "我在。告诉我你想推进什么。",
            Suggestions = new[] { "查看当前状态", "写一本新小说" },
            Source = source,
        };
    }

    private static AgentReflection BuildRuleReflection(AgentObservationContext context, AgentRuntimeObservation observation)
    {
        if (observation.ObservationType is "policy_observation" or "runtime_observation")
            return BuildGovernanceRuleReflection(context, observation);

        var requiresInput = !observation.Success ||
                            observation.Phase.Contains("awaiting_user", StringComparison.OrdinalIgnoreCase) ||
                            observation.Phase.Contains("foundation_intake", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(observation.ToolName, "StartNewNovelProject", StringComparison.OrdinalIgnoreCase);
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
            MissionPatch = BuildRuleMissionPatch(observation, qualityGate, null),
            // Don't recommend next tool — let LLM decide
        };
    }

    private static AgentReflection BuildGovernanceRuleReflection(AgentObservationContext context, AgentRuntimeObservation observation)
    {
        var isProjectStart = string.Equals(observation.ToolName, "StartNewNovelProject", StringComparison.OrdinalIgnoreCase);
        var reply = isProjectStart || observation.Phase.Contains("foundation", StringComparison.OrdinalIgnoreCase)
            ? "这本新小说的工程已经在当前会话里准备好了。下一步我需要你补齐故事地基：类型、核心钩子、主角引擎、主要阅读快感，以及明确不要的方向。"
            : BuildNaturalGovernanceReply(context, observation);

        return new AgentReflection
        {
            Summary = "治理层观察已进入任务反思。",
            GoalSatisfied = true,
            ShouldContinue = false,
            RequiresUserInput = true,
            NextIntent = observation.Phase.Contains("foundation", StringComparison.OrdinalIgnoreCase) ? "foundation_intake" : "await_user",
            ReplyDraft = reply,
            NewTodoItems = new List<string> { "等待作者补充下一步输入" },
            Blockers = new List<string>(),
            QualityGate = new AgentQualityGateReport { Status = "not_applicable" },
            MissionPatch = new AgentMissionPatch
            {
                Status = "blocked",
                Stage = observation.Phase.Contains("foundation", StringComparison.OrdinalIgnoreCase) ? "foundation" : "await_user",
                CurrentFocus = context.ActiveRunId,
            },
        };
    }

    private static string BuildNaturalGovernanceReply(AgentObservationContext context, AgentRuntimeObservation observation)
    {
        if (context.TurnIntent.Type == TurnIntentType.StatusQuery)
            return "我查了一下当前会话的任务黑板：这轮更像是在问进度，而不是要新建或重写内容。你可以让我查看当前状态，或直接说要继续推进哪一章。";
        if (context.MissionPlan.AllowedNextActions.Count > 0)
            return $"当前可以推进的下一步是：{string.Join("、", context.MissionPlan.AllowedNextActions.Take(3))}。你可以让我继续，或补充新的创作要求。";
        return "我没有继续执行新的写入动作。你可以告诉我现在要查看状态、继续任务，还是补充新的创作简报。";
    }

    private static AgentQualityGateReport BuildRuleQualityGate(AgentRuntimeObservation observation)
    {
        var isWritingStep = observation.ToolName is "GenerateChapterWithChanges" or "ValidateChapterDraft" or "RepairChapterDraft" or "CommitValidatedChapter" ||
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
                RewriteDecision = "结构化门禁通过，规则兜底未发现质量阻塞；可以继续自动提交。",
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

    private static AgentToolCall? BuildRuleNextTool(AgentRuntimeObservation observation, AgentQualityGateReport qualityGate)
    {
        // Don't recommend next tool from rules — let LLM decide
        return null;
    }

    private static AgentMissionPatch BuildRuleMissionPatch(
        AgentRuntimeObservation observation,
        AgentQualityGateReport qualityGate,
        AgentToolCall? nextTool)
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
                NextAction = nextTool?.Name ?? string.Empty,
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

    private static string ExtractJsonObject(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        return start >= 0 && end > start ? raw[start..(end + 1)] : raw;
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

    private static bool IsExplicitConfirmation(string msg) =>
        msg.Contains("确认") ||
        msg.Contains("提交") ||
        msg.Contains("就选") ||
        msg.Contains("选这个") ||
        msg.Contains("按推荐") ||
        msg == "好的" ||
        msg == "可以" ||
        msg == "ok" ||
        msg == "yes";
}
