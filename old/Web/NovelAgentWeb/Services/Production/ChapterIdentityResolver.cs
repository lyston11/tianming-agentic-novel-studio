using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;

namespace TM.Web.NovelAgentWeb.Services.Production;

public static class ChapterIdentityResolver
{
    public static async Task<string?> ResolveCanonicalChapterIdAsync(
        NovelAgentDbContext db,
        string projectId,
        string? chapterId,
        CancellationToken cancellationToken = default)
    {
        var value = chapterId?.Trim();
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(value))
            return value;

        var chapterNumber = ResolveChapterNumber(value);
        var chapter = await db.Chapters
            .AsNoTracking()
            .Where(c => c.ProjectId == projectId)
            .Where(c => c.Id == value ||
                        c.Title == value ||
                        (chapterNumber.HasValue && c.ChapterNumber == chapterNumber.Value))
            .OrderBy(c => c.ChapterNumber)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return chapter?.Id ?? value;
    }

    public static async Task<ChapterIdArrayNormalization> NormalizeChapterIdsJsonArrayAsync(
        NovelAgentDbContext db,
        string projectId,
        string json,
        CancellationToken cancellationToken = default)
    {
        var originalIds = ParseStringArray(json);
        if (originalIds.Count == 0)
            return new ChapterIdArrayNormalization(originalIds, originalIds);

        var canonicalIds = new List<string>();
        foreach (var originalId in originalIds)
        {
            var canonicalId = await ResolveCanonicalChapterIdAsync(db, projectId, originalId, cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(canonicalId) &&
                !canonicalIds.Contains(canonicalId, StringComparer.OrdinalIgnoreCase))
            {
                canonicalIds.Add(canonicalId.Trim());
            }
        }

        return new ChapterIdArrayNormalization(originalIds, canonicalIds);
    }

    public static async Task<IReadOnlyList<string>> ResolveAffectedAndDownstreamChapterIdsAsync(
        NovelAgentDbContext db,
        string projectId,
        IEnumerable<string?> projectPackageChapterIds,
        IReadOnlyList<string> affectedChapterIds,
        CancellationToken cancellationToken = default)
    {
        var normalized = new List<string>();
        foreach (var chapterId in affectedChapterIds.Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            var canonicalId = await ResolveCanonicalChapterIdAsync(db, projectId, chapterId, cancellationToken)
                .ConfigureAwait(false);
            AddDistinct(normalized, canonicalId);
        }

        if (normalized.Count == 0)
            return normalized;

        var chapters = await db.Chapters
            .AsNoTracking()
            .Where(chapter => chapter.ProjectId == projectId)
            .OrderBy(chapter => chapter.ChapterNumber)
            .Select(chapter => new ChapterIdentity(chapter.Id, chapter.Title, chapter.ChapterNumber))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var firstAffectedOrdinal = normalized
            .Select(id => ResolveChapterNumberFromCanonical(chapters, id))
            .Concat(affectedChapterIds.Select(ResolveChapterNumber))
            .Where(number => number.HasValue)
            .Select(number => number!.Value)
            .DefaultIfEmpty(0)
            .Min();
        if (firstAffectedOrdinal <= 0)
            return normalized;

        var packageChapterIds = projectPackageChapterIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var chapter in chapters.Where(chapter => chapter.ChapterNumber >= firstAffectedOrdinal))
            AddDistinct(normalized, chapter.Id);

        foreach (var packageChapterId in packageChapterIds)
        {
            var ordinal = ResolveChapterNumberFromCanonical(chapters, packageChapterId) ??
                          ResolveChapterNumber(packageChapterId);
            if (ordinal.HasValue && ordinal.Value >= firstAffectedOrdinal)
                AddDistinct(normalized, packageChapterId);
        }

        return normalized;
    }

    public static List<string> ParseStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<string>();

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return new List<string>();

            return document.RootElement
                .EnumerateArray()
                .Where(element => element.ValueKind == JsonValueKind.String)
                .Select(element => element.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static int? ResolveChapterNumberFromCanonical(
        IEnumerable<ChapterIdentity> chapters,
        string? chapterId)
    {
        if (string.IsNullOrWhiteSpace(chapterId))
            return null;

        foreach (var chapter in chapters)
        {
            if (string.Equals(chapter.Id, chapterId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(chapter.Title, chapterId, StringComparison.OrdinalIgnoreCase))
            {
                return chapter.ChapterNumber;
            }
        }

        return null;
    }

    private static int? ResolveChapterNumber(string? chapterId)
    {
        if (string.IsNullOrWhiteSpace(chapterId))
            return null;

        var match = Regex.Match(chapterId, @"(\d+)(?!.*\d)");
        return match.Success && int.TryParse(match.Groups[1].Value, out var number) && number > 0
            ? number
            : null;
    }

    private static void AddDistinct(List<string> values, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            !values.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(value.Trim());
        }
    }

    private sealed record ChapterIdentity(string Id, string Title, int ChapterNumber);
}

public sealed record ChapterIdArrayNormalization(
    IReadOnlyList<string> OriginalIds,
    IReadOnlyList<string> CanonicalIds);
