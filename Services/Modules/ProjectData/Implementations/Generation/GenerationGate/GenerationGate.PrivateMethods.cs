using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Modules.ProjectData.Implementations.Generation;
using TM.Services.Modules.ProjectData.Models.Tracking;
using TM.Services.Modules.ProjectData.Models.Guides;
using F = TM.Services.Modules.ProjectData.Models.Tracking.ShortIdFieldRegistry;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class GenerationGate
    {
        #region 私有方法

        private string ExtractJsonFromChangesSection(string changesSection)
        {
            if (string.IsNullOrEmpty(changesSection))
                return string.Empty;

            var content = changesSection.Trim();

            if (content.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewline = content.IndexOf('\n');
                if (firstNewline > 0)
                {
                    content = content.Substring(firstNewline + 1);
                }

                var lastBackticks = content.LastIndexOf("```");
                if (lastBackticks > 0)
                {
                    content = content.Substring(0, lastBackticks);
                }
            }

            content = content.Trim();

            var jsonStart = content.IndexOf('{');
            if (jsonStart < 0)
            {
                return content;
            }

            var best = string.Empty;
            var end = content.IndexOf('}', jsonStart + 1);
            while (end > jsonStart)
            {
                var candidate = content.Substring(jsonStart, end - jsonStart + 1);
                try
                {
                    using var doc = JsonDocument.Parse(candidate, new JsonDocumentOptions
                    {
                        CommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    });

                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        return candidate;
                    }
                }
                catch
                {
                    best = candidate;
                }

                end = content.IndexOf('}', end + 1);
            }

            return string.IsNullOrEmpty(best)
                ? content.Substring(jsonStart)
                : best;
        }

        private static string RepairChangesJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return json;
            }

            json = RemoveJsonComments(json);

            json = MapChineseFieldNames(json);

            var sb = new System.Text.StringBuilder(json.Length + 64);
            var inString = false;
            var stringQuoteChar = '"';
            var escape = false;
            var bracketStack = new Stack<char>();

            for (var i = 0; i < json.Length; i++)
            {
                var c = json[i];

                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                        sb.Append(c);
                        continue;
                    }

                    if (c == '\\')
                    {
                        escape = true;
                        sb.Append(c);
                        continue;
                    }

                    if (c == stringQuoteChar)
                    {
                        inString = false;
                        sb.Append('"');
                        continue;
                    }

                    if (c < ' ')
                    {
                        if (c == '\n') { sb.Append("\\n"); continue; }
                        if (c == '\r') { continue; }
                        if (c == '\t') { sb.Append("\\t"); continue; }
                        if (c == '\b') { sb.Append("\\b"); continue; }
                        if (c == '\f') { sb.Append("\\f"); continue; }
                        sb.Append($"\\u{(int)c:X4}");
                        continue;
                    }

                    sb.Append(c);
                    continue;
                }

                if (c == '\'')
                {
                    inString = true;
                    stringQuoteChar = '\'';
                    sb.Append('"');
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    stringQuoteChar = '"';
                    sb.Append('"');
                    continue;
                }

                if (c == '：')
                {
                    sb.Append(':');
                    continue;
                }

                if (c == '，')
                {
                    sb.Append(',');
                    continue;
                }

                if (c == ',')
                {
                    var j = i + 1;
                    while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
                    if (j < json.Length && (json[j] == '}' || json[j] == ']'))
                    {
                        continue;
                    }
                    sb.Append(c);
                    continue;
                }

                if (c == '}')
                {
                    if (bracketStack.Count > 0 && bracketStack.Peek() == '{')
                        bracketStack.Pop();
                    sb.Append(c);
                    var j = i + 1;
                    while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
                    if (j < json.Length && json[j] == '{')
                    {
                        sb.Append(',');
                    }
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    var prevNonWs = FindPrevNonWhitespace(json, i - 1);
                    if (prevNonWs == '{' || prevNonWs == ',')
                    {
                        var identEnd = i;
                        while (identEnd < json.Length && (char.IsLetterOrDigit(json[identEnd]) || json[identEnd] == '_'))
                            identEnd++;
                        var colonPos = identEnd;
                        while (colonPos < json.Length && char.IsWhiteSpace(json[colonPos]))
                            colonPos++;
                        if (colonPos < json.Length && (json[colonPos] == ':' || json[colonPos] == '：'))
                        {
                            var fieldName = json.Substring(i, identEnd - i);
                            if (ChineseFieldNameMap.TryGetValue(fieldName, out var mappedFieldName))
                                fieldName = mappedFieldName;
                            sb.Append('"').Append(fieldName).Append('"');
                            i = identEnd - 1;
                            continue;
                        }
                    }
                }

                if (c == '{' || c == '[')
                    bracketStack.Push(c);
                else if (c == '}' && bracketStack.Count > 0 && bracketStack.Peek() == '{')
                    bracketStack.Pop();
                else if (c == ']' && bracketStack.Count > 0 && bracketStack.Peek() == '[')
                    bracketStack.Pop();

                sb.Append(c);
            }

            if (inString || bracketStack.Count > 0)
            {
                if (inString)
                {
                    sb.Append('"');
                    TM.App.Log("[GG] JSON修复: 自动闭合截断的字符串");
                }

                if (bracketStack.Count > 0)
                {
                    var current = sb.ToString();
                    var lastValidEnd = FindLastValidJsonBreakpoint(current);
                    if (lastValidEnd > 0 && lastValidEnd < current.Length)
                    {
                        sb.Clear();
                        sb.Append(current, 0, lastValidEnd);
                    }

                    var depth = bracketStack.Count;
                    while (bracketStack.Count > 0)
                    {
                        var open = bracketStack.Pop();
                        sb.Append(open == '{' ? '}' : ']');
                    }
                    TM.App.Log($"[GG] JSON修复: 自动闭合 {depth} 层截断括号");
                }
            }

            return sb.ToString();
        }

        private static int FindLastValidJsonBreakpoint(string json)
        {
            for (var i = json.Length - 1; i >= 0; i--)
            {
                var c = json[i];
                if (c == ',' || c == '[' || c == '{')
                    return i + 1;
                if (c == '}' || c == ']' || c == '"')
                    return i + 1;
                if (char.IsDigit(c) || c == 'e' || c == 'l')
                    return i + 1;
            }
            return json.Length;
        }

        private static char FindPrevNonWhitespace(string s, int from)
        {
            for (var i = from; i >= 0; i--)
            {
                if (!char.IsWhiteSpace(s[i]))
                    return s[i];
            }
            return '\0';
        }

        private static string RemoveJsonComments(string json)
        {
            var sb = new System.Text.StringBuilder(json.Length);
            var inStr = false;
            var quoteChar = '"';
            var esc = false;

            for (var i = 0; i < json.Length; i++)
            {
                var c = json[i];

                if (inStr)
                {
                    sb.Append(c);
                    if (esc) { esc = false; continue; }
                    if (c == '\\') { esc = true; continue; }
                    if (c == quoteChar) inStr = false;
                    continue;
                }

                if (c == '"' || c == '\'') { inStr = true; quoteChar = c; sb.Append(c); continue; }

                if (c == '/' && i + 1 < json.Length && json[i + 1] == '/')
                {
                    while (i < json.Length && json[i] != '\n') i++;
                    if (i < json.Length) sb.Append('\n');
                    continue;
                }

                if (c == '/' && i + 1 < json.Length && json[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < json.Length && !(json[i] == '*' && json[i + 1] == '/')) i++;
                    if (i + 1 < json.Length) i++;
                    continue;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        private static readonly Dictionary<string, string> ChineseFieldNameMap = new(StringComparer.Ordinal)
        {
            { "角色状态变化", "CharacterStateChanges" },
            { "角色状态变更", "CharacterStateChanges" },
            { "角色变化", "CharacterStateChanges" },
            { "冲突进度", "ConflictProgress" },
            { "冲突进展", "ConflictProgress" },
            { "伏笔动作", "ForeshadowingActions" },
            { "伏笔操作", "ForeshadowingActions" },
            { "新增情节", "NewPlotPoints" },
            { "新情节点", "NewPlotPoints" },
            { "新增剧情", "NewPlotPoints" },
            { "地点状态变化", "LocationStateChanges" },
            { "地点状态变更", "LocationStateChanges" },
            { "地点变化", "LocationStateChanges" },
            { "势力状态变化", "FactionStateChanges" },
            { "势力状态变更", "FactionStateChanges" },
            { "势力变化", "FactionStateChanges" },
            { "时间推进", "TimeProgression" },
            { "时间进展", "TimeProgression" },
            { "角色移动", "CharacterMovements" },
            { "角色位移", "CharacterMovements" },
            { "物品流转", "ItemTransfers" },
            { "物品转移", "ItemTransfers" },
            { "道具流转", "ItemTransfers" },
            { "秘密知情变化", "SecretRevealChanges" },
            { "秘密揭示变化", "SecretRevealChanges" },
            { "秘密变化", "SecretRevealChanges" },
            { "角色ID", "CharacterId" },
            { "角色编号", "CharacterId" },
            { "新等级", "NewLevel" },
            { "新能力", "NewAbilities" },
            { "失去能力", "LostAbilities" },
            { "关系变化", "RelationshipChanges" },
            { "情感阶段", "EmotionPhase" },
            { "新心理状态", "NewMentalState" },
            { "心理状态", "NewMentalState" },
            { "关键事件", "KeyEvent" },
            { "重要性", "Importance" },
            { "原因", "CausedBy" },
            { "起因", "CausedBy" },
            { "因果", "CausedBy" },
            { "关系", "Relation" },
            { "信任变化", "TrustDelta" },
            { "信任值变化", "TrustDelta" },
            { "冲突ID", "ConflictId" },
            { "冲突编号", "ConflictId" },
            { "新状态", "NewStatus" },
            { "事件", "Event" },
            { "伏笔ID", "ForeshadowId" },
            { "伏笔编号", "ForeshadowId" },
            { "动作", "Action" },
            { "关键词", "Keywords" },
            { "上下文", "Context" },
            { "涉及角色", "InvolvedCharacters" },
            { "故事线", "Storyline" },
            { "地点ID", "LocationId" },
            { "地点编号", "LocationId" },
            { "势力ID", "FactionId" },
            { "势力编号", "FactionId" },
            { "时间段", "TimePeriod" },
            { "经过时间", "ElapsedTime" },
            { "关键时间事件", "KeyTimeEvent" },
            { "出发地", "FromLocation" },
            { "目的地", "ToLocation" },
            { "地点名称", "LocationName" },
            { "目标地点名称", "ToLocationName" },
            { "到达地点名称", "ToLocationName" },
            { "物品ID", "ItemId" },
            { "物品编号", "ItemId" },
            { "物品名称", "ItemName" },
            { "原持有者", "FromHolder" },
            { "新持有者", "ToHolder" },
            { "秘密ID", "SecretId" },
            { "秘密编号", "SecretId" },
            { "秘密名称", "SecretName" },
            { "新增知情者", "NewKnowerIds" },
            { "知情者", "NewKnowerIds" },
            { "知情角色", "NewKnowerIds" },
            { "知情方式", "Method" },
            { "承诺约束变化", "PledgeConstraintChanges" },
            { "承诺变化", "PledgeConstraintChanges" },
            { "契约变化", "PledgeConstraintChanges" },
            { "承诺ID", "PledgeId" },
            { "承诺编号", "PledgeId" },
            { "承诺名称", "PledgeName" },
            { "类型", "Type" },
            { "当事方", "PartyIds" },
            { "条件", "Condition" },
            { "后果", "Consequence" },
            { "倒计时约束变化", "DeadlineConstraintChanges" },
            { "倒计时变化", "DeadlineConstraintChanges" },
            { "时限变化", "DeadlineConstraintChanges" },
            { "倒计时ID", "DeadlineId" },
            { "倒计时编号", "DeadlineId" },
            { "倒计时名称", "DeadlineName" },
            { "截止", "Deadline" },
            { "截止条件", "Deadline" },
            { "时限", "Deadline" },
            { "触发条件", "TriggerCondition" },
            { "到期后果", "Consequence" },
        };

        private static string MapChineseFieldNames(string json)
        {
            foreach (var kv in ChineseFieldNameMap)
            {
                if (json.Contains(kv.Key))
                {
                    json = json.Replace($"\"{kv.Key}\"", $"\"{kv.Value}\"");
                    json = json.Replace($"'{kv.Key}'", $"\"{kv.Value}\"");
                }
            }
            return json;
        }

        private void ValidateRequiredFields(ProtocolValidationResult result, string jsonStr)
        {
            if (result.Changes == null)
            {
                result.AddError("CHANGES对象为空");
                return;
            }

            var requiredFields = ChapterChanges.TopLevelFieldNames;

            try
            {
                using var doc = JsonDocument.Parse(jsonStr);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    result.AddError("CHANGES JSON 不是对象");
                    return;
                }

                foreach (var field in requiredFields)
                {
                    if (!HasPropertyIgnoreCase(doc.RootElement, field))
                    {
                        result.AddError($"CHANGES缺失必需字段: {field}（必须显式声明，即使为空数组）");
                    }
                }
            }
            catch (JsonException ex)
            {
                DebugLogOnce(nameof(ValidateRequiredFields), ex);
                foreach (var field in requiredFields)
                {
                    if (!jsonStr.Contains($"\"{field}\"", StringComparison.OrdinalIgnoreCase))
                    {
                        result.AddError($"CHANGES缺失必需字段: {field}（必须显式声明，即使为空数组）");
                    }
                }
            }
        }

        private static bool HasPropertyIgnoreCase(JsonElement obj, string propertyName)
        {
            if (obj.ValueKind != JsonValueKind.Object)
                return false;

            foreach (var prop in obj.EnumerateObject())
            {
                if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static void ValidateShortIdFields(ProtocolValidationResult result)
        {
            var changes = result.Changes;
            if (changes == null) return;

            void Require(string field, string? value)
            {
                var meta = ShortIdFieldRegistry.GetMeta(field);
                var required = meta?.Required == true;
                if (string.IsNullOrWhiteSpace(value))
                {
                    if (required)
                        result.AddError($"CHANGES协议违规：{field} 必须为 ShortId（格式：13字符、大写字母开头），收到空值或缺失");
                    return;
                }

                if (!ShortIdGenerator.IsLikelyId(value))
                    result.AddError($"CHANGES协议违规：{field} 必须为 ShortId（格式：13字符、大写字母开头），收到非法格式");
            }

            foreach (var ch in changes.CharacterStateChanges ?? new())
            {
                Require(F.CharStateCharacterId, ch.CharacterId);
                foreach (var key in ch.RelationshipChanges?.Keys ?? Enumerable.Empty<string>())
                    Require(F.CharStateRelationshipKey, key);
            }

            foreach (var cp in changes.ConflictProgress ?? new())
                Require(F.ConflictConflictId, cp.ConflictId);

            foreach (var fa in changes.ForeshadowingActions ?? new())
                Require(F.ForeshadowForeshadowId, fa.ForeshadowId);

            foreach (var loc in changes.LocationStateChanges ?? new())
                Require(F.LocationLocationId, loc.LocationId);

            foreach (var fac in changes.FactionStateChanges ?? new())
                Require(F.FactionFactionId, fac.FactionId);

            foreach (var move in changes.CharacterMovements ?? new())
            {
                Require(F.MovementCharacterId, move.CharacterId);
                Require(F.MovementFromLocation, move.FromLocation);
                Require(F.MovementToLocation, move.ToLocation);
            }

            foreach (var transfer in changes.ItemTransfers ?? new())
            {
                Require(F.ItemTransferItemId, transfer.ItemId);
                if (!string.IsNullOrWhiteSpace(transfer.FromHolder))
                    Require(F.ItemTransferFromHolder, transfer.FromHolder);
                if (!string.IsNullOrWhiteSpace(transfer.ToHolder))
                    Require(F.ItemTransferToHolder, transfer.ToHolder);
            }

            foreach (var s in changes.SecretRevealChanges ?? new())
            {
                Require(F.SecretSecretId, s.SecretId);
                foreach (var k in s.NewKnowerIds ?? new())
                    Require(F.SecretNewKnowerId, k);
            }

            foreach (var pp in changes.NewPlotPoints ?? new())
                foreach (var ic in pp.InvolvedCharacters ?? new())
                    Require(F.PlotInvolvedCharacter, ic);

            foreach (var pc in changes.PledgeConstraintChanges ?? new())
            {
                Require(F.PledgePledgeId, pc.PledgeId);
                foreach (var pid in pc.PartyIds ?? new())
                    Require(F.PledgePartyId, pid);
            }

            foreach (var dc in changes.DeadlineConstraintChanges ?? new())
            {
                Require(F.DeadlineDeadlineId, dc.DeadlineId);
                foreach (var pid in dc.PartyIds ?? new())
                    Require(F.DeadlinePartyId, pid);
            }
        }

        private static List<string> ValidateShortIdReferences(ChapterChanges changes, FactSnapshot snapshot, ContextIdCollection? contextIds = null)
        {
            var errors = new List<string>();

            static HashSet<string> BuildSet(IEnumerable<string?> values)
                => new(values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()), StringComparer.OrdinalIgnoreCase);

            var allowedChar = BuildSet(snapshot.CharacterStates?.Select(s => (string?)s.Id) ?? Enumerable.Empty<string?>());
            allowedChar.UnionWith(snapshot.CharacterDescriptions?.Keys ?? Enumerable.Empty<string>());
            allowedChar.UnionWith(snapshot.CharacterLocations?.Where(l => !string.IsNullOrWhiteSpace(l.CharacterId)).Select(l => l.CharacterId!) ?? Enumerable.Empty<string>());

            var allowedLoc = BuildSet(snapshot.LocationStates?.Select(l => (string?)l.Id) ?? Enumerable.Empty<string?>());
            allowedLoc.UnionWith(snapshot.LocationDescriptions?.Keys ?? Enumerable.Empty<string>());
            allowedLoc.UnionWith(snapshot.CharacterLocations?.Where(l => !string.IsNullOrWhiteSpace(l.CurrentLocation)).Select(l => l.CurrentLocation!) ?? Enumerable.Empty<string>());

            var allowedFac = BuildSet(snapshot.FactionStates?.Select(f => (string?)f.Id) ?? Enumerable.Empty<string?>());
            var allowedConflict = BuildSet(snapshot.ConflictProgress?.Select(c => (string?)c.Id) ?? Enumerable.Empty<string?>());
            var allowedFs = BuildSet(snapshot.ForeshadowingStatus?.Select(f => (string?)f.Id) ?? Enumerable.Empty<string?>());

            if (contextIds != null)
            {
                foreach (var id in contextIds.Characters ?? new()) if (!string.IsNullOrWhiteSpace(id)) allowedChar.Add(id);
                foreach (var id in contextIds.Locations ?? new()) if (!string.IsNullOrWhiteSpace(id)) allowedLoc.Add(id);
                foreach (var id in contextIds.Factions ?? new()) if (!string.IsNullOrWhiteSpace(id)) allowedFac.Add(id);
                foreach (var id in contextIds.Conflicts ?? new()) if (!string.IsNullOrWhiteSpace(id)) allowedConflict.Add(id);
                foreach (var id in contextIds.ForeshadowingSetups ?? new()) if (!string.IsNullOrWhiteSpace(id)) allowedFs.Add(id);
                foreach (var id in contextIds.ForeshadowingPayoffs ?? new()) if (!string.IsNullOrWhiteSpace(id)) allowedFs.Add(id);
            }
            var allowedItem = BuildSet(snapshot.ItemStates?.Select(i => (string?)i.Id) ?? Enumerable.Empty<string?>());
            var allowedSecret = BuildSet(snapshot.SecretStates?.Select(s => (string?)s.Id) ?? Enumerable.Empty<string?>());
            var allowedPledge = BuildSet(snapshot.PledgeStates?.Select(p => (string?)p.Id) ?? Enumerable.Empty<string?>());
            var allowedDeadline = BuildSet(snapshot.DeadlineStates?.Select(d => (string?)d.Id) ?? Enumerable.Empty<string?>());

            HashSet<string> AllowedFor(ShortIdEntityType t) => t switch
            {
                ShortIdEntityType.Character => allowedChar,
                ShortIdEntityType.Location => allowedLoc,
                ShortIdEntityType.Conflict => allowedConflict,
                ShortIdEntityType.Foreshadowing => allowedFs,
                ShortIdEntityType.Faction => allowedFac,
                ShortIdEntityType.Item => allowedItem,
                ShortIdEntityType.Secret => allowedSecret,
                ShortIdEntityType.Pledge => allowedPledge,
                ShortIdEntityType.Deadline => allowedDeadline,
                _ => new HashSet<string>(),
            };

            var creatableTypes = new HashSet<ShortIdEntityType>
            {
                ShortIdEntityType.Item, ShortIdEntityType.Secret,
                ShortIdEntityType.Pledge, ShortIdEntityType.Deadline,
                ShortIdEntityType.Location
            };

            void RequireIn(string field, string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                var entityType = ShortIdFieldRegistry.GetEntityType(field);
                if (entityType == null) return;
                var allowed = AllowedFor(entityType.Value);
                if (allowed.Count == 0)
                {
                    if (!creatableTypes.Contains(entityType.Value))
                        errors.Add($"CHANGES协议违规：{ShortIdFieldRegistry.GetChineseName(field)} 账本无追踪记录，字段 {field} 应留空，收到非法值'{value}'");
                    return;
                }
                if (allowed.Contains(value)) return;
                errors.Add($"CHANGES协议违规：{field} 必须为 ShortId（必须来自事实账本），收到非法值'{value}'");
            }

            var createdLocIds = new HashSet<string>(
                (changes.LocationStateChanges ?? new())
                    .Where(l => !string.IsNullOrWhiteSpace(l.LocationName) && !string.IsNullOrWhiteSpace(l.LocationId))
                    .Select(l => l.LocationId!.Trim()),
                StringComparer.OrdinalIgnoreCase);
            createdLocIds.UnionWith(
                (changes.CharacterMovements ?? new())
                    .Where(m => !string.IsNullOrWhiteSpace(m.ToLocationName) && !string.IsNullOrWhiteSpace(m.ToLocation))
                    .Select(m => m.ToLocation!.Trim()));
            allowedLoc.UnionWith(createdLocIds);

            foreach (var ch in changes.CharacterStateChanges ?? new())
            {
                RequireIn(F.CharStateCharacterId, ch.CharacterId);
                foreach (var key in ch.RelationshipChanges?.Keys ?? Enumerable.Empty<string>())
                    RequireIn(F.CharStateRelationshipKey, key);
            }

            foreach (var cp in changes.ConflictProgress ?? new())
                RequireIn(F.ConflictConflictId, cp.ConflictId);

            foreach (var fa in changes.ForeshadowingActions ?? new())
                RequireIn(F.ForeshadowForeshadowId, fa.ForeshadowId);

            foreach (var loc in changes.LocationStateChanges ?? new())
            {
                if (!string.IsNullOrWhiteSpace(loc.LocationId))
                {
                    if (allowedLoc.Count == 0)
                    {
                        if (string.IsNullOrWhiteSpace(loc.LocationName))
                            errors.Add($"CHANGES协议违规：{F.LocationLocationId} 必须为 ShortId（必须来自事实账本或提供LocationName以创建新地点），收到非法值'{loc.LocationId}'");
                    }
                    else if (allowedLoc.Contains(loc.LocationId) || string.IsNullOrWhiteSpace(loc.LocationName))
                    {
                        RequireIn(F.LocationLocationId, loc.LocationId);
                    }
                }
            }

            foreach (var fac in changes.FactionStateChanges ?? new())
                RequireIn(F.FactionFactionId, fac.FactionId);

            foreach (var move in changes.CharacterMovements ?? new())
            {
                RequireIn(F.MovementCharacterId, move.CharacterId);
                if (!string.IsNullOrWhiteSpace(move.FromLocation)
                    && !createdLocIds.Contains(move.FromLocation))
                {
                    RequireIn(F.MovementFromLocation, move.FromLocation);
                }
                if (!string.IsNullOrWhiteSpace(move.ToLocation))
                {
                    if (allowedLoc.Count == 0)
                    {
                        if (string.IsNullOrWhiteSpace(move.ToLocationName))
                            errors.Add($"CHANGES协议违规：{F.MovementToLocation} 必须为 ShortId（必须来自事实账本或提供ToLocationName以创建新地点），收到非法值'{move.ToLocation}'");
                    }
                    else if (allowedLoc.Contains(move.ToLocation) || string.IsNullOrWhiteSpace(move.ToLocationName))
                    {
                        RequireIn(F.MovementToLocation, move.ToLocation);
                    }
                }
            }

            foreach (var transfer in changes.ItemTransfers ?? new())
            {
                if (!string.IsNullOrWhiteSpace(transfer.ItemId))
                {
                    if (allowedItem.Count == 0)
                    {
                        if (string.IsNullOrWhiteSpace(transfer.ItemName))
                            errors.Add($"CHANGES协议违规：{F.ItemTransferItemId} 必须为 ShortId（必须来自事实账本或提供ItemName以创建新物品），收到非法值'{transfer.ItemId}'");
                    }
                    else if (allowedItem.Contains(transfer.ItemId) || string.IsNullOrWhiteSpace(transfer.ItemName))
                    {
                        RequireIn(F.ItemTransferItemId, transfer.ItemId);
                    }
                }
                RequireIn(F.ItemTransferFromHolder, transfer.FromHolder);
                RequireIn(F.ItemTransferToHolder, transfer.ToHolder);
            }

            foreach (var s in changes.SecretRevealChanges ?? new())
            {
                if (!string.IsNullOrWhiteSpace(s.SecretId))
                {
                    if (allowedSecret.Count == 0)
                    {
                        if (string.IsNullOrWhiteSpace(s.SecretName))
                            errors.Add($"CHANGES协议违规：{F.SecretSecretId} 必须为 ShortId（必须来自事实账本或提供SecretName以创建新秘密），收到非法值'{s.SecretId}'");
                    }
                    else if (allowedSecret.Contains(s.SecretId) || string.IsNullOrWhiteSpace(s.SecretName))
                    {
                        RequireIn(F.SecretSecretId, s.SecretId);
                    }
                }
                foreach (var k in s.NewKnowerIds ?? new())
                    RequireIn(F.SecretNewKnowerId, k);
            }

            foreach (var pp in changes.NewPlotPoints ?? new())
                foreach (var ic in pp.InvolvedCharacters ?? new())
                    RequireIn(F.PlotInvolvedCharacter, ic);

            foreach (var pc in changes.PledgeConstraintChanges ?? new())
            {
                if (!string.IsNullOrWhiteSpace(pc.PledgeId))
                {
                    bool isCreate = string.Equals(pc.Action, "create", StringComparison.OrdinalIgnoreCase);
                    if (allowedPledge.Count == 0)
                    {
                        if (!isCreate || string.IsNullOrWhiteSpace(pc.PledgeName))
                            errors.Add($"CHANGES协议违规：{F.PledgePledgeId} 必须为 ShortId（必须来自事实账本或提供Action=create+PledgeName以创建新承诺），收到非法值'{pc.PledgeId}'（Action='{pc.Action}'）");
                    }
                    else if (isCreate && !allowedPledge.Contains(pc.PledgeId) && !string.IsNullOrWhiteSpace(pc.PledgeName))
                    {
                    }
                    else
                    {
                        RequireIn(F.PledgePledgeId, pc.PledgeId);
                    }
                }
                foreach (var pid in pc.PartyIds ?? new())
                    RequireIn(F.PledgePartyId, pid);
            }

            foreach (var dc in changes.DeadlineConstraintChanges ?? new())
            {
                if (!string.IsNullOrWhiteSpace(dc.DeadlineId))
                {
                    bool isCreate = string.Equals(dc.Action, "create", StringComparison.OrdinalIgnoreCase);
                    if (allowedDeadline.Count == 0)
                    {
                        if (!isCreate || string.IsNullOrWhiteSpace(dc.DeadlineName))
                            errors.Add($"CHANGES协议违规：{F.DeadlineDeadlineId} 必须为 ShortId（必须来自事实账本或提供Action=create+DeadlineName以创建新倒计时），收到非法值'{dc.DeadlineId}'（Action='{dc.Action}'）");
                    }
                    else if (isCreate && !allowedDeadline.Contains(dc.DeadlineId) && !string.IsNullOrWhiteSpace(dc.DeadlineName))
                    {
                    }
                    else
                    {
                        RequireIn(F.DeadlineDeadlineId, dc.DeadlineId);
                    }
                }
                foreach (var pid in dc.PartyIds ?? new())
                    RequireIn(F.DeadlinePartyId, pid);
            }

            return errors;
        }

        private List<string> ValidateWorldRuleConstraints(string content, List<WorldRuleConstraint> constraints)
        {
            var violations = new List<string>();
            if (string.IsNullOrEmpty(content))
                return violations;

            foreach (var rule in constraints.Where(c => c.IsHardConstraint))
            {
                if (string.IsNullOrEmpty(rule.Constraint))
                    continue;

                var violation = CheckConstraintViolation(content, rule);
                if (violation != null)
                {
                    violations.Add($"世界观硬约束违反 [{rule.RuleName}]: {violation}");
                }
            }

            return violations;
        }

        private string? CheckConstraintViolation(string content, WorldRuleConstraint rule)
        {
            var constraint = rule.Constraint;

            foreach (var negation in NegationPatterns)
            {
                var idx = constraint.IndexOf(negation, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    var actionStart = idx + negation.Length;
                    var actionLength = Math.Min(10, constraint.Length - actionStart);
                    if (actionLength > 0)
                    {
                        var forbiddenAction = constraint.Substring(actionStart, actionLength).Trim();
                        forbiddenAction = forbiddenAction.TrimEnd('，', '。', '、', '；');

                        if (!string.IsNullOrEmpty(forbiddenAction) && forbiddenAction.Length >= 2)
                        {
                            if (HasViolatingOccurrence(content, forbiddenAction))
                            {
                                return $"正文出现被禁止的内容「{forbiddenAction}」（约束：{constraint}）";
                            }
                        }
                    }
                }
            }

            return null;
        }

        private static bool HasViolatingOccurrence(string content, string forbiddenAction)
        {

            var searchStart = 0;
            while (searchStart < content.Length)
            {
                var pos = content.IndexOf(forbiddenAction, searchStart, StringComparison.Ordinal);
                if (pos < 0) break;

                var contextStart = Math.Max(0, pos - 15);
                var prefix = content.Substring(contextStart, pos - contextStart);
                var sentBreak = prefix.LastIndexOfAny(new[] { '。', '！', '？', '；', '\n' });
                if (sentBreak >= 0) prefix = prefix.Substring(sentBreak + 1);

                var isNegated = false;
                foreach (var neg in NegationPrefixes)
                {
                    if (prefix.Contains(neg, StringComparison.Ordinal))
                    {
                        isNegated = true;
                        break;
                    }
                }

                if (!isNegated)
                {
                    return true;
                }

                searchStart = pos + forbiddenAction.Length;
            }

            return false;
        }

        private const int ForgedIdHardThreshold = 5;
        private const string ForgedIdMarker = "可能为伪造";

        private static int CountForgedIds(CanonicalizationResult canonResult)
        {
            if (canonResult == null) return 0;
            var fromPatch = canonResult.PatchLog?.Count(p => !string.IsNullOrEmpty(p)
                && p.Contains(ForgedIdMarker, StringComparison.Ordinal)) ?? 0;
            var fromAmbig = canonResult.AmbiguousFields?.Count(a => !string.IsNullOrEmpty(a)
                && a.Contains(ForgedIdMarker, StringComparison.Ordinal)) ?? 0;
            return fromPatch + fromAmbig;
        }

        private static int CountChangesItems(ChapterChanges? changes)
        {
            if (changes == null) return 0;
            var count = 0;
            count += changes.CharacterStateChanges?.Count ?? 0;
            count += changes.ConflictProgress?.Count ?? 0;
            count += changes.NewPlotPoints?.Count ?? 0;
            count += changes.ForeshadowingActions?.Count ?? 0;
            count += changes.LocationStateChanges?.Count ?? 0;
            count += changes.FactionStateChanges?.Count ?? 0;
            count += changes.CharacterMovements?.Count ?? 0;
            count += changes.ItemTransfers?.Count ?? 0;
            count += changes.SecretRevealChanges?.Count ?? 0;
            count += changes.PledgeConstraintChanges?.Count ?? 0;
            count += changes.DeadlineConstraintChanges?.Count ?? 0;
            return count;
        }

        #endregion
    }
}
