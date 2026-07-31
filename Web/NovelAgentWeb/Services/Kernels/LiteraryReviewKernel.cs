using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Services.Quality;

namespace TM.Web.NovelAgentWeb.Services.Kernels;

public sealed class LiteraryReviewKernel : IKernel
{
    private readonly ILiteraryReviewModelClient _model;

    public LiteraryReviewKernel(ILiteraryReviewModelClient model)
    {
        _model = model;
    }

    public string Name => "literary_review";

    public async Task<KernelExecutionOutput> ExecuteAsync(
        KernelExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var draftInput = context.Inputs.Single(input => input.ArtifactType == "CandidateChapterDraft");
        var draft = JsonSerializer.Deserialize<ChapterDraftArtifact>(draftInput.ContentJson)
            ?? throw new InvalidOperationException("候选正文 Artifact 无法解析。");
        var request = new LiteraryReviewRequest(
            context.Claim.UserId,
            context.Claim.ProjectId,
            context.Claim.GoalId,
            draft.ChapterId,
            draft.DraftContent,
            context.GoalSnapshot.QualityContractVersion);
        var decision = await _model.ReviewAsync(request, cancellationToken);
        return KernelOutputFactory.Single(
            context,
            "LiteraryReview",
            "LiteraryReviewCompleted",
            "candidate_chapter",
            draft.ChapterId,
            JsonSerializer.Serialize(decision));
    }
}
