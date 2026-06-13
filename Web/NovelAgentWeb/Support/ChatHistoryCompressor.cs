using TM.Web.NovelAgentWeb.Services.Memory;

namespace TM.Web.NovelAgentWeb.Support;

/// <summary>
/// Compresses chat history using layered summarization.
/// Strategy: Recent 5 messages + Summaries (per 10 turns) + MetaSummary (30+ turns)
/// </summary>
public class ChatHistoryCompressor
{
    private readonly ILogger<ChatHistoryCompressor> _logger;
    private readonly IChatHistoryRepository _chatHistory;

    public ChatHistoryCompressor(ILogger<ChatHistoryCompressor> logger, IChatHistoryRepository chatHistory)
    {
        _logger = logger;
        _chatHistory = chatHistory;
    }

    /// <summary>
    /// Compress chat history based on turn count.
    /// </summary>
    public Task<LayeredChatHistory> CompressAsync(List<AgentConversationTurn> chatHistory, CancellationToken ct = default) =>
        CompressCoreAsync(chatHistory, ct);

    public async Task<LayeredChatHistory> CompressAndPersistAsync(
        string userId,
        string? projectId,
        string sessionId,
        List<AgentConversationTurn> chatHistory,
        CancellationToken ct = default)
    {
        var layered = await CompressAsync(chatHistory, ct);
        await SaveSummariesAsync(userId, projectId, sessionId, layered, ct);
        return layered;
    }

    protected virtual Task<LayeredChatHistory> CompressCoreAsync(List<AgentConversationTurn> chatHistory, CancellationToken ct = default)
    {
        var turnCount = chatHistory.Count / 2; // User messages only

        var layered = new LayeredChatHistory
        {
            RecentMessages = chatHistory.TakeLast(5).Select(MapToChatMessage).ToList()
        };

        // Generate summaries every 10 turns (Task 14 will implement)
        if (turnCount >= 10)
        {
            _logger.LogDebug("Chat history has {TurnCount} turns, compression needed", turnCount);
            // TODO: Generate summaries in Task 14
        }

        // Generate meta-summary for 30+ turns (Task 14 will implement)
        if (turnCount >= 30)
        {
            _logger.LogDebug("Chat history has {TurnCount} turns, meta-summary needed", turnCount);
            // TODO: Generate meta-summary in Task 14
        }

        return Task.FromResult(layered);
    }

    public async Task SaveSummariesAsync(
        string userId,
        string? projectId,
        string sessionId,
        LayeredChatHistory layered,
        CancellationToken ct = default)
    {
        foreach (var summary in layered.Summaries)
        {
            await _chatHistory.SaveSummaryAsync(
                userId,
                projectId,
                sessionId,
                summary.StartTurn,
                summary.EndTurn,
                "summary",
                summary.Content,
                summary.KeyDecisions,
                ct);
        }

        if (!string.IsNullOrWhiteSpace(layered.MetaSummary))
        {
            var endTurn = layered.Summaries.Count == 0
                ? 0
                : layered.Summaries.Max(s => s.EndTurn);
            if (endTurn <= 0)
                return;

            await _chatHistory.SaveSummaryAsync(
                userId,
                projectId,
                sessionId,
                1,
                endTurn,
                "meta",
                layered.MetaSummary,
                Array.Empty<string>(),
                ct);
        }
    }

    private static ChatMessage MapToChatMessage(AgentConversationTurn turn)
    {
        return new ChatMessage
        {
            Role = turn.Role,
            Content = turn.Content,
            CreatedAt = DateTime.UtcNow
        };
    }
}
