using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Services.Canon;
using TM.Web.NovelAgentWeb.Services.Quality;

namespace TM.Web.NovelAgentWeb.Services.Kernels;

public sealed class ContinuityReviewKernel : IKernel
{
    private readonly IPotentialViolationDetector _detector;
    private readonly IContinuityReviewModelClient _model;
    private readonly ILightweightCanonExtractionModelClient? _canonExtraction;
    private readonly ILightweightCanonSemanticReviewModelClient? _canonReview;

    public ContinuityReviewKernel(
        IPotentialViolationDetector detector,
        IContinuityReviewModelClient model)
    {
        _detector = detector;
        _model = model;
    }

    public ContinuityReviewKernel(
        IPotentialViolationDetector detector,
        IContinuityReviewModelClient model,
        ILightweightCanonExtractionModelClient canonExtraction,
        ILightweightCanonSemanticReviewModelClient canonReview)
        : this(detector, model)
    {
        _canonExtraction = canonExtraction;
        _canonReview = canonReview;
    }

    public string Name => "continuity_review";

    public async Task<KernelExecutionOutput> ExecuteAsync(
        KernelExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Claim.TaskType == "ExtractContinuitySummary")
            return await ExtractLightweightCanonAsync(context, cancellationToken);
        var draftInput = context.Inputs.Single(input => input.ArtifactType == "CandidateChapterDraft");
        var draft = JsonSerializer.Deserialize<ChapterDraftArtifact>(draftInput.ContentJson)
            ?? throw new InvalidOperationException("候选正文 Artifact 无法解析。");
        var evidence = context.Inputs
            .Where(input => input.ArtifactType == "EvidenceBundle")
            .Select(input => input.ContentJson)
            .ToArray();
        var initialRequest = new ContinuityReviewRequest(
            context.Claim.UserId,
            context.Claim.ProjectId,
            context.Claim.GoalId,
            draft.ChapterId,
            draft.DraftContent,
            evidence,
            [],
            context.GoalSnapshot.QualityContractVersion);
        var potentialViolations = _detector.Detect(initialRequest);
        var request = initialRequest with { PotentialViolations = potentialViolations };
        var decision = await _model.ReviewAsync(request, cancellationToken);
        var artifact = new ContinuityReviewArtifact(
            decision.Verdict,
            potentialViolations,
            decision.Claims,
            decision.Recommendations);
        return KernelOutputFactory.Single(
            context,
            "ContinuityReview",
            "ContinuityReviewCompleted",
            "candidate_chapter",
            draft.ChapterId,
            JsonSerializer.Serialize(artifact));
    }

    private async Task<KernelExecutionOutput> ExtractLightweightCanonAsync(
        KernelExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (_canonExtraction == null || _canonReview == null)
            throw new InvalidOperationException("轻量正史提取模型未配置。");
        var reviewedInput = context.Inputs.SingleOrDefault(input => input.ArtifactType == "ReviewedCandidateChapter")
            ?? throw new InvalidOperationException("轻量正史提取缺少 ReviewedCandidateChapter。");
        var draft = JsonSerializer.Deserialize<ChapterDraftArtifact>(reviewedInput.ContentJson)
            ?? throw new InvalidOperationException("已审候选正文 Artifact 无法解析。");
        var branchId = context.Claim.BranchId
            ?? throw new InvalidOperationException("轻量正史提取任务缺少 CanonBranch。");
        var request = new LightweightCanonExtractionRequest(
            context.Claim.UserId,
            context.Claim.ProjectId,
            context.Claim.GoalId,
            branchId,
            context.Claim.TaskId,
            1,
            draft.ChapterId,
            draft.DraftContent);
        var proposals = await _canonExtraction.ExtractAsync(request, cancellationToken);
        var review = await _canonReview.ReviewAsync(
            new LightweightCanonSemanticReviewRequest(request, proposals),
            cancellationToken);
        var summaryItems = SelectApproved(
            proposals.SummaryItems,
            review.ApprovedSummaryItemIds,
            item => item.Id,
            item => item.Evidence,
            draft.DraftContent,
            "summary item");
        var canonChanges = SelectApproved(
            proposals.CanonChanges,
            review.ApprovedCanonChangeIds,
            item => item.Id,
            item => item.Evidence,
            draft.DraftContent,
            "canon change");
        var artifact = new LightweightCanonArtifact(
            "chapter_body",
            reviewedInput.Id,
            summaryItems,
            canonChanges,
            review.RejectionReasons);
        return KernelOutputFactory.Single(
            context,
            "ContinuitySummary",
            "CandidateLightweightCanonProposed",
            "candidate_chapter",
            draft.ChapterId,
            JsonSerializer.Serialize(artifact));
    }

    private static IReadOnlyList<T> SelectApproved<T>(
        IReadOnlyList<T> proposals,
        IReadOnlyList<string> approvedIds,
        Func<T, string> id,
        Func<T, CanonEvidenceSpan> evidence,
        string body,
        string proposalType)
    {
        var byId = proposals.ToDictionary(id, StringComparer.Ordinal);
        var approved = new List<T>();
        foreach (var approvedId in approvedIds.Distinct(StringComparer.Ordinal))
        {
            if (!byId.TryGetValue(approvedId, out var proposal))
                throw new InvalidOperationException($"语义审查批准了不存在的 {proposalType}：{approvedId}");
            CanonEvidenceValidator.Validate(evidence(proposal), body, proposalType, approvedId);
            approved.Add(proposal);
        }
        return approved;
    }
}
