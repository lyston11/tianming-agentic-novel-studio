using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TM.Services.Modules.ProjectData.Implementations.Tracking.Rules;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class LedgerConsistencyChecker
    {
        private static readonly FactSnapshot StructuralValidationSnapshot = new();

        private static readonly Regex TierNumberRegex = new(@"tier\s*[-_]?\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PlainDigitsRegex = new(@"\d+", RegexOptions.Compiled);
        private static readonly Regex RomanNumeralRegex = new(@"\b[IVXLCDM]+\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ChineseNumberRegex = new(@"[零一二三四五六七八九十百千万两]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex GradeLevelRegex = new(@"\b(SSS|SS|S|A|B|C|D|E|F)\b|(?i)(SSS|SS|S|A|B|C|D|E|F)\s*(级|阶|段)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        #region 构造函数

        public LedgerConsistencyChecker() { }

        #endregion

        #region 公开方法

        public ConsistencyResult Validate(ChapterChanges changes, FactSnapshot factSnapshot)
        {
            return Validate(changes, factSnapshot, LedgerRuleSet.CreateUniversalDefault());
        }

        public ConsistencyResult ValidateStructuralOnly(ChapterChanges changes)
        {
            var result = new ConsistencyResult();
            if (changes == null) return result;
            ValidateTimelineConsistency(changes, StructuralValidationSnapshot, result);
            ValidateItemOwnershipConsistency(changes, StructuralValidationSnapshot, result);
            ValidateFactionRelationshipConsistency(changes, result);
            result.Success = !result.HasIssues;
            return result;
        }

        public ConsistencyResult Validate(ChapterChanges changes, FactSnapshot factSnapshot, LedgerRuleSet ruleSet)
        {
            var result = new ConsistencyResult();

            if (changes == null)
            {
                result.AddIssue("", "NullChanges", "CHANGES对象不为空", "CHANGES对象为空");
                return result;
            }

            ValidateForeshadowingConsistency(changes, factSnapshot, result);

            ValidateConflictConsistency(changes, factSnapshot, result, ruleSet);

            ValidateCharacterConsistency(changes, factSnapshot, result, ruleSet);

            ValidatePledgeConstraintConsistency(changes, factSnapshot, result, ruleSet);

            ValidateDeadlineConstraintConsistency(changes, factSnapshot, result, ruleSet);

            var chapterLocations = new HashSet<string>(
                factSnapshot.LocationDescriptions?.Keys ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            foreach (var l in factSnapshot.LocationStates ?? new List<LocationStateSnapshot>())
            {
                if (!string.IsNullOrWhiteSpace(l.Id))
                    chapterLocations.Add(l.Id);
            }

            var chapterCharacters = new HashSet<string>(
                factSnapshot.CharacterDescriptions?.Keys ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            foreach (var c in factSnapshot.CharacterStates ?? new List<CharacterStateSnapshot>())
            {
                if (!string.IsNullOrWhiteSpace(c.Id))
                    chapterCharacters.Add(c.Id);
            }

            ValidateTimelineConsistency(changes, factSnapshot, result, chapterLocations);

            ValidateItemOwnershipConsistency(changes, factSnapshot, result, chapterCharacters);

            ValidateFactionRelationshipConsistency(changes, result);

            result.Success = !result.HasIssues;
            return result;
        }

        #endregion

        #region V1

        private void ValidateForeshadowingConsistency(
            ChapterChanges changes,
            FactSnapshot factSnapshot,
            ConsistencyResult result)
        {
            if (changes.ForeshadowingActions == null || changes.ForeshadowingActions.Count == 0)
                return;

            foreach (var action in changes.ForeshadowingActions)
            {
                if (string.IsNullOrEmpty(action.ForeshadowId))
                    continue;

                var existing = factSnapshot.ForeshadowingStatus
                    .FirstOrDefault(f => f.Id == action.ForeshadowId);

                if (existing == null)
                {
                    continue;
                }

                var actionLower = action.Action?.ToLowerInvariant() ?? "";

                if (existing.IsResolved && actionLower == "setup")
                {
                    result.AddIssue(new ConsistencyIssue
                    {
                        EntityId = action.ForeshadowId,
                        IssueType = "ForeshadowingRollback",
                        Expected = "已揭示状态不可回退",
                        Actual = "尝试重新埋设"
                    });
                }

                if (actionLower == "payoff" && !existing.IsSetup)
                {
                    result.AddIssue(new ConsistencyIssue
                    {
                        EntityId = action.ForeshadowId,
                        IssueType = "PayoffBeforeSetup",
                        Expected = "先埋设后揭示",
                        Actual = "未埋设即揭示"
                    });
                }
            }
        }

        #endregion

        #region V2

        private void ValidateConflictConsistency(
            ChapterChanges changes,
            FactSnapshot factSnapshot,
            ConsistencyResult result,
            LedgerRuleSet ruleSet)
        {
            if (ruleSet == null || !ruleSet.EnableConflictFlowCheck || ruleSet.ConflictStatusSequence == null || ruleSet.ConflictStatusSequence.Count < 2)
                return;

            if (changes.ConflictProgress == null || changes.ConflictProgress.Count == 0)
                return;

            var statusIndexMap = BuildStatusIndexMap(ruleSet.ConflictStatusSequence);

            foreach (var progress in changes.ConflictProgress)
            {
                if (string.IsNullOrEmpty(progress.ConflictId))
                    continue;

                var existing = factSnapshot.ConflictProgress
                    .FirstOrDefault(c => c.Id == progress.ConflictId);

                if (existing == null)
                {
                    continue;
                }

                var currentStatus = NormalizeConflictStatus(existing.Status);
                var newStatus = NormalizeConflictStatus(progress.NewStatus);

                if (string.IsNullOrWhiteSpace(currentStatus) || string.IsNullOrWhiteSpace(newStatus))
                    continue;

                if (currentStatus == newStatus)
                    continue;

                if (!statusIndexMap.TryGetValue(currentStatus, out var oldIndex) ||
                    !statusIndexMap.TryGetValue(newStatus, out var newIndex))
                {
                    continue;
                }

                if (newIndex < oldIndex)
                {
                    result.AddIssue(new ConsistencyIssue
                    {
                        EntityId = progress.ConflictId,
                        IssueType = "ConflictStatusSkip",
                        Expected = $"冲突状态不可回退（{currentStatus} → {newStatus}）",
                        Actual = $"尝试回退到 {newStatus}"
                    });
                }
            }
        }

        private static Dictionary<string, int> BuildStatusIndexMap(IReadOnlyList<string> sequence)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (sequence == null || sequence.Count < 2)
            {
                return map;
            }

            foreach (var item in sequence)
            {
                var s = NormalizeConflictStatus(item);
                if (string.IsNullOrWhiteSpace(s))
                {
                    continue;
                }

                if (!map.ContainsKey(s))
                {
                    map[s] = map.Count;
                }
            }

            return map;
        }

        private static string NormalizeConflictStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return string.Empty;
            }

            var s = status.Trim();
            var lower = s.ToLowerInvariant();

            if (lower is "pending" or "todo" or "new" or "open")
            {
                return "pending";
            }

            if (lower is "active" or "ongoing" or "running" or "inprogress" or "in_progress")
            {
                return "active";
            }

            if (lower is "climax" or "peak" or "burst" or "explosion")
            {
                return "climax";
            }

            if (lower is "resolved" or "done" or "closed" or "finished" or "complete" or "completed")
            {
                return "resolved";
            }

            if (s.Contains('未') && (s.Contains("开始") || s.Contains("触发") || s.Contains("启动") || s.Contains("发生")))
            {
                return "pending";
            }

            if (s.Contains("进行") || s.Contains("推进") || s.Contains("发展") || s.Contains("展开") || s.Contains("激活") || s.Contains("触发") || s.Contains("开启") || s.Contains("进展") || s.Contains("持续") || s.Contains("继续") || s.Contains("升级"))
            {
                return "active";
            }

            if (s.Contains("高潮") || s.Contains("爆发") || s.Contains("决战") || s.Contains("决裂") || s.Contains("临界") || s.Contains("白热化") || s.Contains("最高点") || s.Contains("紧要关头") || s.Contains("危急"))
            {
                return "climax";
            }

            if (s.Contains("解决") || s.Contains("结束") || s.Contains("完结") || s.Contains("收束") || s.Contains("已结") || s.Contains("闭环") || s.Contains("落幕") || s.Contains("告终") || s.Contains("平息") || s.Contains("化解") || s.Contains("消弭") || s.Contains("了结"))
            {
                return "resolved";
            }

            return lower;
        }

        #endregion

        #region V3

        private void ValidateCharacterConsistency(
            ChapterChanges changes,
            FactSnapshot factSnapshot,
            ConsistencyResult result,
            LedgerRuleSet ruleSet)
        {
            if (changes.CharacterStateChanges == null || changes.CharacterStateChanges.Count == 0)
                return;

            var involvedCharacterIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var states = factSnapshot.CharacterStates;
            if (states == null)
            {
                TM.App.Log("[LedgerConsistencyChecker] CharacterStates is null");
                foreach (var change in changes.CharacterStateChanges)
                {
                    if (string.IsNullOrWhiteSpace(change.CharacterId))
                        continue;

                    result.AddIssue(new ConsistencyIssue
                    {
                        EntityId = change.CharacterId,
                        IssueType = "CharacterNotInvolved",
                        Expected = "本章涉及角色列表应可用（FactSnapshot.CharacterStates 不应为 null）",
                        Actual = "FactSnapshot.CharacterStates 为 null"
                    });
                }
                return;
            }

            foreach (var c in states)
            {
                if (!string.IsNullOrWhiteSpace(c.Id)) involvedCharacterIds.Add(c.Id);
            }

            if (factSnapshot.CharacterDescriptions != null)
            {
                foreach (var kv in factSnapshot.CharacterDescriptions)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Key)) involvedCharacterIds.Add(kv.Key);
                }
            }

            if (involvedCharacterIds.Count == 0)
            {
                TM.App.Log("[LedgerConsistencyChecker] V3: no characters extracted");
                foreach (var change in changes.CharacterStateChanges)
                {
                    if (string.IsNullOrWhiteSpace(change.CharacterId))
                        continue;

                    result.AddIssue(new ConsistencyIssue
                    {
                        EntityId = change.CharacterId,
                        IssueType = "CharacterNotInvolved",
                        Expected = "角色必须在章节上下文中定义",
                        Actual = "上下文角色集合为空"
                    });
                }

                return;
            }

            foreach (var change in changes.CharacterStateChanges)
            {
                if (string.IsNullOrEmpty(change.CharacterId))
                    continue;

                if (!involvedCharacterIds.Contains(change.CharacterId))
                {
                    var charName = EntityNameResolver.ResolveCharacter(change.CharacterId);
                    result.AddIssue(new ConsistencyIssue
                    {
                        EntityId = change.CharacterId,
                        IssueType = "CharacterNotInvolved",
                        Expected = "CHANGES中的角色应在本章涉及角色列表中",
                        Actual = $"角色 {charName} 不在本章涉及角色中"
                    });
                }

                var existing = states.FirstOrDefault(c => string.Equals(c.Id, change.CharacterId, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                    continue;

                if (!string.IsNullOrEmpty(change.NewLevel) && !string.IsNullOrEmpty(existing.Stage))
                {
                    if (ruleSet != null && ruleSet.EnableLevelRegressionCheck)
                    {
                        var oldLevel = ParseLevelNumber(existing.Stage, ruleSet.LevelTextMap);
                        var newLevel = ParseLevelNumber(change.NewLevel, ruleSet.LevelTextMap);

                        if (oldLevel >= 0 && newLevel >= 0 && newLevel < oldLevel)
                        {
                            var keywords = ruleSet.AbilityLossKeywords ?? new List<string>();
                            var hasLossEvent = !string.IsNullOrEmpty(change.KeyEvent) &&
                                (keywords.Count == 0 || keywords.Any(k => !string.IsNullOrWhiteSpace(k) && change.KeyEvent.Contains(k, StringComparison.OrdinalIgnoreCase)));

                            if (!hasLossEvent)
                            {
                                result.AddIssue(new ConsistencyIssue
                                {
                                    EntityId = change.CharacterId,
                                    IssueType = "LevelRegression",
                                    Expected = "等级只升不降（除非有明确失去事件）",
                                    Actual = $"{existing.Stage} → {change.NewLevel}（无失去事件说明）"
                                });
                            }
                        }
                    }
                }

                if (change.LostAbilities != null && change.LostAbilities.Count > 0)
                {
                    if (ruleSet != null && ruleSet.EnableAbilityLossRequiresEvent)
                    {
                        var lossKeywords = ruleSet.AbilityLossKeywords ?? new List<string>();
                        var hasLossEvent = !string.IsNullOrEmpty(change.KeyEvent) &&
                            lossKeywords.Any(k => !string.IsNullOrWhiteSpace(k) && change.KeyEvent.Contains(k, StringComparison.OrdinalIgnoreCase));

                        if (!hasLossEvent)
                        {
                            result.AddIssue(new ConsistencyIssue
                            {
                                EntityId = change.CharacterId,
                                IssueType = "AbilityLossWithoutEvent",
                                Expected = "能力失去必须有对应的KeyEvent说明原因",
                                Actual = $"失去能力 [{string.Join(", ", change.LostAbilities)}] 但KeyEvent未说明原因"
                            });
                        }
                    }
                }

                if (change.RelationshipChanges != null)
                {
                    int maxTrustDelta = ruleSet != null ? ruleSet.MaxTrustDelta : 30;
                    foreach (var (targetId, relChange) in change.RelationshipChanges)
                    {
                        if (Math.Abs(relChange.TrustDelta) > maxTrustDelta)
                        {
                            var targetName = EntityNameResolver.ResolveCharacter(targetId);
                            result.AddIssue(new ConsistencyIssue
                            {
                                EntityId = change.CharacterId,
                                IssueType = "TrustDeltaExceedsLimit",
                                Expected = $"单章信任值变化不超过±{maxTrustDelta}",
                                Actual = $"与 {targetName} 的信任值变化为 {relChange.TrustDelta}"
                            });
                        }
                    }
                }
            }
        }

        private static int ParseLevelNumber(string level, Dictionary<string, int> levelMap)
        {
            if (string.IsNullOrWhiteSpace(level))
                return -1;

            var text = level.Trim();

            var tierMatch = TierNumberRegex.Match(text);
            if (tierMatch.Success && int.TryParse(tierMatch.Groups[1].Value, out var tierNum))
            {
                if (levelMap != null && levelMap.TryGetValue($"Tier-{tierNum}", out var tierValue))
                    return tierValue;
                return tierNum;
            }

            var match = PlainDigitsRegex.Match(text);
            if (match.Success && int.TryParse(match.Value, out var num))
                return num;

            var romanMatch = RomanNumeralRegex.Match(text);
            if (romanMatch.Success && TryParseRomanNumeral(romanMatch.Value, out var romanNum))
                return romanNum;

            var chineseMatch = ChineseNumberRegex.Match(text);
            if (chineseMatch.Success && TryParseChineseNumber(chineseMatch.Value, out var cnNum))
                return cnNum;

            var gradeMatch = GradeLevelRegex.Match(text);
            if (gradeMatch.Success)
            {
                var g = (gradeMatch.Groups[1].Success ? gradeMatch.Groups[1].Value : gradeMatch.Groups[2].Value).ToUpperInvariant();
                if (!string.IsNullOrWhiteSpace(g))
                {
                    if (levelMap.TryGetValue(g, out var gradeValue))
                        return gradeValue;
                }
            }

            foreach (var kv in levelMap)
            {
                if (level.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return kv.Value;
                }
            }

            return -1;
        }

        private static bool TryParseRomanNumeral(string text, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            int total = 0;
            int prev = 0;
            foreach (var c in text.Trim().ToUpperInvariant())
            {
                int cur = c switch
                {
                    'I' => 1,
                    'V' => 5,
                    'X' => 10,
                    'L' => 50,
                    'C' => 100,
                    'D' => 500,
                    'M' => 1000,
                    _ => 0
                };

                if (cur == 0)
                {
                    value = 0;
                    return false;
                }

                if (cur > prev)
                {
                    total += cur - 2 * prev;
                }
                else
                {
                    total += cur;
                }

                prev = cur;
            }

            value = total;
            return value > 0;
        }

        private static bool TryParseChineseNumber(string text, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            int result = 0;
            int section = 0;
            int number = 0;

            foreach (var c in text.Trim())
            {
                int digit = c switch
                {
                    '零' => 0,
                    '一' => 1,
                    '二' => 2,
                    '两' => 2,
                    '三' => 3,
                    '四' => 4,
                    '五' => 5,
                    '六' => 6,
                    '七' => 7,
                    '八' => 8,
                    '九' => 9,
                    _ => -1
                };

                if (digit >= 0)
                {
                    number = digit;
                    continue;
                }

                int unit = c switch
                {
                    '十' => 10,
                    '百' => 100,
                    '千' => 1000,
                    '万' => 10000,
                    _ => 0
                };

                if (unit == 0)
                {
                    value = 0;
                    return false;
                }

                if (unit == 10000)
                {
                    section += number;
                    result += section * 10000;
                    section = 0;
                    number = 0;
                }
                else
                {
                    section += (number == 0 ? 1 : number) * unit;
                    number = 0;
                }
            }

            value = result + section + number;
            return value > 0;
        }

        #endregion

        #region V4

        private void ValidateTimelineConsistency(ChapterChanges changes, FactSnapshot factSnapshot, ConsistencyResult result, HashSet<string>? chapterLocations = null)
        {
            if (changes.CharacterMovements == null || changes.CharacterMovements.Count == 0)
                return;

            var movementsByChar = changes.CharacterMovements
                .Where(m => !string.IsNullOrWhiteSpace(m.CharacterId))
                .GroupBy(m => m.CharacterId, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1);

            foreach (var group in movementsByChar)
            {
                var moves = group.ToList();
                for (int i = 1; i < moves.Count; i++)
                {
                    var prev = moves[i - 1];
                    var curr = moves[i];
                    if (!string.IsNullOrWhiteSpace(prev.ToLocation)
                        && !string.IsNullOrWhiteSpace(curr.FromLocation)
                        && !string.Equals(prev.ToLocation, curr.FromLocation, StringComparison.OrdinalIgnoreCase))
                    {
                        result.AddIssue(new ConsistencyIssue
                        {
                            EntityId = group.Key,
                            IssueType = "MovementChainBreak",
                            Expected = $"移动路径连续（到达{prev.ToLocation}后再从{prev.ToLocation}出发）",
                            Actual = $"从{curr.FromLocation}出发，但上次到达{prev.ToLocation}（路径断裂/同时在两地）"
                        });
                    }
                }
            }

            if (factSnapshot?.CharacterLocations != null && factSnapshot.CharacterLocations.Count > 0)
            {
                var rollingLocationMap = factSnapshot.CharacterLocations
                    .Where(c => !string.IsNullOrWhiteSpace(c.CharacterId) && !string.IsNullOrWhiteSpace(c.CurrentLocation))
                    .ToDictionary(c => c.CharacterId, c => c.CurrentLocation, StringComparer.OrdinalIgnoreCase);

                foreach (var move in changes.CharacterMovements
                    .Where(m => !string.IsNullOrWhiteSpace(m.CharacterId) && !string.IsNullOrWhiteSpace(m.FromLocation)))
                {
                    if (rollingLocationMap.TryGetValue(move.CharacterId, out var knownLoc)
                        && !string.IsNullOrWhiteSpace(knownLoc)
                        && !string.Equals(knownLoc, move.FromLocation, StringComparison.OrdinalIgnoreCase))
                    {
                        if (chapterLocations != null && chapterLocations.Contains(move.FromLocation))
                        {
                            if (!string.IsNullOrWhiteSpace(move.ToLocation))
                                rollingLocationMap[move.CharacterId] = move.ToLocation;
                            continue;
                        }

                        result.AddIssue(new ConsistencyIssue
                        {
                            EntityId = move.CharacterId,
                            IssueType = "MovementStartLocationMismatch",
                            Expected = $"从已知位置 {knownLoc} 出发",
                            Actual = $"CHANGES声称从 {move.FromLocation} 出发（账本位置不符）"
                        });
                    }

                    if (!string.IsNullOrWhiteSpace(move.ToLocation))
                        rollingLocationMap[move.CharacterId] = move.ToLocation;
                }
            }
        }

        #endregion

        #region V5

        private void ValidateItemOwnershipConsistency(ChapterChanges changes, FactSnapshot factSnapshot, ConsistencyResult result, HashSet<string>? chapterCharacters = null)
        {
            if (changes.ItemTransfers == null || changes.ItemTransfers.Count == 0)
                return;

            var rollingHolderMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (factSnapshot?.ItemStates != null)
            {
                foreach (var item in factSnapshot.ItemStates.Where(i => !string.IsNullOrWhiteSpace(i.Id) && !string.IsNullOrWhiteSpace(i.CurrentHolder)))
                    rollingHolderMap[item.Id] = item.CurrentHolder;
            }

            foreach (var transfer in changes.ItemTransfers
                .Where(t => !string.IsNullOrWhiteSpace(t.ItemId) && !string.IsNullOrWhiteSpace(t.FromHolder)))
            {
                if (!rollingHolderMap.ContainsKey(transfer.ItemId))
                {
                    rollingHolderMap[transfer.ItemId] = transfer.FromHolder;
                }
                else if (rollingHolderMap.TryGetValue(transfer.ItemId, out var knownHolder)
                         && !string.Equals(knownHolder, transfer.FromHolder, StringComparison.OrdinalIgnoreCase))
                {
                    if (chapterCharacters != null && chapterCharacters.Contains(transfer.FromHolder))
                    {
                        rollingHolderMap[transfer.ItemId] = transfer.ToHolder ?? string.Empty;
                        continue;
                    }

                    var holderLabel = string.IsNullOrWhiteSpace(knownHolder) ? "无人持有" : knownHolder;

                    result.AddIssue(new ConsistencyIssue
                    {
                        EntityId = transfer.ItemId,
                        IssueType = "ItemOwnershipMismatch",
                        Expected = $"物品由当前持有者 {holderLabel} 转让",
                        Actual = $"CHANGES声称由 {transfer.FromHolder} 转让（持有者不符）"
                    });
                }

                rollingHolderMap[transfer.ItemId] = transfer.ToHolder ?? string.Empty;
            }
        }

        #endregion

        #region V6

        private void ValidateFactionRelationshipConsistency(ChapterChanges changes, ConsistencyResult result)
        {
            if (changes.CharacterStateChanges == null || changes.CharacterStateChanges.Count == 0)
                return;

            var allyPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var enemyPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var change in changes.CharacterStateChanges.Where(c => !string.IsNullOrWhiteSpace(c.CharacterId)))
            {
                foreach (var kv in change.RelationshipChanges ?? new Dictionary<string, RelationshipChange>())
                {
                    var targetId = kv.Key;
                    if (string.IsNullOrWhiteSpace(targetId)) continue;
                    var rel = kv.Value?.Relation ?? string.Empty;

                    var pairKey = string.Compare(change.CharacterId, targetId, StringComparison.OrdinalIgnoreCase) <= 0
                        ? $"{change.CharacterId}|{targetId}"
                        : $"{targetId}|{change.CharacterId}";

                    if (IsAllyRelation(rel)) allyPairs.Add(pairKey);
                    if (IsEnemyRelation(rel)) enemyPairs.Add(pairKey);
                }
            }

            foreach (var pair in allyPairs.Where(p => enemyPairs.Contains(p)))
            {
                var ids = pair.Split('|');
                result.AddIssue(new ConsistencyIssue
                {
                    EntityId = ids[0],
                    IssueType = "RelationshipContradiction",
                    Expected = "同一章节角色关系申报一致",
                    Actual = $"角色 {ids[0]} 与 {ids[1]} 在本章同时被声明为盟友和仇敌（关系矛盾）"
                });
            }
        }

        private static bool IsAllyRelation(string relation) =>
            !string.IsNullOrWhiteSpace(relation) &&
            (relation.Contains("盟友") || relation.Contains("同盟") || relation.Contains("结盟") ||
             relation.Contains("挚友") || relation.Contains("知己") || relation.Contains("朋友") ||
             relation.Contains("好友") || relation.Contains("友人") || relation.Contains("亲友") ||
             relation.Contains("兄弟") || relation.Contains("姐妹") || relation.Contains("手足") ||
             relation.Contains("恋人") || relation.Contains("爱人") || relation.Contains("夫妻") ||
             relation.Contains("伙伴") || relation.Contains("同伴") || relation.Contains("战友") ||
             relation.Contains("心腹") || relation.Contains("亲信") ||
             relation.Contains("ally", StringComparison.OrdinalIgnoreCase) ||
             relation.Contains("friend", StringComparison.OrdinalIgnoreCase));

        private static bool IsEnemyRelation(string relation) =>
            !string.IsNullOrWhiteSpace(relation) &&
            (relation.Contains("仇敌") || relation.Contains("敌对") || relation.Contains("宿敌") ||
             relation.Contains("敌人") || relation.Contains("死敌") || relation.Contains("仇人") ||
             relation.Contains("对头") || relation.Contains("政敌") || relation.Contains("寇仇") ||
             relation.Contains("冤家") || relation.Contains("反目") || relation.Contains("决裂") ||
             relation.Contains("enemy", StringComparison.OrdinalIgnoreCase) ||
             relation.Contains("hostile", StringComparison.OrdinalIgnoreCase));

        #endregion

        #region V7

        private static void ValidatePledgeConstraintConsistency(
            ChapterChanges changes,
            FactSnapshot factSnapshot,
            ConsistencyResult result,
            LedgerRuleSet ruleSet)
        {
            if (ruleSet == null || !ruleSet.EnablePledgeConstraintCheck) return;
            if (changes.PledgeConstraintChanges == null || changes.PledgeConstraintChanges.Count == 0) return;

            var activePledgeIds = new HashSet<string>(
                factSnapshot.PledgeStates?.Select(p => p.Id).Where(id => !string.IsNullOrWhiteSpace(id)) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            var terminalActions = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var pc in changes.PledgeConstraintChanges)
            {
                if (string.IsNullOrWhiteSpace(pc.PledgeId)) continue;
                var action = pc.Action?.ToLowerInvariant() ?? string.Empty;

                if (action == "create" && activePledgeIds.Contains(pc.PledgeId))
                {
                    result.AddIssue(new ConsistencyIssue
                    {
                        EntityId = pc.PledgeId,
                        IssueType = "PledgeDuplicateCreate",
                        Expected = "承诺/契约已存在于账本，不应重复 create",
                        Actual = $"对已有承诺 {pc.PledgeId} 再次执行 create 动作"
                    });
                    continue;
                }

                if (action == "fulfill" || action == "break")
                {
                    if (!terminalActions.TryGetValue(pc.PledgeId, out var actionList))
                    {
                        actionList = new List<string>();
                        terminalActions[pc.PledgeId] = actionList;
                    }
                    actionList.Add(action);
                }
            }

            foreach (var (pledgeId, actions) in terminalActions)
            {
                if (actions.Count > 1)
                {
                    result.AddIssue(new ConsistencyIssue
                    {
                        EntityId = pledgeId,
                        IssueType = "PledgeTerminalActionConflict",
                        Expected = "同一章节承诺只能有一个终止动作（fulfill 或 break）",
                        Actual = $"承诺 {pledgeId} 同章出现多个终止动作：[{string.Join(", ", actions)}]"
                    });
                }
            }
        }

        #endregion

        #region V8

        private static void ValidateDeadlineConstraintConsistency(
            ChapterChanges changes,
            FactSnapshot factSnapshot,
            ConsistencyResult result,
            LedgerRuleSet ruleSet)
        {
            if (ruleSet == null || !ruleSet.EnableDeadlineConstraintCheck) return;
            if (changes.DeadlineConstraintChanges == null || changes.DeadlineConstraintChanges.Count == 0) return;

            var activeDeadlineIds = new HashSet<string>(
                factSnapshot.DeadlineStates?.Select(d => d.Id).Where(id => !string.IsNullOrWhiteSpace(id)) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            var terminalActions = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var dc in changes.DeadlineConstraintChanges)
            {
                if (string.IsNullOrWhiteSpace(dc.DeadlineId)) continue;
                var action = dc.Action?.ToLowerInvariant() ?? string.Empty;

                if (action == "create" && activeDeadlineIds.Contains(dc.DeadlineId))
                {
                    result.AddIssue(new ConsistencyIssue
                    {
                        EntityId = dc.DeadlineId,
                        IssueType = "DeadlineDuplicateCreate",
                        Expected = "倒计时/时限已存在于账本，不应重复 create",
                        Actual = $"对已有倒计时 {dc.DeadlineId} 再次执行 create 动作"
                    });
                    continue;
                }

                if (action == "trigger" || action == "expire" || action == "cancel")
                {
                    if (!terminalActions.ContainsKey(dc.DeadlineId))
                        terminalActions[dc.DeadlineId] = new List<string>();
                    terminalActions[dc.DeadlineId].Add(action);
                }
            }

            foreach (var (deadlineId, actions) in terminalActions)
            {
                if (actions.Count > 1)
                {
                    result.AddIssue(new ConsistencyIssue
                    {
                        EntityId = deadlineId,
                        IssueType = "DeadlineTerminalActionConflict",
                        Expected = "同一章节倒计时只能有一个终止动作（trigger / expire / cancel）",
                        Actual = $"倒计时 {deadlineId} 同章出现多个终止动作：[{string.Join(", ", actions)}]"
                    });
                }
            }
        }

        #endregion
    }
}
