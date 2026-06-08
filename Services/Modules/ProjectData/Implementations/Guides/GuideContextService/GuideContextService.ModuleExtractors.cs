using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.TaskContexts;
using TM.Services.Modules.ProjectData.Models.Tracking;
using TM.Services.Modules.ProjectData.Implementations.Indexing;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class GuideContextService
    {
        #region ModuleExtractors

        public async Task<OutlineTaskContext?> BuildOutlineContextAsync(string volumeId)
        {
            var guide = await LoadGuideAsync<OutlineGuide>("outline_guide.json").ConfigureAwait(false);
            var volumeGuide = guide.Volumes.GetValueOrDefault(volumeId);

            if (volumeGuide == null) return null;

            var characterTask = ExtractCharactersAsync(volumeGuide.ContextIds.Characters);
            var locationTask = ExtractLocationsAsync(volumeGuide.ContextIds.Locations);
            var factionTask = ExtractFactionsAsync(volumeGuide.ContextIds.Factions ?? new List<string>());
            var plotRuleTask = ExtractPlotRulesAsync(volumeGuide.ContextIds.PlotRules ?? new List<string>());
            var previousOutlineTask = ExtractPreviousOutlinesAsync(volumeGuide.ContextIds.PreviousOutlines ?? new List<string>());

            await Task.WhenAll(characterTask, locationTask, factionTask, plotRuleTask, previousOutlineTask).ConfigureAwait(false);

            var characterItems = characterTask.Result;
            var locationItems = locationTask.Result;
            var factionItems = factionTask.Result;
            var plotRuleItems = plotRuleTask.Result;
            var previousOutlineItems = previousOutlineTask.Result;

            var context = new OutlineTaskContext
            {
                VolumeId = volumeId,
                VolumeNumber = volumeGuide.VolumeNumber,
                Theme = volumeGuide.Theme
            };

            foreach (var c in characterItems)
            {
                context.Characters.Add(c);
            }

            foreach (var l in locationItems)
            {
                context.Locations.Add(l);
            }

            foreach (var f in factionItems)
            {
                context.Factions.Add(f);
            }

            foreach (var p in plotRuleItems)
            {
                context.PlotRules.Add(p);
            }

            foreach (var o in previousOutlineItems)
            {
                context.PreviousOutlines.Add(o);
            }

            return context;
        }

        public async Task<string?> GetChapterTitleAsync(string chapterId)
        {
            try
            {
                var guide = await GetContentGuideAsync().ConfigureAwait(false);
                if (guide?.Chapters != null && guide.Chapters.TryGetValue(chapterId, out var entry))
                {
                    return ResolveChapterDisplayTitle(entry, chapterId);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] GetChapterTitleAsync失败: {ex.Message}");
            }
            return null;
        }

        private static string ResolveChapterDisplayTitle(ContentGuideEntry chapterGuide, string chapterId)
        {
            var title = chapterGuide?.Title;
            if (string.IsNullOrWhiteSpace(title))
            {
                title = chapterGuide?.Scenes?.FirstOrDefault()?.Title;
            }
            if (string.IsNullOrWhiteSpace(title))
            {
                title = chapterId;
            }
            return title;
        }

        public async Task<PlanningTaskContext?> BuildPlanningContextAsync(string volumeId)
        {
            var guide = await LoadGuideAsync<PlanningGuide>("planning_guide.json").ConfigureAwait(false);
            var volumeGuide = guide.Volumes.GetValueOrDefault(volumeId);

            if (volumeGuide == null) return null;

            var volumeItem = await ExtractVolumeAsync(volumeGuide.ContextIds.VolumeOutline).ConfigureAwait(false);
            var characterItems = await ExtractCharactersAsync(volumeGuide.ContextIds.Characters).ConfigureAwait(false);
            var plotRuleItems = await ExtractPlotRulesAsync(volumeGuide.ContextIds.PlotRules ?? new List<string>()).ConfigureAwait(false);

            var context = new PlanningTaskContext
            {
                VolumeId = volumeId,
                ChapterPlans = volumeGuide.Chapters
            };

            var volumeData = volumeItem;

            context.VolumeOutline = volumeData;

            foreach (var c in characterItems)
            {
                context.Characters.Add(c);
            }

            foreach (var p in plotRuleItems)
            {
                context.PlotRules.Add(p);
            }

            return context;
        }

        public async Task<BlueprintTaskContext?> BuildBlueprintContextAsync(string chapterId)
        {
            var guide = await LoadGuideAsync<BlueprintGuide>("blueprint_guide.json").ConfigureAwait(false);
            var chapterGuide = guide.Chapters.GetValueOrDefault(chapterId);

            if (chapterGuide == null) return null;

            var volumeTask = ExtractVolumeAsync(chapterGuide.ContextIds.VolumeOutline);
            var characterTask = ExtractCharactersAsync(chapterGuide.ContextIds.Characters);
            var locationTask = ExtractLocationsAsync(chapterGuide.ContextIds.Locations);
            var factionTask = ExtractFactionsAsync(chapterGuide.ContextIds.Factions ?? new List<string>());
            var plotRuleTask = ExtractPlotRulesAsync(chapterGuide.ContextIds.PlotRules ?? new List<string>());
            var previousChapterTask = GetChapterSummaryAsync(chapterGuide.ContextIds.PreviousChapter);

            await Task.WhenAll(volumeTask, characterTask, locationTask, factionTask, plotRuleTask, previousChapterTask).ConfigureAwait(false);

            var volumeItem = volumeTask.Result;
            var characterItems = characterTask.Result;
            var locationItems = locationTask.Result;
            var factionItems = factionTask.Result;
            var plotRuleItems = plotRuleTask.Result;

            var context = new BlueprintTaskContext
            {
                ChapterId = chapterId,
                Title = chapterGuide.Title,
                ChapterGoal = chapterGuide.ChapterGoal,
                PreviousChapterSummary = previousChapterTask.Result,
                Rhythm = chapterGuide.Rhythm
            };

            var volumeData = volumeItem;

            context.VolumeOutline = volumeData;

            foreach (var c in characterItems)
            {
                context.Characters.Add(c);
            }

            foreach (var l in locationItems)
            {
                context.Locations.Add(l);
            }

            foreach (var f in factionItems)
            {
                context.Factions.Add(f);
            }

            foreach (var p in plotRuleItems)
            {
                context.PlotRules.Add(p);
            }

            return context;
        }

        public async Task<ContentTaskContext?> BuildContentContextAsync(string chapterId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var cfg = LayeredContextConfig.TakeSnapshot();
            TM.App.Log($"[GuideContextService] BuildContentContextAsync: chapterId={chapterId}");
            var guide = await GetContentGuideAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            ContentGuideEntry? chapterGuide = null;
            bool hasPackage = guide?.Chapters?.TryGetValue(chapterId, out chapterGuide) == true && chapterGuide != null;
            TM.App.Log($"[GuideContextService] BuildContentContextAsync: chapterId={chapterId}, hasPackage={hasPackage}");

            if (!hasPackage)
            {
                TM.App.Log($"[GuideContextService] 章节 {chapterId} 缺少打包数据");
                return null;
            }

            var prevChapterId = chapterGuide!.ContextIds.PreviousChapter;
            bool hasPrevMd = !string.IsNullOrEmpty(prevChapterId) && HasChapterMd(prevChapterId);

            ct.ThrowIfCancellationRequested();

            if (hasPrevMd)
            {
                TM.App.Log($"[GuideContextService] BuildContentContextAsync: chapterId={chapterId}, mode=M1, prev={prevChapterId}");
                return await BuildFullContextAsync(chapterId, chapterGuide!, guide!, prevChapterId, cfg).ConfigureAwait(false);
            }

            TM.App.Log($"[GuideContextService] 章节 {chapterId} 缺少MD上下文，使用备用模式");
            TM.App.Log($"[GuideContextService] BuildContentContextAsync: chapterId={chapterId}, mode=M2, prev={prevChapterId}");
            return await BuildPackageOnlyContextAsync(chapterId, chapterGuide!, guide!, cfg).ConfigureAwait(false);
        }

        private async Task<ContentTaskContext> BuildFullContextAsync(
            string chapterId,
            ContentGuideEntry chapterGuide,
            ContentGuide guide,
            string prevChapterId,
            LayeredContextConfigSnapshot cfg)
        {
            var currentVol = ChapterParserHelper.ParseChapterId(chapterId)?.volumeNumber ?? 1;

            var taskLayerTask = LoadTaskLayerAsync(chapterId, chapterGuide, guide);
            var storeSummariesTask = _summaryStore.GetPreviousSummariesAsync(chapterId, cfg.PreviousSummaryCount);
            var tailTask = LoadChapterTailAsync(prevChapterId, LayeredContextConfig.PreviousChapterTailLength);
            var factSnapshotTask = ExtractFactSnapshotAsync(chapterId, chapterGuide.ContextIds);
            var milestonesTask = _milestoneStore.GetPreviousMilestonesAsync(currentVol);
            var archivesTask = currentVol > 1
                ? ServiceLocator.Get<VolumeFactArchiveStore>().GetPreviousArchivesAsync(currentVol)
                : Task.FromResult(new List<VolumeFactArchive>());

            var storeSummaries = await storeSummariesTask.ConfigureAwait(false);
            var previousSummaries = LoadPreviousChapterSummaries(
                chapterId,
                storeSummaries,
                cfg.PreviousSummaryCount,
                cfg);

            var mdSummariesTask = previousSummaries.Count == 0
                ? ExtractSummariesFromMdAsync(chapterId, cfg.PreviousSummaryCount, cfg)
                : Task.FromResult(new List<ChapterSummaryEntry>());

            await Task.WhenAll(taskLayerTask, mdSummariesTask, tailTask, factSnapshotTask, milestonesTask, archivesTask).ConfigureAwait(false);

            var context = await taskLayerTask.ConfigureAwait(false);
            context.ContextMode = ContentContextMode.Full;
            context.ContextIds = chapterGuide.ContextIds;

            context.PreviousChapterSummaries = previousSummaries;

            context.MdPreviousChapterSummaries = await mdSummariesTask.ConfigureAwait(false);

            context.PreviousChapterId = prevChapterId;
            context.PreviousChapterTail = await tailTask.ConfigureAwait(false);

            context.FactSnapshot = await factSnapshotTask.ConfigureAwait(false);

            context.HistoricalMilestones = await milestonesTask.ConfigureAwait(false);
            if (context.HistoricalMilestones.Count > 0)
                TM.App.Log($"[GuideContextService] 注入里程碑: {context.HistoricalMilestones.Count}卷");

            if (currentVol > 1)
            {
                context.PreviousVolumeArchives = BuildInjectableArchives(await archivesTask.ConfigureAwait(false), chapterGuide.ContextIds, cfg);
                if (context.PreviousVolumeArchives.Count > 0)
                    TM.App.Log($"[GuideContextService] 注入前卷事实存档: {context.PreviousVolumeArchives.Count}卷");
            }

            var divergenceTask = DetectStateDivergenceAsync(context);
            var trackingTask = DetectTrackingGapsAsync(context, chapterId);
            var volumeEndTask = ValidateVolumeEndChapterAsync(context, chapterId);
            var keySceneTask = TryExpandForKeySceneAsync(context, chapterGuide);

            await Task.WhenAll(divergenceTask, trackingTask, volumeEndTask, keySceneTask).ConfigureAwait(false);

            context.StateDivergenceWarnings.AddRange(await divergenceTask.ConfigureAwait(false));
            context.StateDivergenceWarnings.AddRange(await trackingTask.ConfigureAwait(false));
            context.StateDivergenceWarnings.AddRange(await volumeEndTask.ConfigureAwait(false));

            await PopulateFirstDescriptionSnippetsAsync(context, cfg).ConfigureAwait(false);

            return context;
        }

        private async Task<ContentTaskContext> BuildPackageOnlyContextAsync(
            string chapterId,
            ContentGuideEntry chapterGuide,
            ContentGuide guide,
            LayeredContextConfigSnapshot cfg)
        {
            var currentVolPO = ChapterParserHelper.ParseChapterId(chapterId)?.volumeNumber ?? 1;

            var taskLayerPOTask = LoadTaskLayerAsync(chapterId, chapterGuide, guide);
            var storeSummariesPOTask = _summaryStore.GetPreviousSummariesAsync(chapterId, cfg.PreviousSummaryCount);
            var factSnapshotPOTask = ExtractFactSnapshotAsync(chapterId, chapterGuide.ContextIds);
            var milestonesPOTask = _milestoneStore.GetPreviousMilestonesAsync(currentVolPO);
            var archivesPOTask = currentVolPO > 1
                ? ServiceLocator.Get<VolumeFactArchiveStore>().GetPreviousArchivesAsync(currentVolPO)
                : Task.FromResult(new List<VolumeFactArchive>());

            await Task.WhenAll(taskLayerPOTask, storeSummariesPOTask, factSnapshotPOTask, milestonesPOTask, archivesPOTask).ConfigureAwait(false);

            var context = await taskLayerPOTask.ConfigureAwait(false);
            context.ContextMode = ContentContextMode.PackageOnly;
            context.ContextIds = chapterGuide.ContextIds;

            context.PreviousChapterSummaries = LoadPreviousChapterSummaries(
                chapterId,
                await storeSummariesPOTask.ConfigureAwait(false),
                cfg.PreviousSummaryCount,
                cfg);

            context.FactSnapshot = await factSnapshotPOTask.ConfigureAwait(false);

            context.HistoricalMilestones = await milestonesPOTask.ConfigureAwait(false);
            if (context.HistoricalMilestones.Count > 0)
                TM.App.Log($"[GuideContextService] 注入里程碑(PO): {context.HistoricalMilestones.Count}卷");

            if (currentVolPO > 1)
            {
                context.PreviousVolumeArchives = BuildInjectableArchives(await archivesPOTask.ConfigureAwait(false), chapterGuide.ContextIds, cfg);
                if (context.PreviousVolumeArchives.Count > 0)
                    TM.App.Log($"[GuideContextService] 注入前卷事实存档(PO): {context.PreviousVolumeArchives.Count}卷");
            }

            var divergencePOTask = DetectStateDivergenceAsync(context);
            var trackingPOTask = DetectTrackingGapsAsync(context, chapterId);
            var volumeEndPOTask = ValidateVolumeEndChapterAsync(context, chapterId);
            var keyScenePOTask = TryExpandForKeySceneAsync(context, chapterGuide);

            await Task.WhenAll(divergencePOTask, trackingPOTask, volumeEndPOTask, keyScenePOTask).ConfigureAwait(false);

            context.StateDivergenceWarnings.AddRange(await divergencePOTask.ConfigureAwait(false));
            context.StateDivergenceWarnings.AddRange(await trackingPOTask.ConfigureAwait(false));
            context.StateDivergenceWarnings.AddRange(await volumeEndPOTask.ConfigureAwait(false));

            await PopulateFirstDescriptionSnippetsAsync(context, cfg).ConfigureAwait(false);

            return context;
        }

        private async Task PopulateFirstDescriptionSnippetsAsync(ContentTaskContext context, LayeredContextConfigSnapshot cfg)
        {
            if (!cfg.SemanticRecallEnabled) return;
            if (context?.Characters == null || context.Characters.Count == 0) return;

            try
            {
                var firstIdx = ServiceLocator.Get<EntityFirstChapterIndex>();
                await firstIdx.LoadAsync().ConfigureAwait(false);
                if (firstIdx.Count == 0) return;

                var chunkSearch = ServiceLocator.Get<ContentChunkSearchService>();
                int window = Math.Max(1, cfg.FirstDescriptionWindowSize);
                var snippets = new List<FirstDescriptionSnippet>(context.Characters.Count);

                foreach (var c in context.Characters)
                {
                    if (string.IsNullOrEmpty(c?.Id)) continue;
                    var entry = await firstIdx.GetAsync(c.Id).ConfigureAwait(false);
                    if (entry == null) continue;
                    if (string.Equals(entry.ChapterId, context.ChapterId, StringComparison.OrdinalIgnoreCase)) continue;

                    var hits = await chunkSearch.SearchByChapterPositionAsync(entry.ChapterId, entry.ChunkPosition, window).ConfigureAwait(false);
                    if (hits.Count == 0) continue;
                    var content = string.Join("\n", hits.Select(h => h.Content));
                    if (string.IsNullOrWhiteSpace(content)) continue;
                    snippets.Add(new FirstDescriptionSnippet(c.Id, c.Name ?? string.Empty, entry.ChapterId, content));
                }

                context.FirstDescriptionSnippets = snippets;
                if (snippets.Count > 0)
                    TM.App.Log($"[GuideContextService] 注入首次描写: {snippets.Count} 条");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 首次描写注入失败（非致命）: {ex.Message}");
            }
        }

        private async Task<ContentTaskContext> LoadTaskLayerAsync(
            string chapterId,
            ContentGuideEntry chapterGuide,
            ContentGuide guide)
        {
            var characterTask = ExtractCharactersAsync(chapterGuide.ContextIds.Characters);
            var locationTask = ExtractLocationsAsync(chapterGuide.ContextIds.Locations);
            var factionTask = ExtractFactionsAsync(chapterGuide.ContextIds.Factions ?? new List<string>());
            var plotRuleTask = ExtractPlotRulesAsync(chapterGuide.ContextIds.PlotRules ?? new List<string>());
            var volumeTask = ExtractVolumeAsync(chapterGuide.ContextIds.VolumeOutline);
            var worldRuleTask = ExtractWorldRulesAsync(chapterGuide.ContextIds.WorldRuleIds ?? new List<string>());
            var templateTask = ExtractTemplatesAsync(chapterGuide.ContextIds.TemplateIds);
            var chapterPlanTask = ExtractChapterPlanAsync(chapterGuide.ContextIds.ChapterPlanId);
            var blueprintTask = ExtractBlueprintsAsync(chapterGuide.ContextIds.BlueprintIds ?? new List<string>());
            var volumeDesignTask = ExtractVolumeDesignAsync(chapterGuide.ContextIds.VolumeDesignId);
            var prevSummaryTask = !string.IsNullOrEmpty(chapterGuide.ContextIds.PreviousChapter)
                ? _summaryStore.GetSummaryAsync(chapterGuide.ContextIds.PreviousChapter)
                : Task.FromResult(string.Empty);

            await Task.WhenAll(
                characterTask, locationTask, factionTask, plotRuleTask,
                volumeTask, worldRuleTask, templateTask, chapterPlanTask, blueprintTask, volumeDesignTask,
                prevSummaryTask).ConfigureAwait(false);

            var characterItems = await characterTask.ConfigureAwait(false);
            var locationItems = await locationTask.ConfigureAwait(false);
            var factionItems = await factionTask.ConfigureAwait(false);
            var plotRuleItems = await plotRuleTask.ConfigureAwait(false);
            var volumeItem = await volumeTask.ConfigureAwait(false);
            var worldRuleItems = await worldRuleTask.ConfigureAwait(false);
            var templateItems = await templateTask.ConfigureAwait(false);
            var chapterPlanItem = await chapterPlanTask.ConfigureAwait(false);
            var blueprintItems = await blueprintTask.ConfigureAwait(false);
            var volumeDesignItem = await volumeDesignTask.ConfigureAwait(false);

            var context = new ContentTaskContext
            {
                ChapterId = chapterId,
                Title = ResolveChapterDisplayTitle(chapterGuide, chapterId),
                Summary = chapterGuide.Summary,
                VolumeOutline = volumeItem,
                ChapterPlan = chapterPlanItem,
                VolumeDesign = volumeDesignItem,
                Rhythm = chapterGuide.Rhythm,
                Scenes = chapterGuide.Scenes,
                PreviousChapterSummary = await prevSummaryTask.ConfigureAwait(false)
            };

            foreach (var c in characterItems) context.Characters.Add(c);
            foreach (var l in locationItems) context.Locations.Add(l);
            foreach (var f in factionItems) context.Factions.Add(f);
            foreach (var p in plotRuleItems) context.PlotRules.Add(p);
            foreach (var w in worldRuleItems) context.WorldRules.Add(w);
            foreach (var t in templateItems) context.Templates.Add(t);
            foreach (var b in blueprintItems) context.Blueprints.Add(b);

            return context;
        }

        private List<ChapterSummaryEntry> LoadPreviousChapterSummaries(
            string currentChapterId,
            Dictionary<string, string> allSummaries,
            int count,
            LayeredContextConfigSnapshot cfg)
        {
            var result = new List<ChapterSummaryEntry>();

            if (allSummaries == null || allSummaries.Count == 0)
                return result;

            var currentParsed = ChapterParserHelper.ParseChapterId(currentChapterId);
            var currentVol = currentParsed?.volumeNumber ?? 1;

            var previousAll = allSummaries
                .Where(kv => ChapterParserHelper.CompareChapterId(kv.Key, currentChapterId) < 0)
                .ToList();

            var recentCount = cfg.SummaryRecentWindowCount;
            var recentKeys = previousAll
                .OrderByDescending(kv => kv.Key, Comparer<string>.Create(ChapterParserHelper.CompareChapterId))
                .Take(recentCount)
                .Select(kv => kv.Key)
                .ToHashSet();

            var milestoneAnchorInterval = cfg.MilestoneAnchorInterval;
            var milestoneKeys = new HashSet<string>();
            var volumeStartKeys = new HashSet<string>();
            var midpointKeys = new HashSet<string>();
            foreach (var volGroup in previousAll
                .GroupBy(kv => ChapterParserHelper.ParseChapterId(kv.Key)?.volumeNumber ?? 0)
                .Where(g => g.Key > 0 && g.Key < currentVol))
            {
                var sortedChapters = volGroup
                    .OrderBy(kv => ChapterParserHelper.ParseChapterId(kv.Key)?.chapterNumber ?? 0)
                    .ToList();
                milestoneKeys.Add(sortedChapters.Last().Key);
                if (sortedChapters.Count <= 1) continue;
                volumeStartKeys.Add(sortedChapters[0].Key);
                for (int i = milestoneAnchorInterval; i < sortedChapters.Count - 1; i += milestoneAnchorInterval)
                    midpointKeys.Add(sortedChapters[i].Key);
            }

            var allAnchorKeys = new HashSet<string>(milestoneKeys.Concat(volumeStartKeys).Concat(midpointKeys));
            var maxAnchors = cfg.SummaryMaxCrossVolumeAnchors;
            if (maxAnchors > 0 && allAnchorKeys.Count > maxAnchors)
            {
                allAnchorKeys = new HashSet<string>(
                    allAnchorKeys
                        .OrderByDescending(k => k, Comparer<string>.Create(ChapterParserHelper.CompareChapterId))
                        .Take(maxAnchors));
            }
            var selectedKeys = recentKeys.Union(allAnchorKeys).ToList();
            var selectedSummaries = previousAll
                .Where(kv => selectedKeys.Contains(kv.Key))
                .OrderBy(kv => kv.Key, Comparer<string>.Create(ChapterParserHelper.CompareChapterId))
                .Take(Math.Max(count, recentCount + allAnchorKeys.Count))
                .ToList();

            foreach (var kv in selectedSummaries)
            {
                var parsed = ChapterParserHelper.ParseChapterId(kv.Key);
                var chapterNumber = parsed?.chapterNumber ?? 0;
                var isEndMilestone = milestoneKeys.Contains(kv.Key) && !recentKeys.Contains(kv.Key);
                var isVolumeStart = volumeStartKeys.Contains(kv.Key) && !recentKeys.Contains(kv.Key);
                var isMidpoint = midpointKeys.Contains(kv.Key) && !recentKeys.Contains(kv.Key);
                var prefix = isEndMilestone ? $"[{parsed?.volumeNumber}卷收尾] "
                           : isVolumeStart ? $"[{parsed?.volumeNumber}卷起始] "
                           : isMidpoint ? $"[{parsed?.volumeNumber}卷中段] "
                           : string.Empty;
                result.Add(new ChapterSummaryEntry
                {
                    ChapterId = kv.Key,
                    ChapterNumber = chapterNumber,
                    Summary = prefix + kv.Value
                });
            }

            return result;
        }

        private async Task<List<ChapterSummaryEntry>> ExtractSummariesFromMdAsync(
            string currentChapterId, int count, LayeredContextConfigSnapshot cfg)
        {
            var result = new List<ChapterSummaryEntry>();

            try
            {
                var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
                if (!Directory.Exists(chaptersPath))
                    return result;

                IEnumerable<string> allChapterIds;
                var guide = await GetContentGuideAsync().ConfigureAwait(false);
                if (guide?.Chapters != null && guide.Chapters.Count > 0)
                {
                    allChapterIds = guide.Chapters.Keys;
                }
                else
                {
                    allChapterIds = Directory.GetFiles(chaptersPath, "vol*_ch*.md")
                        .Select(f => Path.GetFileNameWithoutExtension(f));
                }

                var previousChapterFiles = allChapterIds
                    .Where(id => ChapterParserHelper.CompareChapterId(id, currentChapterId) < 0)
                    .OrderByDescending(id => id, Comparer<string>.Create(ChapterParserHelper.CompareChapterId))
                    .Take(count)
                    .Reverse()
                    .ToList();

                foreach (var chapId in previousChapterFiles)
                {
                    var mdPath = Path.Combine(chaptersPath, $"{chapId}.md");
                    if (!File.Exists(mdPath)) continue;

                    var fullContent = await ReadFileHeadAsync(mdPath, 8000).ConfigureAwait(false);
                    var summary = ExtractSampledSummaryFromMd(fullContent, cfg.MdSummaryExtractLength);
                    var parsed = ChapterParserHelper.ParseChapterId(chapId);
                    result.Add(new ChapterSummaryEntry
                    {
                        ChapterId = chapId,
                        ChapterNumber = parsed?.chapterNumber ?? 0,
                        Summary = summary
                    });
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 从MD提取摘要失败: {ex.Message}");
            }

            return result;
        }

        private string ExtractSampledSummaryFromMd(string rawContent, int maxLength)
        {
            var lines = rawContent.Split('\n');
            var bodyStart = 0;
            for (int i = 0; i < Math.Min(3, lines.Length); i++)
            {
                if (lines[i].TrimStart().StartsWith('#'))
                {
                    bodyStart = i + 1;
                    break;
                }
            }
            var body = string.Join(' ', lines.Skip(bodyStart))
                .Replace("\r", "").Replace("\n", " ").Trim();

            if (body.Length <= maxLength) return body;

            var headLen = maxLength * 2 / 5;
            var midLen = maxLength * 3 / 10;
            var tailLen = maxLength - headLen - midLen;

            var head = body.Substring(0, headLen);
            var midStart = Math.Max(headLen, body.Length / 2 - midLen / 2);
            var mid = body.Substring(midStart, Math.Min(midLen, body.Length - midStart));
            var tail = body.Length > tailLen ? body.Substring(body.Length - tailLen) : string.Empty;

            var sb = new System.Text.StringBuilder();
            sb.Append(head).Append("……");
            if (!string.IsNullOrWhiteSpace(mid)) sb.Append("[中段]").Append(mid).Append("……");
            if (!string.IsNullOrWhiteSpace(tail)) sb.Append("[末段]").Append(tail);
            return sb.ToString();
        }

        private async Task<string> LoadChapterTailAsync(string chapterId, int tailLength)
        {
            var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
            var chapterFile = Path.Combine(chaptersPath, $"{chapterId}.md");

            if (!File.Exists(chapterFile)) return string.Empty;

            try
            {
                var tail = await ReadFileTailAsync(chapterFile, tailLength).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(tail))
                    return string.Empty;

                if (tail.Length <= tailLength)
                    return tail.Trim();

                var startIndex = tail.Length - tailLength;
                var paragraphStart = tail.IndexOf("\n\n", startIndex, StringComparison.Ordinal);
                if (paragraphStart > startIndex && paragraphStart < tail.Length - 100)
                {
                    startIndex = paragraphStart + 2;
                }

                return tail.Substring(startIndex).Trim();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 加载章节尾部失败 [{chapterId}]: {ex.Message}");
                return string.Empty;
            }
        }

        private static async Task<string> ReadFileHeadAsync(string filePath, int expectedLength)
        {
            var bytesToRead = Math.Max(4096, Math.Min(65536, expectedLength * 8 + 2048));

            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);

            if (stream.Length <= bytesToRead)
            {
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
                return await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            var buffer = ArrayPool<byte>.Shared.Rent(bytesToRead);
            try
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, bytesToRead)).ConfigureAwait(false);
                if (read <= 0) return string.Empty;
                return Encoding.UTF8.GetString(buffer, 0, read);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private static async Task<string> ReadFileTailAsync(string filePath, int expectedLength)
        {
            var bytesToRead = Math.Max(4096, Math.Min(131072, expectedLength * 8 + 4096));

            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);

            if (stream.Length <= bytesToRead)
            {
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
                var full = await reader.ReadToEndAsync().ConfigureAwait(false);

                var lines = full.Split('\n');
                var bodyStart = 0;
                for (int i = 0; i < Math.Min(3, lines.Length); i++)
                {
                    if (lines[i].TrimStart().StartsWith('#'))
                    {
                        bodyStart = i + 1;
                        break;
                    }
                }
                var body = string.Join('\n', lines.Skip(bodyStart));
                return body.Trim();
            }

            var start = Math.Max(0, stream.Length - bytesToRead - 4);
            stream.Seek(start, SeekOrigin.Begin);

            var remaining = stream.Length - start;
            var bufferSize = remaining > int.MaxValue ? int.MaxValue : (int)remaining;
            var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            try
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, bufferSize)).ConfigureAwait(false);
                if (read <= 0) return string.Empty;
                return Encoding.UTF8.GetString(buffer, 0, read);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private bool HasChapterMd(string chapterId)
        {
            try
            {
                var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
                var chapterFile = Path.Combine(chaptersPath, $"{chapterId}.md");
                return File.Exists(chapterFile);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 检查章节MD失败: {ex.Message}");
                return false;
            }
        }

        private string BuildChapterId(string templateChapterId, int chapterNumber)
        {
            return ChapterParserHelper.ReplaceChapterNumber(templateChapterId, chapterNumber);
        }

        #endregion
    }
}
