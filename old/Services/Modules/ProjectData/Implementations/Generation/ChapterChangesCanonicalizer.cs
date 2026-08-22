using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Modules.ProjectData.Models.Tracking;
using TM.Services.Modules.ProjectData.Models.Guides;

namespace TM.Services.Modules.ProjectData.Implementations.Generation
{
    public class CanonicalizationResult
    {
        public ChapterChanges Canonical { get; set; } = new();
        public List<string> PatchLog { get; set; } = new();
        public List<string> AmbiguousFields { get; set; } = new();
        public bool HasPatches => PatchLog.Count > 0;
    }

    public static class ChapterChangesCanonicalizer
    {
        private static readonly Regex BracketIdPattern =
            new(@"[\(（]([A-Z][0-9A-Za-z]{12})[\)）]", RegexOptions.Compiled);

        private static readonly Regex BracketContentRegex =
            new(@"[\(（][^)）]*[\)）]", RegexOptions.Compiled);

        private const int TrustDeltaMax = 30;

        public static CanonicalizationResult Canonicalize(ChapterChanges changes, FactSnapshot snapshot, ContextIdCollection? contextIds = null)
        {
            var result = new CanonicalizationResult();
            var canonical = result.Canonical = DeepCopy(changes);

            var charMap = BuildCharacterMap(snapshot);
            var locMap = BuildLocationMap(snapshot);
            var factionMap = BuildFactionMap(snapshot);
            var itemMap = BuildItemMap(snapshot);
            var conflictMap = BuildConflictMap(snapshot);
            var foreshadowMap = BuildForeshadowMap(snapshot);
            var secretMap = BuildSecretMap(snapshot);
            var pledgeMap = BuildPledgeMap(snapshot);
            var deadlineMap = BuildDeadlineMap(snapshot);

            var charCurrentLoc = BuildCharCurrentLocMap(snapshot);
            var itemCurrentHolder = BuildItemCurrentHolderMap(snapshot);

            if (contextIds != null)
            {
                foreach (var id in contextIds.Characters ?? new()) if (!string.IsNullOrWhiteSpace(id)) charMap.IdSet.Add(id);
                foreach (var id in contextIds.Locations ?? new()) if (!string.IsNullOrWhiteSpace(id)) locMap.IdSet.Add(id);
                foreach (var id in contextIds.Factions ?? new()) if (!string.IsNullOrWhiteSpace(id)) factionMap.IdSet.Add(id);
                foreach (var id in contextIds.Conflicts ?? new()) if (!string.IsNullOrWhiteSpace(id)) conflictMap.IdSet.Add(id);
                foreach (var id in contextIds.ForeshadowingSetups ?? new()) if (!string.IsNullOrWhiteSpace(id)) foreshadowMap.IdSet.Add(id);
                foreach (var id in contextIds.ForeshadowingPayoffs ?? new()) if (!string.IsNullOrWhiteSpace(id)) foreshadowMap.IdSet.Add(id);
            }

            foreach (var ch in canonical.CharacterStateChanges ?? new())
            {
                ch.CharacterId = Resolve("CharacterStateChanges.CharacterId", ch.CharacterId, charMap, result);

                if (ch.RelationshipChanges is { Count: > 0 })
                {
                    var rebuilt = new Dictionary<string, RelationshipChange>();
                    foreach (var (key, val) in ch.RelationshipChanges)
                    {
                        var resolvedKey = Resolve("CharacterStateChanges.RelationshipChanges.Key", key, charMap, result);
                        if (!ShortIdGenerator.IsLikelyId(resolvedKey))
                        {
                            result.PatchLog.Add($"[Canonicalize] RelationshipChanges: 剔除key仍为名称的关系 '{resolvedKey}'");
                            continue;
                        }
                        if (charMap.IdSet.Count > 0 && !charMap.IdSet.Contains(resolvedKey))
                        {
                            result.PatchLog.Add($"[Canonicalize] RelationshipChanges: 剔除key不在账本中的关系 '{resolvedKey}'（可能为伪造ShortId）");
                            continue;
                        }
                        rebuilt[resolvedKey] = val;
                    }
                    ch.RelationshipChanges = rebuilt;
                }
            }

            foreach (var cp in canonical.ConflictProgress ?? new())
                cp.ConflictId = Resolve("ConflictProgress.ConflictId", cp.ConflictId, conflictMap, result);

            foreach (var fa in canonical.ForeshadowingActions ?? new())
                fa.ForeshadowId = Resolve("ForeshadowingActions.ForeshadowId", fa.ForeshadowId, foreshadowMap, result);

            foreach (var lc in canonical.LocationStateChanges ?? new())
                lc.LocationId = Resolve("LocationStateChanges.LocationId", lc.LocationId, locMap, result);

            foreach (var fc in canonical.FactionStateChanges ?? new())
                fc.FactionId = Resolve("FactionStateChanges.FactionId", fc.FactionId, factionMap, result);

            var lastLoc = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mv in canonical.CharacterMovements ?? new())
            {
                mv.CharacterId = Resolve("CharacterMovements.CharacterId", mv.CharacterId, charMap, result);

                if (!string.IsNullOrWhiteSpace(mv.FromLocation))
                    mv.FromLocation = Resolve("CharacterMovements.FromLocation", mv.FromLocation, locMap, result);

                if ((!IsValidId(mv.FromLocation) || (mv.FromLocation is not null && !locMap.IdSet.Contains(mv.FromLocation))) && !string.IsNullOrWhiteSpace(mv.CharacterId))
                {
                    if (lastLoc.TryGetValue(mv.CharacterId, out var chainLoc))
                    {
                        result.PatchLog.Add($"[Canonicalize] CharacterMovements.FromLocation 同章补值: {mv.CharacterId} → {chainLoc}");
                        mv.FromLocation = chainLoc;
                    }
                    else if (charCurrentLoc.TryGetValue(mv.CharacterId, out var baseLoc) && IsValidId(baseLoc))
                    {
                        result.PatchLog.Add($"[Canonicalize] CharacterMovements.FromLocation 账本补值: {mv.CharacterId} → {baseLoc}");
                        mv.FromLocation = baseLoc;
                    }
                    else if (!IsValidId(mv.FromLocation) || (mv.FromLocation is not null && !locMap.IdSet.Contains(mv.FromLocation)))
                    {
                        result.PatchLog.Add($"[Canonicalize] CharacterMovements.FromLocation '{mv.FromLocation}' 不在已注册地点中，按未知出发地处理（不自动注册，仅 ToLocation/LocationStateChanges 可注册新地点）");
                        result.AmbiguousFields.RemoveAll(a => a.Contains("CharacterMovements.FromLocation"));
                        mv.FromLocation = string.Empty;
                    }
                }

                mv.ToLocation = Resolve("CharacterMovements.ToLocation", mv.ToLocation, locMap, result);

                if (!string.IsNullOrWhiteSpace(mv.CharacterId) && IsValidId(mv.ToLocation))
                    lastLoc[mv.CharacterId] = mv.ToLocation;
            }

