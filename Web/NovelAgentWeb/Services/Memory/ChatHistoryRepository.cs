using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Caching;

namespace TM.Web.NovelAgentWeb.Services.Memory;

public class ChatHistoryRepository : IChatHistoryRepository
{
    private static readonly TimeSpan HotWindowTtl = TimeSpan.FromMinutes(10);
    private const int HotWindowSize = 20;

    private readonly NovelAgentDbContext _context;
    private readonly IDistributedCacheService _redisCache;
    private readonly IMemoryCacheService _memoryCache;
    private readonly ILogger<ChatHistoryRepository> _logger;

    public ChatHistoryRepository(
        NovelAgentDbContext context,
        IDistributedCacheService redisCache,
        IMemoryCacheService memoryCache,
        ILogger<ChatHistoryRepository> logger)
    {
        _context = context;
        _redisCache = redisCache;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    public async Task AppendAsync(
        string userId,
        string? projectId,
        string sessionId,
        string role,
        string content,
        CancellationToken ct = default)
    {
        var trimmed = content.Trim();
        var nextTurnIndex = await _context.AgentChatTurns
            .Where(t => t.SessionId == sessionId)
            .Select(t => (int?)t.TurnIndex)
            .MaxAsync(ct) ?? 0;

        _context.AgentChatTurns.Add(new AgentChatTurn
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            ProjectId = string.IsNullOrWhiteSpace(projectId) ? null : projectId,
            SessionId = sessionId,
            TurnIndex = nextTurnIndex + 1,
            Role = role.Trim(),
            Content = trimmed,
            TokenCount = EstimateTokenCount(trimmed),
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(ct);
        await WriteHotWindowAsync(userId, sessionId, ct);
    }

    public async Task SaveSummaryAsync(
        string userId,
        string? projectId,
        string sessionId,
        int startTurn,
        int endTurn,
        string summaryType,
        string content,
        IReadOnlyList<string> keyDecisions,
        CancellationToken ct = default)
    {
        _context.AgentChatSummaries.Add(new AgentChatSummary
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            ProjectId = string.IsNullOrWhiteSpace(projectId) ? null : projectId,
            SessionId = sessionId,
            StartTurn = startTurn,
            EndTurn = endTurn,
            SummaryType = string.IsNullOrWhiteSpace(summaryType) ? "summary" : summaryType.Trim(),
            Content = content.Trim(),
            KeyDecisionsJson = JsonSerializer.Serialize(keyDecisions),
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(ct);
    }

    public async Task<ChatPromptWindowDto> GetPromptWindowAsync(
        string userId,
        string? projectId,
        string sessionId,
        CancellationToken ct = default)
    {
        var normalizedProjectId = string.IsNullOrWhiteSpace(projectId) ? null : projectId;
        var summariesQuery = _context.AgentChatSummaries
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.ProjectId == normalizedProjectId && s.SessionId == sessionId);

        var metaSummary = await summariesQuery
            .Where(s => s.SummaryType == "meta")
            .OrderByDescending(s => s.EndTurn)
            .ThenByDescending(s => s.CreatedAt)
            .Select(s => s.Content)
            .FirstOrDefaultAsync(ct);

        var summaryRows = await summariesQuery
            .Where(s => s.SummaryType != "meta")
            .OrderBy(s => s.StartTurn)
            .ThenBy(s => s.EndTurn)
            .ThenBy(s => s.CreatedAt)
            .ToListAsync(ct);
        var summaries = summaryRows
            .Select(s => new ChatHistorySummaryDto(
                s.StartTurn,
                s.EndTurn,
                s.SummaryType,
                s.Content,
                DeserializeKeyDecisions(s.KeyDecisionsJson)))
            .ToList();

        var recentMessages = await LoadRecentTurnsAsync(userId, sessionId, ct);

        return new ChatPromptWindowDto(metaSummary, summaries, recentMessages);
    }

    private async Task WriteHotWindowAsync(string userId, string sessionId, CancellationToken ct)
    {
        var hotWindow = await LoadRecentTurnsAsync(userId, sessionId, ct);
        var key = AgentMemoryKeys.ChatHot(userId, sessionId);

        await _redisCache.SetAsync(key, hotWindow, HotWindowTtl, ct);
        _memoryCache.Set(key, hotWindow, HotWindowTtl);

        _logger.LogDebug("Updated chat hot window for user {UserId}, session {SessionId}", userId, sessionId);
    }

    private async Task<List<ChatHistoryTurnDto>> LoadRecentTurnsAsync(string userId, string sessionId, CancellationToken ct)
    {
        var turns = await _context.AgentChatTurns
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.SessionId == sessionId)
            .OrderByDescending(t => t.TurnIndex)
            .Take(HotWindowSize)
            .OrderBy(t => t.TurnIndex)
            .Select(t => new ChatHistoryTurnDto(t.Role, t.Content, t.CreatedAt))
            .ToListAsync(ct);

        return turns;
    }

    private static IReadOnlyList<string> DeserializeKeyDecisions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();

        try
        {
            var decisions = JsonSerializer.Deserialize<List<string>>(json);
            return decisions ?? new List<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static int EstimateTokenCount(string content) =>
        string.IsNullOrWhiteSpace(content) ? 0 : Math.Max(1, content.Length / 2);
}
