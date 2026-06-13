using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Memory;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

public class ProjectKnowledgeUsageService : IProjectKnowledgeUsageService
{
    private const int MaxRecentUploadedKnowledgeIds = 20;

    private readonly NovelAgentDbContext _db;
    private readonly IAgentMemoryEventService _events;
    private readonly IAgentMemoryRepository? _memoryRepository;
    private readonly ILogger<ProjectKnowledgeUsageService> _logger;

    public ProjectKnowledgeUsageService(
        NovelAgentDbContext db,
        IAgentMemoryEventService events,
        ILogger<ProjectKnowledgeUsageService> logger)
        : this(db, events, null, logger)
    {
    }

    public ProjectKnowledgeUsageService(
        NovelAgentDbContext db,
        IAgentMemoryEventService events,
        IAgentMemoryRepository? memoryRepository,
        ILogger<ProjectKnowledgeUsageService> logger)
    {
        _db = db;
        _events = events;
        _memoryRepository = memoryRepository;
        _logger = logger;
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

        await _db.SaveChangesAsync(ct);
        await _events.AppendAsync(
            userId,
            projectId,
            sessionId,
            null,
            "knowledge_processed",
            created ? "imported" : "import_seen",
            "project",
            "imported_knowledge_ids",
            new { knowledgeId, source },
            ct);

        await SyncImportedMemoryAsync(userId, projectId, knowledgeId, sessionId, ct);
        _logger.LogDebug("Marked knowledge {KnowledgeId} imported for project {ProjectId}", knowledgeId, projectId);
    }

    public async Task MarkReferencedAsync(
        string userId,
        string projectId,
        string knowledgeId,
        string? sessionId,
        string? runId,
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

        usage.Status = "referenced";
        usage.SourceSessionId = sessionId ?? usage.SourceSessionId;
        usage.SourceRunId = runId ?? usage.SourceRunId;
        usage.LastUsedAt = DateTime.UtcNow;
        usage.UsageCount++;

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

        await SyncReferencedMemoryAsync(userId, projectId, ct);
        _logger.LogDebug("Marked knowledge {KnowledgeId} referenced for project {ProjectId}", knowledgeId, projectId);
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

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var session = await _memoryRepository.GetSessionMemoryAsync(userId, projectId, sessionId, ct);
            var recent = session.RecentUploadedKnowledgeIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!recent.Contains(knowledgeId, StringComparer.OrdinalIgnoreCase))
                recent.Add(knowledgeId);
            if (recent.Count > MaxRecentUploadedKnowledgeIds)
                recent = recent.TakeLast(MaxRecentUploadedKnowledgeIds).ToList();

            await _memoryRepository.UpdateSessionMemoryAsync(userId, projectId, sessionId, new Dictionary<string, object>
            {
                ["session.recent_uploaded_knowledge_ids"] = recent
            }, ct);
        }
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
                    ProjectLastUsedAt = x.LastUsedAt
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
}
