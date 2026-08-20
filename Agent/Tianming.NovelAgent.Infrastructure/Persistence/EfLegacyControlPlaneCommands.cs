using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tianming.NovelAgent.Application.Ports;

namespace Tianming.NovelAgent.Infrastructure.Persistence;

public sealed class EfLegacyControlPlaneCommands(
    AgentControlDbContext db,
    IAgentUnitOfWork unitOfWork,
    IClock clock,
    IIdGenerator ids,
    IUserScope userScope) : ILegacyControlPlaneCommands
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<LegacyGoalSubmissionResult> SubmitGoalAsync(
        LegacyGoalSubmissionCommand command,
        CancellationToken cancellationToken = default)
    {
        using var scope = userScope.Enter(command.UserId);
        return await unitOfWork.ExecuteAsync(async ct =>
        {
            var existing = await db.CreativeGoals.SingleOrDefaultAsync(goal =>
                goal.UserId == command.UserId &&
                goal.ProjectId == command.ProjectId &&
                goal.IdempotencyKey == command.IdempotencyKey, ct);
            if (existing is not null)
            {
                await EnsureProductionAsync(existing, command, ct);
                return new LegacyGoalSubmissionResult(existing.Id, true);
            }

            var now = clock.UtcNow;
            var goal = new CreativeGoalRecord
            {
                Id = command.GoalId ?? ids.NewId(),
                UserId = command.UserId,
                ProjectId = command.ProjectId,
                SourceSessionId = command.SourceSessionId,
                GoalType = command.GoalType,
                CollaborationMode = command.CollaborationMode,
                HumanReadableObjective = command.HumanReadableObjective,
                TargetChapterRangeJson = command.TargetChapterRangeJson,
                SuccessCriteriaJson = command.SuccessCriteriaJson,
                MustPreserveJson = command.MustPreserveJson,
                MustHappenJson = command.MustHappenJson,
                MustNotChangeJson = command.MustNotChangeJson,
                AcceptancePolicyJson = command.AcceptancePolicyJson,
                ReworkPolicyJson = command.ReworkPolicyJson,
                ExecutionStrategy = command.ExecutionStrategy,
                BookPlanJson = command.BookPlanJson,
                TotalCostLimit = command.TotalCostLimit,
                CanonBaselineVersion = command.Baselines.CanonVersion,
                KnowledgeSnapshotVersion = command.Baselines.KnowledgeVersion,
                QualityContractVersion = command.Baselines.QualityContractVersion,
                StyleProfileVersion = command.Baselines.StyleProfileVersion,
                ModelConfigVersionsJson = command.Baselines.ModelConfigVersionsJson,
                ProtocolVersionsJson = command.Baselines.ProtocolVersionsJson,
                Status = "committed",
                AggregateVersion = 1,
                IdempotencyKey = command.IdempotencyKey,
                CreatedAt = now
            };
            db.CreativeGoals.Add(goal);
            db.GoalContextSnapshots.Add(new GoalContextSnapshotRecord
            {
                Id = command.SnapshotId ?? ids.NewId(),
                UserId = command.UserId,
                ProjectId = command.ProjectId,
                GoalId = goal.Id,
                CanonVersion = command.Baselines.CanonVersion,
                KnowledgeVersion = command.Baselines.KnowledgeVersion,
                QualityContractVersion = command.Baselines.QualityContractVersion,
                StyleProfileVersion = command.Baselines.StyleProfileVersion,
                ModelConfigVersionsJson = command.Baselines.ModelConfigVersionsJson,
                ProtocolVersionsJson = command.Baselines.ProtocolVersionsJson,
                ContentHashesJson = command.Baselines.ContentHashesJson,
                CreatedAt = now
            });
            await EnsureProductionAsync(goal, command, ct);
            return new LegacyGoalSubmissionResult(goal.Id, false);
        }, cancellationToken);
    }

    public async Task PersistCompiledGraphAsync(
        LegacyCompiledGraphCommand command,
        CancellationToken cancellationToken = default)
    {
        using var scope = userScope.Enter(command.UserId);
        await unitOfWork.ExecuteAsync(async ct =>
        {
            var goal = await db.CreativeGoals.SingleOrDefaultAsync(item =>
                item.UserId == command.UserId &&
                item.Id == command.GoalId,
                ct) ?? throw new KeyNotFoundException("Goal 不存在或不属于当前用户。");
            if (goal.ProjectId != command.ProjectId)
                throw new InvalidOperationException("Goal 不属于指定项目。");

            if (await db.TaskGraphs.AnyAsync(graph =>
                    graph.UserId == command.UserId && graph.Id == command.GraphId, ct))
                return 0;

            var branch = await db.CanonBranches.SingleOrDefaultAsync(item =>
                item.UserId == command.UserId &&
                item.GoalId == command.GoalId &&
                item.Status == "active", ct);
            if (branch is null)
            {
                db.CanonBranches.Add(new CanonBranchRecord
                {
                    Id = command.BranchId,
                    UserId = command.UserId,
                    ProjectId = command.ProjectId,
                    GoalId = command.GoalId,
                    CanonBaselineVersion = goal.CanonBaselineVersion,
                    Status = "active",
                    StartChapterNumber = command.BatchStartChapterNumber,
                    EndChapterNumber = command.BatchEndChapterNumber,
                    CreatedAt = command.CreatedAt,
                    UpdatedAt = command.CreatedAt
                });
            }
            else
            {
                if (branch.Id != command.BranchId || branch.ProjectId != command.ProjectId)
                    throw new InvalidOperationException("The active canon branch does not match the compiled graph.");
                if (branch.StartChapterNumber == command.BatchStartChapterNumber &&
                    branch.EndChapterNumber < command.BatchEndChapterNumber)
                {
                    branch.EndChapterNumber = command.BatchEndChapterNumber;
                    branch.UpdatedAt = command.CreatedAt;
                }
            }

            var previousGraphs = await db.TaskGraphs
                .Where(graph => graph.UserId == command.UserId && graph.GoalId == command.GoalId)
                .OrderByDescending(graph => graph.Version)
                .ToListAsync(ct);
            var previousGraph = previousGraphs.FirstOrDefault();
            var previousTasks = previousGraph is null
                ? []
                : await db.KernelTasks
                    .Where(task => task.UserId == command.UserId && task.TaskGraphVersionId == previousGraph.Id)
                    .ToListAsync(ct);

            if (previousGraph is not null)
            {
                previousGraph.Status = "superseded";
                foreach (var task in previousTasks.Where(IsNonTerminal))
                {
                    task.Status = "cancelled";
                    task.LeaseOwner = null;
                    task.LeaseExpiresAt = null;
                    task.UpdatedAt = command.CreatedAt;
                }
            }

            db.TaskGraphs.Add(new TaskGraphRecord
            {
                Id = command.GraphId,
                UserId = command.UserId,
                ProjectId = command.ProjectId,
                GoalId = command.GoalId,
                GoalRevisionId = command.GoalRevisionId,
                Version = command.Version,
                Status = "active",
                GraphJson = command.GraphJson,
                ContentHash = command.ContentHash,
                CreatedAt = command.CreatedAt
            });

            var previousByNode = previousTasks.ToDictionary(TaskNodeId, StringComparer.Ordinal);
            db.KernelTasks.AddRange(command.Tasks.Select(task =>
            {
                previousByNode.TryGetValue(task.NodeId, out var previous);
                var outputArtifactIds = task.Reused && previous is not null
                    ? previous.OutputArtifactIdsJson
                    : task.OutputArtifactIdsJson;
                return new KernelTaskRecord
                {
                    Id = $"{command.GraphId}:{task.NodeId}",
                    UserId = command.UserId,
                    ProjectId = command.ProjectId,
                    GoalId = command.GoalId,
                    TaskGraphVersionId = command.GraphId,
                    BranchId = task.BranchId,
                    KernelName = task.KernelName,
                    TaskType = task.TaskType,
                    Status = task.Status,
                    DependencyTaskIdsJson = task.DependencyTaskIdsJson,
                    InputArtifactIdsJson = task.InputArtifactIdsJson,
                    OutputArtifactIdsJson = outputArtifactIds,
                    IdempotencyKey = $"{command.GraphId}:{task.NodeId}",
                    MaxAttempts = task.MaxAttempts,
                    Priority = task.Priority,
                    CreatedAt = command.CreatedAt,
                    UpdatedAt = command.CreatedAt
                };
            }));

            var revision = command.GoalRevisionId is null
                ? null
                : await db.GoalRevisions.SingleOrDefaultAsync(item =>
                    item.UserId == command.UserId && item.Id == command.GoalRevisionId, ct);
            if (revision is not null)
                revision.TaskGraphVersionId = command.GraphId;

            var production = await db.BookProductions.SingleOrDefaultAsync(item =>
                item.UserId == command.UserId && item.GoalId == command.GoalId, ct);
            if (production is not null)
            {
                production.TaskGraphVersionId = command.GraphId;
                production.UpdatedAt = command.CreatedAt;
                var batch = await db.ProductionBatches.SingleOrDefaultAsync(item =>
                    item.UserId == command.UserId &&
                    item.BookProductionId == production.Id &&
                    item.BatchNumber == production.CurrentBatchNumber, ct);
                if (batch is not null)
                {
                    batch.TaskGraphVersionId = command.GraphId;
                    batch.CanonBranchId = command.BranchId;
                    batch.Status = "running";
                    batch.UpdatedAt = command.CreatedAt;
                }
            }

            return 0;
        }, cancellationToken);
    }

    public async Task CreateArtifactAsync(
        LegacyArtifactCommand command,
        CancellationToken cancellationToken = default)
    {
        using var scope = userScope.Enter(command.UserId);
        await unitOfWork.ExecuteAsync(async ct =>
        {
            var goal = await db.CreativeGoals.SingleOrDefaultAsync(item =>
                item.UserId == command.UserId &&
                item.Id == command.GoalId,
                ct) ?? throw new KeyNotFoundException("Goal 不存在或不属于当前用户。");
            if (goal.ProjectId != command.ProjectId)
                throw new InvalidOperationException("Goal 不属于指定项目。");

            var existing = await db.KernelArtifacts.SingleOrDefaultAsync(item =>
                item.UserId == command.UserId && item.Id == command.ArtifactId, ct);
            var task = command.AttachToTaskOutput
                ? await db.KernelTasks.SingleOrDefaultAsync(item =>
                    item.UserId == command.UserId &&
                    item.GoalId == command.GoalId &&
                    item.Id == command.TaskId, ct)
                  ?? throw new KeyNotFoundException("Artifact task was not found for the current goal.")
                : null;
            if (existing is null)
            {
                db.KernelArtifacts.Add(new KernelArtifactRecord
                {
                    Id = command.ArtifactId,
                    UserId = command.UserId,
                    ProjectId = command.ProjectId,
                    GoalId = command.GoalId,
                    TaskId = command.TaskId,
                    BranchId = command.BranchId,
                    ArtifactType = command.ArtifactType,
                    SchemaVersion = command.SchemaVersion,
                    ContentJson = command.ContentJson,
                    ContentHash = command.ContentHash,
                    Status = command.Status,
                    Authorship = command.Authorship,
                    IsProtected = command.IsProtected,
                    ModelExecutionId = command.ModelExecutionId,
                    CausationId = command.CausationId,
                    CreatedAt = command.CreatedAt
                });
            }

            if (task is not null)
            {
                var outputIds = JsonSerializer.Deserialize<List<string>>(task.OutputArtifactIdsJson) ?? [];
                if (!outputIds.Contains(command.ArtifactId, StringComparer.Ordinal))
                {
                    outputIds.Add(command.ArtifactId);
                    task.OutputArtifactIdsJson = JsonSerializer.Serialize(outputIds);
                    task.UpdatedAt = command.CreatedAt;
                }
            }
            return 0;
        }, cancellationToken);
    }

    public async Task<LegacyGoalRevisionResult> CreateGoalRevisionAsync(
        LegacyGoalRevisionCommand command,
        CancellationToken cancellationToken = default)
    {
        using var scope = userScope.Enter(command.UserId);
        return await unitOfWork.ExecuteAsync(async ct =>
        {
            var goal = await RequiredGoalAsync(command.UserId, command.GoalId, ct);
            if (goal.ProjectId != command.ProjectId)
                throw new InvalidOperationException("Goal does not belong to the specified project.");

            var existing = await db.GoalRevisions.SingleOrDefaultAsync(item =>
                item.UserId == command.UserId && item.Id == command.RevisionId, ct);
            if (existing is null && !string.IsNullOrWhiteSpace(command.ConfirmationIdempotencyKey))
            {
                existing = await db.GoalRevisions.SingleOrDefaultAsync(item =>
                    item.UserId == command.UserId &&
                    item.ProjectId == command.ProjectId &&
                    item.ConfirmationIdempotencyKey == command.ConfirmationIdempotencyKey, ct);
            }
            if (existing is not null)
                return ToState(existing);

            var revisionNumber = command.RevisionNumber > 0
                ? command.RevisionNumber
                : (await db.GoalRevisions
                    .Where(item => item.UserId == command.UserId && item.GoalId == command.GoalId)
                    .Select(item => (int?)item.RevisionNumber)
                    .MaxAsync(ct) ?? 0) + 1;
            var now = command.CreatedAt;
            var revision = new GoalRevisionRecord
            {
                Id = command.RevisionId,
                UserId = command.UserId,
                ProjectId = command.ProjectId,
                GoalId = command.GoalId,
                RevisionNumber = revisionNumber,
                Reason = command.Reason,
                ConstraintChangesJson = command.ConstraintChangesJson,
                ReusableArtifactIdsJson = command.ReusableArtifactIdsJson,
                InvalidatedArtifactIdsJson = command.InvalidatedArtifactIdsJson,
                AffectedNodeIdsJson = command.AffectedNodeIdsJson,
                ConfirmationActorId = command.ConfirmationActorId ?? command.UserId,
                ConfirmationTime = now,
                ConfirmationIdempotencyKey = command.ConfirmationIdempotencyKey ?? string.Empty,
                SourceSessionId = command.SourceSessionId ?? goal.SourceSessionId,
                SourceProposalId = command.SourceProposalId ?? string.Empty,
                PreviousRevisionId = command.PreviousRevisionId,
                CreatedAt = now
            };
            db.GoalRevisions.Add(revision);
            return ToState(revision);
        }, cancellationToken);
    }

    public async Task<LegacyProductionState> ChangeExecutionStrategyAsync(
        LegacyExecutionStrategyCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.ExecutionStrategy is not ("full_auto" or "interactive_batch"))
            throw new ArgumentException("Execution strategy must be full_auto or interactive_batch.", nameof(command));

        using var scope = userScope.Enter(command.UserId);
        return await unitOfWork.ExecuteAsync(async ct =>
        {
            var production = await RequiredProductionAsync(command.UserId, command.GoalId, ct);
            var hasRunningTasks = await db.KernelTasks.AsNoTracking().AnyAsync(item =>
                item.UserId == command.UserId &&
                item.GoalId == command.GoalId &&
                item.Status == "running", ct);
            if (hasRunningTasks)
                throw new InvalidOperationException("Execution strategy can only change at a batch boundary.");

            var goal = await RequiredGoalAsync(command.UserId, command.GoalId, ct);
            var now = clock.UtcNow;
            production.ExecutionStrategy = command.ExecutionStrategy;
            production.UpdatedAt = now;
            production.AggregateVersion++;
            goal.ExecutionStrategy = command.ExecutionStrategy;
            goal.AggregateVersion++;
            return ToState(production);
        }, cancellationToken);
    }

    public async Task<LegacyBatchState> ContinueInteractiveBatchAsync(
        LegacyWorkflowCommand command,
        CancellationToken cancellationToken = default)
    {
        using var scope = userScope.Enter(command.UserId);
        return await unitOfWork.ExecuteAsync(async ct =>
        {
            var production = await RequiredProductionAsync(command.UserId, command.GoalId, ct);
            if (production.ExecutionStrategy != "interactive_batch" || production.Status != "awaiting_user")
                throw new InvalidOperationException("Production is not waiting for the next interactive batch.");

            var batch = await RequiredCurrentBatchAsync(production, ct);
            var goal = await RequiredGoalAsync(command.UserId, command.GoalId, ct);
            var now = clock.UtcNow;
            production.Status = "running";
            production.UpdatedAt = now;
            production.AggregateVersion++;
            goal.Status = "running";
            goal.AggregateVersion++;
            return ToState(batch);
        }, cancellationToken);
    }

    public async Task<LegacyBatchState?> BeginBatchAcceptanceAsync(
        LegacyBeginBatchAcceptanceCommand command,
        CancellationToken cancellationToken = default)
    {
        using var scope = userScope.Enter(command.UserId);
        return await unitOfWork.ExecuteAsync(async ct =>
        {
            var production = await RequiredProductionAsync(command.UserId, command.GoalId, ct);
            var batch = await db.ProductionBatches.SingleOrDefaultAsync(item =>
                item.UserId == command.UserId &&
                item.Id == command.BatchId &&
                item.GoalId == command.GoalId &&
                item.BookProductionId == production.Id &&
                item.BatchNumber == production.CurrentBatchNumber &&
                item.CanonBranchId == command.BranchId, ct);
            if (batch is null)
                return null;

            var canClaim = batch.Status == "running" ||
                (batch.Status == "accepting" &&
                 !command.StaleAcceptingBefore.HasValue &&
                 batch.AcceptanceActor == command.AcceptanceActor) ||
                (batch.Status == "accepting" &&
                 command.StaleAcceptingBefore.HasValue &&
                 batch.UpdatedAt < command.StaleAcceptingBefore.Value);
            if (!canClaim)
                return null;
            if (command.AcceptanceActor == "agent-policy" && production.ExecutionStrategy != "full_auto")
                throw new InvalidOperationException("Agent policy can only accept a full-auto production batch.");

            batch.Status = "accepting";
            batch.AcceptanceActor = command.AcceptanceActor;
            batch.UpdatedAt = clock.UtcNow;
            production.Status = "awaiting_acceptance";
            production.UpdatedAt = batch.UpdatedAt;
            production.AggregateVersion++;
            return ToState(batch);
        }, cancellationToken);
    }

    public async Task<LegacyBatchAdvanceResult> FinalizeMergedBatchAsync(
        LegacyFinalizeMergedBatchCommand command,
        CancellationToken cancellationToken = default)
    {
        using var scope = userScope.Enter(command.UserId);
        return await unitOfWork.ExecuteAsync(async ct =>
        {
            var production = await RequiredProductionAsync(command.UserId, command.GoalId, ct);
            var batch = await db.ProductionBatches.SingleOrDefaultAsync(item =>
                item.UserId == command.UserId &&
                item.BookProductionId == production.Id &&
                item.BatchNumber == production.CurrentBatchNumber &&
                item.CanonBranchId == command.BranchId, ct)
                ?? throw new InvalidOperationException("The merged branch does not belong to the current production batch.");
            if (string.IsNullOrWhiteSpace(batch.TaskGraphVersionId))
                throw new InvalidOperationException("The current production batch has no task graph.");

            var tasks = await db.KernelTasks.Where(task =>
                task.UserId == command.UserId &&
                task.GoalId == command.GoalId &&
                task.TaskGraphVersionId == batch.TaskGraphVersionId &&
                (task.TaskType == "AcceptanceGate" || task.TaskType == "PrefixMerge"))
                .ToListAsync(ct);
            if (tasks.Count != 2)
                throw new InvalidOperationException("The batch acceptance or prefix-merge task is missing.");

            var taskIds = tasks.Select(task => task.Id).ToArray();
            var artifacts = await db.KernelArtifacts.AsNoTracking()
                .Where(artifact =>
                    artifact.UserId == command.UserId &&
                    artifact.GoalId == command.GoalId &&
                    artifact.BranchId == command.BranchId &&
                    taskIds.Contains(artifact.TaskId) &&
                    (artifact.ArtifactType == "AcceptanceDecision" || artifact.ArtifactType == "MergeRecord"))
                .ToListAsync(ct);
            var now = clock.UtcNow;
            foreach (var task in tasks)
            {
                var expectedType = task.TaskType == "PrefixMerge" ? "MergeRecord" : "AcceptanceDecision";
                var outputIds = artifacts
                    .Where(item => item.TaskId == task.Id && item.ArtifactType == expectedType)
                    .Select(item => item.Id)
                    .ToArray();
                if (outputIds.Length == 0)
                    throw new InvalidOperationException($"The {task.TaskType} task has no authoritative artifact output.");
                task.Status = "completed";
                task.OutputArtifactIdsJson = JsonSerializer.Serialize(outputIds);
                task.LeaseOwner = null;
                task.LeaseExpiresAt = null;
                task.CompletedAt = now;
                task.UpdatedAt = now;
            }

            batch.Status = "completed";
            batch.CompletedAt = now;
            batch.UpdatedAt = now;
            production.NextChapterNumber = batch.EndChapterNumber + 1;
            production.UpdatedAt = now;
            production.AggregateVersion++;
            var goal = await RequiredGoalAsync(command.UserId, command.GoalId, ct);

            ProductionBatchRecord? nextBatch = null;
            var shouldCompileNextBatch = false;
            if (production.NextChapterNumber > production.TargetEndChapterNumber)
            {
                var validation = command.ValidationArtifact
                    ?? throw new InvalidOperationException("Completing the final batch requires a book validation artifact.");
                var prefixTask = tasks.Single(task => task.TaskType == "PrefixMerge");
                var validationId = $"book-validation:{production.Id}:{production.AggregateVersion}";
                if (!await db.KernelArtifacts.AnyAsync(item =>
                        item.UserId == command.UserId && item.Id == validationId, ct))
                {
                    db.KernelArtifacts.Add(new KernelArtifactRecord
                    {
                        Id = validationId,
                        UserId = command.UserId,
                        ProjectId = production.ProjectId,
                        GoalId = command.GoalId,
                        TaskId = prefixTask.Id,
                        BranchId = command.BranchId,
                        ArtifactType = "BookValidationReport",
                        SchemaVersion = 1,
                        ContentJson = validation.ContentJson,
                        ContentHash = validation.ContentHash,
                        Status = validation.Status,
                        Authorship = "system",
                        IsProtected = true,
                        CreatedAt = now
                    });
                }

                if (validation.Status == "unadopted")
                {
                    production.Status = "blocked";
                    goal.Status = "awaiting_decision";
                    goal.AggregateVersion++;
                }
                else
                {
                    production.Status = "completed";
                    production.CompletedAt = now;
                    goal.Status = "completed";
                    goal.AggregateVersion++;
                }
            }
            else
            {
                production.CurrentBatchNumber++;
                nextBatch = CreateBatch(production, production.NextChapterNumber, production.CurrentBatchNumber, now);
                db.ProductionBatches.Add(nextBatch);
                shouldCompileNextBatch = production.ExecutionStrategy == "full_auto";
                production.Status = shouldCompileNextBatch ? "running" : "awaiting_user";
                goal.Status = shouldCompileNextBatch ? "running" : "awaiting_next_batch";
                goal.AggregateVersion++;
            }

            return new LegacyBatchAdvanceResult(
                ToState(production),
                ToState(batch),
                nextBatch is null ? null : ToState(nextBatch),
                shouldCompileNextBatch);
        }, cancellationToken);
    }

    public async Task BlockBatchAsync(
        LegacyBlockBatchCommand command,
        CancellationToken cancellationToken = default)
    {
        using var scope = userScope.Enter(command.UserId);
        await unitOfWork.ExecuteAsync(async ct =>
        {
            var production = await RequiredProductionAsync(command.UserId, command.GoalId, ct);
            var batch = await db.ProductionBatches.SingleAsync(item =>
                item.UserId == command.UserId &&
                item.Id == command.BatchId &&
                item.GoalId == command.GoalId &&
                item.BookProductionId == production.Id &&
                item.BatchNumber == production.CurrentBatchNumber &&
                item.Status == "accepting", ct);
            var goal = await RequiredGoalAsync(command.UserId, command.GoalId, ct);
            var now = clock.UtcNow;
            production.Status = "blocked";
            production.UpdatedAt = now;
            production.AggregateVersion++;
            batch.Status = "blocked";
            batch.UpdatedAt = now;
            goal.Status = "awaiting_decision";
            goal.AggregateVersion++;
            return 0;
        }, cancellationToken);
    }

    public async Task ApplyTaskFailureAsync(
        LegacyTaskFailureCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.GoalStatus is not ("awaiting_decision" or "failed"))
            throw new ArgumentException("Unsupported failure goal status.", nameof(command));
        if (command.ProductionStatus is not ("blocked" or "failed"))
            throw new ArgumentException("Unsupported failure production status.", nameof(command));

        using var scope = userScope.Enter(command.UserId);
        await unitOfWork.ExecuteAsync(async ct =>
        {
            var goal = await db.CreativeGoals.SingleOrDefaultAsync(item =>
                item.UserId == command.UserId &&
                item.Id == command.GoalId &&
                (item.Status == "committed" ||
                 item.Status == "running" ||
                 item.Status == "resumed" ||
                 item.Status == "awaiting_user" ||
                 item.Status == "awaiting_next_batch"), ct);
            if (goal is not null)
            {
                goal.Status = command.GoalStatus;
                goal.AggregateVersion++;
            }

            var production = await db.BookProductions.SingleOrDefaultAsync(item =>
                item.UserId == command.UserId &&
                item.GoalId == command.GoalId &&
                (item.Status == "running" || item.Status == "resumed"), ct);
            if (production is not null)
            {
                production.Status = command.ProductionStatus;
                production.AggregateVersion++;
                production.UpdatedAt = clock.UtcNow;
            }

            var batches = await db.ProductionBatches.Where(item =>
                item.UserId == command.UserId &&
                item.GoalId == command.GoalId &&
                (item.Status == "planned" || item.Status == "running" || item.Status == "accepting"))
                .ToListAsync(ct);
            foreach (var batch in batches)
            {
                batch.Status = command.ProductionStatus;
                batch.UpdatedAt = clock.UtcNow;
            }
            return 0;
        }, cancellationToken);
    }

    private async Task EnsureProductionAsync(
        CreativeGoalRecord goal,
        LegacyGoalSubmissionCommand command,
        CancellationToken cancellationToken)
    {
        if (await db.BookProductions.AnyAsync(item =>
                item.UserId == command.UserId && item.GoalId == goal.Id, cancellationToken))
            return;

        var range = JsonSerializer.Deserialize<LegacyChapterRange>(goal.TargetChapterRangeJson, JsonOptions)
            ?? throw new InvalidOperationException("整书合同缺少章节范围。");
        if (range.Start <= 0 || range.End < range.Start)
            throw new InvalidOperationException("整书章节范围无效。");
        var plan = string.IsNullOrWhiteSpace(goal.BookPlanJson) || goal.BookPlanJson.Trim() == "{}"
            ? new LegacyBookPlan()
            : JsonSerializer.Deserialize<LegacyBookPlan>(goal.BookPlanJson, JsonOptions)
              ?? throw new InvalidOperationException("整书计划不能为空。");
        var batchSize = plan.BatchSize is >= 1 and <= 20 ? plan.BatchSize : 5;
        var now = clock.UtcNow;
        var productionId = ids.NewId();
        db.BookProductions.Add(new BookProductionRecord
        {
            Id = productionId,
            UserId = goal.UserId,
            ProjectId = goal.ProjectId,
            GoalId = goal.Id,
            ExecutionStrategy = goal.ExecutionStrategy,
            Status = "running",
            TargetStartChapterNumber = range.Start,
            TargetEndChapterNumber = range.End,
            NextChapterNumber = range.Start,
            BatchSize = batchSize,
            CurrentBatchNumber = 1,
            CompletionCriteriaJson = goal.BookPlanJson,
            PausePolicyJson = goal.AcceptancePolicyJson,
            TaskGraphVersionId = string.Empty,
            AggregateVersion = 1,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.ProductionBatches.Add(new ProductionBatchRecord
        {
            Id = ids.NewId(),
            UserId = goal.UserId,
            ProjectId = goal.ProjectId,
            GoalId = goal.Id,
            BookProductionId = productionId,
            BatchNumber = 1,
            StartChapterNumber = range.Start,
            EndChapterNumber = Math.Min(range.End, range.Start + batchSize - 1),
            Status = "planned",
            AcceptanceActor = goal.ExecutionStrategy == "full_auto" ? "agent-policy" : "user",
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private async Task<CreativeGoalRecord> RequiredGoalAsync(
        string userId,
        string goalId,
        CancellationToken cancellationToken) =>
        await db.CreativeGoals.SingleOrDefaultAsync(item =>
            item.UserId == userId && item.Id == goalId, cancellationToken)
        ?? throw new KeyNotFoundException("Goal was not found for the current user.");

    private async Task<BookProductionRecord> RequiredProductionAsync(
        string userId,
        string goalId,
        CancellationToken cancellationToken) =>
        await db.BookProductions.SingleOrDefaultAsync(item =>
            item.UserId == userId && item.GoalId == goalId, cancellationToken)
        ?? throw new KeyNotFoundException("Production was not found for the current user.");

    private async Task<ProductionBatchRecord> RequiredCurrentBatchAsync(
        BookProductionRecord production,
        CancellationToken cancellationToken) =>
        await db.ProductionBatches.SingleOrDefaultAsync(item =>
            item.UserId == production.UserId &&
            item.BookProductionId == production.Id &&
            item.BatchNumber == production.CurrentBatchNumber, cancellationToken)
        ?? throw new InvalidOperationException("Production has no current batch.");

    private ProductionBatchRecord CreateBatch(
        BookProductionRecord production,
        int startChapter,
        int batchNumber,
        DateTimeOffset now) =>
        new()
        {
            Id = ids.NewId(),
            UserId = production.UserId,
            ProjectId = production.ProjectId,
            GoalId = production.GoalId,
            BookProductionId = production.Id,
            BatchNumber = batchNumber,
            StartChapterNumber = startChapter,
            EndChapterNumber = Math.Min(production.TargetEndChapterNumber, startChapter + production.BatchSize - 1),
            Status = "planned",
            AcceptanceActor = production.ExecutionStrategy == "full_auto" ? "agent-policy" : "user",
            CreatedAt = now,
            UpdatedAt = now
        };

    private static LegacyProductionState ToState(BookProductionRecord production) =>
        new(
            production.Id,
            production.UserId,
            production.ProjectId,
            production.GoalId,
            production.ExecutionStrategy,
            production.Status,
            production.TargetStartChapterNumber,
            production.TargetEndChapterNumber,
            production.NextChapterNumber,
            production.BatchSize,
            production.CurrentBatchNumber,
            production.CompletionCriteriaJson,
            production.PausePolicyJson,
            production.AggregateVersion,
            production.CreatedAt,
            production.UpdatedAt,
            production.CompletedAt);

    private static LegacyBatchState ToState(ProductionBatchRecord batch) =>
        new(
            batch.Id,
            batch.UserId,
            batch.ProjectId,
            batch.GoalId,
            batch.BookProductionId,
            batch.BatchNumber,
            batch.StartChapterNumber,
            batch.EndChapterNumber,
            batch.Status,
            batch.TaskGraphVersionId,
            batch.CanonBranchId,
            batch.AcceptanceActor,
            batch.CreatedAt,
            batch.UpdatedAt,
            batch.CompletedAt);

    private static LegacyGoalRevisionResult ToState(GoalRevisionRecord revision) =>
        new(
            revision.Id,
            revision.GoalId,
            revision.RevisionNumber,
            revision.Reason,
            revision.ConstraintChangesJson,
            revision.ReusableArtifactIdsJson,
            revision.InvalidatedArtifactIdsJson,
            revision.AffectedNodeIdsJson,
            revision.CreatedAt);

    private static bool IsNonTerminal(KernelTaskRecord task) =>
        task.Status is not ("completed" or "reused" or "failed" or "cancelled");

    private static string TaskNodeId(KernelTaskRecord task)
    {
        var separator = task.Id.IndexOf(':');
        return separator < 0 ? task.Id : task.Id[(separator + 1)..];
    }

    private sealed record LegacyChapterRange(int Start, int End);
    private sealed record LegacyBookPlan(int BatchSize = 5);
}
