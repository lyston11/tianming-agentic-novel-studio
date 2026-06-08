using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.TaskContexts;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class GuideContextService
    {
        #region 风险4/5 辅助方法

        private static async Task<List<string>> DetectStateDivergenceAsync(ContentTaskContext context)
        {
            var warnings = new List<string>();
            if (context.FactSnapshot == null)
                return warnings;
            try
            {
                var guideManager = ServiceLocator.Get<GuideManager>();
                var cfg = LayeredContextConfig.TakeSnapshot();
                var threshold = cfg.DriftEscalateThreshold;
                var windowSize = cfg.DriftWarningsRecentChapterWindow;

                var charIdSet = new HashSet<string>(
                    context.Characters.Select(c => c.Id).Where(s => !string.IsNullOrWhiteSpace(s)),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var descriptor in EntityDimensionRegistry.All)
                {
                    try
                    {
                        var records = await descriptor.LoadRecentEntitiesAsync(guideManager, 3).ConfigureAwait(false);
                        foreach (var record in records)
                        {
                            if (record.DriftWarnings.Count == 0) continue;

                            if (string.Equals(descriptor.DimensionCode, "character", StringComparison.OrdinalIgnoreCase)
                                && charIdSet.Count > 0 && !charIdSet.Contains(record.Id))
                                continue;

                            var recentCount = CountRecentDriftWarnings(record.DriftWarnings, context.ChapterId, windowSize);
                            if (recentCount == 0) continue;

                            var warnMsg = recentCount >= threshold
                                ? $"{descriptor.DimensionName}[{record.Name}]: ⚠⚠ 近{windowSize}章累积漂移{recentCount}条（已超严重阈值{threshold}），FactSnapshot 中相关数据可能不完整，请优先以最新状态描述为准并警惕信息矛盾"
                                : $"{descriptor.DimensionName}[{record.Name}]: 近{windowSize}章存在{recentCount}条漂移记录，请以 FactSnapshot 为准";
                            warnings.Add(warnMsg);
                        }
                    }
                    catch (Exception dimEx)
                    {
                        TM.App.Log($"[GuideContextService] {descriptor.DimensionName}维度漂移扫描失败（非致命）: {dimEx.Message}");
                    }
                }

                if (warnings.Count > 0)
                    TM.App.Log($"[GuideContextService] 检测到{warnings.Count}个设定/状态分歧实体（跨8维度，窗口{windowSize}章）");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 状态分歧检测失败: {ex.Message}");
            }
            return warnings;
        }

        private static int CountRecentDriftWarnings(IReadOnlyList<string> warnings, string currentChapterId, int windowSize)
        {
            if (windowSize <= 0 || string.IsNullOrWhiteSpace(currentChapterId))
                return warnings.Count;

            var cp = ChapterParserHelper.ParseChapterIdOrDefault(currentChapterId);
            if (cp.volumeNumber <= 0) return warnings.Count;

            var count = 0;
            foreach (var w in warnings)
            {
                if (string.IsNullOrWhiteSpace(w)) continue;
                var colonIdx = w.IndexOf(':');
                if (colonIdx <= 0) { count++; continue; }

                var chId = w.Substring(0, colonIdx).Trim();
                var wp = ChapterParserHelper.ParseChapterIdOrDefault(chId);
                if (wp.volumeNumber <= 0) { count++; continue; }

                var cmp = ChapterParserHelper.CompareChapterId(chId, currentChapterId);
                if (cmp > 0) continue;

                if (cp.volumeNumber == wp.volumeNumber)
                {
                    if (cp.chapterNumber - wp.chapterNumber < windowSize)
                        count++;
                }
                else if (cp.volumeNumber - wp.volumeNumber == 1)
                {
                    count++;
                }
            }
            return count;
        }

        private async Task<List<string>> DetectTrackingGapsAsync(ContentTaskContext context, string currentChapterId)
        {
            var warnings = new List<string>();
            try
            {
                var guideManager = ServiceLocator.Get<GuideManager>();
                var _csVols2 = guideManager.GetExistingVolumeNumbers("character_state_guide.json");
                var _timelineVols = guideManager.GetExistingVolumeNumbers("timeline_guide.json");

                var csGuidesTask = Task.WhenAll(_csVols2.Select(v =>
                    guideManager.GetGuideAsync<CharacterStateGuide>(GuideManager.GetVolumeFileName("character_state_guide.json", v))));
                var tlGuidesTask = Task.WhenAll(_timelineVols.Select(v =>
                    guideManager.GetGuideAsync<TM.Services.Modules.ProjectData.Models.Guides.TimelineGuide>(GuideManager.GetVolumeFileName("timeline_guide.json", v))));

                CharacterStateGuide[] csGuides;
                TM.Services.Modules.ProjectData.Models.Guides.TimelineGuide[] tlGuides;
                try { csGuides = await csGuidesTask.ConfigureAwait(false); } catch { csGuides = System.Array.Empty<CharacterStateGuide>(); }
                try { tlGuides = await tlGuidesTask.ConfigureAwait(false); }
                catch (Exception _tlEx)
                {
                    TM.App.Log($"[GuideContextService] v1追踪空洞: 加载Timeline guide失败，降级为仅角色状态判断: {_tlEx.Message}");
                    tlGuides = System.Array.Empty<TM.Services.Modules.ProjectData.Models.Guides.TimelineGuide>();
                }

                var stateGuide = new CharacterStateGuide();
                foreach (var _g2 in csGuides)
                    foreach (var (_id2, _entry2) in _g2.Characters)
                    {
                        if (!stateGuide.Characters.ContainsKey(_id2))
                            stateGuide.Characters[_id2] = new CharacterStateEntry { Name = _entry2.Name };
                        stateGuide.Characters[_id2].StateHistory.AddRange(_entry2.StateHistory);
                    }

                var trackedChapters = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in stateGuide.Characters.Values)
                    foreach (var state in entry.StateHistory)
                        if (!string.IsNullOrWhiteSpace(state.Chapter))
                            trackedChapters.Add(state.Chapter);

                foreach (var _gt in tlGuides)
                    foreach (var _te in _gt.ChapterTimeline)
                        if (!string.IsNullOrWhiteSpace(_te.ChapterId))
                            trackedChapters.Add(_te.ChapterId);

                if (trackedChapters.Count == 0) return warnings;

                var chaptersPath = TM.Framework.Common.Helpers.Storage.StoragePathHelper.GetProjectChaptersPath();
                if (!System.IO.Directory.Exists(chaptersPath)) return warnings;

                var allChapterIds = await GetCachedChapterIdsAsync(chaptersPath).ConfigureAwait(false);
                var comparer = Comparer<string>.Create(ChapterParserHelper.CompareChapterId);

                var gapChapters = allChapterIds
                    .Where(id => ChapterParserHelper.CompareChapterId(id, currentChapterId) < 0)
                    .OrderByDescending(id => id, comparer)
                    .Take(10)
                    .Where(id => !trackedChapters.Contains(id))
                    .ToList();

                if (gapChapters.Count > 0)
                {
                    warnings.Add(
                        $"[v1追踪空洞] 近10章中以下章节有正文但无CHANGES记录，账本可能有空洞（FactSnapshot准确性下降）: {string.Join(", ", gapChapters)}");
                    TM.App.Log($"[GuideContextService] v1: 追踪空洞 {gapChapters.Count}章: {string.Join(", ", gapChapters)}");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] v1追踪空洞检测失败: {ex.Message}");
            }
            return warnings;
        }

        private static async Task<List<string>> ValidateVolumeEndChapterAsync(ContentTaskContext context, string chapterId)
        {
            var warnings = new List<string>();
            try
            {
                var parsed = ChapterParserHelper.ParseChapterId(chapterId);
                if (parsed == null) return warnings;

                var volumeService = ServiceLocator.Get<TM.Modules.Generate.Elements.VolumeDesign.Services.VolumeDesignService>();
                await volumeService.InitializeAsync().ConfigureAwait(false);
                var designs = volumeService.GetAllVolumeDesigns()
                    .ToList();
                var design = designs.FirstOrDefault(v => v.VolumeNumber == parsed.Value.volumeNumber);

                if (design != null && design.EndChapter <= 0)
                {
                    warnings.Add(
                        $"[v2卷末存档] 第{parsed.Value.volumeNumber}卷EndChapter未配置，卷末事实存档将永远不触发！跨卷角色基线无法建立，请在卷设计中配置EndChapter");
                    TM.App.Log($"[GuideContextService] v2: 第{parsed.Value.volumeNumber}卷EndChapter未配置");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] v2 EndChapter校验失败: {ex.Message}");
            }
            return warnings;
        }

        #endregion
    }
}
