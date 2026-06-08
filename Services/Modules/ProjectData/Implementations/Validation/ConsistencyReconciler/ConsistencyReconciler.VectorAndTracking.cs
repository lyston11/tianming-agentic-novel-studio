using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Implementations.Indexing;
using TM.Services.Modules.ProjectData.Models.Guides;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class ConsistencyReconciler
    {
        private async Task DetectTrackingGapsAsync(ReconcileResult result)
        {
            var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
            if (!Directory.Exists(chaptersPath)) return;

            var mdFiles = Directory.GetFiles(chaptersPath, "*.md", SearchOption.TopDirectoryOnly);
            if (mdFiles.Length <= 1) return;

            try
            {
                var trackedChapters = new HashSet<string>(StringComparer.Ordinal);

                var csVols = _guideManager.GetExistingVolumeNumbers("character_state_guide.json");
                var cpVols = _guideManager.GetExistingVolumeNumbers("conflict_progress_guide.json");
                var locVols = _guideManager.GetExistingVolumeNumbers("location_state_guide.json");
                var facVols = _guideManager.GetExistingVolumeNumbers("faction_state_guide.json");
                var tlVols = _guideManager.GetExistingVolumeNumbers("timeline_guide.json");
                var itemVols = _guideManager.GetExistingVolumeNumbers("item_state_guide.json");
                var secretVols = _guideManager.GetExistingVolumeNumbers("secret_reveal_guide.json");

                var csTask = Task.WhenAll(csVols.Select(v =>
                    _guideManager.GetGuideAsync<CharacterStateGuide>(GuideManager.GetVolumeFileName("character_state_guide.json", v))));
                var cpTask = Task.WhenAll(cpVols.Select(v =>
                    _guideManager.GetGuideAsync<ConflictProgressGuide>(GuideManager.GetVolumeFileName("conflict_progress_guide.json", v))));
                var locTask = Task.WhenAll(locVols.Select(v =>
                    _guideManager.GetGuideAsync<LocationStateGuide>(GuideManager.GetVolumeFileName("location_state_guide.json", v))));
                var facTask = Task.WhenAll(facVols.Select(v =>
                    _guideManager.GetGuideAsync<FactionStateGuide>(GuideManager.GetVolumeFileName("faction_state_guide.json", v))));
                var tlTask = Task.WhenAll(tlVols.Select(v =>
                    _guideManager.GetGuideAsync<TimelineGuide>(GuideManager.GetVolumeFileName("timeline_guide.json", v))));
                var itemTask = Task.WhenAll(itemVols.Select(v =>
                    _guideManager.GetGuideAsync<ItemStateGuide>(GuideManager.GetVolumeFileName("item_state_guide.json", v))));
                var secretTask = Task.WhenAll(secretVols.Select(v =>
                    _guideManager.GetGuideAsync<SecretRevealGuide>(GuideManager.GetVolumeFileName("secret_reveal_guide.json", v))));

                CharacterStateGuide[] csGuides;
                ConflictProgressGuide[] cpGuides;
                LocationStateGuide[] locGuides;
                FactionStateGuide[] facGuides;
                TimelineGuide[] tlGuides;
                ItemStateGuide[] itemGuides;
                SecretRevealGuide[] secretGuides;

                try { csGuides = await csTask.ConfigureAwait(false); } catch { csGuides = Array.Empty<CharacterStateGuide>(); }
                try { cpGuides = await cpTask.ConfigureAwait(false); } catch { cpGuides = Array.Empty<ConflictProgressGuide>(); }
                try { locGuides = await locTask.ConfigureAwait(false); } catch { locGuides = Array.Empty<LocationStateGuide>(); }
                try { facGuides = await facTask.ConfigureAwait(false); } catch { facGuides = Array.Empty<FactionStateGuide>(); }
                try { tlGuides = await tlTask.ConfigureAwait(false); } catch { tlGuides = Array.Empty<TimelineGuide>(); }
                try { itemGuides = await itemTask.ConfigureAwait(false); } catch { itemGuides = Array.Empty<ItemStateGuide>(); }
                try { secretGuides = await secretTask.ConfigureAwait(false); } catch { secretGuides = Array.Empty<SecretRevealGuide>(); }

                foreach (var g in csGuides)
                    foreach (var entry in g.Characters.Values)
                        foreach (var state in entry.StateHistory)
                            if (!string.IsNullOrWhiteSpace(state.Chapter))
                                trackedChapters.Add(state.Chapter);

                foreach (var g in cpGuides)
                    foreach (var entry in g.Conflicts.Values)
                    {
                        foreach (var id in entry.InvolvedChapters)
                            if (!string.IsNullOrWhiteSpace(id))
                                trackedChapters.Add(id);
                        foreach (var p in entry.ProgressPoints)
                            if (!string.IsNullOrWhiteSpace(p.Chapter))
                                trackedChapters.Add(p.Chapter);
                    }

                foreach (var g in locGuides)
                    foreach (var entry in g.Locations.Values)
                        foreach (var p in entry.StateHistory)
                            if (!string.IsNullOrWhiteSpace(p.Chapter))
                                trackedChapters.Add(p.Chapter);

                foreach (var g in facGuides)
                    foreach (var entry in g.Factions.Values)
                        foreach (var p in entry.StateHistory)
                            if (!string.IsNullOrWhiteSpace(p.Chapter))
                                trackedChapters.Add(p.Chapter);

                foreach (var g in tlGuides)
                {
                    foreach (var p in g.ChapterTimeline)
                        if (!string.IsNullOrWhiteSpace(p.ChapterId))
                            trackedChapters.Add(p.ChapterId);
                    foreach (var entry in g.CharacterLocations.Values)
                    {
                        if (!string.IsNullOrWhiteSpace(entry.LastUpdatedChapter))
                            trackedChapters.Add(entry.LastUpdatedChapter);
                        foreach (var m in entry.MovementHistory)
                            if (!string.IsNullOrWhiteSpace(m.Chapter))
                                trackedChapters.Add(m.Chapter);
                    }
                }

                foreach (var g in itemGuides)
                    foreach (var entry in g.Items.Values)
                        foreach (var p in entry.StateHistory)
                            if (!string.IsNullOrWhiteSpace(p.Chapter))
                                trackedChapters.Add(p.Chapter);

                foreach (var g in secretGuides)
                    foreach (var entry in g.Secrets.Values)
                        foreach (var p in entry.RevealHistory)
                            if (!string.IsNullOrWhiteSpace(p.Chapter))
                                trackedChapters.Add(p.Chapter);

                try
                {
                    var fow = await _guideManager.GetGuideAsync<ForeshadowingStatusGuide>("foreshadowing_status_guide.json").ConfigureAwait(false);
                    foreach (var entry in fow.Foreshadowings.Values)
                    {
                        if (!string.IsNullOrWhiteSpace(entry.ActualSetupChapter))
                            trackedChapters.Add(entry.ActualSetupChapter);
                        if (!string.IsNullOrWhiteSpace(entry.ActualPayoffChapter))
                            trackedChapters.Add(entry.ActualPayoffChapter);
                    }
                }
                catch { }

                try
                {
                    var store = ServiceLocator.Get<PlotPointsIndexService>();
                    var vols = store.GetExistingVolumeNumbers();
                    if (vols.Count > 0)
                    {
                        var entries = await Task.WhenAll(vols.Select(v => store.GetVolumeEntriesAsync(v))).ConfigureAwait(false);
                        foreach (var list in entries)
                            foreach (var e in list)
                                if (!string.IsNullOrWhiteSpace(e.Chapter))
                                    trackedChapters.Add(e.Chapter);
                    }
                }
                catch { }

                if (trackedChapters.Count == 0) return;

                foreach (var mdFile in mdFiles)
                {
                    var chapterId = Path.GetFileNameWithoutExtension(mdFile);
                    if (!trackedChapters.Contains(chapterId))
                    {
                        result.TrackingGaps.Add(chapterId);
                    }
                }

                var mdChapterIds = new HashSet<string>(
                    mdFiles.Select(f => Path.GetFileNameWithoutExtension(f)),
                    StringComparer.OrdinalIgnoreCase);

                var orphanTracked = trackedChapters
                    .Where(id => !mdChapterIds.Contains(id))
                    .ToList();

                if (orphanTracked.Count > 0)
                {
                    TM.App.Log($"[Reconciler] 发现{orphanTracked.Count}个追踪 guide 孤立章节，开始清理...");
                    var callback = ServiceLocator.Get<ContentGenerationCallback>();
                    foreach (var orphanId in orphanTracked)
                    {
                        try
                        {
                            await callback.OnChapterDeletedAsync(orphanId).ConfigureAwait(false);
                            result.TrackingOrphansCleared++;
                            TM.App.Log($"[Reconciler] 已清理孤立追踪数据: {orphanId}");
                        }
                        catch (Exception ex)
                        {
                            TM.App.Log($"[Reconciler] 清理孤立追踪数据失败: {orphanId}: {ex.Message}");
                        }
                    }
                }

                if (result.TrackingGaps.Count > 0)
                {
                    TM.App.Log($"[Reconciler] gaps: {result.TrackingGaps.Count}: " +
                               string.Join(", ", result.TrackingGaps.Take(10)));

                    foreach (var gapId in result.TrackingGaps)
                    {
                        try
                        {
                            var existing = await _summaryStore.GetSummaryAsync(gapId).ConfigureAwait(false);
                            if (!string.IsNullOrWhiteSpace(existing)) continue;

                            var mdPath = Path.Combine(chaptersPath, $"{gapId}.md");
                            if (!File.Exists(mdPath)) continue;

                            var head = await ReadHeadAsync(mdPath, 2000).ConfigureAwait(false);
                            var summary = ExtractSummaryFromHead(head);
                            if (string.IsNullOrWhiteSpace(summary)) continue;

                            await _summaryStore.SetSummaryAsync(gapId, summary).ConfigureAwait(false);
                            result.TrackingGapSummariesRepaired++;
                        }
                        catch (Exception ex) { TM.App.Log($"[Reconciler] 补齐缺章摘要失败 {gapId}: {ex.Message}"); }
                    }

                    if (result.TrackingGapSummariesRepaired > 0)
                        TM.App.Log($"[Reconciler] 已为{result.TrackingGapSummariesRepaired}个缺章补齐摘要");

                    try
                    {
                        var chapList = string.Join("、", result.TrackingGaps.Take(5));
                        var suffix = result.TrackingGaps.Count > 5 ? $" 等{result.TrackingGaps.Count}章" : string.Empty;
                        GlobalToast.Warning("追踪数据缺口",
                            $"{chapList}{suffix} 的角色/冲突等追踪数据缺失，建议重新导入该章内容修复。");
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"追踪缺章检测失败: {ex.Message}");
            }
        }

        private async Task ReconcileKeywordIndexAsync(ReconcileResult result)
        {
            var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
            if (!Directory.Exists(chaptersPath)) return;

            var mdFiles = Directory.GetFiles(chaptersPath, "*.md", SearchOption.TopDirectoryOnly);
            if (mdFiles.Length == 0) return;

            try
            {
                var kwIndexService = ServiceLocator.Get<KeywordChapterIndexService>();

                var indexedIds = await kwIndexService.GetIndexedChapterIdsAsync().ConfigureAwait(false);
                var missingIds = mdFiles
                    .Select(f => Path.GetFileNameWithoutExtension(f))
                    .Where(id => !indexedIds.Contains(id))
                    .ToList();

                if (missingIds.Count == 0) return;
                TM.App.Log($"[Reconciler] 关键词索引缺失 {missingIds.Count} 章，开始 best-effort 补建...");

                var nameToIds = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                void AddEntity(string id, string? name)
                {
                    if (string.IsNullOrWhiteSpace(name)) return;
                    if (!nameToIds.TryGetValue(name, out var ids))
                    {
                        ids = new HashSet<string>(StringComparer.Ordinal);
                        nameToIds[name] = ids;
                    }
                    if (!string.IsNullOrWhiteSpace(id))
                        ids.Add(id);
                }

                async Task<T[]> LoadVolumeGuidesAsync<T>(string baseName) where T : new()
                {
                    var vols = _guideManager.GetExistingVolumeNumbers(baseName);
                    return await Task.WhenAll(vols.Select(v =>
                        _guideManager.GetGuideAsync<T>(GuideManager.GetVolumeFileName(baseName, v)))).ConfigureAwait(false);
                }

                try { foreach (var g in await LoadVolumeGuidesAsync<CharacterStateGuide>("character_state_guide.json")) foreach (var kv in g.Characters) AddEntity(kv.Key, kv.Value.Name); }
                catch (Exception ex) { TM.App.Log($"[Reconciler] 关键词对账：角色Guide失败（仍继续）: {ex.Message}"); }

                try { var fg = await _guideManager.GetGuideAsync<ForeshadowingStatusGuide>("foreshadowing_status_guide.json").ConfigureAwait(false); foreach (var kv in fg.Foreshadowings) AddEntity(kv.Key, kv.Value.Name); }
                catch (Exception ex) { TM.App.Log($"[Reconciler] 关键词对账：伏笔Guide失败（仍继续）: {ex.Message}"); }

                try { foreach (var g in await LoadVolumeGuidesAsync<LocationStateGuide>("location_state_guide.json")) foreach (var kv in g.Locations) AddEntity(kv.Key, kv.Value.Name); }
                catch (Exception ex) { TM.App.Log($"[Reconciler] 关键词对账：地点Guide失败（仍继续）: {ex.Message}"); }

                try { foreach (var g in await LoadVolumeGuidesAsync<FactionStateGuide>("faction_state_guide.json")) foreach (var kv in g.Factions) AddEntity(kv.Key, kv.Value.Name); }
                catch (Exception ex) { TM.App.Log($"[Reconciler] 关键词对账：势力Guide失败（仍继续）: {ex.Message}"); }

                try { foreach (var g in await LoadVolumeGuidesAsync<ConflictProgressGuide>("conflict_progress_guide.json")) foreach (var kv in g.Conflicts) AddEntity(kv.Key, kv.Value.Name); }
                catch (Exception ex) { TM.App.Log($"[Reconciler] 关键词对账：冲突Guide失败（仍继续）: {ex.Message}"); }

                try { foreach (var g in await LoadVolumeGuidesAsync<ItemStateGuide>("item_state_guide.json")) foreach (var kv in g.Items) AddEntity(kv.Key, kv.Value.Name); }
                catch (Exception ex) { TM.App.Log($"[Reconciler] 关键词对账：物品Guide失败（仍继续）: {ex.Message}"); }

                try { foreach (var g in await LoadVolumeGuidesAsync<SecretRevealGuide>("secret_reveal_guide.json")) foreach (var kv in g.Secrets) AddEntity(kv.Key, kv.Value.Name); }
                catch (Exception ex) { TM.App.Log($"[Reconciler] 关键词对账：秘密Guide失败（仍继续）: {ex.Message}"); }

                try { foreach (var g in await LoadVolumeGuidesAsync<PledgeConstraintGuide>("pledge_constraint_guide.json")) foreach (var kv in g.Pledges) AddEntity(kv.Key, kv.Value.Name); }
                catch (Exception ex) { TM.App.Log($"[Reconciler] 关键词对账：承诺Guide失败（仍继续）: {ex.Message}"); }

                try { foreach (var g in await LoadVolumeGuidesAsync<DeadlineConstraintGuide>("deadline_constraint_guide.json")) foreach (var kv in g.Deadlines) AddEntity(kv.Key, kv.Value.Name); }
                catch (Exception ex) { TM.App.Log($"[Reconciler] 关键词对账：倒计时Guide失败（仍继续）: {ex.Message}"); }

                var knownNames = nameToIds.Keys.ToList();
                if (knownNames.Count == 0)
                {
                    TM.App.Log("[Reconciler] 关键词对账：无可用实体名，跳过补建");
                    return;
                }

                foreach (var chapterId in missingIds)
                {
                    try
                    {
                        var summary = await _summaryStore.GetSummaryAsync(chapterId).ConfigureAwait(false);
                        if (string.IsNullOrWhiteSpace(summary)) continue;

                        var matchedNames = knownNames
                            .Where(name => summary.Contains(name, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (matchedNames.Count == 0) continue;

                        var keywords = new HashSet<string>(matchedNames, StringComparer.OrdinalIgnoreCase);
                        foreach (var name in matchedNames)
                        {
                            if (nameToIds.TryGetValue(name, out var ids))
                            {
                                foreach (var id in ids)
                                    keywords.Add(id);
                            }
                        }

                        await kwIndexService.IndexChapterFromKeywordsAsync(chapterId, keywords).ConfigureAwait(false);
                        result.KeywordIndexRepaired++;
                    }
                    catch (Exception ex) { TM.App.Log($"[Reconciler] 关键词索引补建失败 {chapterId}: {ex.Message}"); }
                }

                if (result.KeywordIndexRepaired > 0)
                    TM.App.Log($"[Reconciler] 关键词索引 best-effort 补建完成: {result.KeywordIndexRepaired} 章（实体库 {knownNames.Count} 个 Name / 9 类 Guide）");
            }
            catch (Exception ex)
            {
                result.Errors.Add($"关键词索引对账失败: {ex.Message}");
                TM.App.Log($"[Reconciler] 关键词索引对账失败: {ex.Message}");
            }
        }

        private async Task ReconcileFactArchivesAsync(ReconcileResult result)
        {
            try
            {
                var archivesDir = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides", "fact_archives");
                var allSummaries = await _summaryStore.GetAllSummariesAsync().ConfigureAwait(false);
                if (allSummaries.Count == 0) return;

                var presentVolumes = new HashSet<int>();
                foreach (var chapterId in allSummaries.Keys)
                {
                    var p = ChapterParserHelper.ParseChapterId(chapterId);
                    if (p.HasValue) presentVolumes.Add(p.Value.volumeNumber);
                }

                var volumeDesignService = ServiceLocator.Get<TM.Modules.Generate.Elements.VolumeDesign.Services.VolumeDesignService>();
                if (!volumeDesignService.IsInitialized)
                    await volumeDesignService.InitializeAsync().ConfigureAwait(false);
                var allVolumeDesigns = volumeDesignService.GetAllVolumeDesigns()
                    .ToList();

                var snapshotExtractor = ServiceLocator.Get<FactSnapshotExtractor>();
                var archiveStore = ServiceLocator.Get<VolumeFactArchiveStore>();
                foreach (var vol in presentVolumes.OrderBy(v => v))
                {
                    var isMaxVol = !presentVolumes.Contains(vol + 1);
                    bool isCompleted;
                    var volDesign = allVolumeDesigns.FirstOrDefault(d => d.VolumeNumber == vol);
                    if (volDesign != null && volDesign.EndChapter > 0)
                    {
                        var endChapterId = $"vol{vol}_ch{volDesign.EndChapter}";
                        isCompleted = allSummaries.ContainsKey(endChapterId);
                    }
                    else
                    {
                        var volChapterCount = allSummaries.Keys
                            .Count(id => ChapterParserHelper.ParseChapterId(id)?.volumeNumber == vol);
                        isCompleted = !isMaxVol || volChapterCount >= 7;
                    }
                    if (!isCompleted) continue;

                    var archivePath = Path.Combine(archivesDir, $"vol{vol}.json");
                    if (File.Exists(archivePath)) continue;

                    try
                    {
                        var volSummaries = await _summaryStore.GetVolumeSummariesAsync(vol).ConfigureAwait(false);
                        var lastChapterId = volSummaries.Keys
                            .Where(id => ChapterParserHelper.ParseChapterId(id)?.volumeNumber == vol)
                            .OrderBy(id => ChapterParserHelper.ParseChapterId(id)?.chapterNumber ?? 0)
                            .LastOrDefault() ?? $"vol{vol}_ch0";

                        var snapshot = await snapshotExtractor.ExtractVolumeEndSnapshotAsync(lastChapterId).ConfigureAwait(false);
                        Directory.CreateDirectory(archivesDir);
                        await archiveStore.ArchiveVolumeAsync(vol, snapshot, lastChapterId).ConfigureAwait(false);

                        result.FactArchivesRepaired++;
                        TM.App.Log($"[Reconciler] 已回补第{vol}卷 fact_archive（full snapshot）");
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[Reconciler] 第{vol}卷 fact_archive 回补失败: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"fact_archive对账失败: {ex.Message}");
            }
        }

        public async Task AutoArchiveVolumeIfNeededAsync(int volumeNumber)
        {
            try
            {
                var volSummaries = await _summaryStore.GetVolumeSummariesAsync(volumeNumber).ConfigureAwait(false);
                var lastChapterId = volSummaries.Keys
                    .Where(id => ChapterParserHelper.ParseChapterId(id)?.volumeNumber == volumeNumber)
                    .OrderBy(id => ChapterParserHelper.ParseChapterId(id)?.chapterNumber ?? 0)
                    .LastOrDefault();

                if (string.IsNullOrEmpty(lastChapterId))
                {
                    try
                    {
                        var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
                        if (Directory.Exists(chaptersPath))
                        {
                            var pattern = $"vol{volumeNumber}_ch";
                            var mdFiles = Directory.GetFiles(chaptersPath, "*.md", SearchOption.TopDirectoryOnly)
                                .Select(Path.GetFileNameWithoutExtension)
                                .Where(n => !string.IsNullOrWhiteSpace(n) && n.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                                .Select(n => n!)
                                .ToList();

                            var best = mdFiles
                                .Select(id => new { Id = id, Parsed = ChapterParserHelper.ParseChapterId(id) })
                                .Where(x => x.Parsed.HasValue && x.Parsed.Value.volumeNumber == volumeNumber)
                                .OrderBy(x => x.Parsed!.Value.chapterNumber)
                                .LastOrDefault();

                            if (best != null)
                            {
                                lastChapterId = best.Id;
                                TM.App.Log($"[Reconciler] 第{volumeNumber}卷摘要缺失，已从章节文件推断卷末章: {lastChapterId}");
                            }
                        }
                    }
                    catch (Exception inferEx)
                    {
                        TM.App.Log($"[Reconciler] 第{volumeNumber}卷推断卷末章失败: {inferEx.Message}");
                    }
                }

                if (string.IsNullOrEmpty(lastChapterId))
                {
                    TM.App.Log($"[Reconciler] 第{volumeNumber}卷无已生成章节，跳过自动存档");
                    return;
                }

                try
                {
                    var volumeDesignService = ServiceLocator.Get<TM.Modules.Generate.Elements.VolumeDesign.Services.VolumeDesignService>();
                    if (!volumeDesignService.IsInitialized)
                        await volumeDesignService.InitializeAsync().ConfigureAwait(false);
                    var volDesign = volumeDesignService.GetAllVolumeDesigns()
                        .FirstOrDefault(d => d.VolumeNumber == volumeNumber);
                    if (volDesign != null && volDesign.EndChapter > 0)
                    {
                        var endChapterId = $"vol{volumeNumber}_ch{volDesign.EndChapter}";
                        if (!string.Equals(lastChapterId, endChapterId, StringComparison.OrdinalIgnoreCase))
                        {
                            var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
                            var endFile = Path.Combine(chaptersPath, $"{endChapterId}.md");
                            if (volSummaries.ContainsKey(endChapterId) || File.Exists(endFile))
                            {
                                lastChapterId = endChapterId;
                            }
                            else
                            {
                                TM.App.Log($"[Reconciler] 第{volumeNumber}卷未到EndChapter={volDesign.EndChapter}，跳过自动存档（last={lastChapterId}）");
                                return;
                            }
                        }
                    }
                }
                catch (Exception p6Ex)
                {
                    TM.App.Log($"[Reconciler] P6 EndChapter校验失败（非致命，继续旧逻辑）: {p6Ex.Message}");
                }

                var snapshot = await ServiceLocator.Get<FactSnapshotExtractor>().ExtractVolumeEndSnapshotAsync(lastChapterId).ConfigureAwait(false);
                var archivesDir = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides", "fact_archives");
                Directory.CreateDirectory(archivesDir);
                await ServiceLocator.Get<VolumeFactArchiveStore>().ArchiveVolumeAsync(volumeNumber, snapshot, lastChapterId).ConfigureAwait(false);

                TM.App.Log($"[Reconciler] 第{volumeNumber}卷自动存档完成，最后章节: {lastChapterId}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[Reconciler] 第{volumeNumber}卷自动存档失败（非致命）: {ex.Message}");
            }
        }

        private async Task ReconcileVectorIndicesAsync(ReconcileResult result)
        {
            var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
            if (!Directory.Exists(chaptersPath)) return;

            HashSet<string> mdChapterIds;
            try
            {
                mdChapterIds = new HashSet<string>(
                    Directory.GetFiles(chaptersPath, "*.md", SearchOption.TopDirectoryOnly)
                        .Select(f => Path.GetFileNameWithoutExtension(f))
                        .Where(n => !string.IsNullOrEmpty(n))!,
                    StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"向量对账：枚举 md 文件失败: {ex.Message}");
                return;
            }

            ChapterEmbeddingIndex chapterIdx;
            ChunkEmbeddingIndex chunkIdx;
            EntityFirstChapterIndex firstIdx;
            try
            {
                chapterIdx = ServiceLocator.Get<ChapterEmbeddingIndex>();
                chunkIdx = ServiceLocator.Get<ChunkEmbeddingIndex>();
                firstIdx = ServiceLocator.Get<EntityFirstChapterIndex>();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[Reconciler] 向量对账：无法定位索引服务（向量方案可能未启用）: {ex.Message}");
                return;
            }

            try
            {
                await Task.WhenAll(chapterIdx.LoadAsync(), chunkIdx.LoadAsync(), firstIdx.LoadAsync()).ConfigureAwait(false);

                int removed = 0;

                foreach (var key in chapterIdx.GetAllKeys())
                {
                    if (!mdChapterIds.Contains(key))
                    {
                        if (await chapterIdx.RemoveAsync(key).ConfigureAwait(false)) removed++;
                    }
                }

                foreach (var key in chunkIdx.GetAllKeys())
                {
                    if (!ChunkKey.TryParse(key, out var cid, out _) || !mdChapterIds.Contains(cid))
                    {
                        if (await chunkIdx.RemoveAsync(key).ConfigureAwait(false)) removed++;
                    }
                }

                var orphanFirstChapters = firstIdx.GetAll()
                    .Select(e => e.ChapterId)
                    .Where(cid => !string.IsNullOrEmpty(cid) && !mdChapterIds.Contains(cid))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                foreach (var cid in orphanFirstChapters)
                {
                    removed += await firstIdx.InvalidateByChapterAsync(cid).ConfigureAwait(false);
                }

                if (firstIdx.Count > 0)
                {
                    try
                    {
                        var liveEntityIds = await BuildLiveCharacterIdSetAsync().ConfigureAwait(false);
                        if (liveEntityIds.Count > 0)
                        {
                            var orphanEntityIds = firstIdx.GetAll()
                                .Select(e => e.EntityId)
                                .Where(id => !string.IsNullOrEmpty(id) && !liveEntityIds.Contains(id))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList();
                            if (orphanEntityIds.Count > 0)
                            {
                                var removedByEntity = await firstIdx.InvalidateByEntitiesAsync(orphanEntityIds).ConfigureAwait(false);
                                removed += removedByEntity;
                                if (removedByEntity > 0)
                                    TM.App.Log($"[Reconciler] 首次描写按实体孤立清理 {removedByEntity} 条（已不在 Design/Guide）");
                            }
                        }
                    }
                    catch (Exception entityCleanupEx)
                    {
                        TM.App.Log($"[Reconciler] 首次描写按实体孤立清理失败（非致命）: {entityCleanupEx.Message}");
                    }
                }

                if (removed > 0)
                {
                    result.VectorOrphansCleared = removed;
                    TM.App.Log($"[Reconciler] 向量索引孤立清理 {removed} 条");
                }

                var chapterKeys = new HashSet<string>(chapterIdx.GetAllKeys(), StringComparer.OrdinalIgnoreCase);

                var chunkedChapters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var k in chunkIdx.GetAllKeys())
                {
                    if (ChunkKey.TryParse(k, out var cid, out _))
                        chunkedChapters.Add(cid);
                }

                var missingChapterIds = mdChapterIds
                    .Where(id => !chapterKeys.Contains(id) || !chunkedChapters.Contains(id))
                    .ToList();

                if (missingChapterIds.Count > 0)
                {
                    var emb = ServiceLocator.Get<Services.Framework.AI.Embedding.IMicroEmbeddingService>();
                    if (!emb.IsModelReady())
                    {
                        TM.App.Log($"[Reconciler] 向量模型未就绪，{missingChapterIds.Count} 章补建延后到下次对账");
                    }
                    else
                    {
                        ContentGenerationCallback? callback = null;
                        try { callback = ServiceLocator.Get<ContentGenerationCallback>(); }
                        catch (Exception ex) { TM.App.Log($"[Reconciler] 无法定位 ContentGenerationCallback: {ex.Message}"); }

                        if (callback != null)
                        {
                            int rebuilt = 0;
                            foreach (var cid in missingChapterIds)
                            {
                                try
                                {
                                    var mdPath = Path.Combine(chaptersPath, $"{cid}.md");
                                    if (!File.Exists(mdPath)) continue;
                                    var content = await File.ReadAllTextAsync(mdPath).ConfigureAwait(false);
                                    if (string.IsNullOrWhiteSpace(content)) continue;

                                    await callback.RebuildVectorIndicesForChapterAsync(cid, content, null, null).ConfigureAwait(false);
                                    rebuilt++;
                                }
                                catch (Exception ex)
                                {
                                    TM.App.Log($"[Reconciler] 章节 {cid} 向量补建失败（非致命）: {ex.Message}");
                                }
                            }
                            if (rebuilt > 0)
                            {
                                result.VectorReindexed = rebuilt;
                                TM.App.Log($"[Reconciler] 向量索引补建 {rebuilt}/{missingChapterIds.Count} 章");
                            }
                        }
                    }
                }

                int firstDescCaptured = 0;
                try
                {
                    var embForFirst = ServiceLocator.Get<Services.Framework.AI.Embedding.IMicroEmbeddingService>();
                    if (embForFirst.IsModelReady() && chunkIdx.Count > 0)
                    {
                        var combined = new Dictionary<string, (string Name, string Identity)>(
                            ContentGenerationCallback.BuildDesignCharacterMap(),
                            StringComparer.OrdinalIgnoreCase);

                        try
                        {
                            var csVols = _guideManager.GetExistingVolumeNumbers("character_state_guide.json");
                            if (csVols.Count > 0)
                            {
                                var csGuides = await Task.WhenAll(csVols.Select(v =>
                                    _guideManager.GetGuideAsync<CharacterStateGuide>(GuideManager.GetVolumeFileName("character_state_guide.json", v))))
                                    .ConfigureAwait(false);
                                foreach (var g in csGuides)
                                {
                                    foreach (var kv in g.Characters)
                                    {
                                        if (string.IsNullOrEmpty(kv.Key)) continue;
                                        if (combined.ContainsKey(kv.Key)) continue;
                                        var name = kv.Value?.Name ?? string.Empty;
                                        if (string.IsNullOrWhiteSpace(name)) continue;
                                        combined[kv.Key] = (name, string.Empty);
                                    }
                                }
                            }
                        }
                        catch (Exception guideEx)
                        {
                            TM.App.Log($"[Reconciler] 首次描写补建读取 Guide 层失败（非致命，仅用 Design）: {guideEx.Message}");
                        }

                        var pendingIds = combined
                            .Where(kv => !firstIdx.Contains(kv.Key))
                            .Select(kv => kv.Key)
                            .ToList();

                        if (pendingIds.Count > 0)
                        {
                            foreach (var id in pendingIds)
                            {
                                if (!combined.TryGetValue(id, out var entry)) continue;
                                try
                                {
                                    var ok = await firstIdx.RebuildAsync(id, entry.Name, entry.Identity).ConfigureAwait(false);
                                    if (ok) firstDescCaptured++;
                                }
                                catch (Exception ex)
                                {
                                    TM.App.Log($"[Reconciler] 首次描写补建 {id} 失败（非致命）: {ex.Message}");
                                }
                            }

                            if (firstDescCaptured > 0)
                            {
                                result.FirstDescriptionCaptured = firstDescCaptured;
                                TM.App.Log($"[Reconciler] 首次描写冷启动补建 {firstDescCaptured}/{pendingIds.Count} 个角色（Design∪Guide）");
                            }
                        }
                    }
                    else if (!embForFirst.IsModelReady())
                    {
                        TM.App.Log("[Reconciler] 向量模型未就绪，首次描写冷启动补建延后到下次对账");
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[Reconciler] 首次描写对账失败（非致命）: {ex.Message}");
                }

                if (removed > 0 || result.VectorReindexed > 0 || firstDescCaptured > 0)
                {
                    await Task.WhenAll(chapterIdx.SaveAsync(), chunkIdx.SaveAsync(), firstIdx.SaveAsync()).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"向量索引对账失败: {ex.Message}");
                TM.App.Log($"[Reconciler] 向量索引对账失败（非致命）: {ex.Message}");
            }
        }

        private async Task<HashSet<string>> BuildLiveCharacterIdSetAsync()
        {
            var liveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var designMap = ContentGenerationCallback.BuildDesignCharacterMap();
                foreach (var id in designMap.Keys)
                    if (!string.IsNullOrEmpty(id)) liveIds.Add(id);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[Reconciler] 聚合 Design 角色 ID 失败（非致命，继续 Guide 源）: {ex.Message}");
            }

            try
            {
                var csVols = _guideManager.GetExistingVolumeNumbers("character_state_guide.json");
                if (csVols.Count > 0)
                {
                    var csGuides = await Task.WhenAll(csVols.Select(v =>
                        _guideManager.GetGuideAsync<CharacterStateGuide>(GuideManager.GetVolumeFileName("character_state_guide.json", v))))
                        .ConfigureAwait(false);
                    foreach (var g in csGuides)
                        foreach (var id in g.Characters.Keys)
                            if (!string.IsNullOrEmpty(id)) liveIds.Add(id);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[Reconciler] 聚合 Guide 角色 ID 失败（非致命）: {ex.Message}");
            }

            return liveIds;
        }

        private static async Task<string> ReadHeadAsync(string filePath, int maxChars)
        {
            var bufferSize = maxChars * 3;
            var buffer = new byte[bufferSize];
            int bytesRead;

            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            }

            if (bytesRead == 0) return string.Empty;
            var text = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
            return text.Length > maxChars ? text[..maxChars] : text;
        }

        private static string ExtractSummaryFromHead(string content)
        {
            if (string.IsNullOrEmpty(content)) return string.Empty;

            var cleaned = content.Replace("\r\n", " ").Replace("\n", " ").Trim();
            if (cleaned.Length <= 500) return cleaned;

            var cutRegion = cleaned[..500];
            var lastSentenceEnd = cutRegion.LastIndexOfAny(new[] { '。', '！', '？', '…', '"' });
            if (lastSentenceEnd > 200)
            {
                return cutRegion[..(lastSentenceEnd + 1)] + "……";
            }

            return cutRegion + "……";
        }
    }
}

