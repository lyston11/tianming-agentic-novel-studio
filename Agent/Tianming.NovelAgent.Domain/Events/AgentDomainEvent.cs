namespace Tianming.NovelAgent.Domain.Events;

public sealed record AgentDomainEvent(
    string EventId,
    string EventType,
    string AggregateType,
    string AggregateId,
    long AggregateVersion,
    string UserId,
    string ProjectId,
    string? GoalId,
    string CorrelationId,
    string? CausationId,
    DateTimeOffset OccurredAt,
    string PayloadJson);
