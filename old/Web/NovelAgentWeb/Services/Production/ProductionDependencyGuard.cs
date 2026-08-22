using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class ProductionDependencyGuard : IProductionDependencyGuard
{
    private static readonly HashSet<string> BlockingOutboxStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending",
        "processing",
        "retryable_failed",
        "failed"
    };

    private static readonly HashSet<string> ChapterPostCommitOutboxTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "finalize_chapter_commit_metadata",
        "extract_chapter_continuity_facts",
        "index_chapter_content"
    };

    private readonly NovelAgentDbContext _db;

    public ProductionDependencyGuard(NovelAgentDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ProductionDependencyBlock>> FindBlocksAsync(
        ProductionDependencyGuardRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) ||
            string.IsNullOrWhiteSpace(request.ProjectId) ||
            request.TargetChapterNumber <= 1)
        {
            return Array.Empty<ProductionDependencyBlock>();
        }

        var userRole = await _db.Users
            .AsNoTracking()
            .Where(user => user.Id == request.UserId)
            .Select(user => user.Role)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var isAdmin = string.Equals(userRole, "admin", StringComparison.OrdinalIgnoreCase);

        var projectExists = await _db.NovelProjects
            .AsNoTracking()
            .AnyAsync(project =>
                    project.Id == request.ProjectId &&
                    (isAdmin || project.UserId == request.UserId),
                cancellationToken)
            .ConfigureAwait(false);
        if (!projectExists)
            return Array.Empty<ProductionDependencyBlock>();

        var targetChapterId = await _db.Chapters
            .AsNoTracking()
            .Where(chapter =>
                chapter.ProjectId == request.ProjectId &&
                chapter.ChapterNumber == request.TargetChapterNumber)
            .Select(chapter => chapter.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? string.Empty;
        var previousChapterNumber = request.TargetChapterNumber - 1;
        var previousChapter = await _db.Chapters
            .AsNoTracking()
            .Where(chapter =>
                chapter.ProjectId == request.ProjectId &&
                chapter.ChapterNumber == previousChapterNumber)
            .OrderBy(chapter => chapter.ChapterNumber)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (previousChapter == null)
            return Array.Empty<ProductionDependencyBlock>();

        var query = _db.OutboxEvents
            .AsNoTracking()
            .Where(evt =>
                evt.ProjectId == request.ProjectId &&
                ChapterPostCommitOutboxTypes.Contains(evt.EventType) &&
                BlockingOutboxStatuses.Contains(evt.Status));
        if (!isAdmin)
            query = query.Where(evt => evt.UserId == request.UserId);

        var candidates = await query
                .OrderBy(evt => evt.CreatedAt)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        var chapterVersionIds = candidates
            .Where(evt => string.Equals(evt.AggregateType, "chapter_version", StringComparison.OrdinalIgnoreCase))
            .Select(evt => evt.AggregateId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var chapterIdByVersionId = chapterVersionIds.Count == 0
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : await _db.ChapterVersions
                .AsNoTracking()
                .Where(version =>
                    version.ProjectId == request.ProjectId &&
                    chapterVersionIds.Contains(version.Id))
                .ToDictionaryAsync(
                    version => version.Id,
                    version => version.ChapterId,
                    StringComparer.OrdinalIgnoreCase,
                    cancellationToken)
                .ConfigureAwait(false);

        var blocking = candidates
            .Where(evt => IsBlockingPreviousChapterOutbox(evt, previousChapter.Id, previousChapterNumber, chapterIdByVersionId))
            .ToList();
        if (blocking.Count == 0)
            return Array.Empty<ProductionDependencyBlock>();

        var summary = string.Join("；", blocking
            .Select(evt => $"{evt.EventType}/{evt.Status}/{evt.Id}")
            .Take(6));
        var recommendedArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["projectId"] = request.ProjectId,
            ["chapterNumber"] = request.TargetChapterNumber.ToString()
        };

        return new[]
        {
            new ProductionDependencyBlock
            {
                Code = "previous_chapter_post_commit_outbox_pending",
                Stage = NovelAgentProductionStages.ContextPackage,
                Reason = "上一章提交后的后台事实沉淀尚未完成，下一章生产包不能安全构建。",
                ProjectId = request.ProjectId,
                TargetChapterId = targetChapterId,
                TargetChapterNumber = request.TargetChapterNumber,
                PreviousChapterId = previousChapter.Id,
                PreviousChapterNumber = previousChapterNumber,
                OutboxEventIds = blocking.Select(evt => evt.Id).ToArray(),
                OutboxEventTypes = blocking
                    .Select(evt => evt.EventType)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Statuses = blocking
                    .Select(evt => evt.Status)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Summary = summary,
                RecommendedArguments = recommendedArguments
            }
        };
    }

    private static int ExtractTrailingNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        var index = value.Length - 1;
        while (index >= 0 && char.IsDigit(value[index])) index--;
        return index == value.Length - 1 || !int.TryParse(value[(index + 1)..], out var number) ? 0 : number;
    }

    private static bool IsSameChapterIdentity(string? candidate, string chapterId, int chapterNumber)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(chapterId))
            return false;

        var normalized = candidate.Trim();
        if (string.Equals(normalized, chapterId, StringComparison.OrdinalIgnoreCase))
            return true;
        if (chapterId.EndsWith("-" + normalized, StringComparison.OrdinalIgnoreCase))
            return true;

        var candidateNumber = ExtractTrailingNumber(normalized);
        return candidateNumber > 0 && chapterNumber > 0 && candidateNumber == chapterNumber;
    }

    private static bool IsBlockingPreviousChapterOutbox(
        Data.Entities.OutboxEvent evt,
        string previousChapterId,
        int previousChapterNumber,
        IReadOnlyDictionary<string, string> chapterIdByVersionId)
    {
        if (string.Equals(evt.AggregateType, "chapter", StringComparison.OrdinalIgnoreCase))
            return IsSameChapterIdentity(evt.AggregateId, previousChapterId, previousChapterNumber);

        if (string.Equals(evt.AggregateType, "chapter_version", StringComparison.OrdinalIgnoreCase) &&
            chapterIdByVersionId.TryGetValue(evt.AggregateId, out var chapterId))
        {
            return IsSameChapterIdentity(chapterId, previousChapterId, previousChapterNumber);
        }

        return false;
    }
}
