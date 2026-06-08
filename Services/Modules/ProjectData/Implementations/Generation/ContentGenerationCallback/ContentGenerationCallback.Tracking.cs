using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TM.Modules.Generate.Elements.VolumeDesign.Services;
using TM.Services.Framework.AI.SemanticKernel;
using TM.Services.Modules.ProjectData.Models.Tracking;
using TM.Services.Modules.ProjectData.Implementations.Guides;
using TM.Services.Modules.ProjectData.Models.TaskContexts;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class ContentGenerationCallback
    {
        private async Task TryUpdateVolumeMilestoneAsync(string chapterId, string? summary = null, bool archiveOnEndChapter = true, bool isRewrite = false, ChapterChanges? changes = null)
        {
            try
            {
                var parsed = ChapterParserHelper.ParseChapterId(chapterId);
                if (!parsed.HasValue) return;

                var volumeNumber = parsed.Value.volumeNumber;
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    if (isRewrite)
                    {
                        var currentSummaries = await _summaryStore.GetVolumeSummariesAsync(volumeNumber).ConfigureAwait(false);
                        await _milestoneStore.RebuildVolumeMilestoneAsync(volumeNumber, currentSummaries).ConfigureAwait(false);
                        TM.App.Log($"[ContentCallback] {chapterId} 重写场景，已全量重建第{volumeNumber}卷里程碑");
                    }
                    else
                    {
                        await _milestoneStore.AppendChapterMilestoneAsync(volumeNumber, chapterId, summary).ConfigureAwait(false);
                    }
                }

                if (changes != null)
                {
                    var keyEntry = ExtractKeyEventEntry(chapterId, volumeNumber, parsed.Value.chapterNumber, changes);
                    if (keyEntry != null)
                        await _keyEventStore.UpsertAsync(volumeNumber, keyEntry).ConfigureAwait(false);
                }

                if (archiveOnEndChapter)
                    await TryArchiveVolumeFactAsync(chapterId, volumeNumber).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] {chapterId} 里程碑更新失败（不影响正文）: {ex.Message}");
            }
        }

        private static ChapterKeyEventEntry? ExtractKeyEventEntry(string chapterId, int volumeNumber, int chapterNumber, ChapterChanges changes)
        {
            var entry = new ChapterKeyEventEntry
            {
                ChapterId = chapterId,
                VolumeNumber = volumeNumber,
                ChapterNumber = chapterNumber
            };

            foreach (var c in changes.CharacterStateChanges ?? new())
            {
                if (!string.IsNullOrWhiteSpace(c.KeyEvent) && IsHighImportance(c.Importance))
                    entry.Characters.Add($"{c.CharacterId}:{c.KeyEvent}");
            }

            foreach (var p in changes.NewPlotPoints ?? new())
            {
                if (!string.IsNullOrWhiteSpace(p.Context) && IsHighImportance(p.Importance))
                    entry.Turnings.Add(p.Context);
            }

            foreach (var f in changes.ForeshadowingActions ?? new())
            {
                if (!string.IsNullOrWhiteSpace(f.ForeshadowId)
                    && string.Equals(f.Action, "payoff", StringComparison.OrdinalIgnoreCase))
                    entry.Foreshadows.Add($"{f.ForeshadowId}:回收");
                else if (!string.IsNullOrWhiteSpace(f.ForeshadowId)
                    && string.Equals(f.Action, "setup", StringComparison.OrdinalIgnoreCase))
                    entry.Foreshadows.Add($"{f.ForeshadowId}:埋设");
            }

            foreach (var fa in changes.FactionStateChanges ?? new())
            {
                if (!string.IsNullOrWhiteSpace(fa.Event) && IsHighImportance(fa.Importance))
                    entry.Factions.Add($"{fa.FactionId}:{fa.Event}→{fa.NewStatus}");
            }

            foreach (var cf in changes.ConflictProgress ?? new())
            {
                if (!string.IsNullOrWhiteSpace(cf.Event) && IsHighImportance(cf.Importance))
                    entry.Factions.Add($"{cf.ConflictId}:{cf.Event}→{cf.NewStatus}");
            }

            if (entry.Characters.Count == 0 && entry.Turnings.Count == 0
                && entry.Foreshadows.Count == 0 && entry.Factions.Count == 0)
                return null;

            return entry;
        }

        private static bool IsHighImportance(string? importance) =>
            string.Equals(importance, "important", StringComparison.OrdinalIgnoreCase)
            || string.Equals(importance, "critical", StringComparison.OrdinalIgnoreCase);

        private async Task TryArchiveVolumeFactAsync(string chapterId, int volumeNumber)
        {
            try
            {
                var volumeService = ServiceLocator.Get<VolumeDesignService>();
                if (!volumeService.IsInitialized)
                    await volumeService.InitializeAsync().ConfigureAwait(false);
                var designs = volumeService.GetAllVolumeDesigns()
                    .ToList();
                var volumeDesign = designs.FirstOrDefault(v => v.VolumeNumber == volumeNumber);

                var effectiveEndChapter = volumeDesign?.EndChapter ?? 0;

                if (effectiveEndChapter <= 0)
                {
                    effectiveEndChapter = await ResolveVolumeEndChapterAsync(volumeNumber).ConfigureAwait(false);
                    if (effectiveEndChapter > 0)
                        TM.App.Log($"[ContentCallback] 第{volumeNumber}卷EndChapter未配置，自动推断为: {effectiveEndChapter}");
                }

                if (effectiveEndChapter <= 0)
                {
                    TM.App.Log($"[ContentCallback] 第{volumeNumber}卷无法确定结束章节号（ContentGuide可能为空），跳过卷末存档");
                    return;
                }

                var parsed = ChapterParserHelper.ParseChapterId(chapterId);
                if (!parsed.HasValue || parsed.Value.chapterNumber != effectiveEndChapter) return;

                var snapshot = await ServiceLocator.Get<FactSnapshotExtractor>().ExtractVolumeEndSnapshotAsync(chapterId).ConfigureAwait(false);
                await ServiceLocator.Get<VolumeFactArchiveStore>().ArchiveVolumeAsync(volumeNumber, snapshot, chapterId).ConfigureAwait(false);
                TM.App.Log($"[ContentCallback] 第{volumeNumber}卷事实存档完成: {chapterId}（effectiveEndChapter={effectiveEndChapter}）");

                MilestoneCondenser.TryCondenseInBackground(volumeNumber);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] 卷事实存档失败（不影响正文）: {ex.Message}");
            }
        }

        private static Task<int> ResolveVolumeEndChapterAsync(int volumeNumber)
            => ServiceLocator.Get<GuideContextService>().GetVolumeMaxChapterAsync(volumeNumber);

        private async Task<string> NormalizePersistedContentAsync(string chapterId, string content)
        {
            var normalizedBody = StripLeadingHeadings(content);
            normalizedBody = ContentCleanHelper.StripModelArtifacts(normalizedBody);
            normalizedBody = StripLeadingHeadings(normalizedBody);

            var packagedTitle = await GetPackagedChapterTitleStrictAsync(chapterId).ConfigureAwait(false);

            var canonicalTitle = BuildCanonicalTitle(chapterId, packagedTitle);
            if (string.IsNullOrWhiteSpace(normalizedBody))
            {
                return $"# {canonicalTitle}";
            }

            return $"# {canonicalTitle}\n\n{normalizedBody}";
        }

        private static async Task<string> GetPackagedChapterTitleStrictAsync(string chapterId)
        {
            try
            {
                var guideService = ServiceLocator.Get<GuideContextService>();
                var guide = await guideService.GetContentGuideAsync().ConfigureAwait(false);
                if (guide?.Chapters != null && guide.Chapters.TryGetValue(chapterId, out var entry) && !string.IsNullOrWhiteSpace(entry?.Title))
                    return ChapterParserHelper.NormalizeChapterTitle(entry.Title.Trim());
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] {chapterId} 获取打包标题异常（将使用章节号）: {ex.Message}");
            }

            TM.App.Log($"[ContentCallback] {chapterId} 未找到打包标题，使用章节号落盘");
            return string.Empty;
        }

        private static string BuildCanonicalTitle(string chapterId, string title)
        {
            var parsed = ChapterParserHelper.ParseChapterId(chapterId);
            var chapterNum = parsed?.chapterNumber ?? 0;
            if (chapterNum > 0)
            {
                return string.IsNullOrWhiteSpace(title) ? $"第{chapterNum}章" : $"第{chapterNum}章 {title}";
            }

            return string.IsNullOrWhiteSpace(title) ? chapterId : title;
        }

        private static readonly Regex LeadingChapterTitleLineRegex = new(
            @"^第[一二三四五六七八九十百千万零壹贰叁肆伍陆柒捌玖拾佰仟萬两〇\d]+\s*(?:章节|章)(?:[:：、.\s\u3000]|$)",
            RegexOptions.Compiled);

        private static string StripLeadingHeadings(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }

            var text = content.TrimStart();
            while (!string.IsNullOrEmpty(text))
            {
                var firstLineEnd = text.IndexOf('\n');
                var firstLine = (firstLineEnd >= 0 ? text.Substring(0, firstLineEnd) : text).Trim();

                bool isHeading = firstLine.StartsWith('#')
                              || LeadingChapterTitleLineRegex.IsMatch(firstLine);
                if (!isHeading)
                {
                    break;
                }

                text = firstLineEnd >= 0 ? text.Substring(firstLineEnd + 1).TrimStart() : string.Empty;
            }

            return text.Trim();
        }

        public async Task OnChapterDeletedAsync(string chapterId)
        {
            TM.App.Log($"[ContentCallback] 开始级联清理: {chapterId}");

            _changesWalStore.Delete(chapterId);

            try
            {
                await _summaryStore.RemoveSummaryAsync(chapterId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] 清理摘要失败: {ex.Message}");
            }

            async Task SafeCleanAsync(Task task, string label)
            {
                try { await task.ConfigureAwait(false); }
                catch (Exception ex) { TM.App.Log($"[ContentCallback] 清理{label}失败: {ex.Message}"); }
            }

            await Task.WhenAll(
                SafeCleanAsync(_characterStateService.RemoveChapterDataAsync(chapterId), "角色状态"),
                SafeCleanAsync(_conflictProgressService.RemoveChapterDataAsync(chapterId), "冲突进度"),
                SafeCleanAsync(_plotPointsIndexService.RemoveChapterDataAsync(chapterId), "情节索引"),
                SafeCleanAsync(_foreshadowingStatusService.RemoveChapterDataAsync(chapterId), "伏笔状态"),
                SafeCleanAsync(_locationStateService.RemoveChapterDataAsync(chapterId), "地点状态"),
                SafeCleanAsync(_factionStateService.RemoveChapterDataAsync(chapterId), "势力状态"),
                SafeCleanAsync(_timelineService.RemoveChapterDataAsync(chapterId), "时间线"),
                SafeCleanAsync(_itemStateService.RemoveChapterDataAsync(chapterId), "物品状态"),
                SafeCleanAsync(_secretRevealService.RemoveChapterDataAsync(chapterId), "秘密知情"),
                SafeCleanAsync(_pledgeConstraintService.RemoveChapterDataAsync(chapterId), "承诺/契约"),
                SafeCleanAsync(_deadlineConstraintService.RemoveChapterDataAsync(chapterId), "倒计时/时限")
            ).ConfigureAwait(false);

            try { ServiceLocator.Get<RelationStrengthService>().InvalidateCache(); }
            catch (Exception ex) { TM.App.Log($"[ContentCallback] 关联强度缓存失效失败（非致命）: {ex.Message}"); }

            try
            {
                await _guideManager.FlushAllAsync().ConfigureAwait(false);
                ServiceLocator.Get<GuideContextService>().InvalidateContentGuideCache();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] guides刷盘失败: {ex.Message}");
            }

            try
            {
                await ServiceLocator.Get<KeywordChapterIndexService>().RemoveChapterAsync(chapterId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] 清理关键词索引失败（非致命）: {ex.Message}");
            }

            await CleanVectorIndicesAsync(chapterId).ConfigureAwait(false);

            try
            {
                var _parsed = ChapterParserHelper.ParseChapterId(chapterId);
                if (_parsed.HasValue)
                {
                    var _vol = _parsed.Value.volumeNumber;
                    var _currentSummaries = await _summaryStore.GetVolumeSummariesAsync(_vol).ConfigureAwait(false);
                    await _milestoneStore.RebuildVolumeMilestoneAsync(_vol, _currentSummaries).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] 里程碑重建失败（非致命）: {ex.Message}");
            }

            try
            {
                var _archiveStore = ServiceLocator.Get<VolumeFactArchiveStore>();
                var _archiveParsed = ChapterParserHelper.ParseChapterId(chapterId);
                if (_archiveParsed.HasValue)
                    await _archiveStore.DeleteArchiveAsync(_archiveParsed.Value.volumeNumber).ConfigureAwait(false);
                else
                    _archiveStore.InvalidateCache();
            }
            catch (Exception ex) { TM.App.Log($"[ContentCallback] 存档联动清理失败（非致命）: {ex.Message}"); }

            TM.App.Log($"[ContentCallback] 级联清理完成: {chapterId}");
        }

    }
}
