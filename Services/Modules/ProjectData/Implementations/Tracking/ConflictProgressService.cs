using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class ConflictProgressService
    {
        private readonly GuideManager _guideManager;

        #region 构造函数

        public ConflictProgressService(GuideManager guideManager)
        {
            _guideManager = guideManager;
        }

        #endregion

        private const string BaseFileName = "conflict_progress_guide.json";
        private static string VolumeFileName(string chapterId) =>
            GuideManager.GetVolumeFileName(BaseFileName,
                ChapterParserHelper.ParseChapterIdOrDefault(chapterId).volumeNumber);

        public async Task<ConflictProgressEntry?> GetConflictProgressAsync(string conflictId)
        {
            var volNumbers = _guideManager.GetExistingVolumeNumbers(BaseFileName);
            foreach (var vol in volNumbers.OrderByDescending(v => v))
            {
                var guide = await _guideManager.GetGuideAsync<ConflictProgressGuide>(
                    GuideManager.GetVolumeFileName(BaseFileName, vol)).ConfigureAwait(false);
                if (guide.Conflicts.TryGetValue(conflictId, out var entry))
                    return entry;
            }
            return null;
        }

        public async Task UpdateConflictProgressAsync(string chapterId, ConflictProgressChange change)
        {
            var volFile = VolumeFileName(chapterId);
            var guide = await _guideManager.GetGuideAsync<ConflictProgressGuide>(volFile).ConfigureAwait(false);

            if (!guide.Conflicts.ContainsKey(change.ConflictId))
            {
                var displayName = await TryResolveConflictDisplayNameAsync(change.ConflictId).ConfigureAwait(false) ?? change.ConflictId;
                guide.Conflicts[change.ConflictId] = new ConflictProgressEntry
                {
                    Name = displayName,
                    Status = "pending"
                };
                TM.App.Log($"[ConflictProgress] 自动注册新冲突: {change.ConflictId} (Name={displayName})");
            }

            var conflictEntry = guide.Conflicts[change.ConflictId];
            var oldStatus = conflictEntry.Status;

            if (!string.IsNullOrWhiteSpace(change.NewStatus))
                conflictEntry.Status = change.NewStatus;

            conflictEntry.ProgressPoints.Add(new ConflictProgressPoint
            {
                Chapter = chapterId,
                Event = change.Event,
                Status = change.NewStatus,
                Description = $"{oldStatus} → {change.NewStatus}",
                Importance = string.IsNullOrWhiteSpace(change.Importance) ? "normal" : change.Importance,
                CausedBy = change.CausedBy ?? string.Empty
            });

            if (!conflictEntry.InvolvedChapters.Contains(chapterId))
            {
                conflictEntry.InvolvedChapters.Add(chapterId);
            }

            _guideManager.MarkDirty(volFile);
            TM.App.Log($"[ConflictProgress] 已更新 {change.ConflictId} 状态为 {change.NewStatus}");
        }

        public async Task RemoveChapterDataAsync(string chapterId)
        {
            var volFile = VolumeFileName(chapterId);
            var guide = await _guideManager.GetGuideAsync<ConflictProgressGuide>(volFile).ConfigureAwait(false);
            var modified = false;

            foreach (var (_, entry) in guide.Conflicts)
            {
                var removed = entry.ProgressPoints.RemoveAll(p =>
                    string.Equals(p.Chapter, chapterId, StringComparison.Ordinal));
                if (removed > 0) modified = true;

                if (entry.InvolvedChapters.Remove(chapterId))
                    modified = true;

                if (removed > 0)
                {
                    entry.ProgressPoints.Sort((a, b) => ChapterParserHelper.CompareChapterId(a.Chapter, b.Chapter));
                    var lastStatus = entry.ProgressPoints
                        .LastOrDefault(p => !string.IsNullOrWhiteSpace(p.Status))
                        ?.Status;

                    var newStatus = string.IsNullOrWhiteSpace(lastStatus) ? "pending" : lastStatus;
                    if (!string.Equals(entry.Status, newStatus, StringComparison.OrdinalIgnoreCase))
                    {
                        entry.Status = newStatus;
                        modified = true;
                    }

                    var rebuilt = entry.ProgressPoints
                        .Select(p => p.Chapter)
                        .Where(c => !string.IsNullOrWhiteSpace(c))
                        .Distinct()
                        .ToList();
                    rebuilt.Sort(ChapterParserHelper.CompareChapterId);

                    if (entry.InvolvedChapters.Count != rebuilt.Count || !entry.InvolvedChapters.SequenceEqual(rebuilt))
                    {
                        entry.InvolvedChapters = rebuilt;
                        modified = true;
                    }
                }
            }

            if (modified)
            {
                _guideManager.MarkDirty(volFile);
                TM.App.Log($"[ConflictProgress] 已移除章节 {chapterId} 的进度记录");
            }
        }

        private static async Task<string?> TryResolveConflictDisplayNameAsync(string conflictId)
        {
            try
            {
                var elementsPath = Path.Combine(
                    StoragePathHelper.GetProjectConfigPath(), "Design", "elements.json");
                if (!File.Exists(elementsPath)) return null;

                var json = await File.ReadAllTextAsync(elementsPath).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("data", out var data)) return null;
                if (!data.TryGetProperty("plotrules", out var plotModule)) return null;
                if (!plotModule.TryGetProperty("plot_rules", out var plotRules)) return null;

                foreach (var item in plotRules.EnumerateArray())
                {
                    var id = item.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;
                    if (string.Equals(id, conflictId, StringComparison.OrdinalIgnoreCase))
                    {
                        var name = item.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                        return string.IsNullOrWhiteSpace(name) ? null : name;
                    }
                }
            }
            catch (Exception ex) { TM.App.Log($"[ConflictProgress] 读取冲突名失败: {ex.Message}"); }
            return null;
        }

        public async Task<List<ConflictProgressEntry>> GetActiveConflictsAsync()
        {
            var volNumbers = _guideManager.GetExistingVolumeNumbers(BaseFileName);
            var guides = await Task.WhenAll(volNumbers.TakeLast(5).Select(vol =>
                _guideManager.GetGuideAsync<ConflictProgressGuide>(GuideManager.GetVolumeFileName(BaseFileName, vol)))).ConfigureAwait(false);
            var merged = new System.Collections.Generic.Dictionary<string, ConflictProgressEntry>();
            foreach (var guide in guides)
                foreach (var (id, entry) in guide.Conflicts)
                    merged[id] = entry;
            return merged.Values.Where(c =>
                string.Equals(c.Status, "active", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Status, "climax", StringComparison.OrdinalIgnoreCase)).ToList();
        }

    }
}
