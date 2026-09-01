using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class PledgeConstraintService
    {
        private readonly GuideManager _guideManager;

        public PledgeConstraintService(GuideManager guideManager)
        {
            _guideManager = guideManager;
        }

        private const string BaseFileName = "pledge_constraint_guide.json";
        private static string VolumeFileName(string chapterId) =>
            GuideManager.GetVolumeFileName(BaseFileName,
                ChapterParserHelper.ParseChapterIdOrDefault(chapterId).volumeNumber);

        public async Task UpdatePledgeAsync(string chapterId, PledgeConstraintChange change)
        {
            if (string.IsNullOrWhiteSpace(change.PledgeId) && string.IsNullOrWhiteSpace(change.PledgeName)) return;
            if (string.IsNullOrWhiteSpace(change.Action)) return;

            var volFile = VolumeFileName(chapterId);
            var guide = await _guideManager.GetGuideAsync<PledgeConstraintGuide>(volFile).ConfigureAwait(false);

            var systemId = FindExistingPledgeId(guide, change.PledgeId, change.PledgeName);

            if (systemId == null && string.Equals(change.Action, "create", StringComparison.OrdinalIgnoreCase))
            {
                systemId = ShortIdGenerator.IsLikelyId(change.PledgeId)
                    ? change.PledgeId
                    : ShortIdGenerator.New("PL");

                var displayName = !string.IsNullOrWhiteSpace(change.PledgeName)
                    ? change.PledgeName
                    : (!string.IsNullOrWhiteSpace(change.PledgeId) ? change.PledgeId : systemId);

                guide.Pledges[systemId] = new PledgeEntry
                {
                    Name = displayName,
                    Type = string.IsNullOrWhiteSpace(change.Type) ? "pledge" : change.Type,
                    CurrentStatus = "active",
                    PartyIds = change.PartyIds?.ToList() ?? new List<string>(),
                    Condition = change.Condition ?? string.Empty,
                    Consequence = change.Consequence ?? string.Empty
                };

                TM.App.Log($"[PledgeConstraint] 创建承诺/契约: {systemId} ({displayName})");
            }

            if (systemId == null) return;

            var entry = guide.Pledges[systemId];

            if (!string.IsNullOrWhiteSpace(change.PledgeName))
                entry.Name = change.PledgeName;

            var action = change.Action.ToLowerInvariant();
            switch (action)
            {
                case "fulfill":
                    entry.CurrentStatus = "fulfilled";
                    break;
                case "break":
                    entry.CurrentStatus = "broken";
                    break;
                case "update":
                    if (!string.IsNullOrWhiteSpace(change.Condition))
                        entry.Condition = change.Condition;
                    if (!string.IsNullOrWhiteSpace(change.Consequence))
                        entry.Consequence = change.Consequence;
                    if (change.PartyIds != null && change.PartyIds.Count > 0)
                    {
                        foreach (var pid in change.PartyIds)
                        {
                            if (!string.IsNullOrWhiteSpace(pid) && !entry.PartyIds.Contains(pid, StringComparer.OrdinalIgnoreCase))
                                entry.PartyIds.Add(pid);
                        }
                    }
                    break;
            }

            entry.History.Add(new PledgeConstraintPoint
            {
                Chapter = chapterId,
                Action = change.Action,
                KeyEvent = change.KeyEvent ?? string.Empty,
                Importance = string.IsNullOrWhiteSpace(change.Importance) ? "normal" : change.Importance
            });

            _guideManager.MarkDirty(volFile);
            TM.App.Log($"[PledgeConstraint] {systemId} 在 {chapterId} 执行动作: {change.Action}, 状态={entry.CurrentStatus}");
        }

        public async Task RefreshOverdueStatusAsync(string currentChapterId, int maxDanglingChapters)
        {
            if (maxDanglingChapters <= 0) return;
            var cp = ChapterParserHelper.ParseChapterId(currentChapterId);
            if (!cp.HasValue) return;

            const int AssumedChaptersPerVolume = 100;
            var volNumbers = _guideManager.GetExistingVolumeNumbers(BaseFileName);

            foreach (var vol in volNumbers)
            {
                var volFile = GuideManager.GetVolumeFileName(BaseFileName, vol);
                var guide = await _guideManager.GetGuideAsync<PledgeConstraintGuide>(volFile).ConfigureAwait(false);
                var modified = false;

                foreach (var (_, entry) in guide.Pledges)
                {
                    if (!string.Equals(entry.CurrentStatus, "active", StringComparison.OrdinalIgnoreCase))
                    {
                        if (entry.IsOverdue) { entry.IsOverdue = false; modified = true; }
                        continue;
                    }

                    var firstPoint = entry.History.FirstOrDefault();
                    if (firstPoint == null || string.IsNullOrWhiteSpace(firstPoint.Chapter)) continue;

                    var wp = ChapterParserHelper.ParseChapterId(firstPoint.Chapter);
                    if (!wp.HasValue) continue;

                    var distance = (cp.Value.volumeNumber - wp.Value.volumeNumber) * AssumedChaptersPerVolume
                                 + (cp.Value.chapterNumber - wp.Value.chapterNumber);
                    var shouldOverdue = distance >= maxDanglingChapters;

                    if (entry.IsOverdue != shouldOverdue)
                    {
                        entry.IsOverdue = shouldOverdue;
                        modified = true;
                    }
                }

                if (modified)
                {
                    _guideManager.MarkDirty(volFile);
                }
            }
            TM.App.Log($"[PledgeConstraint] {currentChapterId} 逾期状态刷新完成（阈值 {maxDanglingChapters} 章）");
        }

        public async Task RemoveChapterDataAsync(string chapterId)
        {
            var volFile = VolumeFileName(chapterId);
            var guide = await _guideManager.GetGuideAsync<PledgeConstraintGuide>(volFile).ConfigureAwait(false);
            var modified = false;
            var toRemove = new List<string>();

            foreach (var (id, entry) in guide.Pledges)
            {
                var removed = entry.History.RemoveAll(p =>
                    string.Equals(p.Chapter, chapterId, StringComparison.Ordinal));

                if (removed > 0)
                {
                    modified = true;
                    if (entry.History.Count == 0)
                        toRemove.Add(id);
                    else
                    {
                        var lastStatusAction = entry.History
                            .Select(h => h.Action?.ToLowerInvariant())
                            .LastOrDefault(a => a != "update");
                        entry.CurrentStatus = lastStatusAction switch
                        {
                            "fulfill" => "fulfilled",
                            "break" => "broken",
                            _ => "active"
                        };
                        if (!string.Equals(entry.CurrentStatus, "active", StringComparison.OrdinalIgnoreCase)
                            && entry.IsOverdue)
                        {
                            entry.IsOverdue = false;
                        }
                    }
                }
            }

            foreach (var id in toRemove)
                guide.Pledges.Remove(id);

            if (modified)
            {
                _guideManager.MarkDirty(volFile);
                TM.App.Log($"[PledgeConstraint] 已移除章节 {chapterId} 的承诺/契约记录");
            }
        }

        public async Task<List<PledgeStateSnapshot>> ExtractSnapshotAsync(string chapterId)
        {
            var result = new List<PledgeStateSnapshot>();

            try
            {
                var volNumbers = _guideManager.GetExistingVolumeNumbers(BaseFileName);
                if (volNumbers.Count == 0) return result;

                foreach (var vol in volNumbers)
                {
                    var guide = await _guideManager.GetGuideAsync<PledgeConstraintGuide>(
                        GuideManager.GetVolumeFileName(BaseFileName, vol)).ConfigureAwait(false);

                    foreach (var (id, entry) in guide.Pledges)
                    {
                        if (!string.Equals(entry.CurrentStatus, "active", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (result.Any(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        result.Add(new PledgeStateSnapshot
                        {
                            Id = id,
                            Name = entry.Name,
                            Type = entry.Type,
                            Status = entry.CurrentStatus,
                            PartyIds = string.Join(",", entry.PartyIds.Take(20)),
                            Condition = entry.Condition,
                            Consequence = entry.Consequence,
                            ChapterId = entry.History.LastOrDefault()?.Chapter ?? string.Empty,
                            IsOverdue = entry.IsOverdue
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PledgeConstraint] 快照提取失败: {ex.Message}");
            }

            return result;
        }

        private static string? FindExistingPledgeId(PledgeConstraintGuide guide, string aiPledgeId, string? aiPledgeName)
        {
            if (guide.Pledges.ContainsKey(aiPledgeId))
                return aiPledgeId;

            foreach (var (id, entry) in guide.Pledges)
            {
                if (string.Equals(entry.Name, aiPledgeId, StringComparison.OrdinalIgnoreCase))
                    return id;
                if (!string.IsNullOrWhiteSpace(aiPledgeName) &&
                    string.Equals(entry.Name, aiPledgeName, StringComparison.OrdinalIgnoreCase))
                    return id;
            }

            return null;
        }
    }
}
