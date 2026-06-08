using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GuideCharacterState = TM.Services.Modules.ProjectData.Models.Guides.CharacterState;
using GuideRelationshipState = TM.Services.Modules.ProjectData.Models.Guides.RelationshipState;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class FactSnapshotExtractor
    {
        #region 私有方法 - 角色状态抽取

        private async Task<List<CharacterStateSnapshot>> ExtractCharacterStatesAsync(
            List<string>? characterIds,
            string prevChapterId,
            LayeredContextConfigSnapshot cfg)
        {
            var result = new List<CharacterStateSnapshot>();

            try
            {
                var guide = await AggregateCharacterStateGuideAsync().ConfigureAwait(false);

                if (characterIds != null && characterIds.Count > 0)
                {
                    foreach (var characterId in characterIds)
                    {
                        if (!guide.Characters.TryGetValue(characterId, out var characterEntry))
                            continue;

                        if (characterEntry.StateHistory == null || characterEntry.StateHistory.Count == 0)
                            continue;

                        var state = BinarySearchState(characterEntry.StateHistory, prevChapterId);
                        if (state == null)
                            state = characterEntry.StateHistory.FirstOrDefault();

                        if (state != null)
                        {
                            result.Add(new CharacterStateSnapshot
                            {
                                Id = characterId,
                                Name = characterEntry.Name,
                                Stage = state.Level,
                                Abilities = string.Join("、", state.Abilities ?? new List<string>()),
                                Relationships = FormatRelationships(state.Relationships),
                                ChapterId = state.Chapter
                            });
                        }
                    }
                }

                if (characterIds != null && characterIds.Count > 0 && !string.IsNullOrEmpty(prevChapterId))
                {
                    var existingResultIds = new HashSet<string>(result.Select(r => r.Id));
                    var parsedPrev = ChapterParserHelper.ParseChapterId(prevChapterId);
                    if (parsedPrev.HasValue && parsedPrev.Value.volumeNumber > 1)
                    {
                        var archives = await ServiceLocator.Get<VolumeFactArchiveStore>()
                            .GetPreviousArchivesAsync(parsedPrev.Value.volumeNumber).ConfigureAwait(false);
                        var archivesDesc = archives.OrderByDescending(a => a.VolumeNumber).ToList();
                        if (archivesDesc.Count == 0)
                            TM.App.Log($"[FactSnapshotExtractor] {prevChapterId} vol>1但无前卷存档，跨卷角色基线注入将静默跳过（请确认卷设计已配置EndChapter并触发卷末存档）");
                        var needsBaseline = characterIds.Where(id => !existingResultIds.Contains(id)).ToList();
                        foreach (var charId in needsBaseline)
                        {
                            foreach (var archive in archivesDesc)
                            {
                                var baseline = archive.CharacterStates.FirstOrDefault(s => s.Id == charId);
                                if (baseline == null) continue;
                                result.Add(new CharacterStateSnapshot
                                {
                                    Id = charId,
                                    Name = baseline.Name,
                                    Stage = baseline.Stage,
                                    Abilities = baseline.Abilities,
                                    Relationships = baseline.Relationships,
                                    ChapterId = $"vol{archive.VolumeNumber}_archive"
                                });
                                TM.App.Log($"[FactSnapshotExtractor] {charId} 使用前卷存档基线(第{archive.VolumeNumber}卷)");
                                break;
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(prevChapterId))
                {
                    var existingIds = new HashSet<string>(result.Select(r => r.Id));
                    var comparer = Comparer<string>.Create(ChapterParserHelper.CompareChapterId);
                    var activeSnapshots = new List<CharacterStateSnapshot>();

                    foreach (var (id, entry) in guide.Characters)
                    {
                        if (existingIds.Contains(id)) continue;
                        if (entry.StateHistory == null || entry.StateHistory.Count == 0) continue;

                        var lastState = BinarySearchState(entry.StateHistory, prevChapterId);
                        if (lastState == null || string.IsNullOrEmpty(lastState.Chapter)) continue;

                        if (!IsActiveInRecentChapters(lastState.Chapter, prevChapterId, cfg.ActiveEntityWindowChapters, GetChaptersPerVol()))
                            continue;

                        activeSnapshots.Add(new CharacterStateSnapshot
                        {
                            Id = id,
                            Name = entry.Name,
                            Stage = lastState.Level,
                            Abilities = string.Join("、", lastState.Abilities ?? new List<string>()),
                            Relationships = FormatRelationships(lastState.Relationships),
                            ChapterId = lastState.Chapter
                        });
                    }

                    var injected = activeSnapshots
                        .OrderByDescending(s => s.ChapterId, comparer)
                        .Take(cfg.ActiveEntityWindowMaxCount)
                        .ToList();

                    result.AddRange(injected);

                    if (injected.Count > 0)
                        TM.App.Log($"[FactSnapshotExtractor] 注入近期活跃角色: {injected.Count}条");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactSnapshotExtractor] 抽取角色状态失败: {ex.Message}");
            }

            return result;
        }

        private static int GetChaptersPerVol()
        {
            try
            {
                var volService = ServiceLocator.Get<TM.Modules.Generate.Elements.VolumeDesign.Services.VolumeDesignService>();
                var designs = volService.GetAllVolumeDesigns()
                    .ToList();
                if (designs != null && designs.Count > 0)
                {
                    var withEnd = designs.Where(d => d.EndChapter > 0 && d.VolumeNumber > 0 && d.StartChapter > 0).ToList();
                    if (withEnd.Count > 0)
                        return (int)System.Math.Round(withEnd.Average(d => (double)(d.EndChapter - d.StartChapter + 1)));
                }
            }
            catch (Exception ex) { TM.App.Log($"[FactSnapshot] 读取平均章节数失败: {ex.Message}"); }
            return 20;
        }

        private static bool IsActiveInRecentChapters(string lastChapterId, string prevChapterId, int windowSize, int chaptersPerVol = 20)
        {
            if (string.IsNullOrEmpty(lastChapterId) || string.IsNullOrEmpty(prevChapterId)) return false;

            if (ChapterParserHelper.CompareChapterId(lastChapterId, prevChapterId) > 0)
                return false;

            var current = ChapterParserHelper.ParseChapterId(prevChapterId);
            var last = ChapterParserHelper.ParseChapterId(lastChapterId);
            if (current == null || last == null) return false;

            int distance = (current.Value.volumeNumber - last.Value.volumeNumber) * chaptersPerVol
                         + (current.Value.chapterNumber - last.Value.chapterNumber);
            return distance >= 0 && distance <= windowSize;
        }

        private GuideCharacterState? BinarySearchState(List<GuideCharacterState> history, string targetChapterId)
        {
            if (history == null || history.Count == 0)
                return null;

            int left = 0, right = history.Count - 1;
            int resultIndex = -1;

            while (left <= right)
            {
                int mid = (left + right) / 2;
                int cmp = ChapterParserHelper.CompareChapterId(history[mid].Chapter, targetChapterId);

                if (cmp <= 0)
                {
                    resultIndex = mid;
                    left = mid + 1;
                }
                else
                {
                    right = mid - 1;
                }
            }

            return resultIndex >= 0 ? history[resultIndex] : null;
        }

        private string FormatRelationships(Dictionary<string, GuideRelationshipState>? relationships)
        {
            if (relationships == null || relationships.Count == 0)
                return string.Empty;

            var parts = relationships
                .Where(r => !string.IsNullOrWhiteSpace(r.Key))
                .Select(r =>
                {
                    var base_ = string.IsNullOrWhiteSpace(r.Value.Relation)
                        ? $"{r.Key}(信任{r.Value.Trust:+#;-#;0})"
                        : $"{r.Key}({r.Value.Relation},{r.Value.Trust:+#;-#;0})";
                    return string.IsNullOrWhiteSpace(r.Value.EmotionPhase)
                        ? base_
                        : $"{base_}[情感:{r.Value.EmotionPhase}]";
                });

            return string.Join("、", parts);
        }

        #endregion
    }
}
