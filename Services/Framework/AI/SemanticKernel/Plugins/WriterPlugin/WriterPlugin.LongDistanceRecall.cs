using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.Embedding;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Implementations.Indexing;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.TaskContexts;
using TM.Services.Modules.ProjectData.Models.Tracking;
using TM.Services.Modules.ProjectData.Models.Design.Characters;

namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    public partial class WriterPlugin
    {
        #region 长距离记忆召回

        public async Task PopulateLongDistanceRecallAsync(ContentTaskContext ctx, CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var cfg = LayeredContextConfig.TakeSnapshot();

                var keywordIndex = ServiceLocator.Get<KeywordChapterIndexService>();
                var chunkSearch = ServiceLocator.Get<ContentChunkSearchService>();
                var queryText = BuildVectorSearchQuery(ctx);
                if (string.IsNullOrWhiteSpace(queryText)) return;

                var nameTerms = queryText
                    .Split(new[] { ' ', '\n', '、', '，' }, StringSplitOptions.RemoveEmptyEntries)
                    .ToList();
                var idTerms = CollectQueryIds(ctx).ToList();
                var queryKeywords = nameTerms.Concat(idTerms).Distinct().ToList();

                var tfTopK = Math.Max(1, cfg.SemanticTfRecallTopK);
                var kwTopK = Math.Max(1, cfg.SemanticKeywordRecallTopK);

                List<ContentChunkSearchService.Hit> chunkHits = new();
                try { chunkHits = await chunkSearch.SearchAsync(queryText, topK: tfTopK).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { TM.App.Log($"[WriterPlugin] 全文 TF 召回失败（非致命）: {ex.Message}"); }

                List<string> recallIds = new();
                try { recallIds = await keywordIndex.SearchAsync(queryKeywords, topK: kwTopK).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { TM.App.Log($"[WriterPlugin] 关键词召回失败（非致命）: {ex.Message}"); }

                ct.ThrowIfCancellationRequested();

                var forChunks = new List<ChunkHit>();
                var charChunks = new List<ChunkHit>();
                var genChunks = new List<ChunkHit>();
                var embSvc = ServiceLocator.Get<IMicroEmbeddingService>();
                var vectorActive = cfg.SemanticRecallEnabled && embSvc.IsModelReady();
                if (vectorActive)
                {
                    try
                    {
                        var chapterIdx = ServiceLocator.Get<ChapterEmbeddingIndex>();
                        var chunkIdx = ServiceLocator.Get<IChunkEmbeddingIndex>();
                        await Task.WhenAll(chapterIdx.LoadAsync(ct), chunkIdx.LoadAsync(ct)).ConfigureAwait(false);

                        if (chapterIdx.Count > 0)
                        {
                            var buckets = BuildBucketQueries(ctx);

                            var pending = new List<(string Text, int TopK, string Name)>(3);
                            if (!string.IsNullOrWhiteSpace(buckets.Foreshadowing) && cfg.SemanticForeshadowingTopK > 0)
                                pending.Add((buckets.Foreshadowing, cfg.SemanticForeshadowingTopK, "F"));
                            if (!string.IsNullOrWhiteSpace(buckets.Character) && cfg.SemanticCharacterTopK > 0)
                                pending.Add((buckets.Character, cfg.SemanticCharacterTopK, "C"));
                            if (!string.IsNullOrWhiteSpace(buckets.General) && cfg.SemanticGeneralTopK > 0)
                                pending.Add((buckets.General, cfg.SemanticGeneralTopK, "G"));

                            if (pending.Count > 0)
                            {
                                var texts = pending.Select(p => p.Text).ToArray();
                                var vecs = await embSvc.EncodeBatchAsync(texts, EmbeddingMode.Query, ct).ConfigureAwait(false);

                                async Task<List<ChunkHit>> HybridSearchWithVecAsync(float[] vec, int topKChunks)
                                {
                                    if (vec == null || vec.Length == 0 || topKChunks <= 0)
                                        return new List<ChunkHit>();

                                    int coarseTopN = Math.Min(Math.Max(topKChunks * 3, 10), 30);
                                    var coarse = await chapterIdx.SearchAsync(vec, coarseTopN, ct).ConfigureAwait(false);
                                    if (coarse.Count == 0) return new List<ChunkHit>();

                                    var candidateChapterIds = new HashSet<string>(
                                        coarse.Select(h => h.Key),
                                        StringComparer.OrdinalIgnoreCase);

                                    var fine = await chunkIdx.SearchWithinChaptersAsync(vec, candidateChapterIds, topKChunks, ct).ConfigureAwait(false);

                                    var result = new List<ChunkHit>(fine.Count);
                                    foreach (var h in fine)
                                    {
                                        if (!ChunkKey.TryParse(h.Key, out var cid, out var pos)) continue;
                                        result.Add(new ChunkHit(h.Key, cid, pos, h.Score));
                                    }
                                    return result;
                                }

                                var tasks = new Task<List<ChunkHit>>[pending.Count];
                                for (int i = 0; i < pending.Count; i++)
                                    tasks[i] = HybridSearchWithVecAsync(vecs[i], pending[i].TopK);
                                await Task.WhenAll(tasks).ConfigureAwait(false);

                                for (int i = 0; i < pending.Count; i++)
                                {
                                    switch (pending[i].Name)
                                    {
                                        case "F": forChunks = tasks[i].Result; break;
                                        case "C": charChunks = tasks[i].Result; break;
                                        case "G": genChunks = tasks[i].Result; break;
                                    }
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[WriterPlugin] 向量混合检索异常（非致命，降级两路）: {ex.Message}");
                    }
                }

                var rrf = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                var cat = new Dictionary<string, RecallCategory>(StringComparer.OrdinalIgnoreCase);
                var bestChunk = new Dictionary<string, ChunkHit>(StringComparer.OrdinalIgnoreCase);
                var bestTfContent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                void AddChunk(string chId, int pos, double chunkScore, int rank, RecallCategory c, string? tfContent = null)
                {
                    if (string.IsNullOrEmpty(chId)) return;
                    if (string.Equals(chId, ctx.ChapterId, StringComparison.OrdinalIgnoreCase)) return;
                    if (string.Equals(chId, ctx.PreviousChapterId, StringComparison.OrdinalIgnoreCase)) return;

                    double s = 1.0 / (cfg.SemanticRrfK + rank);
                    rrf[chId] = rrf.TryGetValue(chId, out var prev) ? prev + s : s;

                    if (!bestChunk.TryGetValue(chId, out var existing) || chunkScore > existing.Score)
                    {
                        bestChunk[chId] = new ChunkHit(ChunkKey.Format(chId, pos), chId, pos, chunkScore);
                        cat[chId] = c;
                    }
                    else if (!cat.ContainsKey(chId))
                    {
                        cat[chId] = c;
                    }

                    if (!string.IsNullOrWhiteSpace(tfContent) && !bestTfContent.ContainsKey(chId))
                        bestTfContent[chId] = tfContent!;
                }

                void AddChapter(string chId, int rank, RecallCategory c)
                {
                    if (string.IsNullOrEmpty(chId)) return;
                    if (string.Equals(chId, ctx.ChapterId, StringComparison.OrdinalIgnoreCase)) return;
                    if (string.Equals(chId, ctx.PreviousChapterId, StringComparison.OrdinalIgnoreCase)) return;
                    double s = 1.0 / (cfg.SemanticRrfK + rank);
                    rrf[chId] = rrf.TryGetValue(chId, out var prev) ? prev + s : s;
                    if (!cat.ContainsKey(chId)) cat[chId] = c;
                }

                static double SafeMax<T>(IReadOnlyList<T> list, Func<T, double> sel)
                {
                    if (list.Count == 0) return 1.0;
                    double m = 0;
                    for (int i = 0; i < list.Count; i++) { var v = sel(list[i]); if (v > m) m = v; }
                    return m > 0 ? m : 1.0;
                }

                double tfMax = SafeMax(chunkHits, h => h.Score);
                for (int i = 0; i < chunkHits.Count; i++)
                {
                    var h = chunkHits[i];
                    AddChunk(h.ChapterId, h.Position, h.Score / tfMax, i, RecallCategory.General, h.Content);
                }
                for (int i = 0; i < recallIds.Count; i++) AddChapter(recallIds[i], i, RecallCategory.General);
                double forMax = SafeMax(forChunks, h => h.Score);
                for (int i = 0; i < forChunks.Count; i++)
                    AddChunk(forChunks[i].ChapterId, forChunks[i].Position, forChunks[i].Score / forMax, i, RecallCategory.Foreshadowing);
                double charMax = SafeMax(charChunks, h => h.Score);
                for (int i = 0; i < charChunks.Count; i++)
                    AddChunk(charChunks[i].ChapterId, charChunks[i].Position, charChunks[i].Score / charMax, i, RecallCategory.Character);
                double genMax = SafeMax(genChunks, h => h.Score);
                for (int i = 0; i < genChunks.Count; i++)
                    AddChunk(genChunks[i].ChapterId, genChunks[i].Position, genChunks[i].Score / genMax, i, RecallCategory.General);

                var sorted = rrf
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => (ChapterId: kv.Key, Category: cat[kv.Key]))
                    .ToList();

                var picked = PickByCategoryQuota(sorted,
                    fQ: cfg.SemanticQuotaForeshadowing,
                    cQ: cfg.SemanticQuotaCharacter,
                    gQ: cfg.SemanticQuotaGeneral);

                var fragments = new List<LongDistanceRecallFragment>(picked.Count);
                foreach (var (chId, c) in picked)
                {
                    ct.ThrowIfCancellationRequested();
                    var content = await PickBestContentAsync(chId, bestChunk, bestTfContent, chunkSearch, ct).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(content)) continue;

                    fragments.Add(new LongDistanceRecallFragment
                    {
                        ChapterId = chId,
                        Content = content,
                        Score = rrf[chId],
                        Category = c.ToString(),
                    });
                }

                ctx.LongDistanceRecallFragments = fragments;
                TM.App.Log($"[WriterPlugin] RRF {fragments.Count} 章（TF={chunkHits.Count} KW={recallIds.Count} VecChunks={forChunks.Count + charChunks.Count + genChunks.Count} vectorActive={vectorActive}）");
            }
            catch (OperationCanceledException)
            {
                TM.App.Log("[WriterPlugin] 远距离召回已取消");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WriterPlugin] 远距离召回失败（非致命）: {ex.Message}");
            }
        }

        private readonly record struct ChunkHit(string ChunkKey, string ChapterId, int Position, double Score);

        private static (string Foreshadowing, string Character, string General) BuildBucketQueries(ContentTaskContext ctx)
        {
            var fText = string.Join("。", (ctx.FactSnapshot?.ForeshadowingStatus ?? new List<ForeshadowingStatusSnapshot>())
                .Where(f => f.IsSetup && !f.IsResolved && !string.IsNullOrWhiteSpace(f.Name))
                .Take(5)
                .Select(f => f.Name));

            var cText = string.Join("。", (ctx.Characters ?? new List<CharacterRulesData>())
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .Take(5)
                .Select(c => string.IsNullOrWhiteSpace(c.Identity) ? c.Name : $"{c.Name}：{c.Identity}"));

            var gText = string.Join("。", new[]
            {
                ctx.ChapterPlan?.MainGoal,
                ctx.ChapterPlan?.KeyTurn,
                ctx.Summary,
                ctx.Title,
            }.Where(s => !string.IsNullOrWhiteSpace(s)));

            return (fText, cText, gText);
        }

        private static List<(string ChapterId, RecallCategory Category)> PickByCategoryQuota(
            List<(string ChapterId, RecallCategory Category)> sorted, int fQ, int cQ, int gQ)
        {
            int f = 0, c = 0, g = 0;
            var result = new List<(string, RecallCategory)>(Math.Max(0, fQ + cQ + gQ));
            foreach (var (id, category) in sorted)
            {
                bool keep = category switch
                {
                    RecallCategory.Foreshadowing when f < fQ => ++f > 0,
                    RecallCategory.Character when c < cQ => ++c > 0,
                    RecallCategory.General when g < gQ => ++g > 0,
                    _ => false
                };
                if (keep) result.Add((id, category));
                if (f >= fQ && c >= cQ && g >= gQ) break;
            }
            return result;
        }

        private static async Task<string> PickBestContentAsync(
            string chapterId,
            IReadOnlyDictionary<string, ChunkHit> bestChunk,
            IReadOnlyDictionary<string, string> bestTfContent,
            ContentChunkSearchService chunkSearch,
            CancellationToken ct)
        {
            if (string.IsNullOrEmpty(chapterId)) return string.Empty;

            if (bestTfContent.TryGetValue(chapterId, out var tfContent) && !string.IsNullOrWhiteSpace(tfContent))
                return tfContent;

            if (bestChunk.TryGetValue(chapterId, out var hit))
            {
                try
                {
                    var chunks = await chunkSearch.SearchByChapterPositionAsync(chapterId, hit.Position, windowSize: 1).ConfigureAwait(false);
                    var first = chunks.FirstOrDefault();
                    if (first != null && !string.IsNullOrWhiteSpace(first.Content)) return first.Content;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { TM.App.Log($"[WriterPlugin] 反查 chunk 失败（fallback 章首）: {ex.Message}"); }
            }

            try
            {
                var fallback = (await chunkSearch.SearchByChapterAsync(chapterId, topK: 1).ConfigureAwait(false)).FirstOrDefault();
                return fallback?.Content ?? string.Empty;
            }
            catch (OperationCanceledException) { throw; }
            catch { return string.Empty; }
        }

        public static string BuildVectorSearchQuery(ContentTaskContext ctx)
        {
            var sb = new StringBuilder();

            if (ctx.ChapterPlan != null)
            {
                var plan = ctx.ChapterPlan;
                if (!string.IsNullOrWhiteSpace(plan.ChapterTitle))
                    sb.Append($"「{plan.ChapterTitle}」");

                if (!string.IsNullOrWhiteSpace(plan.MainGoal))
                    sb.Append($"本章目标是{plan.MainGoal}");
                else if (!string.IsNullOrWhiteSpace(plan.ChapterTheme))
                    sb.Append($"本章主题是{plan.ChapterTheme}");

                if (!string.IsNullOrWhiteSpace(plan.KeyTurn))
                    sb.Append($"，关键转折为{plan.KeyTurn}");
            }

            var characterNames = ctx.Characters?
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .Select(c => c.Name)
                .Take(5)
                .ToList();
            if (characterNames?.Count > 0)
            {
                sb.Append(sb.Length > 0 ? "，涉及" : "涉及");
                sb.Append(string.Join("、", characterNames));
            }

            var unresolvedForeshadowings = ctx.FactSnapshot?.ForeshadowingStatus?
                .Where(f => f.IsSetup && !f.IsResolved && !string.IsNullOrWhiteSpace(f.Name))
                .Select(f => f.Name)
                .Take(5)
                .ToList();
            if (unresolvedForeshadowings?.Count > 0)
            {
                sb.Append(sb.Length > 0 ? "，需要呼应伏笔" : "需要呼应伏笔");
                sb.Append(string.Join("、", unresolvedForeshadowings));
            }

            if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan?.Foreshadowing))
            {
                var fText = ctx.ChapterPlan.Foreshadowing;
                if (fText.Length > 80) fText = fText.Substring(0, 80);
                sb.Append($"，伏笔安排：{fText}");
            }

            var plotRuleNames = ctx.PlotRules?
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => p.Name)
                .Take(3)
                .ToList();
            if (plotRuleNames?.Count > 0)
            {
                sb.Append(sb.Length > 0 ? "，遵循" : "遵循");
                sb.Append(string.Join("、", plotRuleNames));
            }

            var query = sb.ToString();
            if (query.Length > 400)
                query = query.Substring(0, 400);
            return query;
        }

        private static IEnumerable<string> CollectQueryIds(ContentTaskContext ctx)
        {
            if (ctx.Characters != null)
            {
                foreach (var c in ctx.Characters.Take(5))
                    if (!string.IsNullOrWhiteSpace(c.Id))
                        yield return c.Id;
            }

            if (ctx.FactSnapshot?.ForeshadowingStatus != null)
            {
                foreach (var f in ctx.FactSnapshot.ForeshadowingStatus
                    .Where(x => x.IsSetup && !x.IsResolved)
                    .Take(5))
                    if (!string.IsNullOrWhiteSpace(f.Id))
                        yield return f.Id;
            }

            if (ctx.PlotRules != null)
            {
                foreach (var p in ctx.PlotRules.Take(3))
                    if (!string.IsNullOrWhiteSpace(p.Id))
                        yield return p.Id;
            }

            if (ctx.Locations != null)
            {
                foreach (var l in ctx.Locations.Take(3))
                    if (!string.IsNullOrWhiteSpace(l.Id))
                        yield return l.Id;
            }

            if (ctx.Factions != null)
            {
                foreach (var fac in ctx.Factions.Take(3))
                    if (!string.IsNullOrWhiteSpace(fac.Id))
                        yield return fac.Id;
            }
        }

        private static async Task<FactSnapshot?> TryBuildLazySnapshotAsync(
            string chapterId,
            ContextIdCollection? contextIds)
        {
            if (contextIds == null) return null;
            try
            {
                var guideService = ServiceLocator.Get<GuideContextService>();
                var snapshot = await guideService.ExtractFactSnapshotForChapterAsync(chapterId, contextIds).ConfigureAwait(false);
                if (snapshot != null)
                    TM.App.Log($"[WriterPlugin] {chapterId} FactSnapshot延迟构建成功，升级为严格路径");
                return snapshot;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WriterPlugin] {chapterId} FactSnapshot延迟构建失败: {ex.Message}");
                return null;
            }
        }

        #endregion
    }
}
