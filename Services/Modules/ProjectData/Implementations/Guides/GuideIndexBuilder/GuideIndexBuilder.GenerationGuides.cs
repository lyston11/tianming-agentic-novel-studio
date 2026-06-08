using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using TM.Services.Modules.ProjectData.Models.Design.Worldview;
using TM.Services.Modules.ProjectData.Models.Design.Location;
using TM.Services.Modules.ProjectData.Models.Design.Plot;
using TM.Services.Modules.ProjectData.Models.Design.Templates;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class GuideIndexBuilder
    {
        #region 6个生成指导（递增依赖）

        public async Task<OutlineGuide> BuildOutlineGuideAsync()
        {
            var guide = new OutlineGuide { Module = "OutlineGuide" };

            var volumesTask = LoadAllAsync<Models.Generate.StrategicOutline.OutlineData>("Generate/GlobalSettings/Outline");
            var charactersTask = LoadAllAsync<CharacterRulesData>("Design/Elements/CharacterRules");
            var factionRulesTask = LoadAllAsync<Models.Design.Factions.FactionRulesData>("Design/Elements/FactionRules");
            var locationRulesTask = LoadAllAsync<LocationRulesData>("Design/Elements/LocationRules");
            var plotRulesTask = LoadAllAsync<PlotRulesData>("Design/Elements/PlotRules");
            var templatesTask = LoadAllAsync<CreativeMaterialData>("Design/Templates/CreativeMaterials");
            var worldRulesTask = LoadAllAsync<WorldRulesData>("Design/GlobalSettings/WorldRules");
            var volumeDesignsTask = LoadAllAsync<VolumeDesignData>("Generate/Elements/VolumeDesign");
            await Task.WhenAll(volumesTask, charactersTask, factionRulesTask, locationRulesTask, plotRulesTask, templatesTask, worldRulesTask, volumeDesignsTask).ConfigureAwait(false);

            var volumes = await volumesTask.ConfigureAwait(false);
            var characters = await charactersTask.ConfigureAwait(false);
            var factionRules = await factionRulesTask.ConfigureAwait(false);
            var locationRules = await locationRulesTask.ConfigureAwait(false);
            var plotRules = await plotRulesTask.ConfigureAwait(false);
            var templates = await templatesTask.ConfigureAwait(false);
            var worldRules = await worldRulesTask.ConfigureAwait(false);
            var volumeDesigns = await volumeDesignsTask.ConfigureAwait(false);

            EnsureRequiredIds(volumes, v => v.Id, "全书大纲", v => v.Name);
            EnsureRequiredCategoryIds(volumes, v => v.Category, v => v.CategoryId, "全书大纲", v => v.Name);
            EnsureRequiredIds(characters, c => c.Id, "角色规则", c => c.Name);
            EnsureRequiredCategoryIds(characters, c => c.Category, c => c.CategoryId, "角色规则", c => c.Name);
            EnsureRequiredIds(factionRules, f => f.Id, "势力规则", f => f.Name);
            EnsureRequiredCategoryIds(factionRules, f => f.Category, f => f.CategoryId, "势力规则", f => f.Name);
            EnsureRequiredIds(locationRules, l => l.Id, "地点规则", l => l.Name);
            EnsureRequiredCategoryIds(locationRules, l => l.Category, l => l.CategoryId, "地点规则", l => l.Name);
            EnsureRequiredIds(plotRules, p => p.Id, "剧情规则", p => p.Name);
            EnsureRequiredCategoryIds(plotRules, p => p.Category, p => p.CategoryId, "剧情规则", p => p.Name);
            EnsureRequiredIds(templates, t => t.Id, "创作素材", t => t.Name);
            EnsureRequiredCategoryIds(templates, t => t.Category, t => t.CategoryId, "创作素材", t => t.Name);
            EnsureRequiredIds(worldRules, w => w.Id, "世界观规则", w => w.Name);
            EnsureRequiredCategoryIds(worldRules, w => w.Category, w => w.CategoryId, "世界观规则", w => w.Name);
            EnsureRequiredIds(volumeDesigns, v => v.Id, "分卷设计", v => v.Name);
            EnsureRequiredCategoryIds(volumeDesigns, v => v.Category, v => v.CategoryId, "分卷设计", v => v.Name);

            var templateIds = templates
                .Select(t => t.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();
            var worldRuleIds = worldRules
                .Select(w => w.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();

            var orderedVolumeDesigns = volumeDesigns
                .Where(v => v.VolumeNumber > 0 && !string.IsNullOrWhiteSpace(v.Id))
                .OrderByDescending(v => v.UpdatedAt)
                .GroupBy(v => v.VolumeNumber)
                .Select(g => g.First())
                .OrderBy(v => v.VolumeNumber)
                .ToList();

            var volumeNameErrors = new List<string>();

            var volumeOutlineId = volumes.MinBy(o => o.CreatedAt)?.Id ?? string.Empty;

            foreach (var vd in orderedVolumeDesigns)
            {
                var volId = vd.Id;

                var unmatchedChars = GetUnmatchedEntityNames(vd.ReferencedCharacterNames, characters, c => c.Name, c => c.Id);
                var unmatchedFactions = GetUnmatchedEntityNames(vd.ReferencedFactionNames, factionRules, f => f.Name, f => f.Id);
                var unmatchedLocs = GetUnmatchedEntityNames(vd.ReferencedLocationNames, locationRules, l => l.Name, l => l.Id);
                if (unmatchedChars.Count > 0)
                    volumeNameErrors.Add($"第{vd.VolumeNumber}卷涉及角色未映射: {string.Join("、", unmatchedChars)}");
                if (unmatchedFactions.Count > 0)
                    volumeNameErrors.Add($"第{vd.VolumeNumber}卷涉及势力未映射: {string.Join("、", unmatchedFactions)}");
                if (unmatchedLocs.Count > 0)
                    volumeNameErrors.Add($"第{vd.VolumeNumber}卷涉及地点未映射: {string.Join("、", unmatchedLocs)}");

                guide.Volumes[volId] = new VolumeGuideEntry
                {
                    VolumeNumber = vd.VolumeNumber,
                    Name = string.IsNullOrWhiteSpace(vd.VolumeTitle) ? vd.Name : vd.VolumeTitle,
                    Theme = vd.VolumeTheme,
                    PlannedChapters = vd.TargetChapterCount,

                    ContextIds = new ContextIdCollection
                    {
                        VolumeOutline = volumeOutlineId,
                        VolumeDesignId = vd.Id,
                        Characters = ResolveEntityIds(vd.ReferencedCharacterNames, characters, c => c.Name, c => c.Id),
                        Factions = ResolveEntityIds(vd.ReferencedFactionNames, factionRules, f => f.Name, f => f.Id),
                        Locations = ResolveEntityIds(vd.ReferencedLocationNames, locationRules, l => l.Name, l => l.Id),
                        PlotRules = new List<string>(),
                        TemplateIds = templateIds,
                        WorldRuleIds = worldRuleIds,
                        PreviousOutlines = new List<string>()
                    }
                };
            }

            if (volumeNameErrors.Count > 0)
            {
                throw new InvalidOperationException(
                    "分卷设计存在未映射到设计元素的引用名称，打包阻断：\n" + string.Join("\n", volumeNameErrors));
            }

            TM.App.Log($"[GuideIndexBuilder] 大纲指导构建完成，共{guide.Volumes.Count}卷");
            return guide;
        }

        public async Task<PlanningGuide> BuildPlanningGuideAsync(OutlineGuide outlineGuide)
        {
            var guide = new PlanningGuide { Module = "PlanningGuide" };

            var chapters = await LoadAllAsync<Models.Generate.ChapterPlanning.ChapterData>("Generate/Elements/Chapter").ConfigureAwait(false);

            EnsureRequiredIds(chapters, c => c.Id, "章节规划", c => c.Name);
            EnsureRequiredCategoryIds(chapters, c => c.Category, c => c.CategoryId, "章节规划", c => c.Name);

            foreach (var (volId, volumeEntry) in outlineGuide.Volumes)
            {
                var volumeNumber = volumeEntry.VolumeNumber;
                var volumePrefix = $"第{volumeNumber}卷";
                var volumeChapters = chapters
                    .Where(c =>
                        !string.IsNullOrEmpty(c.Volume) &&
                        (string.Equals(c.Volume, volumePrefix, StringComparison.Ordinal) ||
                         c.Volume.StartsWith(volumePrefix + " ", StringComparison.Ordinal)))
                    .ToList();

                var planningVolume = new PlanningVolumeEntry
                {
                    VolumeNumber = volumeNumber,
                    ContextIds = new ContextIdCollection
                    {
                        VolumeOutline = volumeEntry.ContextIds.VolumeOutline,
                        VolumeDesignId = volumeEntry.ContextIds.VolumeDesignId,
                        Characters = volumeEntry.ContextIds.Characters,
                        Factions = volumeEntry.ContextIds.Factions,
                        Locations = volumeEntry.ContextIds.Locations,
                        PlotRules = volumeEntry.ContextIds.PlotRules,
                        TemplateIds = volumeEntry.ContextIds.TemplateIds,
                        WorldRuleIds = volumeEntry.ContextIds.WorldRuleIds
                    },
                    Chapters = new Dictionary<string, PlanningChapterEntry>()
                };

                foreach (var chapter in volumeChapters.OrderBy(c => c.ChapterNumber))
                {
                    var chapterId = $"vol{volumeNumber}_ch{chapter.ChapterNumber}";

                    var title = !string.IsNullOrEmpty(chapter.ChapterTitle)
                        ? chapter.ChapterTitle
                        : $"第{chapter.ChapterNumber}章";

                    var rhythmInfo = new RhythmInfo();

                    planningVolume.Chapters[chapterId] = new PlanningChapterEntry
                    {
                        ChapterPlanId = chapter.Id,
                        ChapterNumber = chapter.ChapterNumber,
                        Title = title,
                        Synopsis = chapter.MainGoal,
                        Rhythm = rhythmInfo,
                        PlotAllocations = new List<PlotAllocationEntry>()
                    };
                }

                guide.Volumes[volId] = planningVolume;
            }

            TM.App.Log($"[GuideIndexBuilder] 规划指导构建完成，共{guide.Volumes.Count}卷");
            return guide;
        }

        public async Task<BlueprintGuide> BuildBlueprintGuideAsync(PlanningGuide planningGuide)
        {
            var guide = new BlueprintGuide { Module = "BlueprintGuide" };

            var packageWarnings = new List<PackageWarning>();

            var blueprintsTask = LoadAllAsync<Models.Generate.ChapterBlueprint.BlueprintData>("Generate/Elements/Blueprint");
            var chapterPlansTask = LoadAllAsync<Models.Generate.ChapterPlanning.ChapterData>("Generate/Elements/Chapter");
            var charactersTask = LoadAllAsync<CharacterRulesData>("Design/Elements/CharacterRules");
            var locationRulesTask = LoadAllAsync<LocationRulesData>("Design/Elements/LocationRules");
            var factionRulesTask = LoadAllAsync<Models.Design.Factions.FactionRulesData>("Design/Elements/FactionRules");
            var plotRulesTask = LoadAllAsync<PlotRulesData>("Design/Elements/PlotRules");
            await Task.WhenAll(blueprintsTask, chapterPlansTask, charactersTask, locationRulesTask, factionRulesTask, plotRulesTask).ConfigureAwait(false);

            var blueprints = await blueprintsTask.ConfigureAwait(false);
            var chapterPlans = await chapterPlansTask.ConfigureAwait(false);
            var characters = await charactersTask.ConfigureAwait(false);
            var locationRules = await locationRulesTask.ConfigureAwait(false);
            var factionRules = await factionRulesTask.ConfigureAwait(false);
            var plotRules = await plotRulesTask.ConfigureAwait(false);

            var chapterPlanById = chapterPlans
                .Where(c => !string.IsNullOrWhiteSpace(c.Id))
                .ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);

            EnsureRequiredIds(blueprints, b => b.Id, "章节蓝图", b => b.Name);
            EnsureRequiredCategoryIds(blueprints, b => b.Category, b => b.CategoryId, "章节蓝图", b => b.Name);
            EnsureRequiredIds(plotRules, p => p.Id, "剧情规则", p => p.Name);
            EnsureRequiredCategoryIds(plotRules, p => p.Category, p => p.CategoryId, "剧情规则", p => p.Name);

            var badChapterIdBlueprints = blueprints
                .Where(b => string.IsNullOrWhiteSpace(b.ChapterId)
                         || ChapterParserHelper.ParseChapterId(b.ChapterId) == null)
                .Select(b => string.IsNullOrWhiteSpace(b.ChapterId)
                    ? $"{b.Name ?? b.Id}（关联章节ID为空）"
                    : $"{b.Name ?? b.Id}（{b.ChapterId}）")
                .Distinct()
                .ToList();
            if (badChapterIdBlueprints.Count > 0)
                throw new InvalidOperationException(
                    $"蓝图存在 {badChapterIdBlueprints.Count} 条\"关联章节ID\"格式错误" +
                    $"（期望 vol{{卷号}}_ch{{章号}}，如 vol1_ch3）：" +
                    string.Join("、", badChapterIdBlueprints.Take(5)) +
                    "。请在蓝图设计中修正\"关联章节ID\"字段后重新打包。");

            var chapterInfoMap = new Dictionary<string, (string Title, string Synopsis, string VolumeId, RhythmInfo Rhythm, string ChapterPlanId)>();
            foreach (var (volId, volumeEntry) in planningGuide.Volumes)
            {
                foreach (var (chapterId, chapterEntry) in volumeEntry.Chapters)
                {
                    chapterInfoMap[chapterId] = (chapterEntry.Title, chapterEntry.Synopsis, volId, chapterEntry.Rhythm, chapterEntry.ChapterPlanId);
                }
            }

            var volumeContextMap = planningGuide.Volumes
                .ToDictionary(kv => kv.Key, kv => kv.Value.ContextIds);

            var volumeKeyByNumber = planningGuide.Volumes
                .Where(kv => kv.Value.VolumeNumber > 0)
                .GroupBy(kv => kv.Value.VolumeNumber)
                .ToDictionary(g => g.Key, g => g.First().Key);

            var chapterIds = blueprints
                .Select(b => b.ChapterId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => ChapterParserHelper.ParseChapterIdOrDefault(c).volumeNumber)
                .ThenBy(c => ChapterParserHelper.ParseChapterIdOrDefault(c).chapterNumber);

            var blueprintsByChapter = blueprints.ToLookup(b => b.ChapterId, StringComparer.OrdinalIgnoreCase);

            var characterNameToId = characters
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);
            var characterIdSet = new HashSet<string>(characters
                .Select(c => c.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
            var locationNameToId = locationRules
                .Where(l => !string.IsNullOrWhiteSpace(l.Name))
                .ToDictionary(l => l.Name, l => l.Id, StringComparer.OrdinalIgnoreCase);
            var locationIdSet = new HashSet<string>(locationRules
                .Select(l => l.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
            var factionNameToId = factionRules
                .Where(f => !string.IsNullOrWhiteSpace(f.Name))
                .ToDictionary(f => f.Name, f => f.Id, StringComparer.OrdinalIgnoreCase);
            var factionIdSet = new HashSet<string>(factionRules
                .Select(f => f.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);

            var plotRulesMainCharsSplit = plotRules.ToDictionary(
                p => p.Id,
                p => (p.MainCharacters ?? string.Empty)
                    .Split(new[] { ',', '，', '、', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

            string? previousChapterId = null;
            foreach (var chapterId in chapterIds)
            {
                var chapterInfo = chapterInfoMap.GetValueOrDefault(chapterId);
                var chapterBlueprints = blueprintsByChapter[chapterId].ToList();

                var parsed = ChapterParserHelper.ParseChapterIdOrDefault(chapterId);
                var volumeNumber = parsed.volumeNumber;
                var volumeKey = !string.IsNullOrWhiteSpace(chapterInfo.VolumeId)
                    ? chapterInfo.VolumeId
                    : (volumeKeyByNumber.TryGetValue(volumeNumber, out var key) ? key : string.Empty);

                var blueprintIds = chapterBlueprints
                    .Select(b => b.Id)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct()
                    .ToList();

                volumeContextMap.TryGetValue(volumeKey, out var volumeContextIds);
                var templateIds = volumeContextIds?.TemplateIds ?? new List<string>();
                var worldRuleIds = volumeContextIds?.WorldRuleIds ?? new List<string>();

                static List<string> SplitNames(string? text)
                {
                    if (string.IsNullOrWhiteSpace(text)) return new List<string>();
                    var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "无",
                        "暂无",
                        "空",
                        "-",
                        "/",
                        "none",
                        "n/a",
                        "null"
                    };
                    return text.Split(new[] { ',', '，', '、', '\n', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x) && !ignored.Contains(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                static List<string> MapNamesToIds(List<string> names, Dictionary<string, string> nameToId, HashSet<string> idSet)
                {
                    var ids = new List<string>();
                    foreach (var name in names)
                    {
                        if (idSet.Contains(name))
                        {
                            ids.Add(name);
                            continue;
                        }

                        if (nameToId.TryGetValue(name, out var id))
                        {
                            ids.Add(id);
                        }
                    }
                    return ids.Distinct().ToList();
                }

                static List<string> GetUnmatchedNames(List<string> names, Dictionary<string, string> nameToId, HashSet<string> idSet)
                {
                    var unmatched = new List<string>();
                    foreach (var name in names)
                    {
                        if (!idSet.Contains(name) && !nameToId.ContainsKey(name))
                        {
                            unmatched.Add(name);
                        }
                    }
                    return unmatched.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                }

                Models.Generate.ChapterPlanning.ChapterData? chapterPlan = null;
                if (!string.IsNullOrWhiteSpace(chapterInfo.ChapterPlanId))
                    chapterPlanById.TryGetValue(chapterInfo.ChapterPlanId, out chapterPlan);

                var characterNames = chapterBlueprints.SelectMany(b => SplitNames(b.Cast)).ToList();
                var characterIds = MapNamesToIds(characterNames, characterNameToId, characterIdSet);
                var unmatchedCharacters = GetUnmatchedNames(characterNames, characterNameToId, characterIdSet);
                if (characterIds.Count == 0 && chapterPlan?.ReferencedCharacterNames?.Count > 0)
                {
                    characterIds = MapNamesToIds(chapterPlan.ReferencedCharacterNames, characterNameToId, characterIdSet);
                    var unmatchedFromChapterPlan = GetUnmatchedNames(chapterPlan.ReferencedCharacterNames, characterNameToId, characterIdSet);
                    foreach (var n in unmatchedFromChapterPlan)
                        if (!unmatchedCharacters.Contains(n)) unmatchedCharacters.Add(n);
                }

                var locationNames = chapterBlueprints.SelectMany(b => SplitNames(b.Locations)).ToList();
                var locationIds = MapNamesToIds(locationNames, locationNameToId, locationIdSet);
                var unmatchedLocations = GetUnmatchedNames(locationNames, locationNameToId, locationIdSet);
                if (locationIds.Count == 0 && chapterPlan?.ReferencedLocationNames?.Count > 0)
                {
                    locationIds = MapNamesToIds(chapterPlan.ReferencedLocationNames, locationNameToId, locationIdSet);
                    var unmatchedFromChapterPlan = GetUnmatchedNames(chapterPlan.ReferencedLocationNames, locationNameToId, locationIdSet);
                    foreach (var n in unmatchedFromChapterPlan)
                        if (!unmatchedLocations.Contains(n)) unmatchedLocations.Add(n);
                }

                var factionNames = chapterBlueprints.SelectMany(b => SplitNames(b.Factions)).ToList();
                var factionIds = MapNamesToIds(factionNames, factionNameToId, factionIdSet);
                var unmatchedFactions = GetUnmatchedNames(factionNames, factionNameToId, factionIdSet);
                if (factionIds.Count == 0 && chapterPlan?.ReferencedFactionNames?.Count > 0)
                {
                    factionIds = MapNamesToIds(chapterPlan.ReferencedFactionNames, factionNameToId, factionIdSet);
                    var unmatchedFromChapterPlan = GetUnmatchedNames(chapterPlan.ReferencedFactionNames, factionNameToId, factionIdSet);
                    foreach (var n in unmatchedFromChapterPlan)
                        if (!unmatchedFactions.Contains(n)) unmatchedFactions.Add(n);
                }

                if (unmatchedCharacters.Count > 0)
                {
                    packageWarnings.Add(new PackageWarning
                    {
                        Level = "Error",
                        Source = $"蓝图章节: {chapterId}",
                        Message = $"出场角色名称未映射为ID: {string.Join("、", unmatchedCharacters)}"
                    });
                }

                if (unmatchedLocations.Count > 0)
                {
                    packageWarnings.Add(new PackageWarning
                    {
                        Level = "Error",
                        Source = $"蓝图章节: {chapterId}",
                        Message = $"涉及地点名称未映射为ID: {string.Join("、", unmatchedLocations)}"
                    });
                }

                if (unmatchedFactions.Count > 0)
                {
                    packageWarnings.Add(new PackageWarning
                    {
                        Level = "Error",
                        Source = $"蓝图章节: {chapterId}",
                        Message = $"涉及势力名称未映射为ID: {string.Join("、", unmatchedFactions)}"
                    });
                }

                guide.Chapters[chapterId] = new ChapterGuideEntry
                {
                    ChapterId = chapterId,
                    Title = chapterInfo.Title ?? "",
                    ChapterGoal = chapterInfo.Synopsis ?? "",
                    ContextIds = new ContextIdCollection
                    {
                        VolumeOutline = volumeContextIds?.VolumeOutline ?? string.Empty,
                        VolumeDesignId = volumeContextIds?.VolumeDesignId ?? string.Empty,
                        ChapterPlanId = chapterInfo.ChapterPlanId ?? string.Empty,
                        ChapterBlueprint = chapterId,
                        BlueprintIds = blueprintIds,
                        Characters = characterIds,
                        Locations = locationIds,
                        Factions = factionIds,
                        PlotRules = MatchPlotRulesByCharacters(chapterId, characterIds, characters, plotRules, plotRulesMainCharsSplit),
                        TemplateIds = templateIds,
                        WorldRuleIds = worldRuleIds,
                        PreviousChapter = previousChapterId ?? string.Empty
                    },
                    Rhythm = chapterInfo.Rhythm ?? new RhythmInfo(),
                    Scenes = chapterBlueprints.Select(b => new SceneGuideEntry
                    {
                        SceneId = b.Id,
                        SceneNumber = b.SceneNumber,
                        Purpose = b.OneLineStructure,
                        Title = b.SceneTitle,
                        PovCharacter = b.PovCharacter,
                        Opening = b.Opening,
                        Development = b.Development,
                        Turning = b.Turning,
                        Ending = b.Ending,
                        InfoDrop = b.InfoDrop,
                        CharacterIds = MapNamesToIds(SplitNames(b.Cast), characterNameToId, characterIdSet),
                        LocationId = MapNamesToIds(SplitNames(b.Locations), locationNameToId, locationIdSet).FirstOrDefault() ?? string.Empty
                    }).ToList()
                };

                previousChapterId = chapterId;
            }

            guide.ReverseIndex = BuildReverseIndex(guide.Chapters);

            foreach (var warning in packageWarnings)
            {
                TM.App.Log($"[GuideIndexBuilder] [{warning.Level}] {warning.Source}: {warning.Message}");
            }

            var errorWarnings = packageWarnings
                .Where(w => string.Equals(w.Level, "Error", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (errorWarnings.Count > 0)
            {
                var summary = string.Join(" | ", errorWarnings.Select(w => $"{w.Source}: {w.Message}"));
                throw new InvalidOperationException($"蓝图名称未映射为ID，打包失败：{summary}");
            }

            TM.App.Log($"[GuideIndexBuilder] 蓝图指导构建完成，共{guide.Chapters.Count}章");
            return guide;
        }

        public async Task<ContentGuide> BuildContentGuideAsync(BlueprintGuide blueprintGuide)
        {
            var guide = new ContentGuide { Module = "ContentGuide" };

            var plotRulesTask = LoadAllAsync<PlotRulesData>("Design/Elements/PlotRules");
            var charactersTask = LoadAllAsync<CharacterRulesData>("Design/Elements/CharacterRules");
            var chaptersTask = LoadAllAsync<Models.Generate.ChapterPlanning.ChapterData>("Generate/Elements/Chapter");
            await Task.WhenAll(plotRulesTask, charactersTask, chaptersTask).ConfigureAwait(false);
            var plotRules = await plotRulesTask.ConfigureAwait(false);
            var characters = await charactersTask.ConfigureAwait(false);
            var chapters = await chaptersTask.ConfigureAwait(false);

            EnsureRequiredIds(plotRules, p => p.Id, "剧情规则", p => p.Name);
            EnsureRequiredCategoryIds(plotRules, p => p.Category, p => p.CategoryId, "剧情规则", p => p.Name);
            EnsureRequiredIds(characters, c => c.Id, "角色规则", c => c.Name);
            EnsureRequiredCategoryIds(characters, c => c.Category, c => c.CategoryId, "角色规则", c => c.Name);
            EnsureRequiredIds(chapters, c => c.Id, "章节规划", c => c.Name);
            EnsureRequiredCategoryIds(chapters, c => c.Category, c => c.CategoryId, "章节规划", c => c.Name);

            var outlineToVolumeNumber = new Dictionary<string, int>();
            foreach (var (chapterId, chapterEntry) in blueprintGuide.Chapters)
            {
                var outlineId = chapterEntry.ContextIds?.VolumeOutline;
                if (!string.IsNullOrEmpty(outlineId) && !outlineToVolumeNumber.ContainsKey(outlineId))
                {
                    if (chapterId.StartsWith("vol", StringComparison.Ordinal) && chapterId.Contains("_ch"))
                    {
                        var volPart = chapterId.Split("_ch")[0].Replace("vol", "");
                        if (int.TryParse(volPart, out var volNum))
                        {
                            outlineToVolumeNumber[outlineId] = volNum;
                        }
                    }
                }
            }

            int ParseVolumeNumberFromString(string? volume)
            {
                if (string.IsNullOrWhiteSpace(volume)) return -1;
                var vIdx = volume.IndexOf('第');
                if (vIdx >= 0)
                {
                    var endIdx = volume.IndexOf('卷', vIdx);
                    if (endIdx > vIdx + 1 && int.TryParse(volume.AsSpan(vIdx + 1, endIdx - vIdx - 1), out var vn))
                        return vn;
                }
                if (volume.StartsWith("vol", StringComparison.OrdinalIgnoreCase))
                {
                    var numPart = volume.Substring(3).TrimStart('_');
                    if (int.TryParse(numPart, out var vn2))
                        return vn2;
                }
                return -1;
            }

            var chapterByVolAndNum = new Dictionary<(int, int), Models.Generate.ChapterPlanning.ChapterData>();
            var chapterByNum = new Dictionary<int, Models.Generate.ChapterPlanning.ChapterData>();
            foreach (var ch in chapters)
            {
                if (ch.ChapterNumber <= 0) continue;
                var vn = ParseVolumeNumberFromString(ch.Volume);
                if (vn > 0)
                    chapterByVolAndNum.TryAdd((vn, ch.ChapterNumber), ch);
                chapterByNum.TryAdd(ch.ChapterNumber, ch);
            }

            Models.Generate.ChapterPlanning.ChapterData? FindChapterData(string chapterId, string? outlineId)
            {
                if (!chapterId.Contains("_ch")) return null;
                var parts = chapterId.Split("_ch");
                var volPart = parts[0].Replace("vol", "");
                var chNumPart = parts.LastOrDefault();
                if (!int.TryParse(volPart, out var volumeNumber)) return null;
                if (!int.TryParse(chNumPart, out var chapterNumber)) return null;

                if (chapterByVolAndNum.TryGetValue((volumeNumber, chapterNumber), out var match))
                    return match;

                if (chapterByNum.TryGetValue(chapterNumber, out var fallback))
                    return fallback;

                return null;
            }

            var contentPlotMainCharsSplit = plotRules.ToDictionary(
                p => p.Id,
                p => (p.MainCharacters ?? string.Empty)
                    .Split(new[] { ',', '，', '、', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

            foreach (var (chapterId, blueprint) in blueprintGuide.Chapters
                .OrderBy(c => ChapterParserHelper.ParseChapterIdOrDefault(c.Key).volumeNumber)
                .ThenBy(c => ChapterParserHelper.ParseChapterIdOrDefault(c.Key).chapterNumber))
            {
                var blueprintContextIds = blueprint.ContextIds ?? new ContextIdCollection();

                var chapterPlotRules = blueprintContextIds.PlotRules ?? new List<string>();

                var chapterCharacterIds = blueprintContextIds.Characters ?? new List<string>();
                var chapterCharacterIdSet = new HashSet<string>(chapterCharacterIds, StringComparer.OrdinalIgnoreCase);
                var chapterCharacterNameSet = new HashSet<string>(
                    characters
                        .Where(c => chapterCharacterIdSet.Contains(c.Id))
                        .Select(c => c.Name),
                    StringComparer.OrdinalIgnoreCase);

                var relatedPlotRules = new HashSet<string>(
                    plotRules
                        .Where(p =>
                        {
                            var sp = p.StoryPhase?.Trim();
                            var isChapterIdPhase = !string.IsNullOrEmpty(sp)
                                && ChapterParserHelper.ParseChapterId(sp).HasValue;
                            if (isChapterIdPhase)
                            {
                                return string.Equals(sp, chapterId, StringComparison.Ordinal);
                            }
                            if (!contentPlotMainCharsSplit.TryGetValue(p.Id, out var chars)) return false;
                            return chars.Any(c => chapterCharacterIdSet.Contains(c) || chapterCharacterNameSet.Contains(c));
                        })
                        .Select(c => c.Id),
                    StringComparer.OrdinalIgnoreCase);

                var chapterConflicts = plotRules
                    .Where(p => !string.IsNullOrEmpty(p.Conflict) && relatedPlotRules.Contains(p.Id))
                    .Select(p => p.Id).ToList();

                var chapterForeshadowingSetups = plotRules
                    .Where(p => relatedPlotRules.Contains(p.Id) && string.IsNullOrEmpty(p.Result))
                    .Select(p => p.Id).ToList();

                var chapterForeshadowingPayoffs = plotRules
                    .Where(p => !string.IsNullOrEmpty(p.Result) && relatedPlotRules.Contains(p.Id))
                    .Select(p => p.Id).ToList();

                var chapterData = FindChapterData(chapterId, blueprintContextIds.VolumeOutline);

                var chapterNumber = chapterData?.ChapterNumber ?? 0;
                if (chapterNumber <= 0)
                {
                    var parsed = ChapterParserHelper.ParseChapterId(chapterId);
                    if (parsed.HasValue && parsed.Value.chapterNumber > 0)
                    {
                        chapterNumber = parsed.Value.chapterNumber;
                    }
                }

                if (chapterNumber <= 0)
                {
                    chapterNumber = ChapterParserHelper.ExtractChapterNumberFromSuffix(chapterId);
                }

                if (chapterNumber <= 0 && !string.IsNullOrWhiteSpace(blueprint.Title))
                {
                    var (number, _) = ChapterParserHelper.ExtractChapterParts(blueprint.Title);
                    if (number.HasValue && number.Value > 0)
                    {
                        chapterNumber = number.Value;
                    }
                    else
                    {
                        var (_, fromTitle) = ChapterParserHelper.ParseFromNaturalLanguage(blueprint.Title);
                        if (fromTitle.HasValue && fromTitle.Value > 0)
                        {
                            chapterNumber = fromTitle.Value;
                        }
                    }
                }

                var chapterTitle = !string.IsNullOrWhiteSpace(blueprint.Title)
                    ? blueprint.Title
                    : chapterData?.ChapterTitle ?? string.Empty;

                guide.Chapters[chapterId] = new ContentGuideEntry
                {
                    ChapterId = chapterId,
                    Title = chapterTitle,
                    Summary = blueprint.ChapterGoal,
                    ContextIds = new ContextIdCollection
                    {
                        VolumeOutline = blueprintContextIds.VolumeOutline,
                        VolumeDesignId = blueprintContextIds.VolumeDesignId,
                        ChapterPlanId = blueprintContextIds.ChapterPlanId,
                        ChapterBlueprint = chapterId,
                        BlueprintIds = blueprintContextIds.BlueprintIds ?? new List<string>(),
                        Characters = blueprintContextIds.Characters ?? new List<string>(),
                        Locations = blueprintContextIds.Locations ?? new List<string>(),
                        Factions = blueprintContextIds.Factions ?? new List<string>(),
                        PlotRules = chapterPlotRules,
                        TemplateIds = blueprintContextIds.TemplateIds ?? new List<string>(),
                        WorldRuleIds = blueprintContextIds.WorldRuleIds ?? new List<string>(),
                        Conflicts = chapterConflicts,
                        ForeshadowingSetups = chapterForeshadowingSetups,
                        ForeshadowingPayoffs = chapterForeshadowingPayoffs,
                        PreviousChapter = blueprintContextIds.PreviousChapter
                    },
                    Rhythm = blueprint.Rhythm,
                    Scenes = blueprint.Scenes,
                    ChapterNumber = chapterNumber,
                    Volume = chapterData?.Volume ?? string.Empty,
                    ChapterTheme = chapterData?.ChapterTheme ?? string.Empty,
                    MainGoal = chapterData?.MainGoal ?? string.Empty,
                    KeyTurn = chapterData?.KeyTurn ?? string.Empty,
                    Hook = chapterData?.Hook ?? string.Empty,
                    WorldInfoDrop = chapterData?.WorldInfoDrop ?? string.Empty,
                    CharacterArcProgress = chapterData?.CharacterArcProgress ?? string.Empty,
                    Foreshadowing = chapterData?.Foreshadowing ?? string.Empty
                };
            }

            TM.App.Log($"[GuideIndexBuilder] 正文指导构建完成，共{guide.Chapters.Count}章");
            return guide;
        }

        #endregion
    }
}
