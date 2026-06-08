using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Tracking;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Implementations.Indexing;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class ContentGenerationCallback
    {
        public async Task<bool> RepairTrackingFromWalAsync(string chapterId, ChapterChanges changes)
        {
            TM.App.Log($"[ContentCallback] WAL修复追踪开始: {chapterId}");
            try
            {
                var purgedFirstIdxIds = await RemoveTrackingDataForChapterAsync(chapterId).ConfigureAwait(false);
                await UpdateTrackingGuidesAsync(chapterId, changes).ConfigureAwait(false);
                await _guideManager.FlushAllAsync().ConfigureAwait(false);

                if (VerifyCommitSync(chapterId))
                {
                    _changesWalStore.Delete(chapterId);
                    TM.App.Log($"[ContentCallback] WAL修复追踪完成: {chapterId}");
                }
                else
                {
                    TM.App.Log($"[ContentCallback] WAL修复后提交验证未通过，保留WAL: {chapterId}");
                }

                ServiceLocator.Get<GuideContextService>().InvalidateContentGuideCache();

                await RebuildVectorIndicesAfterRepairAsync(chapterId, changes, purgedFirstIdxIds).ConfigureAwait(false);

                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] WAL修复追踪失败 {chapterId}: {ex.Message}");
                _guideManager.DiscardDirtyAndEvict();
                return false;
            }
        }

        private async Task RebuildVectorIndicesAfterRepairAsync(
            string chapterId,
            ChapterChanges? changes,
            IReadOnlyList<string>? carryOverEntityIds = null)
        {
            try
            {
                var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
                var mdPath = Path.Combine(chaptersPath, $"{chapterId}.md");
                if (!File.Exists(mdPath))
                {
                    TM.App.Log($"[ContentCallback] {chapterId} md 缺失，跳过向量重建（对账兜底）");
                    return;
                }

                var persistedContent = await File.ReadAllTextAsync(mdPath).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(persistedContent))
                {
                    TM.App.Log($"[ContentCallback] {chapterId} md 为空，跳过向量重建");
                    return;
                }

                var nameMap = changes != null ? await BuildEntityNameMapAsync().ConfigureAwait(false) : null;
                await RebuildVectorIndicesForChapterAsync(chapterId, persistedContent, changes, nameMap, carryOverEntityIds).ConfigureAwait(false);
                TM.App.Log($"[ContentCallback] {chapterId} WAL/重建后向量索引补齐完成");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] {chapterId} WAL/重建后向量补齐失败（非致命，对账兜底）: {ex.Message}");
            }
        }

        public async Task<string?> RebuildTrackingFromContentAsync(string chapterId, string contentWithChanges)
        {
            TM.App.Log($"[ContentCallback] 重建追踪开始: {chapterId}");

            var protocol = _generationGate.ValidateChangesProtocol(contentWithChanges);
            if (!protocol.Success || protocol.Changes == null)
            {
                var noChangesMsg = "当前内容不含 CHANGES 块，无法重建追踪。请先确保内容包含有效的 CHANGES 区域。";
                TM.App.Log($"[ContentCallback] 重建追踪失败: {chapterId} 未找到CHANGES块");
                return noChangesMsg;
            }

            var structResult = _generationGate.ValidateStructuralOnly(protocol.Changes);
            if (!structResult.Success)
            {
                var issues = string.Join("; ", structResult.GetIssueDescriptions().Take(5));
                var structMsg = $"CHANGES 结构校验失败: {issues}";
                TM.App.Log($"[ContentCallback] 重建追踪失败: {chapterId} {structMsg}");
                return structMsg;
            }

            ChapterChanges changes;
            try
            {
                var guideService = ServiceLocator.Get<GuideContextService>();
                var contentGuide = await guideService.GetContentGuideAsync().ConfigureAwait(false);
                if (contentGuide?.Chapters == null
                    || !contentGuide.Chapters.TryGetValue(chapterId, out var entry)
                    || entry?.ContextIds == null)
                {
                    throw new InvalidOperationException($"未找到章节 {chapterId} 的 ContextIds，无法执行一致性校验");
                }

                var ctxValid = await guideService.ValidateContextIdsAsync(entry.ContextIds).ConfigureAwait(false);
                if (!ctxValid.IsValid)
                {
                    throw new InvalidOperationException($"ContextIds 校验失败: {ctxValid.GetErrorSummary()}");
                }

                var factSnapshot = await guideService.ExtractFactSnapshotForChapterAsync(chapterId, entry.ContextIds).ConfigureAwait(false);
                var gateResult = await _generationGate.ValidateAsync(
                    chapterId,
                    contentWithChanges,
                    factSnapshot,
                    designElements: null,
                    contextIds: entry.ContextIds).ConfigureAwait(false);
                if (!gateResult.Success)
                {
                    var reasons = string.Join("; ", gateResult.GetHumanReadableFailures(5));
                    var msg = $"重建追踪前校验失败: {reasons}";
                    TM.App.Log($"[ContentCallback] {chapterId} {msg}");
                    return msg;
                }

                changes = gateResult.ParsedChanges ?? protocol.Changes;
            }
            catch (Exception ex)
            {
                var msg = $"重建追踪一致性校验准备失败: {ex.Message}";
                TM.App.Log($"[ContentCallback] {chapterId} {msg}");
                return msg;
            }

            try
            {
                var purgedFirstIdxIds = await RemoveTrackingDataForChapterAsync(chapterId).ConfigureAwait(false);
                TM.App.Log($"[ContentCallback] 重建追踪: {chapterId} 旧数据已清除");

                await UpdateTrackingGuidesAsync(chapterId, changes).ConfigureAwait(false);
                TM.App.Log($"[ContentCallback] 重建追踪: {chapterId} 新追踪已写入");

                await _guideManager.FlushAllAsync().ConfigureAwait(false);
                ServiceLocator.Get<GuideContextService>().InvalidateContentGuideCache();

                try
                {
                    await ServiceLocator.Get<KeywordChapterIndexService>().IndexChapterAsync(chapterId, changes).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ContentCallback] 重建追踪: {chapterId} 关键词索引更新失败（非致命）: {ex.Message}");
                }

                await RebuildVectorIndicesAfterRepairAsync(chapterId, changes, purgedFirstIdxIds).ConfigureAwait(false);

                TM.App.Log($"[ContentCallback] 重建追踪完成: {chapterId}");
                return null;
            }
            catch (Exception ex)
            {
                var errMsg = $"重建追踪失败: {ex.Message}";
                TM.App.Log($"[ContentCallback] {chapterId} {errMsg}");
                _guideManager.DiscardDirtyAndEvict();
                return errMsg;
            }
        }

        private async Task<List<string>> RemoveTrackingDataForChapterAsync(string chapterId)
        {
            List<string> purgedFirstIdxIds = new();
            try
            {
                var firstIdx = ServiceLocator.Get<EntityFirstChapterIndex>();
                await firstIdx.LoadAsync().ConfigureAwait(false);
                purgedFirstIdxIds = firstIdx.GetAll()
                    .Where(e => string.Equals(e.ChapterId, chapterId, StringComparison.OrdinalIgnoreCase))
                    .Select(e => e.EntityId)
                    .ToList();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] {chapterId} 快照 firstIdx 本章条目失败（非致命，carry-over 补偿将跳过）: {ex.Message}");
            }

            async Task SafeRemoveAsync(Task task, string label)
            {
                try { await task.ConfigureAwait(false); }
                catch (Exception ex) { TM.App.Log($"[ContentCallback] {chapterId} 清除{label}失败（重写前）: {ex.Message}"); }
            }

            await Task.WhenAll(
                SafeRemoveAsync(_characterStateService.RemoveChapterDataAsync(chapterId), "角色状态"),
                SafeRemoveAsync(_conflictProgressService.RemoveChapterDataAsync(chapterId), "冲突进度"),
                SafeRemoveAsync(_plotPointsIndexService.RemoveChapterDataAsync(chapterId), "情节索引"),
                SafeRemoveAsync(_foreshadowingStatusService.RemoveChapterDataAsync(chapterId), "伏笔状态"),
                SafeRemoveAsync(_locationStateService.RemoveChapterDataAsync(chapterId), "地点状态"),
                SafeRemoveAsync(_factionStateService.RemoveChapterDataAsync(chapterId), "势力状态"),
                SafeRemoveAsync(_timelineService.RemoveChapterDataAsync(chapterId), "时间线"),
                SafeRemoveAsync(_itemStateService.RemoveChapterDataAsync(chapterId), "物品状态"),
                SafeRemoveAsync(_secretRevealService.RemoveChapterDataAsync(chapterId), "秘密知情"),
                SafeRemoveAsync(_pledgeConstraintService.RemoveChapterDataAsync(chapterId), "承诺/契约"),
                SafeRemoveAsync(_deadlineConstraintService.RemoveChapterDataAsync(chapterId), "倒计时/时限"),
                SafeRemoveAsync(ServiceLocator.Get<KeywordChapterIndexService>().RemoveChapterAsync(chapterId), "关键词索引")
            ).ConfigureAwait(false);

            try { ServiceLocator.Get<RelationStrengthService>().InvalidateCache(); }
            catch (Exception ex) { TM.App.Log($"[ContentCallback] {chapterId} 关联强度缓存失效失败（重写前）: {ex.Message}"); }

            await CleanVectorIndicesAsync(chapterId).ConfigureAwait(false);

            TM.App.Log($"[ContentCallback] 已清除 {chapterId} 旧追踪数据（重写前），carry-over firstIdx 条目={purgedFirstIdxIds.Count}");
            return purgedFirstIdxIds;
        }

        private async Task UpdateTrackingGuidesAsync(string chapterId, ChapterChanges changes)
        {
            async Task UpdateCharStatesAsync()
            {
                var count = changes.CharacterStateChanges?.Count ?? 0;
                foreach (var change in changes.CharacterStateChanges ?? new())
                    await _characterStateService.UpdateCharacterStateAsync(chapterId, change).ConfigureAwait(false);
                if (count > 0)
                    TM.App.Log($"[ContentCallback] {chapterId} 更新角色状态: {count}条");
            }

            async Task UpdateConflictsAsync()
            {
                var count = changes.ConflictProgress?.Count ?? 0;
                foreach (var change in changes.ConflictProgress ?? new())
                    await _conflictProgressService.UpdateConflictProgressAsync(chapterId, change).ConfigureAwait(false);
                if (count > 0)
                    TM.App.Log($"[ContentCallback] {chapterId} 更新冲突进度: {count}条");
            }

            async Task UpdatePlotPointsAsync()
            {
                var count = changes.NewPlotPoints?.Count ?? 0;
                foreach (var change in changes.NewPlotPoints ?? new())
                    await _plotPointsIndexService.AddPlotPointAsync(chapterId, change).ConfigureAwait(false);
                if (count > 0)
                    TM.App.Log($"[ContentCallback] {chapterId} 添加关键情节: {count}条");
            }

            async Task UpdateForeshadowingAsync()
            {
                var count = changes.ForeshadowingActions?.Count ?? 0;
                foreach (var action in changes.ForeshadowingActions ?? new())
                {
                    if (string.Equals(action.Action, "setup", StringComparison.OrdinalIgnoreCase))
                        await _foreshadowingStatusService.MarkAsSetupAsync(action.ForeshadowId, chapterId).ConfigureAwait(false);
                    else if (string.Equals(action.Action, "payoff", StringComparison.OrdinalIgnoreCase))
                        await _foreshadowingStatusService.MarkAsResolvedAsync(action.ForeshadowId, chapterId).ConfigureAwait(false);
                }
                if (count > 0)
                    TM.App.Log($"[ContentCallback] {chapterId} 更新伏笔状态: {count}条");
            }

            async Task UpdateLocationsAsync()
            {
                var count = changes.LocationStateChanges?.Count ?? 0;
                foreach (var change in changes.LocationStateChanges ?? new())
                    await _locationStateService.UpdateLocationStateAsync(chapterId, change).ConfigureAwait(false);
                if (count > 0)
                    TM.App.Log($"[ContentCallback] {chapterId} 更新地点状态: {count}条");
            }

            async Task UpdateFactionsAsync()
            {
                var count = changes.FactionStateChanges?.Count ?? 0;
                foreach (var change in changes.FactionStateChanges ?? new())
                    await _factionStateService.UpdateFactionStateAsync(chapterId, change).ConfigureAwait(false);
                if (count > 0)
                    TM.App.Log($"[ContentCallback] {chapterId} 更新势力状态: {count}条");
            }

            async Task UpdateTimelineAsync()
            {
                if (changes.TimeProgression != null)
                {
                    await _timelineService.UpdateTimeProgressionAsync(chapterId, changes.TimeProgression).ConfigureAwait(false);
                    TM.App.Log($"[ContentCallback] {chapterId} 更新时间推进");
                }
                var movementCount = changes.CharacterMovements?.Count ?? 0;
                if (movementCount > 0)
                {
                    await _timelineService.UpdateCharacterMovementsAsync(chapterId, changes.CharacterMovements!).ConfigureAwait(false);
                    TM.App.Log($"[ContentCallback] {chapterId} 更新角色位置: {movementCount}条");
                }
            }

            async Task UpdateItemsAsync()
            {
                var count = changes.ItemTransfers?.Count ?? 0;
                foreach (var change in changes.ItemTransfers ?? new())
                    await _itemStateService.UpdateItemStateAsync(chapterId, change).ConfigureAwait(false);
                if (count > 0)
                    TM.App.Log($"[ContentCallback] {chapterId} 更新物品流转: {count}条");
            }

            async Task UpdateSecretsAsync()
            {
                var count = changes.SecretRevealChanges?.Count ?? 0;
                foreach (var change in changes.SecretRevealChanges ?? new())
                    await _secretRevealService.UpdateSecretRevealAsync(chapterId, change).ConfigureAwait(false);
                if (count > 0)
                    TM.App.Log($"[ContentCallback] {chapterId} 更新秘密知情: {count}条");
            }

            async Task UpdatePledgesAsync()
            {
                var count = changes.PledgeConstraintChanges?.Count ?? 0;
                foreach (var change in changes.PledgeConstraintChanges ?? new())
                    await _pledgeConstraintService.UpdatePledgeAsync(chapterId, change).ConfigureAwait(false);
                if (count > 0)
                    TM.App.Log($"[ContentCallback] {chapterId} 更新承诺/契约: {count}条");
            }

            async Task UpdateDeadlinesAsync()
            {
                var count = changes.DeadlineConstraintChanges?.Count ?? 0;
                foreach (var change in changes.DeadlineConstraintChanges ?? new())
                    await _deadlineConstraintService.UpdateDeadlineAsync(chapterId, change).ConfigureAwait(false);
                if (count > 0)
                    TM.App.Log($"[ContentCallback] {chapterId} 更新倒计时/时限: {count}条");
            }

            await Task.WhenAll(
                UpdateCharStatesAsync(),
                UpdateConflictsAsync(),
                UpdatePlotPointsAsync(),
                UpdateForeshadowingAsync(),
                UpdateLocationsAsync(),
                UpdateFactionsAsync(),
                UpdateTimelineAsync(),
                UpdateItemsAsync(),
                UpdateSecretsAsync(),
                UpdatePledgesAsync(),
                UpdateDeadlinesAsync()
            ).ConfigureAwait(false);

            await _foreshadowingStatusService.RefreshOverdueStatusAsync(chapterId).ConfigureAwait(false);

            var danglingCfg = TM.Services.Modules.ProjectData.Implementations.LayeredContextConfig.TakeSnapshot();
            await Task.WhenAll(
                _pledgeConstraintService.RefreshOverdueStatusAsync(chapterId, danglingCfg.PledgeMaxDanglingChapters),
                _deadlineConstraintService.RefreshOverdueStatusAsync(chapterId, danglingCfg.DeadlineMaxDanglingChapters)
            ).ConfigureAwait(false);

            TM.App.Log($"[ContentCallback] {chapterId} done");
        }

        private async System.Threading.Tasks.Task<System.Collections.Generic.Dictionary<string, string>> BuildEntityNameMapAsync()
        {
            var cached = _nameMapCache;
            if (cached != null && DateTime.UtcNow < cached.Expires)
                return cached.Map;

            var map = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            try
            {
                var _nmGm = _guideManager;
                var charVols = _nmGm.GetExistingVolumeNumbers("character_state_guide.json")
                    .Select(v => _nmGm.GetGuideAsync<CharacterStateGuide>(GuideManager.GetVolumeFileName("character_state_guide.json", v)));
                var confVols = _nmGm.GetExistingVolumeNumbers("conflict_progress_guide.json")
                    .Select(v => _nmGm.GetGuideAsync<ConflictProgressGuide>(GuideManager.GetVolumeFileName("conflict_progress_guide.json", v)));
                var fowTask = _nmGm.GetGuideAsync<ForeshadowingStatusGuide>("foreshadowing_status_guide.json");
                var locVols = _nmGm.GetExistingVolumeNumbers("location_state_guide.json")
                    .Select(v => _nmGm.GetGuideAsync<LocationStateGuide>(GuideManager.GetVolumeFileName("location_state_guide.json", v)));
                var facVols = _nmGm.GetExistingVolumeNumbers("faction_state_guide.json")
                    .Select(v => _nmGm.GetGuideAsync<FactionStateGuide>(GuideManager.GetVolumeFileName("faction_state_guide.json", v)));
                var pledgeVols = _nmGm.GetExistingVolumeNumbers("pledge_constraint_guide.json")
                    .Select(v => _nmGm.GetGuideAsync<PledgeConstraintGuide>(GuideManager.GetVolumeFileName("pledge_constraint_guide.json", v)));
                var deadlineVols = _nmGm.GetExistingVolumeNumbers("deadline_constraint_guide.json")
                    .Select(v => _nmGm.GetGuideAsync<DeadlineConstraintGuide>(GuideManager.GetVolumeFileName("deadline_constraint_guide.json", v)));
                var secretVols = _nmGm.GetExistingVolumeNumbers("secret_reveal_guide.json")
                    .Select(v => _nmGm.GetGuideAsync<SecretRevealGuide>(GuideManager.GetVolumeFileName("secret_reveal_guide.json", v)));

                var charGuides = await Task.WhenAll(charVols).ConfigureAwait(false);
                var confGuides = await Task.WhenAll(confVols).ConfigureAwait(false);
                var locGuides = await Task.WhenAll(locVols).ConfigureAwait(false);
                var facGuides = await Task.WhenAll(facVols).ConfigureAwait(false);
                var pledgeGuides = await Task.WhenAll(pledgeVols).ConfigureAwait(false);
                var deadlineGuides = await Task.WhenAll(deadlineVols).ConfigureAwait(false);
                var secretGuides = await Task.WhenAll(secretVols).ConfigureAwait(false);
                var _fowGuide = await fowTask.ConfigureAwait(false);

                foreach (var _g in charGuides) foreach (var (id, e) in _g.Characters) if (!string.IsNullOrWhiteSpace(e.Name)) map[id] = e.Name;
                foreach (var _g in confGuides) foreach (var (id, e) in _g.Conflicts) if (!string.IsNullOrWhiteSpace(e.Name)) map[id] = e.Name;
                foreach (var (id, e) in _fowGuide.Foreshadowings) if (!string.IsNullOrWhiteSpace(e.Name)) map[id] = e.Name;
                foreach (var _g in locGuides) foreach (var (id, e) in _g.Locations) if (!string.IsNullOrWhiteSpace(e.Name)) map[id] = e.Name;
                foreach (var _g in facGuides) foreach (var (id, e) in _g.Factions) if (!string.IsNullOrWhiteSpace(e.Name)) map[id] = e.Name;
                foreach (var _g in pledgeGuides) foreach (var (id, e) in _g.Pledges) if (!string.IsNullOrWhiteSpace(e.Name)) map[id] = e.Name;
                foreach (var _g in deadlineGuides) foreach (var (id, e) in _g.Deadlines) if (!string.IsNullOrWhiteSpace(e.Name)) map[id] = e.Name;
                foreach (var _g in secretGuides) foreach (var (id, e) in _g.Secrets) if (!string.IsNullOrWhiteSpace(e.Name)) map[id] = e.Name;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] BuildEntityNameMapAsync失败，将回退到ID: {ex.Message}");
            }
            _nameMapCache = new NameMapCacheEntry(map, DateTime.UtcNow.AddSeconds(60));
            return map;
        }

    }
}
