using Microsoft.EntityFrameworkCore;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Production;

public class ProductionTruthStore : IProductionTruthStore
{
    private readonly NovelAgentDbContext _db;

    public ProductionTruthStore(NovelAgentDbContext db)
    {
        _db = db;
    }

    public async Task<ChapterVersion> CreateChapterVersionAsync(
        CreateChapterVersionRequest request,
        CancellationToken cancellationToken = default)
    {
        var nextVersion = await _db.ChapterVersions
            .Where(v => v.ChapterId == request.ChapterId)
            .Select(v => (int?)v.VersionNumber)
            .MaxAsync(cancellationToken) ?? 0;

        var version = new ChapterVersion
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = request.UserId,
            ProjectId = request.ProjectId,
            ChapterId = request.ChapterId,
            ContentDocumentId = request.ContentDocumentId,
            VersionNumber = nextVersion + 1,
            Title = request.Title,
            WordCount = request.WordCount,
            Status = request.Status,
            RuntimeRunId = request.RuntimeRunId,
            PackageId = request.PackageId,
            GateReportJson = request.GateReportJson,
            AgentReviewJson = request.AgentReviewJson,
            CreatedAt = DateTime.UtcNow
        };

        _db.ChapterVersions.Add(version);

        var chapter = await _db.Chapters.SingleAsync(c => c.Id == request.ChapterId, cancellationToken);
        chapter.CurrentDocumentId = request.ContentDocumentId;
        chapter.Title = request.Title;
        chapter.WordCount = request.WordCount;
        chapter.Status = request.Status;
        chapter.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return version;
    }

    public async Task<ChapterVersion> AttachLatestChapterVersionToRunAsync(
        string projectId,
        string chapterId,
        string runtimeRunId,
        string? PackageId,
        string? GateReportJson,
        string? AgentReviewJson,
        CancellationToken cancellationToken = default)
    {
        var version = await _db.ChapterVersions
            .Where(v => v.ProjectId == projectId && v.ChapterId == chapterId)
            .OrderByDescending(v => v.VersionNumber)
            .ThenByDescending(v => v.CreatedAt)
            .FirstAsync(cancellationToken);

        version.RuntimeRunId = runtimeRunId;
        if (!string.IsNullOrWhiteSpace(PackageId))
            version.PackageId = PackageId;
        if (!string.IsNullOrWhiteSpace(GateReportJson))
            version.GateReportJson = GateReportJson;
        if (!string.IsNullOrWhiteSpace(AgentReviewJson))
            version.AgentReviewJson = AgentReviewJson;

        var outboxEvents = await _db.OutboxEvents
            .Where(e => e.AggregateType == "chapter_version" && e.AggregateId == version.Id)
            .ToListAsync(cancellationToken);
        foreach (var evt in outboxEvents)
        {
            if (string.IsNullOrWhiteSpace(evt.RuntimeRunId))
                evt.RuntimeRunId = runtimeRunId;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return version;
    }

    public async Task<TianmingPackage> CreatePackageAsync(
        CreateTianmingPackageRequest request,
        CancellationToken cancellationToken = default)
    {
        var package = new TianmingPackage
        {
            Id = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString("N") : request.Id,
            UserId = request.UserId,
            ProjectId = request.ProjectId,
            ChapterId = request.ChapterId,
            RuntimeRunId = request.RuntimeRunId,
            PackageKind = request.PackageKind,
            Status = "pending",
            InputJson = request.InputJson,
            DependencyVersionsJson = request.DependencyVersionsJson,
            KnowledgeSnapshotJson = request.KnowledgeSnapshotJson,
            FactSnapshotJson = request.FactSnapshotJson,
            PromptVersion = request.PromptVersion,
            KernelVersion = request.KernelVersion,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.TianmingPackages.Add(package);
        await _db.SaveChangesAsync(cancellationToken);
        return package;
    }

    public Task<TianmingPackage?> GetPackageAsync(
        string projectId,
        string runtimeRunId,
        string packageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId) ||
            string.IsNullOrWhiteSpace(runtimeRunId) ||
            string.IsNullOrWhiteSpace(packageId))
        {
            return Task.FromResult<TianmingPackage?>(null);
        }

        return _db.TianmingPackages
            .FirstOrDefaultAsync(package =>
                package.Id == packageId &&
                package.ProjectId == projectId &&
                package.RuntimeRunId == runtimeRunId,
                cancellationToken);
    }

    public Task<bool> PackageExistsAsync(
        string projectId,
        string runtimeRunId,
        string packageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId) ||
            string.IsNullOrWhiteSpace(runtimeRunId) ||
            string.IsNullOrWhiteSpace(packageId))
        {
            return Task.FromResult(false);
        }

        return _db.TianmingPackages
            .AsNoTracking()
            .AnyAsync(package =>
                package.Id == packageId &&
                package.ProjectId == projectId &&
                package.RuntimeRunId == runtimeRunId,
                cancellationToken);
    }

    public async Task<ProductionEvent> AppendEventAsync(
        CreateProductionEventRequest request,
        CancellationToken cancellationToken = default)
    {
        var canonicalStage = NovelAgentProductionStages.ToCanonicalStage(request.Stage);
        var existing = await _db.ProductionEvents
            .FirstOrDefaultAsync(evt =>
                evt.UserId == request.UserId &&
                evt.ProjectId == request.ProjectId &&
                evt.RuntimeRunId == request.RuntimeRunId &&
                evt.ChapterId == request.ChapterId &&
                evt.PackageId == request.PackageId &&
                evt.EventType == request.EventType &&
                evt.Stage == canonicalStage &&
                evt.Status == request.Status &&
                evt.Message == request.Message &&
                evt.ArtifactType == request.ArtifactType &&
                evt.ArtifactId == request.ArtifactId &&
                evt.DataJson == request.DataJson,
                cancellationToken)
            .ConfigureAwait(false);
        if (existing != null)
        {
            return existing;
        }

        var evt = new ProductionEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            RuntimeRunId = request.RuntimeRunId,
            UserId = request.UserId,
            ProjectId = request.ProjectId,
            ChapterId = request.ChapterId,
            PackageId = request.PackageId,
            EventType = request.EventType,
            Stage = canonicalStage,
            Status = request.Status,
            Message = request.Message,
            ArtifactType = request.ArtifactType,
            ArtifactId = request.ArtifactId,
            DataJson = request.DataJson,
            CreatedAt = DateTime.UtcNow
        };

        _db.ProductionEvents.Add(evt);
        if (!string.IsNullOrWhiteSpace(request.PackageId))
        {
            var package = await _db.TianmingPackages
                .FirstOrDefaultAsync(p => p.Id == request.PackageId && p.ProjectId == request.ProjectId, cancellationToken)
                .ConfigureAwait(false);
            if (package != null)
            {
                package.Status = request.Status;
                package.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return evt;
    }

    public async Task<ChapterDraft> CreateChapterDraftAsync(
        CreateChapterDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RuntimeRunId) ||
            string.IsNullOrWhiteSpace(request.UserId) ||
            string.IsNullOrWhiteSpace(request.ProjectId) ||
            string.IsNullOrWhiteSpace(request.ChapterId) ||
            string.IsNullOrWhiteSpace(request.ArtifactId))
        {
            throw new InvalidOperationException("Chapter draft requires runtimeRunId, userId, projectId, chapterId and artifactId.");
        }

        var existing = await _db.ChapterDrafts
            .FirstOrDefaultAsync(draft =>
                draft.UserId == request.UserId &&
                draft.ProjectId == request.ProjectId &&
                draft.RuntimeRunId == request.RuntimeRunId &&
                draft.ChapterId == request.ChapterId &&
                draft.ArtifactId == request.ArtifactId,
                cancellationToken)
            .ConfigureAwait(false);
        if (existing != null)
        {
            existing.PackageId = string.IsNullOrWhiteSpace(request.PackageId)
                ? existing.PackageId
                : request.PackageId;
            existing.Status = string.IsNullOrWhiteSpace(request.Status)
                ? existing.Status
                : request.Status;
            existing.DraftContent = request.DraftContent ?? string.Empty;
            existing.ChangesJson = request.ChangesJson;
            existing.ContentLength = request.DraftContent?.Length ?? 0;
            existing.RepairAttemptCount = Math.Max(0, request.RepairAttemptCount);
            existing.HasChanges = request.HasChanges;
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return existing;
        }

        var draft = new ChapterDraft
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = request.UserId,
            ProjectId = request.ProjectId,
            RuntimeRunId = request.RuntimeRunId,
            ChapterId = request.ChapterId,
            PackageId = request.PackageId,
            ArtifactId = request.ArtifactId,
            Status = string.IsNullOrWhiteSpace(request.Status) ? "draft_generated" : request.Status,
            DraftContent = request.DraftContent ?? string.Empty,
            ChangesJson = request.ChangesJson,
            ContentLength = request.DraftContent?.Length ?? 0,
            RepairAttemptCount = Math.Max(0, request.RepairAttemptCount),
            HasChanges = request.HasChanges,
            GeneratedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.ChapterDrafts.Add(draft);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return draft;
    }

    public async Task<ChapterChange> CreateChapterChangeAsync(
        CreateChapterChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RuntimeRunId) ||
            string.IsNullOrWhiteSpace(request.UserId) ||
            string.IsNullOrWhiteSpace(request.ProjectId) ||
            string.IsNullOrWhiteSpace(request.ChapterId))
        {
            throw new InvalidOperationException("Chapter change requires runtimeRunId, userId, projectId and chapterId.");
        }

        var changesJson = string.IsNullOrWhiteSpace(request.ChangesJson) ? "{}" : request.ChangesJson;
        var canonicalChangesJson = string.IsNullOrWhiteSpace(request.CanonicalChangesJson) ? "{}" : request.CanonicalChangesJson;
        var parseStatus = string.IsNullOrWhiteSpace(request.ParseStatus) ? "unknown" : request.ParseStatus;
        var existing = await _db.ChapterChanges
            .FirstOrDefaultAsync(change =>
                change.UserId == request.UserId &&
                change.ProjectId == request.ProjectId &&
                change.RuntimeRunId == request.RuntimeRunId &&
                change.ChapterId == request.ChapterId &&
                change.PackageId == request.PackageId &&
                change.ChangesJson == changesJson &&
                change.CanonicalChangesJson == canonicalChangesJson &&
                change.ParseStatus == parseStatus &&
                change.ParseError == request.ParseError,
                cancellationToken)
            .ConfigureAwait(false);
        if (existing != null)
            return existing;

        var change = new ChapterChange
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = request.UserId,
            ProjectId = request.ProjectId,
            RuntimeRunId = request.RuntimeRunId,
            ChapterId = request.ChapterId,
            PackageId = request.PackageId,
            ChangesJson = changesJson,
            CanonicalChangesJson = canonicalChangesJson,
            ParseStatus = parseStatus,
            ParseError = request.ParseError,
            AppliedToFactSnapshot = request.AppliedToFactSnapshot,
            AppliedAt = request.AppliedToFactSnapshot ? DateTime.UtcNow : null,
            CreatedAt = DateTime.UtcNow
        };

        _db.ChapterChanges.Add(change);
        await _db.SaveChangesAsync(cancellationToken);
        return change;
    }

    public async Task<GenerationGateReportRecord> CreateGenerationGateReportAsync(
        CreateGenerationGateReportRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RuntimeRunId) ||
            string.IsNullOrWhiteSpace(request.UserId) ||
            string.IsNullOrWhiteSpace(request.ProjectId) ||
            string.IsNullOrWhiteSpace(request.ChapterId))
        {
            throw new InvalidOperationException("Generation gate report requires runtimeRunId, userId, projectId and chapterId.");
        }

        var artifactId = string.IsNullOrWhiteSpace(request.ArtifactId) ? "generation_gate_report" : request.ArtifactId;
        var status = string.IsNullOrWhiteSpace(request.Status) ? "pending" : request.Status;
        var reportJson = string.IsNullOrWhiteSpace(request.ReportJson) ? "{}" : request.ReportJson;
        var existing = await _db.GenerationGateReports
            .FirstOrDefaultAsync(report =>
                report.UserId == request.UserId &&
                report.ProjectId == request.ProjectId &&
                report.RuntimeRunId == request.RuntimeRunId &&
                report.ChapterId == request.ChapterId &&
                report.PackageId == request.PackageId &&
                report.ArtifactId == artifactId &&
                report.Status == status &&
                report.ReportJson == reportJson &&
                report.ProtocolPassed == request.ProtocolPassed &&
                report.ChangesDetected == request.ChangesDetected &&
                report.FactSnapshotPassed == request.FactSnapshotPassed &&
                report.BlueprintPassed == request.BlueprintPassed &&
                report.RagPassed == request.RagPassed &&
                report.IssueCount == Math.Max(0, request.IssueCount) &&
                report.RepairHintCount == Math.Max(0, request.RepairHintCount),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing != null)
            return existing;

        var report = new GenerationGateReportRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = request.UserId,
            ProjectId = request.ProjectId,
            RuntimeRunId = request.RuntimeRunId,
            ChapterId = request.ChapterId,
            PackageId = request.PackageId,
            ArtifactId = artifactId,
            Status = status,
            ReportJson = reportJson,
            ProtocolPassed = request.ProtocolPassed,
            ChangesDetected = request.ChangesDetected,
            FactSnapshotPassed = request.FactSnapshotPassed,
            BlueprintPassed = request.BlueprintPassed,
            RagPassed = request.RagPassed,
            IssueCount = Math.Max(0, request.IssueCount),
            RepairHintCount = Math.Max(0, request.RepairHintCount),
            ValidatedAt = request.ValidatedAt == default ? DateTime.UtcNow : request.ValidatedAt,
            CreatedAt = DateTime.UtcNow
        };

        _db.GenerationGateReports.Add(report);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return report;
    }

    public async Task<AgentReviewRecord> CreateAgentReviewAsync(
        CreateAgentReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RuntimeRunId) ||
            string.IsNullOrWhiteSpace(request.UserId) ||
            string.IsNullOrWhiteSpace(request.ProjectId) ||
            string.IsNullOrWhiteSpace(request.ChapterId))
        {
            throw new InvalidOperationException("Agent review requires runtimeRunId, userId, projectId and chapterId.");
        }

        var reviewId = string.IsNullOrWhiteSpace(request.ReviewId)
            ? Guid.NewGuid().ToString("N")
            : request.ReviewId;
        var packageId = string.IsNullOrWhiteSpace(request.PackageId) ? null : request.PackageId;

        var existing = await _db.AgentReviews.FirstOrDefaultAsync(review =>
                review.UserId == request.UserId &&
                review.ProjectId == request.ProjectId &&
                review.RuntimeRunId == request.RuntimeRunId &&
                review.ChapterId == request.ChapterId &&
                review.PackageId == packageId &&
                review.ReviewId == reviewId,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing != null)
        {
            existing.OverallResult = string.IsNullOrWhiteSpace(request.OverallResult) ? "Unknown" : request.OverallResult;
            existing.ValidationOverallResult = request.ValidationOverallResult ?? string.Empty;
            existing.RequiresRewrite = request.RequiresRewrite;
            existing.QualityScore = request.QualityScore;
            existing.ContentLength = request.ContentLength;
            existing.CheckCount = Math.Max(0, request.CheckCount);
            existing.Summary = request.Summary ?? string.Empty;
            existing.MeetsAcceptedCreativeIntents = request.MeetsAcceptedCreativeIntents;
            existing.ContinuityRisk = request.ContinuityRisk ?? string.Empty;
            existing.ChapterPacing = request.ChapterPacing ?? string.Empty;
            existing.RecommendedAction = request.RecommendedAction ?? string.Empty;
            existing.ReviewJson = string.IsNullOrWhiteSpace(request.ReviewJson) ? "{}" : request.ReviewJson;
            existing.ReviewedAt = request.ReviewedAt == default ? DateTime.UtcNow : request.ReviewedAt;
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return existing;
        }

        var review = new AgentReviewRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = request.UserId,
            ProjectId = request.ProjectId,
            RuntimeRunId = request.RuntimeRunId,
            ChapterId = request.ChapterId,
            PackageId = packageId,
            ReviewId = reviewId,
            OverallResult = string.IsNullOrWhiteSpace(request.OverallResult) ? "Unknown" : request.OverallResult,
            ValidationOverallResult = request.ValidationOverallResult ?? string.Empty,
            RequiresRewrite = request.RequiresRewrite,
            QualityScore = request.QualityScore,
            ContentLength = request.ContentLength,
            CheckCount = Math.Max(0, request.CheckCount),
            Summary = request.Summary ?? string.Empty,
            MeetsAcceptedCreativeIntents = request.MeetsAcceptedCreativeIntents,
            ContinuityRisk = request.ContinuityRisk ?? string.Empty,
            ChapterPacing = request.ChapterPacing ?? string.Empty,
            RecommendedAction = request.RecommendedAction ?? string.Empty,
            ReviewJson = string.IsNullOrWhiteSpace(request.ReviewJson) ? "{}" : request.ReviewJson,
            ReviewedAt = request.ReviewedAt == default ? DateTime.UtcNow : request.ReviewedAt,
            CreatedAt = DateTime.UtcNow
        };

        _db.AgentReviews.Add(review);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return review;
    }

    public async Task<int> MarkChapterChangesAppliedToFactSnapshotAsync(
        MarkChapterChangesAppliedToFactSnapshotRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) ||
            string.IsNullOrWhiteSpace(request.ProjectId) ||
            string.IsNullOrWhiteSpace(request.RuntimeRunId) ||
            string.IsNullOrWhiteSpace(request.ChapterId))
        {
            return 0;
        }

        var chapterIdCandidates = BuildChapterIdCandidates(request.ChapterId);
        var query = _db.ChapterChanges
            .Where(change =>
                change.UserId == request.UserId &&
                change.ProjectId == request.ProjectId &&
                change.RuntimeRunId == request.RuntimeRunId &&
                chapterIdCandidates.Contains(change.ChapterId) &&
                !change.AppliedToFactSnapshot);
        if (!string.IsNullOrWhiteSpace(request.PackageId))
        {
            query = query.Where(change =>
                change.PackageId == request.PackageId ||
                change.PackageId == null ||
                change.PackageId == string.Empty);
        }

        var changes = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        if (changes.Count == 0)
            return 0;

        var now = DateTime.UtcNow;
        foreach (var change in changes)
        {
            change.AppliedToFactSnapshot = true;
            change.AppliedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return changes.Count;
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

    private static int ExtractTrailingNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        var index = value.Length - 1;
        while (index >= 0 && char.IsDigit(value[index]))
            index--;
        return index == value.Length - 1 || !int.TryParse(value[(index + 1)..], out var number) ? 0 : number;
    }

    public async Task<IReadOnlyList<ProductionEvent>> GetEventsForRunAsync(
        string runtimeRunId,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        return await _db.ProductionEvents
            .Where(e => e.RuntimeRunId == runtimeRunId)
            .OrderBy(e => e.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProductionEvent>> GetEventsForChapterAsync(
        string projectId,
        string chapterId,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        return await _db.ProductionEvents
            .Where(e => e.ProjectId == projectId && e.ChapterId == chapterId)
            .OrderBy(e => e.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<ProjectFactSnapshot> SaveFactSnapshotAsync(
        SaveProjectFactSnapshotRequest request,
        CancellationToken cancellationToken = default)
    {
        var nextVersion = await _db.ProjectFactSnapshots
            .Where(s => s.ProjectId == request.ProjectId && s.ChapterId == request.ChapterId)
            .Select(s => (int?)s.VersionNumber)
            .MaxAsync(cancellationToken) ?? 0;

        var snapshot = new ProjectFactSnapshot
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = request.UserId,
            ProjectId = request.ProjectId,
            ChapterId = request.ChapterId,
            ChapterVersionId = request.ChapterVersionId,
            VersionNumber = nextVersion + 1,
            SnapshotJson = request.SnapshotJson,
            Source = request.Source,
            CreatedAt = DateTime.UtcNow
        };

        _db.ProjectFactSnapshots.Add(snapshot);
        await _db.SaveChangesAsync(cancellationToken);
        return snapshot;
    }

    public async Task<ProjectFactSnapshot?> GetLatestFactSnapshotAsync(
        string projectId,
        string? chapterId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.ProjectFactSnapshots
            .Where(s => s.ProjectId == projectId);

        if (!string.IsNullOrWhiteSpace(chapterId))
        {
            query = query.Where(s => s.ChapterId == chapterId);
        }

        return await query
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<OutboxEvent> EnqueueOutboxAsync(
        EnqueueOutboxEventRequest request,
        CancellationToken cancellationToken = default)
    {
        var evt = new OutboxEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = request.UserId,
            ProjectId = request.ProjectId,
            RuntimeRunId = request.RuntimeRunId,
            EventType = request.EventType,
            AggregateType = request.AggregateType,
            AggregateId = request.AggregateId,
            PayloadJson = request.PayloadJson,
            Status = "pending",
            Attempts = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.OutboxEvents.Add(evt);
        await _db.SaveChangesAsync(cancellationToken);
        return evt;
    }

    public async Task MarkOutboxCompletedAsync(string outboxEventId, CancellationToken cancellationToken = default)
    {
        var evt = await _db.OutboxEvents.SingleAsync(e => e.Id == outboxEventId, cancellationToken);
        evt.Status = "completed";
        evt.CompletedAt = DateTime.UtcNow;
        evt.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkOutboxFailedAsync(
        string outboxEventId,
        string error,
        bool retryable,
        DateTime? nextAttemptAt = null,
        CancellationToken cancellationToken = default)
    {
        var evt = await _db.OutboxEvents.SingleAsync(e => e.Id == outboxEventId, cancellationToken);
        evt.Attempts += 1;
        evt.Status = retryable ? "retryable_failed" : "failed";
        evt.LastError = error;
        evt.NextAttemptAt = nextAttemptAt;
        evt.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }
}
