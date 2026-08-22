using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.DTOs;

public sealed class RuntimeRunDto
{
    public string RunId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public string? ProjectId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public string CurrentPhase { get; init; } = string.Empty;
    public int CurrentStep { get; init; }
    public string ActiveTool { get; init; } = string.Empty;
    public string LastMessage { get; init; } = string.Empty;
    public string SourceMessageId { get; init; } = string.Empty;
    public string IdempotencyKey { get; init; } = string.Empty;
    public string BudgetJson { get; init; } = "{}";
    public string ResultJson { get; init; } = "{}";
    public string ErrorMessage { get; init; } = string.Empty;
    public string FailureJson { get; init; } = "{}";
    public bool CancelRequested { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    public static RuntimeRunDto FromEntity(AgentRuntimeRun run) =>
        new()
        {
            RunId = run.Id,
            UserId = run.UserId,
            SessionId = run.SessionId,
            ProjectId = run.ProjectId,
            Status = run.Status,
            Mode = run.Mode,
            CurrentPhase = run.CurrentPhase,
            CurrentStep = run.CurrentStep,
            ActiveTool = run.ActiveTool,
            LastMessage = run.LastMessage,
            SourceMessageId = run.SourceMessageId,
            IdempotencyKey = run.IdempotencyKey,
            BudgetJson = run.BudgetJson,
            ResultJson = run.ResultJson,
            ErrorMessage = run.ErrorMessage,
            FailureJson = run.FailureJson,
            CancelRequested = run.CancelRequested,
            StartedAt = run.StartedAt,
            CompletedAt = run.CompletedAt,
            CreatedAt = run.CreatedAt,
            UpdatedAt = run.UpdatedAt
        };
}

public sealed class RuntimeActiveRunDto
{
    public bool HasActiveRun { get; init; }
    public string Status { get; init; } = "idle";
    public RuntimeRunDto? Run { get; init; }
    public DateTime? HeartbeatAt { get; init; }
    public bool FromDistributedCache { get; init; }

    public static RuntimeActiveRunDto Empty() =>
        new()
        {
            HasActiveRun = false,
            Status = "idle"
        };

    public static RuntimeActiveRunDto FromState(TM.Web.NovelAgentWeb.Services.AgentRuntime.AgentRuntimeActiveRunState state) =>
        new()
        {
            HasActiveRun = true,
            Status = state.Run.Status,
            Run = RuntimeRunDto.FromEntity(state.Run),
            HeartbeatAt = state.HeartbeatAt,
            FromDistributedCache = state.FromDistributedCache
        };
}

public sealed class RuntimeEventDto
{
    public string EventId { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string SourceMessageId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public string? ProjectId { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Stage { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string ArtifactType { get; init; } = string.Empty;
    public string ArtifactId { get; init; } = string.Empty;
    public string DisplaySurface { get; init; } = string.Empty;
    public string DisplayPolicy { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string DataJson { get; init; } = "{}";
    public DateTime CreatedAt { get; init; }

    public static RuntimeEventDto FromEntity(AgentRuntimeEvent evt) =>
        new()
        {
            EventId = evt.Id,
            RunId = evt.RuntimeRunId,
            SourceMessageId = ExtractSourceMessageId(evt.DataJson),
            UserId = evt.UserId,
            SessionId = evt.SessionId,
            ProjectId = evt.ProjectId,
            Type = evt.Type,
            Stage = evt.Stage,
            Status = evt.Status,
            ArtifactType = evt.ArtifactType,
            ArtifactId = evt.ArtifactId,
            DisplaySurface = evt.DisplaySurface,
            DisplayPolicy = evt.DisplayPolicy,
            Message = evt.Message,
            DataJson = evt.DataJson,
            CreatedAt = evt.CreatedAt
        };

    private static string ExtractSourceMessageId(string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
            return string.Empty;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(dataJson);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                return string.Empty;
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "sourceMessageId", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                    return property.Value.GetString() ?? string.Empty;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            return string.Empty;
        }

        return string.Empty;
    }
}

public sealed class RuntimeInterruptRequest
{
    public string Kind { get; init; } = "freeform";
    public string Message { get; init; } = string.Empty;
    public int Priority { get; init; } = 50;
}

public sealed class RuntimeInterruptDto
{
    public string InterruptId { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public string? ProjectId { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int Priority { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }

    public static RuntimeInterruptDto FromEntity(AgentInterrupt interrupt) =>
        new()
        {
            InterruptId = interrupt.Id,
            RunId = interrupt.RuntimeRunId,
            UserId = interrupt.UserId,
            SessionId = interrupt.SessionId,
            ProjectId = interrupt.ProjectId,
            Kind = interrupt.Kind,
            Status = interrupt.Status,
            Priority = interrupt.Priority,
            Message = interrupt.Message,
            CreatedAt = interrupt.CreatedAt
        };
}
