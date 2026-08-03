using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TM.Framework.Common.Helpers;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.AgentSessions;
using TM.Web.NovelAgentWeb.Services.Goals;
using TM.Web.NovelAgentWeb.Services.Production;
using TM.Web.NovelAgentWeb.Services.Workspace;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.Workflow;

/// <summary>
/// Service for managing volume arc workflow and planning.
/// </summary>
public class WorkflowService : IWorkflowService
{
    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUserService;
    private readonly IWorkspaceFactory _workspaceFactory;
    private readonly IAgentSessionApplicationService _sessionManager;
    private readonly MissionBlackboardRecoveryService _blackboardRecovery;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IProductionWorkflowBridge _productionWorkflowBridge;
    private readonly IProductionChainProjectionService _productionChainProjection;
    private readonly INovelProductionStateQueryService? _productionStateQuery;
    private readonly ILogger<WorkflowService> _logger;

    public WorkflowService(
        NovelAgentDbContext db,
        ICurrentUserService currentUserService,
        IWorkspaceFactory workspaceFactory,
        IAgentSessionApplicationService sessionManager,
        MissionBlackboardRecoveryService blackboardRecovery,
        IServiceScopeFactory scopeFactory,
        IProductionWorkflowBridge productionWorkflowBridge,
        IProductionChainProjectionService productionChainProjection,
        ILogger<WorkflowService> logger,
        INovelProductionStateQueryService? productionStateQuery = null)
    {
        _db = db;
        _currentUserService = currentUserService;
        _workspaceFactory = workspaceFactory;
        _sessionManager = sessionManager;
        _blackboardRecovery = blackboardRecovery;
        _scopeFactory = scopeFactory;
        _productionWorkflowBridge = productionWorkflowBridge;
        _productionChainProjection = productionChainProjection;
        _productionStateQuery = productionStateQuery;
        _logger = logger;
    }

    public async Task<ProjectWorkflowDocument> GetProjectWorkflowAsync(string projectId, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();
        var project = await _db.NovelProjects
            .Include(p => p.StoryConstitution)
            .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, ct);

        if (project == null)
            throw new KeyNotFoundException($"Project {projectId} not found");

