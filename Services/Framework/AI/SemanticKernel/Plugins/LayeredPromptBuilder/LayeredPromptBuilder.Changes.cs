using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.UI.Workspace.Services.Spec;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.TaskContexts;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    public partial class LayeredPromptBuilder
    {
        #region 私有方法 - CHANGES输出要求

        internal static string GetChangesRequirementBlock(ContextIdCollection? contextIds, FactSnapshot? snapshot)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<output_requirements mandatory=\"true\">");
            sb.AppendLine();
            sb.AppendLine("1. 请根据以上信息生成完整章节正文");
            sb.AppendLine("2. 直接输出小说正文内容，以章节标题（# 第X章：标题）开头");
            sb.AppendLine("3. 禁止输出任何自我介绍、AI身份说明、任务确认等非正文内容");
            sb.AppendLine("4. 禁止输出「好的」「我来生成」「以下是内容」等过渡语");
            sb.AppendLine("5. 只输出纯粹的小说章节正文");
            sb.AppendLine();
            sb.AppendLine("6. **正文末尾必须输出变更摘要**，格式如下：");
            sb.AppendLine();
            sb.AppendLine($"<changes_protocol open_tag=\"{ChapterChanges.ChangesXmlOpen}\" close_tag=\"{ChapterChanges.ChangesXmlClose}\" format=\"json\" fields=\"{ChangesTopLevelFieldCount}\" mandatory=\"true\">");
            sb.AppendLine("**⚠ ID字段规则**：所有ID字段均可填写**实体名称**或事实账本中的ShortId，系统自动解析。不确定ShortId时写名称即可。");
            sb.AppendLine();
            sb.AppendLine($"7. 正文写完后，必须用成对的 `{ChapterChanges.ChangesXmlOpen}` 与 `{ChapterChanges.ChangesXmlClose}` 标签包裹变更摘要（仅半角字符、不得简写、不得加额外属性、不得用其他名字替换）。");
            sb.AppendLine($"8. 标签内部只能放合法 JSON 对象本身，必须显式包含{ChangesTopLevelFieldCount}个顶级字段（即使为空数组`[]`或空对象`{{}}`）。不要使用 Markdown 代码块，不要在 JSON 前后写解释文字。");
            sb.AppendLine($"9. 最终输出结构固定为：`{ChapterChanges.ChangesXmlOpen}\\n{{完整JSON对象}}\\n{ChapterChanges.ChangesXmlClose}`。");
            sb.AppendLine();
            sb.AppendLine($"**重要**：上面出现的 `<changes_protocol>`、`<final_checklist>`、`<changes_template>`、`<changes_id_ref>` 等 XML 标签是**系统提示词结构**，不应出现在你的输出里。你输出的唯一一对 XML 标签是 `{ChapterChanges.ChangesXmlOpen}` 与 `{ChapterChanges.ChangesXmlClose}`。");
            sb.AppendLine();

            sb.AppendLine($"**CHANGES字段速查（共{ChangesTopLevelFieldCount}个顶级字段，字段名不可改名；无变化的字段保留空数组`[]`或空对象`{{}}`，不可省略；若末尾提供了预填模板，以模板结构为准填写）**：");

            bool hasCharacters = contextIds?.Characters?.Count > 0;
            bool hasConflicts = contextIds?.Conflicts?.Count > 0;
            bool hasForeshadowing = (contextIds?.ForeshadowingSetups?.Count > 0) || (contextIds?.ForeshadowingPayoffs?.Count > 0);
            bool hasLocations = contextIds?.Locations?.Count > 0;
            bool hasFactions = contextIds?.Factions?.Count > 0;

            if (hasCharacters)
                sb.AppendLine("- `CharacterStateChanges[]`: 角色状态变化 — CharacterId, NewLevel, NewAbilities(增量), LostAbilities(需KeyEvent说明原因), RelationshipChanges{ <角色名或ShortId>: {Relation, TrustDelta(±30以内), EmotionPhase} }, NewMentalState, KeyEvent, Importance(normal/important/critical), CausedBy");
            else
                sb.AppendLine("- `CharacterStateChanges[]`: 本章无追踪角色，保留空数组`[]`");

            if (hasConflicts)
                sb.AppendLine("- `ConflictProgress[]`: 冲突进度 — ConflictId, NewStatus, Event, Importance(normal/important/critical), CausedBy");
            else
                sb.AppendLine("- `ConflictProgress[]`: 本章无追踪冲突，保留空数组`[]`");

            if (hasForeshadowing)
                sb.AppendLine("- `ForeshadowingActions[]`: 伏笔动作 — ForeshadowId, Action(仅setup/payoff)");
            else
                sb.AppendLine("- `ForeshadowingActions[]`: 本章无追踪伏笔，保留空数组`[]`");

            sb.AppendLine("- `NewPlotPoints[]`: 新增情节 — Keywords[], Context, InvolvedCharacters[], Importance(normal/important/critical), Storyline(main/sub/character_arc), CausedBy");

            if (hasLocations)
                sb.AppendLine("- `LocationStateChanges[]`: 地点变化 — LocationId, NewStatus, Event, Importance(normal/important/critical)");
            else
                sb.AppendLine("- `LocationStateChanges[]`: 本章无追踪地点，保留空数组`[]`");

            if (hasFactions)
                sb.AppendLine("- `FactionStateChanges[]`: 势力变化 — FactionId, NewStatus, Event, Importance(normal/important/critical), CausedBy");
            else
                sb.AppendLine("- `FactionStateChanges[]`: 本章无追踪势力，保留空数组`[]`");

            sb.AppendLine("- `TimeProgression{}`: 时间推进 — TimePeriod, ElapsedTime, KeyTimeEvent, Importance(normal/important/critical)");

            if (hasCharacters)
                sb.AppendLine("- `CharacterMovements[]`: 角色移动 — CharacterId, FromLocation(可留空由系统补值), ToLocation, Importance(normal/important/critical)");
            else
                sb.AppendLine("- `CharacterMovements[]`: 本章无追踪角色，保留空数组`[]`");

            bool hasItems = snapshot?.ItemStates?.Count > 0;
            bool hasSecrets = snapshot?.SecretStates?.Count > 0;
            bool hasPledges = snapshot?.PledgeStates?.Count > 0;
            bool hasDeadlines = snapshot?.DeadlineStates?.Count > 0;

            if (hasItems)
                sb.AppendLine("- `ItemTransfers[]`: 物品流转 — ItemId, ItemName(显示名称), FromHolder(可留空由系统补值), ToHolder(毁坏则留空), NewStatus(active/lost/destroyed/sealed), Event, Importance(normal/important/critical), CausedBy");
            else
                sb.AppendLine("- `ItemTransfers[]`: 本章若有新物品出现/流转，新增条目填 ItemName 即可（ID自动生成）；子字段：ItemName, FromHolder, ToHolder, NewStatus(active/lost/destroyed/sealed), Event, Importance");

            if (hasSecrets)
                sb.AppendLine("- `SecretRevealChanges[]`: 秘密知情 — SecretId, SecretName(首次命名时必填), NewKnowerIds[], Method(told/overheard/deduced/discovered/other), KeyEvent, Importance(normal/important/critical), CausedBy");
            else
                sb.AppendLine("- `SecretRevealChanges[]`: 本章若有新秘密揭示，新增条目填 SecretName 即可（ID自动生成）；子字段：SecretName, NewKnowerIds[], Method(told/overheard/deduced/discovered), KeyEvent, Importance");

            if (hasPledges)
                sb.AppendLine("- `PledgeConstraintChanges[]`: 承诺/契约 — PledgeId, PledgeName(创建时必填), Action(create/fulfill/break/update), Type(pledge/contract/oath), PartyIds[], Condition, Consequence, KeyEvent, Importance(normal/important/critical)");
            else
                sb.AppendLine("- `PledgeConstraintChanges[]`: 本章若有新承诺/契约，新增条目填 PledgeName + Action(create) 即可；子字段：PledgeName, Action, Type(pledge/contract/oath), PartyIds[], Condition, Consequence, Importance");

            if (hasDeadlines)
                sb.AppendLine("- `DeadlineConstraintChanges[]`: 倒计时/时限 — DeadlineId, DeadlineName(创建时必填), Action(create/trigger/expire/cancel/update), Type(countdown/curse/threat/event), Deadline, TriggerCondition, Consequence, PartyIds[], KeyEvent, Importance(normal/important/critical)");
            else
                sb.AppendLine("- `DeadlineConstraintChanges[]`: 本章若有新倒计时/时限，新增条目填 DeadlineName + Action(create) 即可；子字段：DeadlineName, Action, Type(countdown/curse/threat/event), Deadline, Consequence, Importance");

            sb.AppendLine();
            sb.AppendLine("**Importance使用规范**：normal(默认，可裁剪) / important(较重要，可压缩) / critical(永久保留，仅用于不可逆事件如角色死亡、血誓、阵营叛变等，每章最多1-2个)");
            sb.AppendLine("</changes_protocol>");
            sb.AppendLine();
            sb.AppendLine("<final_checklist mandatory=\"true\">");
            sb.AppendLine($"1. 是否使用了精确的 `{ChapterChanges.ChangesXmlOpen}` 开标签与 `{ChapterChanges.ChangesXmlClose}` 闭标签（成对、仅半角字符、名字不得修改、不得加属性）？");
            sb.AppendLine($"2. 标签内 JSON 是否包含全部{ChangesTopLevelFieldCount}个顶级字段（即使为空数组`[]`或空对象`{{}}`）？");
            sb.AppendLine("3. 所有ID字段是否均使用事实账本中的实体名称或ShortId？禁止自行构造不存在的ShortId；若末尾提供了预填模板，请优先使用模板中已列出的实体。");
            sb.AppendLine("4. 是否避免把 `<changes_protocol>` `<changes_template>` `<changes_id_ref>` 等系统指令标签误抄到最终输出中？");
            sb.AppendLine("</final_checklist>");
            sb.AppendLine("</output_requirements>");
            return sb.ToString();
        }

        #endregion

        private static void AppendWordCountAnchor(StringBuilder sb, ContentTaskContext taskContext, CreativeSpec? spec)
        {
            if (spec?.TargetWordCount is not > 0) return;
            var target = spec.TargetWordCount.Value;

            sb.AppendLine();
            sb.AppendLine($"<word_count_anchor mandatory=\"true\">【字数硬约束】目标 {target} 字（仅统计正文，不含标题与 {ChapterChanges.ChangesXmlOpen} 标签内的内容）。</word_count_anchor>");
        }

        private void AppendTailEntityChecklist(StringBuilder sb, ContentTaskContext ctx)
        {
            var (chars, factions, locs) = BuildMandatoryEntities(ctx);

            if (chars.Count == 0 && factions.Count == 0 && locs.Count == 0)
                return;

            sb.AppendLine();
            sb.AppendLine("<entity_checklist mandatory=\"true\" priority=\"critical\">");
            sb.AppendLine("**写作前最终确认**：以下实体必须在正文中出现（有对话或行动），缺任何一个将不通过校验：");
            if (chars.Count > 0)
                sb.AppendLine($"  角色：{string.Join(" / ", chars)}");
            if (factions.Count > 0)
                sb.AppendLine($"  势力：{string.Join(" / ", factions)}");
            if (locs.Count > 0)
                sb.AppendLine($"  地点：{string.Join(" / ", locs)}");
            sb.AppendLine("</entity_checklist>");
        }

        #region 私有方法 - CHANGES ID快速参考

        internal static void AppendChangesIdQuickRef(StringBuilder sb, FactSnapshot snapshot)
        {
            var charIds = snapshot.CharacterStates.Where(s => !string.IsNullOrWhiteSpace(s.Id) && !string.IsNullOrWhiteSpace(s.Name)).ToList();
            var conflictIds = snapshot.ConflictProgress.Where(c => !string.IsNullOrWhiteSpace(c.Id) && !string.IsNullOrWhiteSpace(c.Name)).ToList();
            var fsIds = snapshot.ForeshadowingStatus.Where(f => !string.IsNullOrWhiteSpace(f.Id) && !string.IsNullOrWhiteSpace(f.Name) && !f.IsResolved).ToList();
            var locIds = snapshot.LocationStates.Where(l => !string.IsNullOrWhiteSpace(l.Id) && !string.IsNullOrWhiteSpace(l.Name)).ToList();
            var factionIds = snapshot.FactionStates.Where(f => !string.IsNullOrWhiteSpace(f.Id) && !string.IsNullOrWhiteSpace(f.Name)).ToList();
            var locRefIdSet = new HashSet<string>(locIds.Select(l => l.Id), StringComparer.OrdinalIgnoreCase);
            if (snapshot.CharacterLocations != null)
            {
                var locDescForRef = snapshot.LocationDescriptions ?? new Dictionary<string, LocationCoreDescription>();
                foreach (var cl in snapshot.CharacterLocations)
                {
                    var curLoc = cl.CurrentLocation?.Trim();
                    if (string.IsNullOrWhiteSpace(curLoc) || !ShortIdGenerator.IsLikelyId(curLoc) || locRefIdSet.Contains(curLoc)) continue;
                    string? locName = null;
                    if (locDescForRef.TryGetValue(curLoc, out var ld)) locName = ld?.Name;
                    if (string.IsNullOrWhiteSpace(locName))
                    {
                        var fromState = snapshot.LocationStates?.FirstOrDefault(ls => string.Equals(ls.Id, curLoc, StringComparison.OrdinalIgnoreCase));
                        locName = fromState?.Name;
                    }
                    if (!string.IsNullOrWhiteSpace(locName))
                    {
                        locIds.Add(new LocationStateSnapshot { Id = curLoc, Name = locName! });
                        locRefIdSet.Add(curLoc);
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine("<changes_id_ref mandatory=\"true\">");
            sb.AppendLine("CHANGES字段ID参考表（可填写名称或ShortId，系统自动解析；禁止自行构造不存在的实体）：");
            sb.AppendLine("⚠ 角色范围硬限制：角色状态变化 / 角色移动 / 关系变化 只允许引用下方列出的角色（填名称或ShortId均可）；未列出的角色禁止写入角色相关字段。");

            if (charIds.Count == 0)
                sb.AppendLine("  ⚠ 角色：账本无追踪记录 → 本章角色状态变化、角色移动、关系变化、涉及角色列表必须留空");
            else
            {
                sb.Append("  角色可用ID：");
                sb.AppendLine(string.Join(" | ", charIds.Select(s => $"{s.Name}={s.Id}")));
            }

            if (conflictIds.Count == 0)
                sb.AppendLine("  ⚠ 冲突：账本无追踪记录 → 本章冲突进度必须留空");
            else
            {
                sb.Append("  冲突可用ID：");
                sb.AppendLine(string.Join(" | ", conflictIds.Select(c => $"{c.Name}={c.Id}")));
            }

            if (fsIds.Count == 0)
                sb.AppendLine("  ⚠ 伏笔：账本无追踪记录 → 本章伏笔动作必须留空");
            else
            {
                sb.AppendLine("  伏笔可用ID（含当前状态，动作只能是下表允许值）：");
                foreach (var f in fsIds)
                {
                    string state, allowed;
                    if (f.IsSetup) { state = "已埋设"; allowed = "允许揭示，禁止再次埋设"; }
                    else { state = "未埋设"; allowed = "允许埋设，禁止直接揭示"; }
                    sb.AppendLine($"    {f.Name}={f.Id}（{state}，{allowed}）");
                }
            }

            if (locIds.Count == 0)
                sb.AppendLine("  ⚠ 地点：账本无追踪记录 → 本章地点状态变化必须留空");
            else
            {
                sb.Append("  地点可用ID：");
                sb.AppendLine(string.Join(" | ", locIds.Select(l => $"{l.Name}={l.Id}")));
            }

            if (factionIds.Count == 0)
                sb.AppendLine("  ⚠ 势力：账本无追踪记录 → 本章势力状态变化必须留空");
            else
            {
                sb.Append("  势力可用ID：");
                sb.AppendLine(string.Join(" | ", factionIds.Select(f => $"{f.Name}={f.Id}")));
            }

            var itemIds = snapshot.ItemStates?.Where(i => !string.IsNullOrWhiteSpace(i.Id) && !string.IsNullOrWhiteSpace(i.Name)).ToList() ?? new();
            if (itemIds.Count > 0)
            {
                sb.Append("  物品可用ID：");
                sb.AppendLine(string.Join(" | ", itemIds.Select(i => $"{i.Name}={i.Id}")));
            }

            var secretIds = snapshot.SecretStates?.Where(s => !string.IsNullOrWhiteSpace(s.Id) && !string.IsNullOrWhiteSpace(s.Name)).ToList() ?? new();
            if (secretIds.Count > 0)
            {
                sb.Append("  秘密可用ID：");
                sb.AppendLine(string.Join(" | ", secretIds.Select(s => $"{s.Name}={s.Id}")));
            }

            var pledgeIds = snapshot.PledgeStates?.Where(p => !string.IsNullOrWhiteSpace(p.Id) && !string.IsNullOrWhiteSpace(p.Name)).ToList() ?? new();
            if (pledgeIds.Count > 0)
            {
                sb.AppendLine("  承诺/契约可用ID（含当前状态）：");
                foreach (var p in pledgeIds)
                {
                    var typeLabel = p.Type switch { "contract" => "契约", "oath" => "誓言", _ => "承诺" };
                    sb.AppendLine($"    {p.Name}={p.Id}（{typeLabel}，{p.Status}，允许动作：fulfill/break/update）");
                }
            }

            var deadlineIds = snapshot.DeadlineStates?.Where(d => !string.IsNullOrWhiteSpace(d.Id) && !string.IsNullOrWhiteSpace(d.Name)).ToList() ?? new();
            if (deadlineIds.Count > 0)
            {
                sb.AppendLine("  倒计时/时限可用ID（含当前状态）：");
                foreach (var d in deadlineIds)
                {
                    var typeLabel = d.Type switch { "curse" => "诅咒时限", "threat" => "威胁时限", "event" => "事件时限", _ => "倒计时" };
                    sb.AppendLine($"    {d.Name}={d.Id}（{typeLabel}，{d.Status}，允许动作：trigger/expire/cancel/update）");
                }
            }

            sb.AppendLine("</changes_id_ref>");
        }

        #endregion

        #region 预填 CHANGES 模板

        private static readonly JsonSerializerOptions PrefilledJsonOptions = new() { WriteIndented = true };

        public static string BuildPrefilledChangesJson(FactSnapshot snapshot, ContextIdCollection? contextIds)
        {
            var changes = new ChapterChanges
            {
                TimeProgression = new TimeProgressionChange()
            };

            if (contextIds == null)
                return JsonSerializer.Serialize(changes, PrefilledJsonOptions);

            var charIds = (contextIds.Characters ?? new())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var charId in charIds)
                changes.CharacterStateChanges.Add(new CharacterStateChange { CharacterId = charId });

            foreach (var conflictId in (contextIds.Conflicts ?? new())
                .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
                changes.ConflictProgress.Add(new ConflictProgressChange { ConflictId = conflictId });

            var fsStatusLookup = (snapshot.ForeshadowingStatus ?? new())
                .Where(f => !string.IsNullOrWhiteSpace(f.Id))
                .ToDictionary(f => f.Id!, f => f, StringComparer.OrdinalIgnoreCase);
            var addedFsIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var fsId in contextIds.ForeshadowingSetups ?? new())
            {
                if (string.IsNullOrWhiteSpace(fsId) || !addedFsIds.Add(fsId)) continue;
                if (fsStatusLookup.TryGetValue(fsId, out var fs) && (fs.IsSetup || fs.IsResolved)) continue;
                changes.ForeshadowingActions.Add(new ForeshadowingAction { ForeshadowId = fsId, Action = "setup" });
            }

            foreach (var fsId in contextIds.ForeshadowingPayoffs ?? new())
            {
                if (string.IsNullOrWhiteSpace(fsId) || !addedFsIds.Add(fsId)) continue;
                if (!fsStatusLookup.TryGetValue(fsId, out var fs)) continue;
                if (fs.IsResolved || !fs.IsSetup) continue;
                changes.ForeshadowingActions.Add(new ForeshadowingAction { ForeshadowId = fsId, Action = "payoff" });
            }

            foreach (var locId in (contextIds.Locations ?? new())
                .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
                changes.LocationStateChanges.Add(new LocationStateChange { LocationId = locId });

            foreach (var facId in (contextIds.Factions ?? new())
                .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
                changes.FactionStateChanges.Add(new FactionStateChange { FactionId = facId });

            var charLocMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cl in snapshot.CharacterLocations ?? new())
            {
                if (!string.IsNullOrWhiteSpace(cl.CharacterId) && !string.IsNullOrWhiteSpace(cl.CurrentLocation))
                    charLocMap[cl.CharacterId] = cl.CurrentLocation;
            }

            foreach (var charId in charIds)
            {
                var movement = new CharacterMovementChange { CharacterId = charId };
                if (charLocMap.TryGetValue(charId, out var fromLoc))
                    movement.FromLocation = fromLoc;
                changes.CharacterMovements.Add(movement);
            }

            foreach (var item in snapshot.ItemStates ?? new())
                if (!string.IsNullOrWhiteSpace(item.Id))
                    changes.ItemTransfers.Add(new ItemTransferChange { ItemId = item.Id });

            foreach (var secret in snapshot.SecretStates ?? new())
                if (!string.IsNullOrWhiteSpace(secret.Id))
                    changes.SecretRevealChanges.Add(new SecretRevealChange { SecretId = secret.Id });

            foreach (var pledge in snapshot.PledgeStates ?? new())
                if (!string.IsNullOrWhiteSpace(pledge.Id))
                    changes.PledgeConstraintChanges.Add(new PledgeConstraintChange { PledgeId = pledge.Id });

            foreach (var deadline in snapshot.DeadlineStates ?? new())
                if (!string.IsNullOrWhiteSpace(deadline.Id))
                    changes.DeadlineConstraintChanges.Add(new DeadlineConstraintChange { DeadlineId = deadline.Id });

            return JsonSerializer.Serialize(changes, PrefilledJsonOptions);
        }

        internal static void AppendPrefilledChangesTemplate(StringBuilder sb, FactSnapshot snapshot, ContextIdCollection? contextIds)
        {
            var json = BuildPrefilledChangesJson(snapshot, contextIds);
            if (string.IsNullOrWhiteSpace(json)) return;

            sb.AppendLine();
            sb.AppendLine("<changes_template mandatory=\"true\" priority=\"critical\">");
            sb.AppendLine("正文写完后，复制以下预填模板，根据正文内容修改后作为你的 CHANGES 输出：");
            sb.AppendLine("- 有变化的条目：填入空值字段（KeyEvent/NewMentalState/NewStatus 等）");
            sb.AppendLine("- 无变化的条目：删除整条（系统也会自动剔除空条目，但建议主动删除以保持简洁）");
            sb.AppendLine($"- {ChangesTopLevelFieldCount}个顶级字段不可删除，保留空数组 [] 或空对象 {{}}");
            sb.AppendLine("- 新增物品/秘密/承诺/倒计时：在对应空数组新增条目，填 Name 字段即可（ID自动生成）");
            sb.AppendLine("- CharacterMovements: 若模板中已预填 FromLocation 则仅需填 ToLocation；未移动则删除该条目（或保持为空数组）");
            sb.AppendLine($"- \u26a0 必须用成对的 `{ChapterChanges.ChangesXmlOpen}` 与 `{ChapterChanges.ChangesXmlClose}` 标签包裹模板输出（仅半角字符、不得省略），JSON 必须完整包含{ChangesTopLevelFieldCount}个顶级字段");
            sb.AppendLine();
            sb.AppendLine(ChapterChanges.ChangesXmlOpen);
            sb.AppendLine(json);
            sb.AppendLine(ChapterChanges.ChangesXmlClose);
            sb.AppendLine("</changes_template>");
        }

        #endregion
    }
}
