using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Helpers;
using TM.Services.Modules.ProjectData.Interfaces;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class ModuleEnabledService : IModuleEnabledService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

        private static void DebugLogOnce(string key, Exception ex)
        {
            if (!TM.App.IsDebugMode)
            {
                return;
            }

            lock (_debugLogLock)
            {
                if (_debugLoggedKeys.Count >= 500 || !_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[ModuleEnabledService] {key}: {ex.Message}");
        }

        public async Task<int> SetModuleEnabledAsync(string moduleType, string subModule, bool enabled)
        {
            int totalUpdated = 0;

            try
            {
                var functions = NavigationConfigParser.GetFunctionsBySubModule(moduleType, subModule);

                if (functions.Count == 0)
                {
                    TM.App.Log($"[ModuleEnabledService] 未找到功能: {moduleType}/{subModule}");
                    return 0;
                }

                foreach (var func in functions)
                {
                    var updatedCount = await SetFunctionDataEnabledAsync(func.StoragePath, enabled).ConfigureAwait(false);
                    totalUpdated += updatedCount;
                }

                TM.App.Log($"[ModuleEnabledService] {moduleType}/{subModule} 设置为 {(enabled ? "启用" : "禁用")}, 更新了 {totalUpdated} 条数据");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ModuleEnabledService] 设置启用状态失败: {ex.Message}");
            }

            return totalUpdated;
        }

        private async Task<int> SetFunctionDataEnabledAsync(string storagePath, bool enabled)
        {
            if (string.IsNullOrEmpty(storagePath)) return 0;

            int updatedCount = 0;
            var dirPath = ResolveStorageDirectory(storagePath);

            if (!Directory.Exists(dirPath))
            {
                return 0;
            }

            var jsonFiles = Directory.GetFiles(dirPath, "*.json")
                .Where(f => !Path.GetFileName(f).Equals("categories.json", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var jsonFile in jsonFiles)
            {
                var count = await UpdateJsonFileEnabledAsync(jsonFile, enabled).ConfigureAwait(false);
                updatedCount += count;
            }

            if (updatedCount > 0)
            {
                StoragePathHelper.NotifyModuleDataIsEnabledChanged(dirPath, enabled);
            }

            return updatedCount;
        }

        private async Task<int> UpdateJsonFileEnabledAsync(string filePath, bool enabled)
        {
            try
            {
                if (!File.Exists(filePath)) return 0;

                var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
                var jsonArray = JsonNode.Parse(json) as JsonArray;

                if (jsonArray == null || jsonArray.Count == 0) return 0;

                int updatedCount = 0;
                foreach (var item in jsonArray)
                {
                    if (item is JsonObject obj)
                    {
                        obj["IsEnabled"] = enabled;
                        updatedCount++;
                    }
                }

                var updatedJson = jsonArray.ToJsonString(JsonOptions);
                var tmpMes = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(tmpMes, updatedJson).ConfigureAwait(false);
                File.Move(tmpMes, filePath, overwrite: true);

                return updatedCount;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ModuleEnabledService] 更新文件失败 {filePath}: {ex.Message}");
                return 0;
            }
        }

        public async Task<bool> GetModuleEnabledAsync(string moduleType, string subModule)
        {
            var (enabledCount, _) = await GetModuleEnabledStatsAsync(moduleType, subModule).ConfigureAwait(false);
            return enabledCount > 0;
        }

        public async Task<(int enabledCount, int totalCount)> GetModuleEnabledStatsAsync(string moduleType, string subModule)
        {
            int totalEnabled = 0;
            int totalCount = 0;

            try
            {
                var functions = NavigationConfigParser.GetFunctionsBySubModule(moduleType, subModule);

                foreach (var func in functions)
                {
                    var (enabled, total) = await GetFunctionEnabledStatsAsync(func.StoragePath).ConfigureAwait(false);
                    totalEnabled += enabled;
                    totalCount += total;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ModuleEnabledService] 获取启用统计失败: {ex.Message}");
            }

            return (totalEnabled, totalCount);
        }

        private async Task<(int enabledCount, int totalCount)> GetFunctionEnabledStatsAsync(string storagePath)
        {
            if (string.IsNullOrEmpty(storagePath)) return (0, 0);

            int enabledCount = 0;
            int totalCount = 0;

            var dirPath = ResolveStorageDirectory(storagePath);

            if (!Directory.Exists(dirPath)) return (0, 0);

            var jsonFiles = Directory.GetFiles(dirPath, "*.json")
                .Where(f => !Path.GetFileName(f).Equals("categories.json", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var jsonFile in jsonFiles)
            {
                var (enabled, total) = await CountEnabledInFileAsync(jsonFile).ConfigureAwait(false);
                enabledCount += enabled;
                totalCount += total;
            }

            return (enabledCount, totalCount);
        }

        private async Task<(int enabledCount, int totalCount)> CountEnabledInFileAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return (0, 0);

                var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
                var jsonArray = JsonNode.Parse(json) as JsonArray;

                if (jsonArray == null) return (0, 0);

                int enabled = 0;
                int total = jsonArray.Count;

                foreach (var item in jsonArray)
                {
                    if (item is JsonObject obj && obj["IsEnabled"]?.GetValue<bool>() == true)
                    {
                        enabled++;
                    }
                }

                return (enabled, total);
            }
            catch (Exception ex)
            {
                DebugLogOnce($"CountEnabled:{filePath}", ex);
                return (0, 0);
            }
        }

        public async Task<int> SetAllSubModulesEnabledAsync(string moduleType, bool enabled)
        {
            int totalUpdated = 0;

            try
            {
                var modules = NavigationConfigParser.GetModulesByType(moduleType);
                var subModules = modules.Select(m => m.SubModule).Distinct().ToList();

                foreach (var subModule in subModules)
                {
                    var count = await SetModuleEnabledAsync(moduleType, subModule, enabled).ConfigureAwait(false);
                    totalUpdated += count;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ModuleEnabledService] 批量设置启用状态失败: {ex.Message}");
            }

            return totalUpdated;
        }

        private static string ResolveStorageDirectory(string storagePath)
        {
            if (string.IsNullOrWhiteSpace(storagePath))
            {
                return string.Empty;
            }

            var normalized = storagePath.Trim();
            normalized = normalized.Replace('\\', '/');
            normalized = normalized.TrimStart('/');

            if (normalized.StartsWith("Storage/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized["Storage/".Length..];
            }

            var storageRoot = StoragePathHelper.GetStorageRoot();
            var relative = normalized.Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(storageRoot, relative));
        }
    }
}
