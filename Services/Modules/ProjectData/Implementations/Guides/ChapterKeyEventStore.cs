using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.TaskContexts;

namespace TM.Services.Modules.ProjectData.Implementations.Guides
{
    public class ChapterKeyEventStore
    {
        private static readonly JsonSerializerOptions JsonOpts =
            new() { PropertyNameCaseInsensitive = true };

        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public ChapterKeyEventStore()
        {
            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) =>
                    TM.App.Log("[ChapterKeyEventStore] 项目切换");
            }
            catch { }
        }

        public async Task UpsertAsync(int volumeNumber, ChapterKeyEventEntry entry)
        {
            if (volumeNumber <= 0 || entry == null) return;

            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var path = GetFilePath(volumeNumber);
                EnsureDir(path);

                var lines = new List<string>();
                if (File.Exists(path))
                {
                    foreach (var l in await File.ReadAllLinesAsync(path).ConfigureAwait(false))
                    {
                        if (string.IsNullOrWhiteSpace(l)) continue;
                        try
                        {
                            var e = JsonSerializer.Deserialize<ChapterKeyEventEntry>(l, JsonOpts);
                            if (e != null && !string.Equals(e.ChapterId, entry.ChapterId,
                                    StringComparison.OrdinalIgnoreCase))
                                lines.Add(l);
                        }
                        catch { lines.Add(l); }
                    }
                }

                lines.Add(JsonSerializer.Serialize(entry));
                await File.WriteAllLinesAsync(path, lines).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task<List<ChapterKeyEventEntry>> GetPreviousKeyEventsAsync(
            int currentVolumeNumber,
            int maxVolumes = 5,
            int maxEntriesPerVolume = 30)
        {
            if (currentVolumeNumber <= 1) return new();

            var startVol = Math.Max(1, currentVolumeNumber - maxVolumes);
            var result = new List<ChapterKeyEventEntry>();

            for (int vol = startVol; vol < currentVolumeNumber; vol++)
            {
                var entries = await LoadVolumeAsync(vol).ConfigureAwait(false);
                var take = entries.Count <= maxEntriesPerVolume
                    ? entries
                    : entries.Skip(entries.Count - maxEntriesPerVolume).ToList();
                result.AddRange(take);
            }

            return result;
        }

        private async Task<List<ChapterKeyEventEntry>> LoadVolumeAsync(int volumeNumber)
        {
            var path = GetFilePath(volumeNumber);
            if (!File.Exists(path)) return new();

            var result = new List<ChapterKeyEventEntry>();
            try
            {
                foreach (var line in await File.ReadAllLinesAsync(path).ConfigureAwait(false))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var e = JsonSerializer.Deserialize<ChapterKeyEventEntry>(line, JsonOpts);
                        if (e != null) result.Add(e);
                    }
                    catch { }
                }
                result.Sort((a, b) => a.ChapterNumber.CompareTo(b.ChapterNumber));
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterKeyEventStore] 加载 vol{volumeNumber} 失败: {ex.Message}");
            }
            return result;
        }

        private string GetFilePath(int volumeNumber)
            => Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides", "keyevents", $"vol{volumeNumber}.jsonl");

        private static void EnsureDir(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
    }
}
