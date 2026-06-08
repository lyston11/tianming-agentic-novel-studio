using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class GuideManager
    {
        #region 构造函数

        public GuideManager()
        {
            StoragePathHelper.CurrentProjectChanged += (_, _) =>
            {
                try
                {
                    ClearCache();
                    RecoverPendingFlush();
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[GuideManager] 项目切换后清理失败: {ex.Message}");
                }
            };
        }

        #endregion

        #region 字段

        private readonly object _lock = new();

        private readonly Dictionary<string, object> _cache = new();
        private readonly HashSet<string> _dirtyFiles = new();
        private int _changeCount = 0;
        private const int FlushThreshold = 10;
        private const string CommitMarkerFileName = "_commit";

        private readonly Dictionary<string, long> _cacheAccessOrder = new();
        private long _accessCounter = 0L;
        private const int MaxCleanCacheEntries = 1000;

        private readonly SemaphoreSlim _saveSemaphore = new(1, 1);

        private readonly Dictionary<string, (Task<object> Task, int Epoch)> _loadingTasks = new();
        private int _cacheEpoch;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static JsonTypeInfo<T>? TryGetSourceGenTypeInfo<T>()
        {
            var ctx = GuideSerializerContext.Default;
            var info = ctx.GetTypeInfo(typeof(T));
            return info as JsonTypeInfo<T>;
        }

        #endregion

        #region 实体变更事件（供 EntityFirstChapterIndex 等订阅者触发 Rebuild）

        public event Action<string, string, string>? EntryChanged;

        public void RaiseEntryChanged(string entityId, string entityName, string entityDescription)
        {
            if (string.IsNullOrEmpty(entityId)) return;
            try
            {
                EntryChanged?.Invoke(entityId, entityName ?? string.Empty, entityDescription ?? string.Empty);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideManager] EntryChanged 处理器异常（已吞）: {ex.Message}");
            }
        }

        #endregion

        #region 公开方法

        public async Task<T> GetGuideAsync<T>(string fileName) where T : new()
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(fileName, out var cached))
                {
                    _cacheAccessOrder[fileName] = ++_accessCounter;
                    return (T)cached;
                }
            }

            (Task<object> Task, int Epoch) loading;
            lock (_lock)
            {
                if (_cache.TryGetValue(fileName, out var cached))
                {
                    _cacheAccessOrder[fileName] = ++_accessCounter;
                    return (T)cached;
                }

                if (!_loadingTasks.TryGetValue(fileName, out loading))
                {
                    var epoch = Volatile.Read(ref _cacheEpoch);
                    var loadTask = LoadAndCacheInternalAsync<T>(fileName, epoch);
                    loading = (loadTask, epoch);
                    _loadingTasks[fileName] = loading;
                }
            }

            return (T)await loading.Task.ConfigureAwait(false);
        }

        public void MarkDirty(string fileName)
        {
            lock (_lock)
            {
                _dirtyFiles.Add(fileName);
                _changeCount++;
            }
        }

        public bool ShouldFlush()
        {
            lock (_lock)
            {
                return _changeCount >= FlushThreshold;
            }
        }

        public Task FlushAllAsync()
        {
            var projectName = StoragePathHelper.CurrentProjectName;
            return FlushAllAsync(projectName);
        }

        public async Task FlushAllAsync(string projectName)
        {
            await _saveSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                await FlushAllInternalAsync(projectName).ConfigureAwait(false);
            }
            finally
            {
                _saveSemaphore.Release();
            }
        }

        private async Task FlushAllInternalAsync(string projectName)
        {
            List<(string fileName, object guide)> toSave;

            lock (_lock)
            {
                if (_dirtyFiles.Count == 0) return;

                toSave = _dirtyFiles
                    .Where(f => _cache.ContainsKey(f))
                    .Select(f => (f, _cache[f]))
                    .ToList();

                _dirtyFiles.Clear();
            }

            var guidesDir = GetGuidesDir(projectName);
            Directory.CreateDirectory(guidesDir);

            var stagingDir = GetStagingDir(guidesDir);
            var commitMarker = Path.Combine(stagingDir, CommitMarkerFileName);

            try
            {
                if (Directory.Exists(stagingDir))
                    Directory.Delete(stagingDir, recursive: true);
                Directory.CreateDirectory(stagingDir);

                await Task.WhenAll(toSave.Select(async entry =>
                {
                    var (fileName, guide) = entry;
                    var stagingPath = Path.Combine(stagingDir, fileName);
                    await using var fs = new FileStream(stagingPath, FileMode.Create, FileAccess.Write, FileShare.None,
                        bufferSize: 4096, useAsync: true);
                    var sgInfo = GuideSerializerContext.Default.GetTypeInfo(guide.GetType());
                    if (sgInfo != null)
                        await JsonSerializer.SerializeAsync(fs, guide, sgInfo).ConfigureAwait(false);
                    else
                        await JsonSerializer.SerializeAsync(fs, guide, JsonOptions).ConfigureAwait(false);
                })).ConfigureAwait(false);

                await File.WriteAllTextAsync(commitMarker, DateTime.Now.ToString("O")).ConfigureAwait(false);

                if (TM.App.IsDebugMode)
                    TM.App.Log($"[GuideManager] P1: {toSave.Count}");

                CommitStagingFiles(stagingDir, guidesDir);

                if (TM.App.IsDebugMode)
                    TM.App.Log($"[GuideManager] P2: {toSave.Count}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideManager] flush err: {ex.Message}");

                lock (_lock)
                {
                    foreach (var (fileName, _) in toSave)
                        _dirtyFiles.Add(fileName);
                }

                if (!File.Exists(commitMarker))
                {
                    try
                    {
                        if (Directory.Exists(stagingDir))
                            Directory.Delete(stagingDir, recursive: true);
                    }
                    catch { }
                }

                TM.App.Log($"[GuideManager] 已回标{toSave.Count}个dirty文件，等待下次重试");
                throw;
            }

            lock (_lock)
            {
                if (_dirtyFiles.Count == 0)
                    _changeCount = 0;
            }

            if (InfoLogDedup.ShouldLog("GuideManager:Flush:Saved"))
                TM.App.Log($"[GuideManager] 批量保存完成，共{toSave.Count}个文件");
        }

        public void RecoverPendingFlush()
        {
            try
            {
                var projectName = StoragePathHelper.CurrentProjectName;
                var guidesDir = GetGuidesDir(projectName);
                if (!Directory.Exists(guidesDir))
                    Directory.CreateDirectory(guidesDir);

                var stagingDir = GetStagingDir(guidesDir);
                if (!Directory.Exists(stagingDir))
                    return;

                var commitMarker = Path.Combine(stagingDir, CommitMarkerFileName);
                if (!File.Exists(commitMarker))
                {
                    TM.App.Log($"[GuideManager] incomplete, discarded");
                    Directory.Delete(stagingDir, recursive: true);
                    return;
                }

                var stagingFiles = Directory.GetFiles(stagingDir, "*.json");
                if (stagingFiles.Length == 0)
                {
                    Directory.Delete(stagingDir, recursive: true);
                    return;
                }

                TM.App.Log($"[GuideManager] recovering {stagingFiles.Length}");
                CommitStagingFiles(stagingDir, guidesDir);
                TM.App.Log($"[GuideManager] recovered {stagingFiles.Length}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideManager] recover err: {ex.Message}");
            }
        }

        public async Task FlushOnExitAsync()
        {
            await FlushAllAsync().ConfigureAwait(false);
            TM.App.Log("[GuideManager] 已保存所有未写入的数据");
        }

        public void ClearCache()
        {
            lock (_lock)
            {
                Interlocked.Increment(ref _cacheEpoch);
                _cache.Clear();
                _cacheAccessOrder.Clear();
                _loadingTasks.Clear();
                _dirtyFiles.Clear();
                _changeCount = 0;
            }
        }

        public void DiscardDirtyAndEvict()
        {
            lock (_lock)
            {
                Interlocked.Increment(ref _cacheEpoch);
                foreach (var key in _dirtyFiles)
                {
                    _cache.Remove(key);
                    _cacheAccessOrder.Remove(key);
                    _loadingTasks.Remove(key);
                }
                _dirtyFiles.Clear();
                _changeCount = 0;
            }
            TM.App.Log("[GuideManager] 已丢弃脏数据并驱逐缓存");
        }

        public (int CachedCount, int DirtyCount, int ChangeCount) GetStats()
        {
            lock (_lock)
            {
                return (_cache.Count, _dirtyFiles.Count, _changeCount);
            }
        }

        public static string GetVolumeFileName(string baseFileName, int volumeNumber)
        {
            var ext = Path.GetExtension(baseFileName);
            var name = Path.GetFileNameWithoutExtension(baseFileName);
            return $"{name}_vol{volumeNumber}{ext}";
        }

        public List<int> GetExistingVolumeNumbers(string baseFileName)
        {
            var projectName = StoragePathHelper.CurrentProjectName;
            var guidesPath = GetGuidesDir(projectName);
            if (!Directory.Exists(guidesPath)) return new List<int>();
            var prefix = Path.GetFileNameWithoutExtension(baseFileName) + "_vol";
            var result = new List<int>();
            foreach (var f in Directory.GetFiles(guidesPath, $"{prefix}*.json"))
            {
                var fn = Path.GetFileNameWithoutExtension(f);
                if (fn.StartsWith(prefix) && int.TryParse(fn[prefix.Length..], out var v))
                    result.Add(v);
            }
            result.Sort();
            return result;
        }

        #endregion

        #region 私有方法

        private async Task<object> LoadAndCacheInternalAsync<T>(string fileName) where T : new()
        {
            return await LoadAndCacheInternalAsync<T>(fileName, Volatile.Read(ref _cacheEpoch)).ConfigureAwait(false);
        }

        private async Task<object> LoadAndCacheInternalAsync<T>(string fileName, int epoch) where T : new()
        {
            try
            {
                var guide = await LoadFromFileAsync<T>(fileName).ConfigureAwait(false);
                bool needEvict = false;
                List<string>? cleanKeys = null;
                Dictionary<string, long>? accessSnapshot = null;
                int maxEntries = 0;
                HashSet<string>? dirtySnapshot = null;
                lock (_lock)
                {
                    if (epoch == Volatile.Read(ref _cacheEpoch))
                    {
                        _cache[fileName] = guide!;
                        _cacheAccessOrder[fileName] = ++_accessCounter;
                        if (_cache.Count - _dirtyFiles.Count > MaxCleanCacheEntries)
                        {
                            cleanKeys = new List<string>(_cache.Keys);
                            dirtySnapshot = new HashSet<string>(_dirtyFiles);
                            accessSnapshot = new Dictionary<string, long>(_cacheAccessOrder);
                            maxEntries = MaxCleanCacheEntries;
                            needEvict = true;
                        }
                    }
                }
                if (needEvict && cleanKeys != null)
                {
                    cleanKeys = cleanKeys.Where(k => !dirtySnapshot!.Contains(k)).ToList();
                    needEvict = cleanKeys.Count > maxEntries;
                }
                if (needEvict && cleanKeys != null && accessSnapshot != null)
                {
                    var toEvict = cleanKeys
                        .OrderBy(k => accessSnapshot.TryGetValue(k, out var o) ? o : 0L)
                        .Take(cleanKeys.Count - maxEntries)
                        .ToList();
                    lock (_lock)
                    {
                        foreach (var key in toEvict)
                        {
                            if (_dirtyFiles.Contains(key)) continue;
                            _cache.Remove(key);
                            _cacheAccessOrder.Remove(key);
                        }
                    }
                }
                return guide!;
            }
            finally
            {
                lock (_lock)
                {
                    if (_loadingTasks.TryGetValue(fileName, out var loading) && loading.Epoch == epoch)
                        _loadingTasks.Remove(fileName);
                }
            }
        }

        private async Task<T> LoadFromFileAsync<T>(string fileName) where T : new()
        {
            var projectName = StoragePathHelper.CurrentProjectName;
            var guidesDir = GetGuidesDir(projectName);
            var path = Path.Combine(guidesDir, fileName);

            if (!File.Exists(path))
            {
                TM.App.Log($"[GuideManager] 文件不存在，返回空对象: {fileName}");
                return new T();
            }

            try
            {
                await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 4096, useAsync: true);

                T? result;
                var sgInfo = TryGetSourceGenTypeInfo<T>();
                if (sgInfo != null)
                    result = await JsonSerializer.DeserializeAsync(fs, sgInfo).ConfigureAwait(false);
                else
                    result = await JsonSerializer.DeserializeAsync<T>(fs, JsonOptions).ConfigureAwait(false);

                TM.App.Log($"[GuideManager] 加载成功: {fileName}");
                return result ?? new T();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideManager] 读取 {fileName} 失败: {ex.Message}");
                return new T();
            }
        }

        public void EvictCache<T>(string fileName, T newValue) where T : class
        {
            lock (_lock)
            {
                Interlocked.Increment(ref _cacheEpoch);
                _cache[fileName] = newValue;
                _cacheAccessOrder[fileName] = ++_accessCounter;
                _loadingTasks.Remove(fileName);
                _dirtyFiles.Remove(fileName);
            }
        }

        public void CleanupExpiredCache()
        {
            List<string> cleanKeys;
            Dictionary<string, long> accessSnapshot;
            int maxEntries;
            lock (_lock)
            {
                cleanKeys = _cache.Keys.Where(k => !_dirtyFiles.Contains(k)).ToList();
                if (cleanKeys.Count <= MaxCleanCacheEntries) return;
                maxEntries = MaxCleanCacheEntries;
                accessSnapshot = new Dictionary<string, long>(_cacheAccessOrder);
            }

            var toEvict = cleanKeys
                .OrderBy(k => accessSnapshot.TryGetValue(k, out var o) ? o : 0L)
                .Take(cleanKeys.Count - maxEntries)
                .ToList();

            lock (_lock)
            {
                foreach (var key in toEvict)
                {
                    if (_dirtyFiles.Contains(key)) continue;
                    _cache.Remove(key);
                    _cacheAccessOrder.Remove(key);
                }
                TM.App.Log($"[GuideManager] LRU驱逐{toEvict.Count}个缓存条目，剩余{_cache.Count}");
            }
        }

        private static void CommitStagingFiles(string stagingDir, string guidesDir)
        {
            Directory.CreateDirectory(guidesDir);

            var stagingFiles = Directory.GetFiles(stagingDir, "*.json");
            var committedCount = 0;

            foreach (var stagingFile in stagingFiles)
            {
                var fileName = Path.GetFileName(stagingFile);
                var targetPath = Path.Combine(guidesDir, fileName);
                File.Move(stagingFile, targetPath, overwrite: true);

                if (!File.Exists(targetPath))
                    throw new IOException($"CommitStagingFiles 验证失败: {fileName} 移动后目标文件不存在");

                committedCount++;
            }

            if (committedCount != stagingFiles.Length)
                TM.App.Log($"[GuideManager] CommitStagingFiles 警告: 预期提交{stagingFiles.Length}个文件，实际{committedCount}个");

            Directory.Delete(stagingDir, recursive: true);
        }

        private static string GetGuidesDir(string projectName)
        {
            return Path.Combine(StoragePathHelper.GetStorageRoot(), "Projects", projectName, "Config", "guides");
        }

        private static string GetStagingDir(string guidesDir)
        {
            return Path.Combine(guidesDir, ".flush_staging");
        }

        #endregion
    }
}
