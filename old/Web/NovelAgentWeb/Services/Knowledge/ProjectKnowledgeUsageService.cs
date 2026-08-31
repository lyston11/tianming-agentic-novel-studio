using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Services.Production;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

public class ProjectKnowledgeUsageService : IProjectKnowledgeUsageService
{
    private readonly NovelAgentDbContext _db;
    private readonly IAgentMemoryEventService _events;
    private readonly IAgentMemoryRepository? _memoryRepository;
    private readonly ILogger<ProjectKnowledgeUsageService> _logger;
    private readonly IDistributedCacheService? _redisCache;
    private readonly IMemoryCacheService? _memoryCache;
    private readonly IOutputArtifactRecorder? _outputArtifacts;

    public ProjectKnowledgeUsageService(
        NovelAgentDbContext db,
        IAgentMemoryEventService events,
        ILogger<ProjectKnowledgeUsageService> logger,
        IOutputArtifactRecorder? outputArtifacts = null)
        : this(db, events, null, logger, null, null, outputArtifacts)
    {
    }

    public ProjectKnowledgeUsageService(
        NovelAgentDbContext db,
        IAgentMemoryEventService events,
        IAgentMemoryRepository? memoryRepository,
        ILogger<ProjectKnowledgeUsageService> logger,
        IDistributedCacheService? redisCache = null,
        IMemoryCacheService? memoryCache = null,
        IOutputArtifactRecorder? outputArtifacts = null)
    {
        _db = db;
        _events = events;
        _memoryRepository = memoryRepository;
        _logger = logger;
        _redisCache = redisCache;
        _memoryCache = memoryCache;
        _outputArtifacts = outputArtifacts;
    }

    public async Task MarkImportedAsync(
        string userId,
        string projectId,
        string knowledgeId,
        string? sessionId,
        string source,
        CancellationToken ct = default)
    {
        var usage = await FindUsageAsync(userId, projectId, knowledgeId, ct);
        var created = false;
        if (usage == null)
        {
            usage = new ProjectKnowledgeUsage
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                ProjectId = projectId,
                KnowledgeId = knowledgeId,
                Status = "imported",
                SourceSessionId = sessionId,
                FirstSeenAt = DateTime.UtcNow,
                UsageCount = 0,
                Note = source
            };
            _db.ProjectKnowledgeUsages.Add(usage);
            created = true;
        }
        await ApplyDefaultBindingSemanticsAsync(usage, ct);

        await _db.SaveChangesAsync(ct);
        await _events.AppendAsync(
            userId,
            projectId,
            null,
            null,
            "knowledge_processed",
            created ? "imported" : "import_seen",
            "project",
            "imported_knowledge_ids",
            new { knowledgeId, source },
            ct);

        await RecordBindingOutputArtifactAsync(
                usage,
                "knowledge_imported",
                "imported",
                sessionId,
                null,
                source,
                ct)
            .ConfigureAwait(false);

