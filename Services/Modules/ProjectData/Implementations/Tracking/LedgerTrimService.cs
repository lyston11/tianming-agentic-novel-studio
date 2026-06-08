using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class LedgerTrimService
    {
        private readonly GuideManager _guideManager;

        private static readonly System.Collections.Generic.HashSet<string> PlotPointEscalationKeywords =
            new(System.StringComparer.OrdinalIgnoreCase)
            {
                "转折", "揭示", "决战", "牺牲", "死亡", "背叛", "觉醒", "终章",
                "最终", "真相", "关键", "逆转", "崩溃", "覆灭", "重生", "突破",
                "心理", "内心", "心境", "转变", "暗线", "隐线", "伏线", "暗示",
                "预兆", "推进", "心魔", "执念", "动摇", "崩塌", "觉察", "发现",
                "危机", "危急", "绝境", "抉择", "选择", "誓言", "羁绊", "信念",
                "契机", "机缘", "顿悟", "历劫", "渡劫", "天劫", "重逢", "告别",
                "承诺", "复仇", "复活", "陨落", "继承", "传承", "秘密", "线索"
            };

        private static bool HasEscalationKeyword(PlotPointEntry p)
        {
            if (string.IsNullOrEmpty(p.Context)) return false;
            foreach (var kw in PlotPointEscalationKeywords)
                if (p.Context.Contains(kw, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static bool IsCritical(string? importance) =>
            string.Equals(importance, "critical", System.StringComparison.OrdinalIgnoreCase);

        private static bool IsImportant(string? importance) =>
            string.Equals(importance, "important", System.StringComparison.OrdinalIgnoreCase);

        public LedgerTrimService(GuideManager guideManager)
        {
            _guideManager = guideManager;
        }

        public class TrimResult
        {
            public int CharactersTrimmed { get; set; }
            public int TotalStatesTrimmed { get; set; }
            public int ConflictsTrimmed { get; set; }
            public int TotalProgressTrimmed { get; set; }
            public int PlotPointsTrimmed { get; set; }
            public int LocationsTrimmed { get; set; }
            public int TotalLocationStatesTrimmed { get; set; }
            public int FactionsTrimmed { get; set; }
            public int TotalFactionStatesTrimmed { get; set; }
            public int TimelineEntriesTrimmed { get; set; }
            public int CharactersMovementsTrimmed { get; set; }
            public int TotalMovementsTrimmed { get; set; }
            public int ItemsTrimmed { get; set; }
            public int TotalItemStatesTrimmed { get; set; }
            public int ForeshadowingsResolved { get; set; }
            public int SecretsTrimmed { get; set; }
            public int TotalSecretRevealsTrimmed { get; set; }
            public int PledgesTrimmed { get; set; }
            public int TotalPledgeHistoryTrimmed { get; set; }
            public int DeadlinesTrimmed { get; set; }
            public int TotalDeadlineHistoryTrimmed { get; set; }
        }

        public async Task<TrimResult> TrimAllAsync()
        {
            var cfg = LayeredContextConfig.TakeSnapshot();
            var result = new TrimResult();

            await Task.WhenAll(
                TrimCharacterStateHistoryAsync(result, cfg),
                TrimConflictProgressAsync(result, cfg),
                TrimPlotPointsAsync(result, cfg),
                TrimLocationStateHistoryAsync(result, cfg),
                TrimFactionStateHistoryAsync(result, cfg),
                TrimTimelineAsync(result, cfg),
                TrimCharacterMovementsAsync(result, cfg),
                TrimItemStateHistoryAsync(result, cfg),
                TrimForeshadowingsAsync(result),
                TrimSecretRevealHistoryAsync(result, cfg),
                TrimPledgeConstraintHistoryAsync(result, cfg),
                TrimDeadlineConstraintHistoryAsync(result, cfg)).ConfigureAwait(false);

            if (result.TotalStatesTrimmed > 0 ||
                result.TotalProgressTrimmed > 0 ||
                result.PlotPointsTrimmed > 0 ||
                result.TotalLocationStatesTrimmed > 0 ||
                result.TotalFactionStatesTrimmed > 0 ||
                result.TimelineEntriesTrimmed > 0 ||
                result.TotalMovementsTrimmed > 0 ||
                result.TotalItemStatesTrimmed > 0 ||
                result.ForeshadowingsResolved > 0 ||
                result.TotalSecretRevealsTrimmed > 0 ||
                result.TotalPledgeHistoryTrimmed > 0 ||
                result.TotalDeadlineHistoryTrimmed > 0)
            {
                await _guideManager.FlushAllAsync().ConfigureAwait(false);
                TM.App.Log($"[LedgerTrim] 裁剪完成: 角色{result.CharactersTrimmed}个({result.TotalStatesTrimmed}条状态), " +
                           $"冲突{result.ConflictsTrimmed}个({result.TotalProgressTrimmed}条进度), " +
                           $"情节点{result.PlotPointsTrimmed}条, " +
                           $"地点{result.LocationsTrimmed}个({result.TotalLocationStatesTrimmed}条状态), " +
                           $"势力{result.FactionsTrimmed}个({result.TotalFactionStatesTrimmed}条状态), " +
                           $"时间线{result.TimelineEntriesTrimmed}条, " +
                           $"移动{result.CharactersMovementsTrimmed}人({result.TotalMovementsTrimmed}条), " +
                           $"物品{result.ItemsTrimmed}个({result.TotalItemStatesTrimmed}条状态), " +
                           $"伏笔清理{result.ForeshadowingsResolved}条, " +
                           $"秘密{result.SecretsTrimmed}个({result.TotalSecretRevealsTrimmed}条揭示记录), " +
                           $"承诺{result.PledgesTrimmed}个({result.TotalPledgeHistoryTrimmed}条历史), " +
                           $"倒计时{result.DeadlinesTrimmed}个({result.TotalDeadlineHistoryTrimmed}条历史)");
            }
            else
            {
                TM.App.Log("[LedgerTrim] skip: within limits");
            }

            return result;
        }

        private async Task TrimCharacterStateHistoryAsync(TrimResult result, LayeredContextConfigSnapshot cfg)
        {
            var _volFiles = GetVolFiles("character_state_guide.json");
            var _guides = await Task.WhenAll(_volFiles.Select(f => _guideManager.GetGuideAsync<CharacterStateGuide>(f))).ConfigureAwait(false);
            for (int _gi = 0; _gi < _volFiles.Count; _gi++)
            {
                var csFile = _volFiles[_gi];
                var guide = _guides[_gi];
                var csPrev = result.TotalStatesTrimmed;
                foreach (var (characterId, entry) in guide.Characters)
                {
                    var history = entry.StateHistory;
                    if (history.Count <= cfg.LedgerCharacterStateKeepRecent)
                        continue;

                    var trimCount = history.Count - cfg.LedgerCharacterStateKeepRecent;
                    var trimZone = history.Take(trimCount).ToList();
                    var keepZone = history.Skip(trimCount).ToList();

                    var criticalEntries = trimZone.Where(s => IsCritical(s.Importance)).ToList();
                    if (criticalEntries.Count > cfg.LedgerMaxCriticalPerEntity)
                        criticalEntries = criticalEntries.TakeLast(cfg.LedgerMaxCriticalPerEntity).ToList();

                    var importantEntries = trimZone.Where(s => IsImportant(s.Importance)).TakeLast(cfg.LedgerImportantKeepRecent).ToList();
                    var trueNormalEntries = trimZone.Where(s => !IsCritical(s.Importance) && !IsImportant(s.Importance)).ToList();

                    var normalAnchors = BuildNormalAnchors(trueNormalEntries, cfg);
                    var actualTrimmed = System.Math.Max(0, trueNormalEntries.Count - normalAnchors.Count);

                    var rebuilt = new List<CharacterState>(criticalEntries.Count + importantEntries.Count + normalAnchors.Count + keepZone.Count);
                    rebuilt.AddRange(criticalEntries);
                    rebuilt.AddRange(importantEntries);
                    rebuilt.AddRange(normalAnchors);
                    rebuilt.AddRange(keepZone);

                    entry.StateHistory = rebuilt;
                    result.CharactersTrimmed++;
                    result.TotalStatesTrimmed += System.Math.Max(0, actualTrimmed);
                }

                if (result.TotalStatesTrimmed > csPrev) _guideManager.MarkDirty(csFile);
            }
        }

        private static System.Collections.Generic.List<CharacterState> BuildNormalAnchors(
            System.Collections.Generic.List<CharacterState> normals, LayeredContextConfigSnapshot cfg)
        {
            var anchors = new System.Collections.Generic.List<CharacterState>();
            if (normals.Count == 0) return anchors;

            var interval = cfg.LedgerNormalSampleInterval;
            var first = normals.First();
            first.KeyEvent = $"[起始锚点] {first.KeyEvent}（归档{normals.Count}条）";
            anchors.Add(first);

            for (int i = interval; i < normals.Count - 1; i += interval)
            {
                var sample = normals[i];
                sample.KeyEvent = $"[中间快照@{i}] {sample.KeyEvent}";
                anchors.Add(sample);
            }

            if (normals.Count > 1)
            {
                var last = normals.Last();
                last.KeyEvent = $"[截止快照] {last.KeyEvent}";
                anchors.Add(last);
            }

            return anchors;
        }

        private static System.Collections.Generic.List<PlotPointEntry> BuildPlotPointAnchors(
            System.Collections.Generic.List<PlotPointEntry> normals, LayeredContextConfigSnapshot cfg)
        {
            var anchors = new System.Collections.Generic.List<PlotPointEntry>();
            if (normals.Count == 0) return anchors;

            var interval = cfg.LedgerNormalSampleInterval;
            var first = normals.First();
            first.Context = $"[起始锚点] {first.Context}（归档{normals.Count}条）";
            anchors.Add(first);

            for (int i = interval; i < normals.Count - 1; i += interval)
            {
                var sample = normals[i];
                sample.Context = $"[中间快照@{i}] {sample.Context}";
                anchors.Add(sample);
            }

            if (normals.Count > 1)
            {
                var last = normals.Last();
                last.Context = $"[截止快照] {last.Context}";
                anchors.Add(last);
            }

            return anchors;
        }

        private async Task TrimConflictProgressAsync(TrimResult result, LayeredContextConfigSnapshot cfg)
        {
            var _volFiles = GetVolFiles("conflict_progress_guide.json");
            var _guides = await Task.WhenAll(_volFiles.Select(f => _guideManager.GetGuideAsync<ConflictProgressGuide>(f))).ConfigureAwait(false);
            for (int _gi = 0; _gi < _volFiles.Count; _gi++)
            {
                var cpFile = _volFiles[_gi];
                var guide = _guides[_gi];
                var cpPrev = result.TotalProgressTrimmed;
                foreach (var (conflictId, entry) in guide.Conflicts)
                {
                    var points = entry.ProgressPoints;
                    if (points.Count <= cfg.LedgerConflictProgressKeepRecent)
                        continue;

                    var trimCount = points.Count - cfg.LedgerConflictProgressKeepRecent;
                    var trimZone = points.Take(trimCount).ToList();
                    var keepZone = points.Skip(trimCount).ToList();

                    var criticalEntries = trimZone.Where(p => IsCritical(p.Importance)).ToList();
                    if (criticalEntries.Count > cfg.LedgerMaxCriticalPerEntity)
                        criticalEntries = criticalEntries.TakeLast(cfg.LedgerMaxCriticalPerEntity).ToList();

                    var importantEntries = trimZone.Where(p => IsImportant(p.Importance)).TakeLast(cfg.LedgerImportantKeepRecent).ToList();
                    var trueNormalEntries = trimZone.Where(p => !IsCritical(p.Importance) && !IsImportant(p.Importance)).ToList();
                    var actualTrimmed = System.Math.Max(0, trueNormalEntries.Count - (trueNormalEntries.Count > 1 ? 2 : 1));

                    var normalCount = trueNormalEntries.Count > 0 ? System.Math.Min(trueNormalEntries.Count, 2) : 0;
                    var rebuilt = new List<ConflictProgressPoint>(criticalEntries.Count + importantEntries.Count + normalCount + keepZone.Count);
                    rebuilt.AddRange(criticalEntries);
                    rebuilt.AddRange(importantEntries);
                    if (trueNormalEntries.Count > 0)
                    {
                        var firstEntry = trueNormalEntries.First();
                        firstEntry.Description = $"[起始锚点] {firstEntry.Description}（归档{trueNormalEntries.Count}条）";
                        rebuilt.Add(firstEntry);
                        if (trueNormalEntries.Count > 1)
                        {
                            var lastEntry = trueNormalEntries.Last();
                            lastEntry.Description = $"[截止快照] {lastEntry.Description}";
                            rebuilt.Add(lastEntry);
                        }
                    }
                    rebuilt.AddRange(keepZone);

                    entry.ProgressPoints = rebuilt;
                    result.ConflictsTrimmed++;
                    result.TotalProgressTrimmed += System.Math.Max(0, actualTrimmed);
                }

                if (result.TotalProgressTrimmed > cpPrev) _guideManager.MarkDirty(cpFile);
            }
        }

        private async Task TrimPlotPointsAsync(TrimResult result, LayeredContextConfigSnapshot cfg)
        {
            var store = ServiceLocator.Get<PlotPointsIndexService>();
            var volNumbers = store.GetExistingVolumeNumbers();
            var _allPoints = await Task.WhenAll(volNumbers.Select(v => store.GetVolumeEntriesAsync(v))).ConfigureAwait(false);

            for (int _pi = 0; _pi < volNumbers.Count; _pi++)
            {
                var vol = volNumbers[_pi];
                var points = _allPoints[_pi];
                if (points.Count <= cfg.LedgerPlotPointsKeepRecent)
                    continue;

                var trimCount = points.Count - cfg.LedgerPlotPointsKeepRecent;
                var trimZone = points.Take(trimCount).ToList();
                var keepZone = points.Skip(trimCount).ToList();

                foreach (var p in trimZone)
                    if (!IsCritical(p.Importance) && HasEscalationKeyword(p))
                        p.Importance = "critical";

                var criticalEntries = trimZone.Where(p => IsCritical(p.Importance)).ToList();
                if (criticalEntries.Count > cfg.LedgerMaxCriticalPerEntity)
                    criticalEntries = criticalEntries.TakeLast(cfg.LedgerMaxCriticalPerEntity).ToList();

                var importantEntries = trimZone.Where(p => IsImportant(p.Importance)).TakeLast(cfg.LedgerImportantKeepRecent).ToList();
                var trueNormalEntries = trimZone.Where(p => !IsCritical(p.Importance) && !IsImportant(p.Importance)).ToList();
                var normalAnchors = BuildPlotPointAnchors(trueNormalEntries, cfg);
                var actualTrimmed = System.Math.Max(0, trueNormalEntries.Count - normalAnchors.Count);

                var rebuilt = new List<PlotPointEntry>(criticalEntries.Count + importantEntries.Count + normalAnchors.Count + keepZone.Count);
                rebuilt.AddRange(criticalEntries);
                rebuilt.AddRange(importantEntries);
                rebuilt.AddRange(normalAnchors);
                rebuilt.AddRange(keepZone);

                await store.SetVolumeEntriesAsync(vol, rebuilt).ConfigureAwait(false);
                result.PlotPointsTrimmed += System.Math.Max(0, actualTrimmed);
            }
        }

        private async Task TrimLocationStateHistoryAsync(TrimResult result, LayeredContextConfigSnapshot cfg)
        {
            var _volFiles = GetVolFiles("location_state_guide.json");
            var _guides = await Task.WhenAll(_volFiles.Select(f => _guideManager.GetGuideAsync<LocationStateGuide>(f))).ConfigureAwait(false);
            for (int _gi = 0; _gi < _volFiles.Count; _gi++)
            {
                var locFile = _volFiles[_gi];
                var guide = _guides[_gi];
                var locPrev = result.TotalLocationStatesTrimmed;
                foreach (var (_, entry) in guide.Locations)
                {
                    var history = entry.StateHistory;
                    if (history.Count <= cfg.LedgerLocationStateKeepRecent)
                        continue;

                    var trimCount = history.Count - cfg.LedgerLocationStateKeepRecent;
                    var trimZone = history.Take(trimCount).ToList();
                    var keepZone = history.Skip(trimCount).ToList();

                    var criticalEntries = trimZone.Where(s => IsCritical(s.Importance)).ToList();
                    if (criticalEntries.Count > cfg.LedgerMaxCriticalPerEntity)
                        criticalEntries = criticalEntries.TakeLast(cfg.LedgerMaxCriticalPerEntity).ToList();

                    var importantEntries = trimZone.Where(s => IsImportant(s.Importance)).TakeLast(cfg.LedgerImportantKeepRecent).ToList();
                    var trueNormalEntries = trimZone.Where(s => !IsCritical(s.Importance) && !IsImportant(s.Importance)).ToList();
                    var normalAnchors = BuildLocationAnchors(trueNormalEntries, cfg);
                    var actualTrimmed = System.Math.Max(0, trueNormalEntries.Count - normalAnchors.Count);

                    var rebuilt = new List<LocationStatePoint>(criticalEntries.Count + importantEntries.Count + normalAnchors.Count + keepZone.Count);
                    rebuilt.AddRange(criticalEntries);
                    rebuilt.AddRange(importantEntries);
                    rebuilt.AddRange(normalAnchors);
                    rebuilt.AddRange(keepZone);
                    entry.StateHistory = rebuilt;

                    result.LocationsTrimmed++;
                    result.TotalLocationStatesTrimmed += System.Math.Max(0, actualTrimmed);
                }

                if (result.TotalLocationStatesTrimmed > locPrev) _guideManager.MarkDirty(locFile);
            }
        }

        private async Task TrimFactionStateHistoryAsync(TrimResult result, LayeredContextConfigSnapshot cfg)
        {
            var _volFiles = GetVolFiles("faction_state_guide.json");
            var _guides = await Task.WhenAll(_volFiles.Select(f => _guideManager.GetGuideAsync<FactionStateGuide>(f))).ConfigureAwait(false);
            for (int _gi = 0; _gi < _volFiles.Count; _gi++)
            {
                var facFile = _volFiles[_gi];
                var guide = _guides[_gi];
                var facPrev = result.TotalFactionStatesTrimmed;
                foreach (var (_, entry) in guide.Factions)
                {
                    var history = entry.StateHistory;
                    if (history.Count <= cfg.LedgerFactionStateKeepRecent)
                        continue;

                    var trimCount = history.Count - cfg.LedgerFactionStateKeepRecent;
                    var trimZone = history.Take(trimCount).ToList();
                    var keepZone = history.Skip(trimCount).ToList();

                    var criticalEntries = trimZone.Where(s => IsCritical(s.Importance)).ToList();
                    if (criticalEntries.Count > cfg.LedgerMaxCriticalPerEntity)
                        criticalEntries = criticalEntries.TakeLast(cfg.LedgerMaxCriticalPerEntity).ToList();

                    var importantEntries = trimZone.Where(s => IsImportant(s.Importance)).TakeLast(cfg.LedgerImportantKeepRecent).ToList();
                    var trueNormalEntries = trimZone.Where(s => !IsCritical(s.Importance) && !IsImportant(s.Importance)).ToList();
                    var normalAnchors = BuildFactionAnchors(trueNormalEntries, cfg);
                    var actualTrimmed = System.Math.Max(0, trueNormalEntries.Count - normalAnchors.Count);

                    var rebuilt = new List<FactionStatePoint>(criticalEntries.Count + importantEntries.Count + normalAnchors.Count + keepZone.Count);
                    rebuilt.AddRange(criticalEntries);
                    rebuilt.AddRange(importantEntries);
                    rebuilt.AddRange(normalAnchors);
                    rebuilt.AddRange(keepZone);
                    entry.StateHistory = rebuilt;

                    result.FactionsTrimmed++;
                    result.TotalFactionStatesTrimmed += System.Math.Max(0, actualTrimmed);
                }

                if (result.TotalFactionStatesTrimmed > facPrev) _guideManager.MarkDirty(facFile);
            }
        }

        private async Task TrimTimelineAsync(TrimResult result, LayeredContextConfigSnapshot cfg)
        {
            var _volFiles = GetVolFiles("timeline_guide.json");
            var _guides = await Task.WhenAll(_volFiles.Select(f => _guideManager.GetGuideAsync<TimelineGuide>(f))).ConfigureAwait(false);
            for (int _gi = 0; _gi < _volFiles.Count; _gi++)
            {
                var tlFile = _volFiles[_gi];
                var guide = _guides[_gi];
                var tlPrev = result.TimelineEntriesTrimmed;
                var timeline = guide.ChapterTimeline;
                if (timeline.Count <= cfg.LedgerTimelineKeepRecent)
                    continue;

                var trimCount = timeline.Count - cfg.LedgerTimelineKeepRecent;
                var trimZone = timeline.Take(trimCount).ToList();
                var keepZone = timeline.Skip(trimCount).ToList();

                var criticalEntries = trimZone.Where(t => IsCritical(t.Importance)).ToList();
                if (criticalEntries.Count > cfg.LedgerMaxCriticalPerEntity)
                    criticalEntries = criticalEntries.TakeLast(cfg.LedgerMaxCriticalPerEntity).ToList();

                var importantEntries = trimZone.Where(t => IsImportant(t.Importance)).TakeLast(cfg.LedgerImportantKeepRecent).ToList();
                var trueNormalEntries = trimZone.Where(t => !IsCritical(t.Importance) && !IsImportant(t.Importance)).ToList();
                var actualTrimmed = System.Math.Max(0, trimCount - criticalEntries.Count - importantEntries.Count - System.Math.Min(trueNormalEntries.Count, 2));

                var tlNormalCount = trueNormalEntries.Count > 0 ? System.Math.Min(trueNormalEntries.Count, 2) : 0;
                var rebuilt = new List<ChapterTimeEntry>(criticalEntries.Count + importantEntries.Count + tlNormalCount + keepZone.Count);
                rebuilt.AddRange(criticalEntries);
                rebuilt.AddRange(importantEntries);
                if (trueNormalEntries.Count > 0)
                {
                    var firstEntry = trueNormalEntries.First();
                    firstEntry.KeyTimeEvent = $"[起始时间间] {firstEntry.KeyTimeEvent}（归档{trueNormalEntries.Count}条）";
                    rebuilt.Add(firstEntry);
                    if (trueNormalEntries.Count > 1)
                    {
                        var lastEntry = trueNormalEntries.Last();
                        lastEntry.KeyTimeEvent = $"[时间截止快照] {lastEntry.KeyTimeEvent}";
                        rebuilt.Add(lastEntry);
                    }
                }
                rebuilt.AddRange(keepZone);

                guide.ChapterTimeline = rebuilt;
                result.TimelineEntriesTrimmed += System.Math.Max(0, actualTrimmed);

                if (result.TimelineEntriesTrimmed > tlPrev) _guideManager.MarkDirty(tlFile);
            }
        }

        private async Task TrimCharacterMovementsAsync(TrimResult result, LayeredContextConfigSnapshot cfg)
        {
            var _volFiles = GetVolFiles("timeline_guide.json");
            var _guides = await Task.WhenAll(_volFiles.Select(f => _guideManager.GetGuideAsync<TimelineGuide>(f))).ConfigureAwait(false);
            for (int _gi = 0; _gi < _volFiles.Count; _gi++)
            {
                var mvFile = _volFiles[_gi];
                var guide = _guides[_gi];
                var modified = false;

                foreach (var (_, entry) in guide.CharacterLocations)
                {
                    var moves = entry.MovementHistory;
                    if (moves.Count <= cfg.LedgerMovementKeepRecent)
                        continue;

                    var trimCount = moves.Count - cfg.LedgerMovementKeepRecent;
                    var trimZone = moves.Take(trimCount).ToList();
                    var keepZone = moves.Skip(trimCount).ToList();

                    var criticalEntries = trimZone.Where(m => IsCritical(m.Importance)).ToList();
                    if (criticalEntries.Count > cfg.LedgerMaxCriticalPerEntity)
                        criticalEntries = criticalEntries.TakeLast(cfg.LedgerMaxCriticalPerEntity).ToList();

                    var importantEntries = trimZone.Where(m => IsImportant(m.Importance)).TakeLast(cfg.LedgerImportantKeepRecent).ToList();
                    var trueNormalEntries = trimZone.Where(m => !IsCritical(m.Importance) && !IsImportant(m.Importance)).ToList();

                    var mvNormalCount = trueNormalEntries.Count > 0 ? System.Math.Min(trueNormalEntries.Count, 2) : 0;
                    var rebuilt = new List<MovementRecord>(criticalEntries.Count + importantEntries.Count + mvNormalCount + keepZone.Count);
                    rebuilt.AddRange(criticalEntries);
                    rebuilt.AddRange(importantEntries);
                    if (trueNormalEntries.Count > 0)
                    {
                        rebuilt.Add(trueNormalEntries.First());
                        if (trueNormalEntries.Count > 1)
                            rebuilt.Add(trueNormalEntries.Last());
                    }
                    rebuilt.AddRange(keepZone);

                    entry.MovementHistory = rebuilt;
                    var actualTrimmed = trimCount - criticalEntries.Count - importantEntries.Count - System.Math.Min(trueNormalEntries.Count, 2);
                    if (actualTrimmed > 0)
                    {
                        result.CharactersMovementsTrimmed++;
                        result.TotalMovementsTrimmed += actualTrimmed;
                        modified = true;
                    }
                }

                if (modified) _guideManager.MarkDirty(mvFile);
            }
        }

        private async Task TrimItemStateHistoryAsync(TrimResult result, LayeredContextConfigSnapshot cfg)
        {
            var _volFiles = GetVolFiles("item_state_guide.json");
            var _guides = await Task.WhenAll(_volFiles.Select(f => _guideManager.GetGuideAsync<ItemStateGuide>(f))).ConfigureAwait(false);
            for (int _gi = 0; _gi < _volFiles.Count; _gi++)
            {
                var itFile = _volFiles[_gi];
                var guide = _guides[_gi];
                var itPrev = result.TotalItemStatesTrimmed;
                foreach (var (_, entry) in guide.Items)
                {
                    var history = entry.StateHistory;
                    if (history.Count <= cfg.LedgerItemStateKeepRecent)
                        continue;

                    var trimCount = history.Count - cfg.LedgerItemStateKeepRecent;
                    var trimZone = history.Take(trimCount).ToList();
                    var keepZone = history.Skip(trimCount).ToList();

                    var criticalEntries = trimZone.Where(s => IsCritical(s.Importance)).ToList();
                    if (criticalEntries.Count > cfg.LedgerMaxCriticalPerEntity)
                        criticalEntries = criticalEntries.TakeLast(cfg.LedgerMaxCriticalPerEntity).ToList();

                    var importantEntries = trimZone.Where(s => IsImportant(s.Importance)).TakeLast(cfg.LedgerImportantKeepRecent).ToList();
                    var trueNormalEntries = trimZone.Where(s => !IsCritical(s.Importance) && !IsImportant(s.Importance)).ToList();
                    var normalAnchors = BuildItemAnchors(trueNormalEntries, cfg);
                    var actualTrimmed = System.Math.Max(0, trueNormalEntries.Count - normalAnchors.Count);

                    var rebuilt = new List<ItemStatePoint>(criticalEntries.Count + importantEntries.Count + normalAnchors.Count + keepZone.Count);
                    rebuilt.AddRange(criticalEntries);
                    rebuilt.AddRange(importantEntries);
                    rebuilt.AddRange(normalAnchors);
                    rebuilt.AddRange(keepZone);
                    entry.StateHistory = rebuilt;

                    result.ItemsTrimmed++;
                    result.TotalItemStatesTrimmed += System.Math.Max(0, actualTrimmed);
                }

                if (result.TotalItemStatesTrimmed > itPrev) _guideManager.MarkDirty(itFile);
            }
        }

        private async Task TrimForeshadowingsAsync(TrimResult result)
        {
            const string fileName = "foreshadowing_status_guide.json";
            var guide = await _guideManager.GetGuideAsync<TM.Services.Modules.ProjectData.Models.Guides.ForeshadowingStatusGuide>(fileName).ConfigureAwait(false);
            var modified = false;

            var resolvedIds = new System.Collections.Generic.HashSet<string>(
                guide.Foreshadowings.Where(kv => kv.Value.IsResolved).Select(kv => kv.Key));

            var removedPending = guide.PendingList.RemoveAll(p => resolvedIds.Contains(p.Id));
            var removedOverdue = guide.OverdueList.RemoveAll(o => resolvedIds.Contains(o.Id));

            if (removedPending > 0 || removedOverdue > 0)
            {
                result.ForeshadowingsResolved += removedPending + removedOverdue;
                modified = true;
            }

            if (modified)
                _guideManager.MarkDirty(fileName);
        }

        private async Task TrimSecretRevealHistoryAsync(TrimResult result, LayeredContextConfigSnapshot cfg)
        {
            var _volFiles = GetVolFiles("secret_reveal_guide.json");
            var _guides = await System.Threading.Tasks.Task.WhenAll(_volFiles.Select(f => _guideManager.GetGuideAsync<TM.Services.Modules.ProjectData.Models.Guides.SecretRevealGuide>(f))).ConfigureAwait(false);
            for (int _gi = 0; _gi < _volFiles.Count; _gi++)
            {
                var srFile = _volFiles[_gi];
                var guide = _guides[_gi];
                var srPrev = result.TotalSecretRevealsTrimmed;
                foreach (var (_, entry) in guide.Secrets)
                {
                    var history = entry.RevealHistory;
                    if (history.Count <= cfg.LedgerConstraintHistoryKeepRecent)
                        continue;

                    var trimCount = history.Count - cfg.LedgerConstraintHistoryKeepRecent;
                    var trimZone = history.Take(trimCount).ToList();
                    var keepZone = history.Skip(trimCount).ToList();

                    var criticalEntries = trimZone.Where(s => IsCritical(s.Importance)).ToList();
                    if (criticalEntries.Count > cfg.LedgerMaxCriticalPerEntity)
                        criticalEntries = criticalEntries.TakeLast(cfg.LedgerMaxCriticalPerEntity).ToList();

                    var importantEntries = trimZone.Where(s => IsImportant(s.Importance)).TakeLast(cfg.LedgerImportantKeepRecent).ToList();
                    var trueNormalEntries = trimZone.Where(s => !IsCritical(s.Importance) && !IsImportant(s.Importance)).ToList();
                    var normalAnchors = BuildSecretAnchors(trueNormalEntries, cfg);
                    var actualTrimmed = System.Math.Max(0, trueNormalEntries.Count - normalAnchors.Count);

                    var rebuilt = new System.Collections.Generic.List<TM.Services.Modules.ProjectData.Models.Guides.SecretRevealPoint>(
                        criticalEntries.Count + importantEntries.Count + normalAnchors.Count + keepZone.Count);
                    rebuilt.AddRange(criticalEntries);
                    rebuilt.AddRange(importantEntries);
                    rebuilt.AddRange(normalAnchors);
                    rebuilt.AddRange(keepZone);
                    entry.RevealHistory = rebuilt;

                    result.SecretsTrimmed++;
                    result.TotalSecretRevealsTrimmed += System.Math.Max(0, actualTrimmed);
                }

                if (result.TotalSecretRevealsTrimmed > srPrev) _guideManager.MarkDirty(srFile);
            }
        }

        private static System.Collections.Generic.List<TM.Services.Modules.ProjectData.Models.Guides.SecretRevealPoint> BuildSecretAnchors(
            System.Collections.Generic.List<TM.Services.Modules.ProjectData.Models.Guides.SecretRevealPoint> normals, LayeredContextConfigSnapshot cfg)
        {
            var anchors = new System.Collections.Generic.List<TM.Services.Modules.ProjectData.Models.Guides.SecretRevealPoint>();
            if (normals.Count == 0) return anchors;
            var interval = cfg.LedgerNormalSampleInterval;
            var first = normals.First(); first.KeyEvent = $"[起始锚点] {first.KeyEvent}（归档{normals.Count}条）"; anchors.Add(first);
            for (int i = interval; i < normals.Count - 1; i += interval)
            { var s = normals[i]; s.KeyEvent = $"[中间快照@{i}] {s.KeyEvent}"; anchors.Add(s); }
            if (normals.Count > 1) { var last = normals.Last(); last.KeyEvent = $"[截止快照] {last.KeyEvent}"; anchors.Add(last); }
            return anchors;
        }

        private List<string> GetVolFiles(string baseFile)
        {
            var vols = _guideManager.GetExistingVolumeNumbers(baseFile);
            return vols.Select(v => GuideManager.GetVolumeFileName(baseFile, v)).ToList();
        }

        private static List<LocationStatePoint> BuildLocationAnchors(List<LocationStatePoint> normals, LayeredContextConfigSnapshot cfg)
        {
            var anchors = new List<LocationStatePoint>();
            if (normals.Count == 0) return anchors;
            var interval = cfg.LedgerNormalSampleInterval;
            var first = normals.First(); first.Event = $"[起始锚点] {first.Event}（归档{normals.Count}条）"; anchors.Add(first);
            for (int i = interval; i < normals.Count - 1; i += interval)
            { var s = normals[i]; s.Event = $"[中间快照@{i}] {s.Event}"; anchors.Add(s); }
            if (normals.Count > 1) { var last = normals.Last(); last.Event = $"[截止快照] {last.Event}"; anchors.Add(last); }
            return anchors;
        }

        private static List<FactionStatePoint> BuildFactionAnchors(List<FactionStatePoint> normals, LayeredContextConfigSnapshot cfg)
        {
            var anchors = new List<FactionStatePoint>();
            if (normals.Count == 0) return anchors;
            var interval = cfg.LedgerNormalSampleInterval;
            var first = normals.First(); first.Event = $"[起始锚点] {first.Event}（归档{normals.Count}条）"; anchors.Add(first);
            for (int i = interval; i < normals.Count - 1; i += interval)
            { var s = normals[i]; s.Event = $"[中间快照@{i}] {s.Event}"; anchors.Add(s); }
            if (normals.Count > 1) { var last = normals.Last(); last.Event = $"[截止快照] {last.Event}"; anchors.Add(last); }
            return anchors;
        }

        private static List<ItemStatePoint> BuildItemAnchors(List<ItemStatePoint> normals, LayeredContextConfigSnapshot cfg)
        {
            var anchors = new List<ItemStatePoint>();
            if (normals.Count == 0) return anchors;
            var interval = cfg.LedgerNormalSampleInterval;
            var first = normals.First(); first.Event = $"[起始锚点] {first.Event}（归档{normals.Count}条）"; anchors.Add(first);
            for (int i = interval; i < normals.Count - 1; i += interval)
            { var s = normals[i]; s.Event = $"[中间快照@{i}] {s.Event}"; anchors.Add(s); }
            if (normals.Count > 1) { var last = normals.Last(); last.Event = $"[截止快照] {last.Event}"; anchors.Add(last); }
            return anchors;
        }

        private async Task TrimPledgeConstraintHistoryAsync(TrimResult result, LayeredContextConfigSnapshot cfg)
        {
            var volFiles = GetVolFiles("pledge_constraint_guide.json");
            var guides = await System.Threading.Tasks.Task.WhenAll(volFiles.Select(f => _guideManager.GetGuideAsync<PledgeConstraintGuide>(f))).ConfigureAwait(false);
            for (int gi = 0; gi < volFiles.Count; gi++)
            {
                var plFile = volFiles[gi];
                var guide = guides[gi];
                var plPrev = result.TotalPledgeHistoryTrimmed;
                foreach (var (_, entry) in guide.Pledges)
                {
                    var history = entry.History;
                    if (history.Count <= cfg.LedgerConstraintHistoryKeepRecent)
                        continue;

                    var trimCount = history.Count - cfg.LedgerConstraintHistoryKeepRecent;
                    var trimZone = history.Take(trimCount).ToList();
                    var keepZone = history.Skip(trimCount).ToList();

                    var criticalEntries = trimZone.Where(s => IsCritical(s.Importance)).ToList();
                    if (criticalEntries.Count > cfg.LedgerMaxCriticalPerEntity)
                        criticalEntries = criticalEntries.TakeLast(cfg.LedgerMaxCriticalPerEntity).ToList();

                    var importantEntries = trimZone.Where(s => IsImportant(s.Importance)).TakeLast(cfg.LedgerImportantKeepRecent).ToList();
                    var trueNormalEntries = trimZone.Where(s => !IsCritical(s.Importance) && !IsImportant(s.Importance)).ToList();
                    var normalAnchors = BuildPledgeAnchors(trueNormalEntries, cfg);
                    var actualTrimmed = System.Math.Max(0, trueNormalEntries.Count - normalAnchors.Count);

                    var rebuilt = new List<PledgeConstraintPoint>(
                        criticalEntries.Count + importantEntries.Count + normalAnchors.Count + keepZone.Count);
                    rebuilt.AddRange(criticalEntries);
                    rebuilt.AddRange(importantEntries);
                    rebuilt.AddRange(normalAnchors);
                    rebuilt.AddRange(keepZone);
                    entry.History = rebuilt;

                    result.PledgesTrimmed++;
                    result.TotalPledgeHistoryTrimmed += System.Math.Max(0, actualTrimmed);
                }

                if (result.TotalPledgeHistoryTrimmed > plPrev) _guideManager.MarkDirty(plFile);
            }
        }

        private async Task TrimDeadlineConstraintHistoryAsync(TrimResult result, LayeredContextConfigSnapshot cfg)
        {
            var volFiles = GetVolFiles("deadline_constraint_guide.json");
            var guides = await System.Threading.Tasks.Task.WhenAll(volFiles.Select(f => _guideManager.GetGuideAsync<DeadlineConstraintGuide>(f))).ConfigureAwait(false);
            for (int gi = 0; gi < volFiles.Count; gi++)
            {
                var dlFile = volFiles[gi];
                var guide = guides[gi];
                var dlPrev = result.TotalDeadlineHistoryTrimmed;
                foreach (var (_, entry) in guide.Deadlines)
                {
                    var history = entry.History;
                    if (history.Count <= cfg.LedgerConstraintHistoryKeepRecent)
                        continue;

                    var trimCount = history.Count - cfg.LedgerConstraintHistoryKeepRecent;
                    var trimZone = history.Take(trimCount).ToList();
                    var keepZone = history.Skip(trimCount).ToList();

                    var criticalEntries = trimZone.Where(s => IsCritical(s.Importance)).ToList();
                    if (criticalEntries.Count > cfg.LedgerMaxCriticalPerEntity)
                        criticalEntries = criticalEntries.TakeLast(cfg.LedgerMaxCriticalPerEntity).ToList();

                    var importantEntries = trimZone.Where(s => IsImportant(s.Importance)).TakeLast(cfg.LedgerImportantKeepRecent).ToList();
                    var trueNormalEntries = trimZone.Where(s => !IsCritical(s.Importance) && !IsImportant(s.Importance)).ToList();
                    var normalAnchors = BuildDeadlineAnchors(trueNormalEntries, cfg);
                    var actualTrimmed = System.Math.Max(0, trueNormalEntries.Count - normalAnchors.Count);

                    var rebuilt = new List<DeadlineConstraintPoint>(
                        criticalEntries.Count + importantEntries.Count + normalAnchors.Count + keepZone.Count);
                    rebuilt.AddRange(criticalEntries);
                    rebuilt.AddRange(importantEntries);
                    rebuilt.AddRange(normalAnchors);
                    rebuilt.AddRange(keepZone);
                    entry.History = rebuilt;

                    result.DeadlinesTrimmed++;
                    result.TotalDeadlineHistoryTrimmed += System.Math.Max(0, actualTrimmed);
                }

                if (result.TotalDeadlineHistoryTrimmed > dlPrev) _guideManager.MarkDirty(dlFile);
            }
        }

        private static List<PledgeConstraintPoint> BuildPledgeAnchors(List<PledgeConstraintPoint> normals, LayeredContextConfigSnapshot cfg)
        {
            var anchors = new List<PledgeConstraintPoint>();
            if (normals.Count == 0) return anchors;
            var interval = cfg.LedgerNormalSampleInterval;
            var first = normals.First(); first.KeyEvent = $"[起始锚点] {first.KeyEvent}（归档{normals.Count}条）"; anchors.Add(first);
            for (int i = interval; i < normals.Count - 1; i += interval)
            { var s = normals[i]; s.KeyEvent = $"[中间快照@{i}] {s.KeyEvent}"; anchors.Add(s); }
            if (normals.Count > 1) { var last = normals.Last(); last.KeyEvent = $"[截止快照] {last.KeyEvent}"; anchors.Add(last); }
            return anchors;
        }

        private static List<DeadlineConstraintPoint> BuildDeadlineAnchors(List<DeadlineConstraintPoint> normals, LayeredContextConfigSnapshot cfg)
        {
            var anchors = new List<DeadlineConstraintPoint>();
            if (normals.Count == 0) return anchors;
            var interval = cfg.LedgerNormalSampleInterval;
            var first = normals.First(); first.KeyEvent = $"[起始锚点] {first.KeyEvent}（归档{normals.Count}条）"; anchors.Add(first);
            for (int i = interval; i < normals.Count - 1; i += interval)
            { var s = normals[i]; s.KeyEvent = $"[中间快照@{i}] {s.KeyEvent}"; anchors.Add(s); }
            if (normals.Count > 1) { var last = normals.Last(); last.KeyEvent = $"[截止快照] {last.KeyEvent}"; anchors.Add(last); }
            return anchors;
        }
    }
}
