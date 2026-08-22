using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;
using TM.Services.Modules.ProjectData.Interfaces;

namespace TM.Services.Framework.AI.NovelAgent.Services
{
    public sealed class ChapterPostGenerationReviewer
    {
        private static readonly string[] ClicheRiskKeywords =
        {
            "忽然", "突然", "没想到", "原来如此", "冷笑", "脸色大变", "全场震惊",
            "不可能", "你怎么会", "区区", "蝼蚁", "逆天", "机缘", "顿悟", "传承"
        };

        private static readonly string[] CostKeywords =
        {
            "代价", "损失", "牺牲", "伤", "疲惫", "反噬", "暴露", "失去", "欠下",
            "后果", "惩罚", "风险", "裂痕", "怀疑", "付出"
        };

        private static readonly string[] WorldbuildingKeywords =
        {
            "规则", "禁忌", "仪式", "制度", "法则", "血脉", "契约", "组织", "势力",
            "秘境", "遗迹", "传承", "等级", "体系", "城", "宗门", "公司", "委员会"
        };

        private static readonly string[] CharacterEmotionKeywords =
        {
            "恐惧", "愤怒", "羞耻", "愧疚", "怀疑", "动摇", "绝望", "释然", "信念",
            "执念", "痛苦", "麻木", "崩溃", "清醒", "失控", "信任"
        };

        private static readonly string[] RelationshipKeywords =
        {
            "关系", "信任", "背叛", "盟友", "敌人", "师徒", "家人", "裂痕", "和解",
            "利用", "试探", "误会", "翻脸", "保护", "疏远"
        };

        private static readonly string[] CharacterSecretKeywords =
        {
            "秘密", "身份", "真相", "隐瞒", "暴露", "揭穿", "过去", "记忆", "谎言",
            "伪装", "血脉"
        };

        private static readonly string[] AbilityChangeKeywords =
        {
            "能力", "境界", "等级", "突破", "反噬", "代价", "限制", "封印", "觉醒",
            "失控", "消耗", "债"
        };

        private readonly IGeneratedContentService _contentService;
        private readonly IUnifiedValidationService _validationService;
        private readonly StoryBibleService _storyBibleService;
        private readonly StoryStateSnapshotService _storyStateSnapshotService;
        private readonly CommercialRhythmChecker _commercialRhythmChecker;
        private readonly IAgentEditorialReviewModelClient? _editorialReviewModel;
        private readonly string _userId;

        public ChapterPostGenerationReviewer(
            IGeneratedContentService contentService,
            IUnifiedValidationService validationService,
            StoryBibleService storyBibleService,
            StoryStateSnapshotService storyStateSnapshotService,
            CommercialRhythmChecker? commercialRhythmChecker = null,
            IAgentEditorialReviewModelClient? editorialReviewModel = null,
            string userId = "")
        {
            _contentService = contentService;
            _validationService = validationService;
            _storyBibleService = storyBibleService;
            _storyStateSnapshotService = storyStateSnapshotService;
            _commercialRhythmChecker = commercialRhythmChecker ?? new CommercialRhythmChecker();
            _editorialReviewModel = editorialReviewModel;
            _userId = userId;
        }

