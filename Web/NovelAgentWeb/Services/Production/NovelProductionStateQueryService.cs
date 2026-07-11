using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class NovelProductionStateQueryService : INovelProductionStateQueryService
{
    private readonly NovelAgentDbContext _db;
    private readonly IProductionChainProjectionService _productionChainProjection;
    private readonly IProductionDependencyGuard? _dependencyGuard;

    public NovelProductionStateQueryService(
        NovelAgentDbContext db,
        IProductionChainProjectionService productionChainProjection,
        IProductionDependencyGuard? dependencyGuard = null)
    {
        _db = db;
        _productionChainProjection = productionChainProjection;
        _dependencyGuard = dependencyGuard;
    }

    public async Task<NovelProductionStateQueryResult?> QueryAsync(
        NovelProductionStateQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var userRole = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == request.UserId)
            .Select(u => u.Role)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var isAdmin = string.Equals(userRole, "admin", StringComparison.OrdinalIgnoreCase);

        var projectId = request.ProjectId;
        var runId = request.RunId;
        var chapterId = request.ChapterId;
        if (string.IsNullOrWhiteSpace(chapterId) && request.ChapterNumber > 0 && !string.IsNullOrWhiteSpace(projectId))
        {
            chapterId = await _db.Chapters
                .AsNoTracking()
                .Where(c => c.ProjectId == projectId && c.ChapterNumber == request.ChapterNumber)
                .Select(c => c.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false) ?? string.Empty;
        }

        var runtimeRunQuery = _db.AgentRuntimeRuns.AsNoTracking();
        if (!isAdmin)
            runtimeRunQuery = runtimeRunQuery.Where(r => r.UserId == request.UserId);
        if (!string.IsNullOrWhiteSpace(runId))
            runtimeRunQuery = runtimeRunQuery.Where(r => r.Id == runId);
        else
        {
            runtimeRunQuery = runtimeRunQuery.Where(r => r.SessionId == request.SessionId);
            if (!string.IsNullOrWhiteSpace(projectId))
                runtimeRunQuery = runtimeRunQuery.Where(r => r.ProjectId == projectId);
        }

        var runtimeRun = await runtimeRunQuery
            .OrderByDescending(r => r.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (runtimeRun != null)
        {
            runId = runtimeRun.Id;
            projectId = FirstNonEmpty(projectId, runtimeRun.ProjectId);
        }
        var chapterIdCandidates = BuildChapterIdCandidates(chapterId);

        var packageQuery = _db.TianmingPackages.AsNoTracking();
        if (!isAdmin)
            packageQuery = packageQuery.Where(p => p.UserId == request.UserId);
        if (!string.IsNullOrWhiteSpace(runId))
            packageQuery = packageQuery.Where(p => p.RuntimeRunId == runId);
        if (!string.IsNullOrWhiteSpace(projectId))
            packageQuery = packageQuery.Where(p => p.ProjectId == projectId);
        if (chapterIdCandidates.Count > 0)
            packageQuery = packageQuery.Where(p => p.ChapterId != null && chapterIdCandidates.Contains(p.ChapterId));

        var packages = await packageQuery
            .OrderByDescending(p => p.CreatedAt)
            .Take(8)
            .Select(p => new NovelProductionPackageState
            {
                Id = p.Id,
                ProjectId = p.ProjectId,
                ChapterId = p.ChapterId ?? string.Empty,
                RuntimeRunId = p.RuntimeRunId ?? string.Empty,
                PackageKind = p.PackageKind,
                Status = p.Status,
                PromptVersion = p.PromptVersion ?? string.Empty,
                KernelVersion = p.KernelVersion ?? string.Empty,
                RebuiltFromPackageIds = ParseTopLevelStringArray(
                    FirstNonEmpty(p.KnowledgeSnapshotJson, p.InputJson),
                    "rebuiltFromPackageIds"),
                KnowledgeBindingSummary = ParseKnowledgeBindingSummary(p.KnowledgeSnapshotJson),
                CreatedAt = p.CreatedAt
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        packages.Reverse();
        if (string.IsNullOrWhiteSpace(chapterId))
            chapterId = packages.LastOrDefault()?.ChapterId ?? string.Empty;
        chapterIdCandidates = await BuildProjectChapterIdCandidatesAsync(projectId, chapterId, cancellationToken)
            .ConfigureAwait(false);
        var rebuildLinks = BuildRebuildLinks(packages);

        var executedRevisionPlanIds = await QueryExecutedRevisionPlanIdsAsync(
                request.UserId,
                isAdmin,
                runId,
                projectId,
                chapterId,
                cancellationToken)
            .ConfigureAwait(false);

        var revisionPlanQuery = _db.RevisionPlans.AsNoTracking();
        if (!isAdmin)
            revisionPlanQuery = revisionPlanQuery.Where(plan => plan.UserId == request.UserId);
        if (!string.IsNullOrWhiteSpace(runId))
        {
            revisionPlanQuery = executedRevisionPlanIds.Count == 0
                ? revisionPlanQuery.Where(plan => plan.RuntimeRunId == runId)
                : revisionPlanQuery.Where(plan =>
                    plan.RuntimeRunId == runId ||
                    executedRevisionPlanIds.Contains(plan.Id));
        }
        if (!string.IsNullOrWhiteSpace(projectId))
            revisionPlanQuery = revisionPlanQuery.Where(plan => plan.ProjectId == projectId);
        if (chapterIdCandidates.Count > 0)
            revisionPlanQuery = revisionPlanQuery.Where(plan =>
                (plan.TargetChapterId != null && chapterIdCandidates.Contains(plan.TargetChapterId)) ||
                plan.TargetChapterId == null ||
                plan.TargetChapterId == string.Empty);

        var revisionPlans = await revisionPlanQuery
            .OrderByDescending(plan => plan.CreatedAt)
            .Take(12)
            .Select(plan => new NovelProductionRevisionPlanState
            {
                Id = plan.Id,
                ProjectId = plan.ProjectId,
                CreativeIntentId = plan.CreativeIntentId ?? string.Empty,
                KnowledgeConflictReportId = plan.KnowledgeConflictReportId ?? string.Empty,
                RuntimeRunId = plan.RuntimeRunId ?? string.Empty,
                Source = plan.Source,
                PlanType = plan.PlanType,
                TargetScope = plan.TargetScope,
                TargetChapterId = plan.TargetChapterId ?? string.Empty,
                TargetChapterLogicalId = plan.TargetChapterLogicalId ?? string.Empty,
                TargetChapterDisplayName = plan.TargetChapterDisplayName ?? string.Empty,
                Status = plan.Status,
                RequirementsJson = plan.RequirementsJson,
                ImpactAnalysisJson = plan.ImpactAnalysisJson,
                AffectedChapterIdsJson = plan.AffectedChapterIdsJson,
                InvalidatedPackageIdsJson = plan.InvalidatedPackageIdsJson,
                RiskLevel = plan.RiskLevel,
                Recommendation = plan.Recommendation,
                CreatedAt = plan.CreatedAt
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        revisionPlans.Reverse();
        revisionPlans = await AddRevisionPlansForRebuiltPackagesAsync(
                revisionPlans,
                request.UserId,
                isAdmin,
                projectId,
                chapterIdCandidates,
                rebuildLinks,
                cancellationToken)
            .ConfigureAwait(false);

        var stalePackages = BuildStalePackageStates(packages, revisionPlans);

        var productionEventQuery = _db.ProductionEvents.AsNoTracking();
        if (!isAdmin)
            productionEventQuery = productionEventQuery.Where(e => e.UserId == request.UserId);
        if (!string.IsNullOrWhiteSpace(runId))
            productionEventQuery = productionEventQuery.Where(e => e.RuntimeRunId == runId);
        if (!string.IsNullOrWhiteSpace(projectId))
            productionEventQuery = productionEventQuery.Where(e => e.ProjectId == projectId);
        if (chapterIdCandidates.Count > 0)
            productionEventQuery = productionEventQuery.Where(e =>
                (e.ChapterId != null && chapterIdCandidates.Contains(e.ChapterId)) ||
                e.ChapterId == null ||
                e.ChapterId == string.Empty);

        var productionEventEntities = request.IncludeEvents
            ? await productionEventQuery
                .OrderByDescending(e => e.CreatedAt)
                .Take(20)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false)
            : new List<ProductionEvent>();
        var productionEvents = productionEventEntities
            .Select(MapProductionEventState)
            .ToList();
        productionEvents.Reverse();
        var outputArtifacts = productionEventEntities
            .Where(evt => string.Equals(evt.EventType, OutputArtifactRecorder.EventType, StringComparison.OrdinalIgnoreCase))
            .Select(MapOutputArtifactState)
            .Reverse()
            .ToList();
        if (string.IsNullOrWhiteSpace(chapterId))
            chapterId = productionEvents.LastOrDefault()?.ChapterId ?? string.Empty;

        var runtimeEvents = runtimeRun == null || !request.IncludeEvents
            ? new List<NovelRuntimeEventState>()
            : await _db.AgentRuntimeEvents
                .AsNoTracking()
                .Where(e => e.RuntimeRunId == runtimeRun.Id)
                .OrderByDescending(e => e.CreatedAt)
                .Take(20)
                .Select(e => new NovelRuntimeEventState
                {
                    Id = e.Id,
                    Type = e.Type,
                    Stage = e.Stage,
                    Status = e.Status,
                    Message = e.Message,
                    DisplaySurface = e.DisplaySurface,
                    DisplayPolicy = e.DisplayPolicy,
                    DataJson = e.DataJson,
                    CreatedAt = e.CreatedAt
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        runtimeEvents.Reverse();
        var dependencyBlocks = await QueryDependencyBlocksAsync(
                request,
                projectId,
                chapterId,
                runtimeEvents,
                cancellationToken)
            .ConfigureAwait(false);

        var toolQuery = _db.AgentToolExecutions.AsNoTracking();
        if (!isAdmin)
            toolQuery = toolQuery.Where(t => t.UserId == request.UserId);
        if (!string.IsNullOrWhiteSpace(runId))
            toolQuery = toolQuery.Where(t => t.RunId == runId);
        else if (!string.IsNullOrWhiteSpace(projectId))
            toolQuery = toolQuery.Where(t => t.ProjectId == projectId);

        var toolRows = await toolQuery
            .OrderByDescending(t => t.StartedAt)
            .Take(12)
            .Select(t => new
            {
                t.Id,
                RunId = t.RunId ?? string.Empty,
                t.ToolName,
                t.Phase,
                t.Status,
                t.Risk,
                t.ResultMessage,
                t.ErrorMessage,
                t.FailureJson,
                t.SemanticContractJson,
                t.StartedAt,
                t.CompletedAt
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var toolExecutions = toolRows
            .Select(t => new NovelProductionToolExecutionState
            {
                Id = t.Id,
                RunId = t.RunId,
                ToolName = t.ToolName,
                Phase = t.Phase,
                Status = t.Status,
                Risk = t.Risk,
                ResultMessage = t.ResultMessage,
                ErrorMessage = t.ErrorMessage,
                Failure = ParseToolFailure(t.FailureJson),
                SemanticContract = ParseToolSemanticContract(t.SemanticContractJson),
                StartedAt = t.StartedAt,
                CompletedAt = t.CompletedAt
            })
            .ToList();
        toolExecutions.Reverse();

        var outboxQuery = _db.OutboxEvents.AsNoTracking();
        if (!isAdmin)
            outboxQuery = outboxQuery.Where(o => o.UserId == request.UserId);
        if (!string.IsNullOrWhiteSpace(runId))
            outboxQuery = outboxQuery.Where(o => o.RuntimeRunId == runId);
        if (!string.IsNullOrWhiteSpace(projectId))
            outboxQuery = outboxQuery.Where(o => o.ProjectId == projectId);

        var outboxEvents = await outboxQuery
            .OrderByDescending(o => o.CreatedAt)
            .Take(12)
            .Select(o => new NovelProductionOutboxState
            {
                Id = o.Id,
                EventType = o.EventType,
                AggregateType = o.AggregateType,
                AggregateId = o.AggregateId,
                Status = o.Status,
                Attempts = o.Attempts,
                LastError = o.LastError ?? string.Empty,
                NextAttemptAt = o.NextAttemptAt,
                CreatedAt = o.CreatedAt
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        outboxEvents.Reverse();

        var factSnapshotQuery = _db.ProjectFactSnapshots.AsNoTracking();
        if (!isAdmin)
            factSnapshotQuery = factSnapshotQuery.Where(snapshot => snapshot.UserId == request.UserId);
        if (!string.IsNullOrWhiteSpace(projectId))
            factSnapshotQuery = factSnapshotQuery.Where(snapshot => snapshot.ProjectId == projectId);
        if (chapterIdCandidates.Count > 0)
            factSnapshotQuery = factSnapshotQuery.Where(snapshot =>
                snapshot.ChapterId != null && chapterIdCandidates.Contains(snapshot.ChapterId));

        var factSnapshotEntities = await factSnapshotQuery
            .OrderByDescending(snapshot => snapshot.CreatedAt)
            .Take(6)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var factSnapshots = factSnapshotEntities
            .Select(MapFactSnapshotState)
            .Reverse()
            .ToList();

        var chapterDraftQuery = _db.ChapterDrafts.AsNoTracking();
        if (!isAdmin)
            chapterDraftQuery = chapterDraftQuery.Where(draft => draft.UserId == request.UserId);
        if (!string.IsNullOrWhiteSpace(runId))
            chapterDraftQuery = chapterDraftQuery.Where(draft => draft.RuntimeRunId == runId);
        if (!string.IsNullOrWhiteSpace(projectId))
            chapterDraftQuery = chapterDraftQuery.Where(draft => draft.ProjectId == projectId);
        if (chapterIdCandidates.Count > 0)
            chapterDraftQuery = chapterDraftQuery.Where(draft => chapterIdCandidates.Contains(draft.ChapterId));

        var chapterDrafts = await chapterDraftQuery
            .OrderByDescending(draft => draft.CreatedAt)
            .Take(12)
            .Select(draft => new NovelProductionChapterDraftState
            {
                Id = draft.Id,
                ProjectId = draft.ProjectId,
                RuntimeRunId = draft.RuntimeRunId,
                ChapterId = draft.ChapterId,
                PackageId = draft.PackageId ?? string.Empty,
                ArtifactId = draft.ArtifactId,
                Status = draft.Status,
                ContentLength = draft.ContentLength,
                RepairAttemptCount = draft.RepairAttemptCount,
                HasChanges = draft.HasChanges,
                Preview = BuildPreview(draft.DraftContent, 160),
                ChangesJson = draft.ChangesJson ?? string.Empty,
                GeneratedAt = draft.GeneratedAt,
                CreatedAt = draft.CreatedAt
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        chapterDrafts.Reverse();

        var gateReportQuery = _db.GenerationGateReports.AsNoTracking();
        if (!isAdmin)
            gateReportQuery = gateReportQuery.Where(report => report.UserId == request.UserId);
        if (!string.IsNullOrWhiteSpace(runId))
            gateReportQuery = gateReportQuery.Where(report => report.RuntimeRunId == runId);
        if (!string.IsNullOrWhiteSpace(projectId))
            gateReportQuery = gateReportQuery.Where(report => report.ProjectId == projectId);
        if (chapterIdCandidates.Count > 0)
            gateReportQuery = gateReportQuery.Where(report => chapterIdCandidates.Contains(report.ChapterId));

        var gateReports = await gateReportQuery
            .OrderByDescending(report => report.CreatedAt)
            .Take(12)
            .Select(report => new NovelProductionGenerationGateReportState
            {
                Id = report.Id,
                ProjectId = report.ProjectId,
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
        gateReports.Reverse();

        var agentReviewQuery = _db.AgentReviews.AsNoTracking();
        if (!isAdmin)
            agentReviewQuery = agentReviewQuery.Where(review => review.UserId == request.UserId);
        if (!string.IsNullOrWhiteSpace(runId))
            agentReviewQuery = agentReviewQuery.Where(review => review.RuntimeRunId == runId);
        if (!string.IsNullOrWhiteSpace(projectId))
            agentReviewQuery = agentReviewQuery.Where(review => review.ProjectId == projectId);
        if (chapterIdCandidates.Count > 0)
            agentReviewQuery = agentReviewQuery.Where(review => chapterIdCandidates.Contains(review.ChapterId));

        var agentReviews = await agentReviewQuery
            .OrderByDescending(review => review.CreatedAt)
            .Take(12)
            .Select(review => new NovelProductionAgentReviewState
            {
                Id = review.Id,
                ProjectId = review.ProjectId,
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

        var memoryReadQuery = _db.AgentMemoryReads.AsNoTracking();
        if (!isAdmin)
            memoryReadQuery = memoryReadQuery.Where(read => read.UserId == request.UserId);
        if (!string.IsNullOrWhiteSpace(runId))
            memoryReadQuery = memoryReadQuery.Where(read =>
                read.RunId == runId ||
                (read.RunId == null &&
                 ((read.SessionId != null && read.SessionId == request.SessionId) ||
                  (!string.IsNullOrWhiteSpace(projectId) && read.ProjectId == projectId))));
        else
        {
            if (!string.IsNullOrWhiteSpace(request.SessionId))
                memoryReadQuery = memoryReadQuery.Where(read => read.SessionId == request.SessionId);
            if (!string.IsNullOrWhiteSpace(projectId))
                memoryReadQuery = memoryReadQuery.Where(read => read.ProjectId == projectId || read.ProjectId == null);
        }

        var memoryReadRows = await memoryReadQuery
            .OrderByDescending(read => read.CreatedAt)
            .Take(12)
            .Select(read => new
            {
                read.Id,
                read.ProjectId,
                read.SessionId,
                read.RunId,
                read.MemoryScope,
                read.MemoryKeysJson,
                read.SourceType,
                read.Consumer,
                read.CreatedAt
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var memoryReads = memoryReadRows
            .Select(read => new NovelProductionMemoryReadState
            {
                Id = read.Id,
                ProjectId = read.ProjectId ?? string.Empty,
                SessionId = read.SessionId ?? string.Empty,
                RunId = read.RunId ?? string.Empty,
                MemoryScope = read.MemoryScope,
                MemoryKeys = ChapterIdentityResolver.ParseStringArray(read.MemoryKeysJson),
                SourceType = read.SourceType,
                Consumer = read.Consumer,
                CreatedAt = read.CreatedAt
            })
            .Reverse()
            .ToList();

        var memoryPromotionQuery = _db.AgentMemoryPromotions.AsNoTracking();
        if (!isAdmin)
            memoryPromotionQuery = memoryPromotionQuery.Where(promotion => promotion.UserId == request.UserId);
        if (!string.IsNullOrWhiteSpace(runId))
            memoryPromotionQuery = memoryPromotionQuery.Where(promotion =>
                promotion.RunId == runId ||
                (promotion.RunId == null &&
                 ((promotion.SessionId != null && promotion.SessionId == request.SessionId) ||
                  (!string.IsNullOrWhiteSpace(projectId) && promotion.ProjectId == projectId))));
        else
        {
            if (!string.IsNullOrWhiteSpace(request.SessionId))
                memoryPromotionQuery = memoryPromotionQuery.Where(promotion => promotion.SessionId == request.SessionId);
            if (!string.IsNullOrWhiteSpace(projectId))
                memoryPromotionQuery = memoryPromotionQuery.Where(promotion => promotion.ProjectId == projectId || promotion.ProjectId == null);
        }

        var memoryPromotions = await memoryPromotionQuery
            .OrderByDescending(promotion => promotion.CreatedAt)
            .Take(12)
            .Select(promotion => new NovelProductionMemoryPromotionState
            {
                Id = promotion.Id,
                ProjectId = promotion.ProjectId ?? string.Empty,
                SessionId = promotion.SessionId ?? string.Empty,
                RunId = promotion.RunId ?? string.Empty,
                SourceScope = promotion.SourceScope,
                TargetScope = promotion.TargetScope,
                SourceMemoryKey = promotion.SourceMemoryKey,
                TargetMemoryKey = promotion.TargetMemoryKey,
                PromotionReason = promotion.PromotionReason,
                PayloadJson = promotion.PayloadJson,
                CreatedAt = promotion.CreatedAt
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        memoryPromotions.Reverse();

        var chapterChangeQuery = _db.ChapterChanges.AsNoTracking();
        if (!isAdmin)
            chapterChangeQuery = chapterChangeQuery.Where(change => change.UserId == request.UserId);
        if (!string.IsNullOrWhiteSpace(runId))
            chapterChangeQuery = chapterChangeQuery.Where(change => change.RuntimeRunId == runId);
        if (!string.IsNullOrWhiteSpace(projectId))
            chapterChangeQuery = chapterChangeQuery.Where(change => change.ProjectId == projectId);
        if (chapterIdCandidates.Count > 0)
            chapterChangeQuery = chapterChangeQuery.Where(change => chapterIdCandidates.Contains(change.ChapterId));

        var chapterChanges = await chapterChangeQuery
            .OrderByDescending(change => change.CreatedAt)
            .Take(12)
            .Select(change => new NovelProductionChapterChangeState
            {
                Id = change.Id,
                ProjectId = change.ProjectId,
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

        var chainProjectionEvents = BuildChainProjectionEvents(
            productionEvents,
            packages,
            chapterDrafts,
            chapterChanges,
            gateReports,
            agentReviews,
            factSnapshots);

        var productionChains = _productionChainProjection.BuildNovelChains(
            chainProjectionEvents,
            packages,
            revisionPlans,
            outboxEvents,
            rebuildLinks);

        if (runtimeRun == null &&
            packages.Count == 0 &&
            revisionPlans.Count == 0 &&
            productionEvents.Count == 0 &&
            toolExecutions.Count == 0 &&
            factSnapshots.Count == 0 &&
            chapterDrafts.Count == 0 &&
            chapterChanges.Count == 0 &&
            gateReports.Count == 0 &&
            agentReviews.Count == 0 &&
            memoryReads.Count == 0 &&
            memoryPromotions.Count == 0 &&
            dependencyBlocks.Count == 0)
        {
            return null;
        }

        return new NovelProductionStateQueryResult
        {
            RuntimeRun = runtimeRun == null ? null : new NovelRuntimeRunState
            {
                Id = runtimeRun.Id,
                SessionId = runtimeRun.SessionId,
                ProjectId = runtimeRun.ProjectId ?? string.Empty,
                Status = runtimeRun.Status,
                Mode = runtimeRun.Mode,
                CurrentPhase = runtimeRun.CurrentPhase,
                CurrentStep = runtimeRun.CurrentStep,
                ActiveTool = runtimeRun.ActiveTool,
                LastMessage = runtimeRun.LastMessage,
                ErrorMessage = runtimeRun.ErrorMessage,
                Failure = ParseRuntimeRunFailure(runtimeRun.FailureJson),
                CancelRequested = runtimeRun.CancelRequested,
                StartedAt = runtimeRun.StartedAt,
                CompletedAt = runtimeRun.CompletedAt,
                UpdatedAt = runtimeRun.UpdatedAt
            },
            ProjectId = projectId,
            ChapterId = chapterId,
            Packages = packages,
            RebuildLinks = rebuildLinks,
            ProductionChains = productionChains,
            StalePackages = stalePackages,
            DependencyBlocks = dependencyBlocks,
            RevisionPlans = revisionPlans,
            ProductionEvents = productionEvents,
            OutputArtifacts = outputArtifacts,
            RuntimeEvents = runtimeEvents,
            ToolExecutions = toolExecutions,
            OutboxEvents = outboxEvents,
            FactSnapshots = factSnapshots,
            ChapterDrafts = chapterDrafts,
            ChapterChanges = chapterChanges,
            GenerationGateReports = gateReports,
            AgentReviews = agentReviews,
            MemoryReads = memoryReads,
            MemoryPromotions = memoryPromotions
        };
    }

    private async Task<List<ProductionDependencyBlock>> QueryDependencyBlocksAsync(
        NovelProductionStateQueryRequest request,
        string projectId,
        string chapterId,
        IReadOnlyList<NovelRuntimeEventState> runtimeEvents,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(projectId))
            return new List<ProductionDependencyBlock>();

        var targetChapterNumbers = new HashSet<int>();
        if (request.ChapterNumber > 1)
            targetChapterNumbers.Add(request.ChapterNumber);

        if (!string.IsNullOrWhiteSpace(chapterId))
        {
            var chapterNumber = await _db.Chapters
                .AsNoTracking()
                .Where(chapter => chapter.ProjectId == projectId && chapter.Id == chapterId)
                .Select(chapter => (int?)chapter.ChapterNumber)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (chapterNumber > 1)
                targetChapterNumbers.Add(chapterNumber.Value);
        }

        foreach (var runtimeEvent in runtimeEvents)
        {
            if (!string.Equals(runtimeEvent.Stage, NovelAgentProductionStages.ContextPackage, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(runtimeEvent.Status, "blocked", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var previousChapterNumber = GetJsonInt(runtimeEvent.DataJson, "previousChapterNumber");
            if (previousChapterNumber > 0)
                targetChapterNumbers.Add(previousChapterNumber + 1);
        }

        if (targetChapterNumbers.Count == 0)
            return new List<ProductionDependencyBlock>();

        var guard = _dependencyGuard ?? new ProductionDependencyGuard(_db);
        var result = new List<ProductionDependencyBlock>();
        foreach (var targetChapterNumber in targetChapterNumbers.OrderBy(number => number))
        {
            var blocks = await guard.FindBlocksAsync(
                    new ProductionDependencyGuardRequest(
                        request.UserId,
                        projectId,
                        targetChapterNumber),
                    cancellationToken)
                .ConfigureAwait(false);
            foreach (var block in blocks)
            {
                if (!string.IsNullOrWhiteSpace(request.RunId))
                    block.RecommendedArguments["runId"] = request.RunId;
                if (!string.IsNullOrWhiteSpace(chapterId))
                    block.RecommendedArguments["chapterId"] = chapterId;
                result.Add(block);
            }
        }

        return result
            .GroupBy(block => $"{block.Code}:{block.PreviousChapterId}:{block.TargetChapterNumber}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private async Task<List<string>> QueryExecutedRevisionPlanIdsAsync(
        string userId,
        bool isAdmin,
        string runId,
        string projectId,
        string chapterId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return new List<string>();

        var query = _db.ProductionEvents
            .AsNoTracking()
            .Where(evt =>
                evt.RuntimeRunId == runId &&
                evt.EventType == "revision_plan_executed" &&
                evt.ArtifactType == "RevisionPlan" &&
                evt.ArtifactId != null &&
                evt.ArtifactId != string.Empty);
        if (!isAdmin)
            query = query.Where(evt => evt.UserId == userId);
        if (!string.IsNullOrWhiteSpace(projectId))
            query = query.Where(evt => evt.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(chapterId))
            query = query.Where(evt =>
                evt.ChapterId == chapterId ||
                evt.ChapterId == null ||
                evt.ChapterId == string.Empty);

        return await query
            .OrderByDescending(evt => evt.CreatedAt)
            .Select(evt => evt.ArtifactId!)
            .Distinct()
            .Take(24)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<List<NovelProductionRevisionPlanState>> AddRevisionPlansForRebuiltPackagesAsync(
        List<NovelProductionRevisionPlanState> revisionPlans,
        string userId,
        bool isAdmin,
        string projectId,
        IReadOnlyList<string> chapterIdCandidates,
        IReadOnlyList<NovelProductionRebuildLinkState> rebuildLinks,
        CancellationToken cancellationToken)
    {
        var rebuiltFromPackageIds = rebuildLinks
            .Select(link => link.OldPackageId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (rebuiltFromPackageIds.Count == 0)
            return revisionPlans;

        var knownIds = revisionPlans
            .Select(plan => plan.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var query = _db.RevisionPlans.AsNoTracking();
        if (!isAdmin)
            query = query.Where(plan => plan.UserId == userId);
        if (!string.IsNullOrWhiteSpace(projectId))
            query = query.Where(plan => plan.ProjectId == projectId);
        if (chapterIdCandidates.Count > 0)
        {
            query = query.Where(plan =>
                (plan.TargetChapterId != null && chapterIdCandidates.Contains(plan.TargetChapterId)) ||
                plan.TargetChapterId == null ||
                plan.TargetChapterId == string.Empty);
        }

        var candidates = await query
            .OrderByDescending(plan => plan.CreatedAt)
            .Take(50)
            .Select(plan => new NovelProductionRevisionPlanState
            {
                Id = plan.Id,
                ProjectId = plan.ProjectId,
                CreativeIntentId = plan.CreativeIntentId ?? string.Empty,
                KnowledgeConflictReportId = plan.KnowledgeConflictReportId ?? string.Empty,
                RuntimeRunId = plan.RuntimeRunId ?? string.Empty,
                Source = plan.Source,
                PlanType = plan.PlanType,
                TargetScope = plan.TargetScope,
                TargetChapterId = plan.TargetChapterId ?? string.Empty,
                TargetChapterLogicalId = plan.TargetChapterLogicalId ?? string.Empty,
                TargetChapterDisplayName = plan.TargetChapterDisplayName ?? string.Empty,
                Status = plan.Status,
                RequirementsJson = plan.RequirementsJson,
                ImpactAnalysisJson = plan.ImpactAnalysisJson,
                AffectedChapterIdsJson = plan.AffectedChapterIdsJson,
                InvalidatedPackageIdsJson = plan.InvalidatedPackageIdsJson,
                RiskLevel = plan.RiskLevel,
                Recommendation = plan.Recommendation,
                CreatedAt = plan.CreatedAt
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var matched = candidates
            .Where(plan => !knownIds.Contains(plan.Id))
            .Where(plan => ChapterIdentityResolver
                .ParseStringArray(plan.InvalidatedPackageIdsJson)
                .Any(packageId => rebuiltFromPackageIds.Contains(packageId, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(plan => plan.CreatedAt)
            .ToList();
        if (matched.Count == 0)
            return revisionPlans;

        revisionPlans.AddRange(matched);
        return revisionPlans
            .GroupBy(plan => plan.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(plan => plan.CreatedAt).Last())
            .OrderBy(plan => plan.CreatedAt)
            .TakeLast(12)
            .ToList();
    }

    private static List<NovelProductionRebuildLinkState> BuildRebuildLinks(
        IReadOnlyList<NovelProductionPackageState> packages)
    {
        if (packages.Count == 0)
            return new List<NovelProductionRebuildLinkState>();

        var packageById = packages
            .GroupBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        var links = new List<NovelProductionRebuildLinkState>();
        foreach (var package in packages)
        {
            foreach (var oldPackageId in package.RebuiltFromPackageIds)
            {
                packageById.TryGetValue(oldPackageId, out var oldPackage);
                links.Add(new NovelProductionRebuildLinkState
                {
                    OldPackageId = oldPackageId,
                    OldPackageStatus = oldPackage?.Status ?? string.Empty,
                    NewPackageId = package.Id,
                    NewPackageStatus = package.Status,
                    NewPackageKind = package.PackageKind,
                    ChapterId = FirstNonEmpty(package.ChapterId, oldPackage?.ChapterId),
                    RuntimeRunId = package.RuntimeRunId
                });
            }
        }

        return links
            .GroupBy(link => $"{link.OldPackageId}\n{link.NewPackageId}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static List<NovelProductionStalePackageState> BuildStalePackageStates(
        IReadOnlyList<NovelProductionPackageState> packages,
        IReadOnlyList<NovelProductionRevisionPlanState> revisionPlans)
    {
        var stalePackages = packages
            .Where(package => string.Equals(package.Status, "stale", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (stalePackages.Count == 0)
            return new List<NovelProductionStalePackageState>();

        var result = new List<NovelProductionStalePackageState>();
        foreach (var package in stalePackages)
        {
            var packageChapterCandidates = BuildChapterIdCandidates(package.ChapterId);
            var plan = revisionPlans.LastOrDefault(candidate =>
                ChapterIdentityResolver.ParseStringArray(candidate.InvalidatedPackageIdsJson).Contains(package.Id, StringComparer.OrdinalIgnoreCase) ||
                ChapterIdentityResolver.ParseStringArray(candidate.AffectedChapterIdsJson)
                    .Any(chapterId => packageChapterCandidates.Contains(chapterId, StringComparer.OrdinalIgnoreCase)));
            var reason = FirstNonEmpty(
                plan?.Recommendation,
                plan == null ? string.Empty : $"修订计划 {plan.Id} 要求重建受影响章节。",
                "旧生产包已失效，需要重建章节生产包。");

            var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(package.RuntimeRunId))
                args["runId"] = package.RuntimeRunId;
            if (!string.IsNullOrWhiteSpace(package.ChapterId))
                args["chapterId"] = package.ChapterId;
            if (!string.IsNullOrWhiteSpace(plan?.Id))
                args["revisionPlanId"] = plan.Id;

            result.Add(new NovelProductionStalePackageState
            {
                PackageId = package.Id,
                ChapterId = package.ChapterId,
                RuntimeRunId = package.RuntimeRunId,
                RevisionPlanId = plan?.Id ?? string.Empty,
                RevisionPlanStatus = plan?.Status ?? string.Empty,
                Reason = reason,
                RecommendedToolName = "ProduceChapter",
                RecommendedArguments = args
            });
        }

        return result;
    }

    private static List<NovelProductionEventState> BuildChainProjectionEvents(
        IReadOnlyList<NovelProductionEventState> productionEvents,
        IReadOnlyList<NovelProductionPackageState> packages,
        IReadOnlyList<NovelProductionChapterDraftState> chapterDrafts,
        IReadOnlyList<NovelProductionChapterChangeState> chapterChanges,
        IReadOnlyList<NovelProductionGenerationGateReportState> gateReports,
        IReadOnlyList<NovelProductionAgentReviewState> agentReviews,
        IReadOnlyList<NovelProductionFactSnapshotState> factSnapshots)
    {
        var events = productionEvents.ToList();
        var realEvents = productionEvents.ToList();
        var packageByChapterId = packages
            .Where(package => !string.IsNullOrWhiteSpace(package.ChapterId))
            .GroupBy(package => package.ChapterId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(package => package.CreatedAt).Last(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var draft in chapterDrafts)
        {
            AddSyntheticEvent(events, realEvents, new NovelProductionEventState
            {
                Id = $"synthetic:draft:{draft.Id}",
                RuntimeRunId = draft.RuntimeRunId,
                ProjectId = draft.ProjectId,
                ChapterId = draft.ChapterId,
                PackageId = draft.PackageId,
                EventType = "chapter_draft_generated",
                Stage = NovelAgentProductionStages.DraftGenerated,
                Status = NormalizeCompletedArtifactStatus(draft.Status),
                Message = $"正文草稿已生成，长度 {draft.ContentLength} 字。",
                ArtifactType = "chapter_draft",
                ArtifactId = FirstNonEmpty(draft.ArtifactId, draft.Id),
                DataJson = JsonSerializer.Serialize(new
                {
                    draftId = draft.Id,
                    draft.ArtifactId,
                    draft.ContentLength,
                    draft.RepairAttemptCount,
                    draft.HasChanges
                }),
                CreatedAt = draft.CreatedAt
            });
        }

        foreach (var change in chapterChanges)
        {
            AddSyntheticEvent(events, realEvents, new NovelProductionEventState
            {
                Id = $"synthetic:changes:{change.Id}",
                RuntimeRunId = change.RuntimeRunId,
                ProjectId = change.ProjectId,
                ChapterId = change.ChapterId,
                PackageId = change.PackageId,
                EventType = "chapter_changes_recorded",
                Stage = NovelAgentProductionStages.ChangesExtracted,
                Status = NormalizeCompletedArtifactStatus(change.ParseStatus),
                Message = change.AppliedToFactSnapshot
                    ? "CHANGES 已解析并应用到事实快照。"
                    : "CHANGES 已解析。",
                ArtifactType = "chapter_changes",
                ArtifactId = change.Id,
                DataJson = JsonSerializer.Serialize(new
                {
                    changeId = change.Id,
                    change.ParseStatus,
                    change.AppliedToFactSnapshot,
                    change.AppliedAt
                }),
                CreatedAt = change.CreatedAt
            });
        }

        foreach (var report in gateReports)
        {
            AddSyntheticEvent(events, realEvents, new NovelProductionEventState
            {
                Id = $"synthetic:gate:{report.Id}",
                RuntimeRunId = report.RuntimeRunId,
                ProjectId = report.ProjectId,
                ChapterId = report.ChapterId,
                PackageId = report.PackageId,
                EventType = "chapter_gate_validated",
                Stage = NovelAgentProductionStages.GateValidated,
                Status = NormalizeGateStatus(report.Status),
                Message = report.IssueCount > 0
                    ? $"门禁完成，发现 {report.IssueCount} 个问题。"
                    : "门禁通过。",
                ArtifactType = "gate_report",
                ArtifactId = FirstNonEmpty(report.ArtifactId, report.Id),
                DataJson = JsonSerializer.Serialize(new
                {
                    gateReportId = report.Id,
                    report.ProtocolPassed,
                    report.ChangesDetected,
                    report.FactSnapshotPassed,
                    report.BlueprintPassed,
                    report.RagPassed,
                    report.IssueCount,
                    report.RepairHintCount
                }),
                CreatedAt = report.CreatedAt,
                GateEvidence = BuildGateEvidence(report)
            });
        }

        foreach (var review in agentReviews)
        {
            AddSyntheticEvent(events, realEvents, new NovelProductionEventState
            {
                Id = $"synthetic:review:{review.Id}",
                RuntimeRunId = review.RuntimeRunId,
                ProjectId = review.ProjectId,
                ChapterId = review.ChapterId,
                PackageId = review.PackageId,
                EventType = "chapter_quality_reviewed",
                Stage = NovelAgentProductionStages.ReviewCompleted,
                Status = review.RequiresRewrite ? "blocked" : NormalizeCompletedArtifactStatus(review.OverallResult),
                Message = FirstNonEmpty(review.Summary, review.RequiresRewrite ? "Agent 总编验收要求重写。" : "Agent 总编验收完成。"),
                ArtifactType = "agent_review",
                ArtifactId = FirstNonEmpty(review.ReviewId, review.Id),
                DataJson = JsonSerializer.Serialize(new
                {
                    agentReviewId = review.Id,
                    review.ReviewId,
                    review.OverallResult,
                    review.ValidationOverallResult,
                    review.RequiresRewrite,
                    review.QualityScore,
                    review.MeetsAcceptedCreativeIntents,
                    review.ContinuityRisk,
                    review.ChapterPacing,
                    review.RecommendedAction
                }),
                CreatedAt = review.CreatedAt,
                AgentReviewEvidence = BuildAgentReviewEvidence(review)
            });
        }

        foreach (var fact in factSnapshots)
        {
            packageByChapterId.TryGetValue(fact.ChapterId, out var package);
            AddSyntheticEvent(events, realEvents, new NovelProductionEventState
            {
                Id = $"synthetic:facts:{fact.Id}",
                RuntimeRunId = package?.RuntimeRunId ?? string.Empty,
                ProjectId = fact.ProjectId,
                ChapterId = fact.ChapterId,
                PackageId = package?.Id ?? string.Empty,
                EventType = "chapter_continuity_facts_extracted",
                Stage = NovelAgentProductionStages.FactsPersisted,
                Status = "completed",
                Message = "FactSnapshot 已回写。",
                ArtifactType = "fact_snapshot",
                ArtifactId = fact.Id,
                DataJson = JsonSerializer.Serialize(new
                {
                    factSnapshotId = fact.Id,
                    factSnapshotVersion = fact.VersionNumber,
                    fact.ChapterVersionId,
                    fact.Source
                }),
                CreatedAt = fact.CreatedAt,
                FactSnapshotEvidence = new WorkflowFactSnapshotEvidence(
                    fact.ProtagonistName,
                    fact.ProtagonistIdentity,
                    fact.ProtagonistStatus,
                    fact.CurrentLocation,
                    fact.SystemState,
                    fact.EquipmentState,
                    fact.KeyEvents,
                    fact.EndingState,
                    fact.NextChapterMustCarry)
            });
        }

        return events
            .OrderBy(item => item.CreatedAt)
            .ToList();
    }

    private static WorkflowAgentReviewSummaryEvidence BuildAgentReviewEvidence(NovelProductionAgentReviewState review)
    {
        var decision = review.RequiresRewrite
            ? "revise"
            : FirstNonEmpty(review.RecommendedAction, "commit");
        var overallResult = FirstNonEmpty(review.OverallResult, review.ValidationOverallResult);
        IReadOnlyList<string> problems = Array.Empty<string>();
        IReadOnlyList<string> suggestions = Array.Empty<string>();
        bool? meetsAcceptedCreativeIntents = review.MeetsAcceptedCreativeIntents;
        var continuityRisk = review.ContinuityRisk;
        var chapterPacing = review.ChapterPacing;
        var recommendedAction = review.RecommendedAction;

        if (!string.IsNullOrWhiteSpace(review.ReviewJson))
        {
            try
            {
                using var document = JsonDocument.Parse(review.ReviewJson);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var root = document.RootElement;
                    decision = FirstNonEmpty(GetString(root, "decision"), decision);
                    overallResult = FirstNonEmpty(overallResult, GetString(root, "overallResult"), GetString(root, "status"));
                    problems = GetFlexibleStringArray(root, "problems");
                    suggestions = GetFlexibleStringArray(root, "suggestions");
                    meetsAcceptedCreativeIntents = GetNullableBool(root, "meetsAcceptedCreativeIntents") ?? meetsAcceptedCreativeIntents;
                    continuityRisk = FirstNonEmpty(continuityRisk, GetString(root, "continuityRisk"));
                    chapterPacing = FirstNonEmpty(chapterPacing, GetString(root, "chapterPacing"));
                    recommendedAction = FirstNonEmpty(recommendedAction, GetString(root, "recommendedAction"));
                }
            }
            catch (JsonException)
            {
                // Keep persisted columns as the authoritative fallback when review JSON is malformed.
            }
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

    private static WorkflowGateEvidence BuildGateEvidence(NovelProductionGenerationGateReportState report)
    {
        IReadOnlyList<string> issues = Array.Empty<string>();
        IReadOnlyList<string> repairHints = Array.Empty<string>();

        if (!string.IsNullOrWhiteSpace(report.ReportJson))
        {
            try
            {
                using var document = JsonDocument.Parse(report.ReportJson);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var root = document.RootElement;
                    issues = GetFlexibleStringArray(root, "issues");
                    repairHints = GetFlexibleStringArray(root, "repairHints");
                }
            }
            catch (JsonException)
            {
                // ReportJson carries explanatory evidence; persisted columns remain authoritative.
            }
        }

        return new WorkflowGateEvidence(
            FirstNonEmpty(report.Status, NormalizeGateStatus(report.Status)),
            report.ProtocolPassed,
            report.FactSnapshotPassed,
            report.BlueprintPassed,
            report.RagPassed,
            report.ChangesDetected,
            issues,
            repairHints);
    }

    private static void AddSyntheticEvent(
        List<NovelProductionEventState> events,
        IReadOnlyList<NovelProductionEventState> realEvents,
        NovelProductionEventState syntheticEvent)
    {
        if (realEvents.Any(realEvent => IsEquivalentProductionEvent(realEvent, syntheticEvent)))
            return;

        events.Add(syntheticEvent);
    }

    private static bool IsEquivalentProductionEvent(
        NovelProductionEventState realEvent,
        NovelProductionEventState syntheticEvent)
    {
        if (!string.Equals(realEvent.EventType, syntheticEvent.EventType, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(realEvent.ArtifactId) &&
            string.Equals(realEvent.ArtifactId, syntheticEvent.ArtifactId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(realEvent.PackageId) &&
               !string.IsNullOrWhiteSpace(syntheticEvent.PackageId) &&
               string.Equals(realEvent.PackageId, syntheticEvent.PackageId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(realEvent.ChapterId, syntheticEvent.ChapterId, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCompletedArtifactStatus(string? status)
    {
        var normalized = status?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
            return "completed";
        if (normalized.Contains("running", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("pending", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("queued", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("blocked", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("invalid", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        return "completed";
    }

    private static string NormalizeGateStatus(string? status)
    {
        var normalized = status?.Trim() ?? string.Empty;
        if (string.Equals(normalized, "validated", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "passed", StringComparison.OrdinalIgnoreCase))
        {
            return "completed";
        }

        return NormalizeCompletedArtifactStatus(normalized);
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
                .Where(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                .Select(item => item.GetString()!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static NovelProductionEventState MapProductionEventState(ProductionEvent evt)
    {
        var failure = ProductionFailureContractMapper.FromEvent(evt);
        return new NovelProductionEventState
        {
            Id = evt.Id,
            RuntimeRunId = evt.RuntimeRunId,
            ProjectId = evt.ProjectId,
            ChapterId = evt.ChapterId ?? string.Empty,
            PackageId = evt.PackageId ?? string.Empty,
            EventType = evt.EventType,
            Stage = evt.Stage,
            Status = evt.Status,
            Message = evt.Message,
            ArtifactType = evt.ArtifactType ?? string.Empty,
            ArtifactId = evt.ArtifactId ?? string.Empty,
            DataJson = evt.DataJson ?? string.Empty,
            Failure = failure == null
                ? null
                : new NovelProductionEventFailureState
                {
                    Code = failure.Code,
                    Stage = failure.Stage,
                    Message = failure.Message,
                    Recoverable = failure.Recoverable,
                    RecommendedAction = failure.RecommendedAction,
                    ArtifactIds = failure.ArtifactIds.ToList(),
                    RequiresUserDecision = failure.RequiresUserDecision
                },
            CreatedAt = evt.CreatedAt
        };
    }

    private static NovelProductionFactSnapshotState MapFactSnapshotState(ProjectFactSnapshot snapshot)
    {
        var state = new NovelProductionFactSnapshotState
        {
            Id = snapshot.Id,
            ProjectId = snapshot.ProjectId ?? string.Empty,
            ChapterId = snapshot.ChapterId ?? string.Empty,
            ChapterVersionId = snapshot.ChapterVersionId ?? string.Empty,
            VersionNumber = snapshot.VersionNumber,
            Source = snapshot.Source,
            CreatedAt = snapshot.CreatedAt
        };

        if (string.IsNullOrWhiteSpace(snapshot.SnapshotJson))
            return state;

        try
        {
            using var document = JsonDocument.Parse(snapshot.SnapshotJson);
            var root = document.RootElement;
            state.ChapterTitle = GetString(root, "chapterTitle");
            state.ProtagonistName = GetString(root, "protagonistName");
            state.ProtagonistIdentity = GetString(root, "protagonistIdentity");
            state.ProtagonistStatus = GetString(root, "protagonistStatus");
            state.CurrentLocation = GetString(root, "currentLocation");
            state.SystemState = GetString(root, "systemState");
            state.EquipmentState = GetString(root, "equipmentState");
            state.KeyEvents = GetStringArray(root, "keyEvents");
            state.EndingState = FirstNonEmpty(
                GetString(root, "endingState"),
                GetString(root, "chapterEndingState"));
            state.NextChapterMustCarry = GetStringArray(root, "nextChapterMustCarry");
            return state;
        }
        catch (JsonException)
        {
            state.IsParseable = false;
            return state;
        }
    }

    private static NovelProductionOutputArtifactState MapOutputArtifactState(ProductionEvent evt)
    {
        var state = new NovelProductionOutputArtifactState
        {
            Id = evt.Id,
            RuntimeRunId = evt.RuntimeRunId,
            ProjectId = evt.ProjectId,
            ChapterId = evt.ChapterId ?? string.Empty,
            PackageId = evt.PackageId ?? string.Empty,
            Stage = evt.Stage,
            Status = evt.Status,
            ArtifactType = evt.ArtifactType ?? string.Empty,
            ArtifactId = evt.ArtifactId ?? string.Empty,
            Summary = evt.Message,
            CreatedAt = evt.CreatedAt
        };

        if (string.IsNullOrWhiteSpace(evt.DataJson))
            return state;

        try
        {
            using var document = JsonDocument.Parse(evt.DataJson);
            var root = document.RootElement;
            state.ToolName = GetString(root, "toolName");
            state.OutputKind = GetString(root, "outputKind");
            state.VisibleInWorkflow = GetBool(root, "visibleInWorkflow");
            state.VisibleInLibrary = GetBool(root, "visibleInLibrary");
            state.UserVisibleWhere = GetStringArray(root, "userVisibleWhere");
            state.SourceEventType = GetString(root, "sourceEventType");
            state.SourceEventId = GetString(root, "sourceEventId");
            return state;
        }
        catch (JsonException)
        {
            return state;
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.Empty;
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

    private async Task<List<string>> BuildProjectChapterIdCandidatesAsync(
        string projectId,
        string? chapterId,
        CancellationToken cancellationToken)
    {
        var candidates = new HashSet<string>(BuildChapterIdCandidates(chapterId), StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(projectId))
            return candidates.ToList();

        var chapterNumber = ExtractTrailingNumber(chapterId?.Trim() ?? string.Empty);
        if (chapterNumber <= 0)
            return candidates.ToList();

        var canonicalChapterIds = await _db.Chapters
            .AsNoTracking()
            .Where(chapter =>
                chapter.ProjectId == projectId &&
                chapter.ChapterNumber == chapterNumber)
            .Select(chapter => chapter.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var id in canonicalChapterIds)
        {
            foreach (var candidate in BuildChapterIdCandidates(id))
                candidates.Add(candidate);
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

    private static string BuildPreview(string? content, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        var normalized = string.Join(
            " ",
            content
                .Replace("\r", "\n", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (normalized.Length <= maxLength)
            return normalized;
        return normalized[..maxLength] + "...";
    }

    private static NovelRuntimeRunFailureState? ParseRuntimeRunFailure(string? failureJson)
    {
        if (string.IsNullOrWhiteSpace(failureJson) || failureJson.Trim() == "{}")
            return null;

        try
        {
            using var doc = JsonDocument.Parse(failureJson);
            var root = doc.RootElement;
            return new NovelRuntimeRunFailureState
            {
                Code = GetString(root, "code"),
                Stage = GetString(root, "stage"),
                Message = GetString(root, "message"),
                Recoverable = GetBool(root, "recoverable"),
                RecommendedAction = GetString(root, "recommendedAction"),
                ArtifactIds = GetStringArray(root, "artifactIds"),
                RequiresUserDecision = GetBool(root, "requiresUserDecision")
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static NovelProductionToolFailureState? ParseToolFailure(string? failureJson)
    {
        if (string.IsNullOrWhiteSpace(failureJson) || failureJson.Trim() == "{}")
            return null;

        try
        {
            using var doc = JsonDocument.Parse(failureJson);
            var root = doc.RootElement;
            return new NovelProductionToolFailureState
            {
                Code = GetString(root, "code"),
                FailedStage = GetString(root, "failedStage"),
                Reason = GetString(root, "reason"),
                Recoverable = GetBool(root, "recoverable"),
                RecommendedAction = GetString(root, "recommendedAction"),
                RecoverableActions = GetStringArray(root, "recoverableActions"),
                ProducedArtifacts = GetProducedArtifacts(root),
                InputArtifacts = GetInputArtifacts(root),
                RequiresUserDecision = GetBool(root, "requiresUserDecision")
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static NovelProductionToolSemanticContractState ParseToolSemanticContract(string? semanticContractJson)
    {
        if (string.IsNullOrWhiteSpace(semanticContractJson) || semanticContractJson.Trim() == "{}")
            return new NovelProductionToolSemanticContractState();

        try
        {
            using var doc = JsonDocument.Parse(semanticContractJson);
            var root = doc.RootElement;
            return new NovelProductionToolSemanticContractState
            {
                DisplayName = GetString(root, "displayName"),
                DomainSurface = GetString(root, "domainSurface"),
                OutputKind = GetString(root, "outputKind"),
                InputArtifacts = GetStringArray(root, "inputArtifacts"),
                OutputArtifacts = GetStringArray(root, "outputArtifacts"),
                IdempotencyPolicy = GetString(root, "idempotencyPolicy"),
                RollbackPolicy = GetString(root, "rollbackPolicy"),
                UserVisibleWhere = GetString(root, "userVisibleWhere"),
                ResultSemantics = GetString(root, "resultSemantics")
            };
        }
        catch (JsonException)
        {
            return new NovelProductionToolSemanticContractState();
        }
    }

    private static string GetString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int GetInt(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number)
            ? number
            : 0;

    private static bool GetBool(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        value.GetBoolean();

    private static bool? GetNullableBool(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static int GetJsonInt(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return 0;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty(propertyName, out var value))
                return 0;
            return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
                ? number
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static List<string> GetStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            return new List<string>();
        return value
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }

    private static IReadOnlyList<string> GetFlexibleStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
            return Array.Empty<string>();

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            return string.IsNullOrWhiteSpace(text)
                ? Array.Empty<string>()
                : new[] { text.Trim() };
        }

        if (value.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return value
            .EnumerateArray()
            .Select(item => item.ValueKind switch
            {
                JsonValueKind.String => item.GetString() ?? string.Empty,
                JsonValueKind.Object => FirstNonEmpty(
                    GetString(item, "summary"),
                    GetString(item, "text"),
                    GetString(item, "message")),
                _ => string.Empty
            })
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Take(12)
            .ToList();
    }

    private static NovelProductionKnowledgeBindingSummaryState ParseKnowledgeBindingSummary(string? json)
    {
        var state = new NovelProductionKnowledgeBindingSummaryState();
        if (string.IsNullOrWhiteSpace(json))
            return state;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("knowledgeBindingSummary", out var summary) ||
                summary.ValueKind != JsonValueKind.Object)
            {
                return state;
            }

            state.BindingCount = GetInt(summary, "bindingCount");
            state.ShouldEnterGateCount = GetInt(summary, "shouldEnterGateCount");
            state.ShouldEnterBlueprintCount = GetInt(summary, "shouldEnterBlueprintCount");
            state.ShouldEnterFactSnapshotCount = GetInt(summary, "shouldEnterFactSnapshotCount");
            state.HardConstraintCount = GetInt(summary, "hardConstraintCount");
            state.ReferenceCount = GetInt(summary, "referenceCount");
            state.ClassifiedCount = GetInt(summary, "classifiedCount");
            state.PendingClassificationCount = GetInt(summary, "pendingClassificationCount");
            state.ImportedCount = GetInt(summary, "importedCount");
            state.ReferencedCount = GetInt(summary, "referencedCount");
            return state;
        }
        catch (JsonException)
        {
            return state;
        }
    }

    private static List<NovelProductionToolFailureArtifactState> GetProducedArtifacts(JsonElement root)
    {
        if (!root.TryGetProperty("producedArtifacts", out var value) || value.ValueKind != JsonValueKind.Array)
            return new List<NovelProductionToolFailureArtifactState>();

        return value
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new NovelProductionToolFailureArtifactState
            {
                ArtifactType = GetString(item, "artifactType"),
                ArtifactId = GetString(item, "artifactId"),
                Summary = GetString(item, "summary")
            })
            .ToList();
    }

    private static List<NovelProductionToolInputArtifactState> GetInputArtifacts(JsonElement root)
    {
        if (!root.TryGetProperty("inputArtifacts", out var value) || value.ValueKind != JsonValueKind.Array)
            return new List<NovelProductionToolInputArtifactState>();

        return value
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new NovelProductionToolInputArtifactState
            {
                ArtifactName = GetString(item, "artifactName"),
                Status = GetString(item, "status"),
                ArtifactId = GetString(item, "artifactId"),
                Message = GetString(item, "message"),
                BlocksExecution = GetBool(item, "blocksExecution"),
                RecommendedActions = GetStringArray(item, "recommendedActions")
            })
            .ToList();
    }
}
