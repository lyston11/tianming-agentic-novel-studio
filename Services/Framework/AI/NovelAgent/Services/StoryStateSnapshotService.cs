using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.Embedding;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Implementations.Indexing;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.TaskContexts;

namespace TM.Services.Framework.AI.NovelAgent.Services
{
    public sealed class StoryStateSnapshotService
    {
        private readonly IGuideContextService _guideContextService;
        private readonly ContentChunkSearchService _contentChunkSearchService;
        private readonly StoryBibleService _storyBibleService;
        private readonly ChapterEmbeddingIndex? _chapterEmbeddingIndex;
        private readonly IChunkEmbeddingIndex? _chunkEmbeddingIndex;
        private readonly IMicroEmbeddingService? _embeddingService;

        public StoryStateSnapshotService(
            IGuideContextService guideContextService,
            ContentChunkSearchService contentChunkSearchService,
            StoryBibleService storyBibleService,
            ChapterEmbeddingIndex? chapterEmbeddingIndex = null,
            IChunkEmbeddingIndex? chunkEmbeddingIndex = null,
            IMicroEmbeddingService? embeddingService = null)
        {
            _guideContextService = guideContextService;
            _contentChunkSearchService = contentChunkSearchService;
            _storyBibleService = storyBibleService;
            _chapterEmbeddingIndex = chapterEmbeddingIndex;
            _chunkEmbeddingIndex = chunkEmbeddingIndex;
            _embeddingService = embeddingService;
        }

        public async Task<StoryStateSnapshot> BuildForChapterAsync(
            string chapterId,
            CancellationToken ct = default)
        {
            var snapshot = new StoryStateSnapshot
            {
                ChapterId = chapterId?.Trim() ?? string.Empty
            };

            if (string.IsNullOrWhiteSpace(snapshot.ChapterId))
            {
                snapshot.Warnings.Add("章节ID为空，无法读取项目章节上下文。");
                return snapshot;
            }

            try
            {
                var context = await _guideContextService.BuildContentContextAsync(snapshot.ChapterId, ct)
                    .ConfigureAwait(false);
                if (context == null)
                {
                    snapshot.Warnings.Add("未能从 GuideContextService 构建章节上下文。");
                    return snapshot;
                }

                FillFromContentContext(snapshot, context);
                await FillFromForeshadowLedgerAsync(snapshot, ct).ConfigureAwait(false);
                await FillFromCharacterLedgerAsync(snapshot, ct).ConfigureAwait(false);
                await FillVectorRecallFragmentsAsync(snapshot, ct).ConfigureAwait(false);
                await FillSimilarContentFragmentsAsync(snapshot, ct).ConfigureAwait(false);
                return snapshot;
            }
            catch (Exception ex)
            {
                snapshot.Warnings.Add($"读取章节上下文失败：{ex.Message}");
                TM.App.Log($"[StoryStateSnapshotService] BuildForChapterAsync({snapshot.ChapterId}) 失败: {ex.Message}");
                return snapshot;
            }
        }

        private async Task FillFromForeshadowLedgerAsync(
            StoryStateSnapshot snapshot,
            CancellationToken ct)
        {
            var document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var chapterIndex = ExtractTrailingNumber(snapshot.ChapterId);
            var active = document.ForeshadowLedger
                .Where(f => f.Status is ForeshadowLedgerStatus.Planned
                    or ForeshadowLedgerStatus.Setup
                    or ForeshadowLedgerStatus.Reinforced
                    or ForeshadowLedgerStatus.Due)
                .OrderByDescending(f => IsDueForChapter(f, snapshot.ChapterId, chapterIndex))
                .ThenByDescending(f => f.Importance)
                .Take(10)
                .Select(f => FormatForeshadowLedgerItem(f, snapshot.ChapterId, chapterIndex))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            snapshot.ForeshadowLedgerItems.AddRange(active);
            snapshot.ActiveForeshadowing.AddRange(active);
            snapshot.ActiveForeshadowing = snapshot.ActiveForeshadowing
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(16)
                .ToList();
        }

