namespace TM.Web.NovelAgentWeb.Services.Memory;

public record ChatHistoryTurnDto(string Role, string Content, DateTime CreatedAt, string TurnId = "", int TurnIndex = 0);
public record ChatHistorySummaryDto(int StartTurn, int EndTurn, string SummaryType, string Content, IReadOnlyList<string> KeyDecisions);
public record ChatPromptWindowDto(string? MetaSummary, IReadOnlyList<ChatHistorySummaryDto> Summaries, IReadOnlyList<ChatHistoryTurnDto> RecentMessages);

public interface IChatHistoryRepository
{
    Task AppendAsync(string userId, string? projectId, string sessionId, string role, string content, CancellationToken ct = default);
    Task<bool> ReplaceLastAssistantTurnAsync(string userId, string? projectId, string sessionId, string expectedContent, string replacementContent, CancellationToken ct = default);
    Task SaveSummaryAsync(string userId, string? projectId, string sessionId, int startTurn, int endTurn, string summaryType, string content, IReadOnlyList<string> keyDecisions, CancellationToken ct = default);
    Task<ChatPromptWindowDto> GetPromptWindowAsync(string userId, string? projectId, string sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<ChatHistoryTurnDto>> GetHotWindowAsync(string userId, string? projectId, string sessionId, CancellationToken ct = default);
}
