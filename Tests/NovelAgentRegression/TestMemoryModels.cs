using System;
using System.Collections.Generic;

namespace TM.Web.NovelAgentWeb.Support;

// Test-only definitions of the new three-tier memory architecture models
// These mirror the definitions in AgentCore.cs for regression testing

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
}

public sealed class AgentMissionPlan
{
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
}
