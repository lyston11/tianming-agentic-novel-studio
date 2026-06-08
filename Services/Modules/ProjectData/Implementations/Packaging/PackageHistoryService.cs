using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Framework.UI.Workspace.Services;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Publishing;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class PackageHistoryService : IPackageHistoryService
    {
        private readonly IPublishService _publishService;
        private int _retainCount = 5;

        private string PublishedPath => StoragePathHelper.GetProjectConfigPath();

        private string HistoryPath => StoragePathHelper.GetProjectHistoryPath();

        public PackageHistoryService(IPublishService publishService)
        {
            _publishService = publishService;
        }

        public int RetainCount
        {
            get => _retainCount;
            set => _retainCount = Math.Clamp(value, 1, 10);
        }

        public async Task<bool> SaveCurrentToHistoryAsync()
        {
            return await Task.Run(async () =>
            {
                try
                {
                    var manifest = _publishService.GetManifest();
                    if (manifest == null)
                    {
                        TM.App.Log("[PackageHistoryService] 无当前版本可保存");
                        return false;
                    }

                    var versionDir = Path.Combine(HistoryPath, $"v{manifest.Version}");
                    var tmpDir = versionDir + "_tmp";
                    if (Directory.Exists(tmpDir))
                        Directory.Delete(tmpDir, true);
                    Directory.CreateDirectory(tmpDir);

                    try
                    {
                        var manifestPath = Path.Combine(StoragePathHelper.GetCurrentProjectPath(), "manifest.json");
                        if (File.Exists(manifestPath))
                        {
                            await using var s = File.OpenRead(manifestPath);
                            await using var d = File.Create(Path.Combine(tmpDir, "manifest.json"));
                            await s.CopyToAsync(d).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[PackageHistoryService] 复制 manifest.json 失败: {ex.Message}");
                    }

                    var sourceFiles = Directory.GetFiles(PublishedPath, "*.json", SearchOption.TopDirectoryOnly);
                    foreach (var file in sourceFiles)
                    {
                        var fileName = Path.GetFileName(file);
                        await using var s = File.OpenRead(file);
                        await using var d = File.Create(Path.Combine(tmpDir, fileName));
                        await s.CopyToAsync(d).ConfigureAwait(false);
                    }

                    var subDirs = new[] { "Design", "Generate", "guides" };
                    foreach (var subDir in subDirs)
                    {
                        var sourceDir = Path.Combine(PublishedPath, subDir);
                        if (Directory.Exists(sourceDir))
                            await CopyDirectoryAsync(sourceDir, Path.Combine(tmpDir, subDir)).ConfigureAwait(false);
                    }

                    if (Directory.Exists(versionDir))
                        Directory.Delete(versionDir, true);
                    Directory.Move(tmpDir, versionDir);

                    TM.App.Log($"[PackageHistoryService] 版本 {manifest.Version} 已保存到历史");

                    CleanupOldHistory();

                    return true;
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[PackageHistoryService] 保存历史失败: {ex.Message}");
                    return false;
                }
            }).ConfigureAwait(false);
        }

        public async Task<List<PackageHistoryEntry>> GetAllHistoryAsync()
        {
            var entries = new List<PackageHistoryEntry>();
            try
            {
                var currentManifest = await _publishService.GetManifestAsync().ConfigureAwait(false);
                if (currentManifest != null)
                    entries.Add(new PackageHistoryEntry
                    {
                        Version = currentManifest.Version,
                        PublishTime = currentManifest.PublishTime,
                        EnabledSummary = BuildEnabledSummary(currentManifest.EnabledModules),
                        EnabledModules = currentManifest.EnabledModules,
                        IsCurrent = true,
                        HistoryPath = PublishedPath
                    });

                if (Directory.Exists(HistoryPath))
                {
                    var versionDirs = Directory.GetDirectories(HistoryPath, "v*");
                    var currentVersion = currentManifest?.Version;

                    var tasks = versionDirs.Select(async versionDir =>
                    {
                        var manifestFile = Path.Combine(versionDir, "manifest.json");
                        if (!File.Exists(manifestFile)) return null;
                        try
                        {
                            var json = await File.ReadAllTextAsync(manifestFile).ConfigureAwait(false);
                            var manifest = JsonSerializer.Deserialize<ManifestInfo>(json);
                            if (manifest != null && manifest.Version != currentVersion)
                                return new PackageHistoryEntry
                                {
                                    Version = manifest.Version,
                                    PublishTime = manifest.PublishTime,
                                    EnabledSummary = BuildEnabledSummary(manifest.EnabledModules),
                                    EnabledModules = manifest.EnabledModules,
                                    IsCurrent = false,
                                    HistoryPath = versionDir
                                };
                        }
                        catch (Exception ex) { TM.App.Log($"[PackageHistoryService] 读取历史版本失败: {ex.Message}"); }
                        return null;
                    });

                    var results = await Task.WhenAll(tasks).ConfigureAwait(false);
                    entries.AddRange(results.Where(e => e != null).OrderByDescending(e => e!.PublishTime)!);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PackageHistoryService] 异步获取历史失败: {ex.Message}");
            }
            return entries;
        }

        public async Task<bool> RestoreVersionAsync(int version)
        {
            try
            {
                var versionDir = Path.Combine(HistoryPath, $"v{version}");
                if (!Directory.Exists(versionDir))
                {
                    TM.App.Log($"[PackageHistoryService] 版本 {version} 不存在");
                    return false;
                }

                var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
                var hasWrittenChapters = Directory.Exists(chaptersPath)
                    && Directory.GetFiles(chaptersPath, "*.md", SearchOption.TopDirectoryOnly).Length > 0;
                if (hasWrittenChapters)
                {
                    var proceed = StandardDialog.ShowConfirm(
                        $"检测到当前项目已有章节正文。\n\n"
                        + $"恢复到 v{version} 仅回滚 Config/guides，不会回滚 Generated/chapters/*.md。\n"
                        + "继续恢复可能出现“配置版本已回退、正文仍是新版本”的混合态。\n\n"
                        + "是否继续恢复？",
                        "恢复版本确认");

                    if (!proceed)
                    {
                        TM.App.Log($"[PackageHistoryService] 用户取消恢复版本 v{version}（存在已写正文）");
                        return false;
                    }
                }

                await SaveCurrentToHistoryAsync().ConfigureAwait(false);

                var _publishedPath = PublishedPath;
                var _projectPath = StoragePathHelper.GetCurrentProjectPath();
                await System.Threading.Tasks.Task.Run(async () =>
                {
                    var restoreProtected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "current_chapter.json" };
                    var sourceFiles = Directory.GetFiles(versionDir, "*.json", SearchOption.TopDirectoryOnly);
                    foreach (var file in sourceFiles)
                    {
                        var fileName = Path.GetFileName(file);
                        if (restoreProtected.Contains(fileName)) continue;
                        var destFile = Path.Combine(_publishedPath, fileName);
                        await using var s = File.OpenRead(file);
                        await using var d = File.Create(destFile);
                        await s.CopyToAsync(d).ConfigureAwait(false);
                    }
                    var historyManifest = Path.Combine(versionDir, "manifest.json");
                    if (File.Exists(historyManifest))
                    {
                        var destManifest = Path.Combine(_projectPath, "manifest.json");
                        await using var s = File.OpenRead(historyManifest);
                        await using var d = File.Create(destManifest);
                        await s.CopyToAsync(d).ConfigureAwait(false);
                    }

                    var subDirs = new[] { "Design", "Generate", "guides" };
                    foreach (var subDir in subDirs)
                    {
                        var sourceDir = Path.Combine(versionDir, subDir);
                        if (!Directory.Exists(sourceDir)) continue;

                        var destDir = Path.Combine(_publishedPath, subDir);
                        if (Directory.Exists(destDir))
                            Directory.Delete(destDir, true);

                        await CopyDirectoryAsync(sourceDir, destDir).ConfigureAwait(false);
                    }
                }).ConfigureAwait(true);
                try { _publishService.ClearCache(); EntityNameResolver.Invalidate(); }
                catch (Exception ex) { TM.App.Log($"[PackageHistoryService] ClearCache失败: {ex.Message}"); }

                try { ServiceLocator.Get<GuideManager>().ClearCache(); }
                catch (Exception ex) { TM.App.Log($"[PackageHistoryService] GuideManager.ClearCache失败: {ex.Message}"); }

                try { ServiceLocator.Get<ChapterSummaryStore>().InvalidateCache(); }
                catch (Exception ex) { TM.App.Log($"[PackageHistoryService] SummaryStore失效失败: {ex.Message}"); }

                try { ServiceLocator.Get<ChapterMilestoneStore>().InvalidateCache(); }
                catch (Exception ex) { TM.App.Log($"[PackageHistoryService] MilestoneStore失效失败: {ex.Message}"); }

                try { ServiceLocator.Get<VolumeFactArchiveStore>().InvalidateCache(); }
                catch (Exception ex) { TM.App.Log($"[PackageHistoryService] FactArchiveStore失效失败: {ex.Message}"); }

                try { ServiceLocator.Get<GeneratedContentService>().InvalidateStaticCaches(); }
                catch (Exception ex) { TM.App.Log($"[PackageHistoryService] GeneratedContentService缓存失效失败: {ex.Message}"); }

                try { ServiceLocator.Get<KeywordChapterIndexService>().InvalidateCache(); }
                catch (Exception ex) { TM.App.Log($"[PackageHistoryService] KeywordIndex缓存失效失败: {ex.Message}"); }

                try { ServiceLocator.Get<PlotPointsIndexService>().InvalidateCache(); }
                catch (Exception ex) { TM.App.Log($"[PackageHistoryService] PlotPointsIndex缓存失效失败: {ex.Message}"); }

                try { ServiceLocator.Get<SessionContextCache>().Clear(); }
                catch (Exception ex) { TM.App.Log($"[PackageHistoryService] SessionContextCache清空失败: {ex.Message}"); }

                try
                {
                    GuideContextService.RaiseCacheInvalidated();
                    await ServiceLocator.Get<GuideContextService>().InitializeCacheAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[PackageHistoryService] 恢复后预热缓存失败: {ex.Message}");
                }

                try { await ServiceLocator.Get<IChangeDetectionService>().RefreshAllAsync().ConfigureAwait(false); }
                catch (Exception ex) { TM.App.Log($"[PackageHistoryService] 恢复后刷新变更检测失败: {ex.Message}"); }

                TM.App.Log($"[PackageHistoryService] 已恢复到版本 {version}");

                GlobalToast.Warning("版本恢复说明",
                    $"已恢复到 v{version} 配置，但已写章节正文（MD文件）不会回滚。若发现追踪数据异常，请重启应用让系统自动修复。");
                _ = Task.Run(async () =>
                {
                    try { await ServiceLocator.Get<ConsistencyReconciler>().ReconcileAsync().ConfigureAwait(false); }
                    catch (Exception ex) { TM.App.Log($"[PackageHistoryService] 版本恢复后对账失败: {ex.Message}"); }
                });

                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PackageHistoryService] 恢复版本失败: {ex.Message}");
                return false;
            }
        }

        public void CleanupOldHistory()
        {
            try
            {
                if (!Directory.Exists(HistoryPath))
                    return;

                var versionDirs = Directory.GetDirectories(HistoryPath, "v*")
                    .OrderByDescending(d =>
                    {
                        var name = Path.GetFileName(d);
                        return int.TryParse(name.TrimStart('v'), out var v) ? v : 0;
                    })
                    .ToList();

                for (int i = _retainCount; i < versionDirs.Count; i++)
                {
                    Directory.Delete(versionDirs[i], true);
                    TM.App.Log($"[PackageHistoryService] 清理历史版本: {Path.GetFileName(versionDirs[i])}");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PackageHistoryService] 清理历史失败: {ex.Message}");
            }
        }

        public async System.Threading.Tasks.Task<PackageVersionDiff> GetVersionDiffAsync(int historyVersion)
        {
            var diff = new PackageVersionDiff { HistoryVersion = historyVersion };

            try
            {
                var currentManifest = _publishService.GetManifest();
                if (currentManifest == null)
                    return diff;

                diff.CurrentVersion = currentManifest.Version;

                var historyManifestPath = Path.Combine(HistoryPath, $"v{historyVersion}", "manifest.json");
                if (!File.Exists(historyManifestPath))
                    return diff;

                var json = await File.ReadAllTextAsync(historyManifestPath).ConfigureAwait(false);
                var historyManifest = JsonSerializer.Deserialize<ManifestInfo>(json);
                if (historyManifest == null)
                    return diff;

                foreach (var moduleType in currentManifest.EnabledModules.Keys)
                {
                    if (!currentManifest.EnabledModules.TryGetValue(moduleType, out var currentModules))
                        continue;

                    historyManifest.EnabledModules.TryGetValue(moduleType, out var historyModules);
                    historyModules ??= new Dictionary<string, bool>();

                    foreach (var (subModule, currentEnabled) in currentModules)
                    {
                        var historyEnabled = historyModules.GetValueOrDefault(subModule, true);
                        if (currentEnabled != historyEnabled)
                        {
                            diff.DiffItems.Add(new ModuleDiffItem
                            {
                                ModulePath = $"{moduleType}/{subModule}",
                                DisplayName = subModule,
                                Type = DiffType.EnabledChanged,
                                CurrentState = currentEnabled ? "启用" : "禁用",
                                HistoryState = historyEnabled ? "启用" : "禁用"
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PackageHistoryService] 异步获取版本差异失败: {ex.Message}");
            }

            return diff;
        }

        private string BuildEnabledSummary(Dictionary<string, Dictionary<string, bool>> enabledModules)
        {
            var parts = new List<string>();
            foreach (var (moduleType, modules) in enabledModules)
            {
                var enabled = modules.Count(m => m.Value);
                var total = modules.Count;
                parts.Add($"{moduleType}({enabled}/{total})");
            }
            return string.Join(" + ", parts);
        }

        public async Task<bool> ClearAllAsync()
        {
            try
            {
                await Task.Run(() =>
                {
                    var manifestPath = Path.Combine(StoragePathHelper.GetCurrentProjectPath(), "manifest.json");
                    if (File.Exists(manifestPath))
                    {
                        File.Delete(manifestPath);
                        TM.App.Log("[PackageHistoryService] 已删除 manifest.json");
                    }

                    if (Directory.Exists(PublishedPath))
                    {
                        var files = Directory.GetFiles(PublishedPath, "*.json", SearchOption.TopDirectoryOnly);
                        var protectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "project_spec.json" };
                        foreach (var file in files)
                        {
                            var fileName = Path.GetFileName(file);
                            if (!protectedFiles.Contains(fileName))
                                File.Delete(file);
                        }

                        var subDirs = new[] { "Design", "Generate", "guides" };
                        foreach (var subDir in subDirs)
                        {
                            var fullPath = Path.Combine(PublishedPath, subDir);
                            if (Directory.Exists(fullPath))
                                Directory.Delete(fullPath, true);
                        }
                        TM.App.Log("[PackageHistoryService] 已清除当前打包文件");
                    }

                    if (Directory.Exists(HistoryPath))
                    {
                        Directory.Delete(HistoryPath, true);
                        TM.App.Log("[PackageHistoryService] 已清除所有历史记录");
                    }

                    var generatedPath = Path.Combine(StoragePathHelper.GetCurrentProjectPath(), "Generated");
                    if (Directory.Exists(generatedPath))
                    {
                        Directory.Delete(generatedPath, true);
                        TM.App.Log("[PackageHistoryService] 已清除已生成章节");
                    }

                }).ConfigureAwait(false);

                try { _publishService.ClearCache(); } catch { }
                try { ServiceLocator.Get<GuideManager>().ClearCache(); } catch { }
                try { ServiceLocator.Get<ChapterSummaryStore>().InvalidateCache(); } catch { }
                try { ServiceLocator.Get<ChapterMilestoneStore>().InvalidateCache(); } catch { }
                try { ServiceLocator.Get<VolumeFactArchiveStore>().InvalidateCache(); } catch { }
                try { ServiceLocator.Get<KeywordChapterIndexService>().InvalidateCache(); } catch { }
                try { ServiceLocator.Get<PlotPointsIndexService>().InvalidateCache(); } catch { }
                try { ServiceLocator.Get<Indexing.ChapterEmbeddingIndex>().InvalidateCache(); } catch { }
                try { ServiceLocator.Get<Indexing.ChunkEmbeddingIndex>().InvalidateCache(); } catch { }
                try { ServiceLocator.Get<Indexing.EntityFirstChapterIndex>().InvalidateCache(); } catch { }
                try { ServiceLocator.Get<GeneratedContentService>().InvalidateStaticCaches(); } catch { }
                try { ServiceLocator.Get<SessionContextCache>().Clear(); } catch { }
                try { GuideContextService.RaiseCacheInvalidated(); } catch { }
                try { EntityNameResolver.Invalidate(); } catch { }
                try { CurrentChapterTracker.Clear(); } catch { }
                try { ServiceLocator.Get<ContentGenerationCallback>().ClearNameMapCache(); } catch { }

                try { await ServiceLocator.Get<IChangeDetectionService>().RefreshAllAsync().ConfigureAwait(false); } catch { }

                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PackageHistoryService] 清除打包失败: {ex.Message}");
                return false;
            }
        }

        private async Task CopyDirectoryAsync(string sourceDir, string destDir)
        {
            if (!Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                await using var s = File.OpenRead(file);
                await using var d = File.Create(destFile);
                await s.CopyToAsync(d).ConfigureAwait(false);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                await CopyDirectoryAsync(dir, destSubDir).ConfigureAwait(false);
            }
        }
    }
}
