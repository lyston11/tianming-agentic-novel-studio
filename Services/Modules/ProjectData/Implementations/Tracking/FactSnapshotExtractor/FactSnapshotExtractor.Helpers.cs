using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class FactSnapshotExtractor
    {
        #region 私有方法 - 辅助

        private string GetPreviousChapterId(string chapterId)
        {
            var parsed = ChapterParserHelper.ParseChapterId(chapterId);
            if (parsed == null)
                return string.Empty;

            var (vol, ch) = parsed.Value;

            if (ch > 1)
            {
                return ChapterParserHelper.BuildChapterId(vol, ch - 1);
            }
            else if (vol > 1)
            {
                var lastChapterOfPrevVolume = GetLastChapterOfVolume(vol - 1);
                if (lastChapterOfPrevVolume > 0)
                {
                    return ChapterParserHelper.BuildChapterId(vol - 1, lastChapterOfPrevVolume);
                }
                TM.App.Log($"[FactSnapshotExtractor] 无法确定卷{vol - 1}的最后一章，跳过跨卷状态抽取");
                return string.Empty;
            }

            return string.Empty;
        }

        private int GetLastChapterOfVolume(int volumeNumber)
        {
            try
            {
                var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
                if (!System.IO.Directory.Exists(chaptersPath))
                    return 0;

                var volumePrefix = $"vol{volumeNumber}_ch";
                var chapterFiles = System.IO.Directory.GetFiles(chaptersPath, $"vol{volumeNumber}_ch*.md");

                if (chapterFiles.Length == 0)
                    return 0;

                var maxChapter = chapterFiles
                    .Select(f => System.IO.Path.GetFileNameWithoutExtension(f))
                    .Select(name => ChapterParserHelper.ParseChapterId(name))
                    .Where(p => p != null)
                    .Select(p => p!.Value.chapterNumber)
                    .DefaultIfEmpty(0)
                    .Max();

                return maxChapter;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactSnapshotExtractor] 查询卷{volumeNumber}最后一章失败: {ex.Message}");
                return 0;
            }
        }

        private async Task<List<LocationStateSnapshot>> ExtractLocationStatesAsync(
            List<string>? locationIds, LayeredContextConfigSnapshot cfg, bool allVolumes = false, string? prevChapterId = null)
        {
            var result = new List<LocationStateSnapshot>();
            try
            {
                var guide = await AggregateLocationStateGuideAsync(allVolumes).ConfigureAwait(false);

                if (locationIds == null || locationIds.Count > 0)
                {
                    var filterIds = (locationIds != null && locationIds.Count > 0)
                        ? new HashSet<string>(locationIds, StringComparer.OrdinalIgnoreCase)
                        : null;
                    foreach (var (id, entry) in guide.Locations)
                    {
                        if (filterIds != null && !filterIds.Contains(id)) continue;
                        var lastState = entry.StateHistory.LastOrDefault();
                        result.Add(new LocationStateSnapshot
                        {
                            Id = id,
                            Name = entry.Name,
                            Status = entry.CurrentStatus,
                            ChapterId = lastState?.Chapter ?? string.Empty
                        });
                    }
                }

                if (!string.IsNullOrEmpty(prevChapterId) && !allVolumes)
                {
                    var existingIds = new HashSet<string>(result.Select(r => r.Id), StringComparer.OrdinalIgnoreCase);
                    var chaptersPerVol = GetChaptersPerVol();
                    var activeSnapshots = new List<LocationStateSnapshot>();

                    foreach (var (id, entry) in guide.Locations)
                    {
                        if (existingIds.Contains(id)) continue;
                        if (entry.StateHistory == null || entry.StateHistory.Count == 0) continue;
                        var lastState = entry.StateHistory.LastOrDefault();
                        if (lastState == null || string.IsNullOrEmpty(lastState.Chapter)) continue;
                        if (!IsActiveInRecentChapters(lastState.Chapter, prevChapterId, cfg.ActiveEntityWindowChapters, chaptersPerVol))
                            continue;
                        activeSnapshots.Add(new LocationStateSnapshot
                        {
                            Id = id,
                            Name = entry.Name,
                            Status = entry.CurrentStatus,
                            ChapterId = lastState.Chapter
                        });
                    }

                    var comparer = Comparer<string>.Create(ChapterParserHelper.CompareChapterId);
                    var injected = activeSnapshots
                        .OrderByDescending(s => s.ChapterId, comparer)
                        .Take(cfg.ActiveEntityWindowMaxCount)
                        .ToList();
                    result.AddRange(injected);
                    if (injected.Count > 0)
                        TM.App.Log($"[FactSnapshotExtractor] 注入近期活跃地点: {injected.Count}条");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactSnapshotExtractor] 抽取地点状态失败: {ex.Message}");
            }
            return result;
        }

        private async Task<List<FactionStateSnapshot>> ExtractFactionStatesAsync(
            LayeredContextConfigSnapshot cfg, bool applyLimit = false, bool allVolumes = false, List<string>? priorityIds = null, string? prevChapterId = null)
        {
            var result = new List<FactionStateSnapshot>();
            try
            {
                var guide = await AggregateFactionStateGuideAsync(allVolumes).ConfigureAwait(false);
                foreach (var (id, entry) in guide.Factions)
                {
                    var lastState = entry.StateHistory.LastOrDefault();
                    result.Add(new FactionStateSnapshot
                    {
                        Id = id,
                        Name = entry.Name,
                        Status = entry.CurrentStatus,
                        ChapterId = lastState?.Chapter ?? string.Empty
                    });
                }

                if (!string.IsNullOrEmpty(prevChapterId) && !allVolumes)
                {
                    var chaptersPerVol = GetChaptersPerVol();
                    var activeIds = new List<string>();
                    foreach (var (id, entry) in guide.Factions)
                    {
                        if (entry.StateHistory == null || entry.StateHistory.Count == 0) continue;
                        var lastState = entry.StateHistory.LastOrDefault();
                        if (lastState == null || string.IsNullOrEmpty(lastState.Chapter)) continue;
                        if (IsActiveInRecentChapters(lastState.Chapter, prevChapterId, cfg.ActiveEntityWindowChapters, chaptersPerVol))
                            activeIds.Add(id);
                    }
                    if (activeIds.Count > 0)
                    {
                        priorityIds = priorityIds != null
                            ? priorityIds.Union(activeIds).Distinct().ToList()
                            : activeIds;
                        TM.App.Log($"[FactSnapshotExtractor] 近期活跃势力补入优先池: {activeIds.Count}条");
                    }
                }

                if (applyLimit)
                {
                    var max = cfg.SnapshotMaxFactionInject;
                    if (result.Count > max)
                    {
                        var factionComparer = Comparer<string>.Create(ChapterParserHelper.CompareChapterId);
                        if (priorityIds != null && priorityIds.Count > 0)
                        {
                            var prioritySet = new HashSet<string>(priorityIds, StringComparer.OrdinalIgnoreCase);
                            var priority = result.Where(f => prioritySet.Contains(f.Id)).ToList();
                            var others = result
                                .Where(f => !prioritySet.Contains(f.Id))
                                .OrderByDescending(f => f.ChapterId, factionComparer)
                                .Take(Math.Max(0, max - priority.Count))
                                .ToList();
                            result = priority.Concat(others).ToList();
                        }
                        else
                        {
                            result = result
                                .OrderByDescending(f => f.ChapterId, factionComparer)
                                .Take(max)
                                .ToList();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactSnapshotExtractor] 抽取势力状态失败: {ex.Message}");
            }
            return result;
        }

        private async Task<List<TimelineSnapshot>> ExtractTimelineAsync(LayeredContextConfigSnapshot cfg, bool allVolumes = false)
        {
            var result = new List<TimelineSnapshot>();
            try
            {
                var guide = await AggregateTimelineGuideAsync(allVolumes).ConfigureAwait(false);
                var comparer = Comparer<string>.Create(ChapterParserHelper.CompareChapterId);
                var recentCount = cfg.SnapshotMaxTimelineInject;
                var recent = guide.ChapterTimeline
                    .OrderByDescending(t => t.ChapterId, comparer)
                    .Take(recentCount)
                    .OrderBy(t => t.ChapterId, comparer)
                    .ToList();

                foreach (var entry in recent)
                {
                    result.Add(new TimelineSnapshot
                    {
                        ChapterId = entry.ChapterId,
                        TimePeriod = entry.TimePeriod,
                        ElapsedTime = entry.ElapsedTime,
                        KeyTimeEvent = entry.KeyTimeEvent
                    });
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactSnapshotExtractor] 抽取时间线失败: {ex.Message}");
            }
            return result;
        }

        private async Task<List<CharacterLocationSnapshot>> ExtractCharacterLocationsAsync(string prevChapterId, LayeredContextConfigSnapshot cfg, bool skipWindowFilter = false, List<string>? forceIncludeCharacterIds = null)
        {
            var result = new List<CharacterLocationSnapshot>();
            try
            {
                var guide = await AggregateTimelineGuideAsync().ConfigureAwait(false);
                var forceSet = forceIncludeCharacterIds != null && forceIncludeCharacterIds.Count > 0
                    ? new System.Collections.Generic.HashSet<string>(forceIncludeCharacterIds, StringComparer.Ordinal)
                    : null;
                foreach (var (id, entry) in guide.CharacterLocations)
                {
                    if (!skipWindowFilter
                        && (forceSet == null || !forceSet.Contains(id))
                        && !IsActiveInRecentChapters(entry.LastUpdatedChapter, prevChapterId, cfg.ActiveEntityWindowChapters, GetChaptersPerVol()))
                        continue;

                    result.Add(new CharacterLocationSnapshot
                    {
                        CharacterId = id,
                        CharacterName = entry.CharacterName,
                        CurrentLocation = entry.CurrentLocation,
                        ChapterId = entry.LastUpdatedChapter
                    });
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactSnapshotExtractor] 抽取角色位置失败: {ex.Message}");
            }
            return result;
        }

        private async Task<List<ItemStateSnapshot>> ExtractItemStatesAsync(LayeredContextConfigSnapshot cfg, List<string>? characterIds = null, bool applyLimit = true, bool allVolumes = false)
        {
            var result = new List<ItemStateSnapshot>();
            try
            {
                var guide = await AggregateItemStateGuideAsync(allVolumes).ConfigureAwait(false);
                var all = new List<ItemStateSnapshot>(guide.Items.Count);
                foreach (var (id, entry) in guide.Items)
                {
                    var lastState = entry.StateHistory.LastOrDefault();
                    all.Add(new ItemStateSnapshot
                    {
                        Id = id,
                        Name = entry.Name,
                        CurrentHolder = entry.CurrentHolder,
                        Status = entry.CurrentStatus,
                        ChapterId = lastState?.Chapter ?? string.Empty
                    });
                }

                if (!applyLimit)
                {
                    return all;
                }

                var maxInject = cfg.SnapshotMaxItemInject;
                if (characterIds != null && characterIds.Count > 0)
                {
                    var itemComparer = Comparer<string>.Create(ChapterParserHelper.CompareChapterId);
                    var charSet = new System.Collections.Generic.HashSet<string>(characterIds, StringComparer.Ordinal);
                    var related = all.Where(i => charSet.Contains(i.CurrentHolder)).ToList();
                    var others = all
                        .Where(i => !charSet.Contains(i.CurrentHolder))
                        .OrderByDescending(i => i.ChapterId, itemComparer)
                        .ToList();
                    result.AddRange(related);
                    var remaining = maxInject - related.Count;
                    if (remaining > 0 && others.Count > 0)
                        result.AddRange(others.Take(remaining));
                    TM.App.Log($"[FactSnapshotExtractor] 物品注入: 关联{related.Count}条 + 补充{result.Count - related.Count}条");
                }
                else
                {
                    var itemComparer = Comparer<string>.Create(ChapterParserHelper.CompareChapterId);
                    result = all.Count > maxInject
                        ? all.OrderByDescending(i => i.ChapterId, itemComparer).Take(maxInject).ToList()
                        : all;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactSnapshotExtractor] 抽取物品状态失败: {ex.Message}");
            }
            return result;
        }

        #endregion
    }
}
