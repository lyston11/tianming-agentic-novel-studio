using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Guides;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class GuideContextService
    {
        public async Task<int> GetVolumeMaxChapterAsync(int volumeNumber)
        {
            try
            {
                var contentGuide = await GetContentGuideAsync().ConfigureAwait(false);
                if (contentGuide?.Chapters == null || contentGuide.Chapters.Count == 0)
                    return 0;

                var maxChapter = 0;
                foreach (var kvp in contentGuide.Chapters)
                {
                    var p = ChapterParserHelper.ParseChapterId(kvp.Key);
                    if (p.HasValue && p.Value.volumeNumber == volumeNumber)
                        maxChapter = Math.Max(maxChapter, p.Value.chapterNumber);
                }
                return maxChapter;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 推断第{volumeNumber}卷末章节号失败: {ex.Message}");
                return 0;
            }
        }

        #region SnapshotExtract

        public Task<Models.Tracking.FactSnapshot> ExtractFactSnapshotForChapterAsync(
            string chapterId,
            ContextIdCollection contextIds)
            => ExtractFactSnapshotAsync(chapterId, contextIds);

        private async Task<Models.Tracking.FactSnapshot> ExtractFactSnapshotAsync(
            string chapterId,
            ContextIdCollection contextIds)
        {
            try
            {
                var characterIds = contextIds?.Characters ?? new List<string>();
                var locationIds = contextIds?.Locations ?? new List<string>();
                var conflictIds = contextIds?.Conflicts ?? new List<string>();
                var foreshadowingSetupIds = contextIds?.ForeshadowingSetups ?? new List<string>();
                var foreshadowingPayoffIds = contextIds?.ForeshadowingPayoffs ?? new List<string>();
                var worldRuleIds = contextIds?.WorldRuleIds ?? new List<string>();
                var factionIds = contextIds?.Factions ?? new List<string>();

                var snapshot = await _factSnapshotExtractor.ExtractSnapshotAsync(
                    chapterId,
                    characterIds,
                    locationIds,
                    conflictIds,
                    foreshadowingSetupIds,
                    foreshadowingPayoffIds,
                    worldRuleIds,
                    factionIds).ConfigureAwait(false);

                TM.App.Log($"[GuideContextService] 势力注入: {snapshot.FactionStates?.Count ?? 0}条（关联{factionIds.Count}个优先）");

                TM.App.Log($"[GuideContextService] 物品注入: {snapshot.ItemStates?.Count ?? 0}条（FactSnapshotExtractor已过滤）");

                TM.App.Log($"[GuideContextService] 事实快照抽取完成: " +
                    $"角色{snapshot.CharacterStates.Count}, " +
                    $"冲突{snapshot.ConflictProgress.Count}, " +
                    $"伏笔{snapshot.ForeshadowingStatus.Count}, " +
                    $"情节{snapshot.PlotPoints.Count}");

                return snapshot;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 事实快照抽取失败: {ex.Message}");
                throw;
            }
        }

        #endregion
    }
}
