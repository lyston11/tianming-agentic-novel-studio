using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Helpers;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.ChangeDetection;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class ChangeDetectionService : IChangeDetectionService
    {
        private readonly Dictionary<string, ChangeStatus> _statusCache = new();
        private readonly object _cacheLock = new();
        private DateTime? _lastPackageTime;

        private static bool IsUiThread()
        {
            var ctx = SynchronizationContext.Current;
            if (ctx == null) return false;
            return ctx.GetType().Name.Contains("DispatcherSynchronizationContext", StringComparison.OrdinalIgnoreCase);
        }

        private static readonly object _debugLogLock = new();
        private static readonly System.Collections.Generic.HashSet<string> _debugLoggedKeys = new();

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

            System.Diagnostics.Debug.WriteLine($"[ChangeDetectionService] {key}: {ex.Message}");
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public ChangeDetectionService()
        {
            LoadManifestInfoAsync().SafeFireAndForget(ex => TM.App.Log($"[ChangeDetection] 初始化失败: {ex.Message}"));

            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) =>
                {
                    lock (_cacheLock) { _statusCache.Clear(); }
                    _lastPackageTime = null;
                    LoadManifestInfoAsync().SafeFireAndForget(ex => TM.App.Log($"[ChangeDetection] 项目切换重载失败: {ex.Message}"));
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChangeDetectionService] 订阅项目切换事件失败: {ex.Message}");
            }
        }

        private static Dictionary<string, HashSet<string>> PackageSubModuleAllowlist => PackagingAllowlist.SubModules;

        private List<ModuleDefinition> GetAllModules()
        {
            var modules = new List<ModuleDefinition>();

            foreach (var pair in PackageSubModuleAllowlist)
            {
                var moduleType = pair.Key;
                var allowlist = pair.Value;
                var subModules = NavigationConfigParser.GetSubModules(moduleType);

                foreach (var (subModule, displayName) in subModules)
                {
                    if (!allowlist.Contains(displayName))
                        continue;

                    modules.Add(new ModuleDefinition(
                        $"{moduleType}/{subModule}",
                        displayName,
                        $"Modules/{moduleType}/{subModule}"
                    ));
                }
            }

            return modules;
        }

        #region 公共方法

        public bool HasChanges(string modulePath)
        {
            var status = GetStatus(modulePath);
            return status.Status == ChangeStatusType.Changed || status.Status == ChangeStatusType.Never;
        }

        public List<string> GetChangedModules()
        {
            return GetAllStatuses()
                .Where(s => s.Status == ChangeStatusType.Changed || s.Status == ChangeStatusType.Never)
                .Select(s => s.ModulePath)
                .ToList();
        }

        private static readonly SemaphoreSlim _refreshThrottle = new(4, 4);

        public async Task RefreshAllAsync()
        {
            TM.App.Log("[ChangeDetectionService] 刷新所有模块变更状态");

            lock (_cacheLock) { _statusCache.Clear(); }
            await LoadManifestInfoAsync().ConfigureAwait(false);

            var modules = GetAllModules();
            var tasks = modules.Select(async module =>
            {
                await _refreshThrottle.WaitAsync().ConfigureAwait(false);
                try { await RefreshModuleStatusAsync(module).ConfigureAwait(false); }
                finally { _refreshThrottle.Release(); }
            }).ToArray();
            await Task.WhenAll(tasks).ConfigureAwait(false);

            int count;
            lock (_cacheLock) { count = _statusCache.Count; }
            TM.App.Log($"[ChangeDetectionService] 刷新完成，共{count}个模块");
        }

        public ChangeStatus GetStatus(string modulePath)
        {
            lock (_cacheLock)
            {
                if (_statusCache.TryGetValue(modulePath, out var cached))
                    return cached;
            }

            if (IsUiThread())
            {
                lock (_cacheLock)
                {
                    _statusCache[modulePath] = new ChangeStatus
                    {
                        ModulePath = modulePath,
                        Status = ChangeStatusType.Changed,
                        IsEnabled = true
                    };
                }

                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var m = GetAllModules().FirstOrDefault(x => x.Path == modulePath);
                        if (m != null) await RefreshModuleStatusAsync(m).ConfigureAwait(false);
                    }
                    catch (Exception ex) { TM.App.Log($"[ChangeDetection] 后台刷新模块状态失败 {modulePath}: {ex.Message}"); }
                });
                return new ChangeStatus { ModulePath = modulePath, Status = ChangeStatusType.Changed, IsEnabled = true };
            }

            var module = GetAllModules().FirstOrDefault(m => m.Path == modulePath);
            if (module != null)
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try { await RefreshModuleStatusAsync(module).ConfigureAwait(false); }
                    catch (Exception ex) { TM.App.Log($"[ChangeDetection] 后台刷新模块状态失败 {module.Path}: {ex.Message}"); }
                });

            lock (_cacheLock)
            {
                return _statusCache.TryGetValue(modulePath, out var status)
                    ? status
                    : new ChangeStatus { ModulePath = modulePath, Status = ChangeStatusType.Changed, IsEnabled = true };
            }
        }

        public List<ChangeStatus> GetAllStatuses()
        {
            var modules = GetAllModules();

            HashSet<string> cachedKeys;
            lock (_cacheLock)
                cachedKeys = new HashSet<string>(_statusCache.Keys, StringComparer.Ordinal);

            var missing = modules
                .Where(m => !cachedKeys.Contains(m.Path))
                .ToList();

            if (missing.Count > 0)
            {
                if (IsUiThread())
                {
                    lock (_cacheLock)
                    {
                        foreach (var mod in missing)
                        {
                            if (_statusCache.ContainsKey(mod.Path))
                                continue;
                            _statusCache[mod.Path] = new ChangeStatus
                            {
                                ModulePath = mod.Path,
                                DisplayName = mod.DisplayName,
                                Status = ChangeStatusType.Changed,
                                IsEnabled = true,
                                ItemCount = 0,
                                LastPackaged = _lastPackageTime
                            };
                        }
                    }

                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        try
                        {
                            foreach (var mod in missing)
                                await RefreshModuleStatusAsync(mod).ConfigureAwait(false);
                        }
                        catch (Exception ex) { TM.App.Log($"[ChangeDetection] 后台批量刷新模块状态失败: {ex.Message}"); }
                    });
                }
                else
                {
                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        try
                        {
                            foreach (var mod in missing)
                                await RefreshModuleStatusAsync(mod).ConfigureAwait(false);
                        }
                        catch (Exception ex) { TM.App.Log($"[ChangeDetection] 后台批量刷新模块状态失败: {ex.Message}"); }
                    });
                }
            }

            lock (_cacheLock) { return _statusCache.Values.ToList(); }
        }

        public List<ChangeStatus> GetDesignStatuses()
        {
            return GetAllStatuses()
                .Where(s => s.ModulePath.StartsWith("Design/", StringComparison.Ordinal))
                .ToList();
        }

        public List<ChangeStatus> GetGenerateStatuses()
        {
            return GetAllStatuses()
                .Where(s => s.ModulePath.StartsWith("Generate/", StringComparison.Ordinal))
                .ToList();
        }

        public List<ChangeStatus> GetValidateStatuses()
        {
            return GetAllStatuses()
                .Where(s => s.ModulePath.StartsWith("Validate/", StringComparison.Ordinal))
                .ToList();
        }

        public void MarkAsPackaged(string modulePath)
        {
            lock (_cacheLock)
            {
                if (_statusCache.TryGetValue(modulePath, out var status))
                {
                    status.LastPackaged = DateTime.Now;
                    status.Status = ChangeStatusType.Latest;
                }
            }
        }

        public void MarkAllAsPackaged()
        {
            var now = DateTime.Now;
            lock (_cacheLock)
            {
                foreach (var status in _statusCache.Values)
                {
                    status.LastPackaged = now;
                    status.Status = ChangeStatusType.Latest;
                }
            }
            _lastPackageTime = now;
        }

        public void MarkModuleEnabled(string modulePath, bool isEnabled)
        {
            lock (_cacheLock)
            {
                if (_statusCache.TryGetValue(modulePath, out var status))
                {
                    status.IsEnabled = isEnabled;
                    TM.App.Log($"[ChangeDetectionService] 模块启用状态已更新: {modulePath} = {isEnabled}");
                }
            }
        }

        #endregion

        #region 私有方法

        private async Task RefreshModuleStatusAsync(ModuleDefinition module)
        {
            try
            {
                var storageRoot = StoragePathHelper.GetStorageRoot();
                var moduleDirPath = Path.Combine(storageRoot, module.StoragePath);

                bool existingIsEnabled = true;
                lock (_cacheLock)
                {
                    if (_statusCache.TryGetValue(module.Path, out var prev))
                        existingIsEnabled = prev.IsEnabled;
                }
                var status = new ChangeStatus
                {
                    ModulePath = module.Path,
                    DisplayName = module.DisplayName,
                    IsEnabled = existingIsEnabled
                };

                if (Directory.Exists(moduleDirPath))
                {
                    var jsonFiles = Directory.GetFiles(moduleDirPath, "*.json", SearchOption.AllDirectories);

                    if (jsonFiles.Length > 0)
                    {
                        var latestModified = jsonFiles
                            .Select(f => File.GetLastWriteTime(f))
                            .Max();

                        status.LastModified = latestModified;
                        status.ItemCount = await CountDataItemsAsync(jsonFiles).ConfigureAwait(false);
                    }

                    status.LastPackaged = _lastPackageTime;

                    if (_lastPackageTime == null)
                        status.Status = ChangeStatusType.Never;
                    else if (status.LastModified > _lastPackageTime)
                        status.Status = ChangeStatusType.Changed;
                    else
                        status.Status = ChangeStatusType.Latest;
                }
                else
                {
                    status.Status = ChangeStatusType.Never;
                    status.ItemCount = 0;
                }

                lock (_cacheLock) { _statusCache[module.Path] = status; }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChangeDetectionService] 刷新模块状态失败 [{module.Path}]: {ex.Message}");
            }
        }

        private async Task<int> CountDataItemsAsync(string[] jsonFiles)
        {
            int totalCount = 0;

            foreach (var file in jsonFiles)
            {
                var fn = Path.GetFileName(file);
                if (fn.Equals("categories.json", StringComparison.OrdinalIgnoreCase) ||
                    fn.Equals("built_in_categories.json", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var json = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        totalCount += doc.RootElement.GetArrayLength();
                }
                catch (Exception ex)
                {
                    DebugLogOnce(nameof(CountDataItemsAsync), ex);
                }
            }

            return totalCount;
        }

        private async Task LoadManifestInfoAsync()
        {
            try
            {
                var manifestPath = Path.Combine(
                    StoragePathHelper.GetCurrentProjectPath(),
                    "manifest.json");

                if (File.Exists(manifestPath))
                {
                    var json = await File.ReadAllTextAsync(manifestPath).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("publishTime", out var publishTimeElement) ||
                        doc.RootElement.TryGetProperty("PublishTime", out publishTimeElement))
                    {
                        if (DateTime.TryParse(publishTimeElement.GetString(), out var publishTime))
                        {
                            _lastPackageTime = publishTime;
                            TM.App.Log($"[ChangeDetectionService] 加载上次打包时间: {_lastPackageTime}");
                        }
                    }
                }
                else
                {
                    TM.App.Log("[ChangeDetectionService] manifest.json不存在，从未打包");
                    _lastPackageTime = null;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChangeDetectionService] 加载manifest失败: {ex.Message}");
                _lastPackageTime = null;
            }
        }

        #endregion

        #region 内部类

        private class ModuleDefinition
        {
            public string Path { get; }
            public string DisplayName { get; }
            public string StoragePath { get; }

            public ModuleDefinition(string path, string displayName, string storagePath)
            {
                Path = path;
                DisplayName = displayName;
                StoragePath = storagePath;
            }
        }

        #endregion
    }
}
