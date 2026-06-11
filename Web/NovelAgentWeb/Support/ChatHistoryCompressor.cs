namespace TM.Web.NovelAgentWeb.Support;

/// <summary>
/// Compresses chat history using layered summarization.
/// Strategy: Recent 5 messages + Summaries (per 10 turns) + MetaSummary (30+ turns)
/// </summary>
public class ChatHistoryCompressor
{
    private readonly ILogger<ChatHistoryCompressor> _logger;

    public ChatHistoryCompressor(ILogger<ChatHistoryCompressor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Compress chat history based on turn count.
    /// </summary>
    public async Task<LayeredChatHistory> CompressAsync(List<AgentConversationTurn> chatHistory, CancellationToken ct = default)
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

        return layered;
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