        private async Task FillFromCharacterLedgerAsync(
            StoryStateSnapshot snapshot,
            CancellationToken ct)
        {
            var document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var active = document.CharacterLedger
                .Where(c => c.Status is CharacterLedgerStatus.Active
                    or CharacterLedgerStatus.GoalUpdated
                    or CharacterLedgerStatus.SecretSeeded
                    or CharacterLedgerStatus.RelationshipChanged
                    or CharacterLedgerStatus.AbilityChanged
                    or CharacterLedgerStatus.PsychologicalShifted
                    or CharacterLedgerStatus.BeliefShifted)
                .OrderByDescending(c => c.Importance)
                .ThenByDescending(c => c.UpdatedAt)
                .Take(12)
                .Select(FormatCharacterLedgerItem)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            snapshot.CharacterLedgerItems.AddRange(active);
            snapshot.CharacterStates.AddRange(active);
            snapshot.CharacterStates = snapshot.CharacterStates
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(18)
                .ToList();
        }

        private static void FillFromContentContext(StoryStateSnapshot snapshot, ContentTaskContext context)
        {
            snapshot.ChapterTitle = FirstNonEmpty(context.Title, context.ChapterPlan?.ChapterTitle);
            snapshot.ChapterGoal = FirstNonEmpty(context.ChapterPlan?.MainGoal, context.Summary);
            snapshot.ChapterTurn = FirstNonEmpty(context.ChapterPlan?.KeyTurn, context.ChapterPlan?.Hook);
            snapshot.ReaderExperienceGoal = context.ChapterPlan?.ReaderExperienceGoal ?? string.Empty;
            snapshot.PreviousChapterId = context.PreviousChapterId ?? string.Empty;
            snapshot.PreviousChapterSummary = FirstNonEmpty(
                context.PreviousChapterSummary,
                context.PreviousChapterSummaries.FirstOrDefault()?.Summary,
                context.MdPreviousChapterSummaries.FirstOrDefault()?.Summary);

            snapshot.ActiveConflicts.AddRange(context.FactSnapshot?.ConflictProgress
                .Where(c => !string.IsNullOrWhiteSpace(c.Name) || !string.IsNullOrWhiteSpace(c.Status))
                .Take(8)
                .Select(c => $"{c.Name}：{c.Status} {string.Join(" / ", c.RecentProgress.Take(2))}")
                ?? Enumerable.Empty<string>());

            snapshot.ActiveForeshadowing.AddRange(context.FactSnapshot?.ForeshadowingStatus
                .Where(f => !f.IsResolved)
                .Take(8)
                .Select(f => $"{f.Name}：setup={f.IsSetup}, overdue={f.IsOverdue}, payoff={f.PayoffChapterId ?? string.Empty}")
                ?? Enumerable.Empty<string>());

            snapshot.WorldRules.AddRange(context.WorldRules
                .Where(w => !string.IsNullOrWhiteSpace(w.Name) || !string.IsNullOrWhiteSpace(w.OneLineSummary))
                .Take(8)
                .Select(w => $"{w.Name}：{FirstNonEmpty(w.OneLineSummary, w.HardRules, w.PowerSystem)}"));

            snapshot.CharacterStates.AddRange(context.FactSnapshot?.CharacterStates
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .Take(8)
                .Select(c => $"{c.Name}：{FirstNonEmpty(c.Stage, c.Abilities, c.Relationships)}")
                ?? Enumerable.Empty<string>());

            snapshot.UsedPlotPatterns.AddRange(context.Blueprints
                .Where(b => !string.IsNullOrWhiteSpace(b.OneLineStructure) || !string.IsNullOrWhiteSpace(b.SceneTitle))
                .Take(8)
                .Select(b => FirstNonEmpty(b.OneLineStructure, b.SceneTitle, b.Turning)));

            snapshot.LongDistanceRecall.AddRange(context.LongDistanceRecallFragments
                .Where(f => !string.IsNullOrWhiteSpace(f.Content))
                .OrderByDescending(f => f.Score)
                .Take(8)
                .Select(f => $"{f.ChapterId}：{Trim(f.Content, 160)}"));

            snapshot.Warnings.AddRange(context.StateDivergenceWarnings.Where(w => !string.IsNullOrWhiteSpace(w)));
        }

