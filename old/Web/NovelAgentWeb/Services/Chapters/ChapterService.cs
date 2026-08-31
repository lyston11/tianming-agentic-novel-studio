using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Models.Chapters;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.Chapters;

/// <summary>
/// Implementation of chapter CRUD operations backed by database truth records
/// and asynchronous production outbox indexing.
/// </summary>
public class ChapterService : IChapterService
{
    private readonly NovelAgentDbContext _context;
    private readonly ILogger<ChapterService> _logger;
    private readonly IContentDocumentService _contentDocuments;
    private readonly IProductionTruthStore _truthStore;
    private readonly IProductionWorkflowBridge _productionWorkflowBridge;
    private readonly IProductionChainProjectionService _productionChainProjection;

    public ChapterService(
        NovelAgentDbContext context,
        ILogger<ChapterService> logger,
        IContentDocumentService contentDocuments,
        IProductionTruthStore truthStore,
        IProductionWorkflowBridge productionWorkflowBridge,
        IProductionChainProjectionService productionChainProjection)
    {
        _context = context;
        _logger = logger;
        _contentDocuments = contentDocuments;
        _truthStore = truthStore;
        _productionWorkflowBridge = productionWorkflowBridge;
        _productionChainProjection = productionChainProjection;
    }

    public async Task<ChapterResponse> CreateChapterAsync(
        CreateChapterRequest request,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        // Verify project ownership
        var project = await _context.NovelProjects
            .FirstOrDefaultAsync(p => p.Id == request.ProjectId, cancellationToken);

        if (project == null)
        {
            throw new KeyNotFoundException($"Project with ID {request.ProjectId} not found");
        }

        if (!isAdmin && project.UserId != userId)
        {
            throw new UnauthorizedAccessException("You do not have permission to create chapters in this project");
        }

        var idempotencyKey = EmptyToNull(request.IdempotencyKey);

        // Verify volume ownership if provided
        if (!string.IsNullOrEmpty(request.VolumeId))
        {
            var volume = await _context.Volumes
                .FirstOrDefaultAsync(v => v.Id == request.VolumeId && v.ProjectId == request.ProjectId, cancellationToken);

            if (volume == null)
            {
                throw new KeyNotFoundException($"Volume with ID {request.VolumeId} not found in project {request.ProjectId}");
            }
        }

        if (idempotencyKey != null)
        {
            var existing = await FindChapterByIdempotencyKeyAsync(
                    request.ProjectId,
                    idempotencyKey,
                    cancellationToken)
                .ConfigureAwait(false);
            if (existing != null)
                return await MapChapterWithContentAsync(userId, existing, cancellationToken).ConfigureAwait(false);
        }

        // Check for duplicate chapter number in project
        var existingChapter = await _context.Chapters
            .FirstOrDefaultAsync(c => c.ProjectId == request.ProjectId && c.ChapterNumber == request.ChapterNumber, cancellationToken);

        if (existingChapter != null)
        {
            throw new InvalidOperationException($"Chapter number {request.ChapterNumber} already exists in project {request.ProjectId}");
        }

        // Begin transaction for atomic operation
        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // 1. Create chapter entity
            var chapter = new Chapter
            {
                Id = Guid.NewGuid().ToString(),
                ProjectId = request.ProjectId,
                VolumeId = request.VolumeId,
                Title = request.Title,
                ChapterNumber = request.ChapterNumber,
                Status = request.Status,
                WordCount = CountWords(request.Content),
                IdempotencyKey = idempotencyKey,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Chapters.Add(chapter);
            await _context.SaveChangesAsync(cancellationToken);

            // 2. Persist content in database truth records and queue async indexing
            var document = await SaveChapterContentDocumentAsync(userId, chapter, request.Content, cancellationToken);
            await CreateChapterVersionAndIndexOutboxAsync(userId, chapter, document, null, cancellationToken);

            // Commit transaction
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation("Created chapter {ChapterId} in project {ProjectId} and queued indexing",
                chapter.Id, request.ProjectId);

            // Return response with content
            return MapToResponse(chapter, request.Content);
        }
        catch (DbUpdateException ex) when (idempotencyKey != null)
        {
            await transaction.RollbackAsync(cancellationToken);
            _context.ChangeTracker.Clear();
            var existing = await FindChapterByIdempotencyKeyAsync(
                    request.ProjectId,
                    idempotencyKey,
                    cancellationToken)
                .ConfigureAwait(false);
            if (existing != null)
                return await MapChapterWithContentAsync(userId, existing, cancellationToken).ConfigureAwait(false);

            _logger.LogError(ex, "Failed to create chapter in project {ProjectId}", request.ProjectId);
            throw;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Failed to create chapter in project {ProjectId}", request.ProjectId);
            throw;
        }
    }

