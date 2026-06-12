using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.VectorStore;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

/// <summary>
/// Service for managing knowledge base entries with semantic search capabilities.
/// </summary>
public class KnowledgeService : IKnowledgeService
{
    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUserService;
    private readonly SemanticSearchService _searchService;
    private readonly IVectorStore _vectorStore;
    private readonly IMicroEmbeddingService _embedding;
    private readonly ILogger<KnowledgeService> _logger;

    public KnowledgeService(
        NovelAgentDbContext db,
        ICurrentUserService currentUserService,
        SemanticSearchService searchService,
        IVectorStore vectorStore,
        IMicroEmbeddingService embedding,
        ILogger<KnowledgeService> logger)
    {
        _db = db;
        _currentUserService = currentUserService;
        _searchService = searchService;
        _vectorStore = vectorStore;
        _embedding = embedding;
        _logger = logger;
    }

    public async Task<KnowledgeResponse> CreateKnowledgeAsync(
        CreateKnowledgeRequest request, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        // Verify project ownership
        var project = await _db.NovelProjects
            .FirstOrDefaultAsync(p => p.Id == request.ProjectId && p.UserId == userId, ct);

        if (project == null)
            throw new KeyNotFoundException($"Project {request.ProjectId} not found");

        var knowledge = new KnowledgeBase
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = request.ProjectId,
            EntryType = request.EntryType,
            Title = request.Title,
            Content = request.Content,
            CreatedAt = DateTime.UtcNow,
            SourceType = request.SourceType ?? "manual",
            SourceFileId = request.SourceFileId,
            ChunkIndex = request.ChunkIndex,
            ExtractionContext = request.ExtractionContext,
            Tags = request.Tags != null && request.Tags.Count > 0
                ? JsonSerializer.Serialize(request.Tags)
                : null,
            Weight = request.Weight ?? 5
        };

        _db.KnowledgeBases.Add(knowledge);
        await _db.SaveChangesAsync(ct);

        // Vectorize to Qdrant
        _ = Task.Run(async () =>
        {
            try
            {
                var vector = await _embedding.EncodeAsync($"{knowledge.Title} {knowledge.Content}", EmbeddingMode.Passage, ct);
                await _vectorStore.UpsertVectorsAsync(userId, new List<VectorData>
                {
                    new()
                    {
                        Id = Guid.NewGuid().ToString(),
                        Vector = vector,
                        UserId = userId,
                        ProjectId = knowledge.ProjectId,
                        SourceType = "knowledge",
                        SourceId = knowledge.Id,
                        Content = knowledge.Content,
                    }
                }, ct);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to vectorize knowledge {Id}", knowledge.Id); }
        }, ct);

        _logger.LogInformation("Created knowledge entry {KnowledgeId} in project {ProjectId}", knowledge.Id, request.ProjectId);

        return MapToResponse(knowledge);
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
            .Where(k => k.ProjectId == projectId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);

        return knowledgeEntries.Select(MapToResponse).ToList();
    }

    public async Task<KnowledgeResponse> GetKnowledgeAsync(string knowledgeId, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var knowledge = await _db.KnowledgeBases
            .Include(k => k.Project)
            .FirstOrDefaultAsync(k => k.Id == knowledgeId, ct);

        if (knowledge == null)
            throw new KeyNotFoundException($"Knowledge entry {knowledgeId} not found");

        // Verify project ownership
        if (knowledge.Project.UserId != userId)
            throw new UnauthorizedAccessException("Access denied");

        return MapToResponse(knowledge);
    }

    public async Task<KnowledgeResponse> UpdateKnowledgeAsync(
        string knowledgeId, UpdateKnowledgeRequest request, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var knowledge = await _db.KnowledgeBases
            .Include(k => k.Project)
            .FirstOrDefaultAsync(k => k.Id == knowledgeId, ct);

        if (knowledge == null)
            throw new KeyNotFoundException($"Knowledge entry {knowledgeId} not found");

        // Verify project ownership
        if (knowledge.Project.UserId != userId)
            throw new UnauthorizedAccessException("Access denied");

        if (request.Title != null)
            knowledge.Title = request.Title;

        if (request.Content != null)
            knowledge.Content = request.Content;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Updated knowledge entry {KnowledgeId}", knowledgeId);

        return MapToResponse(knowledge);
    }

    public async Task DeleteKnowledgeAsync(string knowledgeId, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var knowledge = await _db.KnowledgeBases
            .Include(k => k.Project)
            .FirstOrDefaultAsync(k => k.Id == knowledgeId, ct);

        if (knowledge == null)
            throw new KeyNotFoundException($"Knowledge entry {knowledgeId} not found");

        // Verify project ownership
        if (knowledge.Project.UserId != userId)
            throw new UnauthorizedAccessException("Access denied");

        _db.KnowledgeBases.Remove(knowledge);
        await _db.SaveChangesAsync(ct);

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

        // Perform semantic search first, then fill gaps from the authoritative DB rows.
        var searchResults = await _searchService.SearchInProjectAsync(
            userId,
            request.ProjectId,
            request.Query,
            topK,
            ct);

        var knowledgeById = await _db.KnowledgeBases
            .AsNoTracking()
            .Where(k => k.ProjectId == request.ProjectId)
            .Where(k => string.IsNullOrEmpty(request.EntryType) || k.EntryType == request.EntryType)
            .ToDictionaryAsync(k => k.Id, ct);

        var results = searchResults
            .Where(r => r.EntityType == "knowledge")
            .Where(r => !string.IsNullOrWhiteSpace(r.EntityId) && knowledgeById.ContainsKey(r.EntityId))
            .Select(r => new KnowledgeSearchResult
            {
                Id = r.EntityId,
                EntryType = knowledgeById[r.EntityId].EntryType,
                Title = knowledgeById[r.EntityId].Title,
                Content = string.IsNullOrWhiteSpace(r.Content) ? knowledgeById[r.EntityId].Content : r.Content,
                Score = r.Score
            })
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
                .Select(x => new KnowledgeSearchResult
                {
                    Id = x.Knowledge.Id,
                    EntryType = x.Knowledge.EntryType,
                    Title = x.Knowledge.Title,
                    Content = x.Knowledge.Content,
                    Score = x.Score
                });

            results.AddRange(textMatches);
        }

        _logger.LogInformation("Knowledge search in project {ProjectId} returned {ResultCount} results", request.ProjectId, results.Count);

        return results;
    }

    public async Task IncrementUsageAsync(string knowledgeId, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var knowledge = await _db.KnowledgeBases
            .Include(k => k.Project)
            .FirstOrDefaultAsync(k => k.Id == knowledgeId, ct);

        if (knowledge == null)
            throw new KeyNotFoundException($"Knowledge entry {knowledgeId} not found");

        // Verify project ownership
        if (knowledge.Project.UserId != userId)
            throw new UnauthorizedAccessException("Access denied");

        knowledge.UsageCount++;
        await _db.SaveChangesAsync(ct);

        _logger.LogDebug("Incremented usage count for knowledge entry {KnowledgeId}", knowledgeId);
    }

    private static KnowledgeResponse MapToResponse(KnowledgeBase knowledge)
    {
        return new KnowledgeResponse
        {
            Id = knowledge.Id,
            ProjectId = knowledge.ProjectId,
            EntryType = knowledge.EntryType,
            Title = knowledge.Title,
            Content = knowledge.Content,
            UsageCount = knowledge.UsageCount,
            CreatedAt = knowledge.CreatedAt,
            VectorId = knowledge.VectorId
        };
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
}
