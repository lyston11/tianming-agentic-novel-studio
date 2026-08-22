using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class FactSnapshotExtractor
    {
        #region 公开方法 - 卷末全量快照（不依赖打包上下文）

        public async Task<FactSnapshot> ExtractVolumeEndSnapshotAsync(string chapterId)
        {
            var cfg = LayeredContextConfig.TakeSnapshot();
            var snapshot = new FactSnapshot();
            try
            {
                var charGuideTask = AggregateCharacterStateGuideAsync(allVolumes: true);
                var conflictGuideTask = AggregateConflictProgressGuideAsync(allVolumes: true);
                var foreshadowGuideTask = _guideManager.GetGuideAsync<ForeshadowingStatusGuide>(ForeshadowingStatusGuideFileName);
                await Task.WhenAll(charGuideTask, conflictGuideTask, foreshadowGuideTask).ConfigureAwait(false);

                var charGuide = await charGuideTask.ConfigureAwait(false);
                foreach (var (id, entry) in charGuide.Characters)
                {
                    var lastState = entry.StateHistory.LastOrDefault();
                    if (lastState == null) continue;
                    snapshot.CharacterStates.Add(new CharacterStateSnapshot
                    {
                        Id = id,
                        Name = entry.Name,
                        Stage = lastState.Level,
                        Abilities = string.Join("、", lastState.Abilities ?? new List<string>()),
                        Relationships = FormatRelationships(lastState.Relationships),
                        ChapterId = lastState.Chapter
                    });
                }

                var conflictGuide = await conflictGuideTask.ConfigureAwait(false);
                foreach (var (id, entry) in conflictGuide.Conflicts)
                {
                    snapshot.ConflictProgress.Add(new ConflictProgressSnapshot
                    {
                        Id = id,
                        Name = entry.Name,
                        Status = entry.Status,
                        RecentProgress = entry.ProgressPoints
                            .TakeLast(3)
                            .Select(p => p.Event)
                            .Where(e => !string.IsNullOrWhiteSpace(e))
                            .ToList()
                    });
                }

                var foreshadowGuide = await foreshadowGuideTask.ConfigureAwait(false);
                foreach (var (id, entry) in foreshadowGuide.Foreshadowings)
                {
                    snapshot.ForeshadowingStatus.Add(new ForeshadowingStatusSnapshot
                    {
                        Id = id,
                        Name = entry.Name,
                        IsSetup = entry.IsSetup,
                        IsResolved = entry.IsResolved,
                        IsOverdue = entry.IsOverdue,
                        SetupChapterId = entry.ActualSetupChapter,
                        PayoffChapterId = entry.ActualPayoffChapter
                    });
                }

                var locTask = ExtractLocationStatesAsync(null, allVolumes: true, cfg: cfg);
                var facTask = ExtractFactionStatesAsync(applyLimit: false, allVolumes: true, cfg: cfg);
                var itemTask = ExtractItemStatesAsync(applyLimit: false, allVolumes: true, cfg: cfg);
                var tlTask = ExtractTimelineAsync(allVolumes: true, cfg: cfg);
                var clTask = ExtractCharacterLocationsAsync(chapterId, skipWindowFilter: true, cfg: cfg);

                var secretTask = ServiceLocator.Get<SecretRevealService>().ExtractSnapshotAsync(chapterId);
                var pledgeTask = ServiceLocator.Get<PledgeConstraintService>().ExtractSnapshotAsync(chapterId);
                var deadlineTask = ServiceLocator.Get<DeadlineConstraintService>().ExtractSnapshotAsync(chapterId);

                await Task.WhenAll(locTask, facTask, itemTask, tlTask, clTask, secretTask, pledgeTask, deadlineTask).ConfigureAwait(false);

                snapshot.LocationStates = locTask.Result;
                snapshot.FactionStates = facTask.Result;
                snapshot.ItemStates = itemTask.Result;
                snapshot.Timeline = tlTask.Result;
                snapshot.CharacterLocations = clTask.Result;
                snapshot.SecretStates = secretTask.Result;
                snapshot.PledgeStates = pledgeTask.Result;
                snapshot.DeadlineStates = deadlineTask.Result;

                TM.App.Log($"[FactSnapshotExtractor] 卷末全量快照: 角色{snapshot.CharacterStates.Count}+冲突{snapshot.ConflictProgress.Count}+伏笔{snapshot.ForeshadowingStatus.Count}+地点{snapshot.LocationStates.Count}+势力{snapshot.FactionStates.Count}+物品{snapshot.ItemStates.Count}+秘密{snapshot.SecretStates.Count}+时间线{snapshot.Timeline.Count}+角色位置{snapshot.CharacterLocations.Count}+承诺{snapshot.PledgeStates.Count}+倒计时{snapshot.DeadlineStates.Count}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FactSnapshotExtractor] 卷末快照抽取失败: {ex.Message}");
            }
            return snapshot;
        }

        #endregion
    }
}
