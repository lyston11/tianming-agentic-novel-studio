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
            ExtractionContext = request.ExtractionContext
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

        // Perform semantic search
        var searchResults = await _searchService.SearchInProjectAsync(
            userId,
            request.ProjectId,
            request.Query,
            request.TopK,
            ct);

        // Filter by entry type if specified
        var results = searchResults
            .Where(r => r.EntityType == "knowledge")
            .Where(r => string.IsNullOrEmpty(request.EntryType) || r.EntityType == request.EntryType)
            .Select(r => new KnowledgeSearchResult
            {
                Id = r.EntityId,
                EntryType = r.EntityType,
                Title = r.ChunkId,
                Content = r.Content,
                Score = r.Score
            })
            .ToList();

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
}
