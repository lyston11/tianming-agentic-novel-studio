using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Modules.VersionTracking.Models;

namespace TM.Services.Modules.VersionTracking
{
    public class VersionTrackingService
    {
        private string _registryPath;
        private string _lastKnownProjectRoot = string.Empty;
        private VersionRegistry _registry = new();
        private int _registryVersion;

        public VersionTrackingService()
        {
            _registryPath = BuildRegistryPath();
            LoadRegistryAsync().SafeFireAndForget(ex => TM.App.Log($"[VersionTracking] 加载注册表失败: {ex.Message}"));
        }

        private static string BuildRegistryPath()
            => Path.Combine(StoragePathHelper.GetCurrentProjectPath(), "version_registry.json");

        private void EnsureCurrentProject()
        {
            var currentRoot = StoragePathHelper.GetCurrentProjectPath();
            if (_lastKnownProjectRoot == currentRoot) return;
            _lastKnownProjectRoot = currentRoot;
            _registryPath = Path.Combine(currentRoot, "version_registry.json");
            Interlocked.Increment(ref _registryVersion);
            _registry = new VersionRegistry();
            LoadRegistryAsync().SafeFireAndForget(ex => TM.App.Log($"[VersionTracking] 重载注册表失败: {ex.Message}"));
            TM.App.Log("[VersionTracking] 检测到项目变化，已异步重载版本注册表（乐观读）");
        }

        public int GetModuleVersion(string moduleName)
        {
            EnsureCurrentProject();
            return _registry.ModuleVersions.TryGetValue(moduleName, out var v) ? v : 0;
        }

        private System.Threading.Tasks.Task _saveTask = System.Threading.Tasks.Task.CompletedTask;
        private readonly object _saveTaskLock = new object();

        public int IncrementModuleVersion(string moduleName, bool showDownstreamToast = true)
        {
            EnsureCurrentProject();
            Interlocked.Increment(ref _registryVersion);
            if (!_registry.ModuleVersions.ContainsKey(moduleName))
                _registry.ModuleVersions[moduleName] = 0;

            _registry.ModuleVersions[moduleName]++;

            var snapshot = new VersionRegistry
            {
                ModuleVersions = new Dictionary<string, int>(_registry.ModuleVersions)
            };
            var path = _registryPath;

            lock (_saveTaskLock)
            {
                _saveTask = _saveTask.ContinueWith(async _ =>
                {
                    try
                    {
                        var json = JsonSerializer.Serialize(snapshot, JsonHelper.Default);
                        var dir = Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);
                        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
                        await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
                        File.Move(tmp, path, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[VersionTracking] 保存版本注册表失败: {ex.Message}");
                    }
                }, System.Threading.Tasks.TaskScheduler.Default).Unwrap();
            }

            TM.App.Log($"[VersionTracking] 模块版本自增: {moduleName} → {_registry.ModuleVersions[moduleName]}");

            if (showDownstreamToast)
            {
                NotifyDownstreamModules(moduleName);
            }

            return _registry.ModuleVersions[moduleName];
        }

        private readonly HashSet<string> _pendingDownstreamNotifications = new();

        public bool SuppressDownstreamToast { get; set; }

        public void FlushPendingDownstreamNotifications(bool showToast = true)
        {
            var pending = _pendingDownstreamNotifications.ToList();
            _pendingDownstreamNotifications.Clear();
            if (!showToast)
            {
                if (pending.Count > 0)
                    TM.App.Log($"[VersionTracking] 已清理 {pending.Count} 个被抑制的下游影响提示");
                return;
            }
            foreach (var moduleName in pending)
                NotifyDownstreamModules(moduleName);
        }

        private void NotifyDownstreamModules(string moduleName)
        {
            if (SuppressDownstreamToast)
            {
                _pendingDownstreamNotifications.Add(moduleName);
                return;
            }
            var downstream = DependencyConfig.GetDownstreamModules(moduleName);
            if (downstream.Count > 0)
            {
                var displayName = DependencyConfig.GetDisplayName(moduleName);
                var downstreamNames = DependencyConfig.GetDisplayNames(downstream);
                var title = $"{displayName}已更新";
                var msg = $"下游模块({downstreamNames})可能需要重新生成";
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    () => GlobalToast.Info(title, msg));
                TM.App.Log($"[VersionTracking] 下游影响提示: {moduleName} → {string.Join(", ", downstream)}");
            }
        }

        public Dictionary<string, int> GetDependencySnapshot(string currentModule)
        {
            EnsureCurrentProject();
            var depModules = DependencyConfig.ModuleDependencies
                .GetValueOrDefault(currentModule, Array.Empty<string>());

            var snapshot = depModules.ToDictionary(
                m => m,
                m => GetModuleVersion(m));

            if (InfoLogDedup.ShouldLog($"VersionTracking:Snapshot:{currentModule}"))
                TM.App.Log($"[VersionTracking] 获取依赖快照: {currentModule} → {snapshot.Count}个依赖");
            return snapshot;
        }

        public List<string> CheckOutdatedDependencies(Dictionary<string, int> savedVersions)
        {
            EnsureCurrentProject();
            if (savedVersions == null || savedVersions.Count == 0)
                return new List<string>();

            var outdated = new List<string>();

            foreach (var kv in savedVersions)
            {
                var currentVersion = GetModuleVersion(kv.Key);
                if (currentVersion > kv.Value)
                {
                    outdated.Add(kv.Key);
                }
            }

            return outdated;
        }

        private async System.Threading.Tasks.Task LoadRegistryAsync()
        {
            var path = _registryPath;
            var loadVersion = Volatile.Read(ref _registryVersion);
            try
            {
                if (!File.Exists(path))
                {
                    if (loadVersion == Volatile.Read(ref _registryVersion))
                        _registry = new VersionRegistry();
                    return;
                }
                var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                var loaded = JsonSerializer.Deserialize<VersionRegistry>(json)
                    ?? new VersionRegistry();
                if (loadVersion == Volatile.Read(ref _registryVersion))
                    _registry = loaded;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[VersionTracking] 加载版本注册表失败: {ex.Message}");
                if (loadVersion == Volatile.Read(ref _registryVersion))
                    _registry = new VersionRegistry();
            }
        }

    }
}
