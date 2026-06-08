using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TM.Services.Framework.AI.SemanticKernel;
using TM.Services.Modules.ProjectData.Models.Tracking;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Implementations.Generation;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class ContentGenerationCallback
    {

        public async Task OnContentGeneratedStrictAsync(
            string chapterId,
            string rawContent,
            FactSnapshot factSnapshot,
            GateResult? gateResult = null,
            DesignElementNames? designElements = null,
            ContextIdCollection? contextIds = null)
        {
            await OnContentGeneratedInternalAsync(
                chapterId,
                rawContent,
                factSnapshot,
                gateResult,
                designElements,
                contextIds).ConfigureAwait(false);
        }

        public async Task OnExternalContentSavedAsync(string chapterId, string content)
        {
            using var _ = GenerationCorrelation.Current == "no-correlation"
                ? GenerationCorrelation.Begin($"ext_{chapterId}_{DateTime.Now:HHmmss}")
                : null;
            TM.App.Log($"[ContentCallback][{GenerationCorrelation.Current}] 外部内容保存: {chapterId}");

            var contentWithChanges = content;
            ChapterChanges? externalChanges = null;
            var protocol = _generationGate.ValidateChangesProtocol(contentWithChanges);
            if (!protocol.Success)
            {
                var hasProtocolError = protocol.Errors
                    .Any(e => !e.Contains("未识别到CHANGES区域", StringComparison.Ordinal)
                           && !e.Contains("内容为空", StringComparison.Ordinal));
                if (hasProtocolError)
                {
                    var reason = string.Join("; ", protocol.Errors.Take(3));
                    throw new InvalidOperationException($"外部内容CHANGES协议无效: {reason}");
                }
            }
            if (protocol.Success && protocol.Changes != null)
            {
                FactSnapshot factSnapshot;
                Models.Guides.ContentGuideEntry? entry = null;
                try
                {
                    var guideService = ServiceLocator.Get<GuideContextService>();
                    var contentGuide = await guideService.GetContentGuideAsync().ConfigureAwait(false);
                    if (contentGuide?.Chapters == null
                        || !contentGuide.Chapters.TryGetValue(chapterId, out entry)
                        || entry?.ContextIds == null)
                    {
                        throw new InvalidOperationException($"未找到章节 {chapterId} 的 ContextIds，无法执行一致性校验");
                    }

                    var ctxValid = await guideService.ValidateContextIdsAsync(entry.ContextIds).ConfigureAwait(false);
                    if (!ctxValid.IsValid)
                    {
                        throw new InvalidOperationException($"ContextIds 校验失败: {ctxValid.GetErrorSummary()}");
                    }

                    factSnapshot = await guideService.ExtractFactSnapshotForChapterAsync(chapterId, entry.ContextIds).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"外部内容一致性校验准备失败: {ex.Message}");
                }

                TM.Services.Framework.AI.SemanticKernel.GenerationProgressHub.ReportPhase(
                    TM.Services.Framework.AI.SemanticKernel.ProgressPhase.Validating, $"正在校验外部保存章节 {chapterId}...");

                var gateResult = await _generationGate.ValidateAsync(
                    chapterId,
                    contentWithChanges,
                    factSnapshot,
                    designElements: null,
                    contextIds: entry.ContextIds).ConfigureAwait(false);

                if (!gateResult.Success)
                {
                    var reasons = string.Join("; ", gateResult.GetHumanReadableFailures(5));
                    throw new InvalidOperationException($"外部内容落盘前校验失败: {reasons}");
                }

                externalChanges = gateResult.ParsedChanges;
                content = gateResult.ContentWithoutChanges ?? protocol.ContentWithoutChanges ?? content;
                TM.App.Log($"[ContentCallback][{GenerationCorrelation.Current}] {chapterId} 检测到CHANGES块，将同步追踪Guide");

                ImportanceCorrector.Correct(externalChanges);

                if (externalChanges != null)
                    await _changesWalStore.WriteAsync(chapterId, externalChanges).ConfigureAwait(false);
            }

            var persistedContent = await NormalizePersistedContentAsync(chapterId, content).ConfigureAwait(false);

            var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
            var stagingPath = Path.Combine(chaptersPath, ".staging");
            var chapterFile = Path.Combine(chaptersPath, $"{chapterId}.md");
            var stagingFile = Path.Combine(stagingPath, $"{chapterId}.md");
            var backupFile = chapterFile + ".bak";
            var hadExistingFile = File.Exists(chapterFile);
            var contentChanged = true;
            if (hadExistingFile)
            {
                try
                {
                    var oldPersisted = await File.ReadAllTextAsync(chapterFile).ConfigureAwait(false);
                    contentChanged = !string.Equals(oldPersisted, persistedContent, StringComparison.Ordinal);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ContentCallback] {chapterId} 比较旧正文失败（按已变更处理）: {ex.Message}");
                    contentChanged = true;
                }
            }

            string? summary = null;
            Dictionary<string, string>? nameMap = null;
            List<string> purgedFirstIdxIds = new();
            var guideFlushed = false;
            try
            {
                if (!Directory.Exists(stagingPath))
                    Directory.CreateDirectory(stagingPath);

                TM.Services.Framework.AI.SemanticKernel.GenerationProgressHub.ReportPhase(
                    TM.Services.Framework.AI.SemanticKernel.ProgressPhase.Persisting, $"正在保存外部章节 {chapterId}...");

                await File.WriteAllTextAsync(stagingFile, persistedContent).ConfigureAwait(false);

                if (hadExistingFile)
                    await Task.Run(async () =>
                    {
                        await using var s = File.OpenRead(chapterFile);
                        await using var d = File.Create(backupFile);
                        await s.CopyToAsync(d).ConfigureAwait(false);
                    }).ConfigureAwait(false);

                File.Move(stagingFile, chapterFile, overwrite: true);

                nameMap = externalChanges != null ? await BuildEntityNameMapAsync().ConfigureAwait(false) : null;
                summary = externalChanges != null
                    ? BuildStructuredSummary(persistedContent, externalChanges, nameMap)
                    : ExtractSummary(persistedContent);

                if (externalChanges != null)
                {
                    if (hadExistingFile)
                    {
                        purgedFirstIdxIds = await RemoveTrackingDataForChapterAsync(chapterId).ConfigureAwait(false);
                        TM.App.Log($"[ContentCallback][{GenerationCorrelation.Current}] {chapterId} 外部重写：已清除旧追踪数据 (carry-over={purgedFirstIdxIds.Count})");
                    }

                    await UpdateTrackingGuidesAsync(chapterId, externalChanges).ConfigureAwait(false);
                    TM.App.Log($"[ContentCallback][{GenerationCorrelation.Current}] {chapterId} 外部CHANGES追踪已更新");
                }
                else if (hadExistingFile && contentChanged)
                {
                    TM.App.Log($"[ContentCallback][{GenerationCorrelation.Current}] {chapterId} 未提供CHANGES，保留旧追踪数据（静默保存）");
                }

                await _guideManager.FlushAllAsync().ConfigureAwait(false);
                guideFlushed = true;

                if (externalChanges != null)
                {
                    if (VerifyCommitSync(chapterId))
                    {
                        _changesWalStore.Delete(chapterId);
                    }
                    else
                    {
                        TM.App.Log($"[ContentCallback][{GenerationCorrelation.Current}] {chapterId} 外部保存提交验证未通过，保留WAL供下次启动恢复");
                    }
                }

                ServiceLocator.Get<GuideContextService>().InvalidateContentGuideCache();

                await UpdateChapterSummaryAsync(chapterId, summary).ConfigureAwait(false);

                await _ledgerTrim.TrimAllAsync().ConfigureAwait(false);

                if (File.Exists(backupFile))
                    File.Delete(backupFile);

                TM.App.Log($"[ContentCallback][{GenerationCorrelation.Current}] {chapterId} ext ok");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback][{GenerationCorrelation.Current}] {chapterId} ext err: {ex.Message}");

                if (!guideFlushed)
                {
                    _guideManager.DiscardDirtyAndEvict();
                }

                try
                {
                    if (File.Exists(stagingFile))
                        File.Delete(stagingFile);

                    if (!guideFlushed)
                    {
                        if (hadExistingFile && File.Exists(backupFile))
                        {
                            await using (var s = File.OpenRead(backupFile))
                            await using (var d = File.Create(chapterFile))
                            {
                                await s.CopyToAsync(d).ConfigureAwait(false);
                            }
                            File.Delete(backupFile);
                        }
                        else if (!hadExistingFile && File.Exists(chapterFile))
                        {
                            File.Delete(chapterFile);
                        }
                    }
                    else
                    {
                        if (File.Exists(backupFile))
                            File.Delete(backupFile);
                        TM.App.Log($"[ContentCallback] {chapterId} partial ok");
                    }
                }
                catch (Exception rollbackEx)
                {
                    TM.App.Log($"[ContentCallback] {chapterId} 回滚失败: {rollbackEx.Message}");
                }

                if (!guideFlushed)
                    throw;
            }

            async Task RunKeywordIndexAsync()
            {
                if (externalChanges == null) return;
                try
                {
                    await ServiceLocator.Get<KeywordChapterIndexService>().IndexChapterAsync(chapterId, externalChanges).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ContentCallback] {chapterId} 关键词索引更新失败（不影响正文）: {ex.Message}");
                }
            }

            async Task BuildVectorsAsync() =>
                await RebuildVectorIndicesForChapterAsync(chapterId, persistedContent, externalChanges, nameMap, purgedFirstIdxIds).ConfigureAwait(false);

            await Task.WhenAll(
                RunKeywordIndexAsync(),
                TryUpdateVolumeMilestoneAsync(chapterId, summary, isRewrite: hadExistingFile, changes: externalChanges),
                BuildVectorsAsync()).ConfigureAwait(false);

            TM.Services.Framework.AI.SemanticKernel.GenerationProgressHub.ReportPhase(
                TM.Services.Framework.AI.SemanticKernel.ProgressPhase.Done, $"外部章节 {chapterId} 保存完成");
        }

        private async Task OnContentGeneratedInternalAsync(
            string chapterId,
            string rawContent,
            FactSnapshot factSnapshot,
            GateResult? gateResult,
            DesignElementNames? designElements = null,
            ContextIdCollection? contextIds = null)
        {
            var cfg = LayeredContextConfig.TakeSnapshot();
            ChapterChanges? changes;
            string content;

            const bool strictGate = true;

            TM.Services.Framework.AI.SemanticKernel.GenerationProgressHub.ReportPhase(
                TM.Services.Framework.AI.SemanticKernel.ProgressPhase.Validating, "正在落盘前最终校验...");

            var gateResultFinal = await _generationGate.ValidateAsync(
                chapterId,
                rawContent,
                factSnapshot,
                designElements,
                contextIds: contextIds).ConfigureAwait(false);

            if (!gateResultFinal.Success)
            {
                var err = $"[ContentCallback] {chapterId} 落盘前校验失败: {string.Join("; ", gateResultFinal.GetHumanReadableFailures(5))}";
                TM.App.Log(err);
                throw new InvalidOperationException(err);
            }

            changes = gateResultFinal.ParsedChanges;
            content = gateResultFinal.ContentWithoutChanges ?? rawContent;
            TM.App.Log($"[ContentCallback] {chapterId} 校验通过");

            ImportanceCorrector.Correct(changes);

            if (changes != null)
                await _changesWalStore.WriteAsync(chapterId, changes).ConfigureAwait(false);

            content = await NormalizePersistedContentAsync(chapterId, content).ConfigureAwait(false);

            var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
            var stagingPath = Path.Combine(chaptersPath, ".staging");
            var chapterFile = Path.Combine(chaptersPath, $"{chapterId}.md");
            var stagingFile = Path.Combine(stagingPath, $"{chapterId}.md");
            var backupFile = chapterFile + ".bak";
            var hadExistingFile = File.Exists(chapterFile);

            string? summary = null;
            List<string> purgedFirstIdxIds = new();
            var guideFlushed = false;
            try
            {
                if (!Directory.Exists(stagingPath))
                {
                    Directory.CreateDirectory(stagingPath);
                }

                TM.Services.Framework.AI.SemanticKernel.GenerationProgressHub.ReportPhase(
                    TM.Services.Framework.AI.SemanticKernel.ProgressPhase.Persisting, $"正在清洗并落盘章节 {chapterId}...");

                await File.WriteAllTextAsync(stagingFile, content).ConfigureAwait(false);
                TM.App.Log($"[ContentCallback] S1: {chapterId}");

                if (hadExistingFile)
                {
                    await Task.Run(async () =>
                    {
                        await using var s = File.OpenRead(chapterFile);
                        await using var d = File.Create(backupFile);
                        await s.CopyToAsync(d).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                }

                File.Move(stagingFile, chapterFile, overwrite: true);
                TM.App.Log($"[ContentCallback] S2: {chapterId}");

                var nameMap = changes != null ? await BuildEntityNameMapAsync().ConfigureAwait(false) : null;
                summary = changes != null
                    ? BuildStructuredSummary(content, changes, nameMap)
                    : ExtractSummary(content);

                if (strictGate && changes != null && !string.IsNullOrWhiteSpace(summary))
                {
                    try
                    {
                        var driftResult = await _driftFallbackPatcher.DetectAndApplyAsync(chapterId, summary, changes).ConfigureAwait(false);
                        if (driftResult.PatchedChanges)
                            summary = BuildStructuredSummary(content, changes, nameMap);
                        foreach (var dirtyFile in driftResult.DirtyGuideFiles)
                            _guideManager.MarkDirty(dirtyFile);
                    }
                    catch (Exception driftEx)
                    {
                        TM.App.Log($"[ContentCallback] 漂移兜底失败（非致命）: {driftEx.Message}");
                    }
                }

                if (hadExistingFile && changes != null)
                {
                    purgedFirstIdxIds = await RemoveTrackingDataForChapterAsync(chapterId).ConfigureAwait(false);
                }

                if (changes != null)
                {
                    await UpdateTrackingGuidesAsync(chapterId, changes).ConfigureAwait(false);
                }
                else
                {
                    TM.App.Log($"[ContentCallback] {chapterId} 无CHANGES，跳过追踪更新");
                }

                await _guideManager.FlushAllAsync().ConfigureAwait(false);
                guideFlushed = true;

                if (changes != null)
                {
                    if (VerifyCommitSync(chapterId))
                    {
                        _changesWalStore.Delete(chapterId);
                    }
                    else
                    {
                        TM.App.Log($"[ContentCallback] {chapterId} 提交验证未通过，保留WAL供下次启动恢复");
                    }
                }

                ServiceLocator.Get<GuideContextService>().InvalidateContentGuideCache();

                async Task WriteSummaryAsync() => await UpdateChapterSummaryAsync(chapterId, summary).ConfigureAwait(false);
                async Task TrimLedgerAsync() => await _ledgerTrim.TrimAllAsync().ConfigureAwait(false);
                async Task IndexKeywordInternalAsync()
                {
                    if (changes == null) return;
                    try { await ServiceLocator.Get<KeywordChapterIndexService>().IndexChapterAsync(chapterId, changes).ConfigureAwait(false); }
                    catch (Exception ex) { TM.App.Log($"[ContentCallback] 关键词索引更新失败（非致命）: {ex.Message}"); }
                }
                async Task BuildVectorsAsync() =>
                    await RebuildVectorIndicesForChapterAsync(chapterId, content, changes, nameMap, purgedFirstIdxIds).ConfigureAwait(false);
                await Task.WhenAll(WriteSummaryAsync(), TrimLedgerAsync(), IndexKeywordInternalAsync(), BuildVectorsAsync()).ConfigureAwait(false);

                if (File.Exists(backupFile))
                {
                    File.Delete(backupFile);
                }

                TM.App.Log($"[ContentCallback] {chapterId} ok");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] {chapterId} err: {ex.Message}");

                if (!guideFlushed)
                {
                    _guideManager.DiscardDirtyAndEvict();
                }

                try
                {
                    if (File.Exists(stagingFile))
                    {
                        File.Delete(stagingFile);
                    }

                    if (!guideFlushed)
                    {
                        if (hadExistingFile && File.Exists(backupFile))
                        {
                            await Task.Run(async () =>
                            {
                                await using var s = File.OpenRead(backupFile);
                                await using var d = File.Create(chapterFile);
                                await s.CopyToAsync(d).ConfigureAwait(false);
                            }).ConfigureAwait(false);
                            File.Delete(backupFile);
                            TM.App.Log($"[ContentCallback] {chapterId} 已恢复备份");
                        }
                        else if (!hadExistingFile && File.Exists(chapterFile))
                        {
                            File.Delete(chapterFile);
                            TM.App.Log($"[ContentCallback] {chapterId} 已删除新建文件");
                        }
                    }
                    else
                    {
                        if (File.Exists(backupFile))
                            File.Delete(backupFile);
                        TM.App.Log($"[ContentCallback] {chapterId} partial ok (idx)");
                    }
                }
                catch (Exception rollbackEx)
                {
                    TM.App.Log($"[ContentCallback] {chapterId} 回滚失败: {rollbackEx.Message}");
                }

                if (!guideFlushed)
                    throw;
            }

            await TryUpdateVolumeMilestoneAsync(chapterId, summary, isRewrite: hadExistingFile, changes: changes).ConfigureAwait(false);

            TM.Services.Framework.AI.SemanticKernel.GenerationProgressHub.ReportPhase(
                TM.Services.Framework.AI.SemanticKernel.ProgressPhase.Done, $"章节 {chapterId} 生成完成");
        }

    }
}
