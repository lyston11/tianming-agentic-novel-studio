using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.Embedding;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Generated;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.VectorStore;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class WebGeneratedContentService : IGeneratedContentService
{
    private const string SourceType = "chapter";
    private const string DocumentRole = "chapter_body";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICurrentUserService? _currentUserService;
    private readonly string _projectId;
    private readonly IVectorStore? _vectorStore;
    private readonly IMicroEmbeddingService? _embeddingService;

    public WebGeneratedContentService(
        IServiceScopeFactory scopeFactory,
        ICurrentUserService? currentUserService,
        string projectId,
        IVectorStore? vectorStore,
        IMicroEmbeddingService? embeddingService)
    {
        _scopeFactory = scopeFactory;
        _currentUserService = currentUserService;
        _projectId = projectId;
        _vectorStore = vectorStore;
        _embeddingService = embeddingService;
    }

    public async Task SaveChapterAsync(string chapterId, string content)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var contentDocuments = scope.ServiceProvider.GetRequiredService<IContentDocumentService>();
        var (userId, project) = await ResolveUserAndProjectAsync(db, chapterId).ConfigureAwait(false);
        var chapter = await ResolveChapterAsync(db, chapterId, project, content).ConfigureAwait(false);

        var document = await contentDocuments.SaveOrReplaceTextAsync(
            userId,
            chapter.ProjectId,
            SourceType,
            chapter.Id,
            DocumentRole,
            chapter.Title,
            content,
            CancellationToken.None).ConfigureAwait(false);

        chapter.CurrentDocumentId = document.Id;
        chapter.WordCount = CountWords(content);
        chapter.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync().ConfigureAwait(false);

        await UpsertChapterVectorsAsync(userId, chapter, content, db).ConfigureAwait(false);
    }

    public async Task<string?> GetChapterAsync(string chapterId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var contentDocuments = scope.ServiceProvider.GetRequiredService<IContentDocumentService>();
        var (userId, project) = await ResolveUserAndProjectAsync(db, chapterId).ConfigureAwait(false);
        var chapter = await db.Chapters
            .AsNoTracking()
            .Where(c => c.Id == chapterId || c.Title == chapterId)
            .Where(c => c.ProjectId == project.Id)
            .OrderBy(c => c.ChapterNumber)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        if (chapter == null)
            return null;

        try
        {
            return await contentDocuments.GetTextAsync(
                userId,
                project.Id,
                SourceType,
                chapter.Id,
                DocumentRole,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    public async Task<bool> DeleteChapterAsync(string chapterId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var contentDocuments = scope.ServiceProvider.GetRequiredService<IContentDocumentService>();
        var (userId, project) = await ResolveUserAndProjectAsync(db, chapterId).ConfigureAwait(false);
        var chapter = await db.Chapters
            .FirstOrDefaultAsync(c => (c.Id == chapterId || c.Title == chapterId) && c.ProjectId == project.Id)
            .ConfigureAwait(false);

        if (chapter == null)
            return false;

        db.Chapters.Remove(chapter);
        await contentDocuments.DeleteBySourceAsync(userId, project.Id, SourceType, chapter.Id, DocumentRole, CancellationToken.None)
            .ConfigureAwait(false);
        await db.SaveChangesAsync().ConfigureAwait(false);
        return true;
    }

    public bool ChapterExists(string chapterId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var (_, project) = ResolveUserAndProjectAsync(db, chapterId).GetAwaiter().GetResult();
        return db.Chapters.Any(c => (c.Id == chapterId || c.Title == chapterId) && c.ProjectId == project.Id);
    }

    public async Task<List<ChapterInfo>> GetGeneratedChaptersAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var (_, project) = await ResolveUserAndProjectAsync(db, "generated-chapters").ConfigureAwait(false);

        return await db.Chapters
            .AsNoTracking()
            .Where(c => c.ProjectId == project.Id)
            .OrderBy(c => c.ChapterNumber)
            .Select(c => new ChapterInfo
            {
                Id = c.Id,
                Title = c.Title,
                VolumeNumber = c.Volume != null ? c.Volume.VolumeNumber : 0,
                ChapterNumber = c.ChapterNumber,
                WordCount = c.WordCount,
                CreatedTime = c.CreatedAt,
                ModifiedTime = c.UpdatedAt
            })
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public Task<bool> VolumeExistsAsync(int volumeNumber)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var (_, project) = ResolveUserAndProjectAsync(db, volumeNumber.ToString()).GetAwaiter().GetResult();
        return db.Volumes.AnyAsync(v => v.ProjectId == project.Id && v.VolumeNumber == volumeNumber);
    }

    public async Task<string> GenerateNextChapterIdFromSourceAsync(string sourceChapterId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var (_, project) = await ResolveUserAndProjectAsync(db, sourceChapterId).ConfigureAwait(false);
        var current = await db.Chapters
            .AsNoTracking()
            .Where(c => c.ProjectId == project.Id && (c.Id == sourceChapterId || c.Title == sourceChapterId))
            .Select(c => c.ChapterNumber)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        var nextNumber = current > 0
            ? current + 1
            : await db.Chapters
                .AsNoTracking()
                .Where(c => c.ProjectId == project.Id)
                .Select(c => (int?)c.ChapterNumber)
                .MaxAsync()
                .ConfigureAwait(false) + 1 ?? 1;

        return $"chapter-{nextNumber:000}";
    }

    private async Task<(string UserId, NovelProject Project)> ResolveUserAndProjectAsync(
        NovelAgentDbContext db,
        string chapterId)
    {
        var userId = _currentUserService?.TryGetUserId();
        var query = db.NovelProjects.AsQueryable();
        if (!string.IsNullOrWhiteSpace(userId))
            query = query.Where(p => p.UserId == userId);
        if (!string.IsNullOrWhiteSpace(_projectId))
            query = query.Where(p => p.Id == _projectId);

        var project = await query
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        if (project != null)
            return (project.UserId, project);

        throw new InvalidOperationException($"No project is available for chapter '{chapterId}'.");
    }

    private async Task UpsertChapterVectorsAsync(
        string userId,
        Chapter chapter,
        string content,
        NovelAgentDbContext db)
    {
        if (_vectorStore == null || _embeddingService == null || string.IsNullOrWhiteSpace(content))
            return;

        List<(ContentVectorPoint Point, string VectorId)> pointBindings = new();
        try
        {
            if (!await _vectorStore.CollectionExistsAsync(userId).ConfigureAwait(false))
                await _vectorStore.InitializeUserCollectionAsync(userId).ConfigureAwait(false);

            await _vectorStore.DeleteVectorsByFilterAsync(
                    userId,
                    new Dictionary<string, object>
                    {
                        ["project_id"] = chapter.ProjectId,
                        ["source_type"] = SourceType,
                        ["source_id"] = chapter.Id
                    })
                .ConfigureAwait(false);

            var chunkRows = string.IsNullOrWhiteSpace(chapter.CurrentDocumentId)
                ? new List<ContentChunk>()
                : await db.ContentChunks
                    .AsNoTracking()
                    .Where(c => c.DocumentId == chapter.CurrentDocumentId)
                    .OrderBy(c => c.ChunkIndex)
                    .ToListAsync()
                    .ConfigureAwait(false);
            var pointRows = string.IsNullOrWhiteSpace(chapter.CurrentDocumentId)
                ? new Dictionary<string, ContentVectorPoint>()
                : await db.ContentVectorPoints
                    .Where(p => p.DocumentId == chapter.CurrentDocumentId && p.ChunkId != null)
                    .ToDictionaryAsync(p => p.ChunkId!)
                    .ConfigureAwait(false);
            var chunks = chunkRows.Count > 0
                ? chunkRows.Select(c => new ChapterVectorChunk(c.ChunkIndex, c.ChunkText, c.Id)).ToList()
                : ChunkContent(content).Select((c, i) => new ChapterVectorChunk(i, c, null)).ToList();
            if (chunks.Count == 0)
                return;

            var vectors = await _embeddingService.EncodeBatchAsync(chunks.Select(c => c.Content).ToList(), EmbeddingMode.Passage)
                .ConfigureAwait(false);
            var payloads = chunks
                .Select((chunk, index) => new VectorData
                {
                    Id = ResolveChapterVectorId(chunk.ContentChunkId, pointRows, pointBindings),
                    Vector = vectors[index],
                    UserId = userId,
                    ProjectId = chapter.ProjectId,
                    SourceType = SourceType,
                    SourceId = chapter.Id,
                    ChapterId = chapter.Id,
                    ChunkIndex = chunk.ChunkIndex,
                    Content = chunk.Content,
                    Metadata = new Dictionary<string, object>
                    {
                        ["chapter_number"] = chapter.ChapterNumber,
                        ["chapter_title"] = chapter.Title,
                        ["chunk_count"] = chunks.Count
                    }
                })
                .ToList();

            await _vectorStore.UpsertVectorsAsync(userId, payloads).ConfigureAwait(false);
            MarkChapterVectorPointsCompleted(userId, pointBindings);
            await db.SaveChangesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await MarkChapterVectorPointsFailedAsync(db, pointBindings.Select(x => x.Point), ex.Message)
                .ConfigureAwait(false);
            TM.App.Log($"[WebGeneratedContentService] Qdrant chapter vector sync failed for {chapter.Id}: {ex.Message}");
        }
    }

    private string ResolveChapterVectorId(
        string? contentChunkId,
        IReadOnlyDictionary<string, ContentVectorPoint> pointRows,
        List<(ContentVectorPoint Point, string VectorId)> pointBindings)
    {
        if (contentChunkId == null || !pointRows.TryGetValue(contentChunkId, out var point))
            return Guid.NewGuid().ToString();

        var vectorId = Guid.TryParse(point.QdrantPointId, out _)
            ? point.QdrantPointId
            : Guid.NewGuid().ToString();
        pointBindings.Add((point, vectorId));
        return vectorId;
    }

    private void MarkChapterVectorPointsCompleted(
        string userId,
        IReadOnlyList<(ContentVectorPoint Point, string VectorId)> bindings)
    {
        var indexedAt = DateTime.UtcNow;
        foreach (var (point, vectorId) in bindings)
        {
            point.QdrantCollection = QdrantVectorStore.GetCollectionName(userId);
            point.QdrantPointId = vectorId;
            point.VectorModel = _embeddingService!.GetType().Name;
            point.IndexStatus = "completed";
            point.IndexedAt = indexedAt;
            point.ErrorMessage = null;
        }
    }

    private async Task MarkChapterVectorPointsFailedAsync(
        NovelAgentDbContext db,
        IEnumerable<ContentVectorPoint> points,
        string errorMessage)
    {
        foreach (var point in points)
        {
            point.VectorModel = _embeddingService!.GetType().Name;
            point.IndexStatus = "failed";
            point.ErrorMessage = errorMessage;
            point.IndexedAt = null;
        }

        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    private sealed record ChapterVectorChunk(int ChunkIndex, string Content, string? ContentChunkId);

    private static async Task<Chapter> ResolveChapterAsync(
        NovelAgentDbContext db,
        string chapterId,
        NovelProject project,
        string content)
    {
        var chapter = await db.Chapters
            .FirstOrDefaultAsync(c => (c.Id == chapterId || c.Title == chapterId) && c.ProjectId == project.Id)
            .ConfigureAwait(false);

        if (chapter != null)
            return chapter;

        var nextNumber = await db.Chapters
            .AsNoTracking()
            .Where(c => c.ProjectId == project.Id)
            .Select(c => (int?)c.ChapterNumber)
            .MaxAsync()
            .ConfigureAwait(false) + 1 ?? 1;

        chapter = new Chapter
        {
            Id = string.IsNullOrWhiteSpace(chapterId) ? Guid.NewGuid().ToString() : chapterId,
            ProjectId = project.Id,
            Title = string.IsNullOrWhiteSpace(chapterId) ? $"第{nextNumber}章" : chapterId,
            ChapterNumber = nextNumber,
            WordCount = CountWords(content),
            Status = "draft",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Chapters.Add(chapter);
        await db.SaveChangesAsync().ConfigureAwait(false);
        return chapter;
    }

    private static int CountWords(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0;

        var count = 0;
        var inWord = false;
        foreach (var c in content)
        {
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
            }
            else if (c is >= '\u4E00' and <= '\u9FFF')
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

    private static List<string> ChunkContent(string content)
    {
        const int chunkSize = 500;
        var chunks = new List<string>();
        if (string.IsNullOrWhiteSpace(content))
            return chunks;

        for (var i = 0; i < content.Length; i += chunkSize)
        {
            var length = Math.Min(chunkSize, content.Length - i);
            var chunk = content.Substring(i, length).Trim();
            if (!string.IsNullOrWhiteSpace(chunk))
                chunks.Add(chunk);
        }

        return chunks;
    }
}
