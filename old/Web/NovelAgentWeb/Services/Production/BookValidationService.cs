using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Content;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class BookValidationService : IBookValidationService
{
    private const int MinimumCommittedWordCount = 1200;

    private readonly NovelAgentDbContext _db;
    private readonly IContentDocumentService _contentDocuments;

    public BookValidationService(
        NovelAgentDbContext db,
        IContentDocumentService contentDocuments)
    {
        _db = db;
        _contentDocuments = contentDocuments;
    }

    public async Task<BookValidationReport> ValidateAsync(
        BookValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        var report = new BookValidationReport
        {
            ProjectId = request.ProjectId ?? string.Empty,
            StartChapterNumber = Math.Max(1, request.StartChapterNumber),
            EndChapterNumber = Math.Max(0, request.EndChapterNumber)
        };

        if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.ProjectId))
        {
            AddIssue(report, "project_scope", "error", 0, string.Empty, "缺少用户或项目上下文，不能执行整书校验。", "先绑定真实项目。");
            Complete(report);
            return report;
        }

        var isAdmin = await IsAdminAsync(request.UserId, cancellationToken).ConfigureAwait(false);
        var projectQuery = _db.NovelProjects
            .AsNoTracking()
            .Where(project => project.Id == request.ProjectId);
        if (!isAdmin)
            projectQuery = projectQuery.Where(project => project.UserId == request.UserId);

        var project = await projectQuery
            .Select(project => new { project.Id, project.Title, project.UserId })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (project == null)
        {
            AddIssue(report, "project_scope", "error", 0, string.Empty, "没有权限读取该项目，或项目不存在。", "确认当前会话绑定的项目。");
            Complete(report);
            return report;
        }

        report.ProjectId = project.Id;
        report.ProjectTitle = project.Title ?? string.Empty;

        var chapters = await _db.Chapters
            .AsNoTracking()
            .Where(chapter => chapter.ProjectId == project.Id)
            .OrderBy(chapter => chapter.ChapterNumber)
            .Select(chapter => new
            {
                chapter.Id,
                chapter.Title,
                chapter.ChapterNumber,
                chapter.Status,
                chapter.WordCount,
                chapter.CurrentDocumentId
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (chapters.Count == 0)
        {
            AddIssue(report, "empty_book", "warning", 0, string.Empty, "当前项目还没有章节。", "先完成章节规划与正文生产。");
            Complete(report);
            return report;
        }

        if (report.StartChapterNumber <= 0)
            report.StartChapterNumber = chapters.Where(chapter => chapter.ChapterNumber > 0).Select(chapter => chapter.ChapterNumber).DefaultIfEmpty(1).Min();
        if (report.EndChapterNumber <= 0)
            report.EndChapterNumber = chapters.Where(chapter => chapter.ChapterNumber > 0).Select(chapter => chapter.ChapterNumber).DefaultIfEmpty(report.StartChapterNumber).Max();

        var selected = chapters
            .Where(chapter => chapter.ChapterNumber >= report.StartChapterNumber &&
                              chapter.ChapterNumber <= report.EndChapterNumber)
            .ToList();
        var latestSnapshots = await LoadLatestFactSnapshotsAsync(
                project.Id,
                selected.Select(chapter => new ChapterIdentity(chapter.Id, chapter.ChapterNumber)),
                cancellationToken)
            .ConfigureAwait(false);
        var bodyByChapterId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var expected in Enumerable.Range(
                     report.StartChapterNumber,
                     report.EndChapterNumber - report.StartChapterNumber + 1))
        {
            if (selected.All(chapter => chapter.ChapterNumber != expected))
            {
                AddIssue(report, "missing_chapter", "error", expected, string.Empty, $"缺少第 {expected} 章。", "补齐章节后再推进后续章节或整书验收。");
            }
        }

        foreach (var chapter in selected)
        {
            latestSnapshots.TryGetValue(chapter.Id, out var snapshot);
            var facts = ParseFactSnapshot(snapshot?.SnapshotJson);
            var body = await ReadBodyAsync(project.UserId, project.Id, chapter.Id, chapter.CurrentDocumentId, cancellationToken)
                .ConfigureAwait(false);
            bodyByChapterId[chapter.Id] = body;
            var item = new BookValidationChapter
            {
                ChapterId = chapter.Id,
                ChapterNumber = chapter.ChapterNumber,
                Title = chapter.Title ?? string.Empty,
                Status = chapter.Status ?? string.Empty,
                WordCount = chapter.WordCount,
                HasBody = !string.IsNullOrWhiteSpace(body),
                CurrentVersionId = await LoadCurrentVersionIdAsync(chapter.Id, cancellationToken).ConfigureAwait(false),
                FactSnapshotId = snapshot?.Id ?? string.Empty,
                FactSnapshotVersion = snapshot?.VersionNumber ?? 0,
                ProtagonistName = facts.ProtagonistName,
                EndingState = facts.EndingState,
                NextChapterMustCarry = facts.NextChapterMustCarry,
                BodyPreview = request.IncludeBodyPreview ? TrimBody(body, 240) : string.Empty
            };
            report.Chapters.Add(item);

            if (!string.Equals(chapter.Status, "committed", StringComparison.OrdinalIgnoreCase))
            {
                AddIssue(report, "chapter_not_committed", "error", chapter.ChapterNumber, chapter.Id, $"第 {chapter.ChapterNumber} 章尚未提交书城。", "先通过门禁并提交章节版本。");
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                AddIssue(report, "missing_body", "error", chapter.ChapterNumber, chapter.Id, $"第 {chapter.ChapterNumber} 章没有正文。", "重新提交正文或修复 ContentDocument 绑定。");
            }
            else if (chapter.WordCount < MinimumCommittedWordCount)
            {
                AddIssue(report, "short_body", "warning", chapter.ChapterNumber, chapter.Id, $"第 {chapter.ChapterNumber} 章字数偏少：{chapter.WordCount}。", "扩写到项目要求的章节体量。");
            }

            if (string.IsNullOrWhiteSpace(snapshot?.Id))
            {
                AddIssue(report, "missing_fact_snapshot", "error", chapter.ChapterNumber, chapter.Id, $"第 {chapter.ChapterNumber} 章缺少 FactSnapshot。", "重跑提交后事实沉淀或后台 outbox。");
            }
        }

        CheckAdjacentContinuity(report, bodyByChapterId);
        Complete(report);
        return report;
    }

    private async Task<bool> IsAdminAsync(string userId, CancellationToken cancellationToken)
    {
        var role = await _db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.Role)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, FactSnapshotRow>> LoadLatestFactSnapshotsAsync(
        string projectId,
        IEnumerable<ChapterIdentity> chapters,
        CancellationToken cancellationToken)
    {
        var candidateToChapterId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var chapter in chapters)
        {
            foreach (var candidate in BuildChapterIdCandidates(chapter.Id, chapter.ChapterNumber))
            {
                candidateToChapterId.TryAdd(candidate, chapter.Id);
            }
        }
        var chapterSet = candidateToChapterId.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var snapshots = await _db.ProjectFactSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.ProjectId == projectId &&
                               snapshot.ChapterId != null &&
                               chapterSet.Contains(snapshot.ChapterId))
            .OrderBy(snapshot => snapshot.ChapterId)
            .ThenByDescending(snapshot => snapshot.VersionNumber)
            .ThenByDescending(snapshot => snapshot.CreatedAt)
            .Select(snapshot => new FactSnapshotRow(
                snapshot.Id,
                snapshot.ChapterId ?? string.Empty,
                snapshot.VersionNumber,
                snapshot.SnapshotJson))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return snapshots
            .Where(snapshot => candidateToChapterId.ContainsKey(snapshot.ChapterId))
            .GroupBy(snapshot => candidateToChapterId[snapshot.ChapterId], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    private async Task<string> LoadCurrentVersionIdAsync(
        string chapterId,
        CancellationToken cancellationToken)
    {
        var currentDocumentId = await _db.Chapters
            .AsNoTracking()
            .Where(chapter => chapter.Id == chapterId)
            .Select(chapter => chapter.CurrentDocumentId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(currentDocumentId))
            return string.Empty;

        return await _db.ChapterVersions
            .AsNoTracking()
            .Where(version => version.ChapterId == chapterId &&
                              version.ContentDocumentId == currentDocumentId)
            .OrderByDescending(version => version.VersionNumber)
            .Select(version => version.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? string.Empty;
    }

    private async Task<string> ReadBodyAsync(
        string userId,
        string projectId,
        string chapterId,
        string? currentDocumentId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(currentDocumentId))
        {
            var chunks = await _db.ContentChunks
                .AsNoTracking()
                .Where(chunk => chunk.DocumentId == currentDocumentId)
                .OrderBy(chunk => chunk.ChunkIndex)
                .Select(chunk => chunk.ChunkText)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (chunks.Count > 0)
                return string.Join("\n", chunks);
        }

        try
        {
            return await _contentDocuments.GetTextAsync(
                    userId,
                    projectId,
                    "chapter",
                    chapterId,
                    "chapter_body",
                    cancellationToken)
                .ConfigureAwait(false) ?? string.Empty;
        }
        catch (KeyNotFoundException)
        {
            return string.Empty;
        }
    }

    private static void CheckAdjacentContinuity(
        BookValidationReport report,
        IReadOnlyDictionary<string, string> bodyByChapterId)
    {
        var chapters = report.Chapters
            .Where(chapter => chapter.ChapterNumber > 0)
            .OrderBy(chapter => chapter.ChapterNumber)
            .ToList();
        for (var i = 1; i < chapters.Count; i++)
        {
            var previous = chapters[i - 1];
            var current = chapters[i];
            if (current.ChapterNumber != previous.ChapterNumber + 1)
                continue;

            if (!string.IsNullOrWhiteSpace(previous.ProtagonistName) &&
                !string.IsNullOrWhiteSpace(current.ProtagonistName) &&
                !string.Equals(previous.ProtagonistName, current.ProtagonistName, StringComparison.OrdinalIgnoreCase))
            {
                AddIssue(report,
                    "protagonist_continuity_mismatch",
                    "error",
                    current.ChapterNumber,
                    current.ChapterId,
                    $"第 {current.ChapterNumber} 章主角硬事实不一致：上一章为 {previous.ProtagonistName}，本章为 {current.ProtagonistName}。",
                    "创建修订计划，重建本章生产包并重新生成正文。");
            }

            foreach (var carry in previous.NextChapterMustCarry)
            {
                if (string.IsNullOrWhiteSpace(carry))
                    continue;
                bodyByChapterId.TryGetValue(current.ChapterId, out var currentBody);
                var visible = ContainsAny(current.EndingState, carry) ||
                              ContainsAny(current.BodyPreview, carry) ||
                              ContainsAny(currentBody ?? string.Empty, carry);
                if (!visible)
                {
                    AddIssue(report,
                        "carry_not_reflected",
                        "warning",
                        current.ChapterNumber,
                        current.ChapterId,
                        $"第 {current.ChapterNumber} 章未明显承接上一章要求：{carry}",
                        "让 Agent 读取相邻章节并决定是否创建修订计划。");
                }
            }
        }
    }

    private static ContinuityFactSummary ParseFactSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ContinuityFactSummary();

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return new ContinuityFactSummary
            {
                ProtagonistName = GetString(root, "protagonistName", "ProtagonistName"),
                EndingState = GetString(root, "endingState", "EndingState"),
                NextChapterMustCarry = GetStringArray(root, "nextChapterMustCarry", "NextChapterMustCarry")
            };
        }
        catch (JsonException)
        {
            return new ContinuityFactSummary();
        }
    }

    private static string GetString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static List<string> GetStringArray(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty(name, out var value) ||
                value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            return value
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? string.Empty)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Take(12)
                .ToList();
        }

        return new List<string>();
    }

    private static bool ContainsAny(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(needle))
            return false;
        if (haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
            return true;

        var compactNeedle = new string(needle.Where(char.IsLetterOrDigit).ToArray());
        if (compactNeedle.Length < 4)
            return false;
        var compactHaystack = new string(haystack.Where(char.IsLetterOrDigit).ToArray());
        return compactHaystack.Contains(compactNeedle, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> BuildChapterIdCandidates(string? chapterId, int chapterNumber)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = chapterId?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            candidates.Add(normalized);
            var markerIndex = normalized.LastIndexOf("chapter-", StringComparison.OrdinalIgnoreCase);
            if (markerIndex >= 0)
                candidates.Add(normalized[markerIndex..]);
        }

        var number = chapterNumber > 0 ? chapterNumber : ExtractTrailingNumber(normalized);
        if (number > 0)
        {
            candidates.Add($"chapter-{number:000}");
            candidates.Add($"chapter-{number}");
        }

        return candidates.ToList();
    }

    private static int ExtractTrailingNumber(string value)
    {
        var index = value.Length - 1;
        while (index >= 0 && char.IsDigit(value[index]))
            index--;
        return index == value.Length - 1 || !int.TryParse(value[(index + 1)..], out var number) ? 0 : number;
    }

    private static void AddIssue(
        BookValidationReport report,
        string code,
        string severity,
        int chapterNumber,
        string chapterId,
        string message,
        string suggestion)
    {
        report.Issues.Add(new BookValidationIssue
        {
            Code = code,
            Severity = severity,
            ChapterNumber = chapterNumber,
            ChapterId = chapterId,
            Message = message,
            Suggestion = suggestion
        });
    }

    private static void Complete(BookValidationReport report)
    {
        report.OverallStatus = report.Issues.Any(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase))
            ? "blocked"
            : report.Issues.Count == 0
                ? "validated"
                : "warnings";
        report.Summary = $"整书校验 {report.OverallStatus}：章节 {report.Chapters.Count} 个，问题 {report.Issues.Count} 个。";
    }

    private static string TrimBody(string body, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;
        var normalized = body.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + "...";
    }

    private sealed record FactSnapshotRow(
        string Id,
        string ChapterId,
        int VersionNumber,
        string SnapshotJson);

    private sealed record ChapterIdentity(string Id, int ChapterNumber);

    private sealed class ContinuityFactSummary
    {
        public string ProtagonistName { get; set; } = string.Empty;
        public string EndingState { get; set; } = string.Empty;
        public List<string> NextChapterMustCarry { get; set; } = new();
    }
}
