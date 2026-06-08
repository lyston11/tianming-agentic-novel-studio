using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Contexts;
using TM.Services.Modules.ProjectData.Models.Validate.ValidationSummary;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class UnifiedValidationService : IUnifiedValidationService
    {
        #region 校验逻辑

        private async Task ExecuteValidationsAsync(ChapterValidationResult result, ValidationContext context, string chapterContent, CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                result.IssuesByModule.TryGetValue(StructuralModuleName, out var knownStructuralIssues);
                var prompt = await BuildValidationPromptAsync(result, context, chapterContent, knownStructuralIssues).ConfigureAwait(false);
                var aiResult = await _aiService.GenerateAsync(prompt, ct).ConfigureAwait(false);

                if (aiResult.Success && !string.IsNullOrEmpty(aiResult.Content))
                {
                    ParseAIValidationResult(result, aiResult.Content);
                    TM.App.Log($"[UnifiedValidationService] AI校验完成: {result.ChapterId}");
                }
                else
                {
                    TM.App.Log($"[UnifiedValidationService] AI校验失败: {aiResult.ErrorMessage}");
                    result.IssuesByModule[SystemModuleName] = new List<ValidationIssue>
                    {
                        new ValidationIssue
                        {
                            Type = "AIValidationFailed",
                            Severity = "Warning",
                            Message = "AI校验失败，未执行校验。"
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UnifiedValidationService] AI校验异常: {ex.Message}");
                result.IssuesByModule[SystemModuleName] = new List<ValidationIssue>
                {
                    new ValidationIssue
                    {
                        Type = "AIValidationException",
                        Severity = "Warning",
                        Message = $"AI校验异常：{ex.Message}，未执行校验。"
                    }
                };
            }
        }

        private async Task<string> BuildValidationPromptAsync(
            ChapterValidationResult result,
            ValidationContext context,
            string chapterContent,
            List<ValidationIssue>? knownStructuralIssues = null)
        {
            var sb = new StringBuilder();

            var templatePrompt = GetValidationTemplateSystemPrompt();
            if (!string.IsNullOrWhiteSpace(templatePrompt))
            {
                sb.AppendLine("<validation_system_prompt>");
                sb.AppendLine(templatePrompt);
                sb.AppendLine("</validation_system_prompt>");
                sb.AppendLine();
            }

            sb.AppendLine("<validation_task>");
            sb.AppendLine();
            sb.AppendLine("<chapter_info>");
            sb.AppendLine($"- 章节ID: {result.ChapterId}");
            sb.AppendLine($"- 章节标题: {result.ChapterTitle}");
            sb.AppendLine($"- 卷号: {result.VolumeNumber}");
            sb.AppendLine($"- 章节号: {result.ChapterNumber}");
            sb.AppendLine($"- 卷名: {result.VolumeName}");
            sb.AppendLine("</chapter_info>");
            sb.AppendLine();

            var contentGuide = await _guideContextService.GetContentGuideAsync().ConfigureAwait(false);
            contentGuide.Chapters.TryGetValue(result.ChapterId, out var guideEntry);
            var contextIds = guideEntry?.ContextIds;

            var templatesTask = contextIds?.TemplateIds?.Count > 0
                ? _guideContextService.ExtractTemplatesAsync(contextIds.TemplateIds)
                : Task.FromResult(new List<Models.Design.Templates.CreativeMaterialData>());
            var worldRulesTask = contextIds?.WorldRuleIds?.Count > 0
                ? _guideContextService.ExtractWorldRulesAsync(contextIds.WorldRuleIds)
                : Task.FromResult(new List<Models.Design.Worldview.WorldRulesData>());
            var charactersTask = contextIds?.Characters?.Count > 0
                ? _guideContextService.ExtractCharactersAsync(contextIds.Characters)
                : Task.FromResult(new List<Models.Design.Characters.CharacterRulesData>());
            var locationsTask = contextIds?.Locations?.Count > 0
                ? _guideContextService.ExtractLocationsAsync(contextIds.Locations)
                : Task.FromResult(new List<Models.Design.Location.LocationRulesData>());
            var plotRulesTask = contextIds?.PlotRules?.Count > 0
                ? _guideContextService.ExtractPlotRulesAsync(contextIds.PlotRules)
                : Task.FromResult(new List<Models.Design.Plot.PlotRulesData>());

            await Task.WhenAll(templatesTask, worldRulesTask, charactersTask, locationsTask, plotRulesTask).ConfigureAwait(false);

            var templateItems = (await templatesTask.ConfigureAwait(false))
                .Take(3)
                .Select(t => $"{t.Name}: 类型={t.Genre}, 构思={TruncateString(t.OverallIdea, 60)}, 世界观构建={TruncateString(t.WorldBuildingMethod, 40)}, 主角塑造={TruncateString(t.ProtagonistDesign, 40)}")
                .ToList();
            AppendSection(sb, "创作模板（文风约束）", templateItems);

            var worldItems = (await worldRulesTask.ConfigureAwait(false))
                .Take(5)
                .Select(w => $"{w.Name}: 硬规则={TruncateString(w.HardRules, 60)}, 力量体系={TruncateString(w.PowerSystem, 40)}")
                .ToList();
            AppendSection(sb, "世界观规则", worldItems);

            var chapterCharacters = await charactersTask.ConfigureAwait(false);
            var characterItems = new List<string>();
            if (chapterCharacters.Count > 0)
            {
                characterItems = chapterCharacters
                    .Take(10)
                    .Select(c => $"{c.Name}: 身份={c.Identity}, 种族={c.Race}, 核心缺陷={TruncateString(c.FlawBelief, 30)}, 外在目标={TruncateString(c.Want, 30)}, 成长路径={TruncateString(c.GrowthPath, 30)}")
                    .ToList();
                AppendSection(sb, "角色设定（本章相关）", characterItems);
            }

            var factionItems = new List<string>();
            if (contextIds?.Factions?.Count > 0)
            {
                var factions = await _guideContextService.ExtractFactionsAsync(contextIds.Factions).ConfigureAwait(false);
                var characterIdToName = chapterCharacters
                    .Where(c => !string.IsNullOrWhiteSpace(c.Id) && !string.IsNullOrWhiteSpace(c.Name))
                    .ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);

                factionItems = factions
                    .Take(8)
                    .Select(f => $"{f.Name}: 类型={f.FactionType}, 目标={TruncateString(f.Goal, 40)}, 领袖={(string.IsNullOrWhiteSpace(f.Leader) ? string.Empty : (characterIdToName.TryGetValue(f.Leader, out var n) ? n : f.Leader))}")
                    .ToList();
                AppendSection(sb, "势力设定（本章相关）", factionItems);
            }

            var locationItems = (await locationsTask.ConfigureAwait(false))
                .Take(8)
                .Select(l => $"{l.Name}: 类型={l.LocationType}, 描述={TruncateString(l.Description, 40)}, 地形={TruncateString(l.Terrain, 30)}")
                .ToList();
            AppendSection(sb, "地点设定（本章相关）", locationItems);

            var plotItems = (await plotRulesTask.ConfigureAwait(false))
                .Take(8)
                .Select(p => $"{p.Name}: 阶段={p.StoryPhase}, 目标={TruncateString(p.Goal, 40)}, 冲突={TruncateString(p.Conflict, 40)}, 结果={TruncateString(p.Result, 40)}")
                .ToList();
            AppendSection(sb, "剧情规则（本章相关）", plotItems);

            var outline = ResolveOutline(context, contextIds?.VolumeOutline);
            if (outline != null)
            {
                AppendSection(sb, "全书大纲", new[]
                {
                    $"一句话大纲={TruncateString(outline.OneLineOutline, 80)}",
                    $"核心冲突={TruncateString(outline.CoreConflict, 60)}",
                    $"主题={TruncateString(outline.Theme, 60)}",
                    $"结局状态={TruncateString(outline.EndingState, 60)}"
                });
            }

            var chapterPlanLines = BuildChapterPlanLines(context, contextIds).ToList();
            AppendSection(sb, "章节规划", chapterPlanLines);

            var blueprintItems = ResolveBlueprintItems(context, contextIds?.BlueprintIds).ToList();
            AppendSection(sb, "章节蓝图", blueprintItems);

            var volumeDesign = ResolveVolumeDesign(context, contextIds?.VolumeDesignId);
            if (volumeDesign != null)
            {
                AppendSection(sb, "分卷设计", new[]
                {
                    $"卷标题={volumeDesign.VolumeTitle}",
                    $"卷主题={TruncateString(volumeDesign.VolumeTheme, 60)}",
                    $"阶段目标={TruncateString(volumeDesign.StageGoal, 60)}",
                    $"主冲突={TruncateString(volumeDesign.MainConflict, 60)}",
                    $"关键事件={TruncateString(volumeDesign.KeyEvents, 60)}"
                });
            }

            var missingRules = new List<string>();
            if (templateItems.Count == 0) missingRules.Add("StyleConsistency");
            if (worldItems.Count == 0) missingRules.Add("WorldviewConsistency");
            if (characterItems.Count == 0) missingRules.Add("CharacterConsistency");
            if (factionItems.Count == 0) missingRules.Add("FactionConsistency");
            if (locationItems.Count == 0) missingRules.Add("LocationConsistency");
            if (plotItems.Count == 0) missingRules.Add("PlotConsistency");
            if (outline == null) missingRules.Add("OutlineConsistency");
            if (chapterPlanLines.Count == 0) missingRules.Add("ChapterPlanConsistency");
            if (blueprintItems.Count == 0) missingRules.Add("BlueprintConsistency");
            if (volumeDesign == null) missingRules.Add("VolumeDesignConsistency");

            if (missingRules.Count > 0)
            {
                sb.AppendLine("<缺失数据说明>");
                sb.AppendLine("以下规则缺少对应数据，请将 result 填写为\"未校验\"（系统按警告处理），problemItems 可为空：");
                foreach (var rule in missingRules.Distinct())
                {
                    sb.AppendLine($"- {rule}（{ValidationRules.GetDisplayName(rule)}）");
                }
                sb.AppendLine("</缺失数据说明>");
                sb.AppendLine();
            }

            sb.AppendLine("<正文内容>");
            sb.AppendLine(chapterContent.Length > ChapterPreviewLength
                ? chapterContent.Substring(0, ChapterPreviewLength) + "..."
                : chapterContent);
            sb.AppendLine("</正文内容>");
            sb.AppendLine();

            if (knownStructuralIssues != null && knownStructuralIssues.Count > 0)
            {
                sb.AppendLine("<已确认结构性问题>");
                foreach (var issue in knownStructuralIssues)
                    sb.AppendLine($"- [{issue.Type}] {issue.Message}");
                sb.AppendLine("</已确认结构性问题>");
                sb.AppendLine("以上结构性问题已由规则层确认。你的任务是专注于设计数据的语义一致性（10条规则），不要重复检查上述已确认问题。");
                sb.AppendLine();
            }

            sb.AppendLine("<校验要求>");
            sb.AppendLine($"请对章节执行{ValidationRules.TotalRuleCount}条校验规则，返回JSON格式的校验结果。");
            sb.AppendLine($"1. moduleResults必须输出完整规则清单（{ValidationRules.TotalRuleCount}项），缺失项视为协议错误");
            sb.AppendLine("2. extendedData为每个规则的差异字段容器，内容允许为空但不允许缺字段名");
            sb.AppendLine("3. 当result为警告/失败/未校验时，problemItems至少1条（未校验可说明原因）");
            sb.AppendLine("4. 当result为通过时，problemItems允许为空数组");
            sb.AppendLine("5. 重要：summary、reason、suggestion 字段中不得引用提示词中的标签名称（如 正文内容、缺失数据说明、已确认结构性问题、校验要求 等），只描述内容本身。");
            sb.AppendLine();
            sb.AppendLine("返回JSON格式：");
            sb.AppendLine("```json");
            sb.AppendLine(BuildJsonTemplateForPrompt());
            sb.AppendLine("```");
            sb.AppendLine("</校验要求>");
            sb.AppendLine();
            sb.AppendLine("<validation_rules_description>");
            sb.AppendLine(BuildRulesDescription());
            sb.AppendLine("</validation_rules_description>");
            sb.AppendLine();
            sb.AppendLine("</validation_task>");

            return sb.ToString();
        }

        private string BuildJsonTemplateForPrompt()
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"overallResult\": \"通过|警告|失败|未校验\",");
            sb.AppendLine("  \"moduleResults\": [");

            for (int i = 0; i < ValidationRules.AllModuleNames.Length; i++)
            {
                var moduleName = ValidationRules.AllModuleNames[i];
                var displayName = ValidationRules.GetDisplayName(moduleName);
                var verificationType = GetVerificationType(moduleName);
                var fields = ValidationRules.GetExtendedDataSchema(moduleName);

                sb.AppendLine("    {");
                sb.AppendLine($"      \"moduleName\": \"{moduleName}\",");
                sb.AppendLine($"      \"displayName\": \"{displayName}\",");
                sb.AppendLine($"      \"verificationType\": \"{verificationType}\",");
                sb.AppendLine("      \"result\": \"通过|警告|失败|未校验\",");
                sb.AppendLine("      \"issueDescription\": \"问题描述（可空）\",");
                sb.AppendLine("      \"fixSuggestion\": \"修复建议（可空）\",");
                sb.AppendLine("      \"extendedData\": {");

                for (int f = 0; f < fields.Length; f++)
                {
                    var field = fields[f];
                    var camel = char.ToLowerInvariant(field[0]) + field.Substring(1);
                    var suffix = f == fields.Length - 1 ? string.Empty : ",";
                    sb.AppendLine($"        \"{camel}\": \"\"{suffix}");
                }

                sb.AppendLine("      },");
                sb.AppendLine("      \"problemItems\": [");
                sb.AppendLine("        {");
                sb.AppendLine("          \"summary\": \"问题简述\",");
                sb.AppendLine("          \"reason\": \"原因依据\",");
                sb.AppendLine("          \"details\": \"补充详情（可选）\",");
                sb.AppendLine("          \"suggestion\": \"修复建议（可选）\"");
                sb.AppendLine("        }");
                sb.AppendLine("      ]");
                sb.Append("    }");
                sb.AppendLine(i == ValidationRules.AllModuleNames.Length - 1 ? string.Empty : ",");
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private string BuildRulesDescription()
        {
            var sb = new StringBuilder();
            sb.AppendLine("1. StyleConsistency（文风模板一致性）：对齐创作模板文风/类型/构思");
            sb.AppendLine("   - extendedData: templateName, genre, overallIdea, styleHint");
            sb.AppendLine("2. WorldviewConsistency（世界观一致性）：对齐硬规则/力量体系/特殊法则");
            sb.AppendLine("   - extendedData: worldRuleName, hardRules, powerSystem, specialLaws");
            sb.AppendLine("3. CharacterConsistency（角色设定一致性）：对齐身份/特质/弧光目标");
            sb.AppendLine("   - extendedData: characterName, identity, coreTraits, arcGoal");
            sb.AppendLine("4. FactionConsistency（势力设定一致性）：对齐势力类型/目标/领袖");
            sb.AppendLine("   - extendedData: factionName, factionType, goal, leader");
            sb.AppendLine("5. LocationConsistency（地点设定一致性）：对齐地点类型/描述/地形");
            sb.AppendLine("   - extendedData: locationName, locationType, description, terrain");
            sb.AppendLine("6. PlotConsistency（剧情规则一致性）：对齐剧情阶段/目标/冲突/结果");
            sb.AppendLine("   - extendedData: plotName, storyPhase, goal, conflict, result");
            sb.AppendLine("7. OutlineConsistency（大纲一致性）：对齐一句话大纲/核心冲突/主题/结局");
            sb.AppendLine("   - extendedData: oneLineOutline, coreConflict, theme, endingState");
            sb.AppendLine("8. ChapterPlanConsistency（章节规划一致性）：对齐本章目标/转折/伏笔");
            sb.AppendLine("   - extendedData: chapterTitle, mainGoal, keyTurn, hook, foreshadowing");
            sb.AppendLine("9. BlueprintConsistency（章节蓝图一致性）：对齐结构/节奏/角色地点清单");
            sb.AppendLine("   - extendedData: chapterId, oneLineStructure, pacingCurve, cast, locations");
            sb.AppendLine("10. VolumeDesignConsistency（分卷设计一致性）：对齐卷主题/阶段目标/主冲突/关键事件");
            sb.AppendLine("   - extendedData: volumeTitle, volumeTheme, stageGoal, mainConflict, keyEvents");

            return sb.ToString();
        }

        private void ParseAIValidationResult(ChapterValidationResult result, string aiContent)
        {
            try
            {
                var jsonStart = aiContent.IndexOf('{');
                var jsonEnd = aiContent.LastIndexOf('}');
                if (jsonStart < 0 || jsonEnd < 0 || jsonEnd <= jsonStart)
                {
                    TM.App.Log("[UnifiedValidationService] AI返回内容中未找到有效JSON");
                    AddProtocolErrorIssue(result, "AI返回内容中未找到有效JSON");
                    return;
                }

                var jsonStr = aiContent.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var doc = JsonDocument.Parse(jsonStr);

                if (!doc.RootElement.TryGetProperty("moduleResults", out var moduleResultsArray))
                {
                    TM.App.Log("[UnifiedValidationService] AI返回JSON中未找到moduleResults字段");
                    AddProtocolErrorIssue(result, "AI返回JSON中未找到moduleResults字段");
                    return;
                }

                var moduleCount = moduleResultsArray.GetArrayLength();
                if (moduleCount != ValidationRules.TotalRuleCount)
                {
                    TM.App.Log($"[UnifiedValidationService] AI协议错误：moduleResults应为{ValidationRules.TotalRuleCount}项，实际为{moduleCount}项");
                    AddProtocolErrorIssue(result, $"moduleResults应为{ValidationRules.TotalRuleCount}项，实际为{moduleCount}项");
                }

                ParseNewProtocolResult(result, moduleResultsArray);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UnifiedValidationService] 解析AI校验结果失败: {ex.Message}");
                AddProtocolErrorIssue(result, $"解析AI校验结果失败: {ex.Message}");
            }
        }

        private void AddProtocolErrorIssue(ChapterValidationResult result, string message)
        {
            if (!result.IssuesByModule.ContainsKey(SystemModuleName))
            {
                result.IssuesByModule[SystemModuleName] = new List<ValidationIssue>();
            }

            result.IssuesByModule[SystemModuleName].Add(new ValidationIssue
            {
                Type = "ProtocolError",
                Severity = "Warning",
                Message = $"AI协议错误：{message}"
            });
        }

        private void ParseNewProtocolResult(ChapterValidationResult result, JsonElement moduleResultsArray)
        {
            var parsedModuleNames = new HashSet<string>();

            foreach (var moduleElement in moduleResultsArray.EnumerateArray())
            {
                var moduleName = moduleElement.TryGetProperty("moduleName", out var mn)
                    ? mn.GetString() ?? "Unknown"
                    : "Unknown";

                if (!ValidationRules.AllModuleNames.Contains(moduleName))
                {
                    TM.App.Log($"[UnifiedValidationService] AI协议错误：未知的moduleName: {moduleName}");
                    AddProtocolErrorIssue(result, $"未知的moduleName: {moduleName}");
                    continue;
                }

                parsedModuleNames.Add(moduleName);

                var moduleResult = moduleElement.TryGetProperty("result", out var r)
                    ? r.GetString() ?? "未校验"
                    : "未校验";

                if (moduleResult != "通过")
                {
                    var issues = new List<ValidationIssue>();
                    var severity = moduleResult == "失败" ? "Error" : "Warning";

                    if (moduleElement.TryGetProperty("problemItems", out var problemItems))
                    {
                        foreach (var item in problemItems.EnumerateArray())
                        {
                            var issue = new ValidationIssue
                            {
                                Type = item.TryGetProperty("reason", out var reason)
                                    ? reason.GetString() ?? ""
                                    : "",
                                Severity = severity,
                                Message = item.TryGetProperty("summary", out var summary)
                                    ? summary.GetString() ?? ""
                                    : "",
                                Suggestion = item.TryGetProperty("suggestion", out var sug)
                                    ? sug.GetString() ?? ""
                                    : "",
                                EntityName = ""
                            };
                            issues.Add(issue);
                        }
                    }

                    if (issues.Count == 0)
                    {
                        var issueDesc = moduleElement.TryGetProperty("issueDescription", out var desc)
                            ? desc.GetString() ?? ""
                            : "";
                        var fixSug = moduleElement.TryGetProperty("fixSuggestion", out var fix)
                            ? fix.GetString() ?? ""
                            : "";

                        var defaultMessage = moduleResult == "未校验"
                            ? $"规则未校验：{ValidationRules.GetDisplayName(moduleName)}"
                            : !string.IsNullOrEmpty(issueDesc) ? issueDesc : $"{moduleName}校验{moduleResult}";

                        issues.Add(new ValidationIssue
                        {
                            Type = moduleResult == "未校验" ? "UnvalidatedRule" : "ValidationIssue",
                            Severity = severity,
                            Message = defaultMessage,
                            Suggestion = string.IsNullOrWhiteSpace(fixSug)
                                ? (moduleResult == "未校验" ? "补齐对应数据后再执行校验" : string.Empty)
                                : fixSug
                        });
                    }

                    result.IssuesByModule[moduleName] = issues;
                }
            }

            var missingModules = ValidationRules.AllModuleNames.Except(parsedModuleNames).ToList();
            if (missingModules.Count > 0)
            {
                TM.App.Log($"[UnifiedValidationService] AI协议错误：缺失模块: {string.Join(", ", missingModules)}");
                AddProtocolErrorIssue(result, $"缺失模块: {string.Join(", ", missingModules)}");
            }
        }

        #endregion
    }
}
