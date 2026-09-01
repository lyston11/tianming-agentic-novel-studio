using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class TimelineService
    {
        private readonly GuideManager _guideManager;

        public TimelineService(GuideManager guideManager)
        {
            _guideManager = guideManager;
        }

        private const string BaseFileName = "timeline_guide.json";
        private static string VolumeFileName(string chapterId) =>
            GuideManager.GetVolumeFileName(BaseFileName,
                ChapterParserHelper.ParseChapterIdOrDefault(chapterId).volumeNumber);

        public async Task UpdateTimeProgressionAsync(string chapterId, TimeProgressionChange change)
        {
            if (change == null) return;
            if (string.IsNullOrWhiteSpace(change.TimePeriod) && string.IsNullOrWhiteSpace(change.ElapsedTime))
                return;

            var volFile = VolumeFileName(chapterId);
            var guide = await _guideManager.GetGuideAsync<TimelineGuide>(volFile).ConfigureAwait(false);

            guide.ChapterTimeline.RemoveAll(t =>
                string.Equals(t.ChapterId, chapterId, StringComparison.Ordinal));

            guide.ChapterTimeline.Add(new ChapterTimeEntry
            {
                ChapterId = chapterId,
                TimePeriod = change.TimePeriod,
                ElapsedTime = change.ElapsedTime,
                KeyTimeEvent = change.KeyTimeEvent,
                Importance = string.IsNullOrWhiteSpace(change.Importance) ? "normal" : change.Importance
            });

            _guideManager.MarkDirty(volFile);
            TM.App.Log($"[TimelineService] 已更新 {chapterId} 时间推进: {change.TimePeriod}, 经过: {change.ElapsedTime}");
        }

        public async Task UpdateCharacterMovementsAsync(string chapterId, List<CharacterMovementChange> movements)
        {
            if (movements == null || movements.Count == 0) return;

            var volFile = VolumeFileName(chapterId);
            var guide = await _guideManager.GetGuideAsync<TimelineGuide>(volFile).ConfigureAwait(false);

            foreach (var move in movements)
            {
                if (string.IsNullOrWhiteSpace(move.CharacterId) || string.IsNullOrWhiteSpace(move.ToLocation))
                    continue;

                if (!guide.CharacterLocations.ContainsKey(move.CharacterId))
                {
                    var resolvedName = move.CharacterId;
                    try
                    {
                        var csVolNum = ChapterParserHelper.ParseChapterIdOrDefault(chapterId).volumeNumber;
                        var csVolFile = GuideManager.GetVolumeFileName("character_state_guide.json", csVolNum);
                        var csGuide = await _guideManager.GetGuideAsync<CharacterStateGuide>(csVolFile).ConfigureAwait(false);
                        if (csGuide.Characters.TryGetValue(move.CharacterId, out var csEntry) && !string.IsNullOrWhiteSpace(csEntry.Name))
                            resolvedName = csEntry.Name;
                    }
                    catch { }

                    guide.CharacterLocations[move.CharacterId] = new CharacterLocationEntry
                    {
                        CharacterName = resolvedName,
                        CurrentLocation = move.ToLocation,
                        LastUpdatedChapter = chapterId
                    };
                }

                var entry = guide.CharacterLocations[move.CharacterId];
                entry.CurrentLocation = move.ToLocation;
                entry.LastUpdatedChapter = chapterId;
                entry.MovementHistory.Add(new MovementRecord
                {
                    Chapter = chapterId,
                    FromLocation = move.FromLocation,
                    ToLocation = move.ToLocation,
                    Importance = string.IsNullOrWhiteSpace(move.Importance) ? "normal" : move.Importance
                });
            }

            _guideManager.MarkDirty(volFile);
            TM.App.Log($"[TimelineService] 已更新 {chapterId} 角色位置移动: {movements.Count}条");
        }

        public async Task RemoveChapterDataAsync(string chapterId)
        {
            var volFile = VolumeFileName(chapterId);
            var guide = await _guideManager.GetGuideAsync<TimelineGuide>(volFile).ConfigureAwait(false);
            var modified = false;

            var removed = guide.ChapterTimeline.RemoveAll(t =>
                string.Equals(t.ChapterId, chapterId, StringComparison.Ordinal));
            if (removed > 0) modified = true;

            foreach (var (_, entry) in guide.CharacterLocations)
            {
                var movesRemoved = entry.MovementHistory.RemoveAll(m =>
                    string.Equals(m.Chapter, chapterId, StringComparison.Ordinal));
                if (movesRemoved > 0)
                {
                    modified = true;

                    entry.MovementHistory.Sort((a, b) => ChapterParserHelper.CompareChapterId(a.Chapter, b.Chapter));
                    var lastMove = entry.MovementHistory.LastOrDefault();
                    if (lastMove != null)
                    {
                        entry.CurrentLocation = lastMove.ToLocation;
                        entry.LastUpdatedChapter = lastMove.Chapter;
                    }
                    else
                    {
                        entry.CurrentLocation = string.Empty;
                        entry.LastUpdatedChapter = string.Empty;
                    }
                }
            }

            if (modified)
            {
                _guideManager.MarkDirty(volFile);
                TM.App.Log($"[TimelineService] 已移除章节 {chapterId} 的时间线记录并重算当前位置");
            }
        }

        public async Task<List<ChapterTimeEntry>> GetRecentTimelineAsync(int count = 5)
        {
            var volNumbers = _guideManager.GetExistingVolumeNumbers(BaseFileName);
            var guides = await Task.WhenAll(volNumbers.TakeLast(5).Select(vol =>
                _guideManager.GetGuideAsync<TimelineGuide>(GuideManager.GetVolumeFileName(BaseFileName, vol)))).ConfigureAwait(false);
            var allEntries = new List<ChapterTimeEntry>();
            foreach (var guide in guides)
                allEntries.AddRange(guide.ChapterTimeline);
            var comparer = Comparer<string>.Create(ChapterParserHelper.CompareChapterId);
            return allEntries.OrderBy(t => t.ChapterId, comparer).TakeLast(count).ToList();
        }

        public async Task<Dictionary<string, CharacterLocationEntry>> GetAllCharacterLocationsAsync()
        {
            var volNumbers = _guideManager.GetExistingVolumeNumbers(BaseFileName);
            var guides = await Task.WhenAll(volNumbers.TakeLast(5).Select(vol =>
                _guideManager.GetGuideAsync<TimelineGuide>(GuideManager.GetVolumeFileName(BaseFileName, vol)))).ConfigureAwait(false);
            var merged = new Dictionary<string, CharacterLocationEntry>();
            foreach (var guide in guides)
                foreach (var (id, entry) in guide.CharacterLocations)
                    merged[id] = entry;
            return merged;
        }
    }
}