            var lastHolder = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in canonical.ItemTransfers ?? new())
            {
                it.ItemId = Resolve("ItemTransfers.ItemId", it.ItemId, itemMap, result);

                if (!string.IsNullOrWhiteSpace(it.FromHolder))
                    it.FromHolder = Resolve("ItemTransfers.FromHolder", it.FromHolder, charMap, result);

                if (!string.IsNullOrWhiteSpace(it.FromHolder)
                    && (!IsValidId(it.FromHolder) || !charMap.IdSet.Contains(it.FromHolder)))
                {
                    if (!string.IsNullOrWhiteSpace(it.ItemId) && lastHolder.TryGetValue(it.ItemId, out var chainHolder))
                    {
                        result.PatchLog.Add($"[Canonicalize] ItemTransfers.FromHolder 同章补值: {it.ItemId} → {chainHolder}");
                        it.FromHolder = chainHolder;
                    }
                    else if (!string.IsNullOrWhiteSpace(it.ItemId)
                        && itemCurrentHolder.TryGetValue(it.ItemId, out var baseHolder)
                        && charMap.IdSet.Contains(baseHolder))
                    {
                        result.PatchLog.Add($"[Canonicalize] ItemTransfers.FromHolder 账本补值: {it.ItemId} → {baseHolder}");
                        it.FromHolder = baseHolder;
                    }
                    else if (!string.IsNullOrWhiteSpace(it.FromHolder) && (!IsValidId(it.FromHolder) || !charMap.IdSet.Contains(it.FromHolder)))
                    {
                        result.PatchLog.Add($"[Canonicalize] ItemTransfers.FromHolder 无法解析，已清空: '{it.FromHolder}'");
                        result.AmbiguousFields.RemoveAll(a => a.Contains("ItemTransfers.FromHolder"));
                        it.FromHolder = string.Empty;
                    }
                }

                it.ToHolder = Resolve("ItemTransfers.ToHolder", it.ToHolder, charMap, result);
                if (!string.IsNullOrWhiteSpace(it.ToHolder) && (!IsValidId(it.ToHolder) || !charMap.IdSet.Contains(it.ToHolder)))
                {
                    result.PatchLog.Add($"[Canonicalize] ItemTransfers.ToHolder 无法解析，已清空: '{it.ToHolder}'");
                    result.AmbiguousFields.RemoveAll(a => a.Contains("ItemTransfers.ToHolder"));
                    it.ToHolder = string.Empty;
                }

                if (!string.IsNullOrWhiteSpace(it.ItemId) && IsValidId(it.ToHolder))
                    lastHolder[it.ItemId] = it.ToHolder;
            }

            foreach (var pp in canonical.NewPlotPoints ?? new())
            {
                for (int i = 0; i < pp.InvolvedCharacters.Count; i++)
                    pp.InvolvedCharacters[i] = Resolve("NewPlotPoints.InvolvedCharacters", pp.InvolvedCharacters[i], charMap, result);
            }

            foreach (var sr in canonical.SecretRevealChanges ?? new())
            {
                sr.SecretId = Resolve("SecretRevealChanges.SecretId", sr.SecretId, secretMap, result);
                for (int i = 0; i < sr.NewKnowerIds.Count; i++)
                    sr.NewKnowerIds[i] = Resolve("SecretRevealChanges.NewKnowerIds", sr.NewKnowerIds[i], charMap, result);
            }

            foreach (var pc in canonical.PledgeConstraintChanges ?? new())
            {
                pc.PledgeId = Resolve("PledgeConstraintChanges.PledgeId", pc.PledgeId, pledgeMap, result);
                for (int i = 0; i < (pc.PartyIds?.Count ?? 0); i++)
                    pc.PartyIds![i] = Resolve("PledgeConstraintChanges.PartyIds", pc.PartyIds[i], charMap, result);
            }

            foreach (var dc in canonical.DeadlineConstraintChanges ?? new())
            {
                dc.DeadlineId = Resolve("DeadlineConstraintChanges.DeadlineId", dc.DeadlineId, deadlineMap, result);
                for (int i = 0; i < (dc.PartyIds?.Count ?? 0); i++)
                    dc.PartyIds![i] = Resolve("DeadlineConstraintChanges.PartyIds", dc.PartyIds[i], charMap, result);
            }

