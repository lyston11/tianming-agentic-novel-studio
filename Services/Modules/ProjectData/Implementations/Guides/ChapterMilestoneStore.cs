using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Implementations.Guides;
using TM.Services.Modules.ProjectData.Models.TaskContexts;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class ChapterMilestoneStore
    {
        private readonly ConcurrentDictionary<int, string> _cache = new();
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private int _cacheEpoch;

        public ChapterMilestoneStore()
        {
            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) =>
                {
                    InvalidateCache();
                    TM.App.Log("[ChapterMilestoneStore] 项目切换，已清除里程碑缓存");
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterMilestoneStore] 订阅项目切换事件失败: {ex.Message}");
            }
        }

        #region 公开方法

        public void InvalidateCache()
        {
            Interlocked.Increment(ref _cacheEpoch);
            _cache.Clear();
        }

        public async Task RebuildVolumeMilestoneAsync(int volumeNumber, Dictionary<string, string> volumeSummaries)
        {
            if (volumeNumber <= 0 || volumeSummaries == null || volumeSummaries.Count == 0)
                return;

            var cfg = LayeredContextConfig.TakeSnapshot();
            var milestone = BuildMilestoneText(volumeNumber, volumeSummaries, cfg);
            var maxChars = cfg.VolumeMilestoneMaxChars;
            if (maxChars > 0 && milestone.Length >= maxChars)
                TM.App.Log($"[MilestoneStore] 第{volumeNumber}卷里程碑已达到上限{maxChars}字，已自动丢弃较早章节摘要以控制规模");

            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                Interlocked.Increment(ref _cacheEpoch);
                await SaveMilestoneAsync(volumeNumber, milestone).ConfigureAwait(false);
                _cache[volumeNumber] = milestone;
            }
            finally
            {
                _writeLock.Release();
            }

            TM.App.Log($"[MilestoneStore] 已更新第{volumeNumber}卷里程碑: {milestone.Length}字");
        }

        public async Task AppendChapterMilestoneAsync(int volumeNumber, string chapterId, string summary)
        {
            if (volumeNumber <= 0 || string.IsNullOrWhiteSpace(summary)) return;

            var cfg = LayeredContextConfig.TakeSnapshot();
            var parsed = ChapterParserHelper.ParseChapterId(chapterId);
            var chNum = parsed?.chapterNumber ?? 0;
            var prefix = chNum > 0 ? $"第{chNum}章" : chapterId;
            var maxChars = cfg.VolumeMilestoneMaxChars > 0
                ? cfg.VolumeMilestoneMaxChars : 12000;
            var perChapterMax = System.Math.Max(200, System.Math.Min(800, maxChars / 10));
            var trunc = summary.Length > perChapterMax ? summary.Substring(0, perChapterMax) + "..." : summary;
            var newLine = $"[{prefix}] {trunc}";

            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                Interlocked.Increment(ref _cacheEpoch);
                var existing = await LoadMilestoneAsync(volumeNumber, cfg).ConfigureAwait(false);
                string updated;
                if (string.IsNullOrWhiteSpace(existing))
                    updated = $"=== 第{volumeNumber}卷 历史摘要 ===" + Environment.NewLine + newLine;
                else
                    updated = existing + Environment.NewLine + newLine;

                if (updated.Length > maxChars)
                {
                    var firstNl = updated.IndexOf('\n');
                    if (firstNl > 0 && firstNl < 300 && updated.StartsWith("=== ", StringComparison.Ordinal))
                    {
                        var header = updated.Substring(0, firstNl).TrimEnd('\r');
                        var body = updated.Substring(firstNl + 1);
                        var keep = maxChars - header.Length - Environment.NewLine.Length;
                        if (keep > 0 && body.Length > keep)
                        {
                            body = body.Substring(body.Length - keep);
                            var firstNlIdx = body.IndexOf('\n');
                            if (firstNlIdx > 0 && firstNlIdx < 200)
                                body = body.Substring(firstNlIdx + 1);
                        }
                        updated = header + Environment.NewLine + body;
                    }
                    else
                    {
                        updated = updated.Substring(updated.Length - maxChars);
                        var nlIdx = updated.IndexOf('\n');
                        if (nlIdx > 0 && nlIdx < 200)
                            updated = updated.Substring(nlIdx + 1);
                    }
                }

                await SaveMilestoneAsync(volumeNumber, updated).ConfigureAwait(false);
                _cache[volumeNumber] = updated;
            }
            finally
            {
                _writeLock.Release();
            }

            TM.App.Log($"[MilestoneStore] 已追加第{volumeNumber}卷/{prefix}里程碑条目");
        }

        public async Task<List<VolumeMilestoneEntry>> GetPreviousMilestonesAsync(int currentVolumeNumber)
        {
            var cfg = LayeredContextConfig.TakeSnapshot();

            var maxVols = cfg.MilestoneMaxPreviousVolumes;
            var startVol = System.Math.Max(1, currentVolumeNumber - maxVols);
            var volCount = currentVolumeNumber - startVol;
            if (volCount <= 0)
                return new List<VolumeMilestoneEntry>();

            var volumes = Enumerable.Range(startVol, volCount).ToList();
            var tasks = volumes.Select(v => LoadMilestoneAsync(v, cfg)).ToList();
            var milestones = await Task.WhenAll(tasks).ConfigureAwait(false);

            var result = new List<VolumeMilestoneEntry>(volCount);
            for (int i = 0; i < volumes.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(milestones[i]))
                {
                    result.Add(new VolumeMilestoneEntry
                    {
                        VolumeNumber = volumes[i],
                        Milestone = milestones[i]
                    });
                }
            }

            return result;
        }

        #endregion

        #region 私有方法

        private static string BuildMilestoneText(int volumeNumber, Dictionary<string, string> volumeSummaries, LayeredContextConfigSnapshot cfg)
        {
            var header = $"=== 第{volumeNumber}卷 历史摘要 ===";
            if (volumeSummaries.Count == 0)
                return header;

            var ordered = volumeSummaries
                .OrderBy(kv => kv.Key, Comparer<string>.Create(ChapterParserHelper.CompareChapterId))
                .ToList();

            var interval = System.Math.Max(1, cfg.MilestoneAnchorInterval);
            var tailRecentCount = System.Math.Max(0, cfg.VolumeMilestoneTailRecentCount);
            var maxChars = cfg.VolumeMilestoneMaxChars;
            if (maxChars <= 0)
                maxChars = 12000;
            var perChapterMax = System.Math.Max(200, System.Math.Min(800, maxChars / 10));

            var selectedKeys = new HashSet<string>();
            if (ordered.Count > 0)
                selectedKeys.Add(ordered[0].Key);
            if (ordered.Count > 1)
                selectedKeys.Add(ordered[^1].Key);

            if (tailRecentCount > 0)
            {
                var take = System.Math.Min(tailRecentCount, ordered.Count);
                var skip = System.Math.Max(0, ordered.Count - take);
                foreach (var kv in ordered.Skip(skip))
                    selectedKeys.Add(kv.Key);
            }

            for (int i = interval; i < ordered.Count - 1; i += interval)
                selectedKeys.Add(ordered[i].Key);

            var selected = ordered
                .Where(kv => selectedKeys.Contains(kv.Key))
                .OrderBy(kv => kv.Key, Comparer<string>.Create(ChapterParserHelper.CompareChapterId))
                .ToList();

            var lines = new List<string>(capacity: selected.Count + 1) { header };
            foreach (var kv in selected)
            {
                var parsed = ChapterParserHelper.ParseChapterId(kv.Key);
                var chNum = parsed?.chapterNumber ?? 0;
                var prefix = chNum > 0 ? $"第{chNum}章" : kv.Key;
                var summary = kv.Value ?? string.Empty;
                if (summary.Length > perChapterMax)
                    summary = summary.Substring(0, perChapterMax) + "...";
                lines.Add($"[{prefix}] {summary}");
            }

            while (GetTotalCharCount(lines) > maxChars && lines.Count > 1)
                lines.RemoveAt(1);

            var merged = string.Join(Environment.NewLine, lines).Trim();
            if (merged.Length <= maxChars)
                return merged;
            return merged.Substring(0, maxChars);
        }

        private static int GetTotalCharCount(List<string> lines)
        {
            if (lines == null || lines.Count == 0) return 0;
            var total = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                total += lines[i]?.Length ?? 0;
                if (i < lines.Count - 1)
                    total += Environment.NewLine.Length;
            }
            return total;
        }

        private string GetMilestonesDir()
        {
            return Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides", "milestones");
        }

        private string GetMilestoneFilePath(int volumeNumber)
        {
            return Path.Combine(GetMilestonesDir(), $"vol{volumeNumber}.txt");
        }

        private async Task<string> LoadMilestoneAsync(int volumeNumber, LayeredContextConfigSnapshot cfg)
        {
            if (_cache.TryGetValue(volumeNumber, out var cached))
                return cached;

            var path = (await MilestoneCondenser.GetEffectiveFilePathAsync(volumeNumber).ConfigureAwait(false))
                       ?? GetMilestoneFilePath(volumeNumber);
            var epoch = Volatile.Read(ref _cacheEpoch);
            if (!File.Exists(path))
                return string.Empty;

            try
            {
                var text = await File.ReadAllTextAsync(path).ConfigureAwait(false);

                var maxChars = cfg.VolumeMilestoneMaxChars;
                if (maxChars > 0 && text.Length > maxChars)
                {
                    var firstLineEnd = text.IndexOf('\n');
                    if (firstLineEnd > 0 && firstLineEnd < 300 && text.StartsWith("=== ", StringComparison.Ordinal))
                    {
                        var header = text.Substring(0, firstLineEnd).TrimEnd('\r');
                        var body = text.Substring(firstLineEnd + 1);
                        var remaining = maxChars - header.Length - Environment.NewLine.Length;
                        if (remaining > 0)
                        {
                            if (body.Length > remaining)
                                body = body.Substring(body.Length - remaining);
                            text = header + Environment.NewLine + body;
                        }
                        else
                        {
                            text = header.Length <= maxChars ? header : header.Substring(0, maxChars);
                        }
                    }
                    else
                    {
                        text = text.Substring(text.Length - maxChars);
                    }

                    TM.App.Log($"[MilestoneStore] vol{volumeNumber}.txt 超过上限{maxChars}字，读取时已自动截断");
                }

                if (epoch == Volatile.Read(ref _cacheEpoch))
                    _cache[volumeNumber] = text;
                return text;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[MilestoneStore] 加载 vol{volumeNumber} 失败: {ex.Message}");
                return string.Empty;
            }
        }

        private async Task SaveMilestoneAsync(int volumeNumber, string milestone)
        {
            var dir = GetMilestonesDir();
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var path = GetMilestoneFilePath(volumeNumber);
            var tmpPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                await File.WriteAllTextAsync(tmpPath, milestone).ConfigureAwait(false);
                File.Move(tmpPath, path, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[MilestoneStore] 保存 vol{volumeNumber} 失败: {ex.Message}");
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                throw;
            }
        }

        #endregion
    }
}
