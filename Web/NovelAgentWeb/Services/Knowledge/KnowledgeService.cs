using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Caching;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Services.VectorStore;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

/// <summary>
/// Service for managing knowledge base entries with semantic search capabilities.
/// </summary>
public class KnowledgeService : IKnowledgeService
{
    private static readonly TimeSpan SearchCacheTtl = TimeSpan.FromMinutes(5);
    private const string UncategorizedDirectoryKey = "Uncategorized";
    private static readonly Dictionary<string, (string Name, string Description)> SystemDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GenrePrinciple"] = ("题材原则", "类型承诺和读者预期"),
        ["TropePattern"] = ("套路模式", "高频套路和风险桥段"),
        ["AntiTropeStrategy"] = ("反套路", "变体、反转和规避策略"),
        ["StyleExample"] = ("风格示例", "叙述风格、对话技巧和节奏样例"),
        ["HardFact"] = ("硬事实", "角色、道具、组织、地点和规则等连续性事实"),
        ["ReaderPromise"] = ("读者承诺", "爽点、情绪和持续期待"),
        ["ThemeDepth"] = ("主题深度", "主题母题和深层表达"),
        ["EmotionArc"] = ("情绪线", "情绪推进和转折节奏"),
        ["RelationshipDynamic"] = ("关系动态", "人物关系张力"),
        ["ProjectUsedPattern"] = ("项目记忆", "项目已用桥段记忆"),
        [UncategorizedDirectoryKey] = ("未分类", "尚未归入目录的知识条目")
    };
    private static readonly Dictionary<string, string> LegacyDirectoryAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["genre_rule"] = "GenrePrinciple",
        ["trope_pattern"] = "TropePattern",
        ["anti_trope"] = "AntiTropeStrategy",
        ["style_example"] = "StyleExample",
        ["hard_fact"] = "HardFact",
        ["reader_promise"] = "ReaderPromise",
        ["theme_depth"] = "ThemeDepth",
        ["emotion_arc"] = "EmotionArc",
        ["relationship_dynamic"] = "RelationshipDynamic",
        ["project_pattern"] = "ProjectUsedPattern",
        ["project_used_pattern"] = "ProjectUsedPattern",
        ["uncategorized"] = UncategorizedDirectoryKey
    };

    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUserService;
    private readonly SemanticSearchService _searchService;
    private readonly ILogger<KnowledgeService> _logger;
    private readonly IProjectKnowledgeUsageService? _projectKnowledgeUsage;
    private readonly IDistributedCacheService? _redisCache;
    private readonly IMemoryCacheService? _memoryCache;
    private readonly IProductionTruthStore _truthStore;

    public KnowledgeService(
        NovelAgentDbContext db,
        ICurrentUserService currentUserService,
        SemanticSearchService searchService,
        ILogger<KnowledgeService> logger,
        IProductionTruthStore truthStore,
        IProjectKnowledgeUsageService? projectKnowledgeUsage = null,
        IDistributedCacheService? redisCache = null,
        IMemoryCacheService? memoryCache = null)
    {
        _db = db;
        _currentUserService = currentUserService;
        _searchService = searchService;
        _logger = logger;
        _projectKnowledgeUsage = projectKnowledgeUsage;
        _redisCache = redisCache;
        _memoryCache = memoryCache;
        _truthStore = truthStore;
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
            request.IdempotencyKey,
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
            FirstNonEmpty(request.IdempotencyKey, BuildExtractedKnowledgeIdempotencyKey(request)),
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
        string? idempotencyKey,
        string sourceType,
        string? sourceUploadTaskId,
        int? chunkIndex,
        string? extractionContext,
        CancellationToken ct)
    {
        var userId = _currentUserService.GetUserId();
        var normalizedEntryType = NormalizeDirectoryKey(entryType);

        // Verify project ownership
        var project = await _db.NovelProjects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, ct);

        if (project == null)
            throw new KeyNotFoundException($"Project {projectId} not found");

        var normalizedIdempotencyKey = EmptyToNull(idempotencyKey);
        if (normalizedIdempotencyKey != null)
        {
            var existing = await FindKnowledgeByIdempotencyKeyAsync(
                    userId,
                    projectId,
                    normalizedIdempotencyKey,
                    ct)
                .ConfigureAwait(false);
            if (existing != null)
            {
                await EnsureKnowledgeIndexOutboxAsync(userId, existing, ct).ConfigureAwait(false);
                return await MapKnowledgeWithContextAsync(existing, projectId, ct).ConfigureAwait(false);
            }
        }

        var knowledge = new KnowledgeBase
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            SourceProjectId = projectId,
            IdempotencyKey = normalizedIdempotencyKey,
            EntryType = normalizedEntryType,
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
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (normalizedIdempotencyKey != null)
        {
            _db.Entry(knowledge).State = EntityState.Detached;
            var existing = await FindKnowledgeByIdempotencyKeyAsync(
                    userId,
                    projectId,
                    normalizedIdempotencyKey,
                    ct)
                .ConfigureAwait(false);
            if (existing != null)
            {
                await EnsureKnowledgeIndexOutboxAsync(userId, existing, ct).ConfigureAwait(false);
                return await MapKnowledgeWithContextAsync(existing, projectId, ct).ConfigureAwait(false);
            }

            throw;
        }

        await EnqueueKnowledgeIndexAsync(userId, knowledge, ct);
        await InvalidateUserKnowledgeCachesAsync(userId, ct);
        await InvalidateProjectKnowledgeCachesAsync(userId, projectId, ct);

        _logger.LogInformation("Created knowledge entry {KnowledgeId} in project {ProjectId}", knowledge.Id, projectId);

        return await MapKnowledgeWithContextAsync(knowledge, projectId, ct).ConfigureAwait(false);
    }

    public async Task<List<KnowledgeResponse>> ListKnowledgeAsync(string projectId, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();
        var scopedProjectId = string.IsNullOrWhiteSpace(projectId) ? null : projectId.Trim();

        if (scopedProjectId != null)
        {
            var project = await _db.NovelProjects
                .FirstOrDefaultAsync(p => p.Id == scopedProjectId && p.UserId == userId, ct);

            if (project == null)
                throw new KeyNotFoundException($"Project {scopedProjectId} not found");
        }

        var knowledgeEntries = await _db.KnowledgeBases
            .Where(k => k.UserId == userId && !k.IsArchived)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);

        var usage = scopedProjectId == null
            ? new Dictionary<string, KnowledgeUsageSnapshot>(StringComparer.OrdinalIgnoreCase)
            : await LoadUsageByKnowledgeIdAsync(
                userId,
                scopedProjectId,
                knowledgeEntries.Select(k => k.Id),
                ct);

        var usageContexts = await LoadUsageContextsByKnowledgeIdAsync(
            userId,
            knowledgeEntries.Select(k => k.Id),
            ct);
        var sourceProjectTitles = await LoadProjectTitlesAsync(
            userId,
            knowledgeEntries.Select(k => k.SourceProjectId),
            ct);
        var constraintEvidence = await LoadConstraintEvidenceByKnowledgeIdAsync(
            userId,
            knowledgeEntries.Select(k => k.Id),
            ct);

        return knowledgeEntries
            .Select(k => MapToResponse(
                k,
                scopedProjectId,
                usage.GetValueOrDefault(k.Id),
                usageContexts.GetValueOrDefault(k.Id),
                sourceProjectTitles.GetValueOrDefault(k.SourceProjectId ?? string.Empty),
                constraintEvidence.GetValueOrDefault(k.Id)))
            .ToList();
    }

    public async Task<List<KnowledgeDirectoryResponse>> ListKnowledgeDirectoriesAsync(CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var customDirectories = await _db.KnowledgeDirectories
            .AsNoTracking()
            .Where(directory => directory.UserId == userId)
            .OrderBy(directory => directory.Name)
            .ToListAsync(ct);

        var entryCounts = await _db.KnowledgeBases
            .AsNoTracking()
            .Where(knowledge => knowledge.UserId == userId && !knowledge.IsArchived)
            .GroupBy(knowledge => knowledge.EntryType)
            .Select(group => new { Key = group.Key, Count = group.Count() })
            .ToListAsync(ct);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in entryCounts)
        {
            var key = NormalizeDirectoryKey(item.Key);
            counts[key] = counts.GetValueOrDefault(key) + item.Count;
        }
        var responses = new List<KnowledgeDirectoryResponse>();

        foreach (var (key, meta) in SystemDirectories)
        {
            responses.Add(new KnowledgeDirectoryResponse
            {
                Key = key,
                Name = meta.Name,
                Description = meta.Description,
                IsSystem = true,
                EntryCount = counts.GetValueOrDefault(key)
            });
        }

        foreach (var directory in customDirectories)
        {
            if (IsSystemDirectory(directory.Key))
            {
                continue;
            }

            responses.Add(new KnowledgeDirectoryResponse
            {
                Key = directory.Key,
                Name = directory.Name,
                Description = "用户自定义知识目录",
                IsSystem = false,
                EntryCount = counts.GetValueOrDefault(directory.Key),
                CreatedAt = directory.CreatedAt,
                UpdatedAt = directory.UpdatedAt
            });
        }

        var knownKeys = new HashSet<string>(responses.Select(item => item.Key), StringComparer.OrdinalIgnoreCase);
        foreach (var entryType in counts.Keys.Where(key => !knownKeys.Contains(key)).OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
        {
            responses.Add(new KnowledgeDirectoryResponse
            {
                Key = entryType,
                Name = entryType,
                Description = "由已有条目自动识别的目录",
                IsSystem = false,
                EntryCount = counts.GetValueOrDefault(entryType)
            });
        }

        return responses;
    }

    public async Task<KnowledgeDirectoryResponse> CreateKnowledgeDirectoryAsync(
        CreateKnowledgeDirectoryRequest request,
        CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();
        var normalizedIdempotencyKey = EmptyToNull(request.IdempotencyKey);
        if (normalizedIdempotencyKey != null)
        {
            var existing = await FindKnowledgeDirectoryByIdempotencyKeyAsync(
                    userId,
                    normalizedIdempotencyKey,
                    ct)
                .ConfigureAwait(false);
            if (existing != null)
                return await MapDirectoryResponseAsync(existing, ct).ConfigureAwait(false);
        }

        var key = NormalizeDirectoryKey(request.Name);
        if (IsSystemDirectory(key))
            throw new InvalidOperationException("System directory already exists.");

        var exists = await _db.KnowledgeDirectories
            .AnyAsync(directory => directory.UserId == userId && directory.Key == key, ct);
        if (exists)
            throw new InvalidOperationException($"Knowledge directory {key} already exists.");

        var now = DateTime.UtcNow;
        var directory = new KnowledgeDirectory
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            Key = key,
            Name = key,
            IdempotencyKey = normalizedIdempotencyKey,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.KnowledgeDirectories.Add(directory);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (normalizedIdempotencyKey != null)
        {
            _db.Entry(directory).State = EntityState.Detached;
            var existing = await FindKnowledgeDirectoryByIdempotencyKeyAsync(
                    userId,
                    normalizedIdempotencyKey,
                    ct)
                .ConfigureAwait(false);
            if (existing != null)
                return await MapDirectoryResponseAsync(existing, ct).ConfigureAwait(false);

            throw;
        }

        return await MapDirectoryResponseAsync(directory, ct).ConfigureAwait(false);
    }

    public async Task<KnowledgeDirectoryResponse> UpdateKnowledgeDirectoryAsync(
        string key,
        UpdateKnowledgeDirectoryRequest request,
        CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();
        var oldKey = NormalizeDirectoryKey(key);
        var newKey = NormalizeDirectoryKey(request.Name);
        if (IsSystemDirectory(oldKey) || IsSystemDirectory(newKey))
            throw new InvalidOperationException("System directories cannot be renamed.");

        var directory = await _db.KnowledgeDirectories
            .FirstOrDefaultAsync(item => item.UserId == userId && item.Key == oldKey, ct);
        if (directory == null)
            throw new KeyNotFoundException($"Knowledge directory {oldKey} not found.");

        var duplicate = await _db.KnowledgeDirectories
            .AnyAsync(item => item.UserId == userId && item.Key == newKey && item.Id != directory.Id, ct);
        if (duplicate)
            throw new InvalidOperationException($"Knowledge directory {newKey} already exists.");

        var affected = await _db.KnowledgeBases
            .Where(knowledge => knowledge.UserId == userId && knowledge.EntryType == oldKey)
            .ToListAsync(ct);

        directory.Key = newKey;
        directory.Name = newKey;
        directory.UpdatedAt = DateTime.UtcNow;
        foreach (var knowledge in affected)
        {
            knowledge.EntryType = newKey;
        }

        await _db.SaveChangesAsync(ct);
        foreach (var knowledge in affected)
        {
            await EnqueueKnowledgeIndexAsync(userId, knowledge, ct);
        }
        await InvalidateKnowledgeCachesForEntriesAsync(userId, affected, ct);

        return new KnowledgeDirectoryResponse
        {
            Key = directory.Key,
            Name = directory.Name,
            Description = "用户自定义知识目录",
            IsSystem = false,
            EntryCount = affected.Count,
            CreatedAt = directory.CreatedAt,
            UpdatedAt = directory.UpdatedAt
        };
    }

    public async Task DeleteKnowledgeDirectoryAsync(string key, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();
        var normalizedKey = NormalizeDirectoryKey(key);
        if (IsSystemDirectory(normalizedKey))
            throw new InvalidOperationException("System directories cannot be deleted.");

        var directory = await _db.KnowledgeDirectories
            .FirstOrDefaultAsync(item => item.UserId == userId && item.Key == normalizedKey, ct);
        if (directory == null)
            throw new KeyNotFoundException($"Knowledge directory {normalizedKey} not found.");

        var affected = await _db.KnowledgeBases
            .Where(knowledge => knowledge.UserId == userId && knowledge.EntryType == normalizedKey)
            .ToListAsync(ct);
        foreach (var knowledge in affected)
        {
            knowledge.EntryType = UncategorizedDirectoryKey;
        }

        _db.KnowledgeDirectories.Remove(directory);
        await _db.SaveChangesAsync(ct);
        foreach (var knowledge in affected)
        {
            await EnqueueKnowledgeIndexAsync(userId, knowledge, ct);
        }
        await InvalidateKnowledgeCachesForEntriesAsync(userId, affected, ct);
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
        var usageContexts = await LoadUsageContextsByKnowledgeIdAsync(userId, new[] { knowledge.Id }, ct);
        var sourceProjectTitles = await LoadProjectTitlesAsync(
            userId,
            new[] { knowledge.SourceProjectId },
            ct);
        var constraintEvidence = await LoadConstraintEvidenceByKnowledgeIdAsync(userId, new[] { knowledge.Id }, ct);
        return MapToResponse(
            knowledge,
            knowledge.SourceProjectId,
            usage.GetValueOrDefault(knowledge.Id),
            usageContexts.GetValueOrDefault(knowledge.Id),
            sourceProjectTitles.GetValueOrDefault(knowledge.SourceProjectId ?? string.Empty),
            constraintEvidence.GetValueOrDefault(knowledge.Id));
    }

    public async Task<KnowledgeResponse> UpdateKnowledgeAsync(
        string knowledgeId, UpdateKnowledgeRequest request, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var knowledge = await _db.KnowledgeBases
            .FirstOrDefaultAsync(k => k.Id == knowledgeId && k.UserId == userId, ct);

        if (knowledge == null)
            throw new KeyNotFoundException($"Knowledge entry {knowledgeId} not found");

        if (!string.IsNullOrWhiteSpace(request.EntryType))
            knowledge.EntryType = NormalizeDirectoryKey(request.EntryType);

        if (request.Title != null)
            knowledge.Title = request.Title;

        if (request.Content != null)
            knowledge.Content = request.Content;

        if (request.Tags != null)
        {
            var tags = request.Tags
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            knowledge.Tags = tags.Count > 0 ? JsonSerializer.Serialize(tags) : null;
        }

        if (request.Weight.HasValue)
            knowledge.Weight = Math.Clamp(request.Weight.Value, 1, 10);

        if (request.IsArchived.HasValue)
            knowledge.IsArchived = request.IsArchived.Value;

        await _db.SaveChangesAsync(ct);
        await EnqueueKnowledgeIndexAsync(userId, knowledge, ct);
        await InvalidateUserKnowledgeCachesAsync(userId, ct);
        if (!string.IsNullOrWhiteSpace(knowledge.SourceProjectId))
            await InvalidateProjectKnowledgeCachesAsync(userId, knowledge.SourceProjectId, ct);

        _logger.LogInformation("Updated knowledge entry {KnowledgeId}", knowledgeId);

        var usage = await LoadUsageByKnowledgeIdAsync(
            userId,
            knowledge.SourceProjectId ?? string.Empty,
            new[] { knowledge.Id },
            ct);
        var usageContexts = await LoadUsageContextsByKnowledgeIdAsync(userId, new[] { knowledge.Id }, ct);
        var sourceProjectTitles = await LoadProjectTitlesAsync(
            userId,
            new[] { knowledge.SourceProjectId },
            ct);
        var constraintEvidence = await LoadConstraintEvidenceByKnowledgeIdAsync(userId, new[] { knowledge.Id }, ct);
        return MapToResponse(
            knowledge,
            knowledge.SourceProjectId,
            usage.GetValueOrDefault(knowledge.Id),
            usageContexts.GetValueOrDefault(knowledge.Id),
            sourceProjectTitles.GetValueOrDefault(knowledge.SourceProjectId ?? string.Empty),
            constraintEvidence.GetValueOrDefault(knowledge.Id));
    }

    public async Task DeleteKnowledgeAsync(string knowledgeId, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var knowledge = await _db.KnowledgeBases
            .FirstOrDefaultAsync(k => k.Id == knowledgeId && k.UserId == userId, ct);

        if (knowledge == null)
            throw new KeyNotFoundException($"Knowledge entry {knowledgeId} not found");

        await EnqueueKnowledgeDeleteAsync(userId, knowledge, ct);

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
        var normalizedEntryType = NormalizeOptionalDirectoryKey(request.EntryType);
        var entryTypeKeys = GetEquivalentDirectoryKeys(normalizedEntryType);
        var cacheKey = BuildSearchCacheKey(userId, request.ProjectId, normalizedEntryType, topK, request.Query);
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
            .Where(k => k.UserId == userId && !k.IsArchived)
            .Where(k => entryTypeKeys == null || entryTypeKeys.Contains(k.EntryType))
            .ToDictionaryAsync(k => k.Id, ct);
        var usageByKnowledgeId = await LoadUsageByKnowledgeIdAsync(
            userId,
            request.ProjectId,
            knowledgeById.Keys,
            ct);

        var semanticResults = searchResults
            .Where(r => r.EntityType == "knowledge")
            .Where(r => !string.IsNullOrWhiteSpace(r.EntityId) && knowledgeById.ContainsKey(r.EntityId))
            .GroupBy(r => r.EntityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.Score).First(),
                StringComparer.OrdinalIgnoreCase);

        var textScores = knowledgeById.Values
            .Select(k => new
            {
                Knowledge = k,
                Score = ScoreTextMatch(k, request.Query)
            })
            .Where(x => x.Score > 0)
            .ToDictionary(x => x.Knowledge.Id, x => x.Score, StringComparer.OrdinalIgnoreCase);

        var candidateIds = semanticResults.Keys
            .Concat(textScores.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var results = candidateIds
            .Select(id =>
            {
                var knowledge = knowledgeById[id];
                semanticResults.TryGetValue(id, out var semanticHit);
                textScores.TryGetValue(id, out var textScore);
                var score = Math.Max(semanticHit?.Score ?? 0, textScore);
                var content = string.IsNullOrWhiteSpace(semanticHit?.Content)
                    ? knowledge.Content
                    : semanticHit!.Content;
                return new
                {
                    Knowledge = knowledge,
                    Content = content,
                    Score = score,
                    TextScore = textScore,
                    EntryTypeRank = GetEntryTypeRank(knowledge.EntryType)
                };
            })
            .OrderByDescending(x => x.TextScore)
            .ThenByDescending(x => x.EntryTypeRank)
            .ThenByDescending(x => x.Score)
            .ThenByDescending(x => x.Knowledge.Weight)
            .ThenByDescending(x => x.Knowledge.CreatedAt)
            .Take(topK)
            .Select(x => MapToSearchResult(
                x.Knowledge,
                x.Content,
                x.Score,
                usageByKnowledgeId.GetValueOrDefault(x.Knowledge.Id)))
            .ToList();

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
        string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();
        await EnsureProjectOwnedAsync(userId, projectId, ct);

        var knowledge = await LoadUserKnowledgeAsync(userId, knowledgeId, ct);

        if (_projectKnowledgeUsage != null)
        {
            var counted = await _projectKnowledgeUsage.MarkReferencedAsync(
                    userId,
                    projectId,
                    knowledge.Id,
                    sessionId,
                    runId,
                    idempotencyKey,
                    ct)
                .ConfigureAwait(false);
            if (!counted)
                return;
        }

        knowledge.UsageCount++;
        await _db.SaveChangesAsync(ct);
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

    private async Task InvalidateKnowledgeCachesForEntriesAsync(
        string userId,
        IReadOnlyCollection<KnowledgeBase> entries,
        CancellationToken ct)
    {
        await InvalidateUserKnowledgeCachesAsync(userId, ct);

        var projectIds = entries
            .Select(entry => entry.SourceProjectId)
            .Where(projectId => !string.IsNullOrWhiteSpace(projectId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var projectId in projectIds)
        {
            await InvalidateProjectKnowledgeCachesAsync(userId, projectId!, ct);
        }
    }

    private static bool IsSystemDirectory(string key) => SystemDirectories.ContainsKey(NormalizeDirectoryKey(key));

    private static string NormalizeDirectoryKey(string? value)
    {
        var key = value?.Trim();
        if (string.IsNullOrWhiteSpace(key))
            return UncategorizedDirectoryKey;
        return LegacyDirectoryAliases.GetValueOrDefault(key, key);
    }

    private static string? NormalizeOptionalDirectoryKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : NormalizeDirectoryKey(value);

    private static List<string>? GetEquivalentDirectoryKeys(string? normalizedKey)
    {
        if (string.IsNullOrWhiteSpace(normalizedKey))
            return null;

        return LegacyDirectoryAliases
            .Where(item => string.Equals(item.Value, normalizedKey, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Key)
            .Append(normalizedKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    private async Task<KnowledgeBase?> FindKnowledgeByIdempotencyKeyAsync(
        string userId,
        string projectId,
        string idempotencyKey,
        CancellationToken ct) =>
        await _db.KnowledgeBases
            .AsNoTracking()
            .FirstOrDefaultAsync(knowledge =>
                    knowledge.UserId == userId &&
                    knowledge.SourceProjectId == projectId &&
                    knowledge.IdempotencyKey == idempotencyKey,
                ct)
            .ConfigureAwait(false);

    private async Task<KnowledgeDirectory?> FindKnowledgeDirectoryByIdempotencyKeyAsync(
        string userId,
        string idempotencyKey,
        CancellationToken ct) =>
        await _db.KnowledgeDirectories
            .AsNoTracking()
            .FirstOrDefaultAsync(directory =>
                    directory.UserId == userId &&
                    directory.IdempotencyKey == idempotencyKey,
                ct)
            .ConfigureAwait(false);

    private async Task<KnowledgeDirectoryResponse> MapDirectoryResponseAsync(
        KnowledgeDirectory directory,
        CancellationToken ct)
    {
        var entryCount = await _db.KnowledgeBases
            .CountAsync(knowledge =>
                    knowledge.UserId == directory.UserId &&
                    knowledge.EntryType == directory.Key,
                ct)
            .ConfigureAwait(false);

        return new KnowledgeDirectoryResponse
        {
            Key = directory.Key,
            Name = directory.Name,
            Description = "用户自定义知识目录",
            IsSystem = false,
            EntryCount = entryCount,
            CreatedAt = directory.CreatedAt,
            UpdatedAt = directory.UpdatedAt
        };
    }

    private async Task<KnowledgeResponse> MapKnowledgeWithContextAsync(
        KnowledgeBase knowledge,
        string projectId,
        CancellationToken ct)
    {
        var usage = await LoadUsageByKnowledgeIdAsync(
            knowledge.UserId,
            projectId,
            new[] { knowledge.Id },
            ct);
        var usageContexts = await LoadUsageContextsByKnowledgeIdAsync(knowledge.UserId, new[] { knowledge.Id }, ct);
        var sourceProjectTitles = await LoadProjectTitlesAsync(
            knowledge.UserId,
            new[] { knowledge.SourceProjectId },
            ct);
        var constraintEvidence = await LoadConstraintEvidenceByKnowledgeIdAsync(knowledge.UserId, new[] { knowledge.Id }, ct);
        return MapToResponse(
            knowledge,
            projectId,
            usage.GetValueOrDefault(knowledge.Id),
            usageContexts.GetValueOrDefault(knowledge.Id),
            sourceProjectTitles.GetValueOrDefault(knowledge.SourceProjectId ?? string.Empty),
            constraintEvidence.GetValueOrDefault(knowledge.Id));
    }

    private static string BuildExtractedKnowledgeIdempotencyKey(CreateExtractedKnowledgeRequest request) =>
        $"extracted:{request.SourceUploadTaskId}:{request.ChunkIndex?.ToString() ?? "none"}:{Sha256(string.Join('|', request.ProjectId, NormalizeDirectoryKey(request.EntryType), request.Title, request.Content))}";

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static KnowledgeResponse MapToResponse(
        KnowledgeBase knowledge,
        string? projectId,
        KnowledgeUsageSnapshot? usage = null,
        IReadOnlyList<KnowledgeProjectUsageResponse>? usageContexts = null,
        string? sourceProjectTitle = null,
        IReadOnlyList<KnowledgeConstraintEvidenceResponse>? constraintEvidence = null)
    {
        return new KnowledgeResponse
        {
            Id = knowledge.Id,
            UsageProjectId = projectId,
            SourceProjectId = knowledge.SourceProjectId,
            SourceProjectTitle = sourceProjectTitle,
            SourceType = knowledge.SourceType,
            SourceUploadTaskId = knowledge.SourceUploadTaskId,
            ChunkIndex = knowledge.ChunkIndex,
            ExtractionContext = knowledge.ExtractionContext,
            EntryType = NormalizeDirectoryKey(knowledge.EntryType),
            Title = knowledge.Title,
            Content = knowledge.Content,
            Tags = ParseTags(knowledge.Tags),
            Weight = knowledge.Weight,
            UsageCount = knowledge.UsageCount,
            CreatedAt = knowledge.CreatedAt,
            IsArchived = knowledge.IsArchived,
            VectorId = knowledge.VectorId,
            ProjectUsageStatus = usage?.Status ?? "none",
            ProjectUsageCount = usage?.UsageCount ?? 0,
            ProjectLastUsedAt = usage?.LastUsedAt,
            ProjectUsages = usageContexts?.ToList() ?? new List<KnowledgeProjectUsageResponse>(),
            ConstraintEvidence = constraintEvidence?.ToList() ?? new List<KnowledgeConstraintEvidenceResponse>()
        };
    }

    private static List<string> ParseTags(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new List<string>();

        try
        {
            var tags = JsonSerializer.Deserialize<List<string>>(raw);
            if (tags != null)
            {
                return tags
                    .Select(t => t.Trim())
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
        catch (JsonException)
        {
            return new List<string>();
        }

        return new List<string>();
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
            SourceType = knowledge.SourceType,
            SourceUploadTaskId = knowledge.SourceUploadTaskId,
            ChunkIndex = knowledge.ChunkIndex,
            ExtractionContext = knowledge.ExtractionContext,
            EntryType = NormalizeDirectoryKey(knowledge.EntryType),
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

    private async Task<Dictionary<string, List<KnowledgeProjectUsageResponse>>> LoadUsageContextsByKnowledgeIdAsync(
        string userId,
        IEnumerable<string> knowledgeIds,
        CancellationToken ct)
    {
        var ids = knowledgeIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<string, List<KnowledgeProjectUsageResponse>>(StringComparer.OrdinalIgnoreCase);
        }

        var usages = await _db.ProjectKnowledgeUsages
            .AsNoTracking()
            .Where(u => u.UserId == userId && ids.Contains(u.KnowledgeId))
            .Join(
                _db.NovelProjects.AsNoTracking().Where(p => p.UserId == userId),
                usage => usage.ProjectId,
                project => project.Id,
                (usage, project) => new
                {
                    usage.KnowledgeId,
                    usage.ProjectId,
                    ProjectTitle = project.Title,
                    usage.Status,
                    usage.UsageCount,
                    usage.FirstSeenAt,
                    usage.LastUsedAt
                })
            .OrderByDescending(row => row.LastUsedAt ?? row.FirstSeenAt)
            .ToListAsync(ct);

        return usages
            .GroupBy(row => row.KnowledgeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => new KnowledgeProjectUsageResponse
                {
                    ProjectId = row.ProjectId,
                    ProjectTitle = row.ProjectTitle,
                    Status = row.Status,
                    UsageCount = row.UsageCount,
                    FirstSeenAt = row.FirstSeenAt,
                    LastUsedAt = row.LastUsedAt
                }).ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, List<KnowledgeConstraintEvidenceResponse>>> LoadConstraintEvidenceByKnowledgeIdAsync(
        string userId,
        IEnumerable<string> knowledgeIds,
        CancellationToken ct)
    {
        var ids = knowledgeIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0)
            return new Dictionary<string, List<KnowledgeConstraintEvidenceResponse>>(StringComparer.OrdinalIgnoreCase);

        var snapshots = await _db.ProjectFactSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.UserId == userId)
            .Where(snapshot => snapshot.SnapshotJson.Contains("knowledgeConstraintEvidence"))
            .Join(
                _db.NovelProjects.AsNoTracking().Where(project => project.UserId == userId),
                snapshot => snapshot.ProjectId,
                project => project.Id,
                (snapshot, project) => new
                {
                    Snapshot = snapshot,
                    ProjectTitle = project.Title
                })
            .OrderByDescending(row => row.Snapshot.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        return snapshots
            .SelectMany(row => ParseConstraintEvidence(row.Snapshot, row.ProjectTitle, ids))
            .GroupBy(item => item.KnowledgeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.CreatedAt)
                    .Take(24)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<KnowledgeConstraintEvidenceResponse> ParseConstraintEvidence(
        ProjectFactSnapshot snapshot,
        string projectTitle,
        IReadOnlySet<string> knowledgeIds)
    {
        if (string.IsNullOrWhiteSpace(snapshot.SnapshotJson))
            yield break;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(snapshot.SnapshotJson);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (document)
        {
            var root = document.RootElement;
            if (!root.TryGetProperty("knowledgeConstraintEvidence", out var evidence) ||
                evidence.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }

            var chapterId = FirstNonEmpty(GetJsonString(root, "chapterId"), snapshot.ChapterId);
            foreach (var item in evidence.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var knowledgeId = GetJsonString(item, "knowledgeId");
                if (string.IsNullOrWhiteSpace(knowledgeId) || !knowledgeIds.Contains(knowledgeId))
                    continue;

                yield return new KnowledgeConstraintEvidenceResponse
                {
                    KnowledgeId = knowledgeId,
                    Title = FirstNonEmpty(GetJsonString(item, "title"), knowledgeId),
                    EntryType = GetJsonString(item, "entryType"),
                    Subject = GetJsonString(item, "subject"),
                    ConstraintLevel = GetJsonString(item, "constraintLevel"),
                    PackagePolicy = GetJsonString(item, "packagePolicy"),
                    EvidenceStatus = FirstNonEmpty(GetJsonString(item, "evidenceStatus"), GetJsonString(item, "status")),
                    GateStatus = GetJsonString(item, "gateStatus"),
                    ProjectId = snapshot.ProjectId,
                    ProjectTitle = projectTitle,
                    ChapterId = chapterId,
                    FactSnapshotId = snapshot.Id,
                    FactSnapshotVersion = snapshot.VersionNumber,
                    CreatedAt = snapshot.CreatedAt,
                    AllowedTerms = GetJsonStringArray(item, "allowedTerms").ToList(),
                    ForbiddenTerms = GetJsonStringArray(item, "forbiddenTerms").ToList(),
                    Violations = GetJsonStringArray(item, "violations").ToList()
                };
            }
        }
    }

    private async Task<Dictionary<string, string>> LoadProjectTitlesAsync(
        string userId,
        IEnumerable<string?> projectIds,
        CancellationToken ct)
    {
        var ids = projectIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return await _db.NovelProjects
            .AsNoTracking()
            .Where(project => project.UserId == userId && ids.Contains(project.Id))
            .ToDictionaryAsync(project => project.Id, project => project.Title, StringComparer.OrdinalIgnoreCase, ct);
    }

    private sealed record KnowledgeUsageSnapshot(string Status, int UsageCount, DateTime? LastUsedAt);

    private static string GetJsonString(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static IEnumerable<string> GetJsonStringArray(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return property.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value));
    }

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

    private static int GetEntryTypeRank(string entryType) =>
        NormalizeDirectoryKey(entryType) switch
        {
            "HardFact" => 4,
            "GenrePrinciple" => 3,
            "AntiTropeStrategy" => 2,
            "TropePattern" => 1,
            _ => 0
        };

    private async Task EnqueueKnowledgeIndexAsync(string userId, KnowledgeBase knowledge, CancellationToken ct)
    {
        await _truthStore.EnqueueOutboxAsync(
            new EnqueueOutboxEventRequest(
                UserId: userId,
                ProjectId: knowledge.SourceProjectId,
                RuntimeRunId: null,
                EventType: "index_knowledge_content",
                AggregateType: "knowledge",
                AggregateId: knowledge.Id,
                PayloadJson: "{}"),
            ct);
    }

    private async Task EnsureKnowledgeIndexOutboxAsync(string userId, KnowledgeBase knowledge, CancellationToken ct)
    {
        var exists = await _db.OutboxEvents
            .AsNoTracking()
            .AnyAsync(evt =>
                    evt.EventType == "index_knowledge_content" &&
                    evt.AggregateType == "knowledge" &&
                    evt.AggregateId == knowledge.Id,
                ct)
            .ConfigureAwait(false);
        if (!exists)
            await EnqueueKnowledgeIndexAsync(userId, knowledge, ct).ConfigureAwait(false);
    }

    private async Task EnqueueKnowledgeDeleteAsync(string userId, KnowledgeBase knowledge, CancellationToken ct)
    {
        await _truthStore.EnqueueOutboxAsync(
            new EnqueueOutboxEventRequest(
                UserId: userId,
                ProjectId: knowledge.SourceProjectId,
                RuntimeRunId: null,
                EventType: "delete_knowledge_content",
                AggregateType: "knowledge",
                AggregateId: knowledge.Id,
                PayloadJson: "{}"),
            ct);
    }
}
