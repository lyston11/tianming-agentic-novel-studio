using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.Services.Canon;

public sealed class ContinuitySummaryExtractor : IContinuitySummaryExtractor
{
    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ILightweightCanonExtractionModelClient _extractionModel;
    private readonly ILightweightCanonSemanticReviewModelClient _reviewModel;
    private readonly CanonChangeExtractor _canonChanges;

    public ContinuitySummaryExtractor(
        NovelAgentDbContext db,
        ICurrentUserService currentUser,
        ILightweightCanonExtractionModelClient extractionModel,
        ILightweightCanonSemanticReviewModelClient reviewModel,
        CanonChangeExtractor canonChanges)
    {
        _db = db;
        _currentUser = currentUser;
        _extractionModel = extractionModel;
        _reviewModel = reviewModel;
        _canonChanges = canonChanges;
    }

    public async Task<LightweightCanonExtractionResult> ExtractAsync(
        string candidateChapterId,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetUserId();
        var candidate = await _db.CandidateChapters.SingleOrDefaultAsync(item =>
            item.Id == candidateChapterId &&
            item.UserId == userId &&
            item.Status == "candidate",
            cancellationToken) ?? throw new KeyNotFoundException("候选章节不存在或不可提取轻量正史。");
        var artifact = await _db.KernelArtifacts.AsNoTracking().SingleAsync(item =>
            item.Id == candidate.CurrentArtifactId &&
            item.UserId == userId &&
            item.BranchId == candidate.BranchId,
            cancellationToken);
        var draftArtifact = JsonSerializer.Deserialize<ChapterDraftArtifact>(artifact.ContentJson)
            ?? throw new InvalidOperationException("候选正文 Artifact 无法解析。");
        var request = new LightweightCanonExtractionRequest(
            userId,
            candidate.ProjectId,
            candidate.GoalId,
            candidate.BranchId,
            candidate.Id,
            candidate.Version,
            candidate.ChapterId,
            draftArtifact.DraftContent);
        var draft = await _extractionModel.ExtractAsync(request, cancellationToken);
        EnsureUniqueIds(draft);
        var review = await _reviewModel.ReviewAsync(
            new LightweightCanonSemanticReviewRequest(request, draft),
            cancellationToken);
        var approvedItems = SelectApproved(
            draft.SummaryItems,
            review.ApprovedSummaryItemIds,
            item => item.Id,
            item => item.Evidence,
            draftArtifact.DraftContent,
            "summary item");
        var approvedChanges = SelectApproved(
            draft.CanonChanges,
            review.ApprovedCanonChangeIds,
            item => item.Id,
            item => item.Evidence,
            draftArtifact.DraftContent,
            "canon change");

        var summary = new ContinuitySummary
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = candidate.UserId,
            ProjectId = candidate.ProjectId,
            ChapterId = candidate.ChapterId,
            ChapterVersionId = $"candidate:{candidate.Id}",
            BranchId = candidate.BranchId,
            Version = candidate.Version,
            SummaryJson = JsonSerializer.Serialize(new
            {
                sourcePriority = "chapter_body",
                bodyArtifactId = artifact.Id,
                items = approvedItems.Select(item => new
                {
                    item.Id,
                    item.Category,
                    item.Content,
                    item.Evidence
                })
            }),
            EvidenceRefsJson = JsonSerializer.Serialize(approvedItems.Select(item => item.Evidence)),
            Status = "candidate",
            CreatedAt = DateTime.UtcNow
        };
        _db.ContinuitySummaries.Add(summary);
        candidate.ContinuitySummaryId = summary.Id;
        var changes = _canonChanges.CreateCandidates(candidate, approvedChanges);
        await _db.SaveChangesAsync(cancellationToken);
        return new LightweightCanonExtractionResult(summary, changes);
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
        var approved = new List<T>(approvedIds.Count);
        foreach (var approvedId in approvedIds.Distinct(StringComparer.Ordinal))
        {
            if (!byId.TryGetValue(approvedId, out var proposal))
                throw new InvalidOperationException($"语义审查批准了不存在的 {proposalType}：{approvedId}");
            CanonEvidenceValidator.Validate(evidence(proposal), body, proposalType, approvedId);
            approved.Add(proposal);
        }
        return approved;
    }

    private static void EnsureUniqueIds(LightweightCanonDraft draft)
    {
        var ids = draft.SummaryItems.Select(item => item.Id)
            .Concat(draft.CanonChanges.Select(item => item.Id))
            .ToArray();
        if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new InvalidOperationException("轻量正史提案 ID 为空或重复。");
    }
}
