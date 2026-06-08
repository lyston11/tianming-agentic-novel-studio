using System;
using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Models.Tracking
{
    public enum ShortIdEntityType
    {
        Character,
        Location,
        Conflict,
        Foreshadowing,
        Faction,
        Item,
        Secret,
        Pledge,
        Deadline
    }

    public sealed record ShortIdFieldMeta(
        string FieldPath,
        string ChineseName,
        ShortIdEntityType EntityType,
        bool Required = false
    );

    public static class ShortIdFieldRegistry
    {

        public const string CharStateCharacterId = "CharacterStateChanges[].CharacterId";
        public const string CharStateRelationshipKey = "CharacterStateChanges[].RelationshipChanges.key";
        public const string ConflictConflictId = "ConflictProgress[].ConflictId";
        public const string ForeshadowForeshadowId = "ForeshadowingActions[].ForeshadowId";
        public const string LocationLocationId = "LocationStateChanges[].LocationId";
        public const string FactionFactionId = "FactionStateChanges[].FactionId";
        public const string MovementCharacterId = "CharacterMovements[].CharacterId";
        public const string MovementFromLocation = "CharacterMovements[].FromLocation";
        public const string MovementToLocation = "CharacterMovements[].ToLocation";
        public const string ItemTransferItemId = "ItemTransfers[].ItemId";
        public const string ItemTransferFromHolder = "ItemTransfers[].FromHolder";
        public const string ItemTransferToHolder = "ItemTransfers[].ToHolder";
        public const string SecretSecretId = "SecretRevealChanges[].SecretId";
        public const string SecretNewKnowerId = "SecretRevealChanges[].NewKnowerIds[]";
        public const string PlotInvolvedCharacter = "NewPlotPoints[].InvolvedCharacters[]";
        public const string PledgePledgeId = "PledgeConstraintChanges[].PledgeId";
        public const string PledgePartyId = "PledgeConstraintChanges[].PartyIds[]";
        public const string DeadlineDeadlineId = "DeadlineConstraintChanges[].DeadlineId";
        public const string DeadlinePartyId = "DeadlineConstraintChanges[].PartyIds[]";

        public static readonly IReadOnlyList<ShortIdFieldMeta> All = new ShortIdFieldMeta[]
        {
            new(CharStateCharacterId,     "角色状态变更的角色ID",               ShortIdEntityType.Character,     Required: true),
            new(CharStateRelationshipKey, "角色状态变更中的关系对象角色ID",      ShortIdEntityType.Character,     Required: true),
            new(ConflictConflictId,       "冲突进度的冲突ID",                   ShortIdEntityType.Conflict,      Required: true),
            new(ForeshadowForeshadowId,   "伏笔动作的伏笔ID",                   ShortIdEntityType.Foreshadowing, Required: true),
            new(LocationLocationId,       "地点状态变化的地点ID",               ShortIdEntityType.Location,      Required: true),
            new(FactionFactionId,         "势力状态变化的势力ID",               ShortIdEntityType.Faction,       Required: true),
            new(MovementCharacterId,      "角色移动的角色ID",                   ShortIdEntityType.Character,     Required: true),
            new(MovementFromLocation,     "角色移动的出发地点ID",               ShortIdEntityType.Location,      Required: false),
            new(MovementToLocation,       "角色移动的到达地点ID",               ShortIdEntityType.Location,      Required: true),
            new(ItemTransferItemId,       "物品流转的物品ID",                   ShortIdEntityType.Item,          Required: true),
            new(ItemTransferFromHolder,   "物品流转的原持有者ID",               ShortIdEntityType.Character,     Required: false),
            new(ItemTransferToHolder,     "物品流转的新持有者ID",               ShortIdEntityType.Character,     Required: false),
            new(SecretSecretId,           "秘密知情变化的秘密ID",               ShortIdEntityType.Secret,        Required: true),
            new(SecretNewKnowerId,        "秘密知情变化的新增知情者ID列表",      ShortIdEntityType.Character,     Required: true),
            new(PlotInvolvedCharacter,    "新增情节的涉及角色ID列表",            ShortIdEntityType.Character,     Required: true),
            new(PledgePledgeId,           "承诺/契约变更的承诺ID",               ShortIdEntityType.Pledge,        Required: true),
            new(PledgePartyId,            "承诺/契约的涉及角色ID列表",          ShortIdEntityType.Character,     Required: false),
            new(DeadlineDeadlineId,       "倒计时/时限变更的倒计时ID",           ShortIdEntityType.Deadline,      Required: true),
            new(DeadlinePartyId,          "倒计时/时限的涉及角色ID列表",      ShortIdEntityType.Character,     Required: false),
        };

        private static readonly IReadOnlyDictionary<string, ShortIdFieldMeta> _byPath
            = BuildIndex();

        private static Dictionary<string, ShortIdFieldMeta> BuildIndex()
        {
            var d = new Dictionary<string, ShortIdFieldMeta>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in All) d[m.FieldPath] = m;
            return d;
        }

        public static ShortIdFieldMeta? GetMeta(string fieldPath)
            => string.IsNullOrWhiteSpace(fieldPath)
                ? null
                : (_byPath.TryGetValue(fieldPath.Trim(), out var m) ? m : null);

        public static bool TryGet(string fieldPath, out ShortIdFieldMeta meta)
        {
            meta = default!;
            if (string.IsNullOrWhiteSpace(fieldPath)) return false;
            return _byPath.TryGetValue(fieldPath.Trim(), out meta!);
        }

        public static string GetChineseName(string fieldPath)
            => GetMeta(fieldPath)?.ChineseName ?? fieldPath;

        public static ShortIdEntityType? GetEntityType(string fieldPath)
            => GetMeta(fieldPath)?.EntityType;
    }
}