            var fsStateMap = BuildForeshadowStateMap(snapshot);
            canonical.ForeshadowingActions = CanonicalizeForeshadowingActions(
                canonical.ForeshadowingActions ?? new(), fsStateMap, result);

            var conflictStatusMap = BuildConflictCurrentStatusMap(snapshot);
            canonical.ConflictProgress = CanonicalizeConflictProgress(
                canonical.ConflictProgress ?? new(), conflictStatusMap, result);

            var newItemIdsByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in canonical.ItemTransfers ?? new())
            {
                var itemIdRaw = it.ItemId?.Trim() ?? string.Empty;
                if (IsValidId(itemIdRaw) && itemMap.IdSet.Contains(itemIdRaw))
                    continue;

                if (!string.IsNullOrWhiteSpace(it.ItemName))
                {
                    var existingId = Resolve("ItemTransfers.ItemName", it.ItemName, itemMap, result);
                    if (IsValidId(existingId) && itemMap.IdSet.Contains(existingId))
                    {
                        if (!string.Equals(it.ItemId, existingId, StringComparison.OrdinalIgnoreCase))
                            result.PatchLog.Add($"[Canonicalize] ItemTransfers.ItemId 按名称命中: {it.ItemName} → {existingId}");
                        it.ItemId = existingId;
                        continue;
                    }
                }

                var nameKey = !string.IsNullOrWhiteSpace(it.ItemName)
                    ? it.ItemName.Trim()
                    : itemIdRaw;
                if (string.IsNullOrWhiteSpace(nameKey))
                    continue;

                if (string.IsNullOrWhiteSpace(it.ItemName))
                    it.ItemName = nameKey;

                var seedKey = StripAnnotations(nameKey).Trim();
                if (string.IsNullOrWhiteSpace(seedKey))
                    seedKey = nameKey.Trim();
                seedKey = seedKey.ToLowerInvariant();

                if (!newItemIdsByName.TryGetValue(nameKey, out var newId))
                {
                    newId = ShortIdGenerator.NewDeterministic("I", $"ITEM|{seedKey}");
                    newItemIdsByName[nameKey] = newId;
                }

                if (!string.Equals(it.ItemId, newId, StringComparison.OrdinalIgnoreCase))
                    result.PatchLog.Add($"[Canonicalize] ItemTransfers.ItemId 自动生成: {nameKey} → {newId}");
                it.ItemId = newId;
                result.AmbiguousFields.RemoveAll(a => a.Contains($"'{nameKey}'"));
            }

            var newSecretIdsByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sr in canonical.SecretRevealChanges ?? new())
            {
                var secretIdRaw = sr.SecretId?.Trim() ?? string.Empty;
                if (IsValidId(secretIdRaw) && secretMap.IdSet.Contains(secretIdRaw))
                    continue;

                if (!string.IsNullOrWhiteSpace(sr.SecretName))
                {
                    var existingId = Resolve("SecretRevealChanges.SecretName", sr.SecretName, secretMap, result);
                    if (IsValidId(existingId) && secretMap.IdSet.Contains(existingId))
                    {
                        if (!string.Equals(sr.SecretId, existingId, StringComparison.OrdinalIgnoreCase))
                            result.PatchLog.Add($"[Canonicalize] SecretRevealChanges.SecretId 按名称命中: {sr.SecretName} → {existingId}");
                        sr.SecretId = existingId;
                        continue;
                    }
                }

                var nameKey = !string.IsNullOrWhiteSpace(sr.SecretName)
                    ? sr.SecretName.Trim()
                    : secretIdRaw;
                if (string.IsNullOrWhiteSpace(nameKey))
                    continue;

                if (string.IsNullOrWhiteSpace(sr.SecretName))
                    sr.SecretName = nameKey;

                var seedKey = StripAnnotations(nameKey).Trim();
                if (string.IsNullOrWhiteSpace(seedKey))
                    seedKey = nameKey.Trim();
                seedKey = seedKey.ToLowerInvariant();

                if (!newSecretIdsByName.TryGetValue(nameKey, out var newId))
                {
                    newId = ShortIdGenerator.NewDeterministic("S", $"SECRET|{seedKey}");
                    newSecretIdsByName[nameKey] = newId;
                }

                if (!string.Equals(sr.SecretId, newId, StringComparison.OrdinalIgnoreCase))
                    result.PatchLog.Add($"[Canonicalize] SecretRevealChanges.SecretId 自动生成: {nameKey} → {newId}");
                sr.SecretId = newId;
                result.AmbiguousFields.RemoveAll(a => a.Contains($"'{nameKey}'"));
            }

            var newPledgeIdsByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pc in canonical.PledgeConstraintChanges ?? new())
            {
                if (!string.Equals(pc.Action, "create", StringComparison.OrdinalIgnoreCase)) continue;
                var pledgeIdRaw = pc.PledgeId?.Trim() ?? string.Empty;
                if (IsValidId(pledgeIdRaw) && pledgeMap.IdSet.Contains(pledgeIdRaw))
                    continue;

                if (!string.IsNullOrWhiteSpace(pc.PledgeName))
                {
                    var existingId = Resolve("PledgeConstraintChanges.PledgeName", pc.PledgeName, pledgeMap, result);
                    if (IsValidId(existingId) && pledgeMap.IdSet.Contains(existingId))
                    {
                        if (!string.Equals(pc.PledgeId, existingId, StringComparison.OrdinalIgnoreCase))
                            result.PatchLog.Add($"[Canonicalize] PledgeConstraintChanges.PledgeId 按名称命中: {pc.PledgeName} → {existingId}");
                        pc.PledgeId = existingId;
                        continue;
                    }
                }

                var nameKey = !string.IsNullOrWhiteSpace(pc.PledgeName) ? pc.PledgeName.Trim() : pledgeIdRaw;
                if (string.IsNullOrWhiteSpace(nameKey)) continue;
                if (string.IsNullOrWhiteSpace(pc.PledgeName)) pc.PledgeName = nameKey;

                var seedKey = StripAnnotations(nameKey).Trim();
                if (string.IsNullOrWhiteSpace(seedKey)) seedKey = nameKey.Trim();
                seedKey = seedKey.ToLowerInvariant();

                if (!newPledgeIdsByName.TryGetValue(nameKey, out var newId))
                {
                    newId = ShortIdGenerator.NewDeterministic("PL", $"PLEDGE|{seedKey}");
                    newPledgeIdsByName[nameKey] = newId;
                }

                if (!string.Equals(pc.PledgeId, newId, StringComparison.OrdinalIgnoreCase))
                    result.PatchLog.Add($"[Canonicalize] PledgeConstraintChanges.PledgeId 自动生成: {nameKey} → {newId}");
                pc.PledgeId = newId;
                result.AmbiguousFields.RemoveAll(a => a.Contains($"'{nameKey}'"));
            }

            var newDeadlineIdsByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dc in canonical.DeadlineConstraintChanges ?? new())
            {
                if (!string.Equals(dc.Action, "create", StringComparison.OrdinalIgnoreCase)) continue;
                var deadlineIdRaw = dc.DeadlineId?.Trim() ?? string.Empty;
                if (IsValidId(deadlineIdRaw) && deadlineMap.IdSet.Contains(deadlineIdRaw))
                    continue;

                if (!string.IsNullOrWhiteSpace(dc.DeadlineName))
                {
                    var existingId = Resolve("DeadlineConstraintChanges.DeadlineName", dc.DeadlineName, deadlineMap, result);
                    if (IsValidId(existingId) && deadlineMap.IdSet.Contains(existingId))
                    {
                        if (!string.Equals(dc.DeadlineId, existingId, StringComparison.OrdinalIgnoreCase))
                            result.PatchLog.Add($"[Canonicalize] DeadlineConstraintChanges.DeadlineId 按名称命中: {dc.DeadlineName} → {existingId}");
                        dc.DeadlineId = existingId;
                        continue;
                    }
                }

                var nameKey = !string.IsNullOrWhiteSpace(dc.DeadlineName) ? dc.DeadlineName.Trim() : deadlineIdRaw;
                if (string.IsNullOrWhiteSpace(nameKey)) continue;
                if (string.IsNullOrWhiteSpace(dc.DeadlineName)) dc.DeadlineName = nameKey;

                var seedKey = StripAnnotations(nameKey).Trim();
                if (string.IsNullOrWhiteSpace(seedKey)) seedKey = nameKey.Trim();
                seedKey = seedKey.ToLowerInvariant();

                if (!newDeadlineIdsByName.TryGetValue(nameKey, out var newId))
                {
                    newId = ShortIdGenerator.NewDeterministic("DL", $"DEADLINE|{seedKey}");
                    newDeadlineIdsByName[nameKey] = newId;
                }

                if (!string.Equals(dc.DeadlineId, newId, StringComparison.OrdinalIgnoreCase))
                    result.PatchLog.Add($"[Canonicalize] DeadlineConstraintChanges.DeadlineId 自动生成: {nameKey} → {newId}");
                dc.DeadlineId = newId;
                result.AmbiguousFields.RemoveAll(a => a.Contains($"'{nameKey}'"));
            }

            var newLocIdsByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string? ResolveOrCreateLocationId(string? idRaw, string? locationName, string fieldLabel)
            {
                var id = idRaw?.Trim() ?? string.Empty;
                if (IsValidId(id) && locMap.IdSet.Contains(id))
                    return id;

                if (!string.IsNullOrWhiteSpace(locationName))
                {
                    var existingId = Resolve($"{fieldLabel}.LocationName", locationName, locMap, result);
                    if (IsValidId(existingId) && locMap.IdSet.Contains(existingId))
                    {
                        if (!string.Equals(id, existingId, StringComparison.OrdinalIgnoreCase))
                            result.PatchLog.Add($"[Canonicalize] {fieldLabel} 按名称命中: {locationName} → {existingId}");
                        return existingId;
                    }
                }

                var nameKey = !string.IsNullOrWhiteSpace(locationName)
                    ? locationName.Trim()
                    : (string.IsNullOrWhiteSpace(id) ? null : id);
                if (nameKey == null)
                    return null;

                if (string.IsNullOrWhiteSpace(locationName) && IsValidId(id))
                {
                    result.PatchLog.Add($"[Canonicalize] {fieldLabel} 拒绝自动生成: ID '{id}' 为 ShortId 格式但不在账本且未提供 Name，保留原值由 Gate 报错");
                    return null;
                }

                var seedKey = StripAnnotations(nameKey).Trim();
                if (string.IsNullOrWhiteSpace(seedKey))
                    seedKey = nameKey.Trim();
                seedKey = seedKey.ToLowerInvariant();

                if (!newLocIdsByName.TryGetValue(nameKey, out var newId))
                {
                    newId = ShortIdGenerator.NewDeterministic("L", $"LOCATION|{seedKey}");
                    newLocIdsByName[nameKey] = newId;
                    locMap.IdSet.Add(newId);
                }

                result.PatchLog.Add($"[Canonicalize] {fieldLabel} 自动生成: {nameKey} → {newId}");
                result.AmbiguousFields.RemoveAll(a => a.Contains($"'{nameKey}'"));
                return newId;
            }

            foreach (var lc in canonical.LocationStateChanges ?? new())
            {
                var rawId = lc.LocationId;
                var rawName = lc.LocationName;
                var resolved = ResolveOrCreateLocationId(rawId, rawName, "LocationStateChanges");
                if (resolved != null)
                {
                    lc.LocationId = resolved;
                    if (string.IsNullOrWhiteSpace(lc.LocationName))
                    {
                        var nameKey = !string.IsNullOrWhiteSpace(rawName)
                            ? rawName.Trim()
                            : (rawId?.Trim() ?? string.Empty);
                        if (!string.IsNullOrWhiteSpace(nameKey) && !IsValidId(nameKey))
                            lc.LocationName = nameKey;
                    }
                }
            }

            foreach (var mv in canonical.CharacterMovements ?? new())
            {
                var rawTo = mv.ToLocation;
                var rawName = mv.ToLocationName;
                var resolved = ResolveOrCreateLocationId(rawTo, rawName, "CharacterMovements.ToLocation");
                if (resolved != null)
                {
                    mv.ToLocation = resolved;
                    if (string.IsNullOrWhiteSpace(mv.ToLocationName))
                    {
                        var nameKey = !string.IsNullOrWhiteSpace(rawName)
                            ? rawName.Trim()
                            : (rawTo?.Trim() ?? string.Empty);
                        if (!string.IsNullOrWhiteSpace(nameKey) && !IsValidId(nameKey))
                            mv.ToLocationName = nameKey;
                    }
                }
            }

            if (newLocIdsByName.Count > 0)
            {
                var existingLocIds = new HashSet<string>(
                    (canonical.LocationStateChanges ?? new()).Select(lc => lc.LocationId),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var (name, id) in newLocIdsByName)
                {
                    if (!existingLocIds.Contains(id))
                    {
                        canonical.LocationStateChanges ??= new();
                        canonical.LocationStateChanges.Add(new LocationStateChange
                        {
                            LocationId = id,
                            LocationName = name,
                            NewStatus = "active",
                            Event = string.Empty,
                            Importance = "normal"
                        });
                        result.PatchLog.Add($"[Canonicalize] 自动补充 LocationStateChange 以确保名称入账: {name} → {id}");
                    }
                }
            }

            canonical.CharacterStateChanges = StripByPrimaryId(canonical.CharacterStateChanges, x => x.CharacterId, "CharacterStateChanges", result, charMap.IdSet);
            canonical.ConflictProgress = StripByPrimaryId(canonical.ConflictProgress, x => x.ConflictId, "ConflictProgress", result, conflictMap.IdSet);
            canonical.ForeshadowingActions = StripByPrimaryId(canonical.ForeshadowingActions, x => x.ForeshadowId, "ForeshadowingActions", result, foreshadowMap.IdSet);
            canonical.LocationStateChanges = StripByPrimaryId(canonical.LocationStateChanges, x => x.LocationId, "LocationStateChanges", result);
            canonical.FactionStateChanges = StripByPrimaryId(canonical.FactionStateChanges, x => x.FactionId, "FactionStateChanges", result, factionMap.IdSet);
            canonical.CharacterMovements = StripByPrimaryId(canonical.CharacterMovements, x => x.CharacterId, "CharacterMovements", result, charMap.IdSet);
            canonical.ItemTransfers = StripByPrimaryId(canonical.ItemTransfers, x => x.ItemId, "ItemTransfers", result);
            canonical.SecretRevealChanges = StripByPrimaryId(canonical.SecretRevealChanges, x => x.SecretId, "SecretRevealChanges", result);
            canonical.PledgeConstraintChanges = StripByPrimaryId(canonical.PledgeConstraintChanges, x => x.PledgeId, "PledgeConstraintChanges", result);
            canonical.DeadlineConstraintChanges = StripByPrimaryId(canonical.DeadlineConstraintChanges, x => x.DeadlineId, "DeadlineConstraintChanges", result);

            canonical.CharacterMovements = canonical.CharacterMovements
                ?.Where(mv =>
                {
                    var to = mv.ToLocation?.Trim() ?? "";
                    if (!IsValidId(to))
                    {
                        result.PatchLog.Add($"[Canonicalize] CharacterMovements: 剔除ToLocation格式非法的条目 CharacterId='{mv.CharacterId}' ToLocation='{mv.ToLocation}'");
                        return false;
                    }
                    return true;
                }).ToList() ?? new();

            if ((canonical.CharacterMovements?.Count ?? 0) > 1)
            {
                foreach (var grp in canonical.CharacterMovements!
                    .GroupBy(m => m.CharacterId)
                    .Where(g => g.Count() > 1))
                {
                    var movs = grp.ToList();
                    for (int i = 1; i < movs.Count; i++)
                    {
                        var prev = movs[i - 1];
                        if (ShortIdGenerator.IsLikelyId(prev.ToLocation) && movs[i].FromLocation != prev.ToLocation)
                        {
                            result.PatchLog.Add($"[Canonicalize] CharacterMovements 链修正 {movs[i].CharacterId}[{i}]: FromLocation '{movs[i].FromLocation}'→'{prev.ToLocation}'");
                            movs[i].FromLocation = prev.ToLocation;
                        }
                    }
                }
            }

            foreach (var ch in canonical.CharacterStateChanges)
            {
                if (ch.RelationshipChanges is not { Count: > 0 }) continue;
                foreach (var (key, rel) in ch.RelationshipChanges)
                {
                    if (Math.Abs(rel.TrustDelta) > TrustDeltaMax)
                    {
                        var clamped = Math.Sign(rel.TrustDelta) * TrustDeltaMax;
                        result.PatchLog.Add($"[Canonicalize] TrustDelta 夹断 {ch.CharacterId}→{key}: {rel.TrustDelta}→{clamped}");
                        rel.TrustDelta = clamped;
                    }
                }
            }

            bool charLedgerHasIds = charMap.IdSet.Count > 0;
            foreach (var pp in canonical.NewPlotPoints ?? new())
                pp.InvolvedCharacters = pp.InvolvedCharacters
                    ?.Select(id => Resolve("NewPlotPoints.InvolvedCharacters", id, charMap, result))
                    .Where(t =>
                    {
                        if (string.IsNullOrWhiteSpace(t)) return false;
                        if (!ShortIdGenerator.IsLikelyId(t))
                        {
                            result.PatchLog.Add($"[Canonicalize] NewPlotPoints.InvolvedCharacters: 剔除非ShortId格式的值 '{t}'");
                            return false;
                        }
                        if (charLedgerHasIds && !charMap.IdSet.Contains(t))
                        {
                            result.PatchLog.Add($"[Canonicalize] NewPlotPoints.InvolvedCharacters: 剔除不在账本中的角色ID '{t}'（可能为伪造ShortId）");
                            return false;
                        }
                        return true;
                    }).ToList() ?? new();
            foreach (var sr in canonical.SecretRevealChanges)
                sr.NewKnowerIds = sr.NewKnowerIds
                    ?.Select(id => Resolve("SecretRevealChanges.NewKnowerIds", id, charMap, result))
                    .Where(t =>
                    {
                        if (string.IsNullOrWhiteSpace(t)) return false;
                        if (!ShortIdGenerator.IsLikelyId(t))
                        {
                            result.PatchLog.Add($"[Canonicalize] SecretRevealChanges.NewKnowerIds: 剔除非ShortId格式的值 '{t}'");
                            return false;
                        }
                        if (charLedgerHasIds && !charMap.IdSet.Contains(t))
                        {
                            result.PatchLog.Add($"[Canonicalize] SecretRevealChanges.NewKnowerIds: 剔除不在账本中的角色ID '{t}'（可能为伪造ShortId）");
                            return false;
                        }
                        return true;
                    }).ToList() ?? new();
            foreach (var pc in canonical.PledgeConstraintChanges ?? new())
                pc.PartyIds = pc.PartyIds
                    ?.Select(id => Resolve("PledgeConstraintChanges.PartyIds", id, charMap, result))
                    .Where(t =>
                    {
                        if (string.IsNullOrWhiteSpace(t)) return false;
                        if (!ShortIdGenerator.IsLikelyId(t))
                        {
                            result.PatchLog.Add($"[Canonicalize] PledgeConstraintChanges.PartyIds: 剔除非ShortId格式的值 '{t}'");
                            return false;
                        }
                        if (charLedgerHasIds && !charMap.IdSet.Contains(t))
                        {
                            result.PatchLog.Add($"[Canonicalize] PledgeConstraintChanges.PartyIds: 剔除不在账本中的角色ID '{t}'（可能为伪造ShortId）");
                            return false;
                        }
                        return true;
                    }).ToList() ?? new();
            foreach (var dc in canonical.DeadlineConstraintChanges ?? new())
                dc.PartyIds = dc.PartyIds
                    ?.Select(id => Resolve("DeadlineConstraintChanges.PartyIds", id, charMap, result))
                    .Where(t =>
                    {
                        if (string.IsNullOrWhiteSpace(t)) return false;
                        if (!ShortIdGenerator.IsLikelyId(t))
                        {
                            result.PatchLog.Add($"[Canonicalize] DeadlineConstraintChanges.PartyIds: 剔除非ShortId格式的值 '{t}'");
                            return false;
                        }
                        if (charLedgerHasIds && !charMap.IdSet.Contains(t))
                        {
                            result.PatchLog.Add($"[Canonicalize] DeadlineConstraintChanges.PartyIds: 剔除不在账本中的角色ID '{t}'（可能为伪造ShortId）");
                            return false;
                        }
                        return true;
                    }).ToList() ?? new();

            int stripped = 0;
            stripped += StripEmpty(canonical.CharacterStateChanges, c =>
                Blank(c.KeyEvent) && Blank(c.NewLevel) && Blank(c.NewMentalState)
                && (c.NewAbilities == null || c.NewAbilities.Count == 0)
                && (c.LostAbilities == null || c.LostAbilities.Count == 0)
                && (c.RelationshipChanges == null || c.RelationshipChanges.Count == 0));
            stripped += StripEmpty(canonical.ConflictProgress, c => Blank(c.NewStatus) && Blank(c.Event));
            stripped += StripEmpty(canonical.LocationStateChanges, c => Blank(c.NewStatus) && Blank(c.Event));
            stripped += StripEmpty(canonical.FactionStateChanges, c => Blank(c.NewStatus) && Blank(c.Event));
            stripped += StripEmpty(canonical.CharacterMovements, c => Blank(c.ToLocation));
            stripped += StripEmpty(canonical.ForeshadowingActions, c => Blank(c.Action));
            stripped += StripEmpty(canonical.NewPlotPoints, c => Blank(c.Context) && (c.Keywords == null || c.Keywords.Count == 0));

            if (stripped > 0)
                result.PatchLog.Add($"[Canonicalize] 剔除{stripped}条值字段全空的预填条目");

            return result;
        }

        private static bool Blank(string? s) => string.IsNullOrWhiteSpace(s);

        private static int StripEmpty<T>(List<T>? list, Func<T, bool> isEmpty)
        {
            if (list == null || list.Count == 0) return 0;
            int before = list.Count;
            list.RemoveAll(x => isEmpty(x));
            return before - list.Count;
        }

        private static string Resolve(string field, string? value, EntityLookup lookup, CanonicalizationResult result)
        {
            if (string.IsNullOrWhiteSpace(value)) return value ?? string.Empty;
            var v = value.Trim();

            if (ShortIdGenerator.IsLikelyId(v) && lookup.IdSet.Contains(v))
                return v;

            var m = BracketIdPattern.Match(v);
            if (m.Success)
            {
                var extracted = m.Groups[1].Value;
                if (lookup.IdSet.Contains(extracted))
                {
                    result.PatchLog.Add($"[Canonicalize] {field}: 括号提取 '{v}' → '{extracted}'");
                    return extracted;
                }
            }

            if (lookup.NameToId.TryGetValue(v, out var byName))
            {
                result.PatchLog.Add($"[Canonicalize] {field}: 名称→ID '{v}' → '{byName}'");
                return byName;
            }

            var stripped = StripAnnotations(v);
            if (stripped != v && lookup.NameToId.TryGetValue(stripped, out var byStripped))
            {
                result.PatchLog.Add($"[Canonicalize] {field}: 去注释→ID '{v}' → '{byStripped}'");
                return byStripped;
            }

            result.AmbiguousFields.Add(
                ShortIdGenerator.IsLikelyId(v)
                    ? $"{field}: '{v}' 格式正确但不在账本中（可能为伪造ID）"
                    : $"{field}: '{v}' 无名称匹配");
            return v;
        }

        private static bool IsValidId(string? v)
            => !string.IsNullOrWhiteSpace(v) && ShortIdGenerator.IsLikelyId(v.Trim());

        private static string StripAnnotations(string v)
        {
            var r = BracketContentRegex.Replace(v, "").Trim();
            return string.IsNullOrWhiteSpace(r) ? v : r;
        }

        private static EntityLookup BuildCharacterMap(FactSnapshot s)
        {
            var lu = new EntityLookup();
            foreach (var e in s.CharacterStates ?? Enumerable.Empty<CharacterStateSnapshot>())
                lu.Add(e.Id, e.Name);
            foreach (var (id, desc) in s.CharacterDescriptions ?? new())
                lu.Add(id, desc.Name);
            foreach (var cl in s.CharacterLocations ?? Enumerable.Empty<CharacterLocationSnapshot>())
            {
                if (string.IsNullOrWhiteSpace(cl.CharacterId)) continue;
                string? name = cl.CharacterName;
                if (string.IsNullOrWhiteSpace(name) && s.CharacterDescriptions?.TryGetValue(cl.CharacterId.Trim(), out var d) == true)
                    name = d?.Name;
                lu.Add(cl.CharacterId.Trim(), name);
            }
            return lu;
        }

        private static EntityLookup BuildLocationMap(FactSnapshot s)
        {
            var lu = new EntityLookup();
            foreach (var e in s.LocationStates ?? Enumerable.Empty<LocationStateSnapshot>())
                lu.Add(e.Id, e.Name);
            foreach (var (id, desc) in s.LocationDescriptions ?? new())
                lu.Add(id, desc.Name);
            foreach (var cl in s.CharacterLocations ?? Enumerable.Empty<CharacterLocationSnapshot>())
            {
                if (string.IsNullOrWhiteSpace(cl.CurrentLocation)) continue;
                var locId = cl.CurrentLocation.Trim();
                string? locName = null;
                if (s.LocationDescriptions?.TryGetValue(locId, out var ld) == true)
                    locName = ld?.Name;
                lu.Add(locId, locName);
            }
            return lu;
        }

        private static EntityLookup BuildFactionMap(FactSnapshot s)
        {
            var lu = new EntityLookup();
            foreach (var e in s.FactionStates ?? Enumerable.Empty<FactionStateSnapshot>())
                lu.Add(e.Id, e.Name);
            return lu;
        }

        private static EntityLookup BuildItemMap(FactSnapshot s)
        {
            var lu = new EntityLookup();
            foreach (var e in s.ItemStates ?? Enumerable.Empty<ItemStateSnapshot>())
                lu.Add(e.Id, e.Name);
            return lu;
        }

        private static EntityLookup BuildConflictMap(FactSnapshot s)
        {
            var lu = new EntityLookup();
            foreach (var e in s.ConflictProgress ?? Enumerable.Empty<ConflictProgressSnapshot>())
                lu.Add(e.Id, e.Name);
            return lu;
        }

        private static EntityLookup BuildForeshadowMap(FactSnapshot s)
        {
            var lu = new EntityLookup();
            foreach (var e in s.ForeshadowingStatus ?? Enumerable.Empty<ForeshadowingStatusSnapshot>())
                lu.Add(e.Id, e.Name);
            return lu;
        }

        private static EntityLookup BuildSecretMap(FactSnapshot s)
        {
            var lu = new EntityLookup();
            foreach (var e in s.SecretStates ?? Enumerable.Empty<SecretStateSnapshot>())
                lu.Add(e.Id, e.Name);
            return lu;
        }

        private static EntityLookup BuildPledgeMap(FactSnapshot s)
        {
            var lu = new EntityLookup();
            foreach (var e in s.PledgeStates ?? Enumerable.Empty<PledgeStateSnapshot>())
                lu.Add(e.Id, e.Name);
            return lu;
        }

        private static EntityLookup BuildDeadlineMap(FactSnapshot s)
        {
            var lu = new EntityLookup();
            foreach (var e in s.DeadlineStates ?? Enumerable.Empty<DeadlineStateSnapshot>())
                lu.Add(e.Id, e.Name);
            return lu;
        }

        private static Dictionary<string, string> BuildCharCurrentLocMap(FactSnapshot s)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cl in s.CharacterLocations ?? Enumerable.Empty<CharacterLocationSnapshot>())
                if (!string.IsNullOrWhiteSpace(cl.CharacterId) && !string.IsNullOrWhiteSpace(cl.CurrentLocation))
                    d.TryAdd(cl.CharacterId, cl.CurrentLocation);
            return d;
        }

        private static Dictionary<string, string> BuildItemCurrentHolderMap(FactSnapshot s)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in s.ItemStates ?? Enumerable.Empty<ItemStateSnapshot>())
                if (!string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.CurrentHolder))
                    d.TryAdd(item.Id, item.CurrentHolder);
            return d;
        }

        private static readonly IReadOnlyList<string> DefaultConflictStatusSequence =
            new[] { "pending", "active", "climax", "resolved" };

        private static Dictionary<string, ForeshadowingStatusSnapshot> BuildForeshadowStateMap(FactSnapshot s)
        {
            var d = new Dictionary<string, ForeshadowingStatusSnapshot>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in s.ForeshadowingStatus ?? Enumerable.Empty<ForeshadowingStatusSnapshot>())
                if (!string.IsNullOrWhiteSpace(f.Id)) d[f.Id] = f;
            return d;
        }

        private static Dictionary<string, string> BuildConflictCurrentStatusMap(FactSnapshot s)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in s.ConflictProgress ?? Enumerable.Empty<ConflictProgressSnapshot>())
                if (!string.IsNullOrWhiteSpace(c.Id) && !string.IsNullOrWhiteSpace(c.Status))
                    d[c.Id] = c.Status.Trim().ToLowerInvariant();
            return d;
        }

        private static List<ForeshadowingAction> CanonicalizeForeshadowingActions(
            List<ForeshadowingAction> actions,
            Dictionary<string, ForeshadowingStatusSnapshot> stateMap,
            CanonicalizationResult result)
        {
            var kept = new List<ForeshadowingAction>();
            foreach (var fa in actions)
            {
                var id = fa.ForeshadowId?.Trim() ?? "";
                if (!ShortIdGenerator.IsLikelyId(id))
                {
                    kept.Add(fa);
                    continue;
                }
                if (!stateMap.TryGetValue(id, out var st))
                {
                    kept.Add(fa);
                    continue;
                }
                var act = fa.Action?.ToLowerInvariant() ?? "";
                if (st.IsResolved && act == "setup")
                {
                    result.PatchLog.Add($"[Canonicalize] ForeshadowingActions: 剔除已揭示伏笔的 re-setup {id}（ForeshadowingRollback 预防）");
                    continue;
                }
                if (st.IsSetup && act == "setup")
                {
                    result.PatchLog.Add($"[Canonicalize] ForeshadowingActions: 剔除已埋设伏笔的重复 setup {id}");
                    continue;
                }
                if (!st.IsSetup && act == "payoff")
                {
                    result.PatchLog.Add($"[Canonicalize] ForeshadowingActions: 剔除未埋设伏笔的 payoff {id}（PayoffBeforeSetup 预防）");
                    continue;
                }
                kept.Add(fa);
            }
            return kept;
        }

        private static List<ConflictProgressChange> CanonicalizeConflictProgress(
            List<ConflictProgressChange> progresses,
            Dictionary<string, string> currentStatusMap,
            CanonicalizationResult result)
        {
            var seqIdx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < DefaultConflictStatusSequence.Count; i++)
                seqIdx[DefaultConflictStatusSequence[i]] = i;

            var kept = new List<ConflictProgressChange>();
            foreach (var cp in progresses)
            {
                var id = cp.ConflictId?.Trim() ?? "";
                if (!ShortIdGenerator.IsLikelyId(id) || !currentStatusMap.TryGetValue(id, out var curStatus))
                {
                    kept.Add(cp);
                    continue;
                }
                var ns = cp.NewStatus?.Trim().ToLowerInvariant() ?? "";
                if (seqIdx.TryGetValue(curStatus, out var ci) && seqIdx.TryGetValue(ns, out var ni) && ni < ci)
                {
                    result.PatchLog.Add($"[Canonicalize] ConflictProgress: 剔除状态回退 {id} {curStatus}→{ns}（ConflictStatusSkip 预防）");
                    continue;
                }
                kept.Add(cp);
            }
            return kept;
        }

        private static List<T> StripByPrimaryId<T>(
            List<T>? list, Func<T, string?> getId, string listName, CanonicalizationResult result,
            HashSet<string>? knownIds = null)
        {
            if (list == null || list.Count == 0) return new();
            var kept = new List<T>();
            foreach (var item in list)
            {
                var id = getId(item)?.Trim();
                if (string.IsNullOrWhiteSpace(id) || !ShortIdGenerator.IsLikelyId(id))
                    result.PatchLog.Add($"[Canonicalize] {listName}: 剔除主ID仍为名称的条目 '{id}'");
                else if (knownIds != null && !knownIds.Contains(id))
                    result.PatchLog.Add($"[Canonicalize] {listName}: 剔除主ID不在账本中的条目 '{id}'（{(knownIds.Count == 0 ? "账本无追踪记录" : "可能为伪造ShortId")}）");
                else
                    kept.Add(item);
            }
            return kept;
        }

        private static ChapterChanges DeepCopy(ChapterChanges src)
        {
            var json = JsonSerializer.Serialize(src);
            return JsonSerializer.Deserialize<ChapterChanges>(json) ?? new ChapterChanges();
        }
    }

    internal class EntityLookup
    {
        private readonly HashSet<string> _ambiguous = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> IdSet { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> NameToId { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string? id, string? name)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            IdSet.Add(id);

            if (string.IsNullOrWhiteSpace(name)) return;
            var n = name.Trim();

            if (_ambiguous.Contains(n)) return;

            if (NameToId.TryGetValue(n, out var existingId) && existingId != id)
            {
                NameToId.Remove(n);
                _ambiguous.Add(n);
            }
            else if (!NameToId.ContainsKey(n))
            {
                NameToId[n] = id;
            }
        }
    }
}
