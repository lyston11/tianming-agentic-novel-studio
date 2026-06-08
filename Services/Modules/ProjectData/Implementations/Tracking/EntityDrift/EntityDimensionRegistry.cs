using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public static class EntityDimensionRegistry
    {
        private static readonly List<EntityDimensionDescriptor> _all = BuildAll();

        public static IReadOnlyList<EntityDimensionDescriptor> All => _all;

        public static EntityDimensionDescriptor? GetByCode(string code)
            => _all.FirstOrDefault(d => string.Equals(d.DimensionCode, code, StringComparison.OrdinalIgnoreCase));

        public static IReadOnlyList<EntityDimensionDescriptor> AutoPatchDimensions
            => _all.Where(d => d.Strategy == DriftStrategy.AutoPatch).ToList();

        public static IReadOnlyList<EntityDimensionDescriptor> WarnOnlyDimensions
            => _all.Where(d => d.Strategy == DriftStrategy.WarnOnly).ToList();

        private static List<EntityDimensionDescriptor> BuildAll()
        {
            return new List<EntityDimensionDescriptor>
            {
                BuildCharacter(),
                BuildFaction(),
                BuildLocation(),
                BuildConflict(),
                BuildItem(),
                BuildForeshadowing(),
                BuildSecret(),
                BuildPledge(),
                BuildDeadline(),
            };
        }

        private static async Task<IReadOnlyList<DriftEntityRecord>> LoadVolumeScopedAsync<TGuide, TEntry>(
            GuideManager gm,
            string baseFileName,
            int recentVolumes,
            Func<TGuide, IDictionary<string, TEntry>> entitiesAccessor,
            Func<TEntry, string> nameAccessor,
            Func<TEntry, List<string>?> driftAccessor)
            where TGuide : class, new()
        {
            var vols = gm.GetExistingVolumeNumbers(baseFileName);
            var recent = recentVolumes <= 0
                ? vols
                : vols.TakeLast(recentVolumes).ToList();
            var result = new List<DriftEntityRecord>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var driftMerge = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var guides = await Task.WhenAll(recent.Select(v =>
                gm.GetGuideAsync<TGuide>(GuideManager.GetVolumeFileName(baseFileName, v)))).ConfigureAwait(false);
            for (int i = 0; i < guides.Length; i++)
            {
                var vol = recent[i];
                var entities = entitiesAccessor(guides[i]);
                if (entities == null) continue;
                foreach (var (id, entry) in entities)
                {
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    var dw = driftAccessor(entry);
                    if (dw != null && dw.Count > 0)
                    {
                        if (!driftMerge.TryGetValue(id, out var merged))
                        {
                            merged = new List<string>();
                            driftMerge[id] = merged;
                        }
                        merged.AddRange(dw);
                    }
                    if (!seen.Add(id)) continue;
                    var name = nameAccessor(entry);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    result.Add(new DriftEntityRecord(id, name, vol, Array.Empty<string>()));
                }
            }

            if (driftMerge.Count == 0) return result;
            return result
                .Select(r => driftMerge.TryGetValue(r.Id, out var dws)
                    ? new DriftEntityRecord(r.Id, r.Name, r.VolumeNumber, dws.Distinct().ToList())
                    : r)
                .ToList();
        }

        private static async Task<IReadOnlyList<DriftEntityRecord>> LoadSingleFileAsync<TGuide, TEntry>(
            GuideManager gm,
            string fileName,
            Func<TGuide, IDictionary<string, TEntry>> entitiesAccessor,
            Func<TEntry, string> nameAccessor,
            Func<TEntry, List<string>?> driftAccessor)
            where TGuide : class, new()
        {
            var guide = await gm.GetGuideAsync<TGuide>(fileName).ConfigureAwait(false);
            var result = new List<DriftEntityRecord>();
            var entities = entitiesAccessor(guide);
            if (entities == null) return result;
            foreach (var (id, entry) in entities)
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                var name = nameAccessor(entry);
                if (string.IsNullOrWhiteSpace(name)) continue;
                var dw = driftAccessor(entry);
                var dwList = (dw != null && dw.Count > 0)
                    ? (IReadOnlyList<string>)dw.Distinct().ToList()
                    : Array.Empty<string>();
                result.Add(new DriftEntityRecord(id, name, 0, dwList));
            }
            return result;
        }

        private static void AppendBoundedWarning(List<string> driftWarnings, string warnMsg, int maxWarn)
        {
            if (driftWarnings == null) return;
            if (maxWarn > 0 && driftWarnings.Count >= maxWarn)
                driftWarnings.RemoveRange(0, driftWarnings.Count - maxWarn + 1);
            driftWarnings.Add(warnMsg);
        }

        private static EntityDimensionDescriptor BuildCharacter()
        {
            const string baseFile = "character_state_guide.json";
            return new EntityDimensionDescriptor
            {
                DimensionName = "角色",
                DimensionCode = "character",
                GuideFileName = baseFile,
                IsVolumeScoped = true,
                Strategy = DriftStrategy.AutoPatch,
                ChangeFieldName = "CharacterStateChanges",
                LoadRecentEntitiesAsync = (gm, recent) => LoadVolumeScopedAsync<CharacterStateGuide, CharacterStateEntry>(
                    gm, baseFile, recent, g => g.Characters, e => e.Name, e => e.DriftWarnings),
                ExtractDeclaredIds = changes => new HashSet<string>(
                    (changes.CharacterStateChanges ?? new()).Select(c => c.CharacterId).Where(s => !string.IsNullOrWhiteSpace(s)),
                    StringComparer.OrdinalIgnoreCase),
                GuideFileNameForVolume = vol => GuideManager.GetVolumeFileName(baseFile, vol),
                AppendDriftWarningAsync = async (gm, vol, id, name, warnMsg, maxWarn) =>
                {
                    var fileName = GuideManager.GetVolumeFileName(baseFile, vol);
                    var guide = await gm.GetGuideAsync<CharacterStateGuide>(fileName).ConfigureAwait(false);
                    if (!guide.Characters.TryGetValue(id, out var entry))
                    {
                        entry = new CharacterStateEntry { Name = name };
                        guide.Characters[id] = entry;
                    }
                    AppendBoundedWarning(entry.DriftWarnings, warnMsg, maxWarn);
                    return fileName;
                },
                AutoPatchAction = (changes, id, name, reason) =>
                {
                    changes.CharacterStateChanges ??= new();
                    if (changes.CharacterStateChanges.Any(c => string.Equals(c.CharacterId, id, StringComparison.OrdinalIgnoreCase)))
                        return;
                    changes.CharacterStateChanges.Add(new CharacterStateChange
                    {
                        CharacterId = id,
                        KeyEvent = $"[自动补录] {reason}",
                        Importance = "normal"
                    });
                }
            };
        }

        private static EntityDimensionDescriptor BuildFaction()
        {
            const string baseFile = "faction_state_guide.json";
            return new EntityDimensionDescriptor
            {
                DimensionName = "势力",
                DimensionCode = "faction",
                GuideFileName = baseFile,
                IsVolumeScoped = true,
                Strategy = DriftStrategy.AutoPatch,
                ChangeFieldName = "FactionStateChanges",
                LoadRecentEntitiesAsync = (gm, recent) => LoadVolumeScopedAsync<FactionStateGuide, FactionStateEntry>(
                    gm, baseFile, recent, g => g.Factions, e => e.Name, e => e.DriftWarnings),
                ExtractDeclaredIds = changes => new HashSet<string>(
                    (changes.FactionStateChanges ?? new()).Select(c => c.FactionId).Where(s => !string.IsNullOrWhiteSpace(s)),
                    StringComparer.OrdinalIgnoreCase),
                GuideFileNameForVolume = vol => GuideManager.GetVolumeFileName(baseFile, vol),
                AppendDriftWarningAsync = async (gm, vol, id, name, warnMsg, maxWarn) =>
                {
                    var fileName = GuideManager.GetVolumeFileName(baseFile, vol);
                    var guide = await gm.GetGuideAsync<FactionStateGuide>(fileName).ConfigureAwait(false);
                    if (!guide.Factions.TryGetValue(id, out var entry))
                    {
                        entry = new FactionStateEntry { Name = name };
                        guide.Factions[id] = entry;
                    }
                    AppendBoundedWarning(entry.DriftWarnings, warnMsg, maxWarn);
                    return fileName;
                },
                AutoPatchAction = (changes, id, name, reason) =>
                {
                    changes.FactionStateChanges ??= new();
                    if (changes.FactionStateChanges.Any(c => string.Equals(c.FactionId, id, StringComparison.OrdinalIgnoreCase)))
                        return;
                    changes.FactionStateChanges.Add(new FactionStateChange
                    {
                        FactionId = id,
                        Event = $"[自动补录] {reason}",
                        Importance = "normal"
                    });
                }
            };
        }

        private static EntityDimensionDescriptor BuildLocation()
        {
            const string baseFile = "location_state_guide.json";
            return new EntityDimensionDescriptor
            {
                DimensionName = "地点",
                DimensionCode = "location",
                GuideFileName = baseFile,
                IsVolumeScoped = true,
                Strategy = DriftStrategy.AutoPatch,
                ChangeFieldName = "LocationStateChanges",
                LoadRecentEntitiesAsync = (gm, recent) => LoadVolumeScopedAsync<LocationStateGuide, LocationStateEntry>(
                    gm, baseFile, recent, g => g.Locations, e => e.Name, e => e.DriftWarnings),
                ExtractDeclaredIds = changes => new HashSet<string>(
                    (changes.LocationStateChanges ?? new()).Select(c => c.LocationId).Where(s => !string.IsNullOrWhiteSpace(s)),
                    StringComparer.OrdinalIgnoreCase),
                GuideFileNameForVolume = vol => GuideManager.GetVolumeFileName(baseFile, vol),
                AppendDriftWarningAsync = async (gm, vol, id, name, warnMsg, maxWarn) =>
                {
                    var fileName = GuideManager.GetVolumeFileName(baseFile, vol);
                    var guide = await gm.GetGuideAsync<LocationStateGuide>(fileName).ConfigureAwait(false);
                    if (!guide.Locations.TryGetValue(id, out var entry))
                    {
                        entry = new LocationStateEntry { Name = name };
                        guide.Locations[id] = entry;
                    }
                    AppendBoundedWarning(entry.DriftWarnings, warnMsg, maxWarn);
                    return fileName;
                },
                AutoPatchAction = (changes, id, name, reason) =>
                {
                    changes.LocationStateChanges ??= new();
                    if (changes.LocationStateChanges.Any(c => string.Equals(c.LocationId, id, StringComparison.OrdinalIgnoreCase)))
                        return;
                    changes.LocationStateChanges.Add(new LocationStateChange
                    {
                        LocationId = id,
                        LocationName = name,
                        NewStatus = "active",
                        Event = $"[自动补录] {reason}",
                        Importance = "normal"
                    });
                }
            };
        }

        private static EntityDimensionDescriptor BuildConflict()
        {
            const string baseFile = "conflict_progress_guide.json";
            return new EntityDimensionDescriptor
            {
                DimensionName = "冲突",
                DimensionCode = "conflict",
                GuideFileName = baseFile,
                IsVolumeScoped = true,
                Strategy = DriftStrategy.WarnOnly,
                ChangeFieldName = "ConflictProgress",
                LoadRecentEntitiesAsync = (gm, recent) => LoadVolumeScopedAsync<ConflictProgressGuide, ConflictProgressEntry>(
                    gm, baseFile, recent, g => g.Conflicts, e => e.Name, e => e.DriftWarnings),
                ExtractDeclaredIds = changes => new HashSet<string>(
                    (changes.ConflictProgress ?? new()).Select(c => c.ConflictId).Where(s => !string.IsNullOrWhiteSpace(s)),
                    StringComparer.OrdinalIgnoreCase),
                GuideFileNameForVolume = vol => GuideManager.GetVolumeFileName(baseFile, vol),
                AppendDriftWarningAsync = async (gm, vol, id, name, warnMsg, maxWarn) =>
                {
                    var fileName = GuideManager.GetVolumeFileName(baseFile, vol);
                    var guide = await gm.GetGuideAsync<ConflictProgressGuide>(fileName).ConfigureAwait(false);
                    if (!guide.Conflicts.TryGetValue(id, out var entry))
                    {
                        entry = new ConflictProgressEntry { Name = name };
                        guide.Conflicts[id] = entry;
                    }
                    AppendBoundedWarning(entry.DriftWarnings, warnMsg, maxWarn);
                    return fileName;
                }
            };
        }

        private static EntityDimensionDescriptor BuildItem()
        {
            const string baseFile = "item_state_guide.json";
            return new EntityDimensionDescriptor
            {
                DimensionName = "物品",
                DimensionCode = "item",
                GuideFileName = baseFile,
                IsVolumeScoped = true,
                Strategy = DriftStrategy.WarnOnly,
                ChangeFieldName = "ItemTransfers",
                LoadRecentEntitiesAsync = (gm, recent) => LoadVolumeScopedAsync<ItemStateGuide, ItemStateEntry>(
                    gm, baseFile, recent, g => g.Items, e => e.Name, e => e.DriftWarnings),
                ExtractDeclaredIds = changes => new HashSet<string>(
                    (changes.ItemTransfers ?? new()).Select(c => c.ItemId).Where(s => !string.IsNullOrWhiteSpace(s)),
                    StringComparer.OrdinalIgnoreCase),
                GuideFileNameForVolume = vol => GuideManager.GetVolumeFileName(baseFile, vol),
                AppendDriftWarningAsync = async (gm, vol, id, name, warnMsg, maxWarn) =>
                {
                    var fileName = GuideManager.GetVolumeFileName(baseFile, vol);
                    var guide = await gm.GetGuideAsync<ItemStateGuide>(fileName).ConfigureAwait(false);
                    if (!guide.Items.TryGetValue(id, out var entry))
                    {
                        entry = new ItemStateEntry { Name = name };
                        guide.Items[id] = entry;
                    }
                    AppendBoundedWarning(entry.DriftWarnings, warnMsg, maxWarn);
                    return fileName;
                }
            };
        }

        private static EntityDimensionDescriptor BuildForeshadowing()
        {
            const string fileName = "foreshadowing_status_guide.json";
            return new EntityDimensionDescriptor
            {
                DimensionName = "伏笔",
                DimensionCode = "foreshadowing",
                GuideFileName = fileName,
                IsVolumeScoped = false,
                Strategy = DriftStrategy.WarnOnly,
                ChangeFieldName = "ForeshadowingActions",
                LoadRecentEntitiesAsync = (gm, _) => LoadSingleFileAsync<ForeshadowingStatusGuide, ForeshadowingStatusEntry>(
                    gm, fileName, g => g.Foreshadowings, e => e.Name, e => e.DriftWarnings),
                ExtractDeclaredIds = changes => new HashSet<string>(
                    (changes.ForeshadowingActions ?? new()).Select(c => c.ForeshadowId).Where(s => !string.IsNullOrWhiteSpace(s)),
                    StringComparer.OrdinalIgnoreCase),
                GuideFileNameForVolume = _ => fileName,
                AppendDriftWarningAsync = async (gm, _, id, name, warnMsg, maxWarn) =>
                {
                    var guide = await gm.GetGuideAsync<ForeshadowingStatusGuide>(fileName).ConfigureAwait(false);
                    if (!guide.Foreshadowings.TryGetValue(id, out var entry))
                    {
                        entry = new ForeshadowingStatusEntry { Name = name };
                        guide.Foreshadowings[id] = entry;
                    }
                    AppendBoundedWarning(entry.DriftWarnings, warnMsg, maxWarn);
                    return fileName;
                }
            };
        }

        private static EntityDimensionDescriptor BuildSecret()
        {
            const string baseFile = "secret_reveal_guide.json";
            return new EntityDimensionDescriptor
            {
                DimensionName = "秘密",
                DimensionCode = "secret",
                GuideFileName = baseFile,
                IsVolumeScoped = true,
                Strategy = DriftStrategy.WarnOnly,
                ChangeFieldName = "SecretRevealChanges",
                LoadRecentEntitiesAsync = (gm, recent) => LoadVolumeScopedAsync<SecretRevealGuide, SecretRevealEntry>(
                    gm, baseFile, recent, g => g.Secrets, e => e.Name, e => e.DriftWarnings),
                ExtractDeclaredIds = changes => new HashSet<string>(
                    (changes.SecretRevealChanges ?? new()).Select(c => c.SecretId).Where(s => !string.IsNullOrWhiteSpace(s)),
                    StringComparer.OrdinalIgnoreCase),
                GuideFileNameForVolume = vol => GuideManager.GetVolumeFileName(baseFile, vol),
                AppendDriftWarningAsync = async (gm, vol, id, name, warnMsg, maxWarn) =>
                {
                    var fileName = GuideManager.GetVolumeFileName(baseFile, vol);
                    var guide = await gm.GetGuideAsync<SecretRevealGuide>(fileName).ConfigureAwait(false);
                    if (!guide.Secrets.TryGetValue(id, out var entry))
                    {
                        entry = new SecretRevealEntry { Name = name };
                        guide.Secrets[id] = entry;
                    }
                    AppendBoundedWarning(entry.DriftWarnings, warnMsg, maxWarn);
                    return fileName;
                }
            };
        }

        private static EntityDimensionDescriptor BuildPledge()
        {
            const string baseFile = "pledge_constraint_guide.json";
            return new EntityDimensionDescriptor
            {
                DimensionName = "承诺",
                DimensionCode = "pledge",
                GuideFileName = baseFile,
                IsVolumeScoped = true,
                Strategy = DriftStrategy.WarnOnly,
                ChangeFieldName = "PledgeConstraintChanges",
                LoadRecentEntitiesAsync = (gm, recent) => LoadVolumeScopedAsync<PledgeConstraintGuide, PledgeEntry>(
                    gm, baseFile, recent, g => g.Pledges, e => e.Name, e => e.DriftWarnings),
                ExtractDeclaredIds = changes => new HashSet<string>(
                    (changes.PledgeConstraintChanges ?? new()).Select(c => c.PledgeId).Where(s => !string.IsNullOrWhiteSpace(s)),
                    StringComparer.OrdinalIgnoreCase),
                GuideFileNameForVolume = vol => GuideManager.GetVolumeFileName(baseFile, vol),
                AppendDriftWarningAsync = async (gm, vol, id, name, warnMsg, maxWarn) =>
                {
                    var fileName = GuideManager.GetVolumeFileName(baseFile, vol);
                    var guide = await gm.GetGuideAsync<PledgeConstraintGuide>(fileName).ConfigureAwait(false);
                    if (!guide.Pledges.TryGetValue(id, out var entry))
                    {
                        entry = new PledgeEntry { Name = name };
                        guide.Pledges[id] = entry;
                    }
                    AppendBoundedWarning(entry.DriftWarnings, warnMsg, maxWarn);
                    return fileName;
                }
            };
        }

        private static EntityDimensionDescriptor BuildDeadline()
        {
            const string baseFile = "deadline_constraint_guide.json";
            return new EntityDimensionDescriptor
            {
                DimensionName = "倒计时",
                DimensionCode = "deadline",
                GuideFileName = baseFile,
                IsVolumeScoped = true,
                Strategy = DriftStrategy.WarnOnly,
                ChangeFieldName = "DeadlineConstraintChanges",
                LoadRecentEntitiesAsync = (gm, recent) => LoadVolumeScopedAsync<DeadlineConstraintGuide, DeadlineEntry>(
                    gm, baseFile, recent, g => g.Deadlines, e => e.Name, e => e.DriftWarnings),
                ExtractDeclaredIds = changes => new HashSet<string>(
                    (changes.DeadlineConstraintChanges ?? new()).Select(c => c.DeadlineId).Where(s => !string.IsNullOrWhiteSpace(s)),
                    StringComparer.OrdinalIgnoreCase),
                GuideFileNameForVolume = vol => GuideManager.GetVolumeFileName(baseFile, vol),
                AppendDriftWarningAsync = async (gm, vol, id, name, warnMsg, maxWarn) =>
                {
                    var fileName = GuideManager.GetVolumeFileName(baseFile, vol);
                    var guide = await gm.GetGuideAsync<DeadlineConstraintGuide>(fileName).ConfigureAwait(false);
                    if (!guide.Deadlines.TryGetValue(id, out var entry))
                    {
                        entry = new DeadlineEntry { Name = name };
                        guide.Deadlines[id] = entry;
                    }
                    AppendBoundedWarning(entry.DriftWarnings, warnMsg, maxWarn);
                    return fileName;
                }
            };
        }
    }
}
