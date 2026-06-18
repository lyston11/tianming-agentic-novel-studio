using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Support;
using AgentToolSearchSnapshotEntity = TM.Web.NovelAgentWeb.Data.Entities.AgentToolSearchSnapshot;

namespace TM.Web.NovelAgentWeb.Services.AgentTools;

public class ToolSearchCacheService : IToolSearchCacheService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IDistributedCacheService _redis;
    private readonly IMemoryCacheService _memory;
    private readonly IAgentMemoryVersionService _versions;
    private readonly NovelAgentDbContext _db;

    public ToolSearchCacheService(
        IDistributedCacheService redis,
        IMemoryCacheService memory,
        IAgentMemoryVersionService versions,
        NovelAgentDbContext db)
    {
        _redis = redis;
        _memory = memory;
        _versions = versions;
        _db = db;
    }

    public async Task<ToolSearchCacheLookup> GetAsync(
        AgentSession session,
        string phase,
        CancellationToken ct = default)
    {
        var version = ToToolSearchVersion(await _versions.GetCombinedVersionAsync(
            session.UserId,
            NullIfEmpty(session.ActiveProjectId),
            session.SessionId,
            ct).ConfigureAwait(false));

        if (!string.Equals(session.ToolSearchCacheVersion, version, StringComparison.Ordinal))
            return ToolSearchCacheLookup.Miss;

        var normalizedPhase = NormalizeScope(phase);

        if (IsValidSessionSnapshot(session, normalizedPhase))
            return new ToolSearchCacheLookup(session.DiscoveredTools, "session-hot");

        var cacheKey = BuildCacheKey(session, normalizedPhase, version);
        var cached = _memory.Get<ToolSearchCacheSnapshot>(cacheKey) ??
                     await _redis.GetAsync<ToolSearchCacheSnapshot>(cacheKey, ct);

        if (cached != null &&
            cached.ExpiresAt > DateTime.UtcNow &&
            string.Equals(cached.Version, version, StringComparison.Ordinal) &&
            string.Equals(cached.Phase, normalizedPhase, StringComparison.OrdinalIgnoreCase))
        {
            ApplySnapshot(session, cached);
            return new ToolSearchCacheLookup(session.DiscoveredTools, "redis-hot");
        }

        cached = await GetSqliteSnapshotAsync(session, normalizedPhase, version, ct).ConfigureAwait(false);
        if (cached == null)
            return ToolSearchCacheLookup.Miss;

        ApplySnapshot(session, cached);
        _memory.Set(cacheKey, cached, RemainingTtl(cached));
        await _redis.SetAsync(cacheKey, cached, RemainingTtl(cached), ct).ConfigureAwait(false);
        return new ToolSearchCacheLookup(session.DiscoveredTools, "sqlite-snapshot");
    }

    public async Task SaveAsync(
        AgentSession session,
        string phase,
        IReadOnlyList<ToolSchema> tools,
        CancellationToken ct = default)
    {
        var version = ToToolSearchVersion(await _versions.GetCombinedVersionAsync(
            session.UserId,
            NullIfEmpty(session.ActiveProjectId),
            session.SessionId,
            ct).ConfigureAwait(false));
        var normalizedPhase = NormalizeScope(phase);
        var now = DateTime.UtcNow;
        var snapshot = new ToolSearchCacheSnapshot
        {
            Phase = normalizedPhase,
            Tools = tools.ToList(),
            Version = version,
            CachedAt = now,
            ExpiresAt = now.Add(CacheTtl)
        };

        ApplySnapshot(session, snapshot);

        await SaveSqliteSnapshotAsync(session, snapshot, ct).ConfigureAwait(false);

        var cacheKey = BuildCacheKey(session, normalizedPhase, version);
        _memory.Set(cacheKey, snapshot, CacheTtl);
        await _redis.SetAsync(cacheKey, snapshot, CacheTtl, ct);
    }

    private static bool IsValidSessionSnapshot(AgentSession session, string scope)
    {
        return !string.IsNullOrWhiteSpace(session.DiscoveredPhase) &&
               session.DiscoveredTools.Count > 0 &&
               (string.Equals(session.DiscoveredPhase, scope, StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(scope, "global", StringComparison.OrdinalIgnoreCase) &&
                 session.DiscoveredPhase.StartsWith("global:", StringComparison.OrdinalIgnoreCase))) &&
               session.LastToolSearchAt != null &&
               DateTime.UtcNow - session.LastToolSearchAt.Value <= CacheTtl;
    }

    private static void ApplySnapshot(AgentSession session, ToolSearchCacheSnapshot snapshot)
    {
        session.DiscoveredPhase = snapshot.Phase;
        session.DiscoveredTools = snapshot.Tools.ToList();
        session.LastToolSearchAt = snapshot.CachedAt;
        session.ToolSearchCacheVersion = snapshot.Version;
    }

    private static string BuildCacheKey(AgentSession session, string phase, string version) =>
        AgentMemoryKeys.ToolCache(
            session.UserId,
            session.SessionId,
            NullIfEmpty(session.ActiveProjectId) ?? "*",
            phase,
            version);

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private async Task<ToolSearchCacheSnapshot?> GetSqliteSnapshotAsync(
        AgentSession session,
        string phase,
        string version,
        CancellationToken ct)
    {
        var projectId = NullIfEmpty(session.ActiveProjectId);
        var now = DateTime.UtcNow;
        var row = await _db.AgentToolSearchSnapshots
            .AsNoTracking()
            .Where(x =>
                x.UserId == session.UserId &&
                x.ProjectId == projectId &&
                x.SessionId == session.SessionId &&
                x.Phase == phase &&
                x.Version == version &&
                x.ExpiresAt > now)
            .OrderByDescending(x => x.CachedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (row == null)
            return null;

        var tools = JsonSerializer.Deserialize<List<ToolSchema>>(row.ToolsJson, JsonOptions()) ?? new List<ToolSchema>();
        if (tools.Count == 0)
            return null;

        return new ToolSearchCacheSnapshot
        {
            Phase = row.Phase,
            Tools = tools,
            Version = row.Version,
            CachedAt = row.CachedAt,
            ExpiresAt = row.ExpiresAt
        };
    }

    private async Task SaveSqliteSnapshotAsync(AgentSession session, ToolSearchCacheSnapshot snapshot, CancellationToken ct)
    {
        var projectId = NullIfEmpty(session.ActiveProjectId);
        var row = await _db.AgentToolSearchSnapshots
            .FirstOrDefaultAsync(x =>
                x.UserId == session.UserId &&
                x.ProjectId == projectId &&
                x.SessionId == session.SessionId &&
                x.Phase == snapshot.Phase &&
                x.Version == snapshot.Version,
                ct)
            .ConfigureAwait(false);

        if (row == null)
        {
            row = new AgentToolSearchSnapshotEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = session.UserId,
                ProjectId = projectId,
                SessionId = session.SessionId,
                Phase = snapshot.Phase,
                Version = snapshot.Version
            };
            _db.AgentToolSearchSnapshots.Add(row);
        }

        row.ToolsJson = JsonSerializer.Serialize(snapshot.Tools, JsonOptions());
        row.CachedAt = snapshot.CachedAt;
        row.ExpiresAt = snapshot.ExpiresAt;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static TimeSpan RemainingTtl(ToolSearchCacheSnapshot snapshot)
    {
        var remaining = snapshot.ExpiresAt - DateTime.UtcNow;
        return remaining <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : remaining;
    }

    private static string NormalizeScope(string phase)
    {
        if (string.IsNullOrWhiteSpace(phase))
            return "global";

        return phase.Trim().ToLowerInvariant() switch
        {
            "planning" => "global:Planning",
            "creation" => "global:Creation",
            "review" => "global:Review",
            "all" => "global:All",
            "conversation" => "global:Conversation",
            _ => phase.Trim()
        };
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string ToToolSearchVersion(string combinedVersion)
    {
        var parts = combinedVersion
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !IsToolExecutionVersionPart(part))
            .ToArray();

        return parts.Length == 0 ? "none=0" : string.Join("|", parts);
    }

    private static bool IsToolExecutionVersionPart(string part)
    {
        var separatorIndex = part.IndexOfAny(new[] { ':', '=' });
        var scope = separatorIndex < 0 ? part : part[..separatorIndex];
        return string.Equals(scope, "tool_execution", StringComparison.Ordinal) ||
               string.Equals(scope, "execution", StringComparison.Ordinal);
    }

    private sealed class ToolSearchCacheSnapshot
    {
        public string Phase { get; set; } = string.Empty;
        public List<ToolSchema> Tools { get; set; } = new();
        public string Version { get; set; } = string.Empty;
        public DateTime CachedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}
