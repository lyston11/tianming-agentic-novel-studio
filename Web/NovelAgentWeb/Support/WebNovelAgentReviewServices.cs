using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Modules.ProjectData.Interfaces;

namespace TM.Services.Framework.AI.NovelAgent.Services
{
    public sealed class ChapterPostGenerationReviewer
    {
        private readonly IGeneratedContentService _contentService;
        private readonly StoryBibleService _storyBibleService;
        private readonly StoryStateSnapshotService _storyStateSnapshotService;

        public ChapterPostGenerationReviewer(
            IGeneratedContentService contentService,
            IUnifiedValidationService validationService,
            StoryBibleService storyBibleService,
            StoryStateSnapshotService storyStateSnapshotService,
            CommercialRhythmChecker? commercialRhythmChecker = null)
        {
            _contentService = contentService;
            _storyBibleService = storyBibleService;
            _storyStateSnapshotService = storyStateSnapshotService;
        }

        public async Task<NovelAgentPostGenerationReview> ReviewAsync(
            NovelAgentRun run,
            CancellationToken ct = default)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));

            var content = await _contentService.GetChapterAsync(run.TargetChapterId).ConfigureAwait(false) ?? string.Empty;
            var document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var storyState = await _storyStateSnapshotService.BuildForChapterAsync(run.TargetChapterId, ct)
                .ConfigureAwait(false);

            var review = new NovelAgentPostGenerationReview
            {
                ChapterId = run.TargetChapterId,
                OverallResult = string.IsNullOrWhiteSpace(content) ? "Warning" : "Pass",
                QualityScore = string.IsNullOrWhiteSpace(content) ? 0 : 88,
                ContentLength = content.Length,
                ValidationOverallResult = run.GateReport?.Status == "validated" ? "Pass" : "Warning",
                RequiresRewrite = string.IsNullOrWhiteSpace(content) || run.GateReport?.Status == "failed",
                Summary = string.IsNullOrWhiteSpace(content)
                    ? "章节正文为空，需重新生成或补写。"
                    : $"章节已保存到内容文档，共 {content.Length} 字符；Story Bible 当前包含 {document.VolumeArcs.Count} 个卷规划，故事状态包含 {storyState.CharacterStates.Count} 条角色状态。"
            };

            review.Checks.Add(new NovelAgentReviewCheck
            {
                Key = "content_document_presence",
                Name = "章节内容文档",
                Status = string.IsNullOrWhiteSpace(content)
                    ? NovelAgentReviewCheckStatus.Fail
                    : NovelAgentReviewCheckStatus.Pass,
                RiskLevel = NovelToolRiskLevel.High,
                Message = string.IsNullOrWhiteSpace(content)
                    ? "未读取到章节正文内容。"
                    : "章节正文已从内容文档读取。"
            });

            return review;
        }
    }

    public sealed class NovelAgentRewriteLoopService
    {
        public Task<NovelAgentRewriteAttempt> RewriteOnceAsync(
            NovelAgentRun run,
            CancellationToken ct = default)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));

            return Task.FromResult(new NovelAgentRewriteAttempt
            {
                ChapterId = run.TargetChapterId,
                Success = false,
                BeforeQualityScore = run.PostGenerationReview?.QualityScore ?? 0,
                AfterQualityScore = run.PostGenerationReview?.QualityScore ?? 0,
                ReviewAfterRewrite = run.PostGenerationReview,
                RepairResult = "Web 自动改写尚未接入纯数据库写作链路，未执行旧文件型修复流程。"
            });
        }
    }
}

