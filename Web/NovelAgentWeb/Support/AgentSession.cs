using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class AgentSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "新会话";
    public string Phase { get; set; } = "idle";
    public string ActiveProjectId { get; set; } = string.Empty;
    public string? ActiveRunId { get; set; }
    public bool IsArchived { get; set; }
    public List<string> RunHistory { get; set; } = new();
    public List<AgentConversationTurn> ChatHistory { get; set; } = new();
    public AgentWorkingMemory WorkingMemory { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Tool Search缓存
    public string? DiscoveredPhase { get; set; }
    public List<ToolSchema> DiscoveredTools { get; set; } = new();
    public DateTime? LastToolSearchAt { get; set; }

    public void NormalizeLegacyState()
    {
        if (IsRemovedLegacyTool(WorkingMemory.PendingToolCall))
            WorkingMemory.PendingToolCall = null;
        if (IsRemovedLegacyTool(WorkingMemory.PendingConfirmation?.ToolCall))
            WorkingMemory.PendingConfirmation = null;
        WorkingMemory.PendingToolCall = null;
        WorkingMemory.PendingConfirmation = null;
        if (string.Equals(Phase, "awaiting_confirmation", StringComparison.OrdinalIgnoreCase))
            Phase = "idle";
        if (string.Equals(WorkingMemory.MissionPlan.Status, "awaiting_confirmation", StringComparison.OrdinalIgnoreCase))
            WorkingMemory.MissionPlan.Status = string.Empty;
        if (string.Equals(WorkingMemory.MissionPlan.Stage, "awaiting_confirmation", StringComparison.OrdinalIgnoreCase))
            WorkingMemory.MissionPlan.Stage = string.Empty;
    }

    private static bool IsRemovedLegacyTool(AgentToolCall? call) =>
        call != null && string.Equals(call.Name, "GenerateChapter", StringComparison.OrdinalIgnoreCase);
}

public sealed class AgentConversationTurn
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AgentSessionManager
{
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();
    private readonly ConcurrentDictionary<string, Channel<AgentSseEvent>> _channels = new();
    private readonly object _fileLock = new();
    private readonly string _sessionsPath;

    public AgentSessionManager(Microsoft.Extensions.Configuration.IConfiguration configuration, Microsoft.AspNetCore.Hosting.IWebHostEnvironment environment)
    {
        var storageRoot = configuration["NovelAgent:StorageRoot"] ?? Path.Combine(environment.ContentRootPath, "App_Data");
        var projectName = configuration["NovelAgent:ProjectName"] ?? "AgenticNovelStudio";
        var dir = Path.Combine(storageRoot, "Projects", projectName, "Agent");
        Directory.CreateDirectory(dir);
        _sessionsPath = Path.Combine(dir, "sessions.json");
        LoadFromDisk();
    }

    public AgentSession GetOrCreateSession(string? sessionId = null)
    {
        sessionId ??= Guid.NewGuid().ToString("N");
        var session = _sessions.GetOrAdd(sessionId, id => new AgentSession { SessionId = id });
        NormalizeLegacyPending(session);
        PersistToDisk();
        return session;
    }

    public AgentSession? GetSession(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var session) ? session : null;

    public IReadOnlyList<AgentSession> ListSessions() =>
        _sessions.Values
            .Where(session => !session.IsArchived)
            .OrderByDescending(session => session.UpdatedAt)
            .ToList();

    public void SaveSession(AgentSession session)
    {
        NormalizeLegacyPending(session);
        _sessions[session.SessionId] = session;
        PersistToDisk();
    }

    public Channel<AgentSseEvent> GetOrCreateChannel(string sessionId) =>
        _channels.GetOrAdd(sessionId, _ => Channel.CreateUnbounded<AgentSseEvent>());

    public async Task SendEventAsync(string sessionId, AgentSseEvent evt, CancellationToken ct = default)
    {
        evt.SessionId = sessionId;
        evt.Timestamp = DateTime.UtcNow;
        var channel = GetOrCreateChannel(sessionId);
        await channel.Writer.WriteAsync(evt, ct);
    }

    public ChannelReader<AgentSseEvent> GetEventReader(string sessionId) =>
        GetOrCreateChannel(sessionId).Reader;

    public void RemoveSession(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
        if (_channels.TryRemove(sessionId, out var channel))
            channel.Writer.TryComplete();
        PersistToDisk();
    }

    private void LoadFromDisk()
    {
        lock (_fileLock)
        {
            if (!File.Exists(_sessionsPath))
                return;

            try
            {
                var json = File.ReadAllText(_sessionsPath);
                var sessions = JsonSerializer.Deserialize<List<AgentSession>>(json, JsonOptions()) ?? new();
                foreach (var session in sessions.Where(s => !string.IsNullOrWhiteSpace(s.SessionId)))
                {
                    NormalizeLegacyPending(session);
                    _sessions[session.SessionId] = session;
                }
            }
            catch
            {
                // Corrupt session history should not prevent the app from starting.
            }
        }
    }

    private void PersistToDisk()
    {
        lock (_fileLock)
        {
            var sessions = _sessions.Values
                .OrderByDescending(session => session.UpdatedAt)
                .Take(100)
                .ToList();
            var json = JsonSerializer.Serialize(sessions, JsonOptions());
            File.WriteAllText(_sessionsPath, json);
        }
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static void NormalizeLegacyPending(AgentSession session)
    {
        session.NormalizeLegacyState();
    }
}
