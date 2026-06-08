using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class ItemStateService
    {
        private readonly GuideManager _guideManager;

        public ItemStateService(GuideManager guideManager)
        {
            _guideManager = guideManager;
        }

        private const string BaseFileName = "item_state_guide.json";
        private static string VolumeFileName(string chapterId) =>
            GuideManager.GetVolumeFileName(BaseFileName,
                ChapterParserHelper.ParseChapterIdOrDefault(chapterId).volumeNumber);

        public async Task UpdateItemStateAsync(string chapterId, ItemTransferChange change)
        {
            if (string.IsNullOrWhiteSpace(change.ItemId)) return;

            var volFile = VolumeFileName(chapterId);
            var guide = await _guideManager.GetGuideAsync<ItemStateGuide>(volFile).ConfigureAwait(false);

            var systemId = FindExistingItemId(guide, change.ItemId, change.ItemName);

            if (systemId == null)
            {
                systemId = ShortIdGenerator.IsLikelyId(change.ItemId)
                    ? change.ItemId
                    : ShortIdGenerator.New("I");
                var displayName = !string.IsNullOrWhiteSpace(change.ItemName) ? change.ItemName : change.ItemId;
                if (!guide.Items.ContainsKey(systemId))
                {
                    guide.Items[systemId] = new ItemStateEntry
                    {
                        Name = displayName,
                        CurrentHolder = change.ToHolder,
                        CurrentStatus = string.IsNullOrWhiteSpace(change.NewStatus) ? "active" : change.NewStatus
                    };
                    TM.App.Log($"[ItemState] 自动创建物品条目: {systemId} ({displayName})");
                }
            }

            var entry = guide.Items[systemId!];

            if (!string.IsNullOrWhiteSpace(change.ItemName))
                entry.Name = change.ItemName;

            if (!string.IsNullOrWhiteSpace(change.ToHolder))
                entry.CurrentHolder = change.ToHolder;
            else if (!string.IsNullOrWhiteSpace(change.NewStatus)
                     && !string.Equals(change.NewStatus, "active", StringComparison.OrdinalIgnoreCase))
                entry.CurrentHolder = string.Empty;

            if (!string.IsNullOrWhiteSpace(change.NewStatus))
                entry.CurrentStatus = change.NewStatus;

            entry.StateHistory.Add(new ItemStatePoint
            {
                Chapter = chapterId,
                Holder = !string.IsNullOrWhiteSpace(change.ToHolder) ? change.ToHolder : entry.CurrentHolder,
                Status = !string.IsNullOrWhiteSpace(change.NewStatus) ? change.NewStatus : entry.CurrentStatus,
                Event = change.Event,
                Importance = string.IsNullOrWhiteSpace(change.Importance) ? "normal" : change.Importance,
                CausedBy = change.CausedBy ?? string.Empty
            });

            _guideManager.MarkDirty(volFile);
            var fromText = string.IsNullOrWhiteSpace(change.FromHolder) ? "(未指定)" : change.FromHolder;
            var toText = string.IsNullOrWhiteSpace(change.ToHolder) ? "(未指定)" : change.ToHolder;
            var statusText = !string.IsNullOrWhiteSpace(change.NewStatus) ? change.NewStatus
                            : !string.IsNullOrWhiteSpace(entry.CurrentStatus) ? entry.CurrentStatus
                            : "(未知)";
            var holderSegment = (fromText == "(未指定)" && toText == "(未指定)")
                ? string.Empty
                : $" 持有者 {fromText} → {toText},";
            TM.App.Log($"[ItemState] 已更新 {systemId} 在 {chapterId}:{holderSegment} 状态={statusText}");
        }

        private static string? FindExistingItemId(ItemStateGuide guide, string aiItemId, string? aiItemName)
        {
            if (guide.Items.ContainsKey(aiItemId))
                return aiItemId;

            foreach (var (id, entry) in guide.Items)
            {
                if (string.Equals(entry.Name, aiItemId, StringComparison.OrdinalIgnoreCase))
                    return id;
                if (!string.IsNullOrWhiteSpace(aiItemName) &&
                    string.Equals(entry.Name, aiItemName, StringComparison.OrdinalIgnoreCase))
                    return id;
            }

            return null;
        }

        public async Task RemoveChapterDataAsync(string chapterId)
        {
            var volFile = VolumeFileName(chapterId);
            var guide = await _guideManager.GetGuideAsync<ItemStateGuide>(volFile).ConfigureAwait(false);
            var modified = false;

            foreach (var (_, entry) in guide.Items)
            {
                var removed = entry.StateHistory.RemoveAll(s =>
                    string.Equals(s.Chapter, chapterId, StringComparison.Ordinal));
                if (removed > 0)
                {
                    modified = true;

                    entry.StateHistory.Sort((a, b) => ChapterParserHelper.CompareChapterId(a.Chapter, b.Chapter));
                    var last = entry.StateHistory.LastOrDefault();
                    if (last != null)
                    {
                        entry.CurrentHolder = last.Holder;
                        entry.CurrentStatus = last.Status;
                    }
                    else
                    {
                        entry.CurrentHolder = string.Empty;
                        entry.CurrentStatus = "unknown";
                    }
                }
            }

            if (modified)
            {
                _guideManager.MarkDirty(volFile);
                TM.App.Log($"[ItemState] 已移除章节 {chapterId} 的物品状态记录并重算当前状态");
            }
        }

        public async Task<Dictionary<string, ItemStateEntry>> GetAllItemStatesAsync()
        {
            var volNumbers = _guideManager.GetExistingVolumeNumbers(BaseFileName);
            var guides = await Task.WhenAll(volNumbers.TakeLast(5).Select(vol =>
                _guideManager.GetGuideAsync<ItemStateGuide>(GuideManager.GetVolumeFileName(BaseFileName, vol)))).ConfigureAwait(false);
            var merged = new Dictionary<string, ItemStateEntry>();
            foreach (var guide in guides)
                foreach (var (id, entry) in guide.Items)
                    merged[id] = entry;
            return merged;
        }
    }
}
