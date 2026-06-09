using System;
using System.Collections.Generic;

namespace TM.Web.NovelAgentWeb.Support;

// Test-only definitions of the new three-tier memory architecture models
// These mirror the definitions in AgentCore.cs for regression testing

public enum TurnIntentType
{
    FreeChat,
    StatusQuery,
    Confirmation,
    Cancel,
    NewProjectSeed,
    CreativeBrief,
    ContinueMission,
    RevisionRequest,
    UserFeedback,
    ProjectSwitch,
    CandidateSelection,
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
    public List<object> ChatHistory { get; set; } = new();

    [System.Text.Json.Serialization.JsonPropertyName("currentGoal")]
    public string CurrentGoal { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("openQuestions")]
    public List<string> OpenQuestions { get; set; } = new();

    [System.Text.Json.Serialization.JsonPropertyName("recentObservations")]
    public List<object> RecentObservations { get; set; } = new();

    [System.Text.Json.Serialization.JsonPropertyName("pendingToolCall")]
    public object? PendingToolCall { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("pendingConfirmation")]
    public object? PendingConfirmation { get; set; }
}

public sealed class AgentRuntimeContext
{
    [System.Text.Json.Serialization.JsonPropertyName("user")]
    public UserProfile User { get; set; } = new();

    [System.Text.Json.Serialization.JsonPropertyName("activeProject")]
    public object? ActiveProject { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("session")]
    public SessionContext Session { get; set; } = new();

    [System.Text.Json.Serialization.JsonPropertyName("mission")]
    public AgentMissionState Mission { get; set; } = new();

    [System.Text.Json.Serialization.JsonPropertyName("missionPlan")]
    public AgentMissionPlan MissionPlan { get; set; } = new();
}

// Stub types to satisfy compilation
public sealed class AgentMissionState
{
    public string CurrentGoal { get; set; } = string.Empty;
    public string CreativePhase { get; set; } = "idle";
    public string Readiness { get; set; } = "unknown";
    public string NextIntent { get; set; } = string.Empty;
    public AgentMissionPlan? MissionPlan { get; set; }
}

public sealed class AgentMissionPlan
{
    public string MissionId { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectTitle { get; set; } = string.Empty;
    public string Status { get; set; } = "idle";
    public string CurrentObjective { get; set; } = string.Empty;
    public string Stage { get; set; } = "idle";
    public AgentTaskSchedulerState? SchedulerState { get; set; }
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

public sealed class AgentToolCall
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string> Arguments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AgentToolExecutionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? RunId { get; set; }
    public bool RequiresConfirmation { get; set; }
    public string Risk { get; set; } = "Low";
}

public sealed class AgentSession
{
    public string SessionId { get; set; } = string.Empty;
    public string? ActiveRunId { get; set; }
    public AgentWorkingMemory WorkingMemory { get; set; } = new();
    public List<object>? ChatHistory { get; set; }
}

public sealed class AgentWorkingMemory
{
    public string? SelectedChapterCandidateId { get; set; }
    public string? LastBuiltContextRunId { get; set; }
    public string? CurrentGoal { get; set; }
    public AgentSessionMemory? SessionMemory { get; set; }
    public AgentProjectMemory? ProjectMemory { get; set; }
    public AgentAuthorMemory? AuthorMemory { get; set; }
    public AgentExecutionMemory? ExecutionMemory { get; set; }
    public List<string> UserPreferences { get; set; } = new();
    public object? PendingToolCall { get; set; }
    public AgentMissionPlan MissionPlan { get; set; } = new();
}

// Memory layer types for AgentMemoryService
public sealed class AgentSessionMemory
{
    public string ChatSummary { get; set; } = string.Empty;
    public List<string> ShortTermPreferences { get; set; } = new();
    public List<string> LastObservations { get; set; } = new();
}

public sealed class AgentProjectMemory
{
    public string ProjectId { get; set; } = string.Empty;
    public string LongTermGoal { get; set; } = string.Empty;
    public string ReaderPromise { get; set; } = string.Empty;
    public string Tone { get; set; } = string.Empty;
    public List<string> Constraints { get; set; } = new();
    public List<string> UnresolvedThreads { get; set; } = new();
}

public sealed class AgentAuthorMemory
{
    public List<string> StyleLikes { get; set; } = new();
    public List<string> StyleDislikes { get; set; } = new();
    public string ConfirmationTolerance { get; set; } = "key_checkpoints";
    public List<string> GenreHabits { get; set; } = new();
}

public sealed class AgentExecutionMemory
{
    public List<string> ToolFailurePatterns { get; set; } = new();
    public List<string> RepeatedBlockers { get; set; } = new();
    public List<string> SuccessfulRepairNotes { get; set; } = new();
}

public sealed class AgentReflection
{
    public string Summary { get; set; } = string.Empty;
    public AgentQualityGateReport QualityGate { get; set; } = new();
    public AgentMissionPatch MissionPatch { get; set; } = new();
}

public sealed class AgentQualityGateReport
{
    public string Status { get; set; } = "not_applicable";
    public string RewriteDecision { get; set; } = string.Empty;
}

public sealed class AgentMissionPatch
{
    public List<AgentChapterTaskPatch> ChapterPatches { get; set; } = new();
}

public sealed class AgentChapterTaskPatch
{
    public string ChapterId { get; set; } = string.Empty;
    public string QualityIssueSummary { get; set; } = string.Empty;
}

