using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Production;

public interface IProductionTruthChapterIdentityMigrationService
{
    Task<ProductionTruthChapterIdentityMigrationResult> NormalizeAsync(
        string? projectId = null,
        CancellationToken cancellationToken = default);
}

public sealed class ProductionTruthChapterIdentityMigrationService : IProductionTruthChapterIdentityMigrationService
{
    private readonly NovelAgentDbContext _db;

    public ProductionTruthChapterIdentityMigrationService(NovelAgentDbContext db)
    {
        _db = db;
    }

    public async Task<ProductionTruthChapterIdentityMigrationResult> NormalizeAsync(
        string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var projectIds = await BuildProjectQuery(projectId)
            .Select(project => project.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var updatedRows = 0;
        var updatedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var currentProjectId in projectIds)
        {
            var index = await ChapterIdentityIndex.CreateAsync(_db, currentProjectId, cancellationToken)
                .ConfigureAwait(false);
            if (index.IsEmpty)
                continue;

            var projectUpdatedRows = 0;
            projectUpdatedRows += await NormalizePackagesAsync(currentProjectId, index, cancellationToken).ConfigureAwait(false);
            projectUpdatedRows += await NormalizeEventsAsync(currentProjectId, index, cancellationToken).ConfigureAwait(false);
            projectUpdatedRows += await NormalizeDraftsAsync(currentProjectId, index, cancellationToken).ConfigureAwait(false);
            projectUpdatedRows += await NormalizeChangesAsync(currentProjectId, index, cancellationToken).ConfigureAwait(false);
            projectUpdatedRows += await NormalizeGateReportsAsync(currentProjectId, index, cancellationToken).ConfigureAwait(false);
            projectUpdatedRows += await NormalizeAgentReviewsAsync(currentProjectId, index, cancellationToken).ConfigureAwait(false);
            projectUpdatedRows += await NormalizeFactSnapshotsAsync(currentProjectId, index, cancellationToken).ConfigureAwait(false);
            projectUpdatedRows += await NormalizeRevisionPlansAsync(currentProjectId, index, cancellationToken).ConfigureAwait(false);

            if (projectUpdatedRows > 0)
                updatedProjects.Add(currentProjectId);

            updatedRows += projectUpdatedRows;
        }

        if (updatedRows > 0)
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new ProductionTruthChapterIdentityMigrationResult(
            updatedRows,
            updatedProjects.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private IQueryable<NovelProject> BuildProjectQuery(string? projectId)
    {
        var query = _db.NovelProjects.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(projectId))
        {
            var trimmed = projectId.Trim();
            query = query.Where(project => project.Id == trimmed);
        }

        return query;
    }

    private async Task<int> NormalizePackagesAsync(
        string projectId,
        ChapterIdentityIndex index,
        CancellationToken cancellationToken)
    {
        var rows = await _db.TianmingPackages
            .Where(item => item.ProjectId == projectId && item.ChapterId != null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return NormalizeChapterId(rows, item => item.ChapterId, (item, value) => item.ChapterId = value, index);
    }

    private async Task<int> NormalizeEventsAsync(
        string projectId,
        ChapterIdentityIndex index,
        CancellationToken cancellationToken)
    {
        var rows = await _db.ProductionEvents
            .Where(item => item.ProjectId == projectId && item.ChapterId != null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return NormalizeChapterId(rows, item => item.ChapterId, (item, value) => item.ChapterId = value, index);
    }

    private async Task<int> NormalizeDraftsAsync(
        string projectId,
        ChapterIdentityIndex index,
        CancellationToken cancellationToken)
    {
        var rows = await _db.ChapterDrafts
            .Where(item => item.ProjectId == projectId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return NormalizeChapterId(rows, item => item.ChapterId, (item, value) => item.ChapterId = value ?? item.ChapterId, index);
    }

    private async Task<int> NormalizeChangesAsync(
        string projectId,
        ChapterIdentityIndex index,
        CancellationToken cancellationToken)
    {
        var rows = await _db.ChapterChanges
            .Where(item => item.ProjectId == projectId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return NormalizeChapterId(rows, item => item.ChapterId, (item, value) => item.ChapterId = value ?? item.ChapterId, index);
    }

    private async Task<int> NormalizeGateReportsAsync(
        string projectId,
        ChapterIdentityIndex index,
        CancellationToken cancellationToken)
    {
        var rows = await _db.GenerationGateReports
            .Where(item => item.ProjectId == projectId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return NormalizeChapterId(rows, item => item.ChapterId, (item, value) => item.ChapterId = value ?? item.ChapterId, index);
    }

    private async Task<int> NormalizeAgentReviewsAsync(
        string projectId,
        ChapterIdentityIndex index,
        CancellationToken cancellationToken)
    {
        var rows = await _db.AgentReviews
            .Where(item => item.ProjectId == projectId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return NormalizeChapterId(rows, item => item.ChapterId, (item, value) => item.ChapterId = value ?? item.ChapterId, index);
    }

    private async Task<int> NormalizeFactSnapshotsAsync(
        string projectId,
        ChapterIdentityIndex index,
        CancellationToken cancellationToken)
    {
        var rows = await _db.ProjectFactSnapshots
            .Where(item => item.ProjectId == projectId && item.ChapterId != null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return NormalizeChapterId(rows, item => item.ChapterId, (item, value) => item.ChapterId = value, index);
    }

    private async Task<int> NormalizeRevisionPlansAsync(
        string projectId,
        ChapterIdentityIndex index,
        CancellationToken cancellationToken)
    {
        var plans = await _db.RevisionPlans
            .Where(plan => plan.ProjectId == projectId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var updatedRows = 0;
        foreach (var plan in plans)
        {
            var changed = false;
            var originalTargetChapterId = plan.TargetChapterId;
            var target = index.Resolve(originalTargetChapterId);
            if (target != null && !IsSame(plan.TargetChapterId, target.ChapterId))
            {
                plan.TargetChapterId = target.ChapterId;
                changed = true;
            }

            if (target != null && string.IsNullOrWhiteSpace(plan.TargetChapterLogicalId) &&
                !string.IsNullOrWhiteSpace(originalTargetChapterId) &&
                !IsSame(originalTargetChapterId, target.ChapterId))
            {
                plan.TargetChapterLogicalId = originalTargetChapterId.Trim();
                changed = true;
            }

            if (target != null && ShouldReplaceDisplayName(plan.TargetChapterDisplayName, originalTargetChapterId))
            {
                plan.TargetChapterDisplayName = target.DisplayName;
                changed = true;
            }

            var affected = NormalizeChapterIdsJson(plan.AffectedChapterIdsJson, index);
            if (affected.Changed)
            {
                plan.AffectedChapterIdsJson = affected.Json;
                changed = true;
            }

            if (changed)
            {
                plan.UpdatedAt = DateTime.UtcNow;
                updatedRows++;
            }
        }

        return updatedRows;
    }

    private static int NormalizeChapterId<T>(
        IEnumerable<T> rows,
        Func<T, string?> getChapterId,
        Action<T, string?> setChapterId,
        ChapterIdentityIndex index)
    {
        var updatedRows = 0;
        foreach (var row in rows)
        {
            var canonical = index.Resolve(getChapterId(row));
            if (canonical == null || IsSame(getChapterId(row), canonical.ChapterId))
                continue;

            setChapterId(row, canonical.ChapterId);
            updatedRows++;
        }

        return updatedRows;
    }

    private static NormalizedJsonArray NormalizeChapterIdsJson(string? json, ChapterIdentityIndex index)
    {
        var originalIds = ChapterIdentityResolver.ParseStringArray(json);
        if (originalIds.Count == 0)
            return new NormalizedJsonArray(json ?? "[]", false);

        var changed = false;
        var canonicalIds = new List<string>();
        foreach (var originalId in originalIds)
        {
            var resolved = index.Resolve(originalId);
            var canonicalId = resolved?.ChapterId ?? originalId.Trim();
            if (!IsSame(originalId, canonicalId))
                changed = true;
            if (!canonicalIds.Contains(canonicalId, StringComparer.OrdinalIgnoreCase))
                canonicalIds.Add(canonicalId);
        }

        return changed
            ? new NormalizedJsonArray(JsonSerializer.Serialize(canonicalIds), true)
            : new NormalizedJsonArray(json ?? "[]", false);
    }

    private static bool ShouldReplaceDisplayName(string? displayName, string? originalTargetChapterId)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return true;

        return IsSame(displayName, originalTargetChapterId) ||
               IsTechnicalChapterId(displayName);
    }

    private static bool IsTechnicalChapterId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        Regex.IsMatch(value.Trim(), @"^chapter[-_\s]?\d+$", RegexOptions.IgnoreCase);

    private static bool IsSame(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private sealed record NormalizedJsonArray(string Json, bool Changed);

    private sealed class ChapterIdentityIndex
    {
        private readonly IReadOnlyList<ChapterIdentity> _chapters;

        private ChapterIdentityIndex(IReadOnlyList<ChapterIdentity> chapters)
        {
            _chapters = chapters;
        }

        public bool IsEmpty => _chapters.Count == 0;

        public static async Task<ChapterIdentityIndex> CreateAsync(
            NovelAgentDbContext db,
            string projectId,
            CancellationToken cancellationToken)
        {
            var chapters = await db.Chapters
                .AsNoTracking()
                .Where(chapter => chapter.ProjectId == projectId)
                .OrderBy(chapter => chapter.ChapterNumber)
                .Select(chapter => new ChapterIdentity(
                    chapter.Id,
                    chapter.Title,
                    chapter.ChapterNumber))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return new ChapterIdentityIndex(chapters);
        }

        public ChapterIdentity? Resolve(string? chapterId)
        {
            var value = chapterId?.Trim();
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var chapterNumber = ResolveChapterNumber(value);
            return _chapters.FirstOrDefault(chapter =>
                IsSame(chapter.ChapterId, value) ||
                IsSame(chapter.Title, value) ||
                (chapterNumber.HasValue && chapter.ChapterNumber == chapterNumber.Value));
        }

        private static int? ResolveChapterNumber(string value)
        {
            var match = Regex.Match(value, @"(\d+)(?!.*\d)");
            return match.Success && int.TryParse(match.Groups[1].Value, out var number) && number > 0
                ? number
                : null;
        }
    }

    private sealed record ChapterIdentity(string ChapterId, string Title, int ChapterNumber)
    {
        public string DisplayName =>
            string.IsNullOrWhiteSpace(Title) || IsTechnicalChapterId(Title)
                ? $"第{ChapterNumber}章"
                : Title.Trim();
    }
}

public sealed record ProductionTruthChapterIdentityMigrationResult(
    int UpdatedRows,
    IReadOnlyList<string> UpdatedProjectIds);
