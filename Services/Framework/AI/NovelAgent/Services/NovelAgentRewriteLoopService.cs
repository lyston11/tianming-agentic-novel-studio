using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TM.Modules.Validate.ValidationSummary.ValidationResult;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Services.Framework.AI.NovelAgent.Services
{
    public sealed class NovelAgentRewriteLoopService
    {
        private const int MaxRepairHints = 10;

        private readonly ChapterRepairService _chapterRepairService;
        private readonly ChapterPostGenerationReviewer _postGenerationReviewer;

        public NovelAgentRewriteLoopService(
            ChapterRepairService chapterRepairService,
            ChapterPostGenerationReviewer postGenerationReviewer)
        {
            _chapterRepairService = chapterRepairService;
            _postGenerationReviewer = postGenerationReviewer;
        }

        public async Task<NovelAgentRewriteAttempt> RewriteOnceAsync(
            NovelAgentRun run,
            CancellationToken ct = default)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (string.IsNullOrWhiteSpace(run.TargetChapterId))
                throw new InvalidOperationException("Agent Run 缺少目标章节ID，无法执行改写。");

            run.PostGenerationReview ??= await _postGenerationReviewer.ReviewAsync(run, ct)
                .ConfigureAwait(false);

            if (!run.PostGenerationReview.RequiresRewrite)
                throw new InvalidOperationException("当前复盘报告未要求改写，无需进入 Rewrite Loop。");

            var hints = BuildRepairHints(run);
            if (hints.Count == 0)
                hints.Add("生成后复盘要求改写，但没有结构化失败项。请整体检查章节是否偏离 Story Bible、缺少故事变量变化或缺少代价后果。");

            var attempt = new NovelAgentRewriteAttempt
            {
                ChapterId = run.TargetChapterId,
                BeforeQualityScore = run.PostGenerationReview.QualityScore,
                RepairHints = hints
            };

            var repairSessionId = Guid.NewGuid();
            var repairedContent = await _chapterRepairService.RepairChapterAsync(
                run.TargetChapterId,
                hints,
                ct,
                progress: null,
                repairSessionId: repairSessionId).ConfigureAwait(false);

            await _chapterRepairService.SaveRepairedAsync(
                run.TargetChapterId,
                repairedContent,
                progress: null,
                repairSessionId: repairSessionId).ConfigureAwait(false);

            attempt.RepairResult = "修复版正文已通过现有严格保存链路落盘。";
            attempt.ReviewAfterRewrite = await _postGenerationReviewer.ReviewAsync(run, ct)
                .ConfigureAwait(false);
            attempt.AfterQualityScore = attempt.ReviewAfterRewrite.QualityScore;
            attempt.Success = !attempt.ReviewAfterRewrite.RequiresRewrite;

            return attempt;
        }

        private static List<string> BuildRepairHints(NovelAgentRun run)
        {
            var review = run.PostGenerationReview;
            if (review == null) return new List<string>();

            var hints = new List<string>();

            foreach (var check in review.Checks
                         .Where(c => c.Status is NovelAgentReviewCheckStatus.Fail or NovelAgentReviewCheckStatus.Warning)
                         .OrderByDescending(c => c.Status == NovelAgentReviewCheckStatus.Fail)
                         .ThenByDescending(c => c.RiskLevel)
                         .Take(MaxRepairHints))
            {
                var suggestion = check.Suggestions.FirstOrDefault();
                var evidence = check.Evidence.FirstOrDefault();
                var hint = string.IsNullOrWhiteSpace(suggestion)
                    ? $"{check.Name}：{check.Message}"
                    : $"{check.Name}：{check.Message} 修复要求：{suggestion}";
                if (!string.IsNullOrWhiteSpace(evidence))
                    hint += $" 参考证据：{evidence}";
                hints.Add(hint);
            }

            if (review.ProposedCanonEntries.Count > 0)
            {
                hints.Add("正文中出现了可能的新设定。不要把未确认设定继续扩大；如必须保留，请在正文中给出清晰来源、限制和代价，并等待进入 Proposed Canon 流程。");
            }

            if (run.ChapterBrief != null)
            {
                if (!string.IsNullOrWhiteSpace(run.ChapterBrief.CoreIdea))
                    hints.Add($"必须重新贴合本章核心创意：{run.ChapterBrief.CoreIdea}");
                if (!string.IsNullOrWhiteSpace(run.ChapterBrief.CostOrConsequence))
                    hints.Add($"必须补足本章代价或后果：{run.ChapterBrief.CostOrConsequence}");
            }

            return hints
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxRepairHints)
                .ToList();
        }
    }
}
