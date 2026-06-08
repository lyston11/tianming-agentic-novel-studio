using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Publishing;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class PublishService : IPublishService
    {
        #region 私有方法

        private async Task PackageModuleAsync(PackageMapping mapping, string configBasePath, ConcurrentBag<string>? warnings = null)
        {
            var storageRoot = StoragePathHelper.GetStorageRoot();
            var sourceBasePath = Path.Combine(storageRoot, "Modules", mapping.ModuleType, mapping.SubModule);
            var moduleConfigPath = Path.Combine(configBasePath, mapping.ModuleType);
            Directory.CreateDirectory(moduleConfigPath);
            var targetPath = Path.Combine(moduleConfigPath, mapping.TargetFile);

            var packageData = new Dictionary<string, object>
            {
                ["module"] = mapping.SubModule,
                ["publishTime"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                ["version"] = 1
            };

            var data = new Dictionary<string, object>();

            var subEntries = await Task.WhenAll(mapping.SubDirectories
                .Where(subDir => Directory.Exists(Path.Combine(sourceBasePath, subDir)))
                .Select(async subDir =>
                {
                    var subDirPath = Path.Combine(sourceBasePath, subDir);
                    var subData = await LoadSubDirectoryDataAsync(subDirPath, warnings).ConfigureAwait(false);
                    return (Key: subDir.ToLowerInvariant(), Value: subData);
                })).ConfigureAwait(false);
            foreach (var entry in subEntries)
                data[entry.Key] = entry.Value;

            packageData["data"] = data;

            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            var tmpPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = File.Create(tmpPath))
            {
                await JsonSerializer.SerializeAsync(stream, packageData, JsonOptions).ConfigureAwait(false);
            }
            File.Move(tmpPath, targetPath, overwrite: true);

            TM.App.Log($"[PublishService] 已打包: {mapping.ModuleType}/{mapping.SubModule} -> {mapping.TargetFile}");
        }

        private async Task<Dictionary<string, object>> LoadSubDirectoryDataAsync(string dirPath, ConcurrentBag<string>? warnings = null)
        {
            var result = new Dictionary<string, object>();

            var jsonFiles = Directory.GetFiles(dirPath, "*.json");
            var entries = await Task.WhenAll(jsonFiles.Select(async file =>
            {
                var fileName = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                try
                {
                    var json = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                    var node = JsonNode.Parse(json);

                    if (node is JsonArray arr)
                    {
                        var filtered = new JsonArray();
                        foreach (var item in arr)
                        {
                            if (item is JsonObject obj && obj["IsEnabled"] is JsonValue enabledValue)
                            {
                                try
                                {
                                    var isEnabled = enabledValue.GetValue<bool>();
                                    if (!isEnabled)
                                        continue;
                                }
                                catch (Exception ex)
                                {
                                    DebugLogOnce("LoadSubDirectoryData_IsEnabled", ex);
                                }
                            }

                            filtered.Add(item?.DeepClone());
                        }
                        return (Key: fileName, Value: (object?)filtered);
                    }
                    else
                    {
                        return (Key: fileName, Value: (object?)JsonSerializer.Deserialize<object>(json, JsonOptions)!);
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[PublishService] 读取文件失败 [{file}]: {ex.Message}");
                    warnings?.Add($"{Path.GetFileName(file)}: {ex.Message}");
                    return (Key: fileName, Value: (object?)null);
                }
            })).ConfigureAwait(false);
            foreach (var e in entries)
                if (e.Value != null) result[e.Key] = e.Value;

            return result;
        }

        private async Task<int> UpdateManifestAsync(string manifestPath, string statisticsConfigBasePath)
        {
            var manifest = await GetManifestAsync().ConfigureAwait(false) ?? new ManifestInfo();

            manifest.ProjectName = manifest.ProjectName.Length > 0 ? manifest.ProjectName : "我的小说";
            manifest.PublishTime = DateTime.Now;
            manifest.Version = manifest.Version + 1;

            manifest.Files = BuildFilesMap();

            manifest.EnabledModules = BuildEnabledModulesMap();

            manifest.Statistics = await BuildStatisticsAsync(statisticsConfigBasePath).ConfigureAwait(false);

            var tmpPath = manifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = File.Create(tmpPath))
            {
                await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions).ConfigureAwait(false);
            }
            File.Move(tmpPath, manifestPath, overwrite: true);

            return manifest.Version;
        }

        private Dictionary<string, List<string>> BuildFilesMap()
        {
            var map = new Dictionary<string, List<string>>();

            foreach (var mapping in GetPackageMappings())
            {
                if (!map.TryGetValue(mapping.ModuleType, out var files))
                {
                    files = new List<string>();
                    map[mapping.ModuleType] = files;
                }
                files.Add(mapping.TargetFile);
            }

            return map;
        }

        private Dictionary<string, Dictionary<string, bool>> BuildEnabledModulesMap()
        {
            var map = new Dictionary<string, Dictionary<string, bool>>();

            var allStatuses = _changeDetectionService.GetAllStatuses();

            foreach (var status in allStatuses)
            {
                var parts = status.ModulePath.Split('/');
                if (parts.Length == 2)
                {
                    var moduleType = parts[0];
                    var subModule = parts[1];

                    if (!map.TryGetValue(moduleType, out var subMap))
                    {
                        subMap = new Dictionary<string, bool>();
                        map[moduleType] = subMap;
                    }
                    subMap[subModule] = status.IsEnabled;
                }
            }

            return map;
        }

        private async Task<StatisticsInfo> BuildStatisticsAsync(string configBasePath)
        {
            var stats = new StatisticsInfo();

            try
            {
                stats.TotalChapters = CountChapters();
                stats.TotalWords = await CountWordsAsync().ConfigureAwait(false);
                stats.TotalCharacters = await CountCharactersAsync(configBasePath).ConfigureAwait(false);
                stats.TotalLocations = await CountLocationsAsync(configBasePath).ConfigureAwait(false);

                TM.App.Log($"[PublishService] 统计完成: {stats.TotalWords}字, {stats.TotalChapters}章节, {stats.TotalCharacters}角色, {stats.TotalLocations}地点");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PublishService] 统计失败: {ex.Message}");
            }

            return stats;
        }

        private int CountChapters()
        {
            var chaptersDir = StoragePathHelper.GetProjectChaptersPath();
            if (!Directory.Exists(chaptersDir))
                return 0;

            return Directory.GetFiles(chaptersDir, "*.md", SearchOption.TopDirectoryOnly).Length;
        }

        private async Task<long> CountWordsAsync()
        {
            var chaptersDir = StoragePathHelper.GetProjectChaptersPath();
            if (!Directory.Exists(chaptersDir))
                return 0;

            var files = Directory.GetFiles(chaptersDir, "*.md", SearchOption.TopDirectoryOnly);

            var counts = await Task.WhenAll(files.Select(async file =>
            {
                try
                {
                    var modified = File.GetLastWriteTime(file);
                    lock (_wordCountCacheLock)
                    {
                        if (_wordCountCache.TryGetValue(file, out var entry) && entry.Modified == modified)
                            return (long)entry.Words;
                    }

                    var content = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                    var cached = string.IsNullOrEmpty(content) ? 0 : CountChineseWords(content);
                    lock (_wordCountCacheLock) { _wordCountCache[file] = (cached, modified); }
                    return (long)cached;
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[PublishService] 跳过文件: {Path.GetFileName(file)} - {ex.Message}");
                    return 0L;
                }
            })).ConfigureAwait(false);

            return counts.Sum();
        }

        private int CountChineseWords(string text) => WordCountHelper.CountRaw(text);

        private async Task<int> CountCharactersAsync(string configBasePath)
        {
            var configPath = Path.Combine(configBasePath, "Design");
            var charactersFile = Path.Combine(configPath, "elements.json");

            if (!File.Exists(charactersFile))
                return 0;

            try
            {
                var json = await File.ReadAllTextAsync(charactersFile).ConfigureAwait(false);
                using var jsonDoc = JsonDocument.Parse(json);

                if (jsonDoc.RootElement.TryGetProperty("data", out var dataProp) &&
                    dataProp.TryGetProperty("characterrules", out var characterRulesProp))
                {
                    int count = 0;
                    foreach (var fileProp in characterRulesProp.EnumerateObject())
                    {
                        if (fileProp.Value.ValueKind == JsonValueKind.Array)
                        {
                            count += fileProp.Value.GetArrayLength();
                        }
                    }
                    return count;
                }
                return 0;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PublishService] 统计角色失败: {ex.Message}");
                return 0;
            }
        }

        private async Task<int> CountLocationsAsync(string configBasePath)
        {
            var configPath = Path.Combine(configBasePath, "Design");
            var elementsFile = Path.Combine(configPath, "elements.json");

            if (!File.Exists(elementsFile))
                return 0;

            try
            {
                var json = await File.ReadAllTextAsync(elementsFile).ConfigureAwait(false);
                using var jsonDoc = JsonDocument.Parse(json);

                if (jsonDoc.RootElement.TryGetProperty("data", out var dataProp) &&
                    dataProp.TryGetProperty("locationrules", out var locationRulesProp))
                {
                    int count = 0;
                    foreach (var fileProp in locationRulesProp.EnumerateObject())
                    {
                        if (fileProp.Value.ValueKind == JsonValueKind.Array)
                        {
                            count += fileProp.Value.GetArrayLength();
                        }
                    }
                    return count;
                }
                return 0;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PublishService] 统计地点失败: {ex.Message}");
                return 0;
            }
        }

        private async Task<string> CreateBackupAsync()
        {
            var configPath = StoragePathHelper.GetProjectConfigPath();
            var backupPath = Path.Combine(StoragePathHelper.GetCurrentProjectPath(), $"_backup_{DateTime.Now:yyyyMMdd_HHmmss}");

            Directory.CreateDirectory(backupPath);

            if (Directory.Exists(configPath))
            {
                await CopyDirectoryAsync(configPath, backupPath).ConfigureAwait(false);
            }

            var manifestSrc = GetManifestPath();
            if (File.Exists(manifestSrc))
            {
                var manifestBak = backupPath + ".manifest.json";
                await Task.Run(async () =>
                {
                    await using var s = File.OpenRead(manifestSrc);
                    await using var d = File.Create(manifestBak);
                    await s.CopyToAsync(d).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }

            return backupPath;
        }

        private async Task RestoreBackupAsync(string backupPath)
        {
            var configPath = StoragePathHelper.GetProjectConfigPath();

            if (Directory.Exists(configPath))
            {
                Directory.Delete(configPath, true);
            }

            await CopyDirectoryAsync(backupPath, configPath).ConfigureAwait(false);

            var manifestBak = backupPath + ".manifest.json";
            if (File.Exists(manifestBak))
            {
                var manifestDest = GetManifestPath();
                await Task.Run(async () =>
                {
                    await using var s = File.OpenRead(manifestBak);
                    await using var d = File.Create(manifestDest);
                    await s.CopyToAsync(d).ConfigureAwait(false);
                    File.Delete(manifestBak);
                }).ConfigureAwait(false);
            }

            Directory.Delete(backupPath, true);
        }

        private async Task CopyDirectoryAsync(string source, string target)
        {
            Directory.CreateDirectory(target);

            foreach (var file in Directory.GetFiles(source))
            {
                var destPath = Path.Combine(target, Path.GetFileName(file));
                await using var sourceStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await sourceStream.CopyToAsync(destStream).ConfigureAwait(false);
            }

            foreach (var dir in Directory.GetDirectories(source))
            {
                await CopyDirectoryAsync(dir, Path.Combine(target, Path.GetFileName(dir))).ConfigureAwait(false);
            }
        }

        private void EnsureDirectoriesExist()
        {
            _ = StoragePathHelper.GetProjectConfigPath("Design");
            _ = StoragePathHelper.GetProjectConfigPath("Generate");
            _ = StoragePathHelper.GetProjectChaptersPath();
            Directory.CreateDirectory(Path.Combine(StoragePathHelper.GetProjectValidationPath(), "reports"));
            _ = StoragePathHelper.GetProjectHistoryPath();
        }

        private string GetManifestPath()
        {
            return Path.Combine(StoragePathHelper.GetCurrentProjectPath(), "manifest.json");
        }

        #endregion

        #region 两阶段提交（staging → Config 原子转正）

        private static string GetStagingConfigPath()
        {
            return Path.Combine(StoragePathHelper.GetCurrentProjectPath(), "Config.staging");
        }

        private static string GetStagingManifestPath()
        {
            return Path.Combine(StoragePathHelper.GetCurrentProjectPath(), "manifest.staging.json");
        }

        private void PrepareStagingConfig()
        {
            var staging = GetStagingConfigPath();
            if (Directory.Exists(staging))
            {
                try { Directory.Delete(staging, true); }
                catch (Exception ex) { TM.App.Log($"[PublishService] 清理旧 staging 失败（继续）: {ex.Message}"); }
            }
            Directory.CreateDirectory(staging);

            var stagingManifest = GetStagingManifestPath();
            if (File.Exists(stagingManifest))
            {
                try { File.Delete(stagingManifest); } catch { }
            }

            Directory.CreateDirectory(Path.Combine(staging, "Design"));
            Directory.CreateDirectory(Path.Combine(staging, "Generate"));
        }

        private async Task PrepareStagingConfigFromExistingAsync()
        {
            var staging = GetStagingConfigPath();
            if (Directory.Exists(staging))
            {
                try { Directory.Delete(staging, true); }
                catch (Exception ex) { TM.App.Log($"[PublishService] 清理旧 staging 失败（继续）: {ex.Message}"); }
            }
            Directory.CreateDirectory(staging);

            var stagingManifest = GetStagingManifestPath();
            if (File.Exists(stagingManifest))
            {
                try { File.Delete(stagingManifest); } catch { }
            }

            var configPath = StoragePathHelper.GetProjectConfigPath();
            if (Directory.Exists(configPath))
            {
                await CopyDirectoryAsync(configPath, staging).ConfigureAwait(false);
            }
            else
            {
                Directory.CreateDirectory(Path.Combine(staging, "Design"));
                Directory.CreateDirectory(Path.Combine(staging, "Generate"));
            }
        }

        private async Task PromoteStagingAsync()
        {
            var staging = GetStagingConfigPath();
            var configPath = StoragePathHelper.GetProjectConfigPath();
            var stagingManifest = GetStagingManifestPath();
            var manifestPath = GetManifestPath();

            if (!Directory.Exists(staging))
                throw new InvalidOperationException("staging 目录不存在，无法转正");

            if (Directory.Exists(configPath))
            {
                Directory.Delete(configPath, true);
            }
            Directory.Move(staging, configPath);

            if (File.Exists(stagingManifest))
            {
                try
                {
                    File.Move(stagingManifest, manifestPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[PublishService] manifest 转正失败（Config 已成功转正，元数据滞后一拍）: {ex.Message}");
                }
            }

            await Task.CompletedTask;
        }

        private void CleanupStaging()
        {
            try
            {
                var staging = GetStagingConfigPath();
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                var stagingManifest = GetStagingManifestPath();
                if (File.Exists(stagingManifest)) File.Delete(stagingManifest);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PublishService] 清理 staging 失败（非致命）: {ex.Message}");
            }
        }

        private async Task<List<string>> ValidateStagingIntegrityAsync(string configBasePath)
        {
            var errors = new List<string>();

            try
            {
                var designGlobalPath = Path.Combine(configBasePath, "Design", "globalsettings.json");
                var designElementsPath = Path.Combine(configBasePath, "Design", "elements.json");
                var generateGlobalPath = Path.Combine(configBasePath, "Generate", "globalsettings.json");
                var generateElementsPath = Path.Combine(configBasePath, "Generate", "elements.json");

                if (!File.Exists(designGlobalPath)) errors.Add("Design/globalsettings.json 缺失");
                if (!File.Exists(designElementsPath)) errors.Add("Design/elements.json 缺失");
                if (!File.Exists(generateGlobalPath)) errors.Add("Generate/globalsettings.json 缺失");
                if (!File.Exists(generateElementsPath)) errors.Add("Generate/elements.json 缺失");

                if (errors.Count > 0) return errors;

                var allCharIds = await CollectIdsFromElementsAsync(designElementsPath, "characterrules").ConfigureAwait(false);
                var allLocIds = await CollectIdsFromElementsAsync(designElementsPath, "locationrules").ConfigureAwait(false);
                var allFacIds = await CollectIdsFromElementsAsync(designElementsPath, "factionrules").ConfigureAwait(false);
                var allPlotIds = await CollectIdsFromElementsAsync(designElementsPath, "plotrules").ConfigureAwait(false);

                var guidesDir = Path.Combine(configBasePath, "guides");
                if (!Directory.Exists(guidesDir))
                {
                    errors.Add("guides/ 目录缺失");
                    return errors;
                }

                var shardFiles = Directory.GetFiles(guidesDir, "content_guide_vol*.json");
                if (shardFiles.Length == 0)
                {
                    errors.Add("content_guide 分片文件未生成（应至少 1 个 content_guide_volN.json）");
                    return errors;
                }

                foreach (var shardFile in shardFiles)
                {
                    try
                    {
                        await using var stream = File.OpenRead(shardFile);
                        using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
                        if (!doc.RootElement.TryGetProperty("Chapters", out var chaptersProp) ||
                            chaptersProp.ValueKind != JsonValueKind.Object)
                            continue;

                        foreach (var chapter in chaptersProp.EnumerateObject())
                        {
                            var chapterId = chapter.Name;
                            if (!chapter.Value.TryGetProperty("ContextIds", out var ctx)) continue;

                            CollectInvalidIds(ctx, "Characters", allCharIds, chapterId, "角色", errors);
                            CollectInvalidIds(ctx, "Locations", allLocIds, chapterId, "地点", errors);
                            CollectInvalidIds(ctx, "Factions", allFacIds, chapterId, "势力", errors);
                            CollectInvalidIds(ctx, "PlotRules", allPlotIds, chapterId, "剧情规则", errors);
                            CollectInvalidIds(ctx, "Conflicts", allPlotIds, chapterId, "冲突", errors);
                            CollectInvalidIds(ctx, "ForeshadowingSetups", allPlotIds, chapterId, "伏笔埋设", errors);
                            CollectInvalidIds(ctx, "ForeshadowingPayoffs", allPlotIds, chapterId, "伏笔回收", errors);
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"读取分片失败 {Path.GetFileName(shardFile)}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"序后强校验异常: {ex.Message}");
            }

            return errors;
        }

        private static async Task<HashSet<string>> CollectIdsFromElementsAsync(string elementsFilePath, string rootKey)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                await using var stream = File.OpenRead(elementsFilePath);
                using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
                if (!doc.RootElement.TryGetProperty("data", out var dataProp))
                    return ids;

                JsonElement keyProp = default;
                bool found = false;
                foreach (var p in dataProp.EnumerateObject())
                {
                    if (string.Equals(p.Name, rootKey, StringComparison.OrdinalIgnoreCase))
                    {
                        keyProp = p.Value;
                        found = true;
                        break;
                    }
                }
                if (!found) return ids;

                if (keyProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var fileEntry in keyProp.EnumerateObject())
                    {
                        if (fileEntry.Value.ValueKind != JsonValueKind.Array) continue;
                        foreach (var item in fileEntry.Value.EnumerateArray())
                        {
                            if (item.ValueKind != JsonValueKind.Object) continue;
                            if (item.TryGetProperty("Id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                            {
                                var id = idProp.GetString();
                                if (!string.IsNullOrWhiteSpace(id)) ids.Add(id!);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PublishService] 收集 {rootKey} ID 失败: {ex.Message}");
            }
            return ids;
        }

        private static void CollectInvalidIds(JsonElement ctx, string field, HashSet<string> validIds,
            string chapterId, string label, List<string> errors)
        {
            if (!ctx.TryGetProperty(field, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
            var invalid = new List<string>();
            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) continue;
                var id = item.GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (!validIds.Contains(id!)) invalid.Add(id!);
            }
            if (invalid.Count > 0)
            {
                errors.Add($"章节 {chapterId} 的{label}列表包含无效ID: {string.Join("、", invalid.Take(5))}{(invalid.Count > 5 ? "..." : string.Empty)}");
            }
        }

        #endregion
    }
}
