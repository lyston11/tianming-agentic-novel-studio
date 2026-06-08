using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Implementations.Guides;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class ConsistencyReconciler
    {
        private readonly GuideManager _guideManager;
        private readonly ChapterSummaryStore _summaryStore;
        private readonly ChapterMilestoneStore _milestoneStore;
        private readonly ChapterChangesWalStore _changesWalStore;

        public ConsistencyReconciler(
            GuideManager guideManager,
            ChapterSummaryStore summaryStore,
            ChapterMilestoneStore milestoneStore,
            ChapterChangesWalStore changesWalStore)
        {
            _guideManager = guideManager;
            _summaryStore = summaryStore;
            _milestoneStore = milestoneStore;
            _changesWalStore = changesWalStore;
        }

        public class ReconcileResult
        {
            public int StagingCleaned { get; set; }
            public int BakCleaned { get; set; }
            public int SummariesRepaired { get; set; }
            public int VectorReindexed { get; set; }
            public List<string> CorruptedGuides { get; set; } = new();
            public List<string> TrackingGaps { get; set; } = new();
            public int TrackingGapSummariesRepaired { get; set; }
            public int KeywordIndexRepaired { get; set; }
            public int FactArchivesRepaired { get; set; }
            public int TrackingOrphansCleared { get; set; }
            public int TrackingGapsRepaired { get; set; }
            public int VectorOrphansCleared { get; set; }
            public int FirstDescriptionCaptured { get; set; }
            public ConcurrentBag<string> Errors { get; set; } = new();

            public bool HasRepairs =>
                StagingCleaned > 0 || BakCleaned > 0 ||
                SummariesRepaired > 0 || VectorReindexed > 0 ||
                CorruptedGuides.Count > 0 || TrackingGapSummariesRepaired > 0 ||
                KeywordIndexRepaired > 0 || FactArchivesRepaired > 0 ||
                TrackingOrphansCleared > 0 || TrackingGapsRepaired > 0 ||
                VectorOrphansCleared > 0 || FirstDescriptionCaptured > 0;
        }

        public async Task<ReconcileResult> ReconcileAsync()
        {
            var result = new ReconcileResult();
            var projectAtStart = TM.Framework.Common.Helpers.Storage.StoragePathHelper.CurrentProjectName;
            TM.App.Log("[Reconciler] 开始一致性对账...");

            try
            {
                _guideManager.RecoverPendingFlush();
                TM.App.Log("[Reconciler] GuideManager pending flush 已检查/恢复");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[Reconciler] RecoverPendingFlush 失败（非致命）: {ex.Message}");
            }

            try
            {
                CleanStagingAndBackups(result);
                if (TM.Framework.Common.Helpers.Storage.StoragePathHelper.CurrentProjectName != projectAtStart) return result;

                await ValidateGuidesIntegrityAsync(result).ConfigureAwait(false);
                if (TM.Framework.Common.Helpers.Storage.StoragePathHelper.CurrentProjectName != projectAtStart) return result;

                await ReconcileChangesWalAsync(result).ConfigureAwait(false);
                if (TM.Framework.Common.Helpers.Storage.StoragePathHelper.CurrentProjectName != projectAtStart) return result;

                await ReconcileSummariesAsync(result).ConfigureAwait(false);
                if (TM.Framework.Common.Helpers.Storage.StoragePathHelper.CurrentProjectName != projectAtStart) return result;

                await DetectTrackingGapsAsync(result).ConfigureAwait(false);
                if (TM.Framework.Common.Helpers.Storage.StoragePathHelper.CurrentProjectName != projectAtStart) return result;

                await Task.WhenAll(
                    ReconcileKeywordIndexAsync(result),
                    ReconcileFactArchivesAsync(result),
                    ReconcileVectorIndicesAsync(result)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"对账过程异常: {ex.Message}");
                TM.App.Log($"[Reconciler] 对账异常: {ex.Message}");
            }

            if (result.HasRepairs || result.TrackingGaps.Count > 0)
            {
                TM.App.Log($"[Reconciler] done: s={result.StagingCleaned}, b={result.BakCleaned}, " +
                           $"sm={result.SummariesRepaired}, vi={result.VectorReindexed}, " +
                           $"cg={result.CorruptedGuides.Count}, tg={result.TrackingGaps.Count}, " +
                           $"wal={result.TrackingGapsRepaired}, " +
                           $"kw={result.KeywordIndexRepaired}, fa={result.FactArchivesRepaired}, " +
                           $"fd={result.FirstDescriptionCaptured}");
            }
            else
            {
                TM.App.Log("[Reconciler] 对账完成: 所有数据一致，无需修复");
            }

            return result;
        }

        private async Task ReconcileChangesWalAsync(ReconcileResult result)
        {
            try
            {
                _changesWalStore.CleanupOrphanTmp();

                var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
                if (!Directory.Exists(chaptersPath)) return;

                var walDir = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides", "changes_wal");

                var mdFiles = Directory.GetFiles(chaptersPath, "*.md", SearchOption.TopDirectoryOnly);
                if (mdFiles.Length == 0) return;

                var mdChapterIds = new HashSet<string>(
                    mdFiles.Select(f => Path.GetFileNameWithoutExtension(f)),
                    StringComparer.OrdinalIgnoreCase);

                var walIds = _changesWalStore.GetAllChapterIds();
                if (walIds.Count == 0) return;

                var callback = ServiceLocator.Get<ContentGenerationCallback>();
                foreach (var walId in walIds)
                {
                    if (!mdChapterIds.Contains(walId))
                    {
                        _changesWalStore.Delete(walId);
                        continue;
                    }

                    try
                    {
                        var mdPath = Path.Combine(chaptersPath, $"{walId}.md");
                        var walPath = Path.Combine(walDir, $"{walId}.json");
                        if (!File.Exists(mdPath) || !File.Exists(walPath))
                        {
                            _changesWalStore.Delete(walId);
                            continue;
                        }
                        if (File.GetLastWriteTimeUtc(mdPath) < File.GetLastWriteTimeUtc(walPath))
                        {
                            _changesWalStore.Delete(walId);
                            continue;
                        }
                    }
                    catch
                    {
                        continue;
                    }

                    var changes = await _changesWalStore.TryReadAsync(walId).ConfigureAwait(false);
                    if (changes == null) continue;

                    var ok = await callback.RepairTrackingFromWalAsync(walId, changes).ConfigureAwait(false);
                    if (ok) result.TrackingGapsRepaired++;
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"WAL回放失败: {ex.Message}");
            }
        }

        private void CleanStagingAndBackups(ReconcileResult result)
        {
            var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
            if (!Directory.Exists(chaptersPath)) return;

            var stagingPath = Path.Combine(chaptersPath, ".staging");
            if (Directory.Exists(stagingPath))
            {
                var stagingFiles = Directory.GetFiles(stagingPath, "*.md");
                foreach (var file in stagingFiles)
                {
                    try
                    {
                        var chapterId = Path.GetFileNameWithoutExtension(file);
                        var finalFile = Path.Combine(chaptersPath, $"{chapterId}.md");

                        if (!File.Exists(finalFile) ||
                            File.GetLastWriteTimeUtc(file) > File.GetLastWriteTimeUtc(finalFile))
                        {
                            File.Move(file, finalFile, overwrite: true);
                            TM.App.Log($"[Reconciler] recovered: {chapterId}");
                        }
                        else
                        {
                            File.Delete(file);
                        }
                        result.StagingCleaned++;
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"清理staging失败 {file}: {ex.Message}");
                    }
                }

                try
                {
                    if (Directory.Exists(stagingPath) && !Directory.EnumerateFileSystemEntries(stagingPath).Any())
                        Directory.Delete(stagingPath);
                }
                catch { }
            }

            var bakFiles = Directory.GetFiles(chaptersPath, "*.bak");
            foreach (var file in bakFiles)
            {
                try
                {
                    var baseName = Path.GetFileNameWithoutExtension(file);
                    var finalFile = Path.Combine(chaptersPath, baseName);

                    if (!File.Exists(finalFile))
                    {
                        File.Move(file, finalFile);
                        TM.App.Log($"[Reconciler] 从bak恢复: {baseName}");
                    }
                    else
                    {
                        File.Delete(file);
                    }
                    result.BakCleaned++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"清理bak失败 {file}: {ex.Message}");
                }
            }

            var configPath = StoragePathHelper.GetProjectConfigPath();
            if (Directory.Exists(configPath))
            {
                foreach (var tmpFile in Directory.GetFiles(configPath, "*.tmp", SearchOption.AllDirectories))
                {
                    try { File.Delete(tmpFile); result.StagingCleaned++; } catch { }
                }
            }

            var manifestTmp = Path.Combine(StoragePathHelper.GetCurrentProjectPath(), "manifest.json.tmp");
            if (File.Exists(manifestTmp))
            {
                try { File.Delete(manifestTmp); result.StagingCleaned++; } catch { }
            }

            try
            {
                var modulesRoot = Path.Combine(StoragePathHelper.GetStorageRoot(), "Modules");
                if (Directory.Exists(modulesRoot))
                {
                    foreach (var tmpFile in Directory.GetFiles(modulesRoot, "*.tmp", SearchOption.AllDirectories))
                    {
                        try { File.Delete(tmpFile); result.StagingCleaned++; } catch { }
                    }
                }
            }
            catch { }

            var guidesPath = Path.Combine(configPath, "guides");
            if (Directory.Exists(guidesPath))
            {
                foreach (var bakFile in Directory.GetFiles(guidesPath, "*.bak"))
                {
                    try
                    {
                        var originalName = bakFile[..^4];
                        if (!File.Exists(originalName))
                        {
                            File.Move(bakFile, originalName);
                            TM.App.Log($"[Reconciler] 从bak恢复guide: {Path.GetFileName(originalName)}");
                        }
                        else
                        {
                            File.Delete(bakFile);
                        }
                    }
                    catch { }
                }
            }

            var projectPath = StoragePathHelper.GetCurrentProjectPath();
            if (Directory.Exists(projectPath))
            {
                foreach (var backupDir in Directory.GetDirectories(projectPath, "_backup_*"))
                {
                    try
                    {
                        Directory.Delete(backupDir, true);
                        result.StagingCleaned++;
                        TM.App.Log($"[Reconciler] 清理孤立备份目录: {Path.GetFileName(backupDir)}");
                    }
                    catch { }
                }
            }
        }

        private async Task ValidateGuidesIntegrityAsync(ReconcileResult result)
        {
            var guidesPath = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides");
            if (!Directory.Exists(guidesPath)) return;

            var guideFiles = Directory.GetFiles(guidesPath, "*.json", SearchOption.AllDirectories);
            foreach (var file in guideFiles)
            {
                var rel = Path.GetRelativePath(guidesPath, file);
                if (rel.StartsWith($"changes_wal{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var json = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                }
                catch (Exception ex)
                {
                    var fileName = Path.GetFileName(file);
                    result.CorruptedGuides.Add(fileName);
                    TM.App.Log($"[Reconciler] 发现损坏的guide: {fileName}: {ex.Message}");

                    var bakFile = file + ".bak";
                    if (File.Exists(bakFile))
                    {
                        try
                        {
                            var bakJson = await File.ReadAllTextAsync(bakFile).ConfigureAwait(false);
                            using var bakDoc = JsonDocument.Parse(bakJson);
                            await Task.Run(async () =>
                            {
                                await using var s = File.OpenRead(bakFile);
                                await using var d = File.Create(file);
                                await s.CopyToAsync(d).ConfigureAwait(false);
                            }).ConfigureAwait(false);
                            result.CorruptedGuides.Remove(fileName);
                            TM.App.Log($"[Reconciler] 已从 bak恢复损坏的guide: {fileName}");
                        }
                        catch
                        {
                            TM.App.Log($"[Reconciler] bak文件也已损坏，无法恢复: {fileName}");
                        }
                    }

                    if (result.CorruptedGuides.Contains(fileName) &&
                        fileName.StartsWith("content_guide", StringComparison.OrdinalIgnoreCase))
                    {
                        var msg = $"content_guide 分片 [{fileName}] 已损坏且无法自动恢复，请重新执行【全量打包】以重建指导文件。";
                        if (!result.Errors.Contains(msg))
                            result.Errors.Add(msg);
                        TM.App.Log($"[Reconciler] [!] {msg}");
                    }
                }
            }

            var milestoneFiles = Directory.GetFiles(guidesPath, "*.txt", SearchOption.AllDirectories);
            foreach (var file in milestoneFiles)
            {
                try
                {
                    await File.ReadAllTextAsync(file).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    var fileName = Path.GetFileName(file);
                    result.CorruptedGuides.Add(fileName);
                    TM.App.Log($"[Reconciler] 发现损坏的里程碑文件: {fileName}: {ex.Message}");
                }
            }
        }

        private async Task ReconcileSummariesAsync(ReconcileResult result)
        {
            var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
            if (!Directory.Exists(chaptersPath)) return;

            var mdFiles = Directory.GetFiles(chaptersPath, "*.md", SearchOption.TopDirectoryOnly);
            if (mdFiles.Length == 0) return;

            Dictionary<string, string> existingSummaries;
            try
            {
                existingSummaries = await _summaryStore.GetAllSummariesAsync().ConfigureAwait(false);
            }
            catch
            {
                existingSummaries = new Dictionary<string, string>();
            }

            var mdChapterIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in mdFiles) mdChapterIds.Add(Path.GetFileNameWithoutExtension(f));

            var orphanAffectedVolumes = new HashSet<int>();
            foreach (var (orphanId, _) in existingSummaries)
            {
                if (!mdChapterIds.Contains(orphanId))
                {
                    try
                    {
                        await _summaryStore.RemoveSummaryAsync(orphanId).ConfigureAwait(false);
                        TM.App.Log($"[Reconciler] 删除孤立摘要: {orphanId}（MD不存在）");
                        result.SummariesRepaired++;
                        var p = ChapterParserHelper.ParseChapterId(orphanId);
                        if (p.HasValue) orphanAffectedVolumes.Add(p.Value.volumeNumber);
                    }
                    catch (Exception ex) { TM.App.Log($"[Reconciler] 删除孤立摘要失败 {orphanId}: {ex.Message}"); }
                }
            }

            foreach (var vol in orphanAffectedVolumes)
            {
                try
                {
                    var volSummaries = await _summaryStore.GetVolumeSummariesAsync(vol).ConfigureAwait(false);
                    await _milestoneStore.RebuildVolumeMilestoneAsync(vol, volSummaries).ConfigureAwait(false);
                    TM.App.Log($"[Reconciler] 已重建第{vol}卷里程碑（因清理{orphanAffectedVolumes.Count}条孤立摘要）");
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"孤立清理后重建里程碑失败 vol{vol}: {ex.Message}");
                }
            }

            var repairedVolumes = new HashSet<int>();
            foreach (var mdFile in mdFiles)
            {
                var chapterId = Path.GetFileNameWithoutExtension(mdFile);
                if (existingSummaries.ContainsKey(chapterId))
                    continue;

                try
                {
                    var content = await ReadHeadAsync(mdFile, 2000).ConfigureAwait(false);
                    var summary = ExtractSummaryFromHead(content);
                    if (!string.IsNullOrWhiteSpace(summary))
                    {
                        await _summaryStore.SetSummaryAsync(chapterId, summary).ConfigureAwait(false);
                        result.SummariesRepaired++;
                        TM.App.Log($"[Reconciler] 补建摘要: {chapterId}");
                        var vol = ChapterParserHelper.ParseChapterId(chapterId)?.volumeNumber ?? 1;
                        repairedVolumes.Add(vol);
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"补建摘要失败 {chapterId}: {ex.Message}");
                }
            }

            foreach (var vol in repairedVolumes)
            {
                try
                {
                    var volSummaries = await _summaryStore.GetVolumeSummariesAsync(vol).ConfigureAwait(false);
                    await _milestoneStore.RebuildVolumeMilestoneAsync(vol, volSummaries).ConfigureAwait(false);
                    TM.App.Log($"[Reconciler] 已重建第{vol}卷里程碑（因补建{volSummaries.Count}条摘要）");
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"重建里程碑失败 vol{vol}: {ex.Message}");
                }
            }
        }

    }
}

