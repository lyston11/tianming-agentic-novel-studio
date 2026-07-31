using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;
using TM.Web.NovelAgentWeb.Services.DomainEvents;
using TM.Web.NovelAgentWeb.Services.Quality;

namespace TM.Web.NovelAgentWeb.Services.Kernels;

public sealed class TianmingWritingKernel : IKernel
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ITianmingProductionKernel _tianming;
    private readonly ITianmingWritingGateway? _gateway;

    public TianmingWritingKernel(ITianmingProductionKernel tianming)
    {
        _tianming = tianming;
    }

    public TianmingWritingKernel(ITianmingWritingGateway gateway)
    {
        _tianming = null!;
        _gateway = gateway;
    }

    public string Name => "tianming_writing";

    public async Task<KernelExecutionOutput> ExecuteAsync(
        KernelExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var compiledContext = context.Inputs.SingleOrDefault(input =>
            input.ArtifactType == "ChapterContextContract")
            ?? throw new InvalidOperationException("天命写作内核缺少 ChapterContextContract。");
        var input = JsonSerializer.Deserialize<TianmingChapterContextInput>(compiledContext.ContentJson, JsonOptions)
            ?? throw new InvalidOperationException("ChapterContextContract 无法解析。");
        var isReworkDraft = context.Claim.TaskType == "DirectedReworkDraft";
        var isDirectedRework = context.Claim.TaskType == "DirectedRework" || isReworkDraft;
        ChapterDraftArtifact? originalDraft = null;
        if (isDirectedRework)
        {
            var draftInput = context.Inputs.SingleOrDefault(item =>
                item.ArtifactType is "CandidateChapterDraft" or "ReviewedCandidateChapter")
                ?? throw new InvalidOperationException("定向返工缺少候选章节正文。");
            originalDraft = JsonSerializer.Deserialize<ChapterDraftArtifact>(draftInput.ContentJson, JsonOptions)
                ?? throw new InvalidOperationException("候选正文 Artifact 无法解析。");
            if (draftInput is { Authorship: "human", IsProtected: true })
            {
                return BuildOutput(
                    context,
                    compiledContext.Id,
                    originalDraft,
                    "ReviewedCandidateChapter",
                    "CandidateChapterNeedsDecision",
                    "human",
                    true);
            }
            var reworkInput = context.Inputs.SingleOrDefault(item => item.ArtifactType == "ReworkIntent");
            if (reworkInput == null)
            {
                var reviewVerdicts = ReadReviewVerdicts(context);
                if (reviewVerdicts.Contains(ReviewVerdict.NeedsDecision))
                    return BuildOutput(context, compiledContext.Id, originalDraft, "ReviewedCandidateChapter", "CandidateChapterNeedsDecision");
                if (!reviewVerdicts.Contains(ReviewVerdict.ReworkRequired))
                    return BuildOutput(context, compiledContext.Id, originalDraft, "ReviewedCandidateChapter", "CandidateChapterReviewPassed");
            }
            var rework = reworkInput == null
                ? BuildReviewDrivenContract(context)
                : JsonSerializer.Deserialize<ReworkIntentArtifactContract>(reworkInput.ContentJson, JsonOptions)
                    ?? throw new InvalidOperationException("ReworkIntent Artifact 无法解析。");
            input.Run.Intent = NovelAgentIntent.RewriteChapter;
            input.Run.DraftArtifact = originalDraft;
            input.Package.DirectedRework = new DirectedReworkContract
            {
                IntentId = rework.IntentId,
                TargetScope = rework.TargetScope,
                SelectionStart = rework.SelectionStart,
                SelectionEnd = rework.SelectionEnd,
                Problem = rework.Problem,
                DesiredEffect = rework.DesiredEffect,
                Preserve = rework.Preserve.ToList(),
                MayChange = rework.MayChange.ToList(),
                MustNotChange = rework.MustNotChange.ToList(),
                AcceptanceCriteria = rework.AcceptanceCriteria.ToList()
            };
        }
        var draft = _gateway == null
            ? await _tianming.GenerateDraftWithChangesAsync(input.Run, input.Package, cancellationToken)
            : await _gateway.GenerateAsync(
                context.Claim.UserId,
                context.Claim.ProjectId,
                input.Run,
                input.Package,
                cancellationToken);
        return BuildOutput(
            context,
            compiledContext.Id,
            draft,
            isReworkDraft ? "CandidateChapterDraft" : isDirectedRework ? "ReviewedCandidateChapter" : "CandidateChapterDraft",
            isReworkDraft
                ? "CandidateChapterReworkDraftProduced"
                : isDirectedRework ? "CandidateChapterReworked" : "CandidateChapterDraftProduced");
    }

    private static IReadOnlyList<ReviewVerdict> ReadReviewVerdicts(KernelExecutionContext context)
    {
        var continuityInput = context.Inputs.SingleOrDefault(item => item.ArtifactType == "ContinuityReview")
            ?? throw new InvalidOperationException("自动定向返工缺少 ContinuityReview。");
        var literaryInput = context.Inputs.SingleOrDefault(item => item.ArtifactType == "LiteraryReview")
            ?? throw new InvalidOperationException("自动定向返工缺少 LiteraryReview。");
        var continuity = JsonSerializer.Deserialize<ContinuityReviewArtifact>(continuityInput.ContentJson, JsonOptions)
            ?? throw new InvalidOperationException("ContinuityReview Artifact 无法解析。");
        var literary = JsonSerializer.Deserialize<LiteraryReviewDecision>(literaryInput.ContentJson, JsonOptions)
            ?? throw new InvalidOperationException("LiteraryReview Artifact 无法解析。");
        return [continuity.Verdict, literary.Verdict];
    }

    private static KernelExecutionOutput BuildOutput(
        KernelExecutionContext context,
        string compiledContextId,
        ChapterDraftArtifact draft,
        string artifactType,
        string eventType,
        string authorship = "agent",
        bool isProtected = false)
    {
        var json = JsonSerializer.Serialize(draft);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        var artifact = new KernelArtifactProposal(artifactType, 1, json, hash, authorship, isProtected);
        var domainEvent = new DomainEventProposal(
            "candidate_chapter",
            draft.ChapterId,
            1,
            eventType,
            ["@artifact:0"],
            context.Inputs.Select(input => input.Id).Append(compiledContextId).Distinct().ToArray(),
            JsonSerializer.Serialize(new { draft.ChapterId, draft.ArtifactId }),
            $"{context.Claim.TaskId}:{eventType}:{hash}");
        return new KernelExecutionOutput([artifact], [domainEvent]);
    }

    private static ReworkIntentArtifactContract BuildReviewDrivenContract(KernelExecutionContext context)
    {
        var reviewInputs = context.Inputs
            .Where(item => item.ArtifactType is "ContinuityReview" or "LiteraryReview")
            .ToList();
        if (reviewInputs.Count != 2)
            throw new InvalidOperationException("自动定向返工必须同时获得连续性与审美审稿结果。");
        return new ReworkIntentArtifactContract(
            $"automatic:{context.Claim.TaskId}",
            "chapter",
            null,
            null,
            "双重审稿发现候选章节仍有未满足项。",
            "修复审稿证据指向的问题，同时保留已经通过的内容。",
            ["已经通过审稿的情节、人物状态与表达"],
            ["审稿证据明确指向的问题范围"],
            ["Goal 合同", "正史硬事实", "审稿未指出的有效内容"],
            reviewInputs.Select(item => item.ContentJson).ToArray());
    }
}