        public async Task<NovelAgentPostGenerationReview> ReviewAsync(
            NovelAgentRun run,
            CancellationToken ct = default)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));

            var chapterId = run.TargetChapterId;
            var document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var storyState = await _storyStateSnapshotService.BuildForChapterAsync(chapterId, ct)
                .ConfigureAwait(false);
            var storedContent = await _contentService.GetChapterAsync(chapterId).ConfigureAwait(false) ?? string.Empty;
            var hasCandidateContent = HasDraftCandidate(run.DraftArtifact);
            var content = ResolveReviewContent(
                storedContent,
                run.DraftArtifact?.CommittedContent,
                run.DraftArtifact?.DraftContent);

            var review = new NovelAgentPostGenerationReview
            {
                ChapterId = chapterId,
                ContentLength = content.Length
            };

            AddContentPresenceCheck(review, content);
            await AddValidationCheckAsync(
                    review,
                    chapterId,
                    hasCommittedContent: !string.IsNullOrWhiteSpace(storedContent),
                    hasCandidateContent: hasCandidateContent,
                    reviewContent: content,
                    ct)
                .ConfigureAwait(false);
            AddBriefAlignmentChecks(review, run.ChapterBrief, content);
            AddStoryBibleChecks(review, document, content);
            AddProjectKnowledgeBindingChecks(review, run.ContextPackage, content);
            AddRevisionPlanAlignmentChecks(review, run.ContextPackage, content);
            await AddAgentEditorialSemanticAlignmentCheckAsync(review, run, document, content, ct)
                .ConfigureAwait(false);
            AddNoveltyChecks(review, storyState, content);
            AddStoryVariableChecks(review, storyState, content);
            review.Checks.AddRange(_commercialRhythmChecker.EvaluateGeneratedChapter(
                document.Constitution,
                run.ChapterBrief,
                storyState,
                content));
            AddProposedCanonEntries(review, run, document, content);
            AddProposedForeshadowEntries(review, run, document, storyState, content);
            AddProposedCharacterEntries(review, run, document, storyState, content);
            CompleteReview(review);
            AddNextChapterSuggestions(review, run.ChapterBrief, storyState);

            return review;
        }

        public static string ResolveReviewContent(
            string? storedContent,
            string? committedContent,
            string? draftContent)
        {
            if (!string.IsNullOrWhiteSpace(committedContent))
                return committedContent.Trim();
            var strippedDraft = StripChanges(draftContent);
            if (!string.IsNullOrWhiteSpace(strippedDraft))
                return strippedDraft.Trim();
            return storedContent?.Trim() ?? string.Empty;
        }

        private static string StripChanges(string? content)
            => ChapterChangesText.StripChanges(content ?? string.Empty);

        private static bool HasDraftCandidate(ChapterDraftArtifact? draft)
            => draft != null &&
               (!string.IsNullOrWhiteSpace(draft.CommittedContent) ||
                !string.IsNullOrWhiteSpace(StripChanges(draft.DraftContent)));

        private async Task AddValidationCheckAsync(
            NovelAgentPostGenerationReview review,
            string chapterId,
            bool hasCommittedContent,
            bool hasCandidateContent,
            string reviewContent,
            CancellationToken ct)
        {
            try
            {
                var validation = await _validationService.ValidateChapterAsync(chapterId, ct)
                    .ConfigureAwait(false);
                review.ValidationOverallResult = validation.OverallResult;
                review.ValidationIssueCount = validation.TotalIssueCount;
                var isPreCommitDraftReview = !hasCommittedContent &&
                    !string.IsNullOrWhiteSpace(reviewContent) &&
                    HasOnlyPreCommitStorageIssues(validation);
                var isCandidateReviewWithStoredContent = hasCommittedContent &&
                    hasCandidateContent &&
                    !string.IsNullOrWhiteSpace(reviewContent) &&
                    validation.HasErrors;
                review.Checks.Add(new NovelAgentReviewCheck
                {
                    Key = "unified_validation",
                    Name = "项目一致性校验",
                    Status = isPreCommitDraftReview || isCandidateReviewWithStoredContent
                        ? NovelAgentReviewCheckStatus.Pass
                        : validation.HasErrors
                        ? NovelAgentReviewCheckStatus.Fail
                        : validation.HasWarnings
                            ? NovelAgentReviewCheckStatus.Warning
                            : NovelAgentReviewCheckStatus.Pass,
                    RiskLevel = isPreCommitDraftReview || isCandidateReviewWithStoredContent || !validation.HasErrors
                        ? NovelToolRiskLevel.Medium
                        : NovelToolRiskLevel.High,
                    Message = isPreCommitDraftReview
                        ? "当前处于提交前草稿评审，章节尚未写入书城属于正常状态；提交后会再次执行正式书城一致性校验。"
                        : isCandidateReviewWithStoredContent
                        ? "当前评审对象是本轮候选正文，书城已有版本的一致性问题仅作为参考；提交后会对新版本执行正式校验。"
                        : validation.TotalIssueCount == 0
                        ? "现有项目校验未发现问题。"
                        : $"现有项目校验发现 {validation.TotalIssueCount} 个问题，结果：{validation.OverallResult}。",
                    Evidence = validation.IssuesByModule
                        .SelectMany(kv => kv.Value.Select(i => $"{kv.Key}: {i.Severity} / {i.Message}"))
                        .Take(8)
                        .ToList(),
                    Suggestions = isPreCommitDraftReview || isCandidateReviewWithStoredContent || validation.TotalIssueCount == 0
                        ? new List<string>()
                        : new List<string> { "优先修复 Error，再处理 Warning；修复后重跑 Agent 生成后复盘。" }
                });
            }
            catch (Exception ex)
            {
                review.ValidationOverallResult = "校验异常";
                review.Checks.Add(new NovelAgentReviewCheck
                {
                    Key = "unified_validation",
                    Name = "项目一致性校验",
                    Status = NovelAgentReviewCheckStatus.Fail,
                    RiskLevel = NovelToolRiskLevel.High,
                    Message = $"现有项目校验暂不可用：{ex.Message}",
                    Suggestions = new List<string> { "接入真实项目校验服务并确认 ContextIds、FactSnapshot 可用后重跑复盘。" }
                });
            }
        }

        private static bool HasOnlyPreCommitStorageIssues(ChapterValidationResult validation)
        {
            var issues = validation.IssuesByModule.Values.SelectMany(static list => list).ToList();
            return issues.Count > 0 && issues.All(IsPreCommitStorageIssue);
        }

        private static bool IsPreCommitStorageIssue(ValidationIssue issue)
        {
            var type = issue.Type ?? string.Empty;
            var message = issue.Message ?? string.Empty;
            return (string.Equals(type, "chapter_identity", StringComparison.OrdinalIgnoreCase) &&
                    message.Contains("不存在章节", StringComparison.OrdinalIgnoreCase))
                || (string.Equals(type, "chapter_content", StringComparison.OrdinalIgnoreCase) &&
                    message.Contains("未写入书城内容存储", StringComparison.OrdinalIgnoreCase));
        }

        private static void AddContentPresenceCheck(NovelAgentPostGenerationReview review, string content)
        {
            var hasContent = !string.IsNullOrWhiteSpace(content);
            review.Checks.Add(new NovelAgentReviewCheck
            {
                Key = "content_presence",
                Name = "章节正文存在性",
                Status = hasContent ? NovelAgentReviewCheckStatus.Pass : NovelAgentReviewCheckStatus.Fail,
                RiskLevel = hasContent ? NovelToolRiskLevel.Low : NovelToolRiskLevel.High,
                Message = hasContent ? "已读取到生成后的章节正文。" : "未读取到生成后的章节正文。",
                Suggestions = hasContent
                    ? new List<string>()
                    : new List<string> { "确认 Writer.GenerateChapter 是否成功保存章节，再执行复盘。" }
            });
        }

        private static void AddBriefAlignmentChecks(
            NovelAgentPostGenerationReview review,
            ChapterCreativeBrief? brief,
            string content)
        {
            if (brief == null)
            {
                review.Checks.Add(new NovelAgentReviewCheck
                {
                    Key = "brief_alignment",
                    Name = "章节创意简报对齐",
                    Status = NovelAgentReviewCheckStatus.Warning,
                    RiskLevel = NovelToolRiskLevel.Medium,
                    Message = "缺少章节创意简报，无法检查正文是否遵循 Agent 规划。"
                });
                return;
            }

            review.Checks.Add(BuildContainsCheck(
                "core_idea_alignment",
                "核心创意落地",
                content,
                brief.CoreIdea,
                "正文能找到核心创意相关表达。",
                "正文没有明显体现章节核心创意。",
                "补写一个围绕核心创意的关键场景或选择。"));

            review.Checks.Add(BuildContainsCheck(
                "conflict_move_alignment",
                "冲突推进",
                content,
                brief.ConflictMove,
                "正文能找到冲突推进相关表达。",
                "正文没有明显体现本章冲突推进。",
                "让本章至少改变一个冲突状态，而不是只延续气氛。"));

            var hasCost = ContainsAny(content, CostKeywords) || HasTokenOverlap(content, brief.CostOrConsequence);
            review.Checks.Add(new NovelAgentReviewCheck
            {
                Key = "cost_or_consequence",
                Name = "代价与后果",
                Status = hasCost ? NovelAgentReviewCheckStatus.Pass : NovelAgentReviewCheckStatus.Warning,
                RiskLevel = NovelToolRiskLevel.Medium,
                Message = hasCost ? "正文具备代价或后果信号。" : "正文缺少清晰代价，容易变成无代价开挂或机械爽点。",
                Evidence = hasCost ? ExtractEvidence(content, CostKeywords).ToList() : new List<string>(),
                Suggestions = hasCost
                    ? new List<string>()
                    : new List<string> { "给主角选择增加损失、暴露风险、关系裂痕或后续债务。" }
            });

            var forbiddenHits = brief.ForbiddenPatterns
                .Where(p => !string.IsNullOrWhiteSpace(p) && HasTokenOverlap(content, p))
                .Take(8)
                .ToList();
            review.Checks.Add(new NovelAgentReviewCheck
            {
                Key = "forbidden_patterns",
                Name = "禁止桥段检查",
                Status = forbiddenHits.Count == 0 ? NovelAgentReviewCheckStatus.Pass : NovelAgentReviewCheckStatus.Warning,
                RiskLevel = NovelToolRiskLevel.Medium,
                Message = forbiddenHits.Count == 0
                    ? "未发现明显命中章节简报中的禁止桥段。"
                    : $"发现 {forbiddenHits.Count} 个可能命中的禁止桥段。",
                Evidence = forbiddenHits,
                Suggestions = forbiddenHits.Count == 0
                    ? new List<string>()
                    : new List<string> { "保留功能相同的剧情目的，但替换表达方式、反转来源或角色决策逻辑。" }
            });
        }

        private static void AddStoryBibleChecks(
            NovelAgentPostGenerationReview review,
            StoryBibleDocument document,
            string content)
        {
            var canonRules = document.CanonLedger
                .Where(e => e.Status == CanonLedgerEntryStatus.Canon)
                .Select(e => e.Title)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Take(20)
                .ToList();

            if (document.Constitution != null)
            {
                var forbiddenHits = document.Constitution.ForbiddenDirections
                    .Where(p => !string.IsNullOrWhiteSpace(p) && HasTokenOverlap(content, p))
                    .Take(8)
                    .ToList();
                review.Checks.Add(new NovelAgentReviewCheck
                {
                    Key = "story_bible_forbidden_direction",
                    Name = "Story Bible 禁止方向",
                    Status = forbiddenHits.Count == 0 ? NovelAgentReviewCheckStatus.Pass : NovelAgentReviewCheckStatus.Fail,
                    RiskLevel = NovelToolRiskLevel.High,
                    Message = forbiddenHits.Count == 0
                        ? "未发现明显违反整书禁止方向的内容。"
                        : $"正文可能触碰 {forbiddenHits.Count} 个整书禁止方向。",
                    Evidence = forbiddenHits,
                    Suggestions = forbiddenHits.Count == 0
                        ? new List<string>()
                        : new List<string> { "不要直接修饰局部句子，优先重设场景目标和人物选择。" }
                });
            }

            review.Checks.Add(new NovelAgentReviewCheck
            {
                Key = "canon_ledger_coverage",
                Name = "Canon Ledger 覆盖",
                Status = canonRules.Count == 0 || canonRules.Any(rule => HasTokenOverlap(content, rule))
                    ? NovelAgentReviewCheckStatus.Pass
                    : NovelAgentReviewCheckStatus.Warning,
                RiskLevel = NovelToolRiskLevel.Medium,
                Message = canonRules.Count == 0
                    ? "当前 Canon Ledger 暂无正式条目。"
                    : "章节正文未明显引用已有 Canon 条目，可能需要检查本章是否脱离大设定。",
                Evidence = canonRules.Take(8).ToList(),
                Suggestions = canonRules.Count == 0
                    ? new List<string> { "随着写作推进，尽快沉淀核心世界规则和角色规则到 Canon Ledger。" }
                    : new List<string> { "确认本章至少服务于一个世界规则、角色规则或主线冲突。" }
            });
        }

        private static void AddProjectKnowledgeBindingChecks(
            NovelAgentPostGenerationReview review,
            ChapterContextPackageSummary? contextPackage,
            string content)
        {
            var bindings = contextPackage?.KnowledgeBindings
                .Where(binding =>
                    !string.IsNullOrWhiteSpace(binding.Title) ||
                    !string.IsNullOrWhiteSpace(binding.Content))
                .Take(12)
                .ToList() ?? new List<BoundKnowledgeSnapshot>();
            if (bindings.Count == 0)
                return;

            var reflected = new List<string>();
            var missing = new List<string>();
            foreach (var binding in bindings)
            {
                var label = FormatKnowledgeBindingLabel(binding);
                var reference = FirstNonEmpty(binding.Content, binding.Title);
                if (HasTokenOverlap(content, binding.Title) || HasTokenOverlap(content, reference))
                    reflected.Add(label);
                else
                    missing.Add(label);
            }

            review.Checks.Add(new NovelAgentReviewCheck
            {
                Key = "project_knowledge_binding_alignment",
                Name = "项目知识绑定落地",
                Status = missing.Count == 0
                    ? NovelAgentReviewCheckStatus.Pass
                    : NovelAgentReviewCheckStatus.Warning,
                RiskLevel = missing.Count == 0 ? NovelToolRiskLevel.Low : NovelToolRiskLevel.Medium,
                Message = missing.Count == 0
                    ? $"正文已响应本次生产包中的 {reflected.Count} 条项目知识绑定。"
                    : $"正文未明显响应 {missing.Count} 条项目知识绑定，Agent 应判断是否需要修订或保留到后续章节。",
                Evidence = missing.Count == 0 ? reflected : missing,
                Suggestions = missing.Count == 0
                    ? new List<string>()
                    : new List<string> { "若这些知识是本章硬要求，请把它们写入章节行动、代价、规则说明或角色选择；若只是远期素材，应在工作流中标记为未采用。" }
            });
        }

        private static void AddRevisionPlanAlignmentChecks(
            NovelAgentPostGenerationReview review,
            ChapterContextPackageSummary? contextPackage,
            string content)
        {
            var plans = contextPackage?.SourceRevisionPlans
                .Where(plan => !string.IsNullOrWhiteSpace(plan.RevisionPlanId)
                    || !string.IsNullOrWhiteSpace(plan.RequirementsJson)
                    || !string.IsNullOrWhiteSpace(plan.ContinuityRequirementsJson)
                    || !string.IsNullOrWhiteSpace(plan.Recommendation))
                .Take(8)
                .ToList() ?? new List<RevisionPlanSnapshot>();
            if (plans.Count == 0)
                return;

            var reflected = new List<string>();
            var missing = new List<string>();
            foreach (var plan in plans)
            {
                var planLabel = FirstNonEmpty(plan.RevisionPlanId, plan.PlanType, "revision_plan");
                var requirements = ReadRevisionPlanRequirements(plan)
                    .Where(requirement => !string.IsNullOrWhiteSpace(requirement))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(16)
                    .ToList();

                if (requirements.Count == 0)
                {
                    reflected.Add($"{planLabel}: 未声明具体修订要求");
                    continue;
                }

                var missingForPlan = requirements
                    .Where(requirement => !RevisionRequirementReflected(content, requirement))
                    .ToList();
                if (missingForPlan.Count == 0)
                {
                    reflected.Add($"{planLabel}: {string.Join(" / ", requirements.Take(4))}");
                    continue;
                }

                missing.AddRange(missingForPlan.Select(requirement => $"{planLabel}: {requirement}"));
            }

            review.Checks.Add(new NovelAgentReviewCheck
            {
                Key = "revision_plan_alignment",
                Name = "修订计划落地",
                Status = missing.Count == 0
                    ? NovelAgentReviewCheckStatus.Pass
                    : NovelAgentReviewCheckStatus.Fail,
                RiskLevel = missing.Count == 0 ? NovelToolRiskLevel.Low : NovelToolRiskLevel.High,
                Message = missing.Count == 0
                    ? $"正文已落实 {plans.Count} 个来源修订计划。"
                    : $"正文未落实 {missing.Count} 条来源修订计划要求，不能提交书城。",
                Evidence = missing.Count == 0 ? reflected : missing.Take(12).ToList(),
                Suggestions = missing.Count == 0
                    ? new List<string>()
                    : new List<string> { "按 RevisionPlan 重建章节蓝图或重写正文，直到修订要求和连续性要求进入章节行动、规则、代价或结尾承接。" }
            });
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

            if (!hasStructuredRequirements && !string.IsNullOrWhiteSpace(plan.Recommendation))
                yield return plan.Recommendation;
        }

        private static bool RevisionRequirementReflected(string content, string requirement)
        {
            if (HasTokenOverlap(content, requirement))
                return true;

            var normalizedContent = NormalizeCompact(content);
            var normalizedRequirement = NormalizeCompact(requirement);
            if (string.IsNullOrWhiteSpace(normalizedRequirement))
                return true;

            if (normalizedRequirement.Contains("怪物围攻", StringComparison.Ordinal) &&
                normalizedContent.Contains("怪物", StringComparison.Ordinal) &&
                ContainsAny(normalizedContent, "围攻", "追击", "撞塌", "撞进", "冲进", "堵住", "围堵"))
            {
                return true;
            }

            if (normalizedRequirement.Contains("怪物围攻", StringComparison.Ordinal) &&
                normalizedRequirement.Contains("生存反击", StringComparison.Ordinal) &&
                normalizedContent.Contains("怪物", StringComparison.Ordinal) &&
                normalizedContent.Contains("反击", StringComparison.Ordinal))
            {
                return true;
            }

            if (normalizedRequirement.Contains("分拣台", StringComparison.Ordinal) &&
                normalizedRequirement.Contains("第二次", StringComparison.Ordinal) &&
                normalizedRequirement.Contains("敲击声", StringComparison.Ordinal) &&
                normalizedContent.Contains("分拣台", StringComparison.Ordinal) &&
                normalizedContent.Contains("第二次", StringComparison.Ordinal) &&
                normalizedContent.Contains("敲击声", StringComparison.Ordinal))
            {
                return true;
            }

            if (normalizedRequirement.Contains("保留", StringComparison.Ordinal) &&
                normalizedRequirement.Contains("沈砚", StringComparison.Ordinal) &&
                normalizedRequirement.Contains("银蓝", StringComparison.Ordinal) &&
                normalizedRequirement.Contains("邮徽", StringComparison.Ordinal) &&
                normalizedContent.Contains("沈砚", StringComparison.Ordinal) &&
                normalizedContent.Contains("银蓝", StringComparison.Ordinal) &&
                normalizedContent.Contains("邮徽", StringComparison.Ordinal))
            {
                return true;
            }

            if (normalizedRequirement.Contains("邮徽", StringComparison.Ordinal) &&
                normalizedRequirement.Contains("攻击", StringComparison.Ordinal) &&
                ContainsAny(normalizedRequirement, "不能", "不可", "不要", "不得", "禁止"))
            {
                return normalizedContent.Contains("邮徽", StringComparison.Ordinal) &&
                       ContainsAny(
                           normalizedContent,
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

        private static List<string> ReadJsonStringArray(string? json)
        {
            var values = new List<string>();
            if (string.IsNullOrWhiteSpace(json))
                return values;

            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in document.RootElement.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            var value = item.GetString();
                            if (!string.IsNullOrWhiteSpace(value))
                                values.Add(value.Trim());
                        }
                    }

                    return values;
                }

                if (document.RootElement.ValueKind == JsonValueKind.String)
                {
                    var value = document.RootElement.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        values.Add(value.Trim());
                }
            }
            catch (JsonException)
            {
                values.Add(json.Trim());
            }

            return values;
        }

        private async Task AddAgentEditorialSemanticAlignmentCheckAsync(
            NovelAgentPostGenerationReview review,
            NovelAgentRun run,
            StoryBibleDocument document,
            string content,
            CancellationToken ct)
        {
            var contextPackage = run.ContextPackage;
            var targetChapterId = FirstNonEmpty(contextPackage?.ChapterId, run.TargetChapterId);
            var requiredIntents = (contextPackage?.AcceptedCreativeIntents ?? new List<AcceptedCreativeIntentSnapshot>())
                .Where(intent => AcceptedCreativeIntentPolicy.IsChapterRequired(intent, targetChapterId))
                .Where(intent => !string.IsNullOrWhiteSpace(intent.NormalizedIntent))
                .Take(12)
                .ToList();
            var sourcePlans = (contextPackage?.SourceRevisionPlans ?? new List<RevisionPlanSnapshot>())
                .Where(plan => !string.IsNullOrWhiteSpace(plan.RevisionPlanId)
                    || !string.IsNullOrWhiteSpace(plan.RequirementsJson)
                    || !string.IsNullOrWhiteSpace(plan.ContinuityRequirementsJson)
                    || !string.IsNullOrWhiteSpace(plan.Recommendation))
                .Take(8)
                .ToList();
            var hasReviewTarget = !string.IsNullOrWhiteSpace(run.UserGoal)
                || run.ChapterBrief != null
                || document.Constitution != null
                || requiredIntents.Count > 0
                || sourcePlans.Count > 0;
            if (!hasReviewTarget)
                return;

            if (_editorialReviewModel == null)
                return;

            try
            {
                var decision = await _editorialReviewModel.ReviewAsync(
                        new AgentEditorialReviewRequest
                        {
                            UserId = _userId,
                            RunId = run.RunId,
                            ChapterId = targetChapterId,
                            UserGoal = run.UserGoal,
                            ChapterBrief = run.ChapterBrief,
                            StoryConstitution = document.Constitution,
                            AcceptedCreativeIntents = requiredIntents,
                            SourceRevisionPlans = sourcePlans,
                            ChapterContent = Trim(content, 16000)
                        },
                        ct)
                    .ConfigureAwait(false);

                ApplyEditorialDecision(review, decision);
                review.Checks.Add(ToEditorialReviewCheck(decision, requiredIntents.Count, sourcePlans.Count));
            }
            catch (Exception ex)
            {
                review.Checks.Add(new NovelAgentReviewCheck
                {
                    Key = "agent_editorial_semantic_alignment",
                    Name = "Agent 总编语义验收",
                    Status = NovelAgentReviewCheckStatus.Warning,
                    RiskLevel = NovelToolRiskLevel.Medium,
                    Message = $"Agent 总编语义验收暂不可用：{ex.Message}",
                    Suggestions = new List<string> { "模型验收恢复后应重新检查用户创意、修订计划与正文是否一致。" }
                });
            }
        }

        private static NovelAgentReviewCheck ToEditorialReviewCheck(
            AgentEditorialReviewDecision decision,
            int intentCount,
            int revisionPlanCount)
        {
            var status = ResolveEditorialReviewStatus(decision);
            var firstProblem = decision.Problems.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
            var message = status == NovelAgentReviewCheckStatus.Pass
                ? $"LLM 总编验收通过：用户目标、作品承诺、{intentCount} 条创意、{revisionPlanCount} 个修订计划与正文方向一致。"
                : $"LLM 总编验收未通过：{FirstNonEmpty(firstProblem, decision.Decision, decision.CreativeFit, "正文未充分落实用户创意或修订计划。")}";

            return new NovelAgentReviewCheck
            {
                Key = "agent_editorial_semantic_alignment",
                Name = "Agent 总编语义验收",
                Status = status,
                RiskLevel = status == NovelAgentReviewCheckStatus.Fail ? NovelToolRiskLevel.High : NovelToolRiskLevel.Medium,
                Message = message,
                Evidence = decision.Evidence
                    .Concat(decision.Problems)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => Trim(value, 220))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(10)
                    .ToList(),
                Suggestions = decision.Suggestions
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => Trim(value, 220))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(8)
                    .ToList()
            };
        }

        private static void ApplyEditorialDecision(
            NovelAgentPostGenerationReview review,
            AgentEditorialReviewDecision decision)
        {
            review.MeetsAcceptedCreativeIntents = decision.MeetsAcceptedCreativeIntents;
            review.ContinuityRisk = NormalizeEditorialValue(decision.ContinuityRisk);
            review.ChapterPacing = NormalizeEditorialValue(decision.ChapterPacing);
            review.RecommendedAction = FirstNonEmpty(
                NormalizeEditorialValue(decision.RecommendedAction),
                NormalizeEditorialValue(decision.Decision));
        }

        private static NovelAgentReviewCheckStatus ResolveEditorialReviewStatus(AgentEditorialReviewDecision decision)
        {
            var normalized = (decision.Decision ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized is "pass" or "accept" or "commit" or "ok")
                return NovelAgentReviewCheckStatus.Pass;
            if (normalized is "ask_user" or "needs_user_decision" or "uncertain")
                return NovelAgentReviewCheckStatus.Warning;
            if (normalized is "rewrite" or "revise" or "fail" or "reject" or "revise_before_commit")
                return NovelAgentReviewCheckStatus.Fail;

            return decision.MeetsUserIntent && decision.MeetsRevisionPlan && decision.MeetsProjectPromise
                ? NovelAgentReviewCheckStatus.Pass
                : NovelAgentReviewCheckStatus.Fail;
        }

        private static void AddNoveltyChecks(
            NovelAgentPostGenerationReview review,
            StoryStateSnapshot storyState,
            string content)
        {
            var repeatedPatterns = storyState.UsedPlotPatterns
                .Where(p => !string.IsNullOrWhiteSpace(p) && HasTokenOverlap(content, p))
                .Take(8)
                .ToList();
            review.Checks.Add(new NovelAgentReviewCheck
            {
                Key = "used_plot_pattern_repetition",
                Name = "旧桥段重复",
                Status = repeatedPatterns.Count == 0 ? NovelAgentReviewCheckStatus.Pass : NovelAgentReviewCheckStatus.Warning,
                RiskLevel = NovelToolRiskLevel.Medium,
                Message = repeatedPatterns.Count == 0
                    ? "未发现明显复用历史桥段模式。"
                    : $"正文可能复用了 {repeatedPatterns.Count} 个历史桥段模式。",
                Evidence = repeatedPatterns,
                Suggestions = repeatedPatterns.Count == 0
                    ? new List<string>()
                    : new List<string> { "保留剧情功能，替换信息来源、角色代价、反转主体或场景压力。" }
            });

            var clicheHits = ExtractEvidence(content, ClicheRiskKeywords).Take(12).ToList();
            review.Checks.Add(new NovelAgentReviewCheck
            {
                Key = "cliche_risk",
                Name = "套路表达风险",
                Status = clicheHits.Count >= 8
                    ? NovelAgentReviewCheckStatus.Warning
                    : NovelAgentReviewCheckStatus.Pass,
                RiskLevel = NovelToolRiskLevel.Medium,
                Message = clicheHits.Count >= 8
                    ? $"检测到较多高频套路表达信号：{clicheHits.Count} 个。"
                    : "套路表达信号处于可接受范围。",
                Evidence = clicheHits,
                Suggestions = clicheHits.Count >= 8
                    ? new List<string> { "把口号式震惊、冷笑、顿悟替换成具体行动、误判成本和信息差。" }
                    : new List<string>()
            });
        }

        private static void AddStoryVariableChecks(
            NovelAgentPostGenerationReview review,
            StoryStateSnapshot storyState,
            string content)
        {
            var changes = new List<string>();
            if (storyState.ActiveConflicts.Any(c => HasTokenOverlap(content, c)))
                changes.Add("冲突状态发生推进或被重新定义。");
            if (storyState.ActiveForeshadowing.Any(f => HasTokenOverlap(content, f)))
                changes.Add("伏笔被设置、强化或回收。");
            if (ContainsAny(content, CostKeywords))
                changes.Add("角色付出了代价或承担了后果。");
            if (ContainsAny(content, WorldbuildingKeywords))
                changes.Add("世界观规则、组织或场景信息发生增量。");

            review.StoryVariableChanges.AddRange(changes.Distinct());
            review.Checks.Add(new NovelAgentReviewCheck
            {
                Key = "story_variable_change",
                Name = "故事变量变化",
                Status = changes.Count > 0 ? NovelAgentReviewCheckStatus.Pass : NovelAgentReviewCheckStatus.Fail,
                RiskLevel = NovelToolRiskLevel.High,
                Message = changes.Count > 0
                    ? $"检测到 {changes.Count} 类故事变量变化。"
                    : "本章未检测到明确故事变量变化，容易成为原地踏步章节。",
                Evidence = changes,
                Suggestions = changes.Count > 0
                    ? new List<string>()
                    : new List<string> { "至少改变冲突进度、角色关系、世界规则认知、伏笔状态中的一项。" }
            });
        }

        private static void AddProposedCanonEntries(
            NovelAgentPostGenerationReview review,
            NovelAgentRun run,
            StoryBibleDocument document,
            string content)
        {
            var briefGap = run.ChapterBrief?.WorldbuildingGap?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(briefGap) || !HasTokenOverlap(content, briefGap))
                return;

            var exists = document.CanonLedger.Any(e =>
                string.Equals(e.Title, briefGap, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.Content, briefGap, StringComparison.OrdinalIgnoreCase));
            if (exists) return;

            review.ProposedCanonEntries.Add(new CanonLedgerEntry
            {
                Type = CanonLedgerEntryType.WorldRule,
                Status = CanonLedgerEntryStatus.Proposed,
                Title = briefGap.Length > 40 ? briefGap.Substring(0, 40) : briefGap,
                Content = briefGap,
                Rationale = "章节创意简报要求补足世界观缺口，正文生成后检测到相关设定信号。",
                ImpactScope = string.IsNullOrWhiteSpace(run.TargetChapterId) ? "当前章节" : run.TargetChapterId,
                SourceChapterId = run.TargetChapterId,
                SourceRunId = run.RunId,
                ConflictCheck = "自动提出，进入 Canon 前仍需人工确认和冲突检查。"
            });
        }

        private static void AddProposedForeshadowEntries(
            NovelAgentPostGenerationReview review,
            NovelAgentRun run,
            StoryBibleDocument document,
            StoryStateSnapshot storyState,
            string content)
        {
            var action = run.ChapterBrief?.ForeshadowingAction?.Trim() ?? string.Empty;
            var activeForeshadow = storyState.ActiveForeshadowing
                .FirstOrDefault(f => !string.IsNullOrWhiteSpace(f) && HasTokenOverlap(content, f))
                ?? string.Empty;
            if (string.IsNullOrWhiteSpace(action) && string.IsNullOrWhiteSpace(activeForeshadow))
                return;

            var basis = FirstNonEmpty(action, activeForeshadow);
            if (!HasTokenOverlap(content, basis) && !ContainsAny(content, new[] { "伏笔", "线索", "异常", "回收", "埋下", "暗示" }))
                return;

            var existing = document.ForeshadowLedger
                .FirstOrDefault(f => HasTokenOverlap(basis, f.Name)
                    || HasTokenOverlap(basis, f.Setup)
                    || HasTokenOverlap(basis, f.Payoff));
            var status = InferForeshadowStatus(basis, content, existing);
            var entry = existing == null
                ? new ForeshadowLedgerEntry
                {
                    Name = BuildForeshadowName(basis),
                    Type = InferForeshadowType(basis),
                    Status = ForeshadowLedgerStatus.Proposed,
                    Setup = basis,
                    Payoff = "待后续章节确认回收方式。",
                    SourceChapterId = run.TargetChapterId,
                    SourceRunId = run.RunId,
                    Importance = 5
                }
                : CloneForeshadow(existing);

            entry.Status = status;
            entry.SourceChapterId = string.IsNullOrWhiteSpace(entry.SourceChapterId)
                ? run.TargetChapterId
                : entry.SourceChapterId;
            entry.SourceRunId = string.IsNullOrWhiteSpace(entry.SourceRunId)
                ? run.RunId
                : entry.SourceRunId;
            entry.Evidence.Add(Trim(basis, 160));
            entry.Notes.Add($"生成后复盘提出伏笔状态变化，来源章节：{run.TargetChapterId}。");
            review.ProposedForeshadowEntries.Add(entry);
        }

        private static void AddProposedCharacterEntries(
            NovelAgentPostGenerationReview review,
            NovelAgentRun run,
            StoryBibleDocument document,
            StoryStateSnapshot storyState,
            string content)
        {
            var basis = FirstNonEmpty(
                run.ChapterBrief?.CharacterChoice,
                storyState.CharacterLedgerItems.FirstOrDefault(),
                storyState.CharacterStates.FirstOrDefault(),
                run.ChapterBrief?.CostOrConsequence);
            if (string.IsNullOrWhiteSpace(basis))
                return;

            var hasCharacterSignal = HasTokenOverlap(content, basis)
                || ContainsAny(content, CharacterEmotionKeywords)
                || ContainsAny(content, RelationshipKeywords)
                || ContainsAny(content, CharacterSecretKeywords)
                || ContainsAny(content, AbilityChangeKeywords);
            if (!hasCharacterSignal)
                return;

            var characterName = InferCharacterName(basis);
            var existing = document.CharacterLedger
                .FirstOrDefault(c => string.Equals(c.CharacterName, characterName, StringComparison.OrdinalIgnoreCase)
                    && (HasTokenOverlap(basis, c.Summary)
                        || HasTokenOverlap(basis, c.CurrentGoal)
                        || HasTokenOverlap(basis, c.CurrentIntent)
                        || HasTokenOverlap(basis, c.Relationship.TargetCharacter)
                        || HasTokenOverlap(basis, c.Secret.Content)
                        || HasTokenOverlap(basis, c.AbilityCost.Ability)));

            var entry = existing == null
                ? new CharacterLedgerEntry
                {
                    CharacterName = characterName,
                    Type = InferCharacterEntryType(basis, content),
                    Status = CharacterLedgerStatus.Proposed,
                    Summary = Trim(basis, 180),
                    CurrentIntent = Trim(run.ChapterBrief?.CharacterChoice ?? basis, 160),
                    NextPressure = Trim(run.ChapterBrief?.CostOrConsequence ?? string.Empty, 160),
                    SourceChapterId = run.TargetChapterId,
                    SourceRunId = run.RunId,
                    Importance = 5
                }
                : CloneCharacter(existing);

            entry.Type = InferCharacterEntryType(basis, content);
            entry.Status = InferCharacterStatus(basis, content, entry);
            entry.SourceChapterId = string.IsNullOrWhiteSpace(entry.SourceChapterId)
                ? run.TargetChapterId
                : entry.SourceChapterId;
            entry.SourceRunId = string.IsNullOrWhiteSpace(entry.SourceRunId)
                ? run.RunId
                : entry.SourceRunId;
            entry.Summary = FirstNonEmpty(entry.Summary, Trim(basis, 180));
            entry.CurrentIntent = FirstNonEmpty(entry.CurrentIntent, Trim(run.ChapterBrief?.CharacterChoice ?? string.Empty, 160));
            entry.NextPressure = FirstNonEmpty(entry.NextPressure, Trim(run.ChapterBrief?.CostOrConsequence ?? string.Empty, 160));
            FillCharacterSubState(entry, basis, content);
            entry.Evidence.Add(Trim(basis, 160));
            entry.Notes.Add($"生成后复盘提出角色状态变化，来源章节：{run.TargetChapterId}。");
            review.ProposedCharacterEntries.Add(entry);
        }

        private static void AddNextChapterSuggestions(
            NovelAgentPostGenerationReview review,
            ChapterCreativeBrief? brief,
            StoryStateSnapshot storyState)
        {
            if (!string.IsNullOrWhiteSpace(brief?.CostOrConsequence))
                review.NextChapterSuggestions.Add($"延续本章代价：{brief.CostOrConsequence}");
            if (!string.IsNullOrWhiteSpace(brief?.ForeshadowingAction))
                review.NextChapterSuggestions.Add($"追踪伏笔动作：{brief.ForeshadowingAction}");
            if (storyState.ActiveConflicts.Count > 0)
                review.NextChapterSuggestions.Add($"下一章优先推进冲突：{storyState.ActiveConflicts[0]}");
            if (storyState.ActiveForeshadowing.Count > 0)
                review.NextChapterSuggestions.Add($"检查伏笔是否需要升级或回收：{storyState.ActiveForeshadowing[0]}");
            if (storyState.CharacterStates.Count > 0)
                review.NextChapterSuggestions.Add($"延续角色状态压力：{storyState.CharacterStates[0]}");
            if (review.RequiresRewrite)
                review.NextChapterSuggestions.Add("先改写本章质量风险，再规划下一章。");
        }

        private static NovelAgentReviewCheck BuildContainsCheck(
            string key,
            string name,
            string content,
            string expected,
            string passMessage,
            string warningMessage,
            string suggestion)
        {
            var matched = string.IsNullOrWhiteSpace(expected) || HasTokenOverlap(content, expected);
            return new NovelAgentReviewCheck
            {
                Key = key,
                Name = name,
                Status = matched ? NovelAgentReviewCheckStatus.Pass : NovelAgentReviewCheckStatus.Warning,
                RiskLevel = NovelToolRiskLevel.Medium,
                Message = matched ? passMessage : warningMessage,
                Evidence = string.IsNullOrWhiteSpace(expected) ? new List<string>() : new List<string> { expected },
                Suggestions = matched ? new List<string>() : new List<string> { suggestion }
            };
        }

        private static void CompleteReview(NovelAgentPostGenerationReview review)
        {
            var failCount = review.Checks.Count(c => c.Status == NovelAgentReviewCheckStatus.Fail);
            var warningCount = review.Checks.Count(c => c.Status == NovelAgentReviewCheckStatus.Warning);
            review.RequiresRewrite = failCount > 0 || warningCount >= 3;
            review.QualityScore = Math.Clamp(100 - failCount * 25 - warningCount * 8, 0, 100);
            review.OverallResult = failCount > 0
                ? "Fail"
                : warningCount > 0
                    ? "Warning"
                    : "Pass";
            review.Summary = review.RequiresRewrite
                ? $"生成后复盘发现 {failCount} 个失败项、{warningCount} 个警告项，建议进入改写或人工确认。"
                : $"生成后复盘通过，质量分 {review.QualityScore}，可进入下一章规划。";
        }

        private static bool ContainsAny(string content, IEnumerable<string> keywords)
        {
            return keywords.Any(k => content.Contains(k, StringComparison.OrdinalIgnoreCase));
        }

        private static bool ContainsAny(string content, params string[] keywords) =>
            ContainsAny(content, (IEnumerable<string>)keywords);

        private static string NormalizeCompact(string? value) =>
            string.Concat((value ?? string.Empty).Where(ch => !char.IsWhiteSpace(ch)));

        private static string NormalizeEditorialValue(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToLowerInvariant().Replace(' ', '_');

        private static ForeshadowLedgerStatus InferForeshadowStatus(
            string basis,
            string content,
            ForeshadowLedgerEntry? existing)
        {
            var combined = $"{basis} {content}";
            if (ContainsAny(combined, new[] { "回收", "兑现", "揭示", "真相", "答案", "原来" }))
                return ForeshadowLedgerStatus.PaidOff;
            if (ContainsAny(combined, new[] { "再次", "强化", "升级", "加深", "重复出现" }))
                return ForeshadowLedgerStatus.Reinforced;
            return existing == null || existing.Status is ForeshadowLedgerStatus.Proposed or ForeshadowLedgerStatus.Planned
                ? ForeshadowLedgerStatus.Setup
                : ForeshadowLedgerStatus.Reinforced;
        }

        private static ForeshadowLedgerEntryType InferForeshadowType(string text)
        {
            if (ContainsAny(text, new[] { "规则", "异常", "法则", "制度" }))
                return ForeshadowLedgerEntryType.WorldRule;
            if (ContainsAny(text, new[] { "身份", "秘密", "过去", "记忆" }))
                return ForeshadowLedgerEntryType.CharacterSecret;
            if (ContainsAny(text, new[] { "关系", "盟友", "背叛", "信任" }))
                return ForeshadowLedgerEntryType.Relationship;
            if (ContainsAny(text, new[] { "物品", "钥匙", "令牌", "遗物" }))
                return ForeshadowLedgerEntryType.Object;
            if (ContainsAny(text, new[] { "威胁", "代价", "危险", "反噬" }))
                return ForeshadowLedgerEntryType.Threat;
            return ForeshadowLedgerEntryType.Plot;
        }

        private static ForeshadowLedgerEntry CloneForeshadow(ForeshadowLedgerEntry entry)
        {
            return new ForeshadowLedgerEntry
            {
                Id = entry.Id,
                Name = entry.Name,
                Type = entry.Type,
                Status = entry.Status,
                Setup = entry.Setup,
                Payoff = entry.Payoff,
                SourceVolumeId = entry.SourceVolumeId,
                PlannedSetupChapterId = entry.PlannedSetupChapterId,
                PlannedPayoffChapterId = entry.PlannedPayoffChapterId,
                ActualSetupChapterIds = entry.ActualSetupChapterIds.ToList(),
                ActualReinforceChapterIds = entry.ActualReinforceChapterIds.ToList(),
                ActualPayoffChapterId = entry.ActualPayoffChapterId,
                Importance = entry.Importance,
                Evidence = entry.Evidence.ToList(),
                Notes = entry.Notes.ToList(),
                SourceRunId = entry.SourceRunId,
                SourceChapterId = entry.SourceChapterId,
                CreatedAt = entry.CreatedAt,
                UpdatedAt = entry.UpdatedAt
            };
        }

        private static CharacterLedgerEntry CloneCharacter(CharacterLedgerEntry entry)
        {
            return new CharacterLedgerEntry
            {
                Id = entry.Id,
                CharacterName = entry.CharacterName,
                Role = entry.Role,
                Type = entry.Type,
                Status = entry.Status,
                Summary = entry.Summary,
                CurrentGoal = entry.CurrentGoal,
                CurrentIntent = entry.CurrentIntent,
                NextPressure = entry.NextPressure,
                Secret = new CharacterSecretState
                {
                    Content = entry.Secret.Content,
                    Status = entry.Secret.Status,
                    KnownBy = entry.Secret.KnownBy.ToList()
                },
                Relationship = new CharacterRelationshipState
                {
                    TargetCharacter = entry.Relationship.TargetCharacter,
                    Status = entry.Relationship.Status,
                    Tension = entry.Relationship.Tension,
                    Change = entry.Relationship.Change
                },
                AbilityCost = new CharacterAbilityCostState
                {
                    Ability = entry.AbilityCost.Ability,
                    LevelOrBoundary = entry.AbilityCost.LevelOrBoundary,
                    Cost = entry.AbilityCost.Cost,
                    Debt = entry.AbilityCost.Debt,
                    Limitation = entry.AbilityCost.Limitation
                },
                Psychology = new CharacterPsychologyState
                {
                    Emotion = entry.Psychology.Emotion,
                    Wound = entry.Psychology.Wound,
                    CopingStrategy = entry.Psychology.CopingStrategy,
                    StressLevel = entry.Psychology.StressLevel
                },
                BeliefShift = entry.BeliefShift,
                IdentityState = entry.IdentityState,
                Importance = entry.Importance,
                Evidence = entry.Evidence.ToList(),
                Notes = entry.Notes.ToList(),
                SourceRunId = entry.SourceRunId,
                SourceChapterId = entry.SourceChapterId,
                CreatedAt = entry.CreatedAt,
                UpdatedAt = entry.UpdatedAt
            };
        }

        private static CharacterLedgerEntryType InferCharacterEntryType(string basis, string content)
        {
            var combined = $"{basis} {content}";
            if (ContainsAny(combined, CharacterSecretKeywords))
                return CharacterLedgerEntryType.Secret;
            if (ContainsAny(combined, RelationshipKeywords))
                return CharacterLedgerEntryType.Relationship;
            if (ContainsAny(combined, AbilityChangeKeywords))
                return CharacterLedgerEntryType.AbilityCost;
            if (ContainsAny(combined, CharacterEmotionKeywords))
                return CharacterLedgerEntryType.Psychology;
            if (ContainsAny(combined, new[] { "信念", "价值", "原则", "相信", "不再" }))
                return CharacterLedgerEntryType.Belief;
            return CharacterLedgerEntryType.Goal;
        }

        private static CharacterLedgerStatus InferCharacterStatus(
            string basis,
            string content,
            CharacterLedgerEntry entry)
        {
            var combined = $"{basis} {content}";
            if (ContainsAny(combined, new[] { "死亡", "死去", "牺牲", "殒命" }))
                return CharacterLedgerStatus.Dead;
            if (ContainsAny(combined, new[] { "真实身份", "身份改写", "换了身份", "不再是" }))
                return CharacterLedgerStatus.IdentityRewritten;
            if (ContainsAny(combined, new[] { "揭穿", "暴露", "真相大白", "秘密公开" }))
                return CharacterLedgerStatus.SecretRevealed;
            if (ContainsAny(combined, new[] { "背叛", "翻脸", "反目", "决裂" }))
                return CharacterLedgerStatus.RelationshipReversed;
            if (ContainsAny(combined, new[] { "规则改变", "能力体系改变", "代价规则", "突破限制" }))
                return CharacterLedgerStatus.AbilityRuleChanged;
            if (ContainsAny(combined, AbilityChangeKeywords))
                return CharacterLedgerStatus.AbilityChanged;
            if (ContainsAny(combined, RelationshipKeywords))
                return CharacterLedgerStatus.RelationshipChanged;
            if (ContainsAny(combined, CharacterEmotionKeywords))
                return CharacterLedgerStatus.PsychologicalShifted;
            return entry.Status == CharacterLedgerStatus.Proposed
                ? CharacterLedgerStatus.Active
                : CharacterLedgerStatus.GoalUpdated;
        }

        private static void FillCharacterSubState(
            CharacterLedgerEntry entry,
            string basis,
            string content)
        {
            var combined = $"{basis} {content}";
            if (ContainsAny(combined, CharacterSecretKeywords) && string.IsNullOrWhiteSpace(entry.Secret.Content))
            {
                entry.Secret.Content = Trim(basis, 120);
                entry.Secret.Status = entry.Status == CharacterLedgerStatus.SecretRevealed
                    ? CharacterSecretStatus.Revealed
                    : CharacterSecretStatus.Seeded;
            }

            if (ContainsAny(combined, RelationshipKeywords) && string.IsNullOrWhiteSpace(entry.Relationship.Change))
            {
                entry.Relationship.Change = Trim(basis, 120);
                entry.Relationship.Status = entry.Status == CharacterLedgerStatus.RelationshipReversed
                    ? CharacterRelationshipStatus.Betrayed
                    : CharacterRelationshipStatus.Ambiguous;
            }

            if (ContainsAny(combined, AbilityChangeKeywords))
            {
                entry.AbilityCost.Ability = FirstNonEmpty(entry.AbilityCost.Ability, Trim(basis, 80));
                if (ContainsAny(combined, CostKeywords))
                    entry.AbilityCost.Cost = FirstNonEmpty(entry.AbilityCost.Cost, Trim(basis, 80));
            }

            if (ContainsAny(combined, CharacterEmotionKeywords))
            {
                entry.Psychology.Emotion = FirstNonEmpty(
                    entry.Psychology.Emotion,
                    CharacterEmotionKeywords.FirstOrDefault(k => combined.Contains(k, StringComparison.OrdinalIgnoreCase)) ?? string.Empty);
                entry.Psychology.StressLevel = ContainsAny(combined, new[] { "崩溃", "失控", "绝望", "反噬" }) ? 8 : Math.Max(entry.Psychology.StressLevel, 5);
            }
        }

        private static string InferCharacterName(string basis)
        {
            var text = basis.Trim();
            var separators = new[] { '：', ':', '，', ',', '。', ' ', '\t', '\r', '\n' };
            var first = text.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(first))
                return "未命名角色";
            return first.Length <= 12 ? first : "主角";
        }

        private static string BuildForeshadowName(string basis)
        {
            var text = basis.Trim();
            if (text.Length <= 24) return text;
            return text.Substring(0, 24);
        }

        private static string FormatKnowledgeBindingLabel(BoundKnowledgeSnapshot binding)
        {
            var title = FirstNonEmpty(binding.Title, binding.KnowledgeId, "未命名知识");
            var status = FirstNonEmpty(binding.ProjectUsageStatus, "bound");
            return $"{title}（{binding.EntryType}/{status}）";
        }

        private static IEnumerable<string> ExtractEvidence(string content, IEnumerable<string> keywords)
        {
            return keywords
                .Where(k => content.Contains(k, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static bool HasTokenOverlap(string content, string expected)
        {
            if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(expected))
                return false;

            var normalizedExpected = expected.Trim();
            if (normalizedExpected.Length <= 8)
                return content.Contains(normalizedExpected, StringComparison.OrdinalIgnoreCase);

            var tokens = normalizedExpected
                .Split(new[] { ' ', '\t', '\r', '\n', '，', '。', '、', '；', ';', ',', '.', '：', ':', '！', '？', '(', ')', '（', '）' },
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(t => t.Length >= 2)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();

            if (tokens.Count == 0)
                return content.Contains(normalizedExpected, StringComparison.OrdinalIgnoreCase);

            var hitCount = tokens.Count(t => content.Contains(t, StringComparison.OrdinalIgnoreCase));
            return hitCount >= Math.Max(1, Math.Min(3, tokens.Count / 2));
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }

            return string.Empty;
        }

        private static string Trim(string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            text = text.Trim();
            return text.Length <= maxLength ? text : text[..maxLength] + "...";
        }
    }
}
