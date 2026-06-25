using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class LocationStateService
    {
        private readonly GuideManager _guideManager;
        private readonly IDesignElementLookupService _elementLookup;

        public LocationStateService(GuideManager guideManager, IDesignElementLookupService elementLookup)
        {
            _guideManager = guideManager;
            _elementLookup = elementLookup;
        }

        private const string BaseFileName = "location_state_guide.json";
        private static string VolumeFileName(string chapterId) =>
            GuideManager.GetVolumeFileName(BaseFileName,
                ChapterParserHelper.ParseChapterIdOrDefault(chapterId).volumeNumber);

        public async Task UpdateLocationStateAsync(string chapterId, LocationStateChange change)
        {
            var volFile = VolumeFileName(chapterId);
            var guide = await _guideManager.GetGuideAsync<LocationStateGuide>(volFile).ConfigureAwait(false);

            if (!guide.Locations.ContainsKey(change.LocationId))
            {
                var displayName = !string.IsNullOrWhiteSpace(change.LocationName)
                    ? change.LocationName
                    : await TryResolveLocationDisplayNameAsync(change.LocationId).ConfigureAwait(false) ?? change.LocationId;
                guide.Locations[change.LocationId] = new LocationStateEntry
                {
                    Name = displayName,
                    CurrentStatus = change.NewStatus
                };
                TM.App.Log($"[LocationState] 自动创建地点条目: {change.LocationId} (Name={displayName})");
            }

            var entry = guide.Locations[change.LocationId];
            if (!string.IsNullOrWhiteSpace(change.NewStatus))
                entry.CurrentStatus = change.NewStatus;
            entry.StateHistory.Add(new LocationStatePoint
            {
                Chapter = chapterId,
                Status = change.NewStatus,
                Event = change.Event,
                Importance = string.IsNullOrWhiteSpace(change.Importance) ? "normal" : change.Importance
            });

            _guideManager.MarkDirty(volFile);
            TM.App.Log($"[LocationState] 已更新 {change.LocationId} 在 {chapterId} 的状态: {change.NewStatus}");
        }

        private async Task<string?> TryResolveLocationDisplayNameAsync(string locationId)
        {
            try
            {
                return await _elementLookup.ResolveLocationNameAsync(locationId).ConfigureAwait(false);
            }
            catch (Exception ex) { TM.App.Log($"[LocationState] 读取地点名失败: {ex.Message}"); }
            return null;
        }

        public async Task RemoveChapterDataAsync(string chapterId)
        {
            var volFile = VolumeFileName(chapterId);
            var guide = await _guideManager.GetGuideAsync<LocationStateGuide>(volFile).ConfigureAwait(false);
            var modified = false;

            foreach (var (_, entry) in guide.Locations)
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
                TM.App.Log($"[LocationState] 已移除章节 {chapterId} 的状态记录并重算当前状态");
            }
        }

        public async Task<Dictionary<string, LocationStateEntry>> GetAllLocationStatesAsync()
        {
            var volNumbers = _guideManager.GetExistingVolumeNumbers(BaseFileName);
            var guides = await Task.WhenAll(volNumbers.TakeLast(5).Select(vol =>
                _guideManager.GetGuideAsync<LocationStateGuide>(GuideManager.GetVolumeFileName(BaseFileName, vol)))).ConfigureAwait(false);
            var merged = new Dictionary<string, LocationStateEntry>();
            foreach (var guide in guides)
                foreach (var (id, entry) in guide.Locations)
                    merged[id] = entry;
            return merged;
        }
    }
}
