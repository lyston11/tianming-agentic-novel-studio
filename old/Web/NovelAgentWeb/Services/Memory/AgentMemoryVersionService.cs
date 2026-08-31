using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Caching;

namespace TM.Web.NovelAgentWeb.Services.Memory;

public class AgentMemoryVersionService : IAgentMemoryVersionService
{
    private readonly NovelAgentDbContext _db;
    private readonly IDistributedCacheService _distributedCache;
    private readonly IMemoryCacheService _memoryCache;

    public AgentMemoryVersionService(
        NovelAgentDbContext db,
        IDistributedCacheService distributedCache,
        IMemoryCacheService memoryCache)
    {
        _db = db;
        _distributedCache = distributedCache;
        _memoryCache = memoryCache;
    }

    public async Task<long> BumpAsync(string userId, string? projectId, string? sessionId, string scope, CancellationToken ct = default)
    {
        var version = _db.Database.IsSqlite()
            ? await BumpSqliteAsync(userId, projectId, sessionId, scope, ct)
            : await BumpWithRetryAsync(userId, projectId, sessionId, scope, ct);

        if (projectId != null && sessionId != null)
        {
            await InvalidateContextCachesAsync(userId, projectId, sessionId, scope, ct);
        }

        return version;
    }

    public async Task<string> GetCombinedVersionAsync(string userId, string? projectId, string? sessionId, CancellationToken ct = default)
    {
        var rows = await _db.AgentMemoryVersions
            .AsNoTracking()
            .Where(v => v.UserId == userId)
            .Where(v => v.ProjectId == projectId || v.ProjectId == null)
            .Where(v => v.SessionId == sessionId || v.SessionId == null)
            .OrderBy(v => v.Scope)
            .ThenBy(v => v.ProjectId)
            .ThenBy(v => v.SessionId)
            .ToListAsync(ct);

        return rows.Count == 0
            ? "none=0"
            : string.Join("|", rows.Select(r => $"{r.Scope}:{r.ProjectId ?? "*"}:{r.SessionId ?? "*"}={r.Version}"));
    }

    private async Task InvalidateContextCachesAsync(string userId, string projectId, string sessionId, string scope, CancellationToken ct)
    {
        var memoryContextPrefix = $"memory-context:{userId}:{sessionId}:{projectId}";
        var toolCachePrefix = $"toolcache:{userId}:{sessionId}:{projectId}";

        _memoryCache.RemoveByPrefix(memoryContextPrefix);
        await _distributedCache.RemoveByPrefixAsync(memoryContextPrefix, ct);

        if (string.Equals(scope, "tool_execution", StringComparison.Ordinal))
            return;

        _memoryCache.RemoveByPrefix(toolCachePrefix);
        await _distributedCache.RemoveByPrefixAsync(toolCachePrefix, ct);
    }

    private async Task<long> BumpSqliteAsync(string userId, string? projectId, string? sessionId, string scope, CancellationToken ct)
    {
        await using var command = _db.Database.GetDbConnection().CreateCommand();
        command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = BuildSqliteUpsertCommand(projectId, sessionId);

        AddParameter(command, "$userId", userId);
        AddParameter(command, "$projectId", projectId);
        AddParameter(command, "$sessionId", sessionId);
        AddParameter(command, "$scope", scope);
        AddParameter(command, "$updatedAt", DateTime.UtcNow);

        if (command.Connection!.State != System.Data.ConnectionState.Open)
        {
            await command.Connection.OpenAsync(ct);
        }

        var result = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }

    private static string BuildSqliteUpsertCommand(string? projectId, string? sessionId)
    {
        var conflictTarget = (projectId, sessionId) switch
        {
            (null, null) => "(user_id, scope) WHERE project_id IS NULL AND session_id IS NULL",
            (_, null) => "(user_id, project_id, scope) WHERE project_id IS NOT NULL AND session_id IS NULL",
            (null, _) => "(user_id, session_id, scope) WHERE project_id IS NULL AND session_id IS NOT NULL",
            _ => "(user_id, project_id, session_id, scope) WHERE project_id IS NOT NULL AND session_id IS NOT NULL"
        };

        return $"""
            INSERT INTO agent_memory_versions (user_id, project_id, session_id, scope, version, updated_at)
            VALUES ($userId, $projectId, $sessionId, $scope, 1, $updatedAt)
            ON CONFLICT {conflictTarget}
            DO UPDATE SET version = version + 1, updated_at = excluded.updated_at
            RETURNING version;
            """;
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private async Task<long> BumpWithRetryAsync(string userId, string? projectId, string? sessionId, string scope, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                return await BumpTrackedAsync(userId, projectId, sessionId, scope, ct);
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                _db.ChangeTracker.Clear();
            }
        }

        return await BumpTrackedAsync(userId, projectId, sessionId, scope, ct);
    }

    private async Task<long> BumpTrackedAsync(string userId, string? projectId, string? sessionId, string scope, CancellationToken ct)
    {
        var row = await _db.AgentMemoryVersions
            .FirstOrDefaultAsync(v =>
                v.UserId == userId &&
                v.ProjectId == projectId &&
                v.SessionId == sessionId &&
                v.Scope == scope, ct);

        if (row == null)
        {
            row = new AgentMemoryVersion
            {
                UserId = userId,
                ProjectId = projectId,
                SessionId = sessionId,
                Scope = scope,
                Version = 0
            };
            _db.AgentMemoryVersions.Add(row);
        }

        row.Version++;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return row.Version;
    }
}
