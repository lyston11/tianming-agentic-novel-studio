using System;
using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Models.Tracking
{
    public static class ConsistencyIssueRegistry
    {
        private static readonly System.Text.RegularExpressions.Regex ExpectedLocationRegex = new(@"期望:\s*从已知位置\s*([^\s,]+)\s*出发", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex ExpectedHolderRegex = new(@"期望:\s*物品由当前持有者\s*([^\s,]+)\s*转让", System.Text.RegularExpressions.RegexOptions.Compiled);

        [Flags]
        public enum BaselineScope
        {
            None = 0,
            Location = 1 << 0,
            Foreshadow = 1 << 1,
            Semantic = 1 << 2,
        }

        public sealed class Descriptor
        {
            public bool IsChangesOnly { get; init; }

            public BaselineScope Scope { get; init; }

            public Func<string, string, string> Humanize { get; init; } = (_, e) => e;
        }

        public static readonly IReadOnlyDictionary<string, Descriptor> All =
            new Dictionary<string, Descriptor>(StringComparer.OrdinalIgnoreCase)
            {
                [IssueTypes.ForeshadowingRollback] = new()
                {
                    IsChangesOnly = true,
                    Scope = BaselineScope.Foreshadow,
                    Humanize = (name, _) => $"伏笔'{name}'已揭示，不能重新埋设。请移除该伏笔的埋设动作",
                },
                [IssueTypes.PayoffBeforeSetup] = new()
                {
                    IsChangesOnly = true,
                    Scope = BaselineScope.Foreshadow,
                    Humanize = (name, _) => $"伏笔'{name}'尚未埋设，不能在本章揭示。请先在正文中自然埋设该伏笔，或移除揭示动作",
                },
                [IssueTypes.ConflictStatusSkip] = new()
                {
                    IsChangesOnly = true,
                    Scope = BaselineScope.None,
                    Humanize = (name, _) => $"冲突'{name}'的状态不能回退。请检查NewStatus是否正确，冲突只能向前推进",
                },
                [IssueTypes.CharacterNotInvolved] = new()
                {
                    IsChangesOnly = true,
                    Scope = BaselineScope.None,
                    Humanize = (name, _) => $"角色'{name}'不在本章涉及角色列表中，但出现在CHANGES里。请将该角色从CHANGES中移除（仅删除CHANGES中涉及该角色的条目，不要修改正文）",
                },
                [IssueTypes.LevelRegression] = new()
                {
                    IsChangesOnly = true,
                    Scope = BaselineScope.Semantic,
                    Humanize = (name, _) => $"角色'{name}'等级/阶段出现回退。除非正文明确发生失去/降级事件，否则不要让新的等级/阶段低于账本记录",
                },
                [IssueTypes.AbilityLossWithoutEvent] = new()
                {
                    IsChangesOnly = true,
                    Scope = BaselineScope.Semantic,
                    Humanize = (name, _) => $"角色'{name}'声明失去能力但缺少原因。若存在失去能力记录，关键事件必须写清楚失去原因（封印/废除/代价等）",
                },
                [IssueTypes.TrustDeltaExceedsLimit] = new()
                {
                    IsChangesOnly = true,
                    Scope = BaselineScope.Semantic,
                    Humanize = (name, _) => $"角色'{name}'与他人的信任值变化幅度过大。请把信任值变化控制在合理范围（默认±30以内），并用关键事件说明关系变化原因",
                },
                [IssueTypes.RelationshipContradiction] = new()
                {
                    IsChangesOnly = true,
                    Scope = BaselineScope.Semantic,
                    Humanize = (name, _) => $"角色'{name}'与他人关系申报矛盾：同一章内同一对角色不能同时声明盟友与仇敌。请统一关系结论并只保留一种",
                },
                [IssueTypes.MovementStartLocationMismatch] = new()
                {
                    IsChangesOnly = true,
                    Scope = BaselineScope.Location,
                    Humanize = (name, error) =>
                    {
                        var m = ExpectedLocationRegex.Match(error);
                        var rawLoc = m.Success ? m.Groups[1].Value : string.Empty;
                        var expectedLoc = !string.IsNullOrWhiteSpace(rawLoc) ? EntityNameResolver.Resolve(rawLoc) : "账本记录位置";
                        return $"角色'{name}'的出发地点必须是账本当前位置：{expectedLoc}。请修正角色移动中的出发地点，或不要为该角色申报移动";
                    },
                },
                [IssueTypes.MovementChainBreak] = new()
                {
                    IsChangesOnly = true,
                    Scope = BaselineScope.Location,
                    Humanize = (name, _) => $"角色'{name}'本章多次移动时路径不连续：上一次到达地点必须等于下一次出发地点。请修正移动链或减少移动次数",
                },
                [IssueTypes.ItemOwnershipMismatch] = new()
                {
                    IsChangesOnly = true,
                    Scope = BaselineScope.Location,
                    Humanize = (name, error) =>
                    {
                        var m = ExpectedHolderRegex.Match(error);
                        var rawHolder = m.Success ? m.Groups[1].Value : string.Empty;
                        var expectedHolder = !string.IsNullOrWhiteSpace(rawHolder) ? EntityNameResolver.ResolveCharacter(rawHolder) : "账本持有者";
                        return $"物品'{name}'的转让起点不符：当前持有者应为'{expectedHolder}'。请修正物品流转中的原持有者，或不要申报该物品转让";
                    },
                },
                [IssueTypes.PledgeDuplicateCreate] = new()
                {
                    IsChangesOnly = true,
                    Scope = BaselineScope.None,
                    Humanize = (name, _) => $"承诺/契约'{name}'已存在于账本，不应重复 create。请将 create 改为 update，或移除重复条目",
                },
                [IssueTypes.PledgeTerminalActionConflict] = new()
                {
                    IsChangesOnly = true,
                    Scope = BaselineScope.None,
                    Humanize = (name, _) => $"承诺/契约'{name}'同章出现多个终止动作（fulfill/break 冲突）。同一章节只能有一个终止动作，请删除多余条目",
                },
                [IssueTypes.DeadlineDuplicateCreate] = new()
                {
                    IsChangesOnly = true,
                    Scope = BaselineScope.None,
                    Humanize = (name, _) => $"倒计时/时限'{name}'已存在于账本，不应重复 create。请将 create 改为 update，或移除重复条目",
                },
                [IssueTypes.DeadlineTerminalActionConflict] = new()
                {
                    IsChangesOnly = true,
                    Scope = BaselineScope.None,
                    Humanize = (name, _) => $"倒计时/时限'{name}'同章出现多个终止动作（trigger/expire/cancel 冲突）。同一章节只能有一个终止动作，请删除多余条目",
                },
                [IssueTypes.OmittedDeclaration] = new()
                {
                    IsChangesOnly = true,
                    Scope = BaselineScope.None,
                    Humanize = (name, error) => string.IsNullOrWhiteSpace(error)
                        ? $"实体'{name}'出现在正文但 CHANGES 未申报，请在重写时补全相应条目"
                        : error,
                },
            };
    }

    public static class IssueTypes
    {
        public const string ForeshadowingRollback = "ForeshadowingRollback";
        public const string PayoffBeforeSetup = "PayoffBeforeSetup";
        public const string ConflictStatusSkip = "ConflictStatusSkip";
        public const string CharacterNotInvolved = "CharacterNotInvolved";
        public const string LevelRegression = "LevelRegression";
        public const string AbilityLossWithoutEvent = "AbilityLossWithoutEvent";
        public const string TrustDeltaExceedsLimit = "TrustDeltaExceedsLimit";
        public const string RelationshipContradiction = "RelationshipContradiction";
        public const string MovementStartLocationMismatch = "MovementStartLocationMismatch";
        public const string MovementChainBreak = "MovementChainBreak";
        public const string ItemOwnershipMismatch = "ItemOwnershipMismatch";
        public const string PledgeDuplicateCreate = "PledgeDuplicateCreate";
        public const string PledgeTerminalActionConflict = "PledgeTerminalActionConflict";
        public const string DeadlineDuplicateCreate = "DeadlineDuplicateCreate";
        public const string DeadlineTerminalActionConflict = "DeadlineTerminalActionConflict";
        public const string OmittedDeclaration = "OmittedDeclaration";
    }
}
