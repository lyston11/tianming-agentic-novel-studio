namespace Tianming.NovelAgent.Contracts.Streaming;

public enum AgentStreamKind
{
    Conversation,
    Workflow
}

public sealed record AgentEventEnvelope<T>(
    string EventId,
    AgentStreamKind StreamKind,
    string StreamId,
    long Sequence,
    string EventType,
    int SchemaVersion,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string? CausationId,
    string? ProjectId,
    string? SessionId,
    string? GoalId,
    string? ProductionId,
    bool Transient,
    T Data);

public readonly record struct AgentEventCursor(AgentStreamKind StreamKind, string StreamId, long Sequence)
{
    public override string ToString() => $"{StreamKind.ToString().ToLowerInvariant()}:{StreamId}:{Sequence}";

    public static bool TryParse(string? value, out AgentEventCursor cursor)
    {
        cursor = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var parts = value.Split(':', 3, StringSplitOptions.TrimEntries);
        return parts.Length == 3
            && Enum.TryParse(parts[0], true, out AgentStreamKind kind)
            && !string.IsNullOrWhiteSpace(parts[1])
            && long.TryParse(parts[2], out var sequence)
            && sequence >= 0
            && Assign(kind, parts[1], sequence, out cursor);
    }

    private static bool Assign(AgentStreamKind kind, string streamId, long sequence, out AgentEventCursor cursor)
    {
        cursor = new AgentEventCursor(kind, streamId, sequence);
        return true;
    }
}
