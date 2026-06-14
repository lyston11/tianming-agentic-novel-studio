using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Models.Chapters;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.VectorStore;

namespace TM.Web.NovelAgentWeb.Services.Chapters;

public class ChapterService : IChapterService
{
    private readonly NovelAgentDbContext _context;
    private readonly IVectorStore _vectorStore;
    private readonly IMicroEmbeddingService _embeddingService;
    private readonly IContentDocumentService _contentDocumentService;
    private readonly ILogger<ChapterService> _logger;
    private const int ChunkSize = 4000;
    private const int ChunkOverlap = 200;

    public ChapterService(
        NovelAgentDbContext context,
        IVectorStore vectorStore,
        IMicroEmbeddingService embeddingService,
        IContentDocumentService contentDocumentService,
        ILogger<ChapterService> logger)
    {
        _context = context;
        _vectorStore = vectorStore;
        _embeddingService = embeddingService;
        _contentDocumentService = contentDocumentService;
        _logger = logger;
    }

    public async Task<ChapterResponse> CreateChapterAsync(
        CreateChapterRequest request,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        var project = await _context.NovelProjects
            .FirstOrDefaultAsync(p => p.Id == request.ProjectId, cancellationToken);

        if (project == null)
            throw new KeyNotFoundException($"Project with ID {request.ProjectId} not found");

        if (!isAdmin && project.UserId != userId)
            throw new UnauthorizedAccessException("You do not have permission to create chapters in this project");

        if (!string.IsNullOrEmpty(request.VolumeId))
        {
            var volume = await _context.Volumes
                .FirstOrDefaultAsync(v => v.Id == request.VolumeId && v.ProjectId == request.ProjectId, cancellationToken);

            if (volume == null)
                throw new KeyNotFoundException($"Volume with ID {request.VolumeId} not found in project {request.ProjectId}");
        }

        var existingChapter = await _context.Chapters
            .FirstOrDefaultAsync(c => c.ProjectId == request.ProjectId && c.ChapterNumber == request.ChapterNumber, cancellationToken);

        if (existingChapter != null)
            throw new InvalidOperationException($"Chapter number {request.ChapterNumber} already exists in project {request.ProjectId}");

        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var chapter = new Chapter
            {
                Id = Guid.NewGuid().ToString(),
                ProjectId = request.ProjectId,
                VolumeId = request.VolumeId,
                Title = request.Title,
                ChapterNumber = request.ChapterNumber,
                Status = request.Status,
                WordCount = CountWords(request.Content),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Chapters.Add(chapter);
            await _context.SaveChangesAsync(cancellationToken);

            var contentDoc = await _contentDocumentService.SaveTextAsync(
                userId, request.ProjectId, "chapter", chapter.Id, "chapter_body",
                chapter.Title, request.Content, cancellationToken);

            chapter.ContentDocumentId = contentDoc.Id;
            await _context.SaveChangesAsync(cancellationToken);

            await GenerateAndStoreEmbeddingsAsync(chapter, request.Content, userId, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation("Created chapter {ChapterId} in project {ProjectId}", chapter.Id, request.ProjectId);

            return MapToResponse(chapter, request.Content);
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
        var chapter = await _context.Chapters
            .Include(c => c.Project)
            .FirstOrDefaultAsync(c => c.Id == chapterId, cancellationToken);

        if (chapter == null)
            throw new KeyNotFoundException($"Chapter with ID {chapterId} not found");

        if (!isAdmin && chapter.Project.UserId != userId)
            throw new UnauthorizedAccessException("You do not have permission to update this chapter");

        if (request.VolumeId != null && request.VolumeId != chapter.VolumeId)
        {
            var volume = await _context.Volumes
                .FirstOrDefaultAsync(v => v.Id == request.VolumeId && v.ProjectId == chapter.ProjectId, cancellationToken);

            if (volume == null)
                throw new KeyNotFoundException($"Volume with ID {request.VolumeId} not found in project {chapter.ProjectId}");
        }

        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var contentChanged = false;
            string? newContent = null;

            if (!string.IsNullOrEmpty(request.Title))
                chapter.Title = request.Title;

            if (!string.IsNullOrEmpty(request.Status))
                chapter.Status = request.Status;

            if (request.VolumeId != null)
                chapter.VolumeId = request.VolumeId;

            chapter.UpdatedAt = DateTime.UtcNow;

            if (!string.IsNullOrEmpty(request.Content))
            {
                contentChanged = true;
                newContent = request.Content;
                chapter.WordCount = CountWords(newContent);

                await _contentDocumentService.SaveTextAsync(
                    userId, chapter.ProjectId, "chapter", chapter.Id, "chapter_body",
                    chapter.Title, newContent, cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);

            if (contentChanged && newContent != null)
            {
                await DeleteChapterVectorsAsync(chapter.Id, userId, cancellationToken);
                await GenerateAndStoreEmbeddingsAsync(chapter, newContent, userId, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation("Updated chapter {ChapterId}, content changed: {ContentChanged}",
                chapterId, contentChanged);

            var content = newContent ?? await _contentDocumentService.GetDocumentContentBySourceAsync(
                "chapter", chapter.Id, cancellationToken) ?? "";

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
            throw new KeyNotFoundException($"Chapter with ID {chapterId} not found");

        if (!isAdmin && chapter.Project.UserId != userId)
            throw new UnauthorizedAccessException("You do not have permission to access this chapter");

        var content = await _contentDocumentService.GetDocumentContentBySourceAsync(
            "chapter", chapter.Id, cancellationToken) ?? "";

        return MapToResponse(chapter, content);
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
            throw new KeyNotFoundException($"Chapter with ID {chapterId} not found");

        if (!isAdmin && chapter.Project.UserId != userId)
            throw new UnauthorizedAccessException("You do not have permission to delete this chapter");

        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await _contentDocumentService.DeleteDocumentBySourceAsync("chapter", chapter.Id, cancellationToken);

            _context.Chapters.Remove(chapter);
            await _context.SaveChangesAsync(cancellationToken);

            await DeleteChapterVectorsAsync(chapter.Id, userId, cancellationToken);

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
        var project = await _context.NovelProjects
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
            throw new KeyNotFoundException($"Project with ID {projectId} not found");

        if (!isAdmin && project.UserId != userId)
            throw new UnauthorizedAccessException("You do not have permission to access this project's chapters");

        var chapters = await _context.Chapters
            .AsNoTracking()
            .Where(c => c.ProjectId == projectId)
            .OrderBy(c => c.ChapterNumber)
            .ToListAsync(cancellationToken);

        return chapters.Select(c => MapToResponse(c, null)).ToList();
    }

    private async Task GenerateAndStoreEmbeddingsAsync(
        Chapter chapter,
        string content,
        string userId,
        CancellationToken cancellationToken)
    {
        var collectionExists = await _vectorStore.CollectionExistsAsync(userId, cancellationToken);
        if (!collectionExists)
        {
            await _vectorStore.InitializeUserCollectionAsync(userId, cancellationToken);
        }

        var chunks = ChunkContent(content);
        if (chunks.Count == 0)
        {
            _logger.LogWarning("No chunks generated for chapter {ChapterId}", chapter.Id);
            return;
        }

        var embeddings = await _embeddingService.EncodeBatchAsync(chunks, EmbeddingMode.Passage, cancellationToken);

        var vectors = new List<VectorData>();
        for (int i = 0; i < chunks.Count; i++)
        {
            vectors.Add(new VectorData
            {
                Id = $"{chapter.Id}_chunk_{i}",
                Vector = embeddings[i],
                UserId = userId,
                ProjectId = chapter.ProjectId,
                SourceType = "chapter",
                SourceId = chapter.Id,
                ChapterId = chapter.Id,
                ChunkIndex = i,
                Content = chunks[i],
                Metadata = new Dictionary<string, object>
                {
                    ["chapter_number"] = chapter.ChapterNumber,
                    ["chapter_title"] = chapter.Title,
                    ["chunk_count"] = chunks.Count
                }
            });
        }

        await _vectorStore.UpsertVectorsAsync(userId, vectors, cancellationToken);

        _logger.LogInformation("Generated and stored {VectorCount} embeddings for chapter {ChapterId}",
            vectors.Count, chapter.Id);
    }

    private async Task DeleteChapterVectorsAsync(
        string chapterId,
        string userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var filters = new Dictionary<string, object>
            {
                ["chapter_id"] = chapterId
            };

            await _vectorStore.DeleteVectorsByFilterAsync(userId, filters, cancellationToken);

            _logger.LogInformation("Deleted vectors for chapter {ChapterId}", chapterId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete vectors for chapter {ChapterId}", chapterId);
        }
    }

    private List<string> ChunkContent(string content)
    {
        var chunks = new List<string>();
        if (string.IsNullOrWhiteSpace(content))
            return chunks;

        var start = 0;
        while (start < content.Length)
        {
            var length = Math.Min(ChunkSize, content.Length - start);
            var end = start + length;

            if (end < content.Length)
            {
                var paragraphBreak = content.LastIndexOf("\n\n", end - 1, length, StringComparison.Ordinal);
                if (paragraphBreak > start + 500)
                {
                    end = paragraphBreak + 2;
                }
            }

            var chunk = content[start..end].Trim();
            if (!string.IsNullOrWhiteSpace(chunk))
            {
                chunks.Add(chunk);
            }

            start = end > start ? end - ChunkOverlap : end;
            if (start >= content.Length) break;
        }

        return chunks;
    }

    private static int CountWords(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0;

        int count = 0;
        bool inWord = false;

        foreach (char c in content)
        {
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
            }
            else if (c >= 0x4E00 && c <= 0x9FFF)
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

    private static ChapterResponse MapToResponse(Chapter chapter, string? content)
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
            ContentPath = chapter.ContentDocumentId ?? "",
            Content = content,
            CreatedAt = chapter.CreatedAt,
            UpdatedAt = chapter.UpdatedAt
        };
    }
}
