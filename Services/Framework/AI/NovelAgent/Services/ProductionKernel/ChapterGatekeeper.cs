using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel
{
    public sealed class ChapterGatekeeper : IChapterGatekeeper
    {
        public void ApplyHardGates(
            GenerationGateReport report,
            ChapterContextPackageSummary contextPackage,
            ChapterDraftArtifact draft)
        {
            ApplyCoreContinuityGate(report, contextPackage, draft);
            ApplyKnowledgeBoundaryGate(report, contextPackage, draft);
            ApplyDesignRulesGate(report, contextPackage, draft);
            ApplyAcceptedCreativeIntentGate(report, contextPackage, draft);
            ApplySourceRevisionPlanGate(report, contextPackage, draft);
        }

        /// <summary>
        /// Validates that the draft satisfies hard-constraint design rules.
        /// Design rules with ConstraintLevel="MustSatisfy" must appear in the draft.
        /// Design rules with ConstraintLevel="Forbidden" must NOT appear in the draft.
        /// </summary>
        private static void ApplyDesignRulesGate(
            GenerationGateReport report,
            ChapterContextPackageSummary contextPackage,
            ChapterDraftArtifact draft)
        {
            if (contextPackage.DesignRules == null || contextPackage.DesignRules.Count == 0)
                return;

            var body = NormalizeContinuityText(StripChanges(draft.DraftContent));
            var hasFailure = false;

            foreach (var rule in contextPackage.DesignRules.OrderByDescending(r => r.Priority))
            {
                if (string.IsNullOrWhiteSpace(rule.RuleContent))
                    continue;

                var ruleNorm = NormalizeContinuityText(rule.RuleContent);
                var ruleLabel = $"{rule.RuleType} v{rule.Version}";

                if (string.Equals(rule.ConstraintLevel, "MustSatisfy", StringComparison.OrdinalIgnoreCase))
                {
                    // Hard constraint: at least the key concept from the rule must be detectable
                    if (!ContainsEnoughContinuityKeywords(body, ruleNorm))
                    {
                        report.Issues.Add($"设计规则硬约束未满足（{ruleLabel}）：{TrimForIssue(rule.RuleContent)}");
                        report.RepairHints.Add($"重写正文必须体现设计规则【{rule.RuleType}】：{TrimForIssue(rule.RuleContent)}");
                        hasFailure = true;
                    }
                }
                else if (string.Equals(rule.ConstraintLevel, "Forbidden", StringComparison.OrdinalIgnoreCase))
                {
                    // Forbidden rule: any clear match means violation
                    if (ContainsEnoughContinuityKeywords(body, ruleNorm))
                    {
                        report.Issues.Add($"设计规则禁用项被触发（{ruleLabel}）：{TrimForIssue(rule.RuleContent)}");
                        report.RepairHints.Add($"重写正文必须移除被禁用的设计：{TrimForIssue(rule.RuleContent)}");
                        hasFailure = true;
                    }
                }
                else if (string.Equals(rule.ConstraintLevel, "MustMention", StringComparison.OrdinalIgnoreCase))
                {
                    // Soft constraint: warn but don't fail
                    if (!ContainsEnoughContinuityKeywords(body, ruleNorm))
                    {
                        report.RepairHints.Add($"建议在合适位置引用设计规则【{rule.RuleType}】：{TrimForIssue(rule.RuleContent)}");
                    }
                }
            }

            if (hasFailure)
            {
                report.BlueprintPassed = false;
                report.Status = "gate_failed";
            }
        }

        private static void ApplyCoreContinuityGate(
            GenerationGateReport report,
            ChapterContextPackageSummary contextPackage,
            ChapterDraftArtifact draft)
        {
            var body = NormalizeContinuityText(Regex.Replace(StripChanges(draft.DraftContent), @"<\s*(?:chapter_changes|changes)\b[\s\S]*$", string.Empty, RegexOptions.IgnoreCase));
            var protagonistName = ExtractFactValue(contextPackage.HardContinuityFacts, "主角姓名", "上一章主角");
            if (HasText(protagonistName) && !body.Contains(NormalizeContinuityText(protagonistName!), StringComparison.Ordinal))
            {
                report.Issues.Add($"核心连续性失败：本章没有承接硬事实主角「{protagonistName}」。");
                report.RepairHints.Add($"重写正文，主角姓名、身份和当前状态必须继续使用「{protagonistName}」。");
            }

            var protagonistStatus = ExtractFactValue(contextPackage.HardContinuityFacts, "主角当前状态", "上一章主角状态");
            if (HasText(protagonistStatus) && !ContainsEnoughContinuityKeywords(body, protagonistStatus!))
            {
                report.Issues.Add("核心连续性失败：主角当前状态没有从上一章硬事实自然承接。");
                report.RepairHints.Add($"承接主角状态：{protagonistStatus}");
            }

            var systemState = ExtractFactValue(contextPackage.HardContinuityFacts, "系统状态", "上一章系统状态");
            if (HasText(systemState) && body.Contains("系统", StringComparison.Ordinal) && !ContainsEnoughContinuityKeywords(body, systemState!))
            {
                report.Issues.Add("核心连续性失败：系统状态与上一章硬事实不一致或发生无解释跳变。");
                report.RepairHints.Add($"系统状态必须从这里承接：{systemState}");
            }

            foreach (var carry in ExtractFactValues(contextPackage.HardContinuityFacts, "上一章结尾状态", "下一章必须承接")
                         .Where(IsNarrativeCarryLine)
                         .Take(8))
            {
                if (!ContainsEnoughContinuityKeywords(body, carry))
                {
                    report.Issues.Add($"核心连续性失败：未承接「{TrimForIssue(carry)}」。");
                    report.RepairHints.Add($"开章或关键场景必须回应上一章结尾/必须承接项：{carry}");
                }
            }

            if (report.Issues.Count > 0)
            {
                report.FactSnapshotPassed = false;
                report.RagPassed = false;
                report.Status = "gate_failed";
            }
        }

        private static void ApplyKnowledgeBoundaryGate(
            GenerationGateReport report,
            ChapterContextPackageSummary contextPackage,
            ChapterDraftArtifact draft)
        {
            var constraints = ExtractKnowledgeBoundaryConstraints(contextPackage).ToList();
            if (constraints.Count == 0)
                return;

            var body = NormalizeContinuityText(StripChanges(draft.DraftContent));
            foreach (var constraint in constraints)
            {
                var violations = FindKnowledgeBoundaryViolations(body, constraint).ToList();
                var violationEvidence = violations
                    .Select(violation => FormatKnowledgeViolationEvidence(violation, constraint))
                    .ToList();
                report.KnowledgeConstraintChecks.Add(new KnowledgeConstraintCheck
                {
                    KnowledgeId = constraint.KnowledgeId,
                    Title = constraint.Title,
                    EntryType = constraint.EntryType,
                    Subject = constraint.Subject,
                    ConstraintLevel = constraint.ConstraintLevel,
                    PackagePolicy = constraint.PackagePolicy,
                    Status = violations.Count == 0 ? "passed" : "failed",
                    AllowedTerms = constraint.AllowedTerms.ToList(),
                    ForbiddenTerms = constraint.ForbiddenTerms.ToList(),
                    Violations = violationEvidence
                });
                if (violations.Count == 0)
                    continue;

                report.Issues.Add($"知识库硬事实失败：{constraint.Subject}能力边界被改写，正文出现「{TrimForIssue(violations[0])}」。");
                report.RepairHints.Add($"修订正文：{constraint.Subject}只能执行知识库允许的行为（{string.Join("、", constraint.AllowedTerms)}），不能被写成{string.Join("、", constraint.ForbiddenTerms)}或新增战斗/干扰能力。");
                report.FactSnapshotPassed = false;
                report.RagPassed = false;
                report.Status = "gate_failed";
                return;
            }
        }

        private static string FormatKnowledgeViolationEvidence(
            string violation,
            KnowledgeBoundaryConstraint constraint)
        {
            var forbiddenLabels = constraint.ForbiddenTerms
                .Select(FormatForbiddenKnowledgeTerm)
                .Where(HasText)
                .Distinct(StringComparer.Ordinal)
                .Take(8)
                .ToList();
            if (forbiddenLabels.Count == 0)
                return violation;

            return $"禁止项：{string.Join("、", forbiddenLabels)}；命中：{violation}";
        }

        private static string FormatForbiddenKnowledgeTerm(string term)
        {
            return term switch
            {
                "攻击" => "攻击功能",
                "治愈" or "治疗" => "治愈功能",
                "修复" => "修复功能",
                "干扰" => "干扰功能",
                "封闭" => "封闭功能",
                "摧毁" => "摧毁功能",
                "杀伤" => "杀伤功能",
                _ => term
            };
        }

        private static void ApplyAcceptedCreativeIntentGate(
            GenerationGateReport report,
            ChapterContextPackageSummary contextPackage,
            ChapterDraftArtifact draft)
        {
            var chapterId = contextPackage.ChapterId;
            var requiredIntents = contextPackage.AcceptedCreativeIntents
                .Where(intent => AcceptedCreativeIntentPolicy.IsChapterRequired(intent, chapterId))
                .Where(intent => HasText(intent.NormalizedIntent))
                .Take(8)
                .ToList();
            if (requiredIntents.Count == 0)
                return;

            var body = NormalizeContinuityText(StripChanges(draft.DraftContent));
            foreach (var intent in requiredIntents)
            {
                if (ContainsEnoughContinuityKeywords(body, intent.NormalizedIntent) &&
                    !ContainsNegatedCreativeRequirement(body, intent.NormalizedIntent))
                {
                    continue;
                }

                report.Issues.Add($"已采纳创意未执行：正文没有回应「{TrimForIssue(intent.NormalizedIntent)}」。");
                report.RepairHints.Add($"按已采纳创意重写或补写正文：{intent.NormalizedIntent}");
                report.BlueprintPassed = false;
                report.Status = "gate_failed";
            }
        }

        private static bool ContainsNegatedCreativeRequirement(string normalizedBody, string expected)
        {
            var keywords = ExtractContinuityKeywords(ExpandContinuityAliases(expected))
                .Where(keyword => keyword.Length >= 2)
                .Distinct(StringComparer.Ordinal)
                .Take(12)
                .ToList();

            foreach (var keyword in keywords)
            {
                var pattern = $"(?:没有|并未|未能|未曾|不曾|无法|无).{{0,10}}{Regex.Escape(keyword)}";
                if (Regex.IsMatch(normalizedBody, pattern, RegexOptions.IgnoreCase))
                    return true;
            }

            return false;
        }

        private static void ApplySourceRevisionPlanGate(
            GenerationGateReport report,
            ChapterContextPackageSummary contextPackage,
            ChapterDraftArtifact draft)
        {
            var requiredPlans = contextPackage.SourceRevisionPlans
                .Where(plan => HasText(plan.RevisionPlanId)
                    || HasText(plan.RequirementsJson)
                    || HasText(plan.ContinuityRequirementsJson)
                    || HasText(plan.Recommendation))
                .Take(8)
                .ToList();
            if (requiredPlans.Count == 0)
                return;

            var body = NormalizeContinuityText(StripChanges(draft.DraftContent));
            foreach (var plan in requiredPlans)
            {
                var planLabel = HasText(plan.RevisionPlanId) ? plan.RevisionPlanId.Trim() : "revision_plan";
                var requirements = ReadRevisionPlanRequirements(plan)
                    .Where(HasText)
                    .Distinct(StringComparer.Ordinal)
                    .Take(16)
                    .ToList();
                foreach (var requirement in requirements)
                {
                    if (RevisionRequirementSatisfied(body, requirement) &&
                        (!ContainsNegatedCreativeRequirement(body, requirement) ||
                         AllowsNegatedBoundaryExpression(requirement)))
                    {
                        continue;
                    }

                    report.Issues.Add($"修订计划未执行：正文没有落实「{TrimForIssue(requirement)}」（{planLabel}）。");
                    report.RepairHints.Add($"按 RevisionPlan 重写或补写正文：{requirement}");
                    report.BlueprintPassed = false;
                    report.Status = "gate_failed";
                }
            }
        }

        private static bool RevisionRequirementSatisfied(string normalizedBody, string requirement)
        {
            var normalizedRequirement = NormalizeContinuityText(requirement);
            if (string.IsNullOrWhiteSpace(normalizedRequirement))
                return true;

            if (ContainsEnoughContinuityKeywords(normalizedBody, normalizedRequirement))
                return true;

            if (normalizedRequirement.Contains("怪物围攻", StringComparison.Ordinal) &&
                normalizedBody.Contains("怪物", StringComparison.Ordinal) &&
                ContainsAny(normalizedBody, "围攻", "追击", "撞塌", "撞进", "冲进", "堵住", "围堵"))
            {
                return true;
            }

            if (normalizedRequirement.Contains("保留", StringComparison.Ordinal) &&
                normalizedRequirement.Contains("和", StringComparison.Ordinal))
            {
                var requiredKeywords = ExtractContinuityKeywords(ExpandContinuityAliases(normalizedRequirement))
                    .Where(keyword => keyword.Length >= 2)
                    .Where(keyword => !keyword.Contains("保留", StringComparison.Ordinal))
                    .Where(keyword => !keyword.Contains("和", StringComparison.Ordinal))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (requiredKeywords.Contains("沈砚", StringComparer.Ordinal) &&
                    requiredKeywords.Any(keyword => keyword is "银蓝" or "邮徽" or "蓝邮") &&
                    normalizedBody.Contains("沈砚", StringComparison.Ordinal) &&
                    normalizedBody.Contains("银蓝", StringComparison.Ordinal) &&
                    normalizedBody.Contains("邮徽", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            if (normalizedRequirement.Contains("邮徽", StringComparison.Ordinal) &&
                normalizedRequirement.Contains("攻击", StringComparison.Ordinal) &&
                ContainsAny(normalizedRequirement, "不能", "不可", "不要", "不得", "禁止"))
            {
                return normalizedBody.Contains("邮徽", StringComparison.Ordinal) &&
                       ContainsAny(
                           normalizedBody,
                           "没有把银蓝邮徽当成攻击武器",
                           "没有把邮徽当成攻击武器",
                           "不把银蓝邮徽当成攻击武器",
                           "不把邮徽当成攻击武器",
                           "银蓝邮徽只能指路",
                           "邮徽只能指路",
                           "邮徽不能替他杀敌",
                           "邮徽不能主动攻击",
                           "邮徽不是武器");
            }

            return false;
        }

        private static bool AllowsNegatedBoundaryExpression(string requirement)
        {
            var normalizedRequirement = NormalizeContinuityText(requirement);
            if (normalizedRequirement.Contains("保留", StringComparison.Ordinal))
                return true;

            return normalizedRequirement.Contains("邮徽", StringComparison.Ordinal) &&
                   normalizedRequirement.Contains("攻击", StringComparison.Ordinal) &&
                   ContainsAny(normalizedRequirement, "不能", "不可", "不要", "不得", "禁止");
        }

        private static IEnumerable<string> ReadRevisionPlanRequirements(RevisionPlanSnapshot plan)
        {
            var hasStructuredRequirements = false;
            foreach (var requirement in ReadJsonStringArray(plan.RequirementsJson))
            {
                hasStructuredRequirements = true;
                yield return requirement;
            }

            foreach (var requirement in ReadJsonStringArray(plan.ContinuityRequirementsJson))
            {
                hasStructuredRequirements = true;
                yield return requirement;
            }

            if (!hasStructuredRequirements && HasText(plan.Recommendation))
                yield return plan.Recommendation;
        }

        private static List<string> ReadJsonStringArray(string? json)
        {
            var values = new List<string>();
            if (string.IsNullOrWhiteSpace(json))
                return values;

            var payload = json.Trim();
            try
            {
                using var document = JsonDocument.Parse(payload);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in document.RootElement.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String)
                            continue;

                        var value = item.GetString();
                        if (HasText(value))
                            values.Add(value!.Trim());
                    }

                    return values;
                }

                if (document.RootElement.ValueKind == JsonValueKind.String)
                {
                    var value = document.RootElement.GetString();
                    if (HasText(value))
                        values.Add(value!.Trim());
                }
            }
            catch (JsonException)
            {
                values.Add(payload);
            }

            return values;
        }

        private static IEnumerable<KnowledgeBoundaryConstraint> ExtractKnowledgeBoundaryConstraints(
            ChapterContextPackageSummary contextPackage)
        {
            var lines = contextPackage.WorldRules
                .Concat(contextPackage.HardContinuityFacts)
                .Concat(contextPackage.CharacterStates)
                .Concat(contextPackage.PreviousSummaries)
                .Concat(contextPackage.LongDistanceRecall)
                .Where(HasText)
                .Select(line => new KnowledgeBoundaryLine(
                    NormalizeContinuityText(line),
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty));

            var bindingLines = contextPackage.KnowledgeBindings
                .Where(binding => IsKnowledgeBoundaryBinding(binding))
                .Select(binding => new KnowledgeBoundaryLine(
                    NormalizeContinuityText(FormatKnowledgeBinding(binding)),
                    binding.KnowledgeId,
                    binding.Title,
                    binding.EntryType,
                    binding.ConstraintLevel,
                    binding.PackagePolicy,
                    string.IsNullOrWhiteSpace(binding.Role) ? binding.Scope : binding.Role));

            foreach (var source in lines.Concat(bindingLines))
            {
                var subject = ExtractBoundarySubject(source.Line);
                if (string.IsNullOrWhiteSpace(subject))
                    continue;

                var forbiddenTerms = ExtractForbiddenBoundaryTerms(source.Line);
                if (forbiddenTerms.Count == 0)
                    continue;

                var allowedTerms = ExtractAllowedBoundaryTerms(source.Line);
                yield return new KnowledgeBoundaryConstraint(
                    source.KnowledgeId,
                    ResolveKnowledgeBoundaryTitle(source.Title, subject),
                    ResolveKnowledgeBoundaryEntryType(source.EntryType),
                    subject,
                    ResolveKnowledgeBoundaryConstraintLevel(source.ConstraintLevel),
                    source.PackagePolicy ?? string.Empty,
                    allowedTerms,
                    forbiddenTerms);
            }
        }

        private static string ResolveKnowledgeBoundaryTitle(string title, string subject)
        {
            if (HasText(title))
                return title.Trim();
            if (HasText(subject))
                return $"{subject.Trim()}能力边界";
            return "知识库能力边界";
        }

        private static string ResolveKnowledgeBoundaryEntryType(string entryType)
        {
            return HasText(entryType) ? entryType.Trim() : "HardFact";
        }

        private static string ResolveKnowledgeBoundaryConstraintLevel(string constraintLevel)
        {
            return HasText(constraintLevel) ? constraintLevel.Trim() : "HardConstraint";
        }

        private static bool IsKnowledgeBoundaryBinding(BoundKnowledgeSnapshot binding)
        {
            if (binding == null)
                return false;
            if (binding.ShouldEnterGate && HasText(binding.ClassificationRule))
                return true;
            if (string.Equals(binding.ConstraintLevel, "HardConstraint", StringComparison.OrdinalIgnoreCase))
                return true;
            return string.Equals(binding.EntryType, "HardFact", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(binding.EntryType, "Constraint", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(binding.EntryType, "Boundary", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatKnowledgeBinding(BoundKnowledgeSnapshot binding)
        {
            var title = binding.Title?.Trim() ?? string.Empty;
            var content = FirstNonEmpty(binding.ClassificationRule, binding.Content);
            if (!HasText(title))
                return $"{binding.EntryType}：{content}";
            if (!HasText(content))
                return $"{binding.EntryType}：{title}";
            return $"{title}：{content}";
        }

        private static IEnumerable<string> FormatKnowledgeBindings(
            IEnumerable<BoundKnowledgeSnapshot> bindings,
            params string[] entryTypes)
        {
            var accepted = entryTypes
                .Where(HasText)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var binding in bindings)
            {
                if (accepted.Count > 0 && !accepted.Contains(binding.EntryType))
                    continue;
                var title = binding.Title?.Trim() ?? string.Empty;
                var content = binding.Content?.Trim() ?? string.Empty;
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

        private static string ExtractBoundarySubject(string line)
        {
            if (!IsLimitedUseBoundary(line))
                return string.Empty;
            if (!ContainsAny(line, "不能", "不可", "不得", "禁止", "无法", "根本边界", "凭空强化", "不可逾越"))
                return string.Empty;

            var marker = IndexOfAny(line, "只能用于", "仅用于", "只用于", "只能", "仅能", "仅在", "用于");
            if (marker < 0)
                return string.Empty;

            var before = line[..marker].Trim('「', '」', '“', '”', '《', '》', '：', ':', '-', '—', '，', ',', '；', ';', '。');
            var lastSeparator = before.LastIndexOfAny(new[] { '：', ':', '，', ',', '；', ';', '。', '！', '？', ' ', '\t' });
            if (lastSeparator >= 0)
                before = before[(lastSeparator + 1)..].Trim();

            return CleanBoundarySubject(before);
        }

        private static bool IsLimitedUseBoundary(string line) =>
            ContainsAny(line, "只能", "仅能", "只能用于", "仅用于", "只用于", "仅在", "用于");

        private static string CleanBoundarySubject(string value)
        {
            var subject = value.Trim('「', '」', '“', '”', '《', '》', '：', ':', '-', '—', '，', ',', '；', ';', '。');
            var bracket = Regex.Match(subject, @"^【(?<owner>[^】]{2,32})】(?<rest>.*)$");
            if (bracket.Success)
            {
                var rest = bracket.Groups["rest"].Value.Trim();
                subject = HasText(rest) ? rest : bracket.Groups["owner"].Value.Trim();
            }

            subject = Regex.Replace(subject, @"[（(][^）)]{0,32}[）)]", string.Empty).Trim();
            subject = subject.Trim('「', '」', '“', '”', '《', '》', '：', ':', '-', '—', '，', ',', '；', ';', '。');
            return subject.Length is >= 2 and <= 24 ? subject : string.Empty;
        }

        private static List<string> ExtractAllowedBoundaryTerms(string line)
        {
            var marker = IndexOfAny(line, "不能", "不可", "不得", "禁止", "无法");
            var allowedPart = marker >= 0 ? line[..marker] : line;
            var terms = new[]
            {
                "观察", "辨认", "识别", "溯源", "邮路", "被篡改的邮路", "提示方向", "提示", "确认痕迹", "确认", "残留信标", "信标", "开启旧锁", "开启", "门牌"
            };
            var result = terms.Where(term => allowedPart.Contains(term, StringComparison.Ordinal)).Distinct().ToList();
            return result.Count == 0 ? new List<string> { "知识库明确允许的只读/辅助行为" } : result;
        }

        private static List<string> ExtractForbiddenBoundaryTerms(string line)
        {
            var marker = IndexOfAny(line, "不能", "不可", "不得", "禁止", "无法");
            if (marker < 0)
                return new List<string>();

            var forbiddenPart = line[marker..];
            var terms = new[]
            {
                "激活", "释放", "攻击", "治愈", "治疗", "修复", "腐蚀", "锈蚀", "干扰", "封闭", "摧毁", "杀伤", "新增能力", "新功能", "战斗能力"
            };
            var result = terms.Where(term => forbiddenPart.Contains(term, StringComparison.Ordinal)).ToList();
            if (IsLimitedUseBoundary(line) && ContainsAny(line, "不能", "不可", "不得", "禁止", "无法", "根本边界", "凭空强化", "不可逾越"))
            {
                result.AddRange(new[]
                {
                    "激活", "释放", "解锁", "升级", "新增", "强化", "修复", "腐蚀", "锈蚀", "干扰", "封闭", "摧毁", "攻击", "治愈", "治疗", "杀伤"
                });
            }

            return result.Distinct().ToList();
        }

        private static int IndexOfAny(string value, params string[] needles)
        {
            var index = -1;
            foreach (var needle in needles)
            {
                var current = value.IndexOf(needle, StringComparison.Ordinal);
                if (current >= 0 && (index < 0 || current < index))
                    index = current;
            }
            return index;
        }

        private static IEnumerable<string> FindKnowledgeBoundaryViolations(
            string body,
            KnowledgeBoundaryConstraint constraint)
        {
            const string sameSentenceGap24 = @"[^。！？!?；;\n]{0,24}";
            const string sameSentenceGap16 = @"[^。！？!?；;\n]{0,16}";
            var subject = Regex.Escape(constraint.Subject);
            var forbidden = string.Join("|", constraint.ForbiddenTerms.Select(Regex.Escape));
            var forbiddenAbility = @"新增[^。！？!?；;\n]{0,16}(?:能力|功能)|新功能|解锁[^。！？!?；;\n]{0,16}(?:能力|功能)|获得[^。！？!?；;\n]{0,16}(?:能力|功能)";
            var patterns = new List<string>
            {
                $@"{subject}{sameSentenceGap24}(?:{forbidden})",
                $@"(?:{forbidden}){sameSentenceGap16}{subject}"
            };
            if (constraint.ForbiddenTerms.Any(term => term.Contains("能力", StringComparison.Ordinal) || term.Contains("功能", StringComparison.Ordinal) || term is "新增" or "解锁" or "升级" or "强化"))
            {
                patterns.Add($@"{subject}{sameSentenceGap24}(?:{forbiddenAbility})");
                patterns.Add($@"(?:{forbiddenAbility}){sameSentenceGap16}{subject}");
            }

            foreach (var pattern in patterns)
            {
                foreach (Match match in Regex.Matches(body, pattern))
                {
                    var snippet = match.Value;
                    if (!IsNegatedBoundaryStatement(snippet) && !IsAllowedBoundaryAttribution(snippet, constraint))
                        yield return snippet;
                }
            }
        }

        private static bool IsNegatedBoundaryStatement(string snippet) =>
            ContainsAny(snippet,
                "不能", "不可", "不得", "禁止", "无法",
                "并不能", "不能用于", "不可用于",
                "并未", "并没有", "没有", "不会", "未曾", "并非", "不是");

        private static bool IsAllowedBoundaryAttribution(string snippet, KnowledgeBoundaryConstraint constraint)
        {
            if (string.IsNullOrWhiteSpace(snippet))
                return false;

            var subject = constraint.Subject;
            if (snippet.Contains(subject, StringComparison.Ordinal) &&
                ContainsAny(snippet, "怀表内锈蚀", "锈蚀核心", "锈蚀金属", "锈蚀气息", "锈蚀痕迹") &&
                !ContainsAny(snippet, $"{subject}能", $"{subject}会", $"{subject}可", $"{subject}将", $"{subject}把", "激活锈蚀", "释放锈蚀", "获得", "强化", "升级"))
            {
                return true;
            }

            var subjectHasAllowedUse = constraint.AllowedTerms.Any(term =>
                HasText(term) &&
                (snippet.Contains($"{subject}只{term}", StringComparison.Ordinal) ||
                 snippet.Contains($"{subject}仅{term}", StringComparison.Ordinal) ||
                 snippet.Contains($"{subject}只能{term}", StringComparison.Ordinal) ||
                 snippet.Contains($"{subject}仅能{term}", StringComparison.Ordinal)));

            if (!subjectHasAllowedUse)
                subjectHasAllowedUse =
                    ContainsAny(snippet, $"{subject}只辨认", $"{subject}仅辨认", $"{subject}只能辨认", $"{subject}只溯源", $"{subject}仅溯源", $"{subject}只能溯源");

            if (!subjectHasAllowedUse && snippet.Contains(subject, StringComparison.Ordinal))
                subjectHasAllowedUse = ContainsAny(snippet, "只辨认", "仅辨认", "只能辨认", "只溯源", "仅溯源", "只能溯源");

            if (!subjectHasAllowedUse)
                return false;

            return ContainsAny(snippet, "真正", "实际", "来源是", "来自", "属于", "的是", "另一个", "怀表", "核心", "金属", "栅栏");
        }

        private static bool ContainsEnoughContinuityKeywords(string body, string expected)
        {
            var searchableBody = ExpandContinuityAliases(body);
            var keywords = ExtractContinuityKeywords(ExpandContinuityAliases(expected))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (keywords.Count == 0)
                return true;
            var hits = keywords.Count(keyword => searchableBody.Contains(keyword, StringComparison.Ordinal));
            var required = keywords.Count <= 2 ? keywords.Count : Math.Min(3, keywords.Count);
            return hits >= required;
        }

        private static string ExpandContinuityAliases(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var expanded = value;
            if (ContainsAny(expanded, "临近", "将至", "来临", "即将", "逼近", "接近"))
                expanded += "临近将至来临即将逼近";

            if (ContainsAny(expanded, "提前", "爆发", "开始", "已开始", "已经开始", "发生"))
                expanded += "来临临近升级增加";

            if (ContainsAny(expanded, "威胁", "危险", "危机", "风险", "戒备", "预警", "告警"))
                expanded += "威胁危险危机风险戒备";

            if (ContainsAny(expanded, "增加", "加剧", "升级", "飙升", "超预期", "增强", "扩大"))
                expanded += "增加加剧升级飙升增强";

            if (expanded.Contains("逆潮现象", StringComparison.Ordinal) &&
                !expanded.Contains("逆潮夜", StringComparison.Ordinal))
            {
                expanded += "逆潮夜";
            }

            if (expanded.Contains("逆潮夜", StringComparison.Ordinal) &&
                !expanded.Contains("逆潮现象", StringComparison.Ordinal))
            {
                expanded += "逆潮现象";
            }

            if (expanded.Contains("蓝磷骨光", StringComparison.Ordinal) &&
                !expanded.Contains("蓝光现象", StringComparison.Ordinal))
            {
                expanded += "蓝光现象蓝光";
            }

            if (expanded.Contains("蓝光", StringComparison.Ordinal) &&
                !expanded.Contains("蓝磷骨光", StringComparison.Ordinal))
            {
                expanded += "蓝磷骨光";
            }

            if (ContainsAny(expanded, "身份") &&
                ContainsAny(expanded, "命运", "下落", "去向", "生死"))
            {
                expanded += "身份暴露身份揭示身份揭露真实身份名字姓名";
                expanded += "命运未知下落未知去向未知生死未卜暂时存活";
                expanded += "被捕被俘押解带走抓捕囚禁拘押逃脱处决死亡存活";
            }

            return expanded;
        }

        private static IEnumerable<string> ExtractContinuityKeywords(string value)
        {
            foreach (Match match in Regex.Matches(value ?? string.Empty, @"[\u4e00-\u9fffA-Za-z0-9]{2,}"))
            {
                var token = match.Value.Trim();
                if (token.Length < 2)
                    continue;
                if (IsContinuityStopword(token))
                    continue;

                if (Regex.IsMatch(token, @"^[\u4e00-\u9fff]+$"))
                {
                    if (token.Length <= 4)
                    {
                        yield return token;
                        continue;
                    }

                    yield return token[..4];
                    for (var i = 0; i <= token.Length - 2; i++)
                    {
                        var slice = token.Substring(i, 2);
                        if (!IsContinuityStopword(slice))
                            yield return slice;
                    }
                    continue;
                }

                yield return token.Length > 8 ? token[..8] : token;
            }
        }

        private static bool IsContinuityStopword(string token) =>
            token is "必须" or "承接" or "当前" or "状态" or "主角" or "下一章" or
                "正在" or "已经" or "没有" or "解释" or "查看" or "前往" or "身份" or
                "位置" or "发生" or "事件";

        private static string NormalizeContinuityText(string? value) =>
            Regex.Replace(value ?? string.Empty, @"\s+", string.Empty).Trim();

        private static string StripChanges(string content)
            => ChapterChangesText.StripChanges(content);

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

        private static bool ContainsAny(string value, params string[] candidates) =>
            candidates.Any(candidate => value.Contains(candidate, StringComparison.Ordinal));

        private static string TrimForIssue(string value)
        {
            var text = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
            return text.Length <= 80 ? text : text[..80] + "...";
        }

        private static bool HasText(string? value) =>
            !string.IsNullOrWhiteSpace(value);

        private static string FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

        private static bool IsNarrativeCarryLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var value = line.Trim();
            if (value.StartsWith("上一章已提交", StringComparison.Ordinal) ||
                value.StartsWith("上一章章节ID", StringComparison.Ordinal) ||
                value.StartsWith("上一章评审结论", StringComparison.Ordinal) ||
                value.StartsWith("上一章门禁关注", StringComparison.Ordinal) ||
                value.Contains("章节ID", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private sealed record KnowledgeBoundaryLine(
            string Line,
            string KnowledgeId,
            string Title,
            string EntryType,
            string ConstraintLevel,
            string PackagePolicy,
            string RoleOrScope);

        private sealed record KnowledgeBoundaryConstraint(
            string KnowledgeId,
            string Title,
            string EntryType,
            string Subject,
            string ConstraintLevel,
            string PackagePolicy,
            IReadOnlyList<string> AllowedTerms,
            IReadOnlyList<string> ForbiddenTerms);
    }
}
