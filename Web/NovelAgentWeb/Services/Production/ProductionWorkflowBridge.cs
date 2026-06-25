using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class ProductionWorkflowBridge : IProductionWorkflowBridge
{
    private readonly NovelAgentDbContext _db;

    public ProductionWorkflowBridge(NovelAgentDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<WorkflowProductionEventSummary>> LoadProjectEventsAsync(
        string projectId,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        var events = await _db.ProductionEvents
            .AsNoTracking()
            .Where(evt => evt.ProjectId == projectId)
            .OrderByDescending(evt => evt.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (events.Count == 0)
            return Array.Empty<WorkflowProductionEventSummary>();

        var projectChapters = await _db.Chapters
            .AsNoTracking()
            .Where(chapter => chapter.ProjectId == projectId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var chapterAliases = BuildChapterAliasMap(projectId, projectChapters);
        var packageIds = events
            .Select(evt => evt.PackageId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var runIds = events
            .Select(evt => evt.RuntimeRunId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var chapterIds = events
            .Select(evt => evt.ChapterId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var normalizedChapterIds = chapterIds
            .Select(chapterId => NormalizeChapterId(chapterId, chapterAliases))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Concat(chapterIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var outboxIds = events
            .Where(evt => string.Equals(evt.ArtifactType, "outbox_event", StringComparison.OrdinalIgnoreCase))
            .Select(evt => evt.ArtifactId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var packages = packageIds.Count == 0
            ? new Dictionary<string, TianmingPackage>(StringComparer.OrdinalIgnoreCase)
            : await _db.TianmingPackages
                .AsNoTracking()
                .Where(package => package.ProjectId == projectId && packageIds.Contains(package.Id))
                .ToDictionaryAsync(package => package.Id, StringComparer.OrdinalIgnoreCase, cancellationToken)
                .ConfigureAwait(false);
        var rebuiltSourcePackageIds = packages.Values
            .SelectMany(ParsePackageRebuiltFromPackageIds)
            .Where(id => !packages.ContainsKey(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (rebuiltSourcePackageIds.Count > 0)
        {
            var rebuiltSourcePackages = await _db.TianmingPackages
                .AsNoTracking()
                .Where(package => package.ProjectId == projectId && rebuiltSourcePackageIds.Contains(package.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var sourcePackage in rebuiltSourcePackages)
                packages[sourcePackage.Id] = sourcePackage;
        }

        var chapterVersions = await _db.ChapterVersions
            .AsNoTracking()
            .Where(version => version.ProjectId == projectId)
            .Where(version =>
                runIds.Contains(version.RuntimeRunId ?? string.Empty) ||
                packageIds.Contains(version.PackageId ?? string.Empty) ||
                normalizedChapterIds.Contains(version.ChapterId))
            .OrderByDescending(version => version.VersionNumber)
            .ThenByDescending(version => version.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var chapterVersionIds = chapterVersions
            .Select(version => version.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var factSnapshots = await _db.ProjectFactSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.ProjectId == projectId)
            .Where(snapshot =>
                normalizedChapterIds.Contains(snapshot.ChapterId ?? string.Empty) ||
                chapterVersionIds.Contains(snapshot.ChapterVersionId ?? string.Empty))
            .OrderByDescending(snapshot => snapshot.VersionNumber)
            .ThenByDescending(snapshot => snapshot.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var gateReports = await _db.GenerationGateReports
            .AsNoTracking()
            .Where(report => report.ProjectId == projectId)
            .Where(report =>
                runIds.Contains(report.RuntimeRunId) ||
                packageIds.Contains(report.PackageId ?? string.Empty) ||
                normalizedChapterIds.Contains(report.ChapterId))
            .OrderByDescending(report => report.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var agentReviews = await _db.AgentReviews
            .AsNoTracking()
            .Where(review => review.ProjectId == projectId)
            .Where(review =>
                runIds.Contains(review.RuntimeRunId) ||
                packageIds.Contains(review.PackageId ?? string.Empty) ||
                normalizedChapterIds.Contains(review.ChapterId))
            .OrderByDescending(review => review.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var memoryReads = runIds.Count == 0
            ? new List<AgentMemoryRead>()
            : await _db.AgentMemoryReads
                .AsNoTracking()
                .Where(read => read.ProjectId == projectId)
                .Where(read => runIds.Contains(read.RunId ?? string.Empty))
                .OrderByDescending(read => read.CreatedAt)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        var memoryPromotions = runIds.Count == 0
            ? new List<AgentMemoryPromotion>()
            : await _db.AgentMemoryPromotions
                .AsNoTracking()
                .Where(promotion => promotion.ProjectId == projectId)
                .Where(promotion => runIds.Contains(promotion.RunId ?? string.Empty))
                .OrderByDescending(promotion => promotion.CreatedAt)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        var outboxes = outboxIds.Count == 0
            ? new Dictionary<string, OutboxEvent>(StringComparer.OrdinalIgnoreCase)
            : await _db.OutboxEvents
                .AsNoTracking()
                .Where(outbox => outbox.ProjectId == projectId && outboxIds.Contains(outbox.Id))
                .ToDictionaryAsync(outbox => outbox.Id, StringComparer.OrdinalIgnoreCase, cancellationToken)
                .ConfigureAwait(false);

        return events
            .Select(evt =>
            {
                packages.TryGetValue(evt.PackageId ?? string.Empty, out var package);
                outboxes.TryGetValue(evt.ArtifactId ?? string.Empty, out var outbox);
                var normalizedChapterId = NormalizeChapterId(
                    FirstNonEmpty(evt.ChapterId, package?.ChapterId),
                    chapterAliases);
                var chapterVersion = FindChapterVersion(evt, normalizedChapterId, chapterVersions);
                var factSnapshot = FindFactSnapshot(evt, normalizedChapterId, chapterVersion, factSnapshots);
                var gateReport = FindGateReport(evt, normalizedChapterId, gateReports);
                var agentReview = FindAgentReview(evt, normalizedChapterId, agentReviews);
                var eventMemoryReads = FindMemoryReads(evt, memoryReads);
                var eventMemoryPromotions = FindMemoryPromotions(evt, memoryPromotions);
                var summaryChapterId = FirstNonEmpty(chapterVersion?.ChapterId, factSnapshot?.ChapterId, normalizedChapterId, evt.ChapterId);

                return new WorkflowProductionEventSummary(
                    evt.Id,
                    evt.RuntimeRunId,
                    summaryChapterId,
                    evt.PackageId ?? string.Empty,
                    evt.EventType,
                    evt.Stage,
                    evt.Status,
                    evt.Message,
                    evt.ArtifactType ?? string.Empty,
                    evt.ArtifactId ?? string.Empty,
                    evt.DataJson ?? string.Empty,
                    evt.CreatedAt.ToString("O"),
                    BuildProductionEvidence(evt, package, packages, chapterVersion, factSnapshot, gateReport, agentReview, eventMemoryReads, eventMemoryPromotions, outbox),
                    ToWorkflowFailure(ProductionFailureContractMapper.FromEvent(evt)));
            })
            .ToList();
    }

    private static Dictionary<string, string> BuildChapterAliasMap(
        string projectId,
        IEnumerable<Chapter> chapters)
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var chapter in chapters)
        {
            if (string.IsNullOrWhiteSpace(chapter.Id))
                continue;

            aliases[chapter.Id] = chapter.Id;
            var projectPrefix = $"{projectId}-";
            if (chapter.Id.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var shortId = chapter.Id[projectPrefix.Length..];
                if (!string.IsNullOrWhiteSpace(shortId))
                    aliases.TryAdd(shortId, chapter.Id);
            }
        }

        return aliases;
    }

    private static string NormalizeChapterId(
        string? chapterId,
        IReadOnlyDictionary<string, string> aliases)
    {
        if (string.IsNullOrWhiteSpace(chapterId))
            return string.Empty;

        return aliases.TryGetValue(chapterId, out var normalized)
            ? normalized
            : chapterId;
    }

    private static WorkflowProductionFailureSummary? ToWorkflowFailure(ProductionFailureContract? failure) =>
        failure == null
            ? null
            : new WorkflowProductionFailureSummary(
                failure.Code,
                failure.Stage,
                failure.Message,
                failure.Recoverable,
                failure.RecommendedAction,
                failure.ArtifactIds,
                failure.RequiresUserDecision);

    private static ChapterVersion? FindChapterVersion(
        ProductionEvent evt,
        string normalizedChapterId,
        IReadOnlyList<ChapterVersion> versions)
    {
        return versions.FirstOrDefault(version =>
                string.Equals(evt.ArtifactType, "chapter_version", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(evt.ArtifactId) &&
                string.Equals(version.Id, evt.ArtifactId, StringComparison.OrdinalIgnoreCase))
            ?? versions.FirstOrDefault(version =>
                !string.IsNullOrWhiteSpace(evt.RuntimeRunId) &&
                string.Equals(version.RuntimeRunId, evt.RuntimeRunId, StringComparison.OrdinalIgnoreCase))
            ?? versions.FirstOrDefault(version =>
                !string.IsNullOrWhiteSpace(evt.PackageId) &&
                string.Equals(version.PackageId, evt.PackageId, StringComparison.OrdinalIgnoreCase))
            ?? versions.FirstOrDefault(version =>
                !string.IsNullOrWhiteSpace(normalizedChapterId) &&
                string.Equals(version.ChapterId, normalizedChapterId, StringComparison.OrdinalIgnoreCase));
    }

    private static ProjectFactSnapshot? FindFactSnapshot(
        ProductionEvent evt,
        string normalizedChapterId,
        ChapterVersion? chapterVersion,
        IReadOnlyList<ProjectFactSnapshot> snapshots)
    {
        if (chapterVersion != null)
        {
            var byVersion = snapshots.FirstOrDefault(snapshot =>
                string.Equals(snapshot.ChapterVersionId, chapterVersion.Id, StringComparison.OrdinalIgnoreCase));
            if (byVersion != null)
                return byVersion;
        }

        return snapshots.FirstOrDefault(snapshot =>
            !string.IsNullOrWhiteSpace(normalizedChapterId) &&
            string.Equals(snapshot.ChapterId, normalizedChapterId, StringComparison.OrdinalIgnoreCase));
    }

    private static GenerationGateReportRecord? FindGateReport(
        ProductionEvent evt,
        string normalizedChapterId,
        IReadOnlyList<GenerationGateReportRecord> reports)
    {
        return reports.FirstOrDefault(report =>
                !string.IsNullOrWhiteSpace(evt.ArtifactId) &&
                string.Equals(report.Id, evt.ArtifactId, StringComparison.OrdinalIgnoreCase))
            ?? reports.FirstOrDefault(report =>
                !string.IsNullOrWhiteSpace(evt.RuntimeRunId) &&
                string.Equals(report.RuntimeRunId, evt.RuntimeRunId, StringComparison.OrdinalIgnoreCase))
            ?? reports.FirstOrDefault(report =>
                !string.IsNullOrWhiteSpace(evt.PackageId) &&
                string.Equals(report.PackageId, evt.PackageId, StringComparison.OrdinalIgnoreCase))
            ?? reports.FirstOrDefault(report =>
                !string.IsNullOrWhiteSpace(normalizedChapterId) &&
                string.Equals(report.ChapterId, normalizedChapterId, StringComparison.OrdinalIgnoreCase));
    }

    private static AgentReviewRecord? FindAgentReview(
        ProductionEvent evt,
        string normalizedChapterId,
        IReadOnlyList<AgentReviewRecord> reviews)
    {
        return reviews.FirstOrDefault(review =>
                !string.IsNullOrWhiteSpace(evt.ArtifactId) &&
                (string.Equals(review.Id, evt.ArtifactId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(review.ReviewId, evt.ArtifactId, StringComparison.OrdinalIgnoreCase)))
            ?? reviews.FirstOrDefault(review =>
                !string.IsNullOrWhiteSpace(evt.RuntimeRunId) &&
                string.Equals(review.RuntimeRunId, evt.RuntimeRunId, StringComparison.OrdinalIgnoreCase))
            ?? reviews.FirstOrDefault(review =>
                !string.IsNullOrWhiteSpace(evt.PackageId) &&
                string.Equals(review.PackageId, evt.PackageId, StringComparison.OrdinalIgnoreCase))
            ?? reviews.FirstOrDefault(review =>
                !string.IsNullOrWhiteSpace(normalizedChapterId) &&
                string.Equals(review.ChapterId, normalizedChapterId, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<AgentMemoryRead> FindMemoryReads(
        ProductionEvent evt,
        IReadOnlyList<AgentMemoryRead> reads) =>
        string.IsNullOrWhiteSpace(evt.RuntimeRunId)
            ? Array.Empty<AgentMemoryRead>()
            : reads
                .Where(read => string.Equals(read.RunId, evt.RuntimeRunId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(read => read.CreatedAt)
                .Take(12)
                .ToList();

    private static IReadOnlyList<AgentMemoryPromotion> FindMemoryPromotions(
        ProductionEvent evt,
        IReadOnlyList<AgentMemoryPromotion> promotions) =>
        string.IsNullOrWhiteSpace(evt.RuntimeRunId)
            ? Array.Empty<AgentMemoryPromotion>()
            : promotions
                .Where(promotion => string.Equals(promotion.RunId, evt.RuntimeRunId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(promotion => promotion.CreatedAt)
                .Take(12)
                .ToList();

    private static WorkflowProductionEvidenceSummary? BuildProductionEvidence(
        ProductionEvent evt,
        TianmingPackage? package,
        IReadOnlyDictionary<string, TianmingPackage> packages,
        ChapterVersion? chapterVersion,
        ProjectFactSnapshot? factSnapshot,
        GenerationGateReportRecord? gateReport,
        AgentReviewRecord? agentReview,
        IReadOnlyList<AgentMemoryRead> memoryReads,
        IReadOnlyList<AgentMemoryPromotion> memoryPromotions,
        OutboxEvent? outbox)
    {
        if (package == null &&
            chapterVersion == null &&
            factSnapshot == null &&
            gateReport == null &&
            agentReview == null &&
            memoryReads.Count == 0 &&
            memoryPromotions.Count == 0 &&
            outbox == null &&
            !HasEventDataEvidence(evt))
        {
            return null;
        }

        var knowledgeBindingJson = FirstNonEmpty(
            package?.KnowledgeSnapshotJson,
            IsKnowledgeBindingsUsedEvent(evt) ? evt.DataJson : string.Empty);

        return new WorkflowProductionEvidenceSummary(
            package?.PackageKind ?? string.Empty,
            package?.Status ?? string.Empty,
            package?.PromptVersion ?? string.Empty,
            package?.KernelVersion ?? string.Empty,
            CountJsonArray(package?.KnowledgeSnapshotJson, "hardContinuityFacts"),
            CountJsonArray(package?.KnowledgeSnapshotJson, "ragQueries"),
            factSnapshot?.VersionNumber ?? 0,
            factSnapshot?.Source ?? string.Empty,
            factSnapshot?.Id ?? string.Empty,
            chapterVersion?.VersionNumber ?? 0,
            chapterVersion?.Id ?? string.Empty,
            chapterVersion?.Status ?? string.Empty,
            chapterVersion?.WordCount ?? 0,
            CountJsonArray(package?.KnowledgeSnapshotJson, "acceptedCreativeIntents"),
            ParseKnowledgeBindings(knowledgeBindingJson, evt),
            ParseCreativeIntents(package?.KnowledgeSnapshotJson, evt),
            ParseAgentReviewChecks(FirstNonEmpty(agentReview?.ReviewJson, chapterVersion?.AgentReviewJson)),
            ParseKnowledgeConstraintEvidence(factSnapshot))
        {
            SourceRevisionPlans = ParseRevisionPlans(package?.KnowledgeSnapshotJson, factSnapshot?.SnapshotJson),
            RebuiltFromPackageIds = ParsePackageRebuiltFromPackageIds(package),
            RebuildLinks = BuildPackageRebuildLinks(package, packages),
            Outbox = ToWorkflowOutboxEvidence(outbox),
            Rollback = ParseRollbackEvidence(evt),
            Gate = ParseGateEvidence(FirstNonEmpty(gateReport?.ReportJson, chapterVersion?.GateReportJson)),
            FactSnapshot = ParseFactSnapshotEvidence(factSnapshot?.SnapshotJson),
            AgentReview = ParseAgentReviewSummary(agentReview, chapterVersion?.AgentReviewJson),
            KnowledgeBindingSummary = ParseKnowledgeBindingSummary(package?.KnowledgeSnapshotJson),
            MemoryReads = memoryReads.Select(ToWorkflowMemoryReadEvidence).ToList(),
            MemoryPromotions = memoryPromotions.Select(ToWorkflowMemoryPromotionEvidence).ToList()
        };
    }

    private static bool HasEventDataEvidence(ProductionEvent evt) =>
        IsKnowledgeBindingsUsedEvent(evt);

    private static bool IsKnowledgeBindingsUsedEvent(ProductionEvent evt) =>
        string.Equals(evt.EventType, "knowledge_bindings_used", StringComparison.OrdinalIgnoreCase);

    private static WorkflowMemoryReadEvidence ToWorkflowMemoryReadEvidence(AgentMemoryRead read) =>
        new(
            read.Id,
            read.ProjectId ?? string.Empty,
            read.SessionId ?? string.Empty,
            read.RunId ?? string.Empty,
            read.MemoryScope,
            ParseJsonStringArray(read.MemoryKeysJson),
            read.SourceType,
            read.Consumer,
            read.CreatedAt.ToString("O"));

    private static WorkflowMemoryPromotionEvidence ToWorkflowMemoryPromotionEvidence(AgentMemoryPromotion promotion) =>
        new(
            promotion.Id,
            promotion.ProjectId ?? string.Empty,
            promotion.SessionId ?? string.Empty,
            promotion.RunId ?? string.Empty,
            promotion.SourceScope,
            promotion.TargetScope,
            promotion.SourceMemoryKey,
            promotion.TargetMemoryKey,
            promotion.PromotionReason,
            promotion.CreatedAt.ToString("O"));

    private static WorkflowGateEvidence? ParseGateEvidence(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        using var document = TryParseJson(json);
        if (document == null || document.RootElement.ValueKind != JsonValueKind.Object)
            return null;

        var root = document.RootElement;
        var issues = GetJsonFlexibleStringArray(root, "issues");
        var repairHints = GetJsonFlexibleStringArray(root, "repairHints");
        var status = FirstNonEmpty(GetJsonString(root, "status"), GetJsonString(root, "gateStatus"));
        if (string.IsNullOrWhiteSpace(status) && issues.Count == 0 && repairHints.Count == 0)
            return null;

        return new WorkflowGateEvidence(
            status,
            GetJsonBool(root, "protocolPassed"),
            GetJsonBool(root, "factSnapshotPassed"),
            GetJsonBool(root, "blueprintPassed"),
            GetJsonBool(root, "ragPassed"),
            GetJsonBool(root, "changesDetected"),
            issues,
            repairHints);
    }

    private static WorkflowFactSnapshotEvidence? ParseFactSnapshotEvidence(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        using var document = TryParseJson(json);
        if (document == null || document.RootElement.ValueKind != JsonValueKind.Object)
            return null;

        var root = document.RootElement;
        var evidence = new WorkflowFactSnapshotEvidence(
            FirstNonEmpty(GetJsonString(root, "protagonistName"), GetJsonString(root, "protagonist")),
            FirstNonEmpty(GetJsonString(root, "protagonistIdentity"), GetJsonString(root, "identity")),
            FirstNonEmpty(GetJsonString(root, "protagonistStatus"), GetJsonString(root, "status")),
            FirstNonEmpty(GetJsonString(root, "currentLocation"), GetJsonString(root, "location")),
            FirstNonEmpty(GetJsonString(root, "systemState"), GetJsonString(root, "system")),
            FirstNonEmpty(GetJsonString(root, "equipmentState"), GetJsonString(root, "equipment")),
            GetJsonFlexibleStringArray(root, "keyEvents"),
            FirstNonEmpty(GetJsonString(root, "endingState"), GetJsonString(root, "ending")),
            GetJsonFlexibleStringArray(root, "nextChapterMustCarry"));

        return HasFactSnapshotEvidence(evidence) ? evidence : null;
    }

    private static bool HasFactSnapshotEvidence(WorkflowFactSnapshotEvidence evidence) =>
        !string.IsNullOrWhiteSpace(evidence.ProtagonistName) ||
        !string.IsNullOrWhiteSpace(evidence.ProtagonistIdentity) ||
        !string.IsNullOrWhiteSpace(evidence.ProtagonistStatus) ||
        !string.IsNullOrWhiteSpace(evidence.CurrentLocation) ||
        !string.IsNullOrWhiteSpace(evidence.SystemState) ||
        !string.IsNullOrWhiteSpace(evidence.EquipmentState) ||
        evidence.KeyEvents.Count > 0 ||
        !string.IsNullOrWhiteSpace(evidence.EndingState) ||
        evidence.NextChapterMustCarry.Count > 0;

    private static WorkflowAgentReviewSummaryEvidence? ParseAgentReviewSummary(
        AgentReviewRecord? review,
        string? fallbackJson)
    {
        var json = FirstNonEmpty(review?.ReviewJson, fallbackJson);
        var decision = string.Empty;
        var overallResult = review?.OverallResult ?? string.Empty;
        IReadOnlyList<string> problems = Array.Empty<string>();
        IReadOnlyList<string> suggestions = Array.Empty<string>();
        bool? meetsAcceptedCreativeIntents = review == null ? null : review.MeetsAcceptedCreativeIntents;
        var continuityRisk = review?.ContinuityRisk ?? string.Empty;
        var chapterPacing = review?.ChapterPacing ?? string.Empty;
        var recommendedAction = review?.RecommendedAction ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(json))
        {
            using var document = TryParseJson(json);
            if (document != null && document.RootElement.ValueKind == JsonValueKind.Object)
            {
                var root = document.RootElement;
                problems = GetJsonFlexibleStringArray(root, "problems");
                suggestions = GetJsonFlexibleStringArray(root, "suggestions");
                decision = GetJsonString(root, "decision");
                overallResult = FirstNonEmpty(
                    overallResult,
                    GetJsonString(root, "overallResult"),
                    GetJsonString(root, "status"));
                meetsAcceptedCreativeIntents ??= GetJsonNullableBool(root, "meetsAcceptedCreativeIntents");
                continuityRisk = FirstNonEmpty(continuityRisk, GetJsonString(root, "continuityRisk"));
                chapterPacing = FirstNonEmpty(chapterPacing, GetJsonString(root, "chapterPacing"));
                recommendedAction = FirstNonEmpty(recommendedAction, GetJsonString(root, "recommendedAction"));
            }
        }

        decision = FirstNonEmpty(decision, recommendedAction);
        if (string.IsNullOrWhiteSpace(decision) &&
            string.IsNullOrWhiteSpace(overallResult) &&
            problems.Count == 0 &&
            suggestions.Count == 0 &&
            meetsAcceptedCreativeIntents == null &&
            string.IsNullOrWhiteSpace(continuityRisk) &&
            string.IsNullOrWhiteSpace(chapterPacing) &&
            string.IsNullOrWhiteSpace(recommendedAction))
        {
            return null;
        }

        return new WorkflowAgentReviewSummaryEvidence(
            decision,
            overallResult,
            problems,
            suggestions,
            meetsAcceptedCreativeIntents,
            continuityRisk,
            chapterPacing,
            recommendedAction);
    }

    private static WorkflowOutboxEvidence? ToWorkflowOutboxEvidence(OutboxEvent? outbox) =>
        outbox == null
            ? null
            : new WorkflowOutboxEvidence(
                outbox.Id,
                outbox.EventType,
                outbox.AggregateType,
                outbox.AggregateId,
                outbox.Status,
                outbox.Attempts,
                outbox.LastError ?? string.Empty);

    private static IReadOnlyList<WorkflowPackageRebuildLinkEvidence> BuildPackageRebuildLinks(
        TianmingPackage? package,
        IReadOnlyDictionary<string, TianmingPackage> packages)
    {
        if (package == null)
            return Array.Empty<WorkflowPackageRebuildLinkEvidence>();

        return ParsePackageRebuiltFromPackageIds(package)
            .Select(oldPackageId =>
            {
                packages.TryGetValue(oldPackageId, out var oldPackage);
                return new WorkflowPackageRebuildLinkEvidence(
                    oldPackageId,
                    oldPackage?.Status ?? string.Empty,
                    package.Id,
                    package.Status,
                    package.PackageKind,
                    package.ChapterId ?? string.Empty,
                    package.RuntimeRunId ?? string.Empty);
            })
            .Take(24)
            .ToList();
    }

    private static WorkflowRollbackEvidence? ParseRollbackEvidence(ProductionEvent evt)
    {
        if (!string.Equals(evt.EventType, "chapter_version_rolled_back", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(evt.DataJson))
        {
            return null;
        }

        using var document = TryParseJson(evt.DataJson);
        if (document == null || document.RootElement.ValueKind != JsonValueKind.Object)
            return null;

        var root = document.RootElement;
        var targetVersionId = FirstNonEmpty(GetJsonString(root, "targetVersionId"), evt.ArtifactId);
        if (string.IsNullOrWhiteSpace(targetVersionId))
            return null;

        return new WorkflowRollbackEvidence(
            targetVersionId,
            GetJsonInt(root, "targetVersionNumber"),
            GetJsonString(root, "currentDocumentId"),
            GetJsonStringArray(root, "invalidatedPackageIds"),
            GetJsonString(root, "reason"));
    }

    private static int CountJsonArray(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return 0;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(propertyName, out var property) ||
                property.ValueKind != JsonValueKind.Array)
                return 0;

            return property.GetArrayLength();
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static IReadOnlyList<WorkflowKnowledgeBindingEvidence> ParseKnowledgeBindings(
        string? json,
        ProductionEvent evt)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<WorkflowKnowledgeBindingEvidence>();

        try
        {
            using var document = JsonDocument.Parse(json);
            if ((!document.RootElement.TryGetProperty("knowledgeBindings", out var property) ||
                 property.ValueKind != JsonValueKind.Array) &&
                (!document.RootElement.TryGetProperty("bindings", out property) ||
                 property.ValueKind != JsonValueKind.Array))
            {
                return Array.Empty<WorkflowKnowledgeBindingEvidence>();
            }

            var usedKnowledgeIds = ParseUsedKnowledgeIds(evt);
            return property.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item =>
                {
                    var knowledgeId = GetJsonString(item, "knowledgeId");
                    return new WorkflowKnowledgeBindingEvidence(
                        knowledgeId,
                        GetJsonString(item, "title"),
                        GetJsonString(item, "entryType"),
                        IsKnowledgeBindingsUsedEvent(evt) || usedKnowledgeIds.Contains(knowledgeId)
                            ? "used"
                            : GetJsonString(item, "projectUsageStatus"),
                        GetJsonInt(item, "weight"))
                    {
                        Role = GetJsonString(item, "role"),
                        ConstraintLevel = GetJsonString(item, "constraintLevel"),
                        PackagePolicy = GetJsonString(item, "packagePolicy"),
                        ClassificationId = GetJsonString(item, "classificationId"),
                        ClassificationRule = GetJsonString(item, "classificationRule"),
                        ShouldEnterGate = GetJsonBool(item, "shouldEnterGate"),
                        ShouldEnterBlueprint = GetJsonBool(item, "shouldEnterBlueprint"),
                        ShouldEnterFactSnapshot = GetJsonBool(item, "shouldEnterFactSnapshot"),
                        ClassificationConfidence = GetJsonDouble(item, "classificationConfidence")
                    };
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.KnowledgeId) ||
                               !string.IsNullOrWhiteSpace(item.Title))
                .Take(12)
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<WorkflowKnowledgeBindingEvidence>();
        }
    }

    private static HashSet<string> ParseUsedKnowledgeIds(ProductionEvent evt)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.Equals(evt.EventType, "knowledge_bindings_used", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(evt.DataJson))
        {
            return ids;
        }

        try
        {
            using var document = JsonDocument.Parse(evt.DataJson);
            if (!document.RootElement.TryGetProperty("knowledgeIds", out var property) ||
                property.ValueKind != JsonValueKind.Array)
                return ids;

            foreach (var item in property.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    ids.Add(item.GetString() ?? string.Empty);
            }
        }
        catch (JsonException)
        {
        }

        return ids;
    }

    private static WorkflowKnowledgeBindingSummaryEvidence? ParseKnowledgeBindingSummary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("knowledgeBindingSummary", out var summary) ||
                summary.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return new WorkflowKnowledgeBindingSummaryEvidence(
                GetJsonInt(summary, "bindingCount"),
                GetJsonInt(summary, "shouldEnterGateCount"),
                GetJsonInt(summary, "shouldEnterBlueprintCount"),
                GetJsonInt(summary, "shouldEnterFactSnapshotCount"),
                GetJsonInt(summary, "hardConstraintCount"),
                GetJsonInt(summary, "referenceCount"),
                GetJsonInt(summary, "classifiedCount"),
                GetJsonInt(summary, "pendingClassificationCount"),
                GetJsonInt(summary, "importedCount"),
                GetJsonInt(summary, "referencedCount"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<WorkflowCreativeIntentEvidence> ParseCreativeIntents(
        string? json,
        ProductionEvent evt)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<WorkflowCreativeIntentEvidence>();

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("acceptedCreativeIntents", out var property) ||
                property.ValueKind != JsonValueKind.Array)
                return Array.Empty<WorkflowCreativeIntentEvidence>();

            var executedIntentIds = ParseExecutedIntentIds(evt);
            return property.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item =>
                {
                    var intentId = GetJsonString(item, "intentId");
                    return new WorkflowCreativeIntentEvidence(
                        intentId,
                        GetJsonString(item, "normalizedIntent"),
                        GetJsonString(item, "targetScope"),
                        GetJsonString(item, "targetChapterId"),
                        GetJsonString(item, "impactLevel"),
                        GetJsonString(item, "source"),
                        executedIntentIds.Contains(intentId) ? "executed" : "accepted");
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.IntentId) ||
                               !string.IsNullOrWhiteSpace(item.NormalizedIntent))
                .Take(24)
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<WorkflowCreativeIntentEvidence>();
        }
    }

    private static IReadOnlyList<WorkflowRevisionPlanEvidence> ParseRevisionPlans(
        string? packageJson,
        string? factSnapshotJson)
    {
        var plans = new Dictionary<string, WorkflowRevisionPlanEvidence>(StringComparer.OrdinalIgnoreCase);

        foreach (var plan in ParseRevisionPlansFromJson(packageJson))
            plans[FirstNonEmpty(plan.RevisionPlanId, $"{plan.TargetChapterId}:{plan.PlanType}:{plan.Recommendation}")] = plan;

        foreach (var plan in ParseRevisionPlansFromJson(factSnapshotJson))
            plans[FirstNonEmpty(plan.RevisionPlanId, $"{plan.TargetChapterId}:{plan.PlanType}:{plan.Recommendation}")] = plan;

        return plans.Values.Take(12).ToList();
    }

    private static IEnumerable<WorkflowRevisionPlanEvidence> ParseRevisionPlansFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            yield break;

        using var document = TryParseJson(json);
        if (document == null ||
            !document.RootElement.TryGetProperty("sourceRevisionPlans", out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in property.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object))
        {
            var targetChapterId = GetJsonString(item, "targetChapterId");
            var targetChapterLogicalId = FirstNonEmpty(
                GetJsonString(item, "targetChapterLogicalId"),
                GetJsonString(item, "logicalTargetChapterId"),
                targetChapterId);
            var targetChapterDisplayName = FirstNonEmpty(
                GetJsonString(item, "targetChapterDisplayName"),
                GetJsonString(item, "displayTargetChapterName"),
                targetChapterLogicalId,
                targetChapterId);
            var evidence = new WorkflowRevisionPlanEvidence(
                GetJsonString(item, "revisionPlanId"),
                GetJsonString(item, "planType"),
                GetJsonString(item, "targetScope"),
                targetChapterId,
                targetChapterLogicalId,
                targetChapterDisplayName,
                GetJsonString(item, "status"),
                ParseJsonStringArray(GetJsonString(item, "affectedChapterIdsJson")),
                ParseJsonStringArray(GetJsonString(item, "invalidatedPackageIdsJson")),
                GetJsonString(item, "riskLevel"),
                GetJsonString(item, "recommendation"));

            if (!string.IsNullOrWhiteSpace(evidence.RevisionPlanId) ||
                !string.IsNullOrWhiteSpace(evidence.Recommendation))
            {
                yield return evidence;
            }
        }
    }

    private static JsonDocument? TryParseJson(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<WorkflowAgentReviewCheckEvidence> ParseAgentReviewChecks(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<WorkflowAgentReviewCheckEvidence>();

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("checks", out var property) ||
                property.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<WorkflowAgentReviewCheckEvidence>();
            }

            return property.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => new WorkflowAgentReviewCheckEvidence(
                    GetJsonString(item, "key"),
                    GetJsonString(item, "name"),
                    GetReviewStatus(item),
                    GetJsonString(item, "message"),
                    GetJsonStringArray(item, "evidence")))
                .Where(item => !string.IsNullOrWhiteSpace(item.Key) ||
                               !string.IsNullOrWhiteSpace(item.Name))
                .Take(12)
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<WorkflowAgentReviewCheckEvidence>();
        }
    }

    private static IReadOnlyList<WorkflowKnowledgeConstraintEvidence> ParseKnowledgeConstraintEvidence(
        ProjectFactSnapshot? factSnapshot)
    {
        if (factSnapshot == null || string.IsNullOrWhiteSpace(factSnapshot.SnapshotJson))
            return Array.Empty<WorkflowKnowledgeConstraintEvidence>();

        try
        {
            using var document = JsonDocument.Parse(factSnapshot.SnapshotJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("knowledgeConstraintEvidence", out var property) ||
                property.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<WorkflowKnowledgeConstraintEvidence>();
            }

            var chapterId = FirstNonEmpty(GetJsonString(root, "chapterId"), factSnapshot.ChapterId);
            return property.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => new WorkflowKnowledgeConstraintEvidence(
                        GetJsonString(item, "knowledgeId"),
                        FirstNonEmpty(GetJsonString(item, "title"), GetJsonString(item, "knowledgeId")),
                        GetJsonString(item, "entryType"),
                        GetJsonString(item, "subject"),
                        GetJsonString(item, "constraintLevel"),
                        GetJsonString(item, "packagePolicy"),
                        FirstNonEmpty(GetJsonString(item, "evidenceStatus"), GetJsonString(item, "status")),
                        GetJsonString(item, "gateStatus"),
                        chapterId,
                        factSnapshot.Id,
                        factSnapshot.VersionNumber,
                        GetJsonStringArray(item, "allowedTerms"),
                        GetJsonStringArray(item, "forbiddenTerms"),
                        GetJsonStringArray(item, "violations"))
                    {
                        ClassificationId = GetJsonString(item, "classificationId"),
                        ClassificationRule = GetJsonString(item, "classificationRule"),
                        ShouldEnterGate = GetJsonBool(item, "shouldEnterGate"),
                        ShouldEnterBlueprint = GetJsonBool(item, "shouldEnterBlueprint"),
                        ShouldEnterFactSnapshot = GetJsonBool(item, "shouldEnterFactSnapshot")
                    })
                .Where(item => !string.IsNullOrWhiteSpace(item.KnowledgeId) ||
                               !string.IsNullOrWhiteSpace(item.Title))
                .Take(24)
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<WorkflowKnowledgeConstraintEvidence>();
        }
    }

    private static string GetReviewStatus(JsonElement item)
    {
        if (!item.TryGetProperty("status", out var property))
            return string.Empty;

        if (property.ValueKind == JsonValueKind.String)
            return property.GetString() ?? string.Empty;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var numeric))
        {
            return numeric switch
            {
                1 => "Pass",
                2 => "Warning",
                3 => "Fail",
                _ => "Unknown"
            };
        }

        return string.Empty;
    }

    private static IReadOnlyList<string> GetJsonStringArray(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return property.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Take(6)
            .ToList();
    }

    private static IReadOnlyList<string> GetJsonFlexibleStringArray(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property))
            return Array.Empty<string>();

        if (property.ValueKind == JsonValueKind.String)
        {
            var value = property.GetString();
            return string.IsNullOrWhiteSpace(value)
                ? Array.Empty<string>()
                : new[] { value.Trim() };
        }

        if (property.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return property.EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : value.ValueKind == JsonValueKind.Object
                    ? FirstNonEmpty(GetJsonString(value, "summary"), GetJsonString(value, "text"), GetJsonString(value, "message"))
                    : string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Take(12)
            .ToList();
    }

    private static IReadOnlyList<string> ParseJsonStringArray(string? json)
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
                .Where(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                .Select(item => item.GetString()!.Trim())
                .Take(24)
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> ParseTopLevelStringArray(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(propertyName, out var property) ||
                property.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            return property.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                .Select(item => item.GetString()!.Trim())
                .Take(24)
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> ParsePackageRebuiltFromPackageIds(TianmingPackage? package)
    {
        if (package == null)
            return Array.Empty<string>();

        return ParseTopLevelStringArray(package.KnowledgeSnapshotJson, "rebuiltFromPackageIds")
            .Concat(ParseTopLevelStringArray(package.InputJson, "rebuiltFromPackageIds"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToList();
    }

    private static HashSet<string> ParseExecutedIntentIds(ProductionEvent evt)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.Equals(evt.EventType, "creative_intents_executed", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(evt.DataJson))
        {
            return ids;
        }

        try
        {
            using var document = JsonDocument.Parse(evt.DataJson);
            if (!document.RootElement.TryGetProperty("intentIds", out var property) ||
                property.ValueKind != JsonValueKind.Array)
                return ids;

            foreach (var item in property.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    ids.Add(item.GetString() ?? string.Empty);
            }
        }
        catch (JsonException)
        {
        }

        return ids;
    }

    private static string GetJsonString(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static int GetJsonInt(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property))
            return 0;
        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : 0;
    }

    private static double GetJsonDouble(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property))
            return 0;
        return property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var value)
            ? value
            : 0;
    }

    private static bool GetJsonBool(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property))
            return false;

        if (property.ValueKind == JsonValueKind.True)
            return true;

        if (property.ValueKind == JsonValueKind.False)
            return false;

        return property.ValueKind == JsonValueKind.String &&
               bool.TryParse(property.GetString(), out var value) &&
               value;
    }

    private static bool? GetJsonNullableBool(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.True)
            return true;

        if (property.ValueKind == JsonValueKind.False)
            return false;

        return property.ValueKind == JsonValueKind.String &&
               bool.TryParse(property.GetString(), out var value)
            ? value
            : null;
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
