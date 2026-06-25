using TM.Web.NovelAgentWeb.Services.Memory;

namespace TM.Web.NovelAgentWeb.Support;

/// <summary>
/// Compresses chat history using layered summarization.
/// Strategy: Recent 5 messages + Summaries (per 10 turns) + MetaSummary (30+ turns)
/// </summary>
public class ChatHistoryCompressor
{
    private const int RecentMessageCount = 5;
    private const int SummaryTurnInterval = 10;
    private const int MetaSummaryTurnThreshold = 30;
    private const int SummarySnippetLength = 80;

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
            RecentMessages = chatHistory.TakeLast(RecentMessageCount).Select(MapToChatMessage).ToList()
        };

        if (turnCount >= SummaryTurnInterval)
        {
            _logger.LogDebug("Chat history has {TurnCount} turns, compression needed", turnCount);
            var fullSummaryBlocks = turnCount / SummaryTurnInterval;
            for (var blockIndex = 0; blockIndex < fullSummaryBlocks; blockIndex++)
            {
                var startTurn = blockIndex * SummaryTurnInterval + 1;
                var endTurn = (blockIndex + 1) * SummaryTurnInterval;
                var blockMessages = chatHistory
                    .Skip(blockIndex * SummaryTurnInterval * 2)
                    .Take(SummaryTurnInterval * 2)
                    .ToList();

                layered.Summaries.Add(new ChatSummary
                {
                    StartTurn = startTurn,
                    EndTurn = endTurn,
                    Content = BuildExtractiveSummary(startTurn, endTurn, blockMessages),
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        if (turnCount >= MetaSummaryTurnThreshold && layered.Summaries.Count > 0)
        {
            _logger.LogDebug("Chat history has {TurnCount} turns, meta-summary needed", turnCount);
            layered.MetaSummary = BuildMetaSummary(turnCount, layered.Summaries);
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

    private static string BuildExtractiveSummary(
        int startTurn,
        int endTurn,
        IReadOnlyList<AgentConversationTurn> messages)
    {
        var first = messages.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.Content))?.Content ?? string.Empty;
        var last = messages.LastOrDefault(m => !string.IsNullOrWhiteSpace(m.Content))?.Content ?? string.Empty;
        return $"第 {startTurn}-{endTurn} 轮摘要：{Truncate(first)} / {Truncate(last)}";
    }

    private static string BuildMetaSummary(int turnCount, IReadOnlyList<ChatSummary> summaries)
    {
        var coveredRange = $"{summaries.Min(s => s.StartTurn)}-{summaries.Max(s => s.EndTurn)}";
        var decisions = summaries
            .SelectMany(s => s.KeyDecisions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
        var decisionText = decisions.Count == 0 ? "暂无显式决策" : string.Join("；", decisions);
        return $"已压缩 {turnCount} 轮对话，覆盖轮次 {coveredRange}。关键决策：{decisionText}";
    }

    private static string Truncate(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= SummarySnippetLength
            ? trimmed
            : trimmed[..SummarySnippetLength];
    }
}
