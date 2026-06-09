using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Models.Chapters;
using TM.Web.NovelAgentWeb.Services.VectorStore;

namespace TM.Web.NovelAgentWeb.Services.Chapters;

/// <summary>
/// Implementation of chapter CRUD operations with synchronization across
/// SQLite (metadata), file system (Markdown content), and Qdrant (vectors).
/// </summary>
public class ChapterService : IChapterService
{
    private readonly NovelAgentDbContext _context;
    private readonly IVectorStore _vectorStore;
    private readonly IMicroEmbeddingService _embeddingService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ChapterService> _logger;
    private const int ChunkSize = 500; // Characters per chunk for embedding

    public ChapterService(
        NovelAgentDbContext context,
        IVectorStore vectorStore,
        IMicroEmbeddingService embeddingService,
        IConfiguration configuration,
        ILogger<ChapterService> logger)
    {
        _context = context;
        _vectorStore = vectorStore;
        _embeddingService = embeddingService;
        _configuration = configuration;
        _logger = logger;
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
                ContentPath = GetChapterContentPath(project.StorageProjectName, request.ChapterNumber),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Chapters.Add(chapter);
            await _context.SaveChangesAsync(cancellationToken);

            // 2. Write content to file system
            var fullPath = GetFullContentPath(userId, project.StorageProjectName, chapter.ContentPath);
            await WriteContentToFileAsync(fullPath, request.Content, cancellationToken);

            // 3. Generate and store embeddings in Qdrant
            await GenerateAndStoreEmbeddingsAsync(chapter, request.Content, userId, cancellationToken);

            // Commit transaction
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation("Created chapter {ChapterId} in project {ProjectId} with {VectorCount} vectors",
                chapter.Id, request.ProjectId, GetChunkCount(request.Content));

            // Return response with content
            return MapToResponse(chapter, request.Content);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Failed to create chapter in project {ProjectId}", request.ProjectId);

            // Clean up file if it was created
            var fullPath = GetFullContentPath(userId, project.StorageProjectName,
                GetChapterContentPath(project.StorageProjectName, request.ChapterNumber));
            DeleteFileIfExists(fullPath);

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

                // Write updated content to file system
                var fullPath = GetFullContentPath(userId, chapter.Project.StorageProjectName, chapter.ContentPath);
                await WriteContentToFileAsync(fullPath, newContent, cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);

            // Regenerate embeddings if content changed
            if (contentChanged && newContent != null)
            {
                // Delete old vectors
                await DeleteChapterVectorsAsync(chapter.Id, chapter.ProjectId, cancellationToken);

                // Generate and store new embeddings
                await GenerateAndStoreEmbeddingsAsync(chapter, newContent, userId, cancellationToken);
            }

            // Commit transaction
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation("Updated chapter {ChapterId}, content changed: {ContentChanged}",
                chapterId, contentChanged);

            // Read current content for response
            var content = newContent ?? await ReadContentFromFileAsync(
                GetFullContentPath(userId, chapter.Project.StorageProjectName, chapter.ContentPath), cancellationToken);

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

        // Read content from file system
        var fullPath = GetFullContentPath(userId, chapter.Project.StorageProjectName, chapter.ContentPath);
        var content = await ReadContentFromFileAsync(fullPath, cancellationToken);

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

            // 2. Delete content file
            var fullPath = GetFullContentPath(userId, chapter.Project.StorageProjectName, chapter.ContentPath);
            DeleteFileIfExists(fullPath);

            // 3. Delete vectors from Qdrant
            await DeleteChapterVectorsAsync(chapter.Id, chapter.ProjectId, cancellationToken);

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

    private async Task GenerateAndStoreEmbeddingsAsync(
        Chapter chapter,
        string content,
        string userId,
        CancellationToken cancellationToken)
    {
        // Ensure Qdrant collection exists
        var collectionExists = await _vectorStore.CollectionExistsAsync(chapter.ProjectId, cancellationToken);
        if (!collectionExists)
        {
            await _vectorStore.InitializeProjectCollectionAsync(chapter.ProjectId, cancellationToken);
        }

        // Split content into chunks for embedding
        var chunks = ChunkContent(content);
        if (chunks.Count == 0)
        {
            _logger.LogWarning("No chunks generated for chapter {ChapterId}", chapter.Id);
            return;
        }

        // Generate embeddings for all chunks
        var embeddings = await _embeddingService.EncodeBatchAsync(chunks, EmbeddingMode.Passage, cancellationToken);

        // Create vector data for Qdrant
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

        // Store vectors in Qdrant
        await _vectorStore.UpsertVectorsAsync(chapter.ProjectId, vectors, cancellationToken);

        _logger.LogInformation("Generated and stored {VectorCount} embeddings for chapter {ChapterId}",
            vectors.Count, chapter.Id);
    }

    private async Task DeleteChapterVectorsAsync(
        string chapterId,
        string projectId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Use the new DeleteVectorsByFilterAsync method
            var filters = new Dictionary<string, object>
            {
                ["chapter_id"] = chapterId
            };

            await _vectorStore.DeleteVectorsByFilterAsync(projectId, filters, cancellationToken);

            _logger.LogInformation("Deleted vectors for chapter {ChapterId} from project {ProjectId}",
                chapterId, projectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete vectors for chapter {ChapterId}", chapterId);
            // Don't throw - we want to continue with other cleanup operations
        }
    }

    private List<string> ChunkContent(string content)
    {
        var chunks = new List<string>();
        if (string.IsNullOrWhiteSpace(content))
        {
            return chunks;
        }

        // Split content into chunks of approximately ChunkSize characters
        for (int i = 0; i < content.Length; i += ChunkSize)
        {
            var chunkLength = Math.Min(ChunkSize, content.Length - i);
            var chunk = content.Substring(i, chunkLength).Trim();

            if (!string.IsNullOrWhiteSpace(chunk))
            {
                chunks.Add(chunk);
            }
        }

        return chunks;
    }

    private int GetChunkCount(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return 0;
        }
        return (int)Math.Ceiling((double)content.Length / ChunkSize);
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

    private string GetChapterContentPath(string storageProjectName, int chapterNumber)
    {
        // Return relative path: Chapters/chapter_{number}.md
        return $"Chapters/chapter_{chapterNumber}.md";
    }

    private string GetFullContentPath(string userId, string storageProjectName, string contentPath)
    {
        // Build full path: App_Data/Users/{userId}/Projects/{projectName}/{contentPath}
        var storageRoot = _configuration["NovelAgent:StorageRoot"]
            ?? Path.Combine(AppContext.BaseDirectory, "App_Data");

        return Path.Combine(storageRoot, "Users", userId, "Projects", storageProjectName, contentPath);
    }

    private async Task WriteContentToFileAsync(string fullPath, string content, CancellationToken cancellationToken)
    {
        // Ensure directory exists
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write content to file
        await File.WriteAllTextAsync(fullPath, content, cancellationToken);
    }

    private async Task<string> ReadContentFromFileAsync(string fullPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Chapter content file not found: {fullPath}");
        }

        return await File.ReadAllTextAsync(fullPath, cancellationToken);
    }

    private void DeleteFileIfExists(string fullPath)
    {
        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                _logger.LogInformation("Deleted content file: {FilePath}", fullPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete content file: {FilePath}", fullPath);
            // Don't throw - this is cleanup code
        }
    }

    private ChapterResponse MapToResponse(Chapter chapter, string? content)
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
            ContentPath = chapter.ContentPath,
            Content = content,
            CreatedAt = chapter.CreatedAt,
            UpdatedAt = chapter.UpdatedAt
        };
    }
}

