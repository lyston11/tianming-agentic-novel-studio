using System.Data;
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Contracts.Conversation;
using Tianming.NovelAgent.Contracts.Streaming;
using Tianming.NovelAgent.Domain.Events;
using Tianming.NovelAgent.Domain.Goals;
using Tianming.NovelAgent.Domain.Production;
using ProductionAggregate = Tianming.NovelAgent.Domain.Production.Production;

namespace Tianming.NovelAgent.Infrastructure.Persistence;

public sealed class EfAgentControlStore(
    AgentControlDbContext db,
    IClock clock,
    IIdGenerator ids,
    IContractHasher hasher) :
    IAgentUnitOfWork,
    IConversationStore,
    IGoalRepository,
    IProductionRepository,
    IAgentEventWriter,
    IStreamEventReader,
    IContextFreezer,
    ICanonLeaseManager
{
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null)
            return await action(cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var result = await action(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<ConversationTurnResult?> FindTurnResultAsync(
        string userId,
        string sessionId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var record = await db.ConversationTurns.AsNoTracking().SingleOrDefaultAsync(
            x => x.UserId == userId && x.SessionId == sessionId && x.IdempotencyKey == idempotencyKey,
            cancellationToken);
        return record is null ? null : AgentJson.Deserialize<ConversationTurnResult>(record.ResultJson);
    }

    public Task SaveTurnAsync(
        string userId,
        string projectId,
        string sessionId,
        string idempotencyKey,
        string userMessageId,
        string userMessage,
        string assistantMessageId,
        ConversationRuntimeResult runtimeResult,
        GoalProposal? proposal,
        ConversationTurnResult result,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        db.ConversationMessages.AddRange(
            new ConversationMessageRecord
            {
                Id = userMessageId,
                UserId = userId,
                ProjectId = projectId,
                SessionId = sessionId,
                Role = "user",
                Content = userMessage,
                CreatedAt = now
            },
            new ConversationMessageRecord
            {
                Id = assistantMessageId,
                UserId = userId,
                ProjectId = projectId,
                SessionId = sessionId,
                Role = "assistant",
                Content = runtimeResult.AssistantMessage,
                CreatedAt = now
            });
        db.ConversationTurns.Add(new ConversationTurnRecord
        {
            Id = userMessageId,
            UserId = userId,
            ProjectId = projectId,
            SessionId = sessionId,
            IdempotencyKey = idempotencyKey,
            ResultJson = AgentJson.Serialize(result),
            CreatedAt = now
        });
        if (proposal is not null)
            db.GoalProposals.Add(ToRecord(proposal, hasher.Hash(proposal.Contract), now));
        return Task.CompletedTask;
    }

    public async Task UpdateTurnResultAsync(
        string userId,
        string sessionId,
        string idempotencyKey,
        ConversationTurnResult result,
        CancellationToken cancellationToken)
    {
        var record = await db.ConversationTurns.SingleAsync(
            x => x.UserId == userId
                && x.SessionId == sessionId
                && x.IdempotencyKey == idempotencyKey,
            cancellationToken);
        record.ResultJson = AgentJson.Serialize(result);
    }

    public async Task<GoalProposal?> GetProposalAsync(
        string userId,
        string proposalId,
        CancellationToken cancellationToken)
    {
        var record = await db.GoalProposals.AsNoTracking().SingleOrDefaultAsync(
            x => x.UserId == userId && x.Id == proposalId,
            cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    public async Task UpdateProposalAsync(GoalProposal proposal, CancellationToken cancellationToken)
    {
        var record = await db.GoalProposals.SingleAsync(
            x => x.UserId == proposal.UserId && x.Id == proposal.Id,
            cancellationToken);
        record.Status = ToWire(proposal.Status);
        record.DecisionReason = proposal.DecisionReason;
        record.UpdatedAt = clock.UtcNow;
    }

    public async Task<ConfirmGoalProposalResult?> FindConfirmationResultAsync(
        string userId,
        string projectId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var revision = await db.GoalRevisions.AsNoTracking().SingleOrDefaultAsync(
            x => x.UserId == userId
                && x.ProjectId == projectId
                && x.ConfirmationIdempotencyKey == idempotencyKey,
            cancellationToken);
        if (revision is null)
            return null;

        var payload = await db.DomainEvents.AsNoTracking()
            .Where(x => x.UserId == userId
                && x.ProjectId == projectId
                && x.AggregateType == "CreativeGoal"
                && x.AggregateId == revision.GoalId
                && x.EventType == "GoalConfirmed")
            .Select(x => x.PayloadJson)
            .SingleOrDefaultAsync(cancellationToken);
        return payload is null
            ? throw new InvalidOperationException("The confirmed goal has no durable confirmation result.")
            : AgentJson.Deserialize<ConfirmGoalProposalResult>(payload);
    }

    async Task<CreativeGoal?> IGoalRepository.GetAsync(
        string userId,
        string goalId,
        CancellationToken cancellationToken) => await GetAsync(userId, goalId, cancellationToken);

    public async Task AddAsync(CreativeGoal goal, CancellationToken cancellationToken)
    {
        db.CreativeGoals.Add(ToRecord(goal));
        foreach (var revision in goal.Revisions)
            db.GoalRevisions.Add(ToRecord(goal, revision));
        await Task.CompletedTask;
    }

    public async Task UpdateAsync(CreativeGoal goal, CancellationToken cancellationToken)
    {
        var record = await db.CreativeGoals.SingleAsync(
            x => x.UserId == goal.UserId && x.Id == goal.Id,
            cancellationToken);
        record.Status = ToWire(goal.Status);
        record.AggregateVersion = goal.Version;
        record.CurrentRevisionId = goal.CurrentRevision.Id;

        var existingIds = await db.GoalRevisions
            .Where(x => x.UserId == goal.UserId && x.GoalId == goal.Id)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        foreach (var revision in goal.Revisions.Where(x => !existingIds.Contains(x.Id)))
            db.GoalRevisions.Add(ToRecord(goal, revision));
    }

    async Task<ProductionAggregate?> IProductionRepository.GetAsync(
        string userId,
        string productionId,
        CancellationToken cancellationToken) => await GetProductionAsync(userId, productionId, cancellationToken);

    public async Task<ProductionAggregate?> FindByGoalIdAsync(
        string userId,
        string goalId,
        CancellationToken cancellationToken)
    {
        var id = await db.BookProductions.AsNoTracking()
            .Where(x => x.UserId == userId && x.GoalId == goalId)
            .Select(x => x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return id is null ? null : await GetProductionAsync(userId, id, cancellationToken);
    }

    public Task AddAsync(
        ProductionAggregate production,
        GoalContract contract,
        FrozenContextReference context,
        CancellationToken cancellationToken)
    {
        var branchId = $"branch:{production.Id}:1";
        db.TaskGraphs.Add(ToTaskGraphRecord(production));
        db.KernelTasks.AddRange(production.Graph.Nodes.Select((node, priority) =>
            ToKernelTaskRecord(production, node, priority, branchId)));
        db.BookProductions.Add(ToProductionRecord(production, contract));
        db.ProductionBatches.Add(ToProductionBatchRecord(production, contract, branchId));
        db.CanonBranches.Add(ToCanonBranchRecord(production, contract, branchId));
        db.GoalContextSnapshots.Add(ToGoalContextSnapshotRecord(production, contract, context));
        return Task.CompletedTask;
    }

    public async Task UpdateAsync(ProductionAggregate production, CancellationToken cancellationToken)
    {
        var record = await db.BookProductions.SingleAsync(
            x => x.UserId == production.UserId && x.Id == production.Id,
            cancellationToken);
        var previousStatus = record.Status;
        record.Status = ToWire(production.Status);
        record.AggregateVersion = production.Version;
        record.CanonLeaseOwner = production.CanonLease?.Owner;
        record.CanonLeaseExpiresAt = production.CanonLease?.ExpiresAt;
        record.CanonLeaseFenceToken = production.CanonLease?.FenceToken;
        record.TerminalReason = production.TerminalReason;
        record.UpdatedAt = clock.UtcNow;
        if (production.Status == ProductionStatus.Completed)
            record.CompletedAt = clock.UtcNow;
        if (previousStatus == "planned" && production.Status == ProductionStatus.Running)
        {
            var batch = await db.ProductionBatches.SingleAsync(
                x => x.UserId == production.UserId && x.BookProductionId == production.Id && x.BatchNumber == 1,
                cancellationToken);
            batch.Status = "running";
            batch.UpdatedAt = clock.UtcNow;
            var rootTasks = await db.KernelTasks
                .Where(x => x.UserId == production.UserId
                    && x.TaskGraphVersionId == production.Graph.Id
                    && x.Status == "planned")
                .ToListAsync(cancellationToken);
            foreach (var task in rootTasks)
            {
                task.Status = "ready";
                task.UpdatedAt = clock.UtcNow;
            }
        }
    }

    public async Task AppendAsync(
        AgentDomainEvent domainEvent,
        AgentStreamKind streamKind,
        string streamId,
        object publicData,
        CancellationToken cancellationToken)
    {
        var sequenceKey = $"{domainEvent.UserId}:{ToWire(streamKind)}:{streamId}";
        var sequence = await db.StreamSequences.SingleOrDefaultAsync(x => x.Id == sequenceKey, cancellationToken);
        long nextSequence;
        if (sequence is null)
        {
            nextSequence = 1;
            db.StreamSequences.Add(new StreamSequenceRecord
            {
                Id = sequenceKey,
                UserId = domainEvent.UserId,
                StreamKind = ToWire(streamKind),
                StreamId = streamId,
                NextSequence = 2,
                Version = 1
            });
        }
        else
        {
            nextSequence = sequence.NextSequence;
            sequence.NextSequence++;
            sequence.Version++;
        }

        var streamEventId = ids.NewId();
        var payload = JsonSerializer.SerializeToElement(publicData, AgentJson.Options);
        var envelope = new AgentEventEnvelope<JsonElement>(
            streamEventId,
            streamKind,
            streamId,
            nextSequence,
            domainEvent.EventType,
            1,
            domainEvent.OccurredAt,
            domainEvent.CorrelationId,
            domainEvent.CausationId,
            domainEvent.ProjectId,
            streamKind == AgentStreamKind.Conversation ? streamId : null,
            domainEvent.GoalId,
            domainEvent.AggregateType == "Production" ? domainEvent.AggregateId : null,
            false,
            payload);

        db.DomainEvents.Add(ToRecord(domainEvent));
        db.StreamEvents.Add(new AgentStreamEventRecord
        {
            Id = streamEventId,
            UserId = domainEvent.UserId,
            ProjectId = domainEvent.ProjectId,
            StreamKind = ToWire(streamKind),
            StreamId = streamId,
            Sequence = nextSequence,
            EventType = domainEvent.EventType,
            SchemaVersion = 1,
            CorrelationId = domainEvent.CorrelationId,
            CausationId = domainEvent.CausationId,
            SessionId = envelope.SessionId,
            GoalId = domainEvent.GoalId,
            ProductionId = envelope.ProductionId,
            PayloadJson = AgentJson.Serialize(payload),
            OccurredAt = domainEvent.OccurredAt
        });
        db.OutboxEvents.Add(new OutboxEventRecord
        {
            Id = ids.NewId(),
            UserId = domainEvent.UserId,
            ProjectId = domainEvent.ProjectId,
            EventType = "agent_stream_event",
            AggregateType = domainEvent.AggregateType,
            AggregateId = domainEvent.AggregateId,
            IdempotencyKey = $"stream:{streamEventId}",
            PayloadJson = AgentJson.Serialize(envelope),
            Status = "pending",
            StreamEventId = streamEventId,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        });
    }

    public async Task<IReadOnlyList<AgentEventEnvelope<JsonElement>>> ReadAsync(
        string userId,
        AgentStreamKind streamKind,
        string streamId,
        long afterSequence,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(limit));
        var kind = ToWire(streamKind);
        var records = await db.StreamEvents.AsNoTracking()
            .Where(x => x.UserId == userId
                && x.StreamKind == kind
                && x.StreamId == streamId
                && x.Sequence > afterSequence)
            .OrderBy(x => x.Sequence)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return records.Select(ToEnvelope).ToArray();
    }

    public Task<FrozenContextReference> FreezeAsync(
        string userId,
        string projectId,
        GoalContract contract,
        CancellationToken cancellationToken)
    {
        var contractHash = hasher.Hash(contract);
        var checkpoint = new ContextCheckpointRecord
        {
            Id = ids.NewId(),
            UserId = userId,
            ProjectId = projectId,
            ContractHash = contractHash,
            CanonBaselineVersion = contract.CanonBaselineVersion,
            KnowledgeSnapshotVersion = contract.KnowledgeSnapshotVersion,
            StyleProfileVersion = contract.StyleProfileVersion,
            ModelVersionsJson = AgentJson.Serialize(contract.ModelVersions),
            PromptVersionsJson = AgentJson.Serialize(new Dictionary<string, string> { ["conversation"] = "1", ["production"] = "1" }),
            SchemaVersionsJson = AgentJson.Serialize(new Dictionary<string, string> { ["goal"] = "1", ["artifact"] = "1" }),
            ProtocolVersionsJson = AgentJson.Serialize(contract.ProtocolVersions),
            CreatedAt = clock.UtcNow
        };
        db.ContextCheckpoints.Add(checkpoint);
        return Task.FromResult(new FrozenContextReference(
            checkpoint.Id,
            contractHash,
            checkpoint.CanonBaselineVersion,
            checkpoint.KnowledgeSnapshotVersion,
            checkpoint.StyleProfileVersion,
            contract.ModelVersions,
            AgentJson.Deserialize<IReadOnlyDictionary<string, string>>(checkpoint.PromptVersionsJson),
            AgentJson.Deserialize<IReadOnlyDictionary<string, string>>(checkpoint.SchemaVersionsJson),
            contract.ProtocolVersions));
    }

    public async Task<CanonWriteLease> AcquireAsync(
        string userId,
        string projectId,
        string productionId,
        string owner,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var record = await db.CanonWriteLeases.SingleOrDefaultAsync(
            x => x.UserId == userId && x.ProjectId == projectId,
            cancellationToken);
        if (record is not null && record.ExpiresAt > now && record.ProductionId != productionId)
            throw new InvalidOperationException("Another production owns the canon write lease.");

        if (record is null)
        {
            record = new CanonWriteLeaseRecord
            {
                Id = ids.NewId(),
                UserId = userId,
                ProjectId = projectId,
                FenceToken = 1,
                Version = 1
            };
            db.CanonWriteLeases.Add(record);
        }
        else
        {
            record.FenceToken++;
            record.Version++;
        }

        record.ProductionId = productionId;
        record.Owner = owner;
        record.ExpiresAt = now.AddMinutes(2);
        record.UpdatedAt = now;
        return new CanonWriteLease(record.Owner, record.ExpiresAt, record.FenceToken);
    }

    private async Task<CreativeGoal?> GetAsync(string userId, string goalId, CancellationToken cancellationToken)
    {
        var record = await db.CreativeGoals.AsNoTracking().SingleOrDefaultAsync(
            x => x.UserId == userId && x.Id == goalId,
            cancellationToken);
        if (record is null)
            return null;
        var revisions = await db.GoalRevisions.AsNoTracking()
            .Where(x => x.UserId == userId && x.GoalId == goalId)
            .OrderBy(x => x.RevisionNumber)
            .ToListAsync(cancellationToken);
        return CreativeGoal.Restore(
            record.Id,
            record.UserId,
            record.ProjectId,
            revisions.Select(ToDomain),
            ParseGoalStatus(record.Status),
            record.AggregateVersion);
    }

    private async Task<ProductionAggregate?> GetProductionAsync(
        string userId,
        string productionId,
        CancellationToken cancellationToken)
    {
        var record = await db.BookProductions.AsNoTracking().SingleOrDefaultAsync(
            x => x.UserId == userId && x.Id == productionId,
            cancellationToken);
        if (record is null)
            return null;
        var graphRecord = await db.TaskGraphs.AsNoTracking().SingleAsync(
            x => x.UserId == userId && x.Id == record.TaskGraphVersionId,
            cancellationToken);
        var graph = AgentJson.Deserialize<TaskGraphSnapshot>(graphRecord.GraphJson).ToDomain();
        var lease = record.CanonLeaseOwner is null || record.CanonLeaseExpiresAt is null || record.CanonLeaseFenceToken is null
            ? null
            : new CanonWriteLease(record.CanonLeaseOwner, record.CanonLeaseExpiresAt.Value, record.CanonLeaseFenceToken.Value);
        return ProductionAggregate.Restore(
            record.Id,
            record.UserId,
            record.ProjectId,
            record.GoalId,
            record.GoalRevisionId,
            ParseMode(record.ExecutionStrategy),
            graph,
            ParseProductionStatus(record.Status),
            record.AggregateVersion,
            lease,
            record.TerminalReason);
    }

    private static GoalProposalRecord ToRecord(GoalProposal proposal, string hash, DateTimeOffset now) => new()
    {
        Id = proposal.Id,
        UserId = proposal.UserId,
        ProjectId = proposal.ProjectId,
        SourceSessionId = proposal.SourceSessionId,
        ContractJson = AgentJson.Serialize(proposal.Contract),
        ContractHash = hash,
        Status = ToWire(proposal.Status),
        DecisionReason = proposal.DecisionReason,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static GoalProposal ToDomain(GoalProposalRecord record) => GoalProposal.Restore(
        record.Id,
        record.UserId,
        record.ProjectId,
        record.SourceSessionId,
        AgentJson.Deserialize<GoalContract>(record.ContractJson),
        Enum.Parse<GoalProposalStatus>(record.Status, true),
        record.DecisionReason);

    private static CreativeGoalRecord ToRecord(CreativeGoal goal)
    {
        var contract = goal.CurrentRevision.Contract;
        return new CreativeGoalRecord
        {
            Id = goal.Id,
            UserId = goal.UserId,
            ProjectId = goal.ProjectId,
            SourceSessionId = goal.CurrentRevision.Confirmation.SourceSessionId,
            HumanReadableObjective = contract.Objective,
            TargetChapterRangeJson = AgentJson.Serialize(contract.ChapterRange),
            SuccessCriteriaJson = AgentJson.Serialize(contract.SuccessCriteria),
            MustPreserveJson = AgentJson.Serialize(contract.MustPreserve),
            MustHappenJson = AgentJson.Serialize(contract.MustHappen),
            MustNotChangeJson = AgentJson.Serialize(contract.MustNotChange),
            AcceptancePolicyJson = AgentJson.Serialize(contract.AcceptancePolicy),
            ReworkPolicyJson = AgentJson.Serialize(contract.ReworkPolicy),
            ExecutionStrategy = ToWire(contract.Mode),
            TotalCostLimit = contract.TotalCostLimit,
            CanonBaselineVersion = contract.CanonBaselineVersion,
            KnowledgeSnapshotVersion = contract.KnowledgeSnapshotVersion,
            QualityContractVersion = contract.QualityContractVersion,
            StyleProfileVersion = contract.StyleProfileVersion,
            ModelConfigVersionsJson = AgentJson.Serialize(contract.ModelVersions),
            ProtocolVersionsJson = AgentJson.Serialize(contract.ProtocolVersions),
            Status = ToWire(goal.Status),
            AggregateVersion = goal.Version,
            IdempotencyKey = goal.CurrentRevision.Confirmation.IdempotencyKey,
            CurrentRevisionId = goal.CurrentRevision.Id,
            CreatedAt = goal.CurrentRevision.Confirmation.ConfirmedAt
        };
    }

    private static GoalRevisionRecord ToRecord(CreativeGoal goal, GoalRevision revision) => new()
    {
        Id = revision.Id,
        UserId = goal.UserId,
        ProjectId = goal.ProjectId,
        GoalId = goal.Id,
        RevisionNumber = revision.RevisionNumber,
        Reason = revision.Reason,
        ContractJson = AgentJson.Serialize(revision.Contract),
        ContextJson = AgentJson.Serialize(revision.Context),
        ContractHash = revision.ContractHash,
        SchemaVersion = revision.SchemaVersion,
        ConfirmationActorId = revision.Confirmation.ActorId,
        ConfirmationTime = revision.Confirmation.ConfirmedAt,
        ConfirmationIdempotencyKey = revision.Confirmation.IdempotencyKey,
        SourceSessionId = revision.Confirmation.SourceSessionId,
        SourceProposalId = revision.Confirmation.SourceProposalId,
        PreviousRevisionId = revision.PreviousRevisionId,
        CreatedAt = revision.Confirmation.ConfirmedAt
    };

    private static GoalRevision ToDomain(GoalRevisionRecord record) => new(
        record.Id,
        record.RevisionNumber,
        AgentJson.Deserialize<GoalContract>(record.ContractJson),
        AgentJson.Deserialize<FrozenContextReference>(record.ContextJson),
        new GoalConfirmation(
            record.ConfirmationActorId,
            record.ConfirmationTime,
            record.ConfirmationIdempotencyKey,
            record.SourceSessionId,
            record.SourceProposalId),
        record.ContractHash,
        record.SchemaVersion,
        record.Reason,
        record.PreviousRevisionId);

    private static BookProductionRecord ToProductionRecord(
        ProductionAggregate production,
        GoalContract contract) => new()
    {
        Id = production.Id,
        UserId = production.UserId,
        ProjectId = production.ProjectId,
        GoalId = production.GoalId,
        GoalRevisionId = production.GoalRevisionId,
        ExecutionStrategy = ToExecutionStrategy(production.Mode),
        Status = ToWire(production.Status),
        TargetStartChapterNumber = contract.ChapterRange.Start,
        TargetEndChapterNumber = contract.ChapterRange.End,
        NextChapterNumber = contract.ChapterRange.Start,
        BatchSize = 1,
        CurrentBatchNumber = 1,
        CompletionCriteriaJson = AgentJson.Serialize(contract.SuccessCriteria),
        PausePolicyJson = AgentJson.Serialize(contract.AcceptancePolicy),
        TaskGraphVersionId = production.Graph.Id,
        AggregateVersion = production.Version,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static ProductionBatchRecord ToProductionBatchRecord(
        ProductionAggregate production,
        GoalContract contract,
        string branchId) => new()
    {
        Id = $"batch:{production.Id}:1",
        UserId = production.UserId,
        ProjectId = production.ProjectId,
        GoalId = production.GoalId,
        BookProductionId = production.Id,
        BatchNumber = 1,
        StartChapterNumber = contract.ChapterRange.Start,
        EndChapterNumber = contract.ChapterRange.Start,
        Status = "planned",
        TaskGraphVersionId = production.Graph.Id,
        CanonBranchId = branchId,
        AcceptanceActor = production.Mode == ProductionMode.AutonomousBook ? "agent-policy" : "user",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static CanonBranchRecord ToCanonBranchRecord(
        ProductionAggregate production,
        GoalContract contract,
        string branchId) => new()
    {
        Id = branchId,
        UserId = production.UserId,
        ProjectId = production.ProjectId,
        GoalId = production.GoalId,
        CanonBaselineVersion = contract.CanonBaselineVersion,
        StartChapterNumber = contract.ChapterRange.Start,
        EndChapterNumber = contract.ChapterRange.Start,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static GoalContextSnapshotRecord ToGoalContextSnapshotRecord(
        ProductionAggregate production,
        GoalContract contract,
        FrozenContextReference context) => new()
    {
        Id = context.ContextId,
        UserId = production.UserId,
        ProjectId = production.ProjectId,
        GoalId = production.GoalId,
        CanonVersion = context.CanonBaselineVersion,
        KnowledgeVersion = context.KnowledgeSnapshotVersion,
        QualityContractVersion = contract.QualityContractVersion,
        StyleProfileVersion = context.StyleProfileVersion,
        ModelConfigVersionsJson = AgentJson.Serialize(context.ModelVersions),
        ProtocolVersionsJson = AgentJson.Serialize(context.ProtocolVersions),
        ContentHashesJson = AgentJson.Serialize(new Dictionary<string, string>
        {
            ["contract"] = context.ContentHash,
            ["canon"] = context.CanonBaselineVersion,
            ["knowledge"] = context.KnowledgeSnapshotVersion
        }),
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static TaskGraphRecord ToTaskGraphRecord(ProductionAggregate production)
    {
        var graphJson = AgentJson.Serialize(TaskGraphSnapshot.From(production.Graph));
        return new TaskGraphRecord
        {
            Id = production.Graph.Id,
            UserId = production.UserId,
            ProjectId = production.ProjectId,
            GoalId = production.GoalId,
            GoalRevisionId = production.GoalRevisionId,
            Version = 1,
            Status = "active",
            GraphJson = graphJson,
            ContentHash = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(graphJson))),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static KernelTaskRecord ToKernelTaskRecord(
        ProductionAggregate production,
        TaskNodeDefinition node,
        int priority,
        string branchId)
    {
        var (taskType, kernelName) = node.Kind switch
        {
            TaskKind.FreezeContext => ("FreezeBaselines", "context_compiler"),
            TaskKind.AnalyzeRequirements => ("AnalyzeCreativeRequirements", "narrative_planning"),
            TaskKind.PlanBatch => ("CompileBatchPlan", "narrative_planning"),
            TaskKind.CompileChapterContext => ("CompileChapterContext", "knowledge_retrieval"),
            TaskKind.WriteCandidate => ("WriteCandidate", "tianming_writing"),
            TaskKind.ReviewContinuity => ("ReviewContinuity", "continuity_review"),
            TaskKind.ReviewLiteraryQuality => ("ReviewLiteraryQuality", "literary_review"),
            TaskKind.AwaitHumanAcceptance => ("AcceptanceGate", "workflow"),
            TaskKind.RequestCanonMerge => ("PrefixMerge", "domain_reducer"),
            TaskKind.FinalizeBatch => ("FinalizeBatch", "workflow"),
            _ => throw new ArgumentOutOfRangeException(nameof(node.Kind), node.Kind, null)
        };
        var now = DateTimeOffset.UtcNow;
        return new KernelTaskRecord
        {
            Id = $"{production.Graph.Id}:{node.Id}",
            UserId = production.UserId,
            ProjectId = production.ProjectId,
            GoalId = production.GoalId,
            TaskGraphVersionId = production.Graph.Id,
            BranchId = branchId,
            KernelName = kernelName,
            TaskType = taskType,
            Status = node.Dependencies.Count == 0 ? "planned" : "blocked",
            DependencyTaskIdsJson = AgentJson.Serialize(node.Dependencies),
            IdempotencyKey = $"{production.Graph.Id}:{node.Id}",
            MaxAttempts = node.MaxAttempts,
            Priority = priority,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static DomainEventRecord ToRecord(AgentDomainEvent value) => new()
    {
        Id = value.EventId,
        UserId = value.UserId,
        ProjectId = value.ProjectId,
        GoalId = value.GoalId ?? string.Empty,
        AggregateType = value.AggregateType,
        AggregateId = value.AggregateId,
        AggregateVersion = value.AggregateVersion,
        EventType = value.EventType,
        CausationId = value.CausationId,
        CorrelationId = value.CorrelationId,
        IdempotencyKey = $"domain:{value.EventId}",
        PayloadJson = value.PayloadJson,
        CreatedAt = value.OccurredAt
    };

    private static AgentEventEnvelope<JsonElement> ToEnvelope(AgentStreamEventRecord record) => new(
        record.Id,
        Enum.Parse<AgentStreamKind>(record.StreamKind, true),
        record.StreamId,
        record.Sequence,
        record.EventType,
        record.SchemaVersion,
        record.OccurredAt,
        record.CorrelationId,
        record.CausationId,
        record.ProjectId,
        record.SessionId,
        record.GoalId,
        record.ProductionId,
        false,
        JsonSerializer.Deserialize<JsonElement>(record.PayloadJson, AgentJson.Options));

    private static CreativeGoalStatus ParseGoalStatus(string value) => value switch
    {
        "committed" => CreativeGoalStatus.Confirmed,
        _ => Enum.Parse<CreativeGoalStatus>(value, true)
    };

    private static ProductionStatus ParseProductionStatus(string value) => value switch
    {
        "awaiting_acceptance" => ProductionStatus.AwaitingAcceptance,
        "merging_canon" => ProductionStatus.MergingCanon,
        _ => Enum.Parse<ProductionStatus>(value, true)
    };

    private static ProductionMode ParseMode(string value) => value switch
    {
        "single_chapter" => ProductionMode.SingleChapter,
        "interactive_batch" => ProductionMode.InteractiveBatch,
        "autonomous_book" or "whole_book" or "full_auto" => ProductionMode.AutonomousBook,
        _ => Enum.Parse<ProductionMode>(value, true)
    };

    private static string ToExecutionStrategy(ProductionMode mode) => mode switch
    {
        ProductionMode.SingleChapter or ProductionMode.InteractiveBatch => "interactive_batch",
        ProductionMode.AutonomousBook => "full_auto",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };

    private static string ToWire(Enum value) => value switch
    {
        ProductionMode.SingleChapter => "single_chapter",
        ProductionMode.InteractiveBatch => "interactive_batch",
        ProductionMode.AutonomousBook => "autonomous_book",
        CreativeGoalStatus.Confirmed => "committed",
        ProductionStatus.AwaitingAcceptance => "awaiting_acceptance",
        ProductionStatus.MergingCanon => "merging_canon",
        _ => value.ToString().ToLowerInvariant()
    };

    private sealed record TaskGraphSnapshot(
        string Id,
        ProductionMode Mode,
        IReadOnlyList<TaskNodeDefinition> Nodes)
    {
        public static TaskGraphSnapshot From(TaskGraph graph) => new(graph.Id, graph.Mode, graph.Nodes);
        public TaskGraph ToDomain() => new(Id, Mode, Nodes);
    }
}