    public async Task<ChapterResponse> UpdateChapterAsync(
        string chapterId,
        UpdateChapterRequest request,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        // Get chapter with project info
        var chapter = await _context.Chapters
            .Include(c => c.Project)
            .FirstOrDefaultAsync(c => c.Id == chapterId, cancellationToken);

        if (chapter == null)
        {
            throw new KeyNotFoundException($"Chapter with ID {chapterId} not found");
        }

        // Verify ownership
        if (!isAdmin && chapter.Project.UserId != userId)
        {
            throw new UnauthorizedAccessException("You do not have permission to update this chapter");
        }

        // Verify volume ownership if changing volume
        if (request.VolumeId != null && request.VolumeId != chapter.VolumeId)
        {
            var volume = await _context.Volumes
                .FirstOrDefaultAsync(v => v.Id == request.VolumeId && v.ProjectId == chapter.ProjectId, cancellationToken);

            if (volume == null)
            {
                throw new KeyNotFoundException($"Volume with ID {request.VolumeId} not found in project {chapter.ProjectId}");
            }
        }

        // Begin transaction for atomic operation
        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var contentChanged = false;
            string? newContent = null;

            // Update metadata
            if (!string.IsNullOrEmpty(request.Title))
            {
                chapter.Title = request.Title;
            }

            if (!string.IsNullOrEmpty(request.Status))
            {
                chapter.Status = request.Status;
            }

            if (request.VolumeId != null)
            {
                chapter.VolumeId = request.VolumeId;
            }

            chapter.UpdatedAt = DateTime.UtcNow;

            // Update content if provided
            if (!string.IsNullOrEmpty(request.Content))
            {
                contentChanged = true;
                newContent = request.Content;
                chapter.WordCount = CountWords(newContent);

                var document = await SaveChapterContentDocumentAsync(userId, chapter, newContent, cancellationToken);
                await CreateChapterVersionAndIndexOutboxAsync(userId, chapter, document, null, cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);

            // Commit transaction
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation("Updated chapter {ChapterId}, content changed: {ContentChanged}",
                chapterId, contentChanged);

            var content = newContent ?? await _contentDocuments.GetTextAsync(userId, chapter.ProjectId, "chapter", chapter.Id, "chapter_body", cancellationToken);

            return MapToResponse(chapter, content);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Failed to update chapter {ChapterId}", chapterId);
            throw;
        }
    }

    public async Task<ChapterResponse> GetChapterByIdAsync(
        string chapterId,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        var chapter = await _context.Chapters
            .Include(c => c.Project)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == chapterId, cancellationToken);

        if (chapter == null)
        {
            throw new KeyNotFoundException($"Chapter with ID {chapterId} not found");
        }

        // Verify ownership
        if (!isAdmin && chapter.Project.UserId != userId)
        {
            throw new UnauthorizedAccessException("You do not have permission to access this chapter");
        }

        var content = await _contentDocuments.GetTextAsync(userId, chapter.ProjectId, "chapter", chapter.Id, "chapter_body", cancellationToken);

        var productionChains = await LoadChapterProductionChainsAsync(
                chapter.ProjectId,
                chapter.Id,
                cancellationToken)
            .ConfigureAwait(false);
        var productionEvidence = await LoadChapterProductionEvidenceAsync(
                chapter.ProjectId,
                chapter.Id,
                productionChains,
                cancellationToken)
            .ConfigureAwait(false);

