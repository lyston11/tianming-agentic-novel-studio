using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    public partial class LayeredPromptBuilder
    {
        #region 私有方法

        private static int ChangesTopLevelFieldCount => ChapterChanges.TopLevelFieldCount;

        internal static string GetChangesProtocolReminder()
            => $"输出格式强制要求：正文末尾必须用成对的 `{ChapterChanges.ChangesXmlOpen}` 与 `{ChapterChanges.ChangesXmlClose}` 标签包裹 JSON 变更摘要（含{ChangesTopLevelFieldCount}个顶级字段）。ID字段可填写实体名称或事实账本中的 ShortId，系统将自动将名称解析为 ShortId。详见系统提示词中的「输出要求」。";

        internal static string GetRewriteOutputContractReminder()
            => $"请保持原有的写作要求和格式规范（包含 {ChapterChanges.ChangesXmlOpen}...{ChapterChanges.ChangesXmlClose} 标签包裹、完整 JSON，以及显式保留{ChangesTopLevelFieldCount}个顶级字段），修复以上问题后重新输出完整内容。";

        private static void AppendChangesFormatReminder(StringBuilder sb)
        {
            sb.AppendLine("<format_reminder mandatory=\"true\">");
            sb.AppendLine(GetChangesProtocolReminder());
            sb.AppendLine("</format_reminder>");
            sb.AppendLine();
        }

        private void AppendFactLedgerSection(StringBuilder sb, FactSnapshot snapshot)
        {
            sb.AppendLine("<fact_ledger immutable=\"true\" override=\"never\">");
            sb.AppendLine("> 禁止推翻；变化必须发生在本章剧情中并记录。");
            sb.AppendLine();

            var factContent = FormatFactSnapshot(snapshot);
            sb.AppendLine(factContent);
            sb.AppendLine("</fact_ledger>");
            sb.AppendLine();
        }

        private string FormatFactSnapshot(FactSnapshot snapshot)
        {
            var sb = new StringBuilder();
            var skippedSections = TM.App.IsDebugMode ? new List<string>() : null;

            if (snapshot.WorldRuleConstraints != null && snapshot.WorldRuleConstraints.Count > 0)
            {
                sb.AppendLine("<section name=\"world_constraints\" role=\"hard_constraint\">");
                foreach (var rule in snapshot.WorldRuleConstraints)
                {
                    if (string.IsNullOrWhiteSpace(rule.RuleName) || string.IsNullOrWhiteSpace(rule.Constraint))
                        continue;
                    sb.AppendLine($"- **{rule.RuleName}**：{rule.Constraint}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }
            else skippedSections?.Add("world_constraints");

            if (snapshot.CharacterStates != null && snapshot.CharacterStates.Count > 0)
            {
                sb.AppendLine("<section name=\"character_states\" role=\"entity_baseline\">");
                sb.AppendLine("> ⚠ **角色状态变化 / 角色移动 / 关系变化 只能包含此处列出的角色**，填写角色名或括号内 ShortId 均可接受。");
                sb.AppendLine("> ⚠ **硬约束**：角色等级/阶段只升不降；若声明能力失去或重大状态变化，必须在关键事件中明确给出剧情原因；关系信任值单章不要出现极端跳变（默认不应超过±30）。");
                foreach (var state in snapshot.CharacterStates)
                {
                    if (string.IsNullOrWhiteSpace(state.Name)) continue;
                    var idLabel = string.IsNullOrWhiteSpace(state.Id) ? "?" : state.Id;
                    sb.AppendLine($"- **{state.Name}**（{idLabel}）");
                    if (!string.IsNullOrWhiteSpace(state.Stage))
                        sb.AppendLine($"  - 阶段：{state.Stage}");
                    if (!string.IsNullOrWhiteSpace(state.Abilities))
                        sb.AppendLine($"  - 能力：{state.Abilities}");
                    if (!string.IsNullOrWhiteSpace(state.Relationships))
                        sb.AppendLine($"  - 关系：{state.Relationships}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }
            else skippedSections?.Add("character_states");

            if (snapshot.ConflictProgress != null && snapshot.ConflictProgress.Count > 0)
            {
                sb.AppendLine("<section name=\"conflict_progress\" role=\"entity_baseline\">");
                sb.AppendLine("> ⚠ **冲突ID可写名称或括号内 ShortId**");
                sb.AppendLine("> ⚠ **硬约束**：冲突状态不可回退；若更新冲突状态，必须是对当前状态的推进或解决，不允许写回更早状态。");
                foreach (var conflict in snapshot.ConflictProgress)
                {
                    if (string.IsNullOrWhiteSpace(conflict.Name)) continue;
                    var idLabel = string.IsNullOrWhiteSpace(conflict.Id) ? "?" : conflict.Id;
                    sb.AppendLine($"- **{conflict.Name}**（{idLabel}）：{conflict.Status}");
                    if (conflict.RecentProgress != null)
                    {
                        foreach (var point in conflict.RecentProgress.Where(p => !string.IsNullOrWhiteSpace(p)))
                        {
                            sb.AppendLine($"  - {point}");
                        }
                    }
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }
            else skippedSections?.Add("conflict_progress");

            var activeForeshadowing = snapshot.ForeshadowingStatus?.Where(f => !f.IsResolved).ToList();
            if (activeForeshadowing != null && activeForeshadowing.Count > 0)
            {
                sb.AppendLine("<section name=\"foreshadowing\" role=\"entity_baseline\">");
                sb.AppendLine("> ⚠ **伏笔ID可写名称或括号内 ShortId**");
                sb.AppendLine("> ⚠ **硬约束**：未埋设不可揭示；已揭示不可重新埋设。若本章不涉及该伏笔流转，请不要在CHANGES中声明动作。");
                foreach (var f in activeForeshadowing)
                {
                    if (string.IsNullOrWhiteSpace(f.Name)) continue;
                    var idLabel = string.IsNullOrWhiteSpace(f.Id) ? "?" : f.Id;
                    var status = f.IsSetup ? "已埋设" : "未埋设";
                    var warning = f.IsOverdue ? " ⚠逾期" : "";
                    sb.AppendLine($"- **{f.Name}**（{idLabel}）：{status}{warning}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }
            else skippedSections?.Add("foreshadowing");

            if (snapshot.PlotPoints != null && snapshot.PlotPoints.Count > 0)
            {
                sb.AppendLine("<section name=\"plot_points\" role=\"narrative_baseline\">");
                var groups = snapshot.PlotPoints
                    .Where(p => !string.IsNullOrWhiteSpace(p.Summary))
                    .GroupBy(p => p.Storyline ?? "main")
                    .OrderByDescending(g => g.Key == "main" ? 2 : g.Key == "sub" ? 1 : 0);
                foreach (var group in groups)
                {
                    var label = group.Key switch
                    {
                        "main" => "主线",
                        "sub" => "支线",
                        "character_arc" => "人物弧光",
                        _ => group.Key
                    };
                    sb.AppendLine($"**{label}**：");
                    foreach (var point in group)
                    {
                        sb.AppendLine($"- {point.ChapterId}: {point.Summary}");
                    }
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }
            else skippedSections?.Add("plot_points");

            if (snapshot.LocationStates != null && snapshot.LocationStates.Count > 0)
            {
                sb.AppendLine("<section name=\"location_states\" role=\"entity_baseline\">");
                sb.AppendLine("> ⚠ **地点ID可写地点名或括号内 ShortId**");
                foreach (var loc in snapshot.LocationStates)
                {
                    if (string.IsNullOrWhiteSpace(loc.Name)) continue;
                    var idLabel = string.IsNullOrWhiteSpace(loc.Id) ? "?" : loc.Id;
                    sb.AppendLine($"- **{loc.Name}**（{idLabel}）：{loc.Status}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }
            else skippedSections?.Add("location_states");

            if (snapshot.FactionStates != null && snapshot.FactionStates.Count > 0)
            {
                sb.AppendLine("<section name=\"faction_states\" role=\"entity_baseline\">");
                sb.AppendLine("> ⚠ **势力ID可写名称或括号内 ShortId**");
                foreach (var fac in snapshot.FactionStates)
                {
                    if (string.IsNullOrWhiteSpace(fac.Name)) continue;
                    var idLabel = string.IsNullOrWhiteSpace(fac.Id) ? "?" : fac.Id;
                    sb.AppendLine($"- **{fac.Name}**（{idLabel}）：{fac.Status}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }
            else skippedSections?.Add("faction_states");

            if (snapshot.Timeline != null && snapshot.Timeline.Count > 0)
            {
                sb.AppendLine("<section name=\"timeline\" role=\"temporal_baseline\">");
                foreach (var t in snapshot.Timeline)
                {
                    if (string.IsNullOrWhiteSpace(t.TimePeriod)) continue;
                    var elapsed = string.IsNullOrWhiteSpace(t.ElapsedTime) ? "" : $"（经过{t.ElapsedTime}）";
                    sb.AppendLine($"- {t.ChapterId}: {t.TimePeriod}{elapsed}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }
            else skippedSections?.Add("timeline");

            if (snapshot.CharacterLocations != null && snapshot.CharacterLocations.Count > 0)
            {
                sb.AppendLine("<section name=\"character_locations\" role=\"spatial_baseline\">");
                sb.AppendLine("> ⚠ **此节为位置参考，不扩展可写角色范围**：此处出现但未在 character_states 中列出的角色，不得写入 CharacterStateChanges / CharacterMovements / RelationshipChanges。CharacterMovements 各字段可写名称或 ShortId（系统自动解析）；FromLocation 留空时系统自动从账本补值；同章内可多次移动（A→B→C），但每次 FromLocation = 上一次的 ToLocation，不得路径断裂。");
                var locDescMap = snapshot.LocationDescriptions ?? new Dictionary<string, LocationCoreDescription>();
                var locIdToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var ls in snapshot.LocationStates ?? new System.Collections.Generic.List<LocationStateSnapshot>())
                    if (!string.IsNullOrWhiteSpace(ls.Id) && !string.IsNullOrWhiteSpace(ls.Name))
                        locIdToName[ls.Id] = ls.Name;
                foreach (var (lid, ldesc) in locDescMap)
                    if (!string.IsNullOrWhiteSpace(lid) && !string.IsNullOrWhiteSpace(ldesc.Name))
                        locIdToName[lid] = ldesc.Name;
                var locNameToId = locDescMap.Values
                    .Where(l => !string.IsNullOrWhiteSpace(l.Name) && !string.IsNullOrWhiteSpace(l.Id))
                    .GroupBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
                foreach (var loc in snapshot.CharacterLocations)
                {
                    var charDisplayName = string.IsNullOrWhiteSpace(loc.CharacterName) ? loc.CharacterId : loc.CharacterName;
                    if (string.IsNullOrWhiteSpace(loc.CurrentLocation)) continue;
                    string locationDisplay;
                    if (ShortIdGenerator.IsLikelyId(loc.CurrentLocation) && locIdToName.TryGetValue(loc.CurrentLocation, out var resolvedName))
                        locationDisplay = $"{resolvedName}（{loc.CurrentLocation}）";
                    else if (locNameToId.TryGetValue(loc.CurrentLocation, out var locId))
                        locationDisplay = $"{loc.CurrentLocation}（{locId}）";
                    else
                        locationDisplay = loc.CurrentLocation;
                    sb.AppendLine($"- **{charDisplayName}**（{loc.CharacterId}）：{locationDisplay}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }
            else skippedSections?.Add("character_locations");

            if (snapshot.ItemStates != null && snapshot.ItemStates.Count > 0)
            {
                sb.AppendLine("<section name=\"item_states\" role=\"entity_baseline\">");
                sb.AppendLine("> ⚠ **硬约束**：物品ID/持有者可写名称或 ShortId，系统自动解析；第一次转让时原持有者必须与账本一致，同章内多次转手各次起点必须与上次终点一致。");
                var charDescMap = snapshot.CharacterDescriptions ?? new Dictionary<string, CharacterCoreDescription>();
                var charIdToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var cs in snapshot.CharacterStates ?? new System.Collections.Generic.List<CharacterStateSnapshot>())
                    if (!string.IsNullOrWhiteSpace(cs.Id) && !string.IsNullOrWhiteSpace(cs.Name))
                        charIdToName[cs.Id] = cs.Name;
                foreach (var (cid, cdesc) in charDescMap)
                    if (!string.IsNullOrWhiteSpace(cid) && !string.IsNullOrWhiteSpace(cdesc.Name))
                        charIdToName[cid] = cdesc.Name;
                var charNameToId = charDescMap.Values
                    .Where(c => !string.IsNullOrWhiteSpace(c.Name) && !string.IsNullOrWhiteSpace(c.Id))
                    .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
                foreach (var item in snapshot.ItemStates)
                {
                    if (string.IsNullOrWhiteSpace(item.Name)) continue;
                    string holderDisplay;
                    if (string.IsNullOrWhiteSpace(item.CurrentHolder))
                        holderDisplay = "无人持有";
                    else if (ShortIdGenerator.IsLikelyId(item.CurrentHolder) && charIdToName.TryGetValue(item.CurrentHolder, out var holderName))
                        holderDisplay = $"{holderName}（{item.CurrentHolder}）";
                    else if (charNameToId.TryGetValue(item.CurrentHolder, out var cId))
                        holderDisplay = $"{item.CurrentHolder}（{cId}）";
                    else
                        holderDisplay = item.CurrentHolder;
                    var itemIdPart = string.IsNullOrWhiteSpace(item.Id) ? string.Empty : $"（{item.Id}）";
                    sb.AppendLine($"- **{item.Name}**{itemIdPart}：{holderDisplay}，状态={item.Status}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }
            else skippedSections?.Add("item_states");

            if (snapshot.SecretStates != null && snapshot.SecretStates.Count > 0)
            {
                sb.AppendLine("<section name=\"secret_states\" role=\"information_gap_baseline\">");
                sb.AppendLine("> ⚠ **硬约束**：秘密ID/知情者可写名称或 ShortId，不得让不知情的角色表现出已知情的行为。");
                var charDescMap2 = snapshot.CharacterDescriptions ?? new System.Collections.Generic.Dictionary<string, CharacterCoreDescription>();
                foreach (var secret in snapshot.SecretStates)
                {
                    if (string.IsNullOrWhiteSpace(secret.Name)) continue;
                    var idPart = string.IsNullOrWhiteSpace(secret.Id) ? string.Empty : $"（{secret.Id}）";
                    var knowerNames = secret.KnowerIds
                        .Select(k => charDescMap2.TryGetValue(k, out var cd) ? $"{cd.Name}（{k}）" : k)
                        .ToList();
                    var knowersStr = knowerNames.Count > 0 ? string.Join("、", knowerNames) : "无人知晓";
                    sb.AppendLine($"- **{secret.Name}**{idPart}：{TruncateLine(secret.Status, 30)}，知情方=[{TruncateLine(knowersStr, 120)}]");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }
            else skippedSections?.Add("secret_states");

            if (snapshot.PledgeStates != null && snapshot.PledgeStates.Count > 0)
            {
                sb.AppendLine("<section name=\"pledge_states\" role=\"behavioral_constraint_baseline\">");
                sb.AppendLine("> ⚠ **硬约束**：活跃的承诺/契约/誓言必须约束角色行为，违反时必须在CHANGES中声明break动作并说明后果。");
                foreach (var pledge in snapshot.PledgeStates)
                {
                    if (string.IsNullOrWhiteSpace(pledge.Name)) continue;
                    var idLabel = string.IsNullOrWhiteSpace(pledge.Id) ? "?" : pledge.Id;
                    var typeLabel = pledge.Type switch { "contract" => "契约", "oath" => "誓言", _ => "承诺" };
                    var parties = string.IsNullOrWhiteSpace(pledge.PartyIds) ? "" : $"，涉及=[{TruncateLine(pledge.PartyIds, 80)}]";
                    var consequence = string.IsNullOrWhiteSpace(pledge.Consequence) ? "" : $"，违反后果={TruncateLine(pledge.Consequence, 80)}";
                    var overdueMark = pledge.IsOverdue ? " ⚠长期未兑现" : string.Empty;
                    sb.AppendLine($"- **{pledge.Name}**（{idLabel}）：{typeLabel}｜{TruncateLine(pledge.Status, 20)}{overdueMark}｜{TruncateLine(pledge.Condition, 80)}{parties}{consequence}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }
            else skippedSections?.Add("pledge_states");

            if (snapshot.DeadlineStates != null && snapshot.DeadlineStates.Count > 0)
            {
                sb.AppendLine("<section name=\"deadline_states\" role=\"temporal_pressure_baseline\">");
                sb.AppendLine("> ⚠ **硬约束**：活跃的倒计时/时限必须在叙事中体现紧迫感，到期时必须在CHANGES中声明trigger/expire动作。");
                foreach (var deadline in snapshot.DeadlineStates)
                {
                    if (string.IsNullOrWhiteSpace(deadline.Name)) continue;
                    var idLabel = string.IsNullOrWhiteSpace(deadline.Id) ? "?" : deadline.Id;
                    var typeLabel = deadline.Type switch { "curse" => "诅咒时限", "threat" => "威胁时限", "event" => "事件时限", _ => "倒计时" };
                    var deadlineInfo = string.IsNullOrWhiteSpace(deadline.Deadline) ? "" : $"，截止={TruncateLine(deadline.Deadline, 40)}";
                    var consequence = string.IsNullOrWhiteSpace(deadline.Consequence) ? "" : $"，后果={TruncateLine(deadline.Consequence, 80)}";
                    var parties = string.IsNullOrWhiteSpace(deadline.PartyIds) ? "" : $"，涉及=[{TruncateLine(deadline.PartyIds, 80)}]";
                    var overdueMark = deadline.IsOverdue ? " ⚠已超期" : string.Empty;
                    sb.AppendLine($"- **{deadline.Name}**（{idLabel}）：{typeLabel}｜{TruncateLine(deadline.Status, 20)}{overdueMark}{deadlineInfo}{consequence}{parties}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }
            else skippedSections?.Add("deadline_states");

            if (skippedSections != null && skippedSections.Count > 0 && InfoLogDedup.ShouldLog("LayeredPromptBuilder:FactLedger:Skipped"))
                TM.App.Log($"[LayeredPromptBuilder] FactLedger 跳过的空子块: {string.Join(", ", skippedSections)}");

            return sb.ToString();
        }

        #endregion
    }
}
