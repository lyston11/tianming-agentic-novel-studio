using Microsoft.EntityFrameworkCore;

namespace Tianming.NovelAgent.Infrastructure.Persistence;

public sealed class AgentControlDbContext(DbContextOptions<AgentControlDbContext> options) : DbContext(options)
{
    public DbSet<ConversationMessageRecord> ConversationMessages => Set<ConversationMessageRecord>();
    public DbSet<ConversationTurnRecord> ConversationTurns => Set<ConversationTurnRecord>();
    public DbSet<ConversationRuntimeCheckpointRecord> ConversationRuntimeCheckpoints => Set<ConversationRuntimeCheckpointRecord>();
    public DbSet<GoalProposalRecord> GoalProposals => Set<GoalProposalRecord>();
    public DbSet<ContextCheckpointRecord> ContextCheckpoints => Set<ContextCheckpointRecord>();
    public DbSet<CreativeGoalRecord> CreativeGoals => Set<CreativeGoalRecord>();
    public DbSet<GoalRevisionRecord> GoalRevisions => Set<GoalRevisionRecord>();
    public DbSet<BookProductionRecord> BookProductions => Set<BookProductionRecord>();
    public DbSet<ProductionBatchRecord> ProductionBatches => Set<ProductionBatchRecord>();
    public DbSet<CanonBranchRecord> CanonBranches => Set<CanonBranchRecord>();
    public DbSet<GoalContextSnapshotRecord> GoalContextSnapshots => Set<GoalContextSnapshotRecord>();
    public DbSet<TaskGraphRecord> TaskGraphs => Set<TaskGraphRecord>();
    public DbSet<KernelTaskRecord> KernelTasks => Set<KernelTaskRecord>();
    public DbSet<KernelArtifactRecord> KernelArtifacts => Set<KernelArtifactRecord>();
    public DbSet<ModelExecutionRecord> ModelExecutions => Set<ModelExecutionRecord>();
    public DbSet<DomainEventRecord> DomainEvents => Set<DomainEventRecord>();
    public DbSet<AgentStreamEventRecord> StreamEvents => Set<AgentStreamEventRecord>();
    public DbSet<StreamSequenceRecord> StreamSequences => Set<StreamSequenceRecord>();
    public DbSet<OutboxEventRecord> OutboxEvents => Set<OutboxEventRecord>();
    public DbSet<CanonWriteLeaseRecord> CanonWriteLeases => Set<CanonWriteLeaseRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureNewTables(modelBuilder);
        ConfigureExistingControlTables(modelBuilder);
    }

    private static void ConfigureNewTables(ModelBuilder modelBuilder)
    {
        var messages = Scoped<ConversationMessageRecord>(modelBuilder, "agent_conversation_messages");
        messages.HasIndex(x => new { x.UserId, x.SessionId, x.CreatedAt });

        var turns = Scoped<ConversationTurnRecord>(modelBuilder, "agent_conversation_turns");
        turns.Property(x => x.ResultJson).HasColumnType("jsonb");
        turns.HasIndex(x => new { x.UserId, x.SessionId, x.IdempotencyKey }).IsUnique();

        var runtimeCheckpoints = Scoped<ConversationRuntimeCheckpointRecord>(
            modelBuilder,
            "agent_conversation_runtime_checkpoints");
        runtimeCheckpoints.Property(x => x.CheckpointJson).HasColumnType("jsonb");
        runtimeCheckpoints.Property(x => x.Version).IsConcurrencyToken();
        runtimeCheckpoints.HasIndex(x => new { x.UserId, x.SessionId, x.Runtime }).IsUnique();

        var proposals = Scoped<GoalProposalRecord>(modelBuilder, "agent_goal_proposals");
        proposals.Property(x => x.ContractJson).HasColumnType("jsonb");
        proposals.HasIndex(x => new { x.UserId, x.ProjectId, x.Status, x.CreatedAt });

        var checkpoints = Scoped<ContextCheckpointRecord>(modelBuilder, "agent_context_checkpoints");
        Json(checkpoints, nameof(ContextCheckpointRecord.ModelVersionsJson));
        Json(checkpoints, nameof(ContextCheckpointRecord.PromptVersionsJson));
        Json(checkpoints, nameof(ContextCheckpointRecord.SchemaVersionsJson));
        Json(checkpoints, nameof(ContextCheckpointRecord.ProtocolVersionsJson));
        checkpoints.HasIndex(x => new { x.UserId, x.ProjectId, x.ContractHash });

        var streamEvents = Scoped<AgentStreamEventRecord>(modelBuilder, "agent_stream_events");
        streamEvents.Property(x => x.PayloadJson).HasColumnType("jsonb");
        streamEvents.HasIndex(x => new { x.UserId, x.StreamKind, x.StreamId, x.Sequence }).IsUnique();
        streamEvents.HasIndex(x => new { x.UserId, x.ProjectId, x.OccurredAt });

        var sequences = modelBuilder.Entity<StreamSequenceRecord>();
        sequences.ToTable("agent_stream_sequences");
        sequences.HasKey(x => x.Id);
        SnakeCase(sequences);
        sequences.Property(x => x.Version).IsConcurrencyToken();
        sequences.HasIndex(x => new { x.UserId, x.StreamKind, x.StreamId }).IsUnique();

        var leases = Scoped<CanonWriteLeaseRecord>(modelBuilder, "agent_canon_write_leases");
        leases.Property(x => x.Version).IsConcurrencyToken();
        leases.HasIndex(x => new { x.UserId, x.ProjectId }).IsUnique();
        leases.HasIndex(x => new { x.ExpiresAt, x.ProductionId });
    }

    private static void ConfigureExistingControlTables(ModelBuilder modelBuilder)
    {
        var goals = Scoped<CreativeGoalRecord>(modelBuilder, "creative_goals");
        Json(goals, nameof(CreativeGoalRecord.TargetChapterRangeJson));
        Json(goals, nameof(CreativeGoalRecord.SuccessCriteriaJson));
        Json(goals, nameof(CreativeGoalRecord.MustPreserveJson));
        Json(goals, nameof(CreativeGoalRecord.MustHappenJson));
        Json(goals, nameof(CreativeGoalRecord.MustNotChangeJson));
        Json(goals, nameof(CreativeGoalRecord.AcceptancePolicyJson));
        Json(goals, nameof(CreativeGoalRecord.ReworkPolicyJson));
        Json(goals, nameof(CreativeGoalRecord.BookPlanJson));
        Json(goals, nameof(CreativeGoalRecord.ModelConfigVersionsJson));
        Json(goals, nameof(CreativeGoalRecord.ProtocolVersionsJson));
        goals.Property(x => x.TotalCostLimit).HasPrecision(18, 6);
        goals.Property(x => x.ReservedCost).HasPrecision(18, 6);
        goals.Property(x => x.ActualCost).HasPrecision(18, 6);
        goals.Property(x => x.AggregateVersion).IsConcurrencyToken();
        goals.HasIndex(x => new { x.UserId, x.ProjectId, x.IdempotencyKey })
            .IsUnique()
            .HasFilter("idempotency_key <> ''");

        var revisions = Scoped<GoalRevisionRecord>(modelBuilder, "goal_revisions");
        Json(revisions, nameof(GoalRevisionRecord.ConstraintChangesJson));
        Json(revisions, nameof(GoalRevisionRecord.ReusableArtifactIdsJson));
        Json(revisions, nameof(GoalRevisionRecord.InvalidatedArtifactIdsJson));
        Json(revisions, nameof(GoalRevisionRecord.AffectedNodeIdsJson));
        Json(revisions, nameof(GoalRevisionRecord.ContractJson));
        Json(revisions, nameof(GoalRevisionRecord.ContextJson));
        revisions.HasIndex(x => new { x.UserId, x.GoalId, x.RevisionNumber }).IsUnique();
        revisions.HasIndex(x => new { x.UserId, x.ProjectId, x.ConfirmationIdempotencyKey })
            .IsUnique()
            .HasFilter("confirmation_idempotency_key <> ''");

        var productions = Scoped<BookProductionRecord>(modelBuilder, "book_productions");
        Json(productions, nameof(BookProductionRecord.CompletionCriteriaJson));
        Json(productions, nameof(BookProductionRecord.PausePolicyJson));
        productions.Property(x => x.AggregateVersion).IsConcurrencyToken();
        productions.HasIndex(x => new { x.UserId, x.GoalId }).IsUnique();

        var batches = Scoped<ProductionBatchRecord>(modelBuilder, "production_batches");
        batches.HasIndex(x => new { x.UserId, x.BookProductionId, x.BatchNumber }).IsUnique();

        var branches = Scoped<CanonBranchRecord>(modelBuilder, "canon_branches");
        branches.HasIndex(x => new { x.UserId, x.GoalId })
            .IsUnique()
            .HasFilter("status = 'active'");

        var goalSnapshots = Scoped<GoalContextSnapshotRecord>(modelBuilder, "goal_context_snapshots");
        Json(goalSnapshots, nameof(GoalContextSnapshotRecord.ModelConfigVersionsJson));
        Json(goalSnapshots, nameof(GoalContextSnapshotRecord.ProtocolVersionsJson));
        Json(goalSnapshots, nameof(GoalContextSnapshotRecord.ContentHashesJson));
        goalSnapshots.HasIndex(x => new { x.UserId, x.GoalId }).IsUnique();

        var graphs = Scoped<TaskGraphRecord>(modelBuilder, "task_graph_versions");
        Json(graphs, nameof(TaskGraphRecord.GraphJson));
        graphs.HasIndex(x => new { x.UserId, x.GoalId, x.Version }).IsUnique();

        var tasks = Scoped<KernelTaskRecord>(modelBuilder, "kernel_tasks");
        Json(tasks, nameof(KernelTaskRecord.DependencyTaskIdsJson));
        Json(tasks, nameof(KernelTaskRecord.InputArtifactIdsJson));
        Json(tasks, nameof(KernelTaskRecord.OutputArtifactIdsJson));
        tasks.HasIndex(x => new { x.UserId, x.IdempotencyKey }).IsUnique();
        tasks.HasIndex(x => new { x.Status, x.NextAttemptAt, x.Priority, x.CreatedAt });

        var artifacts = Scoped<KernelArtifactRecord>(modelBuilder, "kernel_artifacts");
        Json(artifacts, nameof(KernelArtifactRecord.ContentJson));
        artifacts.HasIndex(x => new { x.UserId, x.ProjectId, x.GoalId, x.ArtifactType });
        artifacts.HasIndex(x => new { x.UserId, x.TaskId, x.ContentHash }).IsUnique();

        var executions = Scoped<ModelExecutionRecord>(modelBuilder, "model_executions");
        Json(executions, nameof(ModelExecutionRecord.ResultJson));
        executions.Property(x => x.ReservedCost).HasPrecision(18, 6);
        executions.Property(x => x.ActualCost).HasPrecision(18, 6);
        executions.HasIndex(x => new { x.UserId, x.IdempotencyKey }).IsUnique();
        executions.HasIndex(x => new { x.Status, x.LeaseExpiresAt, x.CreatedAt });

        var domainEvents = Scoped<DomainEventRecord>(modelBuilder, "domain_events");
        Json(domainEvents, nameof(DomainEventRecord.ArtifactRefsJson));
        Json(domainEvents, nameof(DomainEventRecord.EvidenceRefsJson));
        Json(domainEvents, nameof(DomainEventRecord.PayloadJson));
        domainEvents.HasIndex(x => new { x.UserId, x.IdempotencyKey }).IsUnique();
        domainEvents.HasIndex(x => new { x.UserId, x.AggregateType, x.AggregateId, x.AggregateVersion }).IsUnique();

        var outbox = modelBuilder.Entity<OutboxEventRecord>();
        outbox.ToTable("outbox_events");
        outbox.HasKey(x => x.Id);
        SnakeCase(outbox);
        outbox.Property(x => x.PayloadJson).HasColumnType("jsonb");
        outbox.HasIndex(x => new { x.UserId, x.IdempotencyKey }).IsUnique();
        outbox.HasIndex(x => new { x.Status, x.NextAttemptAt, x.CreatedAt });
    }

    private static Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> Scoped<T>(
        ModelBuilder modelBuilder,
        string tableName)
        where T : class
    {
        var entity = modelBuilder.Entity<T>();
        entity.ToTable(tableName);
        entity.HasKey("Id");
        SnakeCase(entity);
        return entity;
    }

    private static void Json<T>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> entity,
        string propertyName)
        where T : class => entity.Property(propertyName).HasColumnType("jsonb");

    private static void SnakeCase<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> entity)
        where T : class
    {
        foreach (var property in entity.Metadata.GetProperties())
            property.SetColumnName(ToSnakeCase(property.Name));
    }

    private static string ToSnakeCase(string value)
    {
        var result = new System.Text.StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (char.IsUpper(current) && index > 0)
                result.Append('_');
            result.Append(char.ToLowerInvariant(current));
        }
        return result.ToString();
    }
}
