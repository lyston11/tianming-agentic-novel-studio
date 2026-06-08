using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.UI.Workspace.Services;
using TM.Modules.Generate.Elements.Chapter.Services;
using TM.Modules.Generate.Elements.VolumeDesign.Services;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;
using System.Reflection;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class WriterPlugin
    {
        private static readonly System.Text.RegularExpressions.Regex BoldStarRegex = new(@"\*\*([^*]+)\*\*", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex BoldUnderRegex = new(@"__([^_]+)__", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex ItalicStarRegex = new(@"(?<![*])\*([^*]+)\*(?![*])", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex InlineCodeRegex = new(@"`([^`]+)`", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex UselessHeadingRegex = new(@"^##\s*(正文|内容|章节内容)\s*\n", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Multiline);
        private static readonly System.Text.RegularExpressions.Regex MultipleNewlineRegex = new(@"\n{3,}", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex ChapterHeadingRegex = new(@"(?m)^\s*#\s*第\s*[0-9一二三四五六七八九十百千]+\s*章.*$", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex GenericHeadingRegex = new(@"(?m)^\s*#\s+(?!章节生成任务\b).+$", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex VolumeNumberRegex = new(@"第\s*(\d+)\s*卷", System.Text.RegularExpressions.RegexOptions.Compiled);

        private PanelCommunicationService? _comm;
        private PanelCommunicationService Comm => _comm ??= ServiceLocator.Get<PanelCommunicationService>();
        private VolumeDesignService? _volumeDesignService;
        private VolumeDesignService VolumeDesignService => _volumeDesignService ??= ServiceLocator.Get<VolumeDesignService>();

        public class SavedChapterResult
        {
            public string ChapterId { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string SavedContent { get; set; } = string.Empty;
            public string DisplayContent { get; set; } = string.Empty;
            public string? ChangesJson { get; set; }
            public double? ChangesDurationSeconds { get; set; }
        }

        private async Task<string> GenerateDefaultNextChapterIdAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var contentService = ServiceLocator.Get<GeneratedContentService>();
            var chapters = await contentService.GetGeneratedChaptersAsync().ConfigureAwait(false);

            var baseChapterNumber = 0;
            if (CurrentChapterTracker.HasCurrentChapter)
            {
                var parsed = ChapterParserHelper.ParseChapterId(CurrentChapterTracker.CurrentChapterId);
                if (parsed.HasValue)
                {
                    baseChapterNumber = parsed.Value.chapterNumber;
                }
            }

            if (baseChapterNumber <= 0 && chapters.Count > 0)
            {
                baseChapterNumber = chapters.Max(c => c.ChapterNumber);
            }

            var targetChapterNumber = baseChapterNumber > 0 ? baseChapterNumber + 1 : 1;
            var volumeNumber = await ResolveVolumeNumberForChapterAsync(ct, targetChapterNumber).ConfigureAwait(false);
            return ChapterParserHelper.BuildChapterId(volumeNumber, targetChapterNumber);
        }

        private async Task<int> ResolveVolumeNumberForChapterAsync(CancellationToken ct, int chapterNumber)
        {
            if (chapterNumber <= 0)
            {
                throw new InvalidOperationException("章节号无效");
            }

            ct.ThrowIfCancellationRequested();
            await VolumeDesignService.InitializeAsync().ConfigureAwait(false);
            var designs = VolumeDesignService.GetAllVolumeDesigns();

            var matches = designs
                .Where(v => v.VolumeNumber > 0)
                .Where(v => v.StartChapter > 0)
                .Where(v => v.EndChapter <= 0
                    ? chapterNumber >= v.StartChapter
                    : chapterNumber >= v.StartChapter && chapterNumber <= v.EndChapter)
                .ToList();

            if (matches.Count == 1)
            {
                return matches[0].VolumeNumber;
            }

            if (matches.Count == 0)
            {
                var volumeNumberFromGuide = await TryResolveVolumeNumberFromContentGuideAsync(ct, designs, chapterNumber).ConfigureAwait(false);
                if (volumeNumberFromGuide.HasValue)
                {
                    return volumeNumberFromGuide.Value;
                }

                var availableVolumes = designs
                    .Where(v => v.VolumeNumber > 0)
                    .Select(v => v.VolumeNumber)
                    .Distinct()
                    .OrderBy(v => v)
                    .ToList();

                if (availableVolumes.Count == 1)
                {
                    var soleVolume = designs.First(v => v.VolumeNumber == availableVolumes[0]);
                    if (soleVolume.StartChapter > 0 && soleVolume.EndChapter > 0)
                    {
                        TM.App.Log($"[WriterPlugin] 警告: 第{chapterNumber}章超出第{soleVolume.VolumeNumber}卷的设计范围({soleVolume.StartChapter}-{soleVolume.EndChapter})，该章节可能缺少规划/蓝图数据");
                    }
                    return availableVolumes[0];
                }

                var chapterService = ServiceLocator.Get<ChapterService>();
                await chapterService.InitializeAsync().ConfigureAwait(false);
                var rewriteCategories = chapterService.GetRewriteCategories();
                if (rewriteCategories.Count == 1)
                {
                    var rewriteMatch = VolumeNumberRegex.Match(rewriteCategories[0].Name);
                    if (rewriteMatch.Success && int.TryParse(rewriteMatch.Groups[1].Value, out var rewriteVolumeNumber) && rewriteVolumeNumber > 0)
                    {
                        TM.App.Log($"[WriterPlugin] 仿写分类兜底: 第{chapterNumber}章 → 第{rewriteVolumeNumber}卷");
                        return rewriteVolumeNumber;
                    }
                }

                throw new InvalidOperationException($"未找到包含第{chapterNumber}章的分卷范围，请在分卷设计或仿写分类中明确卷号。");
            }

            var contentService = ServiceLocator.Get<GeneratedContentService>();
            var currentVolPriority = 0;
            if (CurrentChapterTracker.HasCurrentChapter)
            {
                var currentParsed = ChapterParserHelper.ParseChapterId(CurrentChapterTracker.CurrentChapterId);
                if (currentParsed.HasValue)
                    currentVolPriority = currentParsed.Value.volumeNumber;
            }

            var orderedMatches = matches
                .OrderByDescending(m => m.VolumeNumber == currentVolPriority ? 1 : 0)
                .ThenBy(m => m.VolumeNumber)
                .ToList();

            foreach (var match in orderedMatches)
            {
                var candidateId = ChapterParserHelper.BuildChapterId(match.VolumeNumber, chapterNumber);
                if (!contentService.ChapterExists(candidateId))
                {
                    TM.App.Log($"[WriterPlugin] F3b消歧: 第{chapterNumber}章 → 第{match.VolumeNumber}卷（{candidateId}未落盘）");
                    return match.VolumeNumber;
                }
            }

            var existingIds = orderedMatches
                .Select(m => ChapterParserHelper.BuildChapterId(m.VolumeNumber, chapterNumber))
                .ToList();
            var idList = string.Join("、", existingIds);
            throw new InvalidOperationException(
                $"第{chapterNumber}章在以下卷中均已生成：{idList}。\n" +
                $"如需新建，请指定卷号（如\"生成第X卷第{chapterNumber}章\"）；\n" +
                $"如需重写，请使用 @重写:{existingIds.First()} 指令。");
        }

        private async Task<int?> TryResolveVolumeNumberFromContentGuideAsync(
            CancellationToken ct,
            IList<VolumeDesignData> volumeDesigns,
            int chapterNumber)
        {
            ct.ThrowIfCancellationRequested();

            var guideService = ServiceLocator.Get<GuideContextService>();
            var guide = await guideService.GetContentGuideAsync().ConfigureAwait(false);
            if (guide?.Chapters == null || guide.Chapters.Count == 0)
            {
                return null;
            }

            var volumeNumbers = volumeDesigns
                .Where(v => v.VolumeNumber > 0)
                .Select(v => v.VolumeNumber)
                .Distinct()
                .OrderBy(v => v)
                .ToList();

            var matched = new List<int>();
            foreach (var vol in volumeNumbers)
            {
                var chapterId = ChapterParserHelper.BuildChapterId(vol, chapterNumber);
                if (guide.Chapters.ContainsKey(chapterId))
                {
                    matched.Add(vol);
                }
            }

            if (matched.Count == 1)
            {
                return matched[0];
            }

            return null;
        }

        private static async Task EnsureSequentialChapterContinuityAsync(CancellationToken ct, string chapterId)
        {
            if (string.IsNullOrWhiteSpace(chapterId))
                return;

            var parsed = ChapterParserHelper.ParseChapterId(chapterId);
            if (!parsed.HasValue)
                return;

            ct.ThrowIfCancellationRequested();

            var contentService = ServiceLocator.Get<GeneratedContentService>();
            var generatedChapters = await contentService.GetGeneratedChaptersAsync().ConfigureAwait(false);

            if (generatedChapters.Count == 0)
            {
                if (parsed.Value.chapterNumber > 1)
                {
                    throw new InvalidOperationException(
                        $"⚠ 跨章警告：当前尚未生成任何章节，但请求生成 {chapterId}。\n" +
                        "请先从第 1 章开始，保证章节连贯性。");
                }

                return;
            }

            var frontier = generatedChapters
                .OrderBy(c => c.ChapterNumber)
                .ThenBy(c => c.VolumeNumber)
                .Last();

            var maxGenerated = frontier.ChapterNumber;
            var requestedChapter = parsed.Value.chapterNumber;

            if (maxGenerated > 0)
            {
                var generatedNums = generatedChapters
                    .Select(c => c.ChapterNumber)
                    .Where(n => n > 0)
                    .ToHashSet();

                var missingHistory = new List<int>();
                for (var i = 1; i <= maxGenerated && missingHistory.Count < 20; i++)
                {
                    if (!generatedNums.Contains(i))
                        missingHistory.Add(i);
                }

                if (missingHistory.Count > 0 && requestedChapter >= maxGenerated + 1)
                {
                    var missingList = string.Join("、", missingHistory.Select(n => n.ToString()));
                    throw new InvalidOperationException(
                        $"⚠ 连贯性警告：当前虽然已生成到第 {maxGenerated} 章（{frontier.Title}），但历史章节存在缺口。\n" +
                        $"缺失章节（部分）：{missingList}{(missingHistory.Count >= 20 ? "..." : string.Empty)}\n\n" +
                        "请先补齐缺失章节，再继续生成后续章节。");
                }

                if (requestedChapter > maxGenerated + 1)
                {
                    var missingStart = maxGenerated + 1;
                    var missingEnd = requestedChapter - 1;
                    var missingDesc = missingStart == missingEnd
                        ? $"第 {missingStart} 章"
                        : $"第 {missingStart}～{missingEnd} 章";

                    throw new InvalidOperationException(
                        $"⚠ 跨章警告：当前已生成到第 {maxGenerated} 章（{frontier.Title}），" +
                        $"但请求生成第 {requestedChapter} 章，跳过了 {missingDesc}。\n" +
                        $"为保证章节连贯性，请先生成 {missingDesc}。");
                }
            }
        }

        private async Task EnsureVolumeExistsForRewriteAsync(CancellationToken ct, string chapterId)
        {
            if (string.IsNullOrWhiteSpace(chapterId))
                throw new InvalidOperationException("章节ID不能为空");

            var parsed = ChapterParserHelper.ParseChapterId(chapterId);
            if (!parsed.HasValue)
                throw new InvalidOperationException($"章节ID格式无效: {chapterId}");

            ct.ThrowIfCancellationRequested();
            var targetVolume = parsed.Value.volumeNumber;
            var chapterService = ServiceLocator.Get<ChapterService>();
            await chapterService.InitializeAsync().ConfigureAwait(false);
            var rewriteCategories = chapterService.GetRewriteCategories();
            var rewriteExists = rewriteCategories.Any(c =>
            {
                var m = VolumeNumberRegex.Match(c.Name);
                return m.Success && int.TryParse(m.Groups[1].Value, out var n) && n == targetVolume;
            });
            if (rewriteExists)
            {
                TM.App.Log($"[WriterPlugin] 仿写分类已存在（ChapterService），放行落盘: 第{targetVolume}卷");
                return;
            }

            await VolumeDesignService.InitializeAsync().ConfigureAwait(false);
            var volumeExists = VolumeDesignService.GetAllVolumeDesigns()
                .Any(v => v.VolumeNumber == targetVolume);

            if (!volumeExists)
            {
                TM.App.Log($"[WriterPlugin] 仿写路径：第{targetVolume}卷不存在，自动创建分卷设计");
                var newVolume = new TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign.VolumeDesignData
                {
                    Id = ShortIdGenerator.New("D"),
                    Name = $"第{targetVolume}卷",
                    Category = string.Empty,
                    VolumeNumber = targetVolume,
                    VolumeTitle = string.Empty,
                    IsEnabled = true,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                await VolumeDesignService.AddVolumeDesignAsync(newVolume).ConfigureAwait(false);
                TM.App.Log($"[WriterPlugin] 第{targetVolume}卷分卷设计已自动创建（仿写路径）");
            }
        }

        public async Task<SavedChapterResult> SaveExternalChapterAsync(
            CancellationToken ct,
            string title,
            string content,
            string chapterId = "")
        {
            ct.ThrowIfCancellationRequested();

            var contentService = ServiceLocator.Get<GeneratedContentService>();
            if (string.IsNullOrWhiteSpace(chapterId))
            {
                chapterId = await GenerateDefaultNextChapterIdAsync(ct).ConfigureAwait(false);
            }

            await EnsureVolumeExistsForRewriteAsync(ct, chapterId).ConfigureAwait(false);

            var _extParsed = ChapterParserHelper.ParseChapterId(chapterId);
            if (_extParsed.HasValue && _extParsed.Value.chapterNumber == 1 && _extParsed.Value.volumeNumber > 1)
            {
                var _extPrevVol = _extParsed.Value.volumeNumber - 1;
                try
                {
                    var _extArchiveStore = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.VolumeFactArchiveStore>();
                    var _extPrevArchives = await _extArchiveStore.GetPreviousArchivesAsync(_extParsed.Value.volumeNumber).ConfigureAwait(false);
                    if (!_extPrevArchives.Any(a => a.VolumeNumber == _extPrevVol))
                    {
                        TM.App.Log($"[WriterPlugin] 外部保存检测到新卷第1章，自动存档第{_extPrevVol}卷...");
                        var _extReconciler = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.ConsistencyReconciler>();
                        await _extReconciler.AutoArchiveVolumeIfNeededAsync(_extPrevVol).ConfigureAwait(false);
                        TM.App.Log($"[WriterPlugin] 第{_extPrevVol}卷自动存档完成");
                    }
                }
                catch (Exception _extArchiveEx)
                {
                    TM.App.Log($"[WriterPlugin] 外部保存第{_extPrevVol}卷自动存档失败（不阻断保存）: {_extArchiveEx.Message}");
                }
            }

            var savedContent = StripLeadingTitle(content ?? string.Empty);

            ct.ThrowIfCancellationRequested();
            var callback = ServiceLocator.Get<ContentGenerationCallback>();
            await callback.OnExternalContentSavedAsync(chapterId, savedContent).ConfigureAwait(false);

            var persisted = await contentService.GetChapterAsync(chapterId).ConfigureAwait(false) ?? savedContent;

            return new SavedChapterResult
            {
                ChapterId = chapterId,
                Title = title,
                SavedContent = persisted,
                DisplayContent = persisted
            };
        }

        private static void DebugLogOnce(string key, Exception ex)
            => TM.Framework.Common.Helpers.InfoLogDedup.DebugLogOnce(key, ex, "WriterPlugin");
    }
}
