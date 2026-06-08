using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Models;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Parsing;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Helpers;
using TM.Services.Modules.ProjectData.Models.Guides;

namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Mapping
{
    public class PlanModeMapper : IConversationMessageMapper
    {
        private sealed class ChapterPlanItem
        {
            public ContentGuideEntry Entry { get; init; } = default!;
            public int VolumeNumber { get; init; }
            public int ChapterNumber { get; init; }
        }
        private readonly IPlanParser _parser;
        private static readonly object NameMapLock = new();
        private static Dictionary<string, string>? _characterNameMap;
        private static Dictionary<string, string>? _locationNameMap;
        private static Dictionary<string, string>? _factionNameMap;
        private static Dictionary<string, string>? _plotRuleNameMap;
        private static readonly object ContentGuideCacheLock = new();
        private static DateTime _contentGuideWriteUtc;
        private static List<ChapterPlanItem>? _contentGuideChapterItems;
        private static readonly object PlanStepsCacheLock = new();
        private static string? _lastPlanInput;
        private static DateTime _lastPlanWriteUtc;
        private static IReadOnlyList<PlanStep>? _lastPlanSteps;
        private static bool _lastPlanNoMatch;
        private static IReadOnlyList<int> _lastPlanMissingNumbers = Array.Empty<int>();

        public PlanModeMapper(IPlanParser parser)
        {
            _parser = parser;
        }

        public static System.Threading.Tasks.Task PrewarmContentGuideCacheAsync()
        {
            return System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var guidesDir = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides");
                    var shardFiles = Directory.Exists(guidesDir)
                        ? Directory.GetFiles(guidesDir, "content_guide_vol*.json").OrderBy(f => f).ToArray()
                        : Array.Empty<string>();
                    if (shardFiles.Length == 0)
                    {
                        var legacy = Path.Combine(guidesDir, "content_guide.json");
                        if (!File.Exists(legacy)) return;
                        shardFiles = new[] { legacy };
                    }

                    var writeUtc = shardFiles.Max(f => File.GetLastWriteTimeUtc(f));
                    lock (ContentGuideCacheLock)
                    {
                        if (_contentGuideChapterItems != null && _contentGuideWriteUtc == writeUtc)
                            return;
                    }

                    var mergedEntries = new Dictionary<string, ContentGuideEntry>();
                    foreach (var shardFile in shardFiles)
                    {
                        var json = await File.ReadAllTextAsync(shardFile).ConfigureAwait(false);
                        var shard = JsonSerializer.Deserialize<ContentGuide>(json, JsonHelper.Default);
                        if (shard?.Chapters != null)
                            foreach (var (k, v) in shard.Chapters)
                                mergedEntries[k] = v;
                    }

                    if (mergedEntries.Count == 0) return;

                    var chapters = mergedEntries.Values
                        .Select(entry =>
                        {
                            var parsed = ChapterParserHelper.ParseChapterId(entry.ChapterId);
                            return new ChapterPlanItem
                            {
                                Entry = entry,
                                VolumeNumber = parsed?.volumeNumber ?? 0,
                                ChapterNumber = ResolveChapterNumber(entry)
                            };
                        })
                        .OrderBy(item => item.VolumeNumber > 0 ? 0 : 1)
                        .ThenBy(item => item.VolumeNumber)
                        .ThenBy(item => item.ChapterNumber > 0 ? 0 : 1)
                        .ThenBy(item => item.ChapterNumber)
                        .ThenBy(item => item.Entry.ChapterId)
                        .ToList();

                    lock (ContentGuideCacheLock)
                    {
                        _contentGuideChapterItems = chapters;
                        _contentGuideWriteUtc = writeUtc;
                    }
                    TM.App.Log("[PlanModeMapper] 内容纲要缓存预热完成");
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[PlanModeMapper] 内容纲要缓存预热失败（非致命）: {ex.Message}");
                }
            });
        }

        public async System.Threading.Tasks.Task<ConversationMessage?> TryBuildPlanWithoutModelAsync(string userInput)
        {
            var (guideSteps, usedContentGuide, noMatch, missingNumbers) = await BuildPlanFromContentGuideAsync(userInput).ConfigureAwait(false);
            if (!usedContentGuide)
                return null;

            var steps = guideSteps ?? (IReadOnlyList<PlanStep>)Array.Empty<PlanStep>();
            var pseudoThinking = BuildPseudoThinking(userInput, steps, noMatch, missingNumbers);

            var message = new ConversationMessage
            {
                Role = Microsoft.SemanticKernel.ChatCompletion.AuthorRole.Assistant,
                Timestamp = DateTime.Now,
                AnalysisRaw = pseudoThinking
            };

            message.Payload = new PlanPayload
            {
                Steps = steps,
                RawContent = "[基于打包数据直接生成计划]"
            };

            if (usedContentGuide && noMatch)
                message.Summary = "[提示] 未匹配到章节，请检查@续写/章节号是否存在，或重新打包后再试。";
            else
                message.Summary = GenerateSummary(message);

            TM.App.Log($"[PlanModeMapper] 基于打包数据跳过模型调用，直接生成计划");
            return message;
        }

        private static string BuildPseudoThinking(
            string userInput, IReadOnlyList<PlanStep> steps, bool noMatch,
            IReadOnlyList<int>? missingNumbers = null)
        {
            var sb = new StringBuilder();
            var trimmedInput = userInput.Length > 60 ? userInput[..60] + "..." : userInput;

            sb.AppendLine($"好的，用户的请求是「{trimmedInput}」，让我先理解一下意图。");
            sb.AppendLine();

            if (noMatch)
            {
                sb.AppendLine("我检查了本地的内容纲要数据，但没有找到与这个请求匹配的章节信息。可能是章节编号不存在，或者纲要数据需要重新打包更新。");
                sb.AppendLine();
                sb.AppendLine("我无法为这个请求生成有效的执行计划，需要用户确认章节信息后重试。");
            }
            else if (steps.Count > 0)
            {
                sb.AppendLine("看了一下本地的内容纲要，里面已经有完整的章节规划数据了。不需要我从零开始分析和拆解任务，直接基于已有的规划来构建执行计划就行。");
                sb.AppendLine();

                if (steps.Count == 1)
                {
                    var step = steps[0];
                    sb.AppendLine($"匹配到的章节是「{step.Title}」，这是一个单章任务，直接作为一个执行步骤。");
                }
                else
                {
                    sb.AppendLine($"一共匹配到 {steps.Count} 个章节，按顺序排列：");
                    foreach (var step in steps)
                    {
                        sb.AppendLine($"- {step.Title}");
                    }
                }

                sb.AppendLine();
                sb.AppendLine("每个章节的场景结构、角色关联、剧情要素都已经在纲要里定义好了，可以直接组装成执行计划。");

                if (missingNumbers != null && missingNumbers.Count > 0)
                {
                    sb.AppendLine();
                    var missingStr = missingNumbers.Count <= 10
                        ? string.Join("、", missingNumbers.Select(n => $"第{n}章"))
                        : string.Join("、", missingNumbers.Take(10).Select(n => $"第{n}章")) + $" 等共 {missingNumbers.Count} 章";
                    sb.AppendLine($"[注意] {missingStr} 未在打包数据中找到（可能尚未创建章节规划或需要重新打包），已自动跳过。");
                }
            }

            return sb.ToString();
        }

        private static IReadOnlyList<int> ComputeRequestedChapterNumbers(string userInput)
        {
            if (string.IsNullOrWhiteSpace(userInput)) return Array.Empty<int>();
            var ranges = ChapterParserHelper.ParseChapterRanges(userInput);
            if (ranges?.Count > 0)
            {
                var nums = new HashSet<int>();
                foreach (var (s, e) in ranges)
                    for (var i = s; i <= e; i++) nums.Add(i);
                return nums.OrderBy(n => n).ToList();
            }
            var (start, end) = ChapterParserHelper.ParseChapterRange(userInput) ?? (0, 0);
            if (start > 0 && end >= start)
                return Enumerable.Range(start, end - start + 1).ToList();
            var list = ChapterParserHelper.ParseChapterNumberList(userInput);
            if (list?.Count > 0) return list;
            var (_, ch) = ChapterParserHelper.ParseFromNaturalLanguage(userInput);
            if (ch.HasValue && ch.Value > 0) return new[] { ch.Value };
            return Array.Empty<int>();
        }

        private static async System.Threading.Tasks.Task<(IReadOnlyList<PlanStep>? steps, bool usedContentGuide, bool noMatch, IReadOnlyList<int> missingNumbers)> BuildPlanFromContentGuideAsync(string userInput)
        {
            bool usedContentGuide = false;
            bool noMatch = false;
            IReadOnlyList<int> missingNumbers = Array.Empty<int>();
            try
            {
                var guidesDir = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides");
                var shardFiles = Directory.Exists(guidesDir)
                    ? Directory.GetFiles(guidesDir, "content_guide_vol*.json").OrderBy(f => f).ToArray()
                    : Array.Empty<string>();
                if (shardFiles.Length == 0)
                {
                    var legacy = Path.Combine(guidesDir, "content_guide.json");
                    if (!File.Exists(legacy))
                    {
                        TM.App.Log("[PlanModeMapper] content_guide 数据不存在，回退到模型计划");
                        return (null, usedContentGuide, noMatch, missingNumbers);
                    }
                    shardFiles = new[] { legacy };
                }

                usedContentGuide = true;

                List<ChapterPlanItem>? chapters = null;
                var writeUtc = shardFiles.Max(f => File.GetLastWriteTimeUtc(f));

                lock (PlanStepsCacheLock)
                {
                    if (!string.IsNullOrWhiteSpace(_lastPlanInput)
                        && _lastPlanInput == userInput
                        && _lastPlanWriteUtc == writeUtc
                        && _lastPlanSteps != null)
                    {
                        noMatch = _lastPlanNoMatch;
                        missingNumbers = _lastPlanMissingNumbers;
                        return (_lastPlanSteps, usedContentGuide, noMatch, missingNumbers);
                    }
                }
                lock (ContentGuideCacheLock)
                {
                    if (_contentGuideChapterItems != null && _contentGuideWriteUtc == writeUtc)
                        chapters = _contentGuideChapterItems;
                }

                if (chapters == null)
                {
                    var mergedEntries = new Dictionary<string, ContentGuideEntry>();
                    foreach (var shardFile in shardFiles)
                    {
                        var json = await File.ReadAllTextAsync(shardFile).ConfigureAwait(false);
                        var shard = JsonSerializer.Deserialize<ContentGuide>(json, JsonHelper.Default);
                        if (shard?.Chapters != null)
                            foreach (var (k, v) in shard.Chapters)
                                mergedEntries[k] = v;
                    }

                    if (mergedEntries.Count == 0)
                    {
                        TM.App.Log("[PlanModeMapper] content_guide 无章节数据，回退到模型计划");
                        return (null, usedContentGuide, noMatch, missingNumbers);
                    }

                    chapters = mergedEntries.Values
                        .Select(entry =>
                        {
                            var parsed = ChapterParserHelper.ParseChapterId(entry.ChapterId);
                            var volumeNumber = parsed?.volumeNumber ?? 0;
                            var chapterNumber = ResolveChapterNumber(entry);
                            return new ChapterPlanItem { Entry = entry, VolumeNumber = volumeNumber, ChapterNumber = chapterNumber };
                        })
                        .OrderBy(item => item.VolumeNumber > 0 ? 0 : 1)
                        .ThenBy(item => item.VolumeNumber)
                        .ThenBy(item => item.ChapterNumber > 0 ? 0 : 1)
                        .ThenBy(item => item.ChapterNumber)
                        .ThenBy(item => item.Entry.ChapterId)
                        .ToList();

                    lock (ContentGuideCacheLock) { _contentGuideChapterItems = chapters; _contentGuideWriteUtc = writeUtc; }
                }

                var filtered = ApplyChapterFilter(chapters, userInput);
                if (filtered.Count == 0)
                {
                    TM.App.Log("[PlanModeMapper] 未匹配到章节，保持空计划（不回退模型计划）");
                    noMatch = true;
                    lock (PlanStepsCacheLock)
                    {
                        _lastPlanInput = userInput; _lastPlanWriteUtc = writeUtc;
                        _lastPlanSteps = Array.Empty<PlanStep>(); _lastPlanNoMatch = true;
                        _lastPlanMissingNumbers = Array.Empty<int>();
                    }
                    return (Array.Empty<PlanStep>(), usedContentGuide, noMatch, missingNumbers);
                }

                var requestedNumbers = ComputeRequestedChapterNumbers(userInput);
                var missing = Array.Empty<int>() as IReadOnlyList<int>;
                if (requestedNumbers.Count > 0)
                {
                    var matchedSet = new HashSet<int>(filtered.Select(item => item.ChapterNumber).Where(n => n > 0));
                    missing = requestedNumbers.Where(n => !matchedSet.Contains(n)).ToList();
                    if (missing.Count > 0)
                        TM.App.Log($"[PlanModeMapper] 缺失章节：{string.Join(",", missing)}");
                }
                missingNumbers = missing;

                var steps = new List<PlanStep>();
                var index = 1;
                foreach (var item in filtered)
                {
                    var entry = item.Entry;
                    var chapterNumber = item.ChapterNumber;
                    var title = BuildStepTitle(entry, chapterNumber);
                    var detail = await BuildStepDetailAsync(entry, chapterNumber).ConfigureAwait(false);
                    steps.Add(new PlanStep { Index = index++, Title = title, Detail = detail, ChapterNumber = chapterNumber });
                }

                lock (PlanStepsCacheLock)
                {
                    _lastPlanInput = userInput; _lastPlanWriteUtc = writeUtc;
                    _lastPlanSteps = steps; _lastPlanNoMatch = false; _lastPlanMissingNumbers = missing;
                }

                TM.App.Log($"[PlanModeMapper] 基于打包数据生成计划步骤：{steps.Count} 章");
                return (steps, usedContentGuide, noMatch, missingNumbers);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PlanModeMapper] 基于打包数据生成计划失败: {ex.Message}");
                return (null, usedContentGuide, noMatch, missingNumbers);
            }
        }

        private static int ResolveChapterNumber(ContentGuideEntry entry)
        {
            if (entry.ChapterNumber > 0)
            {
                return entry.ChapterNumber;
            }

            var parsed = ChapterParserHelper.ParseChapterId(entry.ChapterId);
            if (parsed.HasValue && parsed.Value.chapterNumber > 0)
            {
                return parsed.Value.chapterNumber;
            }

            var fromSuffix = ChapterParserHelper.ExtractChapterNumberFromSuffix(entry.ChapterId);
            if (fromSuffix > 0)
            {
                return fromSuffix;
            }

            if (!string.IsNullOrWhiteSpace(entry.Title))
            {
                var (number, _) = ChapterParserHelper.ExtractChapterParts(entry.Title);
                if (number.HasValue && number.Value > 0)
                {
                    return number.Value;
                }

                var (_, chapter) = ChapterParserHelper.ParseFromNaturalLanguage(entry.Title);
                if (chapter.HasValue && chapter.Value > 0)
                {
                    return chapter.Value;
                }
            }

            return 0;
        }

        private static string BuildStepTitle(ContentGuideEntry entry, int chapterNumber)
        {
            var title = string.IsNullOrWhiteSpace(entry.Title)
                ? entry.Scenes?.FirstOrDefault()?.Title
                : entry.Title.Trim();

            if (string.IsNullOrWhiteSpace(title))
            {
                title = chapterNumber > 0 ? $"第{chapterNumber}章" : entry.ChapterId;
            }

            if (chapterNumber > 0 && !ChapterParserHelper.IsChapterTitle(title))
            {
                return $"第{chapterNumber}章 · {title}";
            }

            return title;
        }

        private static async System.Threading.Tasks.Task<string> BuildStepDetailAsync(ContentGuideEntry entry, int chapterNumber)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"    章节ID: {entry.ChapterId}");
            if (entry.ContextIds != null)
            {
                AppendIdList(sb, "    章节蓝图ID", entry.ContextIds.ChapterBlueprint);
            }

            sb.AppendLine();

            if (entry.ContextIds != null)
            {
                var hasNames = (entry.ContextIds.Characters?.Count ?? 0) > 0
                    || (entry.ContextIds.Locations?.Count ?? 0) > 0
                    || (entry.ContextIds.Factions?.Count ?? 0) > 0
                    || (entry.ContextIds.PlotRules?.Count ?? 0) > 0;
                if (hasNames)
                {
                    var nameMaps = await GetNameMappingsAsync().ConfigureAwait(false);
                    AppendNameList(sb, "    涉及角色", entry.ContextIds.Characters, nameMaps.characters);
                    AppendNameList(sb, "    涉及地点", entry.ContextIds.Locations, nameMaps.locations);
                    AppendNameList(sb, "    涉及势力", entry.ContextIds.Factions, nameMaps.factions);
                    AppendNameList(sb, "    涉及剧情", entry.ContextIds.PlotRules, nameMaps.plotRules);
                }
            }

            sb.AppendLine();
            sb.AppendLine("章节信息:");

            if (!string.IsNullOrWhiteSpace(entry.Title)) sb.AppendLine($"    章节主题: {entry.Title.Trim()}");
            if (!string.IsNullOrWhiteSpace(entry.Summary)) sb.AppendLine($"    摘要: {entry.Summary.Trim()}");
            if (!string.IsNullOrWhiteSpace(entry.MainGoal)) sb.AppendLine($"    主目标: {entry.MainGoal.Trim()}");
            if (!string.IsNullOrWhiteSpace(entry.WorldInfoDrop)) sb.AppendLine($"    世界信息: {entry.WorldInfoDrop.Trim()}");
            if (!string.IsNullOrWhiteSpace(entry.ChapterTheme)) sb.AppendLine($"    主题: {entry.ChapterTheme.Trim()}");
            if (!string.IsNullOrWhiteSpace(entry.KeyTurn)) sb.AppendLine($"    关键转折: {entry.KeyTurn.Trim()}");
            if (!string.IsNullOrWhiteSpace(entry.CharacterArcProgress)) sb.AppendLine($"    角色弧线进展: {entry.CharacterArcProgress.Trim()}");
            if (!string.IsNullOrWhiteSpace(entry.Hook)) sb.AppendLine($"    钉子: {entry.Hook.Trim()}");
            if (!string.IsNullOrWhiteSpace(entry.Foreshadowing)) sb.AppendLine($"    伏笔: {entry.Foreshadowing.Trim()}");

            if (entry.Scenes != null && entry.Scenes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("场景:");
                foreach (var scene in entry.Scenes.OrderBy(s => s.SceneNumber))
                {
                    var sceneTitle = string.IsNullOrWhiteSpace(scene.Title) ? "" : scene.Title.Trim();
                    var pov = string.IsNullOrWhiteSpace(scene.PovCharacter) ? string.Empty : $" | 视角: {scene.PovCharacter.Trim()}";
                    sb.AppendLine($"    场景{scene.SceneNumber}: {sceneTitle}{pov}");
                    if (!string.IsNullOrWhiteSpace(scene.Purpose)) sb.AppendLine($"    目的: {scene.Purpose.Trim()}");
                    AppendScenePart(sb, "起", scene.Opening);
                    AppendScenePart(sb, "承", scene.Development);
                    AppendScenePart(sb, "转", scene.Turning);
                    AppendScenePart(sb, "合", scene.Ending);
                    if (!string.IsNullOrWhiteSpace(scene.InfoDrop)) { sb.AppendLine(); sb.AppendLine($"    信息投放: {scene.InfoDrop.Trim()}"); }
                }
            }

            return sb.ToString().Trim();
        }

        private static List<ChapterPlanItem> ApplyChapterFilter(
            List<ChapterPlanItem> chapters,
            string userInput)
        {
            if (string.IsNullOrWhiteSpace(userInput))
            {
                return chapters;
            }

            var continueChapterId = ChapterDirectiveParser.ParseSourceChapterId(userInput);
            if (!string.IsNullOrWhiteSpace(continueChapterId) && IsValidChapterId(continueChapterId))
            {
                var strippedInput = StripContinueDirective(userInput, continueChapterId);
                var hasExplicitRange = !string.IsNullOrWhiteSpace(strippedInput)
                    && (ChapterParserHelper.ParseChapterRange(strippedInput).HasValue
                        || (ChapterParserHelper.ParseChapterRanges(strippedInput)?.Count ?? 0) > 0
                        || (ChapterParserHelper.ParseChapterNumberList(strippedInput)?.Count ?? 0) > 0);

                if (!hasExplicitRange)
                {
                    ChapterPlanItem? FindNextChapter()
                    {
                        var parsed = ChapterParserHelper.ParseChapterId(continueChapterId);
                        int? volFromId = parsed?.volumeNumber;
                        int? chFromId = parsed?.chapterNumber;
                        if (!volFromId.HasValue || !chFromId.HasValue)
                        {
                            var (volNl, chNl) = ChapterParserHelper.ParseFromNaturalLanguage(continueChapterId);
                            volFromId = volNl;
                            chFromId = chNl;
                        }

                        ChapterPlanItem? current = null;
                        if (volFromId.HasValue && chFromId.HasValue)
                        {
                            current = chapters.FirstOrDefault(item =>
                                item.VolumeNumber == volFromId.Value && item.ChapterNumber == chFromId.Value);
                        }

                        current ??= chapters.FirstOrDefault(item =>
                            string.Equals(item.Entry.ChapterId, continueChapterId, StringComparison.OrdinalIgnoreCase));

                        if (current == null)
                            return null;

                        var index = chapters.IndexOf(current);
                        if (index >= 0 && index + 1 < chapters.Count)
                            return chapters[index + 1];

                        return null;
                    }

                    var nextChapter = FindNextChapter();
                    if (nextChapter != null)
                    {
                        return new List<ChapterPlanItem> { nextChapter };
                    }

                    TM.App.Log($"[PlanModeMapper] @续写未找到下一章: {continueChapterId}");
                    return new List<ChapterPlanItem>();
                }

                TM.App.Log($"[PlanModeMapper] @续写含显式章节范围，忽略续写跳转，使用范围过滤: {strippedInput}");
                userInput = strippedInput;
            }

            var (vol, ch) = ChapterParserHelper.ParseFromNaturalLanguage(userInput);
            var (start, end) = ChapterParserHelper.ParseChapterRange(userInput) ?? (0, 0);
            var ranges = ChapterParserHelper.ParseChapterRanges(userInput);
            var list = ChapterParserHelper.ParseChapterNumberList(userInput);
            var targetNumbers = new HashSet<int>();

            if (ranges != null && ranges.Count > 0)
            {
                foreach (var (rangeStart, rangeEnd) in ranges)
                {
                    for (var i = rangeStart; i <= rangeEnd; i++)
                    {
                        targetNumbers.Add(i);
                    }
                }
            }
            else if (start > 0 && end >= start)
            {
                for (var i = start; i <= end; i++)
                {
                    targetNumbers.Add(i);
                }
            }
            else if (list != null && list.Count > 0)
            {
                foreach (var n in list)
                {
                    if (n > 0)
                    {
                        targetNumbers.Add(n);
                    }
                }
            }
            else if (vol.HasValue && ch.HasValue)
            {
                targetNumbers.Add(ch.Value);
            }
            else if (ch.HasValue)
            {
                targetNumbers.Add(ch.Value);
            }

            if (targetNumbers.Count == 0)
            {
                return chapters;
            }

            var byNumber = chapters
                .Where(item => targetNumbers.Contains(item.ChapterNumber))
                .ToList();

            if (byNumber.Count > 0)
            {
                return byNumber;
            }

            var minNumber = targetNumbers.Min();
            var maxNumber = targetNumbers.Max();
            var startIndex = Math.Max(0, minNumber - 1);
            var count = Math.Max(0, maxNumber - minNumber + 1);
            if (startIndex >= chapters.Count || count == 0)
            {
                return new List<ChapterPlanItem>();
            }

            return chapters.Skip(startIndex).Take(count).ToList();
        }

        private static bool IsValidChapterId(string chapterId)
        {
            foreach (var c in chapterId)
            {
                if (c >= '\u4e00' && c <= '\u9fff') return false;
            }
            return true;
        }

        private static string StripContinueDirective(string input, string chapterId)
        {
            var escapedId = System.Text.RegularExpressions.Regex.Escape(chapterId);
            return System.Text.RegularExpressions.Regex.Replace(
                input,
                $@"@(?:续写|continue)[:：\s]*{escapedId}",
                "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
        }

        private static void AppendIdList(StringBuilder sb, string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                sb.AppendLine($"{label}: {value.Trim()}");
            }
        }

        private static void AppendIdList(StringBuilder sb, string label, IReadOnlyCollection<string>? values)
        {
            if (values == null || values.Count == 0)
            {
                return;
            }

            sb.AppendLine($"{label}: {string.Join(", ", values)}");
        }

        private static void AppendNameList(
            StringBuilder sb,
            string label,
            IReadOnlyCollection<string>? values,
            IReadOnlyDictionary<string, string> nameMap)
        {
            if (values == null || values.Count == 0)
            {
                return;
            }

            var names = values
                .Select(id => nameMap.TryGetValue(id, out var name) ? name : id)
                .ToList();

            sb.AppendLine($"{label}: {string.Join(", ", names)}");
        }

        private static async System.Threading.Tasks.Task<(IReadOnlyDictionary<string, string> characters,
            IReadOnlyDictionary<string, string> locations,
            IReadOnlyDictionary<string, string> factions,
            IReadOnlyDictionary<string, string> plotRules)> GetNameMappingsAsync()
        {
            if (_characterNameMap != null && _locationNameMap != null && _factionNameMap != null && _plotRuleNameMap != null)
                return (_characterNameMap, _locationNameMap, _factionNameMap, _plotRuleNameMap);

            var charMap = new Dictionary<string, string>();
            var locMap = new Dictionary<string, string>();
            var facMap = new Dictionary<string, string>();
            var plotMap = new Dictionary<string, string>();

            try
            {
                var elementsPath = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "Design", "elements.json");
                if (File.Exists(elementsPath))
                {
                    var json = await File.ReadAllTextAsync(elementsPath).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("data", out var data))
                    {
                        if (data.TryGetProperty("characterrules", out var charModule) &&
                            charModule.TryGetProperty("character_rules", out var characters))
                            foreach (var item in characters.EnumerateArray())
                            {
                                var id = item.TryGetProperty("Id", out var idP) ? idP.GetString() : null;
                                var name = item.TryGetProperty("Name", out var nameP) ? nameP.GetString() : null;
                                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name)) charMap[id] = name;
                            }
                        if (data.TryGetProperty("locationrules", out var locModule) &&
                            locModule.TryGetProperty("location_rules", out var locations))
                            foreach (var item in locations.EnumerateArray())
                            {
                                var id = item.TryGetProperty("Id", out var idP) ? idP.GetString() : null;
                                var name = item.TryGetProperty("Name", out var nameP) ? nameP.GetString() : null;
                                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name)) locMap[id] = name;
                            }
                        if (data.TryGetProperty("factionrules", out var facModule) &&
                            facModule.TryGetProperty("faction_rules", out var factions))
                            foreach (var item in factions.EnumerateArray())
                            {
                                var id = item.TryGetProperty("Id", out var idP) ? idP.GetString() : null;
                                var name = item.TryGetProperty("Name", out var nameP) ? nameP.GetString() : null;
                                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name)) facMap[id] = name;
                            }
                        if (data.TryGetProperty("plotrules", out var plotModule) &&
                            plotModule.TryGetProperty("plot_rules", out var plotRules))
                            foreach (var item in plotRules.EnumerateArray())
                            {
                                var id = item.TryGetProperty("Id", out var idP) ? idP.GetString() : null;
                                var name = item.TryGetProperty("Name", out var nameP) ? nameP.GetString() : null;
                                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name)) plotMap[id] = name;
                            }
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PlanModeMapper] 名称映射加载失败: {ex.Message}");
            }

            lock (NameMapLock)
            {
                if (_characterNameMap == null)
                {
                    _characterNameMap = charMap;
                    _locationNameMap = locMap;
                    _factionNameMap = facMap;
                    _plotRuleNameMap = plotMap;
                }
                return (_characterNameMap, _locationNameMap ?? new(), _factionNameMap ?? new(), _plotRuleNameMap ?? new());
            }
        }

        private static void AppendScenePart(StringBuilder sb, string label, string? content)
        {
            if (!string.IsNullOrWhiteSpace(content))
            {
                sb.AppendLine($"    {label}: {content.Trim()}");
            }
        }

        public async System.Threading.Tasks.Task<ConversationMessage> MapFromStreamingResultAsync(
            string userInput,
            string rawContent,
            string? thinking)
        {
            var message = new ConversationMessage
            {
                Role = Microsoft.SemanticKernel.ChatCompletion.AuthorRole.Assistant,
                Timestamp = DateTime.Now
            };

            message.AnalysisRaw = thinking ?? string.Empty;
            message.AnalysisBlocks = ThinkingBlockParser.Parse(thinking);

            var (guideSteps, usedContentGuide, noMatch, _) = await BuildPlanFromContentGuideAsync(userInput).ConfigureAwait(false);
            IReadOnlyList<PlanStep> normalizedSteps;
            if (guideSteps != null)
            {
                normalizedSteps = guideSteps;
            }
            else
            {
                var parsedSteps = _parser.Parse(rawContent);

                if (SingleChapterTaskDetector.IsSingleChapterTask(userInput))
                {
                    normalizedSteps = CreateSingleStepPlan(userInput);
                    TM.App.Log($"[PlanModeMapper] 单章节任务，强制 1 个步骤");
                }
                else if (parsedSteps.Count > 0)
                {
                    normalizedSteps = PlanStepNormalizer.Normalize(userInput, rawContent, parsedSteps);
                }
                else
                {
                    normalizedSteps = Array.Empty<PlanStep>();
                }
            }

            message.Payload = new PlanPayload
            {
                Steps = normalizedSteps,
                RawContent = rawContent
            };

            if (usedContentGuide && noMatch)
            {
                message.Summary = "[提示] 未匹配到章节，请检查@续写/章节号是否存在，或重新打包后再试。";
            }
            else
            {
                message.Summary = GenerateSummary(message);
            }

            return message;
        }

        public string GenerateSummary(ConversationMessage message)
        {
            if (message.Payload is PlanPayload planPayload && planPayload.Steps.Count > 0)
            {
                return $"已生成创作计划，共 {planPayload.Steps.Count} 个步骤。\n请在左侧「执行计划」面板查看详细步骤，确认后点击「开始执行」。";
            }

            return "[提示] 计划格式解析失败，请重新描述您的需求。\n\n提示：请明确说明要执行的任务，例如「生成第7章」或「批量生成3章」。";
        }

        private static IReadOnlyList<PlanStep> CreateSingleStepPlan(string userInput)
        {
            var title = userInput.Length > 30
                ? userInput.Substring(0, 30) + "..."
                : userInput;

            return new List<PlanStep>
            {
                new PlanStep
                {
                    Index = 1,
                    Title = title,
                    Detail = userInput
                }
            };
        }
    }
}
