using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class FactionStateService
    {
        private readonly GuideManager _guideManager;
        private readonly IDesignElementLookupService _elementLookup;

        public FactionStateService(GuideManager guideManager, IDesignElementLookupService elementLookup)
        {
            _guideManager = guideManager;
            _elementLookup = elementLookup;
        }

        private const string BaseFileName = "faction_state_guide.json";
        private static string VolumeFileName(string chapterId) =>
            GuideManager.GetVolumeFileName(BaseFileName,
                ChapterParserHelper.ParseChapterIdOrDefault(chapterId).volumeNumber);

        public async Task UpdateFactionStateAsync(string chapterId, FactionStateChange change)
        {
            var volFile = VolumeFileName(chapterId);
            var guide = await _guideManager.GetGuideAsync<FactionStateGuide>(volFile).ConfigureAwait(false);

            if (!guide.Factions.ContainsKey(change.FactionId))
            {
                var displayName = await TryResolveFactionDisplayNameAsync(change.FactionId).ConfigureAwait(false) ?? change.FactionId;
                guide.Factions[change.FactionId] = new FactionStateEntry
                {
                    Name = displayName,
                    CurrentStatus = change.NewStatus
                };
                TM.App.Log($"[FactionState] 自动创建势力条目: {change.FactionId} (Name={displayName})");
            }

            var entry = guide.Factions[change.FactionId];
            if (!string.IsNullOrWhiteSpace(change.NewStatus))
                entry.CurrentStatus = change.NewStatus;
            entry.StateHistory.Add(new FactionStatePoint
            {
                Chapter = chapterId,
                Status = change.NewStatus,
                Event = change.Event,
                Importance = string.IsNullOrWhiteSpace(change.Importance) ? "normal" : change.Importance,
                CausedBy = change.CausedBy ?? string.Empty
            });

            _guideManager.MarkDirty(volFile);
            TM.App.Log($"[FactionState] 已更新 {change.FactionId} 在 {chapterId} 的状态: {change.NewStatus}");
        }

        private async Task<string?> TryResolveFactionDisplayNameAsync(string factionId)
        {
            try
            {
                return await _elementLookup.ResolveFactionNameAsync(factionId).ConfigureAwait(false);
            }
            catch (Exception ex) { TM.App.Log($"[FactionState] 读取势力名失败: {ex.Message}"); }
            return null;
        }

        public async Task RemoveChapterDataAsync(string chapterId)
        {
            var volFile = VolumeFileName(chapterId);
            var guide = await _guideManager.GetGuideAsync<FactionStateGuide>(volFile).ConfigureAwait(false);
            var modified = false;

            foreach (var (_, entry) in guide.Factions)
            {
                var removed = entry.StateHistory.RemoveAll(s =>
                    string.Equals(s.Chapter, chapterId, StringComparison.Ordinal));
                if (removed > 0)
                {
                    modified = true;

                    entry.StateHistory.Sort((a, b) => ChapterParserHelper.CompareChapterId(a.Chapter, b.Chapter));
                    var lastStatus = entry.StateHistory
                        .LastOrDefault(s => !string.IsNullOrWhiteSpace(s.Status))
                        ?.Status;
                    entry.CurrentStatus = string.IsNullOrWhiteSpace(lastStatus) ? "unknown" : lastStatus;
                }
            }

            if (modified)
            {
                _guideManager.MarkDirty(volFile);
                TM.App.Log($"[FactionState] 已移除章节 {chapterId} 的状态记录并重算当前状态");
            }
        }

        public async Task<Dictionary<string, FactionStateEntry>> GetAllFactionStatesAsync()
        {
            var volNumbers = _guideManager.GetExistingVolumeNumbers(BaseFileName);
            var guides = await Task.WhenAll(volNumbers.TakeLast(5).Select(vol =>
                _guideManager.GetGuideAsync<FactionStateGuide>(GuideManager.GetVolumeFileName(BaseFileName, vol)))).ConfigureAwait(false);
            var merged = new Dictionary<string, FactionStateEntry>();
            foreach (var guide in guides)
                foreach (var (id, entry) in guide.Factions)
                    merged[id] = entry;
            return merged;
        }
    }
}
