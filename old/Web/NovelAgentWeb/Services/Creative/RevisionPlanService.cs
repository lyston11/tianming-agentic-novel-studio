using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Production;

namespace TM.Web.NovelAgentWeb.Services.Creative;

public sealed class RevisionPlanService : IRevisionPlanService
{
    private readonly NovelAgentDbContext _db;

    public RevisionPlanService(NovelAgentDbContext db)
    {
        _db = db;
    }

    public async Task<RevisionPlanItem?> CreateAsync(
        CreateRevisionPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) ||
            string.IsNullOrWhiteSpace(request.ProjectId) ||
            string.IsNullOrWhiteSpace(request.Recommendation))
        {
            return null;
        }

        var projectExists = await _db.NovelProjects
            .AsNoTracking()
            .AnyAsync(p => p.Id == request.ProjectId && p.UserId == request.UserId, cancellationToken)
            .ConfigureAwait(false);
        if (!projectExists)
            return null;

        var idempotencyKey = EmptyToNull(request.IdempotencyKey);
        if (idempotencyKey != null)
        {
            var existing = await FindByIdempotencyKeyAsync(
                    request.UserId,
                    request.ProjectId,
                    idempotencyKey,
                    cancellationToken)
                .ConfigureAwait(false);
            if (existing != null)
                return ToItem(existing);
        }

        var targetChapterId = EmptyToNull(request.TargetChapterId);
        var canonicalTargetChapterId = await ChapterIdentityResolver.ResolveCanonicalChapterIdAsync(
                _db,
                request.ProjectId,
                targetChapterId,
                cancellationToken)
            .ConfigureAwait(false);
        var targetIdentity = await ResolveTargetChapterDisplayIdentityAsync(
                request.ProjectId,
                targetChapterId,
                canonicalTargetChapterId,
                cancellationToken)
            .ConfigureAwait(false);
        var affectedChapterIds = await ChapterIdentityResolver.NormalizeChapterIdsJsonArrayAsync(
                _db,
                request.ProjectId,
                request.AffectedChapterIdsJson,
                cancellationToken)
            .ConfigureAwait(false);
        var impactAnalysisJson = MergeChapterIdentityMetadata(
            NormalizeJsonObject(request.ImpactAnalysisJson),
            targetChapterId,
            canonicalTargetChapterId,
            affectedChapterIds.OriginalIds,
            affectedChapterIds.CanonicalIds);

        var now = DateTime.UtcNow;
        var plan = new RevisionPlan
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = request.UserId,
            ProjectId = request.ProjectId,
            CreativeIntentId = EmptyToNull(request.CreativeIntentId),
            KnowledgeConflictReportId = EmptyToNull(request.KnowledgeConflictReportId),
            SessionId = EmptyToNull(request.SessionId),
            RuntimeRunId = EmptyToNull(request.RunId),
            IdempotencyKey = idempotencyKey,
            Source = NormalizeSource(request.Source),
            PlanType = NormalizePlanType(request.PlanType),
            TargetScope = NormalizeScope(request.TargetScope),
            TargetVolumeId = EmptyToNull(request.TargetVolumeId),
            TargetChapterId = canonicalTargetChapterId,
            TargetChapterLogicalId = EmptyToNull(targetIdentity.LogicalId),
            TargetChapterDisplayName = EmptyToNull(targetIdentity.DisplayName),
            Status = NormalizeStatus(request.Status),
            RequirementsJson = NormalizeJsonArray(request.RequirementsJson),
            ContinuityRequirementsJson = NormalizeJsonArray(request.ContinuityRequirementsJson),
            ImpactAnalysisJson = impactAnalysisJson,
            AffectedChapterIdsJson = JsonSerializer.Serialize(affectedChapterIds.CanonicalIds),
            InvalidatedPackageIdsJson = NormalizeJsonArray(request.InvalidatedPackageIdsJson),
            RiskLevel = NormalizeRisk(request.RiskLevel),
            Recommendation = request.Recommendation.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.RevisionPlans.Add(plan);
        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException) when (idempotencyKey != null)
        {
            _db.Entry(plan).State = EntityState.Detached;
            var existing = await FindByIdempotencyKeyAsync(
                    request.UserId,
                    request.ProjectId,
                    idempotencyKey,
                    cancellationToken)
                .ConfigureAwait(false);
            if (existing != null)
                return ToItem(existing);

            throw;
        }

        return ToItem(plan);
    }

    public async Task<RevisionPlanQueryResult> QueryAsync(
        QueryRevisionPlansRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.ProjectId))
        {
            return new RevisionPlanQueryResult
            {
                ProjectId = request.ProjectId,
                Status = string.IsNullOrWhiteSpace(request.Status) ? "all" : NormalizeStatus(request.Status),
                TargetChapterId = request.TargetChapterId
            };
        }

        var status = string.IsNullOrWhiteSpace(request.Status) ? string.Empty : NormalizeStatus(request.Status);
        var targetChapterId = request.TargetChapterId.Trim();
        var canonicalTargetChapterId = await ChapterIdentityResolver.ResolveCanonicalChapterIdAsync(
                _db,
                request.ProjectId,
                targetChapterId,
                cancellationToken)
            .ConfigureAwait(false);
        var targetChapterCandidates = new[] { targetChapterId, canonicalTargetChapterId }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var limit = Math.Clamp(request.Limit, 1, 80);
        var query = _db.RevisionPlans
            .AsNoTracking()
            .Where(plan => plan.UserId == request.UserId && plan.ProjectId == request.ProjectId);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(plan => plan.Status == status);
        if (targetChapterCandidates.Length > 0)
            query = query.Where(plan => plan.TargetChapterId == null || targetChapterCandidates.Contains(plan.TargetChapterId));

        var plans = await query
            .OrderByDescending(plan => plan.UpdatedAt)
            .ThenByDescending(plan => plan.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new RevisionPlanQueryResult
        {
            ProjectId = request.ProjectId,
            Status = string.IsNullOrWhiteSpace(status) ? "all" : status,
            TargetChapterId = canonicalTargetChapterId ?? targetChapterId,
            Items = plans.Select(ToItem).ToList()
        };
    }

    private Task<RevisionPlan?> FindByIdempotencyKeyAsync(
        string userId,
        string projectId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        _db.RevisionPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(plan =>
                    plan.UserId == userId &&
                    plan.ProjectId == projectId &&
                    plan.IdempotencyKey == idempotencyKey,
                cancellationToken);

    private static RevisionPlanItem ToItem(RevisionPlan plan) => new()
    {
        Id = plan.Id,
        ProjectId = plan.ProjectId,
        CreativeIntentId = plan.CreativeIntentId ?? string.Empty,
        KnowledgeConflictReportId = plan.KnowledgeConflictReportId ?? string.Empty,
        SessionId = plan.SessionId ?? string.Empty,
        RunId = plan.RuntimeRunId ?? string.Empty,
        Source = plan.Source,
        PlanType = plan.PlanType,
        TargetScope = plan.TargetScope,
        TargetVolumeId = plan.TargetVolumeId ?? string.Empty,
        TargetChapterId = plan.TargetChapterId ?? string.Empty,
        TargetChapterLogicalId = FirstNonEmpty(plan.TargetChapterLogicalId, ExtractLogicalTargetChapterId(plan.ImpactAnalysisJson)),
        TargetChapterDisplayName = FirstNonEmpty(plan.TargetChapterDisplayName, plan.TargetChapterLogicalId, plan.TargetChapterId),
        Status = plan.Status,
        RequirementsJson = plan.RequirementsJson,
        ContinuityRequirementsJson = plan.ContinuityRequirementsJson,
        ImpactAnalysisJson = plan.ImpactAnalysisJson,
        AffectedChapterIdsJson = plan.AffectedChapterIdsJson,
        InvalidatedPackageIdsJson = plan.InvalidatedPackageIdsJson,
        RiskLevel = plan.RiskLevel,
        Recommendation = plan.Recommendation,
        CreatedAt = plan.CreatedAt,
        UpdatedAt = plan.UpdatedAt
    };

    private static string NormalizeSource(string value) =>
        NormalizeAllowed(value, "creative_intent", "creative_intent", "knowledge_conflict_resolution", "workflow_comment", "agent_review", "user_request");

    private static string NormalizePlanType(string value) =>
        NormalizeAllowed(value, "future_carry", "minor_edit", "chapter_rewrite", "future_carry", "mainline_change", "world_rule_change", "character_arc_change");

    private static string NormalizeScope(string value) =>
        NormalizeAllowed(value, "project", "book", "project", "volume", "chapter", "character", "subplot", "world_rule");

    private static string NormalizeStatus(string value) =>
        NormalizeAllowed(value, "draft", "draft", "accepted", "ready_for_rebuild", "executing", "executed", "rejected", "superseded");

    private static string NormalizeRisk(string value) =>
        NormalizeAllowed(value, "medium", "low", "medium", "high", "critical");

    private static string NormalizeAllowed(string value, string fallback, params string[] allowed)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return allowed.FirstOrDefault(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase)) ?? fallback;
    }

    private static string NormalizeJsonObject(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "{}";

        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Object ? value.Trim() : "{}";
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(new { raw = value.Trim() });
        }
    }

    private static string NormalizeJsonArray(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "[]";

        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Array ? value.Trim() : "[]";
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(new[] { value.Trim() });
        }
    }

    private static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<ChapterDisplayIdentity> ResolveTargetChapterDisplayIdentityAsync(
        string projectId,
        string? logicalTargetChapterId,
        string? canonicalTargetChapterId,
        CancellationToken cancellationToken)
    {
        var logicalId = logicalTargetChapterId?.Trim() ?? string.Empty;
        var canonicalId = canonicalTargetChapterId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(canonicalId))
            return new ChapterDisplayIdentity(logicalId, logicalId);

        var chapter = await _db.Chapters
            .AsNoTracking()
            .Where(chapter => chapter.ProjectId == projectId && chapter.Id == canonicalId)
            .Select(chapter => new
            {
                chapter.Title,
                chapter.ChapterNumber
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (chapter == null)
            return new ChapterDisplayIdentity(logicalId, FirstNonEmpty(logicalId, canonicalId));

        var fallbackLogicalId = FirstNonEmpty(logicalId, chapter.ChapterNumber > 0 ? $"chapter-{chapter.ChapterNumber:000}" : canonicalId);
        var displayName = FirstNonEmpty(
            NormalizeChapterDisplayName(chapter.Title, chapter.ChapterNumber),
            fallbackLogicalId,
            canonicalId);
        return new ChapterDisplayIdentity(fallbackLogicalId, displayName);
    }

    private static string NormalizeChapterDisplayName(string? title, int chapterNumber)
    {
        var normalizedTitle = title?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(normalizedTitle) &&
            !normalizedTitle.StartsWith("chapter-", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedTitle;
        }

        return chapterNumber > 0 ? $"第{chapterNumber}章" : normalizedTitle;
    }

    private static string ExtractLogicalTargetChapterId(string? impactAnalysisJson)
    {
        if (string.IsNullOrWhiteSpace(impactAnalysisJson))
            return string.Empty;

        try
        {
            using var document = JsonDocument.Parse(impactAnalysisJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("chapterIdentity", out var chapterIdentity) ||
                chapterIdentity.ValueKind != JsonValueKind.Object ||
                !chapterIdentity.TryGetProperty("logicalTargetChapterId", out var logicalTarget) ||
                logicalTarget.ValueKind != JsonValueKind.String)
            {
                return string.Empty;
            }

            return logicalTarget.GetString()?.Trim() ?? string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string MergeChapterIdentityMetadata(
        string impactAnalysisJson,
        string? originalTargetChapterId,
        string? canonicalTargetChapterId,
        IReadOnlyList<string> originalAffectedChapterIds,
        IReadOnlyList<string> canonicalAffectedChapterIds)
    {
        var targetChanged = HasText(originalTargetChapterId) &&
            HasText(canonicalTargetChapterId) &&
            !string.Equals(originalTargetChapterId, canonicalTargetChapterId, StringComparison.OrdinalIgnoreCase);
        var affectedChanged = !JsonArraysEqual(originalAffectedChapterIds, canonicalAffectedChapterIds);
        if (!targetChanged && !affectedChanged)
            return impactAnalysisJson;

        JsonObject root;
        try
        {
            root = JsonNode.Parse(impactAnalysisJson)?.AsObject() ?? new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject
            {
                ["raw"] = impactAnalysisJson
            };
        }

        root["chapterIdentity"] = new JsonObject
        {
            ["targetChapterId"] = canonicalTargetChapterId ?? string.Empty,
            ["logicalTargetChapterId"] = originalTargetChapterId ?? string.Empty,
            ["affectedChapterIds"] = ToJsonArray(canonicalAffectedChapterIds),
            ["logicalAffectedChapterIds"] = ToJsonArray(originalAffectedChapterIds)
        };

        return root.ToJsonString();
    }

    private static JsonArray ToJsonArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
            array.Add(value);
        return array;
    }

    private static bool JsonArraysEqual(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right) =>
        left.Count == right.Count &&
        !left.Where((value, index) => !string.Equals(value, right[index], StringComparison.OrdinalIgnoreCase)).Any();

    private static bool HasText(string? value) =>
        !string.IsNullOrWhiteSpace(value);

    private sealed record ChapterDisplayIdentity(string LogicalId, string DisplayName);
}
