using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Modules.Generate.Elements.Blueprint.Services;
using TM.Modules.Generate.Elements.Chapter.Services;
using TM.Modules.Generate.GlobalSettings.Outline.Services;
using TM.Services.Modules.ProjectData.Helpers;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Publishing;
using TM.Modules.Generate.Elements.VolumeDesign.Services;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class PublishService : IPublishService
    {
        #region 公共方法

        public async Task<PublishResult> PublishAllAsync()
        {
            if (!await _publishLock.WaitAsync(0).ConfigureAwait(false))
            {
                TM.App.Log("[PublishService] 已有打包任务进行中，本次请求被拒绝");
                return PublishResult.Failed("已有打包任务进行中", "请等待当前打包完成后再试。");
            }

            try
            {
                return await PublishAllInternalAsync().ConfigureAwait(false);
            }
            finally
            {
                _publishLock.Release();
            }
        }

        private async Task<PublishResult> PublishAllInternalAsync()
        {
            TM.App.Log("[PublishService] 开始打包所有模块");

            var backupPath = string.Empty;
            var packagedModules = new List<string>();
            var promoted = false;

            try
            {
                var endChapterBlocked = await CheckEndChapterConfigurationAsync().ConfigureAwait(false);
                if (endChapterBlocked != null)
                    return endChapterBlocked;

                var allMappings = GetPackageMappings();
                {
                    var storageRoot = StoragePathHelper.GetStorageRoot();
                    var missingFunctions = new List<string>();

                    foreach (var mapping in allMappings)
                    {
                        var sourceBasePath = Path.Combine(storageRoot, "Modules", mapping.ModuleType, mapping.SubModule);
                        foreach (var subDir in mapping.SubDirectories)
                        {
                            var subDirPath = Path.Combine(sourceBasePath, subDir);
                            var hasData = Directory.Exists(subDirPath)
                                          && Directory.GetFiles(subDirPath, "*.json").Length > 0;
                            if (!hasData)
                            {
                                var subModuleName = NavigationConfigParser.GetSubModuleDisplayName(mapping.SubModule);
                                var functionName = NavigationConfigParser.GetDisplayName(subDir);
                                missingFunctions.Add($"{subModuleName}/{functionName}");
                            }
                        }
                    }

                    if (missingFunctions.Count > 0)
                    {
                        var detail = string.Join("、", missingFunctions);
                        TM.App.Log($"[PublishService] 打包阻断：以下业务缺失构建数据: {detail}");
                        return PublishResult.Failed(
                            $"以下业务尚未构建数据，无法打包：{detail}。请先完成构建后重新打包。");
                    }
                }

                var shouldCheckVolumeCompleteness = allMappings
                    .Any(m => string.Equals(m.ModuleType, "Generate", StringComparison.OrdinalIgnoreCase));
                if (shouldCheckVolumeCompleteness)
                {
                    var volumeCompletenessBlocked = await CheckVolumeCompletenessBeforePublishAsync().ConfigureAwait(false);
                    if (volumeCompletenessBlocked != null)
                        return volumeCompletenessBlocked;
                }

                var preflightBlocked = await RunPreflightAsync().ConfigureAwait(false);
                if (preflightBlocked != null)
                    return preflightBlocked;

                PrepareStagingConfig();
                var stagingPath = GetStagingConfigPath();
                var stagingManifestPath = GetStagingManifestPath();
                EnsureDirectoriesExist();

                var pkgMappings = allMappings;
                var packageWarnings = new ConcurrentBag<string>();
                await Task.WhenAll(pkgMappings.Select(mapping => PackageModuleAsync(mapping, stagingPath, packageWarnings))).ConfigureAwait(false);
                packagedModules.AddRange(pkgMappings.Select(m => $"{m.ModuleType}/{m.SubModule}"));

                if (!packageWarnings.IsEmpty)
                {
                    var warningDetail = string.Join("\n", packageWarnings);
                    TM.App.Log($"[PublishService] 打包源数据警告：{warningDetail}");
                    GlobalToast.Warning("打包数据警告", $"以下源数据文件读取失败（已跳过）：\n{warningDetail}", 8000);
                }

                var version = await UpdateManifestAsync(stagingManifestPath, stagingPath).ConfigureAwait(false);
                TM.App.Log($"[PublishService] 已生成 staging manifest，版本: {version}");

                await GenerateGuideFilesAsync(stagingPath).ConfigureAwait(false);

                var integrityErrors = await ValidateStagingIntegrityAsync(stagingPath).ConfigureAwait(false);
                if (integrityErrors.Count > 0)
                {
                    CleanupStaging();
                    var preview = integrityErrors.Take(8).ToList();
                    var detailLines = new List<string>(preview);
                    if (integrityErrors.Count > preview.Count)
                        detailLines.Add($"...另有 {integrityErrors.Count - preview.Count} 项错误未列出");
                    var detail = string.Join("\n", detailLines);
                    TM.App.Log($"[PublishService] 序后强校验阻断 ({integrityErrors.Count} 项): {detail.Replace("\n", " | ")}");
                    return PublishResult.Failed("打包数据完整性校验未通过（staging 已丢弃，原数据未受影响）", detail);
                }

                var previousReportPath = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "package_report.json");
                var report = await new PackageReporter().RunAsync(stagingPath, version,
                    File.Exists(previousReportPath) ? previousReportPath : null).ConfigureAwait(false);
                if (report.AnomalyWarnings.Count > 0)
                {
                    var anomalyDetail = string.Join("\n", report.AnomalyWarnings.Take(5));
                    if (report.AnomalyWarnings.Count > 5)
                        anomalyDetail += $"\n...另有 {report.AnomalyWarnings.Count - 5} 项";
                    GlobalToast.Warning("打包要素波动提醒", anomalyDetail, 8000);
                }

                backupPath = await CreateBackupAsync().ConfigureAwait(false);
                TM.App.Log($"[PublishService] 已创建备份: {backupPath}");

                promoted = true;
                await PromoteStagingAsync().ConfigureAwait(false);
                TM.App.Log("[PublishService] staging 已原子转正");

                try
                {
                    _changeDetectionService.MarkAllAsPackaged();

                    _cachedManifest = null;
                    await RefreshServiceCachesAsync().ConfigureAwait(false);

                    if (!string.IsNullOrEmpty(backupPath) && Directory.Exists(backupPath))
                    {
                        try { Directory.Delete(backupPath, true); }
                        catch (Exception delEx) { TM.App.Log($"[PublishService] 清理备份目录失败（非致命）: {delEx.Message}"); }
                        var _manifestBak = backupPath + ".manifest.json";
                        if (File.Exists(_manifestBak))
                        {
                            try { File.Delete(_manifestBak); }
                            catch (Exception delEx) { TM.App.Log($"[PublishService] 清理备份 manifest 失败（非致命）: {delEx.Message}"); }
                        }
                    }

                    var completenessWarnings = await new ContextIdsCompletenessChecker(_guideContextService).RunAsync().ConfigureAwait(false);
                    NotifyCompletenessWarnings(completenessWarnings);
                }
                catch (Exception postEx)
                {
                    TM.App.Log($"[PublishService] 转正后善后失败（数据已成功转正，无需回滚）: {postEx.Message}");
                }

                TM.App.Log("[PublishService] 打包完成");
                return PublishResult.Success(version, packagedModules);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PublishService] 打包失败: {ex.Message}");

                CleanupStaging();

                if (promoted && !string.IsNullOrEmpty(backupPath))
                {
                    try
                    {
                        await RestoreBackupAsync(backupPath).ConfigureAwait(false);
                        TM.App.Log("[PublishService] 已回滚到备份");
                    }
                    catch (Exception rbEx)
                    {
                        TM.App.Log($"[PublishService] 回滚失败（非致命）: {rbEx.Message}");
                    }
                }
                else if (!string.IsNullOrEmpty(backupPath) && Directory.Exists(backupPath))
                {
                    try { Directory.Delete(backupPath, true); } catch { }
                    var _bak = backupPath + ".manifest.json";
                    if (File.Exists(_bak)) try { File.Delete(_bak); } catch { }
                }

                _cachedManifest = null;

                try { ServiceLocator.Get<GuideManager>().DiscardDirtyAndEvict(); }
                catch (Exception evictEx) { TM.App.Log($"[PublishService] 清理 GuideManager 缓存失败: {evictEx.Message}"); }

                try
                {
                    GuideContextService.RaiseCacheInvalidated();
                    await _guideContextService.InitializeCacheAsync().ConfigureAwait(false);
                }
                catch (Exception cacheEx)
                {
                    TM.App.Log($"[PublishService] 预热缓存失败（非致命）: {cacheEx.Message}");
                }

                try { await _changeDetectionService.RefreshAllAsync().ConfigureAwait(false); }
                catch (Exception ex2) { TM.App.Log($"[PublishService] 刷新变更检测失败: {ex2.Message}"); }

                return PublishResult.Failed("打包失败", ex.Message);
            }
        }

        public async Task<PublishResult> PublishModuleAsync(string moduleName)
        {
            if (!await _publishLock.WaitAsync(0).ConfigureAwait(false))
            {
                TM.App.Log($"[PublishService] 已有打包任务进行中，模块[{moduleName}]打包请求被拒绝");
                return PublishResult.Failed("已有打包任务进行中", "请等待当前打包完成后再试。");
            }

            try
            {
                return await PublishModuleInternalAsync(moduleName).ConfigureAwait(false);
            }
            finally
            {
                _publishLock.Release();
            }
        }

        private async Task<PublishResult> PublishModuleInternalAsync(string moduleName)
        {
            TM.App.Log($"[PublishService] 开始打包模块: {moduleName}");

            var packagedModules = new List<string>();
            var backupPath = string.Empty;
            var promoted = false;

            try
            {
                {
                    var mappingsForModule = GetPackageMappings().Where(m => m.ModuleType == moduleName).ToList();
                    var storageRoot = StoragePathHelper.GetStorageRoot();
                    var missingFunctions = new List<string>();

                    foreach (var mapping in mappingsForModule)
                    {
                        var sourceBasePath = Path.Combine(storageRoot, "Modules", mapping.ModuleType, mapping.SubModule);
                        foreach (var subDir in mapping.SubDirectories)
                        {
                            var subDirPath = Path.Combine(sourceBasePath, subDir);
                            var hasData = Directory.Exists(subDirPath)
                                          && Directory.GetFiles(subDirPath, "*.json").Length > 0;
                            if (!hasData)
                            {
                                var subModuleName = NavigationConfigParser.GetSubModuleDisplayName(mapping.SubModule);
                                var functionName = NavigationConfigParser.GetDisplayName(subDir);
                                missingFunctions.Add($"{subModuleName}/{functionName}");
                            }
                        }
                    }

                    if (missingFunctions.Count > 0)
                    {
                        var detail = string.Join("、", missingFunctions);
                        TM.App.Log($"[PublishService] 单模块打包阻断：以下业务缺失构建数据: {detail}");
                        return PublishResult.Failed(
                            $"以下业务尚未构建数据，无法打包：{detail}。请先完成构建后重新打包。");
                    }
                }

                if (string.Equals(moduleName, "Generate", StringComparison.OrdinalIgnoreCase))
                {
                    var endChapterBlocked = await CheckEndChapterConfigurationAsync().ConfigureAwait(false);
                    if (endChapterBlocked != null)
                        return endChapterBlocked;

                    var volumeCompletenessBlocked = await CheckVolumeCompletenessBeforePublishAsync().ConfigureAwait(false);
                    if (volumeCompletenessBlocked != null)
                        return volumeCompletenessBlocked;
                }

                var preflightBlocked = await RunPreflightAsync().ConfigureAwait(false);
                if (preflightBlocked != null)
                    return preflightBlocked;

                await PrepareStagingConfigFromExistingAsync().ConfigureAwait(false);
                var stagingPath = GetStagingConfigPath();
                var stagingManifestPath = GetStagingManifestPath();
                EnsureDirectoriesExist();

                var mappings = GetPackageMappings().Where(m => m.ModuleType == moduleName).ToList();
                var packageWarnings = new ConcurrentBag<string>();
                await Task.WhenAll(mappings.Select(mapping => PackageModuleAsync(mapping, stagingPath, packageWarnings))).ConfigureAwait(false);

                if (!packageWarnings.IsEmpty)
                {
                    var warningDetail = string.Join("\n", packageWarnings);
                    TM.App.Log($"[PublishService] 单模块打包源数据警告：{warningDetail}");
                    GlobalToast.Warning("打包数据警告", $"以下源数据文件读取失败（已跳过）：\n{warningDetail}", 8000);
                }
                foreach (var mapping in mappings)
                    packagedModules.Add($"{mapping.ModuleType}/{mapping.SubModule}");

                var version = await UpdateManifestAsync(stagingManifestPath, stagingPath).ConfigureAwait(false);

                await GenerateGuideFilesAsync(stagingPath).ConfigureAwait(false);

                var integrityErrors = await ValidateStagingIntegrityAsync(stagingPath).ConfigureAwait(false);
                if (integrityErrors.Count > 0)
                {
                    CleanupStaging();
                    var preview = integrityErrors.Take(8).ToList();
                    var detailLines = new List<string>(preview);
                    if (integrityErrors.Count > preview.Count)
                        detailLines.Add($"...另有 {integrityErrors.Count - preview.Count} 项错误未列出");
                    var detail = string.Join("\n", detailLines);
                    TM.App.Log($"[PublishService] 单模块打包序后强校验阻断 ({integrityErrors.Count} 项): {detail.Replace("\n", " | ")}");
                    return PublishResult.Failed("打包数据完整性校验未通过（staging 已丢弃，原数据未受影响）", detail);
                }

                var previousReportPath = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "package_report.json");
                var report = await new PackageReporter().RunAsync(stagingPath, version,
                    File.Exists(previousReportPath) ? previousReportPath : null).ConfigureAwait(false);
                if (report.AnomalyWarnings.Count > 0)
                {
                    var anomalyDetail = string.Join("\n", report.AnomalyWarnings.Take(5));
                    if (report.AnomalyWarnings.Count > 5)
                        anomalyDetail += $"\n...另有 {report.AnomalyWarnings.Count - 5} 项";
                    GlobalToast.Warning("打包要素波动提醒", anomalyDetail, 8000);
                }

                backupPath = await CreateBackupAsync().ConfigureAwait(false);
                TM.App.Log($"[PublishService] 单模块打包已创建备份: {backupPath}");
                promoted = true;
                await PromoteStagingAsync().ConfigureAwait(false);

                try
                {
                    foreach (var mapping in mappings)
                        _changeDetectionService.MarkAsPackaged($"{mapping.ModuleType}/{mapping.SubModule}");

                    if (!string.IsNullOrEmpty(backupPath) && Directory.Exists(backupPath))
                    {
                        try { Directory.Delete(backupPath, true); }
                        catch (Exception delEx) { TM.App.Log($"[PublishService] 单模块打包：清理备份目录失败（非致命）: {delEx.Message}"); }
                        var _manifestBak = backupPath + ".manifest.json";
                        if (File.Exists(_manifestBak))
                        {
                            try { File.Delete(_manifestBak); }
                            catch (Exception delEx) { TM.App.Log($"[PublishService] 单模块打包：清理备份 manifest 失败（非致命）: {delEx.Message}"); }
                        }
                    }

                    _cachedManifest = null;
                    try { await RefreshServiceCachesAsync().ConfigureAwait(false); }
                    catch (Exception cacheEx) { TM.App.Log($"[PublishService] 单模块打包：刷新缓存失败（非致命）: {cacheEx.Message}"); }

                    try
                    {
                        var completenessWarnings = await new ContextIdsCompletenessChecker(_guideContextService).RunAsync().ConfigureAwait(false);
                        NotifyCompletenessWarnings(completenessWarnings);
                    }
                    catch (Exception completenessEx)
                    {
                        TM.App.Log($"[PublishService] 单模块打包：蓝图完整性预检异常（非致命）: {completenessEx.Message}");
                    }
                }
                catch (Exception postEx)
                {
                    TM.App.Log($"[PublishService] 单模块打包：转正后善后失败（数据已成功转正，无需回滚）: {postEx.Message}");
                }

                return PublishResult.Success(version, packagedModules);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PublishService] 打包模块失败 [{moduleName}]: {ex.Message}");

                CleanupStaging();

                if (promoted && !string.IsNullOrEmpty(backupPath))
                {
                    try
                    {
                        await RestoreBackupAsync(backupPath).ConfigureAwait(false);
                        TM.App.Log("[PublishService] 单模块打包已回滚到备份");
                    }
                    catch (Exception rbEx)
                    {
                        TM.App.Log($"[PublishService] 单模块打包回滚失败（非致命）: {rbEx.Message}");
                    }
                }
                else if (!string.IsNullOrEmpty(backupPath) && Directory.Exists(backupPath))
                {
                    try { Directory.Delete(backupPath, true); } catch { }
                    var _bak = backupPath + ".manifest.json";
                    if (File.Exists(_bak)) try { File.Delete(_bak); } catch { }
                }

                _cachedManifest = null;
                try { ServiceLocator.Get<GuideManager>().DiscardDirtyAndEvict(); }
                catch (Exception ex2) { TM.App.Log($"[PublishService] 模块回滚时清理 GuideManager 缓存失败: {ex2.Message}"); }
                try
                {
                    GuideContextService.RaiseCacheInvalidated();
                    await _guideContextService.InitializeCacheAsync().ConfigureAwait(false);
                }
                catch (Exception cacheEx) { TM.App.Log($"[PublishService] 模块回滚后预热缓存失败: {cacheEx.Message}"); }
                try { await _changeDetectionService.RefreshAllAsync().ConfigureAwait(false); }
                catch (Exception ex2) { TM.App.Log($"[PublishService] 模块回滚后刷新变更检测失败: {ex2.Message}"); }

                return PublishResult.Failed($"打包{moduleName}失败", ex.Message);
            }
        }

        public PublishStatus GetPublishStatus()
        {
            var manifest = GetManifest();
            var changedModules = _changeDetectionService.GetChangedModules();

            return new PublishStatus
            {
                IsPublished = manifest != null,
                LastPublishTime = manifest?.PublishTime,
                CurrentVersion = manifest?.Version ?? 0,
                NeedsRepublish = changedModules.Count > 0,
                ChangedModuleCount = changedModules.Count
            };
        }

        public ManifestInfo? GetManifest() => _cachedManifest;

        public async Task<ManifestInfo?> GetManifestAsync()
        {
            if (_cachedManifest != null)
                return _cachedManifest;

            try
            {
                var manifestPath = GetManifestPath();
                if (File.Exists(manifestPath))
                {
                    var json = await File.ReadAllTextAsync(manifestPath).ConfigureAwait(false);
                    _cachedManifest = JsonSerializer.Deserialize<ManifestInfo>(json, JsonOptions);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PublishService] 读取manifest失败: {ex.Message}");
            }

            return _cachedManifest;
        }

        public bool NeedsRepublish()
        {
            return _changeDetectionService.GetChangedModules().Count > 0;
        }

        public void ClearCache()
        {
            _cachedManifest = null;
            TM.App.Log("[PublishService] 缓存已清除");
        }

        private async Task RefreshServiceCachesAsync()
        {
            try
            {
                GuideContextService.RaiseCacheInvalidated();

                _cachedManifest = null;
                EntityNameResolver.Invalidate();

                await _guideContextService.InitializeCacheAsync().ConfigureAwait(false);

                TM.App.Log("[PublishService] 已刷新并预热缓存");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PublishService] 刷新缓存失败: {ex.Message}");
            }
        }

        private static void NotifyCompletenessWarnings(CompletenessWarnings? warnings)
        {
            if (warnings == null) return;

            var parts = new List<string>();
            if (warnings.EmptyContextWarnings.Count > 0)
                parts.Add($"{warnings.EmptyContextWarnings.Count}个章节蓝图缺少角色/地点/世界观规则关联");
            if (warnings.ForeshadowingWarnings.Count > 0)
                parts.Add($"{warnings.ForeshadowingWarnings.Count}条伏笔已埋设但无揭示计划");
            if (warnings.ConflictWarnings.Count > 0)
                parts.Add($"{warnings.ConflictWarnings.Count}条冲突活跃但无后续章节追踪");

            if (parts.Count > 0)
                GlobalToast.Warning("蓝图完整性提示", string.Join("；", parts), 5000);
        }

        private async Task<PublishResult?> CheckEndChapterConfigurationAsync()
        {
            try
            {
                var volumeService = ServiceLocator.Get<VolumeDesignService>();
                await volumeService.InitializeAsync().ConfigureAwait(false);
                var enabledVolumes = volumeService.GetAllVolumeDesigns()
                    .Where(v => v.IsEnabled && v.VolumeNumber > 0)
                    .ToList();
                if (enabledVolumes.Count > 1)
                {
                    var maxVolumeNumber = enabledVolumes.Max(v => v.VolumeNumber);
                    var unconfigured = enabledVolumes
                        .Where(v => v.EndChapter <= 0 && v.VolumeNumber < maxVolumeNumber)
                        .Select(v => v.VolumeNumber).ToList();
                    if (unconfigured.Count > 0)
                    {
                        var volList = string.Join("、", unconfigured.Select(n => $"第{n}卷"));
                        TM.App.Log($"[PublishService] L-004.1 阻断：{volList}未配置EndChapter，拒绝打包");
                        return PublishResult.Failed($"{volList}未配置\"结束章节\"", "跨卷角色状态基线存档将无法触发，可能导致剧情断裂。请在分卷设计中为每卷填写结束章节号后重新打包。");
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PublishService] L-004.1 EndChapter前置检查异常（非致命，允许继续）: {ex.Message}");
            }
            return null;
        }

        private async Task<PublishResult?> CheckVolumeCompletenessBeforePublishAsync()
        {
            try
            {
                var volumeService = ServiceLocator.Get<VolumeDesignService>();
                var outlineService = ServiceLocator.Get<OutlineService>();
                var chapterService = ServiceLocator.Get<ChapterService>();
                var blueprintService = ServiceLocator.Get<BlueprintService>();
                await Task.WhenAll(
                    volumeService.InitializeAsync(),
                    outlineService.InitializeAsync(),
                    chapterService.InitializeAsync(),
                    blueprintService.InitializeAsync()).ConfigureAwait(false);

                var enabledVolumes = volumeService.GetAllVolumeDesigns()
                    .Where(v => v.IsEnabled && v.VolumeNumber > 0)
                    .OrderBy(v => v.VolumeNumber)
                    .ToList();

                if (enabledVolumes.Count == 0)
                    return null;

                var volumeNumbers = enabledVolumes
                    .Select(v => v.VolumeNumber)
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList();

                var duplicateVolumeNumbers = enabledVolumes
                    .GroupBy(v => v.VolumeNumber)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .OrderBy(n => n)
                    .ToList();
                if (duplicateVolumeNumbers.Count > 0)
                {
                    return PublishResult.Failed(
                        "分卷编号重复，无法校验与打包",
                        $"以下分卷编号重复：{string.Join("、", duplicateVolumeNumbers.Select(n => $"{n}"))}。请确保每个启用分卷的 VolumeNumber 唯一。"
                    );
                }

                var volumeMap = enabledVolumes.ToDictionary(v => v.VolumeNumber, v => v);
                var expectedVolumeNumbers = Enumerable.Range(1, volumeNumbers.Count).ToList();
                if (!volumeNumbers.SequenceEqual(expectedVolumeNumbers))
                {
                    return PublishResult.Failed(
                        "分卷编号不连续，无法按大纲校验与打包",
                        $"当前启用分卷编号：{string.Join("、", volumeNumbers.Select(n => $"{n}"))}。请确保启用分卷编号从1开始且连续。\n例如：启用第1、2、3卷；不要跳号或只启用第1、3卷。"
                    );
                }

                var validTotalChapters = outlineService.GetAllOutlines()
                    .Where(o => o.IsEnabled
                                && o.TotalChapterCount > 0)
                    .Select(o => o.TotalChapterCount)
                    .Distinct()
                    .ToList();

                if (validTotalChapters.Count == 0)
                    return PublishResult.Failed("大纲未配置总章节数，无法校验分卷完整性", "请先在大纲设计中填写总章节数（TotalChapterCount）后再打包。");
                if (validTotalChapters.Count > 1)
                    return PublishResult.Failed("大纲总章节数冲突，无法打包", $"大纲设计中存在多个不同总章节数：{string.Join("、", validTotalChapters)}");

                var totalChapters = validTotalChapters[0];
                var totalVolumes = volumeNumbers.Count;
                if (totalChapters < totalVolumes)
                    return PublishResult.Failed("大纲章节数小于总卷数，无法打包", $"总章节数({totalChapters})不能少于总卷数({totalVolumes})。");

                var volumeDivision = outlineService.GetAllOutlines()
                    .Where(o => o.IsEnabled
                                && !string.IsNullOrWhiteSpace(o.VolumeDivision))
                    .Select(o => o.VolumeDivision)
                    .FirstOrDefault();

                if (!ChapterAllocationHelper.TryParseVolumeDivision(volumeDivision, totalVolumes, totalChapters, out var ranges))
                    ranges = ChapterAllocationHelper.Allocate(totalVolumes, totalChapters);

                var chapters = chapterService.GetAllChapters()
                    .Where(c => c.IsEnabled)
                    .ToList();

                var blueprints = blueprintService.GetAllBlueprints()
                    .Where(b => b.IsEnabled)
                    .ToList();

                static string BuildVolumeCategoryName(TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign.VolumeDesignData v)
                    => v.VolumeNumber > 0 ? $"第{v.VolumeNumber}卷 {v.VolumeTitle}".Trim() : v.Name;

                bool HasRealChapterTitle(string? title)
                {
                    if (string.IsNullOrWhiteSpace(title)) return false;
                    var stripped = ChapterTitlePrefixRegex.Replace(title.Trim(), string.Empty);
                    stripped = PunctuationOnlyRegex.Replace(stripped, string.Empty);
                    return !string.IsNullOrWhiteSpace(stripped);
                }

                var detailParts = new List<string>();

                foreach (var r in ranges)
                {
                    if (!volumeMap.TryGetValue(r.VolumeNumber, out var volume))
                    {
                        detailParts.Add($"第{r.VolumeNumber}卷缺少分卷设计");
                        continue;
                    }

                    var categoryName = BuildVolumeCategoryName(volume);
                    var expectedChapterNums = Enumerable.Range(r.StartChapter, r.TargetChapterCount).ToList();

                    var chapterCandidates = chapters.Where(c =>
                        string.Equals(c.CategoryId, volume.Id, StringComparison.Ordinal)
                        || string.Equals(c.Category, categoryName, StringComparison.Ordinal)
                        || string.Equals(c.Volume, categoryName, StringComparison.Ordinal)
                        || string.Equals(c.Category, volume.Name, StringComparison.Ordinal)
                        || string.Equals(c.Volume, volume.Name, StringComparison.Ordinal)
                        || (!string.IsNullOrWhiteSpace(c.Category) && c.Category.StartsWith($"第{r.VolumeNumber}卷", StringComparison.Ordinal))
                        || (!string.IsNullOrWhiteSpace(c.Volume) && c.Volume.StartsWith($"第{r.VolumeNumber}卷", StringComparison.Ordinal)));

                    var completedChNums = chapterCandidates
                        .Where(c => expectedChapterNums.Contains(c.ChapterNumber) && !string.IsNullOrWhiteSpace(c.ChapterTheme) && HasRealChapterTitle(c.ChapterTitle))
                        .Select(c => c.ChapterNumber)
                        .Distinct()
                        .ToHashSet();

                    var missingChNums = expectedChapterNums.Where(n => !completedChNums.Contains(n)).ToList();

                    var expectedIds = expectedChapterNums.Select(n => $"vol{r.VolumeNumber}_ch{n}").ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var blueprintCandidates = blueprints.Where(b =>
                        string.Equals(b.CategoryId, volume.Id, StringComparison.Ordinal)
                        || string.Equals(b.Category, categoryName, StringComparison.Ordinal)
                        || string.Equals(b.Category, volume.Name, StringComparison.Ordinal)
                        || (!string.IsNullOrWhiteSpace(b.Category) && b.Category.StartsWith($"第{r.VolumeNumber}卷", StringComparison.Ordinal)));

                    var completedBpNums = blueprintCandidates
                        .Where(b => !string.IsNullOrWhiteSpace(b.OneLineStructure)
                                    && !string.IsNullOrWhiteSpace(b.ChapterId)
                                    && expectedIds.Contains(b.ChapterId))
                        .Select(b =>
                        {
                            var idx = b.ChapterId.LastIndexOf("_ch", StringComparison.OrdinalIgnoreCase);
                            if (idx < 0) return 0;
                            var s = b.ChapterId[(idx + 3)..];
                            return int.TryParse(s, out var n) ? n : 0;
                        })
                        .Where(n => n > 0)
                        .Distinct()
                        .ToHashSet();

                    var missingBpNums = expectedChapterNums.Where(n => !completedBpNums.Contains(n)).ToList();

                    if (missingChNums.Count == 0 && missingBpNums.Count == 0)
                        continue;

                    string PartList(List<int> nums)
                    {
                        if (nums.Count == 0) return string.Empty;
                        var preview = nums.Take(10).Select(n => $"{n}").ToList();
                        var suffix = nums.Count > preview.Count ? "..." : string.Empty;
                        return $"缺第{string.Join("、", preview)}章{suffix}";
                    }

                    var volParts = new List<string>();
                    if (missingChNums.Count > 0)
                        volParts.Add($"章节{missingChNums.Count}/{expectedChapterNums.Count} {PartList(missingChNums)}");
                    if (missingBpNums.Count > 0)
                        volParts.Add($"蓝图{missingBpNums.Count}/{expectedChapterNums.Count} {PartList(missingBpNums)}");

                    detailParts.Add($"第{r.VolumeNumber}卷：{string.Join("；", volParts)}");
                }

                if (detailParts.Count == 0)
                    return null;

                var detail = string.Join("\n", detailParts);
                TM.App.Log($"[PublishService] 打包阻断：{detail.Replace("\n", " | ")}");
                return PublishResult.Failed("分卷章节/蓝图未按大纲覆盖，无法打包", detail);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PublishService] 分卷完整性检查异常: {ex.Message}");
                return PublishResult.Failed("分卷完整性检查失败，无法打包", ex.Message);
            }
        }

        private async Task<PublishResult?> RunPreflightAsync()
        {
            try
            {
                var validator = new PreflightValidator(modulePath => _changeDetectionService.GetStatus(modulePath).IsEnabled);
                var pf = await validator.RunAsync().ConfigureAwait(false);
                if (pf.IsValid)
                {
                    if (pf.Warnings.Count > 0)
                        TM.App.Log($"[PublishService] 预检通过（带 {pf.Warnings.Count} 项警告）: {string.Join(" | ", pf.Warnings)}");
                    else
                        TM.App.Log("[PublishService] 预检通过");
                    return null;
                }

                var preview = pf.Errors.Take(8).ToList();
                var detailLines = new List<string>(preview);
                if (pf.Errors.Count > preview.Count)
                    detailLines.Add($"...另有 {pf.Errors.Count - preview.Count} 项错误未列出");
                var detail = string.Join("\n", detailLines);
                TM.App.Log($"[PublishService] 预检阻断 ({pf.Errors.Count} 项错误): {detail.Replace("\n", " | ")}");
                return PublishResult.Failed("打包预检未通过", detail);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PublishService] 预检异常（拒绝继续打包以确保安全）: {ex.Message}");
                return PublishResult.Failed("打包预检异常", ex.Message);
            }
        }

        #endregion
    }
}