        private async Task FillSimilarContentFragmentsAsync(
            StoryStateSnapshot snapshot,
            CancellationToken ct)
        {
            var queries = new[]
                {
                    snapshot.ChapterGoal,
                    snapshot.ChapterTurn,
                    snapshot.ReaderExperienceGoal,
                    snapshot.ActiveConflicts.FirstOrDefault(),
                    snapshot.ActiveForeshadowing.FirstOrDefault()
                }
                .Where(q => !string.IsNullOrWhiteSpace(q))
                .Select(q => q!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToList();

            snapshot.RagSearchQueries.AddRange(queries);

            foreach (var query in queries)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var hits = await _contentChunkSearchService.SearchAsync(query, topK: 3)
                        .ConfigureAwait(false);
                    snapshot.SimilarContentFragments.AddRange(hits
                        .Where(h => !string.Equals(h.ChapterId, snapshot.ChapterId, StringComparison.OrdinalIgnoreCase))
                        .Select(h => $"{h.ChapterId}@{h.Position} score={h.Score:0.###}：{Trim(h.Content, 180)}"));
                }
                catch (Exception ex)
                {
                    snapshot.Warnings.Add($"相似正文片段检索失败：{ex.Message}");
                    TM.App.Log($"[StoryStateSnapshotService] ContentChunkSearch({snapshot.ChapterId}) 失败: {ex.Message}");
                }
            }

            snapshot.SimilarContentFragments = snapshot.SimilarContentFragments
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();
        }

