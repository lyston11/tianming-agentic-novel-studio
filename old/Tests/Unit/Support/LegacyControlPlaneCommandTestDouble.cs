using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tianming.NovelAgent.Application.Ports;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Goals;

namespace Tests.Unit.Support;

public sealed class LegacyControlPlaneCommandTestDouble(NovelAgentDbContext db) : ILegacyControlPlaneCommands
{
    public async Task<LegacyGoalSubmissionResult> SubmitGoalAsync(
        LegacyGoalSubmissionCommand command,
        CancellationToken cancellationToken = default)
    {
        var existing = await db.CreativeGoals.SingleOrDefaultAsync(item =>
            item.UserId == command.UserId &&
            item.ProjectId == command.ProjectId &&
            item.IdempotencyKey == command.IdempotencyKey, cancellationToken);
        if (existing is not null)
            return new LegacyGoalSubmissionResult(existing.Id, true);

        var goal = new CreativeGoal
        {
            Id = command.GoalId ?? Guid.NewGuid().ToString("N"),
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
            IdempotencyKey = command.IdempotencyKey,
            CreatedAt = DateTime.UtcNow
        };
        var range = JsonSerializer.Deserialize<Range>(goal.TargetChapterRangeJson)!;
        var batchSize = ParseBatchSize(goal.BookPlanJson);
        var production = new BookProduction
        {
            Id = Guid.NewGuid().ToString("N"),
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
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.CreativeGoals.Add(goal);
        db.GoalContextSnapshots.Add(new GoalContextSnapshot
        {
            Id = command.SnapshotId ?? Guid.NewGuid().ToString("N"),
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
            CreatedAt = DateTime.UtcNow
        });
        db.BookProductions.Add(production);
        db.ProductionBatches.Add(new ProductionBatch
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = goal.UserId,
            ProjectId = goal.ProjectId,
            GoalId = goal.Id,
            BookProductionId = production.Id,
            BatchNumber = 1,
            StartChapterNumber = range.Start,
            EndChapterNumber = Math.Min(range.End, range.Start + batchSize - 1),
            Status = "planned",
            AcceptanceActor = goal.ExecutionStrategy == BookExecutionStrategies.FullAuto
                ? BookProductionWorkflow.AgentPolicyActor
                : BookProductionWorkflow.UserActor,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
        return new LegacyGoalSubmissionResult(goal.Id, false);
    }

    public async Task PersistCompiledGraphAsync(
        LegacyCompiledGraphCommand command,
        CancellationToken cancellationToken = default)
    {
        if (await db.TaskGraphVersions.AnyAsync(item =>
                item.UserId == command.UserId && item.Id == command.GraphId,
                cancellationToken))
            return;

        var goal = await db.CreativeGoals.SingleAsync(item =>
            item.UserId == command.UserId && item.Id == command.GoalId,
            cancellationToken);
        var branch = await db.CanonBranches.SingleOrDefaultAsync(item =>
            item.UserId == command.UserId &&
            item.GoalId == command.GoalId &&
            item.Status == "active",
            cancellationToken);
        if (branch is null)
        {
            db.CanonBranches.Add(new CanonBranch
            {
                Id = command.BranchId,
                UserId = command.UserId,
                ProjectId = command.ProjectId,
                GoalId = command.GoalId,
                CanonBaselineVersion = goal.CanonBaselineVersion,
                Status = "active",
                StartChapterNumber = command.BatchStartChapterNumber,
                EndChapterNumber = command.BatchEndChapterNumber,
                CreatedAt = command.CreatedAt.UtcDateTime,
                UpdatedAt = command.CreatedAt.UtcDateTime
            });
        }
        else
        {
            branch.EndChapterNumber = Math.Max(branch.EndChapterNumber, command.BatchEndChapterNumber);
            branch.UpdatedAt = command.CreatedAt.UtcDateTime;
        }

        var previous = await db.TaskGraphVersions
            .Where(item => item.UserId == command.UserId && item.GoalId == command.GoalId)
            .OrderByDescending(item => item.Version)
            .FirstOrDefaultAsync(cancellationToken);
        if (previous is not null)
        {
            previous.Status = "superseded";
            var tasks = await db.KernelTasks.Where(item => item.TaskGraphVersionId == previous.Id).ToListAsync(cancellationToken);
            foreach (var task in tasks.Where(item => item.Status is not ("completed" or "reused" or "failed" or "cancelled")))
            {
                task.Status = "cancelled";
                task.LeaseOwner = null;
                task.LeaseExpiresAt = null;
            }
        }
        db.TaskGraphVersions.Add(new TaskGraphVersion
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
            CreatedAt = command.CreatedAt.UtcDateTime
        });
        db.KernelTasks.AddRange(command.Tasks.Select(task => new KernelTask
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
            OutputArtifactIdsJson = task.OutputArtifactIdsJson,
            IdempotencyKey = $"{command.GraphId}:{task.NodeId}",
            MaxAttempts = task.MaxAttempts,
            Priority = task.Priority,
            CreatedAt = command.CreatedAt.UtcDateTime,
            UpdatedAt = command.CreatedAt.UtcDateTime
        }));
        if (command.GoalRevisionId is not null)
        {
            var revision = await db.GoalRevisions.SingleAsync(item => item.Id == command.GoalRevisionId, cancellationToken);
            revision.TaskGraphVersionId = command.GraphId;
        }
        var production = await db.BookProductions.SingleOrDefaultAsync(item =>
            item.UserId == command.UserId && item.GoalId == command.GoalId, cancellationToken);
        if (production is not null)
        {
            var batch = await db.ProductionBatches.SingleAsync(item =>
                item.BookProductionId == production.Id && item.BatchNumber == production.CurrentBatchNumber, cancellationToken);
            batch.TaskGraphVersionId = command.GraphId;
            batch.CanonBranchId = command.BranchId;
            batch.Status = "running";
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CreateArtifactAsync(
        LegacyArtifactCommand command,
        CancellationToken cancellationToken = default)
    {
        var existing = await db.KernelArtifacts.SingleOrDefaultAsync(item =>
            item.Id == command.ArtifactId && item.UserId == command.UserId,
            cancellationToken);
        if (existing is null)
        {
            db.KernelArtifacts.Add(new KernelArtifact
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
                CreatedAt = command.CreatedAt.UtcDateTime
            });
        }
        if (command.AttachToTaskOutput)
        {
            var task = await db.KernelTasks.SingleAsync(item =>
                item.Id == command.TaskId &&
                item.UserId == command.UserId &&
                item.GoalId == command.GoalId,
                cancellationToken);
            var ids = JsonSerializer.Deserialize<List<string>>(task.OutputArtifactIdsJson) ?? [];
            if (!ids.Contains(command.ArtifactId, StringComparer.Ordinal))
                ids.Add(command.ArtifactId);
            task.OutputArtifactIdsJson = JsonSerializer.Serialize(ids);
            task.UpdatedAt = command.CreatedAt.UtcDateTime;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<LegacyGoalRevisionResult> CreateGoalRevisionAsync(
        LegacyGoalRevisionCommand command,
        CancellationToken cancellationToken = default)
    {
        var existing = await db.GoalRevisions.SingleOrDefaultAsync(item =>
            item.UserId == command.UserId && item.Id == command.RevisionId,
            cancellationToken);
        if (existing is not null)
            return ToRevisionResult(existing);
        var revisionNumber = (await db.GoalRevisions
            .Where(item => item.UserId == command.UserId && item.GoalId == command.GoalId)
            .Select(item => (int?)item.RevisionNumber)
            .MaxAsync(cancellationToken) ?? 0) + 1;
        db.GoalRevisions.Add(new GoalRevision
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
            CreatedAt = command.CreatedAt.UtcDateTime
        });
        await db.SaveChangesAsync(cancellationToken);
        return ToRevisionResult(await db.GoalRevisions.AsNoTracking().SingleAsync(item =>
            item.UserId == command.UserId && item.Id == command.RevisionId,
            cancellationToken));
    }

    public async Task<LegacyProductionState> ChangeExecutionStrategyAsync(
        LegacyExecutionStrategyCommand command,
        CancellationToken cancellationToken = default)
    {
        var production = await RequireProductionAsync(command.UserId, command.GoalId, cancellationToken);
        production.ExecutionStrategy = command.ExecutionStrategy;
        production.AggregateVersion++;
        production.UpdatedAt = DateTime.UtcNow;
        var goal = await db.CreativeGoals.SingleAsync(item => item.UserId == command.UserId && item.Id == command.GoalId, cancellationToken);
        goal.ExecutionStrategy = command.ExecutionStrategy;
        goal.AggregateVersion++;
        await db.SaveChangesAsync(cancellationToken);
        return ToState(production);
    }

    public async Task<LegacyBatchState> ContinueInteractiveBatchAsync(
        LegacyWorkflowCommand command,
        CancellationToken cancellationToken = default)
    {
        var production = await RequireProductionAsync(command.UserId, command.GoalId, cancellationToken);
        if (production.ExecutionStrategy != BookExecutionStrategies.InteractiveBatch || production.Status != "awaiting_user")
            throw new InvalidOperationException("当前整书生产不在等待下一批的交互状态。");
        var batch = await RequireCurrentBatchAsync(production, cancellationToken);
        production.Status = "running";
        production.AggregateVersion++;
        production.UpdatedAt = DateTime.UtcNow;
        var goal = await db.CreativeGoals.SingleAsync(item => item.UserId == command.UserId && item.Id == command.GoalId, cancellationToken);
        goal.Status = "running";
        goal.AggregateVersion++;
        await db.SaveChangesAsync(cancellationToken);
        return ToState(batch);
    }

    public async Task<LegacyBatchState?> BeginBatchAcceptanceAsync(
        LegacyBeginBatchAcceptanceCommand command,
        CancellationToken cancellationToken = default)
    {
        var production = await RequireProductionAsync(command.UserId, command.GoalId, cancellationToken);
        var batch = await RequireCurrentBatchAsync(production, cancellationToken);
        if (batch.Id != command.BatchId ||
            batch.CanonBranchId != command.BranchId ||
            batch.AcceptanceActor != command.AcceptanceActor ||
            batch.Status is not ("running" or "accepting"))
            return null;
        batch.Status = "accepting";
        batch.UpdatedAt = DateTime.UtcNow;
        production.Status = "accepting";
        production.AggregateVersion++;
        production.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return ToState(batch);
    }

    public async Task<LegacyBatchAdvanceResult> FinalizeMergedBatchAsync(
        LegacyFinalizeMergedBatchCommand command,
        CancellationToken cancellationToken = default)
    {
        var production = await RequireProductionAsync(command.UserId, command.GoalId, cancellationToken);
        var batch = await RequireCurrentBatchAsync(production, cancellationToken);
        if (batch.CanonBranchId != command.BranchId)
            throw new InvalidOperationException("合并分支不属于当前生产批次。");
        if (batch.Status == "completed")
            return new LegacyBatchAdvanceResult(ToState(production), ToState(batch), null, false);

        var workflowTasks = await db.KernelTasks.Where(task =>
            task.UserId == command.UserId &&
            task.GoalId == command.GoalId &&
            task.TaskGraphVersionId == batch.TaskGraphVersionId &&
            (task.TaskType == BookProductionWorkflow.AcceptanceGate ||
             task.TaskType == BookProductionWorkflow.PrefixMerge))
            .ToListAsync(cancellationToken);
        var workflowTaskIds = workflowTasks.Select(task => task.Id).ToArray();
        var workflowArtifacts = await db.KernelArtifacts.AsNoTracking().Where(artifact =>
            artifact.UserId == command.UserId &&
            artifact.GoalId == command.GoalId &&
            artifact.BranchId == command.BranchId &&
            workflowTaskIds.Contains(artifact.TaskId))
            .ToListAsync(cancellationToken);
        foreach (var task in workflowTasks)
        {
            var artifactType = task.TaskType == BookProductionWorkflow.PrefixMerge
                ? "MergeRecord"
                : "AcceptanceDecision";
            var artifactIds = workflowArtifacts
                .Where(artifact => artifact.TaskId == task.Id && artifact.ArtifactType == artifactType)
                .Select(artifact => artifact.Id)
                .ToArray();
            if (artifactIds.Length == 0)
                throw new InvalidOperationException($"批次 {task.TaskType} 任务缺少权威 Artifact 输出。");
            task.Status = "completed";
            task.OutputArtifactIdsJson = JsonSerializer.Serialize(artifactIds);
            task.LeaseOwner = null;
            task.LeaseExpiresAt = null;
        }

        var now = DateTime.UtcNow;
        foreach (var task in workflowTasks)
        {
            task.CompletedAt = now;
            task.UpdatedAt = now;
        }
        batch.Status = "completed";
        batch.CompletedAt = now;
        batch.UpdatedAt = now;
        production.NextChapterNumber = batch.EndChapterNumber + 1;
        production.AggregateVersion++;
        production.UpdatedAt = now;
        var goal = await db.CreativeGoals.SingleAsync(item => item.UserId == command.UserId && item.Id == command.GoalId, cancellationToken);
        ProductionBatch? next = null;
        var shouldCompile = false;
        if (production.NextChapterNumber > production.TargetEndChapterNumber)
        {
            var blocked = command.ValidationArtifact?.Status == "unadopted";
            production.Status = blocked ? "blocked" : "completed";
            production.CompletedAt = blocked ? null : now;
            goal.Status = blocked ? "awaiting_decision" : "completed";
            goal.AggregateVersion++;
            if (!blocked)
            {
                var project = await db.NovelProjects.SingleAsync(item =>
                    item.Id == production.ProjectId && item.UserId == command.UserId,
                    cancellationToken);
                project.Status = "completed";
                project.UpdatedAt = now;
            }
        }
        else
        {
            production.CurrentBatchNumber++;
            next = CreateBatch(production, production.NextChapterNumber, production.CurrentBatchNumber);
            db.ProductionBatches.Add(next);
            shouldCompile = production.ExecutionStrategy == BookExecutionStrategies.FullAuto;
            production.Status = shouldCompile ? "running" : "awaiting_user";
            goal.Status = shouldCompile ? "running" : "awaiting_next_batch";
            goal.AggregateVersion++;
        }
        await db.SaveChangesAsync(cancellationToken);
        return new LegacyBatchAdvanceResult(
            ToState(production),
            ToState(batch),
            next is null ? null : ToState(next),
            shouldCompile);
    }

    public async Task BlockBatchAsync(
        LegacyBlockBatchCommand command,
        CancellationToken cancellationToken = default)
    {
        var production = await RequireProductionAsync(command.UserId, command.GoalId, cancellationToken);
        var batch = await RequireCurrentBatchAsync(production, cancellationToken);
        if (batch.Id != command.BatchId || batch.Status != "accepting")
            throw new InvalidOperationException("当前批次不在可阻塞状态。");
        production.Status = "blocked";
        production.AggregateVersion++;
        batch.Status = "blocked";
        var goal = await db.CreativeGoals.SingleAsync(item => item.UserId == command.UserId && item.Id == command.GoalId, cancellationToken);
        goal.Status = "awaiting_decision";
        goal.AggregateVersion++;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ApplyTaskFailureAsync(
        LegacyTaskFailureCommand command,
        CancellationToken cancellationToken = default)
    {
        var goal = await db.CreativeGoals.SingleOrDefaultAsync(item =>
            item.UserId == command.UserId && item.Id == command.GoalId,
            cancellationToken);
        if (goal is not null)
        {
            goal.Status = command.GoalStatus;
            goal.AggregateVersion++;
        }
        var production = await db.BookProductions.SingleOrDefaultAsync(item =>
            item.UserId == command.UserId && item.GoalId == command.GoalId,
            cancellationToken);
        if (production is not null)
        {
            production.Status = command.ProductionStatus;
            production.AggregateVersion++;
        }
        var batches = await db.ProductionBatches.Where(item =>
            item.UserId == command.UserId && item.GoalId == command.GoalId)
            .ToListAsync(cancellationToken);
        foreach (var batch in batches)
            batch.Status = command.ProductionStatus;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<BookProduction> RequireProductionAsync(
        string userId,
        string goalId,
        CancellationToken cancellationToken) =>
        await db.BookProductions.SingleOrDefaultAsync(item =>
            item.UserId == userId && item.GoalId == goalId,
            cancellationToken) ?? throw new KeyNotFoundException("整书生产不存在或不属于当前用户。");

    private async Task<ProductionBatch> RequireCurrentBatchAsync(
        BookProduction production,
        CancellationToken cancellationToken) =>
        await db.ProductionBatches.SingleAsync(item =>
            item.UserId == production.UserId &&
            item.BookProductionId == production.Id &&
            item.BatchNumber == production.CurrentBatchNumber,
            cancellationToken);

    private static ProductionBatch CreateBatch(BookProduction production, int start, int number)
    {
        var now = DateTime.UtcNow;
        return new ProductionBatch
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = production.UserId,
            ProjectId = production.ProjectId,
            GoalId = production.GoalId,
            BookProductionId = production.Id,
            BatchNumber = number,
            StartChapterNumber = start,
            EndChapterNumber = Math.Min(production.TargetEndChapterNumber, start + production.BatchSize - 1),
            Status = "planned",
            AcceptanceActor = production.ExecutionStrategy == BookExecutionStrategies.FullAuto
                ? BookProductionWorkflow.AgentPolicyActor
                : BookProductionWorkflow.UserActor,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static LegacyGoalRevisionResult ToRevisionResult(GoalRevision item) => new(
        item.Id,
        item.GoalId,
        item.RevisionNumber,
        item.Reason,
        item.ConstraintChangesJson,
        item.ReusableArtifactIdsJson,
        item.InvalidatedArtifactIdsJson,
        item.AffectedNodeIdsJson,
        new DateTimeOffset(item.CreatedAt, TimeSpan.Zero));

    private static LegacyProductionState ToState(BookProduction item) => new(
        item.Id,
        item.UserId,
        item.ProjectId,
        item.GoalId,
        item.ExecutionStrategy,
        item.Status,
        item.TargetStartChapterNumber,
        item.TargetEndChapterNumber,
        item.NextChapterNumber,
        item.BatchSize,
        item.CurrentBatchNumber,
        item.CompletionCriteriaJson,
        item.PausePolicyJson,
        item.AggregateVersion,
        new DateTimeOffset(item.CreatedAt, TimeSpan.Zero),
        new DateTimeOffset(item.UpdatedAt, TimeSpan.Zero),
        item.CompletedAt.HasValue ? new DateTimeOffset(item.CompletedAt.Value, TimeSpan.Zero) : null);

    private static LegacyBatchState ToState(ProductionBatch item) => new(
        item.Id,
        item.UserId,
        item.ProjectId,
        item.GoalId,
        item.BookProductionId,
        item.BatchNumber,
        item.StartChapterNumber,
        item.EndChapterNumber,
        item.Status,
        item.TaskGraphVersionId,
        item.CanonBranchId,
        item.AcceptanceActor,
        new DateTimeOffset(item.CreatedAt, TimeSpan.Zero),
        new DateTimeOffset(item.UpdatedAt, TimeSpan.Zero),
        item.CompletedAt.HasValue ? new DateTimeOffset(item.CompletedAt.Value, TimeSpan.Zero) : null);

    private static int ParseBatchSize(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
            return 5;
        var batchSize = JsonSerializer.Deserialize<BookPlan>(json)?.BatchSize ?? 5;
        return batchSize is >= 1 and <= 20 ? batchSize : 5;
    }

    private sealed record Range(int Start, int End);
    private sealed record BookPlan(int BatchSize = 5);

    public async Task<string> PauseGoalAsync(
        LegacyGoalPauseCommand command,
        CancellationToken cancellationToken = default)
    {
        var goal = await db.CreativeGoals.SingleOrDefaultAsync(item =>
            item.UserId == command.UserId && item.Id == command.GoalId, cancellationToken)
            ?? throw new KeyNotFoundException("Goal 不存在或不属于当前用户。");
        if (goal.Status is not ("committed" or "running" or "resumed" or "pause_requested"))
            throw new InvalidOperationException("当前 Goal 状态不能请求暂停。");
        var hasRunningTask = await db.KernelTasks.AnyAsync(item =>
            item.UserId == command.UserId && item.GoalId == command.GoalId && item.Status == "running",
            cancellationToken);
        if (hasRunningTask)
        {
            goal.Status = "pause_requested";
        }
        else
        {
            var claimableTasks = await db.KernelTasks.Where(item =>
                item.UserId == command.UserId &&
                item.GoalId == command.GoalId &&
                (item.Status == "queued" || item.Status == "ready"))
                .ToListAsync(cancellationToken);
            foreach (var task in claimableTasks)
            {
                task.Status = "paused";
                task.LeaseOwner = null;
                task.LeaseExpiresAt = null;
                task.UpdatedAt = DateTime.UtcNow;
            }
            goal.Status = "paused";
        }
        var production = await db.BookProductions.SingleOrDefaultAsync(item =>
            item.UserId == command.UserId && item.GoalId == command.GoalId, cancellationToken);
        if (production != null)
        {
            production.Status = goal.Status;
            production.UpdatedAt = DateTime.UtcNow;
            production.AggregateVersion++;
        }
        goal.AggregateVersion++;
        await db.SaveChangesAsync(cancellationToken);
        return goal.Status;
    }

    public async Task ResumeGoalAsync(
        LegacyGoalResumeCommand command,
        CancellationToken cancellationToken = default)
    {
        var goal = await db.CreativeGoals.SingleOrDefaultAsync(item =>
            item.UserId == command.UserId && item.Id == command.GoalId, cancellationToken)
            ?? throw new KeyNotFoundException("Goal 不存在或不属于当前用户。");
        if (goal.Status != "paused")
            throw new InvalidOperationException("只有已到达安全点的暂停 Goal 可以恢复。");
        var snapshotExists = await db.GoalContextSnapshots.AnyAsync(item =>
            item.UserId == command.UserId &&
            item.GoalId == command.GoalId &&
            item.ProjectId == goal.ProjectId, cancellationToken);
        if (!snapshotExists)
            throw new InvalidOperationException("Goal 快照缺失，恢复前必须由用户决定如何重建基线。");
        var tasks = await db.KernelTasks.Where(item =>
            item.UserId == command.UserId && item.GoalId == command.GoalId && item.Status == "paused")
            .ToListAsync(cancellationToken);
        foreach (var task in tasks)
        {
            task.Status = "ready";
            task.UpdatedAt = DateTime.UtcNow;
        }
        goal.Status = "resumed";
        goal.AggregateVersion++;
        var production = await db.BookProductions.SingleOrDefaultAsync(item =>
            item.UserId == command.UserId && item.GoalId == command.GoalId, cancellationToken);
        if (production != null)
        {
            production.Status = "running";
            production.UpdatedAt = DateTime.UtcNow;
            production.AggregateVersion++;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CancelGoalAsync(
        LegacyGoalCancelCommand command,
        CancellationToken cancellationToken = default)
    {
        var goal = await db.CreativeGoals.SingleOrDefaultAsync(item =>
            item.UserId == command.UserId && item.Id == command.GoalId, cancellationToken)
            ?? throw new KeyNotFoundException("Goal 不存在或不属于当前用户。");
        if (goal.Status is "canceled" or "completed")
        {
            if (command.IdempotentRetry)
                return;
            throw new InvalidOperationException("Goal 已经结束，不能重复取消。");
        }
        var branch = await db.CanonBranches.SingleOrDefaultAsync(item =>
            item.UserId == command.UserId && item.GoalId == command.GoalId, cancellationToken);
        if (branch != null && command.FinalBranchStatus is not null)
        {
            branch.Status = command.FinalBranchStatus;
            branch.UpdatedAt = DateTime.UtcNow;
        }
        if (branch != null && command.FinalBranchStatus == "discarded")
        {
            var branchArtifacts = await db.KernelArtifacts.Where(item =>
                item.UserId == command.UserId &&
                item.GoalId == command.GoalId &&
                item.BranchId == branch.Id)
                .ToListAsync(cancellationToken);
            db.KernelArtifacts.RemoveRange(branchArtifacts);
        }
        var pendingTasks = await db.KernelTasks.Where(item =>
            item.UserId == command.UserId && item.GoalId == command.GoalId &&
            (item.Status == "queued" || item.Status == "ready" || item.Status == "blocked" ||
             item.Status == "paused" || item.Status == "running"))
            .ToListAsync(cancellationToken);
        foreach (var task in pendingTasks)
        {
            task.Status = "canceled";
            task.LeaseOwner = null;
            task.LeaseExpiresAt = null;
            task.UpdatedAt = DateTime.UtcNow;
        }
        goal.Status = "canceled";
        goal.AggregateVersion++;
        var production = await db.BookProductions.SingleOrDefaultAsync(item =>
            item.UserId == command.UserId && item.GoalId == command.GoalId, cancellationToken);
        if (production != null)
        {
            production.Status = "canceled";
            production.UpdatedAt = DateTime.UtcNow;
            production.AggregateVersion++;
            var batches = await db.ProductionBatches.Where(item =>
                item.UserId == command.UserId &&
                item.BookProductionId == production.Id &&
                item.Status != "completed")
                .ToListAsync(cancellationToken);
            foreach (var batch in batches)
            {
                batch.Status = "canceled";
                batch.UpdatedAt = DateTime.UtcNow;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<LegacySafePointResult> ReachSafePointAsync(
        LegacySafePointCommand command,
        CancellationToken cancellationToken = default)
    {
        var goal = await db.CreativeGoals.SingleAsync(item =>
            item.Id == command.GoalId &&
            item.UserId == command.UserId &&
            item.ProjectId == command.ProjectId, cancellationToken);
        var disposition = goal.Status switch
        {
            "pause_requested" => "Paused",
            "canceled" => "Canceled",
            "budget_exceeded" => "BudgetExceeded",
            _ => "Continue"
        };
        var ids = new List<string>(command.Artifacts.Count);
        if (disposition != "Continue")
        {
            foreach (var proposal in command.Artifacts)
            {
                var artifactId = Guid.NewGuid().ToString("N");
                db.KernelArtifacts.Add(new KernelArtifact
                {
                    Id = artifactId,
                    UserId = command.UserId,
                    ProjectId = command.ProjectId,
                    GoalId = command.GoalId,
                    TaskId = command.TaskId,
                    BranchId = command.BranchId,
                    ArtifactType = proposal.ArtifactType,
                    SchemaVersion = proposal.SchemaVersion,
                    ContentJson = proposal.ContentJson,
                    ContentHash = proposal.ContentHash,
                    Status = "unadopted",
                    Authorship = proposal.Authorship,
                    IsProtected = proposal.IsProtected
                });
                ids.Add(artifactId);
            }
            var task = await db.KernelTasks.SingleAsync(item =>
                item.Id == command.TaskId &&
                item.UserId == command.UserId &&
                item.LeaseOwner == command.LeaseOwner, cancellationToken);
            task.Status = disposition switch
            {
                "Paused" => "paused",
                "BudgetExceeded" => "budget_exceeded",
                _ => "canceled"
            };
            task.OutputArtifactIdsJson = JsonSerializer.Serialize(ids);
            task.LeaseOwner = null;
            task.LeaseExpiresAt = null;
            task.UpdatedAt = DateTime.UtcNow;
            if (disposition == "Paused")
            {
                goal.Status = "paused";
                goal.AggregateVersion++;
                var production = await db.BookProductions.SingleOrDefaultAsync(item =>
                    item.UserId == command.UserId && item.GoalId == command.GoalId, cancellationToken);
                if (production != null)
                {
                    production.Status = "paused";
                    production.UpdatedAt = DateTime.UtcNow;
                    production.AggregateVersion++;
                }
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        return new LegacySafePointResult(disposition, ids);
    }

    public async Task PersistReworkGraphAsync(
        LegacyReworkGraphCommand command,
        CancellationToken cancellationToken = default)
    {
        var graph = await db.TaskGraphVersions.SingleAsync(item =>
            item.Id == command.GraphId &&
            item.UserId == command.UserId &&
            item.ProjectId == command.ProjectId &&
            item.GoalId == command.GoalId, cancellationToken);
        graph.GraphJson = command.UpdatedGraphJson;
        graph.ContentHash = command.UpdatedGraphContentHash;

        db.KernelArtifacts.Add(new KernelArtifact
        {
            Id = command.IntentArtifact.ArtifactId,
            UserId = command.UserId,
            ProjectId = command.ProjectId,
            GoalId = command.GoalId,
            TaskId = command.IntentArtifact.TaskId,
            BranchId = command.IntentArtifact.BranchId,
            ArtifactType = command.IntentArtifact.ArtifactType,
            SchemaVersion = command.IntentArtifact.SchemaVersion,
            ContentJson = command.IntentArtifact.ContentJson,
            ContentHash = command.IntentArtifact.ContentHash,
            Status = command.IntentArtifact.Status,
            Authorship = command.IntentArtifact.Authorship
        });
        foreach (var task in command.Tasks)
        {
            var inputArtifactIdsJson = task.Id == command.DraftTaskId
                ? command.DraftInputArtifactIdsJson
                : task.InputArtifactIdsJson;
            db.KernelTasks.Add(new KernelTask
            {
                Id = task.Id,
                UserId = command.UserId,
                ProjectId = command.ProjectId,
                GoalId = command.GoalId,
                TaskGraphVersionId = command.GraphId,
                BranchId = command.IntentArtifact.BranchId,
                KernelName = task.KernelName,
                TaskType = task.TaskType,
                Status = task.Status,
                DependencyTaskIdsJson = task.DependencyTaskIdsJson,
                InputArtifactIdsJson = inputArtifactIdsJson,
                OutputArtifactIdsJson = task.OutputArtifactIdsJson,
                IdempotencyKey = task.IdempotencyKey,
                MaxAttempts = task.MaxAttempts,
                Priority = task.Priority
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

}
