using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class ChapterSummaryStore
    {
        private readonly ConcurrentDictionary<int, Dictionary<string, string>> _volumeCache = new();
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private int _cacheEpoch;

        public ChapterSummaryStore()
        {
            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) =>
                {
                    InvalidateCache();
                    TM.App.Log("[ChapterSummaryStore] 项目切换，已清除摘要分片缓存");
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterSummaryStore] 订阅项目切换事件失败: {ex.Message}");
            }
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        #region 公开方法

        internal const int SummaryGuardMaxChars = 1500;

        public async Task SetSummaryAsync(string chapterId, string summary)
        {
            if (summary != null && summary.Length > SummaryGuardMaxChars)
            {
                TM.App.Log($"[SummaryStore] 兜底截断: {chapterId} {summary.Length} → {SummaryGuardMaxChars} chars");
                summary = summary.Substring(0, SummaryGuardMaxChars) + "...";
            }

            var vol = GetVolumeNumber(chapterId);
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                Interlocked.Increment(ref _cacheEpoch);
                var existing = await LoadVolumeInternalAsync(vol).ConfigureAwait(false);
                var updated = new Dictionary<string, string>(existing) { [chapterId] = summary ?? string.Empty };
                await SaveVolumeAsync(vol, updated).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task RemoveSummaryAsync(string chapterId)
        {
            var vol = GetVolumeNumber(chapterId);
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                Interlocked.Increment(ref _cacheEpoch);
                var existing = await LoadVolumeInternalAsync(vol).ConfigureAwait(false);
                if (existing.ContainsKey(chapterId))
                {
                    var updated = new Dictionary<string, string>(existing);
                    updated.Remove(chapterId);
                    await SaveVolumeAsync(vol, updated).ConfigureAwait(false);
                    TM.App.Log($"[SummaryStore] 已移除摘要: {chapterId}");
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task<string> GetSummaryAsync(string chapterId)
        {
            var vol = GetVolumeNumber(chapterId);
            var summaries = await LoadVolumeAsync(vol).ConfigureAwait(false);
            return summaries.GetValueOrDefault(chapterId, string.Empty);
        }

        public async Task<Dictionary<string, string>> GetPreviousSummariesAsync(string currentChapterId, int count)
        {
            var parsed = ChapterParserHelper.ParseChapterId(currentChapterId);
            if (!parsed.HasValue)
                return new Dictionary<string, string>();

            var currentVol = parsed.Value.volumeNumber;

            var allSummaries = new Dictionary<string, string>(await LoadVolumeAsync(currentVol).ConfigureAwait(false));

            var _preloadStart = System.Math.Max(1, currentVol - 5);
            var _preloadTasks = Enumerable.Range(_preloadStart, currentVol - _preloadStart)
                .Select(v => LoadVolumeAsync(v)).ToList();
            if (_preloadTasks.Count > 0) await Task.WhenAll(_preloadTasks).ConfigureAwait(false);

            var volToLoad = currentVol - 1;
            while (volToLoad >= 1)
            {
                var previousCount = allSummaries
                    .Count(kv => ChapterParserHelper.CompareChapterId(kv.Key, currentChapterId) < 0);
                if (previousCount >= count) break;

                var prevVolSummaries = await LoadVolumeAsync(volToLoad).ConfigureAwait(false);
                foreach (var kv in prevVolSummaries)
                    allSummaries.TryAdd(kv.Key, kv.Value);

                volToLoad--;
            }

            return allSummaries;
        }

        public async Task<Dictionary<string, string>> GetVolumeSummariesAsync(int volumeNumber)
        {
            return new Dictionary<string, string>(await LoadVolumeAsync(volumeNumber).ConfigureAwait(false));
        }

        public async Task<Dictionary<string, string>> GetAllSummariesAsync()
        {
            var dir = GetSummariesDir();
            if (!Directory.Exists(dir))
                return new Dictionary<string, string>();

            var files = Directory.GetFiles(dir, "vol*.json");
            var volumeNumbers = files
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .Where(n => n.StartsWith("vol", StringComparison.Ordinal) && int.TryParse(n.Substring(3), out _))
                .Select(n => int.Parse(n.Substring(3)))
                .ToList();

            var allVolumes = await Task.WhenAll(volumeNumbers.Select(v => LoadVolumeAsync(v))).ConfigureAwait(false);

            var result = new Dictionary<string, string>();
            foreach (var summaries in allVolumes)
            {
                foreach (var kv in summaries)
                {
                    result.TryAdd(kv.Key, kv.Value);
                }
            }

            return result;
        }

        public void InvalidateCache()
        {
            Interlocked.Increment(ref _cacheEpoch);
            _volumeCache.Clear();
        }

        public async Task BulkSetAsync(Dictionary<string, string> summaries)
        {
            if (summaries == null || summaries.Count == 0) return;

            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                Interlocked.Increment(ref _cacheEpoch);
                var byVolume = new Dictionary<int, Dictionary<string, string>>();
                foreach (var kv in summaries)
                {
                    var vol = GetVolumeNumber(kv.Key);
                    if (!byVolume.TryGetValue(vol, out var volDict))
                    {
                        volDict = new Dictionary<string, string>();
                        byVolume[vol] = volDict;
                    }
                    volDict[kv.Key] = kv.Value;
                }

                var dir = GetSummariesDir();
                Directory.CreateDirectory(dir);

                foreach (var (vol, volSummaries) in byVolume)
                {
                    await SaveVolumeInternalAsync(vol, volSummaries).ConfigureAwait(false);
                    _volumeCache[vol] = volSummaries;
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        #endregion

        #region 私有方法

        private string GetSummariesDir()
        {
            return Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides", "summaries");
        }

        private string GetVolumeFilePath(int volumeNumber)
        {
            return Path.Combine(GetSummariesDir(), $"vol{volumeNumber}.json");
        }

        private static int GetVolumeNumber(string chapterId)
        {
            return ChapterParserHelper.ParseChapterId(chapterId)?.volumeNumber ?? 1;
        }

        private async Task<Dictionary<string, string>> LoadVolumeAsync(int volumeNumber)
        {
            if (_volumeCache.TryGetValue(volumeNumber, out var cached))
                return cached;

            return await LoadVolumeInternalAsync(volumeNumber).ConfigureAwait(false);
        }

        private async Task<Dictionary<string, string>> LoadVolumeInternalAsync(int volumeNumber)
        {
            var path = GetVolumeFilePath(volumeNumber);
            var epoch = Volatile.Read(ref _cacheEpoch);
            Dictionary<string, string> summaries;

            if (File.Exists(path))
            {
                try
                {
                    await using var stream = File.OpenRead(path);
                    summaries = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, JsonOptions).ConfigureAwait(false)
                                ?? new Dictionary<string, string>();
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[SummaryStore] 加载 vol{volumeNumber} 失败: {ex.Message}");
                    summaries = new Dictionary<string, string>();
                }
            }
            else
            {
                summaries = new Dictionary<string, string>();
            }

            if (epoch == Volatile.Read(ref _cacheEpoch))
                _volumeCache[volumeNumber] = summaries;
            return summaries;
        }

        private async Task SaveVolumeAsync(int volumeNumber, Dictionary<string, string> summaries)
        {
            await SaveVolumeInternalAsync(volumeNumber, summaries).ConfigureAwait(false);
            _volumeCache[volumeNumber] = summaries;
        }

        private async Task SaveVolumeInternalAsync(int volumeNumber, Dictionary<string, string> summaries)
        {
            var dir = GetSummariesDir();
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var path = GetVolumeFilePath(volumeNumber);
            var tmpPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                await using (var stream = File.Create(tmpPath))
                {
                    await JsonSerializer.SerializeAsync(stream, summaries, JsonOptions).ConfigureAwait(false);
                }
                File.Move(tmpPath, path, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SummaryStore] 保存 vol{volumeNumber} 失败: {ex.Message}");

                try
                {
                    if (File.Exists(tmpPath))
                        File.Delete(tmpPath);
                }
                catch { }

                throw;
            }
        }

        #endregion
    }
}
