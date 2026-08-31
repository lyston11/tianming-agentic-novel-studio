using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Framework.Common.Helpers;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using TM.Services.Modules.ProjectData.Models.Design.Factions;
using TM.Services.Modules.ProjectData.Models.Design.Location;
using TM.Services.Modules.ProjectData.Models.Design.Plot;
using TM.Services.Modules.ProjectData.Models.Design.Templates;
using TM.Services.Modules.ProjectData.Models.Design.Worldview;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterBlueprint;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterPlanning;
using TM.Services.Modules.ProjectData.Models.Generate.StrategicOutline;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class ProductionGuideRuntimeDataSource : IGuideRuntimeDataSource
{
    private readonly NovelAgentDbContext? _db;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly string _userId;
    private readonly string _projectId;

    public ProductionGuideRuntimeDataSource(
        NovelAgentDbContext db,
        string userId,
        string projectId)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userId = userId;
        _projectId = projectId;
    }

    public ProductionGuideRuntimeDataSource(
        IServiceScopeFactory scopeFactory,
        string userId,
        string projectId)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _userId = userId;
        _projectId = projectId;
    }

    public async Task<T> LoadGuideAsync<T>(string guideKey) where T : new()
    {
        if (typeof(T) == typeof(ContentGuide) &&
            string.Equals(guideKey, GuideRuntimeDataKeys.ContentGuide, StringComparison.Ordinal))
        {
            var guide = await WithDbAsync(BuildContentGuideAsync).ConfigureAwait(false);
            return (T)(object)guide;
        }

        if (typeof(T) == typeof(OutlineGuide) &&
            string.Equals(guideKey, GuideRuntimeDataKeys.OutlineGuide, StringComparison.Ordinal))
        {
            var guide = await WithDbAsync(BuildOutlineGuideAsync).ConfigureAwait(false);
            return (T)(object)guide;
        }

        return new T();
    }

    public async Task<IReadOnlyList<T>> LoadItemsAsync<T>(string dataKey)
    {
        if (typeof(T) == typeof(CharacterRulesData) &&
            string.Equals(dataKey, GuideRuntimeDataKeys.Characters, StringComparison.Ordinal))
        {
            return CastList<T>(await WithDbAsync(LoadCharactersAsync).ConfigureAwait(false));
        }

        if (typeof(T) == typeof(VolumeDesignData) &&
            string.Equals(dataKey, GuideRuntimeDataKeys.VolumeDesigns, StringComparison.Ordinal))
        {
            return CastList<T>(await WithDbAsync(LoadVolumeDesignsAsync).ConfigureAwait(false));
        }

        if (typeof(T) == typeof(OutlineData) &&
            string.Equals(dataKey, GuideRuntimeDataKeys.Outlines, StringComparison.Ordinal))
        {
            return CastList<T>(await WithDbAsync(LoadOutlinesAsync).ConfigureAwait(false));
        }

        if (typeof(T) == typeof(ChapterData) &&
            string.Equals(dataKey, GuideRuntimeDataKeys.ChapterPlans, StringComparison.Ordinal))
        {
            return CastList<T>(await WithDbAsync(LoadChapterPlansAsync).ConfigureAwait(false));
        }

        if (typeof(T) == typeof(BlueprintData) &&
            string.Equals(dataKey, GuideRuntimeDataKeys.Blueprints, StringComparison.Ordinal))
        {
            return CastList<T>(await WithDbAsync(LoadBlueprintsAsync).ConfigureAwait(false));
        }

        if (typeof(T) == typeof(WorldRulesData) ||
            typeof(T) == typeof(LocationRulesData) ||
            typeof(T) == typeof(FactionRulesData) ||
            typeof(T) == typeof(PlotRulesData) ||
            typeof(T) == typeof(CreativeMaterialData))
        {
            return Array.Empty<T>();
        }

        return Array.Empty<T>();
    }

    private async Task<TResult> WithDbAsync<TResult>(Func<NovelAgentDbContext, Task<TResult>> action)
    {
        if (_db != null)
            return await action(_db).ConfigureAwait(false);

        if (_scopeFactory == null)
            throw new InvalidOperationException("Guide runtime data source requires project-scoped database truth.");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        return await action(db).ConfigureAwait(false);
    }

    private async Task<ContentGuide> BuildContentGuideAsync(NovelAgentDbContext db)
    {
        var chapters = await LoadChapterGuideSourcesAsync(db).ConfigureAwait(false);
        var volumeArcs = await LoadProjectVolumeArcsAsync(db).ConfigureAwait(false);
        var characterIds = await LoadProjectCharacterIdsAsync(db).ConfigureAwait(false);
        var summaries = await LoadChapterSummariesAsync(db).ConfigureAwait(false);

        var guide = new ContentGuide();
        foreach (var chapter in chapters)
        {
            var volume = ResolveVolume(chapter, volumeArcs);
            var previous = chapters
                .Where(item => item.ChapterNumber < chapter.ChapterNumber)
                .OrderByDescending(item => item.ChapterNumber)
                .FirstOrDefault();
            var chapterPlanId = BuildChapterPlanId(chapter.Id);
            var blueprintId = BuildBlueprintId(chapter.Id);
            var title = FirstNonEmpty(chapter.Title, chapter.Brief?.SelectedCandidateTitle, chapter.Brief?.RecommendedCandidateTitle, chapter.Id);
            var mainGoal = FirstNonEmpty(chapter.Brief?.CoreIdea, title);
            var keyTurn = FirstNonEmpty(chapter.Brief?.ConflictMove, volume?.MajorConflict);
            var hook = FirstNonEmpty(chapter.Brief?.CostOrConsequence, chapter.Brief?.ForeshadowingAction, volume?.KeyEvents);

            var entry = new ContentGuideEntry
            {
                ChapterId = chapter.Id,
                Title = title,
                Summary = summaries.TryGetValue(chapter.Id, out var summary) ? summary : mainGoal,
                ChapterNumber = chapter.ChapterNumber,
                Volume = volume?.VolumeTitle ?? string.Empty,
                ChapterTheme = volume?.VolumeTheme ?? string.Empty,
                MainGoal = mainGoal,
                KeyTurn = keyTurn,
                Hook = hook,
                ContextIds = new ContextIdCollection
                {
                    VolumeOutline = BuildOutlineId(),
                    VolumeDesignId = volume?.Id ?? string.Empty,
                    ChapterPlanId = chapterPlanId,
                    ChapterBlueprint = blueprintId,
                    BlueprintIds = new List<string> { blueprintId },
                    Characters = characterIds,
                    PreviousChapter = previous?.Id ?? string.Empty
                }
            };
            guide.Chapters[chapter.Id] = entry;
            AddLogicalChapterAlias(guide, chapter, entry);

            if (!string.IsNullOrWhiteSpace(summary))
            {
                guide.ChapterSummaries[chapter.Id] = summary;
                AddLogicalChapterSummaryAlias(guide, chapter, summary);
            }
        }

        return guide;
    }

    private async Task<OutlineGuide> BuildOutlineGuideAsync(NovelAgentDbContext db)
    {
        var volumeArcs = await LoadProjectVolumeArcsAsync(db).ConfigureAwait(false);
        var characterIds = await LoadProjectCharacterIdsAsync(db).ConfigureAwait(false);
        var guide = new OutlineGuide();
        foreach (var volume in volumeArcs)
        {
            guide.Volumes[volume.Id] = new VolumeGuideEntry
            {
                VolumeNumber = volume.VolumeNumber,
                Name = volume.VolumeTitle,
                Theme = volume.VolumeTheme ?? string.Empty,
                PlannedChapters = volume.TargetChapters ?? 0,
                ContextIds = new ContextIdCollection
                {
                    VolumeOutline = BuildOutlineId(),
                    VolumeDesignId = volume.Id,
                    Characters = characterIds
                }
            };
        }

        return guide;
    }

    private async Task<List<CharacterRulesData>> LoadCharactersAsync(NovelAgentDbContext db)
    {
        var characters = await db.Characters
            .AsNoTracking()
            .Where(character => character.UserId == _userId && character.ProjectId == _projectId)
            .OrderBy(character => character.CreatedAt)
            .ToListAsync()
            .ConfigureAwait(false);

        return characters.Select(character => new CharacterRulesData
        {
            Id = character.Id,
            Name = character.Name,
            CharacterType = MapCharacterRole(character.Role),
            Gender = character.Gender ?? string.Empty,
            Age = character.Age?.ToString() ?? string.Empty,
            Identity = FirstNonEmpty(character.Background, character.Role),
            Appearance = character.Appearance ?? string.Empty,
            Personality = character.Personality ?? string.Empty,
            Want = character.CoreGoal ?? string.Empty,
            Need = character.Motivation ?? string.Empty,
            SpecialAbilities = character.SpecialAbilities ?? string.Empty,
            Relationships = character.Relationships ?? string.Empty,
            CreatedAt = character.CreatedAt,
            UpdatedAt = character.UpdatedAt
        }).ToList();
    }

    private async Task<List<VolumeDesignData>> LoadVolumeDesignsAsync(NovelAgentDbContext db)
    {
        var volumeArcs = await LoadProjectVolumeArcsAsync(db).ConfigureAwait(false);
        return volumeArcs.Select(volume => new VolumeDesignData
        {
            Id = volume.Id,
            Name = volume.VolumeTitle,
            VolumeNumber = volume.VolumeNumber,
            VolumeTitle = volume.VolumeTitle,
            VolumeTheme = volume.VolumeTheme ?? string.Empty,
            StageGoal = FirstNonEmpty(volume.Act1Setup, volume.Act2Confrontation, volume.Act3Climax, volume.Act4Resolution),
            TargetChapterCount = volume.TargetChapters ?? 0,
            StartChapter = 1,
            EndChapter = volume.TargetChapters ?? 0,
            MainConflict = volume.MajorConflict ?? string.Empty,
            PressureSource = volume.ConflictEscalation ?? string.Empty,
            KeyEvents = volume.KeyEvents ?? string.Empty,
            CreatedAt = volume.CreatedAt,
            UpdatedAt = volume.UpdatedAt
        }).ToList();
    }

    private async Task<List<OutlineData>> LoadOutlinesAsync(NovelAgentDbContext db)
    {
        var project = await db.NovelProjects
            .AsNoTracking()
            .FirstOrDefaultAsync(project => project.UserId == _userId && project.Id == _projectId)
            .ConfigureAwait(false);

        if (project == null)
            return new List<OutlineData>();

        return new List<OutlineData>
        {
            new()
            {
                Id = BuildOutlineId(),
                Name = project.Title,
                OneLineOutline = project.Title,
                Theme = project.Genre ?? string.Empty,
                OutlineOverview = project.Title,
                CreatedAt = project.CreatedAt,
                UpdatedAt = project.UpdatedAt
            }
        };
    }

    private async Task<List<ChapterData>> LoadChapterPlansAsync(NovelAgentDbContext db)
    {
        var chapters = await LoadChapterGuideSourcesAsync(db).ConfigureAwait(false);
        var volumeArcs = await LoadProjectVolumeArcsAsync(db).ConfigureAwait(false);
        return chapters.Select(chapter =>
        {
            var volume = ResolveVolume(chapter, volumeArcs);
            var title = FirstNonEmpty(chapter.Title, chapter.Brief?.SelectedCandidateTitle, chapter.Brief?.RecommendedCandidateTitle, chapter.Id);
            return new ChapterData
            {
                Id = BuildChapterPlanId(chapter.Id),
                Name = title,
                ChapterTitle = title,
                ChapterNumber = chapter.ChapterNumber,
                Volume = volume == null ? string.Empty : $"第{volume.VolumeNumber}卷 {volume.VolumeTitle}",
                ChapterTheme = volume?.VolumeTheme ?? string.Empty,
                MainGoal = FirstNonEmpty(chapter.Brief?.CoreIdea, title),
                KeyTurn = FirstNonEmpty(chapter.Brief?.ConflictMove, volume?.MajorConflict),
                Hook = FirstNonEmpty(chapter.Brief?.CostOrConsequence, chapter.Brief?.ForeshadowingAction, volume?.KeyEvents),
                CreatedAt = chapter.CreatedAt,
                UpdatedAt = chapter.UpdatedAt
            };
        }).ToList();
    }

    private async Task<List<BlueprintData>> LoadBlueprintsAsync(NovelAgentDbContext db)
    {
        var chapters = await LoadChapterGuideSourcesAsync(db).ConfigureAwait(false);
        return chapters.Select(chapter =>
        {
            var title = FirstNonEmpty(chapter.Title, chapter.Brief?.SelectedCandidateTitle, chapter.Brief?.RecommendedCandidateTitle, chapter.Id);
            return new BlueprintData
            {
                Id = BuildBlueprintId(chapter.Id),
                Name = title,
                ChapterId = chapter.Id,
                OneLineStructure = FirstNonEmpty(chapter.Brief?.CoreIdea, title),
                SceneNumber = 1,
                SceneTitle = title,
                Opening = FirstNonEmpty(chapter.Brief?.CoreIdea, title),
                Development = FirstNonEmpty(chapter.Brief?.ConflictMove, chapter.Brief?.CharacterChoice),
                Turning = FirstNonEmpty(chapter.Brief?.CostOrConsequence, chapter.Brief?.ForeshadowingAction),
                Ending = FirstNonEmpty(chapter.Brief?.CostOrConsequence, chapter.Brief?.ForeshadowingAction),
                CreatedAt = chapter.CreatedAt,
                UpdatedAt = chapter.UpdatedAt
            };
        }).ToList();
    }

    private Task<List<Chapter>> LoadProjectChaptersAsync(NovelAgentDbContext db) =>
        db.Chapters
            .AsNoTracking()
            .Where(chapter => chapter.ProjectId == _projectId)
            .OrderBy(chapter => chapter.ChapterNumber)
            .ThenBy(chapter => chapter.CreatedAt)
            .ToListAsync();

    private async Task<List<ChapterGuideSource>> LoadChapterGuideSourcesAsync(NovelAgentDbContext db)
    {
        var chapters = (await LoadProjectChaptersAsync(db).ConfigureAwait(false))
            .Select(chapter => ChapterGuideSource.FromChapter(chapter))
            .ToList();

        var existingIds = chapters
            .Select(chapter => chapter.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingNumbers = chapters
            .Select(chapter => chapter.ChapterNumber)
            .Where(number => number > 0)
            .ToHashSet();

        var plannedRuns = await LoadPlannedChapterRunsAsync(db).ConfigureAwait(false);
        foreach (var run in plannedRuns)
        {
            var chapterId = FirstNonEmpty(run.TargetChapterId, run.ChapterBrief?.ChapterId);
            if (string.IsNullOrWhiteSpace(chapterId) || existingIds.Contains(chapterId))
                continue;

            var number = ExtractTrailingNumber(chapterId);
            if (number <= 0)
                number = existingNumbers.Count == 0 ? 1 : existingNumbers.Max() + 1;
            if (existingNumbers.Contains(number))
                continue;

            var title = FirstNonEmpty(
                run.ChapterBrief?.SelectedCandidateTitle,
                run.ChapterBrief?.RecommendedCandidateTitle,
                run.ChapterBrief?.Candidates.FirstOrDefault()?.Title,
                $"第{number}章");
            chapters.Add(new ChapterGuideSource(
                chapterId,
                title,
                number,
                null,
                0,
                "planned",
                run.CreatedAt == default ? DateTime.UtcNow : run.CreatedAt,
                run.UpdatedAt == default ? DateTime.UtcNow : run.UpdatedAt,
                run.ChapterBrief));
            existingIds.Add(chapterId);
            existingNumbers.Add(number);
        }

        return chapters
            .OrderBy(chapter => chapter.ChapterNumber)
            .ThenBy(chapter => chapter.CreatedAt)
            .ToList();
    }

    private async Task<List<NovelAgentRun>> LoadPlannedChapterRunsAsync(NovelAgentDbContext db)
    {
        var rows = await db.AgentRuns
            .AsNoTracking()
            .Where(run =>
                run.UserId == _userId &&
                run.ProjectId == _projectId &&
                run.RunType == NovelAgentIntent.PlanChapter.ToString() &&
                !string.IsNullOrWhiteSpace(run.TargetChapterId) &&
                !string.IsNullOrWhiteSpace(run.OutputDocumentId))
            .OrderBy(run => run.CreatedAt)
            .ToListAsync()
            .ConfigureAwait(false);
        if (rows.Count == 0)
            return new List<NovelAgentRun>();

        var documentIds = rows
            .Select(run => run.OutputDocumentId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var chunks = await db.ContentChunks
            .AsNoTracking()
            .Where(chunk => documentIds.Contains(chunk.DocumentId))
            .OrderBy(chunk => chunk.DocumentId)
            .ThenBy(chunk => chunk.ChunkIndex)
            .ToListAsync()
            .ConfigureAwait(false);
        var textByDocument = chunks
            .GroupBy(chunk => chunk.DocumentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => string.Concat(group.OrderBy(chunk => chunk.ChunkIndex).Select(chunk => chunk.ChunkText)),
                StringComparer.OrdinalIgnoreCase);

        var result = new List<NovelAgentRun>();
        foreach (var row in rows)
        {
            if (!textByDocument.TryGetValue(row.OutputDocumentId!, out var json) || string.IsNullOrWhiteSpace(json))
                continue;
            try
            {
                var run = JsonSerializer.Deserialize<NovelAgentRun>(json, JsonHelper.CnDefault);
                if (run?.Intent == NovelAgentIntent.PlanChapter && run.ChapterBrief != null)
                    result.Add(run);
            }
            catch (JsonException)
            {
                // Ignore malformed historical run documents; they should not poison the production guide.
            }
        }

        return result;
    }

    private Task<List<VolumeArc>> LoadProjectVolumeArcsAsync(NovelAgentDbContext db) =>
        db.VolumeArcs
            .AsNoTracking()
            .Where(volume => volume.UserId == _userId && volume.ProjectId == _projectId)
            .OrderBy(volume => volume.VolumeNumber)
            .ToListAsync();

    private async Task<List<string>> LoadProjectCharacterIdsAsync(NovelAgentDbContext db) =>
        await db.Characters
            .AsNoTracking()
            .Where(character => character.UserId == _userId && character.ProjectId == _projectId)
            .OrderBy(character => character.CreatedAt)
            .Select(character => character.Id)
            .ToListAsync()
            .ConfigureAwait(false);

    private async Task<Dictionary<string, string>> LoadChapterSummariesAsync(NovelAgentDbContext db)
    {
        var events = await db.ProductionEvents
            .AsNoTracking()
            .Where(evt =>
                evt.UserId == _userId &&
                evt.ProjectId == _projectId &&
                evt.EventType == "chapter_summary_recorded")
            .OrderBy(evt => evt.CreatedAt)
            .ToListAsync()
            .ConfigureAwait(false);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var evt in events)
        {
            if (string.IsNullOrWhiteSpace(evt.ChapterId) || string.IsNullOrWhiteSpace(evt.Message))
                continue;
            result[evt.ChapterId] = evt.Message;
        }

        return result;
    }

    private static VolumeArc? ResolveVolume(ChapterGuideSource chapter, IReadOnlyList<VolumeArc> volumeArcs)
    {
        if (!string.IsNullOrWhiteSpace(chapter.VolumeId))
        {
            var byId = volumeArcs.FirstOrDefault(volume =>
                string.Equals(volume.Id, chapter.VolumeId, StringComparison.OrdinalIgnoreCase));
            if (byId != null)
                return byId;
        }

        var parsed = TryParseChapterVolume(chapter.Id);
        if (parsed.HasValue)
        {
            var byNumber = volumeArcs.FirstOrDefault(volume => volume.VolumeNumber == parsed.Value);
            if (byNumber != null)
                return byNumber;
        }

        return volumeArcs.FirstOrDefault();
    }

    private static IReadOnlyList<T> CastList<T>(IEnumerable<object> items) => items.Cast<T>().ToList();

    private static string BuildOutlineId() => "project:outline";

    private static string BuildChapterPlanId(string chapterId) => $"{chapterId}:plan";

    private static string BuildBlueprintId(string chapterId) => $"{chapterId}:blueprint";

    private static void AddLogicalChapterAlias(ContentGuide guide, ChapterGuideSource chapter, ContentGuideEntry entry)
    {
        var alias = BuildLogicalChapterId(chapter.ChapterNumber);
        if (string.IsNullOrWhiteSpace(alias) ||
            string.Equals(alias, chapter.Id, StringComparison.OrdinalIgnoreCase) ||
            guide.Chapters.ContainsKey(alias))
        {
            return;
        }

        guide.Chapters[alias] = entry;
    }

    private static void AddLogicalChapterSummaryAlias(ContentGuide guide, ChapterGuideSource chapter, string summary)
    {
        var alias = BuildLogicalChapterId(chapter.ChapterNumber);
        if (string.IsNullOrWhiteSpace(alias) ||
            string.Equals(alias, chapter.Id, StringComparison.OrdinalIgnoreCase) ||
            guide.ChapterSummaries.ContainsKey(alias))
        {
            return;
        }

        guide.ChapterSummaries[alias] = summary;
    }

    private static string BuildLogicalChapterId(int chapterNumber) =>
        chapterNumber <= 0 ? string.Empty : $"chapter-{chapterNumber:000}";

    private static string MapCharacterRole(string? role) => role?.ToLowerInvariant() switch
    {
        "protagonist" => "主角",
        "antagonist" => "反派",
        "supporting" => "主要角色",
        "minor" => "次要配角",
        _ => "角色"
    };

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static int? TryParseChapterVolume(string chapterId)
    {
        if (string.IsNullOrWhiteSpace(chapterId) || !chapterId.StartsWith("vol", StringComparison.OrdinalIgnoreCase))
            return null;

        var start = 3;
        var end = start;
        while (end < chapterId.Length && char.IsDigit(chapterId[end]))
            end++;

        return end > start && int.TryParse(chapterId[start..end], out var volumeNumber)
            ? volumeNumber
            : null;
    }

    private static int ExtractTrailingNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;
        var end = value.Length - 1;
        while (end >= 0 && !char.IsDigit(value[end]))
            end--;
        if (end < 0)
            return 0;
        var start = end;
        while (start >= 0 && char.IsDigit(value[start]))
            start--;
        return int.TryParse(value[(start + 1)..(end + 1)], out var number) ? number : 0;
    }

    private sealed record ChapterGuideSource(
        string Id,
        string Title,
        int ChapterNumber,
        string? VolumeId,
        int WordCount,
        string Status,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        ChapterCreativeBrief? Brief)
    {
        public static ChapterGuideSource FromChapter(Chapter chapter) => new(
            chapter.Id,
            chapter.Title,
            chapter.ChapterNumber,
            chapter.VolumeId,
            chapter.WordCount,
            chapter.Status,
            chapter.CreatedAt,
            chapter.UpdatedAt,
            null);
    }
}
