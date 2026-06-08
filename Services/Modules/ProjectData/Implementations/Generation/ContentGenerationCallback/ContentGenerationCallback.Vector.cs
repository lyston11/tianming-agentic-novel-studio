using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Modules.Design.Elements.CharacterRules.Services;
using TM.Services.Framework.AI.Embedding;
using TM.Services.Modules.ProjectData.Implementations.Indexing;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class ContentGenerationCallback
    {

        private static bool IsZeroVector(float[]? vec)
        {
            if (vec == null || vec.Length == 0) return true;
            for (int i = 0; i < vec.Length; i++)
                if (vec[i] != 0f) return false;
            return true;
        }

        internal async Task RebuildVectorIndicesForChapterAsync(
            string chapterId,
            string persistedContent,
            ChapterChanges? changes,
            Dictionary<string, string>? nameMap,
            IReadOnlyList<string>? carryOverEntityIds = null)
        {
            if (string.IsNullOrEmpty(chapterId)) return;

            try
            {
                var emb = ServiceLocator.Get<IMicroEmbeddingService>();
                if (!emb.IsModelReady())
                {
                    TM.App.Log($"[ContentCallback] {chapterId} 向量模型未就绪，跳过三层索引建设（对账阶段补建）");
                    return;
                }

                var chapterIdx = ServiceLocator.Get<ChapterEmbeddingIndex>();
                var chunkIdx = ServiceLocator.Get<ChunkEmbeddingIndex>();
                var firstIdx = ServiceLocator.Get<EntityFirstChapterIndex>();

                await Task.WhenAll(chapterIdx.LoadAsync(), chunkIdx.LoadAsync(), firstIdx.LoadAsync()).ConfigureAwait(false);

                try
                {
                    var maxChars = LayeredContextConfig.EmbeddingMaxChars;
                    var head = persistedContent.Length <= maxChars
                        ? persistedContent
                        : persistedContent.Substring(0, maxChars);
                    if (!string.IsNullOrWhiteSpace(head))
                    {
                        var vec = await emb.EncodeAsync(head, EmbeddingMode.Passage).ConfigureAwait(false);
                        if (IsZeroVector(vec))
                        {
                            TM.App.Log($"[ContentCallback] {chapterId} 章节级 Encode 返回零向量（模型异常），跳过 Upsert（对账兜底）");
                        }
                        else
                        {
                            await chapterIdx.UpsertAsync(chapterId, vec).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ContentCallback] {chapterId} 章节级向量建设失败（非致命）: {ex.Message}");
                }

                try
                {
                    var chunkSvc = ServiceLocator.Get<ContentChunkSearchService>();
                    await chunkSvc.InvalidateChapterAsync(chapterId).ConfigureAwait(false);
                    var chunks = await chunkSvc.GetChunksAsync(chapterId).ConfigureAwait(false);
                    await chunkIdx.RemoveByChapterAsync(chapterId).ConfigureAwait(false);
                    if (chunks.Count > 0)
                    {
                        var texts = chunks.Select(c => c.Content).ToList();
                        var vecs = await emb.EncodeBatchAsync(texts, EmbeddingMode.Passage).ConfigureAwait(false);
                        if (vecs.Length == chunks.Count)
                        {
                            var batch = new List<(string Key, float[] Vector)>(chunks.Count);
                            int zeroCount = 0;
                            for (int i = 0; i < chunks.Count; i++)
                            {
                                if (IsZeroVector(vecs[i])) { zeroCount++; continue; }
                                batch.Add((ChunkKey.Format(chunks[i].ChapterId, chunks[i].Position), vecs[i]));
                            }
                            if (zeroCount > 0)
                                TM.App.Log($"[ContentCallback] {chapterId} 段落级 {zeroCount}/{chunks.Count} 条零向量已过滤（模型异常，对账兜底）");
                            if (batch.Count > 0)
                                await chunkIdx.UpsertBatchAsync(batch).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ContentCallback] {chapterId} 段落级向量建设失败（非致命）: {ex.Message}");
                }

                try
                {
                    if (changes?.CharacterStateChanges != null && changes.CharacterStateChanges.Count > 0)
                    {
                        Dictionary<string, (string Name, string Identity)>? designIdToName = null;

                        foreach (var cc in changes.CharacterStateChanges)
                        {
                            if (string.IsNullOrEmpty(cc?.CharacterId)) continue;
                            if (firstIdx.Contains(cc.CharacterId)) continue;

                            string name = string.Empty;
                            string description = string.Empty;
                            nameMap?.TryGetValue(cc.CharacterId, out name!);

                            if (string.IsNullOrWhiteSpace(name))
                            {
                                designIdToName ??= BuildDesignCharacterMap();
                                if (designIdToName.TryGetValue(cc.CharacterId, out var designEntry))
                                {
                                    name = designEntry.Name;
                                    description = designEntry.Identity;
                                }
                            }

                            if (string.IsNullOrWhiteSpace(name)) continue;
                            await firstIdx.TryCaptureAsync(cc.CharacterId, name, description, chapterId).ConfigureAwait(false);
                        }
                    }

                    if (changes != null && carryOverEntityIds != null && carryOverEntityIds.Count > 0)
                    {
                        var declaredIds = new HashSet<string>(
                            (changes.CharacterStateChanges ?? Enumerable.Empty<CharacterStateChange>())
                                .Where(c => !string.IsNullOrEmpty(c?.CharacterId))
                                .Select(c => c!.CharacterId),
                            StringComparer.OrdinalIgnoreCase);

                        var pendingCarry = carryOverEntityIds
                            .Where(id => !string.IsNullOrEmpty(id)
                                      && !declaredIds.Contains(id)
                                      && !firstIdx.Contains(id))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        if (pendingCarry.Count > 0)
                        {
                            var combined = await BuildCombinedCharacterMapAsync(chapterId).ConfigureAwait(false);
                            int compensated = 0;
                            foreach (var eid in pendingCarry)
                            {
                                if (!combined.TryGetValue(eid, out var entry)) continue;
                                if (string.IsNullOrWhiteSpace(entry.Name)) continue;
                                try
                                {
                                    if (await firstIdx.RebuildAsync(eid, entry.Name, entry.Identity).ConfigureAwait(false))
                                        compensated++;
                                }
                                catch (Exception rebuildEx)
                                {
                                    TM.App.Log($"[ContentCallback] {chapterId} 重写补偿 Rebuild {eid} 失败（非致命）: {rebuildEx.Message}");
                                }
                            }
                            if (compensated > 0)
                                TM.App.Log($"[ContentCallback] {chapterId} 重写路径补偿重建 {compensated}/{pendingCarry.Count} 个 carry-over 角色首次描写");
                        }
                    }

                    if (changes == null)
                    {
                        var affected = firstIdx.GetAll()
                            .Where(e => string.Equals(e.ChapterId, chapterId, StringComparison.OrdinalIgnoreCase))
                            .Select(e => e.EntityId)
                            .ToList();

                        if (affected.Count > 0)
                        {
                            var combined = await BuildCombinedCharacterMapAsync(chapterId).ConfigureAwait(false);
                            int refreshed = 0;
                            foreach (var eid in affected)
                            {
                                if (!combined.TryGetValue(eid, out var entry)) continue;
                                if (string.IsNullOrWhiteSpace(entry.Name)) continue;
                                try
                                {
                                    if (await firstIdx.RebuildAsync(eid, entry.Name, entry.Identity).ConfigureAwait(false))
                                        refreshed++;
                                }
                                catch (Exception rebuildEx)
                                {
                                    TM.App.Log($"[ContentCallback] {chapterId} 静默保存 Rebuild {eid} 失败（非致命）: {rebuildEx.Message}");
                                }
                            }
                            if (refreshed > 0)
                                TM.App.Log($"[ContentCallback] {chapterId} 静默保存后刷新 {refreshed}/{affected.Count} 个角色的首次描写位置");
                        }
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ContentCallback] {chapterId} 首次描写捕获失败（非致命）: {ex.Message}");
                }

                await Task.WhenAll(
                    chapterIdx.SaveAsync(),
                    chunkIdx.SaveAsync(),
                    firstIdx.SaveAsync()
                ).ConfigureAwait(false);

                TM.App.Log($"[ContentCallback] {chapterId} 向量索引建设完成");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] {chapterId} 向量索引建设异常（已吞，对账阶段补建）: {ex.Message}");
            }
        }

        private async Task<Dictionary<string, (string Name, string Identity)>> BuildCombinedCharacterMapAsync(string chapterId)
        {
            var combined = new Dictionary<string, (string Name, string Identity)>(
                BuildDesignCharacterMap(), StringComparer.OrdinalIgnoreCase);
            try
            {
                var csVols = _guideManager.GetExistingVolumeNumbers("character_state_guide.json");
                foreach (var v in csVols)
                {
                    var g = await _guideManager.GetGuideAsync<CharacterStateGuide>(
                        GuideManager.GetVolumeFileName("character_state_guide.json", v)).ConfigureAwait(false);
                    foreach (var kv in g.Characters)
                    {
                        if (string.IsNullOrEmpty(kv.Key) || combined.ContainsKey(kv.Key)) continue;
                        var n = kv.Value?.Name ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(n)) continue;
                        combined[kv.Key] = (n, string.Empty);
                    }
                }
            }
            catch (Exception guideEx)
            {
                TM.App.Log($"[ContentCallback] {chapterId} 合并角色映射读 Guide 失败（非致命,仅用 Design）: {guideEx.Message}");
            }
            return combined;
        }

        internal static Dictionary<string, (string Name, string Identity)> BuildDesignCharacterMap()
        {
            var map = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var crSvc = ServiceLocator.Get<CharacterRulesService>();
                foreach (var c in crSvc.GetAllCharacterRules())
                {
                    if (string.IsNullOrEmpty(c?.Id) || string.IsNullOrWhiteSpace(c.Name)) continue;
                    map[c.Id] = (c.Name, c.Identity ?? string.Empty);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] Design 角色映射构建失败（非致命，首次描写仅用 Guide 层 Name）: {ex.Message}");
            }
            return map;
        }

        private async Task CleanVectorIndicesAsync(string chapterId)
        {
            if (string.IsNullOrEmpty(chapterId)) return;

            try
            {
                var chapterIdx = ServiceLocator.Get<ChapterEmbeddingIndex>();
                var chunkIdx = ServiceLocator.Get<ChunkEmbeddingIndex>();
                var firstIdx = ServiceLocator.Get<EntityFirstChapterIndex>();

                await Task.WhenAll(chapterIdx.LoadAsync(), chunkIdx.LoadAsync(), firstIdx.LoadAsync()).ConfigureAwait(false);

                await Task.WhenAll(
                    chapterIdx.RemoveAsync(chapterId),
                    chunkIdx.RemoveByChapterAsync(chapterId),
                    firstIdx.InvalidateByChapterAsync(chapterId)
                ).ConfigureAwait(false);

                await Task.WhenAll(
                    chapterIdx.SaveAsync(),
                    chunkIdx.SaveAsync(),
                    firstIdx.SaveAsync()
                ).ConfigureAwait(false);

                try
                {
                    await ServiceLocator.Get<ContentChunkSearchService>().InvalidateChapterAsync(chapterId).ConfigureAwait(false);
                }
                catch (Exception lruEx)
                {
                    TM.App.Log($"[ContentCallback] {chapterId} 失效 ContentChunkSearch LRU 失败（非致命）: {lruEx.Message}");
                }

                TM.App.Log($"[ContentCallback] {chapterId} 向量索引清理完成");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] {chapterId} 向量索引清理失败（非致命，下次对账补齐）: {ex.Message}");
            }
        }
    }
}