        private async Task FillVectorRecallFragmentsAsync(
            StoryStateSnapshot snapshot,
            CancellationToken ct)
        {
            if (_chapterEmbeddingIndex == null || _chunkEmbeddingIndex == null || _embeddingService == null)
                return;

            if (!_embeddingService.IsModelReady())
                return;

            var query = BuildVectorRecallQuery(snapshot);
            if (string.IsNullOrWhiteSpace(query))
                return;

            if (!snapshot.RagSearchQueries.Contains(query, StringComparer.OrdinalIgnoreCase))
                snapshot.RagSearchQueries.Add(query);

            try
            {
                await Task.WhenAll(
                    _chapterEmbeddingIndex.LoadAsync(ct),
                    _chunkEmbeddingIndex.LoadAsync(ct)).ConfigureAwait(false);

                if (_chapterEmbeddingIndex.Count == 0 || _chunkEmbeddingIndex.Count == 0)
                    return;

                var vector = await _embeddingService.EncodeAsync(query, EmbeddingMode.Query, ct)
                    .ConfigureAwait(false);
                if (vector == null || vector.Length == 0)
                    return;

                var coarse = await _chapterEmbeddingIndex.SearchAsync(vector, topK: 24, ct)
                    .ConfigureAwait(false);
                var chapterIds = coarse
                    .Where(h => !string.Equals(h.Key, snapshot.ChapterId, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(h.Key, snapshot.PreviousChapterId, StringComparison.OrdinalIgnoreCase))
                    .Select(h => h.Key)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(18)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (chapterIds.Count == 0)
                    return;

                var fine = await _chunkEmbeddingIndex.SearchWithinChaptersAsync(vector, chapterIds, topK: 8, ct)
                    .ConfigureAwait(false);

                var vectorFragments = new List<string>();
                foreach (var hit in fine)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!ChunkKey.TryParse(hit.Key, out var chapterId, out var position))
                        continue;
                    if (string.Equals(chapterId, snapshot.ChapterId, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(chapterId, snapshot.PreviousChapterId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var chunks = await _contentChunkSearchService.SearchByChapterPositionAsync(
                            chapterId,
                            position,
                            windowSize: 1,
                            ct)
                        .ConfigureAwait(false);
                    var content = chunks.FirstOrDefault()?.Content;
                    if (string.IsNullOrWhiteSpace(content))
                        continue;

                    vectorFragments.Add($"{chapterId}@{position} vector={hit.Score:0.###}：{Trim(content, 180)}");
                }

                foreach (var fragment in vectorFragments
                    .Where(f => !string.IsNullOrWhiteSpace(f))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(8))
                {
                    snapshot.LongDistanceRecall.Add(fragment);
                    snapshot.SimilarContentFragments.Add(fragment);
                }

                snapshot.LongDistanceRecall = snapshot.LongDistanceRecall
                    .Where(f => !string.IsNullOrWhiteSpace(f))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(12)
                    .ToList();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                snapshot.Warnings.Add($"向量 RAG 召回失败：{ex.Message}");
                TM.App.Log($"[StoryStateSnapshotService] VectorRecall({snapshot.ChapterId}) 失败: {ex.Message}");
            }
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }

            return string.Empty;
        }

        private static string Trim(string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            text = text.Trim();
            return text.Length <= maxLength ? text : text[..maxLength] + "...";
        }

        private static string FormatForeshadowLedgerItem(
            ForeshadowLedgerEntry entry,
            string chapterId,
            int chapterIndex)
        {
            var due = IsDueForChapter(entry, chapterId, chapterIndex) ? "due=true" : "due=false";
            return $"{entry.Name}：status={entry.Status}, {due}, setup={Trim(entry.Setup, 80)}, payoff={Trim(entry.Payoff, 80)}, plannedPayoff={entry.PlannedPayoffChapterId}";
        }

        private static string FormatCharacterLedgerItem(CharacterLedgerEntry entry)
        {
            var parts = new[]
            {
                $"status={entry.Status}",
                string.IsNullOrWhiteSpace(entry.CurrentGoal) ? string.Empty : $"goal={Trim(entry.CurrentGoal, 70)}",
                string.IsNullOrWhiteSpace(entry.CurrentIntent) ? string.Empty : $"intent={Trim(entry.CurrentIntent, 70)}",
                string.IsNullOrWhiteSpace(entry.NextPressure) ? string.Empty : $"pressure={Trim(entry.NextPressure, 70)}",
                string.IsNullOrWhiteSpace(entry.Relationship.TargetCharacter)
                    ? string.Empty
                    : $"relation={entry.Relationship.TargetCharacter}/{entry.Relationship.Status}/{Trim(entry.Relationship.Tension, 40)}",
                string.IsNullOrWhiteSpace(entry.Secret.Content)
                    ? string.Empty
                    : $"secret={entry.Secret.Status}/{Trim(entry.Secret.Content, 50)}",
                string.IsNullOrWhiteSpace(entry.AbilityCost.Ability)
                    ? string.Empty
                    : $"ability={Trim(entry.AbilityCost.Ability, 45)}, cost={Trim(entry.AbilityCost.Cost, 45)}",
                string.IsNullOrWhiteSpace(entry.Psychology.Emotion)
                    ? string.Empty
                    : $"emotion={Trim(entry.Psychology.Emotion, 35)}, stress={entry.Psychology.StressLevel}"
            }.Where(p => !string.IsNullOrWhiteSpace(p));

            return $"{entry.CharacterName}：{string.Join(", ", parts)}";
        }

        private static string BuildVectorRecallQuery(StoryStateSnapshot snapshot)
        {
            var parts = new[]
            {
                snapshot.ChapterTitle,
                snapshot.ChapterGoal,
                snapshot.ChapterTurn,
                snapshot.ReaderExperienceGoal,
                snapshot.ActiveConflicts.FirstOrDefault(),
                snapshot.ActiveForeshadowing.FirstOrDefault(),
                snapshot.CharacterStates.FirstOrDefault(),
                snapshot.WorldRules.FirstOrDefault()
            };

            return string.Join("。", parts
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => Trim(p!, 120)))
                .Trim();
        }

        private static bool IsDueForChapter(
            ForeshadowLedgerEntry entry,
            string chapterId,
            int chapterIndex)
        {
            if (!string.IsNullOrWhiteSpace(entry.PlannedPayoffChapterId)
                && string.Equals(entry.PlannedPayoffChapterId, chapterId, StringComparison.OrdinalIgnoreCase))
                return true;

            var payoffIndex = ExtractTrailingNumber(entry.PlannedPayoffChapterId);
            return chapterIndex > 0 && payoffIndex > 0 && chapterIndex >= payoffIndex - 1;
        }

        private static int ExtractTrailingNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return -1;

            var end = value.Length - 1;
            while (end >= 0 && !char.IsDigit(value[end])) end--;
            if (end < 0) return -1;

            var start = end;
            while (start >= 0 && char.IsDigit(value[start])) start--;
            return int.TryParse(value.Substring(start + 1, end - start), out var number)
                ? number
                : -1;
        }
    }
}