        return MapToResponse(chapter, content, productionChains, productionEvidence);
    }

    public async Task<List<ChapterVersionResponse>> GetChapterVersionsAsync(
        string chapterId,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        var chapter = await _context.Chapters
            .Include(c => c.Project)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == chapterId, cancellationToken);

        if (chapter == null)
        {
            throw new KeyNotFoundException($"Chapter with ID {chapterId} not found");
        }

        if (!isAdmin && chapter.Project.UserId != userId)
        {
            throw new UnauthorizedAccessException("You do not have permission to access this chapter");
        }

        var versions = await _context.ChapterVersions
            .AsNoTracking()
            .Where(version => version.ChapterId == chapterId)
            .OrderByDescending(version => version.VersionNumber)
            .ThenByDescending(version => version.CreatedAt)
            .ToListAsync(cancellationToken);
        if (versions.Count == 0)
            return new List<ChapterVersionResponse>();

        var packageIds = versions
            .Select(version => version.PackageId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var packages = packageIds.Count == 0
            ? new Dictionary<string, TianmingPackage>(StringComparer.OrdinalIgnoreCase)
            : await _context.TianmingPackages
                .AsNoTracking()
                .Where(package => package.ProjectId == chapter.ProjectId && packageIds.Contains(package.Id))
                .ToDictionaryAsync(package => package.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var documentIds = versions
            .Select(version => version.ContentDocumentId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var chunks = await _context.ContentChunks
            .AsNoTracking()
            .Where(chunk => documentIds.Contains(chunk.DocumentId))
            .OrderBy(chunk => chunk.DocumentId)
            .ThenBy(chunk => chunk.ChunkIndex)
            .Select(chunk => new { chunk.DocumentId, chunk.ChunkText })
            .ToListAsync(cancellationToken);
        var contentByDocumentId = chunks
            .GroupBy(chunk => chunk.DocumentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => string.Join(string.Empty, group.Select(chunk => chunk.ChunkText)),
                StringComparer.OrdinalIgnoreCase);

        return versions.Select(version =>
        {
            packages.TryGetValue(version.PackageId ?? string.Empty, out var package);
            contentByDocumentId.TryGetValue(version.ContentDocumentId, out var content);

            return new ChapterVersionResponse
            {
                Id = version.Id,
                ChapterId = version.ChapterId,
                ContentDocumentId = version.ContentDocumentId,
                VersionNumber = version.VersionNumber,
                Title = version.Title,
                WordCount = version.WordCount,
                Status = version.Status,
                RuntimeRunId = version.RuntimeRunId,
                PackageId = version.PackageId,
                KernelVersion = package?.KernelVersion,
                PromptVersion = package?.PromptVersion,
                GateReportJson = version.GateReportJson,
                AgentReviewJson = version.AgentReviewJson,
                RebuiltFromPackageIds = ParseTopLevelStringArray(package?.KnowledgeSnapshotJson, "rebuiltFromPackageIds"),
                IsCurrent = string.Equals(chapter.CurrentDocumentId, version.ContentDocumentId, StringComparison.OrdinalIgnoreCase),
                ContentPreview = TrimText(content ?? string.Empty, 320),
                CreatedAt = version.CreatedAt
            };
        }).ToList();
    }

    public async Task<ChapterVersionCompareResponse> CompareChapterVersionsAsync(
        string chapterId,
        string leftVersionId,
        string rightVersionId,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(leftVersionId) || string.IsNullOrWhiteSpace(rightVersionId))
        {
            throw new ArgumentException("Both leftVersionId and rightVersionId are required");
        }

        var versions = await GetChapterVersionsAsync(
                chapterId,
                userId,
                isAdmin,
                cancellationToken)
            .ConfigureAwait(false);
        var left = versions.FirstOrDefault(version =>
            string.Equals(version.Id, leftVersionId, StringComparison.OrdinalIgnoreCase));
        var right = versions.FirstOrDefault(version =>
            string.Equals(version.Id, rightVersionId, StringComparison.OrdinalIgnoreCase));
        if (left == null)
        {
            throw new KeyNotFoundException($"Chapter version with ID {leftVersionId} not found");
        }

        if (right == null)
        {
            throw new KeyNotFoundException($"Chapter version with ID {rightVersionId} not found");
        }

        var contentByDocumentId = await LoadContentByDocumentIdAsync(
                new[] { left.ContentDocumentId, right.ContentDocumentId },
                cancellationToken)
            .ConfigureAwait(false);
        contentByDocumentId.TryGetValue(left.ContentDocumentId, out var leftContent);
        contentByDocumentId.TryGetValue(right.ContentDocumentId, out var rightContent);
        var blocks = BuildParagraphDiff(leftContent ?? string.Empty, rightContent ?? string.Empty);
        var changedBlocks = blocks.Count(block => block.Kind != "unchanged");
        var packageIds = new[] { left.PackageId, right.PackageId }
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var packages = packageIds.Count == 0
            ? new Dictionary<string, TianmingPackage>(StringComparer.OrdinalIgnoreCase)
            : await _context.TianmingPackages
                .AsNoTracking()
                .Where(package => packageIds.Contains(package.Id))
                .ToDictionaryAsync(package => package.Id, StringComparer.OrdinalIgnoreCase, cancellationToken)
                .ConfigureAwait(false);
        packages.TryGetValue(left.PackageId ?? string.Empty, out var leftPackage);
        packages.TryGetValue(right.PackageId ?? string.Empty, out var rightPackage);

        return new ChapterVersionCompareResponse
        {
            ChapterId = chapterId,
            Left = left,
            Right = right,
            WordCountDelta = right.WordCount - left.WordCount,
            Summary = $"v{left.VersionNumber} -> v{right.VersionNumber}：{changedBlocks} 处段落差异，字数变化 {right.WordCount - left.WordCount}。",
            DiffBlocks = blocks,
            ProductionAlignment = BuildProductionAlignment(left, right, leftPackage, rightPackage)
        };
    }

    public async Task DeleteChapterAsync(
        string chapterId,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        var chapter = await _context.Chapters
            .Include(c => c.Project)
            .FirstOrDefaultAsync(c => c.Id == chapterId, cancellationToken);

        if (chapter == null)
        {
            throw new KeyNotFoundException($"Chapter with ID {chapterId} not found");
        }

        // Verify ownership
        if (!isAdmin && chapter.Project.UserId != userId)
        {
            throw new UnauthorizedAccessException("You do not have permission to delete this chapter");
        }

        // Begin transaction for atomic operation
        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // 1. Delete from database (this will cascade FK references to NULL)
            _context.Chapters.Remove(chapter);
            await _context.SaveChangesAsync(cancellationToken);

            // 2. Delete content document
            await _contentDocuments.DeleteBySourceAsync(userId, chapter.ProjectId, "chapter", chapter.Id, "chapter_body", cancellationToken);

            // 3. Queue async vector cleanup
            await EnqueueChapterVectorDeletionAsync(userId, chapter.ProjectId, chapter.Id, cancellationToken);

            // Commit transaction
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation("Deleted chapter {ChapterId} from project {ProjectId}",
                chapterId, chapter.ProjectId);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Failed to delete chapter {ChapterId}", chapterId);
            throw;
        }
    }

    public async Task<List<ChapterResponse>> GetChaptersByProjectAsync(
        string projectId,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        // Verify project ownership
        var project = await _context.NovelProjects
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
        {
            throw new KeyNotFoundException($"Project with ID {projectId} not found");
        }

        if (!isAdmin && project.UserId != userId)
        {
            throw new UnauthorizedAccessException("You do not have permission to access this project's chapters");
        }

        // Get all chapters for the project (without content)
        var chapters = await _context.Chapters
            .AsNoTracking()
            .Where(c => c.ProjectId == projectId)
            .OrderBy(c => c.ChapterNumber)
            .ToListAsync(cancellationToken);

        return chapters.Select(c => MapToResponse(c, null)).ToList();
    }

    // Private helper methods

    private async Task<ContentDocument> SaveChapterContentDocumentAsync(
        string userId,
        Chapter chapter,
        string content,
        CancellationToken cancellationToken)
    {
        var document = await _contentDocuments.SaveOrReplaceTextAsync(
            userId,
            chapter.ProjectId,
            "chapter",
            chapter.Id,
            "chapter_body",
            chapter.Title,
            content,
            cancellationToken);
        chapter.CurrentDocumentId = document.Id;
        await _context.SaveChangesAsync(cancellationToken);
        return document;
    }

    private async Task CreateChapterVersionAndIndexOutboxAsync(
        string userId,
        Chapter chapter,
        ContentDocument document,
        string? runtimeRunId,
        CancellationToken cancellationToken)
    {
        var version = await _truthStore.CreateChapterVersionAsync(
            new CreateChapterVersionRequest(
                UserId: userId,
                ProjectId: chapter.ProjectId,
                ChapterId: chapter.Id,
                ContentDocumentId: document.Id,
                Title: chapter.Title,
                WordCount: chapter.WordCount,
                Status: chapter.Status,
                RuntimeRunId: runtimeRunId,
                PackageId: null,
                GateReportJson: null,
                AgentReviewJson: null),
            cancellationToken);

        await _truthStore.EnqueueOutboxAsync(
            new EnqueueOutboxEventRequest(
                UserId: userId,
                ProjectId: chapter.ProjectId,
                RuntimeRunId: runtimeRunId,
                EventType: "index_chapter_content",
                AggregateType: "chapter_version",
                AggregateId: version.Id,
                PayloadJson: "{}"),
            cancellationToken);
    }

    private async Task EnqueueChapterVectorDeletionAsync(
        string userId,
        string projectId,
        string chapterId,
        CancellationToken cancellationToken)
    {
        await _truthStore.EnqueueOutboxAsync(
            new EnqueueOutboxEventRequest(
                UserId: userId,
                ProjectId: projectId,
                RuntimeRunId: null,
                EventType: "delete_chapter_content",
                AggregateType: "chapter",
                AggregateId: chapterId,
                PayloadJson: "{}"),
            cancellationToken);
    }

    private async Task<Chapter?> FindChapterByIdempotencyKeyAsync(
        string projectId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        await _context.Chapters
            .AsNoTracking()
            .FirstOrDefaultAsync(chapter =>
                    chapter.ProjectId == projectId &&
                    chapter.IdempotencyKey == idempotencyKey,
                cancellationToken)
            .ConfigureAwait(false);

    private async Task<ChapterResponse> MapChapterWithContentAsync(
        string userId,
        Chapter chapter,
        CancellationToken cancellationToken)
    {
        var content = await _contentDocuments.GetTextAsync(
                userId,
                chapter.ProjectId,
                "chapter",
                chapter.Id,
                "chapter_body",
                cancellationToken)
            .ConfigureAwait(false);

        return MapToResponse(chapter, content);
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<string> ParseTopLevelStringArray(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(propertyName))
            return new List<string>();

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(propertyName, out var property) ||
                property.ValueKind != JsonValueKind.Array)
            {
                return new List<string>();
            }

            return property
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(24)
                .ToList();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static ChapterVersionProductionAlignment BuildProductionAlignment(
        ChapterVersionResponse left,
        ChapterVersionResponse right,
        TianmingPackage? leftPackage,
        TianmingPackage? rightPackage)
    {
        var packageJson = rightPackage?.KnowledgeSnapshotJson;
        return new ChapterVersionProductionAlignment
        {
            LeftPackageId = left.PackageId ?? string.Empty,
            RightPackageId = right.PackageId ?? string.Empty,
            RebuiltFromPackageIds = right.RebuiltFromPackageIds.Count > 0
                ? right.RebuiltFromPackageIds
                : ParseTopLevelStringArray(packageJson, "rebuiltFromPackageIds"),
            AcceptedCreativeIntents = ParseCreativeIntentAlignment(packageJson),
            SourceRevisionPlans = ParseRevisionPlanAlignment(packageJson),
            AgentReviewDecision = ParseAgentReviewDecision(right.AgentReviewJson),
            AgentReviewChecks = ParseAgentReviewCheckAlignment(right.AgentReviewJson)
        };
    }

    private static List<ChapterVersionCreativeIntentAlignment> ParseCreativeIntentAlignment(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<ChapterVersionCreativeIntentAlignment>();

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("acceptedCreativeIntents", out var property) ||
                property.ValueKind != JsonValueKind.Array)
            {
                return new List<ChapterVersionCreativeIntentAlignment>();
            }

            return property.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => new ChapterVersionCreativeIntentAlignment
                {
                    IntentId = GetJsonString(item, "intentId"),
                    NormalizedIntent = GetJsonString(item, "normalizedIntent"),
                    TargetScope = GetJsonString(item, "targetScope"),
                    TargetChapterId = GetJsonString(item, "targetChapterId"),
                    ImpactLevel = GetJsonString(item, "impactLevel"),
                    Source = GetJsonString(item, "source"),
                    Status = FirstNonEmpty(GetJsonString(item, "status"), "accepted")
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.IntentId) ||
                               !string.IsNullOrWhiteSpace(item.NormalizedIntent))
                .Take(24)
                .ToList();
        }
        catch (JsonException)
        {
            return new List<ChapterVersionCreativeIntentAlignment>();
        }
    }

    private static List<ChapterVersionRevisionPlanAlignment> ParseRevisionPlanAlignment(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<ChapterVersionRevisionPlanAlignment>();

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("sourceRevisionPlans", out var property) ||
                property.ValueKind != JsonValueKind.Array)
            {
                return new List<ChapterVersionRevisionPlanAlignment>();
            }

            return property.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => new ChapterVersionRevisionPlanAlignment
                {
                    RevisionPlanId = GetJsonString(item, "revisionPlanId"),
                    PlanType = GetJsonString(item, "planType"),
                    TargetScope = GetJsonString(item, "targetScope"),
                    TargetChapterId = GetJsonString(item, "targetChapterId"),
                    Status = GetJsonString(item, "status"),
                    AffectedChapterIds = ParseJsonStringArray(GetJsonString(item, "affectedChapterIdsJson")),
                    InvalidatedPackageIds = ParseJsonStringArray(GetJsonString(item, "invalidatedPackageIdsJson")),
                    RiskLevel = GetJsonString(item, "riskLevel"),
                    Recommendation = GetJsonString(item, "recommendation")
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.RevisionPlanId) ||
                               !string.IsNullOrWhiteSpace(item.Recommendation))
                .Take(24)
                .ToList();
        }
        catch (JsonException)
        {
            return new List<ChapterVersionRevisionPlanAlignment>();
        }
    }

    private static string ParseAgentReviewDecision(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return string.Empty;

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? GetJsonString(document.RootElement, "decision")
                : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static List<ChapterVersionAgentReviewCheckAlignment> ParseAgentReviewCheckAlignment(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<ChapterVersionAgentReviewCheckAlignment>();

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("checks", out var property) ||
                property.ValueKind != JsonValueKind.Array)
            {
                return new List<ChapterVersionAgentReviewCheckAlignment>();
            }

            return property.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => new ChapterVersionAgentReviewCheckAlignment
                {
                    Key = GetJsonString(item, "key"),
                    Name = GetJsonString(item, "name"),
                    Status = FirstNonEmpty(GetJsonString(item, "status"), GetJsonString(item, "result")),
                    Message = GetJsonString(item, "message"),
                    Evidence = GetJsonStringArray(item, "evidence")
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Key) ||
                               !string.IsNullOrWhiteSpace(item.Name))
                .Take(24)
                .ToList();
        }
        catch (JsonException)
        {
            return new List<ChapterVersionAgentReviewCheckAlignment>();
        }
    }

    private static string GetJsonString(JsonElement element, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName) ||
            element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property))
        {
            return string.Empty;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? string.Empty,
            JsonValueKind.Number => property.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static List<string> GetJsonStringArray(JsonElement element, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName) ||
            element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return new List<string>();
        }

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToList();
    }

    private static List<string> ParseJsonStringArray(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new List<string>();

        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return new List<string>();

            return document.RootElement.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(24)
                .ToList();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private async Task<Dictionary<string, string>> LoadContentByDocumentIdAsync(
        IReadOnlyCollection<string> documentIds,
        CancellationToken cancellationToken)
    {
        var ids = documentIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ids.Count == 0)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var chunks = await _context.ContentChunks
            .AsNoTracking()
            .Where(chunk => ids.Contains(chunk.DocumentId))
            .OrderBy(chunk => chunk.DocumentId)
            .ThenBy(chunk => chunk.ChunkIndex)
            .Select(chunk => new { chunk.DocumentId, chunk.ChunkText })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return chunks
            .GroupBy(chunk => chunk.DocumentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => string.Join(string.Empty, group.Select(chunk => chunk.ChunkText)),
                StringComparer.OrdinalIgnoreCase);
    }

    private static List<ChapterVersionDiffBlock> BuildParagraphDiff(string leftContent, string rightContent)
    {
        var left = SplitParagraphs(leftContent);
        var right = SplitParagraphs(rightContent);
        var lcs = BuildLcsTable(left, right);
        var blocks = new List<ChapterVersionDiffBlock>();
        var i = 0;
        var j = 0;
        while (i < left.Count && j < right.Count)
        {
            if (string.Equals(left[i], right[j], StringComparison.Ordinal))
            {
                blocks.Add(new ChapterVersionDiffBlock
                {
                    Kind = "unchanged",
                    LeftText = left[i],
                    RightText = right[j]
                });
                i++;
                j++;
                continue;
            }

            if (lcs[i + 1, j] >= lcs[i, j + 1])
            {
                if (j < right.Count && lcs[i + 1, j] == lcs[i, j + 1])
                {
                    blocks.Add(new ChapterVersionDiffBlock
                    {
                        Kind = "changed",
                        LeftText = left[i],
                        RightText = right[j]
                    });
                    i++;
                    j++;
                }
                else
                {
                    blocks.Add(new ChapterVersionDiffBlock
                    {
                        Kind = "removed",
                        LeftText = left[i]
                    });
                    i++;
                }
            }
            else
            {
                blocks.Add(new ChapterVersionDiffBlock
                {
                    Kind = "added",
                    RightText = right[j]
                });
                j++;
            }
        }

        while (i < left.Count)
        {
            blocks.Add(new ChapterVersionDiffBlock
            {
                Kind = "removed",
                LeftText = left[i]
            });
            i++;
        }

        while (j < right.Count)
        {
            blocks.Add(new ChapterVersionDiffBlock
            {
                Kind = "added",
                RightText = right[j]
            });
            j++;
        }

        return blocks;
    }

    private static int[,] BuildLcsTable(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        var table = new int[left.Count + 1, right.Count + 1];
        for (var i = left.Count - 1; i >= 0; i--)
        {
            for (var j = right.Count - 1; j >= 0; j--)
            {
                table[i, j] = string.Equals(left[i], right[j], StringComparison.Ordinal)
                    ? table[i + 1, j + 1] + 1
                    : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }

        return table;
    }

    private static List<string> SplitParagraphs(string content) =>
        content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

    private static string TrimText(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim();
        if (normalized.Length <= maxLength)
            return normalized;

        return normalized[..maxLength] + "...";
    }

    private int CountWords(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return 0;
        }

        // Count Chinese characters and English words
        int count = 0;
        bool inWord = false;

        foreach (char c in content)
        {
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
            }
            else if (c >= 0x4E00 && c <= 0x9FFF) // Chinese characters
            {
                count++;
                inWord = false;
            }
            else if (!inWord)
            {
                count++;
                inWord = true;
            }
        }

        return count;
    }

    private async Task<IReadOnlyList<WorkflowProductionChain>> LoadChapterProductionChainsAsync(
        string projectId,
        string chapterId,
        CancellationToken cancellationToken)
    {
        var productionEvents = await _productionWorkflowBridge
            .LoadProjectEventsAsync(projectId, limit: 240, cancellationToken)
            .ConfigureAwait(false);
        if (productionEvents.Count == 0)
            return Array.Empty<WorkflowProductionChain>();

        return _productionChainProjection
            .BuildWorkflowChains(productionEvents)
            .Where(chain => string.Equals(chain.ChapterId, chapterId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private async Task<ChapterProductionEvidenceResponse> LoadChapterProductionEvidenceAsync(
        string projectId,
        string chapterId,
        IReadOnlyList<WorkflowProductionChain> productionChains,
        CancellationToken cancellationToken)
    {
        var revisionPlanIds = productionChains
            .SelectMany(chain => chain.RevisionPlanIds)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var factSnapshotIds = productionChains
            .Select(chain => chain.FactSnapshotId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var outboxIds = productionChains
            .SelectMany(chain => chain.Steps)
            .Select(step => step.OutboxEventId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var chapterVersionIds = productionChains
            .Select(chain => chain.ChapterVersionId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var revisionPlans = await _context.RevisionPlans
            .AsNoTracking()
            .Where(plan => plan.ProjectId == projectId)
            .OrderByDescending(plan => plan.UpdatedAt)
            .Take(80)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var revisionPlanEvidence = revisionPlans
            .Where(plan =>
                revisionPlanIds.Contains(plan.Id) ||
                string.Equals(plan.TargetChapterId, chapterId, StringComparison.OrdinalIgnoreCase) ||
                ParseJsonStringArray(plan.AffectedChapterIdsJson).Contains(chapterId, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(plan => plan.UpdatedAt)
            .Take(12)
            .Select(plan => new ChapterRevisionPlanEvidenceResponse
            {
                Id = plan.Id,
                Source = plan.Source,
                PlanType = plan.PlanType,
                TargetScope = plan.TargetScope,
                TargetChapterId = plan.TargetChapterId ?? string.Empty,
                Status = plan.Status,
                RiskLevel = plan.RiskLevel,
                Recommendation = plan.Recommendation,
                AffectedChapterIds = ParseJsonStringArray(plan.AffectedChapterIdsJson),
                InvalidatedPackageIds = ParseJsonStringArray(plan.InvalidatedPackageIdsJson),
                UpdatedAt = plan.UpdatedAt
            })
            .ToList();

        var factSnapshots = await _context.ProjectFactSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.ProjectId == projectId)
            .Where(snapshot =>
                snapshot.ChapterId == chapterId ||
                factSnapshotIds.Contains(snapshot.Id) ||
                chapterVersionIds.Contains(snapshot.ChapterVersionId ?? string.Empty))
            .OrderByDescending(snapshot => snapshot.VersionNumber)
            .ThenByDescending(snapshot => snapshot.CreatedAt)
            .Take(12)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var latestFactSnapshot = factSnapshots
            .Select(snapshot => new ChapterFactSnapshotEvidenceResponse
            {
                Id = snapshot.Id,
                ChapterVersionId = snapshot.ChapterVersionId ?? string.Empty,
                VersionNumber = snapshot.VersionNumber,
                Source = snapshot.Source,
                SnapshotPreview = TrimText(snapshot.SnapshotJson, 260),
                CreatedAt = snapshot.CreatedAt
            })
            .FirstOrDefault();

        var outboxEvents = await _context.OutboxEvents
            .AsNoTracking()
            .Where(outbox => outbox.ProjectId == projectId)
            .Where(outbox =>
                outboxIds.Contains(outbox.Id) ||
                outbox.AggregateId == chapterId ||
                chapterVersionIds.Contains(outbox.AggregateId))
            .OrderByDescending(outbox => outbox.UpdatedAt)
            .Take(12)
            .Select(outbox => new ChapterOutboxEvidenceResponse
            {
                Id = outbox.Id,
                EventType = outbox.EventType,
                AggregateType = outbox.AggregateType,
                AggregateId = outbox.AggregateId,
                Status = outbox.Status,
                Attempts = outbox.Attempts,
                LastError = outbox.LastError ?? string.Empty,
                NextAttemptAt = outbox.NextAttemptAt,
                CompletedAt = outbox.CompletedAt,
                UpdatedAt = outbox.UpdatedAt
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ChapterProductionEvidenceResponse
        {
            RevisionPlans = revisionPlanEvidence,
            LatestFactSnapshot = latestFactSnapshot,
            OutboxEvents = outboxEvents
        };
    }

    private ChapterResponse MapToResponse(
        Chapter chapter,
        string? content,
        IReadOnlyList<WorkflowProductionChain>? productionChains = null,
        ChapterProductionEvidenceResponse? productionEvidence = null)
    {
        return new ChapterResponse
        {
            Id = chapter.Id,
            ProjectId = chapter.ProjectId,
            VolumeId = chapter.VolumeId,
            Title = chapter.Title,
            ChapterNumber = chapter.ChapterNumber,
            Status = chapter.Status,
            WordCount = chapter.WordCount,
            Content = content,
            ProductionChains = productionChains ?? Array.Empty<WorkflowProductionChain>(),
            ProductionEvidence = productionEvidence ?? new ChapterProductionEvidenceResponse(),
            CreatedAt = chapter.CreatedAt,
            UpdatedAt = chapter.UpdatedAt
        };
    }
}
