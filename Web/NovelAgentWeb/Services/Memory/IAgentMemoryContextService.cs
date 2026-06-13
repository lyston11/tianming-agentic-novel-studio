namespace TM.Web.NovelAgentWeb.Services.Memory;

public record AgentMemoryContextDto(
    ChatMemoryContext Chat,
    SessionMemory Session,
    ProjectMemory Project,
    AuthorMemory Author,
    ExecutionMemory Execution);

public record ChatMemoryContext(
    string? MetaSummary,
    IReadOnlyList<ChatHistorySummaryDto> RecentSummaries,
    IReadOnlyList<ChatHistoryTurnDto> RecentMessages);

public interface IAgentMemoryContextService
{
    Task<AgentMemoryContextDto> BuildAsync(
        string userId,
        string projectId,
        string sessionId,
        CancellationToken ct = default);
}