        var latestGoalId = await _db.CreativeGoals.AsNoTracking()
            .Where(item => item.UserId == userId && item.ProjectId == projectId)
            .OrderByDescending(item => item.CreatedAt)
            .Select(item => item.Id)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        var workspaceEntry = await _workspaceFactory.AcquireAsync(userId, projectId, ct);
        try
        {
            var catalog = new NovelProjectCatalog(workspaceEntry.Workspace, _scopeFactory);
            await catalog.UpsertAsync(MapToCatalogProject(project), ct);

            var workflow = await ProjectWorkflow.BuildAsync(
                workspaceEntry.Workspace,
                catalog,
                _sessionManager,
                _blackboardRecovery,
                projectId,
                ct);

            if (workflow == null)
                throw new KeyNotFoundException($"Project {projectId} not found");

            var productionEvents = await _productionWorkflowBridge
                .LoadProjectEventsAsync(project.Id, cancellationToken: ct)
                .ConfigureAwait(false);
            var toolExecutions = string.IsNullOrWhiteSpace(latestGoalId)
                ? await LoadProjectToolExecutionsAsync(project.Id, userId, ct).ConfigureAwait(false)
                : [];
            var legacyToolExecutionCount = string.IsNullOrWhiteSpace(latestGoalId)
                ? toolExecutions.Count
                : await _db.AgentToolExecutions.AsNoTracking()
                    .CountAsync(item => item.UserId == userId && item.ProjectId == projectId, ct);
            var productionChains = await BuildWorkflowProductionChainsAsync(project.Id, userId, productionEvents, ct)
                .ConfigureAwait(false);
            var creativeIntents = await LoadCreativeIntentEvidenceAsync(project.Id, userId, ct)
                .ConfigureAwait(false);
            var dbLibrary = await BuildDatabaseLibraryAsync(project, workflow.Library, workflow.ChapterArtifacts, productionChains, creativeIntents, ct);
            if (dbLibrary != null)
            {
                var dbTimeline = ProjectWorkflow.BuildArtifactTimeline(
                    dbLibrary,
                    new StoryBibleDocument { AgentRuns = workflow.RawRuns.ToList() },
                    workflow.ChapterArtifacts,
                    workflow.SchedulerTasks,
                    productionEvents);
                var dbStages = ProjectWorkflow.BuildProductionStages(dbLibrary, dbTimeline, workflow.SchedulerTasks, productionEvents, toolExecutions);

                workflow = workflow with
                {
                    Project = dbLibrary.ActiveBook,
                    Library = dbLibrary,
                    IsEmptyProject = workflow.ActivityScore <= 0 && dbLibrary.PlannedChapterCount == 0 && dbLibrary.GeneratedChapterCount == 0,
                    ProductionStages = dbStages,
                    ProductionChains = productionChains,
                    ArtifactTimeline = dbTimeline
                };
            }
            else if (productionEvents.Count > 0 || toolExecutions.Count > 0)
            {
                var timeline = ProjectWorkflow.BuildArtifactTimeline(
                    workflow.Library,
                    new StoryBibleDocument { AgentRuns = workflow.RawRuns.ToList() },
                    workflow.ChapterArtifacts,
                    workflow.SchedulerTasks,
                    productionEvents);
                workflow = workflow with
                {
                    ProductionStages = ProjectWorkflow.BuildProductionStages(
                        workflow.Library,
                        timeline,
                        workflow.SchedulerTasks,
                        productionEvents,
                        toolExecutions),
                    ProductionChains = productionChains,
                    ArtifactTimeline = timeline
                };
            }

            var goalState = await LoadGoalStateAsync(userId, projectId, latestGoalId, ct);
            return workflow with
            {
                LatestGoalId = latestGoalId,
                CreativeIntents = creativeIntents,
                ProductionChains = productionChains,
                GoalState = goalState,
                ProductionStages = goalState == null
                    ? workflow.ProductionStages
                    : BuildGoalProductionStages(goalState),
                LegacyAudit = new WorkflowLegacyAudit(
                    workflow.MissionPlans.Count,
                    workflow.Runs.Count,
                    legacyToolExecutionCount,
                    "legacy_retired")
            };
        }
        finally
        {
            _workspaceFactory.Release(userId, workspaceEntry.ProjectId);
        }
    }

    private static IReadOnlyList<WorkflowProductionStage> BuildGoalProductionStages(WorkflowGoalState state)
    {
        var groups = new[]
        {
            new { Key = "planning", Label = "规划", Types = new[] { "FreezeBaselines", "AnalyzeCreativeRequirements", "CompileBatchPlan", "PlanChapter" } },
            new { Key = "context", Label = "上下文", Types = new[] { "CompileChapterContext" } },
            new { Key = "writing", Label = "正文生成", Types = new[] { "WriteCandidate", "DirectedReworkDraft", "DirectedRework" } },
            new { Key = "review", Label = "双评审", Types = new[] { "ReviewContinuity", "ReviewLiteraryQuality", "ExtractContinuitySummary", "BatchImpactAnalysis" } },
            new { Key = "acceptance", Label = "验收门", Types = new[] { BookProductionWorkflow.AcceptanceGate } },
            new { Key = "merge", Label = "正史合并", Types = new[] { BookProductionWorkflow.PrefixMerge } }
        };
        return groups.Select(group =>
        {
            var tasks = state.Tasks.Where(task => group.Types.Contains(task.TaskType, StringComparer.Ordinal)).ToArray();
            var completed = tasks.Count(task => task.Status is "completed" or "reused");
            var status = tasks.Any(task => task.Status is "failed" or "awaiting_decision")
                ? "blocked"
                : tasks.Any(task => task.Status == "running")
                    ? "running"
                    : tasks.Length > 0 && completed == tasks.Length
                        ? "completed"
                        : "pending";
            var latest = tasks.OrderByDescending(task => task.UpdatedAt).FirstOrDefault();
            var artifactCount = state.Artifacts.Count(artifact => tasks.Any(task => task.TaskId == artifact.TaskId));
            return new WorkflowProductionStage(
                group.Key,
                group.Label,
                "Goal 状态机",
                status,
                $"{completed}/{tasks.Length} 个任务完成",
                latest == null ? "当前批次尚未进入该阶段。" : $"最近任务：{latest.TaskType} · {latest.Status}",
                artifactCount,
                completed,
                tasks.Length,
                latest?.UpdatedAt.ToString("O") ?? string.Empty,
                state.Artifacts.FirstOrDefault(artifact => tasks.Any(task => task.TaskId == artifact.TaskId))?.ArtifactId ?? string.Empty,
                state.ActiveTaskGraphId,
                tasks.Length == 0 ? "当前任务图没有该阶段节点。" : string.Empty,
                status == "blocked" ? "在工作流中处理失败或决策节点。" : "等待状态机推进。",
                []);
        }).ToArray();
    }

    private async Task<WorkflowGoalState?> LoadGoalStateAsync(
        string userId,
        string projectId,
        string goalId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(goalId))
            return null;
        var goal = await _db.CreativeGoals.AsNoTracking().SingleAsync(item =>
            item.UserId == userId && item.ProjectId == projectId && item.Id == goalId,
            cancellationToken);
        var production = await _db.BookProductions.AsNoTracking().SingleOrDefaultAsync(item =>
            item.UserId == userId && item.GoalId == goalId,
            cancellationToken);
        var graph = await _db.TaskGraphVersions.AsNoTracking()
            .Where(item => item.UserId == userId && item.GoalId == goalId && item.Status == "active")
            .OrderByDescending(item => item.Version)
            .FirstOrDefaultAsync(cancellationToken);
        var batches = production == null
            ? []
            : await _db.ProductionBatches.AsNoTracking()
                .Where(item => item.UserId == userId && item.BookProductionId == production.Id)
                .OrderBy(item => item.BatchNumber)
                .Select(item => new WorkflowGoalBatchState(
                    item.Id,
                    item.BatchNumber,
                    item.StartChapterNumber,
                    item.EndChapterNumber,
                    item.Status,
                    item.AcceptanceActor,
                    item.TaskGraphVersionId ?? string.Empty,
                    item.CanonBranchId ?? string.Empty))
                .ToListAsync(cancellationToken);
        var tasks = graph == null
            ? []
            : await _db.KernelTasks.AsNoTracking()
                .Where(item => item.UserId == userId && item.TaskGraphVersionId == graph.Id)
                .OrderBy(item => item.Priority)
                .Select(item => new WorkflowGoalTaskState(
                    item.Id,
                    item.TaskType,
                    item.Status,
                    item.KernelName,
                    item.BranchId ?? string.Empty,
                    item.Attempt,
                    item.MaxAttempts,
                    item.UpdatedAt))
                .ToListAsync(cancellationToken);
        var candidates = await _db.CandidateChapters.AsNoTracking()
            .Where(item => item.UserId == userId && item.GoalId == goalId)
            .OrderBy(item => item.ChapterNumber)
            .ThenByDescending(item => item.Version)
            .Select(item => new WorkflowGoalCandidateState(
                item.Id,
                item.ChapterNumber,
                item.Version,
                item.Status,
                item.Authorship,
                item.IsProtected,
                item.CurrentArtifactId))
            .ToListAsync(cancellationToken);
        var artifacts = await _db.KernelArtifacts.AsNoTracking()
            .Where(item => item.UserId == userId && item.GoalId == goalId)
            .OrderByDescending(item => item.CreatedAt)
            .Take(200)
            .Select(item => new WorkflowGoalArtifactState(
                item.Id,
                item.ArtifactType,
                item.TaskId,
                item.BranchId ?? string.Empty,
                item.Status,
                item.CreatedAt))
            .ToListAsync(cancellationToken);
        return new WorkflowGoalState(
            goal.Id,
            goal.Status,
            goal.ExecutionStrategy,
            production?.Status ?? string.Empty,
            production?.CurrentBatchNumber ?? 0,
            production?.NextChapterNumber ?? 0,
            graph?.Id ?? string.Empty,
            graph?.Version ?? 0,
            batches,
            tasks,
            candidates,
            artifacts);
    }

    public async Task<VolumeArcResponse> CreateVolumeArcAsync(
        CreateVolumeArcRequest request, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var project = await _db.NovelProjects
            .FirstOrDefaultAsync(p => p.Id == request.ProjectId && p.UserId == userId, ct);

        if (project == null)
            throw new KeyNotFoundException($"Project {request.ProjectId} not found");

        var idempotencyKey = EmptyToNull(request.IdempotencyKey);
        if (idempotencyKey != null)
        {
            var existing = await FindVolumeArcByIdempotencyKeyAsync(
                    request.ProjectId,
                    idempotencyKey,
                    ct)
                .ConfigureAwait(false);
            if (existing != null)
                return MapToResponse(existing);
        }

        var volumeArc = new VolumeArc
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            ProjectId = request.ProjectId,
            IdempotencyKey = idempotencyKey,
            VolumeNumber = request.VolumeNumber,
            VolumeTitle = request.VolumeTitle,
            VolumeTheme = request.VolumeTheme,
            TargetChapters = request.TargetChapters,
            CurrentChapters = 0,
            Act1Setup = request.Act1Setup,
            Act2Confrontation = request.Act2Confrontation,
            Act3Climax = request.Act3Climax,
            Act4Resolution = request.Act4Resolution,
            KeyEvents = request.KeyEvents,
            MajorConflict = request.MajorConflict,
            ConflictEscalation = request.ConflictEscalation,
            Status = "planned",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.VolumeArcs.Add(volumeArc);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (idempotencyKey != null)
        {
            _db.Entry(volumeArc).State = EntityState.Detached;
            var existing = await FindVolumeArcByIdempotencyKeyAsync(
                    request.ProjectId,
                    idempotencyKey,
                    ct)
                .ConfigureAwait(false);
            if (existing != null)
                return MapToResponse(existing);

            throw;
        }

        _logger.LogInformation("Created volume arc {VolumeArcId} for project {ProjectId}", volumeArc.Id, request.ProjectId);

        return MapToResponse(volumeArc);
    }

    public async Task<List<VolumeArcResponse>> ListVolumeArcsAsync(string projectId, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var project = await _db.NovelProjects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, ct);

        if (project == null)
            throw new KeyNotFoundException($"Project {projectId} not found");

        var volumeArcs = await _db.VolumeArcs
            .Where(v => v.ProjectId == projectId)
            .OrderBy(v => v.VolumeNumber)
            .ToListAsync(ct);

        return volumeArcs.Select(MapToResponse).ToList();
    }

    public async Task<VolumeArcResponse> GetVolumeArcAsync(string volumeArcId, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var volumeArc = await _db.VolumeArcs
            .Include(v => v.Project)
            .FirstOrDefaultAsync(v => v.Id == volumeArcId, ct);

        if (volumeArc == null)
            throw new KeyNotFoundException($"Volume arc {volumeArcId} not found");

        if (volumeArc.Project?.UserId != userId)
            throw new UnauthorizedAccessException("Access denied");

        return MapToResponse(volumeArc);
    }

    public async Task<VolumeArcResponse> UpdateVolumeArcAsync(
        string volumeArcId, UpdateVolumeArcRequest request, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var volumeArc = await _db.VolumeArcs
            .Include(v => v.Project)
            .FirstOrDefaultAsync(v => v.Id == volumeArcId, ct);

        if (volumeArc == null)
            throw new KeyNotFoundException($"Volume arc {volumeArcId} not found");

        if (volumeArc.Project?.UserId != userId)
            throw new UnauthorizedAccessException("Access denied");

        if (request.VolumeTitle != null)
            volumeArc.VolumeTitle = request.VolumeTitle;
        if (request.VolumeTheme != null)
            volumeArc.VolumeTheme = request.VolumeTheme;
        if (request.TargetChapters.HasValue)
            volumeArc.TargetChapters = request.TargetChapters;
        if (request.CurrentChapters.HasValue)
            volumeArc.CurrentChapters = request.CurrentChapters.Value;
        if (request.Act1Setup != null)
            volumeArc.Act1Setup = request.Act1Setup;
        if (request.Act2Confrontation != null)
            volumeArc.Act2Confrontation = request.Act2Confrontation;
        if (request.Act3Climax != null)
            volumeArc.Act3Climax = request.Act3Climax;
        if (request.Act4Resolution != null)
            volumeArc.Act4Resolution = request.Act4Resolution;
        if (request.KeyEvents != null)
            volumeArc.KeyEvents = request.KeyEvents;
        if (request.MajorConflict != null)
            volumeArc.MajorConflict = request.MajorConflict;
        if (request.ConflictEscalation != null)
            volumeArc.ConflictEscalation = request.ConflictEscalation;
        if (request.Status != null)
        {
            volumeArc.Status = request.Status;
            if (request.Status == "completed" && volumeArc.CompletedAt == null)
                volumeArc.CompletedAt = DateTime.UtcNow;
        }

        volumeArc.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Updated volume arc {VolumeArcId}", volumeArcId);

        return MapToResponse(volumeArc);
    }

    public async Task DeleteVolumeArcAsync(string volumeArcId, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var volumeArc = await _db.VolumeArcs
            .Include(v => v.Project)
            .FirstOrDefaultAsync(v => v.Id == volumeArcId, ct);

        if (volumeArc == null)
            throw new KeyNotFoundException($"Volume arc {volumeArcId} not found");

        if (volumeArc.Project?.UserId != userId)
            throw new UnauthorizedAccessException("Access denied");

        _db.VolumeArcs.Remove(volumeArc);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted volume arc {VolumeArcId}", volumeArcId);
    }

    private static VolumeArcResponse MapToResponse(VolumeArc volumeArc)
    {
        return new VolumeArcResponse
        {
            Id = volumeArc.Id,
            UserId = volumeArc.UserId,
            ProjectId = volumeArc.ProjectId,
            VolumeNumber = volumeArc.VolumeNumber,
            VolumeTitle = volumeArc.VolumeTitle,
            VolumeTheme = volumeArc.VolumeTheme,
            TargetChapters = volumeArc.TargetChapters,
            CurrentChapters = volumeArc.CurrentChapters,
            Act1Setup = volumeArc.Act1Setup,
            Act2Confrontation = volumeArc.Act2Confrontation,
            Act3Climax = volumeArc.Act3Climax,
            Act4Resolution = volumeArc.Act4Resolution,
            KeyEvents = volumeArc.KeyEvents,
            MajorConflict = volumeArc.MajorConflict,
            ConflictEscalation = volumeArc.ConflictEscalation,
            Status = volumeArc.Status,
            CreatedAt = volumeArc.CreatedAt,
            UpdatedAt = volumeArc.UpdatedAt,
            CompletedAt = volumeArc.CompletedAt
        };
    }

    private async Task<VolumeArc?> FindVolumeArcByIdempotencyKeyAsync(
        string projectId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        await _db.VolumeArcs
            .AsNoTracking()
            .FirstOrDefaultAsync(volumeArc =>
                    volumeArc.ProjectId == projectId &&
                    volumeArc.IdempotencyKey == idempotencyKey,
                cancellationToken)
            .ConfigureAwait(false);

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<IReadOnlyList<WorkflowCreativeIntentEvidence>> LoadCreativeIntentEvidenceAsync(
        string projectId,
        string userId,
        CancellationToken ct)
    {
        return await _db.CreativeIntents
            .AsNoTracking()
            .Where(intent => intent.ProjectId == projectId && intent.UserId == userId)
            .OrderByDescending(intent => intent.UpdatedAt)
            .Take(50)
            .Select(intent => new WorkflowCreativeIntentEvidence(
                intent.Id,
                intent.NormalizedIntent,
                intent.TargetScope,
                intent.TargetChapterId ?? string.Empty,
                intent.ImpactLevel,
                intent.Source,
                intent.Status))
            .ToListAsync(ct);
    }

    private async Task<IReadOnlyList<WorkflowChapterRevisionPlanSummary>> LoadChapterRevisionPlanSummariesAsync(
        string projectId,
        string userId,
        CancellationToken ct)
    {
        var plans = await _db.RevisionPlans
            .AsNoTracking()
            .Where(plan => plan.ProjectId == projectId && plan.UserId == userId)
            .OrderByDescending(plan => plan.UpdatedAt)
            .Take(200)
            .Select(plan => new
            {
                plan.Id,
                plan.Source,
                plan.PlanType,
                plan.TargetScope,
                TargetChapterId = plan.TargetChapterId ?? string.Empty,
                TargetChapterLogicalId = plan.TargetChapterLogicalId ?? string.Empty,
                TargetChapterDisplayName = plan.TargetChapterDisplayName ?? string.Empty,
                plan.Status,
                plan.RiskLevel,
                plan.Recommendation,
                plan.AffectedChapterIdsJson,
                plan.InvalidatedPackageIdsJson
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return plans
            .Select(plan => new WorkflowChapterRevisionPlanSummary(
                plan.Id,
                plan.Source,
                plan.PlanType,
                plan.TargetScope,
                plan.TargetChapterId,
                plan.TargetChapterLogicalId,
                plan.TargetChapterDisplayName,
                plan.Status,
                plan.RiskLevel,
                plan.Recommendation,
                ChapterIdentityResolver.ParseStringArray(plan.AffectedChapterIdsJson),
                ChapterIdentityResolver.ParseStringArray(plan.InvalidatedPackageIdsJson)))
            .ToList();
    }

    private async Task<IReadOnlyList<WorkflowProductionChain>> BuildWorkflowProductionChainsAsync(
        string projectId,
        string userId,
        IReadOnlyList<WorkflowProductionEventSummary> productionEvents,
        CancellationToken ct)
    {
        if (_productionStateQuery != null)
        {
            var state = await _productionStateQuery.QueryAsync(
                    new NovelProductionStateQueryRequest(
                        UserId: userId,
                        SessionId: string.Empty,
                        ProjectId: projectId,
                        RunId: string.Empty,
                        ChapterId: string.Empty,
                        ChapterNumber: 0,
                        IncludeEvents: true),
                    ct)
                .ConfigureAwait(false);
            if (state?.ProductionChains.Count > 0)
                return state.ProductionChains.Select(MapProductionChain).ToList();
        }

        return _productionChainProjection.BuildWorkflowChains(productionEvents);
    }

    private static WorkflowProductionChain MapProductionChain(NovelProductionChainState chain) =>
        new(
            chain.Id,
            chain.ChapterId,
            chain.ChapterLogicalId,
            chain.ChapterDisplayName,
            chain.RuntimeRunId,
            chain.PackageId,
            chain.Status,
            chain.Summary,
            chain.Steps
                .OrderBy(step => step.CreatedAt)
                .LastOrDefault(step => step.CreatedAt != default)
                ?.CreatedAt
                .ToString("O") ?? string.Empty,
            chain.ChapterVersionId,
            chain.ChapterVersionNumber,
            chain.FactSnapshotId,
            chain.FactSnapshotVersion,
            chain.RevisionPlanIds,
            chain.RebuildLinks.Select(link => new WorkflowPackageRebuildLinkEvidence(
                link.OldPackageId,
                link.OldPackageStatus,
                link.NewPackageId,
                link.NewPackageStatus,
                link.NewPackageKind,
                link.ChapterId,
                link.RuntimeRunId)).ToList(),
            chain.Steps.Select(MapProductionChainStep).ToList(),
            chain.Evidence);

    private static WorkflowProductionChainStep MapProductionChainStep(NovelProductionChainStepState step) =>
        new(
            step.Key,
            step.Label,
            step.Status,
            step.EventId,
            step.EventType,
            step.Stage,
            step.ArtifactType,
            step.ArtifactId,
            step.Message,
            step.CreatedAt == default ? string.Empty : step.CreatedAt.ToString("O"),
            step.OutboxEventId);

    private async Task<IReadOnlyList<WorkflowToolExecutionSummary>> LoadProjectToolExecutionsAsync(
        string projectId,
        string userId,
        CancellationToken ct)
    {
        var rows = await _db.AgentToolExecutions
            .AsNoTracking()
            .Where(execution => execution.ProjectId == projectId && execution.UserId == userId)
            .OrderByDescending(execution => execution.StartedAt)
            .Take(30)
            .Select(execution => new
            {
                execution.Id,
                RunId = execution.RunId ?? string.Empty,
                execution.ToolName,
                execution.Phase,
                execution.Status,
                execution.Risk,
                execution.ResultMessage,
                execution.ErrorMessage,
                execution.SemanticContractJson,
                execution.FailureJson,
                execution.StartedAt,
                execution.CompletedAt
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows
            .Select(row => new WorkflowToolExecutionSummary
            {
                Id = row.Id,
                RunId = row.RunId,
                ToolName = row.ToolName,
                Phase = row.Phase,
                Status = row.Status,
                Risk = row.Risk,
                ResultMessage = row.ResultMessage,
                ErrorMessage = row.ErrorMessage,
                StartedAt = row.StartedAt.ToString("O"),
                CompletedAt = row.CompletedAt?.ToString("O") ?? string.Empty,
                SemanticContract = ParseToolSemanticContract(row.SemanticContractJson),
                Failure = ParseToolFailure(row.FailureJson)
            })
            .Reverse()
            .ToList();
    }

    private static WorkflowToolSemanticContractSummary ParseToolSemanticContract(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
            return new WorkflowToolSemanticContractSummary();

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return new WorkflowToolSemanticContractSummary
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
            return new WorkflowToolSemanticContractSummary();
        }
    }

    private static WorkflowToolFailureSummary? ParseToolFailure(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return new WorkflowToolFailureSummary
            {
                Code = GetString(root, "code"),
                FailedStage = GetString(root, "failedStage"),
                Reason = GetString(root, "reason"),
                Recoverable = GetBool(root, "recoverable"),
                RecommendedAction = GetString(root, "recommendedAction"),
                InputArtifacts = GetToolInputArtifacts(root)
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool GetBool(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        value.GetBoolean();

    private static List<string> GetStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            return new List<string>();

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            .Select(item => item.GetString()!.Trim())
            .ToList();
    }

    private static IReadOnlyList<WorkflowToolInputArtifactSummary> GetToolInputArtifacts(JsonElement root)
    {
        if (!root.TryGetProperty("inputArtifacts", out var value) || value.ValueKind != JsonValueKind.Array)
            return Array.Empty<WorkflowToolInputArtifactSummary>();

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new WorkflowToolInputArtifactSummary(
                GetString(item, "artifactName"),
                GetString(item, "status"),
                GetString(item, "artifactId"),
                GetString(item, "message"),
                GetBool(item, "blocksExecution"),
                GetStringArray(item, "recommendedActions")))
            .ToList();
    }

    private static NovelProjectInfo MapToCatalogProject(NovelProject project)
    {
        return new NovelProjectInfo
        {
            Id = project.Id,
            Title = project.Title,
            Genre = project.Genre ?? project.StoryConstitution?.Genre ?? string.Empty,
            SubGenre = project.SubGenre ?? project.StoryConstitution?.SubGenre ?? string.Empty,
            CoreHook = project.CoreHook ?? project.StoryConstitution?.CoreHook ?? string.Empty,
            ReaderPromise = project.StoryConstitution?.ReaderPromise ?? string.Empty,
            Status = project.Status,
            CreatedAt = project.CreatedAt,
            UpdatedAt = project.UpdatedAt
        };
    }

    private async Task<NovelLibraryDocument?> BuildDatabaseLibraryAsync(
        NovelProject project,
        NovelLibraryDocument currentLibrary,
        IReadOnlyList<WorkflowChapterArtifactSummary> chapterArtifacts,
        IReadOnlyList<WorkflowProductionChain> productionChains,
        IReadOnlyList<WorkflowCreativeIntentEvidence> creativeIntents,
        CancellationToken ct)
    {
        var volumeArcs = await _db.VolumeArcs
            .Where(v => v.ProjectId == project.Id && v.UserId == project.UserId)
            .OrderBy(v => v.VolumeNumber)
            .ToListAsync(ct);
        var canonicalVolumes = await _db.Volumes
            .Where(v => v.ProjectId == project.Id)
            .OrderBy(v => v.VolumeNumber)
            .ToListAsync(ct);
        var chapters = await _db.Chapters
            .Where(c => c.ProjectId == project.Id)
            .OrderBy(c => c.ChapterNumber)
            .ToListAsync(ct);

        if (canonicalVolumes.Count == 0 && volumeArcs.Count == 0 && chapters.Count == 0)
            return null;

        var chapterContents = await LoadChapterContentsAsync(chapters, ct);
        var revisionPlans = await LoadChapterRevisionPlanSummariesAsync(project.Id, project.UserId, ct)
            .ConfigureAwait(false);
        var volumes = BuildDatabaseVolumes(canonicalVolumes, volumeArcs, chapters, chapterArtifacts, chapterContents, productionChains, creativeIntents, revisionPlans).ToList();
        var allChapters = volumes.SelectMany(v => v.Chapters).ToList();
        var generatedCount = allChapters.Count(c => c.HasGeneratedContent || IsCommitted(c.Status));
        var plannedChapterCount = Math.Max(
            allChapters.Count,
            canonicalVolumes.Count > 0
                ? allChapters.Count
                : volumeArcs.Sum(v => Math.Max(v.TargetChapters ?? 0, v.CurrentChapters)));
        var needsRewriteCount = allChapters.Count(c => c.NeedsRewrite);
        var selectedChapter = allChapters
            .OrderByDescending(c => c.HasGeneratedContent)
            .ThenBy(c => c.VolumeId)
            .ThenBy(c => c.BeatIndex == 0 ? int.MaxValue : c.BeatIndex)
            .FirstOrDefault();

        var book = new NovelBookView(
            project.Id,
            project.Title,
            project.Genre ?? project.StoryConstitution?.Genre ?? string.Empty,
            project.SubGenre ?? project.StoryConstitution?.SubGenre ?? string.Empty,
            project.CoreHook ?? project.StoryConstitution?.CoreHook ?? string.Empty,
            project.StoryConstitution?.ReaderPromise ?? string.Empty,
            project.Status,
            currentLibrary.ActiveBook?.IsActive ?? project.Status != "archived",
            canonicalVolumes.Count > 0 ? canonicalVolumes.Count : volumeArcs.Count,
            generatedCount,
            plannedChapterCount,
            needsRewriteCount,
            project.UpdatedAt.ToString("O"),
            selectedChapter);

        var books = currentLibrary.Books
            .Where(b => !string.Equals(b.ProjectId, project.Id, StringComparison.OrdinalIgnoreCase))
            .Concat(new[] { book })
            .OrderByDescending(b => b.IsActive)
            .ThenByDescending(b => b.UpdatedAt)
            .ToList();

        return new NovelLibraryDocument(
            books,
            book,
            volumes,
            selectedChapter,
            generatedCount,
            plannedChapterCount,
            needsRewriteCount);
    }

    private static IEnumerable<NovelVolumeView> BuildDatabaseVolumes(
        IReadOnlyList<Volume> canonicalVolumes,
        IReadOnlyList<VolumeArc> volumeArcs,
        IReadOnlyList<Chapter> chapters,
        IReadOnlyList<WorkflowChapterArtifactSummary> chapterArtifacts,
        IReadOnlyDictionary<string, string> chapterContents,
        IReadOnlyList<WorkflowProductionChain> productionChains,
        IReadOnlyList<WorkflowCreativeIntentEvidence> creativeIntents,
        IReadOnlyList<WorkflowChapterRevisionPlanSummary> revisionPlans)
    {
        var assignedChapterIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var syntheticChapters = BuildSyntheticWorkflowChapters(chapterArtifacts);
        var assignedSyntheticIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var databaseChapterIds = chapters.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var databaseChapterNumbers = chapters
            .Where(c => c.ChapterNumber > 0)
            .Select(c => c.ChapterNumber)
            .ToHashSet();
        var titleByChapterId = chapterArtifacts
            .Where(a => !string.IsNullOrWhiteSpace(a.ChapterId) && !string.IsNullOrWhiteSpace(a.CandidateTitle))
            .GroupBy(a => a.ChapterId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                ChapterId = group.Key,
                Title = group
                    .OrderByDescending(a => a.UpdatedAt)
                    .Select(a => a.CandidateTitle)
                    .FirstOrDefault(title => !IsWeakChapterTitle(title))
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .ToDictionary(item => item.ChapterId, item => item.Title!, StringComparer.OrdinalIgnoreCase);

        foreach (var volume in canonicalVolumes.OrderBy(v => v.VolumeNumber))
        {
            var volumeChapters = chapters
                .Where(c => !assignedChapterIds.Contains(c.Id))
                .Where(c => string.Equals(c.VolumeId, volume.Id, StringComparison.OrdinalIgnoreCase))
                .Select(chapter => MapDatabaseChapter(chapter, volume.Id, volume.Title, volume.VolumeNumber, chapterContents, titleByChapterId, productionChains, creativeIntents, revisionPlans))
                .OrderBy(c => c.BeatIndex <= 0 ? int.MaxValue : c.BeatIndex)
                .ThenBy(c => c.ChapterId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var chapter in volumeChapters)
                assignedChapterIds.Add(chapter.ChapterId);

            yield return new NovelVolumeView(
                volume.Id,
                volume.Title,
                "committed",
                volumeChapters.FirstOrDefault()?.ChapterId ?? string.Empty,
                volumeChapters.LastOrDefault()?.ChapterId ?? string.Empty,
                volumeChapters.Count,
                volumeChapters);
        }

        if (canonicalVolumes.Count > 0)
        {
            var canonicalUnassigned = chapters
                .Where(c => !assignedChapterIds.Contains(c.Id))
                .Select(chapter => MapDatabaseChapter(chapter, "database-chapters", "未归档章节", 0, chapterContents, titleByChapterId, productionChains, creativeIntents, revisionPlans))
                .ToList();

            if (canonicalUnassigned.Count > 0)
            {
                yield return new NovelVolumeView(
                    "database-chapters",
                    "未归档章节",
                    "Draft",
                    canonicalUnassigned.FirstOrDefault()?.ChapterId ?? string.Empty,
                    canonicalUnassigned.LastOrDefault()?.ChapterId ?? string.Empty,
                    canonicalUnassigned.Count,
                    canonicalUnassigned);
            }

            yield break;
        }

        var nextChapterStart = 1;

        foreach (var volume in volumeArcs)
        {
            var targetCount = Math.Max(volume.TargetChapters ?? 0, volume.CurrentChapters);
            var chapterEnd = targetCount > 0 ? nextChapterStart + targetCount - 1 : int.MaxValue;
            var volumeChapters = chapters
                .Where(c => !assignedChapterIds.Contains(c.Id))
                .Where(c => string.Equals(c.VolumeId, volume.Id, StringComparison.OrdinalIgnoreCase))
                .Concat(chapters
                    .Where(c => !assignedChapterIds.Contains(c.Id))
                    .Where(c => !string.Equals(c.VolumeId, volume.Id, StringComparison.OrdinalIgnoreCase))
                    .Where(c => IsChapterNumberInRange(c.ChapterNumber, nextChapterStart, chapterEnd)))
                .DistinctBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
                .Select(chapter => MapDatabaseChapter(chapter, volume.Id, volume.VolumeTitle, volume.VolumeNumber, chapterContents, titleByChapterId, productionChains, creativeIntents, revisionPlans))
                .ToList();

            var syntheticForVolume = syntheticChapters
                .Where(artifact => !assignedSyntheticIds.Contains(artifact.ChapterId))
                .Where(artifact => !IsCoveredByDatabaseChapter(artifact, databaseChapterIds, databaseChapterNumbers))
                .Where(artifact => IsChapterNumberInRange(ExtractChapterNumber(artifact.ChapterId), nextChapterStart, chapterEnd))
                .Select((artifact, index) =>
                    MapWorkflowArtifactChapter(artifact, volume.Id, volume.VolumeTitle, volume.VolumeNumber, volumeChapters.Count + index + 1, productionChains, creativeIntents, revisionPlans))
                .ToList();
            volumeChapters.AddRange(syntheticForVolume);
            volumeChapters = volumeChapters
                .OrderBy(c => c.BeatIndex <= 0 ? int.MaxValue : c.BeatIndex)
                .ThenBy(c => c.ChapterId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var chapter in volumeChapters)
                assignedChapterIds.Add(chapter.ChapterId);
            foreach (var chapter in syntheticForVolume)
                assignedSyntheticIds.Add(chapter.ChapterId);
            if (targetCount > 0)
                nextChapterStart = chapterEnd + 1;

            yield return new NovelVolumeView(
                volume.Id,
                volume.VolumeTitle,
                volume.Status,
                volumeChapters.FirstOrDefault()?.ChapterId ?? string.Empty,
                volumeChapters.LastOrDefault()?.ChapterId ?? string.Empty,
                volume.TargetChapters ?? volumeChapters.Count,
                volumeChapters);
        }

        var unassigned = chapters
            .Where(c => !assignedChapterIds.Contains(c.Id))
            .Select(chapter => MapDatabaseChapter(chapter, "database-chapters", "数据库章节", 0, chapterContents, titleByChapterId, productionChains, creativeIntents, revisionPlans))
            .ToList();
        unassigned.AddRange(syntheticChapters
            .Where(a => !assignedSyntheticIds.Contains(a.ChapterId))
            .Where(a => !IsCoveredByDatabaseChapter(a, databaseChapterIds, databaseChapterNumbers))
            .Select((artifact, index) => MapWorkflowArtifactChapter(artifact, "workflow-artifacts", "工作流章节", 0, index + 1, productionChains, creativeIntents, revisionPlans)));

        if (unassigned.Count > 0 || volumeArcs.Count == 0)
        {
            yield return new NovelVolumeView(
                "database-chapters",
                "数据库章节",
                "Draft",
                unassigned.FirstOrDefault()?.ChapterId ?? string.Empty,
                unassigned.LastOrDefault()?.ChapterId ?? string.Empty,
                unassigned.Count,
                unassigned);
        }
    }

    private static bool IsChapterNumberInRange(int chapterNumber, int start, int end) =>
        chapterNumber > 0 && chapterNumber >= start && chapterNumber <= end;

    private static bool IsCoveredByDatabaseChapter(
        WorkflowChapterArtifactSummary artifact,
        ISet<string> databaseChapterIds,
        ISet<int> databaseChapterNumbers)
    {
        if (databaseChapterIds.Contains(artifact.ChapterId))
            return true;

        var chapterNumber = ExtractChapterNumber(artifact.ChapterId);
        return chapterNumber > 0 && databaseChapterNumbers.Contains(chapterNumber);
    }

    private static List<WorkflowChapterArtifactSummary> BuildSyntheticWorkflowChapters(
        IReadOnlyList<WorkflowChapterArtifactSummary> chapterArtifacts)
    {
        return chapterArtifacts
            .Where(a => !string.IsNullOrWhiteSpace(a.ChapterId) && (a.HasDraft || a.GateStatus.Length > 0 || a.DraftStatus.Length > 0))
            .GroupBy(a => a.ChapterId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(a => a.HasDraft)
                .ThenByDescending(a => string.Equals(a.GateStatus, "validated", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(a => a.UpdatedAt)
                .First())
            .OrderBy(a => ExtractChapterNumber(a.ChapterId))
            .ToList();
    }

    private static NovelChapterView MapWorkflowArtifactChapter(
        WorkflowChapterArtifactSummary artifact,
        string volumeId,
        string volumeTitle,
        int volumeNumber,
        int fallbackIndex,
        IReadOnlyList<WorkflowProductionChain> productionChains,
        IReadOnlyList<WorkflowCreativeIntentEvidence> creativeIntents,
        IReadOnlyList<WorkflowChapterRevisionPlanSummary> revisionPlans)
    {
        var beatIndex = ExtractChapterNumber(artifact.ChapterId);
        if (beatIndex <= 0)
            beatIndex = fallbackIndex;
        var status = string.IsNullOrWhiteSpace(artifact.Status) ? artifact.DraftStatus : artifact.Status;
        var writingStatus = ResolveArtifactWritingStatus(artifact);
        var title = string.IsNullOrWhiteSpace(artifact.CandidateTitle)
            ? $"第 {beatIndex} 章"
            : artifact.CandidateTitle;

        return new NovelChapterView(
            artifact.ChapterId,
            title,
            volumeId,
            volumeTitle,
            beatIndex,
            volumeNumber > 0 ? $"第 {volumeNumber} 卷" : string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            status,
            artifact.RunId,
            artifact.Intent,
            artifact.UpdatedAt,
            artifact.HasDraft,
            string.Equals(artifact.QualityStatus, "quality_failed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(artifact.Status, "blocked", StringComparison.OrdinalIgnoreCase),
            0,
            artifact.CandidateTitle,
            artifact.DraftPreview,
            artifact.CandidateTitle,
            artifact.QualityScore,
            0,
            artifact.QualityIssues,
            Array.Empty<string>(),
            writingStatus,
            string.Join("; ", artifact.ContextWarnings),
            artifact.DraftStatus,
            artifact.GateStatus,
            string.Equals(artifact.GateStatus, "validated", StringComparison.OrdinalIgnoreCase),
            string.Equals(artifact.GateStatus, "validated", StringComparison.OrdinalIgnoreCase),
            string.Equals(artifact.GateStatus, "validated", StringComparison.OrdinalIgnoreCase),
            artifact.RagRecallCount > 0,
            artifact.RagRecallCount,
            0,
            artifact.GateIssues,
            artifact.RepairHints,
            artifact.DependencyWarnings,
            artifact.ContextWarnings,
            true,
            false,
            ArtifactUserVisibleStatus(artifact),
            writingStatus,
            artifact.RunId,
            artifact.GateStatus,
            artifact.QualityStatus,
            BuildChapterProductionSummary(artifact.ChapterId, productionChains, creativeIntents, revisionPlans));
    }

    private static string ResolveArtifactWritingStatus(WorkflowChapterArtifactSummary artifact)
    {
        if (string.Equals(artifact.GateStatus, "validated", StringComparison.OrdinalIgnoreCase))
            return "validated";
        if (artifact.HasDraft)
            return "draft_generated";
        if (!string.IsNullOrWhiteSpace(artifact.DraftStatus))
            return artifact.DraftStatus;
        return "planned";
    }

    private static string ArtifactUserVisibleStatus(WorkflowChapterArtifactSummary artifact)
    {
        if (string.Equals(artifact.GateStatus, "validated", StringComparison.OrdinalIgnoreCase))
            return "硬门禁已通过";
        if (artifact.HasDraft)
            return "草稿在工作流中";
        return "工作流章节";
    }

    private static int ExtractChapterNumber(string chapterId)
    {
        return ChapterParserHelper.ExtractChapterNumber(chapterId ?? string.Empty);
    }

    private async Task<Dictionary<string, string>> LoadChapterContentsAsync(
        IReadOnlyList<Chapter> chapters,
        CancellationToken ct)
    {
        var documentIds = chapters
            .Select(c => c.CurrentDocumentId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (documentIds.Count == 0)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var chunks = await _db.ContentChunks
            .AsNoTracking()
            .Where(chunk => documentIds.Contains(chunk.DocumentId))
            .OrderBy(chunk => chunk.ChunkIndex)
            .Select(chunk => new { chunk.DocumentId, chunk.ChunkText })
            .ToListAsync(ct);

        var contentByDocumentId = chunks
            .GroupBy(chunk => chunk.DocumentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => string.Join(string.Empty, group.Select(chunk => chunk.ChunkText)),
                StringComparer.OrdinalIgnoreCase);

        return chapters
            .Where(chapter => !string.IsNullOrWhiteSpace(chapter.CurrentDocumentId))
            .Where(chapter => contentByDocumentId.ContainsKey(chapter.CurrentDocumentId!))
            .ToDictionary(
                chapter => chapter.Id,
                chapter => contentByDocumentId[chapter.CurrentDocumentId!],
                StringComparer.OrdinalIgnoreCase);
    }

    private static NovelChapterView MapDatabaseChapter(
        Chapter chapter,
        string volumeId,
        string volumeTitle,
        int volumeNumber,
        IReadOnlyDictionary<string, string> chapterContents,
        IReadOnlyDictionary<string, string> titleByChapterId,
        IReadOnlyList<WorkflowProductionChain> productionChains,
        IReadOnlyList<WorkflowCreativeIntentEvidence> creativeIntents,
        IReadOnlyList<WorkflowChapterRevisionPlanSummary> revisionPlans)
    {
        var writingStatus = ResolveChapterWritingStatus(chapter);
        var content = chapterContents.TryGetValue(chapter.Id, out var value) ? value : string.Empty;
        var wordCount = chapter.WordCount > 0 ? chapter.WordCount : CountWords(content);
        var hasContent = wordCount > 0 || !string.IsNullOrWhiteSpace(content) || IsCommitted(chapter.Status);
        var committed = IsCommitted(chapter.Status);
        titleByChapterId.TryGetValue(chapter.Id, out var artifactTitle);
        var displayTitle = ResolveReadableChapterTitle(chapter.Title, content, chapter.ChapterNumber, artifactTitle);

        return new NovelChapterView(
            chapter.Id,
            displayTitle,
            volumeId,
            volumeTitle,
            chapter.ChapterNumber,
            volumeNumber > 0 ? $"第 {volumeNumber} 卷" : string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            chapter.Status,
            string.Empty,
            string.Empty,
            chapter.UpdatedAt.ToString("O"),
            hasContent,
            IsRewriteStatus(chapter.Status),
            wordCount,
            displayTitle,
            content,
            string.Empty,
            0,
            0,
            Array.Empty<string>(),
            Array.Empty<string>(),
            writingStatus,
            string.Empty,
            committed ? "committed" : string.Empty,
            committed ? "validated" : string.Empty,
            committed,
            committed,
            committed,
            false,
            0,
            0,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            true,
            committed,
            committed ? "已入库" : "数据库章节",
            writingStatus,
            string.Empty,
            committed ? $"gate:{chapter.Id}" : string.Empty,
            string.Empty,
            BuildChapterProductionSummary(chapter.Id, productionChains, creativeIntents, revisionPlans));
    }

    private static WorkflowChapterProductionSummary? BuildChapterProductionSummary(
        string chapterId,
        IReadOnlyList<WorkflowProductionChain> productionChains,
        IReadOnlyList<WorkflowCreativeIntentEvidence> creativeIntents,
        IReadOnlyList<WorkflowChapterRevisionPlanSummary> revisionPlans)
    {
        if (string.IsNullOrWhiteSpace(chapterId) || productionChains.Count == 0)
            return null;

        var chain = productionChains
            .Where(item => ChainMatchesChapter(item, chapterId))
            .OrderByDescending(item => item.UpdatedAt ?? string.Empty, StringComparer.Ordinal)
            .ThenByDescending(item => item.ChapterVersionNumber)
            .ThenByDescending(item => item.FactSnapshotVersion)
            .FirstOrDefault();
        if (chain == null)
            return null;

        var evidence = chain.Evidence;
        var rebuildPackageIds = chain.RebuildLinks
            .Select(link => link.OldPackageId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var matchedRevisionPlans = revisionPlans
            .Where(plan => RevisionPlanMatchesChapterOrChain(plan, chapterId, chain, rebuildPackageIds))
            .Take(12)
            .ToList();
        var matchedCreativeIntents = creativeIntents
            .Where(intent => CreativeIntentMatchesChapter(intent, chapterId))
            .Select(intent => new WorkflowChapterCreativeIntentSummary(
                intent.IntentId,
                intent.NormalizedIntent,
                intent.TargetScope,
                intent.TargetChapterId,
                intent.ImpactLevel,
                intent.Source,
                intent.Status))
            .Take(12)
            .ToList();
        var hasCanonicalEvidence = !string.IsNullOrWhiteSpace(chain.ChapterVersionId)
            || !string.IsNullOrWhiteSpace(chain.FactSnapshotId)
            || chain.RevisionPlanIds.Count > 0
            || chain.RebuildLinks.Count > 0
            || matchedRevisionPlans.Count > 0
            || matchedCreativeIntents.Count > 0
            || evidence.Gate != null
            || evidence.FactSnapshot != null
            || evidence.AgentReview != null
            || evidence.ChapterChangeCount > 0
            || evidence.ChapterChangeArtifactIds.Count > 0;
        var traceItems = BuildChapterProductionTraceItems(chain, evidence, matchedRevisionPlans);
        var summaryStatus = ResolveChapterProductionSummaryStatus(chain, evidence);
        var summaryText = ResolveChapterProductionSummaryText(chain, evidence);

        return new WorkflowChapterProductionSummary(
            chain.Id,
            summaryStatus,
            summaryText,
            chain.RuntimeRunId,
            chain.PackageId,
            chain.UpdatedAt,
            chain.ChapterVersionId,
            chain.ChapterVersionNumber,
            chain.FactSnapshotId,
            chain.FactSnapshotVersion,
            evidence.FactSnapshot?.EndingState ?? string.Empty,
            ResolveChapterProductionGateStatus(chain, evidence),
            evidence.AgentReview?.OverallResult ?? string.Empty,
            evidence.AgentReview?.RecommendedAction ?? string.Empty,
            evidence.ChapterChangeCount,
            chain.RevisionPlanIds.Count,
            chain.RebuildLinks.Count,
            chain.RevisionPlanIds.ToList(),
            matchedRevisionPlans,
            rebuildPackageIds,
            matchedCreativeIntents,
            traceItems,
            hasCanonicalEvidence);
    }

    private static string ResolveChapterProductionSummaryStatus(
        WorkflowProductionChain chain,
        WorkflowProductionChainEvidence evidence)
    {
        var gateStatus = evidence.Gate?.Status ?? string.Empty;
        if (IsGateFailureStatus(gateStatus) && !IsCommittedProductionChain(chain))
            return gateStatus;

        return chain.Status;
    }

    private static string ResolveChapterProductionGateStatus(
        WorkflowProductionChain chain,
        WorkflowProductionChainEvidence evidence)
    {
        if (IsCommittedProductionChain(chain))
            return "validated";

        return evidence.Gate?.Status ?? string.Empty;
    }

    private static string ResolveChapterProductionSummaryText(
        WorkflowProductionChain chain,
        WorkflowProductionChainEvidence evidence)
    {
        var gateStatus = evidence.Gate?.Status ?? string.Empty;
        if (!IsGateFailureStatus(gateStatus) || IsCommittedProductionChain(chain))
            return chain.Summary;

        var issue = evidence.Gate?.Issues.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        return string.IsNullOrWhiteSpace(issue)
            ? "章节门禁证据显示未通过，需要修订后复检。"
            : $"章节门禁证据显示未通过：{issue}";
    }

    private static bool IsGateFailureStatus(string status) =>
        string.Equals(status, "gate_failed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "blocked", StringComparison.OrdinalIgnoreCase);

    private static bool IsCommittedProductionChain(WorkflowProductionChain chain) =>
        string.Equals(chain.Status, "completed", StringComparison.OrdinalIgnoreCase) &&
        (!string.IsNullOrWhiteSpace(chain.ChapterVersionId) ||
         chain.Steps.Any(step =>
             string.Equals(step.Key, "commit", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(step.EventType, "chapter_committed", StringComparison.OrdinalIgnoreCase)));

    private static IReadOnlyList<WorkflowChapterProductionTraceItem> BuildChapterProductionTraceItems(
        WorkflowProductionChain chain,
        WorkflowProductionChainEvidence evidence,
        IReadOnlyList<WorkflowChapterRevisionPlanSummary> revisionPlans)
    {
        var items = new List<WorkflowChapterProductionTraceItem>();

        foreach (var plan in revisionPlans)
        {
            var relatedIds = plan.AffectedChapterIds
                .Concat(plan.InvalidatedPackageIds)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            items.Add(new WorkflowChapterProductionTraceItem(
                $"revision-plan:{plan.RevisionPlanId}",
                "修订计划",
                plan.Status,
                "RevisionPlan",
                plan.RevisionPlanId,
                FirstNonEmpty(plan.Recommendation, plan.PlanType, plan.Source),
                relatedIds));
        }

        foreach (var link in chain.RebuildLinks)
        {
            var artifactId = FirstNonEmpty(link.NewPackageId, link.OldPackageId);
            var relatedIds = new[] { link.OldPackageId, link.NewPackageId, link.ChapterId, link.RuntimeRunId }
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            items.Add(new WorkflowChapterProductionTraceItem(
                $"rebuild-package:{link.OldPackageId}:{link.NewPackageId}",
                "生产包重建",
                FirstNonEmpty(link.NewPackageStatus, link.OldPackageStatus, "recorded"),
                "TianmingPackage",
                artifactId,
                $"{FirstNonEmpty(link.OldPackageId, "旧包")} -> {FirstNonEmpty(link.NewPackageId, "新包")}",
                relatedIds));
        }

        if (!string.IsNullOrWhiteSpace(chain.PackageId))
        {
            var packageStatus = ResolveChapterProductionSummaryStatus(chain, evidence);
            var packageDescription = ResolveChapterProductionSummaryText(chain, evidence);
            items.Add(new WorkflowChapterProductionTraceItem(
                $"package:{chain.PackageId}",
                "当前生产包",
                FirstNonEmpty(packageStatus, "recorded"),
                "TianmingPackage",
                chain.PackageId,
                FirstNonEmpty(packageDescription, chain.RuntimeRunId),
                RelatedIds(chain.RuntimeRunId, chain.ChapterId, chain.ChapterLogicalId)));
        }

        var draftSteps = chain.Steps
            .Where(IsDraftTraceStep)
            .Take(3)
            .ToList();
        var duplicateDraftArtifactIds = draftSteps
            .GroupBy(step => FirstNonEmpty(step.ArtifactId, step.EventId), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var step in draftSteps)
        {
            var artifactId = FirstNonEmpty(step.ArtifactId, step.EventId);
            var key = duplicateDraftArtifactIds.Contains(artifactId)
                ? $"draft:{artifactId}:{FirstNonEmpty(step.EventId, step.Status, step.CreatedAt)}"
                : $"draft:{artifactId}";
            items.Add(new WorkflowChapterProductionTraceItem(
                key,
                "正文草稿",
                FirstNonEmpty(step.Status, "generated"),
                FirstNonEmpty(step.ArtifactType, "chapter_draft"),
                artifactId,
                FirstNonEmpty(step.Message, step.Label, "正文草稿已生成。"),
                RelatedIds(step.EventId, step.OutboxEventId, chain.PackageId, chain.RuntimeRunId)));
        }

        if (evidence.Gate != null)
        {
            var description = evidence.Gate.Issues.Count > 0
                ? string.Join(" / ", evidence.Gate.Issues.Take(3))
                : "协议、事实、蓝图、RAG 与 CHANGES 已校验。";
            items.Add(new WorkflowChapterProductionTraceItem(
                $"gate:{chain.Id}",
                "门禁校验",
                evidence.Gate.Status,
                "GenerationGateReport",
                chain.PackageId,
                description,
                RelatedIds(chain.PackageId, chain.RuntimeRunId)));
        }

        if (evidence.AgentReview != null)
        {
            items.Add(new WorkflowChapterProductionTraceItem(
                $"agent-review:{chain.Id}",
                "Agent 总编验收",
                FirstNonEmpty(evidence.AgentReview.OverallResult, evidence.AgentReview.Decision),
                "AgentReview",
                chain.PackageId,
                BuildAgentReviewTraceDescription(evidence.AgentReview),
                RelatedIds(chain.PackageId, chain.RuntimeRunId)));
        }

        if (evidence.ChapterChangeCount > 0 || evidence.ChapterChangeArtifactIds.Count > 0)
        {
            items.Add(new WorkflowChapterProductionTraceItem(
                $"changes:{chain.Id}",
                "CHANGES 沉淀",
                evidence.ChapterChangeArtifactIds.Count > 0 ? "applied" : "recorded",
                "ChapterChange",
                evidence.ChapterChangeArtifactIds.FirstOrDefault() ?? string.Empty,
                evidence.ChapterChangeCount > 0 ? $"已沉淀 {evidence.ChapterChangeCount} 条变更" : "已记录变更证据",
                evidence.ChapterChangeArtifactIds));
        }

        if (!string.IsNullOrWhiteSpace(chain.ChapterVersionId))
        {
            items.Add(new WorkflowChapterProductionTraceItem(
                $"chapter-version:{chain.ChapterVersionId}",
                "章节版本",
                chain.ChapterVersionNumber > 0 ? $"v{chain.ChapterVersionNumber}" : "recorded",
                "ChapterVersion",
                chain.ChapterVersionId,
                "正文已形成可回滚版本。",
                RelatedIds(chain.PackageId, chain.RuntimeRunId)));
        }

        if (!string.IsNullOrWhiteSpace(chain.FactSnapshotId))
        {
            items.Add(new WorkflowChapterProductionTraceItem(
                $"fact-snapshot:{chain.FactSnapshotId}",
                "事实快照",
                chain.FactSnapshotVersion > 0 ? $"v{chain.FactSnapshotVersion}" : "recorded",
                "ProjectFactSnapshot",
                chain.FactSnapshotId,
                FirstNonEmpty(evidence.FactSnapshot?.EndingState, evidence.FactSnapshot?.ProtagonistStatus, "章节事实已沉淀。"),
                RelatedIds(chain.ChapterVersionId, chain.PackageId, chain.RuntimeRunId)));
        }

        var outboxIds = chain.Steps
            .Select(step => step.OutboxEventId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();
        if (outboxIds.Count > 0)
        {
            items.Add(new WorkflowChapterProductionTraceItem(
                $"outbox:{outboxIds[0]}",
                "后台索引任务",
                "queued",
                "OutboxEvent",
                outboxIds[0],
                $"已记录 {outboxIds.Count} 个后台任务。",
                outboxIds));
        }

        var completedStep = chain.Steps
            .LastOrDefault(step =>
                string.Equals(step.Key, "completed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(step.EventType, "chapter_production_completed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(NovelAgentProductionStages.ToCanonicalStage(step.Stage), NovelAgentProductionStages.RunCompleted, StringComparison.OrdinalIgnoreCase));
        if (completedStep != null)
        {
            var artifactId = FirstNonEmpty(completedStep.ArtifactId, completedStep.EventId, chain.RuntimeRunId, chain.Id);
            items.Add(new WorkflowChapterProductionTraceItem(
                $"completed:{artifactId}",
                "生产完成",
                FirstNonEmpty(completedStep.Status, chain.Status, "completed"),
                FirstNonEmpty(completedStep.ArtifactType, "chapter_production_run"),
                artifactId,
                FirstNonEmpty(completedStep.Message, chain.Summary, "本轮章节生产已完成。"),
                RelatedIds(completedStep.EventId, chain.ChapterVersionId, chain.FactSnapshotId, chain.PackageId, chain.RuntimeRunId)));
        }

        return items
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .Take(20)
            .ToList();
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string BuildAgentReviewTraceDescription(WorkflowAgentReviewSummaryEvidence review)
    {
        var parts = new List<string>();
        var action = FirstNonEmpty(review.RecommendedAction, review.Decision);
        if (!string.IsNullOrWhiteSpace(action))
            parts.Add($"动作：{action}");

        if (review.Problems.Count > 0)
            parts.Add($"问题：{string.Join("；", review.Problems.Take(2))}");

        if (review.Suggestions.Count > 0)
            parts.Add($"建议：{string.Join("；", review.Suggestions.Take(2))}");

        return parts.Count > 0
            ? string.Join("。", parts)
            : FirstNonEmpty(review.OverallResult, review.Decision, "Agent 总编验收已记录。");
    }

    private static IReadOnlyList<string> RelatedIds(params string?[] values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool IsDraftTraceStep(WorkflowProductionChainStep step)
    {
        var canonicalStage = NovelAgentProductionStages.ToCanonicalStage(step.Stage);
        return string.Equals(step.Key, "draft", StringComparison.OrdinalIgnoreCase)
            || string.Equals(step.EventType, "chapter_draft_generated", StringComparison.OrdinalIgnoreCase)
            || string.Equals(canonicalStage, NovelAgentProductionStages.DraftGenerated, StringComparison.OrdinalIgnoreCase)
            || string.Equals(canonicalStage, NovelAgentProductionStages.DraftRewritten, StringComparison.OrdinalIgnoreCase)
            || step.ArtifactType.Contains("draft", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ChainMatchesChapter(WorkflowProductionChain chain, string chapterId)
    {
        if (string.Equals(chain.ChapterId, chapterId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(chain.ChapterLogicalId, chapterId, StringComparison.OrdinalIgnoreCase))
            return true;

        var targetNumber = ExtractChapterNumber(chapterId);
        if (targetNumber <= 0)
            return false;

        return ExtractChapterNumber(chain.ChapterId) == targetNumber
            || ExtractChapterNumber(chain.ChapterLogicalId) == targetNumber;
    }

    private static bool RevisionPlanMatchesChapterOrChain(
        WorkflowChapterRevisionPlanSummary plan,
        string chapterId,
        WorkflowProductionChain chain,
        IReadOnlyList<string> rebuildPackageIds)
    {
        if (chain.RevisionPlanIds.Contains(plan.RevisionPlanId, StringComparer.OrdinalIgnoreCase))
            return true;

        if (RevisionPlanMatchesChapter(plan, chapterId))
            return true;

        return plan.InvalidatedPackageIds.Any(packageId =>
            !string.IsNullOrWhiteSpace(packageId) &&
            (rebuildPackageIds.Contains(packageId, StringComparer.OrdinalIgnoreCase) ||
             string.Equals(packageId, chain.PackageId, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool RevisionPlanMatchesChapter(WorkflowChapterRevisionPlanSummary plan, string chapterId)
    {
        if (string.Equals(plan.TargetChapterId, chapterId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(plan.TargetChapterLogicalId, chapterId, StringComparison.OrdinalIgnoreCase)
            || plan.AffectedChapterIds.Contains(chapterId, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        var targetNumber = ExtractChapterNumber(chapterId);
        if (targetNumber <= 0)
            return false;

        return ExtractChapterNumber(plan.TargetChapterId) == targetNumber
            || ExtractChapterNumber(plan.TargetChapterLogicalId) == targetNumber
            || plan.AffectedChapterIds.Any(id => ExtractChapterNumber(id) == targetNumber);
    }

    private static bool CreativeIntentMatchesChapter(WorkflowCreativeIntentEvidence intent, string chapterId)
    {
        if (string.Equals(intent.TargetChapterId, chapterId, StringComparison.OrdinalIgnoreCase))
            return true;

        var targetNumber = ExtractChapterNumber(chapterId);
        if (targetNumber > 0 && ExtractChapterNumber(intent.TargetChapterId) == targetNumber)
            return true;

        if (!string.IsNullOrWhiteSpace(intent.TargetChapterId))
            return false;

        var scope = (intent.TargetScope ?? string.Empty).Trim().ToLowerInvariant();
        return scope is "book" or "project" or "volume" or "all" or "global" or "novel"
            || scope.Contains("全书", StringComparison.Ordinal)
            || scope.Contains("项目", StringComparison.Ordinal)
            || scope.Contains("整本", StringComparison.Ordinal)
            || scope.Contains("卷", StringComparison.Ordinal);
    }

    private static string ResolveReadableChapterTitle(
        string storedTitle,
        string content,
        int chapterNumber,
        string? artifactTitle = null)
    {
        var heading = ExtractChapterHeading(content);
        if (!string.IsNullOrWhiteSpace(heading))
            return heading;

        if (!IsMachineChapterTitle(storedTitle))
            return storedTitle;

        if (!IsWeakChapterTitle(artifactTitle))
            return artifactTitle!.Trim();

        return chapterNumber > 0 ? $"第 {chapterNumber} 章" : storedTitle;
    }

    private static bool IsMachineChapterTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return true;

        var normalized = title.Trim();
        if (!normalized.StartsWith("chapter-", StringComparison.OrdinalIgnoreCase))
            return false;

        return normalized.Skip("chapter-".Length).All(c => char.IsDigit(c) || c == '_' || c == '-');
    }

    private static bool IsWeakChapterTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return true;

        var normalized = title.Trim();
        return normalized.Equals("目标推进", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("章节推进", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("正文已经提交到书城", StringComparison.OrdinalIgnoreCase)
            || IsMachineChapterTitle(normalized);
    }

    private static string ExtractChapterHeading(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        var lineEnd = content.IndexOfAny(new[] { '\r', '\n' });
        var firstLine = (lineEnd >= 0 ? content[..lineEnd] : content).Trim();
        if (firstLine.StartsWith("#", StringComparison.Ordinal))
            firstLine = firstLine.TrimStart('#').Trim();
        if (firstLine.Length == 0)
            return string.Empty;

        var chapterStart = firstLine.IndexOf('第');
        var chapterEnd = firstLine.IndexOf('章', chapterStart >= 0 ? chapterStart : 0);
        if (chapterStart < 0 || chapterEnd <= chapterStart)
            return firstLine.Length <= 24 ? firstLine : string.Empty;

        var prefix = firstLine[chapterStart..(chapterEnd + 1)].Trim();
        var rest = firstLine[(chapterEnd + 1)..].TrimStart(' ', '\t', ':', '：', '-', '—');
        if (string.IsNullOrWhiteSpace(rest))
            return prefix;

        var subtitle = TakeCompactSubtitle(rest);
        return string.IsNullOrWhiteSpace(subtitle) ? prefix : $"{prefix}：{subtitle}";
    }

    private static string TakeCompactSubtitle(string text)
    {
        var chars = new List<char>(capacity: 8);
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || "，,。.!！?？；;：:、\"“”'‘’（）()【】[]".Contains(c))
                break;
            chars.Add(c);
            if (chars.Count >= 6)
                break;
        }

        if (chars.Count >= 6 && chars[^1] == chars[0])
            chars.RemoveAt(chars.Count - 1);

        return new string(chars.ToArray()).Trim();
    }

    private static int CountWords(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0;

        var count = 0;
        var inWord = false;
        foreach (var c in content)
        {
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
            }
            else if (c is >= '\u4E00' and <= '\u9FFF')
            {
                count++;
                inWord = false;
            }
            else if (!inWord)
            {
                count++;
                inWord = true;
            }
        }

        return count;
    }

    private static string ResolveChapterWritingStatus(Chapter chapter)
    {
        if (IsCommitted(chapter.Status)) return "committed";
        if (IsRewriteStatus(chapter.Status)) return "quality_failed";
        return string.IsNullOrWhiteSpace(chapter.Status) ? "planned" : chapter.Status;
    }

    private static bool IsCommitted(string? status) =>
        string.Equals(status, "committed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "published", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);

    private static bool IsRewriteStatus(string? status) =>
        string.Equals(status, "needs_rewrite", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "rewrite", StringComparison.OrdinalIgnoreCase);
}
