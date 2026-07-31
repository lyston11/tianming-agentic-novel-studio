using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Generated;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Production;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class WebGeneratedContentService : IGeneratedContentService, IAtomicGeneratedChapterCommitService
{
    private const string SourceType = "chapter";
    private const string DocumentRole = "chapter_body";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICurrentUserService? _currentUserService;
    private readonly string _projectId;

    public WebGeneratedContentService(
        IServiceScopeFactory scopeFactory,
        ICurrentUserService? currentUserService,
        string projectId)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _currentUserService = currentUserService;
        _projectId = projectId;
    }

    public Task SaveChapterAsync(string chapterId, string content) =>
        SaveChapterCoreAsync(chapterId, content, title: null, Array.Empty<GeneratedChapterOutboxWrite>());

    public Task SaveChapterAsync(string chapterId, string content, string? title) =>
        SaveChapterCoreAsync(chapterId, content, title, Array.Empty<GeneratedChapterOutboxWrite>());

    public Task SaveChapterAtomicallyAsync(
        string chapterId,
        string content,
        string? title,
        IReadOnlyList<GeneratedChapterOutboxWrite> outboxWrites) =>
        SaveChapterCoreAsync(chapterId, content, title, outboxWrites);

    private async Task SaveChapterCoreAsync(
        string chapterId,
        string content,
        string? title,
        IReadOnlyList<GeneratedChapterOutboxWrite> outboxWrites)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        IDbContextTransaction? transaction = null;
        if (db.Database.IsRelational())
            transaction = await db.Database.BeginTransactionAsync().ConfigureAwait(false);

        try
        {
            var contentDocuments = scope.ServiceProvider.GetRequiredService<IContentDocumentService>();
            var (userId, project) = await ResolveUserAndProjectAsync(db, chapterId).ConfigureAwait(false);
            var chapter = await ResolveChapterAsync(db, chapterId, project, content, title).ConfigureAwait(false);

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
            chapter.Status = "committed";
            chapter.UpdatedAt = DateTime.UtcNow;
            var otherChapterWordCount = await db.Chapters
                .AsNoTracking()
                .Where(c => c.ProjectId == project.Id && c.Id != chapter.Id)
                .SumAsync(c => c.WordCount)
                .ConfigureAwait(false);
            project.WordCount = otherChapterWordCount + chapter.WordCount;
            project.Status = ResolveProjectStatusAfterChapterCommit(project.Status);
            project.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync().ConfigureAwait(false);
            await SynchronizeVolumeArcCurrentChaptersAsync(db, project, chapter.VolumeId).ConfigureAwait(false);

            await PersistProductionTruthAsync(scope.ServiceProvider, userId, project, chapter, document, outboxWrites).ConfigureAwait(false);
            if (transaction != null)
                await transaction.CommitAsync().ConfigureAwait(false);
        }
        catch
        {
            if (transaction != null)
                await transaction.RollbackAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (transaction != null)
                await transaction.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task<string?> GetChapterAsync(string chapterId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        var contentDocuments = scope.ServiceProvider.GetRequiredService<IContentDocumentService>();
        var (userId, project) = await ResolveUserAndProjectAsync(db, chapterId).ConfigureAwait(false);
        var chapter = await FindChapterByLogicalIdAsync(db, project.Id, chapterId, tracking: false)
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
        var truthStore = scope.ServiceProvider.GetRequiredService<IProductionTruthStore>();
        var (userId, project) = await ResolveUserAndProjectAsync(db, chapterId).ConfigureAwait(false);
        var chapter = await FindChapterByLogicalIdAsync(db, project.Id, chapterId, tracking: true)
            .ConfigureAwait(false);

        if (chapter == null)
            return false;

        await truthStore.EnqueueOutboxAsync(
                new EnqueueOutboxEventRequest(
                    UserId: userId,
                    ProjectId: project.Id,
                    RuntimeRunId: null,
                    EventType: "delete_chapter_content",
                    AggregateType: "chapter",
                    AggregateId: chapter.Id,
                    PayloadJson: "{}"))
            .ConfigureAwait(false);

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
        return FindChapterByLogicalIdAsync(db, project.Id, chapterId, tracking: false)
            .GetAwaiter()
            .GetResult() != null;
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
        var requestedNumber = ResolveRequestedChapterNumber(sourceChapterId);
        var current = await db.Chapters
            .AsNoTracking()
            .Where(c => c.ProjectId == project.Id &&
                        (c.Id == sourceChapterId ||
                         c.Title == sourceChapterId ||
                         (requestedNumber.HasValue && c.ChapterNumber == requestedNumber.Value)))
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

    private static async Task PersistProductionTruthAsync(
        IServiceProvider services,
        string userId,
        NovelProject project,
        Chapter chapter,
        ContentDocument document,
        IReadOnlyList<GeneratedChapterOutboxWrite> outboxWrites)
    {
        var truthStore = services.GetRequiredService<IProductionTruthStore>();

        var version = await truthStore.CreateChapterVersionAsync(
                new CreateChapterVersionRequest(
                    userId,
                    project.Id,
                    chapter.Id,
                    document.Id,
                    chapter.Title,
                    chapter.WordCount,
                    chapter.Status,
                    RuntimeRunId: null,
                    PackageId: null,
                    GateReportJson: null,
                    AgentReviewJson: null))
            .ConfigureAwait(false);

        var payloadJson = JsonSerializer.Serialize(new
        {
            userId,
            projectId = project.Id,
            chapterId = chapter.Id,
            chapterVersionId = version.Id,
            contentDocumentId = document.Id,
            title = chapter.Title,
            wordCount = chapter.WordCount
        });

        await truthStore.EnqueueOutboxAsync(
                new EnqueueOutboxEventRequest(
                    userId,
                    project.Id,
                    RuntimeRunId: null,
                    EventType: "index_chapter_content",
                    AggregateType: "chapter_version",
                    AggregateId: version.Id,
                    PayloadJson: payloadJson))
            .ConfigureAwait(false);

        foreach (var outbox in outboxWrites)
        {
            await truthStore.EnqueueOutboxAsync(
                    new EnqueueOutboxEventRequest(
                        userId,
                        project.Id,
                        outbox.RuntimeRunId,
                        outbox.EventType,
                        outbox.AggregateType,
                        outbox.AggregateId,
                        NormalizeOutboxPayload(outbox, userId, project.Id),
                        outbox.IdempotencyKey),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private static string NormalizeOutboxPayload(
        GeneratedChapterOutboxWrite outbox,
        string userId,
        string projectId)
    {
        if (!string.Equals(outbox.EventType, "finalize_chapter_commit_metadata", StringComparison.Ordinal))
            return outbox.PayloadJson;

        var payload = JsonNode.Parse(string.IsNullOrWhiteSpace(outbox.PayloadJson) ? "{}" : outbox.PayloadJson)
            as JsonObject ?? new JsonObject();
        payload["userId"] = userId;
        payload["projectId"] = projectId;
        payload["runtimeRunId"] = outbox.RuntimeRunId;
        return payload.ToJsonString();
    }

    private static async Task<Chapter> ResolveChapterAsync(
        NovelAgentDbContext db,
        string chapterId,
        NovelProject project,
        string content,
        string? explicitTitle = null)
    {
        var chapter = await FindChapterByLogicalIdAsync(db, project.Id, chapterId, tracking: true)
            .ConfigureAwait(false);

        if (chapter != null)
        {
            var readableTitle = ResolveReadableChapterTitle(chapter.Title, content, chapter.ChapterNumber, explicitTitle);
            if (!string.Equals(readableTitle, chapter.Title, StringComparison.Ordinal))
            {
                chapter.Title = readableTitle;
                chapter.UpdatedAt = DateTime.UtcNow;
            }
            await EnsureCanonicalVolumeBindingAsync(db, project, chapter).ConfigureAwait(false);
            await db.SaveChangesAsync().ConfigureAwait(false);
            await SynchronizeVolumeArcCurrentChaptersAsync(db, project, chapter.VolumeId).ConfigureAwait(false);
            return chapter;
        }

        var existingMaxNumber = await db.Chapters
            .AsNoTracking()
            .Where(c => c.ProjectId == project.Id)
            .Select(c => (int?)c.ChapterNumber)
            .MaxAsync()
            .ConfigureAwait(false);
        var nextNumber = ResolveRequestedChapterNumber(chapterId) ?? (existingMaxNumber + 1 ?? 1);
        var resolvedChapterId = await ResolveNewChapterIdAsync(db, project, chapterId, nextNumber).ConfigureAwait(false);

        chapter = new Chapter
        {
            Id = resolvedChapterId,
            ProjectId = project.Id,
            Title = ResolveReadableChapterTitle(chapterId, content, nextNumber, explicitTitle),
            ChapterNumber = nextNumber,
            WordCount = CountWords(content),
            Status = "draft",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await EnsureCanonicalVolumeBindingAsync(db, project, chapter).ConfigureAwait(false);
        db.Chapters.Add(chapter);
        await db.SaveChangesAsync().ConfigureAwait(false);
        await SynchronizeVolumeArcCurrentChaptersAsync(db, project, chapter.VolumeId).ConfigureAwait(false);
        return chapter;
    }

    private static async Task<string> ResolveNewChapterIdAsync(
        NovelAgentDbContext db,
        NovelProject project,
        string? requestedChapterId,
        int chapterNumber)
    {
        if (string.IsNullOrWhiteSpace(requestedChapterId))
            return Guid.NewGuid().ToString();

        var normalized = requestedChapterId.Trim();
        var projectScopedId = BuildProjectScopedChapterId(project.Id, normalized, chapterNumber);
        if (string.Equals(normalized, projectScopedId, StringComparison.OrdinalIgnoreCase))
            return normalized;

        var projectScopedExists = await db.Chapters
            .AsNoTracking()
            .AnyAsync(c => c.Id == projectScopedId)
            .ConfigureAwait(false);
        if (!projectScopedExists)
            return projectScopedId;

        return $"{project.Id}-chapter-{chapterNumber:000}-{Guid.NewGuid():N}";
    }

    private static async Task<Chapter?> FindChapterByLogicalIdAsync(
        NovelAgentDbContext db,
        string projectId,
        string? chapterId,
        bool tracking)
    {
        var requestedNumber = ResolveRequestedChapterNumber(chapterId);
        var query = tracking ? db.Chapters.AsQueryable() : db.Chapters.AsNoTracking();
        return await query
            .Where(c => c.ProjectId == projectId)
            .Where(c => c.Id == chapterId ||
                        c.Title == chapterId ||
                        (requestedNumber.HasValue && c.ChapterNumber == requestedNumber.Value))
            .OrderBy(c => c.ChapterNumber)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
    }

    private static string BuildProjectScopedChapterId(
        string projectId,
        string requestedChapterId,
        int chapterNumber)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return string.IsNullOrWhiteSpace(requestedChapterId)
                ? $"chapter-{chapterNumber:000}"
                : requestedChapterId.Trim();

        var normalized = requestedChapterId.Trim();
        if (normalized.StartsWith(projectId + "-", StringComparison.OrdinalIgnoreCase))
            return normalized;

        var logicalId = IsMachineChapterTitle(normalized) || ResolveRequestedChapterNumber(normalized).HasValue
            ? $"chapter-{chapterNumber:000}"
            : normalized;
        return $"{projectId}-{logicalId}";
    }

    private static int? ResolveRequestedChapterNumber(string? chapterId)
    {
        if (string.IsNullOrWhiteSpace(chapterId))
            return null;

        var digits = new string(chapterId.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var number) && number > 0 ? number : null;
    }

    private static async Task EnsureCanonicalVolumeBindingAsync(
        NovelAgentDbContext db,
        NovelProject project,
        Chapter chapter)
    {
        if (chapter.ChapterNumber <= 0)
            chapter.ChapterNumber = ResolveRequestedChapterNumber(chapter.Id) ?? 1;

        var arc = await ResolveVolumeArcForChapterAsync(db, project, chapter.ChapterNumber).ConfigureAwait(false);
        var volumeNumber = arc?.VolumeNumber ?? 1;
        var arcTitle = arc?.VolumeTitle?.Trim();
        var volumeTitle = string.IsNullOrWhiteSpace(arcTitle)
            ? $"第{volumeNumber}卷"
            : arcTitle;

        var volume = await db.Volumes
            .FirstOrDefaultAsync(v => v.ProjectId == project.Id && v.VolumeNumber == volumeNumber)
            .ConfigureAwait(false);
        if (volume == null)
        {
            volume = new Volume
            {
                Id = $"volume-{project.Id}-{volumeNumber:000}",
                ProjectId = project.Id,
                VolumeNumber = volumeNumber,
                Title = volumeTitle,
                Summary = arc?.VolumeTheme
            };
            db.Volumes.Add(volume);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(arcTitle) || string.IsNullOrWhiteSpace(volume.Title))
                volume.Title = volumeTitle;
            if (!string.IsNullOrWhiteSpace(arc?.VolumeTheme))
                volume.Summary = arc.VolumeTheme;
        }

        chapter.VolumeId = volume.Id;
    }

    private static async Task<VolumeArc?> ResolveVolumeArcForChapterAsync(
        NovelAgentDbContext db,
        NovelProject project,
        int chapterNumber)
    {
        var arcs = await db.VolumeArcs
            .Where(a => a.ProjectId == project.Id)
            .OrderBy(a => a.VolumeNumber)
            .ToListAsync()
            .ConfigureAwait(false);
        if (arcs.Count == 0)
            return null;

        var lowerBound = 1;
        foreach (var arc in arcs)
        {
            var target = Math.Max(arc.TargetChapters ?? 0, 0);
            var upperBound = target > 0 ? lowerBound + target - 1 : int.MaxValue;
            if (chapterNumber >= lowerBound && chapterNumber <= upperBound)
                return arc;
            lowerBound = upperBound == int.MaxValue ? lowerBound : upperBound + 1;
        }

        return arcs.Last();
    }

    private static async Task SynchronizeVolumeArcCurrentChaptersAsync(
        NovelAgentDbContext db,
        NovelProject project,
        string? volumeId)
    {
        if (string.IsNullOrWhiteSpace(volumeId))
            return;

        var volume = await db.Volumes
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == volumeId && v.ProjectId == project.Id)
            .ConfigureAwait(false);
        if (volume == null)
            return;

        var arc = await db.VolumeArcs
            .FirstOrDefaultAsync(a => a.ProjectId == project.Id && a.VolumeNumber == volume.VolumeNumber)
            .ConfigureAwait(false);
        if (arc == null)
            return;

        arc.CurrentChapters = await db.Chapters
            .AsNoTracking()
            .CountAsync(c => c.ProjectId == project.Id && c.VolumeId == volumeId &&
                             (c.Status == "committed" || c.Status == "published" || c.Status == "completed"))
            .ConfigureAwait(false);
        arc.Status = arc.TargetChapters.HasValue && arc.CurrentChapters >= arc.TargetChapters.Value
            ? "completed"
            : arc.CurrentChapters > 0 ? "in_progress" : arc.Status;
        arc.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    private static string ResolveReadableChapterTitle(
        string? requestedTitle,
        string content,
        int chapterNumber,
        string? explicitTitle = null)
    {
        var heading = ExtractChapterHeading(content);
        if (!string.IsNullOrWhiteSpace(heading) && LooksLikeNumberedChapterTitle(heading))
            return CleanReadableChapterTitle(heading);

        var cleanExplicitTitle = CleanReadableChapterTitle(explicitTitle ?? string.Empty);
        if (!IsMachineChapterTitle(cleanExplicitTitle))
            return EnsureChapterTitleHasNumber(cleanExplicitTitle, chapterNumber);

        if (!string.IsNullOrWhiteSpace(heading))
            return CleanReadableChapterTitle(heading);

        if (!IsMachineChapterTitle(requestedTitle))
            return CleanReadableChapterTitle(requestedTitle!.Trim());

        return chapterNumber > 0 ? $"第 {chapterNumber} 章" : "未命名章节";
    }

    private static string EnsureChapterTitleHasNumber(string title, int chapterNumber)
    {
        if (string.IsNullOrWhiteSpace(title))
            return title;

        if (LooksLikeNumberedChapterTitle(title))
            return title;

        return chapterNumber > 0 ? $"第 {chapterNumber} 章：{title}" : title;
    }

    private static bool LooksLikeNumberedChapterTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;

        var chapterStart = title.IndexOf('第');
        var chapterEnd = title.IndexOf('章', chapterStart >= 0 ? chapterStart : 0);
        return chapterStart >= 0 && chapterEnd > chapterStart;
    }

    private static bool IsMachineChapterTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return true;

        var normalized = title.Trim();
        if (!normalized.StartsWith("chapter-", StringComparison.OrdinalIgnoreCase))
            return false;

        return normalized.Skip("chapter-".Length).All(c => char.IsDigit(c) || c == '_' || c == '-');
    }

    private static string ExtractChapterHeading(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        var lineEnd = content.IndexOfAny(new[] { '\r', '\n' });
        var firstLine = (lineEnd >= 0 ? content[..lineEnd] : content).Trim();
        if (firstLine.StartsWith("#", StringComparison.Ordinal))
            firstLine = firstLine.TrimStart('#').Trim();
        firstLine = CleanReadableChapterTitle(firstLine);
        if (firstLine.Length == 0)
            return string.Empty;

        var chapterStart = firstLine.IndexOf('第');
        var chapterEnd = firstLine.IndexOf('章', chapterStart >= 0 ? chapterStart : 0);
        if (chapterStart < 0 || chapterEnd <= chapterStart)
            return firstLine.Length <= 24 ? CleanReadableChapterTitle(firstLine) : string.Empty;

        var prefix = firstLine[chapterStart..(chapterEnd + 1)].Trim();
        var rest = firstLine[(chapterEnd + 1)..].TrimStart(' ', '\t', ':', '：', '-', '—');
        if (string.IsNullOrWhiteSpace(rest))
            return prefix;

        var subtitle = NormalizeChapterSubtitle(rest);
        return string.IsNullOrWhiteSpace(subtitle) ? prefix : $"{prefix}：{subtitle}";
    }

    private static string CleanReadableChapterTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        var cleaned = title.Trim();
        cleaned = cleaned.Trim(' ', '\t', '#', '*', '_', '`', '~', '　');
        while (cleaned.Length >= 2 &&
               ((cleaned[0] == '*' && cleaned[^1] == '*') ||
                (cleaned[0] == '_' && cleaned[^1] == '_') ||
                (cleaned[0] == '`' && cleaned[^1] == '`')))
        {
            cleaned = cleaned[1..^1].Trim();
        }

        return cleaned.Trim(' ', '\t', '#', '*', '_', '`', '~', '　');
    }

    private static string NormalizeChapterSubtitle(string text)
    {
        const int maxSubtitleLength = 48;
        var chars = new List<char>(capacity: Math.Min(maxSubtitleLength, Math.Max(text.Length, 1)));
        var previousWasWhiteSpace = false;
        foreach (var c in text)
        {
            if (c is '\r' or '\n')
                break;
            if (char.IsWhiteSpace(c))
            {
                if (chars.Count > 0 && !previousWasWhiteSpace)
                {
                    chars.Add(' ');
                    previousWasWhiteSpace = true;
                }
                continue;
            }

            chars.Add(c);
            previousWasWhiteSpace = false;
            if (chars.Count >= maxSubtitleLength)
                break;
        }

        return CleanReadableChapterTitle(new string(chars.ToArray()).Trim(' ', '\t', '#', '-', '—'));
    }

    private static string ResolveProjectStatusAfterChapterCommit(string? currentStatus)
    {
        if (string.Equals(currentStatus, "published", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(currentStatus, "completed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(currentStatus, "archived", StringComparison.OrdinalIgnoreCase))
        {
            return currentStatus!;
        }

        return "Writing";
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

}
