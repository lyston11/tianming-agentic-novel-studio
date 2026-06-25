using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TM.Framework.Common.Helpers;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Tracking;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class ProductionPlotPointRecallService : IPlotPointRecallService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly NovelAgentDbContext? _db;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly string _projectId;

    public ProductionPlotPointRecallService(NovelAgentDbContext db, string projectId)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _projectId = projectId;
    }

    public ProductionPlotPointRecallService(IServiceScopeFactory scopeFactory, string projectId)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _projectId = projectId;
    }

    public async Task<List<PlotPointEntry>> SearchRecentAsync(
        string currentChapterId,
        HashSet<string>? characterIds,
        HashSet<string>? otherEntityIds,
        int lookbackVolumes = 0)
    {
        if (string.IsNullOrWhiteSpace(_projectId) ||
            string.IsNullOrWhiteSpace(currentChapterId) ||
            ((characterIds == null || characterIds.Count == 0) &&
             (otherEntityIds == null || otherEntityIds.Count == 0)))
        {
            return new List<PlotPointEntry>();
        }

        if (_db != null)
            return await QueryAsync(_db, currentChapterId, characterIds, otherEntityIds, lookbackVolumes)
                .ConfigureAwait(false);

        if (_scopeFactory == null)
            throw new InvalidOperationException("Plot point recall requires project-scoped database truth.");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        return await QueryAsync(db, currentChapterId, characterIds, otherEntityIds, lookbackVolumes)
            .ConfigureAwait(false);
    }

    public void InvalidateCache()
    {
    }

    private async Task<List<PlotPointEntry>> QueryAsync(
        NovelAgentDbContext db,
        string currentChapterId,
        HashSet<string>? characterIds,
        HashSet<string>? otherEntityIds,
        int lookbackVolumes)
    {
        var events = await db.ProductionEvents
            .AsNoTracking()
            .Where(evt => evt.ProjectId == _projectId)
            .Where(evt => evt.EventType == "chapter_changes_recorded")
            .OrderByDescending(evt => evt.CreatedAt)
            .Take(500)
            .ToListAsync()
            .ConfigureAwait(false);

        return events
            .SelectMany(ExtractPlotPoints)
            .Where(entry => IsChapterInRecallWindow(entry.Chapter, currentChapterId, lookbackVolumes))
            .Where(entry => Matches(entry, characterIds, otherEntityIds))
            .ToList();
    }

    private static IEnumerable<PlotPointEntry> ExtractPlotPoints(ProductionEvent evt)
    {
        var changes = TryReadChanges(evt.DataJson);
        if (changes?.NewPlotPoints == null || changes.NewPlotPoints.Count == 0)
            yield break;

        for (var i = 0; i < changes.NewPlotPoints.Count; i++)
        {
            var change = changes.NewPlotPoints[i];
            if (string.IsNullOrWhiteSpace(change.Context))
                continue;

            yield return new PlotPointEntry
            {
                Id = $"{evt.Id}:{i}",
                Chapter = evt.ChapterId ?? string.Empty,
                Keywords = change.Keywords ?? new List<string>(),
                Context = change.Context,
                InvolvedCharacters = change.InvolvedCharacters ?? new List<string>(),
                Importance = string.IsNullOrWhiteSpace(change.Importance) ? "normal" : change.Importance,
                Storyline = string.IsNullOrWhiteSpace(change.Storyline) ? "main" : change.Storyline,
                CausedBy = change.CausedBy ?? string.Empty
            };
        }
    }

    private static ChapterChanges? TryReadChanges(string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
            return null;

        try
        {
            using var document = JsonDocument.Parse(dataJson);
            if (document.RootElement.TryGetProperty("changes", out var changesElement) &&
                changesElement.ValueKind == JsonValueKind.Object)
            {
                return JsonSerializer.Deserialize<ChapterChanges>(changesElement.GetRawText(), JsonOptions);
            }

            if (document.RootElement.TryGetProperty("changesJson", out var changesJsonElement) &&
                changesJsonElement.ValueKind == JsonValueKind.String)
            {
                var changesJson = changesJsonElement.GetString();
                return string.IsNullOrWhiteSpace(changesJson)
                    ? null
                    : JsonSerializer.Deserialize<ChapterChanges>(changesJson, JsonOptions);
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static bool Matches(
        PlotPointEntry entry,
        HashSet<string>? characterIds,
        HashSet<string>? otherEntityIds)
    {
        var matchCharacter = characterIds is { Count: > 0 } &&
            entry.InvolvedCharacters.Any(characterIds.Contains);
        var matchOther = otherEntityIds is { Count: > 0 } &&
            entry.Keywords.Any(otherEntityIds.Contains);
        return matchCharacter || matchOther;
    }

    private static bool IsChapterInRecallWindow(
        string chapterId,
        string currentChapterId,
        int lookbackVolumes)
    {
        if (string.IsNullOrWhiteSpace(chapterId))
            return false;

        if (string.Equals(chapterId, currentChapterId, StringComparison.OrdinalIgnoreCase))
            return false;

        var current = ChapterParserHelper.ParseChapterId(currentChapterId);
        var candidate = ChapterParserHelper.ParseChapterId(chapterId);
        if (current == null || candidate == null)
            return true;

        if (ChapterParserHelper.CompareChapterId(chapterId, currentChapterId) >= 0)
            return false;

        var minVolume = lookbackVolumes <= 0
            ? 1
            : Math.Max(1, current.Value.volumeNumber - lookbackVolumes);
        return candidate.Value.volumeNumber >= minVolume &&
               candidate.Value.volumeNumber <= current.Value.volumeNumber;
    }
}
