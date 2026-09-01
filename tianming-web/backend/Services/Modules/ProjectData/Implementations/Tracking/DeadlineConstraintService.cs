using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class DeadlineConstraintService
    {
        private readonly GuideManager _guideManager;

        public DeadlineConstraintService(GuideManager guideManager)
        {
            _guideManager = guideManager;
        }

        private const string BaseFileName = "deadline_constraint_guide.json";
        private static string VolumeFileName(string chapterId) =>
            GuideManager.GetVolumeFileName(BaseFileName,
                ChapterParserHelper.ParseChapterIdOrDefault(chapterId).volumeNumber);

        public async Task UpdateDeadlineAsync(string chapterId, DeadlineConstraintChange change)
        {
            if (string.IsNullOrWhiteSpace(change.DeadlineId) && string.IsNullOrWhiteSpace(change.DeadlineName)) return;
            if (string.IsNullOrWhiteSpace(change.Action)) return;

            var volFile = VolumeFileName(chapterId);
            var guide = await _guideManager.GetGuideAsync<DeadlineConstraintGuide>(volFile).ConfigureAwait(false);

            var systemId = FindExistingDeadlineId(guide, change.DeadlineId, change.DeadlineName);

            if (systemId == null && string.Equals(change.Action, "create", StringComparison.OrdinalIgnoreCase))
            {
                systemId = ShortIdGenerator.IsLikelyId(change.DeadlineId)
                    ? change.DeadlineId
                    : ShortIdGenerator.New("DL");

                var displayName = !string.IsNullOrWhiteSpace(change.DeadlineName)
                    ? change.DeadlineName
                    : (!string.IsNullOrWhiteSpace(change.DeadlineId) ? change.DeadlineId : systemId);

                guide.Deadlines[systemId] = new DeadlineEntry
                {
                    Name = displayName,
                    Type = string.IsNullOrWhiteSpace(change.Type) ? "countdown" : change.Type,
                    CurrentStatus = "active",
                    Deadline = change.Deadline ?? string.Empty,
                    TriggerCondition = change.TriggerCondition ?? string.Empty,
                    Consequence = change.Consequence ?? string.Empty,
                    PartyIds = change.PartyIds?.ToList() ?? new List<string>()
                };

                TM.App.Log($"[DeadlineConstraint] 创建倒计时/时限: {systemId} ({displayName})");
            }

            if (systemId == null) return;

            var entry = guide.Deadlines[systemId];

            if (!string.IsNullOrWhiteSpace(change.DeadlineName))
                entry.Name = change.DeadlineName;

            var action = change.Action.ToLowerInvariant();
            switch (action)
            {
                case "trigger":
                    entry.CurrentStatus = "triggered";
                    break;
                case "expire":
                    entry.CurrentStatus = "expired";
                    break;
                case "cancel":
                    entry.CurrentStatus = "cancelled";
                    break;
                case "update":
                    if (!string.IsNullOrWhiteSpace(change.Deadline))
                        entry.Deadline = change.Deadline;
                    if (!string.IsNullOrWhiteSpace(change.TriggerCondition))
                        entry.TriggerCondition = change.TriggerCondition;
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

            entry.History.Add(new DeadlineConstraintPoint
            {
                Chapter = chapterId,
                Action = change.Action,
                KeyEvent = change.KeyEvent ?? string.Empty,
                Importance = string.IsNullOrWhiteSpace(change.Importance) ? "normal" : change.Importance
            });

            _guideManager.MarkDirty(volFile);
            TM.App.Log($"[DeadlineConstraint] {systemId} 在 {chapterId} 执行动作: {change.Action}, 状态={entry.CurrentStatus}");
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
                var guide = await _guideManager.GetGuideAsync<DeadlineConstraintGuide>(volFile).ConfigureAwait(false);
                var modified = false;

                foreach (var (_, entry) in guide.Deadlines)
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
            TM.App.Log($"[DeadlineConstraint] {currentChapterId} 逾期状态刷新完成（阈值 {maxDanglingChapters} 章）");
        }

        public async Task RemoveChapterDataAsync(string chapterId)
        {
            var volFile = VolumeFileName(chapterId);
            var guide = await _guideManager.GetGuideAsync<DeadlineConstraintGuide>(volFile).ConfigureAwait(false);
            var modified = false;
            var toRemove = new List<string>();

            foreach (var (id, entry) in guide.Deadlines)
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
                            "trigger" => "triggered",
                            "expire" => "expired",
                            "cancel" => "cancelled",
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
                guide.Deadlines.Remove(id);

            if (modified)
            {
                _guideManager.MarkDirty(volFile);
                TM.App.Log($"[DeadlineConstraint] 已移除章节 {chapterId} 的倒计时记录");
            }
        }

        public async Task<List<DeadlineStateSnapshot>> ExtractSnapshotAsync(string chapterId)
        {
            var result = new List<DeadlineStateSnapshot>();

            try
            {
                var volNumbers = _guideManager.GetExistingVolumeNumbers(BaseFileName);
                if (volNumbers.Count == 0) return result;

                foreach (var vol in volNumbers)
                {
                    var guide = await _guideManager.GetGuideAsync<DeadlineConstraintGuide>(
                        GuideManager.GetVolumeFileName(BaseFileName, vol)).ConfigureAwait(false);

                    foreach (var (id, entry) in guide.Deadlines)
                    {
                        if (!string.Equals(entry.CurrentStatus, "active", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (result.Any(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        result.Add(new DeadlineStateSnapshot
                        {
                            Id = id,
                            Name = entry.Name,
                            Type = entry.Type,
                            Status = entry.CurrentStatus,
                            Deadline = entry.Deadline,
                            TriggerCondition = entry.TriggerCondition,
                            Consequence = entry.Consequence,
                            PartyIds = string.Join(",", entry.PartyIds.Take(20)),
                            ChapterId = entry.History.LastOrDefault()?.Chapter ?? string.Empty,
                            IsOverdue = entry.IsOverdue
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DeadlineConstraint] 快照提取失败: {ex.Message}");
            }

            return result;
        }

        private static string? FindExistingDeadlineId(DeadlineConstraintGuide guide, string aiDeadlineId, string? aiDeadlineName)
        {
            if (guide.Deadlines.ContainsKey(aiDeadlineId))
                return aiDeadlineId;

            foreach (var (id, entry) in guide.Deadlines)
            {
                if (string.Equals(entry.Name, aiDeadlineId, StringComparison.OrdinalIgnoreCase))
                    return id;
                if (!string.IsNullOrWhiteSpace(aiDeadlineName) &&
                    string.Equals(entry.Name, aiDeadlineName, StringComparison.OrdinalIgnoreCase))
                    return id;
            }

            return null;
        }
    }
}
