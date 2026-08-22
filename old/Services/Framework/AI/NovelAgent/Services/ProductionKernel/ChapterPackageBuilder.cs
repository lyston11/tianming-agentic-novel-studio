using System;
using System.Collections.Generic;
using System.Linq;
using TM.Framework.Common.Helpers;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel
{
    public sealed class ChapterPackageBuilder : IChapterPackageBuilder
    {
        public ChapterContextPackageSummary Build(ChapterPackageBuildRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var run = request.Run ?? new NovelAgentRun();
            var document = request.Document ?? new StoryBibleDocument();
            var storyState = request.StoryState ?? new StoryStateSnapshot();
            var contentContext = request.ContentContext
                ?? throw new InvalidOperationException("ContentTaskContext is required to build a Tianming production package；缺少真实项目上下文时禁止构建章节生产包。");

            var package = new ChapterContextPackageSummary
            {
                ChapterId = run.TargetChapterId,
                Status = "context_ready:real_project_data",
                WorldRules = Merge(
                    contentContext?.WorldRules.Select(w => FirstNonEmpty(w.Name, w.GetCoreSummary(), w.Description)),
                    storyState.WorldRules).Take(12).ToList(),
                CharacterStates = Merge(
                    contentContext?.Characters.Select(c => $"{FirstNonEmpty(c.Name, c.Id)}：{FirstNonEmpty(c.GetCoreSummary(), c.Description)}"),
                    storyState.CharacterStates,
                    storyState.CharacterLedgerItems).Take(16).ToList(),
                ActiveConflicts = storyState.ActiveConflicts.Take(12).ToList(),
                ActiveForeshadowing = storyState.ActiveForeshadowing
                    .Concat(storyState.ForeshadowLedgerItems)
                    .Where(HasText)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(16)
                    .ToList(),
                ChapterBlueprints = Merge(
                    contentContext?.Blueprints.Select(b => FirstNonEmpty(b.Name, b.GetCoreSummary(), b.Description)),
                    contentContext?.Scenes.Select(s => FirstNonEmpty(
                        s.Title,
                        s.Purpose,
                        string.Join(" / ", new[] { s.Opening, s.Development, s.Turning, s.Ending }.Where(HasText)))),
                    BuildBlueprintLines(run)).Take(12).ToList(),
                PreviousSummaries = Merge(
                    contentContext?.PreviousChapterSummaries.Select(s => $"{s.ChapterId}: {s.Summary}"),
                    contentContext?.MdPreviousChapterSummaries.Select(s => $"{s.ChapterId}: {s.Summary}"),
                    new[] { contentContext?.PreviousChapterSummary, storyState.PreviousChapterSummary }).Take(12).ToList(),
                LongDistanceRecall = Merge(
                    contentContext?.LongDistanceRecallFragments.Select(f => $"{f.ChapterId}({f.Score:0.00}): {f.Content}"),
                    storyState.LongDistanceRecall,
                    storyState.SimilarContentFragments).Take(12).ToList(),
                RagQueries = storyState.RagSearchQueries
                    .Where(HasText)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(8)
                    .ToList(),
                Warnings = Merge(contentContext?.StateDivergenceWarnings, storyState.Warnings).Take(12).ToList()
            };

            if (document.Constitution != null)
            {
                package.WorldRules.Insert(0, $"Story Bible：{document.Constitution.Genre}/{document.Constitution.SubGenre}；{document.Constitution.WorldCoreRule}");
                package.ActiveConflicts.Insert(0, $"主冲突引擎：{document.Constitution.MainConflictEngine}");
            }

            ApplyContinuityPack(document, run, package);
            ApplyCanonLedger(document, package);
            Normalize(package);
            return package;
        }

        private static void ApplyCanonLedger(
            StoryBibleDocument document,
            ChapterContextPackageSummary package)
        {
            var canonEntries = document.CanonLedger
                .Where(entry => entry.Status == CanonLedgerEntryStatus.Canon)
                .Where(entry => HasText(entry.Title) || HasText(entry.Content))
                .OrderByDescending(entry => entry.UpdatedAt)
                .ThenByDescending(entry => entry.CreatedAt)
                .Take(24)
                .ToList();

            foreach (var entry in canonEntries)
            {
                var line = FormatCanonLedgerLine(entry);
                if (!HasText(line))
                    continue;

                switch (entry.Type)
                {
                    case CanonLedgerEntryType.CharacterRule:
                        package.CharacterStates.Add(line);
                        break;
                    case CanonLedgerEntryType.Foreshadowing:
                        package.ActiveForeshadowing.Add(line);
                        break;
                    case CanonLedgerEntryType.PlotRule:
                        package.ChapterBlueprints.Add(line);
                        break;
                    default:
                        package.WorldRules.Add(line);
                        break;
                }
            }
        }

        private static string FormatCanonLedgerLine(CanonLedgerEntry entry)
        {
            var title = entry.Title?.Trim() ?? string.Empty;
            var content = entry.Content?.Trim() ?? string.Empty;
            if (!HasText(title) && !HasText(content))
                return string.Empty;
            if (!HasText(title))
                return $"Story Bible Canon：{content}";
            if (!HasText(content))
                return $"Story Bible Canon：{title}";
            return $"Story Bible Canon：{title}：{content}";
        }

        private static void ApplyContinuityPack(
            StoryBibleDocument document,
            NovelAgentRun run,
            ChapterContextPackageSummary package)
        {
            var currentNumber = ExtractChapterNumber(run.TargetChapterId);
            var previousFacts = document.ContinuityFacts
                .Where(f => !string.IsNullOrWhiteSpace(f.ChapterId))
                .Select(f => new { Facts = f, Number = ExtractChapterNumber(f.ChapterId) })
                .Where(x => x.Number > 0 && (currentNumber <= 0 || x.Number < currentNumber))
                .OrderByDescending(x => x.Number)
                .Take(3)
                .Select(x => x.Facts)
                .ToList();

            foreach (var facts in previousFacts)
            {
                foreach (var line in FormatContinuityFactLines(facts))
                    package.HardContinuityFacts.Add(line);

                if (HasText(facts.ProtagonistName))
                {
                    package.CharacterStates.Insert(0,
                        $"{facts.ProtagonistName}：{FirstNonEmpty(facts.ProtagonistIdentity, "主角")}；当前状态={facts.ProtagonistStatus}；位置={facts.CurrentLocation}；系统={facts.SystemState}；装备={facts.EquipmentState}");
                }

                if (HasText(facts.EndingState))
                    package.PreviousSummaries.Insert(0, $"{facts.ChapterId}: {facts.EndingState}");
            }

            var activeCharacters = document.CharacterLedger
                .Where(c => c.Status is CharacterLedgerStatus.Active
                    or CharacterLedgerStatus.GoalUpdated
                    or CharacterLedgerStatus.SecretSeeded
                    or CharacterLedgerStatus.RelationshipChanged
                    or CharacterLedgerStatus.AbilityChanged
                    or CharacterLedgerStatus.PsychologicalShifted
                    or CharacterLedgerStatus.BeliefShifted)
                .OrderByDescending(c => c.Importance)
                .ThenByDescending(c => c.UpdatedAt)
                .Take(8)
                .Select(c => $"{c.CharacterName}：{FirstNonEmpty(c.Role, c.IdentityState)}；{FirstNonEmpty(c.Summary, c.CurrentGoal, c.NextPressure)}");
            package.CharacterStates.AddRange(activeCharacters);

            var activeVolume = document.VolumeArcs
                .Select(v => new
                {
                    Plan = v,
                    Start = ExtractChapterNumber(v.StartChapterId),
                    End = ExtractChapterNumber(v.EndChapterId)
                })
                .Where(x => x.Start <= 0 || currentNumber <= 0 || currentNumber >= x.Start)
                .Where(x => x.End <= 0 || currentNumber <= 0 || currentNumber <= x.End)
                .OrderByDescending(x => x.Start)
                .Select(x => x.Plan)
                .FirstOrDefault();
            if (activeVolume != null)
            {
                package.WorldRules.Insert(0, $"当前卷目标：{FirstNonEmpty(activeVolume.Title, activeVolume.VolumeId)}；{activeVolume.VolumePromise}");
                if (HasText(activeVolume.ExitState))
                    package.ActiveConflicts.Insert(0, $"卷出口状态目标：{activeVolume.ExitState}");
            }
        }

        private static IEnumerable<string> FormatContinuityFactLines(ChapterContinuityFacts facts)
        {
            if (HasText(facts.ProtagonistName)) yield return $"主角姓名：{facts.ProtagonistName}";
            if (HasText(facts.ProtagonistIdentity)) yield return $"主角身份：{facts.ProtagonistIdentity}";
            if (HasText(facts.ProtagonistStatus)) yield return $"主角当前状态：{facts.ProtagonistStatus}";
            if (HasText(facts.CurrentLocation)) yield return $"当前位置：{facts.CurrentLocation}";
            if (HasText(facts.SystemState)) yield return $"系统状态：{facts.SystemState}";
            if (HasText(facts.EquipmentState)) yield return $"装备状态：{facts.EquipmentState}";
            foreach (var keyEvent in facts.KeyEvents.Where(HasText).Take(8))
                yield return $"已发生事件：{keyEvent}";
            if (HasText(facts.EndingState)) yield return $"上一章结尾状态：{facts.EndingState}";
            foreach (var carry in facts.NextChapterMustCarry.Where(IsNarrativeCarryLine).Take(8))
                yield return $"下一章必须承接：{StripCarryPrefix(carry)}";
        }

        private static IEnumerable<string> BuildBlueprintLines(NovelAgentRun run)
        {
            var brief = run.ChapterBrief;
            if (brief == null) yield break;
            if (HasText(brief.VolumeBeatRole)) yield return $"卷节拍：{brief.VolumeBeatRole}";
            if (HasText(brief.CoreIdea)) yield return $"核心创意：{brief.CoreIdea}";
            if (HasText(brief.ConflictMove)) yield return $"冲突推进：{brief.ConflictMove}";
            if (HasText(brief.CharacterChoice)) yield return $"角色选择：{brief.CharacterChoice}";
            if (HasText(brief.CostOrConsequence)) yield return $"代价后果：{brief.CostOrConsequence}";
            if (HasText(brief.ForeshadowingAction)) yield return $"伏笔动作：{brief.ForeshadowingAction}";
        }

        private static void Normalize(ChapterContextPackageSummary package)
        {
            package.WorldRules = NormalizeDistinct(package.WorldRules, 12);
            package.CharacterStates = NormalizeDistinct(package.CharacterStates, 18);
            package.ActiveConflicts = NormalizeDistinct(package.ActiveConflicts, 12);
            package.PreviousSummaries = NormalizeDistinct(package.PreviousSummaries, 12);
            package.HardContinuityFacts = NormalizeDistinct(package.HardContinuityFacts, 24);
        }

        private static List<string> NormalizeDistinct(IEnumerable<string> values, int take) =>
            values
                .Where(HasText)
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(take)
                .ToList();

        private static IEnumerable<string> Merge(params IEnumerable<string?>?[] sources) =>
            sources
                .Where(source => source != null)
                .SelectMany(source => source!)
                .Where(HasText)
                .Select(value => value!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase);

        private static int ExtractChapterNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            return ChapterParserHelper.ExtractChapterNumber(value);
        }

        private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

        private static string FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(HasText)?.Trim() ?? string.Empty;

        private static bool IsNarrativeCarryLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var value = line.Trim();
            if (value.StartsWith("上一章已提交", StringComparison.Ordinal) ||
                value.StartsWith("上一章章节ID", StringComparison.Ordinal) ||
                value.StartsWith("上一章评审结论", StringComparison.Ordinal) ||
                value.StartsWith("上一章门禁关注", StringComparison.Ordinal) ||
                value.Contains("章节ID", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private static string StripCarryPrefix(string line)
        {
            var value = line.Trim();
            const string prefix = "下一章必须承接：";
            return value.StartsWith(prefix, StringComparison.Ordinal)
                ? value[prefix.Length..].Trim()
                : value;
        }
    }
}
