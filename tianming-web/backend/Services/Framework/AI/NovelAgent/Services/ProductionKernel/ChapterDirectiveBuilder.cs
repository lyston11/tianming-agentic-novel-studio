using System;
using System.Collections.Generic;
using System.Linq;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel
{
    public sealed class ChapterDirectiveBuilder : IChapterDirectiveBuilder
    {
        public ChapterDirective Build(NovelAgentRun run, ChapterContextPackageSummary contextPackage)
        {
            var chapterId = FirstNonEmpty(contextPackage.ChapterId, run.TargetChapterId);
            var protagonistName = ExtractFactValue(contextPackage.HardContinuityFacts, "主角姓名", "上一章主角");
            var protagonistIdentity = ExtractFactValue(contextPackage.HardContinuityFacts, "主角身份", "上一章主角身份");
            var protagonistStatus = ExtractFactValue(contextPackage.HardContinuityFacts, "主角当前状态", "上一章主角状态");
            var systemState = ExtractFactValue(contextPackage.HardContinuityFacts, "系统状态", "上一章系统状态");
            var creativeRequirements = contextPackage.AcceptedCreativeIntents
                .Where(intent => HasText(intent.NormalizedIntent))
                .Take(16)
                .Select(intent => AcceptedCreativeIntentPolicy.FormatForDirective(intent, chapterId))
                .ToList();
            var editorialRevisionRequirements = ExtractEditorialRevisionRequirements(contextPackage)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();
            var mustCarry = ExtractFactValues(
                    contextPackage.HardContinuityFacts,
                    "上一章结尾状态",
                    "下一章必须承接")
                .Concat(contextPackage.PreviousSummaries.Take(2))
                .Where(HasText)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();

            var knowledgeBoundaries = contextPackage.WorldRules
                .Concat(contextPackage.HardContinuityFacts)
                .Concat(contextPackage.CharacterStates)
                .Concat(contextPackage.PreviousSummaries)
                .Concat(FormatKnowledgeBindingsForDirective(
                    contextPackage.KnowledgeBindings,
                    "HardFact",
                    "Constraint",
                    "Boundary"))
                .Where(IsKnowledgeBoundaryLine)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();

            return new ChapterDirective
            {
                ChapterId = chapterId,
                TargetTitle = FirstNonEmpty(
                    run.ChapterBrief?.SelectedCandidateTitle,
                    run.ChapterBrief?.RecommendedCandidateTitle,
                    run.TargetChapterId),
                UserGoal = run.UserGoal,
                ChapterObjective = FirstNonEmpty(run.ChapterBrief?.CoreIdea, run.UserGoal, contextPackage.ChapterBlueprints.FirstOrDefault()),
                ProtagonistAnchor = BuildProtagonistAnchor(protagonistName, protagonistIdentity, protagonistStatus),
                SystemAnchor = systemState ?? string.Empty,
                MustCarry = mustCarry,
                SceneBeats = contextPackage.ChapterBlueprints
                    .Concat(FormatKnowledgeBlueprintRequirements(contextPackage.KnowledgeBindings))
                    .Concat(BuildBlueprintLines(run))
                    .Where(HasText)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(12)
                    .ToList(),
                CreativeRequirements = creativeRequirements,
                EditorialRevisionRequirements = editorialRevisionRequirements,
                KnowledgeBoundaries = knowledgeBoundaries,
                StyleGuides = FormatKnowledgeBindingsForDirective(
                        contextPackage.KnowledgeBindings,
                        "Style",
                        "Tone",
                        "Voice")
                    .Take(8)
                    .ToList(),
                WorldKnowledge = FormatKnowledgeBindingsForDirective(
                        contextPackage.KnowledgeBindings,
                        "WorldRule",
                        "World",
                        "Setting",
                        "Lore")
                    .Concat(contextPackage.WorldRules.Where(HasText))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(10)
                    .ToList(),
                CharacterKnowledge = FormatKnowledgeBindingsForDirective(
                        contextPackage.KnowledgeBindings,
                        "Character",
                        "Role",
                        "Relationship")
                    .Concat(contextPackage.CharacterStates.Where(HasText))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(10)
                    .ToList(),
                SceneMaterials = FormatKnowledgeBindingsForDirective(
                        contextPackage.KnowledgeBindings,
                        "Material",
                        "Scene",
                        "Location",
                        "Object")
                    .Take(10)
                    .ToList(),
                ContinuityReferences = contextPackage.PreviousSummaries
                    .Concat(contextPackage.LongDistanceRecall)
                    .Where(HasText)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(8)
                    .ToList(),
                AcceptanceRules =
                {
                    "正文必须围绕 chapterDirective 写作；不能只在 CHANGES 中满足任务。",
                    "开章或关键场景必须自然承接 mustCarry 中的每一项。",
                    "主角姓名、身份、当前状态和系统状态属于硬事实，不能改名、换主角或无解释跳变。",
                    "creativeRequirements 中的已采纳创意是用户/Agent 已确认的生产输入；章节级或章节重写级创意必须在正文真实发生，不能只写在 CHANGES。",
                    "editorialRevisionRequirements 是 Agent 总编验收失败项；重写时必须逐条落实到正文场景、冲突、代价或章末钩子中，不能只写在 CHANGES。",
                    "knowledgeBoundaries 中的知识库硬事实优先级高于桥段爽点；正文不得扩写未授权能力。",
                    "章节正文必须完整推进 sceneBeats，并在末尾输出合法 <chapter_changes> JSON。"
                }
            };
        }

        private static IEnumerable<string> FormatKnowledgeBlueprintRequirements(
            IEnumerable<BoundKnowledgeSnapshot> bindings)
        {
            foreach (var binding in bindings)
            {
                if (!binding.ShouldEnterBlueprint || !HasText(binding.ClassificationRule))
                    continue;

                var title = FirstNonEmpty(binding.Title, binding.KnowledgeId, "知识库");
                yield return $"知识蓝图要求：{title}：{binding.ClassificationRule.Trim()}";
            }
        }

        private static IEnumerable<string> ExtractEditorialRevisionRequirements(ChapterContextPackageSummary contextPackage)
        {
            foreach (var line in contextPackage.Warnings.Concat(contextPackage.HardContinuityFacts).Where(HasText))
            {
                const string marker = "AgentReview修订要求";
                var markerIndex = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (markerIndex < 0)
                    continue;

                var requirement = line[(markerIndex + marker.Length)..].Trim();
                if (requirement.StartsWith("：", StringComparison.Ordinal) ||
                    requirement.StartsWith(":", StringComparison.Ordinal))
                {
                    requirement = requirement[1..].Trim();
                }

                if (HasText(requirement))
                    yield return requirement;
            }

            if (contextPackage.DirectedRework == null)
                yield break;
            yield return $"定向返工问题：{contextPackage.DirectedRework.Problem}";
            yield return $"定向返工目标：{contextPackage.DirectedRework.DesiredEffect}";
            foreach (var item in contextPackage.DirectedRework.AcceptanceCriteria.Where(HasText))
                yield return $"定向返工验收：{item}";
            foreach (var item in contextPackage.DirectedRework.MustNotChange.Where(HasText))
                yield return $"定向返工禁止修改：{item}";
        }

        private static IEnumerable<string> FormatKnowledgeBindingsForDirective(
            IEnumerable<BoundKnowledgeSnapshot> bindings,
            params string[] entryTypes)
        {
            var accepted = entryTypes
                .Where(HasText)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var binding in bindings)
            {
                if (accepted.Count > 0 &&
                    !accepted.Contains(binding.EntryType) &&
                    !ShouldUseClassificationRuleAsBoundary(binding, accepted))
                {
                    continue;
                }
                var title = binding.Title?.Trim() ?? string.Empty;
                var content = FirstNonEmpty(binding.ClassificationRule, binding.Content);
                if (!HasText(title) && !HasText(content))
                    continue;

                var status = HasText(binding.ProjectUsageStatus) ? $"（{binding.ProjectUsageStatus}）" : string.Empty;
                if (!HasText(title))
                {
                    yield return $"{binding.EntryType}{status}：{content}";
                    continue;
                }
                if (!HasText(content))
                {
                    yield return $"{binding.EntryType}{status}：{title}";
                    continue;
                }
                yield return $"{title}{status}：{content}";
            }
        }

        private static bool ShouldUseClassificationRuleAsBoundary(
            BoundKnowledgeSnapshot binding,
            IReadOnlySet<string> accepted)
        {
            if (!HasText(binding.ClassificationRule))
                return false;
            if (!accepted.Contains("HardFact") &&
                !accepted.Contains("Constraint") &&
                !accepted.Contains("Boundary"))
            {
                return false;
            }

            return binding.ShouldEnterGate ||
                   string.Equals(binding.ConstraintLevel, "HardConstraint", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildProtagonistAnchor(
            string? name,
            string? identity,
            string? status)
        {
            var parts = new[]
            {
                HasText(name) ? $"姓名={name}" : string.Empty,
                HasText(identity) ? $"身份={identity}" : string.Empty,
                HasText(status) ? $"当前状态={status}" : string.Empty
            }.Where(HasText);
            return string.Join("；", parts);
        }

        private static bool IsKnowledgeBoundaryLine(string? line)
        {
            if (!HasText(line))
                return false;

            var value = line!;
            return value.Contains("知识库硬事实", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("只能", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("仅能", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("仅在", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("不可", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("不能", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("不得", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> BuildBlueprintLines(NovelAgentRun run)
        {
            var brief = run.ChapterBrief;
            if (brief == null)
                yield break;

            if (HasText(brief.VolumeBeatRole)) yield return $"卷节拍：{brief.VolumeBeatRole}";
            if (HasText(brief.CoreIdea)) yield return $"核心创意：{brief.CoreIdea}";
            if (HasText(brief.ConflictMove)) yield return $"冲突推进：{brief.ConflictMove}";
            if (HasText(brief.CharacterChoice)) yield return $"角色选择：{brief.CharacterChoice}";
            if (HasText(brief.CostOrConsequence)) yield return $"代价后果：{brief.CostOrConsequence}";
            if (HasText(brief.ForeshadowingAction)) yield return $"伏笔动作：{brief.ForeshadowingAction}";
        }

        private static string? ExtractFactValue(IEnumerable<string> facts, params string[] keys) =>
            ExtractFactValues(facts, keys).FirstOrDefault();

        private static IEnumerable<string> ExtractFactValues(IEnumerable<string> facts, params string[] keys)
        {
            foreach (var fact in facts.Where(HasText))
            {
                foreach (var key in keys)
                {
                    var prefix = key + "：";
                    if (fact.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        yield return fact[prefix.Length..].Trim();
                    prefix = key + ":";
                    if (fact.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        yield return fact[prefix.Length..].Trim();
                }
            }
        }

        private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

        private static string FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(HasText)?.Trim() ?? string.Empty;
    }
}
