using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class PlotPointsIndexService
    {
        private readonly ConcurrentDictionary<int, List<PlotPointEntry>> _volumeCache = new();
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private int _cacheEpoch;

        public PlotPointsIndexService()
        {
            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) => InvalidateCache();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PlotPointsIndexService] 订阅项目切换事件失败: {ex.Message}");
            }
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        #region 公开方法 - 写入

        public async Task AddPlotPointAsync(string chapterId, PlotPointChange change)
        {
            var entry = new PlotPointEntry
            {
                Id = ShortIdGenerator.New("D"),
                Chapter = chapterId,
                Keywords = change.Keywords,
                Context = change.Context,
                InvolvedCharacters = change.InvolvedCharacters,
                Importance = change.Importance,
                Storyline = change.Storyline,
                CausedBy = change.CausedBy ?? string.Empty
            };

            var vol = GetVolumeNumber(chapterId);
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                Interlocked.Increment(ref _cacheEpoch);
                var entries = await LoadVolumeAsync(vol).ConfigureAwait(false);
                var updated = new List<PlotPointEntry>(entries) { entry };
                await SaveVolumeAsync(vol, updated).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }

            TM.App.Log($"[PlotPoints] 已添加情节索引: {string.Join(", ", change.Keywords ?? new List<string>())}");
        }

        public async Task RemoveChapterDataAsync(string chapterId)
        {
            var vol = GetVolumeNumber(chapterId);
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                Interlocked.Increment(ref _cacheEpoch);
                var entries = await LoadVolumeAsync(vol).ConfigureAwait(false);
                var removed = entries.Count(p => string.Equals(p.Chapter, chapterId, StringComparison.Ordinal));
                if (removed > 0)
                {
                    var updated = entries
                        .Where(p => !string.Equals(p.Chapter, chapterId, StringComparison.Ordinal))
                        .ToList();
                    await SaveVolumeAsync(vol, updated).ConfigureAwait(false);
                    TM.App.Log($"[PlotPoints] 已移除章节 {chapterId} 的情节记录: {removed}条");
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        #endregion

        #region 公开方法 - 查询

        public async Task<List<PlotPointEntry>> SearchRecentAsync(
            string currentChapterId,
            HashSet<string>? characterIds,
            HashSet<string>? otherEntityIds,
            int lookbackVolumes = 0)
        {
            var parsed = ChapterParserHelper.ParseChapterId(currentChapterId);
            var currentVol = parsed?.volumeNumber ?? 1;
            var minVol = lookbackVolumes <= 0 ? 1 : Math.Max(1, currentVol - lookbackVolumes);
            var volCount = currentVol - minVol + 1;
            if (volCount <= 0)
                return new List<PlotPointEntry>();

            var volumes = Enumerable.Range(minVol, volCount).ToList();
            var allEntries = await Task.WhenAll(volumes.Select(v => LoadVolumeAsync(v))).ConfigureAwait(false);

            var result = new List<PlotPointEntry>();
            foreach (var entries in allEntries)
            {
                foreach (var e in entries)
                {
                    if (string.IsNullOrWhiteSpace(e.Context)) continue;
                    var matchChar = characterIds != null && characterIds.Count > 0 &&
                                   e.InvolvedCharacters != null &&
                                   e.InvolvedCharacters.Any(c => characterIds.Contains(c));
                    var matchOther = otherEntityIds != null && otherEntityIds.Count > 0 &&
                                    e.Keywords != null &&
                                    e.Keywords.Any(k => otherEntityIds.Contains(k));
                    if (matchChar || matchOther)
                        result.Add(e);
                }
            }
            return result;
        }

        public List<int> GetExistingVolumeNumbers()
        {
            var dir = GetPlotPointsDir();
            if (!Directory.Exists(dir)) return new List<int>();
            return Directory.GetFiles(dir, "vol*.json")
                .Select(f =>
                {
                    var name = Path.GetFileNameWithoutExtension(f);
                    return name.StartsWith("vol", StringComparison.Ordinal) && int.TryParse(name.Substring(3), out var n) ? n : -1;
                })
                .Where(n => n > 0)
                .OrderBy(n => n)
                .ToList();
        }

        public async Task<List<PlotPointEntry>> GetVolumeEntriesAsync(int volumeNumber)
            => new List<PlotPointEntry>(await LoadVolumeAsync(volumeNumber).ConfigureAwait(false));

        public async Task SetVolumeEntriesAsync(int volumeNumber, List<PlotPointEntry> entries)
        {
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                Interlocked.Increment(ref _cacheEpoch);
                await SaveVolumeAsync(volumeNumber, entries).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public void InvalidateCache()
        {
            Interlocked.Increment(ref _cacheEpoch);
            _volumeCache.Clear();
        }

        #endregion

        #region 私有方法

        private string GetPlotPointsDir()
            => Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides", "plot_points");

        private string GetVolumeFilePath(int volumeNumber)
            => Path.Combine(GetPlotPointsDir(), $"vol{volumeNumber}.json");

        private static int GetVolumeNumber(string chapterId)
            => ChapterParserHelper.ParseChapterId(chapterId)?.volumeNumber ?? 1;

        private async Task<List<PlotPointEntry>> LoadVolumeAsync(int volumeNumber)
        {
            if (_volumeCache.TryGetValue(volumeNumber, out var cached))
                return cached;
            return await LoadVolumeInternalAsync(volumeNumber).ConfigureAwait(false);
        }

        private async Task<List<PlotPointEntry>> LoadVolumeInternalAsync(int volumeNumber)
        {
            var path = GetVolumeFilePath(volumeNumber);
            var epoch = Volatile.Read(ref _cacheEpoch);
            List<PlotPointEntry> entries;
            if (File.Exists(path))
            {
                try
                {
                    await using var stream = File.OpenRead(path);
                    entries = await JsonSerializer.DeserializeAsync<List<PlotPointEntry>>(stream, JsonOptions).ConfigureAwait(false)
                              ?? new List<PlotPointEntry>();
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[PlotPoints] 加载 vol{volumeNumber} 失败: {ex.Message}");
                    entries = new List<PlotPointEntry>();
                }
            }
            else
            {
                entries = new List<PlotPointEntry>();
            }
            if (epoch == Volatile.Read(ref _cacheEpoch))
                _volumeCache[volumeNumber] = entries;
            return entries;
        }

        private async Task SaveVolumeAsync(int volumeNumber, List<PlotPointEntry> entries)
        {
            var dir = GetPlotPointsDir();
            Directory.CreateDirectory(dir);
            var path = GetVolumeFilePath(volumeNumber);
            var tmpPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var stream = File.Create(tmpPath))
                {
                    await JsonSerializer.SerializeAsync(stream, entries, JsonOptions).ConfigureAwait(false);
                }
                File.Move(tmpPath, path, overwrite: true);
                _volumeCache[volumeNumber] = entries;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PlotPoints] 保存 vol{volumeNumber} 失败: {ex.Message}");
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                throw;
            }
        }

        #endregion
    }
}
