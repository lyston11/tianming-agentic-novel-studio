using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Web.NovelAgentWeb.DTOs;

public sealed class AgentSseEvent
{
    public string EventId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string? RunId { get; set; }
    public string SourceMessageId { get; set; } = string.Empty;
    public string? StepId { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ArtifactType { get; set; } = string.Empty;
    public string ArtifactId { get; set; } = string.Empty;
    public string DisplaySurface { get; set; } = string.Empty;
    public string DisplayPolicy { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public object? Data { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public static class AgentSseEventType
{
    public const string AgentThinking = "agent_thinking";
    public const string AgentObserving = "agent_observing";
    public const string AgentPlanning = "agent_planning";
    public const string AgentActing = "agent_acting";
    public const string AgentReflecting = "agent_reflecting";
    public const string MissionUpdated = "mission_updated";
    public const string RunCreated = "run_created";
    public const string ArtifactPreview = "artifact_preview";
    public const string StepStart = "step_start";
    public const string StepComplete = "step_complete";
    public const string StepFail = "step_fail";
    public const string StepWaiting = "step_waiting";
    public const string RunUpdate = "run_update";
    public const string ConfirmationRequired = "confirmation_required";
    public const string AgentReplyDelta = "agent_reply_delta";
    public const string AgentReply = "agent_reply";
}
