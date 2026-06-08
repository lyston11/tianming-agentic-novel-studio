using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class ForeshadowingStatusService
    {
        private readonly GuideManager _guideManager;

        #region 构造函数

        public ForeshadowingStatusService(GuideManager guideManager)
        {
            _guideManager = guideManager;
        }

        private static string GetLatestExistingChapterId()
        {
            try
            {
                var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
                if (!Directory.Exists(chaptersPath))
                    return string.Empty;

                var files = Directory.GetFiles(chaptersPath, "*.md", SearchOption.TopDirectoryOnly);
                if (files.Length == 0)
                    return string.Empty;

                var latest = string.Empty;
                foreach (var file in files)
                {
                    var id = Path.GetFileNameWithoutExtension(file);
                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    if (string.IsNullOrEmpty(latest) || ChapterParserHelper.CompareChapterId(id, latest) > 0)
                    {
                        latest = id;
                    }
                }

                return latest;
            }
            catch
            {
                return string.Empty;
            }
        }

        #endregion

        private const string GuideFileName = "foreshadowing_status_guide.json";

        public async Task<ForeshadowingStatistics> GetStatisticsAsync()
        {
            var guide = await _guideManager.GetGuideAsync<ForeshadowingStatusGuide>(GuideFileName).ConfigureAwait(false);

            return new ForeshadowingStatistics
            {
                TotalCount = guide.Foreshadowings.Count,
                SetupCount = guide.Foreshadowings.Count(f => f.Value.IsSetup),
                ResolvedCount = guide.Foreshadowings.Count(f => f.Value.IsResolved),
                OverdueCount = guide.Foreshadowings.Count(f => f.Value.IsOverdue),

                Tier1Stats = GetTierStats(guide, "Tier-1"),
                Tier2Stats = GetTierStats(guide, "Tier-2"),
                Tier3Stats = GetTierStats(guide, "Tier-3")
            };
        }

        public async Task<List<ForeshadowingStatusEntry>> GetOverdueForeshadowingsAsync()
        {
            var guide = await _guideManager.GetGuideAsync<ForeshadowingStatusGuide>(GuideFileName).ConfigureAwait(false);

            return guide.Foreshadowings.Values
                .Where(f => f.IsOverdue)
                .OrderBy(f => f.ExpectedPayoffChapter)
                .ToList();
        }

        public async Task MarkAsSetupAsync(string foreshadowId, string chapterId)
        {
            var guide = await _guideManager.GetGuideAsync<ForeshadowingStatusGuide>(GuideFileName).ConfigureAwait(false);

            if (!guide.Foreshadowings.TryGetValue(foreshadowId, out var setupEntry))
            {
                setupEntry = new ForeshadowingStatusEntry { Name = foreshadowId };
                guide.Foreshadowings[foreshadowId] = setupEntry;
                TM.App.Log($"[Foreshadowing] 自动注册新伏笔: {foreshadowId}");
            }

            setupEntry.IsSetup = true;
            setupEntry.ActualSetupChapter = chapterId;
            _guideManager.MarkDirty(GuideFileName);
            TM.App.Log($"[Foreshadowing] 已标记 {foreshadowId} 在 {chapterId} 埋设");
        }

        public async Task MarkAsResolvedAsync(string foreshadowId, string chapterId)
        {
            var guide = await _guideManager.GetGuideAsync<ForeshadowingStatusGuide>(GuideFileName).ConfigureAwait(false);

            if (!guide.Foreshadowings.TryGetValue(foreshadowId, out var resolveEntry))
            {
                resolveEntry = new ForeshadowingStatusEntry { Name = foreshadowId };
                guide.Foreshadowings[foreshadowId] = resolveEntry;
                TM.App.Log($"[Foreshadowing] 自动注册新伏笔: {foreshadowId}");
            }

            resolveEntry.IsResolved = true;
            resolveEntry.IsOverdue = false;
            resolveEntry.ActualPayoffChapter = chapterId;
            _guideManager.MarkDirty(GuideFileName);
            TM.App.Log($"[Foreshadowing] 已标记 {foreshadowId} 在 {chapterId} 揭示");
        }

        public async Task RefreshOverdueStatusAsync(string currentChapterId)
        {
            var parsedCurrent = ChapterParserHelper.ParseChapterId(currentChapterId);
            if (!parsedCurrent.HasValue) return;

            var guide = await _guideManager.GetGuideAsync<ForeshadowingStatusGuide>(GuideFileName).ConfigureAwait(false);
            var modified = false;

            foreach (var (_, entry) in guide.Foreshadowings)
            {
                if (entry.IsResolved)
                {
                    if (entry.IsOverdue) { entry.IsOverdue = false; modified = true; }
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.ExpectedPayoffChapter) ||
                    ChapterParserHelper.ParseChapterId(entry.ExpectedPayoffChapter) == null)
                    continue;

                var shouldOverdue = ChapterParserHelper.CompareChapterId(currentChapterId, entry.ExpectedPayoffChapter) > 0;
                if (entry.IsOverdue != shouldOverdue)
                {
                    entry.IsOverdue = shouldOverdue;
                    modified = true;
                }
            }

            if (modified)
            {
                _guideManager.MarkDirty(GuideFileName);
                TM.App.Log($"[Foreshadowing] {currentChapterId} 刷新逾期状态完成");
            }
        }

        public async Task RemoveChapterDataAsync(string chapterId)
        {
            var guide = await _guideManager.GetGuideAsync<ForeshadowingStatusGuide>(GuideFileName).ConfigureAwait(false);
            var modified = false;

            var latestChapterId = GetLatestExistingChapterId();
            var canCompareLatest = !string.IsNullOrWhiteSpace(latestChapterId) &&
                                   ChapterParserHelper.ParseChapterId(latestChapterId) != null;

            foreach (var (_, entry) in guide.Foreshadowings)
            {
                if (string.Equals(entry.ActualSetupChapter, chapterId, StringComparison.Ordinal))
                {
                    entry.IsSetup = false;
                    entry.ActualSetupChapter = string.Empty;
                    modified = true;
                }

                if (string.Equals(entry.ActualPayoffChapter, chapterId, StringComparison.Ordinal))
                {
                    entry.IsResolved = false;
                    entry.ActualPayoffChapter = string.Empty;
                    modified = true;
                }

                var shouldOverdue = false;
                if (!entry.IsResolved && canCompareLatest &&
                    !string.IsNullOrWhiteSpace(entry.ExpectedPayoffChapter) &&
                    ChapterParserHelper.ParseChapterId(entry.ExpectedPayoffChapter) != null)
                {
                    shouldOverdue = ChapterParserHelper.CompareChapterId(latestChapterId, entry.ExpectedPayoffChapter) > 0;
                }

                if (entry.IsOverdue != shouldOverdue)
                {
                    entry.IsOverdue = shouldOverdue;
                    modified = true;
                }
            }

            if (modified)
            {
                _guideManager.MarkDirty(GuideFileName);
                TM.App.Log($"[Foreshadowing] 已回退章节 {chapterId} 的伏笔状态");
            }
        }

        private ForeshadowingTierStats GetTierStats(ForeshadowingStatusGuide guide, string tier)
        {
            var tierItems = guide.Foreshadowings.Values.Where(f => f.Tier == tier).ToList();
            return new ForeshadowingTierStats
            {
                Total = tierItems.Count,
                Setup = tierItems.Count(f => f.IsSetup),
                Resolved = tierItems.Count(f => f.IsResolved),
                Overdue = tierItems.Count(f => f.IsOverdue)
            };
        }
    }
}
