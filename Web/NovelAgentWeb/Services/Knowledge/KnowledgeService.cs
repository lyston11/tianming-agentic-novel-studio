using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.VectorStore;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

/// <summary>
/// Service for managing knowledge base entries with semantic search capabilities.
/// </summary>
    public class KnowledgeService : IKnowledgeService
    {
    private static readonly TimeSpan SearchCacheTtl = TimeSpan.FromMinutes(5);

    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUserService;
    private readonly SemanticSearchService _searchService;
    private readonly IVectorStore _vectorStore;
    private readonly IMicroEmbeddingService _embedding;
    private readonly ILogger<KnowledgeService> _logger;
    private readonly IProjectKnowledgeUsageService? _projectKnowledgeUsage;
    private readonly IDistributedCacheService? _redisCache;
    private readonly IMemoryCacheService? _memoryCache;

    public KnowledgeService(
        NovelAgentDbContext db,
        ICurrentUserService currentUserService,
        SemanticSearchService searchService,
        IVectorStore vectorStore,
        IMicroEmbeddingService embedding,
        ILogger<KnowledgeService> logger,
        IProjectKnowledgeUsageService? projectKnowledgeUsage = null,
        IDistributedCacheService? redisCache = null,
        IMemoryCacheService? memoryCache = null)
    {
        _db = db;
        _currentUserService = currentUserService;
        _searchService = searchService;
        _vectorStore = vectorStore;
        _embedding = embedding;
        _logger = logger;
        _projectKnowledgeUsage = projectKnowledgeUsage;
        _redisCache = redisCache;
        _memoryCache = memoryCache;
    }

    public async Task<KnowledgeResponse> CreateKnowledgeAsync(
        CreateKnowledgeRequest request, CancellationToken ct = default)
    {
        return await CreateKnowledgeCoreAsync(
            request.ProjectId,
            request.EntryType,
            request.Title,
            request.Content,
            request.Tags,
            request.Weight,
            sourceType: "manual",
            sourceUploadTaskId: null,
            chunkIndex: null,
            extractionContext: null,
            ct).ConfigureAwait(false);
    }

    public async Task<KnowledgeResponse> CreateExtractedKnowledgeAsync(
        CreateExtractedKnowledgeRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.SourceUploadTaskId))
            throw new ArgumentException("Source upload task id is required.", nameof(request));

        return await CreateKnowledgeCoreAsync(
            request.ProjectId,
            request.EntryType,
            request.Title,
            request.Content,
            request.Tags,
            request.Weight,
            sourceType: "extracted",
            sourceUploadTaskId: request.SourceUploadTaskId,
            chunkIndex: request.ChunkIndex,
            extractionContext: request.ExtractionContext,
            ct).ConfigureAwait(false);
    }

    private async Task<KnowledgeResponse> CreateKnowledgeCoreAsync(
        string projectId,
        string entryType,
        string title,
        string content,
        List<string>? tags,
        int? weight,
        string sourceType,
        string? sourceUploadTaskId,
        int? chunkIndex,
        string? extractionContext,
        CancellationToken ct)
    {
        var userId = _currentUserService.GetUserId();

        // Verify project ownership
        var project = await _db.NovelProjects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, ct);

        if (project == null)
            throw new KeyNotFoundException($"Project {projectId} not found");

        var knowledge = new KnowledgeBase
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            SourceProjectId = projectId,
            EntryType = entryType,
            Title = title,
            Content = content,
            CreatedAt = DateTime.UtcNow,
            SourceType = sourceType,
            SourceUploadTaskId = sourceUploadTaskId,
            ChunkIndex = chunkIndex,
            ExtractionContext = extractionContext,
            Tags = tags != null && tags.Count > 0
                ? JsonSerializer.Serialize(tags)
                : null,
            Weight = weight ?? 5
        };

        _db.KnowledgeBases.Add(knowledge);
        await _db.SaveChangesAsync(ct);

        await TryUpsertKnowledgeVectorAsync(userId, knowledge, ct);
        await InvalidateUserKnowledgeCachesAsync(userId, ct);
        await InvalidateProjectKnowledgeCachesAsync(userId, projectId, ct);

        _logger.LogInformation("Created knowledge entry {KnowledgeId} in project {ProjectId}", knowledge.Id, projectId);

        var usage = await LoadUsageByKnowledgeIdAsync(
            userId,
            projectId,
            new[] { knowledge.Id },
            ct);
        return MapToResponse(knowledge, projectId, usage.GetValueOrDefault(knowledge.Id));
    }

    public async Task<List<KnowledgeResponse>> ListKnowledgeAsync(string projectId, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        // Verify project ownership
        var project = await _db.NovelProjects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, ct);

        if (project == null)
            throw new KeyNotFoundException($"Project {projectId} not found");

        var knowledgeEntries = await _db.KnowledgeBases
            .Where(k => k.UserId == userId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);

        var usage = await LoadUsageByKnowledgeIdAsync(
            userId,
            projectId,
            knowledgeEntries.Select(k => k.Id),
            ct);

        return knowledgeEntries
            .Select(k => MapToResponse(k, projectId, usage.GetValueOrDefault(k.Id)))
            .ToList();
    }

    public async Task<KnowledgeResponse> GetKnowledgeAsync(string knowledgeId, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var knowledge = await _db.KnowledgeBases
            .FirstOrDefaultAsync(k => k.Id == knowledgeId && k.UserId == userId, ct);

        if (knowledge == null)
            throw new KeyNotFoundException($"Knowledge entry {knowledgeId} not found");

        var usage = await LoadUsageByKnowledgeIdAsync(
            userId,
            knowledge.SourceProjectId ?? string.Empty,
            new[] { knowledge.Id },
            ct);
        return MapToResponse(knowledge, knowledge.SourceProjectId, usage.GetValueOrDefault(knowledge.Id));
    }

    public async Task<KnowledgeResponse> UpdateKnowledgeAsync(
        string knowledgeId, UpdateKnowledgeRequest request, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var knowledge = await _db.KnowledgeBases
            .FirstOrDefaultAsync(k => k.Id == knowledgeId && k.UserId == userId, ct);

        if (knowledge == null)
            throw new KeyNotFoundException($"Knowledge entry {knowledgeId} not found");

        if (request.Title != null)
            knowledge.Title = request.Title;

        if (request.Content != null)
            knowledge.Content = request.Content;

        await _db.SaveChangesAsync(ct);
        await TryUpsertKnowledgeVectorAsync(userId, knowledge, ct);
        await InvalidateUserKnowledgeCachesAsync(userId, ct);

        _logger.LogInformation("Updated knowledge entry {KnowledgeId}", knowledgeId);

        var usage = await LoadUsageByKnowledgeIdAsync(
            userId,
            knowledge.SourceProjectId ?? string.Empty,
            new[] { knowledge.Id },
            ct);
        return MapToResponse(knowledge, knowledge.SourceProjectId, usage.GetValueOrDefault(knowledge.Id));
    }

    public async Task DeleteKnowledgeAsync(string knowledgeId, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var knowledge = await _db.KnowledgeBases
            .FirstOrDefaultAsync(k => k.Id == knowledgeId && k.UserId == userId, ct);

        if (knowledge == null)
            throw new KeyNotFoundException($"Knowledge entry {knowledgeId} not found");

        await TryDeleteKnowledgeVectorsAsync(userId, knowledge, ct);

        _db.KnowledgeBases.Remove(knowledge);
        await _db.SaveChangesAsync(ct);
        await InvalidateUserKnowledgeCachesAsync(userId, ct);

        _logger.LogInformation("Deleted knowledge entry {KnowledgeId}", knowledgeId);
    }

    public async Task<List<KnowledgeSearchResult>> SearchKnowledgeAsync(
        SearchKnowledgeRequest request, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        // Verify project ownership
        var project = await _db.NovelProjects
            .FirstOrDefaultAsync(p => p.Id == request.ProjectId && p.UserId == userId, ct);

        if (project == null)
            throw new KeyNotFoundException($"Project {request.ProjectId} not found");

        var topK = Math.Clamp(request.TopK <= 0 ? 10 : request.TopK, 1, 50);
        var cacheKey = BuildSearchCacheKey(userId, request.ProjectId, request.EntryType, topK, request.Query);
        var cached = await TryGetSearchCacheAsync(cacheKey, ct).ConfigureAwait(false);
        if (cached != null)
            return cached;

        // Perform semantic search first, then fill gaps from the authoritative DB rows.
        var searchResults = await _searchService.SearchKnowledgeAsync(
            userId,
            request.Query,
            topK,
            ct);

        var knowledgeById = await _db.KnowledgeBases
            .AsNoTracking()
            .Where(k => k.UserId == userId)
            .Where(k => string.IsNullOrEmpty(request.EntryType) || k.EntryType == request.EntryType)
            .ToDictionaryAsync(k => k.Id, ct);
        var usageByKnowledgeId = await LoadUsageByKnowledgeIdAsync(
            userId,
            request.ProjectId,
            knowledgeById.Keys,
            ct);

        var results = searchResults
            .Where(r => r.EntityType == "knowledge")
            .Where(r => !string.IsNullOrWhiteSpace(r.EntityId) && knowledgeById.ContainsKey(r.EntityId))
            .Select(r => MapToSearchResult(
                knowledgeById[r.EntityId],
                string.IsNullOrWhiteSpace(r.Content) ? knowledgeById[r.EntityId].Content : r.Content,
                r.Score,
                usageByKnowledgeId.GetValueOrDefault(r.EntityId)))
            .ToList();

        if (results.Count < topK)
        {
            var existingIds = results.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var textMatches = knowledgeById.Values
                .Where(k => !existingIds.Contains(k.Id))
                .Select(k => new
                {
                    Knowledge = k,
                    Score = ScoreTextMatch(k, request.Query)
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Knowledge.Weight)
                .ThenByDescending(x => x.Knowledge.CreatedAt)
                .Take(topK - results.Count)
                .Select(x => MapToSearchResult(
                    x.Knowledge,
                    x.Knowledge.Content,
                    x.Score,
                    usageByKnowledgeId.GetValueOrDefault(x.Knowledge.Id)));

            results.AddRange(textMatches);
        }

        _logger.LogInformation("Knowledge search in project {ProjectId} returned {ResultCount} results", request.ProjectId, results.Count);
        await SetSearchCacheAsync(cacheKey, results, ct).ConfigureAwait(false);

        return results;
    }

    private async Task EnsureProjectOwnedAsync(string userId, string projectId, CancellationToken ct)
    {
        var projectExists = await _db.NovelProjects
            .AsNoTracking()
            .AnyAsync(p => p.Id == projectId && p.UserId == userId, ct);

        if (!projectExists)
            throw new KeyNotFoundException($"Project {projectId} not found");
    }

    public async Task IncrementUsageAsync(
        string knowledgeId,
        string projectId,
        string? sessionId = null,
        string? runId = null,
        CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();
        await EnsureProjectOwnedAsync(userId, projectId, ct);

        var knowledge = await LoadUserKnowledgeAsync(userId, knowledgeId, ct);

        knowledge.UsageCount++;
        await _db.SaveChangesAsync(ct);
        if (_projectKnowledgeUsage != null)
        {
            await _projectKnowledgeUsage.MarkReferencedAsync(
                userId,
                projectId,
                knowledge.Id,
                sessionId,
                runId,
                ct);
        }
        await InvalidateProjectKnowledgeCachesAsync(userId, projectId, ct);

        _logger.LogDebug("Incremented usage count for knowledge entry {KnowledgeId}", knowledgeId);
    }

    private async Task<KnowledgeBase> LoadUserKnowledgeAsync(
        string userId,
        string knowledgeId,
        CancellationToken ct)
    {
        var knowledge = await _db.KnowledgeBases
            .FirstOrDefaultAsync(k => k.Id == knowledgeId && k.UserId == userId, ct);

        if (knowledge == null)
            throw new KeyNotFoundException($"Knowledge entry {knowledgeId} not found");

        return knowledge;
    }

    private async Task<List<KnowledgeSearchResult>?> TryGetSearchCacheAsync(string cacheKey, CancellationToken ct)
    {
        var memoryCached = _memoryCache?.Get<List<KnowledgeSearchResult>>(cacheKey);
        if (memoryCached != null)
            return memoryCached.ToList();

        if (_redisCache == null)
            return null;

        try
        {
            var redisCached = await _redisCache.GetAsync<List<KnowledgeSearchResult>>(cacheKey, ct)
                .ConfigureAwait(false);
            if (redisCached == null)
                return null;

            _memoryCache?.Set(cacheKey, redisCached, SearchCacheTtl);
            return redisCached.ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to read knowledge search cache for {CacheKey}", cacheKey);
            return null;
        }
    }

    private async Task SetSearchCacheAsync(
        string cacheKey,
        List<KnowledgeSearchResult> results,
        CancellationToken ct)
    {
        _memoryCache?.Set(cacheKey, results, SearchCacheTtl);

        if (_redisCache == null)
            return;

        try
        {
            await _redisCache.SetAsync(cacheKey, results, SearchCacheTtl, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to write knowledge search cache for {CacheKey}", cacheKey);
        }
    }

    private async Task InvalidateProjectKnowledgeCachesAsync(string userId, string projectId, CancellationToken ct)
    {
        var searchPrefix = BuildSearchCachePrefix(userId, projectId);
        _memoryCache?.RemoveByPrefix(searchPrefix);
        _memoryCache?.Remove(BuildInventoryCacheKey(userId, projectId));

        if (_redisCache == null)
            return;

        try
        {
            await _redisCache.RemoveByPrefixAsync(searchPrefix, ct).ConfigureAwait(false);
            await _redisCache.RemoveAsync(BuildInventoryCacheKey(userId, projectId), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to invalidate knowledge caches for user {UserId}, project {ProjectId}", userId, projectId);
        }
    }

    private async Task InvalidateUserKnowledgeCachesAsync(string userId, CancellationToken ct)
    {
        var searchPrefix = BuildSearchCachePrefix(userId, string.Empty);
        _memoryCache?.RemoveByPrefix(searchPrefix);

        if (_redisCache == null)
            return;

        try
        {
            await _redisCache.RemoveByPrefixAsync(searchPrefix, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to invalidate user knowledge caches for user {UserId}", userId);
        }
    }

    private static string BuildSearchCachePrefix(string userId, string projectId) =>
        string.IsNullOrWhiteSpace(projectId)
            ? $"knowledge:search:{userId}:"
            : $"knowledge:search:{userId}:{projectId}";

    private static string BuildSearchCacheKey(
        string userId,
        string projectId,
        string? entryType,
        int topK,
        string query) =>
        $"{BuildSearchCachePrefix(userId, projectId)}:{(string.IsNullOrWhiteSpace(entryType) ? "*" : entryType)}:{topK}:{Sha256(query.Trim())}";

    private static string BuildInventoryCacheKey(string userId, string projectId) =>
        $"knowledge:inventory:{userId}:{projectId}";

    private static string Sha256(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static KnowledgeResponse MapToResponse(
        KnowledgeBase knowledge,
        string? projectId,
        KnowledgeUsageSnapshot? usage = null)
    {
        return new KnowledgeResponse
        {
            Id = knowledge.Id,
            UsageProjectId = projectId,
            SourceProjectId = knowledge.SourceProjectId,
            EntryType = knowledge.EntryType,
            Title = knowledge.Title,
            Content = knowledge.Content,
            UsageCount = knowledge.UsageCount,
            CreatedAt = knowledge.CreatedAt,
            VectorId = knowledge.VectorId,
            ProjectUsageStatus = usage?.Status ?? "none",
            ProjectUsageCount = usage?.UsageCount ?? 0,
            ProjectLastUsedAt = usage?.LastUsedAt
        };
    }

    private static KnowledgeSearchResult MapToSearchResult(
        KnowledgeBase knowledge,
        string content,
        float score,
        KnowledgeUsageSnapshot? usage = null)
    {
        return new KnowledgeSearchResult
        {
            Id = knowledge.Id,
            EntryType = knowledge.EntryType,
            Title = knowledge.Title,
            Content = content,
            Score = score,
            ProjectUsageStatus = usage?.Status ?? "none",
            ProjectUsageCount = usage?.UsageCount ?? 0,
            ProjectLastUsedAt = usage?.LastUsedAt
        };
    }

    private async Task<Dictionary<string, KnowledgeUsageSnapshot>> LoadUsageByKnowledgeIdAsync(
        string userId,
        string projectId,
        IEnumerable<string> knowledgeIds,
        CancellationToken ct)
    {
        var ids = knowledgeIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<string, KnowledgeUsageSnapshot>(StringComparer.OrdinalIgnoreCase);
        }

        var usages = await _db.ProjectKnowledgeUsages
            .AsNoTracking()
            .Where(u => u.UserId == userId && u.ProjectId == projectId && ids.Contains(u.KnowledgeId))
            .ToListAsync(ct);

        return usages.ToDictionary(
            u => u.KnowledgeId,
            u => new KnowledgeUsageSnapshot(u.Status, u.UsageCount, u.LastUsedAt),
            StringComparer.OrdinalIgnoreCase);
    }

    private sealed record KnowledgeUsageSnapshot(string Status, int UsageCount, DateTime? LastUsedAt);

    private static float ScoreTextMatch(KnowledgeBase knowledge, string query)
    {
        var tokens = query
            .Split(new[] { ' ', '\t', '\r', '\n', ',', '，', '.', '。', ';', '；', ':', '：' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tokens.Count == 0)
            tokens.Add(query.Trim());

        var haystack = $"{knowledge.Title}\n{knowledge.Content}\n{knowledge.Tags}".ToLowerInvariant();
        var score = tokens.Count(t => haystack.Contains(t.ToLowerInvariant(), StringComparison.Ordinal));

        var normalizedQuery = query.Trim().ToLowerInvariant();
        if (score == 0 && haystack.Contains(normalizedQuery, StringComparison.Ordinal))
            score = 1;
        if (score == 0 && normalizedQuery.Length >= 2)
        {
            score = Enumerable.Range(0, normalizedQuery.Length - 1)
                .Select(i => normalizedQuery.Substring(i, 2))
                .Distinct(StringComparer.Ordinal)
                .Count(fragment => haystack.Contains(fragment, StringComparison.Ordinal));
        }

        return score == 0 ? 0 : score + Math.Clamp(knowledge.Weight, 1, 10) / 100f;
    }

    private async Task TryUpsertKnowledgeVectorAsync(string userId, KnowledgeBase knowledge, CancellationToken ct)
    {
        try
        {
            var pointId = Guid.TryParse(knowledge.VectorId, out _)
                ? knowledge.VectorId!
                : Guid.NewGuid().ToString();
            var vector = await _embedding.EncodeAsync($"{knowledge.Title} {knowledge.Content}", EmbeddingMode.Passage, ct);

            await _vectorStore.InitializeUserCollectionAsync(userId, ct);
            await _vectorStore.UpsertVectorsAsync(userId, new List<VectorData>
            {
                new()
                {
                    Id = pointId,
                    Vector = vector,
                    UserId = userId,
                    ProjectId = knowledge.SourceProjectId ?? "global",
                    SourceType = "knowledge",
                    SourceId = knowledge.Id,
                    Content = knowledge.Content,
                    Metadata = new Dictionary<string, object>
                    {
                        ["entry_type"] = knowledge.EntryType,
                        ["title"] = knowledge.Title
                    }
                }
            }, ct);

            if (knowledge.VectorId != pointId)
            {
                knowledge.VectorId = pointId;
                await _db.SaveChangesAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to vectorize knowledge {KnowledgeId}", knowledge.Id);
        }
    }

    private async Task TryDeleteKnowledgeVectorsAsync(string userId, KnowledgeBase knowledge, CancellationToken ct)
    {
        try
        {
            await _vectorStore.DeleteVectorsByFilterAsync(userId, new Dictionary<string, object>
            {
                ["source_type"] = "knowledge",
                ["source_id"] = knowledge.Id
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete vectors for knowledge {KnowledgeId}", knowledge.Id);
        }
    }
}
