using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class VolumeFactArchiveStore
    {
        private readonly ConcurrentDictionary<int, VolumeFactArchive> _cache = new();
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private int _cacheEpoch;

        public VolumeFactArchiveStore()
        {
            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) =>
                {
                    InvalidateCache();
                    TM.App.Log("[FactArchiveStore] 项目切换，已清除卷存档缓存");
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactArchiveStore] 订阅项目切换事件失败: {ex.Message}");
            }
        }

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        #region 公开方法

        public void InvalidateCache()
        {
            Interlocked.Increment(ref _cacheEpoch);
            _cache.Clear();
        }

        public async Task ArchiveVolumeAsync(int volumeNumber, FactSnapshot snapshot, string lastChapterId)
        {
            if (volumeNumber <= 0 || snapshot == null) return;

            var archive = new VolumeFactArchive
            {
                VolumeNumber = volumeNumber,
                LastChapterId = lastChapterId,
                ArchivedAt = DateTime.Now,
                CharacterStates = snapshot.CharacterStates ?? new(),
                ConflictProgress = snapshot.ConflictProgress ?? new(),
                ForeshadowingStatus = snapshot.ForeshadowingStatus ?? new(),
                LocationStates = snapshot.LocationStates ?? new(),
                FactionStates = snapshot.FactionStates ?? new(),
                ItemStates = snapshot.ItemStates ?? new(),
                SecretStates = snapshot.SecretStates ?? new(),
                Timeline = snapshot.Timeline ?? new(),
                CharacterLocations = snapshot.CharacterLocations ?? new(),
                PledgeStates = snapshot.PledgeStates ?? new(),
                DeadlineStates = snapshot.DeadlineStates ?? new()
            };

            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                Interlocked.Increment(ref _cacheEpoch);
                await SaveArchiveAsync(volumeNumber, archive).ConfigureAwait(false);
                _cache[volumeNumber] = archive;
            }
            finally
            {
                _writeLock.Release();
            }

            TM.App.Log($"[FactArchiveStore] 第{volumeNumber}卷存档完成: {lastChapterId}，角色{archive.CharacterStates.Count}+冲突{archive.ConflictProgress.Count}+伏笔{archive.ForeshadowingStatus.Count}+地点{archive.LocationStates.Count}+势力{archive.FactionStates.Count}+物品{archive.ItemStates.Count}+秘密{archive.SecretStates.Count}+时间线{archive.Timeline.Count}+承诺{archive.PledgeStates.Count}+倒计时{archive.DeadlineStates.Count}条");
        }

        public async Task DeleteArchiveIfLastChapterAsync(int volumeNumber, string chapterId)
        {
            if (volumeNumber <= 0 || string.IsNullOrWhiteSpace(chapterId)) return;

            var archive = await LoadArchiveAsync(volumeNumber).ConfigureAwait(false);
            if (archive == null) return;
            if (!string.Equals(archive.LastChapterId, chapterId, StringComparison.OrdinalIgnoreCase)) return;

            var path = GetArchiveFilePath(volumeNumber);
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                Interlocked.Increment(ref _cacheEpoch);
                _cache.TryRemove(volumeNumber, out _);
                if (File.Exists(path))
                {
                    File.Delete(path);
                    TM.App.Log($"[FactArchiveStore] 第{volumeNumber}卷存档已删除（LastChapterId={chapterId} 被删除）");
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task DeleteArchiveAsync(int volumeNumber)
        {
            if (volumeNumber <= 0) return;

            var path = GetArchiveFilePath(volumeNumber);
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                Interlocked.Increment(ref _cacheEpoch);
                _cache.TryRemove(volumeNumber, out _);
                if (File.Exists(path))
                {
                    File.Delete(path);
                    TM.App.Log($"[FactArchiveStore] 第{volumeNumber}卷存档已删除（章节删除级联）");
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task<List<VolumeFactArchive>> GetPreviousArchivesAsync(int currentVolumeNumber)
        {
            var result = new List<VolumeFactArchive>();
            if (currentVolumeNumber <= 1) return result;

            var cfg = LayeredContextConfig.TakeSnapshot();
            var dir = GetArchivesDir();
            if (!Directory.Exists(dir)) return result;

            var maxVols = cfg.ArchiveMaxPreviousVolumes;
            var startVol = System.Math.Max(1, currentVolumeNumber - maxVols);
            var volRange = Enumerable.Range(startVol, currentVolumeNumber - startVol);
            var archives = await Task.WhenAll(volRange.Select(vol => LoadArchiveAsync(vol))).ConfigureAwait(false);
            result.AddRange(archives.OfType<VolumeFactArchive>());

            return result;
        }

        #endregion

        #region 私有方法

        private string GetArchivesDir()
        {
            return Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides", "fact_archives");
        }

        private string GetArchiveFilePath(int volumeNumber)
        {
            return Path.Combine(GetArchivesDir(), $"vol{volumeNumber}.json");
        }

        private async Task<VolumeFactArchive?> LoadArchiveAsync(int volumeNumber)
        {
            if (_cache.TryGetValue(volumeNumber, out var cached))
                return cached;

            var path = GetArchiveFilePath(volumeNumber);
            var epoch = Volatile.Read(ref _cacheEpoch);
            if (!File.Exists(path)) return null;

            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                var archive = await JsonSerializer.DeserializeAsync<VolumeFactArchive>(stream, _jsonOptions).ConfigureAwait(false);
                if (archive != null && epoch == Volatile.Read(ref _cacheEpoch))
                    _cache[volumeNumber] = archive;
                return archive;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactArchiveStore] 加载 vol{volumeNumber} 失败: {ex.Message}");
                return null;
            }
        }

        private async Task SaveArchiveAsync(int volumeNumber, VolumeFactArchive archive)
        {
            var dir = GetArchivesDir();
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var path = GetArchiveFilePath(volumeNumber);
            var tmpPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                await using (var stream = File.Create(tmpPath))
                {
                    await JsonSerializer.SerializeAsync(stream, archive, _jsonOptions).ConfigureAwait(false);
                }
                File.Move(tmpPath, path, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactArchiveStore] 保存 vol{volumeNumber} 失败: {ex.Message}");
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                throw;
            }
        }

        #endregion
    }
}
