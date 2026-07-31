using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using TM.Framework.Common.Helpers;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel
{
    public sealed class ChapterPromptBuilder : IChapterPromptBuilder
    {
        private readonly IChapterDirectiveBuilder _chapterDirectiveBuilder;

        public ChapterPromptBuilder(IChapterDirectiveBuilder chapterDirectiveBuilder)
        {
            _chapterDirectiveBuilder = chapterDirectiveBuilder;
        }

        public string BuildWritingSystemPrompt() =>
            """
            你是长篇小说正文写作模型。你必须输出完整章节正文，并在末尾输出成对的 <chapter_changes>...</chapter_changes>。
            CHANGES 内只能是合法 JSON 对象，必须包含这些顶级字段：
            CharacterStateChanges, ConflictProgress, NewPlotPoints, ForeshadowingActions, LocationStateChanges,
            FactionStateChanges, TimeProgression, CharacterMovements, ItemTransfers, SecretRevealChanges,
            PledgeConstraintChanges, DeadlineConstraintChanges。
            所有不存在的变更字段也要用空数组或空对象显式给出。不要使用 Markdown 代码块包裹 CHANGES。
            <chapter_changes> 内部必须直接是上述顶级字段对象，禁止再包一层 changes、CHANGES、chapter_changes、chapterChanges 或任何解释字段。
            正确结构示例：<chapter_changes>{"CharacterStateChanges":[],"ConflictProgress":[],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":null,"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}</chapter_changes>
            正文必须严格遵守上下文包、事实快照、蓝图、长距离召回，不得发明关键实体。
            当 contextPackage.directedRework 存在时，只允许修改 mayChange 指定范围，必须保留 preserve，绝不能改动 mustNotChange；selection 范围外正文必须保持不变。
            如果上下文包声明某个道具/能力只能观察、辨认、溯源或提示方向，正文不得把它写成激活、释放、攻击、治愈、修复、腐蚀、干扰、封闭、摧毁、杀伤或新增功能。
            """;

        public string BuildChangesOnlyRepairSystemPrompt() =>
            """
            你是长篇小说章节修订记录生成模型。你只负责为既有正文补齐合法的 <chapter_changes>...</chapter_changes>。
            只能输出一个成对的 <chapter_changes> XML 块，块内只能是合法 JSON 对象。
            JSON 必须包含这些顶级字段：
            CharacterStateChanges, ConflictProgress, NewPlotPoints, ForeshadowingActions, LocationStateChanges,
            FactionStateChanges, TimeProgression, CharacterMovements, ItemTransfers, SecretRevealChanges,
            PledgeConstraintChanges, DeadlineConstraintChanges。
            所有不存在的变更字段也要用空数组或空对象显式给出。
            <chapter_changes> 内部必须直接是上述顶级字段对象，禁止再包一层 changes、CHANGES、chapter_changes、chapterChanges 或任何解释字段。
            不要输出正文，不要输出 Markdown 代码块，不要解释。
            """;

        public string BuildWritingUserPrompt(
            NovelAgentRun run,
            ChapterContextPackageSummary contextPackage)
        {
            var chapterDirective = _chapterDirectiveBuilder.Build(run, contextPackage);
            var payload = new
            {
                task = NovelAgentProductionStages.DraftGeneration,
                chapterId = run.TargetChapterId,
                chapterDirective,
                chapterBrief = run.ChapterBrief,
                contextPackage,
                requiredChangesSchema = ChapterChanges.TopLevelFieldNames
            };
            return JsonSerializer.Serialize(payload, JsonHelper.CnDefault);
        }

        public string BuildRepairUserPrompt(
            NovelAgentRun run,
            ChapterContextPackageSummary contextPackage,
            ChapterDraftArtifact draft,
            GenerationGateReport report)
        {
            var requiredContinuityCarryChecklist = BuildRequiredContinuityCarryChecklist(contextPackage, report);
            var chapterDirective = _chapterDirectiveBuilder.Build(run, contextPackage);
            var payload = new
            {
                task = NovelAgentProductionStages.DraftRepair,
                chapterId = run.TargetChapterId,
                chapterDirective,
                repairAttempt = draft.RepairAttemptCount + 1,
                repairStrategy = SelectRepairStrategy(draft.RepairAttemptCount + 1),
                gateIssues = report.Issues,
                repairHints = report.RepairHints,
                requiredContinuityCarryChecklist,
                boundaryRewritePolicy = BuildBoundaryRewritePolicy(report),
                repairAcceptanceRules = new[]
                {
                    "逐条修复 gateIssues；每条 requiredContinuityCarryChecklist 都必须在正文开章或关键场景中被明确承接，不能只写在 CHANGES。",
                    "保留已经通过的连续性承接，不要修一条丢一条；修订后正文必须同时覆盖全部未承接项。",
                    "允许自然改写表达，但必须包含足够清晰的实体、目标、压力或后果，让门禁能判断正文已承接。",
                    "若 gateIssues 涉及道具/能力边界，不要保留“激活/释放/功能/新增能力/干扰/修复/腐蚀/强化/升级/扩展”等越权词再用否定句解释；请直接改写为知识库允许的观察、辨认、溯源、提示方向或确认痕迹，例如银蓝邮徽应写为辨认邮路、溯源残留信标、提示方向或确认痕迹。",
                    "不要用“基础共鸣、辨识单元、模块、单元、协议、被动能力、获得强化、升级、扩展范围、追踪场”等包装词给受限道具新增能力；如果知识库只允许辨认/溯源，就只能写辨认/溯源的结果。",
                    "CHANGES 必须同步记录正文中的真实变化，不能用 CHANGES 代替正文情节。"
                },
                contextPackage,
                previousDraft = draft.DraftContent,
                requiredChangesSchema = ChapterChanges.TopLevelFieldNames
            };
            return JsonSerializer.Serialize(payload, JsonHelper.CnDefault);
        }

        public string BuildChangesOnlyRepairUserPrompt(
            NovelAgentRun run,
            ChapterContextPackageSummary contextPackage,
            ChapterDraftArtifact draft,
            GenerationGateReport report)
        {
            var payload = new
            {
                task = "repair_chapter_changes_only",
                chapterId = run.TargetChapterId,
                gateIssues = report.Issues,
                repairHints = report.RepairHints,
                chapterBody = StripChanges(draft.DraftContent),
                hardContinuityFacts = contextPackage.HardContinuityFacts,
                previousSummaries = contextPackage.PreviousSummaries,
                characterStates = contextPackage.CharacterStates,
                activeConflicts = contextPackage.ActiveConflicts,
                requiredChangesSchema = ChapterChanges.TopLevelFieldNames,
                acceptanceRules = new[]
                {
                    "只根据 chapterBody 中已经发生的正文事实生成 CHANGES，不得新增正文外事件。",
                    "CHANGES 必须覆盖角色状态、冲突推进、剧情新增点、伏笔动作、地点变化、移动和关键物品变化。",
                    "必须输出成对的 <chapter_changes>...</chapter_changes>，且内部是单个合法 JSON 对象。",
                    "JSON 对象必须直接包含 requiredChangesSchema 顶级字段；禁止输出 {\"changes\":{...}}、{\"chapter_changes\":{...}} 或 Markdown 代码块。",
                    "禁止输出 previousDraft、正文、解释、Markdown 代码块或额外文本。"
                }
            };
            return JsonSerializer.Serialize(payload, JsonHelper.CnDefault);
        }

        private static object BuildBoundaryRewritePolicy(GenerationGateReport report)
        {
            var enabled = report.Issues.Any(issue =>
                issue.Contains("能力边界", StringComparison.Ordinal) ||
                issue.Contains("知识库硬事实", StringComparison.Ordinal));

            return new
            {
                enabled,
                instruction = enabled
                    ? "如果 gateIssues 涉及道具/能力边界，必须把包含受限主体与越权词的整句改写为允许行为；不要在原句上做同义包装、括号说明、否定解释或能力升级解释。"
                    : "没有能力边界失败时，按 gateIssues 正常修订。",
                forbiddenPackaging = new[]
                {
                    "基础单元升级",
                    "模块升级",
                    "辨识单元",
                    "基础共鸣",
                    "被动能力",
                    "功能强化",
                    "能力扩展",
                    "追踪场",
                    "协议解锁",
                    "权限提升"
                },
                allowedOnlyExamples = new[]
                {
                    "辨认邮路",
                    "溯源残留信标",
                    "提示方向",
                    "确认痕迹",
                    "标记可投递路径"
                }
            };
        }

        private static List<string> BuildRequiredContinuityCarryChecklist(
            ChapterContextPackageSummary contextPackage,
            GenerationGateReport report)
        {
            var items = new List<string>();
            foreach (var issue in report.Issues.Where(HasText))
            {
                var match = Regex.Match(issue, "未承接「(?<item>[^」]+)」");
                if (match.Success)
                    items.Add(match.Groups["item"].Value.Trim());
            }

            foreach (var hint in report.RepairHints.Where(HasText))
            {
                var index = hint.IndexOf("必须承接项：", StringComparison.Ordinal);
                if (index >= 0)
                    items.Add(hint[(index + "必须承接项：".Length)..].Trim());
            }

            foreach (var carry in ExtractFactValues(contextPackage.HardContinuityFacts, "上一章结尾状态", "下一章必须承接"))
            {
                if (report.Issues.Any(issue => issue.Contains(carry, StringComparison.Ordinal)) ||
                    report.RepairHints.Any(hint => hint.Contains(carry, StringComparison.Ordinal)))
                {
                    items.Add(carry.Trim());
                }
            }

            return items
                .Where(HasText)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();
        }

        private static string SelectRepairStrategy(int attempt) =>
            attempt switch
            {
                <= 1 => "patch_missing_continuity_facts_without_changing_valid_plot",
                2 => "rewrite_scene_that_failed_continuity_gate",
                _ => "regenerate_opening_and_key_scene_around_hard_continuity_facts"
            };

        private static IEnumerable<string> ExtractFactValues(IEnumerable<string> facts, params string[] keys)
        {
            foreach (var fact in facts)
            {
                if (!HasText(fact)) continue;
                foreach (var key in keys)
                {
                    var marker = key + "：";
                    var index = fact.IndexOf(marker, StringComparison.Ordinal);
                    if (index >= 0)
                    {
                        var value = fact[(index + marker.Length)..].Trim();
                        if (HasText(value)) yield return value;
                    }
                }
            }
        }

        private static string StripChanges(string content)
            => ChapterChangesText.StripChanges(content);

        private static bool HasText(string? value) =>
            !string.IsNullOrWhiteSpace(value);
    }
}
