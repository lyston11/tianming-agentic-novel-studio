using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Services.Framework.AI.SemanticKernel;
using TM.Services.Modules.ProjectData.Models.Tracking;
using TM.Services.Modules.ProjectData.Implementations.Generation;
using TM.Services.Modules.ProjectData.Models.Guides;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class GenerationGate
    {
        #region 公开方法

        public static (int index, int length) FindSeparatorIndex(string content)
        {
            if (string.IsNullOrEmpty(content))
                return (-1, 0);

            var xmlBlock = ChangesXmlBlockRegex.Matches(content).Cast<Match>().LastOrDefault();
            if (xmlBlock?.Success == true)
            {
                var openTagEnd = content.IndexOf('>', xmlBlock.Index);
                return openTagEnd >= xmlBlock.Index
                    ? (xmlBlock.Index, openTagEnd - xmlBlock.Index + 1)
                    : (xmlBlock.Index, xmlBlock.Length);
            }

            var xmlOpen = ChangesXmlOpenRegex.Matches(content).Cast<Match>().LastOrDefault();
            if (xmlOpen?.Success == true)
                return (xmlOpen.Index, xmlOpen.Length);

            var idx = content.IndexOf(ChangesSeparator, StringComparison.Ordinal);
            if (idx >= 0)
                return (idx, ChangesSeparator.Length);

            var m = ChangesSeparatorLineRegex.Match(content);
            if (m.Success)
                return (m.Index, m.Length);

            return (-1, 0);
        }

        public static int FindChangesStartIndex(string content)
        {
            if (string.IsNullOrEmpty(content))
                return -1;

            var xmlBlock = ChangesXmlBlockRegex.Matches(content).Cast<Match>().LastOrDefault();
            if (xmlBlock?.Success == true) return xmlBlock.Index;

            var xmlOpen = ChangesXmlOpenRegex.Matches(content).Cast<Match>().LastOrDefault();
            if (xmlOpen?.Success == true) return xmlOpen.Index;

            var idx = content.IndexOf(ChangesSeparator, StringComparison.Ordinal);
            if (idx >= 0) return idx;

            var sepMatch = ChangesSeparatorLineRegex.Match(content);
            if (sepMatch.Success) return sepMatch.Index;

            var mdMatch = MdChangesHeaderRegex.Match(content);
            if (mdMatch.Success) return mdMatch.Index;

            return -1;
        }

        public static bool HasChangesRegion(string content)
        {
            if (string.IsNullOrEmpty(content)) return false;
            var (_, changesPart, _) = IdentifyChangesRegion(content);
            return changesPart != null;
        }

        public static string StripChangesSection(string content)
        {
            if (string.IsNullOrEmpty(content)) return content;
            var (body, changesPart, _) = IdentifyChangesRegion(content);
            return changesPart != null ? body : content;
        }

        public ConsistencyResult ValidateStructuralOnly(ChapterChanges changes)
            => _ledgerConsistencyChecker.ValidateStructuralOnly(changes);

        public async Task<GateResult> ValidateAsync(
            string chapterId,
            string rawContent,
            FactSnapshot factSnapshot,
            DesignElementNames? designElements = null,
            ContextIdCollection? contextIds = null)
        {
            var result = new GateResult { ChapterId = chapterId };

            TM.App.Log($"[GG][{GenerationCorrelation.Current}] start: {chapterId}");

            var protocolResult = ValidateChangesProtocol(rawContent, factSnapshot, contextIds);
            if (!protocolResult.Success)
            {
                result.AddFailure(FailureType.Protocol, protocolResult.Errors);
                if (InfoLogDedup.ShouldLog("GG:Protocol:Fail"))
                    TM.App.Log($"[GG] fail: Protocol - {string.Join("; ", protocolResult.Errors.Take(3))}");
                return result;
            }

            result.ParsedChanges = protocolResult.Changes;
            result.ContentWithoutChanges = protocolResult.ContentWithoutChanges;

            var shortIdRefErrors = ValidateShortIdReferences(protocolResult.Changes!, factSnapshot, contextIds);
            if (shortIdRefErrors.Count > 0)
            {
                result.AddFailure(FailureType.Protocol, shortIdRefErrors);
                TM.App.Log($"[GG] fail: ShortIdRef - {string.Join("; ", shortIdRefErrors.Take(3))}");
                return result;
            }

            var ruleSet = await _ledgerRuleSetProvider.GetRuleSetForGateAsync().ConfigureAwait(false);
            var consistencyResult = _ledgerConsistencyChecker.Validate(
                protocolResult.Changes!,
                factSnapshot,
                ruleSet);
            if (!consistencyResult.Success)
            {
                result.AddConsistencyFailure(consistencyResult.Issues);
                TM.App.Log($"[GG] fail: Consistency - {string.Join("; ", consistencyResult.Issues.Take(3).Select(i => i.IssueType))}");
                return result;
            }

            var contentToValidate = protocolResult.ContentWithoutChanges ?? "";

            var step4Task = Task.Run(() =>
            {
                var entityExtractor = new ContentEntityExtractor(factSnapshot);
                return entityExtractor.GetUnknownEntities(contentToValidate);
            });

            var step5Task = Task.Run(() =>
            {
                var issues = new List<string>();
                if (factSnapshot.CharacterDescriptions.Count > 0 || factSnapshot.LocationDescriptions.Count > 0)
                {
                    var descValidator = new ContentDescriptionValidator();
                    issues.AddRange(descValidator.ValidateCharacterDescriptions(contentToValidate, factSnapshot.CharacterDescriptions));
                    issues.AddRange(descValidator.ValidateLocationDescriptions(contentToValidate, factSnapshot.LocationDescriptions));
                }
                return issues;
            });

            var step6Task = Task.Run(() =>
            {
                if (factSnapshot.WorldRuleConstraints.Count > 0)
                {
                    return ValidateWorldRuleConstraints(contentToValidate, factSnapshot.WorldRuleConstraints);
                }
                return new List<string>();
            });

            await Task.WhenAll(step4Task, step5Task, step6Task).ConfigureAwait(false);

            var unknownEntities = await step4Task.ConfigureAwait(false);
            var descIssues = await step5Task.ConfigureAwait(false);
            var ruleViolations = await step6Task.ConfigureAwait(false);

            if (unknownEntities.Count > 0)
            {
                var changes = protocolResult.Changes;
                var functionalEntities = unknownEntities
                    .Where(e => IsEntityInChanges(e, changes))
                    .ToList();
                var backgroundEntities = unknownEntities
                    .Where(e => !IsEntityInChanges(e, changes))
                    .ToList();

                if (unknownEntities.Count > 5 || backgroundEntities.Count > 3)
                {
                    var functionalSet = new HashSet<string>(functionalEntities, StringComparer.OrdinalIgnoreCase);
                    var failures = unknownEntities
                        .Select(e => functionalSet.Contains(e)
                            ? $"正文引入未登记实体(有剧情作用): {e}"
                            : $"正文引入未登记实体(龙套): {e}")
                        .ToList();
                    result.AddFailure(FailureType.Consistency, failures);
                    TM.App.Log($"[GG] fail: 未知实体超限 功能性{functionalEntities.Count}个 龙套{backgroundEntities.Count}个");
                    return result;
                }
                else
                {
                    TM.App.Log($"[GG] warn: 未知实体 功能性{functionalEntities.Count}个 龙套{backgroundEntities.Count}个（允许通过）");
                }
            }

            if (descIssues.Count > 0)
            {
                result.AddFailure(FailureType.Consistency, descIssues);
                TM.App.Log($"[GG] fail: Description - {string.Join("; ", descIssues.Take(3))}");
                return result;
            }

            if (ruleViolations.Count > 0)
            {
                result.AddFailure(FailureType.Consistency, ruleViolations);
                TM.App.Log($"[GG] fail: WorldRule - {string.Join("; ", ruleViolations.Take(3))}");
                return result;
            }

            if (designElements != null)
            {
                var (povFailures, designIssues, qualityWarnings) = ValidateDesignElementPresence(contentToValidate, designElements);
                if (qualityWarnings.Count > 0)
                    TM.App.Log($"[GG] quality-warn: {string.Join("; ", qualityWarnings)}");
                if (povFailures.Count > 0)
                {
                    TM.App.Log($"[GG] quality-warn: POV角色缺席（可接受） {string.Join("; ", povFailures)}");
                }
                var totalElements = designElements.CharacterNames.Count
                                  + designElements.FactionNames.Count
                                  + designElements.LocationNames.Count
                                  + designElements.PlotKeyNames.Count;
                var threshold = Math.Max(3, totalElements / 3);
                if (designIssues.Count > threshold)
                {
                    result.AddFailure(FailureType.Consistency, designIssues);
                    TM.App.Log($"[GG][{GenerationCorrelation.Current}] fail: design {designIssues.Count}/{totalElements} missing (threshold={threshold})");
                    return result;
                }
                else if (designIssues.Count > 0)
                {
                    TM.App.Log($"[GG] warn: design {designIssues.Count}/{totalElements} missing (threshold={threshold}, pass)");
                }
            }

            try
            {
                var omissions = await _omissionDetector.DetectAsync(contentToValidate, protocolResult.Changes!).ConfigureAwait(false);
                if (omissions.Count > 0)
                {
                    var residualOmissions = new List<EntityOmissionRecord>();
                    var autoPatched = new List<EntityOmissionRecord>();
                    foreach (var o in omissions)
                    {
                        var descriptor = EntityDimensionRegistry.GetByCode(o.DimensionCode);
                        if (descriptor?.Strategy == DriftStrategy.AutoPatch && descriptor.AutoPatchAction != null)
                        {
                            try
                            {
                                descriptor.AutoPatchAction(protocolResult.Changes!, o.EntityId, o.EntityName, "在正文中出现，无显式状态变化");
                                autoPatched.Add(o);
                            }
                            catch (Exception patchEx)
                            {
                                TM.App.Log($"[GG] 漏报自动补录失败（{o.DimensionName}/{o.EntityName}）: {patchEx.Message}");
                                residualOmissions.Add(o);
                            }
                        }
                        else
                        {
                            residualOmissions.Add(o);
                        }
                    }

                    if (autoPatched.Count > 0)
                        TM.App.Log($"[GG] 漏报自动补录 {autoPatched.Count} 项: {string.Join("; ", autoPatched.Take(3).Select(o => $"{o.DimensionName}/{o.EntityName}"))}");

                    if (residualOmissions.Count > 0)
                    {
                        var issues = residualOmissions.Select(o => new ConsistencyIssue
                        {
                            EntityId = o.EntityId,
                            IssueType = IssueTypes.OmittedDeclaration,
                            Expected = $"[漏报] {o.DimensionName}'{o.EntityName}'出现于本章正文应在 CHANGES.{o.ChangeFieldName} 中申报",
                            Actual = $"[漏报] {o.DimensionName}'{o.EntityName}'出现在正文但 CHANGES.{o.ChangeFieldName} 未申报"
                        }).ToList();
                        result.AddConsistencyFailure(issues);
                        TM.App.Log($"[GG] fail: Omission {residualOmissions.Count} 项（不可自动补录）: {string.Join("; ", residualOmissions.Take(3).Select(o => $"{o.DimensionName}/{o.EntityName}"))}");
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GG] 漏报检测异常（非致命，跳过）: {ex.Message}");
            }

            result.Success = true;
            TM.App.Log($"[GG][{GenerationCorrelation.Current}] ok: {chapterId}");
            return result;
        }

        private static (List<string> povFailures, List<string> issues, List<string> qualityWarnings) ValidateDesignElementPresence(string content, DesignElementNames elements)
        {
            var povFailures = new List<string>();
            var issues = new List<string>();
            var qualityWarnings = new List<string>();

            foreach (var name in elements.PovCharacterNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var count = CountNameInContent(content, name);
                if (count == 0)
                    povFailures.Add($"视角角色未在正文出现: {name}");
                else if (count == 1)
                    qualityWarnings.Add($"视角角色'{name}'仅出现1次，叙事力度不足");
            }

            foreach (var name in elements.CharacterNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var count = CountNameInContent(content, name);
                if (count == 0)
                    issues.Add($"指定角色未在正文出现: {name}");
                else if (count == 1)
                    qualityWarnings.Add($"角色'{name}'仅出现1次，疑似背景一提");
            }

            foreach (var name in elements.FactionNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var count = CountNameInContent(content, name);
                if (count == 0)
                    issues.Add($"指定势力未在正文出现: {name}");
                else if (count == 1)
                    qualityWarnings.Add($"势力'{name}'仅出现1次，疑似背景一提");
            }

            foreach (var name in elements.LocationNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var count = CountNameInContent(content, name);
                if (count == 0)
                    issues.Add($"指定地点未在正文出现: {name}");
                else if (count == 1)
                    qualityWarnings.Add($"地点'{name}'仅出现1次，疑似背景一提");
            }

            foreach (var name in elements.PlotKeyNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var count = CountNameInContent(content, name);
                if (count == 0)
                    issues.Add($"剧情关键角色未在正文出现: {name}");
                else if (count == 1)
                    qualityWarnings.Add($"剧情关键角色'{name}'仅出现1次，疑似背景一提");
            }

            return (povFailures, issues, qualityWarnings);
        }

        private static int CountNameInContent(string content, string fullName)
        {
            if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(fullName))
                return 0;

            var variants = new List<string>();
            void AddVariant(string? s)
            {
                if (string.IsNullOrWhiteSpace(s)) return;
                var v = s.Trim();
                if (v.Length < 2) return;
                if (!variants.Contains(v)) variants.Add(v);
            }

            AddVariant(fullName);

            var primaryName = EntityNameNormalizeHelper.StripBracketAnnotation(fullName);
            if (!string.IsNullOrWhiteSpace(primaryName) && !string.Equals(primaryName, fullName, StringComparison.Ordinal))
                AddVariant(primaryName);

            var aliasMatches = BracketAliasRegex.Matches(fullName);
            foreach (Match m in aliasMatches)
                AddVariant(m.Groups[1].Value);

            var maxCount = 0;
            foreach (var nameToCount in variants)
            {
                var count = 0;
                var idx = 0;
                while ((idx = content.IndexOf(nameToCount, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    count++;
                    idx += nameToCount.Length;
                }
                if (count > maxCount) maxCount = count;
            }

            return maxCount;
        }

        private static bool IsEntityInChanges(string entityName, ChapterChanges? changes)
        {
            if (changes == null || string.IsNullOrWhiteSpace(entityName))
                return false;

            var name = entityName.Trim();

            foreach (var c in changes.CharacterStateChanges ?? new())
                if (ContainsEntityName(c.CharacterId, name) || ContainsEntityName(c.KeyEvent, name))
                    return true;

            foreach (var c in changes.ConflictProgress ?? new())
                if (ContainsEntityName(c.ConflictId, name) || ContainsEntityName(c.Event, name))
                    return true;

            foreach (var f in changes.ForeshadowingActions ?? new())
                if (ContainsEntityName(f.ForeshadowId, name))
                    return true;

            foreach (var p in changes.NewPlotPoints ?? new())
                if (ContainsEntityName(p.Context, name) ||
                    (p.InvolvedCharacters?.Any(ic => ContainsEntityName(ic, name)) == true))
                    return true;

            foreach (var l in changes.LocationStateChanges ?? new())
                if (ContainsEntityName(l.LocationId, name) || ContainsEntityName(l.LocationName, name) || ContainsEntityName(l.Event, name))
                    return true;

            foreach (var fa in changes.FactionStateChanges ?? new())
                if (ContainsEntityName(fa.FactionId, name) || ContainsEntityName(fa.Event, name))
                    return true;

            foreach (var m in changes.CharacterMovements ?? new())
                if (ContainsEntityName(m.CharacterId, name) || ContainsEntityName(m.ToLocationName, name) || ContainsEntityName(m.ToLocation, name) || ContainsEntityName(m.FromLocation, name))
                    return true;

            foreach (var it in changes.ItemTransfers ?? new())
                if (ContainsEntityName(it.ItemName, name) || ContainsEntityName(it.Event, name))
                    return true;

            return false;
        }

        private static bool ContainsEntityName(string? text, string entityName)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(entityName))
                return false;
            return text.Contains(entityName, StringComparison.OrdinalIgnoreCase);
        }

        public ProtocolValidationResult ValidateChangesProtocol(string rawContent, FactSnapshot? snapshot = null, ContextIdCollection? contextIds = null)
        {
            var result = new ProtocolValidationResult();

            if (string.IsNullOrEmpty(rawContent))
            {
                result.AddError("内容为空");
                return result;
            }

            var (contentPart, changesPart, formatType) = IdentifyChangesRegion(rawContent);

            if (changesPart == null)
            {
                result.AddError($"未识别到CHANGES区域（首选格式：{ChangesXmlOpen}...{ChangesXmlClose}；兼容旧格式和末尾 JSON）");
                return result;
            }

            result.ContentWithoutChanges = contentPart;
            TM.App.Log($"[GG] 识别到CHANGES区域，格式: {formatType}");

            var (changes, parseError) = ParseChangesContent(changesPart);

            if (changes == null)
            {
                result.AddError(parseError ?? "CHANGES解析失败");
                return result;
            }

            result.Changes = changes;

            var jsonStr = ExtractJsonFromChangesSection(changesPart);
            if (!string.IsNullOrEmpty(jsonStr))
            {
                var repairedJsonStr = RepairChangesJson(jsonStr);
                ValidateRequiredFields(result, repairedJsonStr);
            }

            if (!result.HasErrors && result.Changes != null && snapshot != null)
            {
                var canonResult = ChapterChangesCanonicalizer.Canonicalize(result.Changes, snapshot, contextIds);
                result.Changes = canonResult.Canonical;
                var corrId = GenerationCorrelation.Current;
                if (canonResult.HasPatches && InfoLogDedup.ShouldLog($"GG:Canon:Patch:{corrId}"))
                    TM.App.Log($"[GG] 归一化补丁({canonResult.PatchLog.Count}): {string.Join("; ", canonResult.PatchLog)}");
                if (canonResult.AmbiguousFields.Count > 0 && InfoLogDedup.ShouldLog($"GG:Canon:Ambig:{corrId}"))
                    TM.App.Log($"[GG] 归一化歧义({canonResult.AmbiguousFields.Count}): {string.Join("; ", canonResult.AmbiguousFields)}");

                var forgedCount = CountForgedIds(canonResult);
                if (forgedCount > 0)
                {
                    var totalKept = CountChangesItems(result.Changes);
                    var totalAttempted = totalKept + forgedCount;
                    GenerationProgressHub.Report($"⚠ 模型本次伪造了 {forgedCount} 处实体ID（账本不存在），系统已自动剔除");

                    if (forgedCount >= ForgedIdHardThreshold && forgedCount * 5 >= totalAttempted * 4)
                    {
                        var msg = $"模型大面积伪造实体ID（{forgedCount}/{totalAttempted}），请严格使用账本中已存在的实体名称或 ShortId，禁止自造不存在的ID";
                        result.AddError(msg);
                        GenerationProgressHub.Report($"⚠ 检测到大面积伪造（{forgedCount}/{totalAttempted}），触发重写");
                        return result;
                    }
                }
            }

            if (!result.HasErrors)
            {
                ValidateShortIdFields(result);
            }

            result.Success = !result.HasErrors;
            return result;
        }

        private enum ChangesFormatType
        {
            XmlBlock,
            XmlOpenOnly,
            WithMarker,
            TrailingJsonOnly
        }

        private static (string content, string? changes, ChangesFormatType format) IdentifyChangesRegion(string rawContent)
        {
            var xmlBlockMatch = ChangesXmlBlockRegex.Matches(rawContent).Cast<Match>().LastOrDefault();
            if (xmlBlockMatch?.Success == true)
            {
                return (
                    rawContent.Substring(0, xmlBlockMatch.Index).Trim(),
                    xmlBlockMatch.Groups[2].Value.Trim(),
                    ChangesFormatType.XmlBlock
                );
            }

            var xmlOpenMatch = ChangesXmlOpenRegex.Matches(rawContent).Cast<Match>().LastOrDefault();
            if (xmlOpenMatch?.Success == true)
            {
                return (
                    rawContent.Substring(0, xmlOpenMatch.Index).Trim(),
                    rawContent.Substring(xmlOpenMatch.Index + xmlOpenMatch.Length).Trim(),
                    ChangesFormatType.XmlOpenOnly
                );
            }

            var idx = rawContent.IndexOf(ChangesSeparator, StringComparison.Ordinal);
            if (idx >= 0)
            {
                return (
                    rawContent.Substring(0, idx).Trim(),
                    rawContent.Substring(idx + ChangesSeparator.Length).Trim(),
                    ChangesFormatType.WithMarker
                );
            }

            var sepMatch = ChangesSeparatorLineRegex.Match(rawContent);
            if (sepMatch.Success)
            {
                return (
                    rawContent.Substring(0, sepMatch.Index).Trim(),
                    rawContent.Substring(sepMatch.Index + sepMatch.Length).Trim(),
                    ChangesFormatType.WithMarker
                );
            }

            var mdMatch = MdChangesHeaderRegex.Match(rawContent);
            if (mdMatch.Success)
            {
                return (
                    rawContent.Substring(0, mdMatch.Index).Trim(),
                    rawContent.Substring(mdMatch.Index + mdMatch.Length).Trim(),
                    ChangesFormatType.WithMarker
                );
            }

            var jsonResult = TryIdentifyTrailingJson(rawContent);
            if (jsonResult.HasValue)
            {
                return (
                    rawContent.Substring(0, jsonResult.Value.startIndex).Trim(),
                    jsonResult.Value.json,
                    ChangesFormatType.TrailingJsonOnly
                );
            }

            return (rawContent, null, ChangesFormatType.WithMarker);
        }

        private static (int startIndex, string json)? TryIdentifyTrailingJson(string rawContent)
        {
            var lastBrace = rawContent.LastIndexOf('}');
            if (lastBrace >= 0)
            {
                var braceCount = 0;
                var jsonStartIndex = -1;

                for (var i = lastBrace; i >= 0; i--)
                {
                    var c = rawContent[i];
                    if (c == '}') braceCount++;
                    else if (c == '{')
                    {
                        braceCount--;
                        if (braceCount == 0)
                        {
                            jsonStartIndex = i;
                            break;
                        }
                    }
                }

                if (jsonStartIndex >= 0)
                {
                    var candidateJson = rawContent.Substring(jsonStartIndex, lastBrace - jsonStartIndex + 1);
                    var exactResult = TryValidateTrailingJsonCandidate(rawContent, jsonStartIndex, candidateJson);
                    if (exactResult.HasValue)
                    {
                        return exactResult;
                    }
                }
            }

            for (var jsonStartIndex = rawContent.LastIndexOf('{'); jsonStartIndex >= 0; jsonStartIndex = rawContent.LastIndexOf('{', jsonStartIndex - 1))
            {
                var candidateJson = rawContent.Substring(jsonStartIndex);
                var repairedResult = TryValidateTrailingJsonCandidate(rawContent, jsonStartIndex, candidateJson);
                if (repairedResult.HasValue)
                {
                    return repairedResult;
                }
            }

            return null;
        }

        private static (int startIndex, string json)? TryValidateTrailingJsonCandidate(string rawContent, int jsonStartIndex, string candidateJson)
        {
            var repairedJson = RepairChangesJson(candidateJson);
            var candidates = string.Equals(repairedJson, candidateJson, StringComparison.Ordinal)
                ? new[] { candidateJson }
                : new[] { candidateJson, repairedJson };

            foreach (var json in candidates)
            {
                try
                {
                    using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
                    {
                        CommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    });

                    if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;

                    var matchedFields = 0;
                    foreach (var field in ChangesSignatureFields)
                    {
                        if (doc.RootElement.TryGetProperty(field, out _) ||
                            doc.RootElement.TryGetProperty(ToCamelCase(field), out _))
                        {
                            matchedFields++;
                        }
                    }

                    if (matchedFields < 2) continue;

                    var actualStart = jsonStartIndex;
                    var beforeJson = rawContent.Substring(0, jsonStartIndex).TrimEnd();
                    var codeBlockIdx = beforeJson.LastIndexOf("```", StringComparison.Ordinal);
                    if (codeBlockIdx >= 0)
                    {
                        var between = beforeJson.Substring(codeBlockIdx + 3).Trim();
                        if (string.IsNullOrEmpty(between) || between.Equals("json", StringComparison.OrdinalIgnoreCase))
                        {
                            var lineStart = beforeJson.LastIndexOf('\n', codeBlockIdx);
                            actualStart = lineStart >= 0 ? lineStart : codeBlockIdx;
                        }
                    }

                    return (actualStart, json);
                }
                catch (JsonException)
                {
                }
            }

            return null;
        }

        private static string ToCamelCase(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

        private (ChapterChanges? changes, string? error) ParseChangesContent(string changesPart)
        {
            var jsonStr = ExtractJsonFromChangesSection(changesPart);
            if (!string.IsNullOrEmpty(jsonStr))
            {
                var jsonResult = TryParseJsonChanges(jsonStr);
                if (jsonResult != null)
                {
                    return (jsonResult, null);
                }
            }

            return (null, "CHANGES解析失败：未找到有效 JSON，请检查 JSON 语法（括号、逗号、引号），或确认正文末尾输出了包含顶级字段的 JSON 对象。");
        }

        private ChapterChanges? TryParseJsonChanges(string jsonStr)
        {
            try
            {
                try
                {
                    return JsonSerializer.Deserialize<ChapterChanges>(jsonStr, ChangesParseOptions);
                }
                catch (JsonException)
                {
                    var repaired = RepairChangesJson(jsonStr);
                    if (!string.Equals(repaired, jsonStr, StringComparison.Ordinal))
                    {
                        TM.App.Log("[GG] JSON修复后重试");
                        return JsonSerializer.Deserialize<ChapterChanges>(repaired, ChangesParseOptions);
                    }
                    throw;
                }
            }
            catch (JsonException ex)
            {
                TM.App.Log($"[GG] JSON解析失败: {ex.Message}");
                return null;
            }
        }

        private sealed class CharacterStateChangeConverter : System.Text.Json.Serialization.JsonConverter<CharacterStateChange>
        {
            public override CharacterStateChange? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Null)
                {
                    return null;
                }

                using var doc = JsonDocument.ParseValue(ref reader);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return new CharacterStateChange();
                }

                var model = new CharacterStateChange();

                if (TryGetString(root, "CharacterId", out var characterId) || TryGetString(root, "characterId", out characterId))
                {
                    model.CharacterId = characterId;
                }
                else if (TryGetString(root, "Character", out var character) || TryGetString(root, "character", out character))
                {
                    model.CharacterId = character;
                }

                if (TryGetString(root, "NewLevel", out var newLevel) || TryGetString(root, "newLevel", out newLevel) || TryGetString(root, "level", out newLevel))
                {
                    model.NewLevel = newLevel;
                }

                if (TryGetString(root, "NewMentalState", out var mental) || TryGetString(root, "newMentalState", out mental) || TryGetString(root, "mentalState", out mental))
                {
                    model.NewMentalState = mental;
                }

                if (TryGetString(root, "KeyEvent", out var keyEvent) || TryGetString(root, "keyEvent", out keyEvent))
                {
                    model.KeyEvent = keyEvent;
                }
                else if (TryGetString(root, "change", out var altKeyEvent) || TryGetString(root, "Change", out altKeyEvent))
                {
                    model.KeyEvent = altKeyEvent;
                }

                if (root.TryGetProperty("NewAbilities", out var abilitiesEl) || root.TryGetProperty("newAbilities", out abilitiesEl) || root.TryGetProperty("abilities", out abilitiesEl))
                {
                    if (abilitiesEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in abilitiesEl.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                model.NewAbilities.Add(item.GetString() ?? string.Empty);
                            }
                        }
                    }
                    else if (abilitiesEl.ValueKind == JsonValueKind.String)
                    {
                        var s = abilitiesEl.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                        {
                            model.NewAbilities.Add(s);
                        }
                    }
                }

                if (root.TryGetProperty("LostAbilities", out var lostAbilitiesEl) || root.TryGetProperty("lostAbilities", out lostAbilitiesEl))
                {
                    if (lostAbilitiesEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in lostAbilitiesEl.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                model.LostAbilities.Add(item.GetString() ?? string.Empty);
                            }
                        }
                    }
                    else if (lostAbilitiesEl.ValueKind == JsonValueKind.String)
                    {
                        var s = lostAbilitiesEl.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                        {
                            model.LostAbilities.Add(s);
                        }
                    }
                }

                if (TryGetString(root, "Importance", out var importance) || TryGetString(root, "importance", out importance))
                {
                    model.Importance = importance;
                }

                if (TryGetString(root, "CausedBy", out var causedBy) || TryGetString(root, "causedBy", out causedBy))
                {
                    model.CausedBy = causedBy;
                }

                if (root.TryGetProperty("RelationshipChanges", out var relEl) || root.TryGetProperty("relationshipChanges", out relEl))
                {
                    if (relEl.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in relEl.EnumerateObject())
                        {
                            if (prop.Value.ValueKind == JsonValueKind.Object)
                            {
                                var rc = new RelationshipChange();
                                if (TryGetString(prop.Value, "Relation", out var relation) || TryGetString(prop.Value, "relation", out relation))
                                {
                                    rc.Relation = relation;
                                }
                                if (TryGetInt(prop.Value, "TrustDelta", out var delta) || TryGetInt(prop.Value, "trustDelta", out delta))
                                {
                                    rc.TrustDelta = delta;
                                }
                                model.RelationshipChanges[prop.Name] = rc;
                            }
                        }
                    }
                }

                return model;
            }

            public override void Write(Utf8JsonWriter writer, CharacterStateChange value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                writer.WriteString("CharacterId", value.CharacterId);
                writer.WriteString("NewLevel", value.NewLevel);
                writer.WritePropertyName("NewAbilities");
                JsonSerializer.Serialize(writer, value.NewAbilities, options);
                writer.WritePropertyName("LostAbilities");
                JsonSerializer.Serialize(writer, value.LostAbilities, options);
                writer.WritePropertyName("RelationshipChanges");
                JsonSerializer.Serialize(writer, value.RelationshipChanges, options);
                writer.WriteString("NewMentalState", value.NewMentalState);
                writer.WriteString("KeyEvent", value.KeyEvent);
                writer.WriteString("Importance", value.Importance ?? "normal");
                if (!string.IsNullOrEmpty(value.CausedBy))
                    writer.WriteString("CausedBy", value.CausedBy);
                writer.WriteEndObject();
            }

            private static bool TryGetString(JsonElement obj, string name, out string value)
            {
                if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
                {
                    value = el.GetString() ?? string.Empty;
                    return true;
                }
                value = string.Empty;
                return false;
            }

            private static bool TryGetInt(JsonElement obj, string name, out int value)
            {
                if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var el))
                {
                    if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out value))
                    {
                        return true;
                    }
                    if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out value))
                    {
                        return true;
                    }
                }
                value = 0;
                return false;
            }
        }

        #endregion
    }
}