        await SyncImportedMemoryAsync(userId, projectId, knowledgeId, sessionId, ct);
        await InvalidateProjectCachesAsync(userId, projectId, sessionId, ct);
        _logger.LogDebug("Marked knowledge {KnowledgeId} imported for project {ProjectId}", knowledgeId, projectId);
    }

    public async Task<bool> MarkReferencedAsync(
        string userId,
        string projectId,
        string knowledgeId,
        string? sessionId,
        string? runId,
        string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        var usage = await FindUsageAsync(userId, projectId, knowledgeId, ct);
        if (usage == null)
        {
            usage = new ProjectKnowledgeUsage
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                ProjectId = projectId,
                KnowledgeId = knowledgeId,
                FirstSeenAt = DateTime.UtcNow
            };
            _db.ProjectKnowledgeUsages.Add(usage);
        }

        var normalizedKey = FirstNonEmpty(idempotencyKey);
        if (!string.IsNullOrWhiteSpace(normalizedKey) &&
            HasUsageIdempotencyKey(usage.UsageIdempotencyKeysJson, normalizedKey))
        {
            return false;
        }

        usage.Status = "referenced";
        usage.SourceSessionId = sessionId ?? usage.SourceSessionId;
        usage.SourceRunId = runId ?? usage.SourceRunId;
        usage.LastUsedAt = DateTime.UtcNow;
        usage.UsageCount++;
        if (!string.IsNullOrWhiteSpace(normalizedKey))
            usage.UsageIdempotencyKeysJson = AddUsageIdempotencyKey(usage.UsageIdempotencyKeysJson, normalizedKey);
        await ApplyDefaultBindingSemanticsAsync(usage, ct);

        await _db.SaveChangesAsync(ct);
        await _events.AppendAsync(
            userId,
            projectId,
            sessionId,
            runId,
            "knowledge_used",
            "referenced",
            "project",
            "referenced_knowledge_ids",
            new { knowledgeId, usage.UsageCount },
            ct);

        await RecordBindingOutputArtifactAsync(
                usage,
                "knowledge_referenced",
                "referenced",
                sessionId,
                runId,
                "agent_reference",
                ct)
            .ConfigureAwait(false);

        await SyncReferencedMemoryAsync(userId, projectId, ct);
        await InvalidateProjectCachesAsync(userId, projectId, sessionId, ct);
        _logger.LogDebug("Marked knowledge {KnowledgeId} referenced for project {ProjectId}", knowledgeId, projectId);
        return true;
    }

    public async Task<IReadOnlyList<ProjectKnowledgeUsage>> ListForProjectAsync(
        string userId,
        string projectId,
        CancellationToken ct = default)
    {
        return await _db.ProjectKnowledgeUsages
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.ProjectId == projectId)
            .OrderByDescending(x => x.LastUsedAt ?? x.FirstSeenAt)
            .ToListAsync(ct);
    }

    private Task<ProjectKnowledgeUsage?> FindUsageAsync(
        string userId,
        string projectId,
        string knowledgeId,
        CancellationToken ct)
    {
        return _db.ProjectKnowledgeUsages.FirstOrDefaultAsync(x =>
            x.UserId == userId &&
            x.ProjectId == projectId &&
            x.KnowledgeId == knowledgeId, ct);
    }

    private async Task RecordBindingOutputArtifactAsync(
        ProjectKnowledgeUsage usage,
        string stage,
        string status,
        string? sessionId,
        string? runId,
        string source,
        CancellationToken ct)
    {
        if (_outputArtifacts == null)
            return;

        await _outputArtifacts.RecordAsync(
                new OutputArtifactRecordRequest(
                    RuntimeRunId: FirstNonEmpty(runId, usage.SourceRunId, $"knowledge-binding:{usage.Id}"),
                    UserId: usage.UserId,
                    ProjectId: usage.ProjectId,
                    ChapterId: null,
                    PackageId: null,
                    ToolName: "ProjectKnowledgeUsage",
                    Stage: stage,
                    Status: status,
                    ArtifactType: "project_knowledge_binding",
                    ArtifactId: usage.KnowledgeId,
                    OutputKind: "ProcessArtifact",
                    Summary: $"知识 {usage.KnowledgeId} 已在项目 {usage.ProjectId} 标记为 {status}。",
                    UserVisibleWhere: new[] { "创作工作流", "知识库" },
                    VisibleInWorkflow: true,
                    VisibleInLibrary: false,
                    SourceEventType: stage,
                    SourceEventId: usage.Id,
                    Data: new
                    {
                        usage.Id,
                        usage.KnowledgeId,
                        usage.ProjectId,
                        usage.Status,
                        usage.Role,
                        usage.Scope,
                        usage.Priority,
                        usage.ConstraintLevel,
                        usage.PackagePolicy,
                        usage.BoundVersion,
                        usage.UsageCount,
                        sessionId,
                        runId,
                        source
                    }),
                ct)
            .ConfigureAwait(false);
    }

    private async Task ApplyDefaultBindingSemanticsAsync(ProjectKnowledgeUsage usage, CancellationToken ct)
    {
        var knowledge = await _db.KnowledgeBases
            .AsNoTracking()
            .Where(x => x.Id == usage.KnowledgeId && x.UserId == usage.UserId)
            .Select(x => new { x.EntryType, x.Weight, x.IdempotencyKey, x.Id })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        var entryType = knowledge?.EntryType ?? string.Empty;
        if (string.IsNullOrWhiteSpace(usage.Role) ||
            string.Equals(usage.Role, "Reference", StringComparison.OrdinalIgnoreCase))
        {
            usage.Role = string.IsNullOrWhiteSpace(entryType) ? "Reference" : entryType.Trim();
        }
        if (string.IsNullOrWhiteSpace(usage.Scope))
            usage.Scope = "ProjectWide";
        if (usage.Priority <= 0)
            usage.Priority = knowledge?.Weight > 0 ? knowledge.Weight : 50;
        if (string.IsNullOrWhiteSpace(usage.ConstraintLevel) ||
            string.Equals(usage.ConstraintLevel, "Reference", StringComparison.OrdinalIgnoreCase))
        {
            usage.ConstraintLevel = string.Equals(entryType, "HardFact", StringComparison.OrdinalIgnoreCase)
                ? "HardConstraint"
                : "Reference";
        }
        if (string.IsNullOrWhiteSpace(usage.PackagePolicy) ||
            string.Equals(usage.PackagePolicy, "RelevantOnly", StringComparison.OrdinalIgnoreCase))
        {
            usage.PackagePolicy = string.Equals(entryType, "HardFact", StringComparison.OrdinalIgnoreCase)
                ? "DefaultEveryChapter"
                : "RelevantOnly";
        }
        if (string.IsNullOrWhiteSpace(usage.BoundVersion))
            usage.BoundVersion = knowledge?.IdempotencyKey ?? knowledge?.Id;
    }

    private async Task SyncImportedMemoryAsync(
        string userId,
        string projectId,
        string knowledgeId,
        string? sessionId,
        CancellationToken ct)
    {
        if (_memoryRepository == null)
            return;

        var usages = await ListForProjectAsync(userId, projectId, ct);
        var importedIds = usages.Select(x => x.KnowledgeId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var inventory = await BuildInventoryAsync(usages, ct);

        await _memoryRepository.UpdateMemoryAsync(userId, projectId, new Dictionary<string, object>
        {
            ["project.imported_knowledge_ids"] = importedIds,
            ["project.knowledge_inventory"] = inventory
        }, ct);
    }

    private async Task SyncReferencedMemoryAsync(string userId, string projectId, CancellationToken ct)
    {
        if (_memoryRepository == null)
            return;

        var usages = await ListForProjectAsync(userId, projectId, ct);
        var referencedIds = usages
            .Where(x => string.Equals(x.Status, "referenced", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.KnowledgeId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var inventory = await BuildInventoryAsync(usages, ct);

        await _memoryRepository.UpdateMemoryAsync(userId, projectId, new Dictionary<string, object>
        {
            ["project.referenced_knowledge_ids"] = referencedIds,
            ["project.knowledge_inventory"] = inventory
        }, ct);
    }

    private async Task<List<KnowledgeInventoryItem>> BuildInventoryAsync(
        IReadOnlyList<ProjectKnowledgeUsage> usages,
        CancellationToken ct)
    {
        var ids = usages.Select(x => x.KnowledgeId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count == 0)
            return new List<KnowledgeInventoryItem>();

        var knowledge = await _db.KnowledgeBases
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, StringComparer.OrdinalIgnoreCase, ct);

        return usages
            .Where(x => knowledge.ContainsKey(x.KnowledgeId))
            .Select(x =>
            {
                var entry = knowledge[x.KnowledgeId];
                return new KnowledgeInventoryItem
                {
                    KnowledgeId = x.KnowledgeId,
                    Title = entry.Title,
                    EntryType = entry.EntryType,
                    Tags = ParseTags(entry.Tags),
                    Weight = entry.Weight,
                    Source = entry.SourceType ?? "manual",
                    ProjectUsageStatus = x.Status,
                    ProjectUsageCount = x.UsageCount,
                    ProjectLastUsedAt = x.LastUsedAt,
                    Role = x.Role,
                    Scope = x.Scope,
                    Priority = x.Priority,
                    ConstraintLevel = x.ConstraintLevel,
                    PackagePolicy = x.PackagePolicy,
                    BoundVersion = x.BoundVersion ?? string.Empty
                };
            })
            .ToList();
    }

    private static List<string> ParseTags(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<string>();

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (System.Text.Json.JsonException)
        {
            return new List<string>();
        }
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static bool HasUsageIdempotencyKey(string? json, string key) =>
        ReadUsageIdempotencyKeys(json).Contains(key, StringComparer.OrdinalIgnoreCase);

    private static string AddUsageIdempotencyKey(string? json, string key)
    {
        var keys = ReadUsageIdempotencyKeys(json);
        if (!keys.Contains(key, StringComparer.OrdinalIgnoreCase))
            keys.Add(key);
        return System.Text.Json.JsonSerializer.Serialize(keys.TakeLast(64).ToArray());
    }

    private static List<string> ReadUsageIdempotencyKeys(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<string>();

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (System.Text.Json.JsonException)
        {
            return new List<string>();
        }
    }

    private async Task InvalidateProjectCachesAsync(
        string userId,
        string projectId,
        string? sessionId,
        CancellationToken ct)
    {
        var searchPrefix = $"knowledge:search:{userId}:{projectId}";
        var inventoryKey = $"knowledge:inventory:{userId}:{projectId}";
        var projectMemoryKey = $"memory:project:{userId}:{projectId}";

        _memoryCache?.RemoveByPrefix(searchPrefix);
        _memoryCache?.Remove(inventoryKey);
        _memoryCache?.Remove(projectMemoryKey);

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            _memoryCache?.Remove($"memory:session:{userId}:{sessionId}");
        }

        if (_redisCache == null)
            return;

        try
        {
            await _redisCache.RemoveByPrefixAsync(searchPrefix, ct).ConfigureAwait(false);
            await _redisCache.RemoveAsync(inventoryKey, ct).ConfigureAwait(false);
            await _redisCache.RemoveAsync(projectMemoryKey, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                await _redisCache.RemoveAsync($"memory:session:{userId}:{sessionId}", ct)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to invalidate project knowledge caches for user {UserId}, project {ProjectId}", userId, projectId);
        }
    }
}
