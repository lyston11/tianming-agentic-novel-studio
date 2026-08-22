using System.Linq;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Guides;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class FactSnapshotExtractor
    {
        #region 分卷聚合辅助

        private async Task<CharacterStateGuide> AggregateCharacterStateGuideAsync(bool allVolumes = false)
        {
            var vols = _guideManager.GetExistingVolumeNumbers(CharacterStateGuideFileName);
            var recent = vols;
            var merged = new CharacterStateGuide();
            var guides = await Task.WhenAll(recent.Select(vol =>
                _guideManager.GetGuideAsync<CharacterStateGuide>(GuideManager.GetVolumeFileName(CharacterStateGuideFileName, vol)))).ConfigureAwait(false);
            foreach (var g in guides)
            {
                foreach (var (id, entry) in g.Characters)
                {
                    if (!merged.Characters.TryGetValue(id, out var mergedChar))
                    {
                        mergedChar = new CharacterStateEntry { Name = entry.Name };
                        merged.Characters[id] = mergedChar;
                    }
                    mergedChar.StateHistory.AddRange(entry.StateHistory);
                    if (entry.DriftWarnings?.Count > 0)
                        (mergedChar.DriftWarnings ??= new()).AddRange(entry.DriftWarnings);
                }
            }
            foreach (var e in merged.Characters.Values)
                e.StateHistory.Sort((a, b) => ChapterParserHelper.CompareChapterId(a.Chapter, b.Chapter));
            return merged;
        }

        private async Task<ConflictProgressGuide> AggregateConflictProgressGuideAsync(bool allVolumes = false)
        {
            var vols = _guideManager.GetExistingVolumeNumbers(ConflictProgressGuideFileName);
            var recent = vols;
            var merged = new ConflictProgressGuide();
            var guides = await Task.WhenAll(recent.Select(vol =>
                _guideManager.GetGuideAsync<ConflictProgressGuide>(GuideManager.GetVolumeFileName(ConflictProgressGuideFileName, vol)))).ConfigureAwait(false);
            foreach (var g in guides)
            {
                foreach (var (id, entry) in g.Conflicts)
                {
                    if (!merged.Conflicts.TryGetValue(id, out var mergedConflict))
                    {
                        mergedConflict = new ConflictProgressEntry { Name = entry.Name };
                        merged.Conflicts[id] = mergedConflict;
                    }
                    mergedConflict.Status = entry.Status;
                    mergedConflict.ProgressPoints.AddRange(entry.ProgressPoints);
                }
            }
            return merged;
        }

        private async Task<LocationStateGuide> AggregateLocationStateGuideAsync(bool allVolumes = false)
        {
            const string f = "location_state_guide.json";
            var vols = _guideManager.GetExistingVolumeNumbers(f);
            var recent = vols;
            var merged = new LocationStateGuide();
            var guides = await Task.WhenAll(recent.Select(vol =>
                _guideManager.GetGuideAsync<LocationStateGuide>(GuideManager.GetVolumeFileName(f, vol)))).ConfigureAwait(false);
            foreach (var g in guides)
                foreach (var (id, entry) in g.Locations) merged.Locations[id] = entry;
            return merged;
        }

        private async Task<FactionStateGuide> AggregateFactionStateGuideAsync(bool allVolumes = false)
        {
            const string f = "faction_state_guide.json";
            var vols = _guideManager.GetExistingVolumeNumbers(f);
            var recent = vols;
            var merged = new FactionStateGuide();
            var guides = await Task.WhenAll(recent.Select(vol =>
                _guideManager.GetGuideAsync<FactionStateGuide>(GuideManager.GetVolumeFileName(f, vol)))).ConfigureAwait(false);
            foreach (var g in guides)
                foreach (var (id, entry) in g.Factions) merged.Factions[id] = entry;
            return merged;
        }

        private async Task<TimelineGuide> AggregateTimelineGuideAsync(bool allVolumes = false)
        {
            const string f = "timeline_guide.json";
            var vols = _guideManager.GetExistingVolumeNumbers(f);
            var recent = vols;
            var merged = new TimelineGuide();
            var guides = await Task.WhenAll(recent.Select(vol =>
                _guideManager.GetGuideAsync<TimelineGuide>(GuideManager.GetVolumeFileName(f, vol)))).ConfigureAwait(false);
            foreach (var g in guides)
            {
                merged.ChapterTimeline.AddRange(g.ChapterTimeline);
                foreach (var (id, entry) in g.CharacterLocations) merged.CharacterLocations[id] = entry;
            }
            return merged;
        }

        private async Task<ItemStateGuide> AggregateItemStateGuideAsync(bool allVolumes = false)
        {
            const string f = "item_state_guide.json";
            var vols = _guideManager.GetExistingVolumeNumbers(f);
            var recent = vols;
            var merged = new ItemStateGuide();
            var guides = await Task.WhenAll(recent.Select(vol =>
                _guideManager.GetGuideAsync<ItemStateGuide>(GuideManager.GetVolumeFileName(f, vol)))).ConfigureAwait(false);
            foreach (var g in guides)
                foreach (var (id, entry) in g.Items) merged.Items[id] = entry;
            return merged;
        }

        #endregion
    }
}
