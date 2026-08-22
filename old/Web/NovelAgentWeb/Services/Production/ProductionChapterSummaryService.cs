using Microsoft.EntityFrameworkCore;
using System.Text.Encodings.Web;
using System.Text.Json;
using TM.Framework.Common.Helpers;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class ProductionChapterSummaryService : IChapterSummaryService
{
    private const int SummaryGuardMaxChars = 1500;
    private const string SummaryEventType = "chapter_summary_recorded";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    private readonly NovelAgentDbContext? _db;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly string _userId;
    private readonly string _projectId;

    public ProductionChapterSummaryService(
        NovelAgentDbContext db,
        string userId,
        string projectId)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userId = userId;
        _projectId = projectId;
    }

    public ProductionChapterSummaryService(
        IServiceScopeFactory scopeFactory,
        string userId,
        string projectId)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _userId = userId;
        _projectId = projectId;
    }

    public async Task SetSummaryAsync(
        string chapterId,
        string summary,
        string? runtimeRunId = null,
        string? packageId = null)
    {
        if (string.IsNullOrWhiteSpace(_userId) ||
            string.IsNullOrWhiteSpace(_projectId) ||
            string.IsNullOrWhiteSpace(chapterId))
        {
            throw new InvalidOperationException("Chapter summary production event requires userId, projectId and chapterId.");
        }

        var normalizedSummary = NormalizeSummary(summary);
        if (_db != null)
        {
            await AppendAsync(_db, chapterId, normalizedSummary, runtimeRunId, packageId).ConfigureAwait(false);
            return;
        }

        if (_scopeFactory == null)
            throw new InvalidOperationException("Chapter summary production event requires NovelAgentDbContext or IServiceScopeFactory.");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        await AppendAsync(db, chapterId, normalizedSummary, runtimeRunId, packageId).ConfigureAwait(false);
    }

    public async Task<string> GetSummaryAsync(string chapterId)
    {
        if (string.IsNullOrWhiteSpace(chapterId))
            return string.Empty;

        var summaries = await GetAllSummariesAsync().ConfigureAwait(false);
        return summaries.GetValueOrDefault(chapterId, string.Empty);
    }

    public async Task<Dictionary<string, string>> GetPreviousSummariesAsync(string currentChapterId, int count)
    {
        if (string.IsNullOrWhiteSpace(currentChapterId) || count <= 0)
            return new Dictionary<string, string>();

        var summaries = await GetAllSummariesAsync().ConfigureAwait(false);
        return summaries
            .Where(pair => ChapterParserHelper.CompareChapterId(pair.Key, currentChapterId) < 0)
            .OrderByDescending(pair => pair.Key, Comparer<string>.Create(ChapterParserHelper.CompareChapterId))
            .Take(count)
            .OrderBy(pair => pair.Key, Comparer<string>.Create(ChapterParserHelper.CompareChapterId))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<Dictionary<string, string>> GetVolumeSummariesAsync(int volumeNumber)
    {
        if (volumeNumber <= 0)
            return new Dictionary<string, string>();

        var summaries = await GetAllSummariesAsync().ConfigureAwait(false);
        return summaries
            .Where(pair => ChapterParserHelper.ParseChapterId(pair.Key)?.volumeNumber == volumeNumber)
            .OrderBy(pair => pair.Key, Comparer<string>.Create(ChapterParserHelper.CompareChapterId))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<Dictionary<string, string>> GetAllSummariesAsync()
    {
        if (string.IsNullOrWhiteSpace(_projectId))
            return new Dictionary<string, string>();

        if (_db != null)
            return await QuerySummariesAsync(_db).ConfigureAwait(false);

        if (_scopeFactory == null)
            throw new InvalidOperationException("Chapter summary service requires project-scoped database truth.");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        return await QuerySummariesAsync(db).ConfigureAwait(false);
    }

    public void InvalidateCache()
    {
    }

    private async Task AppendAsync(
        NovelAgentDbContext db,
        string chapterId,
        string summary,
        string? runtimeRunId,
        string? packageId)
    {
        var evt = new ProductionEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            RuntimeRunId = string.IsNullOrWhiteSpace(runtimeRunId)
                ? $"chapter-summary-{Guid.NewGuid():N}"
                : runtimeRunId,
            UserId = _userId,
            ProjectId = _projectId,
            ChapterId = chapterId,
            PackageId = packageId,
            EventType = SummaryEventType,
            Stage = NovelAgentProductionStages.FactsPersisted,
            Status = "completed",
            Message = "章节摘要已写入生产事实事件。",
            ArtifactType = "chapter_summary",
            ArtifactId = $"{chapterId}:summary",
            DataJson = JsonSerializer.Serialize(new { chapterId, summary }, JsonOptions),
            CreatedAt = DateTime.UtcNow
        };

        db.ProductionEvents.Add(evt);
        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    private async Task<Dictionary<string, string>> QuerySummariesAsync(NovelAgentDbContext db)
    {
        var query = db.ProductionEvents
            .AsNoTracking()
            .Where(evt => evt.ProjectId == _projectId && evt.EventType == SummaryEventType);

        if (!string.IsNullOrWhiteSpace(_userId))
            query = query.Where(evt => evt.UserId == _userId);

        var events = await query
            .OrderByDescending(evt => evt.CreatedAt)
            .Take(2000)
            .ToListAsync()
            .ConfigureAwait(false);

        var latestByChapter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var evt in events)
        {
            var extracted = ExtractSummary(evt);
            if (extracted == null)
                continue;

            latestByChapter.TryAdd(extracted.Value.ChapterId, extracted.Value.Summary);
        }

        return latestByChapter
            .OrderBy(pair => pair.Key, Comparer<string>.Create(ChapterParserHelper.CompareChapterId))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static SummaryRecord? ExtractSummary(ProductionEvent evt)
    {
        if (string.IsNullOrWhiteSpace(evt.DataJson))
            return null;

        try
        {
            using var document = JsonDocument.Parse(evt.DataJson);
            var root = document.RootElement;
            var chapterId = root.TryGetProperty("chapterId", out var chapterElement) &&
                            chapterElement.ValueKind == JsonValueKind.String
                ? chapterElement.GetString()
                : evt.ChapterId;
            var summary = root.TryGetProperty("summary", out var summaryElement) &&
                          summaryElement.ValueKind == JsonValueKind.String
                ? summaryElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(chapterId) || summary == null)
                return null;

            return new SummaryRecord(chapterId, summary);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string NormalizeSummary(string? summary)
    {
        var normalized = summary ?? string.Empty;
        return normalized.Length <= SummaryGuardMaxChars
            ? normalized
            : normalized[..SummaryGuardMaxChars] + "...";
    }

    private readonly record struct SummaryRecord(string ChapterId, string Summary);
}
