using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TM.Framework.UI.Workspace.Services.Spec;
using TM.Services.Modules.ProjectData.Models.TaskContexts;

namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    public partial class LayeredPromptBuilder
    {
        #region 私有方法 - 本章任务区块

        private void AppendTaskSection(StringBuilder sb, ContentTaskContext ctx, CreativeSpec? spec)
        {
            sb.AppendLine("<task_context>");
            sb.AppendLine("> 请将以下信息视为创作输入，目标是写出连贯自然的小说章节正文。");
            sb.AppendLine();

            if (spec != null)
            {
                var specPrompt = spec.BuildPromptFragment();
                if (!string.IsNullOrWhiteSpace(specPrompt))
                {
                    sb.AppendLine("<section name=\"creative_spec\" role=\"style_constraint\" priority=\"highest\">");
                    sb.AppendLine(specPrompt);
                    sb.AppendLine("</section>");
                    sb.AppendLine();
                }
            }

            static string ResolveId(string? id, Dictionary<string, string> map)
            {
                if (string.IsNullOrWhiteSpace(id)) return string.Empty;
                return map.TryGetValue(id, out var n) ? n : id;
            }

            static string ResolveIds(string? ids, Dictionary<string, string> map)
            {
                if (string.IsNullOrWhiteSpace(ids)) return string.Empty;
                return string.Join("、", ids.Split(new[] { ',', '，', '、', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => ResolveId(s, map)).Where(s => !string.IsNullOrWhiteSpace(s)));
            }

            var characterIdToName = ctx.Characters
                .Where(c => !string.IsNullOrWhiteSpace(c.Id) && !string.IsNullOrWhiteSpace(c.Name))
                .GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
            var factionIdToName = ctx.Factions
                .Where(f => !string.IsNullOrWhiteSpace(f.Id) && !string.IsNullOrWhiteSpace(f.Name))
                .GroupBy(f => f.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
            var locationIdToName = ctx.Locations
                .Where(l => !string.IsNullOrWhiteSpace(l.Id) && !string.IsNullOrWhiteSpace(l.Name))
                .GroupBy(l => l.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

            sb.AppendLine("<section name=\"chapter_task\" role=\"primary_directive\" priority=\"highest\">");
            sb.AppendLine($"- 章节：{ctx.ChapterId}");
            sb.AppendLine($"- 标题：{ctx.Title}");
            if (!string.IsNullOrEmpty(ctx.Summary))
                sb.AppendLine($"- 概要：{ctx.Summary}");
            if (ctx.Rhythm != null)
                sb.AppendLine($"- 节奏：{ctx.Rhythm.PaceType} / {ctx.Rhythm.Intensity} / {ctx.Rhythm.EmotionalTone}");

            var (mandatoryChars, mandatoryFactions, mandatoryLocs) = BuildMandatoryEntities(ctx);
            if (mandatoryChars.Count > 0 || mandatoryFactions.Count > 0 || mandatoryLocs.Count > 0)
            {
                sb.AppendLine("- \u26a0 **本章必须在正文中出场的实体（不可省略，须有实质戏份：对话/行动/情节参与，不能仅作背景一笔带过）**：");
                if (mandatoryChars?.Count > 0)
                    sb.AppendLine($"  - 角色：{string.Join("、", mandatoryChars)}");
                if (mandatoryFactions?.Count > 0)
                    sb.AppendLine($"  - 势力：{string.Join("、", mandatoryFactions)}");
                if (mandatoryLocs?.Count > 0)
                    sb.AppendLine($"  - 地点：{string.Join("、", mandatoryLocs)}");
            }

            bool hasTaskLayer = !string.IsNullOrEmpty(ctx.Title)
                               || !string.IsNullOrEmpty(ctx.Summary)
                               || (ctx.Characters != null && ctx.Characters.Count > 0)
                               || (ctx.WorldRules != null && ctx.WorldRules.Count > 0)
                               || (ctx.Templates != null && ctx.Templates.Count > 0)
                               || ctx.VolumeOutline != null
                               || ctx.VolumeDesign != null
                               || ctx.ChapterPlan != null
                               || (ctx.Blueprints != null && ctx.Blueprints.Count > 0)
                               || (ctx.Scenes != null && ctx.Scenes.Count > 0);
            if (!hasTaskLayer)
            {
                sb.AppendLine("> 当前为纯续写模式：请根据上一章结尾直接续写正文，保持人物口吻与叙事风格一致。");
            }
            sb.AppendLine("</section>");
            sb.AppendLine();

            if (ctx.ChapterPlan != null)
            {
                sb.AppendLine("<section name=\"chapter_plan\" role=\"execution_guide\" priority=\"high\">");
                if (ctx.ChapterPlan.ChapterNumber > 0)
                    sb.AppendLine($"- 章节序号：{ctx.ChapterPlan.ChapterNumber}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.ChapterTitle))
                    sb.AppendLine($"- 章节标题：{TruncateText(ctx.ChapterPlan.ChapterTitle, 80)}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.Volume))
                    sb.AppendLine($"- 所属卷：{TruncateText(ctx.ChapterPlan.Volume, 60)}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.ChapterTheme))
                    sb.AppendLine($"- 章节主题：{TruncateText(ctx.ChapterPlan.ChapterTheme, 120)}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.ReaderExperienceGoal))
                    sb.AppendLine($"- 读者体验目标：{TruncateText(ctx.ChapterPlan.ReaderExperienceGoal, 120)}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.MainGoal))
                    sb.AppendLine($"- 主目标：{TruncateText(ctx.ChapterPlan.MainGoal, 120)}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.ResistanceSource))
                    sb.AppendLine($"- 阻力来源：{TruncateText(ctx.ChapterPlan.ResistanceSource, 120)}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.KeyTurn))
                    sb.AppendLine($"- 关键转折：{TruncateText(ctx.ChapterPlan.KeyTurn, 150)}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.Hook))
                    sb.AppendLine($"- 结尾钩子：{TruncateText(ctx.ChapterPlan.Hook, 150)}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.WorldInfoDrop))
                    sb.AppendLine($"- 世界观信息投放：{TruncateText(ctx.ChapterPlan.WorldInfoDrop, 150)}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.CharacterArcProgress))
                    sb.AppendLine($"- 角色弧光推进：{TruncateText(ctx.ChapterPlan.CharacterArcProgress, 150)}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.MainPlotProgress))
                    sb.AppendLine($"- 主线推进点：{TruncateText(ctx.ChapterPlan.MainPlotProgress, 150)}");
                if (!string.IsNullOrWhiteSpace(ctx.ChapterPlan.Foreshadowing))
                    sb.AppendLine($"- 伏笔埋设/回收：{TruncateText(ctx.ChapterPlan.Foreshadowing, 150)}");
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.Blueprints != null && ctx.Blueprints.Count > 0)
            {
                const int blueprintsCap = 20;
                var blueprints = ctx.Blueprints.Count > blueprintsCap ? ctx.Blueprints.Take(blueprintsCap).ToList() : ctx.Blueprints;
                var bpCountSuffix = ctx.Blueprints.Count > blueprintsCap ? $"/{ctx.Blueprints.Count}" : "";
                sb.AppendLine($"<section name=\"blueprints\" role=\"scene_directive\" priority=\"highest\" count=\"{blueprints.Count}{bpCountSuffix}\">");
                foreach (var blueprint in blueprints)
                {
                    var title = !string.IsNullOrWhiteSpace(blueprint.SceneTitle)
                        ? blueprint.SceneTitle
                        : blueprint.Name;
                    sb.AppendLine($"- **{TruncateText(title ?? string.Empty, 80)}**");
                    if (blueprint.SceneNumber > 0)
                        sb.AppendLine($"  - 场景序号：{blueprint.SceneNumber}");
                    if (!string.IsNullOrWhiteSpace(blueprint.OneLineStructure))
                        sb.AppendLine($"  - 结构：{TruncateText(blueprint.OneLineStructure, 150)}");
                    if (!string.IsNullOrWhiteSpace(blueprint.PacingCurve))
                        sb.AppendLine($"  - 节奏曲线：{TruncateText(blueprint.PacingCurve, 80)}");
                    if (!string.IsNullOrWhiteSpace(blueprint.PovCharacter))
                        sb.AppendLine($"  - 视角角色：{TruncateText(blueprint.PovCharacter, 60)}");
                    if (!string.IsNullOrWhiteSpace(blueprint.Opening))
                        sb.AppendLine($"  - 起：{TruncateText(blueprint.Opening, 200)}");
                    if (!string.IsNullOrWhiteSpace(blueprint.Development))
                        sb.AppendLine($"  - 承：{TruncateText(blueprint.Development, 200)}");
                    if (!string.IsNullOrWhiteSpace(blueprint.Turning))
                        sb.AppendLine($"  - 转：{TruncateText(blueprint.Turning, 200)}");
                    if (!string.IsNullOrWhiteSpace(blueprint.Ending))
                        sb.AppendLine($"  - 合：{TruncateText(blueprint.Ending, 200)}");
                    if (!string.IsNullOrWhiteSpace(blueprint.InfoDrop))
                        sb.AppendLine($"  - 信息投放：{TruncateText(blueprint.InfoDrop, 150)}");
                    if (!string.IsNullOrWhiteSpace(blueprint.Cast))
                        sb.AppendLine($"  - 角色：{TruncateText(blueprint.Cast, 150)}");
                    if (!string.IsNullOrWhiteSpace(blueprint.Locations))
                        sb.AppendLine($"  - 地点：{TruncateText(blueprint.Locations, 150)}");
                    if (!string.IsNullOrWhiteSpace(blueprint.Factions))
                        sb.AppendLine($"  - 势力：{TruncateText(blueprint.Factions, 150)}");
                    if (!string.IsNullOrWhiteSpace(blueprint.ItemsClues))
                        sb.AppendLine($"  - 道具/线索：{TruncateText(blueprint.ItemsClues, 150)}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            sb.AppendLine("> 以下为背景参考资料（历史记录、设定百科等），写作核心指令以上方 chapter_task / chapter_plan / blueprints 为准。");
            sb.AppendLine();

            if (ctx.HistoricalMilestones != null && ctx.HistoricalMilestones.Count > 0)
            {
                sb.AppendLine("<section name=\"historical_milestones\" role=\"background_reference\" priority=\"normal\">");
                sb.AppendLine("> 以下是各前卷的浓缩历史，请确保本章内容与这些已发生事件保持一致。");
                sb.AppendLine();
                foreach (var milestone in ctx.HistoricalMilestones)
                {
                    sb.AppendLine(milestone.Milestone);
                    sb.AppendLine();
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.CompressedKeyEvents != null && ctx.CompressedKeyEvents.Count > 0)
            {
                List<ChapterKeyEventEntry> toRender;
                string sectionDesc;

                if (ctx.HistoricalMilestones == null || ctx.HistoricalMilestones.Count == 0)
                {
                    toRender = ctx.CompressedKeyEvents;
                    sectionDesc = "> 以下是各前卷的关键事件索引（重要角色变化/重大转折/伏笔），请确保本章与已发生事件保持一致。";
                }
                else
                {
                    var coveredVols = new HashSet<int>(ctx.HistoricalMilestones.Select(m => m.VolumeNumber));
                    toRender = ctx.CompressedKeyEvents
                        .Where(ke => !coveredVols.Contains(ke.VolumeNumber))
                        .ToList();
                    sectionDesc = "> 以下是里程碑未覆盖卷的关键事件补充（重要角色变化/重大转折/伏笔），与上方里程碑配合使用。";
                }

                if (toRender.Count > 0)
                {
                    sb.AppendLine("<section name=\"key_event_index\" role=\"background_reference\" priority=\"normal\">");
                    sb.AppendLine(sectionDesc);
                    sb.AppendLine();
                    foreach (var ke in toRender)
                    {
                        var line = ke.ToCompactLine();
                        if (!string.IsNullOrEmpty(line))
                            sb.AppendLine(line);
                    }
                    sb.AppendLine("</section>");
                    sb.AppendLine();
                }
            }

            if (ctx.PreviousVolumeArchives != null && ctx.PreviousVolumeArchives.Count > 0)
            {
                sb.AppendLine("<section name=\"prev_volume_archive\" role=\"cross_volume_baseline\" priority=\"normal\">");
                sb.AppendLine("> 以下是各前卷末尾的结构化历史基线（快照时间：前卷结束时）。");
                sb.AppendLine("> ⚠ 当前卷内的最新状态以上方 `<fact_ledger>` 为准；两者冲突时 `<fact_ledger>` 优先。");
                sb.AppendLine();
                foreach (var archive in ctx.PreviousVolumeArchives)
                {
                    sb.AppendLine($"**第{archive.VolumeNumber}卷末**（{archive.LastChapterId}）");
                    foreach (var cs in archive.CharacterStates)
                    {
                        var parts = new System.Collections.Generic.List<string>();
                        if (!string.IsNullOrWhiteSpace(cs.Stage)) parts.Add($"修为·{cs.Stage}");
                        if (!string.IsNullOrWhiteSpace(cs.Abilities)) parts.Add($"能力·{cs.Abilities}");
                        if (!string.IsNullOrWhiteSpace(cs.Relationships)) parts.Add($"关系·{cs.Relationships}");
                        sb.AppendLine($"  {cs.Name}：{string.Join("；", parts)}");
                    }
                    foreach (var cf in archive.ConflictProgress)
                    {
                        if (!string.IsNullOrWhiteSpace(cf.Status))
                            sb.AppendLine($"  冲突[{cf.Name}]：{cf.Status}");
                    }

                    if (archive.Timeline != null && archive.Timeline.Count > 0)
                    {
                        foreach (var t in archive.Timeline)
                        {
                            if (string.IsNullOrWhiteSpace(t.TimePeriod)) continue;
                            var elapsed = string.IsNullOrWhiteSpace(t.ElapsedTime) ? string.Empty : $"（经过{t.ElapsedTime}）";
                            var timeEvent = string.IsNullOrWhiteSpace(t.KeyTimeEvent) ? string.Empty : $"，要点={t.KeyTimeEvent}";
                            sb.AppendLine($"  时间[{t.ChapterId}]：{TruncateLine(t.TimePeriod, 120)}{TruncateLine(elapsed, 80)}{TruncateLine(timeEvent, 120)}");
                        }
                    }

                    if (archive.CharacterLocations != null && archive.CharacterLocations.Count > 0)
                    {
                        foreach (var loc in archive.CharacterLocations)
                        {
                            if (string.IsNullOrWhiteSpace(loc.CurrentLocation)) continue;
                            var name = string.IsNullOrWhiteSpace(loc.CharacterName) ? loc.CharacterId : loc.CharacterName;
                            sb.AppendLine($"  位置[{name}]：{TruncateLine(loc.CurrentLocation, 120)}");
                        }
                    }

                    if (archive.FactionStates != null && archive.FactionStates.Count > 0)
                    {
                        foreach (var fac in archive.FactionStates)
                        {
                            if (string.IsNullOrWhiteSpace(fac.Status)) continue;
                            sb.AppendLine($"  势力[{fac.Name}]：{TruncateLine(fac.Status, 120)}");
                        }
                    }

                    if (archive.LocationStates != null && archive.LocationStates.Count > 0)
                    {
                        foreach (var locState in archive.LocationStates)
                        {
                            if (string.IsNullOrWhiteSpace(locState.Status)) continue;
                            sb.AppendLine($"  地点[{locState.Name}]：{TruncateLine(locState.Status, 120)}");
                        }
                    }

                    if (archive.ItemStates != null && archive.ItemStates.Count > 0)
                    {
                        foreach (var item in archive.ItemStates)
                        {
                            if (string.IsNullOrWhiteSpace(item.Name)) continue;
                            var holder = string.IsNullOrWhiteSpace(item.CurrentHolder) ? string.Empty : $"，持有者={item.CurrentHolder}";
                            var status = string.IsNullOrWhiteSpace(item.Status) ? string.Empty : $"，状态={TruncateLine(item.Status, 80)}";
                            sb.AppendLine($"  物品[{item.Name}]{holder}{status}");
                        }
                    }

                    if (archive.ForeshadowingStatus != null && archive.ForeshadowingStatus.Count > 0)
                    {
                        foreach (var fs in archive.ForeshadowingStatus.Where(f => !f.IsResolved))
                        {
                            if (string.IsNullOrWhiteSpace(fs.Name)) continue;
                            var overdue = fs.IsOverdue ? "【逾期】" : string.Empty;
                            sb.AppendLine($"  伏笔[{fs.Name}]{overdue}：已埋设未揭示");
                        }
                    }

                    if (archive.SecretStates != null && archive.SecretStates.Count > 0)
                    {
                        foreach (var secret in archive.SecretStates)
                        {
                            if (string.IsNullOrWhiteSpace(secret.Name)) continue;
                            var knowersStr = (secret.KnowerIds != null && secret.KnowerIds.Count > 0)
                                ? string.Join("、", secret.KnowerIds.Take(10))
                                : "无人知晓";
                            sb.AppendLine($"  秘密[{secret.Name}]：{TruncateLine(secret.Status, 30)}，知情=[{TruncateLine(knowersStr, 120)}]");
                        }
                    }

                    if (archive.PledgeStates != null && archive.PledgeStates.Count > 0)
                    {
                        foreach (var p in archive.PledgeStates)
                        {
                            var name = string.IsNullOrWhiteSpace(p.Name) ? p.Id : p.Name;
                            if (string.IsNullOrWhiteSpace(name)) continue;
                            var type = string.IsNullOrWhiteSpace(p.Type) ? string.Empty : $"{TruncateLine(p.Type, 20)}｜";
                            var status = string.IsNullOrWhiteSpace(p.Status) ? string.Empty : $"{TruncateLine(p.Status, 20)}｜";
                            var condition = string.IsNullOrWhiteSpace(p.Condition) ? string.Empty : $"条件={TruncateLine(p.Condition, 80)}";
                            var consequence = string.IsNullOrWhiteSpace(p.Consequence) ? string.Empty : $"，后果={TruncateLine(p.Consequence, 80)}";
                            sb.AppendLine($"  承诺[{name}]：{type}{status}{condition}{consequence}");
                        }
                    }

                    if (archive.DeadlineStates != null && archive.DeadlineStates.Count > 0)
                    {
                        foreach (var d in archive.DeadlineStates)
                        {
                            var name = string.IsNullOrWhiteSpace(d.Name) ? d.Id : d.Name;
                            if (string.IsNullOrWhiteSpace(name)) continue;
                            var type = string.IsNullOrWhiteSpace(d.Type) ? string.Empty : $"{TruncateLine(d.Type, 20)}｜";
                            var status = string.IsNullOrWhiteSpace(d.Status) ? string.Empty : $"{TruncateLine(d.Status, 20)}｜";
                            var deadline = string.IsNullOrWhiteSpace(d.Deadline) ? string.Empty : $"时限={TruncateLine(d.Deadline, 60)}";
                            var trigger = string.IsNullOrWhiteSpace(d.TriggerCondition) ? string.Empty : $"，触发={TruncateLine(d.TriggerCondition, 60)}";
                            var consequence = string.IsNullOrWhiteSpace(d.Consequence) ? string.Empty : $"，后果={TruncateLine(d.Consequence, 80)}";
                            sb.AppendLine($"  倒计时[{name}]：{type}{status}{deadline}{trigger}{consequence}");
                        }
                    }

                    sb.AppendLine();
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            var hasMdSummaries = ctx.MdPreviousChapterSummaries != null && ctx.MdPreviousChapterSummaries.Count > 0;
            if (!hasMdSummaries && ctx.PreviousChapterSummaries != null && ctx.PreviousChapterSummaries.Count > 0)
            {
                sb.AppendLine("<section name=\"chapter_summaries\" role=\"recent_context\" priority=\"normal\">");
                foreach (var summary in ctx.PreviousChapterSummaries)
                {
                    sb.AppendLine($"**第{summary.ChapterNumber}章**：{summary.Summary}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (hasMdSummaries)
            {
                sb.AppendLine("<section name=\"md_summaries\" role=\"recent_context\" priority=\"normal\">");
                foreach (var summary in ctx.MdPreviousChapterSummaries!)
                {
                    sb.AppendLine($"**第{summary.ChapterNumber}章开头**：");
                    sb.AppendLine(summary.Summary);
                    sb.AppendLine();
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.StateDivergenceWarnings != null && ctx.StateDivergenceWarnings.Count > 0)
            {
                sb.AppendLine("<section name=\"consistency_warnings\" role=\"alert\" priority=\"high\">");
                sb.AppendLine("> 以下警告由系统检测生成，反映账本追踪可信度问题，请优先以FactSnapshot数据为准。");
                sb.AppendLine();
                const int warningsCap = 10;
                var warnings = ctx.StateDivergenceWarnings.Count > warningsCap ? ctx.StateDivergenceWarnings.Take(warningsCap).ToList() : ctx.StateDivergenceWarnings;
                foreach (var w in warnings)
                    sb.AppendLine($"- {TruncateText(w, 200)}");
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.Templates != null && ctx.Templates.Count > 0)
            {
                const int templatesCap = 3;
                var templates = ctx.Templates.Count > templatesCap ? ctx.Templates.Take(templatesCap).ToList() : ctx.Templates;
                var tplCountSuffix = ctx.Templates.Count > templatesCap ? $"/{ctx.Templates.Count}" : "";
                sb.AppendLine($"<section name=\"creative_materials\" role=\"style_reference\" priority=\"normal\" count=\"{templates.Count}{tplCountSuffix}\">");
                foreach (var t in templates)
                {
                    sb.AppendLine($"- **{TruncateText(t.Name ?? string.Empty, 80)}**");
                    if (!string.IsNullOrWhiteSpace(t.OverallIdea))
                        sb.AppendLine($"  - 整体构思：{TruncateText(t.OverallIdea, 200)}");
                    if (!string.IsNullOrWhiteSpace(t.PlotStructure))
                        sb.AppendLine($"  - 情节结构：{TruncateText(t.PlotStructure, 200)}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            const int worldRulesCap = 10;
            if (ctx.WorldRules != null && ctx.WorldRules.Count > 0)
            {
                var worldRules = ctx.WorldRules.Count > worldRulesCap ? ctx.WorldRules.Take(worldRulesCap).ToList() : ctx.WorldRules;
                sb.AppendLine($"<section name=\"worldview_rules\" role=\"setting_constraint\" priority=\"normal\" count=\"{worldRules.Count}{(ctx.WorldRules.Count > worldRulesCap ? $"/{ctx.WorldRules.Count}" : "")}\">");
                foreach (var rule in worldRules)
                {
                    sb.AppendLine($"- **{rule.Name}**");
                    if (!string.IsNullOrWhiteSpace(rule.OneLineSummary))
                        sb.AppendLine($"  - 简介：{TruncateText(rule.OneLineSummary, 100)}");
                    if (!string.IsNullOrWhiteSpace(rule.PowerSystem))
                        sb.AppendLine($"  - 力量体系：{TruncateText(rule.PowerSystem, 150)}");
                    if (!string.IsNullOrWhiteSpace(rule.HardRules))
                        sb.AppendLine($"  - 硬规则：{TruncateText(rule.HardRules, 200)}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.VolumeOutline != null)
            {
                sb.AppendLine("<section name=\"outline\" role=\"macro_reference\" priority=\"normal\">");
                sb.AppendLine($"- 名称：{TruncateText(ctx.VolumeOutline.Name, 80)}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeOutline.Theme))
                    sb.AppendLine($"- 主题：{TruncateText(ctx.VolumeOutline.Theme, 120)}");
                if (ctx.VolumeOutline.TotalChapterCount > 0)
                    sb.AppendLine($"- 全书总章节数：{ctx.VolumeOutline.TotalChapterCount}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeOutline.OneLineOutline))
                    sb.AppendLine($"- 一句话大纲：{TruncateText(ctx.VolumeOutline.OneLineOutline, 200)}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeOutline.EmotionalTone))
                    sb.AppendLine($"- 情感基调：{TruncateText(ctx.VolumeOutline.EmotionalTone, 120)}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeOutline.CoreConflict))
                    sb.AppendLine($"- 核心冲突：{TruncateText(ctx.VolumeOutline.CoreConflict, 200)}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeOutline.EndingState))
                    sb.AppendLine($"- 结局目标：{TruncateText(ctx.VolumeOutline.EndingState, 150)}");
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.VolumeDesign != null)
            {
                sb.AppendLine("<section name=\"volume_design\" role=\"structural_guide\" priority=\"normal\">");
                if (ctx.VolumeDesign.VolumeNumber > 0)
                    sb.AppendLine($"- 卷序号：{ctx.VolumeDesign.VolumeNumber}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeDesign.VolumeTitle))
                    sb.AppendLine($"- 卷标题：{TruncateText(ctx.VolumeDesign.VolumeTitle, 80)}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeDesign.VolumeTheme))
                    sb.AppendLine($"- 卷主题：{TruncateText(ctx.VolumeDesign.VolumeTheme, 150)}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeDesign.StageGoal))
                    sb.AppendLine($"- 阶段目标：{TruncateText(ctx.VolumeDesign.StageGoal, 150)}");
                if (ctx.VolumeDesign.TargetChapterCount > 0)
                    sb.AppendLine($"- 目标章节数：{ctx.VolumeDesign.TargetChapterCount}");
                if (ctx.VolumeDesign.StartChapter > 0 || ctx.VolumeDesign.EndChapter > 0)
                    sb.AppendLine($"- 章节范围：{ctx.VolumeDesign.StartChapter} - {ctx.VolumeDesign.EndChapter}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeDesign.MainConflict))
                    sb.AppendLine($"- 主冲突：{TruncateText(ctx.VolumeDesign.MainConflict, 200)}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeDesign.PressureSource))
                    sb.AppendLine($"- 压力来源：{TruncateText(ctx.VolumeDesign.PressureSource, 200)}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeDesign.KeyEvents))
                    sb.AppendLine($"- 关键事件：{TruncateText(ctx.VolumeDesign.KeyEvents, 250)}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeDesign.OpeningState))
                    sb.AppendLine($"- 开篇状态：{TruncateText(ctx.VolumeDesign.OpeningState, 150)}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeDesign.EndingState))
                    sb.AppendLine($"- 收束状态：{TruncateText(ctx.VolumeDesign.EndingState, 150)}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeDesign.ChapterAllocationOverview))
                    sb.AppendLine($"- 章节分配总览：{TruncateText(ctx.VolumeDesign.ChapterAllocationOverview, 250)}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeDesign.PlotAllocation))
                    sb.AppendLine($"- 剧情分配：{TruncateText(ctx.VolumeDesign.PlotAllocation, 250)}");
                if (!string.IsNullOrWhiteSpace(ctx.VolumeDesign.ChapterGenerationHints))
                    sb.AppendLine($"- 章节生成提示：{TruncateText(ctx.VolumeDesign.ChapterGenerationHints, 250)}");
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            const int charactersCap = 30;
            if (ctx.Characters != null && ctx.Characters.Count > 0)
            {
                var characters = ctx.Characters.Count > charactersCap ? ctx.Characters.Take(charactersCap).ToList() : ctx.Characters;
                var bpCharNames = BuildBlueprintCharNames(ctx);
                var fullCount = 0;
                var briefCount = 0;
                sb.AppendLine($"<section name=\"characters\" role=\"entity_reference\" priority=\"normal\" count=\"{characters.Count}{(ctx.Characters.Count > charactersCap ? $"/{ctx.Characters.Count}" : "")}\">");
                foreach (var c in characters)
                {
                    var isFull = bpCharNames.Count == 0 || bpCharNames.Contains(c.Name);
                    sb.AppendLine($"- **{c.Name}**");
                    if (!string.IsNullOrWhiteSpace(c.CharacterType))
                        sb.AppendLine($"  - 类型：{c.CharacterType}");
                    if (!string.IsNullOrWhiteSpace(c.Identity))
                        sb.AppendLine($"  - 身份：{TruncateText(c.Identity, 80)}");
                    if (isFull)
                    {
                        if (!string.IsNullOrWhiteSpace(c.Race))
                            sb.AppendLine($"  - 种族：{c.Race}");
                        if (!string.IsNullOrWhiteSpace(c.Appearance))
                            sb.AppendLine($"  - 外貌：{TruncateText(c.Appearance, 100)}");
                    }
                    else
                    {
                        var hairTag = ExtractHairColorTag(c.Appearance);
                        if (!string.IsNullOrEmpty(hairTag))
                            sb.AppendLine($"  - 发色：{hairTag}");
                    }
                    if (!string.IsNullOrWhiteSpace(c.Want))
                        sb.AppendLine($"  - 外在目标：{TruncateText(c.Want, 80)}");
                    if (isFull && !string.IsNullOrWhiteSpace(c.Need))
                        sb.AppendLine($"  - 内在需求：{TruncateText(c.Need, 80)}");
                    if (!string.IsNullOrWhiteSpace(c.FlawBelief))
                        sb.AppendLine($"  - 致命缺点：{TruncateText(c.FlawBelief, 80)}");
                    if (isFull)
                    {
                        if (!string.IsNullOrWhiteSpace(c.GrowthPath))
                            sb.AppendLine($"  - 成长路径：{TruncateText(c.GrowthPath, 100)}");
                        if (!string.IsNullOrWhiteSpace(c.SpecialAbilities))
                            sb.AppendLine($"  - 特殊能力：{TruncateText(c.SpecialAbilities, 100)}");
                        if (!string.IsNullOrWhiteSpace(c.NonCombatSkills))
                            sb.AppendLine($"  - 非战斗技能：{TruncateText(c.NonCombatSkills, 80)}");
                        if (!string.IsNullOrWhiteSpace(c.SignatureItems))
                            sb.AppendLine($"  - 标志性装备：{TruncateText(c.SignatureItems, 80)}");
                        fullCount++;
                    }
                    else
                    {
                        briefCount++;
                    }
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
                if (briefCount > 0)
                    TM.App.Log($"[LayeredPromptBuilder] 角色注入: {fullCount}完整/{briefCount}精简");
            }

            const int factionsCap = 20;
            if (ctx.Factions != null && ctx.Factions.Count > 0)
            {
                var bpFactionNames = BuildBlueprintFactionNames(ctx);
                var factions = ctx.Factions.Count > factionsCap ? ctx.Factions.Take(factionsCap).ToList() : ctx.Factions;
                sb.AppendLine($"<section name=\"factions\" role=\"entity_reference\" priority=\"normal\" count=\"{factions.Count}{(ctx.Factions.Count > factionsCap ? $"/{ctx.Factions.Count}" : "")}\">");
                int fFullCount = 0, fBriefCount = 0;
                foreach (var f in factions)
                {
                    var isFull = bpFactionNames.Count == 0 || bpFactionNames.Contains(f.Name);
                    sb.AppendLine($"- **{f.Name}**");
                    if (!string.IsNullOrWhiteSpace(f.FactionType))
                        sb.AppendLine($"  - 类型：{f.FactionType}");
                    if (!string.IsNullOrWhiteSpace(f.Goal))
                        sb.AppendLine($"  - 理念目标：{TruncateText(f.Goal, 100)}");
                    if (isFull)
                    {
                        fFullCount++;
                        if (!string.IsNullOrWhiteSpace(f.Leader))
                            sb.AppendLine($"  - 领袖：{ResolveId(f.Leader, characterIdToName)}");
                        if (!string.IsNullOrWhiteSpace(f.StrengthTerritory))
                            sb.AppendLine($"  - 实力/地盘：{TruncateText(f.StrengthTerritory, 100)}");
                        if (!string.IsNullOrWhiteSpace(f.MemberTraits))
                            sb.AppendLine($"  - 成员特征：{TruncateText(f.MemberTraits, 80)}");
                        if (!string.IsNullOrWhiteSpace(f.Allies))
                            sb.AppendLine($"  - 盟友：{TruncateText(f.Allies, 80)}");
                        if (!string.IsNullOrWhiteSpace(f.Enemies))
                            sb.AppendLine($"  - 敌对：{TruncateText(f.Enemies, 80)}");
                        if (!string.IsNullOrWhiteSpace(f.NeutralCompetitors))
                            sb.AppendLine($"  - 中立/竞争：{TruncateText(f.NeutralCompetitors, 80)}");
                    }
                    else
                    {
                        fBriefCount++;
                    }
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
                if (fFullCount + fBriefCount > 0)
                    TM.App.Log($"[LayeredPromptBuilder] 势力注入: {fFullCount}完整/{fBriefCount}精简");
            }

            const int locationsCap = 30;
            if (ctx.Locations != null && ctx.Locations.Count > 0)
            {
                var locations = ctx.Locations.Count > locationsCap ? ctx.Locations.Take(locationsCap).ToList() : ctx.Locations;
                sb.AppendLine($"<section name=\"locations\" role=\"entity_reference\" priority=\"normal\" count=\"{locations.Count}{(ctx.Locations.Count > locationsCap ? $"/{ctx.Locations.Count}" : "")}\">");
                foreach (var loc in locations)
                {
                    sb.AppendLine($"- **{loc.Name}**：{TruncateText(loc.Description, 100)}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            const int plotRulesCap = 20;
            if (ctx.PlotRules != null && ctx.PlotRules.Count > 0)
            {
                var plotRules = ctx.PlotRules.Count > plotRulesCap ? ctx.PlotRules.Take(plotRulesCap).ToList() : ctx.PlotRules;
                sb.AppendLine($"<section name=\"plot_rules\" role=\"narrative_guide\" priority=\"normal\" count=\"{plotRules.Count}{(ctx.PlotRules.Count > plotRulesCap ? $"/{ctx.PlotRules.Count}" : "")}\">");
                foreach (var p in plotRules)
                {
                    sb.AppendLine($"- **{p.Name}**");
                    if (!string.IsNullOrWhiteSpace(p.OneLineSummary))
                        sb.AppendLine($"  - 简介：{TruncateText(p.OneLineSummary, 100)}");
                    if (!string.IsNullOrWhiteSpace(p.Goal))
                        sb.AppendLine($"  - 目标：{TruncateText(p.Goal, 100)}");
                    if (!string.IsNullOrWhiteSpace(p.Conflict))
                        sb.AppendLine($"  - 冲突：{TruncateText(p.Conflict, 100)}");
                    if (!string.IsNullOrWhiteSpace(p.MainCharacters))
                        sb.AppendLine($"  - 主要角色：{ResolveIds(p.MainCharacters, characterIdToName)}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            const int expandedCharsCap = 20;
            if (ctx.ExpandedCharacters != null && ctx.ExpandedCharacters.Count > 0)
            {
                var expandedChars = ctx.ExpandedCharacters.Count > expandedCharsCap ? ctx.ExpandedCharacters.Take(expandedCharsCap).ToList() : ctx.ExpandedCharacters;
                sb.AppendLine($"<section name=\"expanded_characters\" role=\"entity_reference\" priority=\"normal\" count=\"{expandedChars.Count}{(ctx.ExpandedCharacters.Count > expandedCharsCap ? $"/{ctx.ExpandedCharacters.Count}" : "")}\">");
                foreach (var c in expandedChars)
                {
                    sb.AppendLine($"- **{c.Name}**");
                    if (!string.IsNullOrWhiteSpace(c.CharacterType))
                        sb.AppendLine($"  - 类型：{c.CharacterType}");
                    if (!string.IsNullOrWhiteSpace(c.Identity))
                        sb.AppendLine($"  - 身份：{TruncateText(c.Identity, 80)}");
                    if (!string.IsNullOrWhiteSpace(c.Want))
                        sb.AppendLine($"  - 外在目标：{TruncateText(c.Want, 80)}");
                    if (!string.IsNullOrWhiteSpace(c.FlawBelief))
                        sb.AppendLine($"  - 致命缺点：{TruncateText(c.FlawBelief, 80)}");
                    if (!string.IsNullOrWhiteSpace(c.SpecialAbilities))
                        sb.AppendLine($"  - 特殊能力：{TruncateText(c.SpecialAbilities, 100)}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            if (ctx.Scenes != null && ctx.Scenes.Count > 0 && (ctx.Blueprints == null || ctx.Blueprints.Count == 0))
            {
                const int scenesCap = 30;
                var scenes = ctx.Scenes.Count > scenesCap ? ctx.Scenes.Take(scenesCap).ToList() : ctx.Scenes;
                var sceneCountSuffix = ctx.Scenes.Count > scenesCap ? $"/{ctx.Scenes.Count}" : "";
                sb.AppendLine($"<section name=\"scenes\" role=\"scene_structure\" priority=\"normal\" count=\"{scenes.Count}{sceneCountSuffix}\">");
                foreach (var s in scenes)
                {
                    sb.AppendLine($"- 场景{s.SceneNumber}：{TruncateText(s.Purpose ?? string.Empty, 200)}");
                }
                sb.AppendLine("</section>");
                sb.AppendLine();
            }

            sb.AppendLine("</task_context>");
            sb.AppendLine();
        }

        private void AppendChapterTailSection(StringBuilder sb, ContentTaskContext ctx)
        {
            if (string.IsNullOrEmpty(ctx.PreviousChapterTail))
                return;

            sb.AppendLine("<section name=\"chapter_tail\" role=\"connection_anchor\" priority=\"high\">");
            sb.AppendLine("```");
            sb.AppendLine(ctx.PreviousChapterTail);
            sb.AppendLine("```");
            sb.AppendLine("> 请从此处自然衔接，不要复述提示词，不要解释你的写作过程。");
            sb.AppendLine("</section>");
            sb.AppendLine();
        }

        #endregion
    }
}
