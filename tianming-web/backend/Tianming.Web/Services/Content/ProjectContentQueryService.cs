using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Content;

public sealed class ProjectContentQueryService : IProjectContentQueryService
{
    private readonly NovelAgentDbContext _db;
    private readonly IContentDocumentService _contentDocuments;

    public ProjectContentQueryService(
        NovelAgentDbContext db,
        IContentDocumentService contentDocuments)
    {
        _db = db;
        _contentDocuments = contentDocuments;
    }

    public async Task<ProjectContentQueryResult?> QueryAsync(
        ProjectContentQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.ProjectId))
            return null;

        var userRole = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == request.UserId)
            .Select(u => u.Role)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var isAdmin = string.Equals(userRole, "admin", StringComparison.OrdinalIgnoreCase);

        var projectQuery = _db.NovelProjects.AsNoTracking().Where(p => p.Id == request.ProjectId);
        if (!isAdmin)
            projectQuery = projectQuery.Where(p => p.UserId == request.UserId);

        var project = await projectQuery.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (project == null)
            return null;

        var chapterQuery = _db.Chapters
            .AsNoTracking()
            .Include(c => c.Volume)
            .Where(c => c.ProjectId == project.Id);
        if (!string.IsNullOrWhiteSpace(request.ChapterId))
        {
            var normalizedChapterId = request.ChapterId.Trim();
            var scopedSuffix = "-" + normalizedChapterId;
            var chapterNumberFromId = ExtractTrailingNumber(normalizedChapterId);
            chapterQuery = chapterQuery.Where(c =>
                c.Id == normalizedChapterId ||
                c.Title == normalizedChapterId ||
                c.Id.EndsWith(scopedSuffix) ||
                (chapterNumberFromId > 0 && c.ChapterNumber == chapterNumberFromId));
        }

        if (request.ChapterNumber > 0)
            chapterQuery = chapterQuery.Where(c => c.ChapterNumber == request.ChapterNumber);
        if (request.VolumeNumber > 0)
            chapterQuery = chapterQuery.Where(c => c.Volume != null && c.Volume.VolumeNumber == request.VolumeNumber);

        var chapters = await chapterQuery
            .OrderBy(c => c.ChapterNumber)
            .Take(string.IsNullOrWhiteSpace(request.ChapterId) && request.ChapterNumber <= 0 ? 12 : 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = new List<ProjectContentQueryItem>();
        foreach (var chapter in chapters)
        {
            var selectedVersion = await SelectChapterVersionAsync(
                    request,
                    project.Id,
                    chapter.Id,
                    cancellationToken)
                .ConfigureAwait(false);
            if (IsVersionSpecificQuery(request) && selectedVersion == null)
                continue;

            var body = selectedVersion == null
                ? await ReadChapterBodyAsync(project.UserId, project.Id, chapter.Id, cancellationToken)
                    .ConfigureAwait(false)
                : await ReadChapterBodyByDocumentIdAsync(selectedVersion.ContentDocumentId, cancellationToken)
                .ConfigureAwait(false);
            var facts = request.IncludeFacts
                ? request.StoryBible.ContinuityFacts
                    .Where(f => IsSameChapterIdentity(f.ChapterId, chapter.Id, chapter.ChapterNumber))
                    .ToList()
                : new List<ChapterContinuityFacts>();
            var sourceRun = request.StoryBible.AgentRuns
                .Where(r => IsSameChapterIdentity(
                    FirstNonEmpty(r.TargetChapterId, r.ChapterBrief?.ChapterId),
                    chapter.Id,
                    chapter.ChapterNumber))
                .OrderByDescending(r => r.UpdatedAt)
                .FirstOrDefault();
            var selectedRunId = FirstNonEmpty(selectedVersion?.RuntimeRunId, sourceRun?.RunId);
            var selectedPackageId = selectedVersion?.PackageId ?? string.Empty;
            var versionSpecific = IsVersionSpecificQuery(request);
            var chapterIdCandidates = BuildChapterIdCandidates(chapter.Id);
            var latestFactSnapshotQuery = _db.ProjectFactSnapshots
                .AsNoTracking()
                .Where(s => s.ProjectId == project.Id &&
                            s.ChapterId != null &&
                            chapterIdCandidates.Contains(s.ChapterId));
            if (!string.IsNullOrWhiteSpace(selectedVersion?.Id))
                latestFactSnapshotQuery = latestFactSnapshotQuery.Where(s => s.ChapterVersionId == selectedVersion.Id);
            var latestFactSnapshot = await latestFactSnapshotQuery
                .OrderByDescending(s => s.VersionNumber)
                .ThenByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            var latestPackage = selectedVersion == null || string.IsNullOrWhiteSpace(selectedVersion.PackageId)
                ? null
                : await _db.TianmingPackages
                    .AsNoTracking()
                    .Where(p => p.Id == selectedVersion.PackageId)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
            var productionEventsQuery = _db.ProductionEvents
                .AsNoTracking()
                .Where(e => e.ProjectId == project.Id &&
                            e.ChapterId != null &&
                            chapterIdCandidates.Contains(e.ChapterId));
            productionEventsQuery = FilterVersionEvidence(
                productionEventsQuery,
                versionSpecific,
                selectedRunId,
                selectedPackageId);
            var productionEvents = await productionEventsQuery
                .OrderByDescending(e => e.CreatedAt)
                .Take(8)
                .Select(e => new ProjectContentProductionEvent
                {
                    EventType = e.EventType,
                    Stage = e.Stage,
                    Status = e.Status,
                    Message = e.Message,
                    ArtifactType = e.ArtifactType ?? string.Empty,
                    ArtifactId = e.ArtifactId ?? string.Empty,
                    CreatedAt = e.CreatedAt
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            productionEvents.Reverse();
            var chapterChangesQuery = _db.ChapterChanges
                .AsNoTracking()
                .Where(change => change.ProjectId == project.Id && chapterIdCandidates.Contains(change.ChapterId));
            chapterChangesQuery = FilterVersionEvidence(
                chapterChangesQuery,
                versionSpecific,
                selectedRunId,
                selectedPackageId);
            var chapterChanges = await chapterChangesQuery
                .OrderByDescending(change => change.CreatedAt)
                .Take(6)
                .Select(change => new ProjectContentChapterChange
                {
                    Id = change.Id,
                    RuntimeRunId = change.RuntimeRunId,
                    ChapterId = change.ChapterId,
                    PackageId = change.PackageId ?? string.Empty,
                    ParseStatus = change.ParseStatus,
                    ParseError = change.ParseError ?? string.Empty,
                    AppliedToFactSnapshot = change.AppliedToFactSnapshot,
                    AppliedAt = change.AppliedAt,
                    ChangesJson = change.ChangesJson,
                    CanonicalChangesJson = change.CanonicalChangesJson,
                    CreatedAt = change.CreatedAt
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            chapterChanges.Reverse();
            var generationGateReportsQuery = _db.GenerationGateReports
                .AsNoTracking()
                .Where(report => report.ProjectId == project.Id && chapterIdCandidates.Contains(report.ChapterId));
            generationGateReportsQuery = FilterVersionEvidence(
                generationGateReportsQuery,
                versionSpecific,
                selectedRunId,
                selectedPackageId);
            var generationGateReports = await generationGateReportsQuery
                .OrderByDescending(report => report.CreatedAt)
                .Take(6)
                .Select(report => new ProjectContentGenerationGateReport
                {
                    Id = report.Id,
                    RuntimeRunId = report.RuntimeRunId,
                    ChapterId = report.ChapterId,
                    PackageId = report.PackageId ?? string.Empty,
                    ArtifactId = report.ArtifactId,
                    Status = report.Status,
                    ProtocolPassed = report.ProtocolPassed,
                    ChangesDetected = report.ChangesDetected,
                    FactSnapshotPassed = report.FactSnapshotPassed,
                    BlueprintPassed = report.BlueprintPassed,
                    RagPassed = report.RagPassed,
                    IssueCount = report.IssueCount,
                    RepairHintCount = report.RepairHintCount,
                    ReportJson = report.ReportJson,
                    ValidatedAt = report.ValidatedAt,
                    CreatedAt = report.CreatedAt
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            generationGateReports.Reverse();
            var agentReviewsQuery = _db.AgentReviews
                .AsNoTracking()
                .Where(review => review.ProjectId == project.Id && chapterIdCandidates.Contains(review.ChapterId));
            agentReviewsQuery = FilterVersionEvidence(
                agentReviewsQuery,
                versionSpecific,
                selectedRunId,
                selectedPackageId);
            var agentReviews = await agentReviewsQuery
                .OrderByDescending(review => review.CreatedAt)
                .Take(6)
                .Select(review => new ProjectContentAgentReview
                {
                    Id = review.Id,
                    RuntimeRunId = review.RuntimeRunId,
                    ChapterId = review.ChapterId,
                    PackageId = review.PackageId ?? string.Empty,
                    ReviewId = review.ReviewId,
                    OverallResult = review.OverallResult,
                    ValidationOverallResult = review.ValidationOverallResult,
                    RequiresRewrite = review.RequiresRewrite,
                    QualityScore = review.QualityScore,
                    ContentLength = review.ContentLength,
                    CheckCount = review.CheckCount,
                    Summary = review.Summary,
                    MeetsAcceptedCreativeIntents = review.MeetsAcceptedCreativeIntents,
                    ContinuityRisk = review.ContinuityRisk,
                    ChapterPacing = review.ChapterPacing,
                    RecommendedAction = review.RecommendedAction,
                    ReviewJson = review.ReviewJson,
                    ReviewedAt = review.ReviewedAt,
                    CreatedAt = review.CreatedAt
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            agentReviews.Reverse();
            var directRevisionPlans = await QueryDirectRevisionPlansAsync(
                    project.Id,
                    project.UserId,
            chapterIdCandidates,
            versionSpecific,
            selectedRunId,
            cancellationToken)
                .ConfigureAwait(false);

            items.Add(new ProjectContentQueryItem
            {
                ChapterId = chapter.Id,
                SourceRunId = selectedRunId,
                ChapterNumber = chapter.ChapterNumber,
                ChapterTitle = chapter.Title,
                VolumeId = chapter.VolumeId ?? string.Empty,
                VolumeNumber = chapter.Volume?.VolumeNumber ?? 0,
                VolumeTitle = chapter.Volume?.Title ?? string.Empty,
                WordCount = chapter.WordCount,
                Status = chapter.Status,
                CurrentVersionId = selectedVersion?.Id ?? string.Empty,
                CurrentVersionNumber = selectedVersion?.VersionNumber ?? 0,
                PackageId = selectedVersion?.PackageId ?? latestPackage?.Id ?? string.Empty,
                PackageKind = latestPackage?.PackageKind ?? string.Empty,
                PromptVersion = latestPackage?.PromptVersion ?? string.Empty,
                KernelVersion = latestPackage?.KernelVersion ?? string.Empty,
                FactSnapshotId = latestFactSnapshot?.Id ?? string.Empty,
                FactSnapshotVersion = latestFactSnapshot?.VersionNumber ?? 0,
                FactSnapshotJson = latestFactSnapshot?.SnapshotJson ?? string.Empty,
                ProductionEvents = productionEvents,
                ChapterChanges = chapterChanges,
                GenerationGateReports = generationGateReports,
                AgentReviews = agentReviews,
                KnowledgeBindings = ParseKnowledgeBindings(latestPackage?.KnowledgeSnapshotJson),
                SourceRevisionPlans = MergeRevisionPlanSnapshots(ParseSourceRevisionPlans(
                        latestPackage?.KnowledgeSnapshotJson,
                        latestFactSnapshot?.SnapshotJson)
                    .Concat(directRevisionPlans)),
                RebuiltFromPackageIds = ParseTopLevelStringArray(
                    latestPackage?.KnowledgeSnapshotJson,
                    "rebuiltFromPackageIds"),
                BodyPreview = TrimBody(body, request.IncludeBody ? 2000 : 240),
                Body = request.IncludeBody ? body : string.Empty,
                ContinuityFacts = facts
            });
        }

        return new ProjectContentQueryResult
        {
            ProjectId = project.Id,
            ProjectTitle = project.Title,
            Items = items
        };
    }

    public async Task<ProjectChapterIdentity?> ResolveChapterAsync(
        ProjectChapterIdentityRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.ProjectId))
            return null;

        var isAdmin = await IsAdminAsync(request.UserId, cancellationToken).ConfigureAwait(false);
        var projectQuery = _db.NovelProjects.AsNoTracking().Where(p => p.Id == request.ProjectId);
        if (!isAdmin)
            projectQuery = projectQuery.Where(p => p.UserId == request.UserId);

        var projectExists = await projectQuery.AnyAsync(cancellationToken).ConfigureAwait(false);
        if (!projectExists)
            return null;

        var query = _db.Chapters
            .AsNoTracking()
            .Where(chapter => chapter.ProjectId == request.ProjectId);

        if (request.ChapterNumber > 0)
        {
            return await query
                .Where(chapter => chapter.ChapterNumber == request.ChapterNumber)
                .OrderBy(chapter => chapter.Id)
                .Select(chapter => new ProjectChapterIdentity
                {
                    ChapterId = chapter.Id,
                    ChapterNumber = chapter.ChapterNumber,
                    ChapterTitle = chapter.Title
                })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(request.ChapterId))
            return null;

        var normalized = request.ChapterId.Trim();
        return await query
            .Where(chapter =>
                chapter.Id == normalized ||
                chapter.Id.EndsWith("-" + normalized))
            .OrderBy(chapter => chapter.Id.Length)
            .ThenBy(chapter => chapter.Id)
            .Select(chapter => new ProjectChapterIdentity
            {
                ChapterId = chapter.Id,
                ChapterNumber = chapter.ChapterNumber,
                ChapterTitle = chapter.Title
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> IsAdminAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        var role = await _db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.Role)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ChapterVersion?> SelectChapterVersionAsync(
        ProjectContentQueryRequest request,
        string projectId,
        string chapterId,
        CancellationToken cancellationToken)
    {
        var query = _db.ChapterVersions
            .AsNoTracking()
            .Where(version => version.ProjectId == projectId && version.ChapterId == chapterId);

        if (!string.IsNullOrWhiteSpace(request.VersionId))
        {
            var versionId = request.VersionId.Trim();
            query = query.Where(version => version.Id == versionId);
        }
        else if (request.VersionNumber > 0)
        {
            query = query.Where(version => version.VersionNumber == request.VersionNumber);
        }

        return await query
            .OrderByDescending(version => version.VersionNumber)
            .ThenByDescending(version => version.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool IsVersionSpecificQuery(ProjectContentQueryRequest request) =>
        !string.IsNullOrWhiteSpace(request.VersionId) || request.VersionNumber > 0;

    private async Task<string> ReadChapterBodyAsync(
        string userId,
        string projectId,
        string chapterId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _contentDocuments.GetTextAsync(
                    userId,
                    projectId,
                    "chapter",
                    chapterId,
                    "chapter_body",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            var chunks = await _db.ContentChunks
                .AsNoTracking()
                .Where(c => c.Document.SourceType == "chapter" &&
                            c.Document.SourceId == chapterId &&
                            c.Document.ProjectId == projectId &&
                            c.Document.DocumentRole == "chapter_body" &&
                            c.Document.Status == "active")
                .OrderBy(c => c.ChunkIndex)
                .Select(c => c.ChunkText)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return string.Join("", chunks);
        }
    }

    private async Task<string> ReadChapterBodyByDocumentIdAsync(
        string documentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            return string.Empty;

        var chunks = await _db.ContentChunks
            .AsNoTracking()
            .Where(c => c.DocumentId == documentId)
            .OrderBy(c => c.ChunkIndex)
            .Select(c => c.ChunkText)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return string.Join("", chunks);
    }

    private static IQueryable<ProductionEvent> FilterVersionEvidence(
        IQueryable<ProductionEvent> query,
        bool versionSpecific,
        string runtimeRunId,
        string packageId)
    {
        if (!versionSpecific)
            return query;

        var hasRuntimeRunId = !string.IsNullOrWhiteSpace(runtimeRunId);
        var hasPackageId = !string.IsNullOrWhiteSpace(packageId);
        if (hasRuntimeRunId && hasPackageId)
        {
            return query.Where(item =>
                (item.RuntimeRunId == runtimeRunId && item.PackageId == packageId) ||
                (item.RuntimeRunId == runtimeRunId && (item.PackageId == null || item.PackageId == string.Empty)) ||
                ((item.RuntimeRunId == null || item.RuntimeRunId == string.Empty) && item.PackageId == packageId));
        }

        if (hasRuntimeRunId)
            return query.Where(item => item.RuntimeRunId == runtimeRunId);
        if (hasPackageId)
            return query.Where(item => item.PackageId == packageId);
        return query.Where(_ => false);
    }

    private static IQueryable<ChapterChange> FilterVersionEvidence(
        IQueryable<ChapterChange> query,
        bool versionSpecific,
        string runtimeRunId,
        string packageId)
    {
        if (!versionSpecific)
            return query;

        var hasRuntimeRunId = !string.IsNullOrWhiteSpace(runtimeRunId);
        var hasPackageId = !string.IsNullOrWhiteSpace(packageId);
        if (hasRuntimeRunId && hasPackageId)
        {
            return query.Where(item =>
                (item.RuntimeRunId == runtimeRunId && item.PackageId == packageId) ||
                (item.RuntimeRunId == runtimeRunId && (item.PackageId == null || item.PackageId == string.Empty)) ||
                ((item.RuntimeRunId == null || item.RuntimeRunId == string.Empty) && item.PackageId == packageId));
        }

        if (hasRuntimeRunId)
            return query.Where(item => item.RuntimeRunId == runtimeRunId);
        if (hasPackageId)
            return query.Where(item => item.PackageId == packageId);
        return query.Where(_ => false);
    }

    private static IQueryable<GenerationGateReportRecord> FilterVersionEvidence(
        IQueryable<GenerationGateReportRecord> query,
        bool versionSpecific,
        string runtimeRunId,
        string packageId)
    {
        if (!versionSpecific)
            return query;

        var hasRuntimeRunId = !string.IsNullOrWhiteSpace(runtimeRunId);
        var hasPackageId = !string.IsNullOrWhiteSpace(packageId);
        if (hasRuntimeRunId && hasPackageId)
        {
            return query.Where(item =>
                (item.RuntimeRunId == runtimeRunId && item.PackageId == packageId) ||
                (item.RuntimeRunId == runtimeRunId && (item.PackageId == null || item.PackageId == string.Empty)) ||
                ((item.RuntimeRunId == null || item.RuntimeRunId == string.Empty) && item.PackageId == packageId));
        }

        if (hasRuntimeRunId)
            return query.Where(item => item.RuntimeRunId == runtimeRunId);
        if (hasPackageId)
            return query.Where(item => item.PackageId == packageId);
        return query.Where(_ => false);
    }

    private static IQueryable<AgentReviewRecord> FilterVersionEvidence(
        IQueryable<AgentReviewRecord> query,
        bool versionSpecific,
        string runtimeRunId,
        string packageId)
    {
        if (!versionSpecific)
            return query;

        var hasRuntimeRunId = !string.IsNullOrWhiteSpace(runtimeRunId);
        var hasPackageId = !string.IsNullOrWhiteSpace(packageId);
        if (hasRuntimeRunId && hasPackageId)
        {
            return query.Where(item =>
                (item.RuntimeRunId == runtimeRunId && item.PackageId == packageId) ||
                (item.RuntimeRunId == runtimeRunId && (item.PackageId == null || item.PackageId == string.Empty)) ||
                ((item.RuntimeRunId == null || item.RuntimeRunId == string.Empty) && item.PackageId == packageId));
        }

        if (hasRuntimeRunId)
            return query.Where(item => item.RuntimeRunId == runtimeRunId);
        if (hasPackageId)
            return query.Where(item => item.PackageId == packageId);
        return query.Where(_ => false);
    }

    private async Task<List<RevisionPlanSnapshot>> QueryDirectRevisionPlansAsync(
        string projectId,
        string ownerUserId,
        IReadOnlyList<string> chapterIdCandidates,
        bool versionSpecific,
        string runtimeRunId,
        CancellationToken cancellationToken)
    {
        var plans = await _db.RevisionPlans
            .AsNoTracking()
            .Where(plan => plan.ProjectId == projectId && plan.UserId == ownerUserId)
            .OrderByDescending(plan => plan.CreatedAt)
            .Take(64)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return plans
            .Where(plan => RevisionPlanMatchesChapter(plan, chapterIdCandidates))
            .Where(plan => RevisionPlanMatchesVersion(plan, versionSpecific, runtimeRunId))
            .OrderBy(plan => plan.CreatedAt)
            .Take(24)
            .Select(ToRevisionPlanSnapshot)
            .ToList();
    }

    private static bool RevisionPlanMatchesChapter(
        RevisionPlan plan,
        IReadOnlyList<string> chapterIdCandidates)
    {
        if (chapterIdCandidates.Count == 0)
            return false;

        var planCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in new[]
                 {
                     plan.TargetChapterId,
                     plan.TargetChapterLogicalId
                 })
        {
            foreach (var candidate in BuildChapterIdCandidates(value))
                planCandidates.Add(candidate);
        }

        foreach (var affectedChapterId in ParseRawStringArray(plan.AffectedChapterIdsJson))
        {
            foreach (var candidate in BuildChapterIdCandidates(affectedChapterId))
                planCandidates.Add(candidate);
        }

        return planCandidates.Any(candidate => chapterIdCandidates.Contains(candidate, StringComparer.OrdinalIgnoreCase));
    }

    private static bool RevisionPlanMatchesVersion(
        RevisionPlan plan,
        bool versionSpecific,
        string runtimeRunId)
    {
        if (!versionSpecific)
            return true;

        return !string.IsNullOrWhiteSpace(runtimeRunId) &&
               string.Equals(plan.RuntimeRunId, runtimeRunId, StringComparison.OrdinalIgnoreCase);
    }

    private static RevisionPlanSnapshot ToRevisionPlanSnapshot(RevisionPlan plan) => new()
    {
        RevisionPlanId = plan.Id,
        PlanType = plan.PlanType,
        TargetScope = plan.TargetScope,
        TargetChapterId = plan.TargetChapterId ?? string.Empty,
        TargetChapterLogicalId = FirstNonEmpty(plan.TargetChapterLogicalId, plan.TargetChapterId),
        TargetChapterDisplayName = FirstNonEmpty(plan.TargetChapterDisplayName, plan.TargetChapterLogicalId, plan.TargetChapterId),
        Status = plan.Status,
        RequirementsJson = plan.RequirementsJson,
        ContinuityRequirementsJson = plan.ContinuityRequirementsJson,
        AffectedChapterIdsJson = plan.AffectedChapterIdsJson,
        InvalidatedPackageIdsJson = plan.InvalidatedPackageIdsJson,
        RiskLevel = plan.RiskLevel,
        Recommendation = plan.Recommendation
    };

    private static bool IsSameChapterIdentity(string? candidate, string chapterId, int chapterNumber)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(chapterId))
            return false;

        var normalized = candidate.Trim();
        if (string.Equals(normalized, chapterId, StringComparison.OrdinalIgnoreCase))
            return true;
        if (chapterId.EndsWith("-" + normalized, StringComparison.OrdinalIgnoreCase))
            return true;

        var candidateNumber = ExtractTrailingNumber(normalized);
        return candidateNumber > 0 && chapterNumber > 0 && candidateNumber == chapterNumber;
    }

    private static int ExtractTrailingNumber(string value)
    {
        var index = value.Length - 1;
        while (index >= 0 && char.IsDigit(value[index]))
            index--;
        return index == value.Length - 1 || !int.TryParse(value[(index + 1)..], out var number) ? 0 : number;
    }

    private static List<string> BuildChapterIdCandidates(string? chapterId)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = chapterId?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            candidates.Add(normalized);
            var markerIndex = normalized.LastIndexOf("chapter-", StringComparison.OrdinalIgnoreCase);
            if (markerIndex >= 0)
                candidates.Add(normalized[markerIndex..]);
        }

        var number = ExtractTrailingNumber(normalized);
        if (number > 0)
        {
            candidates.Add($"chapter-{number:000}");
            candidates.Add($"chapter-{number}");
        }

        return candidates.ToList();
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static List<BoundKnowledgeSnapshot> ParseKnowledgeBindings(string? knowledgeSnapshotJson)
    {
        if (string.IsNullOrWhiteSpace(knowledgeSnapshotJson))
            return new List<BoundKnowledgeSnapshot>();

        try
        {
            using var doc = JsonDocument.Parse(knowledgeSnapshotJson);
            if (doc.RootElement.TryGetProperty("knowledgeBindings", out var bindings) &&
                bindings.ValueKind == JsonValueKind.Array)
            {
                return JsonSerializer.Deserialize<List<BoundKnowledgeSnapshot>>(bindings.GetRawText())?
                    .Where(binding => !string.IsNullOrWhiteSpace(binding.KnowledgeId) ||
                                      !string.IsNullOrWhiteSpace(binding.Title))
                    .Take(24)
                    .ToList() ?? new List<BoundKnowledgeSnapshot>();
            }

            if (doc.RootElement.TryGetProperty("bindings", out var legacyBindings) &&
                legacyBindings.ValueKind == JsonValueKind.Array)
            {
                return legacyBindings
                    .EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                    .Select(item =>
                    {
                        var id = item.GetString()!.Trim();
                        return new BoundKnowledgeSnapshot
                        {
                            KnowledgeId = id,
                            Title = id
                        };
                    })
                    .Take(24)
                    .ToList();
            }
        }
        catch (JsonException)
        {
            return new List<BoundKnowledgeSnapshot>();
        }

        return new List<BoundKnowledgeSnapshot>();
    }

    private static List<RevisionPlanSnapshot> ParseSourceRevisionPlans(
        string? packageKnowledgeSnapshotJson,
        string? factSnapshotJson)
    {
        return MergeRevisionPlanSnapshots(ParseSourceRevisionPlansFromJson(packageKnowledgeSnapshotJson)
            .Concat(ParseSourceRevisionPlansFromJson(factSnapshotJson)));
    }

    private static List<RevisionPlanSnapshot> MergeRevisionPlanSnapshots(
        IEnumerable<RevisionPlanSnapshot> sourcePlans)
    {
        var plans = new Dictionary<string, RevisionPlanSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in sourcePlans)
        {
            var key = FirstNonEmpty(
                plan.RevisionPlanId,
                $"{plan.TargetChapterId}:{plan.PlanType}:{plan.Recommendation}");
            if (string.IsNullOrWhiteSpace(key))
                continue;

            plans[key] = plans.TryGetValue(key, out var existing)
                ? MergeRevisionPlanSnapshot(existing, plan)
                : plan;
        }

        return plans.Values.Take(24).ToList();
    }

    private static IReadOnlyList<string> ParseRawStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();

            return document.RootElement
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static RevisionPlanSnapshot MergeRevisionPlanSnapshot(
        RevisionPlanSnapshot existing,
        RevisionPlanSnapshot incoming) => new()
    {
        RevisionPlanId = FirstNonEmpty(incoming.RevisionPlanId, existing.RevisionPlanId),
        PlanType = FirstNonEmpty(incoming.PlanType, existing.PlanType),
        TargetScope = FirstNonEmpty(incoming.TargetScope, existing.TargetScope),
        TargetChapterId = FirstNonEmpty(incoming.TargetChapterId, existing.TargetChapterId),
        TargetChapterLogicalId = FirstNonEmpty(incoming.TargetChapterLogicalId, existing.TargetChapterLogicalId, incoming.TargetChapterId, existing.TargetChapterId),
        TargetChapterDisplayName = FirstNonEmpty(incoming.TargetChapterDisplayName, existing.TargetChapterDisplayName, incoming.TargetChapterLogicalId, existing.TargetChapterLogicalId, incoming.TargetChapterId, existing.TargetChapterId),
        Status = FirstNonEmpty(incoming.Status, existing.Status),
        RequirementsJson = FirstNonEmpty(incoming.RequirementsJson, existing.RequirementsJson, "[]"),
        ContinuityRequirementsJson = FirstNonEmpty(incoming.ContinuityRequirementsJson, existing.ContinuityRequirementsJson, "[]"),
        AffectedChapterIdsJson = FirstNonEmpty(incoming.AffectedChapterIdsJson, existing.AffectedChapterIdsJson, "[]"),
        InvalidatedPackageIdsJson = FirstNonEmpty(incoming.InvalidatedPackageIdsJson, existing.InvalidatedPackageIdsJson, "[]"),
        RiskLevel = FirstNonEmpty(incoming.RiskLevel, existing.RiskLevel),
        Recommendation = FirstNonEmpty(incoming.Recommendation, existing.Recommendation)
    };

    private static IEnumerable<RevisionPlanSnapshot> ParseSourceRevisionPlansFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            yield break;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("sourceRevisionPlans", out var property) ||
                property.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }

            foreach (var item in property.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                RevisionPlanSnapshot? snapshot;
                try
                {
                    snapshot = JsonSerializer.Deserialize<RevisionPlanSnapshot>(item.GetRawText());
                }
                catch (JsonException)
                {
                    continue;
                }

                if (snapshot != null &&
                    (!string.IsNullOrWhiteSpace(snapshot.RevisionPlanId) ||
                     !string.IsNullOrWhiteSpace(snapshot.Recommendation)))
                {
                    yield return snapshot;
                }
            }
        }
    }

    private static List<string> ParseTopLevelStringArray(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(propertyName))
            return new List<string>();

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(propertyName, out var property) ||
                property.ValueKind != JsonValueKind.Array)
            {
                return new List<string>();
            }

            return property
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(24)
                .ToList();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static string TrimBody(string body, int maxLength)
    {
        var text = string.IsNullOrWhiteSpace(body)
            ? string.Empty
            : body.Replace("\r\n", "\n").Trim();
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
